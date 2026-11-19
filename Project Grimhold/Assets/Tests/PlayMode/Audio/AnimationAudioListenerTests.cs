#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.Audio
{
    /// <summary>
    /// The animation listeners live on the Animator's child object and must delegate AnimationEvents to the presenter
    /// on the parent, which resolves the clip from its own config and plays it at its own position.
    /// </summary>
    public sealed class AnimationAudioListenerTests
    {
        private static readonly Vector3 PresenterPosition = new(4f, -2f, 0f);

        private readonly List<Object> _owned = new();
        private T Own<T>(T value) where T : Object { _owned.Add(value); return value; }

        private AudioManager _manager;
        private AudioClip _step;
        private AudioClip _custom;

        [SetUp]
        public void SetUp()
        {
            Assume.That(AudioManager.Instance, Is.Null, "Another AudioManager is already published.");
            _manager = Own(new GameObject("Audio test manager")).AddComponent<AudioManager>();
            _step = Own(AudioTestKit.Clip("step"));
            _custom = Own(AudioTestKit.Clip("custom"));
        }

        [TearDown]
        public void Cleanup()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        [Test]
        public void PlayerListener_PlayFootstep_DelegatesToParentPresenterAndPlaysStep()
        {
            PlayerAnimationAudioListener listener = CreatePlayer(withPresenter: true);

            listener.PlayFootstep();

            Assert.That(AudioTestKit.PlayingCount(_manager, _step), Is.EqualTo(1), "PlayFootstep plays the Step clip.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _custom), Is.Zero);
            Assert.That(PlayingSource(_step).transform.position, Is.EqualTo(PresenterPosition),
                "The presenter on the parent object owns the playback position.");
        }

        [Test]
        public void PlayerListener_PlayAudioEvent_DelegatesTheKeyToParentPresenter()
        {
            PlayerAnimationAudioListener listener = CreatePlayer(withPresenter: true);

            listener.PlayAudioEvent("TakeDamage");

            Assert.That(AudioTestKit.PlayingCount(_manager, _custom), Is.EqualTo(1));
            Assert.That(AudioTestKit.PlayingCount(_manager, _step), Is.Zero);

            listener.PlayAudioEvent("NoSuchKey");
            Assert.That(AudioTestKit.ClipsUsedBy(_manager), Has.Count.EqualTo(1), "An unconfigured key stays silent.");
        }

        [Test]
        public void PlayerListener_WithoutPresenter_IsSilent()
        {
            PlayerAnimationAudioListener listener = CreatePlayer(withPresenter: false);

            Assert.DoesNotThrow(() => listener.PlayFootstep());
            Assert.DoesNotThrow(() => listener.PlayAudioEvent("TakeDamage"));

            Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty);
        }

        [Test]
        public void EnemyListener_PlayFootstep_DelegatesToParentPresenterAndPlaysStep()
        {
            EnemyAnimationAudioListener listener = CreateEnemy(withPresenter: true);

            listener.PlayFootstep();

            Assert.That(AudioTestKit.PlayingCount(_manager, _step), Is.EqualTo(1), "PlayFootstep plays the Step clip.");
            Assert.That(AudioTestKit.PlayingCount(_manager, _custom), Is.Zero);
            Assert.That(PlayingSource(_step).transform.position, Is.EqualTo(PresenterPosition),
                "The presenter on the parent object owns the playback position.");
        }

        [Test]
        public void EnemyListener_PlayAudioEvent_DelegatesTheKeyToParentPresenter()
        {
            EnemyAnimationAudioListener listener = CreateEnemy(withPresenter: true);

            listener.PlayAudioEvent("Attack");

            Assert.That(AudioTestKit.PlayingCount(_manager, _custom), Is.EqualTo(1));
            Assert.That(AudioTestKit.PlayingCount(_manager, _step), Is.Zero);

            listener.PlayAudioEvent("NoSuchKey");
            Assert.That(AudioTestKit.ClipsUsedBy(_manager), Has.Count.EqualTo(1), "An unconfigured key stays silent.");
        }

        [Test]
        public void EnemyListener_WithoutPresenter_IsSilent()
        {
            EnemyAnimationAudioListener listener = CreateEnemy(withPresenter: false);

            Assert.DoesNotThrow(() => listener.PlayFootstep());
            Assert.DoesNotThrow(() => listener.PlayAudioEvent("Attack"));

            Assert.That(AudioTestKit.ClipsUsedBy(_manager), Is.Empty);
        }

        private PlayerAnimationAudioListener CreatePlayer(bool withPresenter)
        {
            GameObject root = Own(new GameObject("Player audio root"));
            root.transform.position = PresenterPosition;
            if (withPresenter)
            {
                var presenter = root.AddComponent<PlayerAudioPresenter>();
                AudioTestKit.SetReference(presenter, "_audioConfig", Own(AudioTestKit.Config<PlayerAudioConfig>(
                    (AudioKeys.Step, _step), (AudioKeys.TakeDamage, _custom))));
            }

            var animatorChild = new GameObject("Animator child");
            animatorChild.transform.SetParent(root.transform, false);
            return animatorChild.AddComponent<PlayerAnimationAudioListener>();
        }

        private EnemyAnimationAudioListener CreateEnemy(bool withPresenter)
        {
            GameObject root = Own(new GameObject("Enemy audio root"));
            root.transform.position = PresenterPosition;
            if (withPresenter)
            {
                var presenter = root.AddComponent<EnemyAudioPresenter>();
                AudioTestKit.SetReference(presenter, "_audioConfig", Own(AudioTestKit.Config<EnemyAudioConfig>(
                    (AudioKeys.Step, _step), (AudioKeys.Attack, _custom))));
            }

            var animatorChild = new GameObject("Animator child");
            animatorChild.transform.SetParent(root.transform, false);
            return animatorChild.AddComponent<EnemyAnimationAudioListener>();
        }

        private AudioSource PlayingSource(AudioClip clip)
        {
            foreach (AudioSource source in _manager.GetComponentsInChildren<AudioSource>())
            {
                if (source.isPlaying && source.clip == clip) return source;
            }

            Assert.Fail($"No source is playing '{clip.name}'.");
            return null;
        }
    }
}
#endif
