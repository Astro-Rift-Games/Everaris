using UnityEngine;

/// <summary>
/// Audio set of the Catacombs Specter. Same lookup as <see cref="EnemyAudioConfig"/>, but its key declaration
/// (<see cref="AudioKeys.SpecFor"/>) also accepts the optional Specter state cues, so the other enemies keep strict validation.
/// </summary>
[CreateAssetMenu(fileName = "SpecterAudioConfig", menuName = "Grimhold/Audio/Specter Audio Config")]
public sealed class SpecterAudioConfig : EnemyAudioConfig
{
}
