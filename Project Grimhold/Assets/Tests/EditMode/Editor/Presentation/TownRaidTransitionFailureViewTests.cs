#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class TownRaidTransitionFailureViewTests
{
    private GameObject _root;
    private TownRaidPreparationView _view;

    private static IEnumerable<SessionTransitionResult> FailureResults() =>
        Enum.GetValues(typeof(SessionTransitionResult))
            .Cast<SessionTransitionResult>()
            .Where(result => result != SessionTransitionResult.Succeeded);

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject(nameof(TownRaidTransitionFailureViewTests));
        _view = TownRaidPreparationView.CreateEditorSource(_root.transform);
        _view.Open();
    }

    [TearDown]
    public void TearDown()
    {
        if (_root != null)
        {
            Object.DestroyImmediate(_root);
        }
    }

    [TestCaseSource(nameof(FailureResults))]
    public void TransitionFailure_ShowsTheCatalogMessageWithoutTheEnumName(SessionTransitionResult result)
    {
        _view.ShowTransitionFailure(result);

        SessionTransitionFailureText.TryGetMessage(result, out string expected);
        StringAssert.Contains(expected, StatusText());
        foreach (string enumName in Enum.GetNames(typeof(SessionTransitionResult)))
        {
            StringAssert.DoesNotContain(enumName, StatusText());
        }
    }

    private string StatusText() =>
        _view.transform.Find("RaidCodePanel/Status").GetComponent<TMP_Text>().text;
}
#endif
