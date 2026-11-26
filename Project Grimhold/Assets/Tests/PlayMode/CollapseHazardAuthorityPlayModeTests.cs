#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.DungeonPressure
{
    /// <summary>
    /// End-to-end Fusion PlayMode verification for US-91:
    /// - TASK-335: Authoritative floor rift cycle, damage + radial knockback to Player & PvE,
    ///             environmental knockback protection window, Doc 19 indirect causal attribution, no colliders.
    /// - TASK-336: Global rupture wave warning preceding local telegraph, zero direct wave damage, occupied ignored.
    /// - TASK-339: Absolute closure cancels telegraphs and lets in-flight eruptions finish once then stay Disabled.
    /// </summary>
    public sealed class CollapseHazardAuthorityPlayModeTests
    {
        private const string PlayerPrefabGuid = "fea3a7b256f965a4eb9b965832939741";
        private const string GreenSlimePrefabGuid = "5deca87613df0fa409d98702aec643d4";
        private const string MatchControllerPrefabPath = "Assets/Prefabs/NetworkMatchController.prefab";

        private NetworkRunner _runner;
        private CollapseHazardDirector _director;
        private DungeonPressureController _pressure;
        private CollapseHazardConfig _hazardConfig;

        private static int CharacterMask => LayerMask.GetMask("Character");

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_runner != null)
            {
                var shutdown = _runner.Shutdown();
                while (!shutdown.IsCompleted) yield return null;
                Object.Destroy(_runner.gameObject);
            }

            if (_hazardConfig != null)
            {
                Object.Destroy(_hazardConfig);
                _hazardConfig = null;
            }
        }

        [UnityTest]
        public IEnumerator Task335_Eruption_DamagesAndKnocksBackPlayerAndEnemyOnce_WithoutColliders()
        {
            var riftPos = new Vector2(15f, 0f);
            yield return BeginMatch(
                new[] { new CollapseRiftPlacement(riftPos, Vector2Int.zero, CollapseRiftSurfaceType.Floor, CollapseRiftBatch.Stage2Initial, 0f) },
                eruptionDamage: 15f,
                eruptionKnockback: 20f,
                telegraphSeconds: 0.25f,
                eruptionSeconds: 0.25f,
                cooldownSeconds: 0.5f,
                kbProtectionSeconds: 1.5f);

            var playerObj = SpawnPrefab(PlayerPrefabGuid, new Vector3(riftPos.x + 0.4f, riftPos.y, 0f));
            var player = playerObj.GetComponent<PlayerCharacter>();
            var playerMove = playerObj.GetComponent<PlayerMovementNetworkController>();
            var enemyObj = SpawnPrefab(GreenSlimePrefabGuid, new Vector3(riftPos.x - 0.4f, riftPos.y, 0f));
            var enemy = enemyObj.GetComponent<EnemyCharacter>();
            Physics2D.SyncTransforms();

            float playerStartHp = player.Health;
            float enemyStartHp = enemy.Health;
            Vector2 playerStartPos = player.transform.position;

            // Advance to CriticalPressure so Stage2Initial batch is activatable
            yield return AdvanceToPhase(DungeonPressurePhase.CriticalPressure);
            Assert.That(_director.TryGetFloorRiftState(0, out CollapseRiftNetState state), Is.True);
            Assert.That(state.IsActivatable, Is.True);
            Assert.That(state.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Dormant));

            // Director must never attach physics colliders to block movement
            Assert.That(_director.GetComponentInChildren<Collider2D>(true), Is.Null);

            // Activate rift -> Telegraph -> Erupting -> Cooldown
            Assert.That(_director.TryActivateFloorRift(0), Is.True);
            Assert.That(_director.TryActivateFloorRift(0), Is.False, "Occupied rift must ignore duplicate activation.");

            yield return WaitUntil(() => _director.TryGetFloorRiftState(0, out var s) && s.Stage == CollapseRiftAuthoritativeStage.Erupting);
            yield return WaitTicks(4);

            // Damage applied ONCE per cycle to both player and PvE enemy
            Assert.That(player.Health, Is.LessThan(playerStartHp));
            Assert.That(enemy.Health, Is.LessThan(enemyStartHp));
            float playerHpAfterEruption = player.Health;
            float enemyHpAfterEruption = enemy.Health;

            // Knockback pushed player radially away (+X)
            Assert.That(playerMove.KnockbackSequence, Is.GreaterThan(0));

            yield return WaitUntil(() => _director.TryGetFloorRiftState(0, out var s) && s.Stage == CollapseRiftAuthoritativeStage.Cooldown);
            yield return WaitTicks(3);

            // Still only one hit during the entire eruption phase
            Assert.That(player.Health, Is.EqualTo(playerHpAfterEruption).Within(0.01f));
            Assert.That(enemy.Health, Is.EqualTo(enemyHpAfterEruption).Within(0.01f));
            Assert.That(player.transform.position.x, Is.GreaterThan(playerStartPos.x + 0.05f));
        }

        [UnityTest]
        public IEnumerator Task335_ConsecutiveEruptionsWithinProtectionWindow_AccumulateDamageButSuppressSecondKnockback()
        {
            var riftA = new Vector2(20f, 0f);
            var riftB = new Vector2(20.1f, 0f);
            yield return BeginMatch(
                new[]
                {
                    new CollapseRiftPlacement(riftA, Vector2Int.zero, CollapseRiftSurfaceType.Floor, CollapseRiftBatch.Stage2Initial, 0f),
                    new CollapseRiftPlacement(riftB, Vector2Int.one, CollapseRiftSurfaceType.Floor, CollapseRiftBatch.Stage2Initial, 0f)
                },
                eruptionDamage: 10f,
                eruptionKnockback: 20f,
                telegraphSeconds: 0.15f,
                eruptionSeconds: 0.15f,
                cooldownSeconds: 2.0f,
                kbProtectionSeconds: 3.0f);

            var playerObj = SpawnPrefab(PlayerPrefabGuid, new Vector3(riftA.x + 0.2f, riftA.y, 0f));
            var player = playerObj.GetComponent<PlayerCharacter>();
            var playerMove = playerObj.GetComponent<PlayerMovementNetworkController>();
            Physics2D.SyncTransforms();

            yield return AdvanceToPhase(DungeonPressurePhase.CriticalPressure);

            // Trigger rift A first
            Assert.That(_director.TryActivateFloorRift(0), Is.True);
            yield return WaitUntil(() => _director.TryGetFloorRiftState(0, out var s) && s.Stage == CollapseRiftAuthoritativeStage.Erupting);
            yield return WaitTicks(3);

            int kbSeqAfterFirst = playerMove.KnockbackSequence;
            float hpAfterFirst = player.Health;
            Assert.That(kbSeqAfterFirst, Is.GreaterThan(0));

            // Reposition player over rift B while still inside the 3s protection window and trigger rift B
            player.transform.position = new Vector3(riftB.x + 0.2f, riftB.y, 0f);
            Physics2D.SyncTransforms();
            Assert.That(_director.TryActivateFloorRift(1), Is.True);
            yield return WaitUntil(() => _director.TryGetFloorRiftState(1, out var s) && s.Stage == CollapseRiftAuthoritativeStage.Erupting);
            yield return WaitTicks(3);

            // Second eruption STILL deals damage, but knockback is suppressed by environmental protection window
            Assert.That(player.Health, Is.LessThan(hpAfterFirst));
            Assert.That(playerMove.KnockbackSequence, Is.EqualTo(kbSeqAfterFirst));
        }

        [UnityTest]
        public IEnumerator Task335_PlayerKnockedIntoRift_ResolvesIndirectCausalAttributionPerDoc19()
        {
            var riftPos = new Vector2(25f, 0f);
            yield return BeginMatch(
                new[] { new CollapseRiftPlacement(riftPos, Vector2Int.zero, CollapseRiftSurfaceType.Floor, CollapseRiftBatch.Stage2Initial, 0f) },
                eruptionDamage: 500f,
                eruptionKnockback: 0f,
                telegraphSeconds: 0.15f,
                eruptionSeconds: 0.2f,
                cooldownSeconds: 1.0f,
                kbProtectionSeconds: 0f);

            var pusherObj = SpawnPrefab(PlayerPrefabGuid, new Vector3(riftPos.x - 6f, 0f, 0f));
            var pusher = pusherObj.GetComponent<PlayerCharacter>();
            var victimObj = SpawnPrefab(PlayerPrefabGuid, new Vector3(riftPos.x - 1.8f, 0f, 0f));
            var victim = victimObj.GetComponent<PlayerCharacter>();
            var victimMove = victimObj.GetComponent<PlayerMovementNetworkController>();
            Physics2D.SyncTransforms();

            yield return AdvanceToPhase(DungeonPressurePhase.CriticalPressure);

            // Pusher knocks victim toward the rift (outside rift initially, entering while displaced)
            Assert.That(victimMove.TryApplyKnockback(Vector2.right, 35f, true), Is.EqualTo(KnockbackDecision.Started));
            victim.HazardExposureCausality.RecordAcceptedKnockback(pusher.Id);

            // Move victim inside the rift during the knockback and wait for director presence reporting
            victim.transform.position = new Vector3(riftPos.x, riftPos.y, 0f);
            Physics2D.SyncTransforms();
            yield return WaitTicks(2);

            EntityId riftHazardId = CollapseHazardIdUtility.ForFloorRift(_director.Object.Id.Raw, 0);
            Assert.That(victim.HazardExposureCausality.TryResolveHazardCauser(riftHazardId, out EntityId resolvedPusher), Is.True);
            Assert.That(resolvedPusher, Is.EqualTo(pusher.Id));
        }

        [UnityTest]
        public IEnumerator Task336_GlobalWave_EmitsWarningBeforeTelegraph_DealsZeroDirectDamage_AndActivatesOnlyDormant()
        {
            var riftA = new Vector2(30f, 0f);
            var riftB = new Vector2(40f, 0f);
            yield return BeginMatch(
                new[]
                {
                    new CollapseRiftPlacement(riftA, Vector2Int.zero, CollapseRiftSurfaceType.Floor, CollapseRiftBatch.Stage2Initial, 0f),
                    new CollapseRiftPlacement(riftB, Vector2Int.one, CollapseRiftSurfaceType.Floor, CollapseRiftBatch.Stage3Secondary, 0f)
                },
                eruptionDamage: 15f,
                eruptionKnockback: 10f,
                telegraphSeconds: 0.5f,
                eruptionSeconds: 0.25f,
                cooldownSeconds: 1.0f,
                kbProtectionSeconds: 0f,
                globalWaveWarningSeconds: 0.25f,
                criticalWaveRange: new Vector2(0.85f, 0.95f),
                collapseWaveRange: new Vector2(0.5f, 0.6f));

            var playerObj = SpawnPrefab(PlayerPrefabGuid, new Vector3(riftA.x, riftA.y, 0f));
            var player = playerObj.GetComponent<PlayerCharacter>();
            Physics2D.SyncTransforms();
            float startHp = player.Health;

            // In CriticalPressure, Batch 1 (index 0) is active; Batch 2 (index 1) is harmless
            yield return AdvanceToPhase(DungeonPressurePhase.CriticalPressure);

            // Wait for global wave warning to start
            yield return WaitUntil(() => _director.WaveSequence >= 1 && _director.WaveWarningActive);

            // During global warning: no rift has started Telegraph yet, and player took zero wave damage
            Assert.That(_director.TryGetFloorRiftState(0, out var s0Warn), Is.True);
            Assert.That(s0Warn.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Dormant));
            Assert.That(player.Health, Is.EqualTo(startHp));

            // After global warning ends: rift 0 enters Telegraph, harmless rift 1 stays Dormant/Spawning
            yield return WaitUntil(() => !_director.WaveWarningActive);
            Assert.That(_director.TryGetFloorRiftState(0, out var s0After), Is.True);
            Assert.That(_director.TryGetFloorRiftState(1, out var s1After), Is.True);
            Assert.That(s0After.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Telegraph));
            Assert.That(s1After.IsActivatable, Is.False);
            Assert.That(s1After.Stage, Is.Not.EqualTo(CollapseRiftAuthoritativeStage.Telegraph));
            Assert.That(player.Health, Is.EqualTo(startHp), "The wave itself must inflict zero direct damage.");
        }

        [UnityTest]
        public IEnumerator Task339_AbsoluteClosure_CancelsTelegraph_AndLetsInFlightEruptionFinishOnceThenDisable()
        {
            var riftErupting = new Vector2(50f, 0f);
            var riftTelegraph = new Vector2(60f, 0f);
            yield return BeginMatch(
                new[]
                {
                    new CollapseRiftPlacement(riftErupting, Vector2Int.zero, CollapseRiftSurfaceType.Floor, CollapseRiftBatch.Stage2Initial, 0f),
                    new CollapseRiftPlacement(riftTelegraph, Vector2Int.one, CollapseRiftSurfaceType.Floor, CollapseRiftBatch.Stage2Initial, 0f)
                },
                eruptionDamage: 15f,
                eruptionKnockback: 0f,
                telegraphSeconds: 0.2f,
                eruptionSeconds: 0.35f,
                cooldownSeconds: 1.0f,
                kbProtectionSeconds: 0f,
                criticalWaveRange: new Vector2(30f, 35f),
                collapseWaveRange: new Vector2(30f, 35f));

            var enemyInErupting = SpawnPrefab(GreenSlimePrefabGuid, new Vector3(riftErupting.x, riftErupting.y, 0f)).GetComponent<EnemyCharacter>();
            var enemyInTelegraph = SpawnPrefab(GreenSlimePrefabGuid, new Vector3(riftTelegraph.x, riftTelegraph.y, 0f)).GetComponent<EnemyCharacter>();
            Physics2D.SyncTransforms();

            float hpEruptingBefore = enemyInErupting.Health;
            float hpTelegraphBefore = enemyInTelegraph.Health;

            yield return AdvanceToPhase(DungeonPressurePhase.Collapse);

            // Start rift 0 first so it reaches Erupting while rift 1 is still in Telegraph
            Assert.That(_director.TryActivateFloorRift(0), Is.True);
            yield return WaitUntil(() => _director.TryGetFloorRiftState(0, out var s0) && s0.Stage == CollapseRiftAuthoritativeStage.Erupting);
            Assert.That(_director.TryActivateFloorRift(1), Is.True);
            Assert.That(_director.TryGetFloorRiftState(1, out var s1Before), Is.True);
            Assert.That(s1Before.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Telegraph));

            // Force absolute closure right now
            _director.HandleAbsoluteClosure();

            Assert.That((bool)_director.IsClosed, Is.True);
            Assert.That(_director.TryGetFloorRiftState(0, out var s0AtClosure), Is.True);
            Assert.That(_director.TryGetFloorRiftState(1, out var s1AtClosure), Is.True);
            Assert.That(s0AtClosure.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Erupting), "In-flight eruption must complete.");
            Assert.That(s1AtClosure.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Disabled), "Telegraph must be cancelled immediately.");
            Assert.That(enemyInErupting.Health, Is.LessThan(hpEruptingBefore), "In-flight eruption must apply its effect.");
            Assert.That(enemyInTelegraph.Health, Is.EqualTo(hpTelegraphBefore), "Cancelled telegraph must not deal damage.");

            // Wait for eruption duration to end -> transitions straight to Disabled (never Cooldown)
            yield return WaitUntil(() => _director.TryGetFloorRiftState(0, out var s0End) && s0End.Stage == CollapseRiftAuthoritativeStage.Disabled);
            yield return WaitTicks(10);

            Assert.That(_director.TryGetFloorRiftState(0, out var s0Final), Is.True);
            Assert.That(s0Final.Stage, Is.EqualTo(CollapseRiftAuthoritativeStage.Disabled));
            Assert.That(enemyInTelegraph.Health, Is.EqualTo(hpTelegraphBefore));
            Assert.That(_director.TryActivateFloorRift(0), Is.False, "No activations allowed after closure.");
        }

        private IEnumerator BeginMatch(
            IReadOnlyList<CollapseRiftPlacement> customFloorPlacements,
            float eruptionDamage,
            float eruptionKnockback,
            float telegraphSeconds,
            float eruptionSeconds,
            float cooldownSeconds,
            float kbProtectionSeconds,
            float globalWaveWarningSeconds = 0.25f,
            Vector2? criticalWaveRange = null,
            Vector2? collapseWaveRange = null)
        {
            var runnerGo = new GameObject("CollapseHazardTestRunner");
            _runner = runnerGo.AddComponent<NetworkRunner>();
            runnerGo.AddComponent<EntityRegistry>();
            _runner.ProvideInput = false;

            var startTask = _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single,
                SessionName = "collapse-hazards-test-" + Guid.NewGuid().ToString("N"),
                SceneManager = runnerGo.AddComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = runnerGo.AddComponent<NetworkObjectProviderDefault>()
            });
            while (!startTask.IsCompleted) yield return null;
            Assert.That(startTask.Result.Ok, Is.True, startTask.Result.ErrorMessage);

            Assert.That(runnerGo.AddComponent<NetworkSpawnManager>().InitializeForRunner(
                _runner, default, default, Array.Empty<NetworkPrefabRef>(), SessionStartupContext.FreshSession, null), Is.True);

            _hazardConfig = ScriptableObject.CreateInstance<CollapseHazardConfig>();
            _hazardConfig.ConfigureForTests(
                riftConfig: null,
                eruptionRadius: 1.5f,
                eruptionDamage: eruptionDamage,
                damageType: DamageType.Magical,
                eruptionKnockbackForce: eruptionKnockback,
                environmentalKnockbackProtectionSeconds: kbProtectionSeconds,
                telegraphDurationSeconds: telegraphSeconds,
                eruptionDurationSeconds: eruptionSeconds,
                cooldownDurationSeconds: cooldownSeconds,
                globalWaveWarningSeconds: globalWaveWarningSeconds,
                criticalWaveRange: criticalWaveRange ?? new Vector2(20f, 25f),
                collapseWaveRange: collapseWaveRange ?? new Vector2(10f, 12f),
                maxConcurrentActiveRifts: 0,
                minSimultaneousRiftSpacing: 0f,
                targetLayerMask: CharacterMask);

            var matchPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(MatchControllerPrefabPath);
            Assert.That(matchPrefab, Is.Not.Null, $"Missing prefab at {MatchControllerPrefabPath}");

            NetworkObject matchObj = _runner.Spawn(matchPrefab, Vector3.zero, Quaternion.identity);
            _pressure = matchObj.GetComponent<DungeonPressureController>();
            _director = matchObj.GetComponent<CollapseHazardDirector>();
            Assert.That(_pressure, Is.Not.Null);
            Assert.That(_director, Is.Not.Null, "CollapseHazardDirector must be attached to NetworkMatchController.prefab.");

            _director.ConfigureForTests(_hazardConfig, damageResolver: null, customFloorPlacements, pressureController: _pressure);
            yield return WaitTicks(2);
        }

        private IEnumerator AdvanceToPhase(DungeonPressurePhase targetPhase)
        {
            int guard = 0;
            while (_pressure.Phase < targetPhase && guard++ < 8)
            {
                _pressure.AdvancePhase();
                yield return WaitTicks(3);
            }

            Assert.That(_pressure.Phase, Is.EqualTo(targetPhase));
            yield return WaitUntil(() => _director.LastAppliedPhase == targetPhase);
            // Wait for any newly visible rifts to finish Spawning -> Dormant before activating them.
            if (targetPhase >= DungeonPressurePhase.Reinforcements && _director.FloorRiftCount > 0)
            {
                yield return WaitUntil(() =>
                {
                    for (int i = 0; i < _director.FloorRiftCount; i++)
                    {
                        if (_director.TryGetFloorRiftState(i, out var s) && s.Stage == CollapseRiftAuthoritativeStage.Spawning)
                        {
                            return false;
                        }
                    }
                    return true;
                });
            }
            yield return WaitTicks(2);
        }

        private NetworkObject SpawnPrefab(string guid, Vector3 position)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(path);
            Assert.That(prefab, Is.Not.Null, $"Missing prefab for GUID {guid} ({path})");
            NetworkObject spawned = _runner.Spawn(prefab, position, Quaternion.identity);
            foreach (var behaviour in spawned.GetComponents<NetworkBehaviour>())
            {
                if (behaviour is EnemyFSM || behaviour is EnemyCombatAIController)
                {
                    behaviour.enabled = false;
                }
            }
            return spawned;
        }

        private IEnumerator WaitTicks(int count = 2)
        {
            int target = _runner.Tick.Raw + count;
            while (_runner.Tick.Raw < target) yield return null;
        }

        private IEnumerator WaitUntil(Func<bool> predicate, float timeoutSeconds = 5f)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!predicate())
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Timed out waiting for condition.");
                yield return null;
            }
        }
    }
}
#endif
