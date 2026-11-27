#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.Presentation
{
    /// <summary>
    /// FIX-107: across real player-loop frames, moving the player (and the HUD it carries) while
    /// the map pans must not rebuild the static tile-art meshes of the minimap.
    /// </summary>
    public sealed class RaidMinimapMeshRebuildPlayModeTests
    {
        private const string HudPrefabPath = "Assets/Prefabs/UI/PlayerUI/LocalGameplayHud.prefab";
        private const string CatacombsLayoutPath = "Assets/Data/Minimap/CatacombsMinimapLayout.asset";
        private const int MovingFrames = 30;

        private GameObject _player;

        [TearDown]
        public void TearDown()
        {
            if (_player != null)
                Object.Destroy(_player);
        }

        [UnityTest]
        public IEnumerator MovingPlayer_DoesNotRebuildTileArtMeshesEveryFrame()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            MinimapLayout layout = AssetDatabase.LoadAssetAtPath<MinimapLayout>(CatacombsLayoutPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(layout, Is.Not.Null);
            _player = new GameObject("MovingPlayer");
            Object.Instantiate(prefab, _player.transform, false);
            RaidMinimapView view = _player.GetComponentInChildren<RaidMinimapView>(true);
            // The defect only appears on the production pixel-perfect HUD Canvas (FIX-107 diagnosis).
            Assert.That(view.GetComponentInParent<Canvas>().rootCanvas.pixelPerfect, Is.True);
            Assert.That(view.TryConfigureMap(layout, 4f, 1f, out string error), Is.True, error);
            view.ShowMinimap();
            view.PresentMapPosition(Vector2.zero);
            yield return null;
            yield return null;

            var mapGraphics = new List<Graphic>();
            view.transform.Find("Viewport/Map").GetComponentsInChildren(true, mapGraphics);
            Assert.That(mapGraphics.Count, Is.GreaterThan(1), "The Catacombs layout must produce continuation batches.");
            int dirtyFrames = 0;
            bool dirtyThisFrame = false;
            foreach (Graphic graphic in mapGraphics)
                graphic.RegisterDirtyVerticesCallback(() => dirtyThisFrame = true);

            for (int frame = 1; frame <= MovingFrames; frame++)
            {
                dirtyThisFrame = false;
                Vector2 world = new Vector2(0.137f * frame, -0.091f * frame);
                _player.transform.position = world;
                // Same mapping the presenter applies: the map moves opposite to the player.
                view.PresentMapPosition(-world * 4f);
                yield return null;
                if (dirtyThisFrame)
                    dirtyFrames++;
            }

            Assert.That(dirtyFrames, Is.Zero,
                $"Tile-art meshes were rebuilt in {dirtyFrames} of {MovingFrames} moving frames.");
        }
    }
}
#endif
