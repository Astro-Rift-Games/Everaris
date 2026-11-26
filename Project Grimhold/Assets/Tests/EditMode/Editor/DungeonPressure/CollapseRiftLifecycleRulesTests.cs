#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.DungeonPressure
{
    public sealed class CollapseRiftLifecycleRulesTests
    {
        private const int SpawnTicks = 30;
        private const int TelegraphTicks = 120;
        private const int EruptionTicks = 36;
        private const int CooldownTicks = 180;

        [Test]
        public void PhaseRules_MatchDoc20Section3_VisibilityAndActivatability()
        {
            CollapseRiftNetState initial = default;
            CollapseRiftNetState secondary = default;
            CollapseRiftLifecycleRules.Initialize(ref initial, CollapseRiftBatch.Stage2Initial);
            CollapseRiftLifecycleRules.Initialize(ref secondary, CollapseRiftBatch.Stage3Secondary);

            // Estabilidad (Normal): sin grietas
            CollapseRiftLifecycleRules.ApplyPhaseTransition(ref initial, DungeonPressurePhase.Normal, 0, SpawnTicks, immediate: true, isClosed: false);
            CollapseRiftLifecycleRules.ApplyPhaseTransition(ref secondary, DungeonPressurePhase.Normal, 0, SpawnTicks, immediate: true, isClosed: false);
            Assert.That(initial.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Hidden));
            Assert.That(initial.IsActivatable, Is.False);
            Assert.That(secondary.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Hidden));
            Assert.That(secondary.IsActivatable, Is.False);

            // Inestabilidad (Reinforcements): lote inicial visible e inofensivo; secundario oculto
            CollapseRiftLifecycleRules.ApplyPhaseTransition(ref initial, DungeonPressurePhase.Reinforcements, 10, SpawnTicks, immediate: false, isClosed: false);
            CollapseRiftLifecycleRules.ApplyPhaseTransition(ref secondary, DungeonPressurePhase.Reinforcements, 10, SpawnTicks, immediate: false, isClosed: false);
            Assert.That(initial.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Spawning));
            Assert.That(initial.IsActivatable, Is.False);
            Assert.That(secondary.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Hidden));

            // Avanzar Spawning -> Dormant
            CollapseRiftLifecycleRules.Step(ref initial, 10 + SpawnTicks, EruptionTicks, CooldownTicks, isClosed: false, out _);
            Assert.That(initial.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Dormant));

            // Grieta inofensiva no reacciona a activaciones
            Assert.That(CollapseRiftLifecycleRules.TryActivate(ref initial, 50, TelegraphTicks, isClosed: false), Is.False);
            Assert.That(initial.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Dormant));

            // Deterioro crítico (CriticalPressure): lote inicial activo, lote secundario aparece inofensivo
            CollapseRiftLifecycleRules.ApplyPhaseTransition(ref initial, DungeonPressurePhase.CriticalPressure, 100, SpawnTicks, immediate: false, isClosed: false);
            CollapseRiftLifecycleRules.ApplyPhaseTransition(ref secondary, DungeonPressurePhase.CriticalPressure, 100, SpawnTicks, immediate: false, isClosed: false);
            Assert.That(initial.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Dormant));
            Assert.That(initial.IsActivatable, Is.True);
            Assert.That(secondary.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Spawning));
            Assert.That(secondary.IsActivatable, Is.False);

            // Colapso (Collapse): todas activas, sin ocultar ni reiniciar
            CollapseRiftLifecycleRules.Step(ref secondary, 100 + SpawnTicks, EruptionTicks, CooldownTicks, isClosed: false, out _);
            CollapseRiftLifecycleRules.ApplyPhaseTransition(ref initial, DungeonPressurePhase.Collapse, 200, SpawnTicks, immediate: false, isClosed: false);
            CollapseRiftLifecycleRules.ApplyPhaseTransition(ref secondary, DungeonPressurePhase.Collapse, 200, SpawnTicks, immediate: false, isClosed: false);
            Assert.That(initial.IsActivatable, Is.True);
            Assert.That(secondary.IsActivatable, Is.True);
            Assert.That(secondary.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Dormant));
        }

        [Test]
        public void FullCycle_DormantToTelegraphToEruptingToCooldownToDormant_IgnoresActivationsWhileOccupied()
        {
            CollapseRiftNetState rift = default;
            CollapseRiftLifecycleRules.Initialize(ref rift, CollapseRiftBatch.Stage2Initial);
            CollapseRiftLifecycleRules.ApplyPhaseTransition(ref rift, DungeonPressurePhase.CriticalPressure, 0, SpawnTicks, immediate: true, isClosed: false);
            Assert.That(rift.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Dormant));
            Assert.That(rift.IsActivatable, Is.True);

            // Primera activación válida gana; segunda simultánea se ignora
            Assert.That(CollapseRiftLifecycleRules.TryActivate(ref rift, 100, TelegraphTicks, isClosed: false), Is.True);
            Assert.That(rift.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Telegraph));
            Assert.That(rift.StateStartTick, Is.EqualTo(100));
            Assert.That(rift.PhaseEndTick, Is.EqualTo(100 + TelegraphTicks));
            Assert.That(CollapseRiftLifecycleRules.TryActivate(ref rift, 100, TelegraphTicks, isClosed: false), Is.False);
            Assert.That(CollapseRiftLifecycleRules.TryActivate(ref rift, 150, TelegraphTicks, isClosed: false), Is.False);
            Assert.That(rift.PhaseEndTick, Is.EqualTo(100 + TelegraphTicks));

            // Telegraph -> Erupting
            CollapseRiftLifecycleRules.Step(ref rift, 100 + TelegraphTicks, EruptionTicks, CooldownTicks, isClosed: false, out bool enteredErupting);
            Assert.That(enteredErupting, Is.True);
            Assert.That(rift.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Erupting));
            Assert.That(rift.EffectApplied, Is.False);
            Assert.That(CollapseRiftLifecycleRules.TryActivate(ref rift, 230, TelegraphTicks, isClosed: false), Is.False);

            // Erupting -> Cooldown
            CollapseRiftLifecycleRules.Step(ref rift, 100 + TelegraphTicks + EruptionTicks, EruptionTicks, CooldownTicks, isClosed: false, out enteredErupting);
            Assert.That(enteredErupting, Is.False);
            Assert.That(rift.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Cooldown));
            Assert.That(CollapseRiftLifecycleRules.TryActivate(ref rift, 300, TelegraphTicks, isClosed: false), Is.False);

            // Cooldown -> Dormant
            CollapseRiftLifecycleRules.Step(ref rift, 100 + TelegraphTicks + EruptionTicks + CooldownTicks, EruptionTicks, CooldownTicks, isClosed: false, out enteredErupting);
            Assert.That(rift.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Dormant));

            // Ya disponible de nuevo
            Assert.That(CollapseRiftLifecycleRules.TryActivate(ref rift, 600, TelegraphTicks, isClosed: false), Is.True);
            Assert.That(rift.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Telegraph));
        }

        [Test]
        public void PhaseTransitionDuringEruption_PreservesOngoingCycleWithoutReset()
        {
            CollapseRiftNetState rift = default;
            CollapseRiftLifecycleRules.Initialize(ref rift, CollapseRiftBatch.Stage2Initial);
            CollapseRiftLifecycleRules.ApplyPhaseTransition(ref rift, DungeonPressurePhase.CriticalPressure, 0, SpawnTicks, immediate: true, isClosed: false);
            Assert.That(CollapseRiftLifecycleRules.TryActivate(ref rift, 100, TelegraphTicks, isClosed: false), Is.True);
            CollapseRiftLifecycleRules.Step(ref rift, 100 + TelegraphTicks, EruptionTicks, CooldownTicks, isClosed: false, out _);
            Assert.That(rift.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Erupting));

            int expectedEndTick = rift.PhaseEndTick;
            CollapseRiftLifecycleRules.ApplyPhaseTransition(ref rift, DungeonPressurePhase.Collapse, 230, SpawnTicks, immediate: false, isClosed: false);

            Assert.That(rift.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Erupting));
            Assert.That(rift.PhaseEndTick, Is.EqualTo(expectedEndTick));
        }

        [Test]
        public void EnvironmentalKnockbackProtection_BlocksConsecutivePushesWithinWindow_AndAllowsAfterExpiry()
        {
            var tracker = new EnvironmentalKnockbackProtectionTracker();
            var targetA = new EntityId(101);
            var targetB = new EntityId(102);
            const int windowTicks = 90;

            Assert.That(tracker.CanApplyEnvironmentalKnockback(targetA, 1000, windowTicks), Is.True);
            tracker.RecordEnvironmentalKnockbackApplied(targetA, 1000);

            // Target A blocked inside [1000, 1089]
            Assert.That(tracker.CanApplyEnvironmentalKnockback(targetA, 1000, windowTicks), Is.False);
            Assert.That(tracker.CanApplyEnvironmentalKnockback(targetA, 1050, windowTicks), Is.False);
            Assert.That(tracker.CanApplyEnvironmentalKnockback(targetA, 1089, windowTicks), Is.False);

            // Target B unaffected
            Assert.That(tracker.CanApplyEnvironmentalKnockback(targetB, 1050, windowTicks), Is.True);

            // Target A eligible again at 1090
            Assert.That(tracker.CanApplyEnvironmentalKnockback(targetA, 1090, windowTicks), Is.True);

            // Window <= 0 disables protection
            Assert.That(tracker.CanApplyEnvironmentalKnockback(targetA, 1001, 0), Is.True);
        }

        [Test]
        public void HazardIdUtility_ProducesStableNonZeroDisjointIds_AndRadialDirection()
        {
            EntityId id0 = CollapseHazardIdUtility.ForFloorRift(42u, 0);
            EntityId id1 = CollapseHazardIdUtility.ForFloorRift(42u, 1);
            EntityId id0Again = CollapseHazardIdUtility.ForFloorRift(42u, 0);

            Assert.That(id0.Value, Is.Not.EqualTo(0));
            Assert.That(id0.Value, Is.LessThan(0));
            Assert.That(id0, Is.EqualTo(id0Again));
            Assert.That(id0, Is.Not.EqualTo(id1));

            Vector2 dir = CollapseHazardIdUtility.ResolveRadialKnockbackDirection(new Vector2(2f, 3f), new Vector2(5f, 3f));
            Assert.That(dir.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(dir.y, Is.EqualTo(0f).Within(0.001f));

            Vector2 fallback = CollapseHazardIdUtility.ResolveRadialKnockbackDirection(new Vector2(2f, 3f), new Vector2(2f, 3f));
            Assert.That(fallback.sqrMagnitude, Is.EqualTo(1f).Within(0.001f));
        }
    }
}
#endif
