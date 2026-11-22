#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Presentation
{
    /// <summary>
    /// TASK-304 T7 (S12): the respec left the attribute panel for the Mentor NPC. The view declares nothing about it and
    /// keeps assigning attributes; the panel-level respec coverage now lives in <see cref="TownRespecPanelViewTests"/>.
    /// </summary>
    public sealed class TownAttributeAssignmentViewRespecTests
    {
        private const string ViewPath = "Assets/Prefabs/TownAttributeAssignmentView.prefab";

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void View_NoLongerDeclaresRespecFieldsEventsOrPresentation()
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            System.Type view = typeof(TownAttributeAssignmentView);

            Assert.That(view.GetFields(all).Select(field => field.Name), Has.None.Contain("espec"));
            Assert.That(view.GetEvents(all).Select(@event => @event.Name), Has.None.Contain("Respec"));
            Assert.That(view.GetMethods(all).Select(method => method.Name), Has.None.Contain("Respec"));
        }

        [Test]
        public void View_StillPresentsTheSixRowsAvailablePointsAndRaisesAssignmentAndClose()
        {
            _root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath));
            var view = _root.GetComponent<TownAttributeAssignmentView>();
            Assert.That(CharacterAttributeState.TryCreate(1, 2, 3, 4, 5, 6, 7, out CharacterAttributeState state), Is.True);
            Assert.That(TownAttributeAssignmentPresentation.TryCreate(state, 99, out var presentation), Is.True);
            // Edit Mode does not run Awake for a prefab instance: register the listeners as the player would.
            typeof(TownAttributeAssignmentView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
            foreach (TownAttributeAssignmentRowView row in view.Rows)
            {
                typeof(TownAttributeAssignmentRowView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(row, null);
            }

            CharacterAttribute? assigned = null;
            int closed = 0;
            view.AssignmentRequested += attribute => assigned = attribute;
            view.CloseRequested += () => closed++;

            view.Present(presentation);

            Assert.That(view.AvailablePointsText.text, Is.EqualTo("Available Points: 7"));
            Assert.That(view.Rows.Select(row => row.ValueText.text), Is.EqualTo(new[] { "1", "2", "3", "4", "5", "6" }));
            view.Rows[0].AddButton.onClick.Invoke();
            Assert.That(assigned, Is.EqualTo(CharacterAttribute.Vitality));
            view.CloseButton.onClick.Invoke();
            Assert.That(closed, Is.EqualTo(1));
        }
    }
}
#endif
