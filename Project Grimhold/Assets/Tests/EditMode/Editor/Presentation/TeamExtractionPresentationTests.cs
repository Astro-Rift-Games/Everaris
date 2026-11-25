#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Presentation
{
    public sealed class TeamExtractionPresentationTests
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";

        private GameObject _instance;
        private RaidHudPresenter _presenter;
        private RaidHudView _view;

        [SetUp]
        public void SetUp()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            _instance = Object.Instantiate(prefab);
            _instance.SetActive(false);
            _presenter = _instance.GetComponentInChildren<RaidHudPresenter>(true);
            _view = _instance.GetComponentInChildren<RaidHudView>(true);
            Assert.That(_presenter, Is.Not.Null);
            Assert.That(_view, Is.Not.Null);
            _presenter.Bind(null, null, null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_instance);
        }

        [Test]
        public void View_PresentSanctuaryAssigned_WithTeamPresence_FormatsCorrectly()
        {
            _view.PresentSanctuaryAssigned(1, 2);
            Assert.That(_view.SanctuaryText.text, Is.EqualTo("Santuario asignado (1/2 en el área)"));

            _view.PresentSanctuaryAssigned(2, 2);
            Assert.That(_view.SanctuaryText.text, Is.EqualTo("Santuario asignado (2/2 en el área)"));

            _view.PresentSanctuaryAssigned(1, 1);
            Assert.That(_view.SanctuaryText.text, Is.EqualTo("Santuario asignado"));

            _view.PresentSanctuaryAssigned(0, 0);
            Assert.That(_view.SanctuaryText.text, Is.EqualTo("Santuario asignado"));
        }

        [Test]
        public void View_PresentExtractionCountdown_WithTeamPresence_FormatsCorrectly()
        {
            _view.PresentExtractionCountdown(2.4f, 1, 2);
            Assert.That(_view.ExtractionText.text, Does.Contain("(1/2 en el área)"));
            Assert.That(_view.ExtractionText.text, Does.StartWith("Extracción: "));

            _view.PresentExtractionCountdown(2.4f, 2, 2);
            Assert.That(_view.ExtractionText.text, Does.Contain("(2/2 en el área)"));

            _view.PresentExtractionCountdown(2.4f, 1, 1);
            Assert.That(_view.ExtractionText.text, Does.Not.Contain("en el área"));

            _view.PresentExtractionCountdown(2.4f, 0, 0);
            Assert.That(_view.ExtractionText.text, Does.Not.Contain("en el área"));
        }

        [Test]
        public void Presenter_SanctuarySectionPresence_UpdatesViewAndRespectsDirtyCheck()
        {
            InvokeRefreshSanctuarySectionPresence(true, 1, 2);
            Assert.That(_view.SanctuaryText.text, Is.EqualTo("Santuario asignado (1/2 en el área)"));

            _view.SanctuaryText.text = "dirty-marker";
            InvokeRefreshSanctuarySectionPresence(true, 1, 2);
            Assert.That(_view.SanctuaryText.text, Is.EqualTo("dirty-marker"), "Dirty check should skip re-presentation for identical state.");

            InvokeRefreshSanctuarySectionPresence(true, 2, 2);
            Assert.That(_view.SanctuaryText.text, Is.EqualTo("Santuario asignado (2/2 en el área)"), "Changed presence must trigger presentation.");

            InvokeRefreshSanctuarySectionPresence(false, 0, 0);
            Assert.That(_view.SanctuaryText.text, Is.Empty, "Disabling sanctuary must clear the section.");
        }

        [Test]
        public void Presenter_RitualStatusSectionPresence_CountdownWithPresence_UpdatesViewAndRespectsDirtyCheck()
        {
            var countdown = new ExtractionCountdownSnapshot(
                ExtractionState.InProgress,
                new EntityId(10),
                2.5f,
                5f,
                0.5f);

            InvokeRefreshRitualStatusSectionPresence(true, countdown, true, default, 2, 2);
            Assert.That(_view.ExtractionText.text, Does.Contain("(2/2 en el área)"));

            _view.ExtractionText.text = "dirty-marker-countdown";
            InvokeRefreshRitualStatusSectionPresence(true, countdown, true, default, 2, 2);
            Assert.That(_view.ExtractionText.text, Is.EqualTo("dirty-marker-countdown"), "Dirty check should skip re-presentation for identical countdown presence.");

            InvokeRefreshRitualStatusSectionPresence(true, countdown, true, default, 1, 2);
            Assert.That(_view.ExtractionText.text, Does.Contain("(1/2 en el área)"), "Changed presence must update the countdown view.");
        }

        [Test]
        public void ExtractionSanctuaryPresenter_ResolveObservedCues_AssignedAndCancelledArePrivateToAssignedTeam()
        {
            const ExtractionRitualState Idle = ExtractionRitualState.NotStarted;
            const ExtractionRitualState Running = ExtractionRitualState.InProgress;
            const ExtractionRitualState Cancelled = ExtractionRitualState.Cancelled;
            const ExtractionRitualState Done = ExtractionRitualState.Completed;

            // Assigned cue: only for local owner
            AssertCue("Assigned", true, Idle, Idle, Idle, Idle, previouslyReserved: false, currentlyReserved: true, isLocalOwner: true);
            AssertCue("None", true, Idle, Idle, Idle, Idle, previouslyReserved: false, currentlyReserved: true, isLocalOwner: false);

            // Cancelled cue: only for the local owner's own ritual (single team: world == own)
            AssertCue("Cancelled", true, Running, Cancelled, Running, Cancelled, previouslyReserved: true, currentlyReserved: true, isLocalOwner: true);
            AssertCue("None", true, Running, Cancelled, Idle, Idle, previouslyReserved: true, currentlyReserved: true, isLocalOwner: false);

            // Started cue: public (both local owner and rivals hear it)
            AssertCue("Started", true, Idle, Running, Idle, Running, previouslyReserved: true, currentlyReserved: true, isLocalOwner: true);
            AssertCue("Started", true, Idle, Running, Idle, Idle, previouslyReserved: true, currentlyReserved: true, isLocalOwner: false);

            // Completed cue: public (both hear it)
            AssertCue("Completed", true, Running, Done, Running, Done, previouslyReserved: true, currentlyReserved: true, isLocalOwner: true);
            AssertCue("Completed", true, Running, Done, Idle, Idle, previouslyReserved: true, currentlyReserved: true, isLocalOwner: false);
        }

        [Test]
        public void ExtractionSanctuaryPresenter_ResolveObservedCues_AssignedTeamHearsAnotherTeamsRitualButNotItsCancellation()
        {
            const ExtractionRitualState Idle = ExtractionRitualState.NotStarted;
            const ExtractionRitualState Running = ExtractionRitualState.InProgress;
            const ExtractionRitualState Done = ExtractionRitualState.Completed;

            // Another team starts on the shared Sanctuary while the assigned team is idle.
            AssertCue("Started", true, Idle, Running, Idle, Idle, previouslyReserved: true, currentlyReserved: true, isLocalOwner: true);
            // Another team completes while the assigned team is idle.
            AssertCue("Completed", true, Running, Done, Idle, Idle, previouslyReserved: true, currentlyReserved: true, isLocalOwner: true);
            // Another team's ritual ending as Cancelled in the world signal is never announced as the own cancellation.
            AssertCue("None", true, Running, ExtractionRitualState.Cancelled, Idle, Idle, previouslyReserved: true, currentlyReserved: true, isLocalOwner: true);
        }

        [Test]
        public void ExtractionSanctuaryPresenter_ResolveObservedCues_OwnCancellationIsKeptWhenWorldSignalSwitchesToTheAggregate()
        {
            const ExtractionRitualState Idle = ExtractionRitualState.NotStarted;
            const ExtractionRitualState Running = ExtractionRitualState.InProgress;
            const ExtractionRitualState Cancelled = ExtractionRitualState.Cancelled;

            // Own ritual cancels; the world signal falls back to an idle aggregate (Running -> Idle).
            AssertCue("Cancelled", true, Running, Idle, Running, Cancelled, previouslyReserved: true, currentlyReserved: true, isLocalOwner: true);
            // Own ritual cancels while another team keeps running: the world stays lit, only the private cue plays.
            AssertCue("Cancelled", true, Running, Running, Running, Cancelled, previouslyReserved: true, currentlyReserved: true, isLocalOwner: true);
            // No repeat while the own state stays Cancelled.
            AssertCue("None", true, Idle, Idle, Cancelled, Cancelled, previouslyReserved: true, currentlyReserved: true, isLocalOwner: true);
        }

        [Test]
        public void ExtractionSanctuaryPresenter_ResolveWorldSnapshot_OwnRunningOrCompletedRitualDrivesTheLocalTeam()
        {
            ExtractionRitualSnapshot ownRunning = new ExtractionRitualSnapshot(ExtractionRitualState.InProgress, 10f, 7f, 0.3f);
            ExtractionRitualSnapshot ownDone = new ExtractionRitualSnapshot(ExtractionRitualState.Completed, 10f, 0f, 1f);
            ExtractionRitualSnapshot otherRunning = new ExtractionRitualSnapshot(ExtractionRitualState.InProgress, 10f, 2f, 0.8f);

            Assert.That(ResolveWorldSnapshot(ownRunning, otherRunning, isLocalOwner: true), Is.EqualTo(ownRunning));
            Assert.That(ResolveWorldSnapshot(ownDone, otherRunning, isLocalOwner: true), Is.EqualTo(ownDone));
        }

        [Test]
        public void ExtractionSanctuaryPresenter_ResolveWorldSnapshot_OtherTeamsRitualLightsTheWorldForIdleOrRivalViewers()
        {
            ExtractionRitualSnapshot otherRunning = new ExtractionRitualSnapshot(ExtractionRitualState.InProgress, 10f, 2f, 0.8f);
            ExtractionRitualSnapshot ownIdle = new ExtractionRitualSnapshot(ExtractionRitualState.NotStarted, 10f, 10f, 0f);
            ExtractionRitualSnapshot ownCancelled = new ExtractionRitualSnapshot(ExtractionRitualState.Cancelled, 10f, 10f, 0f);

            Assert.That(ResolveWorldSnapshot(ownIdle, otherRunning, isLocalOwner: true), Is.EqualTo(otherRunning));
            Assert.That(ResolveWorldSnapshot(ownCancelled, otherRunning, isLocalOwner: true), Is.EqualTo(otherRunning));
            Assert.That(ResolveWorldSnapshot(default, otherRunning, isLocalOwner: true), Is.EqualTo(otherRunning),
                "An unavailable own snapshot never drives the world signal.");
            Assert.That(ResolveWorldSnapshot(default, otherRunning, isLocalOwner: false), Is.EqualTo(otherRunning),
                "Rivals keep observing the public aggregate.");
        }

        [Test]
        public void Coordinator_TryGetTeamPresence_WithOverride_ReturnsExpectedValues()
        {
            GameObject root = new GameObject("TestCoordinatorRoot");
            try
            {
                TeamExtractionCoordinator coordinator = root.AddComponent<TeamExtractionCoordinator>();
                coordinator.TeamPresenceOverrideForTest = teamId => (present: 1, required: 2);

                Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId teamId), Is.True);
                bool result = coordinator.TryGetTeamPresence(teamId, out int present, out int required);
                Assert.That(result, Is.True);
                Assert.That(present, Is.EqualTo(1));
                Assert.That(required, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private void InvokeRefreshSanctuarySectionPresence(bool hasSanctuary, int presentCount, int requiredCount)
        {
            MethodInfo method = typeof(RaidHudPresenter).GetMethod(
                "RefreshSanctuarySectionPresence",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_presenter, new object[] { hasSanctuary, presentCount, requiredCount });
        }

        private void InvokeRefreshRitualStatusSectionPresence(
            bool hasCountdown,
            ExtractionCountdownSnapshot countdown,
            bool hasSanctuary,
            ExtractionRitualSnapshot ritual,
            int presentCount,
            int requiredCount)
        {
            MethodInfo method = typeof(RaidHudPresenter).GetMethod(
                "RefreshRitualStatusSectionPresence",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_presenter, new object[] { hasCountdown, countdown, hasSanctuary, ritual, presentCount, requiredCount });
        }

        private static ExtractionRitualSnapshot ResolveWorldSnapshot(
            ExtractionRitualSnapshot own,
            ExtractionRitualSnapshot aggregate,
            bool isLocalOwner)
        {
            MethodInfo resolver = typeof(ExtractionSanctuaryPresenter).GetMethod(
                "ResolveWorldSnapshot",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resolver, Is.Not.Null);
            return (ExtractionRitualSnapshot)resolver.Invoke(null, new object[] { own, aggregate, isLocalOwner });
        }

        private static void AssertCue(
            string expected,
            bool hasPreviousSnapshot,
            ExtractionRitualState previousWorldState,
            ExtractionRitualState currentWorldState,
            ExtractionRitualState previousOwnState,
            ExtractionRitualState currentOwnState,
            bool previouslyReserved,
            bool currentlyReserved,
            bool isLocalOwner)
        {
            MethodInfo resolver = typeof(ExtractionSanctuaryPresenter).GetMethod(
                "ResolveObservedCues",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resolver, Is.Not.Null);
            object result = resolver.Invoke(null, new object[]
            {
                hasPreviousSnapshot,
                previousWorldState,
                currentWorldState,
                previousOwnState,
                currentOwnState,
                previouslyReserved,
                currentlyReserved,
                isLocalOwner
            });
            Assert.That(result.ToString(), Is.EqualTo(expected));
        }
    }
}
#endif
