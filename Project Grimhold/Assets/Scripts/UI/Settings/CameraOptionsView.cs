using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Settings section for the player's gameplay camera preferences. Slider ranges come from
/// <see cref="GameplayCameraConfig"/>; changes are stored through <see cref="GameplayCameraPreferences"/>
/// and applied live by <see cref="LocalCameraController"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class CameraOptionsView : MonoBehaviour
{
    [SerializeField] private GameplayCameraConfig _config;
    [SerializeField] private Slider _offsetIntensity;
    [SerializeField] private Slider _smoothing;
    [SerializeField] private Slider _followSpeed;
    [SerializeField] private Button _reset;

    private readonly List<SliderBinding> _bindings = new();

    private void Awake()
    {
        if (_config == null)
        {
            Debug.LogError($"{nameof(CameraOptionsView)} on {gameObject.name} has no {nameof(GameplayCameraConfig)} assigned; camera settings are disabled.", this);
            return;
        }

        Bind(_offsetIntensity, GameplayCameraPreferences.Setting.MaxOffsetDistance);
        Bind(_smoothing, GameplayCameraPreferences.Setting.SmoothTime);
        Bind(_followSpeed, GameplayCameraPreferences.Setting.FollowSpeed);
        _reset?.onClick.AddListener(ResetToDefaults);
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void OnDestroy()
    {
        foreach (SliderBinding binding in _bindings)
        {
            binding.Slider.onValueChanged.RemoveListener(binding.Listener);
        }

        _bindings.Clear();
        _reset?.onClick.RemoveListener(ResetToDefaults);
    }

    private void Bind(Slider slider, GameplayCameraPreferences.Setting setting)
    {
        if (slider == null)
        {
            return;
        }

        Vector2 range = GameplayCameraPreferences.RangeOf(setting, _config);
        slider.minValue = range.x;
        slider.maxValue = range.y;
        slider.wholeNumbers = false;

        UnityAction<float> listener = value => GameplayCameraPreferences.Set(setting, value, _config);
        slider.onValueChanged.AddListener(listener);
        _bindings.Add(new SliderBinding(slider, setting, listener));
    }

    private void ResetToDefaults()
    {
        GameplayCameraPreferences.ResetAll();
        Refresh();
    }

    private void Refresh()
    {
        foreach (SliderBinding binding in _bindings)
        {
            binding.Slider.SetValueWithoutNotify(GameplayCameraPreferences.Get(binding.Setting, _config));
        }
    }

    private readonly struct SliderBinding
    {
        public readonly Slider Slider;
        public readonly GameplayCameraPreferences.Setting Setting;
        public readonly UnityAction<float> Listener;

        public SliderBinding(Slider slider, GameplayCameraPreferences.Setting setting, UnityAction<float> listener)
        {
            Slider = slider;
            Setting = setting;
            Listener = listener;
        }
    }
}
