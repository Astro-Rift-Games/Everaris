using System;

namespace Grimhold.Backend
{
    /// <summary>Pure retry timing; a server wait hint is a minimum, not a client-side cap.</summary>
    public static class BackendRetrySchedule
    {
        public static int DelayMilliseconds(BackendError error, int retryNumber, int baseMilliseconds = 1000)
        {
            double exponential = Math.Min(30000d, Math.Max(0, baseMilliseconds) *
                Math.Pow(2, Math.Max(0, Math.Min(30, retryNumber - 1))));
            double hint = float.IsNaN(error.retryAfter) || float.IsInfinity(error.retryAfter)
                ? 0 : Math.Max(0, error.retryAfter) * 1000d;
            return (int)Math.Min(int.MaxValue, Math.Ceiling(Math.Max(exponential, hint)));
        }

        public static BackendError WithRetryAfter(BackendError error, string header, DateTimeOffset now)
        {
            double seconds;
            if (!double.TryParse(header, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out seconds))
            {
                seconds = DateTimeOffset.TryParse(header, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var date)
                    ? (date - now).TotalSeconds : 0;
            }
            if (seconds > 0 && !double.IsInfinity(seconds) && !double.IsNaN(seconds))
                error.retryAfter = (float)Math.Max(float.IsNaN(error.retryAfter) ? 0 : error.retryAfter,
                    Math.Min(seconds, int.MaxValue / 1000d));
            return error;
        }
    }
}
