using System;
using System.Collections;
using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

public sealed class SessionStartFailurePolicyTests
{
    private static IEnumerable Reasons()
    {
        foreach (ShutdownReason reason in Enum.GetValues(typeof(ShutdownReason)))
            yield return new TestCaseData((ShutdownReason?)reason,
                reason == ShutdownReason.PhotonCloudTimeout || reason == ShutdownReason.MaxCcuReached
                    ? SessionTransitionResult.ServiceUnavailable : SessionTransitionResult.ConnectionFailed);
        yield return new TestCaseData(null, SessionTransitionResult.ConnectionFailed);
        yield return new TestCaseData((ShutdownReason?)(ShutdownReason)int.MaxValue, SessionTransitionResult.ConnectionFailed);
    }

    [TestCaseSource(nameof(Reasons))]
    public void Classify_OnlyDistinguishableServiceFailures(ShutdownReason? reason, SessionTransitionResult expected)
    {
        Assert.That(SessionStartFailurePolicy.Classify(reason), Is.EqualTo(expected));
    }
}
