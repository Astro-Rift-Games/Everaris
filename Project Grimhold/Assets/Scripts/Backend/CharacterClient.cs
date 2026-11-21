using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Grimhold.Backend
{
    public static class CharacterClient
    {
        public static async Task<(bool success, CharacterData data, BackendError error)> GetCharacterAsync(BackendConfiguration config, string token)
        {
            var url = $"{config.BaseUrl}/character/me";
            return await AuthorizedRequest.SendAsync<CharacterData>(config, token, () => InventoryProtocol.Get(url),
                ProcessResponse<CharacterData>);
        }

        public static async Task<(bool success, CharacterProfileData data, BackendError error)> GetProfileAsync(BackendConfiguration config, string token)
        {
            var url = $"{config.BaseUrl}/character/me/profile";
            return await AuthorizedRequest.SendAsync<CharacterProfileData>(config, token, () => InventoryProtocol.Get(url),
                ProcessResponse<CharacterProfileData>);
        }

        public static async Task<(bool success, CharacterProfileData data, BackendError error)> PatchProfileAsync(BackendConfiguration config, string token, string customNote)
        {
            var url = $"{config.BaseUrl}/character/me/profile";
            var requestData = new PatchProfileRequest { customNote = customNote };
            var json = JsonUtility.ToJson(requestData);

            return await AuthorizedRequest.SendAsync<CharacterProfileData>(config, token, () =>
            {
                var webRequest = InventoryProtocol.Create(url, "PATCH");
                webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                return webRequest;
            }, ProcessResponse<CharacterProfileData>);
        }

        public static async Task<(bool success, CreateCharacterResult data, BackendError error)> PostCreateCharacterAsync(BackendConfiguration config, string token, string name)
        {
            var url = $"{config.BaseUrl}/character/me";
            var requestData = new CreateCharacterRequest { name = name };
            var json = JsonUtility.ToJson(requestData);

            return await AuthorizedRequest.SendAsync<CreateCharacterResult>(config, token, () =>
            {
                var webRequest = InventoryProtocol.Create(url, "POST");
                webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                return webRequest;
            }, ProcessResponse<CreateCharacterResult>);
        }

        public static async Task<(bool success, TownSessionTicketResponse data, BackendError error)> GetTownSessionTicketAsync(BackendConfiguration config, string token)
        {
            var url = $"{config.BaseUrl}/character/me/session-ticket/town";
            return await AuthorizedRequest.SendAsync<TownSessionTicketResponse>(config, token, () =>
            {
                var webRequest = InventoryProtocol.Create(url, "POST");
                webRequest.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                return webRequest;
            }, ProcessResponse<TownSessionTicketResponse>);
        }

        public static async Task<(bool success, VerifyTownTicketResponse data, BackendError error)> VerifyTownSessionTicketAsync(BackendConfiguration config, string token, string rawTicket)
        {
            var url = $"{config.BaseUrl}/character/verify-session-ticket";
            var body = new VerifyTownTicketRequest { ticket = rawTicket };
            var json = JsonUtility.ToJson(body);

            return await AuthorizedRequest.SendAsync<VerifyTownTicketResponse>(config, token, () =>
            {
                var webRequest = InventoryProtocol.Create(url, "POST");
                webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                return webRequest;
            }, ProcessResponse<VerifyTownTicketResponse>);
        }

        public static async Task<(bool success, AuthoritativeRaidAdmissionTicketResponse data, BackendError error)> GetAuthoritativeRaidAdmissionTicketAsync(
            BackendConfiguration config,
            string token,
            string raidCode,
            string reservationId)
        {
            var url = $"{config.BaseUrl}/character/me/raid/admission-ticket";
            var body = new IssueRaidAdmissionTicketRequest { raidCode = raidCode, reservationId = reservationId };
            var json = JsonUtility.ToJson(body);

            return await AuthorizedRequest.SendAsync<AuthoritativeRaidAdmissionTicketResponse>(config, token, () =>
            {
                var webRequest = InventoryProtocol.Create(url, "POST");
                webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                return webRequest;
            }, ProcessResponse<AuthoritativeRaidAdmissionTicketResponse>);
        }

        public static async Task<(bool success, VerifyRaidAdmissionTicketResponse data, BackendError error)> VerifyAuthoritativeRaidAdmissionTicketAsync(
            BackendConfiguration config,
            string token,
            string rawTicket,
            string raidCode)
        {
            var url = $"{config.BaseUrl}/character/raid/verify-admission";
            var body = new VerifyRaidAdmissionTicketRequest { ticket = rawTicket, raidCode = raidCode };
            var json = JsonUtility.ToJson(body);

            return await AuthorizedRequest.SendAsync<VerifyRaidAdmissionTicketResponse>(config, token, () =>
            {
                var webRequest = InventoryProtocol.Create(url, "POST");
                webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                return webRequest;
            }, ProcessResponse<VerifyRaidAdmissionTicketResponse>);
        }

        private static (bool success, T data, BackendError error) ProcessResponse<T>(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                // Admission prepared slots are a legacy string / ID-free object union.
                // Other character endpoints retain their existing Unity serializer behavior.
                var result = typeof(T) == typeof(AuthoritativeRaidAdmissionTicketResponse) ||
                    typeof(T) == typeof(VerifyRaidAdmissionTicketResponse)
                    ? Newtonsoft.Json.JsonConvert.DeserializeObject<T>(request.downloadHandler.text)
                    : JsonUtility.FromJson<T>(request.downloadHandler.text);
                return (true, result, default);
            }
            
            BackendError backendError = default;
            if (!string.IsNullOrEmpty(request.downloadHandler?.text))
            {
                try
                {
                    backendError = JsonUtility.FromJson<BackendError>(request.downloadHandler.text);
                }
                catch
                {
                    backendError = new BackendError { error = "UNKNOWN", message = request.error };
                }
            }
            else
            {
                backendError = new BackendError { error = BackendErrorUtility.ClassifyConnectionError(request.error), message = request.error };
            }
            backendError = BackendRetrySchedule.WithRetryAfter(
                backendError, request.GetResponseHeader("Retry-After"), DateTimeOffset.UtcNow);
            return (false, default, backendError);
        }
    }
}
