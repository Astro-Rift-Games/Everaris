using UnityEngine;

/// <summary>
/// Optional per-avatar draw cue. Each peer plays spatial SFX through the existing AudioManager;
/// no authority gating or simulation writes. Wire references manually; missing outputs are quiet.
/// </summary>
[DisallowMultipleComponent]
public sealed class AimStanceDrawPresenter : MonoBehaviour
{
    [SerializeField] private PlayerMovementNetworkController _movementSource;
    [SerializeField] private AudioClip _drawCompleteClip;
    [SerializeField] private Transform _pulseRoot;
    [SerializeField, Min(0.01f)] private float _pulseDuration = 0.18f;
    [SerializeField, Min(1f)] private float _pulseScale = 1.12f;

    private PlayerMovementNetworkController _subscribedSource;
    private Vector3 _baseScale;
    private float _remaining;

    private void OnEnable()
    {
        if (_movementSource == null)
            _movementSource = GetComponentInParent<PlayerMovementNetworkController>();
        _subscribedSource = _movementSource;
        if (_subscribedSource != null) _subscribedSource.AimStanceDrawCompleted += Present;
        if (_pulseRoot != null) _baseScale = _pulseRoot.localScale;
    }

    /// <summary>Plays optional sound and starts an optional pulse; safe with no assigned outputs.</summary>
    public void Present()
    {
        if (!isActiveAndEnabled) return;
        if (_drawCompleteClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySfx(_drawCompleteClip, transform.position);
        if (_pulseRoot != null) _remaining = Mathf.Max(0.01f, _pulseDuration);
    }

    private void Update()
    {
        if (_pulseRoot == null || _remaining <= 0f) return;
        float duration = Mathf.Max(0.01f, _pulseDuration);
        _remaining = Mathf.Max(0f, _remaining - Time.deltaTime);
        float progress = 1f - _remaining / duration;
        _pulseRoot.localScale = _remaining > 0f
            ? _baseScale * Mathf.Lerp(1f, _pulseScale, Mathf.Sin(progress * Mathf.PI))
            : _baseScale;
    }

    private void OnDisable()
    {
        if (_subscribedSource != null) _subscribedSource.AimStanceDrawCompleted -= Present;
        _subscribedSource = null;
        if (_pulseRoot != null && _remaining > 0f) _pulseRoot.localScale = _baseScale;
        _remaining = 0f;
    }
}
