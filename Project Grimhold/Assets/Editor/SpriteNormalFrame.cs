using System;
using UnityEngine;

internal static class SpriteNormalFrame
{
    // Pixels belong to one slice, ordered from the bottom-left as in Texture2D.
    internal static Color32[] Generate(Color32[] pixels, int width, int height,
        float relief, float detail, int smoothing, bool repeat)
    {
        if (width < 1 || height < 1 || pixels == null || (long)width * height != pixels.Length)
            throw new ArgumentException("Expected one complete frame.", nameof(pixels));

        float[] heights = new float[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 p = pixels[i];
            float luminance = (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) / 255f;
            heights[i] = p.a / 255f * (relief + detail * luminance);
        }

        float Sample(int x, int y)
        {
            if (repeat)
                return heights[((y + height) % height) * width + (x + width) % width];
            return x < 0 || x >= width || y < 0 || y >= height ? 0f : heights[y * width + x];
        }

        float[] buffer = smoothing > 0 ? new float[pixels.Length] : null;
        for (int pass = 0; pass < smoothing; pass++)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                buffer[y * width + x] = (4f * Sample(x, y) + Sample(x - 1, y) + Sample(x + 1, y)
                    + Sample(x, y - 1) + Sample(x, y + 1)) / 8f;
            (heights, buffer) = (buffer, heights);
        }

        var normals = new Color32[pixels.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            Vector3 normal = pixels[i].a == 0 ? Vector3.forward : new Vector3(
                Sample(x - 1, y) - Sample(x + 1, y), Sample(x, y - 1) - Sample(x, y + 1), 2f).normalized;
            normals[i] = new Color(0.5f + 0.5f * normal.x, 0.5f + 0.5f * normal.y,
                0.5f + 0.5f * normal.z, 1f);
        }
        return normals;
    }
}
