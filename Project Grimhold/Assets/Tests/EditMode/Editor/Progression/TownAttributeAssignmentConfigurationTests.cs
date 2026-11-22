#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using Fusion;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Assert = NUnit.Framework.Assert;

namespace Tests.EditMode.Progression
{
    public sealed class TownAttributeAssignmentConfigurationTests
    {
        private const string InputActionsPath = "Assets/Input/PlayerInputActions.inputactions";
        private const string SocialPlayerPath = "Assets/Prefabs/SocialPlayer.prefab";
        private const string RaidParticipantPath = "Assets/Prefabs/NetworkRaidParticipant.prefab";
        private const string ViewPath = "Assets/Prefabs/TownAttributeAssignmentView.prefab";

        [Test]
        public void LocalUI_HasToggleAttributesBoundToC()
        {
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            Assert.That(asset, Is.Not.Null);
            InputAction action = asset.FindAction("LocalUI/ToggleAttributes", true);

            Assert.That(action.bindings, Has.Count.EqualTo(1));
            Assert.That(action.bindings[0].path, Is.EqualTo("<Keyboard>/c"));
        }

        [Test]
        public void ViewPrefab_HasSixCompleteUniqueAuthoredRows()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath);
            Assert.That(prefab, Is.Not.Null);
            TownAttributeAssignmentView view = prefab.GetComponent<TownAttributeAssignmentView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(prefab.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(view.AvailablePointsText, Is.Not.Null);
            Assert.That(view.CloseButton, Is.Not.Null);
            Assert.That(view.Rows.Count, Is.EqualTo(6));

            var attributes = new HashSet<CharacterAttribute>();
            foreach (TownAttributeAssignmentRowView row in view.Rows)
            {
                Assert.That(row, Is.Not.Null);
                Assert.That(row.LabelText, Is.Not.Null);
                Assert.That(row.ValueText, Is.Not.Null);
                Assert.That(row.AddButton, Is.Not.Null);
                Assert.That(attributes.Add(row.Attribute), Is.True);
            }

            Assert.That(attributes, Is.EquivalentTo(System.Enum.GetValues(typeof(CharacterAttribute))));
        }

        [Test]
        public void ViewPrefab_WiresEverySerializedRespecWidget()
        {
            var serialized = LoadSerializedView();

            Assert.That(RespecReference<Button>(serialized, "_respecButton"), Is.Not.Null);
            Assert.That(RespecReference<TMP_Text>(serialized, "_respecCostText"), Is.Not.Null);
            Assert.That(RespecReference<TMP_Text>(serialized, "_respecMessageText"), Is.Not.Null);
            Assert.That(RespecReference<GameObject>(serialized, "_respecConfirmationPanel"), Is.Not.Null);
            Assert.That(RespecReference<TMP_Text>(serialized, "_respecConfirmationText"), Is.Not.Null);
            Assert.That(RespecReference<Button>(serialized, "_respecConfirmButton"), Is.Not.Null);
            Assert.That(RespecReference<Button>(serialized, "_respecCancelButton"), Is.Not.Null);
        }

