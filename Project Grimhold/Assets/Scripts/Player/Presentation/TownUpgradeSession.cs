using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grimhold.Backend;

/// <summary>
/// Screen-local Town NPC upgrade intention (TASK-303, Doc 23 §§9, 11). Previews read the store without
/// mutating it; confirmation sends one backend request through the shared Town queue and only the
/// authoritative result is applied. A rejection leaves the local profile untouched.
/// </summary>
public sealed class TownUpgradeSession : IDisposable
{
    private readonly LocalProfileStore _store;
    private readonly StashTransferCoordinator _coordinator;
    private readonly LootDefinitionCatalog _catalog;
    private readonly TownMerchantType _merchant;
    private readonly Func<bool> _canMutateBuild;
    private readonly Func<bool> _isTradeInFlight;
    private bool _disposed;

    public const string BuildLockedMessage = "Cancela Ready para mejorar equipamiento";
    public const string TradeInFlightMessage = "Espera a que termine el intercambio en curso";

    public event Action Changed;
    public bool IsConfirming { get; private set; }
    public bool IsPending { get; private set; }
    public string Message { get; private set; }
    public UpgradeItemRequest Request { get; private set; }
    public NpcUpgradePreview SelectedPreview { get; private set; }

    /// <param name="canMutateBuild">Ready-lock gate shared with merchant trades; null means always allowed.</param>
    /// <param name="isTradeInFlight">True while a merchant trade is being submitted; upgrades wait for it.</param>
    public TownUpgradeSession(LocalProfileStore store, StashTransferCoordinator coordinator, LootDefinitionCatalog catalog,
        TownMerchantType merchant, Func<bool> canMutateBuild = null, Func<bool> isTradeInFlight = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coordinator = coordinator;
        _catalog = catalog;
        _merchant = merchant;
        _canMutateBuild = canMutateBuild;
        _isTradeInFlight = isTradeInFlight;
        _store.ProfileCommitted += OnCommitted;
    }

    /// <summary>Equipped pieces first, then Inventory instances; never Stash (Doc 23 §9).</summary>
    public IReadOnlyList<TownUpgradeTarget> Targets()
    {
        var targets = new List<TownUpgradeTarget>();
        var equipment = _store.GetPreparedEquipment();
        foreach (var slot in EquipmentSlotRules.AllSlots)
        {
            var instance = equipment.GetInstance(slot);
            var lootId = equipment.Get(slot);
            if (instance?.HasIdentity == true && IsUpgradable(lootId)) targets.Add(TownUpgradeTarget.Equipped(lootId, instance, slot));
        }
        foreach (var item in _store.GetLoadout())
            if (item.Amount == 1 && item.Instance?.HasIdentity == true && IsUpgradable(item.LootId))
                targets.Add(TownUpgradeTarget.InInventory(item.LootId, item.Instance, item.Slot));
        return targets;
    }

    public NpcUpgradePreview Preview(in TownUpgradeTarget target, NpcUpgradeOperation operation)
    {
        Resolve(target.LootId, out var definition);
        var subject = new NpcUpgradeSubject(definition, target.Instance?.Modifiers, target.Instance?.ArmorUpgradeLevel ?? 0);
        long gold = _store.GetCurrency();
        return NpcUpgradeRules.Preview(_merchant, operation, subject, gold > int.MaxValue ? int.MaxValue : (int)gold,
            Counts(_store.GetLoadout()), Counts(_store.GetStash()));
    }

