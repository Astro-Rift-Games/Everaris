using NUnit.Framework;
using UnityEngine;

public sealed class SpriteNormalFrameTests
{
    [Test]
    public void Generate_LuminanceRisesUpward_NormalTiltsTowardNegativeY()
    {
        var pixels = new Color32[9];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte value = (byte)(100 * (i / 3));
            pixels[i] = new Color32(value, value, value, 255);
        }

        Color32[] result = SpriteNormalFrame.Generate(pixels, 3, 3, 0f, 1f, 0, false);
        Assert.That(result.Length, Is.EqualTo(9));
        Assert.That(result[4].r, Is.EqualTo(128).Within(1));
        Assert.That(result[4].g, Is.LessThan(127));
        Assert.That(result[4].b, Is.GreaterThan(128));
    }
}
