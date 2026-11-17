#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Runtime developer/testing tool for testing consumables and treasures in PlayMode.
/// Allows spawning specific test loot pickups or arbitrary LootDefinitions directly at the player.
/// </summary>
[DisallowMultipleComponent]
public sealed class RaidLootTestSpawner : MonoBehaviour
{
    public static RaidLootTestSpawner Instance { get; private set; }

    [Header("Configuration")]
    [Tooltip("Default loot pickup network prefab to spawn.")]
    [SerializeField] private NetworkObject _pickupPrefab;

    [Tooltip("Shared catalog for translating loot identifiers.")]
    [SerializeField] private LootDefinitionCatalog _lootCatalog;

    [Header("Hotkeys & UI")]
    [SerializeField] private KeyCode _spawnConsumablesHotkey = KeyCode.F6;
    [SerializeField] private KeyCode _spawnAllHotkey = KeyCode.F8;

    [Header("Test Consumables")]
    [SerializeField] private LootDefinition _manaPotion;
    [SerializeField] private LootDefinition _manaFragment;
    [SerializeField] private LootDefinition _healthPotion;
    [SerializeField] private LootDefinition _lockpick;
    [SerializeField] private LootDefinition _selfRevive;

    [Header("Test Throwables")]
    [SerializeField] private LootDefinition _explosiveBomb;
    [SerializeField] private LootDefinition _smokeBomb;
    [SerializeField] private LootDefinition _throwingDagger;

    [Header("Test MVP Treasures")]
    [SerializeField] private LootDefinition _chaosCoin;
    [SerializeField] private LootDefinition _pilgrimLight;
    [SerializeField] private LootDefinition _sanctuaryHeart;
    [SerializeField] private LootDefinition _voidBell;
    [SerializeField] private LootDefinition _famineIdol;
    [SerializeField] private LootDefinition _wandererEye;

    [Header("Test Expedition Utilities (TASK-265)")]
    [SerializeField] private LootDefinition _runeCompass;
    [SerializeField] private LootDefinition _arcaneDetector;
    [SerializeField] private LootDefinition _etherDust;
    [SerializeField] private NetworkObject _lootContainerPrefab;