        [Test]
        public void ViewPrefab_RespecConfirmationPanelStartsInactiveAndOwnsConfirmationControls()
        {
            var serialized = LoadSerializedView();
            GameObject panel = RespecReference<GameObject>(serialized, "_respecConfirmationPanel");
            Button confirm = RespecReference<Button>(serialized, "_respecConfirmButton");
            Button cancel = RespecReference<Button>(serialized, "_respecCancelButton");
            TMP_Text confirmationText = RespecReference<TMP_Text>(serialized, "_respecConfirmationText");

            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.activeSelf, Is.False);
            Assert.That(confirm.transform.IsChildOf(panel.transform), Is.True);
            Assert.That(cancel.transform.IsChildOf(panel.transform), Is.True);
            Assert.That(confirmationText.transform.IsChildOf(panel.transform), Is.True);
            Assert.That(confirm, Is.Not.SameAs(cancel));
        }

        [Test]
        public void ViewPrefab_RespecEntryPointStaysOutsideTheAttributeRowsAndTheConfirmationPanel()
        {
            var serialized = LoadSerializedView();
            GameObject panel = RespecReference<GameObject>(serialized, "_respecConfirmationPanel");
            Button respecButton = RespecReference<Button>(serialized, "_respecButton");
            TMP_Text costText = RespecReference<TMP_Text>(serialized, "_respecCostText");
            TMP_Text messageText = RespecReference<TMP_Text>(serialized, "_respecMessageText");
            TownAttributeAssignmentView view = (TownAttributeAssignmentView)serialized.targetObject;

            Assert.That(panel, Is.Not.Null);
            foreach (Component entryPoint in new Component[] { respecButton, costText, messageText })
            {
                Assert.That(entryPoint, Is.Not.Null);
                Assert.That(entryPoint.transform.IsChildOf(panel.transform), Is.False);
                Assert.That(entryPoint.transform.IsChildOf(view.transform), Is.True);
                foreach (TownAttributeAssignmentRowView row in view.Rows)
                {
                    Assert.That(entryPoint.transform.IsChildOf(row.transform), Is.False);
                    Assert.That(entryPoint.transform.IsChildOf(view.Rows[0].transform.parent), Is.False);
                }
            }
        }

        [Test]
        public void ViewPrefab_ExistingAttributeWidgetsStayWired()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath);
            TownAttributeAssignmentView view = prefab.GetComponent<TownAttributeAssignmentView>();

            Assert.That(view.AvailablePointsText.text, Is.EqualTo("Available Points: 0"));
            Assert.That(view.CloseButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("X"));
            Assert.That(view.Rows.Count, Is.EqualTo(6));
            foreach (TownAttributeAssignmentRowView row in view.Rows)
            {
                Assert.That(row.AddButton.onClick.GetPersistentEventCount(), Is.Zero);
            }
        }

        private static SerializedObject LoadSerializedView()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath);
            Assert.That(prefab, Is.Not.Null);
            TownAttributeAssignmentView view = prefab.GetComponent<TownAttributeAssignmentView>();
            Assert.That(view, Is.Not.Null);
            return new SerializedObject(view);
        }

        private static T RespecReference<T>(SerializedObject serialized, string field) where T : Object
        {
            SerializedProperty property = serialized.FindProperty(field);
            Assert.That(property, Is.Not.Null, $"{field} is not a serialized field");
            Object value = property.objectReferenceValue;
            if (value is Component component && typeof(T) == typeof(GameObject))
            {
                return component.gameObject as T;
            }

            return value as T;
        }

        [Test]
        public void Presenter_ExistsOnlyOnceOnSocialPlayerAndNotOnRaidParticipant()
        {
            GameObject social = AssetDatabase.LoadAssetAtPath<GameObject>(SocialPlayerPath);
            GameObject raid = AssetDatabase.LoadAssetAtPath<GameObject>(RaidParticipantPath);
            Assert.That(social, Is.Not.Null);
            Assert.That(raid, Is.Not.Null);

            TownAttributeAssignmentPresenter[] socialPresenters =
                social.GetComponentsInChildren<TownAttributeAssignmentPresenter>(true);
            Assert.That(socialPresenters, Has.Length.EqualTo(1));
            NetworkObject networkObject = social.GetComponent<NetworkObject>();
            Assert.That(networkObject, Is.Not.Null);
            Assert.That(networkObject.NetworkedBehaviours, Does.Contain(socialPresenters[0]));
            var serializedPresenter = new SerializedObject(socialPresenters[0]);
            Assert.That(
                serializedPresenter.FindProperty("_viewPrefab").objectReferenceValue,
                Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath).GetComponent<TownAttributeAssignmentView>()));

            Assert.That(
                raid.GetComponentsInChildren<TownAttributeAssignmentPresenter>(true),
                Is.Empty);
        }
    }
}
#endif
