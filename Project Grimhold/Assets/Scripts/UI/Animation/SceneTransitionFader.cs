using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Full-screen fade between scenes. It covers the screen while the local session is connecting
/// or returning (<see cref="SceneTransitionFadePolicy"/>) and fades in when a stable state is
/// reached. A scene loaded outside a session transition (for example logout to MainMenu) fades in
/// from black. It only observes <see cref="SessionConnectionCoordinator.StateChanged"/>, which is
/// local presentation feedback, and never delays or drives a transition.
/// Lives under the persistent coordinator object so it survives scene loads.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public sealed class SceneTransitionFader : MonoBehaviour
{
    [SerializeField]
    private SessionConnectionCoordinator _coordinator;

    [SerializeField, Min(0f)]
    private float _fadeOutDuration = 0.25f;

    [SerializeField, Min(0f)]
    private float _fadeInDuration = 0.35f;

    [SerializeField, Tooltip("Optional label shown while the screen is fully covered.")]
    private TMP_Text _loadingLabel;

    private readonly UiPanelTransitionState _cover = new();
    private CanvasGroup _canvasGroup;

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_coordinator == null)
        {
            Debug.LogError($"{nameof(SceneTransitionFader)} requires a {nameof(SessionConnectionCoordinator)}.", this);
            enabled = false;
            return;
        }

        _cover.SnapHidden();
        Apply();
    }

    private void OnEnable()
    {
        if (_coordinator == null)
        {
            return;
        }

        _coordinator.StateChanged += OnSessionStateChanged;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        if (_coordinator != null)
        {
            _coordinator.StateChanged -= OnSessionStateChanged;
        }

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update()
    {
        if (_cover.IsAnimating)
        {
            _cover.Advance(Time.unscaledDeltaTime, _fadeOutDuration, _fadeInDuration);
            Apply();
            return;
        }

        if (_cover.IsLogicallyVisible && _loadingLabel != null && _coordinator != null)
        {
            string animated = SceneTransitionFadePolicy.FormatAnimatedStatus(_coordinator.State, Time.unscaledTime);
            if (!string.Equals(_loadingLabel.text, animated, System.StringComparison.Ordinal))
            {
                _loadingLabel.text = animated;
            }
        }
    }

    private void OnSessionStateChanged(SessionConnectionState state)
    {
        if (!IsActiveInstance())
        {
            return;
        }

        if (SceneTransitionFadePolicy.ShouldCover(state))
        {
            _cover.BeginShow();
        }
        else
        {
            _cover.BeginHide();
        }

        Apply();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single || !IsActiveInstance())
        {
            return;
        }

        _cover.SnapShown();
        if (!SceneTransitionFadePolicy.ShouldCover(_coordinator.State))
        {
            _cover.BeginHide();
        }

        Apply();
    }

    // A MainMenu reload instantiates a duplicate coordinator that destroys itself; its fader must stay inert.
    private bool IsActiveInstance() => SessionConnectionCoordinator.Instance == _coordinator;

    private void Apply()
    {
        float alpha = _cover.Visibility;
        _canvasGroup.alpha = alpha;
        _canvasGroup.blocksRaycasts = _cover.IsLogicallyVisible;
        _canvasGroup.interactable = false;
        if (_loadingLabel != null)
        {
            bool visible = _cover.IsLogicallyVisible;
            if (_loadingLabel.gameObject.activeSelf != visible)
            {
                _loadingLabel.gameObject.SetActive(visible);
            }

            if (visible && _coordinator != null)
            {
                string animated = SceneTransitionFadePolicy.FormatAnimatedStatus(_coordinator.State, Time.unscaledTime);
                if (!string.Equals(_loadingLabel.text, animated, System.StringComparison.Ordinal))
                {
                    _loadingLabel.text = animated;
                }
            }
        }
    }
}
