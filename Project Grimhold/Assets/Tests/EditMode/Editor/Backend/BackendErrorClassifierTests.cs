#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using Grimhold.Backend;
using NUnit.Framework;

public sealed class BackendErrorClassifierTests
{
    [TestCase("TOO_MANY_REQUESTS", BackendErrorClass.Retryable)]
    [TestCase("CONCURRENT_MUTATION", BackendErrorClass.Retryable)]
    [TestCase("EXTRACTION_RESULT_PENDING", BackendErrorClass.Retryable)]
    [TestCase("NO_AUTHORITATIVE_RESULT", BackendErrorClass.Retryable)]
    [TestCase("REVISION_CONFLICT", BackendErrorClass.Conflict)]
    [TestCase("RESULT_CONFLICT", BackendErrorClass.Conflict)]
    [TestCase("RESULT_RESERVATION_MISMATCH", BackendErrorClass.Conflict)]
    [TestCase("RESERVATION_MISMATCH", BackendErrorClass.Conflict)]
    [TestCase("RESERVATION_RAID_MISMATCH", BackendErrorClass.Conflict)]
    [TestCase("RESULT_ALREADY_CONSUMED", BackendErrorClass.Terminal)]
    [TestCase("LOADOUT_NOT_EMPTY", BackendErrorClass.Terminal)]
    [TestCase("PREPARED_WEAPON_REQUIRED", BackendErrorClass.Terminal)]
    [TestCase("RESERVATION_ADMITTED", BackendErrorClass.Terminal)]
    [TestCase("RESERVATION_PENDING", BackendErrorClass.Terminal)]
    [TestCase("NO_ACTIVE_RESERVATION", BackendErrorClass.Terminal)]
    [TestCase("EXCEEDS_INVENTORY_CAPACITY", BackendErrorClass.Terminal)]
    [TestCase("EXCEEDS_EXPERIENCE_BUDGET", BackendErrorClass.Terminal)]
    [TestCase("HOST_NOT_IN_RAID", BackendErrorClass.Terminal)]
    [TestCase("INVALID_HOST_SIGNATURE", BackendErrorClass.Terminal)]
    [TestCase("UNKNOWN_LOOT_ID", BackendErrorClass.Terminal)]
    [TestCase("INSUFFICIENT_FUNDS", BackendErrorClass.Terminal)]
    [TestCase("INSUFFICIENT_ITEMS", BackendErrorClass.Terminal)]
    [TestCase("INSUFFICIENT_STASH_ITEMS", BackendErrorClass.Terminal)]
    [TestCase("INSUFFICIENT_LOADOUT_ITEMS", BackendErrorClass.Terminal)]
    [TestCase("INSUFFICIENT_BUYBACK_UNITS", BackendErrorClass.Terminal)]
    [TestCase("BUYBACK_ENTRY_NOT_FOUND", BackendErrorClass.Terminal)]
    [TestCase("ITEM_NOT_FOUND", BackendErrorClass.Terminal)]
    [TestCase("INVALID_QUANTITY", BackendErrorClass.Terminal)]
    [TestCase("VALIDATION_FAILED", BackendErrorClass.Terminal)]
    [TestCase("TRANSACTION_RETENTION_EXCEEDED", BackendErrorClass.Terminal)]
    [TestCase("FUTURE_UNKNOWN_CODE", BackendErrorClass.Terminal)]
    [TestCase(null, BackendErrorClass.Terminal)]
    public void ListedCode_HasOwnerApprovedClassification(string code, BackendErrorClass expected) =>
        Assert.That(BackendErrorClassifier.Classify(code), Is.EqualTo(expected));
}
#endif
