#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Assert = NUnit.Framework.Assert;
using LogType = UnityEngine.LogType;

namespace Tests.PlayMode.Combat
{
    public sealed class ProjectilePresentationPlayModeTests
    {
        private NetworkRunner _runner;
        private readonly List<GameObject> _owned = new();
        private readonly List<ScriptableObject> _configs = new();
        private const string ProjectileGuid = "5cf65ecb3f382c147875a551556fad82";

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_runner != null)
            {
                var shutdown = _runner.Shutdown();
                while (!shutdown.IsCompleted) yield return null;
                if (_runner != null) Object.Destroy(_runner.gameObject);
            }
            foreach (GameObject item in _owned) if (item != null) Object.Destroy(item);
            _owned.Clear();
            foreach (ScriptableObject config in _configs) if (config != null) Object.Destroy(config);
            _configs.Clear();
            foreach (ParticleSystem item in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (item.name.StartsWith("ExpiryTestFizzle")) Object.Destroy(item.gameObject);
            yield return null;
        }

        [UnityTest] public IEnumerator LifetimeExpiry_RaisesOnlyExpiredOnce() => AssertExpiry(0.15f, 100f);
        [UnityTest] public IEnumerator RangeExpiry_RaisesOnlyExpiredOnce() => AssertExpiry(10f, 0.2f);

        private IEnumerator AssertExpiry(float lifetime, float range)
        {
            yield return Begin();
            NetworkProjectile projectile = Spawn(lifetime, range);
            int expired = 0, impacts = 0;
            Vector2 position = default, direction = default;
            SubscribeExpired(projectile, (p, d) => { expired++; position = p; direction = d; });
            projectile.ImpactResolved += (_, _) => impacts++;
            yield return Until(() => expired != 0);
            yield return null;
            Assert.That(expired, Is.EqualTo(1));
            Assert.That(impacts, Is.Zero);
            Assert.That(direction, Is.EqualTo(Vector2.right));
            Assert.That(position.x, Is.InRange(0f, range));
        }

        [UnityTest]
        public IEnumerator Impact_RaisesOnlyImpact()
        {
            yield return Begin();
            GameObject wall = Own(new GameObject("ProjectilePresentationWall"));
            wall.layer = LayerMask.NameToLayer("WorldCollision");
            wall.transform.position = Vector3.right;
            wall.AddComponent<BoxCollider2D>();
            NetworkProjectile projectile = Spawn(10f, 10f);
            int expired = 0, impacts = 0;
            SubscribeExpired(projectile, (_, _) => expired++);
            projectile.ImpactResolved += (_, geometry) => { Assert.That(geometry, Is.True); impacts++; };
            yield return Until(() => impacts != 0);
            Assert.That(impacts, Is.EqualTo(1));
            Assert.That(expired, Is.Zero);
        }

        [UnityTest]
        public IEnumerator GeometryImpact_ExposesNoTargetAndNoInterception()
        {
            yield return Begin();
            GameObject wall = Own(new GameObject("ProjectilePresentationWall"));
            wall.layer = LayerMask.NameToLayer("WorldCollision");
            wall.transform.position = Vector3.right;
            wall.AddComponent<BoxCollider2D>();
            NetworkProjectile projectile = Spawn(10f, 10f);
            int impacts = 0;
            bool hasTarget = true, intercepted = true, hasSurface = false;
            Vector2 surface = default;
            projectile.ImpactResolved += (_, _) =>
            {
                impacts++;
                hasTarget = projectile.TryGetImpactTarget(out _, out _);
                intercepted = projectile.WasImpactIntercepted;
                hasSurface = projectile.TryGetImpactSurfacePoint(out surface);
            };
            yield return Until(() => impacts != 0);
            Assert.That(hasTarget, Is.False);
            Assert.That(intercepted, Is.False);
            Assert.That(hasSurface, Is.True);
            Assert.That(surface.x, Is.EqualTo(0.5f).Within(0.005f), "The surface point is the wall face, not the projectile center.");
        }

