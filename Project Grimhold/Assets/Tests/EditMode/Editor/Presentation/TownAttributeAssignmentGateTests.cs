#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace Tests.EditMode.Presentation
{
    /// <summary>
    /// TASK-304 T7 (S11): the interlock the attribute panel and the Mentor respec share. Assignment waits for a
    /// pending respec, the respec waits for an attribute commit in flight, and both obey the Ready lock.
    /// </summary>
    public sealed class TownAttributeAssignmentGateTests
    {
        [Test]
        public void NewGate_IsIdle_AndMutableWithoutAReadyLockSource()
        {
            var gate = new TownAttributeAssignmentGate(null);

            Assert.That(gate.CanMutateBuild, Is.True);
            Assert.That(gate.IsCommitInFlight, Is.False);
            Assert.That(gate.IsRespecPending, Is.False);
            Assert.That(gate.IsAssignmentBlocked, Is.False);
            Assert.That(gate.IsRespecBlocked, Is.False);
        }

        [Test]
        public void CanMutateBuild_FollowsTheReadyLockSourceLive()
        {
            bool canMutate = true;
            var gate = new TownAttributeAssignmentGate(() => canMutate);

            Assert.That(gate.CanMutateBuild, Is.True);
            canMutate = false;
            Assert.That(gate.CanMutateBuild, Is.False);
            canMutate = true;
            Assert.That(gate.CanMutateBuild, Is.True);
        }

        [Test]
        public void PendingRespec_BlocksAssignment_ButNotAnotherRespecRequest()
        {
            var gate = new TownAttributeAssignmentGate(null) { IsRespecPending = true };

            Assert.That(gate.IsAssignmentBlocked, Is.True);
            Assert.That(gate.IsRespecBlocked, Is.False, "the session itself already ignores a second request while pending");
        }

        [Test]
        public void CommitInFlight_BlocksTheRespec_AndAnotherAssignment()
        {
            var gate = new TownAttributeAssignmentGate(null) { IsCommitInFlight = true };

            Assert.That(gate.IsRespecBlocked, Is.True);
            Assert.That(gate.IsAssignmentBlocked, Is.True);
        }

        [Test]
        public void Releasing_BothFlags_UnblocksBothSides()
        {
            var gate = new TownAttributeAssignmentGate(null) { IsCommitInFlight = true, IsRespecPending = true };

            gate.IsCommitInFlight = false;
            gate.IsRespecPending = false;

            Assert.That(gate.IsAssignmentBlocked, Is.False);
            Assert.That(gate.IsRespecBlocked, Is.False);
        }
    }
}
#endif
