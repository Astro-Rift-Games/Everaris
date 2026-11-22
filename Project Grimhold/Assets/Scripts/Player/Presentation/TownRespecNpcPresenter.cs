using System;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

/// <summary>
/// Presents the Mentor respec panel (TASK-304) after a confirmed interaction with a <see cref="TownRespecNpcInteractable"/>
/// and, when the NPC has one, its dialogue. Input Authority only: <see cref="PlayerInteractionNetworkController.InteractionResolved"/>
/// and the dialogue completion are raised on the owning client alone. All rules live in <see cref="TownRespecSession"/>;
/// this presenter feeds it the same Ready lock and busy flags the attribute panel uses through
/// <see cref="TownAttributeAssignmentGate"/>, owns input suppression and the NPC's facing while the panel is open, and
/// never writes the profile itself: only the backend's authoritative snapshot reaches the store.
/// </summary>
[DisallowMultipleComponent]
public sealed class TownRespecNpcPresenter : MonoBehaviour
{
    private const float MaxInteractionDistance = 4f;

    [SerializeField] private TownRespecPanelView _viewPrefab;
    [SerializeField] private TownAttributeAssignmentPresenter _attributePresenter;
    [SerializeField] private PlayerInteractionNetworkController _interactionController;
    [SerializeField] private DialoguePresenter _dialoguePresenter;

    private TownRespecPanelView _view;
    private LocalProfileStore _store;
    private TownRespecSession _session;
    private TownAttributeAssignmentGate _gate;
    private Func<PlayerInputReader> _inputReaderSource;
    private NetworkObject _openNpc;
    private PlayerInputReader _inputReader;
    private IDisposable _inputSuppression;
    private bool _isListening;
    private bool _isSubscribed;
    private bool _presentedCanMutate;
    private int _openGeneration;

    public bool IsOpen => _view != null && _view.IsOpen;
    public bool IsRespecPending => _session != null && _session.IsPending;

    /// <summary>
    /// Supplies the confirmed local profile, the respec service (null leaves the respec unavailable), the build gate
    /// shared with the attribute panel and the local input reader. Rebinding closes any open panel.
    /// </summary>
    public void BindProfile(
        LocalProfileStore store,
        AttributeRespecService service,
        TownAttributeAssignmentGate gate,
        Func<PlayerInputReader> inputReaderSource)
    {
        UnbindProfile();
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _inputReaderSource = inputReaderSource;
        _session = new TownRespecSession(store, service, () => gate.CanMutateBuild, () => gate.IsRespecBlocked);
        _session.Changed += OnSessionChanged;
    }

    public void UnbindProfile()
    {
        ClosePanel();
        if (_session != null)
        {
            _session.Changed -= OnSessionChanged;
            _session.Dispose();
            _session = null;
        }

        if (_gate != null)
        {
            _gate.IsRespecPending = false;
        }

        _store = null;
        _gate = null;
        _inputReaderSource = null;
    }

    private void Awake() => CacheDependencies();

    private void OnEnable() => Subscribe();

    private void OnDisable()
    {
        Unsubscribe();
        ClosePanel();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        UnbindProfile();
        if (_view != null)
        {
            _view.ConfirmRequested -= OnConfirmRequested;
            _view.CancelRequested -= ClosePanel;
            _view.CloseRequested -= ClosePanel;
            Destroy(_view.gameObject);
            _view = null;
        }
    }

    private void Update()
    {
        if (!IsOpen)
        {
            return;
        }

        if (_openNpc == null || !_openNpc.IsValid ||
            Vector3.Distance(transform.position, _openNpc.transform.position) > MaxInteractionDistance)
        {
            ClosePanel();
            return;
        }

        // Ready can change while the panel is open (a Host cancel resets it): follow the lock instead of waiting
        // for the next open or profile commit.
        if (_gate != null && _gate.CanMutateBuild != _presentedCanMutate)
        {
            Present();
        }
    }

    private void Subscribe()
    {
        if (_isSubscribed)
        {
            return;
        }

        CacheDependencies();
        if (_interactionController != null)
        {
            _interactionController.InteractionResolved += OnInteractionResolved;
        }

        if (_dialoguePresenter != null)
        {
            _dialoguePresenter.DialogueCompletedNormally += OnDialogueCompletedNormally;
        }

        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (_interactionController != null)
        {
            _interactionController.InteractionResolved -= OnInteractionResolved;
        }

        if (_dialoguePresenter != null)
        {
            _dialoguePresenter.DialogueCompletedNormally -= OnDialogueCompletedNormally;
        }

        _isSubscribed = false;
    }

