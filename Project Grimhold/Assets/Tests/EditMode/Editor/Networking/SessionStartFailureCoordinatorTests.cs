using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

public sealed class SessionStartFailureCoordinatorTests
{
    [TestCase(ShutdownReason.PhotonCloudTimeout, SessionTransitionResult.ServiceUnavailable)]
    [TestCase(ShutdownReason.MaxCcuReached, SessionTransitionResult.ServiceUnavailable)]
    [TestCase(ShutdownReason.Error, SessionTransitionResult.RecoveryFailed)]
    public void FailedTownRecovery_PublishesClassifiedFailureOnce(ShutdownReason reason, SessionTransitionResult expected)
    {
        var owner = new GameObject(nameof(SessionStartFailureCoordinatorTests));
        owner.AddComponent<HubSessionLauncher>();
        owner.AddComponent<FusionSessionLauncher>();
        try
        {
            var coordinator = owner.AddComponent<SessionConnectionCoordinator>();
            var finish = typeof(SessionConnectionCoordinator).GetMethod("EnterRecoveryFailed", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = (SessionTransitionResult)finish.Invoke(coordinator, new object[] { (ShutdownReason?)reason });
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(coordinator.State, Is.EqualTo(SessionConnectionState.Failed));
            Assert.That(coordinator.TryConsumeLastTransitionFailure(out var failure), Is.True);
            Assert.That(failure, Is.EqualTo(expected));
            Assert.That(coordinator.TryConsumeLastTransitionFailure(out _), Is.False);
        }
        finally { Object.DestroyImmediate(owner); }
    }

    [TestCase(SessionTransitionResult.ServiceUnavailable)]
    [TestCase(SessionTransitionResult.ConnectionFailed)]
    public void SuccessfulTownRecovery_PreservesOriginalFailureOnce(SessionTransitionResult expected)
    {
        var owner = new GameObject(nameof(SessionStartFailureCoordinatorTests));
        owner.AddComponent<HubSessionLauncher>();
        owner.AddComponent<FusionSessionLauncher>();
        try
        {
            var coordinator = owner.AddComponent<SessionConnectionCoordinator>();
            typeof(SessionConnectionCoordinator).GetMethod("CompleteTownEntry", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(coordinator, new object[] { (SessionTransitionResult?)expected });
            Assert.That(coordinator.TryConsumeLastTransitionFailure(out var failure), Is.True);
            Assert.That(failure, Is.EqualTo(expected));
            Assert.That(coordinator.TryConsumeLastTransitionFailure(out _), Is.False);
            Assert.That(coordinator.ActiveTicket, Is.Null);
        }
        finally { Object.DestroyImmediate(owner); }
    }
}
