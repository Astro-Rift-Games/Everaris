using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Single State-Authority director for Collapse environmental hazards (Doc 20 §3-§6, §10-§12;
/// TASK-335, TASK-336, TASK-339). Co-located on NetworkMatchController with
/// <see cref="DungeonPressureController"/> so up to <see cref="MaxFloorRifts"/> floor rifts
/// replicate via a single delta-compressed Fusion NetworkArray without adding physics colliders
/// or altering dungeon navigation.
/// </summary>
[DisallowMultipleComponent]
public sealed class CollapseHazardDirector : NetworkBehaviour, ICollapseRiftActivationSink
{
    public const int MaxFloorRifts = 256;

    [Header("Configuration")]
    [SerializeField] private CollapseHazardConfig _config;

    [Header("Dependencies (Optional Overrides)")]
    [SerializeField] private DungeonPressureController _pressureController;
    [SerializeField] private NetworkMatchController _matchController;

    [Networked, Capacity(MaxFloorRifts)]
    public NetworkArray<CollapseRiftNetState> FloorRifts => default;

    [Networked] public int FloorRiftCount { get; set; }
    [Networked] public int PlacementSeed { get; set; }
    [Networked] public DungeonPressurePhase LastAppliedPhase { get; set; }
    [Networked] public int WaveSequence { get; set; }
    [Networked] public NetworkBool WaveWarningActive { get; set; }
    [Networked] public int WaveWarningEndTick { get; set; }
    [Networked] public int NextWaveTick { get; set; }
    [Networked] public NetworkBool IsClosed { get; set; }

    private readonly List<CollapseRiftPlacement> _floorPlacements = new();
    private readonly List<CollapseRiftPlacement> _wallPlacements = new();
    private readonly List<Vector2> _floorPositions = new();
    private readonly List<CollapseRiftNetState> _waveStatesScratch = new();
    private readonly List<int> _waveSelectedIndices = new();
    private readonly List<EntityId> _processedTargets = new();
    private readonly EnvironmentalKnockbackProtectionTracker _envKnockbackProtection = new();

    private NetworkSpawnManager _spawnManager;
    private EntityRegistry _registry;
    private IDamageResolver _damageResolver;
    private Collider2D[] _colliderBuffer;
    private ContactFilter2D _contactFilter;
    private ICollapseSurfaceGrid _customGrid;
    private IReadOnlyList<CollapseRiftPlacement> _customFloorPlacements;
    private string _generatedRaidId = string.Empty;
    private bool _placementsBuilt;
    private bool _isSpawned;
    private bool _loggedInvalidConfig;

    public CollapseHazardConfig Config => _config;
    public bool IsSpawned => _isSpawned;
    public bool HasReadableState => _isSpawned && Object != null && Object.IsValid;
    public IReadOnlyList<CollapseRiftPlacement> FloorPlacements => _floorPlacements;
    public IReadOnlyList<CollapseRiftPlacement> WallPlacements => _wallPlacements;

    public void ConfigureForTests(
        CollapseHazardConfig config,
        IDamageResolver damageResolver = null,
        IReadOnlyList<CollapseRiftPlacement> customFloorPlacements = null,
        ICollapseSurfaceGrid customGrid = null,
        DungeonPressureController pressureController = null,
        NetworkMatchController matchController = null)
    {
        _config = config;
        _damageResolver = damageResolver;
        _customFloorPlacements = customFloorPlacements;
        _customGrid = customGrid;
        if (pressureController != null) _pressureController = pressureController;
        if (matchController != null) _matchController = matchController;
        _placementsBuilt = false;
        _loggedInvalidConfig = false;
        RebuildBufferFromConfig();

        if (HasReadableState && HasStateAuthority)
        {
            BuildPlacementsForCurrentRaid(initializeNetStates: true, immediate: true);
        }
    }

    public override void Spawned()
    {
        _isSpawned = true;
        ResolveDependencies();
        RebuildBufferFromConfig();

        _registry?.TryRegisterCollapseRiftActivationSink(this);

        bool isFreshStart = HasStateAuthority &&
            (_spawnManager == null || _spawnManager.ShouldInitializeMatchPhase) &&
            !HostMigrationRestoreUtility.IsRestoreSpawn(this);

        if (isFreshStart)
        {
            IsClosed = false;
            WaveSequence = 0;
            WaveWarningActive = false;
            WaveWarningEndTick = 0;
            NextWaveTick = 0;
            LastAppliedPhase = DungeonPressurePhase.Normal;
            FloorRiftCount = 0;
            PlacementSeed = 0;
            _envKnockbackProtection.Clear();
        }

        if (ValidateConfig())
        {
            BuildPlacementsForCurrentRaid(initializeNetStates: isFreshStart, immediate: !isFreshStart);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        _isSpawned = false;
        if (_registry != null)
        {
            _registry.TryUnregisterCollapseRiftActivationSink(this);
            _registry = null;
        }
        _envKnockbackProtection.Clear();
    }

    private void OnDestroy()
    {
        _isSpawned = false;
    }

    public bool TryGetFloorRiftState(int index, out CollapseRiftNetState state)
    {
        state = default;
        if (!HasReadableState || index < 0 || index >= FloorRiftCount || index >= MaxFloorRifts)
        {
            return false;
        }

        try
        {
            state = FloorRifts[index];
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public bool TryGetWaveWarningState(out int sequence, out bool active, out int warningEndTick)
    {
        sequence = 0;
        active = false;
        warningEndTick = 0;
        if (!HasReadableState)
        {
            return false;
        }

        try
        {
            sequence = WaveSequence;
            active = WaveWarningActive;
            warningEndTick = WaveWarningEndTick;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures local client placement list matches the authoritative <see cref="PlacementSeed"/>
    /// so presentation indices map 1:1 to <see cref="FloorRifts"/>.
    /// </summary>
    public void EnsureClientPlacementsSynced()
    {
        if (!HasReadableState || _config == null)
        {
            return;
        }

        string raidId = ReadRaidGenerationId();
        if (!_placementsBuilt || (!string.IsNullOrWhiteSpace(raidId) && raidId != _generatedRaidId))
        {
            BuildPlacementsForCurrentRaid(initializeNetStates: false, immediate: true);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !ValidateConfig())
        {
            return;
        }

        if (_spawnManager != null && _spawnManager.IsHostMigrationRecoveryInProgress)
        {
            return;
        }

        EnsureAuthoritativePlacementsUpToDate();

        int currentTick = Runner.Tick.Raw;
        float dt = Runner.DeltaTime;

        // 1. Apply phase transitions from DungeonPressureController when not closed
        if (!IsClosed && _pressureController != null && _pressureController.TryGetPhase(out DungeonPressurePhase currentPhase))
        {
            if (currentPhase != LastAppliedPhase)
            {
                ApplyPhaseTransitionToAllRifts(currentPhase, currentTick, immediate: false);
                ScheduleNextWaveOnPhaseChange(currentPhase, currentTick, dt);
                LastAppliedPhase = currentPhase;
            }
        }

#if UNITY_EDITOR
        ProcessEditorDebugRequests(currentTick, dt);
#endif

        // 2. Mandatory pre-damage causal exposure reporting for all visible activatable rifts (Doc 19 §8)
        ReportHazardPresenceForAllActiveRifts();

        // 3. Global rupture waves (TASK-336): never run after closure or before CriticalPressure
        if (!IsClosed)
        {
            StepGlobalWaveScheduler(currentTick, dt);
        }

        // 4. Step every floor rift lifecycle and resolve single-shot eruption effects
        int eruptionTicks = SecondsToTicks(_config.EruptionDurationSeconds, dt);
        int cooldownTicks = SecondsToTicks(_config.CooldownDurationSeconds, dt);
        int count = Mathf.Min(FloorRiftCount, _floorPositions.Count);

        for (int i = 0; i < count; i++)
        {
            CollapseRiftNetState state = FloorRifts[i];
            CollapseRiftLifecycleRules.Step(
                ref state,
                currentTick,
                eruptionTicks,
                cooldownTicks,
                IsClosed,
                out _);

            if (state.Stage == CollapseRiftAuthoritativeStage.Erupting && !state.EffectApplied)
            {
                ResolveEruptionImpact(i, _floorPositions[i], currentTick, dt);
                state.EffectApplied = true;
            }

            FloorRifts.Set(i, state);
        }
    }

    /// <summary>
    /// Authoritative activation of a single floor rift by index (used by tests, waves, and local triggers).
    /// Returns false if closed, harmless, or occupied.
    /// </summary>
    public bool TryActivateFloorRift(int riftIndex)
    {
        if (!HasStateAuthority || !HasReadableState || IsClosed || _config == null)
        {
            return false;
        }

        if (riftIndex < 0 || riftIndex >= FloorRiftCount || riftIndex >= MaxFloorRifts)
        {
            return false;
        }

        int telegraphTicks = SecondsToTicks(_config.TelegraphDurationSeconds, Runner.DeltaTime);
        CollapseRiftNetState state = FloorRifts[riftIndex];
        if (!CollapseRiftLifecycleRules.TryActivate(ref state, Runner.Tick.Raw, telegraphTicks, IsClosed))
        {
            return false;
        }

        FloorRifts.Set(riftIndex, state);
        return true;
    }

    /// <summary>
    /// Seam for compatible local abilities (e.g. Seismic Strike in TASK-337).
    /// Activates all activatable Dormant rifts within <paramref name="radius"/> of <paramref name="center"/>
    /// with full local telegraph warning, without triggering a global wave.
    /// </summary>
    public int TryActivateRiftsInRadius(Vector2 center, float radius)
    {
        if (!HasStateAuthority || !HasReadableState || IsClosed || _config == null || radius <= 0f)
        {
            return 0;
        }

        int telegraphTicks = SecondsToTicks(_config.TelegraphDurationSeconds, Runner.DeltaTime);
        int currentTick = Runner.Tick.Raw;
        float radiusSq = radius * radius;
        int count = Mathf.Min(FloorRiftCount, _floorPositions.Count);
        int activated = 0;

        for (int i = 0; i < count; i++)
        {
            if ((_floorPositions[i] - center).sqrMagnitude > radiusSq)
            {
                continue;
            }

            CollapseRiftNetState state = FloorRifts[i];
            if (CollapseRiftLifecycleRules.TryActivate(ref state, currentTick, telegraphTicks, IsClosed))
            {
                FloorRifts.Set(i, state);
                activated++;
            }
        }

        return activated;
    }

    /// <summary>
    /// Deterministic absolute-closure cutoff (Doc 20 §11, TASK-339).
    /// Cancels waves and telegraphs, disables dormant/cooldown rifts, and allows already-erupting
    /// rifts to finish their single functional eruption before transitioning directly to Disabled.
    /// </summary>
    public void HandleAbsoluteClosure()
    {
        if (!HasStateAuthority || !HasReadableState || IsClosed)
        {
            return;
        }

        int currentTick = Runner.Tick.Raw;
        float dt = Runner.DeltaTime;
        IsClosed = true;
        WaveWarningActive = false;
        WaveWarningEndTick = currentTick;
        NextWaveTick = 0;

        int count = Mathf.Min(FloorRiftCount, _floorPositions.Count);
        int inFlightEruptions = 0;
        int cancelledTelegraphs = 0;

        for (int i = 0; i < count; i++)
        {
            CollapseRiftNetState state = FloorRifts[i];
            if (state.Stage == CollapseRiftAuthoritativeStage.Telegraph)
            {
                cancelledTelegraphs++;
            }
            else if (state.Stage == CollapseRiftAuthoritativeStage.Erupting)
            {
                inFlightEruptions++;
                // Ensure any eruption already in-flight at the closure tick applies its functional
                // effect deterministically before participants are defeated by closure if still unapplied.
                if (!state.EffectApplied && ValidateConfig())
                {
                    ResolveEruptionImpact(i, _floorPositions[i], currentTick, dt);
                    state.EffectApplied = true;
                }
            }

            CollapseRiftLifecycleRules.ApplyAbsoluteClosure(ref state, currentTick);
            FloorRifts.Set(i, state);
        }

        Debug.Log($"[CollapseHazardDirector] Absolute closure applied at tick {currentTick}. InFlightEruptionsPreserved={inFlightEruptions}, TelegraphsCancelled={cancelledTelegraphs}.");
    }

    private void StepGlobalWaveScheduler(int currentTick, float dt)
    {
        DungeonPressurePhase phase = LastAppliedPhase;
        if (!CollapseWaveScheduler.AreGlobalWavesEnabled(phase))
        {
            WaveWarningActive = false;
            NextWaveTick = 0;
            return;
        }

        if (WaveWarningActive)
        {
            if (currentTick >= WaveWarningEndTick)
            {
                WaveWarningActive = false;
                ExecuteGlobalWaveActivation(currentTick, dt);
                ScheduleNextWaveTick(phase, currentTick, dt);
            }
            return;
        }

        if (NextWaveTick <= 0)
        {
            ScheduleNextWaveTick(phase, currentTick, dt);
            return;
        }

        if (currentTick >= NextWaveTick)
        {
            BeginGlobalWaveWarning(currentTick, dt);
        }
    }

    private void BeginGlobalWaveWarning(int currentTick, float dt)
    {
        int warningTicks = SecondsToTicks(_config.GlobalWaveWarningSeconds, dt);
        WaveSequence++;
        WaveWarningActive = true;
        WaveWarningEndTick = currentTick + warningTicks;
        Debug.Log($"[CollapseHazardDirector] Global rupture wave #{WaveSequence} warning started (phase={LastAppliedPhase}, executes at tick {WaveWarningEndTick}).");
    }

    private void ExecuteGlobalWaveActivation(int currentTick, float dt)
    {
        int count = Mathf.Min(FloorRiftCount, _floorPositions.Count);
        _waveStatesScratch.Clear();
        for (int i = 0; i < count; i++)
        {
            _waveStatesScratch.Add(FloorRifts[i]);
        }

        CollapseWaveScheduler.SelectEligibleRiftIndicesForWave(
            _waveStatesScratch,
            _floorPositions,
            _config.MaxConcurrentActiveRifts,
            _config.MinSimultaneousRiftSpacing,
            _waveSelectedIndices);

        int telegraphTicks = SecondsToTicks(_config.TelegraphDurationSeconds, dt);
        int activatedCount = 0;
        for (int i = 0; i < _waveSelectedIndices.Count; i++)
        {
            int idx = _waveSelectedIndices[i];
            CollapseRiftNetState state = FloorRifts[idx];
            if (CollapseRiftLifecycleRules.TryActivate(ref state, currentTick, telegraphTicks, IsClosed))
            {
                FloorRifts.Set(idx, state);
                activatedCount++;
            }
        }

        Debug.Log($"[CollapseHazardDirector] Global rupture wave #{WaveSequence} activated {activatedCount}/{count} rifts.");
    }

    private void ScheduleNextWaveOnPhaseChange(DungeonPressurePhase newPhase, int currentTick, float dt)
    {
        if (!CollapseWaveScheduler.AreGlobalWavesEnabled(newPhase))
        {
            WaveWarningActive = false;
            NextWaveTick = 0;
            return;
        }

        if (!WaveWarningActive)
        {
            ScheduleNextWaveTick(newPhase, currentTick, dt);
        }
    }

    private void ScheduleNextWaveTick(DungeonPressurePhase phase, int currentTick, float dt)
    {
        int intervalTicks = CollapseWaveScheduler.ComputeNextWaveIntervalTicks(
            phase,
            _config.CriticalPressureWaveIntervalRange,
            _config.CollapseWaveIntervalRange,
            PlacementSeed,
            WaveSequence,
            dt);

        NextWaveTick = intervalTicks > 0 ? currentTick + intervalTicks : 0;
    }

    private void ReportHazardPresenceForAllActiveRifts()
    {
        if (_registry == null || _colliderBuffer == null)
        {
            return;
        }

        int count = Mathf.Min(FloorRiftCount, _floorPositions.Count);
        float radius = _config.EruptionRadius;
        uint networkIdRaw = Object != null ? Object.Id.Raw : 1u;

        for (int i = 0; i < count; i++)
        {
            CollapseRiftNetState state = FloorRifts[i];
            if (!state.IsActivatable && state.Stage != CollapseRiftAuthoritativeStage.Erupting)
            {
                continue;
            }

            if (state.Stage == CollapseRiftAuthoritativeStage.Hidden ||
                state.Stage == CollapseRiftAuthoritativeStage.Disabled)
            {
                continue;
            }

            int hitCount = OverlapTargets(_floorPositions[i], radius);
            if (hitCount <= 0)
            {
                continue;
            }

            EntityId hazardId = CollapseHazardIdUtility.ForFloorRift(networkIdRaw, i);
            for (int h = 0; h < hitCount; h++)
            {
                if (TryResolveTarget(_colliderBuffer[h], out _, out IDamageable damageable) &&
                    damageable is PlayerCharacter player &&
                    player.HazardExposureCausality != null)
                {
                    player.HazardExposureCausality.ReportHazardPresence(hazardId);
                }
            }

            Array.Clear(_colliderBuffer, 0, hitCount);
        }
    }

    private void ResolveEruptionImpact(int riftIndex, Vector2 center, int currentTick, float dt)
    {
        if (_registry == null || _colliderBuffer == null)
        {
            return;
        }

        if (_damageResolver == null)
        {
            ResolveDamageResolver();
            if (_damageResolver == null)
            {
                return;
            }
        }

        int hitCount = OverlapTargets(center, _config.EruptionRadius);
        if (hitCount <= 0)
        {
            return;
        }

        EntityId hazardId = CollapseHazardIdUtility.ForFloorRift(Object != null ? Object.Id.Raw : 1u, riftIndex);
        int protectionTicks = _config.EnvironmentalKnockbackProtectionSeconds > 0f
            ? Mathf.CeilToInt(_config.EnvironmentalKnockbackProtectionSeconds / Mathf.Max(0.001f, dt))
            : 0;

        for (int h = 0; h < hitCount; h++)
        {
            Collider2D col = _colliderBuffer[h];
            if (!TryResolveTarget(col, out EntityId targetId, out IDamageable damageable))
            {
                continue;
            }

            if (!damageable.CanReceiveDamage)
            {
                continue;
            }

            // Consistency rule (Doc 20 §11): never mutate already-extracted participants during closure.
            if (damageable is PlayerCharacter player && IsParticipantExtracted(player))
            {
                continue;
            }

            Vector2 targetPos = col.bounds.center;
            Vector2 hitPoint = col.ClosestPoint(center);
            Vector2 knockbackDir = CollapseHazardIdUtility.ResolveRadialKnockbackDirection(center, targetPos);

            float knockbackForce = 0f;
            if (_config.EruptionKnockbackForce > 0f &&
                _envKnockbackProtection.CanApplyEnvironmentalKnockback(targetId, currentTick, protectionTicks))
            {
                knockbackForce = _config.EruptionKnockbackForce;
            }

            var request = new DamageRequest(
                attackerId: new EntityId(0),
                targetId: targetId,
                amount: _config.EruptionDamage,
                damageType: _config.EruptionDamageType,
                direction: knockbackDir,
                hitPoint: hitPoint,
                simulationTick: currentTick,
                knockbackForce: knockbackForce,
                environmentalHazardId: hazardId);

            DamageResult result = _damageResolver.Resolve(in request);
            if (knockbackForce > 0f && (result.IsApplied || _config.EruptionDamage == 0f))
            {
                _envKnockbackProtection.RecordEnvironmentalKnockbackApplied(targetId, currentTick);
            }
        }

        Array.Clear(_colliderBuffer, 0, hitCount);
    }

    private static bool IsParticipantExtracted(PlayerCharacter player)
    {
        RaidAvatarParticipantLink link = player.GetComponent<RaidAvatarParticipantLink>();
        return link != null &&
            link.TryResolveParticipant(out NetworkRaidParticipant participant) &&
            participant != null &&
            participant.State == RaidParticipantState.Extracted;
    }

    private int OverlapTargets(Vector2 center, float radius)
    {
        _contactFilter.SetLayerMask(_config.TargetLayerMask);
        _processedTargets.Clear();
        return Physics2D.OverlapCircle(center, radius, _contactFilter, _colliderBuffer);
    }

    private bool TryResolveTarget(Collider2D col, out EntityId targetId, out IDamageable damageable)
    {
        damageable = null;
        targetId = default;
        if (col == null ||
            !_registry.TryGetEntityId(col, out targetId) ||
            targetId.Value == 0 ||
            !_registry.IsDamageCollider(targetId, col) ||
            _processedTargets.Contains(targetId))
        {
            return false;
        }

        _processedTargets.Add(targetId);
        return _registry.TryGetDamageable(targetId, out damageable) &&
            (damageable is not ICharacter character || character.IsAlive);
    }

    private void EnsureAuthoritativePlacementsUpToDate()
    {
        string raidId = ReadRaidGenerationId();
        if (_placementsBuilt && (string.IsNullOrWhiteSpace(raidId) || raidId == _generatedRaidId))
        {
            return;
        }

        bool shouldInitStates = !_placementsBuilt || (!string.IsNullOrWhiteSpace(raidId) && raidId != _generatedRaidId);
        BuildPlacementsForCurrentRaid(initializeNetStates: shouldInitStates, immediate: false);
    }

    private void BuildPlacementsForCurrentRaid(bool initializeNetStates, bool immediate)
    {
        _floorPlacements.Clear();
        _wallPlacements.Clear();
        _floorPositions.Clear();

        string raidId = ReadRaidGenerationId();
        if (!string.IsNullOrWhiteSpace(raidId))
        {
            _generatedRaidId = raidId;
        }

        int seed = HasStateAuthority
            ? CollapseRiftPhaseRules.ComputeDeterministicSeed(_generatedRaidId, 1337)
            : (PlacementSeed != 0 ? PlacementSeed : CollapseRiftPhaseRules.ComputeDeterministicSeed(_generatedRaidId, 1337));

        if (_customFloorPlacements != null)
        {
            for (int i = 0; i < _customFloorPlacements.Count && i < MaxFloorRifts; i++)
            {
                _floorPlacements.Add(_customFloorPlacements[i]);
            }
        }
        else if (_config != null && _config.RiftConfig != null)
        {
            ICollapseSurfaceGrid grid = _customGrid ?? TilemapCollapseSurfaceGrid.BuildFromTilemaps(
                FindObjectsByType<Tilemap>());

            List<Vector2> floorExclusions = CollectSortedFloorExclusions();
            List<Vector2> wallExclusions = CollectSortedWallExclusions();

            CollapseRiftPlacementService.GeneratePlacements(
                grid,
                _config.RiftConfig.BuildPlacementSettings(),
                floorExclusions,
                wallExclusions,
                seed,
                _floorPlacements,
                _wallPlacements);

            if (_floorPlacements.Count > MaxFloorRifts)
            {
                _floorPlacements.RemoveRange(MaxFloorRifts, _floorPlacements.Count - MaxFloorRifts);
            }
        }

        for (int i = 0; i < _floorPlacements.Count; i++)
        {
            _floorPositions.Add(_floorPlacements[i].WorldPosition);
        }

        _placementsBuilt = true;

        if (HasStateAuthority && HasReadableState)
        {
            PlacementSeed = seed;
            FloorRiftCount = _floorPlacements.Count;

            if (initializeNetStates)
            {
                DungeonPressurePhase currentPhase = _pressureController != null && _pressureController.TryGetPhase(out DungeonPressurePhase p)
                    ? p
                    : DungeonPressurePhase.Normal;
                int currentTick = Runner.Tick.Raw;
                int spawnTicks = SecondsToTicks(_config.SpawnDurationSeconds, Runner.DeltaTime);

                for (int i = 0; i < FloorRiftCount; i++)
                {
                    CollapseRiftNetState state = default;
                    CollapseRiftLifecycleRules.Initialize(ref state, _floorPlacements[i].Batch);
                    CollapseRiftLifecycleRules.ApplyPhaseTransition(ref state, currentPhase, currentTick, spawnTicks, immediate, IsClosed);
                    FloorRifts.Set(i, state);
                }

                LastAppliedPhase = currentPhase;
                ScheduleNextWaveOnPhaseChange(currentPhase, currentTick, Runner.DeltaTime);
            }
        }
    }

    private void ApplyPhaseTransitionToAllRifts(DungeonPressurePhase newPhase, int currentTick, bool immediate)
    {
        int spawnTicks = SecondsToTicks(_config.SpawnDurationSeconds, Runner.DeltaTime);
        int count = Mathf.Min(FloorRiftCount, _floorPositions.Count);
        for (int i = 0; i < count; i++)
        {
            CollapseRiftNetState state = FloorRifts[i];
            CollapseRiftLifecycleRules.ApplyPhaseTransition(ref state, newPhase, currentTick, spawnTicks, immediate, IsClosed);
            FloorRifts.Set(i, state);
        }
    }

    private List<Vector2> CollectSortedFloorExclusions()
    {
        var centers = new List<Vector2>();
        if (_customGrid != null) return centers;

        ExtractionSanctuary[] sanctuaries = FindObjectsByType<ExtractionSanctuary>();
        for (int i = 0; i < sanctuaries.Length; i++)
        {
            if (sanctuaries[i] != null)
            {
                Vector3 pos = sanctuaries[i].transform.position;
                centers.Add(new Vector2(pos.x, pos.y));
            }
        }
        CollapseRiftPlacementService.SortExclusionCenters(centers);
        return centers;
    }

    private List<Vector2> CollectSortedWallExclusions()
    {
        var centers = new List<Vector2>();
        if (_customGrid != null) return centers;

        DungeonLightProp[] lightProps = FindObjectsByType<DungeonLightProp>();
        for (int i = 0; i < lightProps.Length; i++)
        {
            if (lightProps[i] != null)
            {
                Vector3 pos = lightProps[i].transform.position;
                centers.Add(new Vector2(pos.x, pos.y));
            }
        }
        CollapseRiftPlacementService.SortExclusionCenters(centers);
        return centers;
    }

    private string ReadRaidGenerationId()
    {
        if (_matchController != null && _matchController.Object != null && _matchController.Object.IsValid)
        {
            try
            {
                return _matchController.RaidGenerationId.ToString();
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    private void ResolveDependencies()
    {
        if (_pressureController == null) _pressureController = GetComponent<DungeonPressureController>();
        if (_matchController == null) _matchController = GetComponent<NetworkMatchController>();
        if (Runner != null)
        {
            _spawnManager = Runner.GetComponent<NetworkSpawnManager>();
            _registry = Runner.GetComponent<EntityRegistry>();
        }
        ResolveDamageResolver();
    }

    private void ResolveDamageResolver()
    {
        if (_damageResolver != null) return;
        _damageResolver = GetComponent<IDamageResolver>()
            ?? (Runner != null ? Runner.GetComponent<IDamageResolver>() : null)
            ?? FindAnyObjectByType<DamageResolver>(FindObjectsInactive.Exclude);
    }

    private void RebuildBufferFromConfig()
    {
        int size = _config != null ? Mathf.Max(4, _config.MaxTargetsPerQuery) : 32;
        if (_colliderBuffer == null || _colliderBuffer.Length != size)
        {
            _colliderBuffer = new Collider2D[size];
        }

        _contactFilter = new ContactFilter2D
        {
            useLayerMask = true,
            useTriggers = true
        };
        if (_config != null)
        {
            _contactFilter.SetLayerMask(_config.TargetLayerMask);
        }
    }

    private bool ValidateConfig()
    {
        if (_config != null && _config.Validate(out _))
        {
            return true;
        }

        if (!_loggedInvalidConfig)
        {
            _loggedInvalidConfig = true;
            string reason = _config == null ? "_config is NULL" : (_config.Validate(out string err) ? "" : err);
            Debug.LogError($"[CollapseHazardDirector] Invalid CollapseHazardConfig ({reason}). Collapse hazards disabled.", this);
        }

        return false;
    }

    private static int SecondsToTicks(float seconds, float deltaTime)
    {
        if (seconds <= 0f || deltaTime <= 0f) return 1;
        return Mathf.Max(1, Mathf.CeilToInt(seconds / deltaTime));
    }

#if UNITY_EDITOR
    private bool _forceWaveRequested;

    public void ForceTriggerGlobalWave()
    {
        _forceWaveRequested = true;
    }

    private void ProcessEditorDebugRequests(int currentTick, float dt)
    {
        if (_forceWaveRequested)
        {
            _forceWaveRequested = false;
            if (!IsClosed && !WaveWarningActive)
            {
                BeginGlobalWaveWarning(currentTick, dt);
            }
        }
    }
#endif
}
