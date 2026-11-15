#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

public sealed class TownRaidTransitionFailurePresenterTests
{
    [Test]
    public void BoundView_ConsumesFailuresArrivingLater_ExactlyOnce()
    {
        var root = new GameObject(nameof(TownRaidTransitionFailurePresenterTests));
        var previous = SessionConnectionCoordinator.Instance;
        var instance = typeof(SessionConnectionCoordinator).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        try
        {
            var coordinator = root.AddComponent<SessionConnectionCoordinator>();
            instance.SetValue(null, coordinator);
            var presenter = root.AddComponent<TownRaidPreparationPresenter>();
            var view = TownRaidPreparationView.CreateEditorSource(root.transform);
            typeof(TownRaidPreparationPresenter).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(presenter, view);
            var consume = typeof(TownRaidPreparationPresenter).GetMethod(
                "ConsumeTransitionFailure", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(consume, Is.Not.Null, "Continuous presentation feedback seam is required.");
            consume.Invoke(presenter, null);
            typeof(SessionConnectionCoordinator).GetField("_pendingTransitionFailure", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(coordinator, SessionTransitionResult.RecoveryFailed);

            consume.Invoke(presenter, null);

            var status = view.transform.Find("RaidCodePanel/Status").GetComponent<TMP_Text>();
            SessionTransitionFailureText.TryGetMessage(SessionTransitionResult.RecoveryFailed, out string expected);
            Assert.That(status.text, Does.Contain(expected));
            Assert.That(coordinator.TryConsumeLastTransitionFailure(out _), Is.False);
            status.text = "Sin novedades";
            consume.Invoke(presenter, null);
            Assert.That(status.text, Is.EqualTo("Sin novedades"));
        }
        finally
        {
            // Unbind normally destroys the view deferred; detach it for EditMode teardown.
            var presenter = root.GetComponent<TownRaidPreparationPresenter>();
            if (presenter != null)
                typeof(TownRaidPreparationPresenter).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(presenter, null);
            Object.DestroyImmediate(root);
            instance.SetValue(null, previous);
        }
    }
}
#endif
