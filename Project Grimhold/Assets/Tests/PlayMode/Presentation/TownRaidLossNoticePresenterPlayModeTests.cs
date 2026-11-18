#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.Presentation
{
    public sealed class TownRaidLossNoticePresenterPlayModeTests
    {
        private const string ViewPrefabPath = "Assets/Prefabs/UI/TownRaidLossNotice.prefab";

        private GameObject _host;
        private GameObject _viewObject;
        private GameObject _readerObject;
        private TownRaidLossNoticePresenter _presenter;
        private TownRaidLossNoticeView _view;
        private PlayerInputReader _reader;

        [SetUp]
        public void SetUp()
        {
            var viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPrefabPath);
            Assert.That(viewPrefab, Is.Not.Null, "TownRaidLossNotice prefab must exist.");
            _viewObject = Object.Instantiate(viewPrefab);
            _view = _viewObject.GetComponentInChildren<TownRaidLossNoticeView>(true);
            Assert.That(_view, Is.Not.Null);

            _readerObject = new GameObject("TownRaidLossNoticeInputReader");
            _reader = _readerObject.AddComponent<PlayerInputReader>();

            _host = new GameObject("TownRaidLossNoticePresenterTests");
            _host.SetActive(false);
            _presenter = _host.AddComponent<TownRaidLossNoticePresenter>();
            SetField(_presenter, "_view", _view);
            _presenter.BindInputReader(_reader);
            _host.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_viewObject);
            Object.DestroyImmediate(_readerObject);
        }

        [Test]
        public void Show_PresentsTheNoticeAndHoldsGameplayInputSuppression()
        {
            _presenter.Show(Notice());

            Assert.That(_view.IsOpen, Is.True);
            Assert.That(_reader.IsGameplayInputSuppressed, Is.True);
        }

        [Test]
        public void Continue_ClosesTheNoticeAndReleasesSuppression()
        {
            _presenter.Show(Notice());

            ClickContinue();

            Assert.That(_view.IsOpen, Is.False);
            Assert.That(_reader.IsGameplayInputSuppressed, Is.False);
        }

        [Test]
        public void ShowTwiceWithoutClosing_HoldsASingleToken()
        {
            _presenter.Show(Notice());
            _presenter.Show(Notice());

            ClickContinue();

            Assert.That(_reader.IsGameplayInputSuppressed, Is.False,
                "A second Show must not stack a second token that Continue never releases.");
        }

        [Test]
        public void ShowAndContinueRepeatedly_DoesNotLeakSuppression()
        {
            for (int cycle = 0; cycle < 3; cycle++)
            {
                _presenter.Show(Notice());
                Assert.That(_reader.IsGameplayInputSuppressed, Is.True, $"cycle {cycle}");

                ClickContinue();
                Assert.That(_reader.IsGameplayInputSuppressed, Is.False, $"cycle {cycle}");
            }
        }

        [Test]
        public void ContinueWithoutAnOpenNotice_LeavesOtherOwnersSuppressionUntouched()
        {
            using System.IDisposable other = _reader.AcquireGameplayInputSuppression();

            ClickContinue();

            Assert.That(_reader.IsGameplayInputSuppressed, Is.True);
        }

        [Test]
        public void Show_StacksWithAnotherOwnerAndReleasesOnlyItsOwnToken()
        {
            using System.IDisposable other = _reader.AcquireGameplayInputSuppression();

            _presenter.Show(Notice());
            ClickContinue();

            Assert.That(_reader.IsGameplayInputSuppressed, Is.True);
        }

        [Test]
        public void DisablingThePresenterWhileOpen_ReleasesSuppression_AndEnablingReacquiresIt()
        {
            _presenter.Show(Notice());

            _host.SetActive(false);
            Assert.That(_reader.IsGameplayInputSuppressed, Is.False);
            Assert.That(_view.IsOpen, Is.True, "Disabling the presenter must not discard the pending notice.");

            _host.SetActive(true);
            Assert.That(_reader.IsGameplayInputSuppressed, Is.True);

            ClickContinue();
            Assert.That(_reader.IsGameplayInputSuppressed, Is.False);
        }

        [Test]
        public void DestroyingThePresenterWhileOpen_ReleasesSuppression()
        {
            _presenter.Show(Notice());

            Object.DestroyImmediate(_host);

            Assert.That(_reader.IsGameplayInputSuppressed, Is.False);
        }

        [Test]
        public void Show_WithoutAReader_StillPresentsTheNoticeAndWarns()
        {
            _presenter.BindInputReader(null);
            Object.DestroyImmediate(_readerObject);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("PlayerInputReader"));

            _presenter.Show(Notice());

            Assert.That(_view.IsOpen, Is.True);
        }

        private void ClickContinue()
        {
            var button = (Button)typeof(TownRaidLossNoticeView)
                .GetField("_continueButton", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_view);
            button.onClick.Invoke();
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static RaidLossNotice Notice()
        {
            var reservation = new PendingLoadoutReservation(
                "reservation-1",
                new[] { new StashItem(new LootId("bone"), 3, 0) },
                new PreparedEquipmentLoadout(new LootId("sword"), default));
            Assert.That(
                RaidLossNotice.TryCreate(RaidLossNoticeReason.ConnectionLost, reservation, out RaidLossNotice notice, out string error),
                Is.True,
                error);
            return notice;
        }
    }
}
#endif
