namespace Grimhold.Backend
{
    /// <summary>
    /// Outcome of one refresh-token exchange, shared by every request that was waiting on it.
    /// <see cref="EndsSession"/> is true only when the backend proved the credentials unusable;
    /// transport failures, 5xx, 429 and validation errors leave the session and its tokens intact.
    /// </summary>
    public readonly struct TokenRefreshResult
    {
        private TokenRefreshResult(bool success, bool endsSession, BackendError error)
        {
            Success = success;
            EndsSession = endsSession;
            Error = error;
        }

        public bool Success { get; }
        public bool EndsSession { get; }
        public BackendError Error { get; }

        public static TokenRefreshResult Succeeded() => new TokenRefreshResult(true, false, default);
        public static TokenRefreshResult SessionEnded(BackendError error) => new TokenRefreshResult(false, true, error);
        public static TokenRefreshResult Failed(BackendError error) => new TokenRefreshResult(false, false, error);
    }
}
