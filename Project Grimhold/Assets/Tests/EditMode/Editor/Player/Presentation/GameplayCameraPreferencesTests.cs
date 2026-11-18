using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Setting = GameplayCameraPreferences.Setting;

public sealed class GameplayCameraPreferencesTests
{
    private GameplayCameraPreferencesTestScope _scope;
    private GameplayCameraConfig _config;

    [SetUp]
    public void SetUp()
    {
        _scope = new GameplayCameraPreferencesTestScope();
        _config = ScriptableObject.CreateInstance<GameplayCameraConfig>();
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(_config);
        _scope.Dispose();
    }

    [Test]
    public void Get_Unset_ReturnsConfigDefaults()
    {
        Assert.That(GameplayCameraPreferences.Get(Setting.MaxOffsetDistance, _config), Is.EqualTo(_config.MaxOffsetDistance));
        Assert.That(GameplayCameraPreferences.Get(Setting.SmoothTime, _config), Is.EqualTo(_config.SmoothTime));
        Assert.That(GameplayCameraPreferences.Get(Setting.FollowSpeed, _config), Is.EqualTo(_config.FollowSpeed));
    }

    [Test]
    public void Get_Unset_FollowsLiveConfigEdits()
    {
        EditConfig("_followSpeed", 77f);

        Assert.That(GameplayCameraPreferences.Get(Setting.FollowSpeed, _config), Is.EqualTo(77f));
    }

    [Test]
    public void Set_PersistsAcrossCacheInvalidation()
    {
        GameplayCameraPreferences.Set(Setting.MaxOffsetDistance, 1.5f, _config);
        GameplayCameraPreferences.Set(Setting.SmoothTime, 0.3f, _config);
        GameplayCameraPreferences.Set(Setting.FollowSpeed, 20f, _config);

        GameplayCameraPreferences.InvalidateCacheForTests();

        Assert.That(GameplayCameraPreferences.Get(Setting.MaxOffsetDistance, _config), Is.EqualTo(1.5f));
        Assert.That(GameplayCameraPreferences.Get(Setting.SmoothTime, _config), Is.EqualTo(0.3f));
        Assert.That(GameplayCameraPreferences.Get(Setting.FollowSpeed, _config), Is.EqualTo(20f));
    }

    [Test]
    public void Set_OnlyChangesThatSetting()
    {
        GameplayCameraPreferences.Set(Setting.SmoothTime, 0.4f, _config);

        Assert.That(GameplayCameraPreferences.Get(Setting.SmoothTime, _config), Is.EqualTo(0.4f));
        Assert.That(GameplayCameraPreferences.Get(Setting.MaxOffsetDistance, _config), Is.EqualTo(_config.MaxOffsetDistance));
        Assert.That(PlayerPrefs.HasKey(GameplayCameraPreferences.KeyOf(Setting.MaxOffsetDistance)), Is.False);
    }

    [TestCase(Setting.MaxOffsetDistance)]
    [TestCase(Setting.SmoothTime)]
    [TestCase(Setting.FollowSpeed)]
    public void Set_ClampsToConfigRangeOnWrite(Setting setting)
    {
        Vector2 range = GameplayCameraPreferences.RangeOf(setting, _config);

        GameplayCameraPreferences.Set(setting, range.y + 1000f, _config);
        Assert.That(PlayerPrefs.GetFloat(GameplayCameraPreferences.KeyOf(setting)), Is.EqualTo(range.y));
        Assert.That(GameplayCameraPreferences.Get(setting, _config), Is.EqualTo(range.y));

        GameplayCameraPreferences.Set(setting, range.x - 1000f, _config);
        Assert.That(PlayerPrefs.GetFloat(GameplayCameraPreferences.KeyOf(setting)), Is.EqualTo(range.x));
        Assert.That(GameplayCameraPreferences.Get(setting, _config), Is.EqualTo(range.x));
    }

    [Test]
    public void Get_ClampsStoredValueToCurrentConfigRange()
    {
        GameplayCameraPreferences.Set(Setting.FollowSpeed, 90f, _config);
        EditConfig("_followSpeedRange", new Vector2(10f, 60f));

        Assert.That(GameplayCameraPreferences.Get(Setting.FollowSpeed, _config), Is.EqualTo(60f));
    }

    [Test]
    public void Get_ClampsHandEditedStoredValue()
    {
        PlayerPrefs.SetFloat(GameplayCameraPreferences.KeyOf(Setting.FollowSpeed), 100000f);
        GameplayCameraPreferences.InvalidateCacheForTests();

        Assert.That(GameplayCameraPreferences.Get(Setting.FollowSpeed, _config),
            Is.EqualTo(GameplayCameraPreferences.RangeOf(Setting.FollowSpeed, _config).y));
    }

