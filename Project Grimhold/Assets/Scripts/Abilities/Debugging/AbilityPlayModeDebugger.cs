#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Developer-only overlay (F8) that unlocks abilities in the local profile and allows equipping and testing
/// abilities live in Town, CatacombsMap, and other raid maps. Compiled out of release builds.
/// </summary>
[DisallowMultipleComponent]
public sealed class AbilityPlayModeDebugger : MonoBehaviour
{
    private const KeyCode ToggleKey = KeyCode.F8;

    public static AbilityPlayModeDebugger Instance { get; private set; }

    private ApplicationStashContext _context;
    private AbilityDefinitionCatalog _catalog;
    private bool _visible;
    private Rect _windowRect = new(12f, 300f, 450f, 520f);
    private Vector2 _scroll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
#if UNITY_INCLUDE_TESTS
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName.StartsWith("InitTestScene")) return;
#endif
        if (Instance != null) return;
        var go = new GameObject("AbilityPlayModeDebugger");
        Instance = go.AddComponent<AbilityPlayModeDebugger>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Initialize(ApplicationStashContext context, AbilityDefinitionCatalog catalog)
    {
        if (context != null) _context = context;
        if (catalog != null) _catalog = catalog;
    }

    private LocalProfileStore Store => _context != null ? _context.Store : null;

    private void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
        {
            _visible = !_visible;
        }
    }

    private void EnsureCatalogAndContext()
    {
        if (_catalog == null)
        {
#if UNITY_EDITOR
            _catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<AbilityDefinitionCatalog>(
                "Assets/Scriptable Objects/Abilities/Catalogs/AbilityDefinitionCatalog.asset");
#endif
            if (_catalog == null)
            {
                var managers = FindObjectsByType<NetworkSpawnManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var m in managers)
                {
                    var f = typeof(NetworkSpawnManager).GetField("_abilityCatalog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (f?.GetValue(m) is AbilityDefinitionCatalog c) { _catalog = c; break; }
                }
            }
        }

        if (_context == null)
        {
            _context = FindAnyObjectByType<ApplicationStashContext>();
        }
    }

    private PlayerCharacter FindLocalPlayer()
    {
        var players = FindObjectsByType<PlayerCharacter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].Object != null && players[i].Object.IsValid && players[i].HasInputAuthority)
                return players[i];
        }
        return players.Length > 0 ? players[0] : null;
    }

    private void OnGUI()
    {
        if (!_visible)
        {
            return;
        }

        EnsureCatalogAndContext();

        _windowRect = GUI.Window(888124, _windowRect, DrawWindow, $"Abilities Debug [{ToggleKey}]");
        _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - _windowRect.width));
        _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, Screen.height - _windowRect.height));
    }

    private void DrawWindow(int windowId)
    {
        PlayerCharacter localPlayer = FindLocalPlayer();
        PlayerAbilityRuntimeNetworkController runtime = localPlayer != null
            ? localPlayer.GetComponent<PlayerAbilityRuntimeNetworkController>()
            : null;

        if (runtime != null)
        {
            GUILayout.Label("<b>=== Live Raid / Map Testing ===</b>");
            string s1Text = runtime.IsSlotAvailable(UniversalAbilitySlot.Slot1) ? "Slot 1: Configurado" : "Slot 1: Vacío";
            string s2Text = runtime.IsSlotAvailable(UniversalAbilitySlot.Slot2) ? "Slot 2: Configurado" : "Slot 2: Vacío";
            GUILayout.Label($"{s1Text} | {s2Text}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Equipar S1: Proyectil Arcano"))
            {
                runtime.RequestDebugOverrideSlot(UniversalAbilitySlot.Slot1, new AbilityId("arcane_projectile"));
            }
            if (GUILayout.Button("Equipar S2: Orbes Protectores"))
            {
                runtime.RequestDebugOverrideSlot(UniversalAbilitySlot.Slot2, new AbilityId("protective_orbs"));
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Equipar S1: Potenciar (Empower)"))
            {
                runtime.RequestDebugOverrideSlot(UniversalAbilitySlot.Slot1, new AbilityId("empower"));
            }
            if (GUILayout.Button("Limpiar Enfriamientos"))
            {
                runtime.RequestDebugResetCooldowns();
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Disparar proyectil enemigo hacia mí (Test Orbes)", GUILayout.Height(30f)))
            {
                runtime.RequestDebugSpawnHostileProjectileTowardMe();
            }

            GUILayout.Space(8);
        }

        LocalProfileStore store = Store;
        if (_catalog != null)
        {
            GUILayout.Label("<b>=== Repertoire & Profile Store ===</b>");
            if (store != null && store.IsAvailable)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"Unlock all ({_catalog.DefinitionCount})"))
                {
                    int unlocked = AbilityDebugUnlocker.UnlockAll(store, _catalog);
                    Debug.Log($"[AbilityDebugger] Unlocked {unlocked} abilities.", this);
                }
                if (GUILayout.Button("Relock all"))
                {
                    int relocked = AbilityDebugUnlocker.RelockAll(store);
                    Debug.Log($"[AbilityDebugger] Relocked {relocked} abilities.", this);
                }
                GUILayout.EndHorizontal();
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (AbilityDefinition definition in _catalog.Definitions)
            {
                if (definition == null) continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{definition.DisplayName} ({definition.Id})", GUILayout.ExpandWidth(true));

                if (runtime != null)
                {
                    if (GUILayout.Button("Set S1", GUILayout.Width(55f)))
                    {
                        runtime.RequestDebugOverrideSlot(UniversalAbilitySlot.Slot1, definition.AbilityId);
                    }
                    if (GUILayout.Button("Set S2", GUILayout.Width(55f)))
                    {
                        runtime.RequestDebugOverrideSlot(UniversalAbilitySlot.Slot2, definition.AbilityId);
                    }
                }

                if (store != null && store.IsAvailable)
                {
                    bool unlocked = store.IsAbilityUnlocked(definition.AbilityId);
                    if (unlocked)
                    {
                        GUILayout.Label("Unlocked", GUILayout.Width(60f));
                    }
                    else if (GUILayout.Button("Unlock", GUILayout.Width(60f)))
                    {
                        Debug.Log($"[AbilityDebugger] Unlock '{definition.Id}': {AbilityDebugUnlocker.Unlock(store, definition)}", this);
                    }
                }

                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
        else
        {
            GUILayout.Label("Catalog loading...");
        }

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
    }
}
#endif
