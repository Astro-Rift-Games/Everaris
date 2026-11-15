namespace Grimhold.Backend
{
    public enum BackendCommitStatus { None, Saving, Retrying, DeferredUntilLogin, Failed, Saved }

    /// <summary>Per-operation local presentation projection, never a persistence acknowledgement.</summary>
    public sealed class BackendCommitFeedback
    {
        private readonly bool _supportsLoginRecovery;

        public BackendCommitFeedback(bool supportsLoginRecovery = true)
        {
            _supportsLoginRecovery = supportsLoginRecovery;
        }

        public BackendCommitStatus Status { get; private set; }
        public BackendError Error { get; private set; }
        public void Begin()
        {
            Status = BackendCommitStatus.Saving;
            Error = default;
        }

        public void Retrying(BackendError error)
        {
            Status = BackendCommitStatus.Retrying;
            Error = error;
        }

        public void Complete(bool success, BackendError error)
        {
            Error = success ? default : error;
            Status = success ? BackendCommitStatus.Saved
                : _supportsLoginRecovery &&
                  (BackendErrorClassifier.Classify(error.error) == BackendErrorClass.Retryable ||
                   BackendErrorUtility.IsTransportFailure(error.error))
                    ? BackendCommitStatus.DeferredUntilLogin : BackendCommitStatus.Failed;
        }

        public void DeferHydration()
        {
            Status = _supportsLoginRecovery ? BackendCommitStatus.DeferredUntilLogin : BackendCommitStatus.Failed;
            Error = default;
        }
    }
}
