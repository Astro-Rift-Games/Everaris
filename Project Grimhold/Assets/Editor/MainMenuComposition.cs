#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Explicit, repeatable production authoring of the main menu (chosen design: centered stack + auth dialog).
/// The UI is authored inside MainMenu Canvas.prefab; MainMenu.unity only instances it and keeps scene-service references.
/// SettingsPanel stays a nested prefab instance because Town and Raid menus share it.
/// </summary>
public static class MainMenuComposition
{
    private const string ScenePath = "Assets/Scenes/MainMenu.unity";
    private const string CanvasPrefabPath = "Assets/Prefabs/UI/MainMenu Canvas.prefab";
    private const string SharedSettingsPrefabPath = "Assets/Prefabs/UI/SettingsPanel.prefab";
    private const string CreditsPrefabPath = "Assets/Prefabs/UI/CreditsPanel.prefab";
    // Leftover from before Town existed: the stash belongs to Town, not the main menu.
    private const string StashPrefabPath = "Assets/Prefabs/UI/StashInventory.prefab";
    private const string StashToggleName = "OpenCloseStash";
    private static readonly Color ErrorColor = new Color(1f, .54f, .48f);
    private static Button _button;
    private static TMP_InputField _input;
    private static Image _panel;
    private static TextMeshProUGUI _text;
    private static Color _accent;

