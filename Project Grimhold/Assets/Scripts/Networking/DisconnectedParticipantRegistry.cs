using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Maintains the set of disconnected participants that are retained in the Raid.
/// </summary>
public sealed class DisconnectedParticipantRegistry
{
    private readonly List<NetworkObject> _retainedParticipants = new();

    public IReadOnlyList<NetworkObject> RetainedParticipants => _retainedParticipants;

    public void Clear()
    {
        _retainedParticipants.Clear();
    }

    public void Retain(NetworkObject participantObject)
    {
        if (participantObject != null && !_retainedParticipants.Contains(participantObject))
        {
            _retainedParticipants.Add(participantObject);
        }
    }

    public bool IsRetained(ProfileId profileId)
    {
        return TryGetRetainedParticipant(profileId, out _);
    }

    public bool TryGetRetainedParticipant(ProfileId profileId, out NetworkObject participantObject)
    {
        foreach (NetworkObject retained in _retainedParticipants)
        {
            if (retained != null && retained.IsValid &&
                retained.TryGetBehaviour(out NetworkRaidParticipant participant) &&
                participant.ProfileId.ToString() == profileId.Value)
            {
                participantObject = retained;
                return true;
            }
        }

        participantObject = null;
        return false;
    }

    /// <summary>
    /// FIX-47: Resolves a retained disconnected participant by immutable <see cref="RaidParticipantId"/>
    /// so team extraction requirements continue to include retained disconnected teammates.
    /// </summary>
    public bool TryGetRetainedParticipant(RaidParticipantId participantId, out NetworkRaidParticipant participant)
    {
        participant = null;
        if (!participantId.IsValid)
        {
            return false;
        }

        for (int index = 0; index < _retainedParticipants.Count; index++)
        {
            NetworkObject retained = _retainedParticipants[index];
            if (retained != null && retained.IsValid &&
                retained.TryGetBehaviour(out NetworkRaidParticipant candidate) &&
                candidate.RaidParticipantId == participantId)
            {
                participant = candidate;
                return true;
            }
        }

        return false;
    }

    public void Remove(ProfileId profileId)
    {
        for (int i = _retainedParticipants.Count - 1; i >= 0; i--)
        {
            NetworkObject retained = _retainedParticipants[i];
            if (retained != null && retained.IsValid &&
                retained.TryGetBehaviour(out NetworkRaidParticipant participant) &&
                participant.ProfileId.ToString() == profileId.Value)
            {
                _retainedParticipants.RemoveAt(i);
                break;
            }
        }
    }

    /// <summary>
    /// Checks if there is any retained participant still unresolved (Raiding).
    /// As a side effect, removes from tracking any participant that has been resolved.
    /// </summary>
    public bool HasUnresolvedRetainedParticipant()
    {
        bool unresolved = false;
        for (int index = _retainedParticipants.Count - 1; index >= 0; index--)
        {
            NetworkObject participantObject = _retainedParticipants[index];
            if (participantObject == null ||
                !participantObject.IsValid ||
                !participantObject.TryGetBehaviour(out NetworkRaidParticipant participant) ||
                participant.State != RaidParticipantState.Raiding)
            {
                _retainedParticipants.RemoveAt(index);
                continue;
            }

            unresolved = true;
        }

        return unresolved;
    }
}
