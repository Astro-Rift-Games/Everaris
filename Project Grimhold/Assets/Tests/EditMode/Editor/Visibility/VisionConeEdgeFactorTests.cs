using NUnit.Framework;
using ProjectGrimhold.Gameplay.Visibility;
using UnityEngine;

namespace Tests.EditMode.Visibility
{
    /// <summary>
    /// Dynamic entities fade across a soft band just inside the cone edge instead of popping:
    /// fully visible before the band, transparent as they approach the edge, gone on it. Nothing
    /// outside the cone (and so outside the fog's clear shape) is ever shown.
    /// </summary>
    public sealed class VisionConeEdgeFactorTests
    {
        private const float NearRadius = 2f;
        private const float HalfAngle = 45f;
        private const float Soft = 20f;

        private static Vector2 AtAngle(float degrees, float distance = 10f)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)) * distance; // facing is up
        }

        private static float Factor(Vector2 point, float halfAngle = HalfAngle, float soft = Soft) =>
            VisionConeRules.EdgeFactor(Vector2.zero, Vector2.up, point, NearRadius, halfAngle, soft);

        [Test]
        public void InsideTheNearRadius_IsFullyVisibleAtAnyAngle()
        {
            Assert.That(Factor(AtAngle(180f, distance: 1.5f)), Is.EqualTo(1f));
        }

        [Test]
        public void BeforeTheBand_IsFullyVisible()
        {
            Assert.That(Factor(AtAngle(0f)), Is.EqualTo(1f));
            Assert.That(Factor(AtAngle(HalfAngle - Soft)), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void ApproachingTheEdge_BecomesTransparent()
        {
            Assert.That(Factor(AtAngle(HalfAngle - Soft * 0.5f)), Is.EqualTo(0.5f).Within(1e-3f));
        }

        [Test]
        public void OnAndBeyondTheEdge_IsHidden()
        {
            Assert.That(Factor(AtAngle(HalfAngle)), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(Factor(AtAngle(HalfAngle + 5f)), Is.EqualTo(0f));
        }

        [Test]
        public void ZeroSoftness_KeepsTheBinaryCone()
        {
            Assert.That(Factor(AtAngle(HalfAngle - 1f), soft: 0f), Is.EqualTo(1f));
            Assert.That(Factor(AtAngle(HalfAngle + 1f), soft: 0f), Is.EqualTo(0f));
        }

        [Test]
        public void FullCircleCone_StaysOpaqueBehindTheViewer()
        {
            Assert.That(Factor(AtAngle(180f), halfAngle: 180f), Is.EqualTo(1f));
        }

        [Test]
        public void ConeNarrowerThanTheBand_IsStillOpaqueOnItsAxis()
        {
            Assert.That(Factor(AtAngle(0f), halfAngle: 8f), Is.EqualTo(1f));
            Assert.That(Factor(AtAngle(4f), halfAngle: 8f), Is.EqualTo(0.5f).Within(1e-3f));
        }

        [Test]
        public void Query_NeverSeesPastTheConeEdge_AndFadesInsideIt()
        {
            var query = new VisionQuery(Vector2.zero, Vector2.up, NearRadius, VisionConeRules.CosineOfHalfAngle(HalfAngle * 2f),
                0, new RaycastHit2D[4], null, VisibilityFadeTimings.Instant, 1f, Soft);
            Vector2 insideBand = AtAngle(HalfAngle - Soft * 0.25f);

            Assert.That(query.IsPointVisible(insideBand, null, requiresCone: true), Is.True);
            Assert.That(query.ConeEdgeFactor(insideBand), Is.EqualTo(0.25f).Within(1e-3f));
            Assert.That(query.IsPointVisible(AtAngle(HalfAngle + 2f), null, requiresCone: true), Is.False);
        }
    }
}
