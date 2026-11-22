using System;
using Fusion;
using UnityEngine;

/// <summary>Binds only the Input Authority Town player to confirmed local character attributes.</summary>
[DisallowMultipleComponent]
public sealed class TownAttributeAssignmentPresenter : NetworkBehaviour
{
    [SerializeField] private TownAttributeAssignmentView _viewPrefab;
    [SerializeField] private TownPlayerMenuPresenter _menu;
    [SerializeField] private LootDefinitionCatalog _lootCatalog;

    private TownAttributeAssignmentBinding _binding;
    private bool _isMenuHosted;
    private bool _reportedStatisticsFailure;
    private TownAttributeAssignmentView _view;
    private ApplicationStashContext _profileContext;
    private LocalProfileStore _store;
    private TownAttributeMutationEndpoint _endpoint;
    private TownRespecSession _respecSession;
    private TownRaidPreparationDirectory _preparationDirectory;
    private ProfileId _localProfileId;
    private PlayerInputReader _inputReader;
    private IDisposable _inputSuppression;

    public override void Spawned() => TryBind();

    private bool _presentedCanMutate;

    /// <summary>
    /// Ready can change while the panel is open (a Host cancel resets it), so an open panel follows the
    /// gate instead of waiting for the next open or profile commit.
    /// </summary>
    public override void Render()
    {
        if (_binding == null || _view == null || !_view.IsOpen) return;
        bool canMutate = CanMutate();
        if (canMutate == _presentedCanMutate) return;
        _presentedCanMutate = canMutate;
        _binding.Refresh();
    }

    private bool CanMutate() =>
        TownEquipmentMutationGate.CanMutate(_preparationDirectory, _localProfileId);

