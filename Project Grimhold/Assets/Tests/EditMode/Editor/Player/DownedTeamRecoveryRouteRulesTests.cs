using System.Collections.Generic;
using NUnit.Framework;

namespace Tests.EditMode.Player
{
    /// <summary>Team recovery-route rule behind Accelerated Resolution (GD 13 section 9, S1/S4/S5/S8).</summary>
    public sealed class DownedTeamRecoveryRouteRulesTests
    {
        private static DownedTeamMemberRouteSnapshot Active(bool connected = true)
        {
            return new DownedTeamMemberRouteSnapshot(
                isRaiding: true, hasAvatar: true, isDowned: false, isAlive: true,
                isConnected: connected, canBeginSelf: false, activeSessionKind: RecoveryKind.None);
        }

        private static DownedTeamMemberRouteSnapshot Downed(
            bool connected = true, bool canBeginSelf = false, RecoveryKind session = RecoveryKind.None)
        {
            return new DownedTeamMemberRouteSnapshot(
                isRaiding: true, hasAvatar: true, isDowned: true, isAlive: true,
                isConnected: connected, canBeginSelf: canBeginSelf, activeSessionKind: session);
        }

        private static DownedTeamMemberRouteSnapshot Out(RaidParticipantState state)
        {
            return new DownedTeamMemberRouteSnapshot(
                isRaiding: state == RaidParticipantState.Raiding, hasAvatar: true, isDowned: false,
                isAlive: state != RaidParticipantState.Defeated,
                isConnected: true, canBeginSelf: false, activeSessionKind: RecoveryKind.None);
        }

        private static bool Accelerate(params DownedTeamMemberRouteSnapshot[] team)
        {
            return DownedTeamRecoveryRouteRules.ShouldAccelerate(team);
        }

        [Test]
        public void Solo_DownedWithoutSelfReviver_Accelerates()
        {
            Assert.That(Accelerate(Downed()), Is.True);
        }

        [Test]
        public void Solo_DownedWithUsableSelfReviver_KeepsNormalDrain()
        {
            Assert.That(Accelerate(Downed(canBeginSelf: true)), Is.False);
        }

        [Test]
        public void Solo_DownedWithSelfReviveAlreadyUsed_Accelerates()
        {
            // A used reviver is reported by CanBeginSelf as false; nothing else offers a route.
            Assert.That(Accelerate(Downed(canBeginSelf: false)), Is.True);
        }

        [Test]
        public void Duo_ActiveConnectedTeammateAndDownedMember_KeepsNormalDrain()
        {
            Assert.That(Accelerate(Active(), Downed()), Is.False);
            Assert.That(Accelerate(Downed(), Active()), Is.False);
        }

        [Test]
        public void Duo_BothDownedWithoutSelfReviver_Accelerates()
        {
            Assert.That(Accelerate(Downed(), Downed()), Is.True);
        }

        [Test]
        public void Duo_BothDownedAndOneCanSelfRevive_KeepsNormalDrain()
        {
            Assert.That(Accelerate(Downed(), Downed(canBeginSelf: true)), Is.False);
            Assert.That(Accelerate(Downed(canBeginSelf: true), Downed()), Is.False);
        }

        [Test]
        public void Duo_ActiveTeammateDisconnected_AcceleratesTheDownedMember()
        {
            Assert.That(Accelerate(Active(connected: false), Downed()), Is.True);
        }

        [TestCase(RaidParticipantState.Extracted)]
        [TestCase(RaidParticipantState.Defeated)]
        [TestCase(RaidParticipantState.Aborted)]
        public void Duo_TeammateNoLongerRaiding_AcceleratesTheDownedMember(RaidParticipantState state)
        {
            Assert.That(Accelerate(Out(state), Downed()), Is.True);
        }

        [Test]
        public void Duo_RaidingTeammateWithoutResolvableAvatar_IsUnknownAndNeverAccelerates()
        {
            // A Raiding participant always owns an avatar, so one that does not resolve yet is not
            // "gone": acceleration latches, so the team waits until the member is known.
            var noAvatar = new DownedTeamMemberRouteSnapshot(
                isRaiding: true, hasAvatar: false, isDowned: false, isAlive: false,
                isConnected: true, canBeginSelf: false, activeSessionKind: RecoveryKind.None);

            Assert.That(Accelerate(noAvatar, Downed()), Is.False);
            Assert.That(Accelerate(Downed(), noAvatar), Is.False);
        }

        [Test]
        public void Duo_FinishedTeammateWithoutAvatar_StillCountsAsFinished()
        {
            var noAvatar = new DownedTeamMemberRouteSnapshot(
                isRaiding: false, hasAvatar: false, isDowned: false, isAlive: false,
                isConnected: true, canBeginSelf: false, activeSessionKind: RecoveryKind.None);

            Assert.That(Accelerate(noAvatar, Downed()), Is.True);
        }

