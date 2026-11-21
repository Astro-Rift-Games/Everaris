using UnityEngine;

/// <summary>
/// Dialogue data on an NPC whose existing interactable owns the interaction.
/// </summary>
[DisallowMultipleComponent]
public sealed class TownNpcDialogueTrigger : MonoBehaviour, IDialogueTrigger
{
    [SerializeField] private DialogueSequence _primarySequence;
    [SerializeField] private DialogueSequence _secondarySequence;
    [SerializeField] private DialogueSequence _lockedSequence;

    public DialogueSequence PrimarySequence => _primarySequence;
    public DialogueSequence SecondarySequence => _secondarySequence;
    public DialogueSequence LockedSequence => _lockedSequence;
}
