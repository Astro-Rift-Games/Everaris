using System;

/// <summary>
/// Authoritative Host publication state for a Party member's extraction result (FIX-08).
/// </summary>
public enum AuthorityPublishStatus : byte
{
    Idle = 0,
    Publishing = 1,
    Retrying = 2,
    Published = 3,
    Failed = 4
}

/// <summary>
/// Pure retry-and-gating coordinator for Host authoritative extraction result publication (FIX-08).
/// Guarantees:
/// 1. The client's local-commit RPC is NEVER dispatched before the Host confirms backend publication.
/// 2. Transient publication failures retry with bounded exponential backoff using the same idempotent
///    receipt (preventing duplicate authoritative results).
/// 3. Exposes clear publication status (Publishing / Retrying / Published / Failed) for the Results UI.
/// </summary>
public sealed class ExtractionAuthorityPublishCoordinator
{
    public const int DefaultMaxRounds = 4;
    public const float DefaultBaseBackoffSeconds = 1.0f;
    public const float DefaultMaxBackoffSeconds = 8.0f;

    private readonly int _maxRounds;
    private readonly float _baseBackoffSeconds;
    private readonly float _maxBackoffSeconds;

    private int _activeSequence;
    private int _publishedSequence;
    private int _roundsAttempted;
    private float _nextAttemptAt;

    public AuthorityPublishStatus Status { get; private set; } = AuthorityPublishStatus.Idle;
    public int RoundsAttempted => _roundsAttempted;
    public int PublishedSequence => _publishedSequence;

    public ExtractionAuthorityPublishCoordinator(
        int maxRounds = DefaultMaxRounds,
        float baseBackoffSeconds = DefaultBaseBackoffSeconds,
        float maxBackoffSeconds = DefaultMaxBackoffSeconds)
    {
        _maxRounds = Math.Max(1, maxRounds);
        _baseBackoffSeconds = Math.Max(0.1f, baseBackoffSeconds);
        _maxBackoffSeconds = Math.Max(_baseBackoffSeconds, maxBackoffSeconds);
    }

    /// <summary>True only once publication for <paramref name="resultSequence"/> has been confirmed.</summary>
    public bool ShouldDispatchCommitRpc(int resultSequence) =>
        resultSequence > 0 && _publishedSequence == resultSequence && Status == AuthorityPublishStatus.Published;

    /// <summary>True when a new publication attempt may start at time <paramref name="now"/>.</summary>
    public bool CanStartAttempt(int resultSequence, float now)
    {
        if (resultSequence <= 0)
        {
            return false;
        }

        EnsureSequence(resultSequence);
        if (Status == AuthorityPublishStatus.Publishing ||
            Status == AuthorityPublishStatus.Published ||
            Status == AuthorityPublishStatus.Failed)
        {
            return false;
        }

        return now >= _nextAttemptAt;
    }

    public void MarkAttemptStarted(int resultSequence)
    {
        EnsureSequence(resultSequence);
        _roundsAttempted++;
        Status = _roundsAttempted > 1 ? AuthorityPublishStatus.Retrying : AuthorityPublishStatus.Publishing;
    }

    public void MarkAttemptSucceeded(int resultSequence)
    {
        EnsureSequence(resultSequence);
        _publishedSequence = resultSequence;
        _nextAttemptAt = 0f;
        Status = AuthorityPublishStatus.Published;
    }

    /// <summary>
    /// Records a failed publish attempt. Schedules a backoff retry while retryable and within budget;
    /// otherwise transitions to <see cref="AuthorityPublishStatus.Failed"/>.
    /// </summary>
    public AuthorityPublishStatus MarkAttemptFailed(int resultSequence, float now, bool isRetryable)
    {
        EnsureSequence(resultSequence);
        if (isRetryable && _roundsAttempted < _maxRounds)
        {
            int shift = Math.Max(0, Math.Min(_roundsAttempted - 1, 4));
            float delay = Math.Min(_maxBackoffSeconds, _baseBackoffSeconds * (1 << shift));
            _nextAttemptAt = now + delay;
            Status = AuthorityPublishStatus.Retrying;
            return Status;
        }

        _nextAttemptAt = 0f;
        Status = AuthorityPublishStatus.Failed;
        return Status;
    }

    public void Reset()
    {
        _activeSequence = 0;
        _publishedSequence = 0;
        _roundsAttempted = 0;
        _nextAttemptAt = 0f;
        Status = AuthorityPublishStatus.Idle;
    }

    private void EnsureSequence(int resultSequence)
    {
        if (_activeSequence == resultSequence)
        {
            return;
        }

        _activeSequence = resultSequence;
        _roundsAttempted = 0;
        _nextAttemptAt = 0f;
        Status = _publishedSequence == resultSequence ? AuthorityPublishStatus.Published : AuthorityPublishStatus.Idle;
    }
}
