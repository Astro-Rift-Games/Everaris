#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Presentation
{
    public sealed class CollapseRiftProceduralComposerTests
    {
        private CollapseRiftRuntimeKit _floorKit;
        private CollapseRiftRuntimeKit _wallKit;

        [SetUp]
        public void SetUp()
        {
            _floorKit = CollapseRiftPlaceholderKitFactory.CreateFloorKit();
            _wallKit = CollapseRiftPlaceholderKitFactory.CreateWallKit();
        }

        [Test]
        public void FloorAndWallComposition_SameSeedAndCell_ProduceByteIdenticalResults()
        {
            int floorSeedA = CollapseRiftComposer.ComputeRiftSeed(12345, new Vector2Int(10, 4), CollapseRiftSurfaceType.Floor);
            int floorSeedB = CollapseRiftComposer.ComputeRiftSeed(12345, new Vector2Int(10, 4), CollapseRiftSurfaceType.Floor);
            Assert.That(floorSeedA, Is.EqualTo(floorSeedB));

            CollapseRiftComposedBitmap floorA = CollapseRiftComposer.ComposeFloorRift(_floorKit, floorSeedA, 48);
            CollapseRiftComposedBitmap floorB = CollapseRiftComposer.ComposeFloorRift(_floorKit, floorSeedB, 48);

            Assert.That(CountOpaque(floorA.Mask), Is.GreaterThan(20), "Floor rift must produce visible pixels.");
            Assert.That(floorA.Mask, Is.EqualTo(floorB.Mask));
            Assert.That(floorA.GrowthOrder, Is.EqualTo(floorB.GrowthOrder));
            Assert.That(floorA.CoreMask, Is.EqualTo(floorB.CoreMask));

            int wallSeedA = CollapseRiftComposer.ComputeRiftSeed(12345, new Vector2Int(10, 14), CollapseRiftSurfaceType.Wall);
            CollapseRiftComposedBitmap wallA = CollapseRiftComposer.ComposeWallRift(_wallKit, wallSeedA, 48, 32);
            CollapseRiftComposedBitmap wallB = CollapseRiftComposer.ComposeWallRift(_wallKit, wallSeedA, 48, 32);

            Assert.That(CountOpaque(wallA.Mask), Is.GreaterThan(20), "Wall rift must produce visible pixels.");
            Assert.That(wallA.Mask, Is.EqualTo(wallB.Mask));
            Assert.That(wallA.GrowthOrder, Is.EqualTo(wallB.GrowthOrder));
            Assert.That(wallA.CoreMask, Is.EqualTo(wallB.CoreMask));
        }

        [Test]
        public void FloorAndWallComposition_DifferentSeedsProduceDifferentSilhouettes()
        {
            int seed1 = CollapseRiftComposer.ComputeRiftSeed(100, new Vector2Int(1, 2), CollapseRiftSurfaceType.Floor);
            int seed2 = CollapseRiftComposer.ComputeRiftSeed(100, new Vector2Int(7, 9), CollapseRiftSurfaceType.Floor);
            Assert.That(seed1, Is.Not.EqualTo(seed2));

            CollapseRiftComposedBitmap f1 = CollapseRiftComposer.ComposeFloorRift(_floorKit, seed1, 48);
            CollapseRiftComposedBitmap f2 = CollapseRiftComposer.ComposeFloorRift(_floorKit, seed2, 48);
            Assert.That(MasksDiffer(f1.Mask, f2.Mask), Is.True, "Different seeds/cells must create distinct floor silhouettes.");

            int wSeed1 = CollapseRiftComposer.ComputeRiftSeed(200, new Vector2Int(3, 14), CollapseRiftSurfaceType.Wall);
            int wSeed2 = CollapseRiftComposer.ComputeRiftSeed(200, new Vector2Int(9, 14), CollapseRiftSurfaceType.Wall);
            CollapseRiftComposedBitmap w1 = CollapseRiftComposer.ComposeWallRift(_wallKit, wSeed1, 48, 32);
            CollapseRiftComposedBitmap w2 = CollapseRiftComposer.ComposeWallRift(_wallKit, wSeed2, 48, 32);
            Assert.That(MasksDiffer(w1.Mask, w2.Mask), Is.True, "Different seeds/cells must create distinct wall silhouettes.");
        }

        [Test]
        public void BfsGrowthOrder_IsZeroAtOrigin_AndIncreasesAlongBranches()
        {
            const int size = 16;
            var mask = new bool[size * size];
            // Horizontal branch from x=2..13 at y=8
            for (int x = 2; x <= 13; x++)
            {
                mask[8 * size + x] = true;
            }

            var origin = new Vector2Int(2, 8);
            byte[] order = CollapseRiftBfsGrowth.ComputeGrowthOrder(mask, size, origin);

            Assert.That(order[origin.y * size + origin.x], Is.EqualTo(0));
            for (int x = 3; x <= 13; x++)
            {
                Assert.That(order[8 * size + x], Is.GreaterThanOrEqualTo(order[8 * size + (x - 1)]));
            }
            Assert.That(order[8 * size + 13], Is.EqualTo(255));
        }

        [Test]
        public void WallRift_RespectsWallFaceHeightBounds_AndGrowsBottomToTop()
        {
            const int maxWallHeightPx = 32; // 2 cells * 16 PPU
            for (int i = 0; i < 16; i++)
            {
                int seed = CollapseRiftComposer.ComputeRiftSeed(9000 + i, new Vector2Int(i * 3, 14), CollapseRiftSurfaceType.Wall);
                CollapseRiftComposedBitmap wall = CollapseRiftComposer.ComposeWallRift(_wallKit, seed, 48, maxWallHeightPx);

                Assert.That(CountOpaque(wall.Mask), Is.GreaterThan(15));
                Assert.That(wall.MaxOccupiedY, Is.LessThan(maxWallHeightPx), "Wall rift must never exceed WallFaceHeightCells.");

                // Verify zero opaque pixels exist at y >= maxWallHeightPx
                for (int y = maxWallHeightPx; y < wall.Size; y++)
                {
                    for (int x = 0; x < wall.Size; x++)
                    {
                        Assert.That(wall.Mask[y * wall.Size + x], Is.False);
                    }
                }

                // Origin starts at bottom base and growth increases upward
                Assert.That(wall.Origin.y, Is.LessThanOrEqualTo(4));
                Assert.That(wall.GrowthOrder[wall.Origin.y * wall.Size + wall.Origin.x], Is.EqualTo(0));
            }
        }

        [Test]
        public void FloorRift_StaysStrictlyInsideCellBounds_WithCoreAndEdgeSeparation()
        {
            for (int i = 0; i < 16; i++)
            {
                int seed = CollapseRiftComposer.ComputeRiftSeed(5000 + i, new Vector2Int(i * 2, i), CollapseRiftSurfaceType.Floor);
                CollapseRiftComposedBitmap floor = CollapseRiftComposer.ComposeFloorRift(_floorKit, seed, 48);

                int opaque = 0;
                int coreCount = 0;
                int edgeCount = 0;
                int maxOrder = 0;
                for (int idx = 0; idx < floor.Mask.Length; idx++)
                {
                    if (!floor.Mask[idx]) continue;
                    opaque++;
                    if (floor.CoreMask[idx]) coreCount++;
                    else edgeCount++;
                    if (floor.GrowthOrder[idx] > maxOrder) maxOrder = floor.GrowthOrder[idx];
                }

                Assert.That(opaque, Is.GreaterThan(20));
                Assert.That(coreCount, Is.GreaterThan(0), "Rift must retain interior core pixels.");
                Assert.That(edgeCount, Is.GreaterThan(0), "Rift must retain outer edge pixels.");
                Assert.That(maxOrder, Is.GreaterThan(128), "BFS growth must span outward to branch tips.");
                Assert.That(floor.GrowthOrder[floor.Origin.y * floor.Size + floor.Origin.x], Is.EqualTo(0));
            }
        }

        private static int CountOpaque(bool[] mask)
        {
            int count = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i]) count++;
            }
            return count;
        }

        private static bool MasksDiffer(bool[] a, bool[] b)
        {
            if (a.Length != b.Length) return true;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return true;
            }
            return false;
        }
    }
}
#endif
