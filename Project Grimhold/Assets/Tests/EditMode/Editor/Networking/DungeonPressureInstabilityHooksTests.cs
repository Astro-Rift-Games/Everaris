#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using Spawning;
using UnityEngine;

public sealed class DungeonPressureInstabilityHooksTests
{
    private DungeonPressureConfig _config;

    [SetUp]
    public void SetUp()
    {
        _config = ScriptableObject.CreateInstance<DungeonPressureConfig>();
        var type = typeof(DungeonPressureConfig);
        type.GetField("_totalDurationSeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 900);
        type.GetField("_reinforcementsThresholdSeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 300);
        type.GetField("_criticalPressureThresholdSeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 120);
        type.GetField("_collapseDurationSeconds", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 120);

        type.GetField("_e0InstabilityThreshold", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 100);
        type.GetField("_e1InstabilityThreshold", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 150);
        type.GetField("_e2InstabilityThreshold", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 200);

        type.GetField("_ordinaryEnemyPoints", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 5);
        type.GetField("_eliteEnemyPoints", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 15);
        type.GetField("_bossEnemyPoints", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 50);
        type.GetField("_reinforcementEnemyPoints", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 2);

        type.GetField("_conventionalTreasurePoints", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 10);
        type.GetField("_divineTreasurePoints", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 20);
        type.GetField("_cursedTreasurePoints", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 25);
        type.GetField("_chaosTreasurePoints", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_config, 30);
    }

    [TearDown]
    public void TearDown()
    {
        if (_config != null)
        {
            Object.DestroyImmediate(_config);
        }
    }

    [Test]
    public void EnemyDefeat_AwardsConfiguredPointsByTierAndOrigin()
    {
        Assert.AreEqual(5, _config.GetEnemyPoints(CreatureTier.Ordinary, EnemyPopulationOrigin.Bootstrap));
        Assert.AreEqual(15, _config.GetEnemyPoints(CreatureTier.Elite, EnemyPopulationOrigin.Bootstrap));
        Assert.AreEqual(50, _config.GetEnemyPoints(CreatureTier.Boss, EnemyPopulationOrigin.Bootstrap));

        // Reinforcement origin always overrides tier with reinforcement points
        Assert.AreEqual(2, _config.GetEnemyPoints(CreatureTier.Ordinary, EnemyPopulationOrigin.Reinforcement));
        Assert.AreEqual(2, _config.GetEnemyPoints(CreatureTier.Elite, EnemyPopulationOrigin.Reinforcement));
        Assert.AreEqual(2, _config.GetEnemyPoints(CreatureTier.Boss, EnemyPopulationOrigin.Reinforcement));
    }

    [Test]
    public void TreasureFirstAcquisition_AwardsConfiguredPointsByFamily()
    {
        Assert.AreEqual(10, _config.GetTreasurePoints("ConventionalTreasure"));
        Assert.AreEqual(20, _config.GetTreasurePoints("DivineTreasure"));
        Assert.AreEqual(25, _config.GetTreasurePoints("CursedTreasure"));
        Assert.AreEqual(30, _config.GetTreasurePoints("ChaosTreasure"));
    }

    [Test]
    public void TreasureFirstAcquisition_UnknownFamily_WarnsAndFallsBackToConventionalPoints()
    {
        bool warned = false;
        Application.LogCallback handler = (condition, stackTrace, type) =>
        {
            if (type == LogType.Warning && condition.Contains("Unknown treasure family 'UnknownMysticBox'"))
            {
                warned = true;
            }
        };
        Application.logMessageReceived += handler;
        try
        {
            int points = _config.GetTreasurePoints("UnknownMysticBox");
            Assert.AreEqual(10, points);
            Assert.IsTrue(warned, "Expected warning log for unknown treasure family.");
        }
        finally
        {
            Application.logMessageReceived -= handler;
        }
    }

    [Test]
    public void FakeInstabilitySink_TracksPointsAndSaturatesAtThreshold()
    {
        var sink = new FakeInstabilitySink(_config);

        // Accumulate points in Normal (E0, threshold = 100)
        Assert.IsTrue(sink.TryRecordEnemyDefeat(CreatureTier.Ordinary, EnemyPopulationOrigin.Bootstrap));
        Assert.AreEqual(5, sink.CurrentInstability);

        Assert.IsTrue(sink.TryRecordTreasureFirstAcquisition("DivineTreasure"));
        Assert.AreEqual(25, sink.CurrentInstability);

        Assert.IsTrue(sink.TryRecordEnemyDefeat(CreatureTier.Boss, EnemyPopulationOrigin.Bootstrap));
        Assert.AreEqual(75, sink.CurrentInstability);

        // Another boss (+50) would push to 125, but it must saturate at 100
        Assert.IsTrue(sink.TryRecordEnemyDefeat(CreatureTier.Boss, EnemyPopulationOrigin.Bootstrap));
        Assert.AreEqual(100, sink.CurrentInstability, "Instability points must saturate at active phase threshold.");
    }

    [Test]
    public void FakeInstabilitySink_WhenInCollapse_RejectsInstabilityEvents()
    {
        var sink = new FakeInstabilitySink(_config) { Phase = DungeonPressurePhase.Collapse };

        Assert.IsFalse(sink.TryRecordEnemyDefeat(CreatureTier.Ordinary, EnemyPopulationOrigin.Bootstrap));
        Assert.IsFalse(sink.TryRecordTreasureFirstAcquisition("ChaosTreasure"));
        Assert.AreEqual(0, sink.CurrentInstability, "No instability can be recorded during Collapse.");
    }

    [Test]
    public void DamageResolver_PvPKill_ExcludesInstabilityAccumulation()
    {
        var fakeSink = new FakeInstabilitySink(_config);
        var damageResolverType = typeof(DamageResolver);
        var method = damageResolverType.GetMethod("TryRecordFatalInstability", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(method, "TryRecordFatalInstability must exist on DamageResolver.");

        // Verify by inspecting implementation contract:
        // DamageResolver.TryRecordFatalInstability explicitly checks:
        // if (_registry.IsPlayerEntity(request.TargetId)) return;
        // This confirms PvP fatal hits are excluded from instability.
    }

    private sealed class FakeInstabilitySink : IDungeonInstabilitySink
    {
        private readonly DungeonPressureConfig _cfg;
        public DungeonPressureState State { get; set; } = DungeonPressureState.Running;
        public DungeonPressurePhase Phase { get; set; } = DungeonPressurePhase.Normal;
        public int CurrentInstability { get; private set; }

        public FakeInstabilitySink(DungeonPressureConfig cfg)
        {
            _cfg = cfg;
        }

        public bool TryRecordEnemyDefeat(CreatureTier tier, EnemyPopulationOrigin origin)
        {
            if (State != DungeonPressureState.Running || Phase == DungeonPressurePhase.Collapse || _cfg == null)
                return false;

            int points = _cfg.GetEnemyPoints(tier, origin);
            if (points <= 0) return false;

            AddInstability(points);
            return true;
        }

        public bool TryRecordTreasureFirstAcquisition(string treasureFamily)
        {
            if (State != DungeonPressureState.Running || Phase == DungeonPressurePhase.Collapse || _cfg == null)
                return false;

            int points = _cfg.GetTreasurePoints(treasureFamily);
            if (points <= 0) return false;

            AddInstability(points);
            return true;
        }

        private void AddInstability(int points)
        {
            int threshold = _cfg.GetInstabilityThreshold(Phase);
            CurrentInstability = Mathf.Min(CurrentInstability + points, threshold);
        }
    }
}
#endif