    [MenuItem("Grimhold/Author main menu")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before composing.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene; resolve it before composing.");
        LoadSkin();
        var contents = PrefabUtility.LoadPrefabContents(CanvasPrefabPath);
        try
        {
            UnpackMenuOnlyNestedPrefabs(contents);
            ComposePrefab(contents);
            PrefabUtility.SaveAsPrefabAsset(contents, CanvasPrefabPath, out bool saved);
            if (!saved) throw new InvalidOperationException("Could not save " + CanvasPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
        WireScene();
    }

    // MainMenu and LoginPanel become plain children so their content can be re-laid out.
    // Settings and Credits keep their own prefabs: their content is not part of this layout.
    private static void UnpackMenuOnlyNestedPrefabs(GameObject contents)
    {
        foreach (var t in contents.GetComponentsInChildren<Transform>(true).ToArray())
        {
            if (t == null) continue; // destroyed with a removed stash instance
            var go = t.gameObject;
            if (go == contents || !PrefabUtility.IsOutermostPrefabInstanceRoot(go)) continue;
            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (path == StashPrefabPath) { Object.DestroyImmediate(go); continue; }
            if (path == SharedSettingsPrefabPath || path == CreditsPrefabPath) continue;
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
        }
        foreach (var t in contents.GetComponentsInChildren<Transform>(true).Where(t => t.name == StashToggleName).ToArray())
            Object.DestroyImmediate(t.gameObject);
    }

    private static void ComposePrefab(GameObject contents)
    {
        var controller = contents.GetComponentsInChildren<MainMenuController>(true).Single();
        var canvas = controller.GetComponentInParent<Canvas>(true);
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
        var menu = (GameObject)Get(controller, "menuPanel");
        // The menu and every container above it fill the canvas so chips and labels can pin to screen corners.
        for (var t = menu.transform; t != canvas.transform; t = t.parent) Stretch((RectTransform)t);
        // Retain legacy development/lobby references and objects, but not their menu affordances.
        var holder = menu.transform.Find("ButtonHolder");
        if (holder != null) holder.gameObject.SetActive(false);
        var play = MoveButton((Button)Get(controller, "createRoomButton"), menu.transform, "Jugar", 110, 88);
        MoveButton((Button)Get(controller, "_settingsButton"), menu.transform, "Configuración", -43, 54);
        MoveButton((Button)Get(controller, "_creditsButton"), menu.transform, "Créditos", -109, 54);
        MoveButton((Button)Get(controller, "_exitButton"), menu.transform, "Salir", -175, 54);
        var playLabel = play.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name != "PlaySubtitle");
        Place(playLabel.rectTransform, play.transform, new Vector2(0, 10), new Vector2(340, 34)); playLabel.fontSize = 28;
        Set(controller, "_playSubtitle", Text(play.transform, "PlaySubtitle", "", new Vector2(0, -21), new Vector2(340, 26), 16));
        var title = canvas.GetComponentsInChildren<TextMeshProUGUI>(true).Single(t => t.name == "Title" && t.GetComponentInParent<MainMenuDialogView>(true) == null);
        Place(title.rectTransform, menu.transform, new Vector2(0, 242), new Vector2(720, 110)); SkinText(title); title.text = "GRIMHOLD"; title.fontSize = 84; title.characterSpacing = 9;
        var status = (TextMeshProUGUI)Get(controller, "_statusText");
        Place(status.rectTransform, menu.transform, new Vector2(0, 44), new Vector2(720, 26)); SkinText(status); status.fontSize = 16; status.text = "";
        var retry = Button(menu.transform, "TownRetry", "Reintentar", new Vector2(0, 15), new Vector2(180, 28), 16);
        Set(controller, "_townRetryButton", retry); retry.gameObject.SetActive(false);
        var divider = Rect(menu.transform, "MenuDivider", new Vector2(0, -8), new Vector2(360, 2));
        Ensure<Image>(divider.gameObject).color = _accent;
        var server = Panel(menu.transform, "ServerChip", Vector2.zero, new Vector2(380, 46)); Pin(server, new Vector2(0, 1), new Vector2(222, -50));
        Set(controller, "_serverText", Text(server, "ServerText", "", new Vector2(-60, 0), new Vector2(240, 40), 16));
        Set(controller, "_serverRetryButton", Button(server, "ServerRetry", "Reintentar", new Vector2(124, 0), new Vector2(116, 34), 16));
        var account = Panel(menu.transform, "AccountChip", Vector2.zero, new Vector2(410, 46)); Pin(account, Vector2.one, new Vector2(-237, -50));
        Set(controller, "_accountText", Text(account, "AccountText", "", new Vector2(-72, 0), new Vector2(250, 40), 16));
        var action = Button(account, "AccountAction", "Iniciar sesión", new Vector2(125, 0), new Vector2(150, 34), 16);
        Set(controller, "_accountActionButton", action); Set(controller, "_accountActionText", action.GetComponentInChildren<TextMeshProUGUI>());
        var version = contents.GetComponentsInChildren<ApplicationVersionLabel>(true).Single().GetComponent<TextMeshProUGUI>();
        Place(version.rectTransform, menu.transform, Vector2.zero, new Vector2(210, 30)); Pin(version.rectTransform, Vector2.zero, new Vector2(137, 28)); SkinText(version); version.fontSize = 16;
        var hints = Text(menu.transform, "KeyboardHints", "Enter Seleccionar · Esc Volver", Vector2.zero, new Vector2(350, 30), 16);
        Pin(hints.rectTransform, new Vector2(1, 0), new Vector2(-207, 28));

        var auth = Scrim(canvas.transform, "AuthDialog");
        Set(controller, "_authDialogRoot", auth.gameObject);
        ComposeLogin((LoginPanelView)Get(controller, "_loginPanel"), auth);
        ComposeCharacter((CharacterCreationPanelView)Get(controller, "_characterCreationPanel"), auth);
        Set(controller, "_confirmationDialog", Dialog(canvas.transform, "ConfirmationDialog", true));
        Set(controller, "_infoDialog", Dialog(canvas.transform, "InfoDialog", false));
        auth.gameObject.SetActive(false);
        foreach (var field in new[] { "_settingsPanel", "_creditsPanel" }) ((Component)Get(controller, field)).transform.SetAsLastSibling();
        Navigate(menu.transform);
    }

    // Scene-only services stay scene objects; their references are instance overrides on the prefab instance.
    private static void WireScene()
    {
        var scene = SceneManager.GetActiveScene().path == ScenePath ? SceneManager.GetActiveScene() : EditorSceneManager.OpenScene(ScenePath);
        var controller = All<MainMenuController>(scene).Single();
        // Legacy scene overrides pinned positions, sizes and font sizes of the old layout; the prefab now owns them.
        var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(controller.gameObject);
        foreach (var component in instanceRoot.GetComponentsInChildren<Component>(true).Where(c => c is RectTransform || c is TMP_Text))
            if (PrefabUtility.IsPartOfPrefabInstance(component) && !PrefabUtility.IsAddedComponentOverride(component))
                PrefabUtility.RevertObjectOverride(component, InteractionMode.AutomatedAction);
        // Overrides whose prefab targets no longer exist (old layout objects) are dead weight in the scene file.
        PrefabUtility.RemoveUnusedOverrides(new[] { instanceRoot }, InteractionMode.AutomatedAction);
        var loginFlow = All<LoginFlowController>(scene).Single();
        Set(controller, "_loginFlowController", loginFlow);
        Set(controller, "_connectionCoordinator", All<SessionConnectionCoordinator>(scene).Single());
        Set(controller, "_backendConfig", loginFlow.Config);
        var eventSystem = All<EventSystem>(scene).Single();
        var so = new SerializedObject(eventSystem);
        so.FindProperty("m_FirstSelected").objectReferenceValue = ((Button)Get(controller, "createRoomButton")).gameObject;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save MainMenu.");
    }

    private static void ComposeLogin(LoginPanelView view, RectTransform auth)
    {
        Place((RectTransform)view.transform, auth, Vector2.zero, new Vector2(500, 760));
        var old = view.transform.Find("Panel"); if (old != null) old.gameObject.SetActive(false);
        var panel = Panel(view.transform, "AuthPanel", Vector2.zero, new Vector2(500, 0));
        Column(panel, 30, 10);
        var fitter = Ensure<ContentSizeFitter>(panel.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = (TextMeshProUGUI)Get(view, "_titleText") ?? view.GetComponentsInChildren<TextMeshProUGUI>(true).Single(t => t.name == "Login Title Text");
        SkinText(title); title.fontSize = 28; title.text = "Inicia sesión para jugar"; Row(title.rectTransform, panel, 40); Set(view, "_titleText", title);
        var lead = Text(panel, "Lead", "Tu personaje y tu alijo se guardan en tu cuenta.", Vector2.zero, new Vector2(440, 48), 18);
        Row(lead.rectTransform, panel, 48); Set(view, "_leadText", lead);
        var tabs = Rect(panel, "TabsRow", Vector2.zero, new Vector2(440, 44)); Line(tabs, 8); Row(tabs, panel, 44);
        var signInTab = Button(tabs, "SignInTab", "Iniciar sesión", Vector2.zero, new Vector2(216, 42)); Flexible(signInTab.transform);
        var createTab = Button(tabs, "CreateAccountTab", "Crear cuenta", Vector2.zero, new Vector2(216, 42)); Flexible(createTab.transform);
        signInTab.transform.SetAsFirstSibling();
        Set(view, "_signInTabButton", signInTab); Set(view, "_createAccountTabButton", createTab);
        var step = Text(panel, "AccountStep", "Paso 1 de 2 · Cuenta", Vector2.zero, new Vector2(440, 26), 16); Row(step.rectTransform, panel, 26);
        Set(view, "_accountStep", step.gameObject);

        Heading(panel, "UsuarioHeading", "Usuario");
        var username = (TMP_InputField)Get(view, "_usernameInput");
        SkinInput(username, false); Row(username.transform, panel, 44);
        var usernameError = ErrorText(panel, "UsernameError"); Set(view, "_usernameErrorText", usernameError);

        Heading(panel, "ContrasenaHeading", "Contraseña");
        var passwordRow = Rect(panel, "PasswordRow", Vector2.zero, new Vector2(440, 44)); Line(passwordRow, 8); Row(passwordRow, panel, 44);
        var password = (TMP_InputField)Get(view, "_passwordInput");
        SkinInput(password, true); password.transform.SetParent(passwordRow, false); password.transform.SetAsFirstSibling(); Flexible(password.transform);
        var show = Button(passwordRow, "ShowPassword", "Mostrar", Vector2.zero, new Vector2(110, 42), 16); Fixed(show.transform, 110);
        Set(view, "_showPasswordButton", show); Set(view, "_showPasswordText", show.GetComponentInChildren<TextMeshProUGUI>());
        var passwordError = ErrorText(panel, "PasswordError"); Set(view, "_passwordErrorText", passwordError);

        var group = Rect(panel, "ConfirmPasswordGroup", Vector2.zero, new Vector2(440, 0)); Column(group, 0, 10); Row(group, panel, 0);
        var confirm = group.GetComponentInChildren<TMP_InputField>(true);
        if (confirm == null) { confirm = Object.Instantiate(_input, group); confirm.name = "ConfirmPassword"; }
        Heading(group, "ConfirmarHeading", "Confirmar contraseña");
        SkinInput(confirm, true); Row(confirm.transform, group, 44);
        var confirmError = ErrorText(group, "ConfirmError");
        Set(view, "_confirmPasswordInput", confirm); Set(view, "_confirmPasswordGroup", group.gameObject);
        Set(view, "_confirmPasswordErrorText", confirmError);
        var caps = ErrorText(panel, "CapsLock"); caps.color = _text.color; Set(view, "_capsLockWarningText", caps);

        var remember = Rect(panel, "RememberUsername", Vector2.zero, new Vector2(440, 32)); Row(remember, panel, 32);
        var rememberLine = Line(remember, 12); rememberLine.childAlignment = TextAnchor.MiddleLeft; rememberLine.childControlHeight = false; rememberLine.childForceExpandHeight = false;
        var toggle = Ensure<Toggle>(remember.gameObject);
        var box = Rect(remember, "Box", Vector2.zero, new Vector2(24, 24)); Fixed(box, 24); box.SetAsFirstSibling();
        var image = Ensure<Image>(box.gameObject); CopyImage((Image)_input.targetGraphic, image);
        var mark = Rect(box, "Mark", Vector2.zero, new Vector2(14, 14)); var check = Ensure<Image>(mark.gameObject); check.color = _accent;
        toggle.targetGraphic = image; toggle.graphic = check; toggle.isOn = false;
        var rememberLabel = Text(remember, "RememberLabel", "Recordar nombre de usuario", Vector2.zero, new Vector2(400, 30), 18);
        rememberLabel.alignment = TextAlignmentOptions.MidlineLeft; rememberLabel.raycastTarget = true; Flexible(rememberLabel.transform);
        Set(view, "_rememberUsernameToggle", toggle); Set(view, "_rememberUsernameLabel", rememberLabel);

        var status = (TextMeshProUGUI)Get(view, "_statusText"); SkinText(status); status.fontSize = 16; status.text = ""; Row(status.rectTransform, panel, 0);
        var buttons = Rect(panel, "ButtonsRow", Vector2.zero, new Vector2(440, 48)); Line(buttons, 12); Row(buttons, panel, 48);
        var back = (Button)Get(view, "_backButton"); back.transform.SetParent(buttons, false); StyleButton(back, "Volver"); Fixed(back.transform, 166);
        var submit = Button(buttons, "Submit", "Iniciar sesión", Vector2.zero, new Vector2(258, 46)); Flexible(submit.transform);
        back.transform.SetAsFirstSibling();
        Set(view, "_submitButton", submit); Set(view, "_submitText", submit.GetComponentInChildren<TextMeshProUGUI>());
        var forgot = Button(panel, "ForgotPassword", "¿Olvidaste tu contraseña?", Vector2.zero, new Vector2(350, 34), 16); Row(forgot.transform, panel, 34);
        Set(view, "_forgotPasswordButton", forgot);

        // Authored defaults match sign-in mode; LoginPanelView toggles these at runtime.
        group.gameObject.SetActive(false); step.gameObject.SetActive(false);
        foreach (var label in new[] { usernameError, passwordError, confirmError, caps }) { label.text = ""; label.gameObject.SetActive(false); }
        // Hidden legacy children (old register/back links) carried an underline style.
        foreach (var text in view.GetComponentsInChildren<TextMeshProUGUI>(true)) text.fontStyle &= ~FontStyles.Underline;
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        Navigate(view.transform);
        view.gameObject.SetActive(false);
    }

    private static void ComposeCharacter(CharacterCreationPanelView view, RectTransform auth)
    {
        Place((RectTransform)view.transform, auth, Vector2.zero, new Vector2(500, 470));
        var panel = Panel(view.transform, "CharacterPanel", Vector2.zero, new Vector2(500, 470)); panel.SetAsFirstSibling();
        Set(view, "_stepText", Text(panel, "CharacterStep", "Paso 2 de 2 · Personaje", new Vector2(0, 194), new Vector2(440, 30), 16));
        Text(panel, "CharacterTitle", "Nombra a tu personaje", new Vector2(0, 146), new Vector2(440, 40), 28);
        Text(panel, "CharacterLead", "Tienes un personaje por cuenta.\nLos demás jugadores verán este nombre.", new Vector2(0, 89), new Vector2(440, 60), 18);
        var field = (TMP_InputField)Get(view, "_characterNameInput");
        SkinInput(field, false); Place((RectTransform)field.transform, view.transform, new Vector2(0, 0), new Vector2(440, 42));
        var heading = Text(view.transform, field.name + "Heading", "Nombre del personaje", new Vector2(0, 36), new Vector2(440, 26), 18); heading.alignment = TextAlignmentOptions.MidlineLeft;
        var create = (Button)Get(view, "_createButton"); Place((RectTransform)create.transform, view.transform, new Vector2(0, -111), new Vector2(440, 48)); StyleButton(create, "Crear personaje");
        var status = (TextMeshProUGUI)Get(view, "_statusText"); Place(status.rectTransform, view.transform, new Vector2(0, -60), new Vector2(440, 44)); SkinText(status); status.fontSize = 16; status.text = "";
        var later = Button(view.transform, "FinishLater", "Cerrar sesión y terminar después", new Vector2(0, -182), new Vector2(440, 42), 18);
        Set(view, "_finishLaterButton", later); Set(view, "_finishLaterText", later.GetComponentInChildren<TextMeshProUGUI>());
        Navigate(view.transform); view.gameObject.SetActive(false);
    }

    private static MainMenuDialogView Dialog(Transform canvas, string name, bool confirmation)
    {
        var root = Scrim(canvas, name); var view = Ensure<MainMenuDialogView>(root.gameObject);
        var panel = Panel(root, "Panel", Vector2.zero, new Vector2(500, 300));
        Set(view, "_titleText", Text(panel, "Title", "", new Vector2(0, 96), new Vector2(440, 44), 28));
        Set(view, "_bodyText", Text(panel, "Body", "", new Vector2(0, 13), new Vector2(440, 100), 18));
        Set(view, "_cancelButton", Button(panel, "Cancel", confirmation ? "Cancelar" : "Volver", new Vector2(confirmation ? -112 : 0, -100), new Vector2(210, 46)));
        if (confirmation) { var accept = Button(panel, "Accept", "Salir", new Vector2(112, -100), new Vector2(210, 46)); Set(view, "_acceptButton", accept); Set(view, "_acceptText", accept.GetComponentInChildren<TextMeshProUGUI>()); }
        Navigate(root); root.gameObject.SetActive(false); return view;
    }

    private static void LoadSkin()
    {
        _button = Load("Assets/Prefabs/UI/MainMenu.prefab").GetComponentsInChildren<Button>(true).Single(b => b.name == "Play Button");
        var login = Load("Assets/Prefabs/UI/LoginPanel.prefab");
        _input = login.GetComponentsInChildren<TMP_InputField>(true).Single(i => i.name == "Username Input");
        _panel = login.GetComponentsInChildren<Image>(true).Single(i => i.name == "Panel");
        _text = _button.GetComponentInChildren<TextMeshProUGUI>(true);
        _accent = Load(SharedSettingsPrefabPath).GetComponentsInChildren<Slider>(true).First().fillRect.GetComponent<Image>().color;
    }

    private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new InvalidOperationException(path);
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
    private static Object Get(Object target, string name) => new SerializedObject(target).FindProperty(name).objectReferenceValue;
    private static void Set(Object target, string name, Object value) { var so = new SerializedObject(target); so.FindProperty(name).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    private static T Ensure<T>(GameObject g) where T : Component => g.GetComponent<T>() ?? g.AddComponent<T>();
    private static void Place(RectTransform r, Transform parent, Vector2 pos, Vector2 size) { r.SetParent(parent, false); r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f); r.localScale = Vector3.one; r.anchoredPosition = pos; r.sizeDelta = size; r.gameObject.SetActive(true); }
    private static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size) { var existing = parent.Find(name); var r = existing != null ? (RectTransform)existing : new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); Place(r, parent, pos, size); return r; }
    private static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
    private static void Pin(RectTransform r, Vector2 anchor, Vector2 pos) { r.anchorMin = r.anchorMax = anchor; r.anchoredPosition = pos; }
    private static RectTransform Panel(Transform parent, string name, Vector2 pos, Vector2 size) { var r = Rect(parent, name, pos, size); CopyImage(_panel, Ensure<Image>(r.gameObject)); return r; }
    private static RectTransform Scrim(Transform parent, string name) { var r = Rect(parent, name, Vector2.zero, Vector2.zero); Stretch(r); var i = Ensure<Image>(r.gameObject); i.color = new Color(0, 0, 0, .6f); i.raycastTarget = true; return r; }

