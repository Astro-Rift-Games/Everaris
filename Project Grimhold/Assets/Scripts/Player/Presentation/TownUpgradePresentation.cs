using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Presenter-owned adapter between the Mejoras tab and <see cref="TownUpgradeSession"/>: keeps the selected
/// target (by instance) and operation, and rebuilds <see cref="MerchantUpgradeViewModel"/> from fresh
/// previews on every change. Selecting and previewing never mutate the profile (Doc 23 §11).
/// </summary>
public sealed class TownUpgradePresentation : IMerchantUpgradeIntentions, IDisposable
{
    private readonly TownUpgradeSession _session;
    private readonly LootDefinitionCatalog _catalog;
    private IReadOnlyList<TownUpgradeTarget> _targets = Array.Empty<TownUpgradeTarget>();
    private string _selectedInstanceId;
    private NpcUpgradeOperation? _selectedOperation;
    private bool _disposed;

    public MerchantUpgradeViewModel Current { get; private set; } = MerchantUpgradeViewModel.Empty;
    public event Action<MerchantUpgradeViewModel> Changed;

    public TownUpgradePresentation(TownUpgradeSession session, LootDefinitionCatalog catalog)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _catalog = catalog;
        _session.Changed += Refresh;
        Rebuild();
    }

    public void SelectTarget(int index)
    {
        if (_disposed || _session.IsPending || index < 0 || index >= _targets.Count) return;
        _selectedInstanceId = _targets[index].Instance.InstanceId;
        _selectedOperation = null;
        _session.ClearMessage();
        Refresh();
    }

    public void SelectOperation(NpcUpgradeOperation operation)
    {
        if (_disposed || _session.IsPending || !TryGetSelected(out var target)) return;
        foreach (var candidate in Operations(target))
            if (candidate == operation) { _selectedOperation = operation; Refresh(); return; }
    }

    /// <summary>Selects the intent on the session and confirms it once; disabled operations send nothing.</summary>
    public async Task ConfirmAsync()
    {
        if (_disposed || !Current.CanConfirm || !TryGetSelected(out var target)) return;
        if (_session.Select(target, Current.SelectedOperation)) await _session.ConfirmAsync();
        Refresh();
    }

    public void Cancel()
    {
        if (_disposed || _session.IsPending) return;
        _session.Cancel();
        _selectedInstanceId = null;
        _selectedOperation = null;
        Refresh();
    }

    public void Dispose()
    {
        _disposed = true;
        _session.Changed -= Refresh;
        Changed = null;
    }

    private void Refresh()
    {
        if (_disposed) return;
        Rebuild();
        Changed?.Invoke(Current);
    }

    private void Rebuild()
    {
        _targets = _session.Targets();
        var rows = new MerchantUpgradeRowViewModel[_targets.Count];
        int selected = -1;
        for (int i = 0; i < _targets.Count; i++)
        {
            bool isSelected = _targets[i].Instance.InstanceId == _selectedInstanceId;
            if (isSelected) selected = i;
            rows[i] = new MerchantUpgradeRowViewModel(Title(_targets[i]), isSelected);
        }
        if (selected < 0)
        {
            _selectedInstanceId = null;
            Current = new MerchantUpgradeViewModel(rows, null, Array.Empty<TownUpgradeOperationViewModel>(), default,
                Array.Empty<string>(), false, _session.IsPending, _session.Message);
            return;
        }

        var target = _targets[selected];
        var operations = Operations(target);
        var operationModels = new TownUpgradeOperationViewModel[operations.Count];
        NpcUpgradePreview selectedPreview = null;
        for (int i = 0; i < operations.Count; i++)
        {
            var preview = _session.Preview(target, operations[i]);
            operationModels[i] = TownUpgradeViewModel.Operation(preview);
            if (_selectedOperation == operations[i]) selectedPreview = preview;
        }
        if (selectedPreview == null && operations.Count > 0)
        {
            _selectedOperation = operations[0];
            selectedPreview = _session.Preview(target, operations[0]);
        }
        bool canConfirm = selectedPreview != null && selectedPreview.CanConfirm && !_session.IsPending;
        Current = new MerchantUpgradeViewModel(rows, rows[selected].Title, operationModels,
            _selectedOperation ?? default, TownUpgradeViewModel.PreviewLines(selectedPreview, _catalog), canConfirm,
            _session.IsPending, _session.Message);
    }

    private bool TryGetSelected(out TownUpgradeTarget target)
    {
        foreach (var candidate in _targets)
            if (candidate.Instance.InstanceId == _selectedInstanceId) { target = candidate; return true; }
        target = default;
        return false;
    }

    private IReadOnlyList<NpcUpgradeOperation> Operations(in TownUpgradeTarget target) =>
        _catalog != null && _catalog.TryGet(target.LootId.Value, out var definition)
            ? NpcUpgradeRules.OperationsFor(definition) : Array.Empty<NpcUpgradeOperation>();

    private string Title(in TownUpgradeTarget target)
    {
        LootDefinition definition = null;
        _catalog?.TryGet(target.LootId.Value, out definition);
        return TownUpgradeViewModel.RowTitle(definition, target.Instance, target.IsEquipped);
    }
}
