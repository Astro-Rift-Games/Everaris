#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
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
    /// TASK-330 T4 (S1, S2, S5) on a real single-peer runner: the registry that lives on the productive
    /// NetworkMatchController prefab resolves an Encounter from enemy deaths on State Authority, and only from them.
    /// Real Host Migration needs a multi-process run and is not covered here.
    /// </summary>
    public sealed class EncounterRegistryPlayModeTests
    {
        private const string MatchControllerPrefabPath = "Assets/Prefabs/NetworkMatchController.prefab";
        private const string GreenSlimePrefabGuid = "5deca87613df0fa409d98702aec643d4";

        private NetworkRunner _runner;
        private NetworkSpawnManager _manager;
        private EncounterRegistry _registry;
        private EnemyFatalDamageSimulationDriver _damage;
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
        }

        // ---- S2: resolution from enemy deaths --------------------------------------------------

        [UnityTest]
        public IEnumerator EncounterEnemyDeaths_ResolveTheEncounterWhenTheLastMemberDies()
        {
            yield return Begin();
            ConfigureAndActivate(roomId: 5, cooldownSeconds: 60f, keys: new uint[] { 1u, 2u });
            EnemyCharacter first = SpawnEnemy(5, 1u);
            EnemyCharacter second = SpawnEnemy(5, 2u);
            Assert.That(Status(5).State, Is.EqualTo(EncounterState.Active));
            Assert.That(Status(5).AliveMemberCount, Is.EqualTo(2));

            yield return Kill(first);

            Assert.That(Status(5).State, Is.EqualTo(EncounterState.Active), "One member is still alive.");
            Assert.That(Status(5).AliveMemberCount, Is.EqualTo(1));

            yield return Kill(second);

            Assert.That(Status(5).AliveMemberCount, Is.EqualTo(0));
            Assert.That(Status(5).State, Is.EqualTo(EncounterState.Resolved).Or.EqualTo(EncounterState.Cooldown));
            Assert.That(Status(5).MemberCount, Is.EqualTo(2), "The eliminated members still belong to the cycle.");
        }

        [UnityTest]
        public IEnumerator NonEncounterEnemyDeath_DoesNotTouchAnyEncounter()
        {
            yield return Begin();
            ConfigureAndActivate(roomId: 5, cooldownSeconds: 60f, keys: new uint[] { 1u });
            EnemyCharacter member = SpawnEnemy(5, 1u);
            EnemyCharacter stranger = SpawnEnemy(0, 0u);

            yield return Kill(stranger);

            Assert.That(stranger.IsAlive, Is.False);
            Assert.That(member.IsAlive, Is.True);
            Assert.That(Status(5).State, Is.EqualTo(EncounterState.Active));
            Assert.That(Status(5).AliveMemberCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DespawningAnEncounterEnemy_IsNotAValidElimination()
        {
            yield return Begin();
            ConfigureAndActivate(roomId: 5, cooldownSeconds: 60f, keys: new uint[] { 1u });
            EnemyCharacter member = SpawnEnemy(5, 1u);

            _runner.Despawn(member.Object);
            yield return WaitTicks(5);

            Assert.That(Status(5).State, Is.EqualTo(EncounterState.Active));
            Assert.That(Status(5).AliveMemberCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DeathOfAnEnemyWithBrokenDeathDependencies_StillResolvesItsEncounter()
        {
            yield return Begin();
            ConfigureAndActivate(roomId: 5, cooldownSeconds: 60f, keys: new uint[] { 1u });
            EnemyCharacter member = SpawnEnemy(5, 1u);
            typeof(EnemyCharacter).GetField("_deathDependenciesValid", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(member, false);

            yield return Kill(member);

            Assert.That(Status(5).AliveMemberCount, Is.EqualTo(0), "A dependency problem must not block the resolution.");
            Assert.That(Status(5).State, Is.EqualTo(EncounterState.Resolved).Or.EqualTo(EncounterState.Cooldown));
        }

        [UnityTest]
        public IEnumerator ResolvedEncounter_AdvancesToReactivableOnTheAuthorityTickFromTheMomentOfResolution()
        {
            yield return Begin();
            const float cooldown = 0.3f;
            ConfigureAndActivate(roomId: 5, cooldownSeconds: cooldown, keys: new uint[] { 1u });
            EnemyCharacter member = SpawnEnemy(5, 1u);
            yield return WaitTicks(10);
            Assert.That(_registry.ElapsedTicks, Is.EqualTo(0), "The registry clock only runs while an encounter is cooling down.");

            yield return Kill(member);
            yield return WaitUntil(() => Status(5).State == EncounterState.Reactivable);

            float dt = _runner.DeltaTime;
            Assert.That(_registry.ElapsedTicks * dt, Is.GreaterThanOrEqualTo(cooldown));
            Assert.That((_registry.ElapsedTicks - 1) * dt, Is.LessThan(cooldown), "The cooldown ends as soon as it elapsed.");
            int atReactivable = _registry.ElapsedTicks;
            yield return WaitTicks(10);
            Assert.That(_registry.ElapsedTicks, Is.EqualTo(atReactivable), "The clock stops once nothing is cooling down.");
            Assert.That(Status(5).State, Is.EqualTo(EncounterState.Reactivable));
        }

        // ---- S1: identity and queryable state ----------------------------------------------------

        [UnityTest]
        public IEnumerator ConfiguredRooms_AreQueryableByIdBeforeTheyActivate()
        {
            yield return Begin();
            ConfigureRooms((5, 60f), (9, 60f));

            Assert.That(_registry.RoomCount, Is.EqualTo(2));
            Assert.That(Status(5).State, Is.EqualTo(EncounterState.NotActivated));
            Assert.That(Status(9).Id, Is.EqualTo(new EncounterId(9)));
            Assert.That(_registry.TryGetState(new EncounterId(9), out EncounterState state), Is.True);
            Assert.That(state, Is.EqualTo(EncounterState.NotActivated));
            Assert.That(_registry.TryGetStatus(new EncounterId(77), out _), Is.False);
        }

        // ---- S5: reset between raids -------------------------------------------------------------

        [UnityTest]
        public IEnumerator ResetForRaidClosure_ClearsEveryEncounterAndTheClock()
        {
            yield return Begin();
            ConfigureAndActivate(roomId: 5, cooldownSeconds: 60f, keys: new uint[] { 1u });
            yield return Kill(SpawnEnemy(5, 1u));
            yield return WaitTicks(3);
            Assert.That(_registry.ElapsedTicks, Is.GreaterThan(0));

            _registry.ResetForRaidClosure();
            yield return WaitTicks(3);

            Assert.That(_registry.TryGetStatus(new EncounterId(5), out _), Is.False);
            Assert.That(_registry.IsConfigured, Is.False);
            Assert.That(_registry.RoomCount, Is.EqualTo(0));
            Assert.That(_registry.ElapsedTicks, Is.EqualTo(0));
            Assert.That(_registry.TryActivate(new EncounterId(5), new uint[] { 1u }), Is.False, "A closed raid keeps no rooms to activate.");
        }

        // ---- S5: reconciliation after a Host Migration restore ---------------------------------------

        [UnityTest]
        public IEnumerator Reconcile_AMemberRestoredDeadButStillAliveInTheRegistry_IsEliminatedAndTheRestIsKept()
        {
            yield return Begin();
            ConfigureAndActivate(roomId: 5, cooldownSeconds: 60f, keys: new uint[] { 1u, 2u });
            EnemyCharacter dead = SpawnEnemy(5, 1u);
            EnemyCharacter alive = SpawnEnemy(5, 2u);
            // The death never reached the registry, as when a snapshot catches the two out of step.
            typeof(CharacterBase).GetProperty(nameof(CharacterBase.Health)).SetValue(dead, 0f);
            Assert.That(dead.IsAlive, Is.False);
            Assert.That(Status(5).AliveMemberCount, Is.EqualTo(2));

            _manager.ReconcileEncounterRegistry(new[] { dead.Object, alive.Object });

            Assert.That(Status(5).AliveMemberCount, Is.EqualTo(1));
            Assert.That(Status(5).State, Is.EqualTo(EncounterState.Active));
            Assert.That(alive.IsAlive, Is.True, "The enemies are never changed by the reconciliation.");
        }

        [UnityTest]
        public IEnumerator Reconcile_ConsistentRestoredState_ChangesNothing()
        {
            yield return Begin();
            ConfigureAndActivate(roomId: 5, cooldownSeconds: 60f, keys: new uint[] { 1u, 2u });
            EnemyCharacter first = SpawnEnemy(5, 1u);
            EnemyCharacter second = SpawnEnemy(5, 2u);
            EnemyCharacter stranger = SpawnEnemy(0, 0u);

            _manager.ReconcileEncounterRegistry(new[] { first.Object, second.Object, stranger.Object });

            Assert.That(Status(5).AliveMemberCount, Is.EqualTo(2));
            Assert.That(Status(5).State, Is.EqualTo(EncounterState.Active));
        }

        // ---- S5: Host Migration restore of the replicated state ----------------------------------------
        // A single-peer runner can never be IsResume, so a restore spawn is simulated through the narrowest seam,
        // EncounterRegistry.Test_BeginRestoreAdoption: real replicated state, restore pending, no rooms bound.

        [UnityTest]
        public IEnumerator RestoredRegistry_AdoptsTheCopiedStateAndClockInsteadOfResettingThem()
        {
            yield return Begin();
            yield return BuildMidRaidState();
            EncounterStatus room5 = Status(5);
            EncounterStatus room9 = Status(9);
            int ticks = _registry.ElapsedTicks;
            Assert.That(room5.State, Is.EqualTo(EncounterState.Active));
            Assert.That(room5.AliveMemberCount, Is.EqualTo(1));
            Assert.That(ticks, Is.GreaterThan(0));

            EncounterRegistry restored = SimulateRestoreSpawn(_registry);
            Assert.That(restored.ElapsedTicks, Is.EqualTo(ticks), "The clock is replicated state.");
            Assert.That(restored.Configure(MidRaidRooms()), Is.True);

            AssertSameStatus(restored, room5);
            AssertSameStatus(restored, room9);
            Assert.That(restored.ElapsedTicks, Is.EqualTo(ticks), "Adoption must not reset the clock.");
            Assert.That(restored.IsConfigured, Is.True);
        }

        [UnityTest]
        public IEnumerator DeathBeforeTheRestoreIsAdopted_IsHeldAndAppliedByTheFirstConfigure()
        {
            yield return Begin();
            yield return BuildMidRaidState();
            EncounterRegistry restored = SimulateRestoreSpawn(_registry);

            Assert.That(restored.NotifyMemberDeath(new EncounterId(5), 2u), Is.False, "Nothing can be applied before the rooms are bound.");
            Assert.That(restored.PendingEliminationCount, Is.EqualTo(1));
            Assert.That(restored.Configure(MidRaidRooms()), Is.True);

            Assert.That(restored.PendingEliminationCount, Is.EqualTo(0));
            Assert.That(StatusOf(restored, 5).AliveMemberCount, Is.EqualTo(0));
            Assert.That(StatusOf(restored, 5).State, Is.EqualTo(EncounterState.Resolved));
        }

        [UnityTest]
        public IEnumerator SceneChangeBeforeTheRestoreIsAdopted_IsIgnoredAndTheCopiedStateSurvives()
        {
            yield return Begin();
            yield return BuildMidRaidState();
            EncounterRegistry restored = SimulateRestoreSpawn(_registry);

            restored.ResetForSceneChange();

            Assert.That(StatusOf(restored, 5).AliveMemberCount, Is.EqualTo(1), "The copied state must survive an early scene-load callback.");
            Assert.That(restored.Configure(MidRaidRooms()), Is.True);
            Assert.That(StatusOf(restored, 5).AliveMemberCount, Is.EqualTo(1), "It is still adopted afterwards.");

            restored.ResetForSceneChange();
            Assert.That(restored.TryGetStatus(new EncounterId(5), out _), Is.False, "Once adopted, a scene change resets normally.");
        }

        [UnityTest]
        public IEnumerator RestoreThatCannotBeBound_ByARoomSetFailure_IsDiscardedWithAnErrorAndTheQueueIsDropped()
        {
            yield return Begin();
            yield return BuildMidRaidState();
            EncounterRegistry restored = SimulateRestoreSpawn(_registry);
            restored.NotifyMemberDeath(new EncounterId(5), 2u);
            LogAssert.Expect(LogType.Error, new Regex("Cannot bind the scene's Encounter rooms"));
            LogAssert.Expect(LogType.Error, new Regex("Restored encounter state could not be adopted.*Cannot bind"));

            Assert.That(restored.Configure(new[] { Room(5), Room(5) }), Is.False);

            Assert.That(restored.TryGetStatus(new EncounterId(5), out _), Is.False, "Stale slots are no longer readable.");
            Assert.That(restored.RoomCount, Is.EqualTo(0));
            Assert.That(restored.ElapsedTicks, Is.EqualTo(0));
            Assert.That(restored.PendingEliminationCount, Is.EqualTo(0));
            Assert.That(restored.Configure(MidRaidRooms()), Is.True);
            Assert.That(StatusOf(restored, 5).State, Is.EqualTo(EncounterState.NotActivated), "The discarded restore is not adopted later.");
            restored.ResetForSceneChange();
            Assert.That(restored.TryGetStatus(new EncounterId(5), out _), Is.False, "Scene changes reset normally again.");
        }

        [UnityTest]
        public IEnumerator RestoreInASceneWithoutRooms_IsDiscardedWithAnError()
        {
            yield return Begin();
            yield return BuildMidRaidState();
            EncounterRegistry restored = SimulateRestoreSpawn(_registry);
            LogAssert.Expect(LogType.Error, new Regex("Restored encounter state could not be adopted.*no Encounter rooms"));

            _manager.ConfigureForScene(CreateConfiguration());

            Assert.That(restored.TryGetStatus(new EncounterId(5), out _), Is.False);
            Assert.That(restored.PendingEliminationCount, Is.EqualTo(0));
            restored.ResetForSceneChange();
            Assert.That(restored.IsConfigured, Is.False);
        }

        [UnityTest]
        public IEnumerator DiscardUnadoptedRestore_IsExplicitAndHasNoEffectWithoutAPendingRestore()
        {
            yield return Begin();
            yield return BuildMidRaidState();
            _registry.DiscardUnadoptedRestore("nothing pending");
            Assert.That(Status(5).AliveMemberCount, Is.EqualTo(1), "A live registry is never discarded.");

            EncounterRegistry restored = SimulateRestoreSpawn(_registry);
            LogAssert.Expect(LogType.Error, new Regex("could not be adopted.*test reason.*2 replicated"));
            restored.DiscardUnadoptedRestore("test reason");
            Assert.That(restored.TryGetStatus(new EncounterId(9), out _), Is.False);

            restored.DiscardUnadoptedRestore("second call");
        }

        // ---- State kept after the raid closed ----------------------------------------------------------

        [UnityTest]
        public IEnumerator DeathAfterRaidClosure_IsDroppedWithOneWarningAndNeverQueued()
        {
            yield return Begin();
            ConfigureAndActivate(roomId: 5, cooldownSeconds: 60f, keys: new uint[] { 1u });
            _registry.ResetForRaidClosure();
            int warnings = 0;
            void Count(string message, string stack, LogType type)
            {
                if (type == LogType.Warning && message.Contains(nameof(EncounterRegistry))) warnings++;
            }

            Application.logMessageReceived += Count;
            try
            {
                Assert.That(_registry.NotifyMemberDeath(new EncounterId(5), 1u), Is.False);
                Assert.That(_registry.NotifyMemberDeath(new EncounterId(5), 1u), Is.False);
            }
            finally
            {
                Application.logMessageReceived -= Count;
            }

            Assert.That(_registry.PendingEliminationCount, Is.EqualTo(0), "Only a pending restore may hold deaths.");
            Assert.That(warnings, Is.EqualTo(1), "The drop is reported once, not per death.");

            ConfigureAndActivate(roomId: 5, cooldownSeconds: 60f, keys: new uint[] { 1u });
            Assert.That(Status(5).AliveMemberCount, Is.EqualTo(1), "Nothing leaks into the next raid's rooms.");
        }

        // ---- Activation seam (both spawn orders) -------------------------------------------------

        [UnityTest]
        public IEnumerator SceneConfiguredAfterTheRegistrySpawned_PublishesItsRoomsAndActivatesThem()
        {
            yield return Begin();
            Assert.That(_manager.EncounterRegistry, Is.SameAs(_registry), "The runner-scoped manager owns the reference.");

            _manager.ConfigureForScene(CreateConfiguration(3, 4));

            Assert.That(_registry.RoomCount, Is.EqualTo(2));
            Assert.That(Status(3).State, Is.EqualTo(EncounterState.NotActivated));
            Assert.That(_manager.TryActivateEncounterRoom(new EncounterId(4), new uint[] { 1u, 2u }, out string failure), Is.True, failure);
            Assert.That(Status(4).State, Is.EqualTo(EncounterState.Active));
            Assert.That(Status(4).AliveMemberCount, Is.EqualTo(2));
            Assert.That(Status(3).State, Is.EqualTo(EncounterState.NotActivated), "NoEncounter rooms stay NotActivated.");
        }

        [UnityTest]
        public IEnumerator RegistrySpawnedAfterTheSceneWasConfigured_PublishesTheSameRooms()
        {
            yield return Begin(spawnMatchController: false);
            _manager.ConfigureForScene(CreateConfiguration(3, 4));
            Assert.That(_manager.EncounterRegistry, Is.Null);

            SpawnMatchController();

            Assert.That(_manager.EncounterRegistry, Is.SameAs(_registry));
            Assert.That(_registry.RoomCount, Is.EqualTo(2));
            Assert.That(Status(4).State, Is.EqualTo(EncounterState.NotActivated));
            Assert.That(_manager.TryActivateEncounterRoom(new EncounterId(3), new uint[] { 1u }, out string failure), Is.True, failure);
            Assert.That(Status(3).State, Is.EqualTo(EncounterState.Active));
        }

        [UnityTest]
        public IEnumerator TryActivateEncounterRoom_WithoutARegistryOrWithAnUnknownRoom_FailsWithAReason()
        {
            yield return Begin(spawnMatchController: false);
            Assert.That(_manager.TryActivateEncounterRoom(new EncounterId(3), new uint[] { 1u }, out string noRegistry), Is.False);
            Assert.That(noRegistry, Does.Contain(nameof(EncounterRegistry)));

            SpawnMatchController();
            _manager.ConfigureForScene(CreateConfiguration(3));
            Assert.That(_manager.TryActivateEncounterRoom(new EncounterId(99), new uint[] { 1u }, out string unknown), Is.False);
            Assert.That(unknown, Does.Contain("99"));
            Assert.That(Status(3).State, Is.EqualTo(EncounterState.NotActivated));
        }

        [UnityTest]
        public IEnumerator SceneChange_ResetsTheRegistryOnStateAuthority()
        {
            yield return Begin();
            _manager.ConfigureForScene(CreateConfiguration(3));
            Assert.That(_manager.TryActivateEncounterRoom(new EncounterId(3), new uint[] { 1u }, out _), Is.True);

            _manager.OnSceneLoadStart(_runner);

            Assert.That(_registry.TryGetStatus(new EncounterId(3), out _), Is.False, "No encounter state carries over to the next scene.");
            Assert.That(_registry.IsConfigured, Is.False);
        }

        // ---- Fixture ------------------------------------------------------------------------------

        private IEnumerator Begin(bool spawnMatchController = true)
        {
            var runnerObject = new GameObject("EncounterRegistryTestRunner");
            _runner = runnerObject.AddComponent<NetworkRunner>();
            runnerObject.AddComponent<EntityRegistry>();
            _runner.ProvideInput = false;
            _damage = runnerObject.AddComponent<EnemyFatalDamageSimulationDriver>();
            _manager = runnerObject.AddComponent<NetworkSpawnManager>();
            Assert.That(_manager.InitializeForRunner(
                _runner, default, default, Array.Empty<NetworkPrefabRef>(), SessionStartupContext.FreshSession, null), Is.True);

            var start = _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single,
                SessionName = "encounter-registry-" + Guid.NewGuid().ToString("N"),
                SceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = runnerObject.AddComponent<NetworkObjectProviderDefault>()
            });
            while (!start.IsCompleted) yield return null;
            Assert.That(start.Result.Ok, Is.True, start.Result.ShutdownReason.ToString());

            if (spawnMatchController)
            {
                SpawnMatchController();
            }
        }

        private void SpawnMatchController()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(MatchControllerPrefabPath);
            Assert.That(prefab, Is.Not.Null, MatchControllerPrefabPath);
            NetworkObject match = _runner.Spawn(prefab, Vector3.zero, Quaternion.identity);
            _registry = match.GetComponent<EncounterRegistry>();
            Assert.That(_registry, Is.Not.Null, $"{nameof(EncounterRegistry)} must be composed on {MatchControllerPrefabPath}.");
        }

        private void ConfigureRooms(params (int id, float cooldownSeconds)[] rooms)
        {
            var definitions = new List<EncounterRoomDefinition>();
            foreach ((int id, float cooldownSeconds) in rooms)
            {
                definitions.Add(new EncounterRoomDefinition(new EncounterId(id), new EncounterLifecycleSettings(cooldownSeconds, 1)));
            }

            Assert.That(_registry.Configure(definitions), Is.True);
        }

        private void ConfigureAndActivate(int roomId, float cooldownSeconds, uint[] keys)
        {
            ConfigureRooms((roomId, cooldownSeconds));
            Assert.That(_registry.TryActivate(new EncounterId(roomId), keys), Is.True);
        }

        private static EncounterRoomDefinition Room(int id, float cooldownSeconds = 60f) =>
            new EncounterRoomDefinition(new EncounterId(id), new EncounterLifecycleSettings(cooldownSeconds, 1));

        private static EncounterRoomDefinition[] MidRaidRooms() => new[] { Room(5), Room(9) };

        /// <summary>Room 5: one of two members dead (Active). Room 9: resolved and cooling down, so the registry clock runs.</summary>
        private IEnumerator BuildMidRaidState()
        {
            Assert.That(_registry.Configure(MidRaidRooms()), Is.True);
            Assert.That(_registry.TryActivate(new EncounterId(5), new uint[] { 1u, 2u }), Is.True);
            Assert.That(_registry.TryActivate(new EncounterId(9), new uint[] { 1u }), Is.True);
            EnemyCharacter firstOfRoom5 = SpawnEnemy(5, 1u);
            SpawnEnemy(5, 2u);
            EnemyCharacter onlyOfRoom9 = SpawnEnemy(9, 1u);
            yield return Kill(firstOfRoom5);
            yield return Kill(onlyOfRoom9);
            yield return WaitTicks(3);
        }

        /// <summary>
        /// Puts the registry in the state <c>Spawned</c> leaves a restored object in: the replicated state is already there
        /// (here, the registry's own), no scene rooms are bound yet and the restore is pending. A second controller built with
        /// CopyStateFrom cannot be used: Fusion requires equal NetworkIds, which only the restorer's snapshot objects have.
        /// </summary>
        private static EncounterRegistry SimulateRestoreSpawn(EncounterRegistry registry)
        {
            registry.Test_BeginRestoreAdoption();
            return registry;
        }

        private static EncounterStatus StatusOf(EncounterRegistry registry, int roomId)
        {
            Assert.That(registry.TryGetStatus(new EncounterId(roomId), out EncounterStatus status), Is.True, $"Encounter {roomId} is not queryable.");
            return status;
        }

        private static void AssertSameStatus(EncounterRegistry registry, EncounterStatus expected)
        {
            EncounterStatus actual = StatusOf(registry, expected.Id.Value);
            Assert.That(actual.State, Is.EqualTo(expected.State), expected.Id.ToString());
            Assert.That(actual.AliveMemberCount, Is.EqualTo(expected.AliveMemberCount), expected.Id.ToString());
            Assert.That(actual.MemberCount, Is.EqualTo(expected.MemberCount), expected.Id.ToString());
            Assert.That(actual.ReactivationCount, Is.EqualTo(expected.ReactivationCount), expected.Id.ToString());
        }

        private EncounterStatus Status(int roomId)
        {
            Assert.That(_registry.TryGetStatus(new EncounterId(roomId), out EncounterStatus status), Is.True, $"Encounter {roomId} is not queryable.");
            return status;
        }

        private EnemyCharacter SpawnEnemy(int encounterId, uint memberKey)
        {
            NetworkPrefabId prefabId = _runner.Config.PrefabTable.GetId(NetworkObjectGuid.Parse(GreenSlimePrefabGuid));
            NetworkObject prefab = _runner.Config.PrefabTable.Load(prefabId, true);
            NetworkObject enemy = _runner.Spawn(prefab, Vector3.zero, Quaternion.identity, inputAuthority: null, onBeforeSpawned: (_, instance) =>
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

        private static IEnumerator WaitUntil(Func<bool> predicate, float timeoutSeconds = 8f)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, "The expected state was not reached.");
        }

        /// <summary>A valid Required scene configuration with one authored Encounter room per id.</summary>
        private NetworkSpawnSceneConfiguration CreateConfiguration(params int[] roomIds)
        {
            var configurationObject = Track(new GameObject("EncounterSpawnConfiguration"));
            var configuration = configurationObject.AddComponent<NetworkSpawnSceneConfiguration>();
            Transform[] playerPoints = Markers(1, "Player");
            Transform[] enemyPoints = Markers(2, "GlobalEnemy");

            var rooms = new EncounterRoom[roomIds.Length];
            for (int i = 0; i < roomIds.Length; i++)
            {
                rooms[i] = CreateRoom(roomIds[i], Markers(2, "RoomMarker" + roomIds[i]));
            }

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

        private EncounterRoom CreateRoom(int id, Transform[] spawnPoints)
        {
            GameObject roomObject = Track(new GameObject("Room_" + id));
            var room = roomObject.AddComponent<EncounterRoom>();
            Collider2D zone = roomObject.AddComponent<BoxCollider2D>();
            var serialized = new SerializedObject(room);
            serialized.FindProperty("_encounterId").intValue = id;
            serialized.FindProperty("_generationZone").objectReferenceValue = zone;
            SerializedProperty pointsProperty = serialized.FindProperty("_spawnPoints");
            pointsProperty.arraySize = spawnPoints.Length;
            for (int i = 0; i < spawnPoints.Length; i++) pointsProperty.GetArrayElementAtIndex(i).objectReferenceValue = spawnPoints[i];
            serialized.FindProperty("_composition._minEnemies").intValue = 1;
            serialized.FindProperty("_composition._maxEnemies").intValue = 2;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return room;
        }

        private Transform[] Markers(int count, string prefix)
        {
            GameObject root = Track(new GameObject(prefix + "Root"));
            var markers = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                var marker = new GameObject($"{prefix}_{i + 1}");
                marker.transform.SetParent(root.transform);
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
