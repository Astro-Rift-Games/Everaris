using NUnit.Framework;

public sealed class EncounterSeedRulesTests
{
    private const ulong SessionSeed = 0xA1B2C3D4E5F60718UL;

    [Test]
    public void Derive_SameInputs_ReturnsSameSeed()
    {
        Assert.That(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7)),
            Is.EqualTo(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7))));
    }

    [Test]
    public void Derive_DiffersPerEncounterId()
    {
        Assert.That(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7)),
            Is.Not.EqualTo(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(8))));
    }

    [Test]
    public void Derive_DiffersPerSceneLoadGeneration()
    {
        Assert.That(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7)),
            Is.Not.EqualTo(EncounterSeedRules.Derive(SessionSeed, 4, new EncounterId(7))));
    }

    [Test]
    public void Derive_DiffersPerSessionSeed()
    {
        Assert.That(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7)),
            Is.Not.EqualTo(EncounterSeedRules.Derive(SessionSeed + 1UL, 3, new EncounterId(7))));
    }

    [Test]
    public void Derive_DoesNotShareTheLootContainerStream()
    {
        Assert.That(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7)),
            Is.Not.EqualTo(LootContainerSeedRules.Derive(SessionSeed, 3, 7)));
    }

    [Test]
    public void Derive_RejectsNoneEncounterId()
    {
        Assert.Throws<System.ArgumentException>(() => EncounterSeedRules.Derive(SessionSeed, 3, EncounterId.None));
    }

    [Test]
    public void CreateRandom_SameInputs_ProducesSameSequence()
    {
        System.Random first = EncounterSeedRules.CreateRandom(SessionSeed, 2, new EncounterId(5));
        System.Random second = EncounterSeedRules.CreateRandom(SessionSeed, 2, new EncounterId(5));
        for (int i = 0; i < 8; i++)
        {
            Assert.That(first.Next(), Is.EqualTo(second.Next()));
        }
    }

    [Test]
    public void CreateRandom_DifferentEncounterIds_ProduceDifferentSequences()
    {
        System.Random first = EncounterSeedRules.CreateRandom(SessionSeed, 2, new EncounterId(5));
        System.Random second = EncounterSeedRules.CreateRandom(SessionSeed, 2, new EncounterId(6));
        bool differs = false;
        for (int i = 0; i < 8 && !differs; i++)
        {
            differs = first.Next() != second.Next();
        }

        Assert.That(differs, Is.True);
    }


    // Per reactivation cycle (TASK-330 T5, S3)

    [Test]
    public void Derive_CycleZero_IsTheInitialActivationSeedUnchanged()
    {
        Assert.That(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7), 0),
            Is.EqualTo(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7))));
    }

    [Test]
    public void Derive_DiffersPerCycle()
    {
        var seen = new System.Collections.Generic.HashSet<ulong>();
        for (int cycle = 0; cycle < 16; cycle++)
        {
            Assert.That(seen.Add(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7), cycle)), Is.True, $"cycle {cycle} repeated an earlier seed");
        }
    }

    [Test]
    public void Derive_SameCycle_IsReproducible()
    {
        Assert.That(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7), 2),
            Is.EqualTo(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7), 2)));
    }

    [Test]
    public void Derive_CycleDoesNotCollideWithAnotherRoomOrGeneration()
    {
        ulong cycleOne = EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7), 1);
        Assert.That(cycleOne, Is.Not.EqualTo(EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(8), 0)));
        Assert.That(cycleOne, Is.Not.EqualTo(EncounterSeedRules.Derive(SessionSeed, 4, new EncounterId(7), 0)));
    }

    [Test]
    public void Derive_RejectsANegativeCycle()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => EncounterSeedRules.Derive(SessionSeed, 3, new EncounterId(7), -1));
    }

    [Test]
    public void CreateRandom_CycleZeroMatchesTheCycleLessOverloadAndOtherCyclesDiffer()
    {
        System.Random plain = EncounterSeedRules.CreateRandom(SessionSeed, 2, new EncounterId(5));
        System.Random cycleZero = EncounterSeedRules.CreateRandom(SessionSeed, 2, new EncounterId(5), 0);
        System.Random cycleOne = EncounterSeedRules.CreateRandom(SessionSeed, 2, new EncounterId(5), 1);
        bool differs = false;
        for (int i = 0; i < 8; i++)
        {
            int expected = plain.Next();
            Assert.That(cycleZero.Next(), Is.EqualTo(expected));
            differs |= cycleOne.Next() != expected;
        }

        Assert.That(differs, Is.True);
    }
}
