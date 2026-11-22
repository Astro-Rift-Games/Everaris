#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Presentation
{
    public sealed class InteractionPromptPlacementTests
    {
        private static readonly Vector2 Screen = new(1920f, 1080f);
        private static readonly Vector2 PromptSize = new(320f, 44f);
        private static readonly Vector2 CentrePivot = new(0.5f, 0.5f);
        private const string CharacterHeadSheetPath = "Assets/Art/Character/Head/Idle/Player-Head-Idle-S-Sheet.png";

        private GameObject _root;
        private Texture2D _texture;
        private Sprite _sprite;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Interactable");
            _texture = new Texture2D(16, 32);
            // 16 pixels per unit: the sprite covers 1 x 2 world units around its centre.
            _sprite = Sprite.Create(
                _texture, new Rect(0f, 0f, 16f, 32f), CentrePivot, 16f, 0, SpriteMeshType.FullRect);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_sprite);
            Object.DestroyImmediate(_texture);
        }

        [Test]
        public void ResolveScreenPosition_RestsThePromptBottomEdgeAboveTheAnchor()
        {
            Vector2 position = InteractionPromptPlacement.ResolveScreenPosition(
                new Vector2(960f, 500f), PromptSize, CentrePivot, gap: 8f, Screen, margin: 12f);

            Assert.That(position, Is.EqualTo(new Vector2(960f, 530f)));
        }

        [Test]
        public void ResolveScreenPosition_AccountsForTheRectPivot()
        {
            Vector2 position = InteractionPromptPlacement.ResolveScreenPosition(
                new Vector2(960f, 500f), PromptSize, new Vector2(0f, 0f), gap: 8f, Screen, margin: 12f);

            Assert.That(position, Is.EqualTo(new Vector2(800f, 508f)));
        }

        [Test]
        public void ResolveScreenPosition_KeepsTheWholePromptInsideTheScreenMargin()
        {
            Vector2 topRight = InteractionPromptPlacement.ResolveScreenPosition(
                new Vector2(1900f, 1070f), PromptSize, CentrePivot, gap: 8f, Screen, margin: 12f);
            Vector2 bottomLeft = InteractionPromptPlacement.ResolveScreenPosition(
                new Vector2(-50f, -200f), PromptSize, CentrePivot, gap: 8f, Screen, margin: 12f);

            Assert.That(topRight, Is.EqualTo(new Vector2(1748f, 1046f)));
            Assert.That(bottomLeft, Is.EqualTo(new Vector2(172f, 34f)));
        }

        [Test]
        public void ResolveScreenPosition_SnapsThePromptCornerToWholePixels()
        {
            var oddSize = new Vector2(321f, 45f);

            Vector2 position = InteractionPromptPlacement.ResolveScreenPosition(
                new Vector2(960.4f, 500.6f), oddSize, CentrePivot, gap: 8f, Screen, margin: 12f);

            // The rect's bottom-left corner lands on a whole pixel, so its frame and text stay crisp.
            Vector2 corner = position - Vector2.Scale(oddSize, CentrePivot);
            Assert.That(corner, Is.EqualTo(new Vector2(800f, 509f)));
        }

        [Test]
        public void TryResolveWorldAnchor_PrefersTheExplicitAnchor()
        {
            SpriteRenderer renderer = AddRenderer(_root, Vector3.zero);
            var anchor = new GameObject("PromptAnchor");
            anchor.transform.SetParent(_root.transform, false);
            anchor.transform.position = new Vector3(3f, 7f, 0f);

            bool resolved = InteractionPromptPlacement.TryResolveWorldAnchor(
                anchor.transform, new[] { renderer }, _root.transform, out Vector3 worldAnchor);

            Assert.That(resolved, Is.True);
            Assert.That(worldAnchor, Is.EqualTo(new Vector3(3f, 7f, 0f)));
        }

        [Test]
        public void TryResolveWorldAnchor_UsesTheTopCentreOfTheCombinedSpriteArtwork()
        {
            _root.transform.position = new Vector3(10f, 5f, 0f);
            SpriteRenderer body = AddRenderer(_root, Vector3.zero);
            SpriteRenderer hat = AddRenderer(_root, new Vector3(1f, 2f, 0f));

            bool resolved = InteractionPromptPlacement.TryResolveWorldAnchor(
                null, new[] { body, hat }, _root.transform, out Vector3 worldAnchor);

            // Body spans x 9.5..10.5, y 4..6; hat spans x 10.5..11.5, y 6..8.
            Assert.That(resolved, Is.True);
            Assert.That(worldAnchor.x, Is.EqualTo(10.5f).Within(0.0001f));
            Assert.That(worldAnchor.y, Is.EqualTo(8f).Within(0.0001f));
        }

        [Test]
        public void TryResolveWorldAnchor_IgnoresTheTransparentPaddingOfTheSpriteFrame()
        {
            // Character animation frames are 96 px squares (6 world units) with the artwork in the middle:
            // this head frame reaches 3 units above its pivot, but the head itself stops at 13 px (0.8125).
            Sprite head = FindSprite(CharacterHeadSheetPath, "Player-Head-Idle-S-Sheet_0");
            _root.transform.position = new Vector3(10f, 5f, 0f);
            SpriteRenderer renderer = AddRenderer(_root, Vector3.zero);
            renderer.sprite = head;

            bool resolved = InteractionPromptPlacement.TryResolveWorldAnchor(
                null, new[] { renderer }, _root.transform, out Vector3 worldAnchor);

            Assert.That(resolved, Is.True);
            Assert.That(renderer.bounds.max.y, Is.EqualTo(8f).Within(0.0001f), "frame top");
            Assert.That(worldAnchor.y, Is.EqualTo(5.8125f).Within(0.0001f), "artwork top");
        }

        [Test]
        public void TryResolveWorldAnchor_IgnoresHiddenAndDestroyedRenderers()
        {
            SpriteRenderer body = AddRenderer(_root, Vector3.zero);
            SpriteRenderer hidden = AddRenderer(_root, new Vector3(0f, 10f, 0f));
            hidden.enabled = false;
            SpriteRenderer destroyed = AddRenderer(_root, new Vector3(0f, 20f, 0f));
            Object.DestroyImmediate(destroyed.gameObject);

            bool resolved = InteractionPromptPlacement.TryResolveWorldAnchor(
                null, new[] { body, hidden, destroyed }, _root.transform, out Vector3 worldAnchor);

            Assert.That(resolved, Is.True);
            Assert.That(worldAnchor.y, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void TryResolveWorldAnchor_FallsBackToTheTargetPosition()
        {
            _root.transform.position = new Vector3(-4f, 2f, 0f);

            bool resolved = InteractionPromptPlacement.TryResolveWorldAnchor(
                null, null, _root.transform, out Vector3 worldAnchor);

            Assert.That(resolved, Is.True);
            Assert.That(worldAnchor, Is.EqualTo(new Vector3(-4f, 2f, 0f)));
        }

        [Test]
        public void TryResolveWorldAnchor_WithoutAnyTarget_ReportsNoAnchor()
        {
            bool resolved = InteractionPromptPlacement.TryResolveWorldAnchor(null, null, null, out _);

            Assert.That(resolved, Is.False);
        }

        private static Sprite FindSprite(string sheetPath, string spriteName)
        {
            foreach (Object asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(sheetPath))
            {
                if (asset is Sprite sprite && sprite.name == spriteName)
                {
                    return sprite;
                }
            }

            Assert.Fail($"Sprite '{spriteName}' not found in '{sheetPath}'.");
            return null;
        }

        private SpriteRenderer AddRenderer(GameObject parent, Vector3 localPosition)
        {
            var child = new GameObject("Sprite");
            child.transform.SetParent(parent.transform, false);
            child.transform.localPosition = localPosition;
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = _sprite;
            return renderer;
        }
    }
}
#endif
