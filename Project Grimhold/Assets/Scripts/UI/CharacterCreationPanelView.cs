using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CharacterCreationPanelView : MonoBehaviour
{
    [SerializeField] private TMP_InputField _characterNameInput;
    [SerializeField] private Button _createButton;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshProUGUI _stepText;
    [SerializeField] private Button _finishLaterButton;
    [SerializeField] private TextMeshProUGUI _finishLaterText;

    public void Open()
    {
        if (_stepText != null) _stepText.text = "Paso 2 de 2 · Personaje";
        if (_finishLaterText != null) _finishLaterText.text = "Cerrar sesión y terminar después";
        gameObject.SetActive(true);
        SetInteractable(true);
    }

    public void AddFinishLaterListener(UnityEngine.Events.UnityAction action)
        => _finishLaterButton?.onClick.AddListener(action);
    public void RemoveFinishLaterListener(UnityEngine.Events.UnityAction action)
        => _finishLaterButton?.onClick.RemoveListener(action);

    public string CharacterName => _characterNameInput.text.Trim();

    public void SetInteractable(bool interactable)
    {
        if (_characterNameInput != null) _characterNameInput.interactable = interactable;
        if (_createButton != null) _createButton.interactable = interactable;
        if (_finishLaterButton != null) _finishLaterButton.interactable = interactable;
    }

    public void SetStatus(string message)
    {
        if (_statusText != null)
            _statusText.text = message;
    }

    public void AddCreateListener(UnityEngine.Events.UnityAction action)
        => _createButton.onClick.AddListener(action);

    public void RemoveCreateListener(UnityEngine.Events.UnityAction action)
        => _createButton.onClick.RemoveListener(action);
}
