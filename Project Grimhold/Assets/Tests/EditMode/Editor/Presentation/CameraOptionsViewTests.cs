#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Setting = GameplayCameraPreferences.Setting;

namespace Tests.EditMode.Presentation
{
    public sealed class CameraOptionsViewTests
    {
        private const string SettingsPrefabPath = "Assets/Prefabs/UI/SettingsPanel.prefab";
        private const string ConfigAssetPath = "Assets/Scriptable Objects/GameplayCameraConfig.asset";

        private GameplayCameraPreferencesTestScope _preferences;
        private GameplayCameraConfig _config;
        private GameObject _root;
        private CameraOptionsView _view;
        private Slider _offset;
        private Slider _smoothing;
        private Slider _followSpeed;
        private Button _reset;

        [SetUp]
        public void SetUp()
        {
            _preferences = new GameplayCameraPreferencesTestScope();
            _config = ScriptableObject.CreateInstance<GameplayCameraConfig>();
            _root = new GameObject("CameraOptionsViewTests");
            _view = _root.AddComponent<CameraOptionsView>();
            _offset = CreateSlider("Offset", "_offsetIntensity");
            _smoothing = CreateSlider("Smoothing", "_smoothing");
            _followSpeed = CreateSlider("FollowSpeed", "_followSpeed");
            var resetObject = new GameObject("Reset", typeof(RectTransform), typeof(Button));
            resetObject.transform.SetParent(_root.transform);
            _reset = resetObject.GetComponent<Button>();
            SetField("_reset", _reset);
            SetField("_config", _config);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_config);
            _preferences.Dispose();
        }

        [Test]
        public void Awake_SliderRangesComeFromConfig()
        {
            var configObject = new SerializedObject(_config);
            configObject.FindProperty("_maxOffsetDistanceRange").vector2Value = new Vector2(1f, 5f);
            configObject.FindProperty("_smoothTimeRange").vector2Value = new Vector2(0.05f, 0.4f);
            configObject.FindProperty("_followSpeedRange").vector2Value = new Vector2(10f, 80f);
            configObject.ApplyModifiedPropertiesWithoutUndo();

            Invoke("Awake");

            AssertRange(_offset, 1f, 5f);
            AssertRange(_smoothing, 0.05f, 0.4f);
            AssertRange(_followSpeed, 10f, 80f);
            Assert.That(_offset.wholeNumbers, Is.False);
        }

        [Test]
        public void Enable_UnsetPreferences_ShowsConfigDefaultsWithoutWritingPreferences()
        {
            Invoke("Awake");
            Invoke("OnEnable");

            Assert.That(_offset.value, Is.EqualTo(_config.MaxOffsetDistance));
            Assert.That(_smoothing.value, Is.EqualTo(_config.SmoothTime));
            Assert.That(_followSpeed.value, Is.EqualTo(_config.FollowSpeed));
            Assert.That(PlayerPrefs.HasKey(GameplayCameraPreferences.KeyOf(Setting.MaxOffsetDistance)), Is.False);
        }

        [Test]
        public void Enable_StoredPreferences_ShowsEffectiveValues()
        {
            GameplayCameraPreferences.Set(Setting.MaxOffsetDistance, 1.5f, _config);
            GameplayCameraPreferences.Set(Setting.FollowSpeed, 40f, _config);

            Invoke("Awake");
            Invoke("OnEnable");

            Assert.That(_offset.value, Is.EqualTo(1.5f));
            Assert.That(_followSpeed.value, Is.EqualTo(40f));
            Assert.That(_smoothing.value, Is.EqualTo(_config.SmoothTime));
        }

        [Test]
        public void SliderChange_WritesOnlyItsPreference()
        {
            Invoke("Awake");
            Invoke("OnEnable");

            _smoothing.value = 0.3f;

            Assert.That(GameplayCameraPreferences.Get(Setting.SmoothTime, _config), Is.EqualTo(0.3f));
            Assert.That(PlayerPrefs.GetFloat(GameplayCameraPreferences.KeyOf(Setting.SmoothTime)), Is.EqualTo(0.3f));
            Assert.That(PlayerPrefs.HasKey(GameplayCameraPreferences.KeyOf(Setting.MaxOffsetDistance)), Is.False);
            Assert.That(PlayerPrefs.HasKey(GameplayCameraPreferences.KeyOf(Setting.FollowSpeed)), Is.False);
        }

