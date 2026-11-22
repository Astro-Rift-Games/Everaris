#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Tests.PlayMode.Presentation
{
    /// <summary>
    /// The interaction prompt hangs above its interactable on a Screen Space Overlay HUD instead of resting at a
    /// fixed HUD position.
    /// </summary>
    public sealed class InteractionPromptWorldPlacementPlayModeTests
    {
        private const float PixelTolerance = 1f;

        private GameObject _cameraObject;
        private Camera _camera;
        private GameObject _canvasObject;
        private RectTransform _promptRect;
        private InteractionHudPresenter _presenter;
        private InteractionPromptStyle _style;
        private TextMeshProUGUI _label;
        private readonly Vector3[] _corners = new Vector3[4];

        [SetUp]
        public void SetUp()
        {
            _cameraObject = new GameObject("WorldCamera");
            _camera = _cameraObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 5f;
            _camera.transform.position = new Vector3(0f, 0f, -10f);

            _canvasObject = new GameObject("Hud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var promptObject = new GameObject(
                "InteractionPrompt", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            _promptRect = promptObject.GetComponent<RectTransform>();
            _promptRect.SetParent(_canvasObject.transform, false);
            _promptRect.anchorMin = new Vector2(0.5f, 0f);
            _promptRect.anchorMax = new Vector2(0.5f, 0f);
            _promptRect.pivot = new Vector2(0.5f, 0.5f);
            _promptRect.sizeDelta = new Vector2(200f, 40f);
            _promptRect.anchoredPosition = new Vector2(0f, 72f);

            _presenter = _canvasObject.AddComponent<InteractionHudPresenter>();
            SetField("_promptRoot", promptObject);
            SetField("_promptText", promptObject.GetComponent<TextMeshProUGUI>());
            SetField("_worldCamera", _camera);

            _style = ScriptableObject.CreateInstance<InteractionPromptStyle>();
            SetField("_promptStyle", _style);
            _label = promptObject.GetComponent<TextMeshProUGUI>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_canvasObject);
            Object.DestroyImmediate(_cameraObject);
            Object.DestroyImmediate(_style);
        }

        [UnityTest]
        public IEnumerator Prompt_RestsItsBottomEdgeAboveTheWorldAnchor()
        {
            yield return null;
            var worldAnchor = new Vector3(0.5f, 1f, 0f);

            PlacePromptAbove(worldAnchor);

            Vector2 anchorOnScreen = _camera.WorldToScreenPoint(worldAnchor);
            _promptRect.GetWorldCorners(_corners);
            float bottomCentreX = (_corners[0].x + _corners[3].x) * 0.5f;
            Assert.That(bottomCentreX, Is.EqualTo(anchorOnScreen.x).Within(PixelTolerance));
            float gapInPixels = _style.Gap * _canvasObject.GetComponent<Canvas>().scaleFactor;
            Assert.That(_corners[0].y, Is.EqualTo(anchorOnScreen.y + gapInPixels).Within(PixelTolerance));
        }

        [UnityTest]
        public IEnumerator Prompt_FollowsTheAnchorWhenItMoves()
        {
            yield return null;
            PlacePromptAbove(new Vector3(-1f, 0f, 0f));
            Vector3 first = _promptRect.position;

            PlacePromptAbove(new Vector3(1f, 2f, 0f));

            Assert.That(_promptRect.position.x, Is.GreaterThan(first.x));
            Assert.That(_promptRect.position.y, Is.GreaterThan(first.y));
        }

        [UnityTest]
        public IEnumerator Prompt_StaysOnScreenWhenTheAnchorIsOutsideTheView()
        {
            yield return null;

            PlacePromptAbove(new Vector3(500f, 500f, 0f));

            _promptRect.GetWorldCorners(_corners);
            Assert.That(_corners[2].x, Is.LessThanOrEqualTo(Screen.width));
            Assert.That(_corners[2].y, Is.LessThanOrEqualTo(Screen.height));
            Assert.That(_corners[0].x, Is.GreaterThanOrEqualTo(0f));
            Assert.That(_corners[0].y, Is.GreaterThanOrEqualTo(0f));
        }

        [UnityTest]
        public IEnumerator Prompt_UsesTheSharedStyleGapInCanvasUnits()
        {
            yield return null;
            var worldAnchor = new Vector3(0f, 0f, 0f);
            SetStyle(gap: 40f, screenMargin: 0f);

            PlacePromptAbove(worldAnchor);

            Vector2 anchorOnScreen = _camera.WorldToScreenPoint(worldAnchor);
            float scale = _canvasObject.GetComponent<Canvas>().scaleFactor;
            _promptRect.GetWorldCorners(_corners);
            Assert.That(_corners[0].y, Is.EqualTo(anchorOnScreen.y + (40f * scale)).Within(PixelTolerance));
        }

        [UnityTest]
        public IEnumerator Prompt_ResizesToItsTextInTheSameFrame()
        {
            // A prompt that sizes to its content, as the shared prefab does.
            var layout = _promptRect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            var labelObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(_promptRect, false);
            Object.DestroyImmediate(_label);
            _label = labelObject.GetComponent<TextMeshProUGUI>();
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            SetField("_promptText", _label);
            var fitter = _promptRect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            yield return null;

            ApplyPromptText("[F] Ir");
            float shortWidth = _promptRect.rect.width;
            ApplyPromptText("[F] Comerciar con el Mercader General");
            float longWidth = _promptRect.rect.width;

            Assert.That(shortWidth, Is.GreaterThan(0f));
            Assert.That(longWidth, Is.GreaterThan(shortWidth * 2f));
        }

        private void ApplyPromptText(string text)
        {
            MethodInfo method = typeof(InteractionHudPresenter).GetMethod(
                "ApplyPromptText",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "InteractionHudPresenter.ApplyPromptText");
            method.Invoke(_presenter, new object[] { text, false });
        }

        private void SetStyle(float gap, float screenMargin)
        {
            var serialized = new UnityEditor.SerializedObject(_style);
            serialized.FindProperty("_gap").floatValue = gap;
            serialized.FindProperty("_screenMargin").floatValue = screenMargin;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void PlacePromptAbove(Vector3 worldAnchor)
        {
            MethodInfo method = typeof(InteractionHudPresenter).GetMethod(
                "PlacePromptAbove",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "InteractionHudPresenter.PlacePromptAbove");
            method.Invoke(_presenter, new object[] { worldAnchor });
        }

        private void SetField(string fieldName, object value)
        {
            FieldInfo field = typeof(InteractionHudPresenter).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(_presenter, value);
        }
    }
}
#endif
