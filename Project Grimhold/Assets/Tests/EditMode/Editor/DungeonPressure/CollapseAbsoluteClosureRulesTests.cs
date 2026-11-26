#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace Tests.EditMode.DungeonPressure
{
    public sealed class CollapseAbsoluteClosureRulesTests
    {
        private const int SpawnTicks = 30;
        private const int TelegraphTicks = 120;
        private const int EruptionTicks = 36;
        private const int CooldownTicks = 180;

        [Test]
        public void AbsoluteClosure_CancelsTelegraphAndDisablesDormantSpawningCooldown()
        {
            CollapseRiftNetState telegraph = MakeRift(CollapseRiftAuthoritativeStage.Telegraph, activatable: true, startTick: 100, endTick: 220);
            CollapseRiftNetState dormant = MakeRift(CollapseRiftAuthoritativeStage.Dormant, activatable: true, startTick: 0, endTick: 0);
            CollapseRiftNetState spawning = MakeRift(CollapseRiftAuthoritativeStage.Spawning, activatable: false, startTick: 100, endTick: 130);
            CollapseRiftNetState cooldown = MakeRift(CollapseRiftAuthoritativeStage.Cooldown, activatable: true, startTick: 100, endTick: 280);

            CollapseRiftLifecycleRules.ApplyAbsoluteClosure(ref telegraph, currentTick: 150);
            CollapseRiftLifecycleRules.ApplyAbsoluteClosure(ref dormant, currentTick: 150);
            CollapseRiftLifecycleRules.ApplyAbsoluteClosure(ref spawning, currentTick: 150);
            CollapseRiftLifecycleRules.ApplyAbsoluteClosure(ref cooldown, currentTick: 150);

            Assert.That(telegraph.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Disabled));
            Assert.That(telegraph.IsActivatable, Is.False);
            Assert.That(dormant.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Disabled));
            Assert.That(spawning.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Disabled));
            Assert.That(cooldown.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Disabled));

            // Stepping after closure never transitions a cancelled telegraph into Erupting
            CollapseRiftLifecycleRules.Step(ref telegraph, currentTick: 500, EruptionTicks, CooldownTicks, isClosed: true, out bool enteredErupting);
            Assert.That(enteredErupting, Is.False);
            Assert.That(telegraph.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Disabled));

            // No new activations allowed after closure
            Assert.That(CollapseRiftLifecycleRules.TryActivate(ref dormant, 200, TelegraphTicks, isClosed: true), Is.False);
        }

        [Test]
        public void AbsoluteClosure_AllowsInFlightEruptionToFinishOnce_ThenDisablesWithoutCooldown()
        {
            CollapseRiftNetState erupting = MakeRift(CollapseRiftAuthoritativeStage.Erupting, activatable: true, startTick: 200, endTick: 236);
            erupting.EffectApplied = false;

            CollapseRiftLifecycleRules.ApplyAbsoluteClosure(ref erupting, currentTick: 210);

            // Doc 20 §11 exception: ongoing eruption remains Erupting until completion
            Assert.That(erupting.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Erupting));
            Assert.That(erupting.IsActivatable, Is.False);
            Assert.That(erupting.PhaseEndTick, Is.EqualTo(236));

            // Still erupting at tick 235
            CollapseRiftLifecycleRules.Step(ref erupting, currentTick: 235, EruptionTicks, CooldownTicks, isClosed: true, out _);
            Assert.That(erupting.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Erupting));

            // At end tick 236: transitions directly to Disabled (never Cooldown or Dormant)
            CollapseRiftLifecycleRules.Step(ref erupting, currentTick: 236, EruptionTicks, CooldownTicks, isClosed: true, out _);
            Assert.That(erupting.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Disabled));

            // Cannot re-activate afterwards
            Assert.That(CollapseRiftLifecycleRules.TryActivate(ref erupting, 300, TelegraphTicks, isClosed: true), Is.False);
        }

        private static CollapseRiftNetState MakeRift(
            CollapseRiftAuthoritativeStage stage,
            bool activatable,
            int startTick,
            int endTick)
        {
            return new CollapseRiftNetState
            {
                Stage = stage,
                IsActivatable = activatable,
                StateStartTick = startTick,
                PhaseEndTick = endTick
            };
        }
    }
}
#endif
