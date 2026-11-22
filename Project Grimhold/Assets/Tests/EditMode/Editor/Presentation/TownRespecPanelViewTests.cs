#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Presentation
{
    /// <summary>
    /// TASK-304 T7 (S11, S13): the Mentor respec panel. Ports what the attribute-panel respec widgets covered (cost,
    /// availability, confirmation, pending, placeholders, button events) to the dedicated panel prefab.
    /// </summary>
    public sealed class TownRespecPanelViewTests
    {
        private const string PanelPath = "Assets/Prefabs/TownRespecPanel.prefab";
        private static readonly CharacterAttribute[] Attributes =
        {
            CharacterAttribute.Vitality, CharacterAttribute.Resistance, CharacterAttribute.Strength,
            CharacterAttribute.Dexterity, CharacterAttribute.Intelligence, CharacterAttribute.Luck
        };

        private GameObject _root;
        private TownRespecPanelView _view;
        private SerializedObject _serialized;
        private int _confirmed;
        private int _cancelled;
        private int _closed;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
            Assert.That(prefab, Is.Not.Null, "TownRespecPanel.prefab must exist");
            _root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _view = _root.GetComponent<TownRespecPanelView>();
            _serialized = new SerializedObject(_view);
            _confirmed = _cancelled = _closed = 0;
            // Edit Mode does not run Awake for a prefab instance: open the panel as the player would.
            _view.Open();
            _view.ConfirmRequested += () => _confirmed++;
            _view.CancelRequested += () => _cancelled++;
            _view.CloseRequested += () => _closed++;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private T Field<T>(string name) where T : Object
        {
            SerializedProperty property = _serialized.FindProperty(name);
            Assert.That(property, Is.Not.Null, name + " is not a serialized field");
            Assert.That(property.objectReferenceValue, Is.Not.Null, name + " is not wired");
            return (T)property.objectReferenceValue;
        }

        private TMP_Text AttributeValue(CharacterAttribute attribute)
        {
            SerializedProperty array = _serialized.FindProperty("_attributeValueTexts");
            Assert.That(array.arraySize, Is.EqualTo(6));
            return (TMP_Text)array.GetArrayElementAtIndex((int)attribute).objectReferenceValue;
        }

        private static TownRespecPresentation Respec(
            int? cost = 100,
            TownRespecBlockReason block = TownRespecBlockReason.None,
            string blockMessage = null,
            bool confirming = false,
            bool pending = false,
            string confirmation = null,
            string message = null) =>
            new(cost, block, blockMessage, confirming, pending, confirmation, message);

        private static TownRespecPanelPresentation Panel(in TownRespecPresentation respec, int vitality = 5) =>
            TownRespecPanelPresentation.Create(AbilityTestFactory.CreateAttributes(vitality, 4, 3, 2, 1, 0), respec);

        [Test]
        public void Prefab_WiresEveryWidgetTheViewDrives()
        {
            foreach (string field in new[]
                     {
                         "_titleText", "_pointsToReturnText", "_costText", "_messageText", "_confirmationText"
                     })
            {
                Assert.That(Field<TMP_Text>(field), Is.Not.Null, field);
            }

            foreach (string field in new[] { "_confirmButton", "_cancelButton", "_closeButton" })
            {
                Assert.That(Field<Button>(field), Is.Not.Null, field);
            }

            foreach (CharacterAttribute attribute in Attributes)
            {
                Assert.That(AttributeValue(attribute), Is.Not.Null, attribute.ToString());
            }

            Assert.That(Field<Button>("_confirmButton"), Is.Not.SameAs(Field<Button>("_cancelButton")));
        }

        [Test]
        public void Prefab_IsAnOverlayCanvasThatStartsClosedInItsAuthoredForm()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);

            Assert.That(prefab.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(prefab.GetComponent<TownRespecPanelView>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<GraphicRaycaster>(), Is.Not.Null);
        }

        [Test]
        public void Present_ShowsTheMentorTitleTheSixAttributesPointsAndCost()
        {
            _view.Present(Panel(Respec(confirming: true, confirmation: "¿Reasignar?")));

            Assert.That(Field<TMP_Text>("_titleText").text, Is.EqualTo("Mentor"));
            Assert.That(AttributeValue(CharacterAttribute.Vitality).text, Is.EqualTo("5"));
            Assert.That(AttributeValue(CharacterAttribute.Resistance).text, Is.EqualTo("4"));
            Assert.That(AttributeValue(CharacterAttribute.Strength).text, Is.EqualTo("3"));
            Assert.That(AttributeValue(CharacterAttribute.Dexterity).text, Is.EqualTo("2"));
            Assert.That(AttributeValue(CharacterAttribute.Intelligence).text, Is.EqualTo("1"));
            Assert.That(AttributeValue(CharacterAttribute.Luck).text, Is.EqualTo("0"));
            Assert.That(Field<TMP_Text>("_pointsToReturnText").text, Is.EqualTo("Puntos que vuelven al pool: 15"));
            Assert.That(Field<TMP_Text>("_costText").text, Is.EqualTo("Costo: 100 de Oro"));
        }

        [Test]
        public void Present_Confirming_ShowsTheConfirmationTextAndEnablesBothButtons()
        {
            _view.Present(Panel(Respec(confirming: true, confirmation: "¿Reasignar?")));

            Assert.That(Field<TMP_Text>("_confirmationText").text, Is.EqualTo("¿Reasignar?"));
            Assert.That(Field<Button>("_confirmButton").interactable, Is.True);
            Assert.That(Field<Button>("_cancelButton").interactable, Is.True);
        }

        [Test]
        public void Present_Blocked_ShowsTheReasonAndOnlyAllowsCancelling()
        {
            _view.Present(Panel(Respec(
                block: TownRespecBlockReason.InsufficientGold,
                blockMessage: "No tenés Oro suficiente para reasignar (cuesta 100 de Oro).")));

            Assert.That(Field<TMP_Text>("_messageText").text,
                Is.EqualTo("No tenés Oro suficiente para reasignar (cuesta 100 de Oro)."));
            Assert.That(Field<TMP_Text>("_confirmationText").text, Is.Empty);
            Assert.That(Field<Button>("_confirmButton").interactable, Is.False);
            Assert.That(Field<Button>("_cancelButton").interactable, Is.True);
        }

        [Test]
        public void Present_AResultMessageWinsOverTheBlockMessage()
        {
            _view.Present(Panel(Respec(
                block: TownRespecBlockReason.NoAssignedPoints,
                blockMessage: "No hay puntos asignados para reasignar.",
                message: "Atributos reasignados")));

            Assert.That(Field<TMP_Text>("_messageText").text, Is.EqualTo("Atributos reasignados"));
        }

        [Test]
        public void Present_Pending_DisablesEveryButtonButTheCloseOne()
        {
            _view.Present(Panel(Respec(pending: true, message: "Reasignando atributos…")));

            Assert.That(Field<Button>("_confirmButton").interactable, Is.False);
            Assert.That(Field<Button>("_cancelButton").interactable, Is.False);
            Assert.That(Field<TMP_Text>("_messageText").text, Is.EqualTo("Reasignando atributos…"));
        }

        [Test]
        public void Present_UnknownCost_ShowsAPlaceholderAndCannotConfirm()
        {
            _view.Present(Panel(Respec(cost: null, block: TownRespecBlockReason.CostUnavailable)));

            Assert.That(Field<TMP_Text>("_costText").text, Is.EqualTo("Costo: —"));
            Assert.That(Field<Button>("_confirmButton").interactable, Is.False);
        }

        [Test]
        public void Present_UnavailableAttributes_ShowsPlaceholders()
        {
            _view.Present(TownRespecPanelPresentation.Unavailable(Respec(cost: null)));

            foreach (CharacterAttribute attribute in Attributes)
            {
                Assert.That(AttributeValue(attribute).text, Is.EqualTo("—"), attribute.ToString());
            }

            Assert.That(Field<TMP_Text>("_pointsToReturnText").text, Is.EqualTo("Puntos que vuelven al pool: —"));
        }

        [Test]
        public void Buttons_RaiseTheirEvents_OncePerClick()
        {
            Field<Button>("_confirmButton").onClick.Invoke();
            Field<Button>("_cancelButton").onClick.Invoke();
            Field<Button>("_closeButton").onClick.Invoke();

            Assert.That((_confirmed, _cancelled, _closed), Is.EqualTo((1, 1, 1)));
        }

        [Test]
        public void OpenAndClose_ToggleTheLogicalVisibility()
        {
            Assert.That(_view.IsOpen, Is.True);

            _view.Close();
            Assert.That(_view.IsOpen, Is.False);

            _view.Open();
            Assert.That(_view.IsOpen, Is.True);
        }

        [Test]
        public void Destroy_StopsRaisingTheEvents()
        {
            Button confirm = Field<Button>("_confirmButton");
            typeof(TownRespecPanelView)
                .GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_view, null);

            confirm.onClick.Invoke();

            Assert.That(_confirmed, Is.Zero);
        }
    }
}
#endif
