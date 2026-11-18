using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Reusable info/confirmation dialog. The callback is consumed before invoking application actions.</summary>
public sealed class MainMenuDialogView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _bodyText;
    [SerializeField] private TextMeshProUGUI _acceptText;
    [SerializeField] private Button _acceptButton;
    [SerializeField] private Button _cancelButton;
    private Action<bool> _resolved;
    public bool IsOpen => gameObject.activeSelf;

    private void OnEnable()
    {
        _acceptButton?.onClick.AddListener(Accept);
        _cancelButton?.onClick.AddListener(Cancel);
    }
    private void OnDisable()
    {
        _acceptButton?.onClick.RemoveListener(Accept);
        _cancelButton?.onClick.RemoveListener(Cancel);
        _resolved = null;
    }
    public void Open(string title, string body, string acceptLabel, Action<bool> resolved)
    {
        if (_titleText == null || _bodyText == null || _cancelButton == null ||
            (acceptLabel != null && (_acceptButton == null || _acceptText == null)))
            Debug.LogError($"[{nameof(MainMenuDialogView)}] Assign title, body, cancel and confirmation accept references.", this);
        if (_titleText != null) _titleText.text = title;
        if (_bodyText != null) _bodyText.text = body;
        if (_acceptText != null) _acceptText.text = acceptLabel ?? "";
        if (_acceptButton != null) _acceptButton.gameObject.SetActive(acceptLabel != null);
        gameObject.SetActive(true);
        _resolved = resolved;
        _cancelButton?.Select();
    }
    public void Accept() => Resolve(true);
    public void Cancel() => Resolve(false);
    private void Resolve(bool accepted)
    {
        var resolved = _resolved;
        _resolved = null;
        gameObject.SetActive(false);
        resolved?.Invoke(accepted);
    }
    private void Update()
    {
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) Cancel();
    }
}
