using System;
using System.Threading.Tasks;
using Grimhold.Backend;

public static class RemoteIdempotentRetryPolicy
{
    public const int MaxRetries = 3;
    internal static int BackoffMs = 1000;
    internal static Func<int, Task> DelayAsync = Task.Delay;

    /// <summary>
    /// Executes an idempotent remote operation, with at most MaxRetries attempts.
    /// Retries classified transient errors and transport failures only. Revision conflicts stop:
    /// none of the wrapped operations refreshes authoritative revision state.
    /// Do NOT use this for non-idempotent endpoints or blind expectedRevision replay.
    /// </summary>
    public static async Task<(bool success, T result, BackendError error)> ExecuteWithRetryAsync<T>(
        Func<Task<(bool success, T result, BackendError error)>> operation,
        Action<BackendError> onRetry = null)
    {
        int retries = 0;
        while (retries < MaxRetries)
        {
            var (success, result, error) = await operation();
            if (success)
            {
                return (true, result, default);
            }

            if (BackendErrorClassifier.Classify(error.error) == BackendErrorClass.Retryable ||
                BackendErrorUtility.IsTransportFailure(error.error))
            {
                retries++;
                if (retries >= MaxRetries)
                {
                    UnityEngine.Debug.LogError($"[RemoteIdempotentRetryPolicy] Max retries reached for error {error.error}. Aborting.");
                    return (false, default, error);
                }

                int delay = BackendRetrySchedule.DelayMilliseconds(error, retries, BackoffMs);
                onRetry?.Invoke(error);
                UnityEngine.Debug.LogWarning($"[RemoteIdempotentRetryPolicy] {error.error} detected. Retrying {retries}/{MaxRetries} in {delay}ms...");
                await DelayAsync(delay);
                continue;
            }

            // Fatal error, do not retry
            return (false, default, error);
        }

        return (false, default, new BackendError { error = "MAX_RETRIES_EXCEEDED", message = "Exceeded maximum retries for operation." });
    }

    public static async Task<(bool success, BackendError error)> ExecuteWithRetryAsync(
        Func<Task<(bool success, BackendError error)>> operation)
    {
        var (success, result, error) = await ExecuteWithRetryAsync(
            async () =>
            {
                var (opSuccess, opError) = await operation();
                return (opSuccess, true, opError);
            });

        return (success, error);
    }
}
