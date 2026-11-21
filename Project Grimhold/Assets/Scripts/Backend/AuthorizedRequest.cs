using System;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Grimhold.Backend
{
    /// <summary>
    /// The single authenticated send path of every backend client: it attaches the Bearer credential and
    /// absorbs an expired access token (FIX-34) so callers never see it.
    ///
    /// On <c>401 TOKEN_EXPIRED</c> the session's refresh token is exchanged once (single-flight, shared by all
    /// concurrent requests) and the original request is replayed exactly once with the renewed token, without
    /// touching the stash services. Requests started while a refresh is in flight wait for it. Every
    /// unrecoverable credential failure (revoked token, rejected refresh, a second expiry, no refresh token)
    /// is reported as <see cref="BackendErrorUtility.Unauthorized"/>, so the callers' existing session-closing
    /// handling is unchanged. A refresh that fails without invalidating the credentials (transport failure,
    /// 5xx, 429, validation) is surfaced as that transient failure and is never retried here.
    /// </summary>
    public static class AuthorizedRequest
    {
        /// <param name="create">Builds a fresh request on every call (a sent request cannot be replayed).</param>
        /// <param name="process">The client's own response mapping, applied to every attempt.</param>
        public static async Task<(bool success, T data, BackendError error)> SendAsync<T>(
            BackendConfiguration config,
            string token,
            Func<UnityWebRequest> create,
            Func<UnityWebRequest, (bool success, T data, BackendError error)> process)
        {
            ApplicationAuthContext session = ApplicationAuthContext.Instance;
            if (session != null && session.IsRefreshInFlight && session.TryResolveCurrentToken(token, out _))
            {
                await session.WaitForRefreshAsync();
            }

            // A token captured before a refresh (a verifier, a pending retry) is replaced by the live one.
            if (session != null && session.TryResolveCurrentToken(token, out string current))
            {
                token = current;
            }

            var attempt = await SendOnceAsync(config, token, create, process);
            if (attempt.success || !IsTokenExpired(attempt.status, attempt.error))
            {
                return (attempt.success, attempt.data, attempt.error);
            }

            if (session == null || !session.TryResolveCurrentToken(token, out string live))
            {
                return Unauthorized<T>(attempt.error);
            }

            if (live != token)
            {
                // Another request already renewed the session while this one was in flight.
                token = live;
            }
            else
            {
                if (string.IsNullOrEmpty(session.RefreshToken))
                {
                    return Unauthorized<T>(attempt.error);
                }

                TokenRefreshResult refresh = await session.RefreshAccessTokenAsync(config);
                if (!refresh.Success)
                {
                    return refresh.EndsSession ? Unauthorized<T>(refresh.Error) : (false, default, refresh.Error);
                }

                token = session.Token;
            }

            var retry = await SendOnceAsync(config, token, create, process);
            if (!retry.success && IsTokenExpired(retry.status, retry.error))
            {
                return Unauthorized<T>(retry.error);
            }

            return (retry.success, retry.data, retry.error);
        }

        private static async Task<(bool success, T data, BackendError error, long status)> SendOnceAsync<T>(
            BackendConfiguration config,
            string token,
            Func<UnityWebRequest> create,
            Func<UnityWebRequest, (bool success, T data, BackendError error)> process)
        {
            using var request = create();
            request.SetRequestHeader("Authorization", $"Bearer {token}");
            request.timeout = config.TimeoutSeconds;

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            var (success, data, error) = process(request);
            return (success, data, error, request.responseCode);
        }

        private static bool IsTokenExpired(long status, BackendError error) =>
            status == 401 && error.error == BackendErrorUtility.TokenExpired;

        private static (bool success, T data, BackendError error) Unauthorized<T>(BackendError cause) =>
            (false, default, new BackendError { error = BackendErrorUtility.Unauthorized, message = cause.message });
    }
}
