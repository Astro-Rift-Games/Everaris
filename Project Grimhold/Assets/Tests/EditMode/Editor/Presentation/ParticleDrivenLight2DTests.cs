using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed class ParticleDrivenLight2DTests
{
    private GameObject _root;

    [TearDown]
    public void TearDown()
    {
        if (_root != null) Object.DestroyImmediate(_root);
    }

    [Test]
    public void Level_RisesOverFadeIn_AndClampsAtOne()
    {
        float level = ParticleLightLevel.Step(0f, lit: true, deltaSeconds: .05f, fadeInSeconds: .1f, fadeOutSeconds: .2f);
        Assert.That(level, Is.EqualTo(.5f).Within(.0001f));
        level = ParticleLightLevel.Step(level, true, .5f, .1f, .2f);
        Assert.That(level, Is.EqualTo(1f));
    }

    [Test]
    public void Level_FallsOverFadeOut_AndClampsAtZero()
    {
        float level = ParticleLightLevel.Step(1f, lit: false, deltaSeconds: .05f, fadeInSeconds: .1f, fadeOutSeconds: .2f);
        Assert.That(level, Is.EqualTo(.75f).Within(.0001f));
        level = ParticleLightLevel.Step(level, false, 5f, .1f, .2f);
        Assert.That(level, Is.EqualTo(0f));
    }

    [Test]
    public void Level_ZeroFadeDurations_SnapImmediately()
    {
        Assert.That(ParticleLightLevel.Step(0f, true, .01f, 0f, 0f), Is.EqualTo(1f));
        Assert.That(ParticleLightLevel.Step(1f, false, .01f, 0f, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void Level_NegativeOrNaNDelta_LeavesLevelUnchanged()
    {
        Assert.That(ParticleLightLevel.Step(.4f, true, -1f, .1f, .2f), Is.EqualTo(.4f));
        Assert.That(ParticleLightLevel.Step(.4f, false, float.NaN, .1f, .2f), Is.EqualTo(.4f));
    }

    [Test]
    public void Light_StartsDark_FollowsEmission_AndRestoresAuthoredIntensity()
    {
        _root = new GameObject("ParticleLight");
        ParticleSystem particles = _root.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var lightObject = new GameObject("Light");
        lightObject.transform.SetParent(_root.transform, false);
        Light2D light = lightObject.AddComponent<Light2D>();
        light.intensity = 2f;
        ParticleDrivenLight2D driven = lightObject.AddComponent<ParticleDrivenLight2D>();
        driven.Configure(light, particles, fadeInSeconds: .1f, fadeOutSeconds: .2f);

        driven.Advance(0f);
        Assert.That(light.intensity, Is.EqualTo(0f));
        Assert.That(light.enabled, Is.False);

        particles.Play(true);
        driven.Advance(.05f);
        Assert.That(light.enabled, Is.True);
        Assert.That(light.intensity, Is.EqualTo(1f).Within(.0001f));
        driven.Advance(1f);
        Assert.That(light.intensity, Is.EqualTo(2f).Within(.0001f));

        particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        driven.Advance(.1f);
        Assert.That(light.intensity, Is.EqualTo(1f).Within(.0001f));
        driven.Advance(1f);
        Assert.That(light.intensity, Is.EqualTo(0f));
        Assert.That(light.enabled, Is.False);
    }

    [Test]
    public void Light_MissingParticles_StaysDark()
    {
        _root = new GameObject("ParticleLight");
        Light2D light = _root.AddComponent<Light2D>();
        light.intensity = 2f;
        ParticleDrivenLight2D driven = _root.AddComponent<ParticleDrivenLight2D>();
        driven.Configure(light, null, .1f, .2f);

        driven.Advance(1f);

        Assert.That(light.intensity, Is.EqualTo(0f));
        Assert.That(light.enabled, Is.False);
    }
}
