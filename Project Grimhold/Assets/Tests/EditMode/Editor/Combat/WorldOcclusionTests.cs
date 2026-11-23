using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Combat
{
    /// <summary>Walls and closed doors (WorldCollision) stop every attack, area effect and thrown object.</summary>
    public sealed class WorldOcclusionTests
    {
        private readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _objects)
                if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        private void CreateBox(Vector2 center, Vector2 size, string layer)
        {
            var go = new GameObject(layer) { layer = LayerMask.NameToLayer(layer) };
            go.transform.position = center;
            go.AddComponent<BoxCollider2D>().size = size;
            _objects.Add(go);
            Physics2D.SyncTransforms();
        }

        [Test]
        public void Mask_IsTheWorldCollisionLayer()
        {
            Assert.That(WorldOcclusion.Mask, Is.EqualTo(LayerMask.GetMask("WorldCollision")));
        }

        [Test]
        public void IsBlocked_WhenAWallOrClosedDoorCrossesTheLine()
        {
            CreateBox(new Vector2(1f, 0f), new Vector2(0.2f, 4f), "WorldCollision");

            Assert.That(WorldOcclusion.IsBlocked(Vector2.zero, new Vector2(2f, 0f)), Is.True);
            Assert.That(WorldOcclusion.IsBlocked(new Vector2(2f, 0f), Vector2.zero), Is.True, "Either direction.");
        }

        [Test]
        public void IsNotBlocked_ByAClearLineOrByNonWorldColliders()
        {
            CreateBox(new Vector2(1f, 3f), new Vector2(0.2f, 1f), "WorldCollision"); // off the line
            CreateBox(new Vector2(1f, 0f), new Vector2(0.2f, 4f), "Default");        // a body, not a wall

            Assert.That(WorldOcclusion.IsBlocked(Vector2.zero, new Vector2(2f, 0f)), Is.False);
        }

        [Test]
        public void ClampToFirstObstacle_StopsJustBeforeTheFirstWall()
        {
            CreateBox(new Vector2(3f, 0f), new Vector2(0.2f, 4f), "WorldCollision");

            Vector2 landing = WorldOcclusion.ClampToFirstObstacle(Vector2.zero, new Vector2(6f, 0f));

            Assert.That(landing.x, Is.LessThan(2.9f), "The landing stays on the near side of the wall.");
            Assert.That(landing.x, Is.GreaterThan(2.7f), "It lands against the wall, not short of it.");
            Assert.That(landing.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(WorldOcclusion.IsBlocked(Vector2.zero, landing), Is.False);
        }

        [Test]
        public void ClampToFirstObstacle_KeepsTheTargetWhenTheWayIsClear()
        {
            Assert.That(WorldOcclusion.ClampToFirstObstacle(Vector2.zero, new Vector2(6f, 0f)), Is.EqualTo(new Vector2(6f, 0f)));
        }
    }
}
