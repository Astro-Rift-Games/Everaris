#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using NUnit.Framework;
using Spawning;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using LogType = UnityEngine.LogType;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.Encounters
{
    /// <summary>
    /// TASK-330 T5 (S3, S6) on a real single-peer runner and the productive NetworkMatchController prefab: a resolved room
    /// regenerates its Encounter only under the Doc 10 §9.2 conditions, through the real enemy spawn path. Real Host Migration needs
    /// a multi-process run and is not covered here.
    /// </summary>
    public sealed class EncounterReactivationPlayModeTests
    {
        private const string MatchControllerPrefabPath = "Assets/Prefabs/NetworkMatchController.prefab";
        private const string GreenSlimePrefabGuid = "5deca87613df0fa409d98702aec643d4";
        private const string PlayerGuid = "fea3a7b256f965a4eb9b965832939741";
        private const string ParticipantGuid = "c39d451563bae6e43934008a0dadc6d6";
        private const string PressureConfigPath = "Assets/Scriptable Objects/DefaultDungeonPressureConfig.asset";
        private const int RoomId = 5;
        private const int SecondRoomId = 6;
        private static readonly Vector3 ZoneCenter = new Vector3(100f, 0f, 0f);
        private static readonly Vector3 SecondZoneCenter = new Vector3(200f, 0f, 0f);

        private readonly struct RoomSpec
        {
            public RoomSpec(int id, int minEnemies, int maxEnemies, Vector3 zoneCenter)
            {
                Id = id;
                MinEnemies = minEnemies;
                MaxEnemies = maxEnemies;
                ZoneCenter = zoneCenter;
            }

            public int Id { get; }
            public int MinEnemies { get; }
            public int MaxEnemies { get; }
            public Vector3 ZoneCenter { get; }
        }

        /// <summary>The cadence that keeps the registry's own reactivation pass out of a test that drives the spawn manager directly.</summary>
        private const float NoRegistryCadence = 100000f;

        private NetworkRunner _runner;
        private NetworkSpawnManager _manager;
        private EncounterRegistry _registry;
        private DungeonPressureController _pressure;
        private DungeonPressureConfig _pressureConfig;
        private EnemyFatalDamageSimulationDriver _damage;
        private NetworkObject _participantObject;
        private NetworkObject _avatarObject;
        private PlayerAbilityRuntimeSimulationDriver _simulationDriver;
        private uint _nextFakeEnemyId = 9_000_000u;
        private readonly List<uint> _fakeEnemies = new List<uint>();
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_runner != null && _runner.IsRunning)
            {
                var shutdown = _runner.Shutdown();
                while (!shutdown.IsCompleted) yield return null;
            }

            if (_runner != null) Object.DestroyImmediate(_runner.gameObject);
            foreach (GameObject go in _sceneObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _sceneObjects.Clear();
            _fakeEnemies.Clear();
        }

        // ---- S3: a resolved room regenerates when every condition holds ---------------------------------

        [UnityTest]
        public IEnumerator ResolvedRoom_RespawnsAndBecomesActiveWithOneReactivation()
        {
            yield return Begin(maxReactivations: 1);
            yield return ResolveFirstCycle();

            yield return WaitUntil(() => Status().State == EncounterState.Active && Status().ReactivationCount == 1);

            EncounterStatus status = Status();
            Assert.That(status.AliveMemberCount, Is.InRange(1, 2));
            Assert.That(status.MemberCount, Is.EqualTo(status.AliveMemberCount));
            List<EnemyCharacter> living = LivingMembers();
            Assert.That(living, Has.Count.EqualTo(status.AliveMemberCount), "Every member of the new cycle is a real, living enemy of the room.");
            Assert.That(living.Select(e => e.transform.position).Distinct().Count(), Is.EqualTo(living.Count), "Distinct room markers.");
            foreach (EnemyCharacter enemy in living)
            {
                Assert.That(enemy.EncounterIdValue, Is.EqualTo(RoomId));
                Assert.That(EncounterMemberKeyCodec.TryDecode(enemy.EncounterMemberKey, out int cycle, out uint index), Is.True);
                Assert.That(cycle, Is.EqualTo(1), "Members carry the cycle they belong to.");
                Assert.That(index, Is.InRange(1u, 2u));
                Assert.That(enemy.PopulationOrigin, Is.EqualTo(EnemyPopulationOrigin.Bootstrap), "Room population, not pressure Reinforcements.");
            }

            Assert.That(_manager.PopulationTracker.GetActivePopulationByOrigin(EnemyPopulationOrigin.Reinforcement), Is.EqualTo(0));
            Assert.That(_manager.PopulationTracker.TotalActivePopulation, Is.EqualTo(living.Count), "The new members count against the global population.");
        }

        [UnityTest]
        public IEnumerator ReactivatedRoom_ResolvesFromItsNewMembersAndStopsAtMaxReactivations()
        {
            yield return Begin(maxReactivations: 1);
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Active && Status().ReactivationCount == 1);
            int enemiesBefore = AllRoomEnemies().Count;

            foreach (EnemyCharacter member in LivingMembers()) yield return Kill(member);
            yield return WaitUntil(() => Status().State == EncounterState.Reactivable);
            yield return WaitSeconds(1.2f);

            Assert.That(Status().State, Is.EqualTo(EncounterState.Reactivable), "MaxReactivations was reached: the room never respawns again.");
            Assert.That(Status().ReactivationCount, Is.EqualTo(1));
            Assert.That(AllRoomEnemies(), Has.Count.EqualTo(enemiesBefore), "No enemy was spawned.");
        }

        [UnityTest]
        public IEnumerator MaxReactivationsZero_TheDefault_NeverRespawns()
        {
            yield return Begin(maxReactivations: 0);
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Reactivable);
            int enemiesBefore = AllRoomEnemies().Count;

            yield return WaitSeconds(1.2f);

            Assert.That(Status().State, Is.EqualTo(EncounterState.Reactivable));
            Assert.That(Status().ReactivationCount, Is.EqualTo(0));
            Assert.That(AllRoomEnemies(), Has.Count.EqualTo(enemiesBefore));
        }

        // ---- S3: each condition blocks, and its removal lets the room regenerate ----------------------------

        [UnityTest]
        public IEnumerator PlayerInsideTheGenerationZone_BlocksTheRespawnUntilItLeaves()
        {
            yield return Begin(maxReactivations: 1, withAvatar: true);
            MoveAvatar(ZoneCenter);
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Reactivable);

            yield return WaitSeconds(1.2f);
            Assert.That(Status().State, Is.EqualTo(EncounterState.Reactivable), "A player stands in the zone.");
            Assert.That(Status().ReactivationCount, Is.EqualTo(0));

            MoveAvatar(Vector3.zero);
            yield return WaitUntil(() => Status().State == EncounterState.Active && Status().ReactivationCount == 1);
        }

        [UnityTest]
        public IEnumerator DisallowedDungeonPhase_BlocksTheRespawnUntilThePhaseAllowsIt()
        {
            yield return Begin(maxReactivations: 1);
            SetAllowedPhases(EncounterGenerationPhases.Normal);
            SetPressure(DungeonPressureState.Running, DungeonPressurePhase.CriticalPressure);
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Reactivable);

            yield return WaitSeconds(1.2f);
            Assert.That(Status().State, Is.EqualTo(EncounterState.Reactivable), "CriticalPressure is not an allowed phase.");

            SetPressure(DungeonPressureState.Running, DungeonPressurePhase.Normal);
            yield return WaitUntil(() => Status().State == EncounterState.Active && Status().ReactivationCount == 1);
        }

        [UnityTest]
        public IEnumerator DungeonPressureNotRunning_BlocksTheRespawn()
        {
            yield return Begin(maxReactivations: 1);
            SetPressure(DungeonPressureState.NotStarted, DungeonPressurePhase.Normal);
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Reactivable);

            yield return WaitSeconds(1.2f);
            Assert.That(Status().State, Is.EqualTo(EncounterState.Reactivable), "With no running Dungeon there is no phase that allows generating.");

            SetPressure(DungeonPressureState.Running, DungeonPressurePhase.Normal);
            yield return WaitUntil(() => Status().State == EncounterState.Active && Status().ReactivationCount == 1);
        }

        [UnityTest]
        public IEnumerator InsufficientGlobalCapacity_BlocksTheRespawnUntilCapacityIsFree()
        {
            yield return Begin(maxReactivations: 1);
            FillGlobalCapacity();
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Reactivable);

            yield return WaitSeconds(1.2f);
            Assert.That(_manager.PopulationTracker.GetAvailableGlobalCapacity(_pressureConfig.MaxGlobalEnemies), Is.EqualTo(0));
            Assert.That(Status().State, Is.EqualTo(EncounterState.Reactivable), "No Spawn Population Budget left.");
            Assert.That(AllRoomEnemies().Count(e => e.IsAlive), Is.EqualTo(0));

            FreeGlobalCapacity();
            yield return WaitUntil(() => Status().State == EncounterState.Active && Status().ReactivationCount == 1);
        }

        // ---- S3/S5: member identity across cycles ---------------------------------------------------------

        [UnityTest]
        public IEnumerator DeathOfAnEarlierCycleBody_DoesNotAffectTheNewCycle()
        {
            yield return Begin(maxReactivations: 1);
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Active && Status().ReactivationCount == 1);
            int alive = Status().AliveMemberCount;

            // A body that still carries a first-cycle key (member index 1) dies now: it is not a member of cycle 1.
            EnemyCharacter oldBody = SpawnEnemy(RoomId, 1u);
            yield return Kill(oldBody);

            Assert.That(Status().State, Is.EqualTo(EncounterState.Active));
            Assert.That(Status().AliveMemberCount, Is.EqualTo(alive), "The old body's index collides with a current member but its cycle does not.");

            EnemyCharacter member = LivingMembers()[0];
            yield return Kill(member);
            Assert.That(Status().AliveMemberCount, Is.EqualTo(alive - 1), "A death of the current cycle still counts.");
        }

        [UnityTest]
        public IEnumerator ReconcileWithEarlierCycleBodiesPresent_KeepsTheNewCycleIntact()
        {
            yield return Begin(maxReactivations: 1);
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Active && Status().ReactivationCount == 1);
            int alive = Status().AliveMemberCount;
            NetworkObject[] everyEnemy = AllRoomEnemies().Select(e => e.Object).ToArray();
            Assert.That(everyEnemy.Length, Is.GreaterThan(alive), "The dead bodies of the first cycle are still in the world.");

            _manager.ReconcileEncounterRegistry(everyEnemy);

            Assert.That(Status().State, Is.EqualTo(EncounterState.Active));
            Assert.That(Status().AliveMemberCount, Is.EqualTo(alive), "Dead first-cycle bodies are not unrecorded deaths of the new members.");
        }

        // ---- S3: one pass over several rooms, partial spawns and refused activations -----------------------

        [UnityTest]
        public IEnumerator TwoRoomsReactivatingInOnePass_ShareTheLimitedCapacity_TheSecondStaysReactivable()
        {
            yield return Begin(
                maxReactivations: 1,
                rooms: new[] { new RoomSpec(RoomId, 2, 2, ZoneCenter), new RoomSpec(SecondRoomId, 2, 2, SecondZoneCenter) });
            FillGlobalCapacity(leaveFree: 2);
            SetPressure(DungeonPressureState.NotStarted, DungeonPressurePhase.Normal);
            yield return ResolveFirstCycle(RoomId);
            yield return ResolveFirstCycle(SecondRoomId);
            yield return WaitUntil(() => Status(RoomId).State == EncounterState.Reactivable && Status(SecondRoomId).State == EncounterState.Reactivable);

            SetPressure(DungeonPressureState.Running, DungeonPressurePhase.Normal);
            yield return WaitUntil(() => Status(RoomId).State == EncounterState.Active);
            yield return WaitSeconds(1.2f);

            Assert.That(Status(RoomId).ReactivationCount, Is.EqualTo(1));
            Assert.That(Status(SecondRoomId).State, Is.EqualTo(EncounterState.Reactivable), "The first room used the only free capacity of the pass.");
            Assert.That(Status(SecondRoomId).ReactivationCount, Is.EqualTo(0));
            Assert.That(LivingMembers(SecondRoomId), Is.Empty);
            Assert.That(_manager.PopulationTracker.GetAvailableGlobalCapacity(_pressureConfig.MaxGlobalEnemies), Is.EqualTo(0));

            FreeGlobalCapacity();
            yield return WaitUntil(() => Status(SecondRoomId).State == EncounterState.Active && Status(SecondRoomId).ReactivationCount == 1);
        }

        [UnityTest]
        public IEnumerator PartialSpawnAtLeastMinEnemies_ActivatesTheRoomWithTheSubset()
        {
            yield return Begin(maxReactivations: 1, cadenceSeconds: NoRegistryCadence, rooms: new[] { new RoomSpec(RoomId, 1, 2, ZoneCenter) });
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Reactivable);
            _manager.Test_ReactivationSpawnLimit = 1;

            Assert.That(Pass(out string detail), Is.EqualTo(EncounterReactivationOutcome.Reactivated));

            Assert.That(detail, Does.Contain("only 1 of 2"));
            Assert.That(Status().State, Is.EqualTo(EncounterState.Active));
            Assert.That(Status().ReactivationCount, Is.EqualTo(1));
            Assert.That(Status().MemberCount, Is.EqualTo(1));
            Assert.That(LivingMembers(), Has.Count.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PartialSpawnBelowMinEnemies_DespawnsTheTailAndKeepsTheReactivation()
        {
            yield return Begin(maxReactivations: 1, cadenceSeconds: NoRegistryCadence, rooms: new[] { new RoomSpec(RoomId, 2, 2, ZoneCenter) });
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Reactivable);
            _manager.Test_ReactivationSpawnLimit = 1;

            Assert.That(Pass(out string detail), Is.EqualTo(EncounterReactivationOutcome.SpawnFailed));
            yield return WaitTicks(3);

            Assert.That(detail, Does.Contain("below").And.Contain("MinEnemies"));
            Assert.That(Status().State, Is.EqualTo(EncounterState.Reactivable), "The room stays Reactivable.");
            Assert.That(Status().ReactivationCount, Is.EqualTo(0), "No reactivation is consumed.");
            Assert.That(LivingMembers(), Is.Empty, "The tail was despawned.");
            Assert.That(_manager.PopulationTracker.TotalActivePopulation, Is.EqualTo(0));

            _manager.Test_ReactivationSpawnLimit = -1;
            Assert.That(Pass(out _), Is.EqualTo(EncounterReactivationOutcome.Reactivated), "The same room reactivates once spawning works.");
            Assert.That(Status().ReactivationCount, Is.EqualTo(1));
            Assert.That(LivingMembers(), Has.Count.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator RegistryRefusingTheActivation_DespawnsTheSpawnedEnemiesAndRestoresThePopulation()
        {
            yield return Begin(maxReactivations: 1, cadenceSeconds: NoRegistryCadence, rooms: new[] { new RoomSpec(RoomId, 2, 2, ZoneCenter) });
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Reactivable);
            int population = _manager.PopulationTracker.TotalActivePopulation;
            int spawnedEnemies = AllRoomEnemies().Count;
            // The scene is replaced between the spawn and the activation: the registry has no rooms any more.
            _manager.Test_BeforeReactivationCommit = () => _registry.ResetForSceneChange();

            Assert.That(Pass(out string detail), Is.EqualTo(EncounterReactivationOutcome.ActivationRejected));
            yield return WaitTicks(3);

            Assert.That(detail, Does.Contain("despawned"));
            Assert.That(_manager.PopulationTracker.TotalActivePopulation, Is.EqualTo(population), "The despawned enemies left the population.");
            Assert.That(AllRoomEnemies(), Has.Count.EqualTo(spawnedEnemies), "No enemy of the refused cycle remains.");
        }

        [UnityTest]
        public IEnumerator HostMigrationRecoveryInProgress_SkipsReactivationUntilItCloses()
        {
            yield return Begin(maxReactivations: 1);
            _manager.Test_SetHostMigrationRecoveryState(NetworkSpawnManager.HostMigrationRecoveryWindowState.Open);
            yield return ResolveFirstCycle();

            yield return WaitSeconds(1.2f);
            Assert.That(Status().State, Is.Not.EqualTo(EncounterState.Active), "Nothing regenerates while the recovery window is open.");
            Assert.That(Status().ReactivationCount, Is.EqualTo(0));
            Assert.That(LivingMembers(), Is.Empty);

            _manager.Test_SetHostMigrationRecoveryState(NetworkSpawnManager.HostMigrationRecoveryWindowState.Inactive);
            yield return WaitUntil(() => Status().State == EncounterState.Active && Status().ReactivationCount == 1);
        }

        // ---- S3: which players count as present in the zone ----------------------------------------------

        [UnityTest]
        public IEnumerator DownedPlayerInsideTheZone_StillBlocksTheRespawn()
        {
            yield return Begin(maxReactivations: 1, withAvatar: true);
            MoveAvatar(ZoneCenter);
            var player = _avatarObject.GetComponent<PlayerCharacter>();
            SetField(_avatarObject.GetComponent<PlayerDownedStateNetworkController>(), "_downedDrainPerSecond", 0f);
            yield return InSimulation(() => Assert.That(
                _avatarObject.GetComponent<DamageResolver>().Resolve(new DamageRequest(
                    default, player.Id, 10000f, DamageType.TrueDamage, Vector2.right, player.transform.position, _runner.Tick)).IsApplied, Is.True));
            Assert.That(player.IsDowned, Is.True);
            Assert.That(player.IsAlive, Is.True, "A downed player is still alive.");

            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Reactivable);
            yield return WaitSeconds(1.2f);

            Assert.That(Status().State, Is.EqualTo(EncounterState.Reactivable), "A downed player can still be revived: it needs the zone protected.");
            Assert.That(Status().ReactivationCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator ExtractedParticipantInsideTheZone_DoesNotBlockTheRespawn()
        {
            yield return Begin(maxReactivations: 1, withAvatar: true);
            MoveAvatar(ZoneCenter);
            Assert.That(_participantObject.GetComponent<NetworkRaidParticipant>().TryMarkExtracted(_avatarObject), Is.True);
            Assert.That(_participantObject.GetComponent<NetworkRaidParticipant>().State, Is.EqualTo(RaidParticipantState.Extracted));
            SetPressure(DungeonPressureState.NotStarted, DungeonPressurePhase.Normal); // resolve the first cycle before any pass can run
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Reactivable);
            SetPressure(DungeonPressureState.Running, DungeonPressurePhase.Normal);

            yield return WaitUntil(() => Status().State == EncounterState.Active && Status().ReactivationCount == 1);
        }

        // ---- Misconfiguration is reported, not silent -------------------------------------------------------

        [UnityTest]
        public IEnumerator MissingPressureConfig_LogsOneErrorAndKeepsTheRoomReactivable()
        {
            // Begin expects the single error; any further error log would fail the test, so "once" is part of the assertion.
            yield return Begin(maxReactivations: 1, withoutPressureConfig: true);
            yield return ResolveFirstCycle();
            yield return WaitUntil(() => Status().State == EncounterState.Reactivable);

            yield return WaitSeconds(1.2f);

            Assert.That(Status().State, Is.EqualTo(EncounterState.Reactivable));
            Assert.That(Status().ReactivationCount, Is.EqualTo(0));
            Assert.That(LivingMembers(), Is.Empty);
        }

        // ---- Fixture ------------------------------------------------------------------------------------

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(target, value);

        private IEnumerator Begin(
            int maxReactivations,
            bool withAvatar = false,
            float cadenceSeconds = 0.1f,
            RoomSpec[] rooms = null,
            bool withoutPressureConfig = false)
        {
            var runnerObject = new GameObject("EncounterReactivationTestRunner");
            _runner = runnerObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = false;
            PlayModeRunnerComposition.AddLocalInputContext(_runner);
            runnerObject.AddComponent<EntityRegistry>();
            PlayModeRunnerComposition.AddTeamExtractionProgress(_runner);
            _damage = runnerObject.AddComponent<EnemyFatalDamageSimulationDriver>();
            _simulationDriver = runnerObject.AddComponent<PlayerAbilityRuntimeSimulationDriver>();

            RaidCode.TryParse("123456", out var code);
            RaidTeamId.TryCreate(1, out var team);
            var profile = new ProfileId("reactivation-1");
            var roster = new[] { new RaidLaunchParticipant(profile, team) };
            Assert.That(RaidLaunchContext.TryCreate(code, profile, roster, profile, 1, out var launch), Is.True);

            _manager = runnerObject.AddComponent<NetworkSpawnManager>();
            Assert.That(_manager.InitializeForRunner(
                _runner, default, default, new[] { new NetworkPrefabRef(GreenSlimePrefabGuid) }, SessionStartupContext.FreshSession, launch), Is.True);

            var start = _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single,
                SessionName = "encounter-reactivation-" + Guid.NewGuid().ToString("N"),
                SceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = runnerObject.AddComponent<NetworkObjectProviderDefault>()
            });
            while (!start.IsCompleted) yield return null;
            Assert.That(start.Result.Ok, Is.True, start.Result.ShutdownReason.ToString());

            // The frozen cohort's Luck must resolve for any enemy spawn: one admitted, spawned participant.
            RaidParticipantIdAssignment.TryResolve(launch.ParticipantProfileIds, profile, out var participantId);
            _participantObject = _runner.Spawn(Load(ParticipantGuid), onBeforeSpawned: (_, instance) =>
                instance.GetComponent<NetworkRaidParticipant>().Initialize(profile.Value, participantId,
                    ProgressionBalanceDefaults.InitialCharacterAttributeState, ExperienceCurve.InitialLevel, 0, "reactivation-test"));
            PlayerRef player = PlayerRef.FromEncoded(1);
            _manager.Test_RegisterParticipant(player, _participantObject);
            _manager.Test_AdmitProfile(profile, player);

            if (withAvatar)
            {
                _avatarObject = _runner.Spawn(Load(PlayerGuid), Vector3.zero, Quaternion.identity,
                    onBeforeSpawned: (_, instance) => instance.GetComponent<RaidAvatarParticipantLink>().Initialize(_participantObject));
                _manager.Test_RegisterAvatar(player, _avatarObject);
                Assert.That(_participantObject.GetComponent<NetworkRaidParticipant>().TrySetCurrentAvatar(_avatarObject), Is.True);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(MatchControllerPrefabPath);
            Assert.That(prefab, Is.Not.Null, MatchControllerPrefabPath);
            NetworkObject match = _runner.Spawn(prefab, Vector3.zero, Quaternion.identity);
            _registry = match.GetComponent<EncounterRegistry>();
            _pressure = match.GetComponent<DungeonPressureController>();
            Assert.That(_registry, Is.Not.Null, $"{nameof(EncounterRegistry)} must be composed on {MatchControllerPrefabPath}.");
            _pressureConfig = AssetDatabase.LoadAssetAtPath<DungeonPressureConfig>(PressureConfigPath);
            Assert.That(_pressureConfig, Is.Not.Null, PressureConfigPath);

            // A short cadence keeps the test fast; the default is asserted through the prefab field below.
            var serialized = new SerializedObject(_registry);
            Assert.That(serialized.FindProperty("_reactivationCheckSeconds").floatValue, Is.EqualTo(1f), "Prefab default cadence.");
            Assert.That(serialized.FindProperty("_generationPhases").enumValueFlag, Is.EqualTo((int)EncounterGenerationPhases.All), "Prefab default: every phase.");
            Assert.That(serialized.FindProperty("_pressureConfig").objectReferenceValue, Is.SameAs(_pressureConfig), "Same config as the pressure systems.");
            serialized.FindProperty("_reactivationCheckSeconds").floatValue = cadenceSeconds;
            if (withoutPressureConfig)
            {
                serialized.FindProperty("_pressureConfig").objectReferenceValue = null;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            SetPressure(DungeonPressureState.Running, DungeonPressurePhase.Normal);
            rooms ??= new[] { new RoomSpec(RoomId, 1, 2, ZoneCenter) };
            if (withoutPressureConfig)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("EncounterRegistry.*_pressureConfig.*every Encounter reactivation is blocked"));
            }

            _manager.ConfigureForScene(CreateConfiguration(maxReactivations, rooms));
            Assert.That(_registry.IsConfigured, Is.True);
        }

        /// <summary>Activates the room with two first-cycle enemies and kills both: cooldown 0, so the room becomes Reactivable at once.</summary>
        private IEnumerator ResolveFirstCycle(int roomId = RoomId)
        {
            Assert.That(_manager.TryActivateEncounterRoom(new EncounterId(roomId), new uint[] { 1u, 2u }, out string failure), Is.True, failure);
            EnemyCharacter first = SpawnEnemy(roomId, 1u);
            EnemyCharacter second = SpawnEnemy(roomId, 2u);
            yield return Kill(first);
            yield return Kill(second);
            Assert.That(Status(roomId).State, Is.Not.EqualTo(EncounterState.Active));
        }

        /// <summary>The avatar is driven by a Fusion NetworkTransform: only a teleport moves it for good on State Authority.</summary>
        private void MoveAvatar(Vector3 position)
        {
            _avatarObject.GetComponent<NetworkTransform>().Teleport(position);
            Assert.That(Vector3.Distance(_avatarObject.transform.position, position), Is.LessThan(0.01f));
        }

        private void SetPressure(DungeonPressureState state, DungeonPressurePhase phase)
        {
            // Large timers: the clock must neither advance the phase nor stop the Dungeon while a test runs.
            _pressure.RemainingTicks = int.MaxValue / 2;
            _pressure.CollapseRemainingTicks = int.MaxValue / 2;
            _pressure.Phase = phase;
            _pressure.State = state;
        }

        private void SetAllowedPhases(EncounterGenerationPhases phases)
        {
            var serialized = new SerializedObject(_registry);
            serialized.FindProperty("_generationPhases").enumValueFlag = (int)phases;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void FillGlobalCapacity(int leaveFree = 0)
        {
            int free = _manager.PopulationTracker.GetAvailableGlobalCapacity(_pressureConfig.MaxGlobalEnemies) - leaveFree;
            for (int i = 0; i < free; i++)
            {
                uint id = _nextFakeEnemyId++;
                _fakeEnemies.Add(id);
                _manager.PopulationTracker.Register(id, EnemyPopulationOrigin.Bootstrap, 1);
            }
        }

        private void FreeGlobalCapacity()
        {
            foreach (uint id in _fakeEnemies) _manager.PopulationTracker.Unregister(id);
            _fakeEnemies.Clear();
        }

        private EncounterStatus Status(int roomId = RoomId)
        {
            Assert.That(_registry.TryGetStatus(new EncounterId(roomId), out EncounterStatus status), Is.True);
            return status;
        }

        private static List<EnemyCharacter> AllRoomEnemiesOf(int roomId) =>
            Object.FindObjectsByType<EnemyCharacter>(FindObjectsSortMode.None).Where(e => e.EncounterIdValue == roomId).ToList();

        private List<EnemyCharacter> AllRoomEnemies(int roomId = RoomId) => AllRoomEnemiesOf(roomId);

        private List<EnemyCharacter> LivingMembers(int roomId = RoomId) => AllRoomEnemies(roomId).Where(e => e.IsAlive).ToList();

        /// <summary>One reactivation pass for the room at index 0, as the registry runs it, but called by the test for a deterministic outcome.</summary>
        private EncounterReactivationOutcome Pass(out string detail, int roomIndex = 0, bool phaseAllowsGeneration = true)
        {
            _manager.RefreshEncounterPresence();
            return _manager.TryReactivateEncounterRoom(roomIndex, phaseAllowsGeneration, _pressureConfig.MaxGlobalEnemies, out detail);
        }

        private IEnumerator InSimulation(Action operation)
        {
            int before = _simulationDriver.CompletionSequence;
            _simulationDriver.RequestOperation(operation);
            yield return WaitUntil(() => _simulationDriver.CompletionSequence != before);
        }

        private NetworkObject Load(string guid) =>
            _runner.Config.PrefabTable.Load(_runner.Config.PrefabTable.GetId(NetworkObjectGuid.Parse(guid)), true);

        private EnemyCharacter SpawnEnemy(int encounterId, uint memberKey)
        {
            NetworkObject enemy = _runner.Spawn(Load(GreenSlimePrefabGuid), Vector3.zero, Quaternion.identity, inputAuthority: null, onBeforeSpawned: (_, instance) =>
            {
                var character = instance.GetComponent<EnemyCharacter>();
                character.EncounterIdValue = encounterId;
                character.EncounterMemberKey = memberKey;
            });
            Assert.That(enemy, Is.Not.Null);
            return enemy.GetComponent<EnemyCharacter>();
        }

        private IEnumerator Kill(EnemyCharacter enemy)
        {
            int tick = _runner.Tick;
            _damage.Target = enemy;
            _damage.Resolver = null;
            _damage.DamageAmount = 1000f;
            _damage.IsRequested = true;
            while (_damage.IsRequested && _runner.Tick < tick + 30) yield return null;
            Assert.That(_damage.IsRequested, Is.False, "The fatal damage did not run in simulation.");
            Assert.That(enemy.IsAlive, Is.False);
            yield return WaitTicks(2);
        }

        private IEnumerator WaitTicks(int ticks)
        {
            int until = _runner.Tick.Raw + ticks;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (_runner.Tick.Raw < until && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(_runner.Tick.Raw, Is.GreaterThanOrEqualTo(until));
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        private static IEnumerator WaitUntil(Func<bool> predicate, float timeoutSeconds = 10f)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, "The expected state was not reached.");
        }

        /// <summary>A valid Required scene configuration with one authored Encounter room: 2 markers, 1-2 enemies, cooldown 0.</summary>
        private NetworkSpawnSceneConfiguration CreateConfiguration(int maxReactivations, RoomSpec[] roomSpecs)
        {
            var configurationObject = Track(new GameObject("EncounterReactivationConfiguration"));
            var configuration = configurationObject.AddComponent<NetworkSpawnSceneConfiguration>();
            Transform[] playerPoints = Markers(1, "Player", Vector3.zero);
            Transform[] enemyPoints = Markers(2, "GlobalEnemy", Vector3.zero);
            EncounterRoom[] rooms = roomSpecs.Select(spec => CreateRoom(maxReactivations, spec)).ToArray();

            var serialized = new SerializedObject(configuration);
            serialized.FindProperty("_spawnPointPolicy").enumValueIndex = (int)SceneSpawnPointPolicy.Required;
            SerializedProperty areas = serialized.FindProperty("_playerSpawnAreas");
            areas.arraySize = 1;
            SerializedProperty areaPoints = areas.GetArrayElementAtIndex(0).FindPropertyRelative("_spawnPoints");
            areaPoints.arraySize = 1;
            areaPoints.GetArrayElementAtIndex(0).objectReferenceValue = playerPoints[0];

            SerializedProperty groups = serialized.FindProperty("_spawnGroups");
            groups.arraySize = 1;
            SerializedProperty group = groups.GetArrayElementAtIndex(0);
            group.FindPropertyRelative(nameof(SpawnGroupDefinition.Group)).enumValueIndex = (int)SpawnGroupType.Enemies;
            group.FindPropertyRelative(nameof(SpawnGroupDefinition.Amount)).intValue = 1;
            SerializedProperty points = group.FindPropertyRelative(nameof(SpawnGroupDefinition.SpawnPoints));
            points.arraySize = enemyPoints.Length;
            for (int i = 0; i < enemyPoints.Length; i++) points.GetArrayElementAtIndex(i).objectReferenceValue = enemyPoints[i];

            SerializedProperty roomsProperty = serialized.FindProperty("_encounterRooms");
            roomsProperty.arraySize = rooms.Length;
            for (int i = 0; i < rooms.Length; i++) roomsProperty.GetArrayElementAtIndex(i).objectReferenceValue = rooms[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(configuration.Validate(out string failure), Is.True, failure);
            return configuration;
        }

        private EncounterRoom CreateRoom(int maxReactivations, RoomSpec spec)
        {
            GameObject roomObject = Track(new GameObject("Room_" + spec.Id));
            roomObject.transform.position = spec.ZoneCenter;
            var room = roomObject.AddComponent<EncounterRoom>();
            BoxCollider2D zone = roomObject.AddComponent<BoxCollider2D>();
            zone.size = new Vector2(10f, 10f);
            zone.isTrigger = true;
            Transform[] spawnPoints = Markers(2, "RoomMarker" + spec.Id, spec.ZoneCenter + new Vector3(20f, 0f, 0f));
            var serialized = new SerializedObject(room);
            serialized.FindProperty("_encounterId").intValue = spec.Id;
            serialized.FindProperty("_generationZone").objectReferenceValue = zone;
            SerializedProperty pointsProperty = serialized.FindProperty("_spawnPoints");
            pointsProperty.arraySize = spawnPoints.Length;
            for (int i = 0; i < spawnPoints.Length; i++) pointsProperty.GetArrayElementAtIndex(i).objectReferenceValue = spawnPoints[i];
            serialized.FindProperty("_composition._minEnemies").intValue = spec.MinEnemies;
            serialized.FindProperty("_composition._maxEnemies").intValue = spec.MaxEnemies;
            serialized.FindProperty("_cooldownSeconds").floatValue = 0f;
            serialized.FindProperty("_maxReactivations").intValue = maxReactivations;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return room;
        }

        private Transform[] Markers(int count, string prefix, Vector3 origin)
        {
            GameObject root = Track(new GameObject(prefix + "Root"));
            var markers = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var marker = new GameObject($"{prefix}_{i + 1}");
                marker.transform.SetParent(root.transform);
                marker.transform.position = origin + new Vector3(i * 3f, 0f, 0f);
                markers[i] = marker.transform;
            }

            return markers;
        }

        private GameObject Track(GameObject go)
        {
            _sceneObjects.Add(go);
            return go;
        }
    }
}
#endif
