/// <summary>One upgradable item in the Mejoras list.</summary>
public readonly struct MerchantUpgradeRowViewModel
{
    public string Title { get; }
    public bool IsSelected { get; }

    public MerchantUpgradeRowViewModel(string title, bool isSelected)
    {
        Title = title;
        IsSelected = isSelected;
    }
}
