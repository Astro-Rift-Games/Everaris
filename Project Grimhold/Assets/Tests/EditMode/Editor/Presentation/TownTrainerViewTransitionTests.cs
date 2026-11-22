#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Presentation
{
    /// <summary>
    /// The Trainer panel is built at runtime, so it must add the shared open/close transition itself; every other
    /// Town panel carries it on its prefab.
    /// </summary>
    public sealed class TownTrainerViewTransitionTests
    {
        private TownTrainerView _view;

        [SetUp]
        public void SetUp()
        {
            _view = TownTrainerView.Create(null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_view != null)
            {
                Object.DestroyImmediate(_view.gameObject);
            }
        }

        [Test]
        public void RuntimeBuiltPanel_CarriesTheSharedPanelTransition()
        {
            Transform panel = _view.transform.Find("TrainerPanel");
            Assert.That(panel, Is.Not.Null, "TrainerPanel");

            Assert.That(panel.GetComponent<UiPanelTransition>(), Is.Not.Null);
            Assert.That(panel.GetComponent<CanvasGroup>(), Is.Not.Null);
        }

        [Test]
        public void RuntimeBuiltPanel_StartsClosedAndStillOpensAndCloses()
        {
            Assert.That(_view.IsOpen, Is.False);

            _view.Open();
            Assert.That(_view.IsOpen, Is.True);

            _view.Close();
            Assert.That(_view.IsOpen, Is.False);
        }
    }
}
#endif
