using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class MainMenuCompositionTests
{
    private Scene _scene;
    private MainMenuController _menu;
    private T[] All<T>() where T : Component => _scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
    private static Object Ref(Object owner, string field) => new SerializedObject(owner).FindProperty(field).objectReferenceValue;
    private static void Assigned(Object owner, params string[] fields)
    {
        foreach (var field in fields) Assert.That(Ref(owner, field), Is.Not.Null, owner.GetType().Name + "." + field);
    }
    [SetUp] public void Open()
    {
        _scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/MainMenu.unity");
        _menu = All<MainMenuController>().Single();
    }
    [TearDown] public void Close() { EditorSceneManager.ClosePreviewScene(_scene); }

    [Test] public void ProductionHasNoPreviewChrome()
    {
        foreach (var t in All<Transform>()) Assert.That(t.name, Is.Not.EqualTo("PreviewStateToolbar").And.Not.EqualTo("PreviewNotice").And.Not.EqualTo("MainMenuDesignPreview"));
        foreach (var t in All<TextMeshProUGUI>()) Assert.That(t.text.ToLowerInvariant(), Does.Not.Contain("ui preview").And.Not.Contain("states are examples"));
    }
    [Test] public void ControllerAndDialogDependenciesAreAuthored()
    {
        Assigned(_menu, "createRoomButton", "menuPanel", "_loginPanel", "_characterCreationPanel", "_settingsButton", "_creditsButton", "_exitButton", "_settingsPanel", "_creditsPanel", "_loginFlowController", "_connectionCoordinator", "_backendConfig", "_statusText", "_accountText", "_accountActionText", "_accountActionButton", "_serverText", "_playSubtitle", "_serverRetryButton", "_townRetryButton", "_confirmationDialog", "_infoDialog", "_authDialogRoot");
        var root = (GameObject)Ref(_menu, "_authDialogRoot");
        Assert.That(_menu.transform.IsChildOf(root.transform), Is.False);
        var login = (LoginPanelView)Ref(_menu, "_loginPanel");
        var character = (CharacterCreationPanelView)Ref(_menu, "_characterCreationPanel");
        Assert.That(login.transform.parent, Is.EqualTo(root.transform));
        Assert.That(character.transform.parent, Is.EqualTo(root.transform));
        var dialogs = All<MainMenuDialogView>();
        Assert.That(dialogs.Length, Is.EqualTo(2));
        foreach (var d in dialogs) Assigned(d, "_titleText", "_bodyText", "_cancelButton");
        Assigned(Ref(_menu, "_confirmationDialog"), "_acceptText", "_acceptButton");
        Assert.That(root.GetComponent<Image>().raycastTarget, Is.True);
    }
    [Test] public void FormFieldsAndUsernameOnlyPolicyAreWired()
    {
        var login = All<LoginPanelView>().Single();
        Assigned(login, "_usernameInput", "_passwordInput", "_backButton", "_statusText", "_confirmPasswordInput", "_signInTabButton", "_createAccountTabButton", "_submitButton", "_showPasswordButton", "_forgotPasswordButton", "_rememberUsernameToggle", "_rememberUsernameLabel", "_usernameErrorText", "_passwordErrorText", "_confirmPasswordErrorText", "_capsLockWarningText", "_showPasswordText", "_submitText", "_titleText", "_leadText", "_accountStep", "_confirmPasswordGroup");
        var label = ((TextMeshProUGUI)Ref(login, "_rememberUsernameLabel")).text;
        Assert.That(label, Does.Contain("usuario").And.Not.Contain("sesión").And.Not.Contain("signed in"));
        Assigned(All<CharacterCreationPanelView>().Single(), "_characterNameInput", "_createButton", "_statusText", "_stepText", "_finishLaterButton", "_finishLaterText");
    }
    [Test] public void CenteredCompositionUsesIncumbentSkinAndSpanishCopy()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/MainMenu.prefab").GetComponentsInChildren<Button>(true).Single(b => b.name == "Play Button");
        var play = (Button)Ref(_menu, "createRoomButton");
        Assert.That(((RectTransform)play.transform).anchoredPosition, Is.EqualTo(new Vector2(0, 110)));
        foreach (var b in new[] { play, (Button)Ref(_menu, "_settingsButton"), (Button)Ref(_menu, "_creditsButton"), (Button)Ref(_menu, "_exitButton") })
        {
            Assert.That(b.image.sprite, Is.EqualTo(source.image.sprite));
            Assert.That(b.transition, Is.EqualTo(Selectable.Transition.SpriteSwap));
            Assert.That(b.spriteState.highlightedSprite, Is.EqualTo(source.spriteState.highlightedSprite));
            Assert.That(b.spriteState.pressedSprite, Is.EqualTo(source.spriteState.pressedSprite));
            Assert.That(b.spriteState.selectedSprite, Is.EqualTo(source.spriteState.selectedSprite));
            Assert.That(b.spriteState.disabledSprite, Is.EqualTo(source.spriteState.disabledSprite));
            Assert.That(b.GetComponentInChildren<TextMeshProUGUI>(true).font, Is.EqualTo(source.GetComponentInChildren<TextMeshProUGUI>(true).font));
        }
        var labels = All<TextMeshProUGUI>().Select(t => t.text).ToArray();
        foreach (var text in new[] { "Jugar", "Configuración", "Créditos", "Salir", "Paso 1 de 2 · Cuenta", "Paso 2 de 2 · Personaje", "Cerrar sesión y terminar después" }) Assert.That(labels, Does.Contain(text));
        Assert.That(All<ApplicationVersionLabel>().Length, Is.EqualTo(1));
    }
    [Test] public void MenuUiLivesInPrefabInstancedByScene()
    {
        var root = PrefabUtility.GetOutermostPrefabInstanceRoot(_menu.gameObject);
        Assert.That(root, Is.Not.Null, "MainMenuController must belong to a prefab instance, not inlined scene objects.");
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root), Is.EqualTo("Assets/Prefabs/UI/MainMenu Canvas.prefab"));
        Assert.That(PrefabUtility.GetAddedGameObjects(root), Is.Empty, "Menu UI must be authored in the prefab, not added as scene overrides.");
        var settings = (Component)Ref(_menu, "_settingsPanel");
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(settings.gameObject), Is.EqualTo("Assets/Prefabs/UI/SettingsPanel.prefab"),
            "SettingsPanel is shared with Town and Raid menus and must stay a nested prefab instance.");
        var credits = (Component)Ref(_menu, "_creditsPanel");
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(credits.gameObject), Is.EqualTo("Assets/Prefabs/UI/CreditsPanel.prefab"),
            "Credits content is out of scope and stays editable in its own prefab.");
    }
    [Test] public void MainMenuHasNoLeftoverStash()
    {
        var root = _menu.GetComponentInParent<Canvas>(true).transform;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            Assert.That(t.name.ToLowerInvariant(), Does.Not.Contain("stash"), "Stash UI belongs to Town, not the main menu.");
            foreach (var c in t.GetComponents<Component>().Where(c => c != null))
                Assert.That(c.GetType().Name.ToLowerInvariant(), Does.Not.Contain("stash"), t.name);
        }
    }
    [Test] public void MenuPanelFillsTheWholeCanvas()
    {
        var canvas = _menu.GetComponentInParent<Canvas>(true).transform;
        for (var t = (RectTransform)((GameObject)Ref(_menu, "menuPanel")).transform; t != canvas; t = (RectTransform)t.parent)
        {
            // Layout leaves sub-pixel float noise in offsets; 0.01 px tolerance still rejects any real inset.
            foreach (var (actual, expected, what) in new[] { (t.anchorMin, Vector2.zero, "anchorMin"), (t.anchorMax, Vector2.one, "anchorMax"), (t.offsetMin, Vector2.zero, "offsetMin"), (t.offsetMax, Vector2.zero, "offsetMax") })
            {
                Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.01f), t.name + "." + what + ".x");
                Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.01f), t.name + "." + what + ".y");
            }
        }
    }
    [Test] public void SignInFormCollapsesHiddenRegisterRows()
    {
        var login = All<LoginPanelView>().Single();
        ((GameObject)Ref(login, "_confirmPasswordGroup")).SetActive(false);
        ((GameObject)Ref(login, "_accountStep")).SetActive(false);
        foreach (var field in new[] { "_usernameErrorText", "_passwordErrorText", "_confirmPasswordErrorText", "_capsLockWarningText" })
            ((Component)Ref(login, field)).gameObject.SetActive(false);
        RebuildLayout(login.transform);
        var password = (RectTransform)((Component)Ref(login, "_passwordInput")).transform;
        var remember = (RectTransform)((Component)Ref(login, "_rememberUsernameToggle")).transform;
        Assert.That(Bottom(login.transform, password) - Top(login.transform, remember), Is.LessThan(40f),
            "Sign-in mode must not keep empty space for the hidden confirm-password row.");
    }
    [Test] public void LoginTextIsNotUnderlinedAndRememberLabelSitsBesideItsBox()
    {
        var login = All<LoginPanelView>().Single();
        foreach (var text in login.GetComponentsInChildren<TextMeshProUGUI>(true))
            Assert.That(text.fontStyle & FontStyles.Underline, Is.EqualTo(FontStyles.Normal), text.name);
        RebuildLayout(login.transform);
        var label = (TextMeshProUGUI)Ref(login, "_rememberUsernameLabel");
        var box = (RectTransform)((Toggle)Ref(login, "_rememberUsernameToggle")).targetGraphic.transform;
        Assert.That(label.horizontalAlignment, Is.EqualTo(HorizontalAlignmentOptions.Left));
        Assert.That(Left(login.transform, label.rectTransform) - Right(login.transform, box), Is.InRange(0f, 24f),
            "The remember label must start right next to its checkbox.");
    }

    private static void RebuildLayout(Transform root)
    {
        foreach (var rect in root.GetComponentsInChildren<RectTransform>(true).Reverse()) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }
    // Local transform chain up to `space`: preview scenes do not scale the canvas, so world corners collapse to zero.
    private static Vector3[] Corners(Transform space, RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetLocalCorners(corners);
        var toSpace = Matrix4x4.identity;
        for (var t = (Transform)rect; t != space; t = t.parent)
            toSpace = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * toSpace;
        for (int i = 0; i < 4; i++) corners[i] = toSpace.MultiplyPoint3x4(corners[i]);
        return corners;
    }
    private static float Bottom(Transform space, RectTransform rect) => Corners(space, rect)[0].y;
    private static float Top(Transform space, RectTransform rect) => Corners(space, rect)[1].y;
    private static float Left(Transform space, RectTransform rect) => Corners(space, rect)[0].x;
    private static float Right(Transform space, RectTransform rect) => Corners(space, rect)[2].x;

    [Test] public void ModalNavigationIsExplicitAndContained()
    {
        foreach (var root in All<MainMenuDialogView>().Select(d => d.transform).Concat(new[] { All<LoginPanelView>().Single().transform, All<CharacterCreationPanelView>().Single().transform }))
        foreach (var s in root.GetComponentsInChildren<Selectable>(true))
        {
            Assert.That(s.navigation.mode, Is.EqualTo(Navigation.Mode.Explicit), s.name);
            foreach (var target in new[] { s.navigation.selectOnUp, s.navigation.selectOnDown, s.navigation.selectOnLeft, s.navigation.selectOnRight }.Where(t => t != null)) Assert.That(target.transform.IsChildOf(root), Is.True, s.name);
        }
    }
}
