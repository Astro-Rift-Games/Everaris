using System.Threading.Tasks;
using Grimhold.Backend;

/// <summary>One Town NPC upgrade request; callers apply only the returned authoritative snapshot.</summary>
public interface IRemoteUpgradeService
{
    Task<(bool success, UpgradeItemResult data, BackendError error)> UpgradeItemAsync(UpgradeItemRequest request);
}
