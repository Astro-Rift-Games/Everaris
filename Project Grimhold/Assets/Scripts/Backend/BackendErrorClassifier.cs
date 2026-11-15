namespace Grimhold.Backend
{
    public enum BackendErrorClass { Retryable, Conflict, Terminal }

    /// <summary>Domain classification only. Transport errors remain owned by BackendErrorUtility.</summary>
    public static class BackendErrorClassifier
    {
        public static BackendErrorClass Classify(string code)
        {
            switch (code)
            {
                case "TOO_MANY_REQUESTS":
                case "CONCURRENT_MUTATION":
                case "EXTRACTION_RESULT_PENDING":
                case "NO_AUTHORITATIVE_RESULT":
                    return BackendErrorClass.Retryable;
                case "REVISION_CONFLICT":
                case "RESULT_CONFLICT":
                case "RESULT_RESERVATION_MISMATCH":
                case "RESERVATION_MISMATCH":
                case "RESERVATION_RAID_MISMATCH":
                    return BackendErrorClass.Conflict;
                default:
                    // Includes RESULT_ALREADY_CONSUMED, EXCEEDS_*, INSUFFICIENT_* and
                    // all validation/commerce rejections. Unknown codes fail closed.
                    return BackendErrorClass.Terminal;
            }
        }
    }
}
