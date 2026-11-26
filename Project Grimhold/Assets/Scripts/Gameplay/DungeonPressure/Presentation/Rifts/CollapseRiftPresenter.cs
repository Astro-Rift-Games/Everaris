using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Local client presentation coordinator for floor and wall collapse rifts in CatacombsMap.
/// Observes the replicated <see cref="DungeonPressureController"/> phase and derives a deterministic
/// per-raid placement seed from <see cref="NetworkMatchController.RaidGenerationId"/> so Host, Clients,
/// late-joiners, and Host Migration peers all render identical random rift placements without extra network state.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class CollapseRiftPresenter : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private DungeonPressureController _pressureController;
    [SerializeField] private NetworkMatchController _matchController;
    [SerializeField] private CollapseHazardDirector _hazardDirector;
    [SerializeField] private CollapseRiftConfig _config;

    [Header("Wall Animation Inspector Control")]
    [Tooltip("Optional Inspector override for wall rift time between frames in seconds. When <= 0, uses CollapseRiftConfig.WallFrameIntervalSeconds.")]
    [Min(0f)]
    [SerializeField] private float _wallFrameIntervalOverrideSeconds = 0f;

    [Header("Offline / Editor Preview")]
    [SerializeField] private int _fallbackSeed = 1337;

    private readonly List<CollapseFloorRiftVisual> _floorRifts = new();
    private readonly List<CollapseWallRiftVisual> _wallRifts = new();
    private readonly List<CollapseRiftPlacement> _floorPlacementsBuffer = new();
    private readonly List<CollapseRiftPlacement> _wallPlacementsBuffer = new();

    private Transform _riftsRoot;
    private ICollapseSurfaceGrid _customGrid;
    private CollapseRiftAtlas _activeAtlas;
    private DungeonPressurePhase _lastPhase = DungeonPressurePhase.Normal;
    private bool _hasResolvedInitialPhase;
    private bool _hasGeneratedRifts;
    private bool _hasSyncedAuthoritativeRifts;
    private bool _loggedMissingConfig;
    private int _activeSeed;
    private string _activeRaidGenerationId = string.Empty;

    public CollapseRiftConfig Config => _config;
    public DungeonPressurePhase CurrentPhase => _lastPhase;
    public int ActiveSeed => _activeSeed;
    public float EffectiveWallFrameIntervalSeconds => _wallFrameIntervalOverrideSeconds > 0f
        ? _wallFrameIntervalOverrideSeconds
        : (_config != null ? _config.WallFrameIntervalSeconds : 0.15f);
    public IReadOnlyList<CollapseFloorRiftVisual> FloorRifts => _floorRifts;
    public IReadOnlyList<CollapseWallRiftVisual> WallRifts => _wallRifts;
    public CollapseRiftAtlas ActiveAtlas => _activeAtlas;

    private void OnEnable()
    {
        ResolveDependencies();
        if (Application.isPlaying)
        {
            if (_config == null)
            {
                LogMissingConfigOnce();
                return;
            }

            if (!_hasResolvedInitialPhase && TryGetControllerPhase(out DungeonPressurePhase phase))
            {
                EnsureGeneratedForCurrentRaid();
                ApplyInitialPhase(phase);
            }
        }
    }

    private void OnDisable()
    {
        ResetPresentation();
    }

    private void OnDestroy()
    {
        ResetPresentation();
    }

    public void Configure(
        DungeonPressureController controller,
        CollapseRiftConfig config,
        ICollapseSurfaceGrid customGrid = null,
        NetworkMatchController matchController = null,
        int fallbackSeed = 1337)
    {
        _pressureController = controller;
        _matchController = matchController;
        _config = config;
        _customGrid = customGrid;
        _fallbackSeed = fallbackSeed;

        ClearSpawnedRifts();
        _hasGeneratedRifts = false;
        _hasResolvedInitialPhase = false;
        _activeRaidGenerationId = string.Empty;

        EnsureGeneratedForCurrentRaid();
        DungeonPressurePhase initialPhase = TryGetControllerPhase(out DungeonPressurePhase phase)
            ? phase
            : DungeonPressurePhase.Normal;
        ApplyInitialPhase(initialPhase);
    }

    public void SetWallFrameIntervalSeconds(float seconds)
    {
        _wallFrameIntervalOverrideSeconds = Mathf.Max(0f, seconds);
        ApplyWallFrameIntervalToAll();
    }

    public void ApplyInitialPhase(DungeonPressurePhase phase)
    {
        EnsureGeneratedForCurrentRaid();
        _hasResolvedInitialPhase = true;
        _lastPhase = phase;

        int visibleFloor = 0;
        int activatableFloor = 0;
        for (int i = 0; i < _floorRifts.Count; i++)
        {
            if (_floorRifts[i] != null)
            {
                _floorRifts[i].ApplyDungeonPhase(phase, immediate: true);
                if (_floorRifts[i].VisualState != CollapseRiftVisualState.Hidden) visibleFloor++;
                if (_floorRifts[i].IsActivatable) activatableFloor++;
            }
        }

        int visibleWall = 0;
        for (int i = 0; i < _wallRifts.Count; i++)
        {
            if (_wallRifts[i] != null)
            {
                _wallRifts[i].ApplyDungeonPhase(phase, immediate: true);
                if (_wallRifts[i].VisualState != CollapseRiftVisualState.Hidden) visibleWall++;
            }
        }

        if (Application.isPlaying)
        {
            Debug.Log($"[CollapseRiftPresenter] Applied initial phase {phase} (immediate). VisibleFloor={visibleFloor}/{_floorRifts.Count} (activatable={activatableFloor}), VisibleWall={visibleWall}/{_wallRifts.Count}.", this);
        }
    }

    public void ApplyPhaseTransition(DungeonPressurePhase newPhase)
    {
        EnsureGeneratedForCurrentRaid();
        DungeonPressurePhase previousPhase = _lastPhase;
        _lastPhase = newPhase;

        int visibleFloor = 0;
        int activatableFloor = 0;
        for (int i = 0; i < _floorRifts.Count; i++)
        {
            if (_floorRifts[i] != null)
            {
                _floorRifts[i].ApplyDungeonPhase(newPhase, immediate: false);
                if (_floorRifts[i].VisualState != CollapseRiftVisualState.Hidden) visibleFloor++;
                if (_floorRifts[i].IsActivatable) activatableFloor++;
            }
        }

        int visibleWall = 0;
        for (int i = 0; i < _wallRifts.Count; i++)
        {
            if (_wallRifts[i] != null)
            {
                _wallRifts[i].ApplyDungeonPhase(newPhase, immediate: false);
                if (_wallRifts[i].VisualState != CollapseRiftVisualState.Hidden) visibleWall++;
            }
        }

        if (Application.isPlaying)
        {
            Debug.Log($"[CollapseRiftPresenter] Phase transition {previousPhase} -> {newPhase}. VisibleFloor={visibleFloor}/{_floorRifts.Count} (activatable={activatableFloor}), VisibleWall={visibleWall}/{_wallRifts.Count}, WallFrameInterval={EffectiveWallFrameIntervalSeconds:0.###}s.", this);
        }
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (_config == null)
        {
            LogMissingConfigOnce();
            return;
        }

        if (_pressureController == null || _matchController == null || _hazardDirector == null)
        {
            ResolveDependencies();
        }

        CheckRaidGenerationChange();

        if (!_hasResolvedInitialPhase)
        {
            if (TryGetControllerPhase(out DungeonPressurePhase initialPhase))
            {
                ApplyInitialPhase(initialPhase);
            }
            else if (!_hasGeneratedRifts)
            {
                ApplyInitialPhase(DungeonPressurePhase.Normal);
            }
        }

        EvaluatePresentation(Time.deltaTime);
    }

    public void EvaluatePresentation(float deltaTime)
    {
        if (TryGetControllerPhase(out DungeonPressurePhase phase) && phase != _lastPhase)
        {
            ApplyPhaseTransition(phase);
        }

        ApplyWallFrameIntervalToAll();

        if (_hazardDirector != null && _hazardDirector.HasReadableState && _hazardDirector.FloorRiftCount > 0)
        {
            SyncFloorRiftsFromAuthority();
        }
        else
        {
            for (int i = 0; i < _floorRifts.Count; i++)
            {
                if (_floorRifts[i] != null)
                {
                    _floorRifts[i].TickVisual(deltaTime);
                }
            }
        }

        for (int i = 0; i < _wallRifts.Count; i++)
        {
            if (_wallRifts[i] != null)
            {
                _wallRifts[i].TickVisual(deltaTime);
            }
        }
    }

    private void SyncFloorRiftsFromAuthority()
    {
        bool suppressBurst = !_hasSyncedAuthoritativeRifts;
        int currentTick = _hazardDirector.Runner != null ? _hazardDirector.Runner.Tick.Raw : 0;
        float dt = _hazardDirector.Runner != null ? _hazardDirector.Runner.DeltaTime : Time.fixedDeltaTime;
        int count = Mathf.Min(_floorRifts.Count, _hazardDirector.FloorRiftCount);

        for (int i = 0; i < count; i++)
        {
            CollapseFloorRiftVisual visual = _floorRifts[i];
            if (visual == null)
            {
                continue;
            }

            if (_hazardDirector.TryGetFloorRiftState(i, out CollapseRiftNetState state))
            {
                CollapseRiftVisualState visualState = CollapseRiftLifecycleRules.ToVisualState(state.Stage);
                float elapsed = Mathf.Max(0f, (currentTick - state.StateStartTick) * dt);
                visual.SyncFromAuthority(visualState, elapsed, state.IsActivatable, suppressBurst);
            }
        }

        _hasSyncedAuthoritativeRifts = true;
    }

    public void ResetPresentation()
    {
        ClearSpawnedRifts();
        _hasGeneratedRifts = false;
        _hasResolvedInitialPhase = false;
        _hasSyncedAuthoritativeRifts = false;
        _lastPhase = DungeonPressurePhase.Normal;
        _activeRaidGenerationId = string.Empty;
    }

    private void ResolveDependencies()
    {
        if (_pressureController == null)
        {
            _pressureController = GetComponent<DungeonPressureController>()
                ?? FindAnyObjectByType<DungeonPressureController>();
        }

        if (_matchController == null)
        {
            _matchController = _pressureController != null
                ? _pressureController.GetComponent<NetworkMatchController>()
                : FindAnyObjectByType<NetworkMatchController>();
        }

        if (_hazardDirector == null)
        {
            _hazardDirector = _pressureController != null
                ? _pressureController.GetComponent<CollapseHazardDirector>()
                : FindAnyObjectByType<CollapseHazardDirector>();
        }
    }

    private void LogMissingConfigOnce()
    {
        if (_loggedMissingConfig)
        {
            return;
        }

        _loggedMissingConfig = true;
        Debug.LogError("[CollapseRiftPresenter] _config is NULL! Assign DefaultCollapseRiftConfig.asset in the Inspector or no collapse rifts will spawn.", this);
    }

    private bool TryGetControllerPhase(out DungeonPressurePhase phase)
    {
        if (_pressureController != null && _pressureController.TryGetPhase(out phase))
        {
            return true;
        }

        phase = _lastPhase;
        return false;
    }

    private string ReadCurrentRaidGenerationId()
    {
        if (_matchController != null && _matchController.Object != null && _matchController.Object.IsValid)
        {
            try
            {
                return _matchController.RaidGenerationId.ToString();
            }
            catch (System.InvalidOperationException)
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    private void CheckRaidGenerationChange()
    {
        string raidId = ReadCurrentRaidGenerationId();
        if (string.IsNullOrWhiteSpace(raidId) || raidId == _activeRaidGenerationId)
        {
            return;
        }

        // Re-generate once the replicated RaidGenerationId becomes available so Host & Clients match 100%.
        _activeRaidGenerationId = raidId;
        _hasGeneratedRifts = false;
        DungeonPressurePhase current = TryGetControllerPhase(out DungeonPressurePhase phase) ? phase : _lastPhase;
        ApplyInitialPhase(current);
    }

    private void EnsureGeneratedForCurrentRaid()
    {
        if (_hasGeneratedRifts || _config == null)
        {
            return;
        }

        ClearSpawnedRifts();
        EnsureRiftsRoot();

        string raidId = ReadCurrentRaidGenerationId();
        if (!string.IsNullOrWhiteSpace(raidId))
        {
            _activeRaidGenerationId = raidId;
        }

        _activeSeed = CollapseRiftPhaseRules.ComputeDeterministicSeed(_activeRaidGenerationId, _fallbackSeed);

        ICollapseSurfaceGrid grid = _customGrid ?? TilemapCollapseSurfaceGrid.BuildFromTilemaps(
            FindObjectsByType<Tilemap>());

        List<Vector2> floorExclusions = CollectFloorExclusionCenters();
        List<Vector2> wallExclusions = CollectWallExclusionCenters();

        CollapseRiftPlacementSettings ps = _config.BuildPlacementSettings();
        CollapseRiftPlacementService.GeneratePlacements(
            grid,
            ps,
            floorExclusions,
            wallExclusions,
            _activeSeed,
            _floorPlacementsBuffer,
            _wallPlacementsBuffer);

        bool useProcedural = _config.RenderMode == CollapseRiftRenderMode.Procedural;
        if (useProcedural)
        {
            CollapseRiftRuntimeKit floorKit = _config.ProceduralFloorKit != null
                ? _config.ProceduralFloorKit.BuildRuntimeKit()
                : CollapseRiftPlaceholderKitFactory.CreateFloorKit();
            CollapseRiftRuntimeKit wallKit = _config.ProceduralWallKit != null
                ? _config.ProceduralWallKit.BuildRuntimeKit()
                : CollapseRiftPlaceholderKitFactory.CreateWallKit();
            int maxWallHeightPx = ps.WallFaceHeightCells * wallKit.PixelsPerWorldCell;
            _activeAtlas = CollapseRiftAtlas.Build(
                _floorPlacementsBuffer,
                _wallPlacementsBuffer,
                floorKit,
                wallKit,
                _activeSeed,
                _config.ProceduralCellPixelSize,
                maxWallHeightPx);
        }

        Vector2 cycleRange = _config.Stage3CycleIntervalRange;
        for (int i = 0; i < _floorPlacementsBuffer.Count; i++)
        {
            CollapseRiftPlacement placement = _floorPlacementsBuffer[i];
            CollapseFloorRiftVisual visual = CreateFloorVisual(placement, i);
            float initialDelay = Mathf.Lerp(
                Mathf.Max(1f, cycleRange.x),
                Mathf.Max(cycleRange.x, cycleRange.y),
                placement.PhaseOffsetSeconds % 1f);
            visual.Initialize(_config, placement.Batch, placement.PhaseOffsetSeconds, initialDelay);
            if (useProcedural && _activeAtlas != null && i < _activeAtlas.FloorSprites.Count)
            {
                visual.SetProceduralVisual(_activeAtlas.FloorSprites[i], _config.ProceduralFloorMaterial);
            }
            _floorRifts.Add(visual);
        }

        float wallInterval = EffectiveWallFrameIntervalSeconds;
        for (int i = 0; i < _wallPlacementsBuffer.Count; i++)
        {
            CollapseRiftPlacement placement = _wallPlacementsBuffer[i];
            CollapseWallRiftVisual visual = CreateWallVisual(placement, i);
            visual.Initialize(_config, placement.PhaseOffsetSeconds, wallInterval);
            if (useProcedural && _activeAtlas != null && i < _activeAtlas.WallSprites.Count)
            {
                visual.SetProceduralVisual(_activeAtlas.WallSprites[i], _config.ProceduralWallMaterial);
            }
            _wallRifts.Add(visual);
        }

        _hasGeneratedRifts = true;

        if (Application.isPlaying)
        {
            string sampleFloor = _floorPlacementsBuffer.Count > 0 ? _floorPlacementsBuffer[0].WorldPosition.ToString("F1") : "none";
            string sampleWall = _wallPlacementsBuffer.Count > 0 ? _wallPlacementsBuffer[0].WorldPosition.ToString("F1") : "none";
            Debug.Log(
                $"[CollapseRiftPresenter] Generated collapse rifts ({_config.RenderMode}) for RaidId='{_activeRaidGenerationId}' (seed={_activeSeed}). " +
                $"GridBounds={grid.CellBounds}, Floor={_floorRifts.Count}/{ps.Stage2FloorCount + ps.Stage3FloorCount} (first={sampleFloor}), " +
                $"Wall={_wallRifts.Count}/{ps.WallCount} (first={sampleWall}), Exclusions(floor={floorExclusions.Count}, wall={wallExclusions.Count}).",
                this);
        }
    }

    private CollapseFloorRiftVisual CreateFloorVisual(CollapseRiftPlacement placement, int index)
    {
        CollapseFloorRiftVisual visual;
        if (_config.FloorRiftPrefab != null)
        {
            visual = Instantiate(_config.FloorRiftPrefab, new Vector3(placement.WorldPosition.x, placement.WorldPosition.y, 0f), Quaternion.identity, _riftsRoot);
            visual.name = $"FloorRift_{placement.Batch}_{index}";
        }
        else
        {
            var go = new GameObject($"FloorRift_{placement.Batch}_{index}");
            go.transform.SetParent(_riftsRoot, false);
            go.transform.position = new Vector3(placement.WorldPosition.x, placement.WorldPosition.y, 0f);
            visual = go.AddComponent<CollapseFloorRiftVisual>();
        }

        if (!Application.isPlaying)
        {
            visual.gameObject.hideFlags = HideFlags.DontSave;
        }

        return visual;
    }

    private CollapseWallRiftVisual CreateWallVisual(CollapseRiftPlacement placement, int index)
    {
        CollapseWallRiftVisual visual;
        if (_config.WallRiftPrefab != null)
        {
            visual = Instantiate(_config.WallRiftPrefab, new Vector3(placement.WorldPosition.x, placement.WorldPosition.y, 0f), Quaternion.identity, _riftsRoot);
            visual.name = $"WallRift_{index}";
        }
        else
        {
            var go = new GameObject($"WallRift_{index}");
            go.transform.SetParent(_riftsRoot, false);
            go.transform.position = new Vector3(placement.WorldPosition.x, placement.WorldPosition.y, 0f);
            visual = go.AddComponent<CollapseWallRiftVisual>();
        }

        if (!Application.isPlaying)
        {
            visual.gameObject.hideFlags = HideFlags.DontSave;
        }

        return visual;
    }

    private void EnsureRiftsRoot()
    {
        if (_riftsRoot != null)
        {
            return;
        }

        Transform existing = transform.Find("CollapseRiftsRoot");
        if (existing != null)
        {
            _riftsRoot = existing;
            return;
        }

        var go = new GameObject("CollapseRiftsRoot");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        if (!Application.isPlaying)
        {
            go.hideFlags = HideFlags.DontSave;
        }
        _riftsRoot = go.transform;
    }

    private void ClearSpawnedRifts()
    {
        _floorRifts.Clear();
        _wallRifts.Clear();

        if (_activeAtlas != null)
        {
            _activeAtlas.Dispose();
            _activeAtlas = null;
        }

        if (_riftsRoot == null)
        {
            Transform existing = transform.Find("CollapseRiftsRoot");
            if (existing != null)
            {
                _riftsRoot = existing;
            }
        }

        if (_riftsRoot != null)
        {
            GameObject oldRoot = _riftsRoot.gameObject;
            _riftsRoot = null;

            // Rename and deactivate immediately (without reparenting, which is forbidden during OnDisable/OnDestroy)
            // so same-frame EnsureRiftsRoot() never re-parents new rifts under a root scheduled for deferred Destroy().
            oldRoot.name = "CollapseRiftsRoot_Disposing";
            oldRoot.SetActive(false);

            if (Application.isPlaying)
            {
                Destroy(oldRoot);
            }
            else
            {
                DestroyImmediate(oldRoot);
            }
        }
    }

    private void ApplyWallFrameIntervalToAll()
    {
        float interval = EffectiveWallFrameIntervalSeconds;
        for (int i = 0; i < _wallRifts.Count; i++)
        {
            if (_wallRifts[i] != null)
            {
                _wallRifts[i].SetFrameIntervalSeconds(interval);
            }
        }
    }

    private List<Vector2> CollectFloorExclusionCenters()
    {
        var centers = new List<Vector2>();
        if (_customGrid != null)
        {
            return centers;
        }

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

    private List<Vector2> CollectWallExclusionCenters()
    {
        var centers = new List<Vector2>();
        if (_customGrid != null)
        {
            return centers;
        }

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

#if UNITY_EDITOR
    [ContextMenu("Preview/Phase: Normal")]
    public void PreviewNormal() => PreviewPhase(DungeonPressurePhase.Normal);

    [ContextMenu("Preview/Phase: Reinforcements")]
    public void PreviewReinforcements() => PreviewPhase(DungeonPressurePhase.Reinforcements);

    [ContextMenu("Preview/Phase: Critical Pressure")]
    public void PreviewCriticalPressure() => PreviewPhase(DungeonPressurePhase.CriticalPressure);

    [ContextMenu("Preview/Phase: Collapse")]
    public void PreviewCollapse() => PreviewPhase(DungeonPressurePhase.Collapse);

    [ContextMenu("Preview/Clear Preview")]
    public void ClearPreview()
    {
        ResetPresentation();
        UnityEditor.SceneView.RepaintAll();
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
    }

    public void PreviewPhase(DungeonPressurePhase phase)
    {
        if (_config == null)
        {
            return;
        }

        ApplyInitialPhase(phase);
        if (phase == DungeonPressurePhase.CriticalPressure || phase == DungeonPressurePhase.Collapse)
        {
            // Trigger telegraph/eruption preview snapshot on active floor rifts so lighting & glow can be calibrated in Edit Mode.
            for (int i = 0; i < _floorRifts.Count; i++)
            {
                if (_floorRifts[i] != null && _floorRifts[i].IsActivatable)
                {
                    _floorRifts[i].ForceVisualState(i % 2 == 0
                        ? CollapseRiftVisualState.Telegraph
                        : CollapseRiftVisualState.Erupting);
                }
            }
        }

        UnityEditor.SceneView.RepaintAll();
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
    }

    private void OnValidate()
    {
        if (_wallFrameIntervalOverrideSeconds < 0f)
        {
            _wallFrameIntervalOverrideSeconds = 0f;
        }

        if (_wallRifts.Count > 0)
        {
            ApplyWallFrameIntervalToAll();
        }
    }
#endif
}
