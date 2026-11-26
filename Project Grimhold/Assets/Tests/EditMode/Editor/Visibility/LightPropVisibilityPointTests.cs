using System.Linq;
using NUnit.Framework;
using ProjectGrimhold.Gameplay.Visibility;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Tests.EditMode.Visibility
{
    /// <summary>
    /// Light props are hidden behind walls by occlusion. A wall-mounted torch samples its
    /// visibility on the floor in front of its wall; a point inside the wall would be
    /// occluded by that same wall and the torch would never show.
    /// </summary>
    public sealed class LightPropVisibilityPointTests
    {
        private const string ScenePath = "Assets/Scenes/CatacombsMap.unity";
        private static readonly string[] SolidTilemaps = { "Walls", "SolidWalls", "Obstacles", "ArchCollision" };
        private Scene _scene;

        [SetUp]
        public void OpenSceneReadOnly() => _scene = EditorSceneManager.OpenPreviewScene(ScenePath);

        [TearDown]
        public void CloseWithoutSaving()
        {
            if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
        }

        [Test]
        public void EveryCatacombsLightProp_SamplesVisibilityOutsideSolidTiles()
        {
            GameObject[] roots = _scene.GetRootGameObjects();
            Tilemap[] solids = roots.SelectMany(r => r.GetComponentsInChildren<Tilemap>(true))
                .Where(map => SolidTilemaps.Contains(map.name)).ToArray();
            DungeonLightProp[] props = roots.SelectMany(r => r.GetComponentsInChildren<DungeonLightProp>(true)).ToArray();
            Assert.That(solids, Is.Not.Empty);
            Assert.That(props, Is.Not.Empty);

            foreach (DungeonLightProp prop in props)
            {
                EntityVisibilityPresenter presenter = prop.GetComponent<EntityVisibilityPresenter>();
                Assert.That(presenter, Is.Not.Null, $"{prop.name} at {prop.transform.position} has no visibility presenter.");
                Vector3 point = presenter.VisibilityPoint;
                foreach (Tilemap solid in solids)
                {
                    Assert.That(solid.HasTile(solid.WorldToCell(point)), Is.False,
                        $"{prop.name} at {prop.transform.position} samples visibility inside {solid.name} at {point}.");
                }
            }
        }
    }
}
