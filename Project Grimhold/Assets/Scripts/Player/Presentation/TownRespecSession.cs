using System;
using System.Threading.Tasks;

/// <summary>
/// Screen-local Town attribute respec intention (TASK-304, Doc 04 §11, Doc 22 §6). A request only opens an explicit
/// confirmation; confirming sends one backend request and only its authoritative snapshot reaches the profile.
/// The transaction id is created once per confirmed intent and kept until the server's answer is known, so a
/// retry after a lost response is replayed by the server and never charged twice.
/// </summary>
public sealed class TownRespecSession : IDisposable
{
    private readonly LocalProfileStore _store;
    private readonly AttributeRespecService _service;
    private readonly Func<bool> _canMutateBuild;
    private readonly Func<bool> _isBusy;
    private readonly Func<string> _newTransactionId;
    private string _transactionId;
    private bool _disposed;

    public event Action Changed;
    public bool IsConfirming { get; private set; }
    public bool IsPending { get; private set; }
    public string Message { get; private set; }

    /// <param name="service">Null leaves the respec unavailable.</param>
    /// <param name="canMutateBuild">Ready-lock gate shared with attribute assignment; null means always allowed.</param>
    /// <param name="isBusy">True while another attribute operation is in flight.</param>
    /// <param name="newTransactionId">Test seam; defaults to the shared timestamped <see cref="ShopTransactionId"/>.</param>
    public TownRespecSession(
        LocalProfileStore store,
        AttributeRespecService service,
        Func<bool> canMutateBuild = null,
        Func<bool> isBusy = null,
        Func<string> newTransactionId = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _service = service;
        _canMutateBuild = canMutateBuild;
        _isBusy = isBusy;
        _newTransactionId = newTransactionId ?? CreateTransactionId;
        _store.ProfileCommitted += OnCommitted;
    }

    public TownRespecPresentation Presentation
    {
        get
        {
            TownRespecBlockReason block = Evaluate();
            int? cost = _store.RespecCost;
            return new TownRespecPresentation(
                cost,
                block,
                TownRespecMessages.BlockMessage(block, cost),
                IsConfirming,
                IsPending,
                IsConfirming && cost.HasValue ? TownRespecMessages.ConfirmationText(cost.Value) : null,
                Message);
        }
    }

    /// <summary>Reads the server's price when the profile does not have it yet, then notifies the panel.</summary>
    public async Task RefreshCostAsync()
    {
        if (_disposed || _service == null) return;
        await _service.EnsureCostAsync();
        if (!_disposed) Changed?.Invoke();
    }

    /// <summary>Opens the confirmation; a blocked state shows its reason instead and returns false.</summary>
    public bool RequestRespec()
    {
        if (_disposed || IsPending) return false;
        TownRespecBlockReason block = Evaluate();
        if (block != TownRespecBlockReason.None) return Reject(block);
        IsConfirming = true;
        Message = null;
        Changed?.Invoke();
        return true;
    }

    public void Cancel()
    {
        if (_disposed || IsPending) return;
        IsConfirming = false;
        Message = null;
        Changed?.Invoke();
    }

    /// <summary>Sends the confirmed intent once; repeated calls while pending are ignored.</summary>
    public async Task ConfirmAsync()
    {
        if (_disposed || !IsConfirming || IsPending) return;
        // Ready may have been pressed, or the build changed, since the confirmation opened.
        TownRespecBlockReason block = Evaluate();
        if (block != TownRespecBlockReason.None)
        {
            Reject(block);
            return;
        }

        IsPending = true;
        IsConfirming = false;
        Message = TownRespecMessages.PendingMessage;
        Changed?.Invoke();
        try
        {
            _transactionId ??= _newTransactionId();
            AttributeRespecOutcome outcome = await _service.RespecAsync(_transactionId);
            if (outcome.IsFinal) _transactionId = null;
            Message = TownRespecMessages.OutcomeMessage(outcome);
        }
        finally
        {
            IsPending = false;
            if (!_disposed) Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _store.ProfileCommitted -= OnCommitted;
        Changed = null;
    }

    private TownRespecBlockReason Evaluate()
    {
        if (_service == null) return TownRespecBlockReason.Unavailable;
        if (_isBusy != null && _isBusy()) return TownRespecBlockReason.Busy;
        if (_canMutateBuild != null && !_canMutateBuild()) return TownRespecBlockReason.BuildLocked;
        if (_store.PendingReservation != null) return TownRespecBlockReason.ReservationPending;
        if (!_store.TryGetCharacterAttributeState(out CharacterAttributeState attributes)) return TownRespecBlockReason.Unavailable;
        if (!_store.RespecCost.HasValue) return TownRespecBlockReason.CostUnavailable;
        if (AssignedPoints(attributes) == 0) return TownRespecBlockReason.NoAssignedPoints;
        return _store.GetCurrency() < _store.RespecCost.Value
            ? TownRespecBlockReason.InsufficientGold
            : TownRespecBlockReason.None;
    }

    /// <summary>Doc 04 §11: the minimum per attribute is 0, so the sum is what a respec can give back.</summary>
    private static long AssignedPoints(in CharacterAttributeState attributes) =>
        (long)attributes.Vitality + attributes.Resistance + attributes.Strength +
        attributes.Dexterity + attributes.Intelligence + attributes.Luck;

    private bool Reject(TownRespecBlockReason block)
    {
        IsConfirming = false;
        Message = TownRespecMessages.BlockMessage(block, _store.RespecCost);
        Changed?.Invoke();
        return false;
    }

    /// <summary>
    /// A commit while the dialog is open (a reread, an assignment) may leave nothing to respec. Any commit outside our
    /// own request also means the authoritative state moved on, so the kept transaction id is released: reusing it
    /// would make the server replay the old receipt instead of applying the new intent, while a delayed original
    /// request carries the old expected revision and is rejected as a conflict.
    /// </summary>
    private void OnCommitted(ProfileId profile)
    {
        if (_disposed || profile != _store.ProfileId || IsPending) return;
        _transactionId = null;
        if (!IsConfirming) return;
        TownRespecBlockReason block = Evaluate();
        if (block != TownRespecBlockReason.None) Reject(block);
    }

    private static string CreateTransactionId() =>
        new ShopTransactionId(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Guid.NewGuid()).ToString();
}
