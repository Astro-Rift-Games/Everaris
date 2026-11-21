using System;

/// <summary>
/// Confirmed-profile dependencies of the local Town merchant session, supplied by the Town
/// profile composition: confirmed Inventory and Currency reads and the persistence boundary.
/// </summary>
public sealed class TownMerchantProfileSource
{
    public BuybackLedger BuybackLedger { get; }
    public IBuybackService BuybackService { get; }
    public ProfileId ProfileId { get; }
    public IInventoryReadSource Inventory { get; }
    public IPlayerCurrencyService Currency { get; }
    public IShopTransactionService ShopTransactionService { get; }

    /// <summary>
    /// The Town build gate (<see cref="TownEquipmentMutationGate"/>): false while the player is Ready,
    /// which blocks every purchase, sale and buyback. Null means no preparation can lock the build.
    /// </summary>
    public Func<bool> CanMutateBuild { get; }

    public TownMerchantProfileSource(
        ProfileId profileId,
        IInventoryReadSource inventory,
        IPlayerCurrencyService currency,
        IShopTransactionService shopTransactionService,
        BuybackLedger buybackLedger = null,
        IBuybackService buybackService = null,
        Func<bool> canMutateBuild = null)
    {
        if (!profileId.IsValid)
        {
            throw new ArgumentException("The merchant profile must be valid.", nameof(profileId));
        }

        BuybackLedger = buybackLedger;
        BuybackService = buybackService;
        CanMutateBuild = canMutateBuild;
        ProfileId = profileId;
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
        ShopTransactionService = shopTransactionService ?? throw new ArgumentNullException(nameof(shopTransactionService));
    }
}