    [Test]
    public void Set_NonFiniteValue_IsIgnored()
    {
        GameplayCameraPreferences.Set(Setting.SmoothTime, 0.2f, _config);

        GameplayCameraPreferences.Set(Setting.SmoothTime, float.NaN, _config);
        GameplayCameraPreferences.Set(Setting.SmoothTime, float.PositiveInfinity, _config);

        Assert.That(GameplayCameraPreferences.Get(Setting.SmoothTime, _config), Is.EqualTo(0.2f));
        Assert.That(PlayerPrefs.GetFloat(GameplayCameraPreferences.KeyOf(Setting.SmoothTime)), Is.EqualTo(0.2f));
    }

    [Test]
    public void Get_DoesNotReadPlayerPrefsAgainOnceCached()
    {
        GameplayCameraPreferences.Set(Setting.MaxOffsetDistance, 2f, _config);

        PlayerPrefs.SetFloat(GameplayCameraPreferences.KeyOf(Setting.MaxOffsetDistance), 5f);

        Assert.That(GameplayCameraPreferences.Get(Setting.MaxOffsetDistance, _config), Is.EqualTo(2f));
    }

    [Test]
    public void ResetAll_ClearsOverridesAndStoredKeys()
    {
        foreach (Setting setting in Enum.GetValues(typeof(Setting)))
        {
            GameplayCameraPreferences.Set(setting, GameplayCameraPreferences.RangeOf(setting, _config).y, _config);
        }

        GameplayCameraPreferences.ResetAll();

        foreach (Setting setting in Enum.GetValues(typeof(Setting)))
        {
            Assert.That(PlayerPrefs.HasKey(GameplayCameraPreferences.KeyOf(setting)), Is.False, setting.ToString());
            Assert.That(GameplayCameraPreferences.Get(setting, _config), Is.EqualTo(GameplayCameraPreferences.DefaultOf(setting, _config)), setting.ToString());
        }
    }

    [Test]
    public void Get_Unset_AssetEditsOfEveryPreferenceParameterApplyLive()
    {
        EditConfig("_maxOffsetDistance", 1.25f);
        EditConfig("_smoothTime", 0.45f);
        EditConfig("_followSpeed", 33f);

        Assert.That(GameplayCameraPreferences.Get(Setting.MaxOffsetDistance, _config), Is.EqualTo(1.25f));
        Assert.That(GameplayCameraPreferences.Get(Setting.SmoothTime, _config), Is.EqualTo(0.45f));
        Assert.That(GameplayCameraPreferences.Get(Setting.FollowSpeed, _config), Is.EqualTo(33f));
    }

    [Test]
    public void Settings_DoNotIncludeOrthographicSize_ZoomIsNotAPlayerPreference()
    {
        Assert.That(Enum.GetNames(typeof(Setting)), Is.EquivalentTo(new[] { "MaxOffsetDistance", "SmoothTime", "FollowSpeed" }));
        Assert.That(typeof(GameplayCameraConfig).GetProperty("OrthographicSizeRange"), Is.Null,
            "The config must not expose a player range for the orthographic size.");
    }

    [Test]
    public void ConfigRanges_HaveDesignerDefaultsAndContainTheDefaultValues()
    {
        Assert.That(_config.MaxOffsetDistanceRange, Is.EqualTo(new Vector2(0f, 6f)));
        Assert.That(_config.SmoothTimeRange, Is.EqualTo(new Vector2(0f, 0.5f)));
        Assert.That(_config.FollowSpeedRange, Is.EqualTo(new Vector2(5f, 100f)));

        foreach (Setting setting in Enum.GetValues(typeof(Setting)))
        {
            Vector2 range = GameplayCameraPreferences.RangeOf(setting, _config);
            float value = GameplayCameraPreferences.DefaultOf(setting, _config);
            Assert.That(value, Is.InRange(range.x, range.y), setting.ToString());
        }
    }

    private void EditConfig(string field, float value)
    {
        var configObject = new SerializedObject(_config);
        configObject.FindProperty(field).floatValue = value;
        configObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private void EditConfig(string field, Vector2 value)
    {
        var configObject = new SerializedObject(_config);
        configObject.FindProperty(field).vector2Value = value;
        configObject.ApplyModifiedPropertiesWithoutUndo();
    }
}
