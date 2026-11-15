using System;

namespace Grimhold.Backend
{
    [Serializable]
    public struct BackendError
    {
        public string error;
        public string message;
        // Optional seconds supplied by the server; zero means no wait hint.
        public float retryAfter;
    }
}
