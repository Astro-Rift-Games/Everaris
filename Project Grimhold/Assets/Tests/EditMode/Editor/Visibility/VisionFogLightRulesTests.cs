using NUnit.Framework;
using ProjectGrimhold.Gameplay.Visibility;
using UnityEngine;

namespace Tests.EditMode.Visibility
{
    public sealed class VisionFogLightRulesTests
    {
        private static readonly Rect View = new Rect(-10f, -10f, 20f, 20f);

        private static FogLightSource Light(float x, float y, float radius = 5f, float intensity = 1.2f, float visibility = 1f) =>
            new FogLightSource(new Vector2(x, y), radius, intensity, visibility);

        [Test]
        public void Select_SkipsExtinguishedHiddenAndOffscreenLights()
        {
            var output = new Vector4[4];
            FogLightSource[] sources =
            {
                Light(0f, 0f, intensity: 0f),      // extinguished
                Light(1f, 0f, visibility: 0f),     // behind a wall
                Light(40f, 0f),                    // circle far outside the view
                Light(14f, 0f),                    // centre outside, circle reaches the view
                Light(2f, 2f)
            };

            int count = VisionFogLightRules.Select(sources, View, Vector2.zero, 1.2f, output);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(output[0], Is.EqualTo(new Vector4(2f, 2f, 5f, 1f)));
            Assert.That(output[1], Is.EqualTo(new Vector4(14f, 0f, 5f, 1f)));
        }

        [Test]
        public void Select_KeepsTheLightsNearestTheViewerWhenOverCapacity()
        {
            var output = new Vector4[2];
            FogLightSource[] sources = { Light(8f, 0f), Light(-1f, 0f), Light(0f, 6f), Light(3f, 0f) };

            int count = VisionFogLightRules.Select(sources, View, Vector2.zero, 1.2f, output);

            Assert.That(count, Is.EqualTo(2));
            Assert.That((Vector2)output[0], Is.EqualTo(new Vector2(-1f, 0f)));
            Assert.That((Vector2)output[1], Is.EqualTo(new Vector2(3f, 0f)));
        }

        [Test]
        public void Select_StrengthFollowsIntensityAndVisibility_ClampedToOne()
        {
            var output = new Vector4[3];
            FogLightSource[] sources = { Light(0f, 0f, intensity: 0.6f), Light(1f, 0f, intensity: 3f), Light(2f, 0f, visibility: 0.5f) };

            VisionFogLightRules.Select(sources, View, Vector2.zero, 1.2f, output);

            Assert.That(output[0].w, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(output[1].w, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(output[2].w, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Select_ClearsSlotsLeftFromThePreviousFrame()
        {
            var output = new Vector4[3];
            VisionFogLightRules.Select(new[] { Light(0f, 0f), Light(1f, 0f), Light(2f, 0f) }, View, Vector2.zero, 1.2f, output);

            int count = VisionFogLightRules.Select(new[] { Light(0f, 0f) }, View, Vector2.zero, 1.2f, output);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(output[1], Is.EqualTo(Vector4.zero));
            Assert.That(output[2], Is.EqualTo(Vector4.zero));
        }
    }
}
