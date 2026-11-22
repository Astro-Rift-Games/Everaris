using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Local-only rendering surface of the Mentor respec panel (TASK-304): title, the six current attributes, the points a
/// respec returns, the server's cost, the block/result message, and Confirmar / Cancelar. It owns no rules; the
/// presenter feeds it a <see cref="TownRespecPanelPresentation"/> and reacts to its events.
/// </summary>
[DisallowMultipleComponent]
public sealed class TownRespecPanelView : MonoBehaviour
{
    [SerializeField] private TMP_Text _titleText;

    [Tooltip("One value text per attribute, in CharacterAttribute order: Vitality, Resistance, Strength, Dexterity, Intelligence, Luck.")]
    [SerializeField] private TMP_Text[] _attributeValueTexts;

    [SerializeField] private TMP_Text _pointsToReturnText;
    [SerializeField] private TMP_Text _costText;
    [SerializeField] private TMP_Text _messageText;
    [SerializeField] private TMP_Text _confirmationText;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private Button _cancelButton;
    [SerializeField] private Button _closeButton;

    private bool _isWired;

    public bool IsOpen => UiPanelVisibility.IsVisible(gameObject);

    public event Action ConfirmRequested;
    public event Action CancelRequested;
    public event Action CloseRequested;

    private void Awake() => EnsureWired();

    private void OnDestroy()
    {
        if (!_isWired)
        {
            return;
        }

        RemoveListener(_confirmButton, RequestConfirm);
        RemoveListener(_cancelButton, RequestCancel);
        RemoveListener(_closeButton, RequestClose);
        _isWired = false;
    }

    public void Open()
    {
        EnsureWired();
        UiPanelVisibility.SetVisible(gameObject, true);
    }

    public void Close() => UiPanelVisibility.SetVisible(gameObject, false);

    public void Present(in TownRespecPanelPresentation presentation)
    {
        EnsureWired();
        SetText(_titleText, TownRespecPanelPresentation.Title);
        if (_attributeValueTexts != null)
        {
            for (int index = 0; index < _attributeValueTexts.Length; index++)
            {
                SetText(_attributeValueTexts[index], presentation.ValueText((CharacterAttribute)index));
            }
        }

        TownRespecPresentation respec = presentation.Respec;
        SetText(_pointsToReturnText, presentation.PointsToReturnText);
        SetText(_costText, respec.CostText);
        SetText(_messageText, respec.DisplayMessage ?? string.Empty);
        SetText(_confirmationText, respec.ConfirmationText ?? string.Empty);
        SetInteractable(_confirmButton, respec.IsConfirming && respec.CanRespec);
        SetInteractable(_cancelButton, !respec.IsPending);
    }

    private void EnsureWired()
    {
        if (_isWired)
        {
            return;
        }

        AddListener(_confirmButton, RequestConfirm);
        AddListener(_cancelButton, RequestCancel);
        AddListener(_closeButton, RequestClose);
        _isWired = true;
    }

    private void RequestConfirm() => ConfirmRequested?.Invoke();
    private void RequestCancel() => CancelRequested?.Invoke();
    private void RequestClose() => CloseRequested?.Invoke();

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
        {
            text.text = value;
        }
    }

    private static void SetInteractable(Button button, bool interactable)
    {
        if (button != null)
        {
            button.interactable = interactable;
        }
    }

    private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
        {
            button.onClick.AddListener(action);
        }
    }

    private static void RemoveListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
        {
            button.onClick.RemoveListener(action);
        }
    }
}
