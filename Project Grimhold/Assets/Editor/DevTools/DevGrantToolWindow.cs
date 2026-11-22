using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Grimhold/Dev/Grant Items: one-click test items and gold for the character you are playing (Play Mode), so
/// Town upgrades can be tested without raiding. It runs tools/dev-grant/grant.js against the LOCAL MongoDB and
/// rereads the profile. Outside Play Mode an account username can be used instead. Editor-only; never ships.
/// </summary>
public sealed class DevGrantToolWindow : EditorWindow
{
    private const string UserPref = "Grimhold.DevGrant.User";
    private const string NodePref = "Grimhold.DevGrant.Node";

    private string _user;
    private string _node;
    private int _gold = 1000;
    private readonly List<(string lootId, int amount)> _items = new();
    private bool _showCustom;
    private bool _running;
    private string _message = string.Empty;
    private MessageType _messageType = MessageType.None;
    private Vector2 _scroll;

    [MenuItem("Grimhold/Dev/Grant Items...")]
    public static void Open() => GetWindow<DevGrantToolWindow>(false, "Dev Grant");

    private void OnEnable()
    {
        _user = EditorPrefs.GetString(UserPref, string.Empty);
        _node = EditorPrefs.GetString(NodePref, "node");
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeChanged;

    private void OnPlayModeChanged(PlayModeStateChange _) => Repaint();

    private static string SessionCharacterId()
    {
        var auth = EditorApplication.isPlaying ? ApplicationAuthContext.Instance : null;
        return auth != null && auth.IsAuthenticated ? auth.CharacterId : null;
    }

    private void OnGUI()
    {
        string characterId = SessionCharacterId();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        using (new EditorGUI.DisabledScope(_running))
        {
            if (characterId != null)
            {
                EditorGUILayout.HelpBox($"Personaje de la sesión: {ApplicationAuthContext.Instance.CharacterName}. " +
                    "Los objetos van al Inventario y se sincronizan solos.", MessageType.Info);
                if (GUILayout.Button("Dar materiales de mejora", GUILayout.Height(32)))
                    Run(() => DevGrantTool.BuildArgumentsForCharacter(characterId, "upgrade-materials", null, 0));
                if (GUILayout.Button("Dar equipo de prueba de mejoras", GUILayout.Height(32)))
                    Run(() => DevGrantTool.BuildArgumentsForCharacter(characterId, "upgrade-gear", null, 0));
                if (GUILayout.Button("Dar 10000 de oro", GUILayout.Height(32)))
                    Run(() => DevGrantTool.BuildArgumentsForCharacter(characterId, null, null, 10000));
            }
            else
            {
                EditorGUILayout.HelpBox("Entra en Play Mode con tu personaje para usar los botones rápidos. " +
                    "Sin Play Mode puedes dar objetos por usuario en \"Opciones\".", MessageType.Info);
            }

            EditorGUILayout.Space();
            _showCustom = EditorGUILayout.Foldout(_showCustom, "Opciones (ítems sueltos, oro, usuario)", true);
            if (_showCustom) DrawCustom(characterId);
        }
        if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, _messageType);
        EditorGUILayout.EndScrollView();
    }

    private void DrawCustom(string characterId)
    {
        EditorGUI.indentLevel++;
        if (characterId == null)
        {
            EditorGUI.BeginChangeCheck();
            _user = EditorGUILayout.TextField("Usuario de la cuenta", _user);
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(UserPref, _user ?? string.Empty);
        }
        _gold = EditorGUILayout.IntSlider("Oro", _gold, 0, DevGrantTool.MaxGold);
        for (int i = 0; i < _items.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            _items[i] = (EditorGUILayout.TextField(_items[i].lootId), EditorGUILayout.IntField(_items[i].amount, GUILayout.Width(60)));
            if (GUILayout.Button("X", GUILayout.Width(24))) { _items.RemoveAt(i); i--; }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("Agregar ítem del catálogo")) _items.Add(("RES_RUNE_FRAGMENT", 10));
        if (GUILayout.Button("Dar"))
        {
            var items = new List<(string, int)>(_items);
            Run(() => characterId != null
                ? DevGrantTool.BuildArgumentsForCharacter(characterId, null, items, _gold)
                : DevGrantTool.BuildArguments(_user, null, items, _gold));
        }
        EditorGUI.BeginChangeCheck();
        _node = EditorGUILayout.TextField("Ejecutable de node", _node);
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(NodePref, _node ?? "node");
        EditorGUI.indentLevel--;
    }

    // Async void is the Unity GUI entry point; every failure is reported in the window and the Console.
    private async void Run(System.Func<string[]> buildArguments)
    {
        string[] arguments;
        try { arguments = buildArguments(); }
        catch (System.ArgumentException exception) { Show(exception.Message, MessageType.Warning); return; }

        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        var tool = new DevGrantTool(args => DevGrantTool.RunNodeAsync(repoRoot, _node, args), PlayModeReread());
        _running = true;
        Show("Dando objetos...", MessageType.None);
        try
        {
            string message = await tool.RunAsync(arguments);
            bool failed = message.StartsWith("Error");
            Show(message, failed ? MessageType.Error : MessageType.Info);
            if (failed) Debug.LogWarning("[DevGrant] " + message); else Debug.Log("[DevGrant] " + message);
        }
        finally
        {
            _running = false;
            Repaint();
        }
    }

    private static System.Func<System.Threading.Tasks.Task<bool>> PlayModeReread()
    {
        if (!EditorApplication.isPlaying) return null;
        var stash = Object.FindAnyObjectByType<ApplicationStashContext>();
        var reconciliation = stash != null ? stash.GetComponent<IProfileReconciliation>() : null;
        return reconciliation != null ? reconciliation.ReconcileAsync : null;
    }

    private void Show(string message, MessageType type)
    {
        _message = message;
        _messageType = type;
        Repaint();
    }
}
