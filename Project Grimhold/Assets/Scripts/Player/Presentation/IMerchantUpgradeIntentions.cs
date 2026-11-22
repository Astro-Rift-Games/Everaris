using System;
using System.Threading.Tasks;

/// <summary>What the Mejoras tab may ask for; the presenter-owned implementation talks to the upgrade session.</summary>
public interface IMerchantUpgradeIntentions
{
    MerchantUpgradeViewModel Current { get; }
    event Action<MerchantUpgradeViewModel> Changed;
    void SelectTarget(int index);
    void SelectOperation(NpcUpgradeOperation operation);
    Task ConfirmAsync();
    void Cancel();
}
