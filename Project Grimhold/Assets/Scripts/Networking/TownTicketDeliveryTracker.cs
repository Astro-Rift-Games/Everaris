using System;
using System.Collections.Generic;
using Fusion;

/// <summary>
/// Pure client-side Town ticket delivery & bounded retry tracker (FIX-94).
/// Tracks delivery per Town State Authority peer:
/// - Resets all entries when the local ticket changes.
/// - Drops entries for peers that are no longer State Authority (handling A -> B -> A round-trips).
/// - Retries rejected or unconfirmed deliveries with bounded exponential backoff so a legitimate
///   client recovers without changing ticket, reconnecting, or flooding network/console.
/// </summary>
public sealed class TownTicketDeliveryTracker
{
    public const int DefaultMaxAttempts = 4;
    public const float DefaultBaseRetryDelaySeconds = 0.5f;
    public const float DefaultUnconfirmedTimeoutSeconds = 1.5f;

    public enum DeliveryStatus
    {
        None,
        Pending,
        Confirmed,
        RetryScheduled,
        Exhausted
    }

    private struct Entry
    {
        public DeliveryStatus Status;
        public int Attempts;
        public float NextAttemptAt;
        public TownSenderRegistrationResult LastResult;
    }

    private readonly int _maxAttempts;
    private readonly float _baseRetryDelaySeconds;
    private readonly float _unconfirmedTimeoutSeconds;
    private readonly Dictionary<PlayerRef, Entry> _entries = new();
    private readonly List<PlayerRef> _staleAuthorities = new();
    private string _activeTicket;

    public TownTicketDeliveryTracker(
        int maxAttempts = DefaultMaxAttempts,
        float baseRetryDelaySeconds = DefaultBaseRetryDelaySeconds,
        float unconfirmedTimeoutSeconds = DefaultUnconfirmedTimeoutSeconds)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _baseRetryDelaySeconds = Math.Max(0.05f, baseRetryDelaySeconds);
        _unconfirmedTimeoutSeconds = Math.Max(0.1f, unconfirmedTimeoutSeconds);
    }

    public string ActiveTicket => _activeTicket;

    public DeliveryStatus GetStatus(PlayerRef authority) =>
        _entries.TryGetValue(authority, out Entry entry) ? entry.Status : DeliveryStatus.None;

    public int GetAttempts(PlayerRef authority) =>
        _entries.TryGetValue(authority, out Entry entry) ? entry.Attempts : 0;

    /// <summary>
    /// Evaluates the current set of distinct Town State Authority peers and fills <paramref name="authoritiesToSend"/>
    /// with every authority that should receive <paramref name="rawTicket"/> at time <paramref name="now"/>.
    /// Returns true if any authority newly transitioned to <see cref="DeliveryStatus.Exhausted"/>.
    /// </summary>
    public bool CollectDueDeliveries(
        string rawTicket,
        IReadOnlyList<PlayerRef> currentAuthorities,
        float now,
        List<PlayerRef> authoritiesToSend,
        out PlayerRef newlyExhaustedAuthority)
    {
        authoritiesToSend.Clear();
        newlyExhaustedAuthority = PlayerRef.None;
        if (string.IsNullOrWhiteSpace(rawTicket) || currentAuthorities == null)
        {
            return false;
        }

        if (!string.Equals(_activeTicket, rawTicket, StringComparison.Ordinal))
        {
            _activeTicket = rawTicket;
            _entries.Clear();
        }

        // Remove authorities no longer active so an A -> B -> A migration always re-delivers to A.
        _staleAuthorities.Clear();
        foreach (PlayerRef tracked in _entries.Keys)
        {
            if (!Contains(currentAuthorities, tracked))
            {
                _staleAuthorities.Add(tracked);
            }
        }

        for (int i = 0; i < _staleAuthorities.Count; i++)
        {
            _entries.Remove(_staleAuthorities[i]);
        }

        bool anyExhausted = false;
        for (int i = 0; i < currentAuthorities.Count; i++)
        {
            PlayerRef authority = currentAuthorities[i];
            if (authority.IsNone || Contains(authoritiesToSend, authority))
            {
                continue;
            }

            if (!_entries.TryGetValue(authority, out Entry entry))
            {
                _entries[authority] = new Entry
                {
                    Status = DeliveryStatus.Pending,
                    Attempts = 1,
                    NextAttemptAt = now + _unconfirmedTimeoutSeconds
                };
                authoritiesToSend.Add(authority);
                continue;
            }

            if (entry.Status == DeliveryStatus.Confirmed || entry.Status == DeliveryStatus.Exhausted)
            {
                continue;
            }

            if (now < entry.NextAttemptAt)
            {
                continue;
            }

            if (entry.Attempts >= _maxAttempts)
            {
                entry.Status = DeliveryStatus.Exhausted;
                _entries[authority] = entry;
                if (!anyExhausted)
                {
                    anyExhausted = true;
                    newlyExhaustedAuthority = authority;
                }
                continue;
            }

            entry.Attempts++;
            entry.Status = DeliveryStatus.Pending;
            entry.NextAttemptAt = now + ComputeUnconfirmedTimeout(entry.Attempts);
            _entries[authority] = entry;
            authoritiesToSend.Add(authority);
        }

        return anyExhausted;
    }

    /// <summary>
    /// Records an authority's acknowledgement or rejection for the currently active ticket.
    /// Returns true if the authority transitioned to <see cref="DeliveryStatus.Exhausted"/>.
    /// </summary>
    public bool RecordResponse(PlayerRef authority, TownSenderRegistrationResult result, float now)
    {
        if (authority.IsNone)
        {
            return false;
        }

        if (!_entries.TryGetValue(authority, out Entry entry))
        {
            entry = new Entry { Attempts = 1 };
        }

        entry.LastResult = result;
        if (result == TownSenderRegistrationResult.Accepted)
        {
            entry.Status = DeliveryStatus.Confirmed;
            entry.NextAttemptAt = 0f;
            _entries[authority] = entry;
            return false;
        }

        if (entry.Attempts >= _maxAttempts)
        {
            bool wasExhausted = entry.Status == DeliveryStatus.Exhausted;
            entry.Status = DeliveryStatus.Exhausted;
            entry.NextAttemptAt = 0f;
            _entries[authority] = entry;
            return !wasExhausted;
        }

        entry.Status = DeliveryStatus.RetryScheduled;
        entry.NextAttemptAt = now + ComputeRetryDelay(entry.Attempts);
        _entries[authority] = entry;
        return false;
    }

    public void RemoveAuthority(PlayerRef authority) => _entries.Remove(authority);

    public void Clear()
    {
        _entries.Clear();
        _staleAuthorities.Clear();
        _activeTicket = null;
    }

    private float ComputeRetryDelay(int completedAttempts)
    {
        int shift = Math.Max(0, Math.Min(completedAttempts - 1, 4));
        return _baseRetryDelaySeconds * (1 << shift);
    }

    private float ComputeUnconfirmedTimeout(int attempts)
    {
        int shift = Math.Max(0, Math.Min(attempts - 1, 3));
        return _unconfirmedTimeoutSeconds * (1 << shift);
    }

    private static bool Contains(IReadOnlyList<PlayerRef> list, PlayerRef value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return true;
            }
        }
        return false;
    }
}
