#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.DungeonPressure
{
    public sealed class CollapseWaveSchedulerTests
    {
        [Test]
        public void GlobalWaves_DisabledInNormalAndReinforcements_EnabledInCriticalAndCollapse()
        {
            Assert.That(CollapseWaveScheduler.AreGlobalWavesEnabled(DungeonPressurePhase.Normal), Is.False);
            Assert.That(CollapseWaveScheduler.AreGlobalWavesEnabled(DungeonPressurePhase.Reinforcements), Is.False);
            Assert.That(CollapseWaveScheduler.AreGlobalWavesEnabled(DungeonPressurePhase.CriticalPressure), Is.True);
            Assert.That(CollapseWaveScheduler.AreGlobalWavesEnabled(DungeonPressurePhase.Collapse), Is.True);
        }

        [Test]
        public void ComputeNextWaveIntervalTicks_RespectsPhaseRanges_IsDeterministic_AndIntensifiesInCollapse()
        {
            var criticalRange = new Vector2(18f, 24f);
            var collapseRange = new Vector2(9f, 13f);
            const float dt = 1f / 60f;

            for (int seq = 0; seq < 10; seq++)
            {
                int critA = CollapseWaveScheduler.ComputeNextWaveIntervalTicks(
                    DungeonPressurePhase.CriticalPressure, criticalRange, collapseRange, seed: 777, waveSequence: seq, deltaTime: dt);
                int critB = CollapseWaveScheduler.ComputeNextWaveIntervalTicks(
                    DungeonPressurePhase.CriticalPressure, criticalRange, collapseRange, seed: 777, waveSequence: seq, deltaTime: dt);
                int col = CollapseWaveScheduler.ComputeNextWaveIntervalTicks(
                    DungeonPressurePhase.Collapse, criticalRange, collapseRange, seed: 777, waveSequence: seq, deltaTime: dt);

                Assert.That(critA, Is.EqualTo(critB));
                Assert.That(critA, Is.InRange(Mathf.CeilToInt(18f / dt), Mathf.CeilToInt(24f / dt)));
                Assert.That(col, Is.InRange(Mathf.CeilToInt(9f / dt), Mathf.CeilToInt(13f / dt)));
                Assert.That(col, Is.LessThan(critA));
            }
        }

        [Test]
        public void SelectEligibleRifts_SelectsOnlyActivatableDormant_IgnoresHarmlessAndOccupied_AndEnforcesOverlapLimits()
        {
            var states = new List<CollapseRiftNetState>
            {
                MakeState(CollapseRiftAuthoritativeStage.Dormant, activatable: true),   // 0: eligible
                MakeState(CollapseRiftAuthoritativeStage.Dormant, activatable: false),  // 1: harmless -> skip
                MakeState(CollapseRiftAuthoritativeStage.Telegraph, activatable: true), // 2: occupied -> skip
                MakeState(CollapseRiftAuthoritativeStage.Erupting, activatable: true),  // 3: occupied -> skip
                MakeState(CollapseRiftAuthoritativeStage.Cooldown, activatable: true),  // 4: occupied -> skip
                MakeState(CollapseRiftAuthoritativeStage.Dormant, activatable: true),   // 5: too close to #0 (dist 1.0 < 3.0) -> skip
                MakeState(CollapseRiftAuthoritativeStage.Dormant, activatable: true),   // 6: eligible (dist 10.0)
                MakeState(CollapseRiftAuthoritativeStage.Dormant, activatable: true)    // 7: eligible (dist 20.0)
            };

            var positions = new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(5f, 0f),
                new Vector2(50f, 0f),
                new Vector2(60f, 0f),
                new Vector2(70f, 0f),
                new Vector2(1f, 0f),
                new Vector2(10f, 0f),
                new Vector2(20f, 0f)
            };

            var selected = new List<int>();
            // No concurrency cap, min spacing = 3.0
            CollapseWaveScheduler.SelectEligibleRiftIndicesForWave(states, positions, maxConcurrentActiveRifts: 0, minSimultaneousRiftSpacing: 3.0f, selected);
            Assert.That(selected, Is.EqualTo(new[] { 0, 6, 7 }));

            // Concurrency cap = 3 (already 2 active: #2 Telegraph and #3 Erupting -> only 1 more allowed!)
            CollapseWaveScheduler.SelectEligibleRiftIndicesForWave(states, positions, maxConcurrentActiveRifts: 3, minSimultaneousRiftSpacing: 3.0f, selected);
            Assert.That(selected, Is.EqualTo(new[] { 0 }));
        }

        private static CollapseRiftNetState MakeState(CollapseRiftAuthoritativeStage stage, bool activatable)
        {
            return new CollapseRiftNetState
            {
                Stage = stage,
                IsActivatable = activatable
            };
        }
    }
}
#endif
