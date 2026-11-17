using NUnit.Framework;

public sealed class ChargeStunRulesTests
{
    private const float StunSeconds = 1f;
    private static readonly EntityId Target = new EntityId(7);

    private static DamageResult Applied(float damage, bool isFatal = false) =>
        new DamageResult(Target, true, damage, isFatal ? 0f : 10f, isFatal, DamageFailureReason.None);

    private static DamageResult Rejected(DamageFailureReason reason) =>
        new DamageResult(Target, false, 0f, 10f, false, reason);

    // ---- Effective damage ----------------------------------------------------------------

    [Test]
    public void IsEffectiveDamage_AppliedPositiveDamage_IsTrue()
    {
        Assert.That(ChargeStunRules.IsEffectiveDamage(Applied(5f)), Is.True);
    }

    [Test]
    public void IsEffectiveDamage_AppliedButMitigatedToZero_IsFalse()
    {
        Assert.That(ChargeStunRules.IsEffectiveDamage(Applied(0f)), Is.False);
    }

    [TestCase(float.NaN)]
    [TestCase(-1f)]
    public void IsEffectiveDamage_AppliedWithNonPositiveAmount_IsFalse(float damage)
    {
        Assert.That(ChargeStunRules.IsEffectiveDamage(Applied(damage)), Is.False);
    }

    [Test]
    public void IsEffectiveDamage_DefaultResult_IsFalse()
    {
        // The resolver was never invoked (no damage and no knockback): nothing was applied.
        Assert.That(ChargeStunRules.IsEffectiveDamage(default), Is.False);
    }

    [Test]
    public void IsEffectiveDamage_NotAppliedEvenWithAReportedAmount_IsFalse()
    {
        var inconsistent = new DamageResult(Target, false, 5f, 10f, false, DamageFailureReason.RejectedByTarget);
        Assert.That(ChargeStunRules.IsEffectiveDamage(inconsistent), Is.False);
    }

    // ---- Stun decision -------------------------------------------------------------------

    [Test]
    public void ShouldApplyStun_EffectiveDamageOnSurvivingTarget_IsTrue()
    {
        Assert.That(ChargeStunRules.ShouldApplyStun(StunSeconds, Applied(5f), targetIsAlive: true), Is.True);
    }

    [Test]
    public void ShouldApplyStun_DamageMitigatedToZero_IsFalse()
    {
        Assert.That(ChargeStunRules.ShouldApplyStun(StunSeconds, Applied(0f), targetIsAlive: true), Is.False);
    }

    [TestCase(DamageFailureReason.InvalidAmount)] // Push-only impact: zero base damage with knockback.
    [TestCase(DamageFailureReason.FriendlyFireBlocked)]
    [TestCase(DamageFailureReason.TargetUnavailable)] // Immune target (CanReceiveDamage is false).
    [TestCase(DamageFailureReason.RejectedByTarget)]
    [TestCase(DamageFailureReason.InvalidTarget)]
    [TestCase(DamageFailureReason.TargetDead)]
    [TestCase(DamageFailureReason.SelfDamageRejected)]
    [TestCase(DamageFailureReason.MissingAuthority)]
    public void ShouldApplyStun_DamageRejectedByTheResolver_IsFalse(DamageFailureReason reason)
    {
        Assert.That(ChargeStunRules.ShouldApplyStun(StunSeconds, Rejected(reason), targetIsAlive: true), Is.False);
    }

    [Test]
    public void ShouldApplyStun_ResolverNeverInvoked_IsFalse()
    {
        Assert.That(ChargeStunRules.ShouldApplyStun(StunSeconds, default, targetIsAlive: true), Is.False);
    }

    [Test]
    public void ShouldApplyStun_TargetDiedFromTheImpact_IsFalse()
    {
        Assert.That(ChargeStunRules.ShouldApplyStun(StunSeconds, Applied(5f, isFatal: true), targetIsAlive: false), Is.False);
    }

    [Test]
    public void ShouldApplyStun_TargetNoLongerAlive_IsFalse()
    {
        Assert.That(ChargeStunRules.ShouldApplyStun(StunSeconds, Applied(5f), targetIsAlive: false), Is.False);
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void ShouldApplyStun_DisabledOrInvalidDuration_IsFalse(float stunSeconds)
    {
        Assert.That(ChargeStunRules.ShouldApplyStun(stunSeconds, Applied(5f), targetIsAlive: true), Is.False);
    }
}
