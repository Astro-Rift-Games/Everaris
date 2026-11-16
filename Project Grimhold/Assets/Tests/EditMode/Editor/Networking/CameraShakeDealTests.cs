using NUnit.Framework;
using UnityEngine;

public sealed class CameraShakeDealTests
{
    private CameraShakeConfig _config;
    private WeaponDefinition _weapon;
    private GameObject _go;
    private CameraShakeController _controller;
    private float _scale;

    [SetUp]
    public void SetUp()
    {
        _scale = CameraShakeController.AccessibilityScale;
        CameraShakeController.AccessibilityScale = 1f;
        _config = ScriptableObject.CreateInstance<CameraShakeConfig>();
        _weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
        _go = new GameObject("DealShakeTest");
        _controller = _go.AddComponent<CameraShakeController>();
        _controller.Configure(_config);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_go);
        Object.DestroyImmediate(_config);
        Object.DestroyImmediate(_weapon);
        CameraShakeController.AccessibilityScale = _scale;
    }

    private CombatShakeWeights Resolve(bool tagged = true, bool fatal = false, bool character = true,
        Vector2? direction = null, bool unresolved = false)
    {
        var feedback = new CombatPresentationEvent(1, CombatFeedbackKind.ConfirmedImpact, default,
            Vector2.zero, 10f, 1, default,
            tagged ? new WeaponAttackTag(EquipmentSlot.WeaponSetAMainHand, 1, 1, AttackType.Ranged) : default,
            direction ?? new Vector2(3f, 4f), fatal);
        return CombatShakeWeights.Resolve(feedback, unresolved ? null : _weapon, _config, character);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnsetOrUntagged_UsesConfig(bool tagged)
    {
        var result = Resolve(tagged);
        Assert.That(result.Intensity, Is.EqualTo(_config.DealDamageIntensity));
        Assert.That(result.Duration, Is.EqualTo(_config.DealDamageDuration));
        Assert.That(result.Kick, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void WeaponOverride_UsedOnlyForResolvedTaggedWeapon()
    {
        JsonUtility.FromJsonOverwrite("{\"_presentation\":{\"_dealShake\":{\"_enabled\":true,\"_intensity\":0.7,\"_duration\":0.3}}}", _weapon);
        var result = Resolve();
        Assert.That(result.Intensity, Is.EqualTo(.7f));
        Assert.That(result.Duration, Is.EqualTo(.3f));
        Assert.That(Resolve(false).Intensity, Is.EqualTo(_config.DealDamageIntensity));
        Assert.That(Resolve(false).Duration, Is.EqualTo(_config.DealDamageDuration));
        Assert.That(Resolve(unresolved: true).Intensity, Is.EqualTo(_config.DealDamageIntensity));
        Assert.That(Resolve(unresolved: true).Duration, Is.EqualTo(_config.DealDamageDuration));
    }

    [TestCase(false, 1f)]
    [TestCase(true, 1.5f)]
    public void KillMultiplier_OnlyFatal(bool fatal, float multiplier)
    {
        Assert.That(Resolve(fatal: fatal).Intensity,
            Is.EqualTo(_config.DealDamageIntensity * multiplier).Within(.00001f));
        Assert.That(Resolve(fatal: fatal).Duration, Is.EqualTo(_config.DealDamageDuration));
    }

    [Test]
    public void DirectionalKick_NormalizesDirection_AndZeroStaysZero()
    {
        JsonUtility.FromJsonOverwrite("{\"_directionalKickStrength\":0.1}", _config);
        Assert.That(Vector2.Distance(Resolve().Kick, new Vector2(.06f, .08f)), Is.LessThan(.00001f));
        Assert.That(Resolve(direction: Vector2.zero).Kick, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void NonCharacter_ProducesNoShake()
    {
        var result = Resolve(character: false);
        Assert.That(result.Intensity, Is.Zero);
        Assert.That(result.Duration, Is.Zero);
        Assert.That(result.Kick, Is.EqualTo(Vector2.zero));
    }

    [TestCase(0f)]
    [TestCase(.5f)]
    [TestCase(1f)]
    public void Kick_RespectsAccessibilityClampAndDecay(float scale)
    {
        CameraShakeController.AccessibilityScale = scale;
        _controller.RequestDirectionalImpulse(new Vector2(3f, 4f), 1f);
        _controller.UpdateShake(0f);
        Vector3 expected = new Vector3(.6f, .8f) * _config.MaxOffset * scale;
        Assert.That(Vector3.Distance(_controller.ShakeOffset, expected), Is.LessThan(.00001f));
        _controller.UpdateShake(.5f);
        _controller.UpdateShake(0f);
        Assert.That(_controller.ShakeOffset.magnitude, Is.LessThanOrEqualTo(expected.magnitude * .5f));
        _controller.UpdateShake(1f);
        _controller.UpdateShake(0f);
        Assert.That(_controller.ShakeOffset, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void DisabledKick_DoesNotAccelerateAnExistingImpulse()
    {
        _controller.RequestDirectionalImpulse(new Vector2(.1f, 0f), 1f);
        _controller.RequestDirectionalImpulse(Vector2.zero, 0f);
        _controller.UpdateShake(.1f);
        _controller.UpdateShake(0f);
        Assert.That(_controller.ShakeOffset.x, Is.EqualTo(.09f).Within(.00001f));
    }

    [Test]
    public void ResetTransientTrauma_ClearsKick()
    {
        _controller.RequestDirectionalImpulse(Vector2.right, 1f);
        _controller.ResetTransientTrauma();
        _controller.UpdateShake(0f);
        Assert.That(_controller.ShakeOffset, Is.EqualTo(Vector3.zero));
    }
}
