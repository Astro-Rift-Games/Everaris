using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presents the local predictive interaction prompt and confirmed interaction results.
/// It never queries or mutates gameplay directly.
/// </summary>
[DisallowMultipleComponent]
public sealed class InteractionHudPresenter : MonoBehaviour
{
    [SerializeField]
    private GameObject _promptRoot;

    [SerializeField]
    private TMP_Text _promptText;

    [SerializeField]
    private GameObject _feedbackRoot;

    [SerializeField]
    private TMP_Text _feedbackText;

    [SerializeField]
    private GameObject _progressBarRoot;

    [SerializeField]
    private Image _progressBarFill;

    [SerializeField, Min(0f)]
    private float _feedbackDuration = 1.25f;

    [SerializeField, Min(0f)]
    private float _attemptPulseDuration = 0.15f;

    [SerializeField]
    private Color _rejectionColor = new(1f, 0.35f, 0.2f, 1f);

    private LocalInteractionCandidateSource _candidateSource;
    private PlayerInteractionNetworkController _interactionController;
    private NetworkRunner _runner;
    private int _lastConsumedSequence;
    private float _feedbackRemaining;
    private float _attemptPulseRemaining;
    private bool _isBound;
    private bool _wasLockpicking;
    private Color _promptBaseColor = Color.white;

    public bool IsLockpickingProgressVisible => _progressBarRoot != null && _progressBarRoot.activeSelf;
    public float LockpickProgressFill => _progressBarFill != null ? _progressBarFill.fillAmount : 0f;

    private void Awake()
    {
        if (_promptText != null)
        {
            _promptBaseColor = _promptText.color;
        }
    }

    public void Bind(
        LocalInteractionCandidateSource candidateSource,
        PlayerInteractionNetworkController interactionController,
        NetworkRunner runner)
    {
        Unbind();

        _candidateSource = candidateSource;
        _interactionController = interactionController;
        _runner = runner;
        if (_interactionController == null)
        {
            HideAll();
            return;
        }

        _lastConsumedSequence = _interactionController.CurrentInteractionSequence;
        _interactionController.InteractionResolved += OnInteractionResolved;
        _isBound = true;
        RefreshPrompt();
    }

    public void Unbind()
    {
        if (_isBound && _interactionController != null)
        {
            _interactionController.InteractionResolved -= OnInteractionResolved;
        }

        _candidateSource = null;
        _interactionController = null;
        _runner = null;
        _isBound = false;
        _wasLockpicking = false;
        HideAll();
    }

    private void Update()
    {
        if (!_isBound)
        {
            return;
        }

        RefreshPrompt();

        if (_interactionController != null && _interactionController.IsLockpicking)
        {
            UpdateLockpickProgress();
        }
        else if (_wasLockpicking)
        {
            EndLockpickProgress();
        }

        if (_attemptPulseRemaining > 0f)
        {
            _attemptPulseRemaining -= Time.deltaTime;
            if (_promptText != null)
            {
                _promptText.color = Color.Lerp(
                    _promptBaseColor,
                    _rejectionColor,
                    Mathf.Clamp01(_attemptPulseRemaining / Mathf.Max(0.001f, _attemptPulseDuration)));
            }
        }
        else if (_promptText != null && _promptText.color != _promptBaseColor)
        {
            _promptText.color = _promptBaseColor;
        }

        if (_wasLockpicking)
        {
            return;
        }

        if (_feedbackRemaining <= 0f)
        {
            return;
        }

        _feedbackRemaining -= Time.deltaTime;
        if (_feedbackRemaining <= 0f && _feedbackRoot != null)
        {
            _feedbackRoot.SetActive(false);
            if (_progressBarRoot != null)
            {
                _progressBarRoot.SetActive(false);
            }
        }
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void OnInteractionResolved(InteractionPresentationEvent interactionEvent)
    {
        if (interactionEvent.Sequence <= _lastConsumedSequence)
        {
            return;
        }

        _lastConsumedSequence = interactionEvent.Sequence;

        if (interactionEvent.Success && interactionEvent.IsConsumed)
        {
            HideFeedback();
            return;
        }

        if (!interactionEvent.Success &&
            interactionEvent.FailureReason == InteractionFailureReason.LootRejected &&
            (interactionEvent.LootFailureReason == LootTransferFailureReason.InventoryFull ||
             interactionEvent.LootFailureReason == LootTransferFailureReason.MaxTransportableExceeded))
        {
            TryPlayPickupRejection(interactionEvent.TargetId);
            HideFeedback();
            return;
        }

        if (!interactionEvent.Success && IsContextualRejection(interactionEvent.FailureReason))
        {
            _attemptPulseRemaining = _attemptPulseDuration;
            HideFeedback();
            return;
        }

        if (interactionEvent.Success
            && interactionEvent.FailureReason == InteractionFailureReason.LockpickingStarted)
        {
            UpdateLockpickProgress();
            return;
        }

        ShowFeedback(interactionEvent.Success
            ? "Interacción aceptada"
            : GetFailureMessage(interactionEvent.FailureReason));
    }

    private void RefreshPrompt()
    {
        bool hasCandidate = _candidateSource != null && _candidateSource.HasCandidate;
        bool showPrompt = hasCandidate;

        if (_promptRoot != null)
        {
            _promptRoot.SetActive(showPrompt);
        }

        if (_promptText != null && showPrompt)
        {
            _promptText.text = InteractionPromptText.Format(_candidateSource?.CurrentPromptText);
        }
    }

    private void ShowFeedback(string message)
    {
        if (_feedbackText != null)
        {
            _feedbackText.text = message;
        }

        if (_feedbackRoot != null)
        {
            _feedbackRoot.SetActive(true);
        }

        _feedbackRemaining = _feedbackDuration;
    }

    public void ShowStatusFeedback(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            ShowFeedback(message);
        }
    }

