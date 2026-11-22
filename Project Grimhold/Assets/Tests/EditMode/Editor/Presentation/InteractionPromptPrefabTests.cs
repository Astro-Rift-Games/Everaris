#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.EditMode.Presentation
{
    /// <summary>
    /// The interaction prompt is one shared prefab with one shared style asset: a designer edits its look and its
    /// spacing in a single place and both the Town and the Raid HUD follow.
    /// </summary>
    public sealed class InteractionPromptPrefabTests
    {
        private const string PromptPrefabPath = "Assets/Prefabs/UI/PlayerUI/InteractionPrompt.prefab";
        private const string StylePath = "Assets/Scriptable Objects/InteractionPromptStyle.asset";

        private static readonly string[] HudPrefabPaths =
        {
            "Assets/Prefabs/UI/PlayerUI/LocalGameplayHud.prefab",
            "Assets/Prefabs/SocialPlayer.prefab"
        };

        [Test]
        public void SharedPrompt_IsAFramedLabelThatSizesToItsText()
        {
            GameObject prompt = AssetDatabase.LoadAssetAtPath<GameObject>(PromptPrefabPath);
            Assert.That(prompt, Is.Not.Null, PromptPrefabPath);

            Image frame = prompt.GetComponent<Image>();
            Assert.That(frame, Is.Not.Null, "framed background on the prompt root");
            Assert.That(frame.sprite, Is.Not.Null);
            Assert.That(frame.type, Is.EqualTo(Image.Type.Sliced), "the frame stretches through its 9-slice only");

            TMP_Text label = prompt.GetComponentInChildren<TMP_Text>(true);
            Assert.That(label, Is.Not.Null, "label");
            Assert.That(label.transform.parent, Is.SameAs(prompt.transform));
            Assert.That(label.enableAutoSizing, Is.False, "the frame adapts to the text, never the other way round");
            Assert.That(label.textWrappingMode, Is.EqualTo(TextWrappingModes.NoWrap));

            ContentSizeFitter fitter = prompt.GetComponent<ContentSizeFitter>();
            Assert.That(fitter, Is.Not.Null);
            Assert.That(fitter.horizontalFit, Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
            Assert.That(fitter.verticalFit, Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
            Assert.That(prompt.GetComponent<HorizontalOrVerticalLayoutGroup>(), Is.Not.Null, "padding around the label");
        }

        [TestCaseSource(nameof(HudPrefabPaths))]
        public void Hud_UsesTheSharedPromptPrefabAndStartsWithItHidden(string hudPath)
        {
            SerializedObject presenter = PresenterOf(hudPath);
            var promptRoot = presenter.FindProperty("_promptRoot").objectReferenceValue as GameObject;
            var promptText = presenter.FindProperty("_promptText").objectReferenceValue as TMP_Text;
            Assert.That(promptRoot, Is.Not.Null, "_promptRoot");
            Assert.That(promptText, Is.Not.Null, "_promptText");

            GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(promptRoot);

            Assert.That(source, Is.Not.Null, "the HUD prompt must be an instance of the shared prefab");
            Assert.That(AssetDatabase.GetAssetPath(source), Is.EqualTo(PromptPrefabPath));
            Assert.That(source.transform.parent, Is.Null, "the instance root, not a child of the prompt");
            Assert.That(promptText.transform.IsChildOf(promptRoot.transform), Is.True);
            Assert.That(promptRoot.activeSelf, Is.False, "prompt starts hidden");
        }

        [Test]
        public void EveryHud_SharesOnePromptStyleAsset()
        {
            InteractionPromptStyle style = AssetDatabase.LoadAssetAtPath<InteractionPromptStyle>(StylePath);
            Assert.That(style, Is.Not.Null, StylePath);

            foreach (string hudPath in HudPrefabPaths)
            {
                Assert.That(
                    PresenterOf(hudPath).FindProperty("_promptStyle").objectReferenceValue,
                    Is.SameAs(style),
                    hudPath);
            }
        }

        private static SerializedObject PresenterOf(string hudPath)
        {
            GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>(hudPath);
            Assert.That(hud, Is.Not.Null, hudPath);
            InteractionHudPresenter presenter = hud.GetComponentInChildren<InteractionHudPresenter>(true);
            Assert.That(presenter, Is.Not.Null, $"{hudPath} presenter");
            return new SerializedObject(presenter);
        }
    }
}
#endif