        [UnityTest]
        public IEnumerator BodyImpact_ExposesHitBodyAndReplicatedOffset()
        {
            yield return Begin();
            GameObject body = Own(new GameObject("ProjectilePresentationBody"));
            body.layer = LayerMask.NameToLayer("WorldCollision");
            body.transform.position = new Vector3(1f, 0f, 0f);
            var collider = body.AddComponent<BoxCollider2D>();
            var damageable = body.AddComponent<StuckTargetDamageable>();
            damageable.Id = new EntityId(7);
            Assert.That(_runner.GetComponent<EntityRegistry>().TryRegister(damageable.Id, damageable, new[] { collider }), Is.True);
            Physics2D.SyncTransforms();
            NetworkProjectile projectile = Spawn(10f, 10f);
            int impacts = 0;
            bool geometryImpact = true, hasTarget = false, intercepted = true;
            Transform target = null;
            Vector2 offset = default, surface = default;
            bool hasSurface = false;
            projectile.ImpactResolved += (_, geometry) =>
            {
                impacts++;
                geometryImpact = geometry;
                hasTarget = projectile.TryGetImpactTarget(out target, out offset);
                intercepted = projectile.WasImpactIntercepted;
                hasSurface = projectile.TryGetImpactSurfacePoint(out surface);
            };
            yield return Until(() => impacts != 0);
            Assert.That(geometryImpact, Is.False);
            Assert.That(intercepted, Is.False);
            Assert.That(hasTarget, Is.True);
            Assert.That(target, Is.SameAs(body.transform));
            Assert.That(hasSurface, Is.True);
            Assert.That(surface.x, Is.EqualTo(0.5f).Within(0.005f));
            Assert.That(Vector2.Distance(offset, surface - new Vector2(1f, 0f)), Is.LessThan(0.0001f),
                "The body offset is taken from the contact surface point, not the projectile center.");
        }

        private sealed class StuckTargetDamageable : MonoBehaviour, IDamageable
        {
            public EntityId Id { get; set; }
            public bool CanReceiveDamage => true;
            public DamageResult ApplyDamage(in DamageRequest request) => default;
        }

