using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Grimhold.Backend
{
    /// <summary>
    /// HTTP client for the progression persistence endpoints.
    /// </summary>
    public static class ProgressionClient
    {
        // ------------------------------------------------------------------
        // GET progression — called at login to hydrate the local Unity state
        // ------------------------------------------------------------------

        public static async Task<(bool success, ProgressionData data, BackendError error)>
            GetProgressionAsync(BackendConfiguration config, string token)
        {
            var baseUrl = config.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/character/me/progression";
            return await AuthorizedRequest.SendAsync<ProgressionData>(config, token,
                () => InventoryProtocol.Get(url), ProcessResponse<ProgressionData>);
        }

        // ------------------------------------------------------------------
        // Commit progression
        // ------------------------------------------------------------------

        public static async Task<(bool success, CommitProgressionResult data, BackendError error)>
            CommitProgressionAsync(BackendConfiguration config, string token, CommitProgressionRequest request)
        {
            var baseUrl = config.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/character/me/progression/commit";
            Debug.Log($"[ProgressionClient] Committing to URL: {url}");
            return await PostJson<CommitProgressionRequest, CommitProgressionResult>(config, token, url, request);
        }

        // ------------------------------------------------------------------
        // Purchase ability
        // ------------------------------------------------------------------

        public static async Task<(bool success, PurchaseAbilityResult data, BackendError error)>
            PurchaseAbilityAsync(BackendConfiguration config, string token, string transactionId, string abilityId, int expectedRevision)
        {
            var baseUrl = config.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/character/me/abilities/purchase";
            var request = new PurchaseAbilityRequest
            {
                transactionId = transactionId,
                abilityId = abilityId,
                expectedRevision = expectedRevision
            };
            return await PostJson<PurchaseAbilityRequest, PurchaseAbilityResult>(config, token, url, request);
        }

        // ------------------------------------------------------------------
        // Shared helpers
        // ------------------------------------------------------------------

        private static async Task<(bool success, TResult data, BackendError error)>
            PostJson<TRequest, TResult>(BackendConfiguration config, string token, string url, TRequest body)
        {
            var json = JsonUtility.ToJson(body);
            return await AuthorizedRequest.SendAsync<TResult>(config, token, () =>
            {
                var webRequest = InventoryProtocol.Create(url, "POST");
                webRequest.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                return webRequest;
            }, request =>
            {
                var responseText = request.downloadHandler?.text ?? "null";
                Debug.Log($"[ProgressionClient] POST {url} returned HTTP {request.responseCode}. Raw body: {responseText}");
                return ProcessResponse<TResult>(request);
            });
        }

        private static (bool success, T data, BackendError error) ProcessResponse<T>(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                var result = JsonUtility.FromJson<T>(request.downloadHandler.text);
                return (true, result, default);
            }

            var err = BackendRetrySchedule.WithRetryAfter(ParseError(request),
                request.GetResponseHeader("Retry-After"), System.DateTimeOffset.UtcNow);
            return (false, default, err);
        }

        private static BackendError ParseError(UnityWebRequest request)
        {
            if (!string.IsNullOrEmpty(request.downloadHandler?.text))
            {
                try   { return JsonUtility.FromJson<BackendError>(request.downloadHandler.text); }
                catch { /* fall through */ }
            }
            return new BackendError { error = BackendErrorUtility.ClassifyConnectionError(request.error), message = request.error };
        }
    }
}
