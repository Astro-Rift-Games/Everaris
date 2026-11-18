using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Grimhold.Backend
{
    /// <summary>Unauthenticated, bounded availability probe. Never sends or stores session credentials.</summary>
    public sealed class BackendHealthClient : IBackendHealthClient
    {
        public async Task<bool> CheckAsync(BackendConfiguration config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.BaseUrl)) return false;
            using var request = InventoryProtocol.Create(config.BaseUrl.TrimEnd('/') + "/health", "GET");
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = System.Math.Max(1, config.TimeoutSeconds);
            var operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();
            return request.result == UnityWebRequest.Result.Success;
        }
    }
}
