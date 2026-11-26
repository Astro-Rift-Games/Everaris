#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.Downed
{
    /// <summary>
    /// TASK-321: the Host-only <see cref="DownedTeamResolutionCoordinator"/> hosted by the
    /// NetworkMatchController prefab accelerates Downed members of a team with no recovery route, with
    /// a real roster, participants and avatars on one authoritative runner. Not multi-peer proof.
    /// </summary>
    public sealed class DownedTeamResolutionPlayModeTests
    {
        private const string PlayerGuid = "fea3a7b256f965a4eb9b965832939741";
        private const string ParticipantGuid = "c39d451563bae6e43934008a0dadc6d6";
        private const string MatchControllerGuid = "b91f8c7e96a4d784a92c3bd1a88dfec8";
        private const float AcceleratedSeconds = 1f;
        private const float NormalDrain = 1f;

        private NetworkRunner _runner;
        private PlayerAbilityRuntimeSimulationDriver _driver;
        private PlayerEquipmentSimulationDriver _inventory;
        private CharacterAttributeState _attributes;
        private ProfileId[] _profiles;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_runner == null)
            {
                yield break;
            }

            var shutdown = _runner.Shutdown();
            while (!shutdown.IsCompleted)
            {
                yield return null;
            }

            Object.Destroy(_runner.gameObject);
        }

        [UnityTest]
        public IEnumerator Solo_DownedWithoutSelfReviver_IsAcceleratedAndResolvesDefinitiveDefeat()
        {
            yield return Begin(teamSize: 1);
            Avatar solo = SpawnAvatar(0);

            yield return Down(solo);
            yield return WaitFor(() => solo.Downed.IsResolutionAccelerated, "Solo without a reviver was not accelerated.");
            int startTick = _runner.Tick.Raw;
            // The bar is not cut: the latched rate empties the (almost) full reserve in the configured time.
            // Asserted on the rate, not on DownedHealth, because a slow frame can run several ticks.
            float fullBarRate = solo.Downed.InitialDownedHealth / AcceleratedSeconds;
            Assert.That(solo.Downed.AcceleratedDrainPerSecond, Is.EqualTo(fullBarRate).Within(fullBarRate * 0.05f),
                "The bar is not cut instantly.");

            yield return WaitFor(() => !solo.Character.IsDowned, "Accelerated resolution never resolved the Downed cycle.");

            float elapsed = (_runner.Tick.Raw - startTick) * _runner.DeltaTime;
            Assert.That(elapsed, Is.EqualTo(AcceleratedSeconds).Within(0.4f));
            Assert.That(solo.Character.IsAlive, Is.False);
            Assert.That(solo.Downed.DefeatedCycle, Is.EqualTo(1));
            Assert.That(solo.Downed.IsResolutionAccelerated, Is.False, "The rate is cleared with the Downed state.");
        }

        [UnityTest]
        public IEnumerator Solo_DownedWithUsableSelfReviver_KeepsNormalDrain()
        {
            yield return Begin(teamSize: 1);
            Avatar solo = SpawnAvatar(0);
            yield return GiveSelfReviver(solo);
            yield return Down(solo);
            Assert.That(solo.Recovery.CanBeginSelf(), Is.True);

            yield return WaitTicks(60);

            AssertNormalDrain(solo);
        }

        [UnityTest]
        public IEnumerator Duo_ActiveConnectedTeammate_KeepsNormalDrain()
        {
            yield return Begin(teamSize: 2);
            Avatar active = SpawnAvatar(0);
            Avatar downed = SpawnAvatar(1);
            yield return Down(downed);

            yield return WaitTicks(60);

            AssertNormalDrain(downed);
            Assert.That(active.Character.IsDowned, Is.False);
        }

        [UnityTest]
        public IEnumerator Duo_BothDownedWithoutSelfReviver_AcceleratesBothMembers()
        {
            yield return Begin(teamSize: 2);
            Avatar first = SpawnAvatar(0);
            Avatar second = SpawnAvatar(1);

            yield return Down(first);
            yield return Down(second);

            yield return WaitFor(
                () => first.Downed.IsResolutionAccelerated || !first.Character.IsDowned,
                "The first Downed member was not accelerated.");
            yield return WaitFor(
                () => second.Downed.IsResolutionAccelerated || !second.Character.IsDowned,
                "The second Downed member was not accelerated.");
            yield return WaitFor(
                () => !first.Character.IsDowned && !second.Character.IsDowned,
                "Both Downed members must resolve in the accelerated window.");
            Assert.That(first.Downed.DefeatedCycle, Is.EqualTo(1));
            Assert.That(second.Downed.DefeatedCycle, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Duo_ActiveTeammateDisconnected_AcceleratesTheDownedMember()
        {
            yield return Begin(teamSize: 2);
            Avatar active = SpawnAvatar(0);
            Avatar downed = SpawnAvatar(1);
            yield return Down(downed);
            yield return WaitTicks(10);
            Assert.That(downed.Downed.IsResolutionAccelerated, Is.False, "A connected teammate is a route.");

            Disconnect(active);
            Assert.That((bool)active.Connectivity.IsDisconnected, Is.True, "Production retains the disconnected Raider.");

            yield return WaitFor(
                () => downed.Downed.IsResolutionAccelerated || !downed.Character.IsDowned,
                "A disconnected Active teammate is not a recovery route.");
            yield return WaitFor(() => !downed.Character.IsDowned, "The accelerated cycle never resolved.");
            Assert.That(downed.Downed.DefeatedCycle, Is.EqualTo(1));
            Assert.That(active.Character.IsAlive, Is.True);
        }

        [UnityTest]
        public IEnumerator Duo_ActiveTeammateNotInSpawnManager_IsFoundByScanAndKeepsNormalDrain()
        {
            // A participant outside the manager's tracked roster (retained, or restored by Host Migration and
            // not rebound yet) still exists on the runner: the coordinator's scan finds it and reads it as
            // Active and connected, which is a route.
            yield return Begin(teamSize: 2);
            SpawnAvatar(0, register: false);
            Avatar downed = SpawnAvatar(1);
            yield return Down(downed);

            yield return WaitTicks(60);

            AssertNormalDrain(downed);
        }

        [UnityTest]
        public IEnumerator Duo_TeammateNotSpawnedYet_DoesNotAccelerateUntilKnown()
        {
            yield return Begin(teamSize: 2);
            Avatar downed = SpawnAvatar(1);
            yield return Down(downed);

            yield return WaitTicks(90);

            AssertNormalDrain(downed);
            Avatar late = SpawnAvatar(0);
            yield return WaitTicks(90);
            AssertNormalDrain(downed);
            Assert.That(late.Character.IsDowned, Is.False);
        }

        [UnityTest]
        public IEnumerator Duo_BothDownedOneDisconnectedAndRetained_AcceleratesBoth()
        {
            // The retained participant leaves the manager's tracked roster (OnPlayerLeft), exactly as in production.
            yield return Begin(teamSize: 2);
            Avatar retained = SpawnAvatar(0);
            Avatar connected = SpawnAvatar(1);
            Disconnect(retained);
            Assert.That((bool)retained.Connectivity.IsDisconnected, Is.True);
            yield return Down(retained);
            yield return Down(connected);

            yield return WaitFor(
                () => (retained.Downed.IsResolutionAccelerated || !retained.Character.IsDowned) &&
                      (connected.Downed.IsResolutionAccelerated || !connected.Character.IsDowned),
                "Both Downed members of a team without a route must be accelerated, retained one included.");
            yield return WaitFor(
                () => !retained.Character.IsDowned && !connected.Character.IsDowned,
                "Both Downed members must resolve in the accelerated window.");
            Assert.That(retained.Downed.DefeatedCycle, Is.EqualTo(1));
            Assert.That(connected.Downed.DefeatedCycle, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Duo_TeammateGoneAfterBeingSeen_AcceleratesTheDownedMember()
        {
            // A Duo turned Solo: the teammate's objects were despawned after it finalized (extracted, aborted).
            yield return Begin(teamSize: 2);
            Avatar leaver = SpawnAvatar(0);
            Avatar downed = SpawnAvatar(1);
            yield return Down(downed);
            // Long enough for the coordinator to resolve (and remember) the teammate after its retry throttle.
            yield return WaitTicks(40);
            Assert.That(downed.Downed.IsResolutionAccelerated, Is.False, "A connected teammate is a route.");

            _runner.Despawn(leaver.AvatarObject);
            _runner.Despawn(leaver.ParticipantObject);

            yield return WaitFor(
                () => downed.Downed.IsResolutionAccelerated || !downed.Character.IsDowned,
                "A teammate that is known to be gone is not a recovery route.");
            yield return WaitFor(() => !downed.Character.IsDowned, "The accelerated cycle never resolved.");
            Assert.That(downed.Downed.DefeatedCycle, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Duo_RaidingTeammateWithoutAvatar_DoesNotAccelerate()
        {
            // A Raiding participant always owns an avatar; one that has none yet is unknown, not gone.
            yield return Begin(teamSize: 2);
            SpawnParticipant(0, register: true);
            Avatar downed = SpawnAvatar(1);
            yield return Down(downed);

            yield return WaitTicks(60);

            AssertNormalDrain(downed);
        }

        [UnityTest]
        public IEnumerator Duo_TeammateDefeatedAndLeft_AcceleratesTheDownedMember()
        {
            // The Defeated teammate leaves: its participant object is kept with State Defeated but leaves the
            // manager's tracked roster, so the Downed member is effectively Solo (S4).
            yield return Begin(teamSize: 2);
            Avatar defeated = SpawnAvatar(0);
            Avatar downed = SpawnAvatar(1);
            yield return Down(defeated);
            SetField(defeated.Downed, "_downedDrainPerSecond", 100000f);
            var participant = defeated.ParticipantObject.GetComponent<NetworkRaidParticipant>();
            yield return WaitFor(() => participant.State == RaidParticipantState.Defeated,
                "The teammate never reached definitive Defeat.");
            Assert.That(defeated.Character.IsAlive, Is.False);

            Disconnect(defeated);
            Assert.That(defeated.ParticipantObject.IsValid, Is.True, "A Defeated participant is kept when it leaves.");
            // Long enough for the coordinator to read the Defeated teammate after its retry throttle.
            yield return WaitTicks(40);
            yield return Down(downed);

            yield return WaitFor(
                () => downed.Downed.IsResolutionAccelerated || !downed.Character.IsDowned,
                "A Defeated teammate that left is not a recovery route.");
            yield return WaitFor(() => !downed.Character.IsDowned, "The accelerated cycle never resolved.");
            Assert.That(downed.Downed.DefeatedCycle, Is.EqualTo(1));
        }

        private static void AssertNormalDrain(Avatar avatar)
        {
            Assert.That(avatar.Character.IsDowned, Is.True, "The Downed member must not have been resolved early.");
            Assert.That(avatar.Downed.IsResolutionAccelerated, Is.False, "The normal drain must apply.");
            Assert.That(avatar.Downed.DownedHealth, Is.LessThan(75f));
            Assert.That(avatar.Downed.DownedHealth, Is.GreaterThan(70f), "Only the normal drain applies.");
        }

        private IEnumerator Begin(int teamSize)
        {
            var owner = new GameObject("DownedTeamResolutionTestRunner");
            _runner = owner.AddComponent<NetworkRunner>();
            PlayModeRunnerComposition.AddLocalInputContext(_runner);
            owner.AddComponent<EntityRegistry>();
            _driver = owner.AddComponent<PlayerAbilityRuntimeSimulationDriver>();
            _inventory = owner.AddComponent<PlayerEquipmentSimulationDriver>();

            Assert.That(RaidCode.TryParse("123456", out RaidCode code), Is.True);
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId team), Is.True);
            _profiles = new ProfileId[teamSize];
            var roster = new RaidLaunchParticipant[teamSize];
            for (int i = 0; i < teamSize; i++)
            {
                _profiles[i] = new ProfileId($"downed-team-player-{i + 1}");
                roster[i] = new RaidLaunchParticipant(_profiles[i], team);
            }

            Assert.That(RaidLaunchContext.TryCreate(
                code, _profiles[0], roster, _profiles[0], 1, out RaidLaunchContext launch), Is.True);
            Assert.That(owner.AddComponent<NetworkSpawnManager>().InitializeForRunner(_runner, default, default,
                Array.Empty<NetworkPrefabRef>(), SessionStartupContext.FreshSession, launch), Is.True);

            var start = _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single,
                SessionName = $"downed-team-resolution-{Guid.NewGuid():N}",
                SceneManager = owner.AddComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = owner.AddComponent<NetworkObjectProviderDefault>()
            });
            while (!start.IsCompleted)
            {
                yield return null;
            }

            Assert.That(start.Result.Ok, Is.True, start.Result.ShutdownReason.ToString());
            Assert.That(CharacterAttributeState.TryCreate(20, 20, 20, 20, 20, 20, 0, out _attributes), Is.True);

            NetworkObject match = _runner.Spawn(Load(MatchControllerGuid), Vector3.zero, Quaternion.identity);
            Assert.That(match.GetComponent<DownedTeamResolutionCoordinator>(), Is.Not.Null,
                "NetworkMatchController must compose the DownedTeamResolutionCoordinator.");
            yield return WaitTicks(2);
        }

        private NetworkObject SpawnParticipant(int profileIndex, bool register)
        {
            ProfileId profile = _profiles[profileIndex];
            Assert.That(RaidParticipantIdAssignment.TryResolve(_profiles, profile, out RaidParticipantId id), Is.True);
            NetworkObject participantObject = _runner.Spawn(Load(ParticipantGuid),
                onBeforeSpawned: (_, instance) => instance.GetComponent<NetworkRaidParticipant>()
                    .Initialize(profile.Value, id, _attributes, ExperienceCurve.InitialLevel, 0, "downed-team-test"));
            // Raid admission tracks the participant in the manager's roster; an unregistered one models a
            // participant outside it (retained, or restored by Host Migration and not rebound yet).
            if (register)
            {
                _runner.GetComponent<NetworkSpawnManager>().Test_RegisterParticipant(
                    PlayerRef.FromEncoded(id.Value), participantObject);
            }

            return participantObject;
        }

        private Avatar SpawnAvatar(int profileIndex, bool register = true)
        {
            NetworkObject participantObject = SpawnParticipant(profileIndex, register);
            PlayerRef player = PlayerRef.FromEncoded(participantObject.GetComponent<NetworkRaidParticipant>().RaidParticipantId.Value);
            NetworkRaidParticipant participant = participantObject.GetComponent<NetworkRaidParticipant>();
            NetworkObject avatarObject = _runner.Spawn(Load(PlayerGuid), Vector3.right * (10f * profileIndex),
                Quaternion.identity,
                onBeforeSpawned: (_, instance) =>
                    instance.GetComponent<RaidAvatarParticipantLink>().Initialize(participant.Object));
            Assert.That(participant.TrySetCurrentAvatar(avatarObject), Is.True);
            ExtractionProgressTestAssertions.AssertRegistered(_runner, avatarObject);

            var avatar = new Avatar
            {
                Player = player,
                ParticipantObject = participantObject,
                AvatarObject = avatarObject,
                Character = avatarObject.GetComponent<PlayerCharacter>(),
                Downed = avatarObject.GetComponent<PlayerDownedStateNetworkController>(),
                Recovery = avatarObject.GetComponent<PlayerDownedRecoveryNetworkController>(),
                Receiver = avatarObject.GetComponent<PlayerLootReceiver>(),
                Connectivity = participant.GetComponent<RaidParticipantConnectivity>()
            };
            Assert.That(avatar.Connectivity, Is.Not.Null, "The participant prefab composes connectivity.");
            SetField(avatar.Downed, "_downedDrainPerSecond", NormalDrain);
            SetField(avatar.Downed, "_acceleratedResolutionSeconds", AcceleratedSeconds);
            return avatar;
        }

        private IEnumerator Down(Avatar avatar)
        {
            // A fatal hit leaves Health at 0 and then enters Downed; reproduce that authoritative state.
            yield return InSimulation(() =>
            {
                typeof(CharacterBase).GetProperty("Health", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .SetValue(avatar.Character, 0f);
                Assert.That(avatar.Downed.TryEnterDowned(), Is.True);
            });
            Assert.That(avatar.Character.IsDowned, Is.True);
        }

        private IEnumerator GiveSelfReviver(Avatar avatar)
        {
            int sequence = _inventory.CompletionSequence;
            _inventory.RequestInitializeLoadout(avatar.Receiver,
                new[] { new LootEntry(PlayerDownedRecoveryNetworkController.SelfReviverLootId, 1) });
            yield return WaitFor(() => _inventory.CompletionSequence != sequence, "Inventory setup did not run.");
            Assert.That(_inventory.LastResult, Is.True, _inventory.LastError);
            Assert.That(avatar.Recovery.CarriesSelfReviver, Is.True);
        }

        private NetworkObject Load(string guid) =>
            _runner.Config.PrefabTable.Load(_runner.Config.PrefabTable.GetId(NetworkObjectGuid.Parse(guid)), true);

        private IEnumerator InSimulation(Action action)
        {
            int before = _driver.CompletionSequence;
            _driver.RequestOperation(action);
            yield return WaitFor(() => _driver.CompletionSequence != before, "Simulation operation did not run.");
        }

        private IEnumerator WaitTicks(int count)
        {
            int until = _runner.Tick.Raw + count;
            yield return WaitFor(() => _runner.Tick.Raw >= until, "Simulation did not advance.");
        }

        private static IEnumerator WaitFor(Func<bool> predicate, string message)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!predicate() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, message);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        /// <summary>Runs the production departure path: the Raider is retained and flagged disconnected.</summary>
        private void Disconnect(Avatar avatar)
        {
            _runner.GetComponent<NetworkSpawnManager>().OnPlayerLeft(_runner, avatar.Player);
        }

        private struct Avatar
        {
            internal PlayerRef Player;
            internal NetworkObject ParticipantObject;
            internal NetworkObject AvatarObject;
            internal PlayerCharacter Character;
            internal PlayerDownedStateNetworkController Downed;
            internal PlayerDownedRecoveryNetworkController Recovery;
            internal PlayerLootReceiver Receiver;
            internal RaidParticipantConnectivity Connectivity;
        }
    }
}
#endif