    private static void SkinText(TextMeshProUGUI t)
    {
        t.font = _text.font; t.fontSharedMaterial = _text.fontSharedMaterial; t.color = _text.color;
        t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false; t.enableAutoSizing = false;
        t.fontStyle &= ~FontStyles.Underline; // the legacy LoginPanel back link was underlined
    }
    private static TextMeshProUGUI Text(Transform parent, string name, string value, Vector2 pos, Vector2 size, float fontSize) { var r = Rect(parent, name, pos, size); var t = Ensure<TextMeshProUGUI>(r.gameObject); SkinText(t); t.text = value; t.fontSize = fontSize; return t; }
    private static void CopyImage(Image source, Image target) { target.sprite = source.sprite; target.overrideSprite = null; target.type = source.type; target.color = source.color; target.material = source.material; target.preserveAspect = source.preserveAspect; target.fillCenter = source.fillCenter; target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier; }
    private static void CopySelectable(Selectable source, Selectable target) { target.transition = source.transition; target.colors = source.colors; target.spriteState = source.spriteState; }

    private static void StyleButton(Button b, string label, float size = 22)
    {
        var image = Ensure<Image>(b.gameObject); CopyImage(_button.image, image); b.targetGraphic = image; CopySelectable(_button, b);
        var t = b.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(x => x.name != "PlaySubtitle") ?? Text(b.transform, "Label", label, Vector2.zero, Vector2.zero, size);
        t.rectTransform.SetParent(b.transform, false); t.gameObject.SetActive(true);
        // Stretch the label so it follows layout-driven button sizes.
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one; t.rectTransform.pivot = new Vector2(.5f, .5f);
        t.rectTransform.offsetMin = new Vector2(6, 1); t.rectTransform.offsetMax = new Vector2(-6, -1);
        SkinText(t); t.text = label; t.fontSize = size;
    }
    private static Button Button(Transform parent, string name, string label, Vector2 pos, Vector2 size, float fontSize = 22) { var r = Rect(parent, name, pos, size); var b = Ensure<Button>(r.gameObject); StyleButton(b, label, fontSize); return b; }
    private static Button MoveButton(Button b, Transform parent, string label, float y, float height) { Place((RectTransform)b.transform, parent, new Vector2(0, y), new Vector2(360, height)); StyleButton(b, label); return b; }

