using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

public sealed class DialogueSequenceSelectionTests
{
    private sealed class Trigger : IDialogueTrigger
    {
        public DialogueSequence PrimarySequence { get; set; }
        public DialogueSequence SecondarySequence { get; set; }
        public DialogueSequence LockedSequence { get; set; }
    }

    private DialogueSequence _primary;
    private DialogueSequence _secondary;
    private DialogueSequence _locked;

    [SetUp]
    public void SetUp()
    {
        _primary = ScriptableObject.CreateInstance<DialogueSequence>();
        _secondary = ScriptableObject.CreateInstance<DialogueSequence>();
        _locked = ScriptableObject.CreateInstance<DialogueSequence>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_primary);
        Object.DestroyImmediate(_secondary);
        Object.DestroyImmediate(_locked);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Locked_WithLockedSequence_PlaysLockedSequenceEvenOnRepeatVisit(bool alreadyInteracted)
    {
        var trigger = new Trigger { PrimarySequence = _primary, SecondarySequence = _secondary, LockedSequence = _locked };

        Assert.That(DialogueSequenceSelection.Select(trigger, alreadyInteracted, buildLocked: true), Is.SameAs(_locked));
    }

    [Test]
    public void Locked_WithoutLockedSequence_FallsBackToPrimaryOnFirstVisit()
    {
        var trigger = new Trigger { PrimarySequence = _primary, SecondarySequence = _secondary };

        Assert.That(DialogueSequenceSelection.Select(trigger, alreadyInteracted: false, buildLocked: true), Is.SameAs(_primary));
    }

    [Test]
    public void Locked_WithoutLockedSequence_FallsBackToSecondaryOnRepeatVisit()
    {
        var trigger = new Trigger { PrimarySequence = _primary, SecondarySequence = _secondary };

        Assert.That(DialogueSequenceSelection.Select(trigger, alreadyInteracted: true, buildLocked: true), Is.SameAs(_secondary));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Unlocked_IgnoresLockedSequence_AndUsesPrimaryOnFirstVisit(bool hasLockedSequence)
    {
        var trigger = new Trigger { PrimarySequence = _primary, SecondarySequence = _secondary, LockedSequence = hasLockedSequence ? _locked : null };

        Assert.That(DialogueSequenceSelection.Select(trigger, alreadyInteracted: false, buildLocked: false), Is.SameAs(_primary));
    }

    [Test]
    public void Unlocked_RepeatVisit_UsesSecondaryWhenPresent()
    {
        var trigger = new Trigger { PrimarySequence = _primary, SecondarySequence = _secondary, LockedSequence = _locked };

        Assert.That(DialogueSequenceSelection.Select(trigger, alreadyInteracted: true, buildLocked: false), Is.SameAs(_secondary));
    }

    [Test]
    public void Unlocked_RepeatVisit_FallsBackToPrimaryWithoutSecondary()
    {
        var trigger = new Trigger { PrimarySequence = _primary, LockedSequence = _locked };

        Assert.That(DialogueSequenceSelection.Select(trigger, alreadyInteracted: true, buildLocked: false), Is.SameAs(_primary));
    }
}
