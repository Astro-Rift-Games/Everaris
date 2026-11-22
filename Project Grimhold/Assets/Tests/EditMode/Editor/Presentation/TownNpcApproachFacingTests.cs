#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Presentation
{
    /// <summary>A Town NPC acknowledges the local player while they stand in interaction range.</summary>
    public sealed class TownNpcApproachFacingTests
    {
        private TownNpcApproachFacing _facing;
        private TownNpcDirectionalView _merchant;
        private TownNpcDirectionalView _mentor;

        [SetUp]
        public void SetUp()
        {
            _facing = new TownNpcApproachFacing();
            _merchant = CreateNpc("Merchant", Vector3.zero);
            _mentor = CreateNpc("Mentor", new Vector3(10f, 0f, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            DestroyNpc(_merchant);
            DestroyNpc(_mentor);
        }

        [Test]
        public void Track_TurnsTheNpcTowardsThePlayer()
        {
            _facing.Track(_merchant, new Vector3(0f, 3f, 0f));

            Assert.That(_merchant.CurrentFacing, Is.EqualTo(CharacterVisualDirection.North));
        }

        [Test]
        public void Track_KeepsFollowingThePlayerWhileTheyStayInRange()
        {
            _facing.Track(_merchant, new Vector3(0f, -3f, 0f));

            _facing.Track(_merchant, new Vector3(0f, 3f, 0f));

            Assert.That(_merchant.CurrentFacing, Is.EqualTo(CharacterVisualDirection.North));
        }

        [Test]
        public void Track_WithoutNpc_RestoresTheInitialFacing()
        {
            _facing.Track(_merchant, new Vector3(0f, 3f, 0f));

            _facing.Track(null, new Vector3(0f, 30f, 0f));

            Assert.That(_merchant.CurrentFacing, Is.EqualTo(_merchant.InitialFacing));
        }

        [Test]
        public void Track_AnotherNpc_RestoresThePreviousOneAndTurnsTheNewOne()
        {
            _facing.Track(_merchant, new Vector3(0f, 3f, 0f));

            _facing.Track(_mentor, new Vector3(10f, 3f, 0f));

            Assert.That(_merchant.CurrentFacing, Is.EqualTo(_merchant.InitialFacing));
            Assert.That(_mentor.CurrentFacing, Is.EqualTo(CharacterVisualDirection.North));
        }

        [Test]
        public void Release_RestoresTheInitialFacing()
        {
            _facing.Track(_merchant, new Vector3(0f, 3f, 0f));

            _facing.Release();

            Assert.That(_merchant.CurrentFacing, Is.EqualTo(_merchant.InitialFacing));
        }

        [Test]
        public void Track_AfterTheNpcWasDestroyed_DoesNotThrow()
        {
            _facing.Track(_merchant, new Vector3(0f, 3f, 0f));
            DestroyNpc(_merchant);

            Assert.DoesNotThrow(() => _facing.Track(_mentor, new Vector3(10f, 3f, 0f)));
            Assert.DoesNotThrow(() => _facing.Release());
        }

        [Test]
        public void SocialPlayerPrefab_DrivesApproachFacingFromItsLocalInteractionCandidate()
        {
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/SocialPlayer.prefab");
            Assert.That(prefab, Is.Not.Null, "SocialPlayer prefab");

            TownNpcApproachFacingPresenter presenter = prefab.GetComponent<TownNpcApproachFacingPresenter>();

            Assert.That(presenter, Is.Not.Null, "The Town avatar must carry the approach facing presenter.");
            Assert.That(
                presenter.GetComponent<LocalInteractionCandidateSource>(),
                Is.Not.Null,
                "It reads the candidate source on the same GameObject.");
        }

        private static TownNpcDirectionalView CreateNpc(string name, Vector3 position)
        {
            var npc = new GameObject(name);
            npc.transform.position = position;
            return npc.AddComponent<TownNpcDirectionalView>();
        }

        private static void DestroyNpc(TownNpcDirectionalView npc)
        {
            if (npc != null)
            {
                Object.DestroyImmediate(npc.gameObject);
            }
        }
    }
}
#endif
