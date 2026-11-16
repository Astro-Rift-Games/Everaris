using UnityEngine;

/// <summary>
/// Optional weapon keys (case-insensitive): Charge starts once in the confirmed windup and is
/// stopped on cancellation/interruption, disable or release. Ranged Cast replaces Shoot when
/// present; melee still releases Swing. Character impacts prefer Attack, falling back to Hit
/// only when Attack is absent. Scenery uses Block; real reload starts use Reload.
/// Missing keys stay silent; configurations without Charge/Cast retain their previous timing.
/// </summary>
[CreateAssetMenu(fileName = "WeaponAudioConfig", menuName = "Grimhold/Audio/Weapon Audio Config")]
public class WeaponAudioConfig : EntityAudioConfig
{
}
