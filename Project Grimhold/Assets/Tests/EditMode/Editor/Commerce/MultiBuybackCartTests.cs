using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Commerce
{
    public sealed class MultiBuybackCartTests
    {
        private static readonly ProfileId Profile = new ProfileId("multi-buyback-player");
        private static readonly LootId Sword = new LootId("arming_sword");
        private static readonly LootId Helmet = new LootId("placeholder_helmet");
        private static readonly LootId Potion = new LootId("CON_HEALTH_POTION");

        private readonly List<UnityEngine.Object> _created = new();
        private LootDefinitionCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            var swordDef = CreateLoot(Sword.Value, buy: 60, sell: 30);
            var helmetDef = CreateLoot(Helmet.Value, buy: 40, sell: 20);
            var potionDef = CreateLoot(Potion.Value, buy: 40, sell: 20);

            _catalog = Track(ScriptableObject.CreateInstance<LootDefinitionCatalog>());
            typeof(LootDefinitionCatalog)
                .GetField("_definitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_catalog, new List<LootDefinition> { swordDef, helmetDef, potionDef });
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) UnityEngine.Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        [Test]
        public void Draft_AllowsMultipleDistinctBuybackEntries_AndRemovesIndividually()
        {
            var draft = new MerchantTradeDraft();
            var entryA = new MerchantBuybackEntry("bb-1", Sword, 1, 45);
            var entryB = new MerchantBuybackEntry("bb-2", Helmet, 1, 30);

            Assert.That(draft.TrySetBuyback(entryA, 1), Is.True);
            Assert.That(draft.TrySetBuyback(entryB, 1), Is.True,
                "Draft must allow adding multiple distinct buyback entries to the same cart.");

            Assert.That(draft.TryRemoveBuyback("bb-1"), Is.True);
            Assert.That(draft.IsEmpty, Is.False,
                "Removing one buyback entry must preserve the remaining buyback entry in the draft.");
            Assert.That(draft.TryRemoveBuyback("bb-2"), Is.True);
            Assert.That(draft.IsEmpty, Is.True);
        }

        [Test]
        public void Preview_CalculatesCombinedCostAndCapacity_ForMultipleBuybacks()
        {
            var draft = new MerchantTradeDraft();
            var entryA = new MerchantBuybackEntry("bb-1", Sword, 1, 45);
            var entryB = new MerchantBuybackEntry("bb-2", Helmet, 1, 30);

            var ledger = new BuybackLedger();
            ledger.Replace(new BuybackData
            {
                entries = new[]
                {
                    new BuybackEntryData { entryId = "bb-1", lootId = Sword.Value, amount = 1, unitPrice = 45 },
                    new BuybackEntryData { entryId = "bb-2", lootId = Helmet.Value, amount = 1, unitPrice = 30 }
                }
            });

            Assert.That(draft.TrySetBuyback(entryA, 1), Is.True);
            Assert.That(draft.TrySetBuyback(entryB, 1), Is.True);

            var preview = MerchantTradePreview.Calculate(
                Array.Empty<LootEntry>(),
                slotCapacity: 10,
                confirmedCurrency: 100,
                draft: draft,
                availableStock: _ => 0,
                catalog: _catalog,
                buybackLedger: ledger);

            Assert.That(preview.CanConfirm, Is.True);
            Assert.That(preview.PurchaseTotal, Is.EqualTo(75));
            Assert.That(preview.ProjectedCurrency, Is.EqualTo(25));
            Assert.That(preview.ProjectedOccupiedSlots, Is.EqualTo(2));

            // Insufficient funds for combined buybacks must block confirmation
            var poorPreview = MerchantTradePreview.Calculate(
                Array.Empty<LootEntry>(),
                slotCapacity: 10,
                confirmedCurrency: 60,
                draft: draft,
                availableStock: _ => 0,
                catalog: _catalog,
                buybackLedger: ledger);

            Assert.That(poorPreview.CanConfirm, Is.False);
            Assert.That(poorPreview.HasBlock(MerchantTradeBlockReason.InsufficientCurrency), Is.True);
        }

        [Test]
        public void TradeSession_AllowsSelectingMultipleBuybacks_AndSubmitsSingleAtomicTicket()
        {
            var ledger = new BuybackLedger();
            ledger.Replace(new BuybackData
            {
                entries = new[]
                {
                    new BuybackEntryData { entryId = "bb-1", lootId = Sword.Value, amount = 1, unitPrice = 45 },
                    new BuybackEntryData { entryId = "bb-2", lootId = Helmet.Value, amount = 1, unitPrice = 30 }
                }
            });

            var shop = new RecordingShopService();
            var endpoint = new StubEndpoint(_catalog);
            var inventory = new StubInventory();
            MerchantShopViewModel lastVm = default;

            using var session = new TownMerchantTradeSession(
                Profile,
                endpoint,
                shop,
                inventory,
                () => 100,
                vm => lastVm = vm,
                () => 0d,
                5d,
                buybackLedger: ledger,
                buybackService: shop);

            Assert.That(session.SetBuyback("bb-1", 1), Is.True);
            // Second buyback row must remain editable in the view model!
            var row2 = lastVm.MerchantRows.First(r => r.BuybackEntryId == "bb-2");
            Assert.That(row2.CanAdd, Is.True, "Selecting one buyback entry must not lock out other buyback entries.");

            Assert.That(session.SetBuyback("bb-2", 1), Is.True);
            Assert.That(lastVm.CanConfirm, Is.True);
            Assert.That(lastVm.PurchaseTotal, Is.EqualTo(75));

            Assert.That(session.ConfirmTrade(), Is.True);
            Assert.That(shop.ExecutedTickets, Has.Count.EqualTo(1),
                "Multiple buybacks must be sent in a single atomic trade ticket.");
            Assert.That(shop.ExecutedTickets[0].Buybacks, Has.Count.EqualTo(2));
            Assert.That(shop.ExecutedTickets[0].Buybacks.Select(b => b.EntryId).ToArray(),
                Is.EqualTo(new[] { "bb-1", "bb-2" }));
            Assert.That(shop.SingleBuybackCalls, Is.EqualTo(0));
            Assert.That(endpoint.ReportCalls, Is.EqualTo(0),
                "Buyback-only trade must not report stock outcome to Fusion merchant endpoint.");
        }

        private LootDefinition CreateLoot(string id, int buy, int sell)
        {
            var def = Track(ScriptableObject.CreateInstance<LootDefinition>());
            typeof(LootDefinition).GetField("_id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(def, id);
            typeof(LootDefinition).GetField("_displayName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(def, id);
            typeof(LootDefinition).GetField("_buyValuePerUnit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(def, buy);
            typeof(LootDefinition).GetField("_sellValuePerUnit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(def, sell);
            typeof(LootDefinition).GetField("_objectValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(def, sell);
            return def;
        }

        private T Track<T>(T obj) where T : UnityEngine.Object
        {
            _created.Add(obj);
            return obj;
        }

        private sealed class RecordingShopService : IShopTransactionService, IBuybackService
        {
            public List<MerchantTradeTicket> ExecutedTickets { get; } = new();
            public int SingleBuybackCalls { get; private set; }

            public Task<StashOperationResult> ExecuteTradeAsync(ProfileId profileId, MerchantTradeTicket ticket)
            {
                ExecutedTickets.Add(ticket);
                return Task.FromResult(StashOperationResult.Success);
            }

            public bool IsTradeApplied(ProfileId profileId, ShopTransactionId transactionId) => false;

            public Task<StashOperationResult> ExecuteBuybackAsync(ProfileId profileId, MerchantBuybackRequest request)
            {
                SingleBuybackCalls++;
                return Task.FromResult(StashOperationResult.Success);
            }
        }

        private sealed class StubInventory : IInventoryReadSource
        {
            public int SlotCapacity => 30;
            public int Revision => 0;
            public event Action Changed;
            public bool TryGetLootContent(out IReadOnlyList<LootEntry> content)
            {
                content = Array.Empty<LootEntry>();
                return true;
            }
            public void RaiseChanged() => Changed?.Invoke();
        }

        private sealed class StubEndpoint : IMerchantTradeEndpoint
        {
            public StubEndpoint(LootDefinitionCatalog catalog) => Catalog = catalog;
            public LootDefinitionCatalog Catalog { get; }
            public IReadOnlyList<MerchantStockItem> Stock => Array.Empty<MerchantStockItem>();
            public TownMerchantType MerchantType => TownMerchantType.General;
            public int ReportCalls { get; private set; }
            public event Action StockChanged;
            public event Action AuthorityChanged;
            public int GetAvailableStock(LootId lootId) => 0;
            public bool TrySubmitTrade(ProfileId profileId, Guid requestId, IReadOnlyList<MerchantTradeLine> purchases, IReadOnlyList<MerchantTradeLine> sales) => true;
            public void ReportTradeOutcome(Guid requestId, ShopTransactionId transactionId, bool persisted) => ReportCalls++;
            public void RequestPendingTrades(ProfileId profileId) { }
            public void SetTradeClient(IMerchantTradeClient client) { }
            public void ClearTradeClient(IMerchantTradeClient client) { }
            public void RaiseEvents() { StockChanged?.Invoke(); AuthorityChanged?.Invoke(); }
        }
    }
}
