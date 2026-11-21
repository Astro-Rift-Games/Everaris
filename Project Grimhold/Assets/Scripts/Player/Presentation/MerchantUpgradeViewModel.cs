using System;
using System.Collections.Generic;

/// <summary>Everything the Mejoras tab renders; computed by TownUpgradePresentation, never by the UI.</summary>
public sealed class MerchantUpgradeViewModel
{
    public static readonly MerchantUpgradeViewModel Empty = new(Array.Empty<MerchantUpgradeRowViewModel>(), null,
        Array.Empty<TownUpgradeOperationViewModel>(), default, Array.Empty<string>(), false, false, null);

    public IReadOnlyList<MerchantUpgradeRowViewModel> Rows { get; }
    public string SelectedTitle { get; }
    public bool HasSelection => SelectedTitle != null;
    public IReadOnlyList<TownUpgradeOperationViewModel> Operations { get; }
    public NpcUpgradeOperation SelectedOperation { get; }
    public IReadOnlyList<string> PreviewLines { get; }
    public bool CanConfirm { get; }
    public bool IsPending { get; }
    public string Message { get; }

    public MerchantUpgradeViewModel(IReadOnlyList<MerchantUpgradeRowViewModel> rows, string selectedTitle,
        IReadOnlyList<TownUpgradeOperationViewModel> operations, NpcUpgradeOperation selectedOperation,
        IReadOnlyList<string> previewLines, bool canConfirm, bool isPending, string message)
    {
        Rows = rows;
        SelectedTitle = selectedTitle;
        Operations = operations;
        SelectedOperation = selectedOperation;
        PreviewLines = previewLines;
        CanConfirm = canConfirm;
        IsPending = isPending;
        Message = message ?? string.Empty;
    }
}
