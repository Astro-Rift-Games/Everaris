#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Assert = NUnit.Framework.Assert;

namespace Tests.PlayMode.Audio
{
    /// <summary>
    /// Weapon impact audio for observers, end to end on real player avatars: the controller's replicated snapshot and
    /// Render event feed <see cref="WeaponAudioPresenter"/>. A Single-mode runner that spawns an avatar without Input
    /// Authority is State Authority of an avatar it does not drive (the host that is not the attacker); the replicated
    /// proxy of a remote client, the AOI/late-join path and the real network are only provable host + client.
    /// </summary>
    public sealed class ObservedImpactAudioTests
    {
        private const string PlayerPrefabGuid = "fea3a7b256f965a4eb9b965832939741";

        private readonly List<Object> _owned = new();
        private readonly AudioRunnerFixture _fixture = new();
        private AudioManager _manager;
        private AudioClip _blockClip;
        private AudioClip _attackClip;

        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return _fixture.Shutdown();
            for (int i = _owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            _manager = null;
        }

        [UnityTest]
        public IEnumerator Observer_HearsEachImpactOnce_AsAWorldSoundAtTheVictim()
        {
            yield return Begin();
            PlayerCharacter attacker = Spawn(withInputAuthority: false, Vector3.zero, out PlayerCombatNetworkController combat);
            PlayerCharacter victim = Spawn(withInputAuthority: false, new Vector3(30f, 0f, 0f), out _);
            yield return AudioTestKit.WaitFrames(5);
            Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty, "Spawning must be silent.");

            // Scenery (unknown target) selects Block at the replicated point.
            Record(combat, attacker.Id, new EntityId(9999), new Vector2(3f, 4f));
            yield return WaitUntilPlaying(_blockClip, 1);
            yield return AudioTestKit.WaitFrames(20);
            Assert.That(AudioTestKit.PlayingCount(_manager, _blockClip), Is.EqualTo(1), "One impact is one cue, not one per Render.");
            AudioSource block = PlayingSource(_blockClip);
            Assert.That(block.transform.position, Is.EqualTo(new Vector3(3f, 4f, block.transform.position.z)));
            Assert.That(block.spatialBlend, Is.EqualTo(1f), "Observed impacts are 3D world sounds.");

            // A character victim selects Attack (config has Attack and Block).
            Record(combat, attacker.Id, victim.Id, new Vector2(30f, 0f));
            yield return WaitUntilPlaying(_attackClip, 1);
            yield return AudioTestKit.WaitFrames(20);
            Assert.That(AudioTestKit.PlayingCount(_manager, _attackClip), Is.EqualTo(1));
            Assert.That(AudioTestKit.PlayingCount(_manager, _blockClip), Is.EqualTo(1), "The earlier impact does not replay.");
        }

