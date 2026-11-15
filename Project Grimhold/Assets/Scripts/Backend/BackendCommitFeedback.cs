namespace Grimhold.Backend
{
    public enum BackendCommitStatus { None, Saving, Retrying, DeferredUntilLogin, Failed, Saved }

    /// <summary>Per-operation local presentation projection, never a persistence acknowledgement.</summary>
    public sealed class BackendCommitFeedback
    {
        private readonly bool _supportsLoginRecovery;
        private readonly bool _retiresMissingDefeatResult;

        public BackendCommitFeedback(bool supportsLoginRecovery = true, bool retiresMissingDefeatResult = false)
        {
            _supportsLoginRecovery = supportsLoginRecovery;
            _retiresMissingDefeatResult = retiresMissingDefeatResult;
        }

        public BackendCommitStatus Status { get; private set; }
        public BackendError Error { get; private set; }
        public string Message { get; private set; } = string.Empty;

        public void Begin() => SetState(BackendCommitStatus.Saving, default);

        public void Retrying(BackendError error) => SetState(BackendCommitStatus.Retrying, error);

        public void Complete(bool success, BackendError error) => SetState(
            success ? BackendCommitStatus.Saved
                : _retiresMissingDefeatResult && error.error == "NO_AUTHORITATIVE_RESULT" ? BackendCommitStatus.Failed
                : _supportsLoginRecovery ? BackendCommitStatus.DeferredUntilLogin : BackendCommitStatus.Failed,
            success ? default : error);

        public void RejectLocalDefeat() => SetState(BackendCommitStatus.Failed,
            new BackendError { error = "LOCAL_DEFEAT_PENDING" });

        public void DeferHydration() => SetState(
            _supportsLoginRecovery ? BackendCommitStatus.DeferredUntilLogin : BackendCommitStatus.Failed, default);

        private void SetState(BackendCommitStatus status, BackendError error)
        {
            Status = status;
            Error = error;
            // Presentation reads this every frame; format only when the operation changes.
            Message = BackendErrorMessages.FormatCommit(this);
        }
    }
}
