using UnityEditor.Tilemaps;
using UnityEngine;

/// <summary>
/// GameObjectBrush whose asset instances are listed in the Tile Palette brush dropdown.
/// The package's GameObjectBrush hides its own assets, so preconfigured prefab brushes
/// (for example TorchBrush) need this subclass to be selectable.
/// The package also empties the brush when a pick or move finds no prefab (clicking the
/// palette grid, picking an empty scene cell, finishing a move); this brush keeps its
/// authored configuration in those cases.
/// </summary>
[CustomGridBrush(false, true, false, "Prefab Brush")]
[CreateAssetMenu(fileName = "New Prefab Brush", menuName = "2D/Brushes/Prefab Palette Brush")]
public sealed class PrefabPaletteBrush : GameObjectBrush
{
    private Snapshot _beforeMove;

    public override void Pick(GridLayout gridLayout, GameObject brushTarget, BoundsInt position, Vector3Int pivot)
    {
        Snapshot authored = Snapshot.Capture(this);
        base.Pick(gridLayout, brushTarget, position, pivot);
        authored.RestoreIfEmptied(this);
    }

    public override void MoveStart(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
    {
        _beforeMove = Snapshot.Capture(this);
        base.MoveStart(gridLayout, brushTarget, position);
    }

    public override void MoveEnd(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
    {
        base.MoveEnd(gridLayout, brushTarget, position);
        _beforeMove?.RestoreIfEmptied(this);
        _beforeMove = null;
    }

    private static bool HasAnyPrefab(GameObjectBrush brush)
    {
        for (int i = 0; i < brush.cellCount; i++)
        {
            if (brush.cells[i].gameObject != null)
                return true;
        }

        return false;
    }

    private sealed class Snapshot
    {
        private readonly Vector3Int _size;
        private readonly Vector3Int _pivot;
        private readonly GameObject[] _gameObjects;
        private readonly Vector3[] _offsets;
        private readonly Vector3[] _scales;
        private readonly Quaternion[] _orientations;

        private Snapshot(GameObjectBrush brush)
        {
            _size = brush.size;
            _pivot = brush.pivot;
            int count = brush.cellCount;
            _gameObjects = new GameObject[count];
            _offsets = new Vector3[count];
            _scales = new Vector3[count];
            _orientations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                BrushCell cell = brush.cells[i];
                _gameObjects[i] = cell.gameObject;
                _offsets[i] = cell.offset;
                _scales[i] = cell.scale;
                _orientations[i] = cell.orientation;
            }
        }

        public static Snapshot Capture(GameObjectBrush brush) => new(brush);

        public void RestoreIfEmptied(GameObjectBrush brush)
        {
            if (HasAnyPrefab(brush) || System.Array.TrueForAll(_gameObjects, go => go == null))
                return;

            brush.Init(_size, _pivot);
            for (int i = 0; i < brush.cellCount && i < _gameObjects.Length; i++)
            {
                BrushCell cell = brush.cells[i];
                cell.gameObject = _gameObjects[i];
                cell.offset = _offsets[i];
                cell.scale = _scales[i];
                cell.orientation = _orientations[i];
            }
        }
    }
}
