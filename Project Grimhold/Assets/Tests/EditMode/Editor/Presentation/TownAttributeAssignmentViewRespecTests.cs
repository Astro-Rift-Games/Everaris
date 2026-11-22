#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Presentation
{
    /// <summary>TASK-304 T3 (S6): the optional respec widgets of the Town attribute panel.</summary>
    public sealed class TownAttributeAssignmentViewRespecTests
    {
        private const string ViewPath = "Assets/Prefabs/TownAttributeAssignmentView.prefab";

        private GameObject _root;
        private TownAttributeAssignmentView _view;
        private Button _respecButton;
        private Button _confirmButton;
        private Button _cancelButton;
        private TMP_Text _costText;
        private TMP_Text _messageText;
        private TMP_Text _confirmationText;
        private GameObject _confirmationPanel;
        private int _requested;
        private int _confirmed;
        private int _cancelled;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private static TownRespecPresentation Presentation(
            int? cost = 100,
            TownRespecBlockReason block = TownRespecBlockReason.None,
            string blockMessage = null,
            bool confirming = false,
            bool pending = false,
            string confirmation = null,
            string message = null) =>
            new(cost, block, blockMessage, confirming, pending, confirmation, message);

        private void BuildView()
        {
            _requested = _confirmed = _cancelled = 0;
            _root = new GameObject("RespecView");
            _view = _root.AddComponent<TownAttributeAssignmentView>();
            _respecButton = Child<Button>("RespecButton");
            _confirmButton = Child<Button>("ConfirmButton");
            _cancelButton = Child<Button>("CancelButton");
            _costText = Child<TextMeshProUGUI>("CostText");
            _messageText = Child<TextMeshProUGUI>("MessageText");
            _confirmationText = Child<TextMeshProUGUI>("ConfirmationText");
            _confirmationPanel = new GameObject("ConfirmationPanel");
            _confirmationPanel.transform.SetParent(_root.transform, false);
            AbilityTestFactory.SetPrivateField(_view, "_respecButton", _respecButton);
            AbilityTestFactory.SetPrivateField(_view, "_respecCostText", _costText);
            AbilityTestFactory.SetPrivateField(_view, "_respecMessageText", _messageText);
            AbilityTestFactory.SetPrivateField(_view, "_respecConfirmationPanel", _confirmationPanel);
            AbilityTestFactory.SetPrivateField(_view, "_respecConfirmationText", _confirmationText);
            AbilityTestFactory.SetPrivateField(_view, "_respecConfirmButton", _confirmButton);
            AbilityTestFactory.SetPrivateField(_view, "_respecCancelButton", _cancelButton);
            // Edit Mode does not run Awake for a component added by a test: register the listeners as the player would.
            typeof(TownAttributeAssignmentView)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_view, null);
            _view.RespecRequested += () => _requested++;
            _view.RespecConfirmed += () => _confirmed++;
            _view.RespecCancelled += () => _cancelled++;
        }

        private T Child<T>(string name) where T : Component
        {
            var child = new GameObject(name);
            child.transform.SetParent(_root.transform, false);
            return child.AddComponent<T>();
        }

        [Test]
        public void ExistingPrefab_WithoutRespecFields_StaysUsable()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath);
            Assert.That(prefab, Is.Not.Null);
            _root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var view = _root.GetComponent<TownAttributeAssignmentView>();

            Assert.DoesNotThrow(() => view.PresentRespec(Presentation(confirming: true, confirmation: "x", message: "y")));
            Assert.DoesNotThrow(() => view.PresentRespec(Presentation(cost: null)));
        }

        [Test]
        public void Present_ShowsTheCost_AndEnablesTheButtonOnlyWhenRespecIsAllowed()
        {
            BuildView();

            _view.PresentRespec(Presentation());
            Assert.That(_costText.text, Is.EqualTo("Costo: 100 de Oro"));
            Assert.That(_respecButton.interactable, Is.True);
            Assert.That(_confirmationPanel.activeSelf, Is.False);

            _view.PresentRespec(Presentation(block: TownRespecBlockReason.InsufficientGold,
                blockMessage: "No tenés Oro suficiente para reasignar (cuesta 100 de Oro)."));
            Assert.That(_respecButton.interactable, Is.False);
            Assert.That(_messageText.text, Is.EqualTo("No tenés Oro suficiente para reasignar (cuesta 100 de Oro)."));
        }

        [Test]
        public void Present_AResultMessageWinsOverTheBlockMessage()
        {
            BuildView();

            _view.PresentRespec(Presentation(block: TownRespecBlockReason.NoAssignedPoints,
                blockMessage: "No hay puntos asignados para reasignar.", message: "Atributos reasignados"));

            Assert.That(_messageText.text, Is.EqualTo("Atributos reasignados"));
        }

        [Test]
        public void Present_Confirming_ShowsThePanelWithTheTextAndBothButtons()
        {
            BuildView();

            _view.PresentRespec(Presentation(confirming: true, confirmation: "¿Reasignar?"));

            Assert.That(_confirmationPanel.activeSelf, Is.True);
            Assert.That(_confirmationText.text, Is.EqualTo("¿Reasignar?"));
            Assert.That(_confirmButton.interactable, Is.True);
            Assert.That(_cancelButton.interactable, Is.True);
            Assert.That(_respecButton.interactable, Is.False, "the request button waits for the answer");
        }

        [Test]
        public void Present_Pending_DisablesEveryRespecButton()
        {
            BuildView();

            _view.PresentRespec(Presentation(pending: true, message: "Reasignando atributos…"));

            Assert.That(_respecButton.interactable, Is.False);
            Assert.That(_confirmButton.interactable, Is.False);
            Assert.That(_cancelButton.interactable, Is.False);
            Assert.That(_messageText.text, Is.EqualTo("Reasignando atributos…"));
        }

        [Test]
        public void Present_UnknownCost_ShowsAPlaceholder()
        {
            BuildView();

            _view.PresentRespec(Presentation(cost: null, block: TownRespecBlockReason.CostUnavailable));

            Assert.That(_costText.text, Is.EqualTo("Costo: —"));
            Assert.That(_respecButton.interactable, Is.False);
        }

        [Test]
        public void Buttons_RaiseTheirEvents_OncePerClick()
        {
            BuildView();

            _respecButton.onClick.Invoke();
            _confirmButton.onClick.Invoke();
            _cancelButton.onClick.Invoke();

            Assert.That((_requested, _confirmed, _cancelled), Is.EqualTo((1, 1, 1)));
        }

        [Test]
        public void Destroy_StopsRaisingTheEvents()
        {
            BuildView();
            typeof(TownAttributeAssignmentView)
                .GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_view, null);

            _respecButton.onClick.Invoke();

            Assert.That(_requested, Is.Zero);
        }
    }
}
#endif
