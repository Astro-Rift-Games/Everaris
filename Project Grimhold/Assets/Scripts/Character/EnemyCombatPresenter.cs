using UnityEngine;

/// <summary>
/// Presenter component responsible for enemy attack anticipation telegraph on Host, Client, and Proxies
/// (TASK-41 / TASK-316). Observes replicated <see cref="EnemyCombatAIController"/> anticipation state
/// purely for visual readability; disabling this component never alters authoritative attack timing.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyCombatPresenter : CombatPresenterBase
{
    [Header("Anticipation Visual Feedback")]
    [SerializeField]
    private SpriteRenderer _bodySpriteRenderer;

    [Tooltip("Tint blended onto the enemy body during Melee anticipation wind-up.")]
    [SerializeField]
    private Color _meleeAnticipationTint = new Color(1.0f, 0.55f, 0.35f, 1.0f);

    [Tooltip("Tint blended onto the enemy body during Ranged anticipation wind-up.")]
    [SerializeField]
    private Color _rangedAnticipationTint = new Color(1.0f, 0.85f, 0.35f, 1.0f);

    [Header("Ranged Aim Telegraph Line")]
    [Tooltip("Length of the directional anticipation indicator shown while a Ranged enemy prepares a shot.")]
    [SerializeField, Min(0.25f)]
    private float _rangedTelegraphLength = 2.5f;

    [SerializeField]
    private Color _rangedTelegraphColor = new Color(1.0f, 0.35f, 0.25f, 0.8f);

    private EnemyCombatAIController _enemyCombat;
    private LineRenderer _aimTelegraphLine;
    private Color _baseBodyColor = Color.white;
    private bool _hasCapturedBodyColor;
    private bool _anticipationVisualActive;

    protected override void CacheDependencies()
    {
        base.CacheDependencies();

        if (_enemyCombat == null)
        {
            _enemyCombat = GetComponentInParent<EnemyCombatAIController>();
        }

        if (_bodySpriteRenderer == null)
        {
            Transform body = transform.parent != null ? transform.parent.Find("Body") : transform.Find("Body");
            if (body != null)
            {
                _bodySpriteRenderer = body.GetComponent<SpriteRenderer>();
            }
            if (_bodySpriteRenderer == null)
            {
                _bodySpriteRenderer = GetComponentInParent<SpriteRenderer>();
            }
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        CaptureBodyColor();
    }

    protected override void OnAttackPerformed(AttackPerformedEvent attackEvent)
    {
        // Execution happened; clear any anticipation cue immediately.
        ClearAnticipationVisuals();
    }

    protected override void LateUpdate()
    {
        if (_enemyCombat == null || _enemyCombat.Object == null || !_enemyCombat.Object.IsValid)
        {
            if (_anticipationVisualActive)
            {
                ClearAnticipationVisuals();
            }
            return;
        }

        if (!_enemyCombat.IsPreparingAttack)
        {
            if (_anticipationVisualActive)
            {
                ClearAnticipationVisuals();
            }
            return;
        }

        float progress = _enemyCombat.PreparationProgress;
        bool isRanged = _enemyCombat.PreparedAttackType == AttackType.Ranged;

        if (_bodySpriteRenderer != null)
        {
            if (!_hasCapturedBodyColor)
            {
                CaptureBodyColor();
            }

            Color targetTint = isRanged ? _rangedAnticipationTint : _meleeAnticipationTint;
            _bodySpriteRenderer.color = Color.Lerp(_baseBodyColor, targetTint, progress);
        }

        if (isRanged)
        {
            UpdateRangedTelegraphLine(progress);
        }
        else if (_aimTelegraphLine != null)
        {
            _aimTelegraphLine.enabled = false;
        }

        _anticipationVisualActive = true;
    }

    protected override void OnCancelAndRestore()
    {
        ClearAnticipationVisuals();
    }

    private void CaptureBodyColor()
    {
        if (_bodySpriteRenderer != null && !_anticipationVisualActive)
        {
            _baseBodyColor = _bodySpriteRenderer.color;
            _hasCapturedBodyColor = true;
        }
    }

    private void ClearAnticipationVisuals()
    {
        if (_bodySpriteRenderer != null && _hasCapturedBodyColor)
        {
            _bodySpriteRenderer.color = _baseBodyColor;
        }

        if (_aimTelegraphLine != null)
        {
            _aimTelegraphLine.enabled = false;
        }

        _anticipationVisualActive = false;
    }

    private void UpdateRangedTelegraphLine(float progress)
    {
        EnsureAimTelegraphLine();
        if (_aimTelegraphLine == null)
        {
            return;
        }

        Vector2 dir = _enemyCombat.PreparedAttackDirection;
        if (dir.sqrMagnitude < 0.0001f)
        {
            _aimTelegraphLine.enabled = false;
            return;
        }

        dir.Normalize();
        Vector3 origin = _enemyCombat.AttackOrigin.position;
        Vector3 end = origin + new Vector3(dir.x, dir.y, 0f) * _rangedTelegraphLength;

        float alpha = Mathf.Lerp(0.18f, _rangedTelegraphColor.a, progress);
        float width = Mathf.Lerp(0.025f, 0.06f, progress);
        Color startColor = new Color(_rangedTelegraphColor.r, _rangedTelegraphColor.g, _rangedTelegraphColor.b, alpha);
        Color endColor = new Color(_rangedTelegraphColor.r, _rangedTelegraphColor.g, _rangedTelegraphColor.b, alpha * 0.25f);

        _aimTelegraphLine.enabled = true;
        _aimTelegraphLine.startWidth = width;
        _aimTelegraphLine.endWidth = width * 0.5f;
        _aimTelegraphLine.startColor = startColor;
        _aimTelegraphLine.endColor = endColor;
        _aimTelegraphLine.SetPosition(0, origin);
        _aimTelegraphLine.SetPosition(1, end);
    }

    private void EnsureAimTelegraphLine()
    {
        if (_aimTelegraphLine != null)
        {
            return;
        }

        Transform existing = transform.Find("RangedAnticipationLine");
        if (existing != null && existing.TryGetComponent(out LineRenderer lr))
        {
            _aimTelegraphLine = lr;
            return;
        }

        var lineObj = new GameObject("RangedAnticipationLine");
        lineObj.transform.SetParent(transform, false);
        _aimTelegraphLine = lineObj.AddComponent<LineRenderer>();
        _aimTelegraphLine.useWorldSpace = true;
        _aimTelegraphLine.positionCount = 2;
        _aimTelegraphLine.sortingOrder = 5;
        _aimTelegraphLine.enabled = false;

        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            _aimTelegraphLine.material = new Material(shader);
        }
    }
}