    private void HideFeedback()
    {
        _feedbackRemaining = 0f;
        if (_feedbackRoot != null)
        {
            _feedbackRoot.SetActive(false);
        }
        if (_progressBarRoot != null)
        {
            _progressBarRoot.SetActive(false);
        }
    }

    private void UpdateLockpickProgress()
    {
        _wasLockpicking = true;
        float duration = _interactionController != null ? Mathf.Max(0.01f, _interactionController.LockpickDurationSeconds) : 3f;
        float remaining = _interactionController != null ? Mathf.Max(0f, _interactionController.LockpickRemainingSeconds) : 0f;
        float progress = Mathf.Clamp01(1f - (remaining / duration));

        if (_feedbackRoot != null && !_feedbackRoot.activeSelf)
        {
            _feedbackRoot.SetActive(true);
        }

        EnsureProgressBar();

        if (_progressBarFill != null)
        {
            _progressBarFill.fillAmount = progress;
        }

        if (_progressBarRoot != null && !_progressBarRoot.activeSelf)
        {
            _progressBarRoot.SetActive(true);
        }

        if (_feedbackText != null)
        {
            _feedbackText.text = $"Forzando cerradura… {Mathf.RoundToInt(progress * 100f)}%";
        }

        _feedbackRemaining = _feedbackDuration;
    }

    private void EndLockpickProgress()
    {
        _wasLockpicking = false;
        if (_progressBarRoot != null)
        {
            _progressBarRoot.SetActive(false);
        }

        if (_feedbackText != null && _feedbackRemaining > 0f)
        {
            _feedbackText.text = "Forzado interrumpido";
            _feedbackRemaining = 1.0f;
        }
    }

    private void EnsureProgressBar()
    {
        if (_progressBarRoot != null && _progressBarFill != null)
        {
            return;
        }

        if (_feedbackRoot == null)
        {
            return;
        }

        Transform existing = _feedbackRoot.transform.Find("LockpickProgressBar");
        if (existing != null)
        {
            _progressBarRoot = existing.gameObject;
            Transform fillChild = existing.Find("Fill");
            if (fillChild != null)
            {
                _progressBarFill = fillChild.GetComponent<Image>();
            }
            return;
        }

        GameObject barRoot = new GameObject("LockpickProgressBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        barRoot.transform.SetParent(_feedbackRoot.transform, false);
        RectTransform barRect = barRoot.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0.5f, 0f);
        barRect.anchorMax = new Vector2(0.5f, 0f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.anchoredPosition = new Vector2(0f, -8f);
        barRect.sizeDelta = new Vector2(320f, 12f);

        Image bgImage = barRoot.GetComponent<Image>();
        bgImage.color = new Color(0.12f, 0.12f, 0.16f, 0.85f);

        GameObject fillObj = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillObj.transform.SetParent(barRoot.transform, false);
        RectTransform fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = Vector2.zero;
        fillRect.anchoredPosition = Vector2.zero;

        Image fillImage = fillObj.GetComponent<Image>();
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = 0f;
        fillImage.color = new Color(0.95f, 0.72f, 0.2f, 1f);

        _progressBarRoot = barRoot;
        _progressBarFill = fillImage;
    }

    private void HideAll()
    {
        _feedbackRemaining = 0f;
        _attemptPulseRemaining = 0f;

        if (_promptText != null)
        {
            _promptText.color = _promptBaseColor;
        }

        if (_promptRoot != null)
        {
            _promptRoot.SetActive(false);
        }

        HideFeedback();
    }

    private void TryPlayPickupRejection(EntityId targetId)
    {
        if (_runner == null || targetId.Value == 0)
        {
            return;
        }

        var networkId = new NetworkId { Raw = unchecked((uint)targetId.Value) };
        if (_runner.TryFindObject(networkId, out NetworkObject target) && target != null &&
            target.Id.Raw == networkId.Raw &&
            target.TryGetComponent(out LootPickupRejectionPresenter presenter))
        {
            presenter.PlayRejectedPickup();
        }
    }

    private static bool IsContextualRejection(InteractionFailureReason reason)
    {
        return reason == InteractionFailureReason.InvalidTarget ||
            reason == InteractionFailureReason.InteractionDisabled ||
            reason == InteractionFailureReason.InteractorUnavailable ||
            reason == InteractionFailureReason.TargetUnavailable ||
            reason == InteractionFailureReason.OutOfRange;
    }

    private static string GetFailureMessage(InteractionFailureReason reason)
    {
        return reason switch
        {
            InteractionFailureReason.InteractionDisabled => "No podés interactuar ahora",
            InteractionFailureReason.InteractorUnavailable => "No podés interactuar",
            InteractionFailureReason.OutOfRange => "Fuera de alcance",
            InteractionFailureReason.TargetUnavailable => "Objetivo no disponible",
            InteractionFailureReason.ReceiverNotFound => "No se pudo recibir el loot",
            InteractionFailureReason.LootRejected => "Loot rechazado",
            InteractionFailureReason.LockpickRequired => "Requiere Ganzúa",
            InteractionFailureReason.LockpickingStarted => "Forzando cerradura…",
            _ => "Nada para interactuar"
        };
    }
}