        [UnityTest] public IEnumerator Shutdown_RaisesNeitherEvent() => AssertShutdown(ShutdownReason.Ok);
        [UnityTest] public IEnumerator MigrationShutdown_RaisesNeitherEvent() => AssertShutdown(ShutdownReason.HostMigration);
        private IEnumerator AssertShutdown(ShutdownReason reason)
        {
            yield return Begin();
            NetworkProjectile projectile = Spawn(10f, 100f);
            int expired = 0, impacts = 0;
            SubscribeExpired(projectile, (_, _) => expired++);
            projectile.ImpactResolved += (_, _) => impacts++;
            var shutdown = _runner.Shutdown(destroyGameObject: false, shutdownReason: reason);
            while (!shutdown.IsCompleted) yield return null;
            Assert.That(expired, Is.Zero);
            Assert.That(impacts, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ExternalDespawn_IsNotExpiry()
        {
            yield return Begin();
            NetworkProjectile projectile = Spawn(10f, 100f);
            int expired = 0, impacts = 0;
            SubscribeExpired(projectile, (_, _) => expired++);
            projectile.ImpactResolved += (_, _) => impacts++;
            _runner.Despawn(projectile.Object);
            yield return null;
            Assert.That(expired, Is.Zero);
            Assert.That(impacts, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Fizzle_OnlyOnExpiry_AndNullIsSilent()
        {
            yield return Begin();
            GameObject template = Own(new GameObject("ExpiryTestFizzle"));
            ParticleSystem particles = template.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = 0.1f;
            main.playOnAwake = false;
            NetworkProjectile projectile = Spawn(0.25f, 100f);
            var presenter = projectile.gameObject.AddComponent<ProjectileExpiryParticlePresenter>();
            Set(presenter, "_fizzleParticles", particles);
            Assert.That(CountFizzles(), Is.Zero);
            yield return Until(() => CountFizzles() == 1);
            yield return new WaitForSeconds(0.5f);
            Assert.That(CountFizzles(), Is.Zero, "Fizzle must clean itself up.");
            // A missing optional prefab is silent: capture only this projectile's lifecycle,
            // not the runner's unrelated startup diagnostics.
            var logs = new List<string>();
            void Capture(string message, string stack, LogType logType)
            { if (logType != LogType.Log) logs.Add(message); }
            Application.logMessageReceived += Capture;
            try
            {
                NetworkProjectile noPrefab = Spawn(0.15f, 100f);
                noPrefab.gameObject.AddComponent<ProjectileExpiryParticlePresenter>();
                NetworkObject flight = noPrefab.Object;
                yield return Until(() => flight == null || !flight.IsValid);
                Assert.That(CountFizzles(), Is.Zero);
                Assert.That(logs, Is.Empty);
            }
            finally { Application.logMessageReceived -= Capture; }
        }

        [UnityTest]
        public IEnumerator Trail_DetachesStopsAndDestroys_AndNullIsSafe()
        {
            yield return Begin();
            GameObject root = Own(new GameObject("TrailTest"));
            var detacher = root.AddComponent<ProjectileTrailDetacher>();
            GameObject trailObject = Own(new GameObject("Trail"));
            trailObject.transform.SetParent(root.transform);
            TrailRenderer trail = trailObject.AddComponent<TrailRenderer>();
            trail.time = 0.1f;
            GameObject particleObject = Own(new GameObject("Particles"));
            particleObject.transform.SetParent(root.transform);
            ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.startLifetime = 0.2f;
            particles.Play();
            Set(detacher, "_trails", new[] { trail });
            Set(detacher, "_particles", new[] { particles });
            detacher.Despawned(_runner, true);
            Assert.That(trail.transform.parent, Is.Null);
            Assert.That(particles.transform.parent, Is.Null);
            Assert.That(trail.emitting, Is.False);
            Assert.That(particles.isEmitting, Is.False);
            Object.Destroy(root);
            yield return new WaitForSeconds(0.4f);
            Assert.That(trail == null, Is.True);
            Assert.That(particles == null, Is.True);
            var empty = Own(new GameObject("EmptyTrail")).AddComponent<ProjectileTrailDetacher>();
            Assert.DoesNotThrow(() => empty.Despawned(_runner, false));
        }

        [UnityTest]
        public IEnumerator TaggedProfile_SpawnAppliesTint_ExpirySupersedesDefaultFizzle()
        {
            yield return Begin();
            var profile = Config<ProjectileVisualProfile>();
            var weapon = Config<WeaponDefinition>();
            var loot = Config<LootDefinition>();
            var catalog = Config<LootDefinitionCatalog>();
            Set(loot, "_id", "projectile-profile-test");
            Set(loot, "_weaponDefinition", weapon);
            Set(catalog, "_definitions", new List<LootDefinition> { loot });
            Set(weapon, "_projectileVisual", profile);
            Set(profile, "_overrideTint", true);
            Set(profile, "_tint", Color.magenta);
            var equipment = Own(new GameObject("ProfileEquipment")).AddComponent<PlayerWeaponEquipmentNetworkController>();
            Set(equipment, "_lootCatalog", catalog);
            ParticleSystem fizzle = Own(new GameObject("ExpiryTestFizzle")).AddComponent<ParticleSystem>();
            fizzle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = fizzle.main;
            main.playOnAwake = false;
            main.duration = .1f;
            main.startLifetime = .1f;
            Set(profile, "_fizzleParticles", fizzle);
            var tag = new WeaponAttackTag(EquipmentSlot.WeaponSetAMainHand, 1, 1, AttackType.Ranged);
            NetworkProjectile projectile = Spawn(.3f, 100f, instance =>
            {
                var presenter = instance.AddComponent<ProjectileVisualProfilePresenter>();
                Set(presenter, "_equipmentSource", equipment);
                Set(presenter, "_spriteRenderer", instance.GetComponentInChildren<SpriteRenderer>());
                Set(presenter, "_animator", instance.GetComponentInChildren<Animator>());
                var defaults = instance.AddComponent<ProjectileExpiryParticlePresenter>();
                Set(defaults, "_fizzleParticles", fizzle);
            }, tag);
            Assert.That(projectile.GetComponentInChildren<SpriteRenderer>().color, Is.EqualTo(Color.magenta));
            Assert.That(CountFizzles(), Is.Zero);
            yield return Until(() => CountFizzles() > 0);
            Assert.That(CountFizzles(), Is.EqualTo(1), "The profile must supersede the prefab fizzle.");
            yield return new WaitForSeconds(.5f);
            Assert.That(CountFizzles(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator RegisteredProfileTrail_DetachesRoot_AndWaitsForLongestChild()
        {
            yield return Begin();
            GameObject root = Own(new GameObject("ProfileTrailRoot"));
            var detacher = root.AddComponent<ProjectileTrailDetacher>();
            GameObject tail = Own(new GameObject("RegisteredTail"));
            tail.transform.SetParent(root.transform);
            var trail = tail.AddComponent<TrailRenderer>();
            trail.time = .1f;
            GameObject child = new GameObject("LongerParticles");
            child.transform.SetParent(tail.transform);
            var particles = child.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.startLifetime = .4f;
            particles.Emit(1);
            detacher.Register(tail);
            detacher.Register(tail);
            detacher.Despawned(_runner, true);
            Assert.That(tail.transform.parent, Is.Null);
            Assert.That(trail.emitting, Is.False);
            Assert.That(particles.isEmitting, Is.False);
            yield return new WaitForSeconds(.15f);
            Assert.That(tail != null, Is.True, "The short trail must not destroy longer-lived child particles.");
            yield return new WaitForSeconds(.5f);
            Assert.That(tail == null, Is.True);
        }

        [UnityTest]
        public IEnumerator ProjectileLifecycle_EmitsNoCombatTrace()
        {
            yield return Begin();
            var traces = new List<string>();
            void Capture(string message, string stack, LogType type)
            { if (message.Contains("[CombatTrace]")) traces.Add(message); }
            Application.logMessageReceived += Capture;
            try
            {
                NetworkProjectile projectile = Spawn(0.15f, 100f);
                NetworkObject flight = projectile.Object;
                yield return Until(() => flight == null || !flight.IsValid);
                Assert.That(traces, Is.Empty);
            }
            finally { Application.logMessageReceived -= Capture; }
        }

        private IEnumerator Begin()
        {
            GameObject root = new("ProjectilePresentationRunner");
            _runner = root.AddComponent<NetworkRunner>();
            root.AddComponent<EntityRegistry>();
            var start = _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single, SessionName = $"projectile-presentation-{Guid.NewGuid():N}",
                SceneManager = root.AddComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = root.AddComponent<NetworkObjectProviderDefault>()
            });
            while (!start.IsCompleted) yield return null;
            Assert.That(start.Result.Ok, Is.True);
        }
        private NetworkProjectile Spawn(float lifetime, float range, Action<GameObject> configure = null,
            WeaponAttackTag tag = default)
        {
            var prefab = _runner.Config.PrefabTable.Load(
                _runner.Config.PrefabTable.GetId(NetworkObjectGuid.Parse(ProjectileGuid)), true);
            return _runner.Spawn(prefab, Vector3.zero, Quaternion.identity, onBeforeSpawned: (_, instance) =>
            {
                var request = new ProjectileSpawnRequest(new EntityId(1), Vector2.zero, Vector2.right,
                    1f, DamageType.Physical, 2f, lifetime, range, _runner.Tick.Raw, weaponTag: tag);
                configure?.Invoke(instance.gameObject);
                instance.GetComponent<NetworkProjectile>().InitializeNetworkState(request, LayerMask.GetMask("WorldCollision"));
            }).GetComponent<NetworkProjectile>();
        }
        private static void SubscribeExpired(NetworkProjectile projectile, Action<Vector2, Vector2> callback)
        {
            projectile.Expired += callback;
        }
        // Emulates Inspector wiring of private serialized references.
        private static void Set(object target, string field, object value) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static int CountFizzles()
        {
            int count = 0;
            foreach (ParticleSystem item in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (item.name == "ExpiryTestFizzle(Clone)") count++;
            return count;
        }
        private GameObject Own(GameObject item) { _owned.Add(item); return item; }
        private T Config<T>() where T : ScriptableObject
        { var config = ScriptableObject.CreateInstance<T>(); _configs.Add(config); return config; }
        private static IEnumerator Until(Func<bool> predicate)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(predicate(), Is.True, "Projectile lifecycle deadline exceeded.");
        }
    }
}
#endif
