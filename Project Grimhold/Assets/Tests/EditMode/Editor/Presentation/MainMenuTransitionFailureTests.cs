using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

public sealed class MainMenuTransitionFailureTests
{
    private static IEnumerable Failures()
    {
        foreach (SessionTransitionResult result in Enum.GetValues(typeof(SessionTransitionResult)))
            if (result != SessionTransitionResult.Succeeded) yield return new TestCaseData(result);
        yield return new TestCaseData((SessionTransitionResult)int.MaxValue);
    }

    [TestCaseSource(nameof(Failures))]
    public void TownConnectionFailure_DisplaysCatalogTextWithoutEnumName(SessionTransitionResult result)
    {
        // Inactive owner avoids unrelated serialized button lifecycle requirements.
        var owner = new GameObject(nameof(MainMenuTransitionFailureTests));
        owner.SetActive(false);
        var labelObject = new GameObject("Status", typeof(RectTransform));
        labelObject.transform.SetParent(owner.transform);
        try
        {
            var controller = owner.AddComponent<MainMenuController>();
            var label = labelObject.AddComponent<TextMeshProUGUI>();
            typeof(MainMenuController).GetField("_statusText", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(controller, label);
            var show = typeof(MainMenuController).GetMethod("ShowTownConnectionFailure", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(show, Is.Not.Null);
            show.Invoke(controller, new object[] { result });

            Assert.That(SessionTransitionFailureText.TryGetMessage(result, out string expected), Is.True);
            Assert.That(label.text, Is.EqualTo(expected));
            Assert.That(label.gameObject.activeSelf, Is.True);
            Assert.That(label.text, Does.Not.Contain(result.ToString()));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }
}
