using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Local-only rendering surface for confirmed Town character attributes.</summary>
[DisallowMultipleComponent]
public sealed class TownAttributeAssignmentView : MonoBehaviour
{
    [SerializeField] private TMP_Text _availablePointsText;
    [SerializeField] private Button _closeButton;
    [SerializeField] private TownAttributeAssignmentRowView[] _rows;
    [SerializeField] private TownEquipmentSlotView[] _equipmentSlots;
    [SerializeField] private RectTransform _statLinesRoot;
    [SerializeField] private TownStatLineView _headerLineTemplate;
    [SerializeField] private TownStatLineView _statLineTemplate;
    [SerializeField] private TownStatLineView _noteLineTemplate;

    [Header("Respec (TASK-304) — optional until the prefab is wired")]
    [SerializeField] private Button _respecButton;
    [SerializeField] private TMP_Text _respecCostText;
    [SerializeField] private TMP_Text _respecMessageText;
    [SerializeField] private GameObject _respecConfirmationPanel;
    [SerializeField] private TMP_Text _respecConfirmationText;
    [SerializeField] private Button _respecConfirmButton;
    [SerializeField] private Button _respecCancelButton;

    private readonly List<GameObject> _spawnedLines = new();

    public TMP_Text AvailablePointsText => _availablePointsText;
    public Button CloseButton => _closeButton;
    public IReadOnlyList<TownAttributeAssignmentRowView> Rows => _rows;
    public bool IsOpen => UiPanelVisibility.IsVisible(gameObject);

    public event Action<CharacterAttribute> AssignmentRequested;
    public event Action CloseRequested;
    public event Action RespecRequested;
    public event Action RespecConfirmed;
    public event Action RespecCancelled;

    private void Awake()
    {
        if (_closeButton != null)
        {
            _closeButton.onClick.AddListener(RequestClose);
        }

        AddRespecListener(_respecButton, RequestRespec);
        AddRespecListener(_respecConfirmButton, ConfirmRespec);
        AddRespecListener(_respecCancelButton, CancelRespec);

        if (_rows == null)
        {
            return;
        }

        foreach (TownAttributeAssignmentRowView row in _rows)
        {
            if (row != null)
            {
                row.AssignmentRequested += OnAssignmentRequested;
            }
        }
    }

    private void OnDestroy()
    {
        if (_closeButton != null)
        {
            _closeButton.onClick.RemoveListener(RequestClose);
        }

        RemoveRespecListener(_respecButton, RequestRespec);
        RemoveRespecListener(_respecConfirmButton, ConfirmRespec);
        RemoveRespecListener(_respecCancelButton, CancelRespec);

        if (_rows == null)
        {
            return;
        }

        foreach (TownAttributeAssignmentRowView row in _rows)
        {
            if (row != null)
            {
                row.AssignmentRequested -= OnAssignmentRequested;
            }
        }
    }

    public void Present(in TownAttributeAssignmentPresentation presentation)
    {
        _availablePointsText.text = presentation.IsBuildLocked
            ? $"Available Points: {presentation.AvailablePoints} — Locked while Ready"
            : $"Available Points: {presentation.AvailablePoints}";
        foreach (TownAttributeAssignmentRowView row in _rows)
        {
            if (row != null && presentation.TryGet(row.Attribute, out int value, out bool canAssign))
            {
                row.Present(value, canAssign);
            }
        }
    }

    public void PresentUnavailable()
    {
        _availablePointsText.text = "Available Points: —";
        foreach (TownAttributeAssignmentRowView row in _rows)
        {
            row?.PresentUnavailable();
        }
    }

