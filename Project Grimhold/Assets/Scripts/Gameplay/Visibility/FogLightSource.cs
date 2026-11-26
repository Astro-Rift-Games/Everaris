using UnityEngine;

namespace ProjectGrimhold.Gameplay.Visibility
{
    /// <summary>A light that may thin the vision fog: world position, outer radius, current intensity and how visible its source is (0..1).</summary>
    public readonly struct FogLightSource
    {
        public FogLightSource(Vector2 position, float radius, float intensity, float visibility)
        {
            Position = position;
            Radius = radius;
            Intensity = intensity;
            Visibility = visibility;
        }

        public Vector2 Position { get; }
        public float Radius { get; }
        public float Intensity { get; }
        public float Visibility { get; }
    }
}
