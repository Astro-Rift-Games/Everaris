#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using System.Reflection;

public sealed class RaidHostReturnWaitTextTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(16)]
    public void PendingHostWithConnectedPeers_ShowsReasonAndRemainingCount(int remaining)
    {
        Assert.That(RaidHostReturnWaitText.TryGetMessage(true, true, remaining, out string message), Is.True);
        Assert.That(message, Is.EqualTo($"Esperando a que los demás participantes salgan de la expedición ({remaining} restantes)."));
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public void NoPendingWait_DoesNotShowNotice(bool pending, bool connected)
    {
        Assert.That(RaidHostReturnWaitText.TryGetMessage(pending, connected, 2, out string message), Is.False);
        Assert.That(message, Is.Null);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void RetainedRoutingWithoutActiveCount_DoesNotInventAZeroOrNegativeCount(int remaining)
    {
        Assert.That(RaidHostReturnWaitText.TryGetMessage(true, true, remaining, out string message), Is.True);
        Assert.That(message, Is.EqualTo("Esperando a que los demás participantes salgan de la expedición."));
    }

    [Test]
    public void Coordinator_PendingGetterIsReadOnlyAndDoesNotConsumeTheRequest()
    {
        var root = new GameObject(nameof(RaidHostReturnWaitTextTests));
        try
        {
            var coordinator = root.AddComponent<SessionConnectionCoordinator>();
            var property = typeof(SessionConnectionCoordinator).GetProperty("IsHostResultsReturnPending");
            Assert.That(property, Is.Not.Null);
            Assert.That(property.CanWrite, Is.False);
            Assert.That(property.GetValue(coordinator), Is.False);
            typeof(SessionConnectionCoordinator).GetField("_hostResultsReturnRequested", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(coordinator, true);
            Assert.That(property.GetValue(coordinator), Is.True);
            Assert.That(property.GetValue(coordinator), Is.True);
        }
        finally { Object.DestroyImmediate(root); }
    }
}
#endif
