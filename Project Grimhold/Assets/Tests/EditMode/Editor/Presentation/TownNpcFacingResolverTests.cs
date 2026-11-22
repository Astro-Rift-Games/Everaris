#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Presentation
{
    /// <summary>
    /// Town NPCs have no side-on animation, so someone standing beside them must be met with a front-facing pose
    /// (South, SouthEast, SouthWest). The back-facing poses are kept for someone clearly behind the NPC.
    /// </summary>
    public sealed class TownNpcFacingResolverTests
    {
        private const CharacterVisualDirection S = CharacterVisualDirection.South;
        private const CharacterVisualDirection SE = CharacterVisualDirection.SouthEast;
        private const CharacterVisualDirection SW = CharacterVisualDirection.SouthWest;
        private const CharacterVisualDirection N = CharacterVisualDirection.North;
        private const CharacterVisualDirection NE = CharacterVisualDirection.NorthEast;
        private const CharacterVisualDirection NW = CharacterVisualDirection.NorthWest;

        [TestCase(1f, 0f, SE)]
        [TestCase(1f, 0.2f, SE)]
        [TestCase(1f, 0.5f, SE)]
        [TestCase(-1f, 0f, SW)]
        [TestCase(-1f, 0.2f, SW)]
        [TestCase(-1f, 0.5f, SW)]
        public void SomeoneBesideTheNpc_IsMetWithAFrontFacingPose(float x, float y, CharacterVisualDirection expected)
        {
            Assert.That(TownNpcFacingResolver.Resolve(new Vector2(x, y), S), Is.EqualTo(expected));
        }

        [TestCase(0f, -1f, S)]
        [TestCase(1f, -1f, SE)]
        [TestCase(-1f, -1f, SW)]
        [TestCase(0f, 1f, N)]
        [TestCase(1f, 1f, NE)]
        [TestCase(-1f, 1f, NW)]
        public void SomeoneClearlyInFrontOrBehind_GetsTheMatchingPose(float x, float y, CharacterVisualDirection expected)
        {
            Assert.That(TownNpcFacingResolver.Resolve(new Vector2(x, y), S), Is.EqualTo(expected));
        }

        [Test]
        public void NearTheFrontBackLimit_TheNpcKeepsThePoseItAlreadyShows()
        {
            // 27 degrees above the NPC's side: between the limits for turning its back and for facing front again.
            Vector2 direction = FromElevation(27f, 1f);

            Assert.That(TownNpcFacingResolver.Resolve(direction, SE), Is.EqualTo(SE));
            Assert.That(TownNpcFacingResolver.Resolve(direction, NE), Is.EqualTo(NE));
        }

        [Test]
        public void NearTheCentreSideLimit_TheNpcKeepsThePoseItAlreadyShows()
        {
            // 23 degrees off straight below: between the limits for the centre pose and the diagonal one.
            Vector2 direction = FromElevation(-67f, 1f);

            Assert.That(TownNpcFacingResolver.Resolve(direction, S), Is.EqualTo(S));
            Assert.That(TownNpcFacingResolver.Resolve(direction, SE), Is.EqualTo(SE));
        }

        [Test]
        public void MovingWellPastALimit_TurnsTheNpc()
        {
            Assert.That(TownNpcFacingResolver.Resolve(FromElevation(50f, 1f), SE), Is.EqualTo(NE));
            Assert.That(TownNpcFacingResolver.Resolve(FromElevation(5f, 1f), NE), Is.EqualTo(SE));
            Assert.That(TownNpcFacingResolver.Resolve(FromElevation(-40f, 1f), S), Is.EqualTo(SE));
            Assert.That(TownNpcFacingResolver.Resolve(FromElevation(-85f, 1f), SE), Is.EqualTo(S));
        }

        [Test]
        public void SomeoneStandingOnTheNpc_LeavesItsPoseUntouched()
        {
            Assert.That(TownNpcFacingResolver.Resolve(Vector2.zero, NW), Is.EqualTo(NW));
            Assert.That(TownNpcFacingResolver.Resolve(new Vector2(float.NaN, 1f), SE), Is.EqualTo(SE));
        }

        [Test]
        public void DirectionalView_FacesFrontTowardsSomeoneStandingBesideIt()
        {
            var npc = new GameObject("NPC");
            try
            {
                TownNpcDirectionalView view = npc.AddComponent<TownNpcDirectionalView>();
                view.RestoreInitialFacing();

                view.FaceTarget(npc.transform.position + new Vector3(1f, 0.2f, 0f));

                Assert.That(view.CurrentFacing, Is.EqualTo(SE));
            }
            finally
            {
                Object.DestroyImmediate(npc);
            }
        }

        /// <summary>Direction at <paramref name="elevationDegrees"/> above the horizontal, on the given side.</summary>
        private static Vector2 FromElevation(float elevationDegrees, float side)
        {
            float radians = elevationDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians) * side, Mathf.Sin(radians));
        }
    }
}
#endif
