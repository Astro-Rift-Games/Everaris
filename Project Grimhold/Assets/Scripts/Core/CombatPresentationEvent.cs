using UnityEngine;

/// <summary>Immutable authoritative combat result delivered only to local presentation.</summary>
public readonly struct CombatPresentationEvent
{
    public int Sequence { get; }
    public CombatFeedbackKind Kind { get; }
    public EntityId TargetId { get; }
    public Vector2 HitPoint { get; }
    public Vector2 HitDirection { get; }
    public float AppliedDamage { get; }
    public int SimulationTick { get; }
    public AttackFailureReason AttackFailureReason { get; }
    public WeaponAttackTag WeaponTag { get; }
    public EquipmentSlot SourceSlot => WeaponTag.SourceSlot;
    public int AttackSequence => WeaponTag.AttackSequence;
    public int WeaponCatalogIndexPlusOne => WeaponTag.WeaponCatalogIndexPlusOne;
    public AttackType AttackType => WeaponTag.AttackType;
    public bool HasWeaponTag => WeaponTag.HasWeaponTag;
    public bool IsFatal { get; }
    public DamageType DamageType { get; }
    public bool IsWeaponImpact => Kind == CombatFeedbackKind.ConfirmedImpact && HasWeaponTag;
    public bool IsMeleeImpact => IsWeaponImpact && AttackType == AttackType.Melee;

    // Compatibility for existing melee-only consumers and fixtures.
    public EquipmentSlot MeleeSourceSlot => SourceSlot;
    public int MeleeAttackSequence => IsMeleeImpact ? AttackSequence : 0;
    public int MeleeWeaponCatalogIndexPlusOne => IsMeleeImpact ? WeaponCatalogIndexPlusOne : 0;

    public CombatPresentationEvent(int sequence, CombatFeedbackKind kind, EntityId targetId,
        Vector2 hitPoint, float appliedDamage, int simulationTick, AttackFailureReason attackFailureReason,
        EquipmentSlot meleeSourceSlot = EquipmentSlot.None, int meleeAttackSequence = 0,
        int meleeWeaponCatalogIndexPlusOne = 0, Vector2 hitDirection = default)
        : this(sequence, kind, targetId, hitPoint, appliedDamage, simulationTick, attackFailureReason,
            new WeaponAttackTag(meleeSourceSlot, meleeAttackSequence, meleeWeaponCatalogIndexPlusOne, AttackType.Melee),
            hitDirection, false) { }

    public CombatPresentationEvent(int sequence, CombatFeedbackKind kind, EntityId targetId,
        Vector2 hitPoint, float appliedDamage, int simulationTick, AttackFailureReason attackFailureReason,
        WeaponAttackTag weaponTag, Vector2 hitDirection = default, bool isFatal = false,
        DamageType damageType = DamageType.Physical)
    {
        Sequence = sequence;
        Kind = kind;
        TargetId = targetId;
        HitPoint = hitPoint;
        HitDirection = hitDirection;
        AppliedDamage = appliedDamage;
        SimulationTick = simulationTick;
        AttackFailureReason = attackFailureReason;
        WeaponTag = weaponTag;
        IsFatal = isFatal;
        DamageType = damageType;
    }
}
