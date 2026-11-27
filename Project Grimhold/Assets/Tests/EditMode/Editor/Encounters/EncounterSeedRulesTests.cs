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
}
