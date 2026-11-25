using System;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

namespace Tests.EditMode.Scenario
{
    public sealed class TeamSanctuaryTests
    {
        private GameObject _root;
        private ExtractionSanctuary _sanctuary;
        private ExtractionZone _zone;
        private ExtractionConfig _config;
        private EntityRegistry _registry;
        private ExtractionSanctuaryAssignmentService _assignmentService;
        private BoxCollider2D _collider;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TestSanctuary");
            _root.AddComponent<NetworkObject>();
            _collider = _root.AddComponent<BoxCollider2D>();
            _zone = _root.AddComponent<ExtractionZone>();
            _sanctuary = _root.AddComponent<ExtractionSanctuary>();

            _config = ScriptableObject.CreateInstance<ExtractionConfig>();
            SetField(_config, "_ritualDurationSeconds", 5f);
            SetField(_config, "_soloProgressQuota", 100);
            SetField(_config, "_duoProgressQuota", 150);
            SetField(_config, "_maxTeamsPerRaid", 4);

            _registry = _root.AddComponent<EntityRegistry>();
            _assignmentService = _root.AddComponent<ExtractionSanctuaryAssignmentService>();

            SetField(_sanctuary, "_config", _config);
            SetField(_sanctuary, "_extractionZone", _zone);
            SetField(_sanctuary, "_interactionCollider", _collider);
            SetField(_sanctuary, "_registry", _registry);
            SetField(_sanctuary, "_assignmentService", _assignmentService);
            SetField(_sanctuary, "_compositionValid", true);
            _sanctuary.SetAllowEditModeSimulation(true);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
            UnityEngine.Object.DestroyImmediate(_config);
        }

