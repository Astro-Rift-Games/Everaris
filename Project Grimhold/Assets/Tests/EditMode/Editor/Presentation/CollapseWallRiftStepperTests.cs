#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace Tests.EditMode.Presentation
{
    public sealed class CollapseWallRiftStepperTests
    {
        private static CollapseWallRiftStepper CreateStepper(float frameIntervalSeconds = 0.2f, float phaseOffsetSeconds = 0f)
        {
            return new CollapseWallRiftStepper(
                spawnFrameCount: 7,
                stage1FrameCount: 1,
                stage2FrameCount: 7,
                stage3FrameCount: 7,
                stage4FrameCount: 8,
                frameIntervalSeconds: frameIntervalSeconds,
                phaseOffsetSeconds: phaseOffsetSeconds);
        }

        [Test]
        public void Stage1Normal_HasNoWallRiftsVisible()
        {
            var stepper = CreateStepper();
            stepper.ApplyDungeonPhase(DungeonPressurePhase.Normal, immediate: false);

            Assert.That(stepper.IsVisible, Is.False);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Hidden));
        }

        [Test]
        public void Stage2Reinforcements_PlaysStage0Spawn_ThenRestsOnStage1()
        {
            var stepper = CreateStepper(frameIntervalSeconds: 0.1f);
            stepper.ApplyDungeonPhase(DungeonPressurePhase.Reinforcements, immediate: false);

            Assert.That(stepper.IsVisible, Is.True);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Spawning));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(0));

            stepper.Tick(0.35f);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Spawning));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(3));

            // Complete 7 frames (0.7s total) -> rests on Stage1
            stepper.Tick(0.36f);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Dormant));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(0));

            // Stays on Stage1 even as time continues
            stepper.Tick(2.0f);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Dormant));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(0));
        }

        [Test]
        public void Stage2Reinforcements_LateJoinImmediate_SkipsSpawnAndShowsStage1()
        {
            var stepper = CreateStepper(frameIntervalSeconds: 0.1f);
            stepper.ApplyDungeonPhase(DungeonPressurePhase.Reinforcements, immediate: true);

            Assert.That(stepper.IsVisible, Is.True);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Dormant));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(0));
        }

        [Test]
        public void Stage3CriticalPressure_LoopsStage1PlusStage2_AtConfiguredFrameInterval()
        {
            var stepper = CreateStepper(frameIntervalSeconds: 0.25f);
            stepper.ApplyDungeonPhase(DungeonPressurePhase.CriticalPressure, immediate: true);

            // Total sequence: Stage1 (1 frame) + Stage2 (7 frames) = 8 frames
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Dormant));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(0));

            // After 1 interval (0.25s) -> Stage2 frame 0
            stepper.Tick(0.26f);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Telegraph));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(0));

            // After 6 more intervals (total 7 intervals) -> Stage2 frame 6
            stepper.Tick(1.5f);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Telegraph));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(6));

            // After 1 more interval (total 8 intervals) -> wraps back to Stage1 frame 0
            stepper.Tick(0.25f);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Dormant));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(0));
        }

        [Test]
        public void Stage4Collapse_LoopsStage2PlusStage3PlusStage4_AtConfiguredFrameInterval()
        {
            var stepper = CreateStepper(frameIntervalSeconds: 0.1f);
            stepper.ApplyDungeonPhase(DungeonPressurePhase.Collapse, immediate: true);

            // Total sequence: Stage2 (7) + Stage3 (7) + Stage4 (8) = 22 frames
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Telegraph));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(0));

            // Advance 7 frames -> Stage3 (Erupting) frame 0
            stepper.Tick(0.71f);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Erupting));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(0));

            // Advance 7 more frames (14 total) -> Stage4 (Cooldown) frame 0
            stepper.Tick(0.70f);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Cooldown));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(0));

            // Advance 7 more frames (21 total) -> Stage4 frame 7
            stepper.Tick(0.70f);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Cooldown));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(7));

            // Advance 1 more frame (22 total) -> wraps back to Stage2 frame 0 (never Stage1 in Etapa 4)
            stepper.Tick(0.10f);
            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Telegraph));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(0));
        }

        [Test]
        public void FrameIntervalSeconds_CanBeAdjustedDynamicallyFromInspector()
        {
            var stepper = CreateStepper(frameIntervalSeconds: 0.5f);
            stepper.ApplyDungeonPhase(DungeonPressurePhase.CriticalPressure, immediate: true);

            stepper.SetFrameIntervalSeconds(0.1f);
            stepper.Tick(0.11f);

            Assert.That(stepper.CurrentStage, Is.EqualTo(CollapseRiftVisualState.Telegraph));
            Assert.That(stepper.CurrentStageFrameIndex, Is.EqualTo(0));
        }
    }
}
#endif
