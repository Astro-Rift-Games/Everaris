#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

/// <summary>EditMode tests for FIX-16: accurate Spanish feedback for Party invitations and leave failures.</summary>
public sealed class TownPartyFeedbackTests
{
    [Test]
    public void EveryInvitationResult_ProducesDistinctSpanishMessageWithoutEnumNames()
    {
        var messages = new HashSet<string>(StringComparer.Ordinal);
        foreach (TownPartyInvitationResult result in Enum.GetValues(typeof(TownPartyInvitationResult)))
        {
            string message = TownPartyFeedbackText.GetInvitationResultMessage(result);
            Assert.That(message, Is.Not.Null.And.Not.Empty, $"Missing message for {result}");
            StringAssert.DoesNotContain(result.ToString(), message, $"Message for {result} leaked enum name.");
            Assert.That(messages.Add(message), Is.True, $"Duplicate invitation message for {result}: '{message}'");
        }
    }

    [Test]
    public void PendingAndPartyChangeCancellation_AreDistinctFromUnavailableAndBusy()
    {
        string unavailable = TownPartyFeedbackText.GetInvitationResultMessage(TownPartyInvitationResult.Unavailable);
        string busy = TownPartyFeedbackText.GetInvitationResultMessage(TownPartyInvitationResult.Busy);
        string inviterPending = TownPartyFeedbackText.GetInvitationResultMessage(TownPartyInvitationResult.InviterAlreadyHasPending);
        string recipientPending = TownPartyFeedbackText.GetInvitationResultMessage(TownPartyInvitationResult.RecipientHasPending);
        string cancelledByChange = TownPartyFeedbackText.GetInvitationResultMessage(TownPartyInvitationResult.CancelledByPartyChange);

        Assert.That(inviterPending, Is.Not.EqualTo(unavailable));
        Assert.That(recipientPending, Is.Not.EqualTo(unavailable));
        Assert.That(inviterPending, Is.Not.EqualTo(recipientPending));
        Assert.That(cancelledByChange, Is.Not.EqualTo(busy));
        StringAssert.Contains("pendiente", inviterPending);
        StringAssert.Contains("pendiente", recipientPending);
        StringAssert.Contains("cambió", cancelledByChange);
    }

    [Test]
    public void LeaveFailureResults_ProduceDistinctSpanishMessagesWithoutEnumNames()
    {
        Assert.That(TownPartyFeedbackText.TryGetLeaveFailureMessage(TownPartyLeaveResult.Left, out string leftMsg), Is.False);
        Assert.That(leftMsg, Is.Null);

        Assert.That(TownPartyFeedbackText.TryGetLeaveFailureMessage(TownPartyLeaveResult.NotInDuo, out string notInDuoMsg), Is.True);
        Assert.That(TownPartyFeedbackText.TryGetLeaveFailureMessage(TownPartyLeaveResult.Unavailable, out string unavailableMsg), Is.True);
        Assert.That(notInDuoMsg, Is.Not.Empty);
        Assert.That(unavailableMsg, Is.Not.Empty);
        Assert.That(notInDuoMsg, Is.Not.EqualTo(unavailableMsg));
        StringAssert.DoesNotContain(nameof(TownPartyLeaveResult.NotInDuo), notInDuoMsg);
        StringAssert.DoesNotContain(nameof(TownPartyLeaveResult.Unavailable), unavailableMsg);
    }
}
#endif
