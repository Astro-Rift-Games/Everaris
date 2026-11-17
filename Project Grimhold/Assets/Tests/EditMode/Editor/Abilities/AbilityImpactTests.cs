using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class AbilityImpactTests
{
    private static readonly EntityId Caster = new EntityId(1);
    private static readonly EntityId Target = new EntityId(2);
    private static readonly Vector2 Direction = Vector2.right;
    private static readonly Vector2 HitPoint = new Vector2(1.5f, 0.25f);
    private const int Tick = 42;

    private GameObject _owner;

    [SetUp]
    public void SetUp() => _owner = new GameObject(nameof(AbilityImpactTests));

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_owner);

    // ---- Result overload -----------------------------------------------------------------

    [Test]
    public void TryApplyWithResult_Damage_ReturnsTheResolverResult()
    {
        var expected = new DamageResult(Target, true, 3.5f, 6.5f, false, DamageFailureReason.None);
        var resolver = new RecordingResolver(expected);

        bool applied = AbilityImpact.TryApply(resolver, Caster, Target, null, 5f, DamageType.Physical, 8f, Direction,
            HitPoint, Tick, _owner, out DamageResult result);

        Assert.That(applied, Is.True);
        Assert.That(resolver.Requests, Has.Count.EqualTo(1));
        AssertRequest(resolver.Requests[0], 5f, DamageType.Physical, 8f);
        AssertSameResult(result, expected);
    }

    [Test]
    public void TryApplyWithResult_ZeroDamagePush_StillResolvesAndReturnsTheRejection()
    {
        var expected = new DamageResult(Target, false, 0f, 10f, false, DamageFailureReason.InvalidAmount);
        var resolver = new RecordingResolver(expected);

        bool applied = AbilityImpact.TryApply(resolver, Caster, Target, null, 0f, DamageType.Physical, 8f, Direction,
            HitPoint, Tick, _owner, out DamageResult result);

        Assert.That(applied, Is.True);
        Assert.That(resolver.Requests, Has.Count.EqualTo(1), "Knockback-only impacts keep the resolver policy checks.");
        AssertRequest(resolver.Requests[0], 0f, DamageType.Physical, 8f);
        AssertSameResult(result, expected);
    }

    [Test]
    public void TryApplyWithResult_NoDamageAndNoKnockback_DoesNotResolveAndReportsNothingApplied()
    {
        var resolver = new RecordingResolver(new DamageResult(Target, true, 9f, 1f, false, DamageFailureReason.None));

        bool applied = AbilityImpact.TryApply(resolver, Caster, Target, null, 0f, DamageType.Physical, 0f, Direction,
            HitPoint, Tick, _owner, out DamageResult result);

        Assert.That(applied, Is.True);
        Assert.That(resolver.Requests, Is.Empty);
        AssertSameResult(result, default);
    }

    [Test]
    public void TryApplyWithResult_MissingResolver_FailsLogsAndReportsNothingApplied()
    {
        LogAssert.Expect(LogType.Error, $"GameObject requires an {nameof(IDamageResolver)} on the caster to apply damage.");

        bool applied = AbilityImpact.TryApply(null, Caster, Target, null, 5f, DamageType.Physical, 8f, Direction,
            HitPoint, Tick, _owner, out DamageResult result);

        Assert.That(applied, Is.False);
        AssertSameResult(result, default);
    }

    // ---- Legacy overload (unchanged contract) --------------------------------------------

    [Test]
    public void TryApply_Damage_ForwardsTheSameRequestAndReturnsTrue()
    {
        var resolver = new RecordingResolver(new DamageResult(Target, false, 0f, 10f, false, DamageFailureReason.FriendlyFireBlocked));

        bool applied = AbilityImpact.TryApply(resolver, Caster, Target, null, 5f, DamageType.Magical, 3f, Direction,
            HitPoint, Tick, _owner);

        Assert.That(applied, Is.True, "A resolver rejection is not a TryApply failure.");
        Assert.That(resolver.Requests, Has.Count.EqualTo(1));
        AssertRequest(resolver.Requests[0], 5f, DamageType.Magical, 3f);
    }

    [Test]
    public void TryApply_ZeroDamagePush_StillResolves()
    {
        var resolver = new RecordingResolver(default);

        Assert.That(AbilityImpact.TryApply(resolver, Caster, Target, null, 0f, DamageType.Physical, 8f, Direction,
            HitPoint, Tick, _owner), Is.True);
        Assert.That(resolver.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void TryApply_NoDamageAndNoKnockback_DoesNotResolve()
    {
        var resolver = new RecordingResolver(default);

        Assert.That(AbilityImpact.TryApply(resolver, Caster, Target, null, 0f, DamageType.Physical, 0f, Direction,
            HitPoint, Tick, _owner), Is.True);
        Assert.That(resolver.Requests, Is.Empty);
    }

    [Test]
    public void TryApply_MissingResolver_FailsAndLogs()
    {
        LogAssert.Expect(LogType.Error, $"GameObject requires an {nameof(IDamageResolver)} on the caster to apply damage.");

        Assert.That(AbilityImpact.TryApply(null, Caster, Target, null, 5f, DamageType.Physical, 0f, Direction,
            HitPoint, Tick, _owner), Is.False);
    }

    [Test]
    public void TryApply_MissingResolverWithoutDamageOrKnockback_Succeeds()
    {
        Assert.That(AbilityImpact.TryApply(null, Caster, Target, null, 0f, DamageType.Physical, 0f, Direction,
            HitPoint, Tick, _owner), Is.True);
        LogAssert.NoUnexpectedReceived();
    }

    // ---- Helpers -------------------------------------------------------------------------

    private static void AssertRequest(in DamageRequest request, float amount, DamageType type, float knockback)
    {
        Assert.That(request.AttackerId, Is.EqualTo(Caster));
        Assert.That(request.TargetId, Is.EqualTo(Target));
        Assert.That(request.Amount, Is.EqualTo(amount));
        Assert.That(request.DamageType, Is.EqualTo(type));
        Assert.That(request.KnockbackForce, Is.EqualTo(knockback));
        Assert.That(request.Direction, Is.EqualTo(Direction));
        Assert.That(request.HitPoint, Is.EqualTo(HitPoint));
        Assert.That(request.SimulationTick, Is.EqualTo(Tick));
    }

    private static void AssertSameResult(in DamageResult actual, in DamageResult expected)
    {
        Assert.That(actual.TargetId, Is.EqualTo(expected.TargetId));
        Assert.That(actual.IsApplied, Is.EqualTo(expected.IsApplied));
        Assert.That(actual.AppliedDamage, Is.EqualTo(expected.AppliedDamage));
        Assert.That(actual.RemainingHealth, Is.EqualTo(expected.RemainingHealth));
        Assert.That(actual.IsFatal, Is.EqualTo(expected.IsFatal));
        Assert.That(actual.FailureReason, Is.EqualTo(expected.FailureReason));
    }

    private sealed class RecordingResolver : IDamageResolver
    {
        private readonly DamageResult _result;

        public RecordingResolver(DamageResult result) => _result = result;

        public List<DamageRequest> Requests { get; } = new List<DamageRequest>();

        public DamageResult Resolve(in DamageRequest request)
        {
            Requests.Add(request);
            return _result;
        }
    }
}
