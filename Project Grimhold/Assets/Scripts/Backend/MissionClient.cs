using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Grimhold.Backend
{
    [Serializable]
    public struct ClaimMissionRequest
    {
        public string transactionId;
        public string missionId;
        public string claimKey;
        public int expectedRevision;
    }

    [Serializable]
    public struct ClaimMissionResult
    {
        public bool alreadySecured;
        public string transactionId;
        public string missionId;
        public string claimKey;
        public long goldGranted;
        public long currency;
        public int revision;
    }

    /// <summary>
    /// HTTP client for authoritative mission monetary reward claims (TASK-310).
    /// </summary>
    public static class MissionClient
    {
        public static async Task<(bool success, ClaimMissionResult data, BackendError error)>
            ClaimMissionAsync(
                BackendConfiguration config,
                string token,
                string transactionId,
                string missionId,
                string claimKey,
                int expectedRevision)
        {
            var baseUrl = config.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/character/me/missions/claim";
            var request = new ClaimMissionRequest
            {
                transactionId = transactionId,
                missionId = missionId,
                claimKey = claimKey,
                expectedRevision = expectedRevision
            };
            var json = JsonUtility.ToJson(request);

            return await AuthorizedRequest.SendAsync<ClaimMissionResult>(config, token, () =>
            {
                var webRequest = InventoryProtocol.Create(url, "POST");
                webRequest.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                return webRequest;
            }, req =>
            {
                var responseText = req.downloadHandler?.text ?? "null";
                Debug.Log($"[MissionClient] POST {url} returned HTTP {req.responseCode}. Raw body: {responseText}");
                return ProcessResponse(req);
            });
        }

        private static (bool success, ClaimMissionResult data, BackendError error) ProcessResponse(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                var result = JsonUtility.FromJson<ClaimMissionResult>(request.downloadHandler.text);
                return (true, result, default);
            }

            var err = BackendRetrySchedule.WithRetryAfter(
                ParseError(request),
                request.GetResponseHeader("Retry-After"),
                DateTimeOffset.UtcNow);
            return (false, default, err);
        }

        private static BackendError ParseError(UnityWebRequest request)
        {
            if (!string.IsNullOrEmpty(request.downloadHandler?.text))
            {
                try { return JsonUtility.FromJson<BackendError>(request.downloadHandler.text); }
                catch { /* fall through */ }
            }
            return new BackendError
            {
                error = BackendErrorUtility.ClassifyConnectionError(request.error),
                message = request.error
            };
        }
    }
}
