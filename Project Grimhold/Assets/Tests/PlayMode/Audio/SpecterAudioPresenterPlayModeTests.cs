#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections;
using System.Collections.Generic;
using Fusion;
using NUnit.Framework;
using ProjectGrimhold.Gameplay.Enemies.Specter;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Assert = NUnit.Framework.Assert;

namespace Tests.PlayMode.Audio
{
    /// <summary>
    /// <see cref="SpecterAudioPresenter"/> against the real Specter prefab in a real Fusion runner: each state cue plays
    /// once per transition through the pooled <see cref="AudioManager"/>, in 3D at the telegraph, and never for state
    /// that was already there when the presenter started observing.
    /// </summary>
    public sealed class SpecterAudioPresenterPlayModeTests
    {
        private const string SpecterPrefabGuid = "35da693761f9d7244bac33c1559a3918";
        private static readonly Vector2 Telegraph = new(3f, -2f);

        private readonly List<Object> _owned = new();
        private readonly AudioRunnerFixture _fixture = new();
        private SpecterStateSimulationDriver _driver;
        private SpecterAIController _specter;
        private SpecterAudioPresenter _presenter;
        private AudioManager _manager;
        private AudioClip _channel;
        private AudioClip _detonate;
        private AudioClip _reappear;

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
        public IEnumerator Spawn_IsSilent()
        {
            yield return Begin(withAudioManager: true, withCueKeys: true);

            Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty, "A freshly spawned Specter plays nothing.");
        }

        [UnityTest]
        public IEnumerator Channel_PlaysOnce_In3D_AtTheTelegraph()
        {
            yield return Begin(withAudioManager: true, withCueKeys: true);

            yield return MoveTo(SpecterStateType.Canalizacion);
            yield return AudioTestKit.WaitFrames(20);

            Assert.That(_specter.CurrentState, Is.EqualTo(SpecterStateType.Canalizacion));
            Assert.That(AudioTestKit.PlayingCount(_manager, _channel), Is.EqualTo(1), "Channel plays once, not every frame.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _detonate), Is.Zero);
            Assert.That(AudioTestKit.PlayingCount(_manager, _reappear), Is.Zero);
            AudioSource source = PlayingSource(_channel);
            Assert.That(source.spatialBlend, Is.EqualTo(1f), "World sound: 3D.");
            Assert.That(source.loop, Is.False);
            Assert.That((Vector2)source.transform.position, Is.EqualTo(Telegraph), "The warning is heard where the circle is.");
        }

        [UnityTest]
        public IEnumerator AttackAndFadeCycle_PlaysEachCueExactlyOnce()
        {
            yield return Begin(withAudioManager: true, withCueKeys: true);

            yield return MoveTo(SpecterStateType.Canalizacion);
            yield return MoveTo(SpecterStateType.Detonacion);
            yield return MoveTo(SpecterStateType.Recuperacion);
            yield return MoveTo(SpecterStateType.Desvanecimiento);
            yield return MoveTo(SpecterStateType.Desvanecido);
            yield return MoveTo(SpecterStateType.Reaparicion);
            yield return MoveTo(SpecterStateType.Persecucion);
            yield return AudioTestKit.WaitFrames(20);

            Assert.That(AudioTestKit.PlayingCount(_manager, _channel), Is.EqualTo(1));
            Assert.That(AudioTestKit.PlayingCount(_manager, _detonate), Is.EqualTo(1));
            Assert.That(AudioTestKit.PlayingCount(_manager, _reappear), Is.EqualTo(1));
            Assert.That(PlayingSource(_detonate).spatialBlend, Is.EqualTo(1f));
            Assert.That((Vector2)PlayingSource(_detonate).transform.position, Is.EqualTo(Telegraph));
        }

        [UnityTest]
        public IEnumerator ChannelCancelledAndRestarted_PlaysTheChannelCueEachTime()
        {
            yield return Begin(withAudioManager: true, withCueKeys: true);

            yield return MoveTo(SpecterStateType.Canalizacion);
            yield return MoveTo(SpecterStateType.Persecucion);
            yield return MoveTo(SpecterStateType.Canalizacion);
            yield return AudioTestKit.WaitFrames(20);

            Assert.That(AudioTestKit.PlayingCount(_manager, _channel), Is.EqualTo(2), "One cue per channel start.");
        }

        [UnityTest]
        public IEnumerator PresenterEnabledMidState_DoesNotReplayTheCurrentState_ButPlaysTheNextTransition()
        {
            yield return Begin(withAudioManager: true, withCueKeys: true);
            _presenter.enabled = false;
            yield return MoveTo(SpecterStateType.Canalizacion);

            _presenter.enabled = true;
            yield return AudioTestKit.WaitFrames(20);

            Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty, "Late joiners / restores do not replay a state entered earlier.");

            yield return MoveTo(SpecterStateType.Detonacion);
            yield return AudioTestKit.WaitFrames(20);

            Assert.That(AudioTestKit.PlayingCount(_manager, _detonate), Is.EqualTo(1));
            Assert.That(AudioTestKit.PlayingCount(_manager, _channel), Is.Zero);
        }

