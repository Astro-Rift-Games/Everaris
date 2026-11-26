using NUnit.Framework;

namespace Tests.EditMode.Player
{
    /// <summary>Accelerated Resolution wording (GD 13 section 14: the Downed player knows when it starts).</summary>
    public sealed class DownedAcceleratedResolutionFeedbackTests
    {
        [Test]
        public void DownedStateText_Accelerated_NamesAcceleratedResolution()
        {
            Assert.That(DownedRecoveryFeedbackRules.DownedStateText(RecoveryKind.None, false, false, true),
                Is.EqualTo("ABATIDO · Resolución Acelerada"));
            Assert.That(DownedRecoveryFeedbackRules.DownedStateText(RecoveryKind.None, true, true, true),
                Is.EqualTo("ABATIDO · Resolución Acelerada"));
        }

        [Test]
        public void DownedStateText_RunningSessionStillNamesTheRecoveryForm()
        {
            Assert.That(DownedRecoveryFeedbackRules.DownedStateText(RecoveryKind.Assisted, false, false, true),
                Is.EqualTo("ABATIDO · Reanimando"));
        }

        [Test]
        public void DownedStateText_NotAccelerated_MatchesTheExistingText()
        {
            Assert.That(DownedRecoveryFeedbackRules.DownedStateText(RecoveryKind.None, false, false, false),
                Is.EqualTo(DownedRecoveryFeedbackRules.DownedStateText(RecoveryKind.None, false, false)));
            Assert.That(DownedRecoveryFeedbackRules.DownedStateText(RecoveryKind.None, true, false, false),
                Is.EqualTo("ABATIDO · pulsa [F] para auto-reanimarte"));
        }

        [Test]
        public void AcceleratedResolutionNotice_OnlyOnEntry()
        {
            Assert.That(DownedRecoveryFeedbackRules.AcceleratedResolutionNotice(false, true),
                Is.EqualTo("Sin vía de reanimación: Resolución Acelerada"));
            Assert.That(DownedRecoveryFeedbackRules.AcceleratedResolutionNotice(true, true), Is.Null);
            Assert.That(DownedRecoveryFeedbackRules.AcceleratedResolutionNotice(true, false), Is.Null);
            Assert.That(DownedRecoveryFeedbackRules.AcceleratedResolutionNotice(false, false), Is.Null);
        }
    }
}