    /// <summary>
    /// Shows the respec cost, availability, confirmation and result. Every respec widget is optional, so a prefab
    /// that does not author them keeps working and simply shows nothing.
    /// </summary>
    public void PresentRespec(in TownRespecPresentation presentation)
    {
        if (_respecCostText != null)
        {
            _respecCostText.text = presentation.CostText;
        }

        if (_respecButton != null)
        {
            _respecButton.interactable = presentation.CanRespec && !presentation.IsConfirming;
        }

        if (_respecMessageText != null)
        {
            _respecMessageText.text = presentation.DisplayMessage ?? string.Empty;
        }

        if (_respecConfirmationPanel != null)
        {
            _respecConfirmationPanel.SetActive(presentation.IsConfirming);
        }

        if (_respecConfirmationText != null)
        {
            _respecConfirmationText.text = presentation.ConfirmationText ?? string.Empty;
        }

        if (_respecConfirmButton != null)
        {
            _respecConfirmButton.interactable = presentation.IsConfirming && presentation.CanRespec;
        }

        if (_respecCancelButton != null)
        {
            _respecCancelButton.interactable = !presentation.IsPending;
        }
    }

    /// <summary>Shows the character sheet: derived statistics and what is prepared in each slot.</summary>
    public void PresentStatistics(in TownCharacterStatisticsPresentation statistics)
    {
        BuildLines(TownCharacterStatisticsLines.Build(statistics));
        if (_equipmentSlots == null)
        {
            return;
        }

        IReadOnlyList<TownEquipmentSlotEntry> entries = statistics.Slots;
        foreach (TownEquipmentSlotView slotView in _equipmentSlots)
        {
            if (slotView == null)
            {
                continue;
            }

            bool found = false;
            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index].Slot == slotView.Slot)
                {
                    slotView.Present(entries[index]);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                slotView.PresentEmpty();
            }
        }
    }

    /// <summary>Replaces the character sheet with a single note and empties the equipment slots.</summary>
    public void PresentStatisticsUnavailable()
    {
        BuildLines(new[] { new TownStatLine(TownStatLineKind.Note, "Statistics unavailable") });
        if (_equipmentSlots == null)
        {
            return;
        }

        foreach (TownEquipmentSlotView slotView in _equipmentSlots)
        {
            slotView?.PresentEmpty();
        }
    }

    private void BuildLines(IReadOnlyList<TownStatLine> lines)
    {
        foreach (GameObject spawned in _spawnedLines)
        {
            if (spawned == null)
            {
                continue;
            }

            // Deactivate first: Destroy is deferred in Play Mode and the row must stop counting immediately.
            spawned.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(spawned);
            }
            else
            {
                DestroyImmediate(spawned);
            }
        }

        _spawnedLines.Clear();
        if (_statLinesRoot == null)
        {
            return;
        }

        foreach (TownStatLine line in lines)
        {
            TownStatLineView template = line.Kind switch
            {
                TownStatLineKind.Header => _headerLineTemplate,
                TownStatLineKind.Note => _noteLineTemplate,
                _ => _statLineTemplate
            };
            if (template == null)
            {
                continue;
            }

            TownStatLineView row = Instantiate(template, _statLinesRoot, false);
            row.gameObject.SetActive(true);
            row.Present(line);
            _spawnedLines.Add(row.gameObject);
        }
    }

    /// <summary>Hides the panel's own close button when a host window provides one.</summary>
    public void SetCloseButtonVisible(bool visible)
    {
        if (_closeButton != null)
        {
            _closeButton.gameObject.SetActive(visible);
        }
    }

    public void Open() => UiPanelVisibility.SetVisible(gameObject, true);
    public void Close() => UiPanelVisibility.SetVisible(gameObject, false);

    private void OnAssignmentRequested(CharacterAttribute attribute) =>
        AssignmentRequested?.Invoke(attribute);

    private void RequestClose() => CloseRequested?.Invoke();
    private void RequestRespec() => RespecRequested?.Invoke();
    private void ConfirmRespec() => RespecConfirmed?.Invoke();
    private void CancelRespec() => RespecCancelled?.Invoke();

    private static void AddRespecListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
        {
            button.onClick.AddListener(action);
        }
    }

    private static void RemoveRespecListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
        {
            button.onClick.RemoveListener(action);
        }
    }
}
