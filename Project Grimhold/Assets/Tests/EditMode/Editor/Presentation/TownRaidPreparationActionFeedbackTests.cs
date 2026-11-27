#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Assert = NUnit.Framework.Assert;

/// <summary>EditMode tests for FIX-11: visible Spanish feedback for Create, Join, Ready and Start rejections.</summary>
public sealed class TownRaidPreparationActionFeedbackTests
{
    private static readonly ProfileId Host = new("host");
    private static readonly ProfileId AllyA = new("ally_a");
    private static readonly ProfileId AllyB = new("ally_b");

    private GameObject _root;
    private TownRaidPreparationView _view;
    private bool _cardFallbackLogExpected;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject(nameof(TownRaidPreparationActionFeedbackTests));
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
    public void EveryRejectedActionResult_HasNonEmptySpanishMessageWithoutEnumNames()
    {
        foreach (TownRaidPreparationActionResult result in Enum.GetValues(typeof(TownRaidPreparationActionResult)))
        {
            bool hasMessage = TownRaidPreparationActionText.TryGetMessage(result, out string message);
            if (result == TownRaidPreparationActionResult.Succeeded)
            {
                Assert.That(hasMessage, Is.False);
                Assert.That(message, Is.Null);
                continue;
            }

            Assert.That(hasMessage, Is.True, $"Expected message for {result}");
            Assert.That(message, Is.Not.Null.And.Not.Empty);
            StringAssert.DoesNotContain(result.ToString(), message, $"Message for {result} leaked enum name.");
        }
    }

    [TestCase(TownRaidPreparationActionResult.CodeNotFound)]
    [TestCase(TownRaidPreparationActionResult.PreparationFull)]
    [TestCase(TownRaidPreparationActionResult.PreparationLaunching)]
    [TestCase(TownRaidPreparationActionResult.AlreadyInPreparation)]
    [TestCase(TownRaidPreparationActionResult.PartyUnavailable)]
    [TestCase(TownRaidPreparationActionResult.DirectoryUnavailable)]
    [TestCase(TownRaidPreparationActionResult.NotAllMembersReady)]
    public void ShowActionRejected_DisplaysSpecificReasonInView(TownRaidPreparationActionResult result)
    {
        _view.ShowActionRejected(result);

        Assert.That(TownRaidPreparationActionText.TryGetMessage(result, out string expected), Is.True);
        StringAssert.Contains(expected, StatusText());
    }

    [Test]
    public void DisabledStartButton_NamesUnreadyMembersForHost()
    {
        RaidCode.TryParse("100001", out RaidCode code);
        var snapshot = new TownRaidPreparationSnapshot(
            code,
            Host,
            TownRaidPreparationState.Waiting,
            new[]
            {
                new TownRaidPreparationMember(Host, true),
                new TownRaidPreparationMember(AllyA, false),
                new TownRaidPreparationMember(AllyB, false)
            },
            1);
        Assert.That(
            TownRaidPreparationPresentation.TryCreate(snapshot, Host, ResolveName, out TownRaidPreparationPresentation presentation),
            Is.True);

        Present(presentation);

        Button startButton = _view.transform.Find("RaidCodePanel/Iniciar raid").GetComponent<Button>();
        Assert.That(startButton.interactable, Is.False);
        StringAssert.Contains("Faltan marcar Listo: Ana, Luis.", StatusText());
    }

    [Test]
    public void DisabledStartButton_SingleUnreadyMember_UsesSingularSpanishHint()
    {
        RaidCode.TryParse("100001", out RaidCode code);
        var snapshot = new TownRaidPreparationSnapshot(
            code,
            Host,
            TownRaidPreparationState.Waiting,
            new[]
            {
                new TownRaidPreparationMember(Host, true),
                new TownRaidPreparationMember(AllyA, false)
            },
            1);
        Assert.That(
            TownRaidPreparationPresentation.TryCreate(snapshot, Host, ResolveName, out TownRaidPreparationPresentation presentation),
            Is.True);

        Present(presentation);

        StringAssert.Contains("Falta marcar Listo: Ana.", StatusText());
    }

    [Test]
    public void UnreadyHint_IsOmittedWhenAllMembersAreReadyOrViewerIsClient()
    {
        RaidCode.TryParse("100001", out RaidCode code);
        var waitingNotReady = new TownRaidPreparationSnapshot(
            code,
            Host,
            TownRaidPreparationState.Waiting,
            new[]
            {
                new TownRaidPreparationMember(Host, true),
                new TownRaidPreparationMember(AllyA, false)
            },
            1);
        Assert.That(
            TownRaidPreparationPresentation.TryCreate(waitingNotReady, AllyA, ResolveName, out TownRaidPreparationPresentation clientPresentation),
            Is.True);
        Assert.That(TownRaidPreparationActionText.FormatUnreadyMembersHint(clientPresentation), Is.Empty);

        var allReady = new TownRaidPreparationSnapshot(
            code,
            Host,
            TownRaidPreparationState.Waiting,
            new[]
            {
                new TownRaidPreparationMember(Host, true),
                new TownRaidPreparationMember(AllyA, true)
            },
            2);
        Assert.That(
            TownRaidPreparationPresentation.TryCreate(allReady, Host, ResolveName, out TownRaidPreparationPresentation hostReadyPresentation),
            Is.True);
        Assert.That(TownRaidPreparationActionText.FormatUnreadyMembersHint(hostReadyPresentation), Is.Empty);
    }

    private void Present(in TownRaidPreparationPresentation presentation)
    {
        if (!_cardFallbackLogExpected)
        {
            LogAssert.Expect(LogType.Error, "TownRaidPreparationView requires member card prefab and container references; using text roster fallback.");
            _cardFallbackLogExpected = true;
        }

        _view.PresentPreparation(presentation);
    }

    private string StatusText() =>
        _view.transform.Find("RaidCodePanel/Status").GetComponent<TMP_Text>().text;

    private static bool ResolveName(ProfileId id, out string name)
    {
        if (id == Host) { name = "Host"; return true; }
        if (id == AllyA) { name = "Ana"; return true; }
        if (id == AllyB) { name = "Luis"; return true; }
        name = null;
        return false;
    }
}
#endif