        [Test]
        public void TryAssignTeam_AssignsSlotAndIsIdempotent()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId team1), Is.True);

            Assert.That(_sanctuary.IsAssignedToTeam(team1), Is.False);
            Assert.That(_sanctuary.TryAssignTeam(team1), Is.True);
            Assert.That(_sanctuary.IsAssignedToTeam(team1), Is.True);

            // Re-assigning same team is idempotent
            Assert.That(_sanctuary.TryAssignTeam(team1), Is.True);
            Assert.That(_sanctuary.IsAssignedToTeam(team1), Is.True);
        }

        [Test]
        public void TryAssignTeam_AssignsSeveralTeamsToTheSameSanctuary()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId team1), Is.True);
            Assert.That(RaidTeamId.TryCreate(2, out RaidTeamId team2), Is.True);

            Assert.That(_sanctuary.TryAssignTeam(team1), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(team2), Is.True,
                "Several teams may receive the same Sanctuary simultaneously (GD 10 section 8).");

            Assert.That(_sanctuary.IsAssignedToTeam(team1), Is.True, "The first team keeps its assignment.");
            Assert.That(_sanctuary.IsAssignedToTeam(team2), Is.True);
            Assert.That(_sanctuary.IsReserved, Is.True);
            Assert.That(_sanctuary.TryAssignTeam(team1), Is.True, "Each team's assignment stays idempotent.");
            Assert.That(_sanctuary.TryAssignTeam(team2), Is.True);
        }

        [Test]
        public void CanInteract_RequiresAssignedTeamAndActiveAliveCharacter()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId team1), Is.True);
            Assert.That(RaidTeamId.TryCreate(2, out RaidTeamId unassignedTeam), Is.True);
            _sanctuary.TryAssignTeam(team1);

            var characterObj = new GameObject("TestPlayer");
            MockPlayerCharacter mockChar = characterObj.AddComponent<MockPlayerCharacter>();
            var charId = new EntityId(10);
            mockChar.AssignedId = charId;
            mockChar.IsAliveState = true;
            mockChar.IsDownedState = false;

            Assert.That(_registry.TryRegisterEntity(charId, mockChar, null), Is.True);

            try
            {
                // Unassigned team cannot interact
                _sanctuary.OverrideTeamIdForTest = unassignedTeam;
                var request = new InteractionRequest(charId, _sanctuary.Id, 0);
                Assert.That(_sanctuary.CanInteract(request), Is.False);

                // Assigned team, active character can interact
                _sanctuary.OverrideTeamIdForTest = team1;
                Assert.That(_sanctuary.CanInteract(request), Is.True);

                // Downed character cannot initiate ritual
                mockChar.IsDownedState = true;
                Assert.That(_sanctuary.CanInteract(request), Is.False);

                // Dead character cannot initiate ritual
                mockChar.IsDownedState = false;
                mockChar.IsAliveState = false;
                Assert.That(_sanctuary.CanInteract(request), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(characterObj);
            }
        }

        [Test]
        public void Interact_StartsRitualOnlyForAssignedInteractingTeam()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId team1), Is.True);
            Assert.That(RaidTeamId.TryCreate(2, out RaidTeamId team2), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(team1), Is.True);

            var characterObj = new GameObject("Player1");
            MockPlayerCharacter mockChar = characterObj.AddComponent<MockPlayerCharacter>();
            var charId = new EntityId(15);
            mockChar.AssignedId = charId;
            mockChar.IsAliveState = true;
            mockChar.IsDownedState = false;
            Assert.That(_registry.TryRegisterEntity(charId, mockChar, null), Is.True);

            try
            {
                _sanctuary.OverrideTeamIdForTest = team1;
                var request = new InteractionRequest(charId, _sanctuary.Id, 0);

                InteractionResult result = _sanctuary.Interact(request);
                Assert.That(result.Success, Is.True);

                Assert.That(_sanctuary.TryGetRitualProgress(team1, out ExtractionRitualSnapshot snap1), Is.True);
                Assert.That(snap1.State, Is.EqualTo(ExtractionRitualState.InProgress));

                Assert.That(_sanctuary.TryGetRitualProgress(team2, out _), Is.False,
                    "A team without the reservation has no ritual on this Sanctuary.");

                _sanctuary.OverrideTeamIdForTest = team2;
                Assert.That(_sanctuary.CanInteract(request), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(characterObj);
            }
        }

        [Test]
        public void SharedSanctuary_EachTeamRunsItsOwnRitualWithoutAffectingTheOther()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId team1), Is.True);
            Assert.That(RaidTeamId.TryCreate(2, out RaidTeamId team2), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(team1), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(team2), Is.True);

            var characterObj = new GameObject("SharedSanctuaryPlayer");
            MockPlayerCharacter mockChar = characterObj.AddComponent<MockPlayerCharacter>();
            var charId = new EntityId(25);
            mockChar.AssignedId = charId;
            Assert.That(_registry.TryRegisterEntity(charId, mockChar, null), Is.True);

            try
            {
                var request = new InteractionRequest(charId, _sanctuary.Id, 0);

                _sanctuary.OverrideTeamIdForTest = team1;
                Assert.That(_sanctuary.Interact(request).Success, Is.True);

                Assert.That(_sanctuary.TryGetRitualProgress(team2, out ExtractionRitualSnapshot untouched), Is.True);
                Assert.That(untouched.State, Is.EqualTo(ExtractionRitualState.NotStarted),
                    "Starting one team's ritual must not start or block the other team's ritual.");

                _sanctuary.OverrideTeamIdForTest = team2;
                Assert.That(_sanctuary.CanInteract(request), Is.True,
                    "The second team can start its ritual while the first team's ritual runs.");
                Assert.That(_sanctuary.Interact(request).Success, Is.True);

                // Team 1 loses every required member; team 2 keeps one.
                _sanctuary.RequiredMemberCountProviderForTest = team => team == team1 ? 0 : 1;
                _sanctuary.FixedUpdateNetwork();

                Assert.That(_sanctuary.TryGetRitualProgress(team1, out ExtractionRitualSnapshot cancelled), Is.True);
                Assert.That(cancelled.State, Is.EqualTo(ExtractionRitualState.Cancelled));
                Assert.That(_sanctuary.TryGetRitualProgress(team2, out ExtractionRitualSnapshot running), Is.True);
                Assert.That(running.State, Is.EqualTo(ExtractionRitualState.InProgress),
                    "Cancelling one team's ritual must not cancel the other team's ritual.");
                Assert.That(_sanctuary.IsAssignedToTeam(team1), Is.True);
                Assert.That(_sanctuary.IsAssignedToTeam(team2), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(characterObj);
            }
        }

        [Test]
        public void RitualProgression_CancelsOnlyWhenRemainingRequiredCountReachesZero()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId team1), Is.True);
            _sanctuary.TryAssignTeam(team1);

            var characterObj = new GameObject("Player");
            MockPlayerCharacter mockChar = characterObj.AddComponent<MockPlayerCharacter>();
            var charId = new EntityId(20);
            mockChar.AssignedId = charId;
            mockChar.IsAliveState = true;
            mockChar.IsDownedState = false;
            Assert.That(_registry.TryRegisterEntity(charId, mockChar, null), Is.True);

            try
            {
                _sanctuary.OverrideTeamIdForTest = team1;
                var request = new InteractionRequest(charId, _sanctuary.Id, 0);
                Assert.That(_sanctuary.Interact(request).Success, Is.True);

                // Provide 1 remaining member: ritual does NOT cancel
                _sanctuary.RequiredMemberCountProviderForTest = _ => 1;
                _sanctuary.FixedUpdateNetwork();

                Assert.That(_sanctuary.TryGetRitualProgress(team1, out ExtractionRitualSnapshot snap), Is.True);
                Assert.That(snap.State, Is.EqualTo(ExtractionRitualState.InProgress));

                // When remaining required count reaches 0, ritual cancels per A-4 / D-5
                _sanctuary.RequiredMemberCountProviderForTest = _ => 0;
                _sanctuary.FixedUpdateNetwork();

                Assert.That(_sanctuary.TryGetRitualProgress(team1, out ExtractionRitualSnapshot cancelledSnap), Is.True);
                Assert.That(cancelledSnap.State, Is.EqualTo(ExtractionRitualState.Cancelled));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(characterObj);
            }
        }

        [Test]
        public void DuoMembers_ResolveToTheSameTeamKeyedSanctuaryAssignment()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId duoTeam), Is.True);
            Assert.That(RaidTeamId.TryCreate(2, out RaidTeamId rivalTeam), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(duoTeam), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(rivalTeam), Is.True);
            SetRitualState(_sanctuary, duoTeam, ExtractionRitualState.Completed);

            var firstMemberObj = new GameObject("DuoMember1");
            var secondMemberObj = new GameObject("DuoMember2");
            MockPlayerCharacter firstMember = firstMemberObj.AddComponent<MockPlayerCharacter>();
            MockPlayerCharacter secondMember = secondMemberObj.AddComponent<MockPlayerCharacter>();
            firstMember.AssignedId = new EntityId(30);
            secondMember.AssignedId = new EntityId(31);

            try
            {
                // Both members of the Duo resolve to the same team, hence to the same Sanctuary slot.
                _sanctuary.OverrideTeamIdForTest = duoTeam;
                Assert.That(_sanctuary.IsOwnedBy(firstMember.AssignedId), Is.True);
                Assert.That(_sanctuary.IsOwnedBy(secondMember.AssignedId), Is.True);
                Assert.That(_sanctuary.CanUseExtraction(firstMember.AssignedId), Is.True);
                Assert.That(_sanctuary.CanUseExtraction(secondMember.AssignedId), Is.True,
                    "The ritual is the team's, so every Duo member shares its outcome.");

                // A member of the other team sharing the Sanctuary gets that team's own slot, not the Duo's.
                _sanctuary.OverrideTeamIdForTest = rivalTeam;
                Assert.That(_sanctuary.IsOwnedBy(firstMember.AssignedId), Is.True);
                Assert.That(_sanctuary.CanUseExtraction(firstMember.AssignedId), Is.False,
                    "The shared Sanctuary does not hand the Duo's completed ritual to another team.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(firstMemberObj);
                UnityEngine.Object.DestroyImmediate(secondMemberObj);
            }
        }

        [Test]
        public void CopyStateFrom_RestoresEverySharedTeamSlotIdentically()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId completedTeam), Is.True);
            Assert.That(RaidTeamId.TryCreate(2, out RaidTeamId runningTeam), Is.True);
            Assert.That(RaidTeamId.TryCreate(3, out RaidTeamId lateTeam), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(completedTeam), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(runningTeam), Is.True);
            SetRitualState(_sanctuary, completedTeam, ExtractionRitualState.Completed);
            SetRitualState(_sanctuary, runningTeam, ExtractionRitualState.InProgress);

            var restoredObject = new GameObject("RestoredSanctuary");
            try
            {
                restoredObject.AddComponent<NetworkObject>();
                restoredObject.AddComponent<BoxCollider2D>();
                restoredObject.AddComponent<ExtractionZone>();
                ExtractionSanctuary restored = restoredObject.AddComponent<ExtractionSanctuary>();
                SetField(restored, "_config", _config);
                SetField(restored, "_compositionValid", true);
                restored.SetAllowEditModeSimulation(true);

                restored.CopyStateFrom(_sanctuary);

                Assert.That(ReadSlots(restored), Is.EqualTo(ReadSlots(_sanctuary)),
                    "Slot order, team identities and ritual states are restored exactly.");
                Assert.That(restored.IsAssignedToTeam(completedTeam), Is.True);
                Assert.That(restored.IsAssignedToTeam(runningTeam), Is.True);
                Assert.That(restored.IsAssignedToTeam(lateTeam), Is.False);
                Assert.That(restored.IsReserved, Is.True);
                Assert.That(restored.CanUseExtraction(completedTeam), Is.True);
                Assert.That(restored.CanUseExtraction(runningTeam), Is.False);
                Assert.That(restored.TryGetRitualProgress(completedTeam, out ExtractionRitualSnapshot completed), Is.True);
                Assert.That(completed.State, Is.EqualTo(ExtractionRitualState.Completed));
                Assert.That(restored.TryGetRitualProgress(runningTeam, out ExtractionRitualSnapshot running), Is.True);
                Assert.That(running.State, Is.EqualTo(ExtractionRitualState.InProgress));

                // The restored Sanctuary keeps both assignments idempotent and still accepts a later team.
                Assert.That(restored.TryAssignTeam(completedTeam), Is.True);
                Assert.That(restored.TryAssignTeam(runningTeam), Is.True);
                Assert.That(restored.TryAssignTeam(lateTeam), Is.True);
                Assert.That(restored.CanUseExtraction(completedTeam), Is.True, "Re-assignment never resets a restored ritual.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(restoredObject);
            }
        }

        [Test]
        public void PublicAggregate_IdleLowerSlotAndRunningLaterSlot_ReportsTheRunningRitual()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId idleTeam), Is.True);
            Assert.That(RaidTeamId.TryCreate(2, out RaidTeamId runningTeam), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(idleTeam), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(runningTeam), Is.True);
            SetRitualState(_sanctuary, runningTeam, ExtractionRitualState.InProgress);

            Assert.That(_sanctuary.TryGetRitualProgress(out ExtractionRitualSnapshot aggregate), Is.True);
            Assert.That(aggregate.State, Is.EqualTo(ExtractionRitualState.InProgress),
                "Any team in ritual lights the shared Sanctuary for everyone (L4), whatever its slot order.");
        }

        [Test]
        public void PublicAggregate_CompletedLowerSlotAndRunningLaterSlot_PrefersTheRunningRitual()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId completedTeam), Is.True);
            Assert.That(RaidTeamId.TryCreate(2, out RaidTeamId runningTeam), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(completedTeam), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(runningTeam), Is.True);
            SetRitualState(_sanctuary, completedTeam, ExtractionRitualState.Completed);
            SetRitualState(_sanctuary, runningTeam, ExtractionRitualState.InProgress);

            Assert.That(_sanctuary.TryGetRitualProgress(out ExtractionRitualSnapshot aggregate), Is.True);
            Assert.That(aggregate.State, Is.EqualTo(ExtractionRitualState.InProgress));
        }

        [Test]
        public void PublicAggregate_IdleLowerSlotAndCompletedLaterSlot_ReportsCompleted()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId idleTeam), Is.True);
            Assert.That(RaidTeamId.TryCreate(2, out RaidTeamId completedTeam), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(idleTeam), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(completedTeam), Is.True);
            SetRitualState(_sanctuary, completedTeam, ExtractionRitualState.Completed);

            Assert.That(_sanctuary.TryGetRitualProgress(out ExtractionRitualSnapshot aggregate), Is.True);
            Assert.That(aggregate.State, Is.EqualTo(ExtractionRitualState.Completed));
            Assert.That(aggregate.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void PublicAggregate_NoRunningOrCompletedRitual_FallsBackToTheFirstAssignedSlot()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId cancelledTeam), Is.True);
            Assert.That(RaidTeamId.TryCreate(2, out RaidTeamId idleTeam), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(cancelledTeam), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(idleTeam), Is.True);
            SetRitualState(_sanctuary, cancelledTeam, ExtractionRitualState.Cancelled);

            Assert.That(_sanctuary.TryGetRitualProgress(out ExtractionRitualSnapshot aggregate), Is.True);
            Assert.That(aggregate.State, Is.EqualTo(ExtractionRitualState.Cancelled));
        }

        [Test]
        public void PublicAggregate_WithoutAssignedTeam_IsUnavailable()
        {
            Assert.That(_sanctuary.TryGetRitualProgress(out ExtractionRitualSnapshot aggregate), Is.False);
            Assert.That(aggregate, Is.EqualTo(default(ExtractionRitualSnapshot)));
        }

        [Test]
        public void PublicAggregate_SingleTeam_EqualsThatTeamSnapshotInEveryState()
        {
            Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId team), Is.True);
            Assert.That(_sanctuary.TryAssignTeam(team), Is.True);

            foreach (ExtractionRitualState state in new[]
            {
                ExtractionRitualState.NotStarted,
                ExtractionRitualState.InProgress,
                ExtractionRitualState.Cancelled,
                ExtractionRitualState.Completed
            })
            {
                SetRitualState(_sanctuary, team, state);

                Assert.That(_sanctuary.TryGetRitualProgress(team, out ExtractionRitualSnapshot own), Is.True, state.ToString());
                Assert.That(_sanctuary.TryGetRitualProgress(out ExtractionRitualSnapshot aggregate), Is.True, state.ToString());
                Assert.That(aggregate, Is.EqualTo(own), $"A single-team Sanctuary behaves as before in {state}.");
            }
        }

        private static void SetRitualState(ExtractionSanctuary sanctuary, RaidTeamId teamId, ExtractionRitualState state)
        {
            TeamSanctuarySlot[] slots = ReadSlots(sanctuary);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].TeamIdRaw == teamId.Value)
                {
                    slots[i].RitualState = state;
                    return;
                }
            }

            Assert.Fail($"Team {teamId.Value} has no slot to update.");
        }

        private static TeamSanctuarySlot[] ReadSlots(ExtractionSanctuary sanctuary)
        {
            FieldInfo field = typeof(ExtractionSanctuary).GetField("_editModeSlots", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (TeamSanctuarySlot[])field.GetValue(sanctuary);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }

        private sealed class MockPlayerCharacter : MonoBehaviour, ICharacter
        {
            public EntityId AssignedId { get; set; }
            public bool IsAliveState = true;
            public bool IsDownedState = false;

            public EntityId Id => AssignedId;
            public bool IsAlive => IsAliveState;
            public bool IsDowned => IsDownedState;
        }
    }
}
