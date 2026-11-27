using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SocialPlayerIdentity))]
public sealed class TownPartyHudPresenter : NetworkBehaviour
{
    [SerializeField] private SocialPlayerIdentity _identity;
    [SerializeField] private TownPartyHudView _view;
    [SerializeField] private InteractionHudPresenter _interactionHud;

    private TownPartyDirectory _directory;
    private int _presentedPartyId;
    private int _presentedRevision = -1;
    private string _presentedLocalName;
    private string _presentedCompanionName;
    private bool _presentedCompanionInExpedition;
    private bool _presentedAwaitingContinuation;
    private bool _bound;

    private void Awake() => CacheDependencies();

    public override void Spawned()
    {
        CacheDependencies();
        _view?.SetOwned(HasInputAuthority);
        Bind();
    }

    private void OnEnable()
    {
        if (Object != null && Object.IsValid) Bind();
    }

    public override void Render()
    {
        if (!HasInputAuthority || _identity == null || _view == null) return;
        EnsureDirectory();
        ProfileId local = new(_identity.ProfileId.ToString());
        if (_directory != null && _directory.TryGetParty(local, out TownPartySnapshot party))
        {
            _directory.TryGetDisplayName(local, out string localName);
            string companionName = null;
            bool companionInExpedition = false;
            if (party.Members.Count == TownPartyRules.MaxMembers)
            {
                ProfileId companion = party.Members[0] == local ? party.Members[1] : party.Members[0];
                _directory.TryGetDisplayName(companion, out companionName);
                companionInExpedition = !_directory.TryResolvePlayer(companion, out _) &&
                                        _directory.IsProfileInLaunchingPreparation(companion);
            }
            if (!_presentedAwaitingContinuation &&
                _presentedPartyId == party.PartyId && _presentedRevision == party.Revision &&
                _presentedLocalName == localName && _presentedCompanionName == companionName &&
                _presentedCompanionInExpedition == companionInExpedition) return;
            if (!TownPartyPresentation.TryCreate(party, local, localName, companionName, companionInExpedition, out TownPartyPresentation presentation)) return;
            _view.Present(presentation);
            _presentedPartyId = party.PartyId;
            _presentedRevision = party.Revision;
            _presentedLocalName = localName;
            _presentedCompanionName = companionName;
            _presentedCompanionInExpedition = companionInExpedition;
            _presentedAwaitingContinuation = false;
            return;
        }

        // FIX-46: When the local player returned to Town while their Duo partner is still in Dungeon,
        // keep showing the Party HUD with the partner marked "en expedición" until restored or expired.
        SessionConnectionCoordinator coordinator = SessionConnectionCoordinator.Instance;
        if (coordinator != null && coordinator.TryGetPendingPartyContinuation(out TownPartyContinuationContext pending) &&
            pending.Members.Count == TownPartyRules.MaxMembers)
        {
            string localName = null;
            string companionName = null;
            ProfileId companion = pending.Members[0] == local ? pending.Members[1] : pending.Members[0];
            _directory?.TryGetDisplayName(local, out localName);
            _directory?.TryGetDisplayName(companion, out companionName);
            if (string.IsNullOrWhiteSpace(localName)) localName = _identity.DisplayName.ToString();
            if (_presentedAwaitingContinuation && _presentedLocalName == localName && _presentedCompanionName == companionName) return;
            if (!TownPartyPresentation.TryCreateAwaitingExpedition(pending, local, localName, companionName, out TownPartyPresentation presentation)) return;
            _view.Present(presentation);
            _presentedPartyId = -1;
            _presentedRevision = -1;
            _presentedLocalName = localName;
            _presentedCompanionName = companionName;
            _presentedCompanionInExpedition = true;
            _presentedAwaitingContinuation = true;
            return;
        }

        ClearPresentedParty();
    }

    public override void Despawned(NetworkRunner runner, bool hasState) => Unbind();
    private void OnDisable() => Unbind();
    private void OnDestroy() => Unbind();

    private void Bind()
    {
        if (_bound || !HasInputAuthority || _view == null) return;
        _view.LeaveRequested += LeaveParty;
        _view.SetOwned(true);
        _bound = true;
        EnsureDirectory();
    }

    private void Unbind()
    {
        if (_view != null)
        {
            _view.LeaveRequested -= LeaveParty;
            _view.SetOwned(false);
        }
        if (_directory != null)
        {
            _directory.LeaveResultReceived -= OnLeaveResultReceived;
        }
        _directory = null;
        _bound = false;
        _presentedPartyId = 0;
        _presentedRevision = -1;
        _presentedLocalName = null;
        _presentedCompanionName = null;
        _presentedCompanionInExpedition = false;
        _presentedAwaitingContinuation = false;
    }

    private void ClearPresentedParty()
    {
        if (_presentedPartyId == 0 && !_presentedAwaitingContinuation) return;
        _view.ClearParty();
        _presentedPartyId = 0;
        _presentedRevision = -1;
        _presentedLocalName = null;
        _presentedCompanionName = null;
        _presentedCompanionInExpedition = false;
        _presentedAwaitingContinuation = false;
    }

    private void EnsureDirectory()
    {
        TownPartyDirectory resolved = Runner != null ? Runner.GetComponent<TownPartyDirectoryContext>()?.Directory : null;
        if (_directory == resolved) return;
        if (_directory != null)
        {
            _directory.LeaveResultReceived -= OnLeaveResultReceived;
        }
        _directory = resolved;
        if (_directory != null)
        {
            _directory.LeaveResultReceived += OnLeaveResultReceived;
        }
    }

    private void LeaveParty()
    {
        EnsureDirectory();
        if (_directory == null || !_directory.RequestLeaveParty())
        {
            ShowLeaveFailure(TownPartyLeaveResult.Unavailable);
        }
    }

    private void OnLeaveResultReceived(TownPartyLeaveResult result)
    {
        ShowLeaveFailure(result);
    }

    private void ShowLeaveFailure(TownPartyLeaveResult result)
    {
        if (TownPartyFeedbackText.TryGetLeaveFailureMessage(result, out string message))
        {
            _interactionHud?.ShowStatusFeedback(message);
        }
    }

    private void CacheDependencies()
    {
        if (_identity == null) _identity = GetComponent<SocialPlayerIdentity>();
        if (_interactionHud == null) _interactionHud = GetComponentInChildren<InteractionHudPresenter>(true);
    }

#if UNITY_EDITOR
    private void OnValidate() => CacheDependencies();
#endif
}
