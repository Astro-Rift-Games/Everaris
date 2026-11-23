using UnityEditor;
using UnityEditor.Tilemaps;

/// <summary>
/// Reuses the package GameObjectBrush editor (paint preview and cell inspector),
/// which is not bound to subclasses by its own CustomEditor attribute.
/// </summary>
[CustomEditor(typeof(PrefabPaletteBrush))]
public sealed class PrefabPaletteBrushEditor : GameObjectBrushEditor
{
}
