using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Grimhold.Backend
{
    [Serializable]
    public struct TransportBlockReportRequest
    {
        public string source;
    }

    [Serializable]
    public struct TransportBlockReportResult
    {
        public bool accepted;
    }

    /// <summary>
    /// HTTP client for non-authoritative, analytics-only client reports (TASK-309).
    /// </summary>
    public static class TelemetryClient
    {
        public const string RaidAdmissionSource = "raid_admission";

        /// <summary>The whole report body: only the whitelisted source, never any personal data.</summary>
        public static string BuildTransportBlockBody() =>
            JsonUtility.ToJson(new TransportBlockReportRequest { source = RaidAdmissionSource });

        public static async Task<(bool success, TransportBlockReportResult data, BackendError error)>
            ReportTransportBlockAsync(BackendConfiguration config, string token)
        {
            var url = $"{config.BaseUrl.TrimEnd('/')}/character/me/telemetry/transport-block";
            var json = BuildTransportBlockBody();

            return await AuthorizedRequest.SendAsync<TransportBlockReportResult>(config, token, () =>
            {
                var webRequest = InventoryProtocol.Create(url, "POST");
                webRequest.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                return webRequest;
            }, ProcessResponse);
        }

        private static (bool success, TransportBlockReportResult data, BackendError error) ProcessResponse(
            UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                var result = JsonUtility.FromJson<TransportBlockReportResult>(request.downloadHandler.text);
                return (result.accepted, result, default);
            }

            return (false, default, ParseError(request));
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
