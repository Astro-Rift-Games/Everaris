using System;
using System.Collections.Generic;
using Fusion;

/// <summary>
/// Deduplicates repetitive Town security warnings per sender while the sender remains in the same
/// unresolved state (FIX-59). Cleared when the sender verifies, changes state, or leaves.
/// </summary>
public sealed class TownSecurityWarningGate
{
    private readonly Dictionary<string, string> _lastReasonBySender = new(StringComparer.Ordinal);

    /// <summary>Returns true only on the first warning for this sender+reason episode.</summary>
    public bool ShouldLog(string senderKey, string reasonKey)
    {
        if (string.IsNullOrEmpty(senderKey))
        {
            return true;
        }

        if (_lastReasonBySender.TryGetValue(senderKey, out string previous) &&
            string.Equals(previous, reasonKey, StringComparison.Ordinal))
        {
            return false;
        }

        _lastReasonBySender[senderKey] = reasonKey ?? string.Empty;
        return true;
    }

    public void ResetSender(string senderKey)
    {
        if (!string.IsNullOrEmpty(senderKey))
        {
            _lastReasonBySender.Remove(senderKey);
        }
    }

    public void Clear() => _lastReasonBySender.Clear();
}

/// <summary>
/// Bounded deferred queue for Town authority RPCs whose sender's ticket verification is still in flight (FIX-59).
/// Executes queued actions in order once verification completes, or invokes their rejection callback
/// if verification fails or times out so the player never experiences a silent drop.
/// </summary>
public sealed class TownPendingRpcQueue
{
    public const int DefaultMaxTotalEntries = 16;
    public const int DefaultMaxEntriesPerSender = 3;
    public const float DefaultTimeoutSeconds = 5f;

    public delegate bool ResolveSenderFunc(PlayerRef sender, out ProfileId profileId, out TownSenderResolutionStatus status);

    private readonly struct Entry
    {
        public Entry(PlayerRef sender, float enqueuedAt, Action<ProfileId> onVerified, Action onRejected)
        {
            Sender = sender;
            EnqueuedAt = enqueuedAt;
            OnVerified = onVerified;
            OnRejected = onRejected;
        }

        public PlayerRef Sender { get; }
        public float EnqueuedAt { get; }
        public Action<ProfileId> OnVerified { get; }
        public Action OnRejected { get; }
    }

    private readonly List<Entry> _entries = new();
    private readonly int _maxTotalEntries;
    private readonly int _maxEntriesPerSender;
    private readonly float _timeoutSeconds;

    public TownPendingRpcQueue(
        int maxTotalEntries = DefaultMaxTotalEntries,
        int maxEntriesPerSender = DefaultMaxEntriesPerSender,
        float timeoutSeconds = DefaultTimeoutSeconds)
    {
        _maxTotalEntries = Math.Max(1, maxTotalEntries);
        _maxEntriesPerSender = Math.Max(1, maxEntriesPerSender);
        _timeoutSeconds = Math.Max(0.1f, timeoutSeconds);
    }

    public int Count => _entries.Count;

    /// <summary>
    /// Enqueues a deferred Town action for <paramref name="sender"/>. Returns false if the queue is
    /// saturated (caller should immediately notify the sender of failure).
    /// </summary>
    public bool Enqueue(PlayerRef sender, float now, Action<ProfileId> onVerified, Action onRejected)
    {
        if (sender.IsNone || onVerified == null)
        {
            return false;
        }

        if (_entries.Count >= _maxTotalEntries || CountForSender(sender) >= _maxEntriesPerSender)
        {
            return false;
        }

        _entries.Add(new Entry(sender, now, onVerified, onRejected));
        return true;
    }

    /// <summary>
    /// Evaluates queued actions against the current verification status. Verified actions run in FIFO order;
    /// terminally rejected or timed-out actions invoke their rejection callback once and are removed.
    /// </summary>
    public void Drain(float now, ResolveSenderFunc resolver)
    {
        if (_entries.Count == 0 || resolver == null)
        {
            return;
        }

        int index = 0;
        while (index < _entries.Count)
        {
            Entry entry = _entries[index];
            bool verified = resolver(entry.Sender, out ProfileId profileId, out TownSenderResolutionStatus status);
            if (verified && status == TownSenderResolutionStatus.Verified)
            {
                _entries.RemoveAt(index);
                entry.OnVerified(profileId);
                continue;
            }

            if (status == TownSenderResolutionStatus.PendingVerification &&
                now - entry.EnqueuedAt < _timeoutSeconds)
            {
                index++;
                continue;
            }

            _entries.RemoveAt(index);
            entry.OnRejected?.Invoke();
        }
    }

    public void RemoveSender(PlayerRef sender)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Sender == sender)
            {
                _entries.RemoveAt(i);
            }
        }
    }

    public void Clear() => _entries.Clear();

    private int CountForSender(PlayerRef sender)
    {
        int count = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Sender == sender)
            {
                count++;
            }
        }
        return count;
    }
}
