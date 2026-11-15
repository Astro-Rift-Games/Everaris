#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

public sealed class SessionTransitionFailureTextTests
{
    private static IEnumerable<SessionTransitionResult> FailureResults() =>
        Enum.GetValues(typeof(SessionTransitionResult))
            .Cast<SessionTransitionResult>()
            .Where(result => result != SessionTransitionResult.Succeeded);

    [TestCaseSource(nameof(FailureResults))]
    public void EveryFailure_HasAReadableMessage(SessionTransitionResult result)
    {
        Assert.That(SessionTransitionFailureText.TryGetMessage(result, out string message), Is.True);
        Assert.That(message, Is.Not.Null.And.Not.Empty);
    }

    [TestCaseSource(nameof(FailureResults))]
    public void EveryFailure_MessageCarriesNoInternalEnumName(SessionTransitionResult result)
    {
        SessionTransitionFailureText.TryGetMessage(result, out string message);

        foreach (string enumName in Enum.GetNames(typeof(SessionTransitionResult)))
        {
            StringAssert.DoesNotContain(enumName, message);
        }
    }

    [Test]
    public void Success_HasNoMessage()
    {
        Assert.That(SessionTransitionFailureText.TryGetMessage(SessionTransitionResult.Succeeded, out string message), Is.False);
        Assert.That(message, Is.Null);
    }

    [Test]
    public void ServiceUnavailable_IsReportedAsAServiceProblem()
    {
        SessionTransitionFailureText.TryGetMessage(SessionTransitionResult.ServiceUnavailable, out string service);
        SessionTransitionFailureText.TryGetMessage(SessionTransitionResult.ConnectionFailed, out string local);

        StringAssert.Contains("servicio", service);
        Assert.That(service, Is.Not.EqualTo(local));
    }

    [Test]
    public void UndefinedValue_FallsBackToAGenericReadableMessage()
    {
        var undefined = (SessionTransitionResult)999;

        Assert.That(SessionTransitionFailureText.TryGetMessage(undefined, out string message), Is.True);
        StringAssert.DoesNotContain("999", message);
        Assert.That(message, Is.EqualTo(SessionTransitionFailureText.GenericMessage));
    }
}
#endif
