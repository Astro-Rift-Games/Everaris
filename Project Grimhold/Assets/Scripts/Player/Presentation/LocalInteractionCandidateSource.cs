using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Performs a read-only local interaction query for the player with Input Authority.
/// It shares the authoritative selection policy but never executes an interaction.
/// </summary>
[DisallowMultipleComponent]
public sealed class LocalInteractionCandidateSource : NetworkBehaviour
{
    [SerializeField]
    private PlayerInteractionConfig _config;

    [SerializeField]
    private MonoBehaviour _characterSource;

    [SerializeField]
    private MonoBehaviour _querySource;

    [SerializeField]
    private Transform _interactionOrigin;

    private ICharacter _character;
    private IInteractionTargetQuery _query;
    private EntityRegistry _registry;
    private bool _dependenciesValid;
    private EntityId _metadataTargetId;
    private NetworkObject _metadataNetworkObject;
    private SocialPlayerInteractable _socialPlayerInteractable;
    private DownedReviveInteractable _reviveInteractable;
    private NetworkDoor _door;
    private string _metadataPromptText;
    private string _currentPromptText;
    private Transform _promptAnchor;
    private Vector3 _promptLocalAnchor;
    private Vector2 _promptOffset;

    public bool HasCandidate { get; private set; }
    public InteractionTarget CurrentCandidate { get; private set; }
    public string CurrentPromptText => _currentPromptText;

    /// <summary>Network object of the current candidate for local presentation, or null without one.</summary>
    public NetworkObject CurrentCandidateObject => HasCandidate ? _metadataNetworkObject : null;

    /// <summary>
    /// World point the local prompt hangs from for the current candidate. Presentation only; false without a
    /// candidate.
    /// </summary>
    public bool TryGetPromptWorldAnchor(out Vector3 worldAnchor)
    {
        if (!HasCandidate || _metadataNetworkObject == null)
        {
            worldAnchor = default;
            return false;
        }

        worldAnchor = _promptAnchor != null
            ? _promptAnchor.position
            : _metadataNetworkObject.transform.TransformPoint(_promptLocalAnchor);
        worldAnchor += (Vector3)_promptOffset;
        return true;
    }

    private void Awake()
    {
        CacheDependencies();
    }

    public override void Spawned()
    {
        CacheDependencies();
        _registry = Runner.GetComponent<EntityRegistry>();
        _dependenciesValid = ValidateDependencies();
        ClearCandidate();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ClearCandidate();
        _registry = null;
    }

    private void OnDisable()
    {
        ClearCandidate();
    }

    public override void Render()
    {
        if (!HasInputAuthority || !_dependenciesValid)
        {
            ClearCandidate();
            return;
        }

        Vector2 origin = _interactionOrigin != null
            ? (Vector2)_interactionOrigin.position
            : (Vector2)transform.position;

        InteractionTargetQuery targetQuery = new InteractionTargetQuery(
            _character.Id,
            origin,
            _config.MaximumDistance,
            _config.TargetLayerMask);

        IReadOnlyList<InteractionTarget> candidates = _query.FindTargets(targetQuery);
        HasCandidate = InteractionResolver.TrySelect(
            _character.Id,
            Runner.Tick,
            _config.MaximumDistance,
            candidates,
            _registry.TryGetInteractionHandler,
            out InteractionTarget selectedTarget,
            out _,
            out _);

        CurrentCandidate = HasCandidate ? selectedTarget : default;
        if (HasCandidate)
        {
            RefreshPromptMetadata(selectedTarget.TargetId);
        }
        else
        {
            ClearPromptMetadata();
        }
    }

    private void ClearCandidate()
    {
        HasCandidate = false;
        CurrentCandidate = default;
        ClearPromptMetadata();
    }

    private void RefreshPromptMetadata(EntityId targetId)
    {
        var networkId = new NetworkId { Raw = unchecked((uint)targetId.Value) };
        if (Runner == null || !Runner.TryFindObject(networkId, out NetworkObject networkObject) ||
            networkObject == null || networkObject.Id.Raw != networkId.Raw)
        {
            ClearPromptMetadata();
            return;
        }

        if (_metadataTargetId == targetId && ReferenceEquals(_metadataNetworkObject, networkObject))
        {
            if (_socialPlayerInteractable != null)
            {
                _currentPromptText = _socialPlayerInteractable.PromptText;
            }
            else if (_reviveInteractable != null)
            {
                // The avatar's metadata describes its corpse, so a Downed target overrides it each frame.
                _currentPromptText = _reviveInteractable.IsTargetDowned
                    ? _reviveInteractable.ResolvePromptText(_character as PlayerCharacter)
                    : _metadataPromptText;
            }
            else if (_door != null)
            {
                _currentPromptText = _door.PromptText;
            }
            return;
        }

        _metadataTargetId = targetId;
        _metadataNetworkObject = networkObject;
        InteractionPromptMetadata metadata = networkObject.GetComponent<InteractionPromptMetadata>();
        _socialPlayerInteractable = networkObject.GetComponent<SocialPlayerInteractable>();
        _reviveInteractable = networkObject.GetComponent<DownedReviveInteractable>();
        _door = networkObject.GetComponent<NetworkDoor>();
        _metadataPromptText = metadata != null ? metadata.PromptText : null;
        _promptAnchor = metadata != null ? metadata.PromptAnchor : null;
        _promptOffset = metadata != null ? metadata.PromptOffset : Vector2.zero;
        _promptLocalAnchor = ResolvePromptLocalAnchor(networkObject);
        _currentPromptText = _socialPlayerInteractable != null
            ? _socialPlayerInteractable.PromptText
            : _reviveInteractable != null && _reviveInteractable.IsTargetDowned
                ? _reviveInteractable.ResolvePromptText(_character as PlayerCharacter)
                : _door != null
                    ? _door.PromptText
                    : _metadataPromptText;
    }

    private void ClearPromptMetadata()
    {
        _metadataTargetId = default;
        _metadataNetworkObject = null;
        _socialPlayerInteractable = null;
        _reviveInteractable = null;
        _door = null;
        _metadataPromptText = null;
        _currentPromptText = null;
        _promptAnchor = null;
        _promptLocalAnchor = default;
        _promptOffset = default;
    }

    /// <summary>
    /// Measured once per target change and kept in the target's local space, so the prompt follows a moving
    /// target without bobbing with its animation frames.
    /// </summary>
    private static Vector3 ResolvePromptLocalAnchor(NetworkObject networkObject)
    {
        Transform target = networkObject.transform;
        return InteractionPromptPlacement.TryResolveWorldAnchor(
            null,
            networkObject.GetComponentsInChildren<SpriteRenderer>(),
            target,
            out Vector3 worldAnchor)
            ? target.InverseTransformPoint(worldAnchor)
            : Vector3.zero;
    }

    private void CacheDependencies()
    {
        _character = _characterSource != null ? _characterSource as ICharacter : GetComponent<ICharacter>();
        _query = _querySource != null ? _querySource as IInteractionTargetQuery : GetComponent<IInteractionTargetQuery>();

        if (_interactionOrigin == null)
        {
            _interactionOrigin = transform;
        }
    }

    private bool ValidateDependencies()
    {
        if (_config == null || _character == null || _query == null || _registry == null)
        {
            Debug.LogError($"{nameof(LocalInteractionCandidateSource)} has missing interaction dependencies.", this);
            return false;
        }

        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheDependencies();
    }
#endif
}