    private static void SkinInput(TMP_InputField field, bool secret)
    {
        field.gameObject.SetActive(true);
        CopyImage((Image)_input.targetGraphic, (Image)field.targetGraphic); CopySelectable(_input, field);
        field.contentType = secret ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard; field.text = "";
        foreach (var t in field.GetComponentsInChildren<TextMeshProUGUI>(true)) { t.font = _input.textComponent.font; t.fontSharedMaterial = _input.textComponent.fontSharedMaterial; t.fontSize = 20; t.fontStyle &= ~FontStyles.Underline; if (t != field.textComponent) t.text = ""; }
        field.textViewport.anchorMin = Vector2.zero; field.textViewport.anchorMax = Vector2.one; field.textViewport.offsetMin = new Vector2(12, 2); field.textViewport.offsetMax = new Vector2(-12, -2);
    }

    // Vertical/horizontal layout helpers: hidden rows (inactive) collapse instead of leaving gaps.
    private static VerticalLayoutGroup Column(RectTransform r, int padding, float spacing)
    {
        var v = Ensure<VerticalLayoutGroup>(r.gameObject);
        v.padding = new RectOffset(padding, padding, padding, padding); v.spacing = spacing; v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        return v;
    }
    private static HorizontalLayoutGroup Line(RectTransform r, float spacing)
    {
        var h = Ensure<HorizontalLayoutGroup>(r.gameObject);
        h.spacing = spacing; h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
        return h;
    }
    private static void Row(Transform child, RectTransform parent, float height)
    {
        child.SetParent(parent, false); child.SetAsLastSibling();
        var element = Ensure<LayoutElement>(child.gameObject);
        element.minHeight = element.preferredHeight = height > 0 ? height : -1;
    }
    private static void Fixed(Transform child, float width) { var e = Ensure<LayoutElement>(child.gameObject); e.minWidth = e.preferredWidth = width; e.flexibleWidth = 0; }
    private static void Flexible(Transform child) { var e = Ensure<LayoutElement>(child.gameObject); e.flexibleWidth = 1; }
    private static void Heading(RectTransform parent, string name, string label)
    {
        var heading = Text(parent, name, label, Vector2.zero, new Vector2(440, 26), 18); heading.alignment = TextAlignmentOptions.MidlineLeft;
        Row(heading.rectTransform, parent, 26);
    }
    private static TextMeshProUGUI ErrorText(RectTransform parent, string name)
    {
        var t = Text(parent, name, "", Vector2.zero, new Vector2(440, 24), 16); t.alignment = TextAlignmentOptions.TopLeft; t.color = ErrorColor;
        Row(t.rectTransform, parent, 0);
        return t;
    }

    private static void Navigate(Transform root)
    {
        var all = root.GetComponentsInChildren<Selectable>(true);
        foreach (var s in all) s.navigation = new Navigation { mode = Navigation.Mode.Explicit };
        var controls = all.Where(s => s.gameObject.activeInHierarchy).ToArray();
        for (int i = 0; i < controls.Length; i++) { var nav = new Navigation { mode = Navigation.Mode.Explicit }; nav.selectOnUp = nav.selectOnLeft = controls[(i + controls.Length - 1) % controls.Length]; nav.selectOnDown = nav.selectOnRight = controls[(i + 1) % controls.Length]; controls[i].navigation = nav; }
    }
}
#endif
