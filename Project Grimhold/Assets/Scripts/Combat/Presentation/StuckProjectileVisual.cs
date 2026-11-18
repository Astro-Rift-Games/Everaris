using ProjectGrimhold.Gameplay.Visibility;
using UnityEngine;

/// <summary>
/// Local, presentation-only copy of a projectile sprite left embedded at its impact point.
/// It never touches simulation: it follows an optional body, mirrors that body's vision opacity,
/// holds, fades out and destroys itself. A destroyed body ends it immediately, and so does an anchor
/// collider that was disabled, deactivated or destroyed (for example a broken breakable object).
/// The copy shows only the trailing part of the sprite, cut at the surface, so it reads as embedded.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class StuckProjectileVisual : MonoBehaviour
{
    // Floor for |flight . normal| so near-grazing hits do not blow the sinking depth up.
    private const float MinObliqueCosine = 0.35f;

    private SpriteRenderer _renderer;
    private Transform _target;
    private bool _hasTarget;
    private Vector3 _targetOffset;
    private EntityVisibilityPresenter _targetVisibility;
    private Collider2D _anchor;
    private bool _hasAnchor;
    private Vector3 _anchorOffset;
    private Color _baseColor;
    private float _holdSeconds;
    private float _fadeSeconds;
    private float _elapsed;
    private Sprite _ownedSprite;

    public bool IsExpired { get; private set; }

    /// <summary>
    /// Creates a world-rooted copy of <paramref name="source"/> whose cut edge sits at
    /// <paramref name="worldPosition"/>. The tip side along <paramref name="flightDirection"/> is cropped away,
    /// keeping <paramref name="visibleFraction"/> of the sprite length. A non-null <paramref name="target"/>
    /// is followed with a world-space offset. An optional <paramref name="anchor"/> collider ends the visual as soon
    /// as it is destroyed, disabled or deactivated, and, with no <paramref name="target"/>, is followed instead.
    /// <paramref name="extraKeptWorldLength"/> keeps that much more sprite length behind the cut edge, so a cut edge
    /// sunk deeper into the surface (oblique hit) leaves the same length visible outside.
    /// </summary>
    public static StuckProjectileVisual Create(SpriteRenderer source, Vector3 worldPosition, Vector2 flightDirection,
        float visibleFraction, Transform target, float holdSeconds, float fadeSeconds, int sortingLayerId,
        Collider2D anchor = null, float extraKeptWorldLength = 0f)
    {
        Sprite cropped = CreateCroppedSprite(source, flightDirection, visibleFraction, extraKeptWorldLength);
        Transform sourceTransform = source.transform;
        var root = new GameObject("StuckProjectile");
        root.transform.SetPositionAndRotation(worldPosition, sourceTransform.rotation);
        root.transform.localScale = sourceTransform.lossyScale;

        SpriteRenderer copy = root.AddComponent<SpriteRenderer>();
        copy.sprite = cropped != null ? cropped : source.sprite;
        copy.color = source.color;
        copy.flipX = source.flipX;
        copy.flipY = source.flipY;
        copy.sharedMaterial = source.sharedMaterial;
        copy.sortingLayerID = sortingLayerId;
        copy.sortingOrder = source.sortingOrder;

        StuckProjectileVisual visual = root.AddComponent<StuckProjectileVisual>();
        visual._ownedSprite = cropped;
        visual.Initialize(copy, target, anchor, holdSeconds, fadeSeconds);
        return visual;
    }

    /// <summary>
    /// Extra distance along the flight the cut edge must sink so both of its ends reach a surface hit at an angle.
    /// The edge is perpendicular to the flight, so its end sits off the surface by
    /// <c>(width / 2) * |perpendicular . normal|</c>; dividing by <c>|flight . normal|</c> (floored so grazing
    /// hits stay bounded) gives the depth, clamped to <paramref name="edgeWidth"/>. Head-on hits need none.
    /// </summary>
    public static float ComputeObliqueDepth(Vector2 flightDirection, Vector2 surfaceNormal, float edgeWidth)
    {
        if (edgeWidth <= 0f || flightDirection.sqrMagnitude < 1e-6f || surfaceNormal.sqrMagnitude < 1e-6f) return 0f;
        Vector2 flight = flightDirection.normalized;
        Vector2 normal = surfaceNormal.normalized;
        float across = Mathf.Abs(flight.x * normal.y - flight.y * normal.x);
        float along = Mathf.Abs(Vector2.Dot(flight, normal));
        return Mathf.Clamp(edgeWidth * 0.5f * across / Mathf.Max(along, MinObliqueCosine), 0f, edgeWidth);
    }

    /// <summary>
    /// Crops the trailing part of the source sprite (opposite the tip, as seen through the renderer's
    /// rotation and flips) and places its pivot on the cut edge. Returns null when the sprite cannot be
    /// cropped (missing or Tight-packed), so the caller falls back to the uncropped sprite.
    /// </summary>
    private static Sprite CreateCroppedSprite(SpriteRenderer source, Vector2 flightDirection, float visibleFraction,
        float extraKeptWorldLength)
    {
        Sprite sprite = source.sprite;
        if (sprite == null || (sprite.packed && sprite.packingMode == SpritePackingMode.Tight)) return null;

        Rect rect = sprite.packed ? sprite.textureRect : sprite.rect;
        Vector2 local = source.transform.InverseTransformDirection(flightDirection);
        if (source.flipX) local.x = -local.x;
        if (source.flipY) local.y = -local.y;

        bool alongX = Mathf.Abs(local.x) >= Mathf.Abs(local.y);
        bool tipAtMax = (alongX ? local.x : local.y) >= 0f;
        float length = alongX ? rect.width : rect.height;
        Vector3 scale = source.transform.lossyScale;
        float axisScale = Mathf.Max(Mathf.Abs(alongX ? scale.x : scale.y), 1e-6f);
        float extraPixels = Mathf.Max(0f, extraKeptWorldLength) * sprite.pixelsPerUnit / axisScale;
        float kept = Mathf.Clamp(Mathf.RoundToInt(length * Mathf.Clamp01(visibleFraction) + extraPixels), 1f, length);

        Vector2 pivot = new Vector2(sprite.pivot.x / rect.width, sprite.pivot.y / rect.height);
        float cutPivot = tipAtMax ? 1f : 0f;
        if (alongX)
        {
            rect = new Rect(tipAtMax ? rect.x : rect.xMax - kept, rect.y, kept, rect.height);
            pivot.x = cutPivot;
        }
        else
        {
            rect = new Rect(rect.x, tipAtMax ? rect.y : rect.yMax - kept, rect.width, kept);
            pivot.y = cutPivot;
        }

        Sprite cropped = Sprite.Create(sprite.texture, rect, pivot, sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        cropped.name = sprite.name + " (stuck)";
        return cropped;
    }

    /// <summary>Destroys the cropped sprite this visual created; the source sprite is never touched.</summary>
    public void ReleaseOwnedSprite()
    {
        if (_ownedSprite == null) return;
        if (Application.isPlaying) Destroy(_ownedSprite);
        else DestroyImmediate(_ownedSprite);
        _ownedSprite = null;
    }

    /// <summary>Opacity multiplier for <paramref name="elapsed"/> seconds: 1 while holding, then linear to 0.</summary>
    public static float EvaluateFade(float elapsed, float holdSeconds, float fadeSeconds)
    {
        if (elapsed <= holdSeconds) return 1f;
        if (fadeSeconds <= 0f) return 0f;
        return Mathf.Clamp01(1f - (elapsed - holdSeconds) / fadeSeconds);
    }

    /// <summary>Advances the visual; returns true once it has expired and must be destroyed.</summary>
    public bool Tick(float deltaTime)
    {
        if (IsExpired) return true;

        if (_hasTarget && _target == null)
            return Expire();
        if (_hasAnchor && (_anchor == null || !_anchor.enabled || !_anchor.gameObject.activeInHierarchy))
            return Expire();

        _elapsed += Mathf.Max(0f, deltaTime);
        if (_elapsed >= _holdSeconds + _fadeSeconds)
            return Expire();

        if (_hasTarget) transform.position = _target.position + _targetOffset;
        else if (_hasAnchor) transform.position = _anchor.transform.position + _anchorOffset;

        float visibility = _targetVisibility != null ? _targetVisibility.AppliedAlpha : 1f;
        _renderer.forceRenderingOff = visibility <= 0f;
        Color color = _baseColor;
        color.a *= EvaluateFade(_elapsed, _holdSeconds, _fadeSeconds) * visibility;
        _renderer.color = color;
        return false;
    }

    private void Initialize(SpriteRenderer renderer, Transform target, Collider2D anchor, float holdSeconds, float fadeSeconds)
    {
        _renderer = renderer;
        _baseColor = renderer.color;
        _holdSeconds = Mathf.Max(0f, holdSeconds);
        _fadeSeconds = Mathf.Max(0f, fadeSeconds);
        _target = target;
        _hasTarget = target != null;
        _anchor = anchor;
        _hasAnchor = anchor != null;
        if (_hasAnchor) _anchorOffset = transform.position - anchor.transform.position;
        if (!_hasTarget) return;

        _targetOffset = transform.position - target.position;
        _targetVisibility = target.GetComponentInChildren<EntityVisibilityPresenter>();
    }

    private bool Expire()
    {
        IsExpired = true;
        Color color = _baseColor;
        color.a = 0f;
        _renderer.color = color;
        return true;
    }

    private void OnDestroy()
    {
        ReleaseOwnedSprite();
    }

    private void LateUpdate()
    {
        if (Tick(Time.deltaTime)) Destroy(gameObject);
    }
}
