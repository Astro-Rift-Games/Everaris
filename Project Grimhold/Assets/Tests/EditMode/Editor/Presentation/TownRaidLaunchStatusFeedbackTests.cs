#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Assert = NUnit.Framework.Assert;

/// <summary>EditMode tests for FIX-13: visible launch confirmation progress, disabled Leave during launch, and connecting status text.</summary>
public sealed class TownRaidLaunchStatusFeedbackTests
{
    private static readonly ProfileId Host = new("host");
    private static readonly ProfileId Ally = new("ally");

    private GameObject _root;
    private TownRaidPreparationView _view;
    private bool _cardFallbackLogExpected;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject(nameof(TownRaidLaunchStatusFeedbackTests));
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
    public void LaunchingState_ShowsConfirmedAndPendingMembersAndDisablesLeaveAndReady()
    {
        TownRaidPreparationPresentation presentation = CreateLaunchingPresentation(hostAck: true, allyAck: false);

        Present(presentation);

        string text = StatusText();
        StringAssert.Contains("Lanzando expedición", text);
        StringAssert.Contains("Confirmados: 1/2", text);
        StringAssert.Contains("Host", text);
        StringAssert.Contains("Esperando confirmación: Ana", text);

        Button leaveButton = _view.transform.Find("RaidCodePanel/Abandonar preparacion").GetComponent<Button>();
        Button readyButton = _view.transform.Find("RaidCodePanel/Ready").GetComponent<Button>();
        Button startButton = _view.transform.Find("RaidCodePanel/Iniciar raid").GetComponent<Button>();
        Assert.That(leaveButton.gameObject.activeSelf, Is.False);
        Assert.That(leaveButton.interactable, Is.False);
        Assert.That(readyButton.interactable, Is.False);
        Assert.That(startButton.interactable, Is.False);

        // SetBusy(false) must never re-enable Leave or Ready while still Launching.
        _view.SetBusy(false);
        Assert.That(leaveButton.interactable, Is.False);
        Assert.That(readyButton.interactable, Is.False);
    }

    [Test]
    public void LaunchingState_WhenAllConfirmed_ShowsConnectingToRaidText()
    {
        TownRaidPreparationPresentation presentation = CreateLaunchingPresentation(hostAck: true, allyAck: true);

        Present(presentation);

        string text = StatusText();
        StringAssert.Contains("Todos confirmados (2/2)", text);
        StringAssert.Contains("Conectando a la incursión", text);
    }

    [TestCase(SessionConnectionState.ConnectingTown, "Conectando a Town")]
    [TestCase(SessionConnectionState.ConnectingRaid, "Conectando a la incursión")]
    [TestCase(SessionConnectionState.ReturningTown, "Volviendo a Town")]
    public void TransitionFadePolicy_ProvidesSpanishStatusTextWithoutEnumNames(
        SessionConnectionState state,
        string expectedBase)
    {
        string status = SceneTransitionFadePolicy.GetStatusText(state);
        Assert.That(status, Is.EqualTo(expectedBase));
        StringAssert.DoesNotContain(state.ToString(), status);

        string animated0 = SceneTransitionFadePolicy.FormatAnimatedStatus(state, 0f);
        string animated1 = SceneTransitionFadePolicy.FormatAnimatedStatus(state, 0.5f);
        string animated2 = SceneTransitionFadePolicy.FormatAnimatedStatus(state, 1.0f);
        Assert.That(animated0, Is.EqualTo(expectedBase + "."));
        Assert.That(animated1, Is.EqualTo(expectedBase + ".."));
        Assert.That(animated2, Is.EqualTo(expectedBase + "..."));
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

    private static TownRaidPreparationPresentation CreateLaunchingPresentation(bool hostAck, bool allyAck)
    {
        RaidCode.TryParse("100001", out RaidCode code);
        var snapshot = new TownRaidPreparationSnapshot(
            code,
            Host,
            TownRaidPreparationState.Launching,
            new[]
            {
                new TownRaidPreparationMember(Host, true, launchRevision: 1, launchAcknowledged: hostAck, teamOrdinal: 1),
                new TownRaidPreparationMember(Ally, true, launchRevision: 1, launchAcknowledged: allyAck, teamOrdinal: 1)
            },
            snapshotRevision: 3,
            launchRevision: 1,
            frozenMemberCount: 2);
        Assert.That(
            TownRaidPreparationPresentation.TryCreate(snapshot, Host, ResolveName, out TownRaidPreparationPresentation presentation),
            Is.True);
        return presentation;
    }

    private static bool ResolveName(ProfileId id, out string name)
    {
        if (id == Host) { name = "Host"; return true; }
        if (id == Ally) { name = "Ana"; return true; }
        name = null;
        return false;
    }
}
#endif
