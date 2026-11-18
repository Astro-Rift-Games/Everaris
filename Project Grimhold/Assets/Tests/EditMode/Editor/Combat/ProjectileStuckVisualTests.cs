#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using ProjectGrimhold.Gameplay.Visibility;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Combat
{
    public sealed class ProjectileStuckVisualTests
    {
        private readonly List<Object> _owned = new();
        private GameObject _root;
        private Transform _view;
        private SpriteRenderer _renderer;
        private ProjectileVisualProfilePresenter _presenter;
        private ProjectileVisualProfile _profile;
        private Sprite _sprite;
        private WeaponAttackTag Tag => new(EquipmentSlot.WeaponSetAMainHand, 1, 1, AttackType.Ranged);

        [SetUp]
        public void Setup()
        {
            _root = Own(new GameObject("StuckTestProjectile"));
            var view = new GameObject("View");
            view.transform.SetParent(_root.transform, false);
            // As in the projectile prefab: the art points up (+Y) and the View is turned so it follows the root's +X.
            view.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            _view = view.transform;
            _renderer = view.AddComponent<SpriteRenderer>();
            _renderer.color = new Color(1f, 1f, 1f, 0.8f);
            _renderer.sortingLayerName = "Traps";
            _renderer.sortingOrder = 10;
            _presenter = _root.AddComponent<ProjectileVisualProfilePresenter>();
            var equipment = _root.AddComponent<PlayerWeaponEquipmentNetworkController>();
            _profile = Own(ScriptableObject.CreateInstance<ProjectileVisualProfile>());
            var weapon = Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            var loot = Own(ScriptableObject.CreateInstance<LootDefinition>());
            Set(loot, "_id", "stuck-test");
            Set(loot, "_weaponDefinition", weapon);
            var catalog = Own(ScriptableObject.CreateInstance<LootDefinitionCatalog>());
            var serialized = new SerializedObject(catalog);
            var entries = serialized.FindProperty("_definitions");
            entries.arraySize = 1;
            entries.GetArrayElementAtIndex(0).objectReferenceValue = loot;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Set(equipment, "_lootCatalog", catalog);
            Set(_presenter, "_equipmentSource", equipment);
            Set(_presenter, "_spriteRenderer", _renderer);
            Set(weapon, "_projectileVisual", _profile);
            var texture = Own(new Texture2D(4, 10));
            _sprite = Own(Sprite.Create(texture, new Rect(0, 0, 4, 10), Vector2.one * .5f, 1f));
            Set(_profile, "_sprite", _sprite);
            Set(_profile, "_stickOnImpact", true);
            Set(_profile, "_stickPenetration", 0.25f);
            Set(_profile, "_stickVisibleFraction", 0.5f);
            Set(_profile, "_stickHoldSeconds", 4.5f);
            Set(_profile, "_stickFadeSeconds", 0.5f);
            Set(_profile, "_stickBodySortingLayer", "VFX");
            // Flight direction is the projectile root's right axis (NetworkProjectile.Render).
            _root.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (StuckProjectileVisual stuck in Object.FindObjectsByType<StuckProjectileVisual>(FindObjectsInactive.Include))
            {
                // The visual owns its cropped sprite; EditMode never runs OnDestroy, so release it here.
                Sprite cropped = stuck.GetComponent<SpriteRenderer>().sprite;
                Object.DestroyImmediate(stuck.gameObject);
                if (cropped != null && cropped != _sprite) Object.DestroyImmediate(cropped);
            }
            for (int i = _owned.Count - 1; i >= 0; i--) if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [Test]
        public void GeometryImpact_LeavesWorldStuckCopyOfFlightSprite_CutEdgeAtImpactPlusPenetration()
        {
            _presenter.BeginFlight(Tag, DamageType.Physical);
            _presenter.PresentImpact(new Vector2(2f, 3f), true);

            StuckProjectileVisual[] stuck = Object.FindObjectsByType<StuckProjectileVisual>();
            Assert.That(stuck, Has.Length.EqualTo(1));
            Transform visual = stuck[0].transform;
            Assert.That(visual.parent, Is.Null, "The stuck visual must outlive the despawned projectile.");
            Assert.That(Vector2.Distance(visual.position, new Vector2(2f, 3.25f)), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(visual.rotation, _renderer.transform.rotation), Is.LessThan(0.01f));
            var copy = stuck[0].GetComponent<SpriteRenderer>();
            Assert.That(copy.color, Is.EqualTo(new Color(1f, 1f, 1f, 0.8f)));
            Assert.That(copy.flipY, Is.False);
            Assert.That(copy.sortingLayerName, Is.EqualTo("Traps"));
            Assert.That(copy.sortingOrder, Is.EqualTo(10));
        }

        [Test]
        public void StuckCopy_KeepsTrailingHalf_WithPivotOnCutEdge_AndNothingDrawnPastTheSurface()
        {
            _presenter.BeginFlight(Tag, DamageType.Physical);
            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            var copy = stuck.GetComponent<SpriteRenderer>();
            Sprite cropped = copy.sprite;
            Assert.That(cropped, Is.Not.SameAs(_sprite), "The tip must be cut, not reuse the full sprite.");
            Assert.That(cropped.rect.width, Is.EqualTo(_sprite.rect.width));
            Assert.That(cropped.rect.height, Is.EqualTo(5f));
            Assert.That(cropped.rect.x, Is.EqualTo(_sprite.rect.x));
            Assert.That(cropped.rect.y, Is.EqualTo(_sprite.rect.y), "The trailing (bottom, opposite the tip) part is kept.");
            Assert.That(cropped.texture, Is.SameAs(_sprite.texture));
            Assert.That(cropped.pixelsPerUnit, Is.EqualTo(_sprite.pixelsPerUnit));
            Assert.That(cropped.pivot.y, Is.EqualTo(cropped.rect.height), "The pivot sits on the cut edge.");
            Assert.That(cropped.pivot.x, Is.EqualTo(_sprite.pivot.x), "The pivot is preserved on the other axis.");
            Assert.That(copy.bounds.max.y, Is.EqualTo(3.25f).Within(0.0001f));
            Assert.That(copy.bounds.size.y, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(copy.bounds.size.x, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void StuckCopy_RoundsVisibleLength_AndNeverDropsBelowOnePixel()
        {
            Set(_profile, "_stickVisibleFraction", 0.33f);
            _presenter.BeginFlight(Tag, DamageType.Physical);
            Assert.That(_presenter.PresentStick(Vector2.zero, null, default).GetComponent<SpriteRenderer>().sprite.rect.height, Is.EqualTo(3f));

            Set(_profile, "_stickVisibleFraction", 0.05f);
            _presenter.BeginFlight(Tag, DamageType.Physical);
            Assert.That(_presenter.PresentStick(Vector2.zero, null, default).GetComponent<SpriteRenderer>().sprite.rect.height, Is.EqualTo(1f));
        }

        [Test]
        public void StuckCopy_WithFullFraction_KeepsFullLengthEndingAtTheSurface()
        {
            Set(_profile, "_stickVisibleFraction", 1f);
            _presenter.BeginFlight(Tag, DamageType.Physical);
            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            var copy = stuck.GetComponent<SpriteRenderer>();
            Assert.That(copy.sprite.rect, Is.EqualTo(_sprite.rect));
            Assert.That(copy.sprite.pivot.y, Is.EqualTo(10f));
            Assert.That(copy.bounds.max.y, Is.EqualTo(3.25f).Within(0.0001f));
            Assert.That(copy.bounds.size.y, Is.EqualTo(10f).Within(0.0001f));
        }

        [Test]
        public void StuckCopy_WithFlippedRenderer_KeepsTheSideOppositeTheRenderedTip()
        {
            _renderer.flipY = true;
            _presenter.BeginFlight(Tag, DamageType.Physical);
            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            var copy = stuck.GetComponent<SpriteRenderer>();
            Assert.That(copy.flipY, Is.True);
            Assert.That(copy.sprite.rect.height, Is.EqualTo(5f));
            Assert.That(copy.sprite.rect.y, Is.EqualTo(5f), "Flipped, the rendered tip is the sprite bottom, so the top part is kept.");
            Assert.That(copy.sprite.pivot.y, Is.Zero);
            Assert.That(copy.bounds.max.y, Is.EqualTo(3.25f).Within(0.0001f));
        }

        [Test]
        public void StuckCopy_CropsAlongTheFlightAxis_ForARotatedView()
        {
            _root.transform.rotation = Quaternion.identity; // Flight = world +X; art-up maps to +X through the View.
            _presenter.BeginFlight(Tag, DamageType.Physical);
            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            var copy = stuck.GetComponent<SpriteRenderer>();
            Assert.That(Vector2.Distance(stuck.transform.position, new Vector2(2.25f, 3f)), Is.LessThan(0.0001f));
            Assert.That(copy.sprite.rect.width, Is.EqualTo(4f));
            Assert.That(copy.sprite.rect.height, Is.EqualTo(5f));
            Assert.That(copy.sprite.rect.y, Is.Zero);
            Assert.That(copy.bounds.max.x, Is.EqualTo(2.25f).Within(0.0001f));
            Assert.That(copy.bounds.size.x, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void StuckCopy_CropsTheWidthAxis_WhenFlightFollowsTheSpriteRightAxis()
        {
            _view.localRotation = Quaternion.identity; // Flight (root +X, here world +Y) is the sprite's local +X.
            _presenter.BeginFlight(Tag, DamageType.Physical);
            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            var copy = stuck.GetComponent<SpriteRenderer>();
            Assert.That(copy.sprite.rect.width, Is.EqualTo(2f));
            Assert.That(copy.sprite.rect.height, Is.EqualTo(10f));
            Assert.That(copy.sprite.rect.x, Is.Zero);
            Assert.That(copy.sprite.pivot.x, Is.EqualTo(2f));
            Assert.That(copy.sprite.pivot.y, Is.EqualTo(5f));
            Assert.That(copy.bounds.max.y, Is.EqualTo(3.25f).Within(0.0001f));
        }

        [Test]
        public void StuckCopy_DropsTheAlongFlightRendererOffset_ButKeepsThePerpendicularOne()
        {
            // Root is turned 90 degrees: root-local x is world +Y (flight), root-local y is world -X.
            _view.localPosition = new Vector3(0.4f, 0.1f, 0f);
            _presenter.BeginFlight(Tag, DamageType.Physical);
            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            Assert.That(Vector2.Distance(stuck.transform.position, new Vector2(1.9f, 3.25f)), Is.LessThan(0.0001f));
        }

        [Test]
        public void StuckVisual_ReleasesItsOwnedCroppedSprite_ButNotTheSourceSprite()
        {
            _presenter.BeginFlight(Tag, DamageType.Physical);
            StuckProjectileVisual stuck = _presenter.PresentStick(Vector2.zero, null, default);
            Sprite cropped = stuck.GetComponent<SpriteRenderer>().sprite;
            Assert.That(cropped, Is.Not.SameAs(_sprite));

            stuck.ReleaseOwnedSprite();

            Assert.That(cropped == null, Is.True);
            Assert.That(_sprite == null, Is.False);
        }

        [Test]
        public void ProfileWithoutStick_OrNoProfile_LeavesNoStuckVisual()
        {
            Set(_profile, "_stickOnImpact", false);
            _presenter.BeginFlight(Tag, DamageType.Physical);
            Assert.That(_presenter.HasStickOverride, Is.False);
            _presenter.PresentImpact(Vector2.zero, true);
            _presenter.BeginFlight(default, DamageType.Physical);
            _presenter.PresentImpact(Vector2.zero, true);
            Assert.That(Object.FindObjectsByType<StuckProjectileVisual>(), Is.Empty);
        }

        [Test]
        public void BodyImpact_SnapsToTargetOffset_FollowsBody_AndUsesBodySortingLayer()
        {
            Transform body = Own(new GameObject("StuckTestBody")).transform;
            body.position = new Vector3(10f, 10f, 0f);
            _presenter.BeginFlight(Tag, DamageType.Physical);

            // The replicated offset wins over this peer's interpolated impact position.
            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(-50f, -50f), body, new Vector2(0.5f, 0f));

            Assert.That(stuck, Is.Not.Null);
            Assert.That(Vector2.Distance(stuck.transform.position, new Vector2(10.5f, 10.25f)), Is.LessThan(0.0001f));
            Assert.That(stuck.GetComponent<SpriteRenderer>().sortingLayerName, Is.EqualTo("VFX"));
            body.position = new Vector3(20f, 0f, 0f);
            Assert.That(stuck.Tick(0.1f), Is.False);
            Assert.That(Vector2.Distance(stuck.transform.position, new Vector2(20.5f, 0.25f)), Is.LessThan(0.0001f));
        }

        [Test]
        public void StuckVisual_HoldsFullAlpha_FadesOut_ThenExpires()
        {
            _presenter.BeginFlight(Tag, DamageType.Physical);
            StuckProjectileVisual stuck = _presenter.PresentStick(Vector2.zero, null, default);
            var copy = stuck.GetComponent<SpriteRenderer>();

            Assert.That(stuck.Tick(4.5f), Is.False);
            Assert.That(copy.color.a, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(stuck.Tick(0.25f), Is.False);
            Assert.That(copy.color.a, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(stuck.Tick(0.25f), Is.True);
            Assert.That(stuck.IsExpired, Is.True);
            Assert.That(copy.color.a, Is.Zero);
        }

        [Test]
        public void BodyStuckVisual_ExpiresWhenBodyIsDestroyed()
        {
            GameObject body = Own(new GameObject("StuckTestBody"));
            _presenter.BeginFlight(Tag, DamageType.Physical);
            StuckProjectileVisual stuck = _presenter.PresentStick(Vector2.zero, body.transform, default);
            Object.DestroyImmediate(body);
            Assert.That(stuck.Tick(0.01f), Is.True);
        }

        [Test]
        public void BodyStuckVisual_MirrorsBodyVisibility()
        {
            GameObject body = Own(new GameObject("StuckTestBody"));
            var visibility = body.AddComponent<EntityVisibilityPresenter>();
            // One empty slot: ApplyVisibility skips null renderers but ignores a null array.
            Set(visibility, "_renderers", new Renderer[1]);
            _presenter.BeginFlight(Tag, DamageType.Physical);
            StuckProjectileVisual stuck = _presenter.PresentStick(Vector2.zero, body.transform, default);
            var copy = stuck.GetComponent<SpriteRenderer>();

            visibility.ApplyVisibility(0.5f);
            stuck.Tick(0.1f);
            Assert.That(copy.color.a, Is.EqualTo(0.4f).Within(0.0001f));
            visibility.ApplyVisibility(0f);
            stuck.Tick(0.1f);
            Assert.That(copy.forceRenderingOff, Is.True, "A body hidden by vision must hide its stuck arrow.");
            visibility.ApplyVisibility(1f);
            stuck.Tick(0.1f);
            Assert.That(copy.forceRenderingOff, Is.False);
            Assert.That(copy.color.a, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void AnchoredVisual_ExpiresWhenAnchorColliderIsDisabled()
        {
            BoxCollider2D anchor = Own(new GameObject("StuckTestAnchor")).AddComponent<BoxCollider2D>();
            StuckProjectileVisual stuck = CreateAnchored(anchor, null);

            Assert.That(stuck.Tick(0.01f), Is.False);
            anchor.enabled = false;
            Assert.That(stuck.Tick(0.01f), Is.True);
            Assert.That(stuck.IsExpired, Is.True);
        }

        [Test]
        public void AnchoredVisual_ExpiresWhenAnchorObjectIsDeactivated_OrDestroyed()
        {
            BoxCollider2D deactivated = Own(new GameObject("StuckTestAnchorA")).AddComponent<BoxCollider2D>();
            StuckProjectileVisual first = CreateAnchored(deactivated, null);
            Assert.That(first.Tick(0.01f), Is.False);
            deactivated.gameObject.SetActive(false);
            Assert.That(first.Tick(0.01f), Is.True);

            BoxCollider2D destroyed = Own(new GameObject("StuckTestAnchorB")).AddComponent<BoxCollider2D>();
            StuckProjectileVisual second = CreateAnchored(destroyed, null);
            Object.DestroyImmediate(destroyed.gameObject);
            Assert.That(second.Tick(0.01f), Is.True);
        }

        [Test]
        public void AnchorOnlyVisual_FollowsTheAnchorWhenItMoves()
        {
            BoxCollider2D anchor = Own(new GameObject("StuckTestAnchor")).AddComponent<BoxCollider2D>();
            anchor.transform.position = new Vector3(1f, 1f, 0f);
            StuckProjectileVisual stuck = CreateAnchored(anchor, null);
            Vector3 start = stuck.transform.position;

            anchor.transform.position = new Vector3(6f, -2f, 0f);
            Assert.That(stuck.Tick(0.1f), Is.False);

            Assert.That(Vector2.Distance(stuck.transform.position, start + new Vector3(5f, -3f, 0f)), Is.LessThan(0.0001f));
        }

        [Test]
        public void BodyAndAnchor_FollowTheBody_ButExpireWithTheAnchor()
        {
            Transform body = Own(new GameObject("StuckTestBody")).transform;
            BoxCollider2D anchor = Own(new GameObject("StuckTestAnchor")).AddComponent<BoxCollider2D>();
            StuckProjectileVisual stuck = CreateAnchored(anchor, body);

            body.position = new Vector3(4f, 4f, 0f);
            Assert.That(stuck.Tick(0.1f), Is.False);
            Assert.That(Vector2.Distance(stuck.transform.position, new Vector2(6f, 7f)), Is.LessThan(0.0001f));
            anchor.enabled = false;
            Assert.That(stuck.Tick(0.1f), Is.True);
        }

        [Test]
        public void PresentStick_AnchorsToTheColliderUnderTheStuckPoint_SoABrokenObjectEndsIt()
        {
            BoxCollider2D box = Own(new GameObject("StuckTestBreakable")).AddComponent<BoxCollider2D>();
            box.transform.position = new Vector3(2f, 3.5f, 0f); // Spans y 3..4: flight (+Y) enters at y = 3.
            box.size = Vector2.one;
            Physics2D.SyncTransforms();
            _presenter.BeginFlight(Tag, DamageType.Physical);

            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            Assert.That(stuck.Tick(0.01f), Is.False);
            box.transform.position += new Vector3(1f, 0f, 0f);
            Assert.That(stuck.Tick(0.01f), Is.False);
            Assert.That(Vector2.Distance(stuck.transform.position, new Vector2(3f, 3.25f)), Is.LessThan(0.0001f), "A moving anchor carries the arrow.");
            box.enabled = false; // BreakableObject disables its colliders when broken.
            Assert.That(stuck.Tick(0.01f), Is.True);
        }

        [Test]
        public void PresentStick_IgnoresItsOwnProjectileColliders_AndOtherLayers()
        {
            BoxCollider2D own = new GameObject("OwnCollider").AddComponent<BoxCollider2D>();
            own.transform.SetParent(_root.transform, false);
            own.transform.position = new Vector3(2f, 3.5f, 0f);
            BoxCollider2D other = Own(new GameObject("StuckTestOtherLayer")).AddComponent<BoxCollider2D>();
            other.transform.position = new Vector3(2f, 3.5f, 0f);
            other.gameObject.layer = 5;
            Physics2D.SyncTransforms();
            _presenter.BeginFlight(Tag, DamageType.Physical);

            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default, 1 << 0);
            own.enabled = false;
            other.enabled = false;

            Assert.That(stuck.Tick(0.01f), Is.False, "Only a collider on the impact mask that is not the projectile's own can anchor it.");
        }

        [Test]
        public void PresentStick_ForABody_PrefersAColliderInsideTheBodyHierarchy()
        {
            Transform body = Own(new GameObject("StuckTestBody")).transform;
            body.position = new Vector3(2f, 3.5f, 0f);
            BoxCollider2D bodyBox = new GameObject("BodyCollider").AddComponent<BoxCollider2D>();
            bodyBox.transform.SetParent(body, false);
            BoxCollider2D neighbour = Own(new GameObject("StuckTestNeighbour")).AddComponent<BoxCollider2D>();
            neighbour.transform.position = new Vector3(2f, 3.5f, 0f);
            Physics2D.SyncTransforms();
            _presenter.BeginFlight(Tag, DamageType.Physical);

            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), body, new Vector2(0f, -0.5f));
            neighbour.enabled = false;
            Assert.That(stuck.Tick(0.01f), Is.False, "The overlapping neighbour must not anchor a body impact.");
            bodyBox.enabled = false;
            Assert.That(stuck.Tick(0.01f), Is.True);
        }

        // Oblique hits: the cut edge is perpendicular to the flight, so its ends must both reach the surface.
        private const float EdgeWidth = 2f;

        [Test]
        public void HeadOnHit_AddsNoExtraDepth_AndKeepsTheStraightHitPlacement()
        {
            AddWall(new Vector2(2f, 3.5f), new Vector2(10f, 1f)); // Underside at y = 3, flight +Y.
            Set(_profile, "_stickEdgeWidth", EdgeWidth);
            _presenter.BeginFlight(Tag, DamageType.Physical);

            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            var copy = stuck.GetComponent<SpriteRenderer>();
            Assert.That(Vector2.Distance(stuck.transform.position, new Vector2(2f, 3.25f)), Is.LessThan(0.0001f));
            Assert.That(copy.sprite.rect.height, Is.EqualTo(5f));
        }

        [Test]
        public void ObliqueHit_At45Degrees_SinksTheCutEdgeSoBothEndsReachTheWall()
        {
            Set(_profile, "_stickPenetration", 0f);
            AddWall(new Vector2(2f, 3.5f), new Vector2(10f, 1f)); // Underside at y = 3.
            Set(_profile, "_stickEdgeWidth", EdgeWidth);
            _root.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            Vector2 flight = _root.transform.right;
            _presenter.BeginFlight(Tag, DamageType.Physical);

            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            float depth = EdgeWidth * 0.5f * Mathf.Tan(45f * Mathf.Deg2Rad);
            Vector2 center = stuck.transform.position;
            Assert.That(Vector2.Distance(center, new Vector2(2f, 3f) + flight * depth), Is.LessThan(0.0001f));
            Vector2 across = new Vector2(-flight.y, flight.x) * (EdgeWidth * 0.5f);
            float endA = (center + across).y - 3f;
            float endB = (center - across).y - 3f;
            Assert.That(endA, Is.GreaterThanOrEqualTo(-0.001f), "One cut-edge end must not hang off the wall.");
            Assert.That(endB, Is.GreaterThanOrEqualTo(-0.001f), "The other cut-edge end must not hang off the wall.");
            Assert.That(Mathf.Min(endA, endB), Is.LessThanOrEqualTo(0.001f), "Sinking must not overshoot: one end sits on the wall.");
            Assert.That(stuck.GetComponent<SpriteRenderer>().sprite.rect.height, Is.EqualTo(6f),
                "The sunk length is kept so the outside length stays the same.");
        }

        [Test]
        public void GrazingHit_ClampsTheExtraDepthToTheEdgeWidth()
        {
            Set(_profile, "_stickPenetration", 0f);
            AddWall(new Vector2(2f, 3.5f), new Vector2(10f, 1f));
            Set(_profile, "_stickEdgeWidth", EdgeWidth);
            _root.transform.rotation = Quaternion.Euler(0f, 0f, 10f);
            Vector2 flight = _root.transform.right;
            _presenter.BeginFlight(Tag, DamageType.Physical);

            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            Assert.That(Vector2.Distance(stuck.transform.position, new Vector2(2f, 3f) + flight * EdgeWidth), Is.LessThan(0.0001f));
            Assert.That(stuck.GetComponent<SpriteRenderer>().sprite.rect.height, Is.EqualTo(5f + EdgeWidth));
        }

        [Test]
        public void ObliqueHit_WithZeroEdgeWidth_AddsNoExtraDepth()
        {
            Set(_profile, "_stickPenetration", 0f);
            AddWall(new Vector2(2f, 3.5f), new Vector2(10f, 1f));
            Set(_profile, "_stickEdgeWidth", 0f);
            _root.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            _presenter.BeginFlight(Tag, DamageType.Physical);

            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            Assert.That(Vector2.Distance(stuck.transform.position, new Vector2(2f, 3f)), Is.LessThan(0.0001f));
            Assert.That(stuck.GetComponent<SpriteRenderer>().sprite.rect.height, Is.EqualTo(5f));
        }

        [Test]
        public void ObliqueHit_WithNoSurfaceFound_AddsNoExtraDepth()
        {
            Set(_profile, "_stickPenetration", 0f);
            Set(_profile, "_stickEdgeWidth", EdgeWidth);
            _root.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            Physics2D.SyncTransforms();
            _presenter.BeginFlight(Tag, DamageType.Physical);

            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            Assert.That(Vector2.Distance(stuck.transform.position, new Vector2(2f, 3f)), Is.LessThan(0.0001f));
            Assert.That(stuck.GetComponent<SpriteRenderer>().sprite.rect.height, Is.EqualTo(5f));
        }

        [Test]
        public void ObliqueHit_IgnoresItsOwnProjectileColliderWhenFindingTheSurface()
        {
            Set(_profile, "_stickPenetration", 0f);
            Set(_profile, "_stickEdgeWidth", EdgeWidth);
            BoxCollider2D own = new GameObject("OwnCollider").AddComponent<BoxCollider2D>();
            own.transform.SetParent(_root.transform, false);
            own.size = new Vector2(2f, 2f); // Covers the ray; it is the projectile, not a surface.
            _root.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            _root.transform.position = new Vector3(2f, 3f, 0f);
            Physics2D.SyncTransforms();
            _presenter.BeginFlight(Tag, DamageType.Physical);

            StuckProjectileVisual stuck = _presenter.PresentStick(new Vector2(2f, 3f), null, default);

            Assert.That(Vector2.Distance(stuck.transform.position, new Vector2(2f, 3f)), Is.LessThan(0.0001f));
        }

        private BoxCollider2D AddWall(Vector2 center, Vector2 size)
        {
            BoxCollider2D wall = Own(new GameObject("StuckTestWall")).AddComponent<BoxCollider2D>();
            wall.transform.position = center;
            wall.size = size;
            Physics2D.SyncTransforms();
            return wall;
        }

        private StuckProjectileVisual CreateAnchored(Collider2D anchor, Transform target) =>
            StuckProjectileVisual.Create(_renderer, new Vector3(2f, 3f, 0f), Vector2.up, 0.5f, target, 4.5f, 0.5f,
                _renderer.sortingLayerID, anchor);

        private T Own<T>(T item) where T : Object { _owned.Add(item); return item; }
        private static void Set(Object target, string field, object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            Assert.That(property, Is.Not.Null, field);
            if (value is bool boolean) property.boolValue = boolean;
            else if (value is float number) property.floatValue = number;
            else if (value is string text) property.stringValue = text;
            else if (value is Renderer[] renderers) property.arraySize = renderers.Length;
            else property.objectReferenceValue = value as Object;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
