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
        public void ViewPrefab_HasNoRespecSectionOrConfirmation_TheMentorOwnsTheRespec()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath);
            Assert.That(prefab, Is.Not.Null);

            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(child.name, Does.Not.Contain("Respec"), "the attribute panel returns to assignment only");
            }
        }

        [Test]
        public void ViewPrefab_RowsContainerFillsTheColumnBelowTheTitleAgain()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath);
            TownAttributeAssignmentView view = prefab.GetComponent<TownAttributeAssignmentView>();
            var rows = (RectTransform)view.Rows[0].transform.parent;

            Assert.That(rows.name, Is.EqualTo("Rows"));
            Assert.That(rows.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(rows.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(rows.anchoredPosition, Is.EqualTo(new Vector2(0f, -45f)));
            Assert.That(rows.sizeDelta, Is.EqualTo(new Vector2(-60f, -150f)));
            Assert.That(rows.pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)));
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