    private void OnInteractionResolved(InteractionPresentationEvent interactionEvent)
    {
        if (!interactionEvent.Success || interactionEvent.TargetId.Value == 0)
        {
            return;
        }

        HandleInteraction(ResolveNetworkObject(interactionEvent.TargetId));
    }

    private void OnDialogueCompletedNormally(EntityId targetId) =>
        HandleDialogueCompleted(ResolveNetworkObject(targetId));

    /// <summary>
    /// A confirmed interaction opens the panel right away only when the NPC has no dialogue; otherwise the dialogue
    /// plays first and <see cref="HandleDialogueCompleted"/> opens it. Returns whether the panel opened.
    /// </summary>
    public bool HandleInteraction(NetworkObject target)
    {
        if (!IsMentor(target) || target.GetComponentInChildren<IDialogueTrigger>() != null)
        {
            return false;
        }

        return OpenForTarget(target);
    }

    /// <summary>Opens the panel once the Mentor's dialogue ended; restores the NPC's facing when it cannot open.</summary>
    public bool HandleDialogueCompleted(NetworkObject target)
    {
        if (!IsMentor(target) || IsOpen)
        {
            return false;
        }

        if (OpenForTarget(target))
        {
            return true;
        }

        target.GetComponentInChildren<TownNpcDirectionalView>()?.RestoreInitialFacing();
        return false;
    }

    /// <summary>Opens the panel for the respec NPC; any other target, or a missing local profile, leaves it closed.</summary>
    public bool OpenForTarget(NetworkObject target)
    {
        if (!IsMentor(target) || IsOpen)
        {
            return false;
        }

        if (!EnsureProfileBound())
        {
            Debug.LogWarning("Mentor is unavailable because the local profile is not bound.", this);
            return false;
        }

        if (!EnsureView())
        {
            return false;
        }

        _openNpc = target;
        _session.Cancel();
        Present();
        _view.Open();
        if (!_view.IsOpen)
        {
            _openNpc = null;
            return false;
        }

        target.GetComponentInChildren<TownNpcDirectionalView>()?.FaceTarget(transform.position);
        StartListening();
        AcquireInputSuppression();
        _ = BeginRespecAsync(++_openGeneration);
        return true;
    }

    public void ClosePanel()
    {
        // A request still awaiting the backend keeps running and keeps its interlock: only the panel goes away.
        _openGeneration++;
        StopListening();
        _session?.Cancel();
        if (_view != null)
        {
            _view.Close();
        }

        if (_openNpc != null)
        {
            _openNpc.GetComponentInChildren<TownNpcDirectionalView>()?.RestoreInitialFacing();
            _openNpc = null;
        }

        ReleaseInputSuppression();
    }

    /// <summary>
    /// Reads the server's price when the profile does not have it yet, then offers the explicit confirmation. A blocked
    /// state shows its reason instead, and the player can only cancel.
    /// </summary>
    private async Task BeginRespecAsync(int generation)
    {
        if (!_store.RespecCost.HasValue)
        {
            try
            {
                await _session.RefreshCostAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[{nameof(TownRespecNpcPresenter)}] The respec cost refresh failed: {exception.Message}", this);
            }
        }

        if (generation != _openGeneration || _session == null || !IsOpen)
        {
            return;
        }

        _session.RequestRespec();
    }

    private async void OnConfirmRequested()
    {
        TownRespecSession session = _session;
        if (session == null)
        {
            return;
        }

        try
        {
            await session.ConfirmAsync();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[{nameof(TownRespecNpcPresenter)}] The respec failed unexpectedly: {exception}", this);
        }
    }

    private void OnSessionChanged()
    {
        if (_gate != null && _session != null)
        {
            _gate.IsRespecPending = _session.IsPending;
        }

        Present();
    }

    private void Present()
    {
        if (_view == null || _session == null || _store == null)
        {
            return;
        }

        _presentedCanMutate = _gate != null && _gate.CanMutateBuild;
        TownRespecPresentation respec = _session.Presentation;
        _view.Present(_store.TryGetCharacterAttributeState(out CharacterAttributeState attributes)
            ? TownRespecPanelPresentation.Create(attributes, respec)
            : TownRespecPanelPresentation.Unavailable(respec));
    }

    private static bool IsMentor(NetworkObject target) =>
        target != null && target.TryGetComponent(out TownRespecNpcInteractable _);

    private NetworkObject ResolveNetworkObject(EntityId entityId)
    {
        NetworkRunner runner = ResolveRunner();
        if (runner == null || !runner.IsRunning)
        {
            return null;
        }

        var networkId = new NetworkId { Raw = unchecked((uint)entityId.Value) };
        return runner.TryFindObject(networkId, out NetworkObject target) ? target : null;
    }

