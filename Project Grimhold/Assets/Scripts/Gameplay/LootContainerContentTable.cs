using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stores immutable authoring configuration for authoritative initial loot rolls.
/// Runtime roll results and seeds are never written back to this asset.
/// </summary>
[CreateAssetMenu(fileName = "LootContainerContentTable", menuName = "Grimhold/Loot/Loot Container Content Table")]
public sealed class LootContainerContentTable : ScriptableObject
{
    [SerializeField]
    private LootContainerContentTableEntry[] _entries = Array.Empty<LootContainerContentTableEntry>();

    [SerializeField, Min(0)]
    private int _minimumDistinctStacks = 1;

    [SerializeField, Min(0)]
    private int _maximumDistinctStacks = 1;

    [SerializeField]
    private bool _allowEmpty;

    [SerializeField, Range(0, CharacterDerivedStatisticsCalculator.BasisPointsDenominator)]
    [Tooltip("Chance in basis points for a Chaos Coin bonus from a compatible source (2000 = 20%).")]
    private int _chaosCoinChanceBasisPoints = 2000;

    public IReadOnlyList<LootContainerContentTableEntry> Entries => _entries;
    public int MinimumDistinctStacks => _minimumDistinctStacks;
    public int MaximumDistinctStacks => _maximumDistinctStacks;
    public bool AllowEmpty => _allowEmpty;
    public int ChaosCoinChanceBasisPoints => _chaosCoinChanceBasisPoints;
}
