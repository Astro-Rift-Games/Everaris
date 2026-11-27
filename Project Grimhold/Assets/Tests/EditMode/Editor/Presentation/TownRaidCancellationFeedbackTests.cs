#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;

/// <summary>EditMode tests for FIX-12: distinct Spanish cancellation notices across dissolution and launch abort.</summary>
public sealed class TownRaidCancellationFeedbackTests
{
    private static readonly ProfileId Host = new("host");
    private static readonly ProfileId Client = new("client");

    private GameObject _root;
    private TownRaidPreparationView _view;
    private bool _cardFallbackLogExpected;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject(nameof(TownRaidCancellationFeedbackTests));
        _view = TownRaidPreparationView.CreateEditorSource(_root.transform);
        _cardFallbackLogExpected = false;
        _view.Open();
    }

    [TearDown]
    public void TearDown()
    {
        if (_root != null)
        {
            UnityEngine.Object.DestroyImmediate(_root);
        }
    }

    [Test]
    public void EveryCancelReason_ProducesDistinctSpanishMessageWithoutEnumNames()
    {
        var messages = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownRaidPreparationCancelReason reason in Enum.GetValues(typeof(TownRaidPreparationCancelReason)))
        {
            bool hasMessage = TownRaidCancellationText.TryGetMessage(reason, out string message);
            if (reason == TownRaidPreparationCancelReason.None)
            {
                Assert.That(hasMessage, Is.False);
                Assert.That(message, Is.Null);
                continue;
            }

            Assert.That(hasMessage, Is.True, $"Expected cancellation message for {reason}");
            Assert.That(message, Is.Not.Null.And.Not.Empty);
            StringAssert.DoesNotContain(reason.ToString(), message, $"Message for {reason} leaked enum name.");
            Assert.That(messages.Add(message), Is.True, $"Duplicate message for {reason}: '{message}'");
        }
    }

    [TestCase(TownRaidPreparationCancelReason.HostLeft)]
    [TestCase(TownRaidPreparationCancelReason.HostDisconnected)]
    [TestCase(TownRaidPreparationCancelReason.AcknowledgeTimeout)]
    [TestCase(TownRaidPreparationCancelReason.ReleaseTimeout)]
    [TestCase(TownRaidPreparationCancelReason.MemberDisconnected)]
    [TestCase(TownRaidPreparationCancelReason.MemberRejectedLaunch)]
    [TestCase(TownRaidPreparationCancelReason.ReleaseFailed)]
    public void CancellationNotice_SurvivesDissolutionRepaintAndClearsOnReopenOrNewPreparation(
        TownRaidPreparationCancelReason reason)
    {
        Assert.That(TownRaidCancellationText.TryGetMessage(reason, out string expected), Is.True);

        _view.ShowCancellationNotice(reason);
        StringAssert.Contains(expected, StatusText());

        // Dissolution repaints NoPreparation a moment later; affected members must still see why it dissolved.
        _view.PresentNoPreparation();
        StringAssert.Contains(expected, StatusText());

        // Entering a new preparation clears the previous cancellation notice.
        PresentPreparation();
        StringAssert.DoesNotContain(expected, StatusText());

        // Reopening also clears it.
        _view.ShowCancellationNotice(reason);
        _view.Open();
        StringAssert.DoesNotContain(expected, StatusText());
    }

    [Test]
    public void LocalSpecificRejection_IsNotOverwrittenByGenericMemberRejectedLaunch()
    {
        _view.ShowPreparationRejected(ExpeditionPreparationResult.PreparedWeaponRequired);
        _view.ShowCancellationNotice(TownRaidPreparationCancelReason.MemberRejectedLaunch);

        StringAssert.Contains("Necesitás un arma equipada", StatusText());
        TownRaidCancellationText.TryGetMessage(TownRaidPreparationCancelReason.MemberRejectedLaunch, out string generic);
        StringAssert.DoesNotContain(generic, StatusText());
    }

    private void PresentPreparation()
    {
        if (!_cardFallbackLogExpected)
        {
            LogAssert.Expect(LogType.Error, "TownRaidPreparationView requires member card prefab and container references; using text roster fallback.");
            _cardFallbackLogExpected = true;
        }

        RaidCode.TryParse("100001", out RaidCode code);
        var snapshot = new TownRaidPreparationSnapshot(
            code,
            Host,
            TownRaidPreparationState.Waiting,
            new[]
            {
                new TownRaidPreparationMember(Host, true),
                new TownRaidPreparationMember(Client, true)
            },
            1);
        Assert.That(
            TownRaidPreparationPresentation.TryCreate(snapshot, Client, out TownRaidPreparationPresentation presentation),
            Is.True);
        _view.PresentPreparation(presentation);
    }

    private string StatusText() =>
        _view.transform.Find("RaidCodePanel/Status").GetComponent<TMP_Text>().text;
}
#endif
