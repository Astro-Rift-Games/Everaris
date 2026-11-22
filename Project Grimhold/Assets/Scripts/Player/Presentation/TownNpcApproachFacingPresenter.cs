using Fusion;
using UnityEngine;

/// <summary>
/// Makes the Town NPC next to the local player turn towards them while they stand in interaction range.
/// It follows the local interaction candidate, so it is inert on remote avatars and never touches gameplay state.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LocalInteractionCandidateSource))]
public sealed class TownNpcApproachFacingPresenter : MonoBehaviour
{
    private readonly TownNpcApproachFacing _facing = new();

    private LocalInteractionCandidateSource _candidateSource;
    private NetworkObject _candidateObject;
    private TownNpcDirectionalView _candidateView;

    private void Awake()
    {
        _candidateSource = GetComponent<LocalInteractionCandidateSource>();
    }

    private void LateUpdate()
    {
        _facing.Track(ResolveCandidateView(), transform.position);
    }

    private void OnDisable()
    {
        _facing.Release();
        _candidateObject = null;
        _candidateView = null;
    }

    private TownNpcDirectionalView ResolveCandidateView()
    {
        NetworkObject candidate = _candidateSource != null ? _candidateSource.CurrentCandidateObject : null;
        if (!ReferenceEquals(candidate, _candidateObject))
        {
            // Resolved once per candidate change, never per frame.
            _candidateObject = candidate;
            _candidateView = candidate != null ? candidate.GetComponentInChildren<TownNpcDirectionalView>() : null;
        }

        return _candidateView;
    }
}
