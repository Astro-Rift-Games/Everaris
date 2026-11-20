using System;
using System.Threading.Tasks;
using Grimhold.Backend;

public enum PreStartRefundOutcome
{
    /// <summary>The backend refunded and the local ownership was restored.</summary>
    Refunded,
    /// <summary>The backend refunded but the local store could not apply it; the next hydration repairs it.</summary>
    LocalRestoreFailed,
    /// <summary>
    /// The backend answered success with nothing refunded (no active reservation, for example a retry whose
    /// first attempt already succeeded). Local ownership is left to the next profile sync: restoring would
    /// invent items the backend no longer owns, and a loss clear has no reservation to consume.
    /// </summary>
    NothingToRefund,
    /// <summary>The backend rejected the refund definitively and the participation was consumed as a loss.</summary>
    FellBackToLoss,
    /// <summary>The refund did not complete (transport/transient exhaustion); the reservation is left for login recovery.</summary>
    Failed
}

/// <summary>
/// Orders the FIX-95 pre-start refund: the in-flight admission settles first, the backend refund
/// (idempotently retried by its caller) decides, and only its confirmed refund restores local ownership.
/// A definitive rejection falls back to the existing loss clear.
/// </summary>
public static class PreStartRefundFlow
{
    /// <summary>Backend answers that mean the refund can never succeed for this reservation.</summary>
    public static bool IsTerminalRejection(BackendError error)
    {
        switch (error.error)
        {
            case "RESERVATION_MISMATCH":
            case "RAID_ALREADY_STARTED":
            case "REFUND_WINDOW_EXPIRED":
            case "EXTRACTION_RESULT_PENDING":
            case "RESULT_ALREADY_CONSUMED":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Waits for an in-flight admission request without letting its failure matter: the backend decides
    /// the refund on its own state, and a lost admit only means the reservation is not marked admitted.
    /// </summary>
    public static async Task AwaitAdmissionAsync(Task admission)
    {
        if (admission == null) return;
        try { await admission; }
        catch (Exception) { /* the admission request reports its own failure */ }
    }

    public static async Task<PreStartRefundOutcome> ExecuteAsync(
        Task admission,
        Func<Task<(bool success, bool refunded, BackendError error)>> refund,
        Func<StashOperationResult> restoreLocal,
        Func<Task> clearAsLoss)
    {
        await AwaitAdmissionAsync(admission);
        var (success, refunded, error) = await refund();
        if (success && !refunded) return PreStartRefundOutcome.NothingToRefund;
        if (success)
            return restoreLocal() == StashOperationResult.Success
                ? PreStartRefundOutcome.Refunded
                : PreStartRefundOutcome.LocalRestoreFailed;

        if (!IsTerminalRejection(error)) return PreStartRefundOutcome.Failed;
        await clearAsLoss();
        return PreStartRefundOutcome.FellBackToLoss;
    }
}
