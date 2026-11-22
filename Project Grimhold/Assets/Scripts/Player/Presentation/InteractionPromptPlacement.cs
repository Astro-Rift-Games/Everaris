using UnityEngine;

/// <summary>
/// Pure placement rules for the local interaction prompt, which hangs above its interactable instead of
/// sitting at a fixed HUD position. Presentation only: it never reads or changes gameplay state.
/// </summary>
public static class InteractionPromptPlacement
{
    /// <summary>
    /// Resolves the world point the prompt hangs from: the explicit anchor when the prefab provides one, else the
    /// top-centre of the visible sprite artwork, else the target's own position.
    /// Reading sprite meshes allocates, so resolve this when the target changes, never per frame.
    /// </summary>
    public static bool TryResolveWorldAnchor(
        Transform explicitAnchor,
        SpriteRenderer[] renderers,
        Transform fallback,
        out Vector3 worldAnchor)
    {
        if (explicitAnchor != null)
        {
            worldAnchor = explicitAnchor.position;
            return true;
        }

        if (TryMeasureVisibleArtwork(renderers, out Vector3 min, out Vector3 max))
        {
            worldAnchor = new Vector3((min.x + max.x) * 0.5f, max.y, (min.z + max.z) * 0.5f);
            return true;
        }

        if (fallback != null)
        {
            worldAnchor = fallback.position;
            return true;
        }

        worldAnchor = default;
        return false;
    }

    /// <summary>
    /// Resolves where the prompt's pivot goes on screen so its bottom edge rests <paramref name="gap"/> pixels
    /// above the anchor, horizontally centred, fully inside the screen margin and with its corner on whole pixels.
    /// </summary>
    public static Vector2 ResolveScreenPosition(
        Vector2 anchorScreenPoint,
        Vector2 promptSize,
        Vector2 pivot,
        float gap,
        Vector2 screenSize,
        float margin)
    {
        float left = anchorScreenPoint.x - (promptSize.x * 0.5f);
        float bottom = anchorScreenPoint.y + gap;

        left = ClampToScreen(left, promptSize.x, screenSize.x, margin);
        bottom = ClampToScreen(bottom, promptSize.y, screenSize.y, margin);

        return new Vector2(
            Mathf.Round(left) + (promptSize.x * pivot.x),
            Mathf.Round(bottom) + (promptSize.y * pivot.y));
    }

    private static float ClampToScreen(float start, float size, float screenSize, float margin)
    {
        // A prompt wider than the usable screen pins to the near margin instead of inverting the range.
        float max = Mathf.Max(margin, screenSize - margin - size);
        return Mathf.Clamp(start, margin, max);
    }

    /// <summary>
    /// Measures the sprites' mesh outlines instead of their frames: animation frames carry wide transparent
    /// padding around the artwork, which would push the prompt far above the character.
    /// </summary>
    private static bool TryMeasureVisibleArtwork(SpriteRenderer[] renderers, out Vector3 min, out Vector3 max)
    {
        min = default;
        max = default;
        if (renderers == null)
        {
            return false;
        }

        bool hasArtwork = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                renderer.sprite == null)
            {
                continue;
            }

            Transform rendererTransform = renderer.transform;
            Vector2[] outline = renderer.sprite.vertices;
            for (int vertex = 0; vertex < outline.Length; vertex++)
            {
                Vector3 point = rendererTransform.TransformPoint(outline[vertex]);
                if (hasArtwork)
                {
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }
                else
                {
                    min = point;
                    max = point;
                    hasArtwork = true;
                }
            }
        }

        return hasArtwork;
    }
}
