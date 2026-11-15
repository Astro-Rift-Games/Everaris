/// <summary>A locally accepted defeat whose exact backend receipt remains unacknowledged.</summary>
public sealed class PendingDefeatCommit
{
    public ProgressionReceipt Receipt { get; }
    public string ReservationId { get; }

    public PendingDefeatCommit(ProgressionReceipt receipt, string reservationId = null)
    {
        Receipt = receipt;
        ReservationId = reservationId;
    }
}
