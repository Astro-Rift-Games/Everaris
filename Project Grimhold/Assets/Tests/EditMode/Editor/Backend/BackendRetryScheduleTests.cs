#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine;

public sealed class BackendRetryScheduleTests
{
    [TestCase(1, 1000)]
    [TestCase(2, 2000)]
    [TestCase(3, 4000)]
    [TestCase(10, 30000)]
    [TestCase(int.MaxValue, 30000)]
    public void Backoff_DoublesAndCaps(int retry, int expected) =>
        Assert.That(BackendRetrySchedule.DelayMilliseconds(default, retry), Is.EqualTo(expected));

    [TestCase(null, 4f, 4000)]
    [TestCase("7", 4f, 7000)]
    [TestCase("2", 4f, 4000)]
    [TestCase("invalid", 4f, 4000)]
    [TestCase("-1", 0f, 1000)]
    [TestCase(null, -5f, 1000)]
    [TestCase("45", 0f, 45000)]
    public void WaitHint_IsRespectedWithoutShorteningBackoff(string header, float bodySeconds, int expected)
    {
        var error = JsonUtility.FromJson<BackendError>("{\"error\":\"TOO_MANY_REQUESTS\",\"retryAfter\":" +
            bodySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
        error = BackendRetrySchedule.WithRetryAfter(error, header, DateTimeOffset.UtcNow);
        Assert.That(BackendRetrySchedule.DelayMilliseconds(error, 1), Is.EqualTo(expected));
        Assert.That(error.error, Is.EqualTo("TOO_MANY_REQUESTS"));
    }

    [Test]
    public void HttpDateHint_UsesRemainingTime()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var error = BackendRetrySchedule.WithRetryAfter(default, now.AddSeconds(12).ToString("r"), now);
        Assert.That(BackendRetrySchedule.DelayMilliseconds(error, 1), Is.EqualTo(12000));
    }

    [Test]
    public void MissingHint_PreservesErrorAndZeroDelayTestSeam()
    {
        var error = BackendRetrySchedule.WithRetryAfter(new BackendError { error = "TIMEOUT", message = "same" }, null, DateTimeOffset.UtcNow);
        Assert.That(error.message, Is.EqualTo("same"));
        Assert.That(BackendRetrySchedule.DelayMilliseconds(error, 2, 0), Is.Zero);
    }
}
#endif
