#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

/// <summary>Dev grant Editor tool (odd/tasks/dev-grant-tools.md S3-S8): arguments, outcome and Play Mode reread.</summary>
public sealed class DevGrantToolTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    [Test]
    public void BuildArguments_MapsUserPresetItemsAndGold()
    {
        var args = DevGrantTool.BuildArguments(" tester_a ", "upgrade-gear",
            new[] { ("RES_RUNE_FRAGMENT", 10), ("rapier", 1) }, 5000);
        Assert.That(args, Is.EqualTo(new[]
        {
            "tools/dev-grant/grant.js", "--user", "tester_a", "--preset", "upgrade-gear",
            "--item", "RES_RUNE_FRAGMENT:10", "--item", "rapier:1", "--gold", "5000"
        }));
        Assert.That(DevGrantTool.BuildArguments("u", null, Array.Empty<(string, int)>(), 1),
            Is.EqualTo(new[] { "tools/dev-grant/grant.js", "--user", "u", "--gold", "1" }));
    }

    [Test]
    public void BuildArgumentsForCharacter_UsesTheSessionCharacterId()
    {
        Assert.That(DevGrantTool.BuildArgumentsForCharacter("6ae4138546721f4a437e5612", "upgrade-materials", null, 0),
            Is.EqualTo(new[] { "tools/dev-grant/grant.js", "--character", "6ae4138546721f4a437e5612", "--preset", "upgrade-materials" }));
        Assert.That(() => DevGrantTool.BuildArgumentsForCharacter("", "upgrade-gear", null, 0),
            Throws.ArgumentException.With.Message.EqualTo("Entra en Play Mode con un personaje para usar los botones rápidos."));
        Assert.That(() => DevGrantTool.BuildArgumentsForCharacter("not-an-id", "upgrade-gear", null, 0), Throws.ArgumentException);
    }

    [TestCase("", "upgrade-gear", 0, "Indica el usuario de la cuenta.")]
    [TestCase("u", null, 0, "Elige un preset, ítems u oro.")]
    [TestCase("u", null, 100001, "El oro debe estar entre 0 y 100000.")]
    public void BuildArguments_RejectsIncompleteRequests(string user, string preset, int gold, string message) =>
        Assert.That(() => DevGrantTool.BuildArguments(user, preset, Array.Empty<(string, int)>(), gold),
            Throws.ArgumentException.With.Message.EqualTo(message));

    [TestCase("", 1)]
    [TestCase("RES_RUNE_FRAGMENT", 0)]
    [TestCase("RES_RUNE_FRAGMENT", 1000)]
    [TestCase("bad:id", 1)]
    public void BuildArguments_RejectsInvalidItems(string lootId, int amount) =>
        Assert.That(() => DevGrantTool.BuildArguments("u", null, new[] { (lootId, amount) }, 0), Throws.ArgumentException);

    [Test]
    public void Interpret_SuccessReturnsTheScriptSummary()
    {
        var outcome = DevGrantTool.Interpret(0,
            "[dev-grant] OK revision 9, gold 5100, granted [{\"lootId\":\"RES_RUNE_FRAGMENT\",\"amount\":20}]\n[dev-grant] Re-enter the Town (or relaunch) so the game syncs the new Inventory.\n", "");
        Assert.That(outcome.Success, Is.True);
        Assert.That(outcome.Message, Is.EqualTo("OK revision 9, gold 5100, granted [{\"lootId\":\"RES_RUNE_FRAGMENT\",\"amount\":20}]"));
    }

    [Test]
    public void Interpret_FailureReturnsTheScriptError()
    {
        var outcome = DevGrantTool.Interpret(1, "", "[dev-grant] INVENTORY_FULL: Inventory is full.\n");
        Assert.That(outcome.Success, Is.False);
        Assert.That(outcome.Message, Is.EqualTo("INVENTORY_FULL: Inventory is full."));
        Assert.That(DevGrantTool.Interpret(9, "", "").Message, Is.EqualTo("El script terminó con código 9 sin mensaje."));
    }

    [Test]
    public async Task Run_SuccessInPlayMode_RereadsTheProfileOnce()
    {
        var calls = new List<string[]>();
        int rereads = 0;
        var tool = new DevGrantTool(args => { calls.Add(args); return Task.FromResult((0, "[dev-grant] OK revision 9, gold 5100, granted []\n", "")); },
            () => { rereads++; return Task.FromResult(true); });

        string message = await tool.RunAsync(new[] { "tools/dev-grant/grant.js", "--user", "u", "--gold", "1" });

        Assert.That(calls.Count, Is.EqualTo(1));
        Assert.That(rereads, Is.EqualTo(1));
        Assert.That(message, Is.EqualTo("OK revision 9, gold 5100, granted []. Inventario sincronizado."));
    }

    [Test]
    public async Task Run_WithoutPlayMode_TellsToReenterTheTown_AndFailureDoesNotReread()
    {
        var offline = new DevGrantTool(_ => Task.FromResult((0, "[dev-grant] OK revision 2, gold 1, granted []\n", "")), null);
        Assert.That(await offline.RunAsync(new[] { "x" }),
            Is.EqualTo("OK revision 2, gold 1, granted []. Entra al Pueblo para ver los objetos."));

        int rereads = 0;
        var failing = new DevGrantTool(_ => Task.FromResult((1, "", "[dev-grant] NOT_LOCAL: MONGODB_URI is not local.\n")),
            () => { rereads++; return Task.FromResult(true); });
        Assert.That(await failing.RunAsync(new[] { "x" }), Is.EqualTo("Error: NOT_LOCAL: MONGODB_URI is not local."));
        Assert.That(rereads, Is.Zero);
    }

    [Test]
    public async Task NodeProcess_RunsTheRealScript_AndCapturesItsValidationError()
    {
        // No database is touched: the script rejects the arguments before loading any configuration.
        var (exitCode, stdout, stderr) = await DevGrantTool.RunNodeAsync(RepoRoot, "node",
            new[] { "tools/dev-grant/grant.js", "--gold", "1" });
        Assert.That(exitCode, Is.EqualTo(1), stdout + stderr);
        Assert.That(DevGrantTool.Interpret(exitCode, stdout, stderr).Message, Is.EqualTo("VALIDATION_FAILED: --user <username> is required (or --character <id>)."));
    }
}
#endif