        [Test]
        public void Downed_WithValidSelfSessionInProgress_KeepsNormalDrainEvenWhenDisconnected()
        {
            Assert.That(Accelerate(Downed(connected: true, session: RecoveryKind.Self)), Is.False);
            Assert.That(Accelerate(Downed(connected: false, session: RecoveryKind.Self)), Is.False);
        }

        [TestCase(RecoveryKind.Assisted, true)]
        [TestCase(RecoveryKind.Assisted, false)]
        [TestCase(RecoveryKind.Handoff, true)]
        [TestCase(RecoveryKind.Handoff, false)]
        public void Downed_WithAnyValidRecoverySessionInProgress_KeepsNormalDrain(RecoveryKind session, bool connected)
        {
            // A running revive or handoff is a recovery route even when its reviver or giver is
            // disconnected but the session is still valid: accelerating would latch over a recovery.
            Assert.That(Accelerate(Active(connected: false), Downed(connected: connected, session: session)), Is.False);
        }

        [Test]
        public void Downed_DisconnectedWithoutSessionButCarryingReviver_Accelerates()
        {
            Assert.That(Accelerate(Downed(connected: false, canBeginSelf: true)), Is.True);
        }

        [Test]
        public void Team_WithoutDownedRaidingMember_NeverAccelerates()
        {
            Assert.That(Accelerate(Active()), Is.False);
            Assert.That(Accelerate(Active(), Active()), Is.False);
            Assert.That(Accelerate(Active(connected: false)), Is.False);
            Assert.That(Accelerate(Out(RaidParticipantState.Extracted)), Is.False);
            Assert.That(Accelerate(Out(RaidParticipantState.Defeated), Active(connected: false)), Is.False);
            Assert.That(Accelerate(), Is.False);
        }

        [Test]
        public void DownedMemberNotRaiding_IsNotAcceleratedAndProvidesNoRoute()
        {
            var extractedDowned = new DownedTeamMemberRouteSnapshot(
                isRaiding: false, hasAvatar: true, isDowned: true, isAlive: true,
                isConnected: true, canBeginSelf: true, activeSessionKind: RecoveryKind.Self);

            Assert.That(DownedTeamRecoveryRouteRules.IsAcceleratable(extractedDowned), Is.False);
            Assert.That(DownedTeamRecoveryRouteRules.ProvidesRoute(extractedDowned), Is.False);
            Assert.That(Accelerate(extractedDowned, Downed()), Is.True);
        }

        [Test]
        public void IsAcceleratable_OnlyForDownedRaidingMemberWithAvatar()
        {
            Assert.That(DownedTeamRecoveryRouteRules.IsAcceleratable(Downed()), Is.True);
            Assert.That(DownedTeamRecoveryRouteRules.IsAcceleratable(Downed(connected: false)), Is.True);
            Assert.That(DownedTeamRecoveryRouteRules.IsAcceleratable(Active()), Is.False);
        }

        [Test]
        public void Duo_TeammateStateUnknown_NeverAcceleratesTheDownedMember()
        {
            // Acceleration latches, so a member whose state is not known (default snapshot) must never be
            // read as "no route": the team is not accelerated until every member is known.
            var unknown = default(DownedTeamMemberRouteSnapshot);

            Assert.That(Accelerate(unknown, Downed()), Is.False);
            Assert.That(Accelerate(Downed(), unknown), Is.False);
            Assert.That(Accelerate(unknown, Downed(), Downed()), Is.False);
        }

        [Test]
        public void UnknownMember_IsNeitherARouteNorAcceleratable()
        {
            var unknown = default(DownedTeamMemberRouteSnapshot);

            Assert.That(unknown.IsKnown, Is.False);
            Assert.That(DownedTeamRecoveryRouteRules.ProvidesRoute(unknown), Is.False);
            Assert.That(DownedTeamRecoveryRouteRules.IsAcceleratable(unknown), Is.False);
            Assert.That(Downed().IsKnown, Is.True);
        }

        [Test]
        public void Team_WithEveryMemberKnown_StillAcceleratesWhenNoRouteExists()
        {
            // Known facts (a teammate that is no longer Raiding) keep S4 working for a Duo turned Solo.
            Assert.That(Accelerate(Out(RaidParticipantState.Extracted), Downed()), Is.True);
            Assert.That(Accelerate(Out(RaidParticipantState.Aborted), Downed()), Is.True);
        }

        [Test]
        public void ShouldAccelerate_AcceptsAListWithoutMutatingIt()
        {
            var team = new List<DownedTeamMemberRouteSnapshot> { Downed() };

            Assert.That(DownedTeamRecoveryRouteRules.ShouldAccelerate(team), Is.True);
            Assert.That(team, Has.Count.EqualTo(1));
        }
    }
}