    private void OnEnable()
    {
        if (Object != null && Object.IsValid)
        {
            TryBind();
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState) => Cleanup();
    private void OnDisable() => Cleanup();
    private void OnDestroy() => Cleanup();

    private void TryBind()
    {
        if (_binding != null || Object == null || !Object.IsValid || !HasInputAuthority)
        {
            return;
        }

        LocalPlayerJoinContext joinContext = Runner != null
            ? Runner.GetComponent<LocalPlayerJoinContext>()
            : null;
        ProfileId localProfileId = joinContext != null
            ? joinContext.JoinData.ProfileId
            : default;
        LocalInputContext inputContext = Runner != null
            ? Runner.GetComponent<LocalInputContext>()
            : null;

        _profileContext = FindAnyObjectByType<ApplicationStashContext>();
        _store = _profileContext != null ? _profileContext.Store : null;
        _inputReader = inputContext != null ? inputContext.Reader : null;
        if (_profileContext == null || _store == null || _inputReader == null ||
            !localProfileId.IsValid || _store.ProfileId != localProfileId)
        {
            ClearReferences();
            return;
        }

        if (_viewPrefab == null)
        {
            Debug.LogError(
                $"[{nameof(TownAttributeAssignmentPresenter)}] Missing required serialized {nameof(TownAttributeAssignmentView)} prefab.",
                this);
            ClearReferences();
            return;
        }

        _localProfileId = localProfileId;
        _preparationDirectory = FindAnyObjectByType<TownRaidPreparationDirectory>();
        _endpoint = new TownAttributeMutationEndpoint(_store, CanMutate);
        _respecSession = new TownRespecSession(_store, CreateRespecService(), CanMutate, () => _isCommitInFlight);
        _respecSession.Changed += PresentRespec;

        _view = Instantiate(_viewPrefab, transform, false);
        _view.name = _viewPrefab.name;

        _view.Close();
        _view.AssignmentRequested += AssignAttribute;
        _view.CloseRequested += ClosePanel;
        _view.RespecRequested += RequestRespec;
        _view.RespecConfirmed += ConfirmRespec;
        _view.RespecCancelled += CancelRespec;
        if (_menu != null)
        {
            // The Town player menu owns the hotkey, Escape and the input-suppression token.
            _isMenuHosted = true;
            _view.SetCloseButtonVisible(false);
            _menu.RegisterTab(new TownMenuTabRegistration(
                TownMenuTabIds.Attributes,
                "Attributes",
                (RectTransform)_view.transform,
                OpenView,
                _view.Close));
        }
        else
        {
            _inputReader.AttributesToggleRequested += TogglePanel;
            _inputReader.InventoryCloseRequested += TryClosePanelFromInput;
        }

        ApplicationStashContext boundContext = _profileContext;
        LocalProfileStore boundStore = _store;
        _binding = new TownAttributeAssignmentBinding(
            localProfileId,
            ProgressionBalanceDefaults.InitialMaximumAttributeValue,
            boundStore.TryGetCharacterAttributeState,
            handler => boundContext.ProfileCommitted += handler,
            handler => boundContext.ProfileCommitted -= handler,
            Present,
            PresentUnavailable,
            TryBuildStatistics,
            PresentStatistics,
            PresentStatisticsUnavailable,
            CanMutate);
    }

    /// <summary>
    /// Wires the respec to the profile's remote services; without them the session reports it as unavailable
    /// instead of guessing a cost or sending anything.
    /// </summary>
    private AttributeRespecService CreateRespecService()
    {
        var remoteService = _profileContext.GetComponent<RemoteInventoryService>();
        if (remoteService == null)
        {
            return null;
        }

        var reconciliationService = _profileContext.GetComponent<ProfileReconciliationService>();
        return new AttributeRespecService(
            _store,
            remoteService.RespecAsync,
            reconciliationService != null ? reconciliationService.ReconcileAsync : null,
            remoteService.GetRespecCostAsync);
    }

    /// <summary>
    /// Builds the character sheet from the confirmed attributes and the prepared equipment, with the same
    /// calculators and balance defaults the Raid player uses.
    /// </summary>
    private bool TryBuildStatistics(
        in CharacterAttributeState state,
        out TownCharacterStatisticsPresentation statistics)
    {
        statistics = default;
        if (_store == null || _lootCatalog == null)
        {
            ReportStatisticsFailure("the profile store or the loot catalog is unavailable");
            return false;
        }

        if (!TownCharacterStatisticsBuilder.TryBuild(
                state,
                _store.GetPreparedEquipment(),
                ResolveLoot,
                ProgressionBalanceDefaults.InitialCharacterDerivedStatisticsConfiguration,
                TownCharacterStatisticsBuilder.DefaultDefenseMitigationConstant,
                out statistics,
                out string failure))
        {
            ReportStatisticsFailure(failure);
            return false;
        }

        return true;
    }

    private LootDefinition ResolveLoot(LootId lootId) =>
        _lootCatalog.TryGet(lootId.Value, out LootDefinition definition) ? definition : null;

    private void ReportStatisticsFailure(string reason)
    {
        if (_reportedStatisticsFailure)
        {
            return;
        }

        _reportedStatisticsFailure = true;
        Debug.LogError(
            $"[{nameof(TownAttributeAssignmentPresenter)}] The character sheet could not be built: {reason}",
            this);
    }

    private void PresentStatistics(TownCharacterStatisticsPresentation statistics) =>
        _view?.PresentStatistics(statistics);

    private void PresentStatisticsUnavailable() => _view?.PresentStatisticsUnavailable();

    private void TogglePanel()
    {
        if (_view == null)
        {
            return;
        }

        if (_view.IsOpen)
        {
            ClosePanel();
            return;
        }

        if (_inputReader == null || _inputReader.IsGameplayInputSuppressed)
        {
            return;
        }

        _inputSuppression = _inputReader.AcquireGameplayInputSuppression();
        OpenView();
    }

    private void OpenView()
    {
        // The Ready state can change without a profile commit, so showing the panel always refreshes.
        _presentedCanMutate = CanMutate();
        _binding?.Refresh();
        _view.Open();
        _ = RefreshRespecCostAsync();
    }

    private async System.Threading.Tasks.Task RefreshRespecCostAsync()
    {
        try
        {
            if (_respecSession != null)
            {
                await _respecSession.RefreshCostAsync();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[{nameof(TownAttributeAssignmentPresenter)}] The respec cost refresh failed: {exception.Message}", this);
        }
    }

    private void RequestRespec() => _respecSession?.RequestRespec();

    private void CancelRespec() => _respecSession?.Cancel();

    private async void ConfirmRespec()
    {
        if (_respecSession == null)
        {
            return;
        }

        try
        {
            await _respecSession.ConfirmAsync();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[{nameof(TownAttributeAssignmentPresenter)}] The respec failed unexpectedly: {exception}", this);
        }
    }

    private void PresentRespec()
    {
        if (_respecSession != null)
        {
            _view?.PresentRespec(_respecSession.Presentation);
        }
    }

    private bool TryClosePanelFromInput()
    {
        if (_view == null || !_view.IsOpen)
        {
            return false;
        }

        ClosePanel();
        return true;
    }

    private bool _isCommitInFlight = false;

    private async void AssignAttribute(CharacterAttribute attribute)
    {
        if (_endpoint == null || _isCommitInFlight || (_respecSession != null && _respecSession.IsPending)) return;

        if (!_endpoint.TryAssign(attribute, out _))
        {
            // A Ready lock may have closed assignment since the panel opened: show the locked state.
            _binding?.Refresh();
            return;
        }

        var remoteService         = _profileContext?.GetComponent<RemoteInventoryService>();
        var reconciliationService = _profileContext?.GetComponent<ProfileReconciliationService>();

        if (remoteService == null)
        {
            Debug.LogWarning("[TownAttributeAssignmentPresenter] RemoteInventoryService not found, attribute not committed to backend.");
            return;
        }

        _isCommitInFlight = true;
        PresentRespec();
        try
        {
            var attributeName = attribute.ToString();
            var (success, data, error) = await RemoteOperationPolicy.ExecuteWithReconciliationAsync(
                () => remoteService.CommitProgressionAsync(attributeName),
                async () => reconciliationService != null && await reconciliationService.ReconcileAsync()
            );

            if (success)
            {
                Debug.Log($"[TownAttributeAssignmentPresenter] Attribute {attributeName} committed. Applying authoritative state.");
                if (CharacterAttributeState.TryCreate(
                        data.vitality, data.resistance, data.strength,
                        data.dexterity, data.intelligence, data.luck, data.availablePoints,
                        out CharacterAttributeState authoritativeState))
                {
                    _store.ForceCharacterAttributeState(authoritativeState);
                }
            }
            else
            {
                // Backend rejected definitively (or reconciled a transport/revision failure).
                Debug.LogError($"[TownAttributeAssignmentPresenter] Backend rejected {attributeName}: {error.error} - {error.message}. Local state may have been reconciled.");
            }
        }
        finally
        {
            _isCommitInFlight = false;
            PresentRespec();
        }
    }

    private void Present(TownAttributeAssignmentPresentation presentation)
    {
        _view?.Present(presentation);
        // Gold, attributes and the Ready gate all feed the respec availability, so it follows every refresh.
        PresentRespec();
    }

    private void PresentUnavailable() => _view?.PresentUnavailable();

    private void ClosePanel()
    {
        _respecSession?.Cancel();
        _view?.Close();
        _inputSuppression?.Dispose();
        _inputSuppression = null;
    }

    private void Cleanup()
    {
        _binding?.Dispose();
        _binding = null;

        if (_isMenuHosted && _menu != null)
        {
            _menu.UnregisterTab(TownMenuTabIds.Attributes);
        }

        _isMenuHosted = false;

        if (_inputReader != null)
        {
            _inputReader.AttributesToggleRequested -= TogglePanel;
            _inputReader.InventoryCloseRequested -= TryClosePanelFromInput;
        }

        if (_view != null)
        {
            _view.AssignmentRequested -= AssignAttribute;
            _view.CloseRequested -= ClosePanel;
            _view.RespecRequested -= RequestRespec;
            _view.RespecConfirmed -= ConfirmRespec;
            _view.RespecCancelled -= CancelRespec;
        }

        _respecSession?.Dispose();
        _respecSession = null;
        ClosePanel();
        if (_view != null)
        {
            Destroy(_view.gameObject);
            _view = null;
        }

        ClearReferences();
    }

    private void ClearReferences()
    {
        _profileContext = null;
        _store = null;
        _endpoint = null;
        _respecSession = null;
        _preparationDirectory = null;
        _localProfileId = default;
        _inputReader = null;
    }
}
