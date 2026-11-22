using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mejoras tab of the Blacksmith and Arcanist (TASK-303). Renders <see cref="MerchantUpgradeViewModel"/> and
/// forwards clicks to <see cref="IMerchantUpgradeIntentions"/>; it holds no services, rules or prices.
/// Blocked operations stay selectable so their reason is visible; only Confirm is gated (Doc 23 §§9.4, 11).
/// </summary>
[DisallowMultipleComponent]
public sealed class MerchantUpgradePanel : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private Transform _rowContainer;
    [SerializeField] private Button _rowTemplate;

    [Header("Selection")]
    [SerializeField] private TMP_Text _selectedTitleText;
    [SerializeField] private Button[] _operationButtons = new Button[2];
    [SerializeField] private TMP_Text[] _operationLabels = new TMP_Text[2];
    [SerializeField] private TMP_Text _previewText;

    [Header("Actions")]
    [SerializeField] private TMP_Text _messageText;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private Button _cancelButton;

    private const string EmptyMessage = "Selecciona un objeto equipado o del Inventario para ver sus mejoras";

    private readonly List<Button> _rows = new List<Button>();
    private IMerchantUpgradeIntentions _intentions;
    private MerchantUpgradeViewModel _model = MerchantUpgradeViewModel.Empty;

    private void Awake()
    {
        if (_rowTemplate != null) _rowTemplate.gameObject.SetActive(false);
        if (_confirmButton != null) _confirmButton.onClick.AddListener(OnConfirmClicked);
        if (_cancelButton != null) _cancelButton.onClick.AddListener(OnCancelClicked);
        for (int i = 0; i < _operationButtons.Length; i++)
        {
            int index = i;
            if (_operationButtons[i] != null) _operationButtons[i].onClick.AddListener(() => OnOperationClicked(index));
        }
    }

    private void OnDestroy()
    {
        Unbind();
        if (_confirmButton != null) _confirmButton.onClick.RemoveListener(OnConfirmClicked);
        if (_cancelButton != null) _cancelButton.onClick.RemoveListener(OnCancelClicked);
        foreach (Button button in _operationButtons) if (button != null) button.onClick.RemoveAllListeners();
    }

    public void Bind(IMerchantUpgradeIntentions intentions)
    {
        Unbind();
        _intentions = intentions;
        if (_intentions != null) _intentions.Changed += Present;
        Present(_intentions?.Current ?? MerchantUpgradeViewModel.Empty);
    }

    public void Unbind()
    {
        if (_intentions != null) _intentions.Changed -= Present;
        _intentions = null;
    }

    public void Present(MerchantUpgradeViewModel model)
    {
        _model = model ?? MerchantUpgradeViewModel.Empty;
        RenderRows();
        RenderSelection();
    }

    private void RenderRows()
    {
        if (_rowContainer == null || _rowTemplate == null) return;
        IReadOnlyList<MerchantUpgradeRowViewModel> rows = _model.Rows;
        while (_rows.Count > rows.Count)
        {
            Destroy(_rows[_rows.Count - 1].gameObject);
            _rows.RemoveAt(_rows.Count - 1);
        }
        while (_rows.Count < rows.Count)
        {
            Button row = Instantiate(_rowTemplate, _rowContainer);
            int index = _rows.Count;
            row.onClick.AddListener(() => _intentions?.SelectTarget(index));
            row.gameObject.SetActive(true);
            _rows.Add(row);
        }
        for (int i = 0; i < rows.Count; i++)
        {
            SetText(_rows[i].GetComponentInChildren<TMP_Text>(true), (rows[i].IsSelected ? "> " : string.Empty) + rows[i].Title);
            _rows[i].interactable = !_model.IsPending;
        }
    }

    private void RenderSelection()
    {
        SetText(_selectedTitleText, _model.HasSelection ? _model.SelectedTitle : string.Empty);
        for (int i = 0; i < _operationButtons.Length; i++)
        {
            Button button = _operationButtons[i];
            if (button == null) continue;
            bool visible = i < _model.Operations.Count;
            button.gameObject.SetActive(visible);
            if (!visible) continue;
            TownUpgradeOperationViewModel operation = _model.Operations[i];
            bool selected = operation.Operation == _model.SelectedOperation;
            SetText(i < _operationLabels.Length ? _operationLabels[i] : null, (selected ? "> " : string.Empty) + operation.Label);
            button.interactable = !_model.IsPending;
        }

        SetText(_previewText, string.Join("\n", _model.PreviewLines));
        SetText(_messageText, !string.IsNullOrEmpty(_model.Message) ? _model.Message
            : _model.HasSelection ? string.Empty : EmptyMessage);
        if (_confirmButton != null) _confirmButton.interactable = _model.CanConfirm;
        if (_cancelButton != null) _cancelButton.interactable = _model.HasSelection && !_model.IsPending;
    }

    private void OnOperationClicked(int index)
    {
        if (index < _model.Operations.Count) _intentions?.SelectOperation(_model.Operations[index].Operation);
    }

    private void OnConfirmClicked() => _ = _intentions?.ConfirmAsync();

    private void OnCancelClicked() => _intentions?.Cancel();

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null) text.text = value ?? string.Empty;
    }
}
