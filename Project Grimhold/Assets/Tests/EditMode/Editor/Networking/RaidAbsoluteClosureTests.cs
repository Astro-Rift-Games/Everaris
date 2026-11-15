using NUnit.Framework;
using UnityEditor;

public sealed class RaidAbsoluteClosureTests
{
    [TestCase(RaidParticipantState.Raiding, true)]
    [TestCase(RaidParticipantState.Extracted, false)]
    [TestCase(RaidParticipantState.Defeated, false)]
    [TestCase(RaidParticipantState.Aborted, false)]
    public void Selection_OnlyUnresolvedRaiders_RegardlessOfConnectivityOrDowned(
        RaidParticipantState state, bool expected)
    {
        // Connectivity and Downed do not change the functional participant state.
        Assert.That(AbsoluteClosureRules.ShouldDefeat(state), Is.EqualTo(expected));
    }

    [Test]
    public void DefaultAsset_TotalPlusCollapse_Equals17Minutes()
    {
        var config = AssetDatabase.LoadAssetAtPath<DungeonPressureConfig>(
            "Assets/Scriptable Objects/DefaultDungeonPressureConfig.asset");
        Assert.That(config, Is.Not.Null);
        Assert.That(config.Validate(out string error), Is.True, error);
        Assert.That(config.TotalDurationSeconds, Is.EqualTo(900));
        Assert.That(config.CollapseDurationSeconds, Is.EqualTo(120));
        Assert.That(config.TotalDurationSeconds + config.CollapseDurationSeconds, Is.EqualTo(17 * 60));
        Assert.That(config.ReinforcementsThresholdSeconds, Is.EqualTo(240));
        Assert.That(config.CriticalPressureThresholdSeconds, Is.EqualTo(60));
    }

    [TestCase(RaidParticipantState.Raiding, true, false, true)]
    [TestCase(RaidParticipantState.Raiding, true, true, true)]
    [TestCase(RaidParticipantState.Raiding, false, false, false)]
    [TestCase(RaidParticipantState.Defeated, true, false, false)]
    [TestCase(RaidParticipantState.Extracted, true, true, false)]
    [TestCase(RaidParticipantState.Aborted, true, false, false)]
    public void AvatarlessDefeat_OnlyAuthorityResolvesAnUnresolvedRaiderOnce(
        RaidParticipantState initial, bool authority, bool disconnected, bool expected)
    {
        Assert.That(AbsoluteClosureRules.TryResolveDefeat(authority, initial, 7, disconnected,
            out var state, out int sequence, out var cause), Is.EqualTo(expected));
        Assert.That(state, Is.EqualTo(expected ? RaidParticipantState.Defeated : initial));
        Assert.That(sequence, Is.EqualTo(expected ? 8 : 7));
        Assert.That(cause, Is.EqualTo(!expected ? ExpeditionProgressionFinalizationCause.None
            : disconnected ? ExpeditionProgressionFinalizationCause.DefeatWhileDisconnectedConfirmed
            : ExpeditionProgressionFinalizationCause.DefeatConfirmed));
        if (expected)
        {
            Assert.That(AbsoluteClosureRules.TryResolveDefeat(authority, state, sequence, disconnected,
                out var again, out int againSequence, out _), Is.False);
            Assert.That(again, Is.EqualTo(state));
            Assert.That(againSequence, Is.EqualTo(sequence));
        }
    }
}
