using UnityEngine;

/// <summary>
/// Presents confirmed weapon audio. Optional Charge uses a stoppable per-hand source, cut on
/// cancellation, disable and release. Optional Cast replaces ranged Shoot; Attack precedes Hit
/// for character impacts. Swing/Shoot/Cast use the shared release clock; Reload observes
/// replicated hand reload starts. Impacts resolve only the event weapon, or the generic fallback.
/// Missing optional clips and unavailable runner registries are quiet.
/// </summary>
[DisallowMultipleComponent]
public class WeaponAudioPresenter : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private MonoBehaviour _combatControllerSource;
    [SerializeField] private PlayerWeaponEquipmentNetworkController _equipmentSource;
    [SerializeField] private EntityRegistry _entityRegistry;

    [Header("Fallback Configuration")]
    [SerializeField]
    [Tooltip("Optional generic audio for attacks without a resolvable confirmed weapon.")]
    private WeaponAudioConfig _fallbackAudioConfig;

    [Header("Environment Detection")]
    [SerializeField]
    [Tooltip("Capas consideradas escenario u obstáculo para impactos de bloqueo.")]
    private LayerMask _obstacleLayers = 1 << 6 | 1 << 11;
    [SerializeField, Min(0.1f)] private float _sceneryCheckRadius = 0.8f;

    private ICombatController _combatController;
    private PlayerCombatNetworkController _playerCombatController;
    private bool _isSubscribed;
    private readonly AudioSource[] _chargeSources = new AudioSource[5];
    private readonly WeaponAudioCue[] _cues =
        { new(), new(), new(), new(), new() }; // Four independent hand slots + legacy strategy.

    private void Awake() => CacheDependencies();

    private void OnEnable()
    {
        CacheDependencies();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        for (int i = 0; i < _cues.Length; i++)
        {
            _cues[i].Suspend();
            StopCharge(i);
        }
    }

    private void CacheDependencies()
    {
        if (_combatControllerSource != null)
        {
            _combatController = _combatControllerSource as ICombatController;
            _playerCombatController = _combatControllerSource as PlayerCombatNetworkController;
        }
        if (_combatController == null)
        {
            _combatController = GetComponentInParent<ICombatController>();
            _playerCombatController = GetComponentInParent<PlayerCombatNetworkController>();
        }
        if (_equipmentSource == null)
            _equipmentSource = GetComponentInParent<PlayerWeaponEquipmentNetworkController>();
        ResolveRunnerRegistry();
    }

    private void ResolveRunnerRegistry()
    {
        // Awake/OnEnable can precede Fusion Spawned; retry only this avatar's explicit runner.
        if (_entityRegistry == null && _playerCombatController != null && _playerCombatController.Runner != null)
            _entityRegistry = _playerCombatController.Runner.GetComponent<EntityRegistry>();
    }

    private void Subscribe()
    {
        if (_isSubscribed) return;
        if (_combatController != null) _combatController.AttackPerformed += OnAttackPerformed;
        if (_playerCombatController != null)
        {
            _playerCombatController.CombatFeedbackResolved += OnCombatFeedbackResolved;
            _playerCombatController.AttackPresentationResumed += OnAttackPresentationResumed;
        }
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed) return;
        if (_combatController != null) _combatController.AttackPerformed -= OnAttackPerformed;
        if (_playerCombatController != null)
        {
            _playerCombatController.CombatFeedbackResolved -= OnCombatFeedbackResolved;
            _playerCombatController.AttackPresentationResumed -= OnAttackPresentationResumed;
        }
        _isSubscribed = false;
    }

    private static int CueIndex(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.WeaponSetAMainHand => 0, EquipmentSlot.WeaponSetAOffHand => 1,
        EquipmentSlot.WeaponSetBMainHand => 2, EquipmentSlot.WeaponSetBOffHand => 3, _ => 4
    };

    private void OnAttackPerformed(AttackPerformedEvent attack)
    {
        int index = CueIndex(attack.SourceSlot);
        WeaponAudioCue cue = _cues[index];
        if (cue.Accept(attack)) StopCharge(index);
        if (_playerCombatController == null) UpdateCharge(index, 0f);
        else if (_playerCombatController.TryGetAttackPresentationSeconds(attack, out float seconds))
            UpdateCharge(index, seconds);
        // Non-player strategies without a confirmed timeline retain their immediate notification.
        if (!attack.HasReleaseTimeline && cue.TryRelease(0f)) PlayRelease(attack);
    }

    private void OnAttackPresentationResumed(AttackPerformedEvent attack)
    {
        int index = CueIndex(attack.SourceSlot);
        if (_cues[index].Accept(attack, resumed: true)) StopCharge(index);
    }

    private void LateUpdate()
    {
        if (_playerCombatController == null) return;
        for (int i = 0; i < _cues.Length; i++)
        {
            WeaponAudioCue cue = _cues[i];
            if (!cue.Active) continue;
            AttackPerformedEvent attack = cue.Attack;
            if (attack.HasReleaseTimeline)
            {
                if (!_playerCombatController.TryGetAttackPresentationSeconds(attack, out float seconds))
                {
                    cue.CancelRelease();
                    StopCharge(i);
                }
                else
                {
                    UpdateCharge(i, seconds);
                    if (cue.TryRelease(seconds)) PlayRelease(attack);
                }
            }
            if (_playerCombatController.TryGetAttackReloadPresentation(attack, out WeaponReloadState reload) &&
                cue.TryReload(reload))
                Play(ResolveAudioConfig(attack.WeaponCatalogIndexPlusOne), "Reload", transform.position);
        }
    }

    private void UpdateCharge(int index, float seconds)
    {
        if (!_cues[index].TryCharge(seconds)) return;
        WeaponAudioConfig config = ResolveAudioConfig(_cues[index].Attack.WeaponCatalogIndexPlusOne);
        AudioManager manager = AudioManager.Instance;
        if (manager == null || config == null || !config.TryGetClip("Charge", out CustomClip clip) || !clip.IsValid) return;
        if (_chargeSources[index] == null)
            _chargeSources[index] = manager.CreateSfxSource(transform, $"WeaponCharge_{index}");
        manager.PlaySfxAttached(clip, _chargeSources[index]);
    }

    private void StopCharge(int index)
    {
        if (_chargeSources[index] != null) _chargeSources[index].Stop();
    }

    private void PlayRelease(in AttackPerformedEvent attack)
    {
        StopCharge(CueIndex(attack.SourceSlot));
        WeaponAudioConfig config = ResolveAudioConfig(attack.WeaponCatalogIndexPlusOne);
        Play(config, WeaponAudioCue.ReleaseKey(attack.AttackType,
            config != null && config.TryGetClip("Cast", out _)), transform.position);
        if (attack.AttackType != AttackType.Melee || config == null || AudioManager.Instance == null) return;
        Vector2 origin = attack.Origin != Vector2.zero ? attack.Origin : (Vector2)transform.position;
        Vector2 checkPosition = origin + attack.Direction * (_sceneryCheckRadius * 0.7f);
        Collider2D obstacle = Physics2D.OverlapCircle(checkPosition, _sceneryCheckRadius, _obstacleLayers);
        if (obstacle != null) Play(config, "Block", obstacle.transform.position);
    }

    private void OnCombatFeedbackResolved(CombatPresentationEvent feedback)
    {
        if (feedback.Kind != CombatFeedbackKind.ConfirmedImpact || feedback.AppliedDamage <= 0f) return;
        ResolveRunnerRegistry();
        WeaponAudioConfig config = ResolveAudioConfig(feedback.WeaponCatalogIndexPlusOne);
        Play(config, WeaponAudioCue.ImpactKey(IsCharacterEntity(feedback.TargetId),
            config != null && config.TryGetClip("Attack", out _)),
            new Vector3(feedback.HitPoint.x, feedback.HitPoint.y, transform.position.z));
    }

    private static void Play(WeaponAudioConfig config, string key, Vector3 position)
    {
        if (AudioManager.Instance != null && config != null && config.TryGetClip(key, out CustomClip clip))
            AudioManager.Instance.PlaySfx(clip, position);
    }

    private bool IsCharacterEntity(EntityId targetId) =>
        _entityRegistry != null && targetId.Value != 0 &&
        _entityRegistry.TryGetDamageable(targetId, out IDamageable damageable) && damageable is CharacterBase;

    private WeaponAudioConfig ResolveAudioConfig(int confirmedWeaponIndexPlusOne = 0)
    {
        if (_equipmentSource == null)
            _equipmentSource = GetComponentInParent<PlayerWeaponEquipmentNetworkController>();
        if (confirmedWeaponIndexPlusOne > 0 && _equipmentSource != null &&
            _equipmentSource.TryGetWeaponByCatalogIndexPlusOne(confirmedWeaponIndexPlusOne, out LootDefinition weapon))
        {
            WeaponAudioConfig config = weapon.WeaponDefinition.AudioConfig;
            if (config != null) return config;
        }
        return _fallbackAudioConfig;
    }
}