        [UnityTest]
        public IEnumerator MissingCueKeys_AreSilent_WithoutErrors()
        {
            yield return Begin(withAudioManager: true, withCueKeys: false);

            yield return MoveTo(SpecterStateType.Canalizacion);
            yield return MoveTo(SpecterStateType.Detonacion);
            yield return MoveTo(SpecterStateType.Reaparicion);
            yield return AudioTestKit.WaitFrames(20);

            Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty, "Optional cues without content stay silent.");
        }

        [UnityTest]
        public IEnumerator AudioManagerAppearingLate_DoesNotCatchUpCuesObservedWithoutIt()
        {
            yield return Begin(withAudioManager: false, withCueKeys: true);

            yield return MoveTo(SpecterStateType.Canalizacion);
            yield return AudioTestKit.WaitFrames(5);
            Assert.That(AudioManager.Instance, Is.Null, "Precondition: no AudioManager yet.");

            _manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            yield return AudioTestKit.WaitFrames(20);

            Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty, "A cue observed without a manager is not played late.");

            yield return MoveTo(SpecterStateType.Detonacion);
            yield return AudioTestKit.WaitFrames(10);
            Assert.That(AudioTestKit.PlayingCount(_manager, _detonate), Is.EqualTo(1));
        }

        private AudioSource PlayingSource(AudioClip clip)
        {
            foreach (AudioSource source in _manager.GetComponentsInChildren<AudioSource>())
            {
                if (source.isPlaying && source.clip == clip) return source;
            }

            Assert.Fail($"No pooled source is playing '{clip.name}'.");
            return null;
        }

        private IEnumerator MoveTo(SpecterStateType state)
        {
            _driver.Request(state);
            int frames = 300;
            while (_driver.IsRequested && frames-- > 0) yield return null;
            Assert.That(_driver.IsRequested, Is.False, "The state request was not consumed by a simulation tick.");
            // Let Render/LateUpdate present the state before the next transition.
            yield return AudioTestKit.WaitFrames(8);
        }

        private IEnumerator Begin(bool withAudioManager, bool withCueKeys)
        {
            Assume.That(AudioManager.Instance, Is.Null, "Another AudioManager is already published.");
            yield return _fixture.Start("SpecterAudioRunner", typeof(SpecterStateSimulationDriver));
            _driver = _fixture.Root.GetComponent<SpecterStateSimulationDriver>();
            _driver.AttackTargetPosition = Telegraph;

            _channel = Own(AudioTestKit.Clip("channel"));
            _detonate = Own(AudioTestKit.Clip("detonate"));
            _reappear = Own(AudioTestKit.Clip("reappear"));
            var filler = Own(AudioTestKit.Clip("filler"));
            var entries = new List<(string, AudioClip)>
            {
                (AudioKeys.TakeDamage, filler), (AudioKeys.Step, filler), (AudioKeys.Attack, filler)
            };
            if (withCueKeys)
            {
                entries.Add((AudioKeys.Channel, _channel));
                entries.Add((AudioKeys.Detonate, _detonate));
                entries.Add((AudioKeys.Reappear, _reappear));
            }

            var config = Own(AudioTestKit.Config<SpecterAudioConfig>(entries.ToArray()));
            if (withAudioManager) _manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();

            NetworkObject spawned = _fixture.Runner.Spawn(_fixture.Load(SpecterPrefabGuid), Vector3.zero, Quaternion.identity);
            _specter = spawned.GetComponent<SpecterAIController>();
            Assert.That(_specter, Is.Not.Null);
            // Keep the AI still: only the driver moves the state.
            _specter.enabled = false;
            _presenter = spawned.GetComponentInChildren<SpecterAudioPresenter>(true);
            Assert.That(_presenter, Is.Not.Null, "The Specter prefab must carry a SpecterAudioPresenter.");
            AudioTestKit.SetReference(_presenter, "_audioConfig", config);
            _driver.Target = _specter;

            // The first valid sample only seeds the baseline.
            yield return AudioTestKit.WaitFrames(5);
            Assert.That(_specter.CurrentState, Is.EqualTo(SpecterStateType.Reposo));
            if (_manager != null) Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty, "Spawning must be silent.");
        }
    }
}
#endif
