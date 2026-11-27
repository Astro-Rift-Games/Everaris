using System;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Scenario
{
    /// <summary>
    /// FIX-47: Uses the REAL NetworkSpawnManager participant lookup (over _spawnedPlayers and
    /// _disconnectedRegistry, without bypassing via MemberSnapshotsProviderForTest) to verify that
    /// a retained disconnected teammate blocks extraction start, cancels an active countdown with
    /// RequiredMemberDisconnected across all controllers, and unblocks solo extraction once
    /// definitively defeated ("Continuar sin compañero").
    /// </summary>
    [TestFixture]
    public sealed class TeamExtractionDisconnectedMemberTests
    {
        private GameObject _root;
        private NetworkSpawnManager _spawnManager;
        private TeamExtractionCoordinator _coordinator;
        private ExtractionConfig _config;
        private EntityRegistry _registry;
        private TestSanctuary _sanctuary;
        private TestZone _zone;
        private TestAssignmentService _assignmentService;

        private GameObject _anaParticipantObj;
        private GameObject _betoParticipantObj;
        private GameObject _anaAvatarObj;
        private GameObject _betoAvatarObj;
        private PlayerExtractionController _anaController;
        private PlayerExtractionController _betoController;

        private RaidTeamId _teamId;
        private RaidParticipantId _anaId;
        private RaidParticipantId _betoId;

        private readonly Dictionary<NetworkObject, RaidParticipantId> _ids = new();
        private readonly Dictionary<NetworkObject, (RaidParticipantState state, bool connected, Vector2? pos, PlayerExtractionController ctrl)> _states = new();

        [SetUp]
        public void SetUp()
        {
            Assert.IsTrue(RaidTeamId.TryCreate(1, out _teamId));
            Assert.IsTrue(RaidParticipantId.TryCreate(1, out _anaId));
            Assert.IsTrue(RaidParticipantId.TryCreate(2, out _betoId));

            _root = new GameObject("Fix47Root");
            _spawnManager = _root.AddComponent<NetworkSpawnManager>();
            _coordinator = _root.AddComponent<TeamExtractionCoordinator>();

            _config = ScriptableObject.CreateInstance<ExtractionConfig>();
            SetField(_config, "_countdownDurationSeconds", 3f);
            SetField(_config, "_boundaryTolerance", 0.5f);
            SetField(_config, "_progressQuota", 100);
            SetField(_config, "_duoProgressQuota", 150);
            SetField(_config, "_maxTeamsPerRaid", 4);

            _registry = _root.AddComponent<EntityRegistry>();
            _sanctuary = _root.AddComponent<TestSanctuary>();
            _zone = _root.AddComponent<TestZone>();
            _assignmentService = _root.AddComponent<TestAssignmentService>();

            var sanctuaryId = new EntityId(77);
            _sanctuary.SanctuaryId = sanctuaryId;
            _zone.ZoneId = sanctuaryId;
            _registry.TryRegisterExtractionSanctuary(sanctuaryId, _sanctuary);
            _registry.TryRegisterExtractionZone(sanctuaryId, _zone);
            _assignmentService.Assign(_teamId, sanctuaryId);

            // Real NetworkSpawnManager passed into TeamExtractionCoordinator (NO MemberSnapshotsProviderForTest!).
            _coordinator.SetDependenciesForTest(_registry, _spawnManager, _assignmentService, null, _config);
            Assert.IsTrue(_coordinator.TryInitializeTeam(_teamId));
            _coordinator.TeamMembersProviderForTest = id => id == _teamId ? new[] { _anaId, _betoId } : null;
            _coordinator.ParticipantIdReaderForTest = obj => obj != null && _ids.TryGetValue(obj, out var pid) ? pid : default;
            _coordinator.ParticipantStateReaderForTest = obj => _states[obj];

            _anaAvatarObj = CreateAvatar("AnaAvatar", out _anaController);
            _betoAvatarObj = CreateAvatar("BetoAvatar", out _betoController);

            _anaParticipantObj = new GameObject("AnaParticipant");
            var anaNetObj = _anaParticipantObj.AddComponent<NetworkObject>();
            _anaParticipantObj.AddComponent<NetworkRaidParticipant>();
            _ids[anaNetObj] = _anaId;

            _betoParticipantObj = new GameObject("BetoParticipant");
            var betoNetObj = _betoParticipantObj.AddComponent<NetworkObject>();
            _betoParticipantObj.AddComponent<NetworkRaidParticipant>();
            _ids[betoNetObj] = _betoId;

            // Both start connected inside _spawnedPlayers.
            var spawnedPlayers = GetSpawnedPlayers(_spawnManager);
            spawnedPlayers[PlayerRef.FromIndex(1)] = anaNetObj;
            spawnedPlayers[PlayerRef.FromIndex(2)] = betoNetObj;

            _states[anaNetObj] = (RaidParticipantState.Raiding, true, Vector2.zero, _anaController);
            _states[betoNetObj] = (RaidParticipantState.Raiding, true, Vector2.zero, _betoController);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_anaParticipantObj != null) Object.DestroyImmediate(_anaParticipantObj);
            if (_betoParticipantObj != null) Object.DestroyImmediate(_betoParticipantObj);
            if (_anaAvatarObj != null) Object.DestroyImmediate(_anaAvatarObj);
            if (_betoAvatarObj != null) Object.DestroyImmediate(_betoAvatarObj);
            if (_config != null) Object.DestroyImmediate(_config);
        }

        [Test]
        public void DisconnectedRetainedTeammate_BlocksStart_AndCancelsActiveCountdownWithRequiredMemberDisconnected()
        {
            var anaNetObj = _anaParticipantObj.GetComponent<NetworkObject>();
            var betoNetObj = _betoParticipantObj.GetComponent<NetworkObject>();

            // 1. Both connected inside sanctuary -> countdown starts for both controllers.
            _coordinator.FixedUpdateNetwork();
            Assert.IsTrue(_coordinator.GetSlotForTest(0).IsActive, "Duo extraction must start when both are connected and inside.");
            Assert.AreEqual(ExtractionState.InProgress, _anaController.State);
            Assert.AreEqual(ExtractionState.InProgress, _betoController.State);

            // 2. Beto disconnects: removed from _spawnedPlayers and retained in _disconnectedRegistry (real OnPlayerLeft storage).
            GetSpawnedPlayers(_spawnManager).Remove(PlayerRef.FromIndex(2));
            GetDisconnectedRegistry(_spawnManager).Retain(betoNetObj);
            _states[betoNetObj] = (RaidParticipantState.Raiding, false, Vector2.zero, _betoController);

            // Real NetworkSpawnManager lookup MUST still find Beto in _disconnectedRegistry!
            Assert.IsTrue(
                _spawnManager.TryGetRaidParticipantCore(_betoId, _coordinator.ParticipantIdReaderForTest, out NetworkObject foundObj, out _),
                "NetworkSpawnManager must resolve retained disconnected participants.");
            Assert.AreSame(betoNetObj, foundObj);

            // 3. FixedUpdateNetwork cancels active countdown with RequiredMemberDisconnected on BOTH controllers!
            _coordinator.FixedUpdateNetwork();
            TeamExtractionSlot afterCancel = _coordinator.GetSlotForTest(0);
            Assert.IsFalse(afterCancel.IsActive, "Active countdown must cancel when required teammate disconnects.");
            Assert.AreEqual(TeamExtractionCancelReason.RequiredMemberDisconnected, afterCancel.LastCancelReason);
            Assert.AreEqual(_betoId, afterCancel.LastBlockingParticipantId);
            Assert.AreEqual(ExtractionState.None, _anaController.State);
            Assert.AreEqual(ExtractionState.None, _betoController.State);

            // 4. Subsequent tick while Beto is still retained-disconnected MUST NOT start solo extraction for Ana.
            _coordinator.FixedUpdateNetwork();
            Assert.IsFalse(_coordinator.GetSlotForTest(0).IsActive, "Ana must NOT extract alone while Beto is retained.");

            // 5. Ana chooses "Continuar sin compañero" -> Beto becomes Defeated and leaves _disconnectedRegistry.
            _states[betoNetObj] = (RaidParticipantState.Defeated, false, Vector2.zero, _betoController);
            GetDisconnectedRegistry(_spawnManager).Clear();

            // 6. Now Ana can extract solo!
            _coordinator.FixedUpdateNetwork();
            Assert.IsTrue(_coordinator.GetSlotForTest(0).IsActive,
                "After Continuar sin compañero marks Beto Defeated, Ana must be able to extract solo.");
            Assert.AreEqual(ExtractionState.InProgress, _anaController.State);
            Assert.IsNotNull(anaNetObj);
        }

        private GameObject CreateAvatar(string name, out PlayerExtractionController controller)
        {
            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();
            go.AddComponent<PlayerMovementNetworkController>();
            go.AddComponent<PlayerCombatNetworkController>();
            var character = go.AddComponent<TestCharacter>();
            controller = go.AddComponent<PlayerExtractionController>();
            SetField(controller, "_config", _config);
            SetField(controller, "_characterSource", character);
            return go;
        }

        private static Dictionary<PlayerRef, NetworkObject> GetSpawnedPlayers(NetworkSpawnManager manager) =>
            (Dictionary<PlayerRef, NetworkObject>)typeof(NetworkSpawnManager)
                .GetField("_spawnedPlayers", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(manager);

        private static DisconnectedParticipantRegistry GetDisconnectedRegistry(NetworkSpawnManager manager) =>
            (DisconnectedParticipantRegistry)typeof(NetworkSpawnManager)
                .GetField("_disconnectedRegistry", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(manager);

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private sealed class TestSanctuary : MonoBehaviour, IExtractionSanctuary
        {
            public EntityId SanctuaryId { get; set; }
            public EntityId Id => SanctuaryId;
            public bool IsAnyRitualCompleted => true;
            public bool IsReserved => true;
            public EntityId OwnerId => default;
            public ExtractionRitualState RitualState => ExtractionRitualState.Completed;
            public bool IsAssignedToTeam(RaidTeamId teamId) => true;
            public bool TryAssignTeam(RaidTeamId teamId) => true;
            public bool CanUseExtraction(RaidTeamId teamId) => true;
            public bool CanUseExtraction(EntityId participantId) => true;
            public bool IsOwnedBy(EntityId playerId) => true;
            public bool TryReserve(EntityId playerId) => true;
            public bool TryGetRitualProgress(RaidTeamId teamId, out ExtractionRitualSnapshot snapshot) { snapshot = default; return false; }
            public bool TryGetRitualProgress(out ExtractionRitualSnapshot snapshot) { snapshot = default; return false; }
        }

        private sealed class TestZone : MonoBehaviour, IExtractionZone
        {
            public EntityId ZoneId { get; set; }
            public EntityId Id => ZoneId;
            public bool IsAvailable => true;
            public bool ContainsExact(Vector2 position) => true;
            public bool ContainsWithTolerance(Vector2 position, float tolerance) => true;
            public bool TrySetAvailability(bool available) => true;
            public bool IsOccupant(EntityId participantId) => true;
        }

        private sealed class TestAssignmentService : ExtractionSanctuaryAssignmentService
        {
            private readonly Dictionary<RaidTeamId, EntityId> _assignments = new();
            public void Assign(RaidTeamId teamId, EntityId sanctuaryId) => _assignments[teamId] = sanctuaryId;
            public override SanctuaryAssignmentResult TryGetAssignment(RaidTeamId teamId)
            {
                if (_assignments.TryGetValue(teamId, out EntityId id))
                {
                    return SanctuaryAssignmentResult.Assigned(teamId, id, true);
                }
                return SanctuaryAssignmentResult.Rejected(teamId, SanctuaryAssignmentFailureReason.NoSanctuariesConfigured);
            }
        }

        private sealed class TestCharacter : MonoBehaviour, ICharacter
        {
            public EntityId Id => new EntityId(1);
            public bool IsAlive => true;
            public bool IsDowned => false;
        }
    }
}
