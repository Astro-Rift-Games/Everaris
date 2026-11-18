using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// One priced buyback line of an authority-issued trade ticket.
/// </summary>
public readonly struct MerchantPricedBuybackLine
{
    public string EntryId { get; }
    public LootId LootId { get; }
    public int Amount { get; }
    public long UnitPrice { get; }
    public long Total => UnitPrice * Amount;

    public MerchantPricedBuybackLine(string entryId, LootId lootId, int amount, long unitPrice)
    {
        EntryId = entryId;
        LootId = lootId;
        Amount = amount;
        UnitPrice = unitPrice;
    }
}

/// <summary>
/// Immutable authority-issued trade: one transaction id and every priced purchase and sale line,
/// persisted as a single atomic profile transaction. Prices are already resolved by State
/// Authority; persistence never looks them up again.
/// </summary>
public sealed class MerchantTradeTicket
{
    public ShopTransactionId TransactionId { get; }
    public IReadOnlyList<MerchantPricedTradeLine> Purchases { get; }
    public IReadOnlyList<MerchantPricedTradeLine> Sales { get; }
    public IReadOnlyList<MerchantPricedBuybackLine> Buybacks { get; }

    public bool IsEmpty => Purchases.Count == 0 && Sales.Count == 0 && Buybacks.Count == 0;
    public int LineCount => Purchases.Count + Sales.Count + Buybacks.Count;

    public MerchantTradeTicket(
        ShopTransactionId transactionId,
        IEnumerable<MerchantPricedTradeLine> purchases,
        IEnumerable<MerchantPricedTradeLine> sales,
        IEnumerable<MerchantPricedBuybackLine> buybacks = null)
    {
        if (purchases == null) throw new ArgumentNullException(nameof(purchases));
        if (sales == null) throw new ArgumentNullException(nameof(sales));

        TransactionId = transactionId;
        Purchases = Array.AsReadOnly(new List<MerchantPricedTradeLine>(purchases).ToArray());
        Sales = Array.AsReadOnly(new List<MerchantPricedTradeLine>(sales).ToArray());
        Buybacks = buybacks != null
            ? Array.AsReadOnly(new List<MerchantPricedBuybackLine>(buybacks).ToArray())
            : Array.Empty<MerchantPricedBuybackLine>();
    }

    /// <summary>
    /// Whether the ticket is structurally valid: a valid transaction id, at least one line, valid
    /// loot and positive amounts, a positive buy price and a non-negative sell price, and each
    /// loot at most once per side. The same loot may appear once as purchase and once as sale.
    /// </summary>
    public bool IsWellFormed =>
        TransactionId.IsValid &&
        !IsEmpty &&
        AreValid(Purchases, minimumUnitPrice: 1) &&
        AreValid(Sales, minimumUnitPrice: 0) &&
        AreValidBuybacks(Buybacks);

    private static bool AreValid(IReadOnlyList<MerchantPricedTradeLine> lines, int minimumUnitPrice)
    {
        var seen = new HashSet<LootId>();
        foreach (MerchantPricedTradeLine line in lines)
        {
            if (!line.LootId.IsValid || line.Amount <= 0 || line.UnitPrice < minimumUnitPrice || !seen.Add(line.LootId))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreValidBuybacks(IReadOnlyList<MerchantPricedBuybackLine> lines)
    {
        var seen = new HashSet<string>();
        foreach (MerchantPricedBuybackLine line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.EntryId) || !line.LootId.IsValid || line.Amount <= 0 || line.UnitPrice < 0 || !seen.Add(line.EntryId))
            {
                return false;
            }
        }

        return true;
    }
}
