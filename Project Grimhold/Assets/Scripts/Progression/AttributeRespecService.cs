using System;
using System.Threading.Tasks;
using Grimhold.Backend;
using UnityEngine;

/// <summary>
/// Remote-first attribute respec (TASK-304, Doc 04 §11). The caller owns the transaction id: it creates one per
/// confirmed intent and passes the same id again while <see cref="AttributeRespecOutcome.IsFinal"/> is false, so a
/// lost response is replayed by the server instead of charged twice. Only the authoritative snapshot is applied to
/// the local profile, in one commit; any failure leaves the profile as it was and rereads the backend.
/// </summary>
public sealed class AttributeRespecService
{
    public delegate Task<(bool success, RespecResult data, BackendError error)> BackendCall(
        string transactionId, int expectedRevision);

    private readonly LocalProfileStore _store;
    private readonly BackendCall _backend;
    private readonly Func<Task<bool>> _reconcile;
    private readonly Func<Task<int?>> _fetchCost;

    /// <param name="backend">Null means no respec endpoint is reachable.</param>
    /// <param name="reconcile">Rereads the backend profile; returns whether the local profile now matches it.</param>
    /// <param name="fetchCost">Reads the server's respec price; used only while the store does not know it yet.</param>
    public AttributeRespecService(
        LocalProfileStore store,
        BackendCall backend,
        Func<Task<bool>> reconcile,
        Func<Task<int?>> fetchCost = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _backend = backend;
        _reconcile = reconcile;
        _fetchCost = fetchCost;
    }

    /// <summary>Whether the server's respec cost is known, asking the backend once when it is not.</summary>
    public async Task<bool> EnsureCostAsync()
    {
        if (_store.RespecCost.HasValue) return true;
        if (_fetchCost == null) return false;
        try
        {
            int? cost = await _fetchCost();
            if (!cost.HasValue) return false;
            _store.SetRespecCost(cost.Value);
            return _store.RespecCost.HasValue;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[{nameof(AttributeRespecService)}] The respec cost could not be read: {exception.Message}");
            return false;
        }
    }

    public async Task<AttributeRespecOutcome> RespecAsync(string transactionId)
    {
        if (_backend == null || string.IsNullOrEmpty(transactionId) || !_store.IsAvailable)
        {
            return new AttributeRespecOutcome(AttributeRespecStatus.Unavailable, null, null, true);
        }

        (bool success, RespecResult data, BackendError error) response;
        try
        {
            response = await _backend(transactionId, _store.RemoteRevision);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[{nameof(AttributeRespecService)}] Respec outcome unknown: {exception.Message}");
            response = (false, default, new BackendError { error = "REMOTE_RESULT_UNKNOWN", message = exception.Message });
        }

        return response.success
            ? await ApplyAsync(response.data)
            : await RejectAsync(response.error);
    }

    private async Task<AttributeRespecOutcome> ApplyAsync(RespecResult data)
    {
        if (_store.ApplyConfirmedRespec(data) == StashOperationResult.Success)
        {
            return new AttributeRespecOutcome(
                data.alreadySecured ? AttributeRespecStatus.AlreadyApplied : AttributeRespecStatus.Applied,
                null, null, true);
        }

        // The server did apply the respec: take its state from a reread instead of the refused snapshot.
        Debug.LogWarning($"[{nameof(AttributeRespecService)}] The confirmed respec snapshot was refused locally; reconciling.");
        return new AttributeRespecOutcome(
            await ReconcileAsync() ? AttributeRespecStatus.Diverged : AttributeRespecStatus.RecoveryFailed,
            null, null, true);
    }

    private async Task<AttributeRespecOutcome> RejectAsync(BackendError error)
    {
        string code = error.error;
        bool isFinal = IsDefinitive(code);
        if (code == BackendErrorUtility.Unauthorized)
        {
            return new AttributeRespecOutcome(AttributeRespecStatus.Rejected, code, error.message, true);
        }

        // Even a refusal can leave the local revision or balance stale, and a lost answer may follow a committed respec.
        if (!await ReconcileAsync())
        {
            return new AttributeRespecOutcome(AttributeRespecStatus.RecoveryFailed, code, error.message, isFinal);
        }

        return new AttributeRespecOutcome(
            isFinal ? AttributeRespecStatus.Rejected : AttributeRespecStatus.Unconfirmed, code, error.message, isFinal);
    }

    /// <summary>Codes proving the server decided: the respec was refused and its transaction id is spent or unneeded.</summary>
    private static bool IsDefinitive(string code) => code switch
    {
        "NO_ASSIGNED_POINTS" or "INSUFFICIENT_FUNDS" or "RESERVATION_PENDING" or "REVISION_CONFLICT" or
        "TRANSACTION_ID_REUSED" or "TRANSACTION_RETENTION_EXCEEDED" or "INVENTORY_FULL" or
        "INVENTORY_MIGRATION_REQUIRED" or "VALIDATION_FAILED" or "INVALID_TRANSACTION_ID" or "INVALID_REVISION" or
        "CHARACTER_NOT_FOUND" or "UNAUTHORIZED" => true,
        _ => false
    };

    private async Task<bool> ReconcileAsync()
    {
        try
        {
            return _reconcile != null && await _reconcile();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[{nameof(AttributeRespecService)}] Reconciliation failed: {exception.Message}");
            return false;
        }
    }
}