        [UnityTest]
        public IEnumerator Attacker_HearsTheImpactExactlyOnce_ThroughTheFeedbackPathOnly()
        {
            yield return Begin();
            PlayerCharacter attacker = Spawn(withInputAuthority: true, Vector3.zero, out PlayerCombatNetworkController combat);
            Assert.That(combat.HasInputAuthority, Is.True);
            int observed = 0;
            combat.ObservedImpactPresented += _ => observed++;
            yield return AudioTestKit.WaitFrames(5);

            Record(combat, attacker.Id, new EntityId(9999), new Vector2(3f, 4f));
            yield return WaitUntilPlaying(_blockClip, 1);
            yield return AudioTestKit.WaitFrames(30);

            Assert.That(AudioTestKit.PlayingCount(_manager, _blockClip), Is.EqualTo(1), "The attacker never hears the impact twice.");
            Assert.That(observed, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Observer_SnapshotExistingAtSpawn_IsSilent_AndTheNextImpactPlaysOnce()
        {
            yield return Begin();
            // State present before Spawned: late join, or a host-migration restore (CopyStateFrom before Spawned).
            NetworkObject restored = _fixture.Runner.Spawn(_fixture.Load(PlayerPrefabGuid), Vector3.zero, Quaternion.identity,
                PlayerRef.None, onBeforeSpawned: (_, instance) =>
                {
                    var controller = instance.GetComponent<PlayerCombatNetworkController>();
                    PropertyInfo snapshot = typeof(PlayerCombatNetworkController).GetProperty("LastObservedImpact",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    PropertyInfo sequence = typeof(PlayerCombatNetworkController).GetProperty("CombatFeedbackSequence",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(snapshot, Is.Not.Null);
                    Assert.That(sequence, Is.Not.Null);
                    snapshot.SetValue(controller,
                        new ObservedImpactSnapshot { Sequence = 7, TargetIdValue = 9999, HitPoint = new Vector2(1f, 1f) });
                    sequence.SetValue(controller, 7); // A real restore copies both: the next impact is sequence 8.
                });
            Configure(restored);
            var combat = restored.GetComponent<PlayerCombatNetworkController>();
            Assert.That(combat.CurrentObservedImpact.Sequence, Is.EqualTo(7), "Precondition: Spawned kept the restored state.");

            yield return AudioTestKit.WaitFrames(30);
            Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty, "A snapshot that predates Spawned is never replayed.");

            Record(combat, restored.GetComponent<PlayerCharacter>().Id, new EntityId(9999), new Vector2(2f, 2f));
            yield return WaitUntilPlaying(_blockClip, 1);
            yield return AudioTestKit.WaitFrames(20);
            Assert.That(AudioTestKit.PlayingCount(_manager, _blockClip), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Observer_ImpactsInTheSameTickCoalesceIntoOneCue()
        {
            yield return Begin();
            PlayerCharacter attacker = Spawn(withInputAuthority: false, Vector3.zero, out PlayerCombatNetworkController combat);
            yield return AudioTestKit.WaitFrames(5);

            // Accepted limitation: one snapshot per avatar, so impacts recorded before the next Render are one cue (the newest).
            Record(combat, attacker.Id, new EntityId(9999), new Vector2(1f, 1f));
            Record(combat, attacker.Id, new EntityId(9998), new Vector2(2f, 2f));
            yield return WaitUntilPlaying(_blockClip, 1);
            yield return AudioTestKit.WaitFrames(30);

            Assert.That(AudioTestKit.PlayingCount(_manager, _blockClip), Is.EqualTo(1));
            Assert.That(PlayingSource(_blockClip).transform.position.x, Is.EqualTo(2f), "The newest impact wins.");
        }

        private IEnumerator Begin()
        {
            Assume.That(AudioManager.Instance, Is.Null, "Another AudioManager is already published.");
            yield return _fixture.Start("ObservedImpactAudioRunner");
            _blockClip = Own(AudioTestKit.Clip("block"));
            _attackClip = Own(AudioTestKit.Clip("attack"));
            _manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
        }

        private PlayerCharacter Spawn(bool withInputAuthority, Vector3 position, out PlayerCombatNetworkController combat)
        {
            NetworkObject player = _fixture.Runner.Spawn(_fixture.Load(PlayerPrefabGuid), position, Quaternion.identity,
                withInputAuthority ? _fixture.Runner.LocalPlayer : PlayerRef.None);
            Configure(player);
            combat = player.GetComponent<PlayerCombatNetworkController>();
            return player.GetComponent<PlayerCharacter>();
        }

        private void Configure(NetworkObject player)
        {
            var presenter = player.GetComponentInChildren<WeaponAudioPresenter>(true);
            Assert.That(presenter, Is.Not.Null, "The player prefab must carry a WeaponAudioPresenter.");
            AudioTestKit.SetReference(presenter, "_fallbackAudioConfig",
                Own(AudioTestKit.Config<WeaponAudioConfig>((AudioKeys.Block, _blockClip), (AudioKeys.Attack, _attackClip))));
        }

        private void Record(PlayerCombatNetworkController combat, EntityId attacker, EntityId target, Vector2 hitPoint)
        {
            var request = new DamageRequest(attacker, target, 7f, DamageType.Physical, Vector2.right, hitPoint,
                _fixture.Runner.Tick);
            combat.RecordResolvedDamage(new DamageResolvedEvent(request,
                new DamageResult(request.TargetId, true, 7f, 1f, false, default)));
        }

        private IEnumerator WaitUntilPlaying(AudioClip clip, int count)
        {
            int frames = 300;
            while (AudioTestKit.PlayingCount(_manager, clip) < count && frames-- > 0) yield return null;
            Assert.That(AudioTestKit.PlayingCount(_manager, clip), Is.GreaterThanOrEqualTo(count), $"'{clip.name}' never played.");
        }

        private AudioSource PlayingSource(AudioClip clip)
        {
            foreach (AudioSource source in _manager.GetComponentsInChildren<AudioSource>())
                if (source.isPlaying && source.clip == clip) return source;
            Assert.Fail($"No source is playing '{clip.name}'.");
            return null;
        }
    }
}
#endif
