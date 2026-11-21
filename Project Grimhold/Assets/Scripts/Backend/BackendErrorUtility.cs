using UnityEngine;

namespace Grimhold.Backend
{
    public static class BackendErrorUtility
    {
        public const string NetworkError = "NETWORK_ERROR";
        public const string Timeout = "TIMEOUT";
        public const string RequestCancelled = "REQUEST_CANCELLED";
        public const string TooManyRequests = "TOO_MANY_REQUESTS";
        public const string Unauthorized = "UNAUTHORIZED";
        // The access JWT is expired but the session may still be renewable (FIX-34).
        public const string TokenExpired = "TOKEN_EXPIRED";
        // The refresh token is unknown, expired, revoked or was reused.
        public const string RefreshInvalid = "REFRESH_INVALID";

        public static string ClassifyConnectionError(string requestError)
        {
            if (string.IsNullOrEmpty(requestError))
                return NetworkError;

            var lower = requestError.ToLowerInvariant();
            if (lower.Contains("timeout")) return Timeout;
            if (lower.Contains("abort") || lower.Contains("cancel")) return RequestCancelled;
            
            return NetworkError;
        }

        public static bool IsTransportFailure(string errorCode)
        {
            return errorCode == NetworkError || errorCode == Timeout || errorCode == RequestCancelled;
        }
    }
}
