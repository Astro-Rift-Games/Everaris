#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace Tests.EditMode.Player.Presentation
{
    public sealed class WindupChargeWindowTests
    {
        [TestCase(-.01f, 1f, false, 0f)]
        [TestCase(0f, 1f, true, 0f)]
        [TestCase(.5f, 1f, true, .5f)]
        [TestCase(.999f, 1f, true, .999f)]
        [TestCase(1f, 1f, false, 0f)]
        [TestCase(2f, 1f, false, 0f)]
        [TestCase(0f, 0f, false, 0f)]
        [TestCase(float.NaN, 1f, false, 0f)]
        public void Resolve_UsesOnlyTheRemainingConfirmedWindow(float seconds, float release, bool visible, float progress)
        {
            Assert.That(WindupChargeWindow.TryResolve(seconds, release, out float actual), Is.EqualTo(visible));
            Assert.That(actual, Is.EqualTo(progress).Within(.0001f));
        }
    }
}
#endif
