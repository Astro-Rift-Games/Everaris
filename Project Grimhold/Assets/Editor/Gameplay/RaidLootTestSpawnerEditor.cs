using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RaidLootTestSpawner))]
public sealed class RaidLootTestSpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var spawner = (RaidLootTestSpawner)target;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("--- PlayMode Test Loot Spawning ---", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Entra a PlayMode en una raid (Gameplay) para spawnear loot interactivamente con los botones de abajo o presionando F8.", MessageType.Info);
            return;
        }

        GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
        if (GUILayout.Button("★ Spawn ALL Consumables (F6) ★", GUILayout.Height(36)))
        {
            spawner.SpawnAllConsumables();
        }
        GUI.backgroundColor = new Color(0.4f, 0.9f, 0.4f);
        if (GUILayout.Button("Spawn ALL Test Items (F8)", GUILayout.Height(30)))
        {
            spawner.SpawnAllTestLoot();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Spawn Mana (2x Potion, 5x Fragment)", GUILayout.Height(28)))
        {
            spawner.SpawnManaConsumables();
        }
        if (GUILayout.Button("Spawn Health Potion (2x)", GUILayout.Height(28)))
        {
            spawner.SpawnHealthConsumable();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Spawn Self-Reviver (1x)", GUILayout.Height(28)))
        {
            spawner.SpawnSelfRevive();
        }
        if (GUILayout.Button("Spawn Lockpick (5x)", GUILayout.Height(28)))
        {
            spawner.SpawnLockpick();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Spawn All 6 MVP Treasures", GUILayout.Height(28)))
        {
            spawner.SpawnTreasures();
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Spawn Utilities (Compass, Detector, Ether)", GUILayout.Height(28)))
        {
            spawner.SpawnExpeditionUtilities();
        }
        if (GUILayout.Button("Spawn Test Chest (Loot Container)", GUILayout.Height(28)))
        {
            spawner.SpawnTestContainer();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);
        if (spawner.CustomItemToSpawn != null)
        {
            if (GUILayout.Button($"Spawn Custom: {spawner.CustomItemToSpawn.DisplayName} (x{spawner.CustomItemAmount})", GUILayout.Height(28)))
            {
                spawner.SpawnCustomItem();
            }
        }
    }
}
