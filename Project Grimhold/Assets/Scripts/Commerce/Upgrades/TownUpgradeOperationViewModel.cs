/// <summary>One upgrade button of the Mejoras tab: label, whether it can be confirmed and why not (Doc 23 §§9.4, 11).</summary>
public readonly struct TownUpgradeOperationViewModel
{
    public NpcUpgradeOperation Operation { get; }
    public string Label { get; }
    public bool Enabled { get; }
    public string Reason { get; }

    public TownUpgradeOperationViewModel(NpcUpgradeOperation operation, string label, bool enabled, string reason)
    {
        Operation = operation;
        Label = label;
        Enabled = enabled;
        Reason = reason ?? string.Empty;
    }
}
