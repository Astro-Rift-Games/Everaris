using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Builds authored EncounterRoom components through serialized properties, as the Inspector would.</summary>
internal sealed class EncounterRoomFactory
{
    private readonly List<GameObject> _created = new List<GameObject>();

    public void Destroy()
    {
        foreach (GameObject go in _created)
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        _created.Clear();
    }

    public Transform[] Markers(int count, string prefix = "Marker")
    {
        var root = Track(new GameObject(prefix + "Root"));
        var markers = new Transform[count];
        for (int i = 0; i < count; i++)
        {
            var marker = new GameObject($"{prefix}_{i + 1}");
            marker.transform.SetParent(root.transform);
            marker.transform.position = new Vector3(i, 0f, 0f);
            markers[i] = marker.transform;
        }

        return markers;
    }

    public EncounterRoom Room(int id, Transform[] spawnPoints, bool withZone = true, int min = 1, int max = 2)
    {
        GameObject go = Track(new GameObject("Room_" + id));
        var room = go.AddComponent<EncounterRoom>();
        Collider2D zone = withZone ? go.AddComponent<BoxCollider2D>() : null;
        var serialized = new SerializedObject(room);
        serialized.FindProperty("_encounterId").intValue = id;
        serialized.FindProperty("_generationZone").objectReferenceValue = zone;
        SerializedProperty points = serialized.FindProperty("_spawnPoints");
        points.arraySize = spawnPoints.Length;
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            points.GetArrayElementAtIndex(i).objectReferenceValue = spawnPoints[i];
        }

        serialized.FindProperty("_composition._minEnemies").intValue = min;
        serialized.FindProperty("_composition._maxEnemies").intValue = max;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return room;
    }

    public static void Set(EncounterRoom room, string path, int value) => Apply(room, path, p => p.intValue = value);

    public static void Set(EncounterRoom room, string path, float value) => Apply(room, path, p => p.floatValue = value);

    public static void SetPoint(EncounterRoom room, int index, Transform value)
    {
        Apply(room, "_spawnPoints", p => p.GetArrayElementAtIndex(index).objectReferenceValue = value);
    }

    private static void Apply(EncounterRoom room, string path, System.Action<SerializedProperty> write)
    {
        var serialized = new SerializedObject(room);
        write(serialized.FindProperty(path));
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private GameObject Track(GameObject go)
    {
        _created.Add(go);
        return go;
    }
}
