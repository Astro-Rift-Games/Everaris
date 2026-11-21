using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Grimhold.Backend
{
    public static class AuthenticationClient
    {
        public static async Task<(bool success, LoginResult result, BackendError error)> PostLoginAsync(BackendConfiguration config, string username, string password)
        {
            var url = $"{config.BaseUrl}/auth/login";
            var requestData = new LoginRequest { username = username, password = password };
            var json = JsonUtility.ToJson(requestData);

            using var request = InventoryProtocol.Create(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = config.TimeoutSeconds;

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                var result = JsonUtility.FromJson<LoginResult>(request.downloadHandler.text);
                return (true, result, default);
            }
            else
            {
                BackendError backendError = default;
                if (!string.IsNullOrEmpty(request.downloadHandler.text))
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
        public static async Task<(bool success, LoginResult result, BackendError error)> PostRegisterAsync(BackendConfiguration config, string username, string password)
        {
            var url = $"{config.BaseUrl}/auth/register";
            var requestData = new LoginRequest { username = username, password = password };
            var json = JsonUtility.ToJson(requestData);

            using var request = InventoryProtocol.Create(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = config.TimeoutSeconds;

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                var result = JsonUtility.FromJson<LoginResult>(request.downloadHandler.text);
                return (true, result, default);
            }
            else
            {
                BackendError backendError = default;
                if (!string.IsNullOrEmpty(request.downloadHandler.text))
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

        /// <summary>
        /// Exchanges the refresh token for a new access/refresh pair. Unauthenticated and strictly one attempt:
        /// the backend treats a second use of the same refresh token as reuse and revokes the whole session
        /// family, so this call is never retried here.
        /// </summary>
        public static async Task<(bool success, RefreshResult result, BackendError error)> PostRefreshAsync(BackendConfiguration config, string refreshToken)
        {
            var url = $"{config.BaseUrl}/auth/refresh";
            var json = JsonUtility.ToJson(new RefreshRequest { refreshToken = refreshToken });

            using var request = InventoryProtocol.Create(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = config.TimeoutSeconds;

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    return (true, JsonUtility.FromJson<RefreshResult>(request.downloadHandler.text), default);
                }
                catch (ArgumentException)
                {
                    return (false, default, new BackendError { error = "INVALID_REFRESH_RESPONSE", message = "Refresh response is not valid JSON." });
                }
            }

            BackendError backendError = default;
            if (!string.IsNullOrEmpty(request.downloadHandler.text))
            {
                try
                {
                    backendError = JsonUtility.FromJson<BackendError>(request.downloadHandler.text);
                }
                catch
                {
                    backendError = default;
                }
            }
            if (string.IsNullOrEmpty(backendError.error))
            {
                backendError = new BackendError { error = BackendErrorUtility.ClassifyConnectionError(request.error), message = request.error };
            }
            backendError = BackendRetrySchedule.WithRetryAfter(
                backendError, request.GetResponseHeader("Retry-After"), DateTimeOffset.UtcNow);
            return (false, default, backendError);
        }

        public static async Task<bool> PostLogoutAsync(BackendConfiguration config, string token)
        {
            var url = $"{config.BaseUrl}/auth/logout";
            // Authenticated like every other call, so an expired access token is renewed first and the
            // backend can still revoke the refresh family.
            var (success, _, error) = await AuthorizedRequest.SendAsync<bool>(config, token, () =>
            {
                var webRequest = InventoryProtocol.Create(url, "POST");
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                return webRequest;
            }, request => request.result == UnityWebRequest.Result.Success
                ? (true, true, default)
                : (false, false, ParseError(request)));

            if (!success)
            {
                Debug.LogWarning($"[{nameof(AuthenticationClient)}] Logout failed (network or server error), proceeding with local logout. Error: {error.error}");
            }

            return success;
        }

        private static BackendError ParseError(UnityWebRequest request)
        {
            if (!string.IsNullOrEmpty(request.downloadHandler?.text))
            {
                try
                {
                    var parsed = JsonUtility.FromJson<BackendError>(request.downloadHandler.text);
                    if (!string.IsNullOrEmpty(parsed.error)) return parsed;
                }
                catch { /* fall through */ }
            }
            return new BackendError { error = BackendErrorUtility.ClassifyConnectionError(request.error), message = request.error };
        }
    }
}
