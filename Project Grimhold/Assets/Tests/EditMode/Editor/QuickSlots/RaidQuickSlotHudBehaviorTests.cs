#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Editor.QuickSlots
{
    [Category("QuickSlots")]
    public sealed class RaidQuickSlotHudBehaviorTests
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";
        private static readonly ProfileId TestProfileId = new("21521521521521521521521521521521");
        private static readonly LootId Potion = new("healthpotion");

        private GameObject _playerInstance;
        private RaidQuickSlotHudPresenter _presenter;
        private RaidQuickSlotHudView _view;
        private LootDefinitionCatalog _catalog;
        private LocalProfileStore _store;
        private FakeInventorySource _inventory;
        private QuickSlotController _controller;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.That(prefab, Is.Not.Null, "NetworkPlayer prefab must exist.");

            _playerInstance = Object.Instantiate(prefab);
            _presenter = _playerInstance.GetComponentInChildren<RaidQuickSlotHudPresenter>(true);
            _view = _playerInstance.GetComponentInChildren<RaidQuickSlotHudView>(true);
            Assert.That(_presenter, Is.Not.Null, "Presenter must be present in prefab.");
            Assert.That(_view, Is.Not.Null, "View must be present in prefab.");

            _catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
                "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
            Assert.That(_catalog, Is.Not.Null, "Catalog must exist.");

            var repository = new InMemoryLocalProfileRepository();
            Assert.That(repository.Initialize(TestProfileId, _catalog), Is.True);
            _store = new LocalProfileStore(repository, TestProfileId, _catalog);
            _inventory = new FakeInventorySource();
            _controller = new QuickSlotController(_store, _inventory);

            var consumable = _playerInstance.GetComponentInChildren<PlayerConsumableNetworkController>(true);
            var inputReader = _playerInstance.GetComponentInChildren<PlayerInputReader>(true);
            if (inputReader == null)
            {
                inputReader = _playerInstance.AddComponent<PlayerInputReader>();
            }
            var character = _playerInstance.GetComponent<PlayerCharacter>();

            _presenter.Bind(_controller, consumable, inputReader, character);
        }

        [TearDown]
        public void TearDown()
        {
            _presenter?.Unbind();
            _controller?.Dispose();
            if (_playerInstance != null)
            {
                Object.DestroyImmediate(_playerInstance);
            }
        }

        [Test]
        public void Aggregation_MultipleStacksOfSameItem_PresentsAggregatedSumInHud()
        {
            // Two stacks of health potion with different amounts (e.g. 3 and 4) -> total 7
            _inventory.Set(
                new LootEntry(Potion, 3),
                new LootEntry(Potion, 4));

            var result = _controller.TryAssign(0, Potion);
            Assert.That(result, Is.EqualTo(QuickSlotAssignmentResult.Success));

            RaidQuickSlotHudSlotView slot0 = _view.GetSlot(0);
            TMP_Text amountText = GetField<TMP_Text>(slot0, "_amountText");
            GameObject overlay = GetField<GameObject>(slot0, "_unavailableOverlay");

            Assert.That(amountText.text, Is.EqualTo("7"), "Aggregated amount of multiple stacks must be 7.");
            Assert.That(overlay.activeSelf, Is.False, "Overlay must be disabled when units are available.");
        }

        [Test]
        public void DepletedSlot_PresentsDisabledWithZeroCount_AndRetainsBinding()
        {
            // Initially assign with 2 potions
            _inventory.Set(new LootEntry(Potion, 2));
            _controller.TryAssign(0, Potion);

            // Deplete to 0
            _inventory.Set();

            RaidQuickSlotHudSlotView slot0 = _view.GetSlot(0);
            TMP_Text amountText = GetField<TMP_Text>(slot0, "_amountText");
            GameObject overlay = GetField<GameObject>(slot0, "_unavailableOverlay");

            // Binding retained with 0 units
            Assert.That(_controller.Bindings.Get(0), Is.EqualTo(Potion), "Slot binding must be retained upon depletion (TASK-230).");
            Assert.That(_controller.GetAvailableAmount(Potion), Is.EqualTo(0));
            Assert.That(amountText.text, Is.EqualTo("0"), "Amount must display 0.");
            Assert.That(overlay.activeSelf, Is.True, "Overlay must be active (unavailable) when depleted.");
        }

        [Test]
        public void DownedPlayer_PresentsSlotAsUnavailable_EvenWithAvailableUnits()
        {
            _inventory.Set(new LootEntry(Potion, 5));
            _controller.TryAssign(0, Potion);

            RaidQuickSlotHudSlotView slot0 = _view.GetSlot(0);
            GameObject overlay = GetField<GameObject>(slot0, "_unavailableOverlay");
            Assert.That(overlay.activeSelf, Is.False);

            // Simulate downed state observed by presenter
            SetField(_presenter, "_observedDowned", true);
            InvokeMethod(_presenter, "Refresh");

            Assert.That(overlay.activeSelf, Is.True, "Overlay must be active when downed even if items are available.");
        }

        private static T GetField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on {target.GetType().Name}.");
            return (T)field.GetValue(target);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static void InvokeMethod(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Method '{methodName}' not found on {target.GetType().Name}.");
            method.Invoke(target, null);
        }

        private sealed class FakeInventorySource : IInventoryReadSource
        {
            private List<LootEntry> _content = new();

            public int SlotCapacity => 30;
            public int Revision { get; private set; }
            public IReadOnlyList<LootEntry> Content => _content;
            public event Action Changed;

            public void Set(params LootEntry[] entries)
            {
                _content = new List<LootEntry>(entries);
                Revision++;
                Changed?.Invoke();
            }

            public bool TryGetLootContent(out IReadOnlyList<LootEntry> content)
            {
                content = _content;
                return true;
            }
        }
    }
}
#endif
