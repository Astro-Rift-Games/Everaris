using System.Collections.Generic;
using UnityEngine;

namespace ProjectGrimhold.Gameplay.Visibility
{
    /// <summary>
    /// Chooses the bounded set of lights the fog shader receives. Only lit lights whose source the
    /// viewer can see, and whose radius reaches the view, count; the nearest to the viewer win.
    /// </summary>
    public static class VisionFogLightRules
    {
        /// <summary>Must match MAX_FOG_LIGHTS in VisionFog.shader.</summary>
        public const int MaxLights = 16;

        /// <summary>
        /// Writes up to <c>output.Length</c> lights as (x, y, radius, strength), nearest to <paramref name="origin"/> first,
        /// zeroes the remaining slots and returns how many were written. Allocation-free.
        /// </summary>
        public static int Select(IReadOnlyList<FogLightSource> sources, Rect view, Vector2 origin,
            float referenceIntensity, Vector4[] output)
        {
            int capacity = output.Length;
            int count = 0;
            float reference = Mathf.Max(0.01f, referenceIntensity);
            for (int i = 0; i < sources.Count; i++)
            {
                FogLightSource source = sources[i];
                float strength = Mathf.Clamp01(source.Intensity / reference) * Mathf.Clamp01(source.Visibility);
                if (source.Radius <= 0f || strength <= 0f || !Reaches(view, source.Position, source.Radius))
                {
                    continue;
                }

                float distance = (source.Position - origin).sqrMagnitude;
                int slot = count;
                while (slot > 0 && DistanceSquared(output[slot - 1], origin) > distance)
                {
                    slot--;
                }

                if (slot >= capacity)
                {
                    continue;
                }

                int last = Mathf.Min(count, capacity - 1);
                for (int j = last; j > slot; j--)
                {
                    output[j] = output[j - 1];
                }

                output[slot] = new Vector4(source.Position.x, source.Position.y, source.Radius, strength);
                count = Mathf.Min(count + 1, capacity);
            }

            for (int i = count; i < capacity; i++)
            {
                output[i] = Vector4.zero;
            }

            return count;
        }

        private static bool Reaches(Rect view, Vector2 position, float radius)
        {
            float x = Mathf.Clamp(position.x, view.xMin, view.xMax);
            float y = Mathf.Clamp(position.y, view.yMin, view.yMax);
            float dx = position.x - x;
            float dy = position.y - y;
            return dx * dx + dy * dy <= radius * radius;
        }

        private static float DistanceSquared(Vector4 light, Vector2 origin)
        {
            float dx = light.x - origin.x;
            float dy = light.y - origin.y;
            return dx * dx + dy * dy;
        }
    }
}