    [Header("Custom Spawn Slot")]
    public LootDefinition CustomItemToSpawn;
    public int CustomItemAmount = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
#if UNITY_INCLUDE_TESTS
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName.StartsWith("InitTestScene")) return;
#endif
        if (Instance != null) return;
        var existing = FindAnyObjectByType<RaidLootTestSpawner>();
        if (existing != null)
        {
            Instance = existing;
            return;
        }

        var go = new GameObject("[LootTestSpawner]");
        Instance = go.AddComponent<RaidLootTestSpawner>();
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

        EnsureDependencies();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void EnsureDependencies()
    {
        if (_spawnConsumablesHotkey == KeyCode.None)
        {
            _spawnConsumablesHotkey = KeyCode.F6;
        }

        if (_spawnAllHotkey == KeyCode.None)
        {
            _spawnAllHotkey = KeyCode.F8;
        }

        if (_lootCatalog == null)
        {
            _lootCatalog = Resources.Load<LootDefinitionCatalog>("Loot/Catalogs/LootDefinitionCatalog");
#if UNITY_EDITOR
            if (_lootCatalog == null)
            {
                _lootCatalog = UnityEditor.AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
                    "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
            }
#endif
        }

        if (_pickupPrefab == null)
        {
            _pickupPrefab = Resources.Load<NetworkObject>("Prefabs/LootPickup");
#if UNITY_EDITOR
            if (_pickupPrefab == null)
            {
                _pickupPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<NetworkObject>(
                    "Assets/Prefabs/LootPickup.prefab");
            }
#endif
        }

        if (_lootCatalog != null)
        {
            if (_healthPotion == null) _lootCatalog.TryGet("healthpotion", out _healthPotion);
            if (_manaPotion == null) _lootCatalog.TryGet("CON_MANA_POTION", out _manaPotion);
            if (_manaFragment == null) _lootCatalog.TryGet("CON_MANA_FRAGMENT", out _manaFragment);
            if (_explosiveBomb == null) _lootCatalog.TryGet("CON_EXPLOSIVE_BOMB", out _explosiveBomb);
            if (_smokeBomb == null) _lootCatalog.TryGet("CON_SMOKE_BOMB", out _smokeBomb);
            if (_throwingDagger == null) _lootCatalog.TryGet("CON_THROWING_DAGGER", out _throwingDagger);
            if (_lockpick == null) _lootCatalog.TryGet("CON_LOCKPICK", out _lockpick);
            if (_selfRevive == null) _lootCatalog.TryGet("CON_SELF_REVIVE", out _selfRevive);
            if (_runeCompass == null) _lootCatalog.TryGet("UTL_RUNE_COMPASS", out _runeCompass);
            if (_arcaneDetector == null) _lootCatalog.TryGet("UTL_ARCANE_DETECTOR", out _arcaneDetector);
            if (_etherDust == null) _lootCatalog.TryGet("RES_ETHER_DUST", out _etherDust);
            if (_chaosCoin == null) _lootCatalog.TryGet("TRE_CHAOS_COIN", out _chaosCoin);
            if (_pilgrimLight == null) _lootCatalog.TryGet("TRE_PILGRIM_LIGHT", out _pilgrimLight);
            if (_sanctuaryHeart == null) _lootCatalog.TryGet("TRE_SANCTUARY_HEART", out _sanctuaryHeart);
            if (_voidBell == null) _lootCatalog.TryGet("TRE_VOID_BELL", out _voidBell);
            if (_famineIdol == null) _lootCatalog.TryGet("TRE_FAMINE_IDOL", out _famineIdol);
            if (_wandererEye == null) _lootCatalog.TryGet("TRE_WANDERER_EYE", out _wandererEye);
        }

#if UNITY_EDITOR
        if (_selfRevive == null)
        {
            _selfRevive = UnityEditor.AssetDatabase.LoadAssetAtPath<LootDefinition>(
                "Assets/Scriptable Objects/Loot/Definitions/CON_SELF_REVIVE.asset");
        }
        if (_lockpick == null)
        {
            _lockpick = UnityEditor.AssetDatabase.LoadAssetAtPath<LootDefinition>(
                "Assets/Scriptable Objects/Loot/Definitions/CON_LOCKPICK.asset");
        }
        if (_explosiveBomb == null)
        {
            _explosiveBomb = UnityEditor.AssetDatabase.LoadAssetAtPath<LootDefinition>(
                "Assets/Scriptable Objects/Loot/Definitions/CON_EXPLOSIVE_BOMB.asset");
        }
        if (_smokeBomb == null)
        {
            _smokeBomb = UnityEditor.AssetDatabase.LoadAssetAtPath<LootDefinition>(
                "Assets/Scriptable Objects/Loot/Definitions/CON_SMOKE_BOMB.asset");
        }
        if (_throwingDagger == null)
        {
            _throwingDagger = UnityEditor.AssetDatabase.LoadAssetAtPath<LootDefinition>(
                "Assets/Scriptable Objects/Loot/Definitions/CON_THROWING_DAGGER.asset");
        }
#endif
    }

    private void Update()
    {
        if (!Application.isPlaying) return;

        if (_spawnConsumablesHotkey != KeyCode.None && Input.GetKeyDown(_spawnConsumablesHotkey))
        {
            SpawnAllConsumables();
        }

        if (_spawnAllHotkey != KeyCode.None && _spawnAllHotkey != _spawnConsumablesHotkey && Input.GetKeyDown(_spawnAllHotkey))
        {
            SpawnAllTestLoot();
        }
    }

    [ContextMenu("Spawn All Consumables At Player (F6)")]
    public void SpawnAllConsumables()
    {
        EnsureDependencies();
        var runner = FindActiveRunner();
        if (runner == null || !runner.IsServer)
        {
            var context = FindAnyObjectByType<ApplicationStashContext>();
            var store = context != null ? context.Store : null;
            if (store != null && store.IsAvailable)
            {
                var stashItems = new List<StashItem>();
                void AddItem(LootDefinition def, int amount)
                {
                    if (def != null && def.LootId.IsValid)
                        stashItems.Add(new StashItem(def.LootId, amount));
                }
                AddItem(_healthPotion, 5);
                AddItem(_manaPotion, 5);
                AddItem(_manaFragment, 10);
                AddItem(_explosiveBomb, 5);
                AddItem(_smokeBomb, 5);
                AddItem(_throwingDagger, 10);
                AddItem(_lockpick, 5);
                AddItem(_selfRevive, 1);
                AddItem(_runeCompass, 1);
                AddItem(_arcaneDetector, 1);
                store.TryImportItems(stashItems);
                Debug.Log($"[LootTestSpawner] Imported {stashItems.Count} consumable stacks to Town Stash.");
                return;
            }

            Debug.LogWarning("[LootTestSpawner] Cannot spawn loot: Active runner is null or not State Authority/Host.");
            return;
        }

        Vector3 origin = GetPlayerSpawnAnchor();

        var consumables = new (LootDefinition def, int amount)[]
        {
            (_healthPotion, 2),
            (_manaPotion, 2),
            (_manaFragment, 5),
            (_explosiveBomb, 3),
            (_smokeBomb, 2),
            (_throwingDagger, 5),
            (_lockpick, 5),
            (_selfRevive, 1),
            (_runeCompass, 1),
            (_arcaneDetector, 1)
        };

        float spacing = 0.8f;
        int count = 0;
        for (int i = 0; i < consumables.Length; i++)
        {
            if (consumables[i].def != null)
            {
                float offsetX = ((count % 5) - 2) * spacing;
                float offsetY = -((count / 5) + 1) * spacing;
                Vector3 spawnPos = origin + new Vector3(offsetX, offsetY, 0f);
                SpawnLootAt(runner, consumables[i].def, consumables[i].amount, spawnPos);
                count++;
            }
        }

        // Si el jugador local está actualmente Abatido, otorgar 1 Auto-reanimador directamente a su inventario
        // para que pueda probar la canalización de auto-reanimación de inmediato con [F] sin necesidad de recogerlo del piso.
        TryGrantSelfReviverIfDowned(runner);

        Debug.Log($"[LootTestSpawner] Spawned {count} consumables at position {origin}.");
    }

    [ContextMenu("Spawn All Test Items At Player (F8)")]
    public void SpawnAllTestLoot()
    {
        EnsureDependencies();
        var runner = FindActiveRunner();
        if (runner == null || !runner.IsServer)
        {
            Debug.LogWarning("[LootTestSpawner] Cannot spawn loot: Active runner is null or not State Authority/Host.");
            return;
        }

        Vector3 origin = GetPlayerSpawnAnchor();

        var items = new (LootDefinition def, int amount)[]
        {
            (_manaPotion, 2),
            (_manaFragment, 5),
            (_healthPotion, 2),
            (_explosiveBomb, 3),
            (_smokeBomb, 2),
            (_throwingDagger, 5),
            (_lockpick, 5),
            (_selfRevive, 1),
            (_chaosCoin, 1),
            (_pilgrimLight, 1),
            (_sanctuaryHeart, 1),
            (_voidBell, 1),
            (_famineIdol, 1),
            (_wandererEye, 1),
            (_runeCompass, 1),
            (_arcaneDetector, 1),
            (_etherDust, 3)
        };

        float spacing = 1.0f;
        int count = 0;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].def != null)
            {
                float offsetX = ((count % 5) - 2) * spacing;
                float offsetY = -((count / 5) + 1) * spacing;
                Vector3 spawnPos = origin + new Vector3(offsetX, offsetY, 0f);
                SpawnLootAt(runner, items[i].def, items[i].amount, spawnPos);
                count++;
            }
        }

        Debug.Log($"[LootTestSpawner] Spawned {count} test items at position {origin}.");
    }

    public void SpawnManaConsumables()
    {
        EnsureDependencies();
        var runner = FindActiveRunner();
        if (runner == null || !runner.IsServer) return;
        Vector3 origin = GetPlayerSpawnAnchor();
        if (_manaPotion != null) SpawnLootAt(runner, _manaPotion, 2, origin + new Vector3(-0.75f, -1f, 0f));
        if (_manaFragment != null) SpawnLootAt(runner, _manaFragment, 5, origin + new Vector3(0.75f, -1f, 0f));
    }

    public void SpawnHealthConsumable()
    {
        EnsureDependencies();
        var runner = FindActiveRunner();
        if (runner == null || !runner.IsServer) return;
        Vector3 origin = GetPlayerSpawnAnchor();
        if (_healthPotion != null) SpawnLootAt(runner, _healthPotion, 2, origin + new Vector3(0f, -1f, 0f));
    }

    [ContextMenu("Spawn Self-Revive At Player")]
    public void SpawnSelfRevive()
    {
        EnsureDependencies();
        var runner = FindActiveRunner();
        if (runner == null || !runner.IsServer)
        {
            var context = FindAnyObjectByType<ApplicationStashContext>();
            var store = context != null ? context.Store : null;
            if (store != null && store.IsAvailable && _selfRevive != null && _selfRevive.LootId.IsValid)
            {
                store.TryImportItems(new[] { new StashItem(_selfRevive.LootId, 1) });
                Debug.Log("[LootTestSpawner] Imported 1x Auto-reanimador to Town Stash.");
                return;
            }
            return;
        }

        TryGrantSelfReviverIfDowned(runner);
        Vector3 origin = GetPlayerSpawnAnchor();
        if (_selfRevive != null)
        {
            SpawnLootAt(runner, _selfRevive, 1, origin + new Vector3(0f, -1f, 0f));
        }
    }

    [ContextMenu("Spawn Lockpick At Player")]
    public void SpawnLockpick()
    {
        EnsureDependencies();
        var runner = FindActiveRunner();
        if (runner == null || !runner.IsServer) return;
        Vector3 origin = GetPlayerSpawnAnchor();
        if (_lockpick != null)
        {
            SpawnLootAt(runner, _lockpick, 5, origin + new Vector3(0f, -1f, 0f));
        }
    }

    private void TryGrantSelfReviverIfDowned(NetworkRunner runner)
    {
        if (_selfRevive == null || runner == null || !runner.IsServer) return;

        var characters = FindObjectsByType<PlayerCharacter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var pc in characters)
        {
            if (pc != null && pc.Object != null && pc.Object.IsValid && pc.IsDowned)
            {
                if (pc.TryGetComponent(out PlayerDownedRecoveryNetworkController recovery) &&
                    pc.TryGetComponent(out PlayerLootReceiver lootReceiver) &&
                    !recovery.CarriesSelfReviver && !recovery.SelfReviveUsed)
                {
                    var grantReq = new LootTransferRequest(pc.Id, pc.Id, _selfRevive.LootId, 1, runner.Tick);
                    if (lootReceiver.ValidateReceive(grantReq) == LootTransferFailureReason.None)
                    {
                        if (lootReceiver.IsRaidLootOriginAware)
                        {
                            lootReceiver.CommitRaidLootReceive(grantReq, RaidLootOriginTransfer.Dungeon(1));
                        }
                        else
                        {
                            lootReceiver.CommitReceive(grantReq);
                        }
                        Debug.Log($"[LootTestSpawner] Granted 1x Auto-reanimador directly to Downed player {pc.Id}.");
                    }
                }
            }
        }
    }

    public void SpawnTreasures()
    {
        EnsureDependencies();
        var runner = FindActiveRunner();
        if (runner == null || !runner.IsServer) return;
        Vector3 origin = GetPlayerSpawnAnchor();
        var treasures = new LootDefinition[] { _chaosCoin, _pilgrimLight, _sanctuaryHeart, _voidBell, _famineIdol, _wandererEye };
        for (int i = 0; i < treasures.Length; i++)
        {
            if (treasures[i] != null)
            {
                Vector3 pos = origin + new Vector3((i - 2.5f) * 0.9f, -1.2f, 0f);
                SpawnLootAt(runner, treasures[i], 1, pos);
            }
        }
    }

    public void SpawnExpeditionUtilities()
    {
        EnsureDependencies();
        var runner = FindActiveRunner();
        if (runner == null || !runner.IsServer) return;
        Vector3 origin = GetPlayerSpawnAnchor();
        if (_runeCompass != null) SpawnLootAt(runner, _runeCompass, 1, origin + new Vector3(-1.0f, -1.2f, 0f));
        if (_arcaneDetector != null) SpawnLootAt(runner, _arcaneDetector, 1, origin + new Vector3(0f, -1.2f, 0f));
        if (_etherDust != null) SpawnLootAt(runner, _etherDust, 3, origin + new Vector3(1.0f, -1.2f, 0f));
    }

    public void SpawnTestContainer()
    {
        EnsureDependencies();
        var runner = FindActiveRunner();
        if (runner == null || !runner.IsServer) return;
        if (_lootContainerPrefab == null)
        {
            _lootContainerPrefab = Resources.Load<NetworkObject>("Prefabs/LootContainer");
#if UNITY_EDITOR
            if (_lootContainerPrefab == null)
            {
                _lootContainerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<NetworkObject>(
                    "Assets/Prefabs/LootContainer.prefab");
            }
#endif
        }
        if (_lootContainerPrefab == null)
        {
            Debug.LogWarning("[LootTestSpawner] Cannot spawn test container: _lootContainerPrefab is null.");
            return;
        }

        Vector3 spawnPos = GetPlayerSpawnAnchor() + new Vector3(1.5f, 0f, 0f);
        try
        {
            runner.Spawn(_lootContainerPrefab, spawnPos, Quaternion.identity);
            Debug.Log($"[LootTestSpawner] Spawned test container at {spawnPos}.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LootTestSpawner] Failed to spawn container: {ex.Message}");
        }
    }

    public void SpawnCustomItem()
    {
        EnsureDependencies();
        if (CustomItemToSpawn == null)
        {
            Debug.LogWarning("[LootTestSpawner] No CustomItemToSpawn selected.");
            return;
        }

        var runner = FindActiveRunner();
        if (runner == null || !runner.IsServer) return;
        Vector3 origin = GetPlayerSpawnAnchor();
        SpawnLootAt(runner, CustomItemToSpawn, Mathf.Max(1, CustomItemAmount), origin + Vector3.down);
    }

    private void SpawnLootAt(NetworkRunner runner, LootDefinition definition, int amount, Vector3 position)
    {
        if (_pickupPrefab == null || definition == null)
        {
            Debug.LogWarning($"[LootTestSpawner] Cannot spawn {definition?.name ?? "null"}: Missing pickup prefab.");
            return;
        }

        LootEntry entry = new LootEntry(definition.LootId, amount);
        try
        {
            runner.Spawn(
                _pickupPrefab,
                position,
                Quaternion.identity,
                inputAuthority: null,
                onBeforeSpawned: (callbackRunner, instance) =>
                {
                    if (instance != null && instance.TryGetComponent<NetworkLootPickup>(out var pickup))
                    {
                        pickup.TrySetSpawnContentOverride(callbackRunner, instance, entry);
                    }
                });
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LootTestSpawner] Failed to spawn {definition.LootId}: {ex.Message}");
        }
    }

    private Vector3 GetPlayerSpawnAnchor()
    {
        var cameraBinder = FindAnyObjectByType<LocalPlayerCameraBinder>();
        if (cameraBinder != null)
        {
            return cameraBinder.transform.position;
        }

        var characters = FindObjectsByType<PlayerCharacter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var pc in characters)
        {
            if (pc.Object != null && pc.Object.IsValid && pc.HasInputAuthority)
            {
                return pc.transform.position;
            }
        }

        if (characters.Length > 0)
        {
            return characters[0].transform.position;
        }

        return transform.position;
    }

    private NetworkRunner FindActiveRunner()
    {
        return NetworkRunner.GetRunnerForGameObject(gameObject) ?? FindAnyObjectByType<NetworkRunner>();
    }
}
#endif
