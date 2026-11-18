#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine;

public sealed class RemoteInventoryMutationGateTests
{
    [Test]
    public async Task RunExclusiveAsync_SerializesOperations()
    {
        var gate = new RemoteInventoryMutationGate();
        var events = new List<string>();
        var releaseFirst = new TaskCompletionSource<bool>();

        var first = gate.RunExclusiveAsync(async () =>
        {
            events.Add("first:start");
            await releaseFirst.Task;
            events.Add("first:end");
            return 1;
        });
        var second = gate.RunExclusiveAsync(() =>
        {
            events.Add("second:start");
            return Task.FromResult(2);
        });

        Assert.That(second.IsCompleted, Is.False);
        Assert.That(events, Is.EqualTo(new[] { "first:start" }));
        releaseFirst.SetResult(true);
        Assert.That(await first, Is.EqualTo(1));
        Assert.That(await second, Is.EqualTo(2));
        Assert.That(events, Is.EqualTo(new[] { "first:start", "first:end", "second:start" }));
    }

    [Test]
    public async Task RunExclusiveAsync_ReleasesTheGateWhenTheOperationThrows()
    {
        var gate = new RemoteInventoryMutationGate();

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.RunExclusiveAsync<int>(() => throw new InvalidOperationException("boom")));

        var next = gate.RunExclusiveAsync(() => Task.FromResult(true));
        Assert.That(next.IsCompleted, Is.True, "A failed operation must not leave the gate held.");
        Assert.That(await next, Is.True);
    }

    [Test]
    public async Task RemoteInventoryService_InventoryMutationsWaitForTheSharedMutationGate()
    {
        var authObject = new GameObject("MutationGateAuth");
        var serviceObject = new GameObject("MutationGateService");
        try
        {
            var auth = authObject.AddComponent<ApplicationAuthContext>();
            // EditMode does not run MonoBehaviour.Awake automatically for this component.
            if (ApplicationAuthContext.Instance != auth)
                typeof(ApplicationAuthContext).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(auth, null);
            Assert.That(ApplicationAuthContext.Instance, Is.SameAs(auth));
            auth.Initialize("test-token", new CharacterData { characterId = "test-profile", name = "Test" }, default);
            var service = serviceObject.AddComponent<RemoteInventoryService>();

            var release = new TaskCompletionSource<bool>();
            var holder = service.MutationGate.RunExclusiveAsync(async () => await release.Task);
            var transfer = service.TransferAllAsync(true);

            Assert.That(transfer.IsCompleted, Is.False, "An inventory mutation must wait for the shared gate.");
            release.SetResult(true);
            await holder;
            var result = await transfer;
            Assert.That(result.success, Is.False);
            Assert.That(result.error.error, Is.EqualTo("INVENTORY_MIGRATION_REQUIRED"));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(serviceObject);
            UnityEngine.Object.DestroyImmediate(authObject);
        }
    }
}
#endif