    private NetworkRunner ResolveRunner() =>
        TryGetComponent(out NetworkObject networkObject) ? networkObject.Runner : null;

    /// <summary>
    /// Binds lazily from the attribute presenter's gate and the application profile at the first open: the Town
    /// composition (outside this presenter) owns when they exist, so nothing here assumes the order they spawn in.
    /// </summary>
    private bool EnsureProfileBound()
    {
        TownAttributeAssignmentGate gate = _attributePresenter != null ? _attributePresenter.Gate : null;
        if (_session != null && (gate == null || gate == _gate))
        {
            return true;
        }

        if (gate == null)
        {
            return false;
        }

        ApplicationStashContext context = FindAnyObjectByType<ApplicationStashContext>();
        if (context == null || !context.IsAvailable || context.Store == null)
        {
            return false;
        }

        BindProfile(context.Store, CreateRespecService(context), gate, ResolveInputReader);
        return true;
    }

    private PlayerInputReader ResolveInputReader()
    {
        NetworkRunner runner = ResolveRunner();
        LocalInputContext context = runner != null ? runner.GetComponent<LocalInputContext>() : null;
        return context != null ? context.Reader : null;
    }

    /// <summary>
    /// Wires the respec to the profile's remote services; without them the session reports it as unavailable instead
    /// of guessing a cost or sending anything.
    /// </summary>
    private static AttributeRespecService CreateRespecService(ApplicationStashContext context)
    {
        var remoteService = context.GetComponent<RemoteInventoryService>();
        if (remoteService == null)
        {
            return null;
        }

        var reconciliationService = context.GetComponent<ProfileReconciliationService>();
        return new AttributeRespecService(
            context.Store,
            remoteService.RespecAsync,
            reconciliationService != null ? reconciliationService.ReconcileAsync : null,
            remoteService.GetRespecCostAsync);
    }

    private bool EnsureView()
    {
        if (_view != null)
        {
            return true;
        }

        if (_viewPrefab == null)
        {
            Debug.LogError($"{nameof(TownRespecNpcPresenter)} cannot open because the panel prefab is not assigned in the Inspector.", this);
            return false;
        }

        _view = Instantiate(_viewPrefab, transform, false);
        _view.name = _viewPrefab.name;
        _view.Close();
        _view.ConfirmRequested += OnConfirmRequested;
        _view.CancelRequested += ClosePanel;
        _view.CloseRequested += ClosePanel;
        return true;
    }

    private void StartListening()
    {
        if (_isListening || _store == null)
        {
            return;
        }

        _store.ProfileCommitted += OnProfileCommitted;
        _isListening = true;
    }

    private void StopListening()
    {
        if (!_isListening)
        {
            return;
        }

        if (_store != null)
        {
            _store.ProfileCommitted -= OnProfileCommitted;
        }

        _isListening = false;
    }

    private void OnProfileCommitted(ProfileId profileId)
    {
        if (IsOpen)
        {
            Present();
        }
    }

    private void AcquireInputSuppression()
    {
        if (_inputSuppression != null)
        {
            return;
        }

        PlayerInputReader reader = _inputReaderSource?.Invoke();
        if (reader == null)
        {
            Debug.LogWarning($"{nameof(TownRespecNpcPresenter)} has no local input reader; gameplay input stays active while the panel is open.", this);
            return;
        }

        _inputReader = reader;
        _inputReader.InventoryCloseRequested += TryClosePanelFromInput;
        _inputReader.InteractPressedLocally += ClosePanelFromInteraction;
        _inputSuppression = _inputReader.AcquireGameplayInputSuppression();
    }

    private void ReleaseInputSuppression()
    {
        if (_inputReader != null)
        {
            _inputReader.InventoryCloseRequested -= TryClosePanelFromInput;
            _inputReader.InteractPressedLocally -= ClosePanelFromInteraction;
            _inputReader = null;
        }

        _inputSuppression?.Dispose();
        _inputSuppression = null;
    }

    private bool TryClosePanelFromInput()
    {
        if (!IsOpen)
        {
            return false;
        }

        ClosePanel();
        return true;
    }

    private void ClosePanelFromInteraction()
    {
        if (IsOpen)
        {
            ClosePanel();
        }
    }

    private void CacheDependencies()
    {
        if (_interactionController == null)
        {
            _interactionController = GetComponent<PlayerInteractionNetworkController>();
        }

        if (_dialoguePresenter == null)
        {
            _dialoguePresenter = GetComponent<DialoguePresenter>();
        }

        if (_attributePresenter == null)
        {
            _attributePresenter = GetComponent<TownAttributeAssignmentPresenter>();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheDependencies();
    }
#endif
}
