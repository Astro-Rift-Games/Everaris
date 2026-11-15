#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEngine.TestTools;

[Category("BACK06")]
public class RemoteIdempotentRetryPolicyTests
{
    [SetUp]
    public void SetUp()
    {
        RemoteIdempotentRetryPolicy.BackoffMs = 0;
    }

    [TearDown]
    public void TearDown()
    {
        RemoteIdempotentRetryPolicy.BackoffMs = 1000;
        RemoteIdempotentRetryPolicy.DelayAsync = Task.Delay;
    }

    [Test]
    public async Task Retryable_ExhaustionHasThreeAttemptsTwoDelaysAndFeedback()
    {
        var delays = new System.Collections.Generic.List<int>();
        int attempts = 0;
        int notices = 0;
        RemoteIdempotentRetryPolicy.BackoffMs = 1000;
        RemoteIdempotentRetryPolicy.DelayAsync = ms => { delays.Add(ms); return Task.CompletedTask; };
        LogAssert.Expect(UnityEngine.LogType.Error,
            "[RemoteIdempotentRetryPolicy] Max retries reached for error TOO_MANY_REQUESTS. Aborting.");
        var outcome = await RemoteIdempotentRetryPolicy.ExecuteWithRetryAsync(
            () => { attempts++; return Task.FromResult((false, 0, new BackendError { error = "TOO_MANY_REQUESTS", retryAfter = 1.5f })); },
            error => { notices++; Assert.That(error.error, Is.EqualTo("TOO_MANY_REQUESTS")); });
        Assert.That(outcome.success, Is.False);
        Assert.That(outcome.error.retryAfter, Is.EqualTo(1.5f));
        Assert.That(attempts, Is.EqualTo(3));
        Assert.That(delays, Is.EqualTo(new[] { 1500, 2000 }));
        Assert.That(notices, Is.EqualTo(2));
    }

    [Test]
    public async Task ExecuteWithRetry_Success_DirectlyReturnsSuccess()
    {
        int attempts = 0;
        var (success, result, error) = await RemoteIdempotentRetryPolicy.ExecuteWithRetryAsync(
            () => { attempts++; return Task.FromResult((true, 42, default(BackendError))); }
        );

        Assert.That(success, Is.True);
        Assert.That(result, Is.EqualTo(42));
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteWithRetry_RevisionConflict_StopsWithoutRevisionRefresh()
    {
        int attempts = 0;
        var (success, result, error) = await RemoteIdempotentRetryPolicy.ExecuteWithRetryAsync(
            () => { 
                attempts++; 
                if (attempts == 1) return Task.FromResult((false, 0, new BackendError { error = "REVISION_CONFLICT" }));
                return Task.FromResult((true, 42, default(BackendError))); 
            }
        );

        Assert.That(success, Is.False);
        Assert.That(error.error, Is.EqualTo("REVISION_CONFLICT"));
        Assert.That(attempts, Is.EqualTo(1));
    }

    [TestCase("TOO_MANY_REQUESTS")]
    [TestCase("CONCURRENT_MUTATION")]
    [TestCase("EXTRACTION_RESULT_PENDING")]
    [TestCase("NO_AUTHORITATIVE_RESULT")]
    public async Task ExecuteWithRetry_Retryable_DoesNotDropRemainingAttempts(string code)
    {
        int attempts = 0;
        var outcome = await RemoteIdempotentRetryPolicy.ExecuteWithRetryAsync(() =>
        {
            attempts++;
            return Task.FromResult(attempts < 3
                ? (false, 0, new BackendError { error = code })
                : (true, 42, default(BackendError)));
        });
        Assert.That(outcome.success, Is.True);
        Assert.That(outcome.result, Is.EqualTo(42));
        Assert.That(attempts, Is.EqualTo(3));
    }

    [TestCase("RESERVATION_PENDING")]
    [TestCase("RESULT_ALREADY_CONSUMED")]
    [TestCase("RESULT_CONFLICT")]
    public async Task ExecuteWithRetry_NonRetryable_ReturnsOriginalErrorOnce(string code)
    {
        int attempts = 0;
        var outcome = await RemoteIdempotentRetryPolicy.ExecuteWithRetryAsync(() =>
        {
            attempts++;
            return Task.FromResult((false, new BackendError { error = code, message = "unchanged" }));
        });
        Assert.That(outcome.success, Is.False);
        Assert.That(outcome.error.error, Is.EqualTo(code));
        Assert.That(outcome.error.message, Is.EqualTo("unchanged"));
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteWithRetry_TransportFailure_RetriesAndSucceeds()
    {
        int attempts = 0;
        var (success, result, error) = await RemoteIdempotentRetryPolicy.ExecuteWithRetryAsync(
            () => { 
                attempts++; 
                if (attempts == 1) return Task.FromResult((false, 0, new BackendError { error = BackendErrorUtility.NetworkError }));
                return Task.FromResult((true, 42, default(BackendError))); 
            }
        );

        Assert.That(success, Is.True);
        Assert.That(result, Is.EqualTo(42));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task ExecuteWithRetry_ExhaustsRetries_ReturnsFailure()
    {
        int attempts = 0;
        LogAssert.Expect(UnityEngine.LogType.Error,
            "[RemoteIdempotentRetryPolicy] Max retries reached for error TIMEOUT. Aborting.");
        var (success, result, error) = await RemoteIdempotentRetryPolicy.ExecuteWithRetryAsync(
            () => { 
                attempts++; 
                return Task.FromResult((false, 0, new BackendError { error = BackendErrorUtility.Timeout }));
            }
        );

        Assert.That(success, Is.False);
        Assert.That(error.error, Is.EqualTo(BackendErrorUtility.Timeout));
        Assert.That(attempts, Is.EqualTo(RemoteIdempotentRetryPolicy.MaxRetries));
    }

    [Test]
    public async Task ExecuteWithRetry_DomainError_FailsImmediatelyWithoutRetry()
    {
        int attempts = 0;
        var (success, result, error) = await RemoteIdempotentRetryPolicy.ExecuteWithRetryAsync(
            () => { 
                attempts++; 
                return Task.FromResult((false, 0, new BackendError { error = "UNSUPPORTED_EQUIPMENT_LAYOUT" }));
            }
        );

        Assert.That(success, Is.False);
        Assert.That(error.error, Is.EqualTo("UNSUPPORTED_EQUIPMENT_LAYOUT"));
        Assert.That(attempts, Is.EqualTo(1));
    }
}
#endif