        [TestCase(0, Setting.MaxOffsetDistance, 2f)]
        [TestCase(1, Setting.SmoothTime, 0.25f)]
        [TestCase(2, Setting.FollowSpeed, 30f)]
        public void SliderChange_MapsEachSliderToItsSetting(int slider, Setting setting, float value)
        {
            Invoke("Awake");
            Invoke("OnEnable");

            new[] { _offset, _smoothing, _followSpeed }[slider].value = value;

            Assert.That(GameplayCameraPreferences.Get(setting, _config), Is.EqualTo(value));
        }

        [Test]
        public void ResetButton_ClearsPreferencesAndRestoresDefaultsOnSliders()
        {
            Invoke("Awake");
            Invoke("OnEnable");
            _offset.value = 1f;
            _smoothing.value = 0.4f;
            _followSpeed.value = 20f;

            _reset.onClick.Invoke();

            foreach (Setting setting in System.Enum.GetValues(typeof(Setting)))
            {
                Assert.That(PlayerPrefs.HasKey(GameplayCameraPreferences.KeyOf(setting)), Is.False, setting.ToString());
                Assert.That(GameplayCameraPreferences.Get(setting, _config), Is.EqualTo(GameplayCameraPreferences.DefaultOf(setting, _config)), setting.ToString());
            }

            Assert.That(_offset.value, Is.EqualTo(_config.MaxOffsetDistance));
            Assert.That(_smoothing.value, Is.EqualTo(_config.SmoothTime));
            Assert.That(_followSpeed.value, Is.EqualTo(_config.FollowSpeed));
        }

        [Test]
        public void Destroy_StopsListeningToSliders()
        {
            Invoke("Awake");
            Invoke("OnEnable");
            Invoke("OnDestroy");

            _followSpeed.value = 40f;

            Assert.That(PlayerPrefs.HasKey(GameplayCameraPreferences.KeyOf(Setting.FollowSpeed)), Is.False);
        }

        [Test]
        public void SettingsPanelPrefab_ComposesCameraSectionWithConfigSlidersAndReset()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPrefabPath);
            var options = prefab.GetComponent<CameraOptionsView>();
            Assert.That(options, Is.Not.Null, "SettingsPanel must host the camera options section.");

            var so = new SerializedObject(options);
            var config = AssetDatabase.LoadAssetAtPath<GameplayCameraConfig>(ConfigAssetPath);
            Assert.That(so.FindProperty("_config").objectReferenceValue, Is.SameAs(config));

            var wired = new Object[4];
            string[] fields = { "_offsetIntensity", "_smoothing", "_followSpeed", "_reset" };
            for (int i = 0; i < fields.Length; i++)
            {
                wired[i] = so.FindProperty(fields[i]).objectReferenceValue;
                Assert.That(wired[i], Is.Not.Null, fields[i]);
            }

            Assert.That(wired, Is.Unique, "Every camera control must be its own object.");
            Assert.That(so.FindProperty("_zoom"), Is.Null, "Zoom is not a player setting.");
            Assert.That(prefab.GetComponentsInChildren<TMPro.TMP_Text>(true).Select(text => text.text),
                Does.Not.Contain("Zoom"), "The Settings panel must not expose a zoom control.");
            Assert.That(prefab.transform.Find("Panel/Camera Rows").childCount, Is.EqualTo(3));

            var audio = new SerializedObject(prefab.GetComponent<SettingsPanelView>());
            var screenDamage = new SerializedObject(prefab.GetComponent<ScreenDamageOptionsView>());
            var reserved = new System.Collections.Generic.HashSet<Object>
            {
                audio.FindProperty("_masterSlider").objectReferenceValue,
                audio.FindProperty("_musicSlider").objectReferenceValue,
                audio.FindProperty("_sfxSlider").objectReferenceValue,
                audio.FindProperty("_backButton").objectReferenceValue,
                screenDamage.FindProperty("_intensity").objectReferenceValue,
            };
            foreach (Object control in wired)
            {
                Assert.That(reserved.Contains(control), Is.False, $"{control.name} is already used by another Settings section.");
            }
        }

        private static void AssertRange(Slider slider, float min, float max)
        {
            Assert.That(slider.minValue, Is.EqualTo(min));
            Assert.That(slider.maxValue, Is.EqualTo(max));
        }

        private Slider CreateSlider(string name, string field)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Slider));
            child.transform.SetParent(_root.transform);
            Slider slider = child.GetComponent<Slider>();
            slider.minValue = -100f;
            slider.maxValue = 100f;
            SetField(field, slider);
            return slider;
        }

        // EditMode AddComponent does not invoke lifecycle callbacks; exercise the real wiring explicitly.
        private void Invoke(string method) => typeof(CameraOptionsView)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_view, null);

        private void SetField(string name, object value) => typeof(CameraOptionsView)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_view, value);
    }
}
#endif
