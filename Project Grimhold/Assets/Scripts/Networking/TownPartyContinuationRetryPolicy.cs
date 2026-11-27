using System;

/// <summary>
/// Pure retry-backoff and expiration policy for Town Party continuation restoration (FIX-07).
/// Prevents an ACK from triggering an immediate re-send loop while a Duo partner is still in Dungeon,
/// bounds requests to at most one per backoff interval, and transitions the waiting player to a usable
/// Solo Party once the continuation window expires.
/// </summary>
public sealed class TownPartyContinuationRetryPolicy
{
    public const float DefaultBaseIntervalSeconds = 1.0f;
    public const float DefaultMaxIntervalSeconds = 5.0f;
    public const float DefaultExpirationSeconds = 45.0f;

    public enum PollAction
    {
        Wait,
        SendRestoreRequest,
        Expired
    }

    private readonly float _baseIntervalSeconds;
    private readonly float _maxIntervalSeconds;
    private readonly float _expirationSeconds;
    private TownPartyContinuationContext _activeContext;
    private float _startedAt;
    private float _nextSendAt;
    private int _attempts;
    private bool _hasActiveWait;

    public TownPartyContinuationRetryPolicy(
        float baseIntervalSeconds = DefaultBaseIntervalSeconds,
        float maxIntervalSeconds = DefaultMaxIntervalSeconds,
        float expirationSeconds = DefaultExpirationSeconds)
    {
        _baseIntervalSeconds = Math.Max(0.1f, baseIntervalSeconds);
        _maxIntervalSeconds = Math.Max(_baseIntervalSeconds, maxIntervalSeconds);
        _expirationSeconds = Math.Max(_baseIntervalSeconds, expirationSeconds);
    }

    public int Attempts => _attempts;
    public bool HasActiveWait => _hasActiveWait;

    public PollAction Evaluate(TownPartyContinuationContext context, float now)
    {
        if (context == null)
        {
            Reset();
            return PollAction.Wait;
        }

        if (!_hasActiveWait || _activeContext == null || !_activeContext.Equals(context))
        {
            _activeContext = context;
            _startedAt = now;
            _attempts = 1;
            _nextSendAt = now + _baseIntervalSeconds;
            _hasActiveWait = true;
            return PollAction.SendRestoreRequest;
        }

        if (now - _startedAt >= _expirationSeconds)
        {
            Reset();
            return PollAction.Expired;
        }

        if (now < _nextSendAt)
        {
            return PollAction.Wait;
        }

        _attempts++;
        _nextSendAt = now + ComputeInterval(_attempts);
        return PollAction.SendRestoreRequest;
    }

    /// <summary>
    /// Called when the Town authority acknowledges a still-pending continuation claim (companion has
    /// not returned yet). Enforces backoff instead of requesting an immediate retry.
    /// </summary>
    public void OnAcknowledgedPending(float now)
    {
        if (!_hasActiveWait)
        {
            return;
        }

        float minNext = now + ComputeInterval(Math.Max(1, _attempts));
        if (_nextSendAt < minNext)
        {
            _nextSendAt = minNext;
        }
    }

    /// <summary>
    /// Allows one immediate re-submission after a Town authority epoch change (migration) without
    /// extending the overall expiration deadline.
    /// </summary>
    public void OnAuthorityEpochChanged()
    {
        if (_hasActiveWait)
        {
            _nextSendAt = 0f;
        }
    }

    public void Reset()
    {
        _activeContext = null;
        _startedAt = 0f;
        _nextSendAt = 0f;
        _attempts = 0;
        _hasActiveWait = false;
    }

    private float ComputeInterval(int attempts)
    {
        int shift = Math.Max(0, Math.Min(attempts - 1, 4));
        float backoff = _baseIntervalSeconds * (1 << shift);
        return Math.Min(_maxIntervalSeconds, backoff);
    }
}
