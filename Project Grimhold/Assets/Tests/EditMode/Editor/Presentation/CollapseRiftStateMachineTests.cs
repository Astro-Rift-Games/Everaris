#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace Tests.EditMode.Presentation
{
    public sealed class CollapseRiftStateMachineTests
    {
        private static CollapseRiftTimingSettings CreateTiming(float phaseOffsetSeconds = 0f)
        {
            return new CollapseRiftTimingSettings(
                spawnFrameCount: 7,
                spawnFps: 14f,
                telegraphFrameCount: 7,
                telegraphFps: 7f,
                telegraphDurationSeconds: 2.0f,
                eruptionFrameCount: 7,
                eruptionFps: 14f,
                cooldownFrameCount: 8,
                cooldownFps: 16f,
                phaseOffsetSeconds: phaseOffsetSeconds);
        }

        [Test]
        public void Stage1Normal_HidesBothFloorBatches_AndDisallowsActivation()
        {
            var batch1 = new CollapseRiftStateMachine(CollapseRiftBatch.Stage2Initial, CreateTiming());
            var batch2 = new CollapseRiftStateMachine(CollapseRiftBatch.Stage3Secondary, CreateTiming());

            batch1.ApplyDungeonPhase(DungeonPressurePhase.Normal, immediate: false);
            batch2.ApplyDungeonPhase(DungeonPressurePhase.Normal, immediate: false);

            Assert.That(batch1.State, Is.EqualTo(CollapseRiftVisualState.Hidden));
            Assert.That(batch1.IsActivatable, Is.False);
            Assert.That(batch1.TryTriggerActivation(), Is.False);

            Assert.That(batch2.State, Is.EqualTo(CollapseRiftVisualState.Hidden));
            Assert.That(batch2.IsActivatable, Is.False);
            Assert.That(batch2.TryTriggerActivation(), Is.False);
        }

        [Test]
        public void Stage2Reinforcements_SpawnsBatch1WithoutActivation_AndKeepsBatch2Hidden()
        {
            var batch1 = new CollapseRiftStateMachine(CollapseRiftBatch.Stage2Initial, CreateTiming());
            var batch2 = new CollapseRiftStateMachine(CollapseRiftBatch.Stage3Secondary, CreateTiming());

            batch1.ApplyDungeonPhase(DungeonPressurePhase.Reinforcements, immediate: false);
            batch2.ApplyDungeonPhase(DungeonPressurePhase.Reinforcements, immediate: false);

            Assert.That(batch1.State, Is.EqualTo(CollapseRiftVisualState.Spawning));
            Assert.That(batch1.CurrentFrameIndex, Is.EqualTo(0));
            Assert.That(batch1.IsActivatable, Is.False);
            Assert.That(batch2.State, Is.EqualTo(CollapseRiftVisualState.Hidden));

            // Advance through 7 spawn frames at 14 fps = 0.5s total
            batch1.Tick(0.25f);
            Assert.That(batch1.State, Is.EqualTo(CollapseRiftVisualState.Spawning));
            Assert.That(batch1.CurrentFrameIndex, Is.EqualTo(3));

            batch1.Tick(0.26f);
            Assert.That(batch1.State, Is.EqualTo(CollapseRiftVisualState.Dormant));
            Assert.That(batch1.CurrentFrameIndex, Is.EqualTo(0));
            Assert.That(batch1.TryTriggerActivation(), Is.False, "Stage 2 floor rifts must not activate.");
        }

        [Test]
        public void Stage3CriticalPressure_ActivatesBatch1_AndSpawnsBatch2WithoutActivation()
        {
            var batch1 = new CollapseRiftStateMachine(CollapseRiftBatch.Stage2Initial, CreateTiming());
            var batch2 = new CollapseRiftStateMachine(CollapseRiftBatch.Stage3Secondary, CreateTiming());

            batch1.ApplyDungeonPhase(DungeonPressurePhase.Reinforcements, immediate: true);
            batch2.ApplyDungeonPhase(DungeonPressurePhase.Reinforcements, immediate: true);

            batch1.ApplyDungeonPhase(DungeonPressurePhase.CriticalPressure, immediate: false);
            batch2.ApplyDungeonPhase(DungeonPressurePhase.CriticalPressure, immediate: false);

            Assert.That(batch1.State, Is.EqualTo(CollapseRiftVisualState.Dormant));
            Assert.That(batch1.IsActivatable, Is.True);
            Assert.That(batch1.TryTriggerActivation(), Is.True);
            Assert.That(batch1.State, Is.EqualTo(CollapseRiftVisualState.Telegraph));

            Assert.That(batch2.State, Is.EqualTo(CollapseRiftVisualState.Spawning));
            Assert.That(batch2.IsActivatable, Is.False);
            batch2.Tick(0.6f);
            Assert.That(batch2.State, Is.EqualTo(CollapseRiftVisualState.Dormant));
            Assert.That(batch2.TryTriggerActivation(), Is.False, "New Stage 3 floor rifts remain dormant until Stage 4.");
        }

        [Test]
        public void Stage4Collapse_ActivatesAllFloorRifts_FromStage2AndStage3()
        {
            var batch1 = new CollapseRiftStateMachine(CollapseRiftBatch.Stage2Initial, CreateTiming());
            var batch2 = new CollapseRiftStateMachine(CollapseRiftBatch.Stage3Secondary, CreateTiming());

            batch1.ApplyDungeonPhase(DungeonPressurePhase.CriticalPressure, immediate: true);
            batch2.ApplyDungeonPhase(DungeonPressurePhase.CriticalPressure, immediate: true);

            batch1.ApplyDungeonPhase(DungeonPressurePhase.Collapse, immediate: false);
            batch2.ApplyDungeonPhase(DungeonPressurePhase.Collapse, immediate: false);

            Assert.That(batch1.IsActivatable, Is.True);
            Assert.That(batch2.IsActivatable, Is.True);
            Assert.That(batch1.TryTriggerActivation(), Is.True);
            Assert.That(batch2.TryTriggerActivation(), Is.True);
        }

        [Test]
        public void ActiveCycle_ExecutesTelegraphLoop_Eruption_Cooldown_Dormant_AndIgnoresBusyRetrigger()
        {
            var rift = new CollapseRiftStateMachine(CollapseRiftBatch.Stage2Initial, CreateTiming());
            rift.ApplyDungeonPhase(DungeonPressurePhase.CriticalPressure, immediate: true);

            Assert.That(rift.TryTriggerActivation(), Is.True);
            Assert.That(rift.State, Is.EqualTo(CollapseRiftVisualState.Telegraph));

            // Telegraph loops across 7 frames at 7 fps (1s per loop) over 2.0s duration
            rift.Tick(0.5f);
            Assert.That(rift.State, Is.EqualTo(CollapseRiftVisualState.Telegraph));
            Assert.That(rift.CurrentFrameIndex, Is.EqualTo(3));
            Assert.That(rift.TryTriggerActivation(), Is.False, "Busy rift in Telegraph must ignore activation.");

            rift.Tick(1.0f); // 1.5s elapsed -> wraps around loop
            Assert.That(rift.State, Is.EqualTo(CollapseRiftVisualState.Telegraph));
            Assert.That(rift.CurrentFrameIndex, Is.EqualTo(3));

            // Finish Telegraph -> Erupting (7 frames at 14 fps = 0.5s)
            rift.Tick(0.51f);
            Assert.That(rift.State, Is.EqualTo(CollapseRiftVisualState.Erupting));
            Assert.That(rift.TryTriggerActivation(), Is.False, "Busy rift in Erupting must ignore activation.");

            // Finish Erupting -> Cooldown (8 frames at 16 fps = 0.5s)
            rift.Tick(0.51f);
            Assert.That(rift.State, Is.EqualTo(CollapseRiftVisualState.Cooldown));
            Assert.That(rift.TryTriggerActivation(), Is.False, "Busy rift in Cooldown must ignore activation.");

            // Finish Cooldown -> Dormant
            rift.Tick(0.51f);
            Assert.That(rift.State, Is.EqualTo(CollapseRiftVisualState.Dormant));
            Assert.That(rift.TryTriggerActivation(), Is.True, "Dormant active rift can trigger again.");
        }

        [Test]
        public void TelegraphPhaseOffset_DesynchronizesNeighboringRifts()
        {
            var riftA = new CollapseRiftStateMachine(CollapseRiftBatch.Stage2Initial, CreateTiming(phaseOffsetSeconds: 0f));
            var riftB = new CollapseRiftStateMachine(CollapseRiftBatch.Stage2Initial, CreateTiming(phaseOffsetSeconds: 0.43f));

            riftA.ApplyDungeonPhase(DungeonPressurePhase.Collapse, immediate: true);
            riftB.ApplyDungeonPhase(DungeonPressurePhase.Collapse, immediate: true);

            riftA.TryTriggerActivation();
            riftB.TryTriggerActivation();
            riftA.Tick(0.05f);
            riftB.Tick(0.05f);

            Assert.That(riftA.CurrentFrameIndex, Is.Not.EqualTo(riftB.CurrentFrameIndex));
        }

        [Test]
        public void ImmediatePhaseSync_ForLateJoinOrReconnect_EntersDormantWithoutReplayingSpawnOrEruption()
        {
            var batch1 = new CollapseRiftStateMachine(CollapseRiftBatch.Stage2Initial, CreateTiming());
            var batch2 = new CollapseRiftStateMachine(CollapseRiftBatch.Stage3Secondary, CreateTiming());

            batch1.ApplyDungeonPhase(DungeonPressurePhase.Collapse, immediate: true);
            batch2.ApplyDungeonPhase(DungeonPressurePhase.Collapse, immediate: true);

            Assert.That(batch1.State, Is.EqualTo(CollapseRiftVisualState.Dormant));
            Assert.That(batch2.State, Is.EqualTo(CollapseRiftVisualState.Dormant));
            Assert.That(batch1.IsActivatable, Is.True);
            Assert.That(batch2.IsActivatable, Is.True);
        }

        [Test]
        public void DeterministicSeed_IsStableForSameRaidId_AndDiffersAcrossRaids()
        {
            int seedA1 = CollapseRiftPhaseRules.ComputeDeterministicSeed("raid-abc-001");
            int seedA2 = CollapseRiftPhaseRules.ComputeDeterministicSeed("raid-abc-001");
            int seedB = CollapseRiftPhaseRules.ComputeDeterministicSeed("raid-abc-002");

            Assert.That(seedA1, Is.EqualTo(seedA2));
            Assert.That(seedA1, Is.Not.EqualTo(seedB));
        }
    }
}
#endif
