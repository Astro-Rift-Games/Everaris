#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Presentation
{
    /// <summary>EditMode tests for FIX-15: team extraction blocking and cancellation reasons in the main Raid HUD.</summary>
    public sealed class TeamExtractionFeedbackHudTests
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
        public void FeedbackText_FormatsBlockedAndCancelledMessagesInSpanishWithoutEnumNames()
        {
            string outsideNamed = TeamExtractionFeedbackText.FormatBlockedMessage(
                TeamExtractionCancelReason.RequiredMemberOutsideArea, "Ana", 1, 2);
            Assert.That(outsideNamed, Is.EqualTo("Esperando a Ana en el área (1/2)"));

            string outsideGeneric = TeamExtractionFeedbackText.FormatBlockedMessage(
                TeamExtractionCancelReason.RequiredMemberOutsideArea, null, 1, 2);
            Assert.That(outsideGeneric, Is.EqualTo("Esperando a tu compañero en el área (1/2)"));

            string disconnectedNamed = TeamExtractionFeedbackText.FormatBlockedMessage(
                TeamExtractionCancelReason.RequiredMemberDisconnected, "Luis", 1, 2);
            Assert.That(disconnectedNamed, Is.EqualTo("Extracción bloqueada: Luis está desconectado"));

            foreach (TeamExtractionCancelReason reason in Enum.GetValues(typeof(TeamExtractionCancelReason)))
            {
                string cancelled = TeamExtractionFeedbackText.FormatCancelledMessage(reason, "Ana");
                Assert.That(cancelled, Is.Not.Null.And.Not.Empty);
                StringAssert.DoesNotContain(reason.ToString(), cancelled);
            }

            Assert.That(
                TeamExtractionFeedbackText.FormatCancelledMessage(TeamExtractionCancelReason.RequiredMemberOutsideArea, "Ana"),
                Is.EqualTo("Extracción cancelada: Ana salió del área"));
            Assert.That(
                TeamExtractionFeedbackText.FormatCancelledMessage(TeamExtractionCancelReason.RequiredMemberDisconnected, "Luis"),
                Is.EqualTo("Extracción cancelada: Luis se desconectó"));
            Assert.That(
                TeamExtractionFeedbackText.FormatCancelledMessage(TeamExtractionCancelReason.CompositionChanged, null),
                Is.EqualTo("Extracción cancelada: cambió la composición del equipo"));
            Assert.That(
                TeamExtractionFeedbackText.FormatCancelledMessage(TeamExtractionCancelReason.None, null),
                Is.EqualTo("Extracción: cancelada"));
        }

        [Test]
        public void Presenter_ShowsWaitingForTeammate_WhenRitualCompletedAndTeammateOutsideArea()
        {
            var ritual = new ExtractionRitualSnapshot(ExtractionRitualState.Completed, 10f, 0f, 1f);
            var teamStatus = new TeamExtractionStatusSnapshot(
                presentCount: 1,
                requiredCount: 2,
                blockReason: TeamExtractionCancelReason.RequiredMemberOutsideArea,
                blockingMemberName: "Ana");

            InvokeRefreshRitualStatusSection(false, default, true, ritual, 1, 2, teamStatus);

            Assert.That(_view.ExtractionText.text, Is.EqualTo("Esperando a Ana en el área (1/2)"));
        }

        [Test]
        public void Presenter_ShowsDisconnectedBlock_WhenTeammateDisconnected()
        {
            var ritual = new ExtractionRitualSnapshot(ExtractionRitualState.Completed, 10f, 0f, 1f);
            var teamStatus = new TeamExtractionStatusSnapshot(
                presentCount: 1,
                requiredCount: 2,
                blockReason: TeamExtractionCancelReason.RequiredMemberDisconnected,
                blockingMemberName: "Luis");

            InvokeRefreshRitualStatusSection(false, default, true, ritual, 1, 2, teamStatus);

            Assert.That(_view.ExtractionText.text, Is.EqualTo("Extracción bloqueada: Luis está desconectado"));
        }

        [Test]
        public void Presenter_ShowsSpecificCancellationReason_WhenCountdownCancels()
        {
            var ritual = new ExtractionRitualSnapshot(ExtractionRitualState.Completed, 10f, 0f, 1f);
            var activeCountdown = new ExtractionCountdownSnapshot(
                ExtractionState.InProgress, new EntityId(10), 3.0f, 5f, 0.4f);
            var activeStatus = new TeamExtractionStatusSnapshot(2, 2);

            InvokeRefreshRitualStatusSection(true, activeCountdown, true, ritual, 2, 2, activeStatus);
            StringAssert.Contains("(2/2 en el área)", _view.ExtractionText.text);

            var cancelledStatus = new TeamExtractionStatusSnapshot(
                presentCount: 1,
                requiredCount: 2,
                blockReason: TeamExtractionCancelReason.RequiredMemberOutsideArea,
                blockingMemberName: "Ana",
                lastCancelReason: TeamExtractionCancelReason.RequiredMemberOutsideArea,
                lastCancelMemberName: "Ana");

            InvokeRefreshRitualStatusSection(true, ExtractionCountdownSnapshot.None(), true, ritual, 1, 2, cancelledStatus);
            Assert.That(_view.ExtractionText.text, Is.EqualTo("Extracción cancelada: Ana salió del área"));
        }

        [Test]
        public void Coordinator_TryGetTeamExtractionStatus_WithOverride_ReturnsExpectedSnapshot()
        {
            GameObject root = new GameObject("TestCoordinatorStatusRoot");
            try
            {
                TeamExtractionCoordinator coordinator = root.AddComponent<TeamExtractionCoordinator>();
                coordinator.TeamExtractionStatusOverrideForTest = _ => new TeamExtractionStatusSnapshot(
                    1, 2,
                    TeamExtractionCancelReason.RequiredMemberOutsideArea, "Ana",
                    TeamExtractionCancelReason.RequiredMemberDisconnected, "Luis");

                Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId teamId), Is.True);
                Assert.That(coordinator.TryGetTeamExtractionStatus(teamId, out TeamExtractionStatusSnapshot snapshot), Is.True);
                Assert.That(snapshot.PresentCount, Is.EqualTo(1));
                Assert.That(snapshot.RequiredCount, Is.EqualTo(2));
                Assert.That(snapshot.BlockReason, Is.EqualTo(TeamExtractionCancelReason.RequiredMemberOutsideArea));
                Assert.That(snapshot.BlockingMemberName, Is.EqualTo("Ana"));
                Assert.That(snapshot.LastCancelReason, Is.EqualTo(TeamExtractionCancelReason.RequiredMemberDisconnected));
                Assert.That(snapshot.LastCancelMemberName, Is.EqualTo("Luis"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private void InvokeRefreshRitualStatusSection(
            bool hasCountdown,
            ExtractionCountdownSnapshot countdown,
            bool hasSanctuary,
            ExtractionRitualSnapshot ritual,
            int presentCount,
            int requiredCount,
            TeamExtractionStatusSnapshot teamStatus)
        {
            MethodInfo method = typeof(RaidHudPresenter).GetMethod(
                "RefreshRitualStatusSectionWithTeamStatus",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[]
                {
                    typeof(bool),
                    typeof(ExtractionCountdownSnapshot),
                    typeof(bool),
                    typeof(ExtractionRitualSnapshot),
                    typeof(int),
                    typeof(int),
                    typeof(TeamExtractionStatusSnapshot).MakeByRefType()
                },
                modifiers: null);
            Assert.That(method, Is.Not.Null);
            method.Invoke(_presenter, new object[] { hasCountdown, countdown, hasSanctuary, ritual, presentCount, requiredCount, teamStatus });
        }
    }
}
#endif
