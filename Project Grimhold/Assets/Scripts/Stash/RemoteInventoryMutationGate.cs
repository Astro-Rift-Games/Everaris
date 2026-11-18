using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Serializes revision-guarded remote inventory mutations so each one reads the revision committed
/// by the previous one instead of racing it into REVISION_CONFLICT. The gate is not reentrant:
/// never start a gated operation from inside another one.
/// </summary>
public sealed class RemoteInventoryMutationGate
{
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public Task WaitAsync() => _semaphore.WaitAsync();

    public void Release() => _semaphore.Release();

    public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> operation)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        await _semaphore.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