    public bool Select(in TownUpgradeTarget target, NpcUpgradeOperation operation)
    {
        if (_disposed || IsPending) return false;
        if (!Available()) return false;
        var preview = Preview(target, operation);
        if (!preview.CanConfirm) return Reject(NpcUpgradeMessages.For(preview));
        SelectedPreview = preview;
        Request = new UpgradeItemRequest
        {
            transactionId = new ShopTransactionId(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Guid.NewGuid()).ToString(),
            expectedRevision = _store.RemoteRevision,
            merchant = _merchant == TownMerchantType.Arcanist ? "arcanist" : "blacksmith",
            operation = NpcUpgradeRules.WireName(operation),
            instanceId = target.Instance.InstanceId,
            location = target.IsEquipped ? UpgradeItemRequest.EquipmentLocation : UpgradeItemRequest.LoadoutLocation,
            loadoutSlot = target.IsEquipped ? 0 : target.LoadoutSlot,
            equipmentSlot = target.EquipmentSlot
        };
        IsConfirming = true;
        Message = null;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Drops a previous result or rejection text when the player moves on; never while pending.</summary>
    public void ClearMessage()
    {
        if (IsPending || string.IsNullOrEmpty(Message)) return;
        Message = null;
    }

    public void Cancel()
    {
        if (IsPending) return;
        IsConfirming = false;
        Message = null;
        Request = default;
        SelectedPreview = null;
        Changed?.Invoke();
    }

    /// <summary>Sends the selected intent once; repeated calls while pending are ignored.</summary>
    public async Task ConfirmAsync()
    {
        if (_disposed || !IsConfirming || IsPending) return;
        if (!MutationAllowed()) return;
        IsPending = true;
        IsConfirming = false;
        Message = "Aplicando mejora…";
        Changed?.Invoke();
        try
        {
            var result = _coordinator != null
                ? await _coordinator.UpgradeItemAsync(Request, _store) : StashTransferOutcome.Unavailable();
            Message = OutcomeMessage(result);
        }
        finally
        {
            IsPending = false;
            Request = default;
            SelectedPreview = null;
            if (!_disposed) Changed?.Invoke();
        }
    }

    public const string UnconfirmedMessage = "No se pudo confirmar la mejora; el inventario se sincronizó";
    public const string UnsyncedMessage = "No se pudo confirmar la mejora ni sincronizar el inventario. Cierra el mercader y vuelve a intentarlo";

    /// <summary>Status decides first (sync claims only when a reread succeeded); the code refines remote failures.</summary>
    public static string OutcomeMessage(in StashTransferOutcome outcome) => outcome.Status switch
    {
        StashTransferStatus.Success => "Mejora aplicada",
        StashTransferStatus.RecoveryFailed => UnsyncedMessage,
        StashTransferStatus.Diverged => "La mejora se confirmó; el inventario se sincronizó con el servidor",
        StashTransferStatus.Unavailable => "El servicio de mejoras no está disponible",
        _ => FailureMessage(outcome.RemoteErrorCode)
    };

    public static string FailureMessage(string code) => code switch
    {
        "INSUFFICIENT_FUNDS" => "Oro insuficiente para esta mejora",
        "INSUFFICIENT_MATERIALS" => NpcUpgradeMessages.MissingMaterialsStash,
        "UPGRADE_LIMIT_REACHED" => NpcUpgradeMessages.NpcLimit,
        "UPGRADE_GRADE_LOCKED" => NpcUpgradeMessages.GradeLocked,
        "UPGRADE_SOCKET_EXISTS" => NpcUpgradeMessages.SocketExists,
        "UPGRADE_WRONG_MERCHANT" or "UPGRADE_NOT_ELIGIBLE" => NpcUpgradeMessages.WrongMerchant,
        "INSTANCE_MISMATCH" => "El objeto ya no está disponible",
        "REVISION_CONFLICT" => "El inventario cambió. Vuelve a intentarlo",
        "RESERVATION_PENDING" => "No se puede modificar el inventario mientras hay una reserva de Raid pendiente",
        "INVENTORY_MIGRATION_REQUIRED" => "El inventario debe sincronizarse antes de mejorar",
        null or "" or "REMOTE_RESULT_UNKNOWN" or "TRANSACTION_ID_REUSED" or "TIMEOUT" or "NETWORK_ERROR" or
            "REQUEST_CANCELLED" or "INVALID_INVENTORY" or "INTERNAL_SERVER_ERROR" or "SERVICE_UNAVAILABLE" or
            "TRANSACTION_RETENTION_EXCEEDED" => UnconfirmedMessage,
        _ => "El servidor rechazó la mejora"
    };

    public void Dispose()
    {
        _disposed = true;
        _store.ProfileCommitted -= OnCommitted;
        Changed = null;
    }

    private bool IsUpgradable(LootId lootId) =>
        Resolve(lootId, out var definition) && NpcUpgradeRules.IsUpgradableBy(_merchant, definition);

    private bool MutationAllowed()
    {
        if (_canMutateBuild != null && !_canMutateBuild()) return Reject(BuildLockedMessage);
        return _isTradeInFlight == null || !_isTradeInFlight() || Reject(TradeInFlightMessage);
    }

    private bool Available()
    {
        if (!MutationAllowed()) return false;
        if (!_store.IsAvailable || _store.InventorySchemaVersion != 3 || _store.RequiresEquipmentHydration)
            return Reject(FailureMessage("INVENTORY_MIGRATION_REQUIRED"));
        return _store.PendingReservation == null || Reject(FailureMessage("RESERVATION_PENDING"));
    }

    private bool Reject(string message)
    {
        IsConfirming = false;
        Request = default;
        SelectedPreview = null;
        Message = message;
        Changed?.Invoke();
        return false;
    }

    private void OnCommitted(ProfileId profile)
    {
        if (profile == _store.ProfileId && !IsPending && IsConfirming) Reject(FailureMessage("REVISION_CONFLICT"));
    }

    private bool Resolve(LootId id, out LootDefinition definition)
    {
        definition = null;
        return id.IsValid && _catalog != null && _catalog.TryGet(id.Value, out definition);
    }

    private static Dictionary<string, int> Counts(IReadOnlyList<StashItem> items)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!item.IsValid) continue;
            counts.TryGetValue(item.LootId.Value, out int amount);
            counts[item.LootId.Value] = amount + item.Amount;
        }
        return counts;
    }
}
