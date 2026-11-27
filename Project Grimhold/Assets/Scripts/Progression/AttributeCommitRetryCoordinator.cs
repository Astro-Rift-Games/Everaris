using System;
using System.Threading.Tasks;
using Grimhold.Backend;

/// <summary>
/// Idempotent attribute-point spend runner (FIX-36). Creates one timestamped <see cref="ShopTransactionId"/>
/// per spend intent and replays the exact same request (<c>attribute</c>, <c>expectedRevision</c>,
/// <c>transactionId</c>) across transient transport/server failures so a lost response converges to the
/// backend's authoritative attributes and revision without double spending.
/// </summary>
public static class AttributeCommitRetryCoordinator
{
    public static Task<(bool success, CommitProgressionResult result, BackendError error)> ExecuteAsync(
        string attributeName,
        int expectedRevision,
        Func<CommitProgressionRequest, Task<(bool success, CommitProgressionResult result, BackendError error)>> sendAsync,
        Func<string> createTransactionId = null,
        Action<BackendError> onRetry = null)
    {
        if (sendAsync == null)
        {
            throw new ArgumentNullException(nameof(sendAsync));
        }

        string transactionId = (createTransactionId ?? CreateTransactionId)();
        var request = new CommitProgressionRequest
        {
            attribute = attributeName,
            expectedRevision = expectedRevision,
            transactionId = transactionId
        };

        // Do NOT retry REVISION_CONFLICT blindly: a genuine conflict means another mutation happened
        // and the caller should reconcile. If the first attempt actually committed on the server and
        // its response was lost, the retry hits the backend's receipt check BEFORE revision check
        // and returns 200 with alreadySecured=true.
        return RemoteIdempotentRetryPolicy.ExecuteWithRetryAsync(
            () => sendAsync(request),
            onRetry: onRetry,
            retryRevisionConflict: false);
    }

    public static string CreateTransactionId() =>
        new ShopTransactionId(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Guid.NewGuid()).ToString();
}
