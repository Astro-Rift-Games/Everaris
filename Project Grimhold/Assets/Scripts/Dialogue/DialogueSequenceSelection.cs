/// <summary>
/// Chooses which sequence of an <see cref="IDialogueTrigger"/> is played for one interaction.
/// </summary>
public static class DialogueSequenceSelection
{
    /// <summary>
    /// A locked build plays the trigger's locked sequence when it has one. Otherwise the first visit
    /// plays the primary sequence and repeat visits the secondary one (falling back to primary).
    /// </summary>
    public static DialogueSequence Select(IDialogueTrigger trigger, bool alreadyInteracted, bool buildLocked)
    {
        if (buildLocked && trigger.LockedSequence != null)
        {
            return trigger.LockedSequence;
        }

        return (alreadyInteracted && trigger.SecondarySequence != null)
            ? trigger.SecondarySequence
            : trigger.PrimarySequence;
    }
}
