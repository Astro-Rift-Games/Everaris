using NUnit.Framework;

/// <summary>TASK-330 T5 (S3): "la fase de Dungeon permite generar", configurable per phase (L8), default every phase.</summary>
public sealed class EncounterGenerationPhaseGateTests
{
    private static readonly DungeonPressurePhase[] Phases =
    {
        DungeonPressurePhase.Normal,
        DungeonPressurePhase.Reinforcements,
        DungeonPressurePhase.CriticalPressure,
        DungeonPressurePhase.Collapse
    };

    [Test]
    public void FlagOf_MapsEachPhaseToItsOwnDistinctFlag()
    {
        Assert.That(EncounterGenerationPhaseGate.FlagOf(DungeonPressurePhase.Normal), Is.EqualTo(EncounterGenerationPhases.Normal));
        Assert.That(EncounterGenerationPhaseGate.FlagOf(DungeonPressurePhase.Reinforcements), Is.EqualTo(EncounterGenerationPhases.Reinforcements));
        Assert.That(EncounterGenerationPhaseGate.FlagOf(DungeonPressurePhase.CriticalPressure), Is.EqualTo(EncounterGenerationPhases.CriticalPressure));
        Assert.That(EncounterGenerationPhaseGate.FlagOf(DungeonPressurePhase.Collapse), Is.EqualTo(EncounterGenerationPhases.Collapse));
    }

    [Test]
    public void All_CoversEveryPhaseOfTheDungeon()
    {
        EncounterGenerationPhases union = EncounterGenerationPhases.None;
        foreach (DungeonPressurePhase phase in Phases) union |= EncounterGenerationPhaseGate.FlagOf(phase);
        Assert.That(union, Is.EqualTo(EncounterGenerationPhases.All));
    }

    [Test]
    public void Allows_DefaultAllPhases_AllowsEveryPhaseWhilePressureRuns()
    {
        foreach (DungeonPressurePhase phase in Phases)
        {
            Assert.That(EncounterGenerationPhaseGate.Allows(EncounterGenerationPhases.All, true, phase), Is.True, phase.ToString());
        }
    }

    [Test]
    public void Allows_ConfiguredSubset_AllowsOnlyThosePhases()
    {
        EncounterGenerationPhases subset = EncounterGenerationPhases.Normal | EncounterGenerationPhases.Reinforcements;
        Assert.That(EncounterGenerationPhaseGate.Allows(subset, true, DungeonPressurePhase.Normal), Is.True);
        Assert.That(EncounterGenerationPhaseGate.Allows(subset, true, DungeonPressurePhase.Reinforcements), Is.True);
        Assert.That(EncounterGenerationPhaseGate.Allows(subset, true, DungeonPressurePhase.CriticalPressure), Is.False);
        Assert.That(EncounterGenerationPhaseGate.Allows(subset, true, DungeonPressurePhase.Collapse), Is.False);
    }

    [Test]
    public void Allows_NoPhaseConfigured_NeverAllows()
    {
        foreach (DungeonPressurePhase phase in Phases)
        {
            Assert.That(EncounterGenerationPhaseGate.Allows(EncounterGenerationPhases.None, true, phase), Is.False, phase.ToString());
        }
    }

    [Test]
    public void Allows_PressureNotRunning_IsClosedForEveryConfiguration()
    {
        foreach (DungeonPressurePhase phase in Phases)
        {
            Assert.That(EncounterGenerationPhaseGate.Allows(EncounterGenerationPhases.All, false, phase), Is.False, phase.ToString());
        }
    }

    [Test]
    public void Allows_AnUndefinedPhaseValue_IsNeverAllowed()
    {
        Assert.That(EncounterGenerationPhaseGate.Allows(EncounterGenerationPhases.All, true, (DungeonPressurePhase)9), Is.False);
    }
}
