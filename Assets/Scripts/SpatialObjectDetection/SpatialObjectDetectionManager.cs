using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.SpatialAwareness;
using Microsoft.MixedReality.Toolkit.Utilities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class SpatialObjectDetectionManager : MonoBehaviour,
    IMixedRealitySpatialAwarenessObservationHandler<SpatialAwarenessMeshObject>
{
    private static readonly ProfilerMarker BaselineCaptureProfilerMarker =
        new ProfilerMarker("MRFlood.SpatialMapping.CaptureBaseline");
    private static readonly ProfilerMarker DifferenceScanProfilerMarker =
        new ProfilerMarker("MRFlood.SpatialMapping.ScanDifferences");
    private static readonly ProfilerMarker RelevantMeshUpdateProfilerMarker =
        new ProfilerMarker("MRFlood.SpatialMapping.RelevantMeshUpdated");

    [Header("References")]
    [SerializeField] private CityAnchorManager cityAnchorManager;
    [SerializeField] private CityPlacementManager cityPlacementManager;
    [SerializeField] private Transform selectedPlaneTransform;
    [SerializeField] private Transform scanCenterTransform;
    [Tooltip("Optional child plane/mesh that defines the focused scan area. When assigned, its projected mesh bounds replace Scan Half Extents.")]
    [SerializeField] private Transform scanAreaReferenceTransform;
    [SerializeField] private bool useScanAreaReferenceAsCenter = true;

    [Header("Mode")]
    [SerializeField] private SpatialPlacementMode currentMode = SpatialPlacementMode.None;
    [SerializeField] private bool requireConfirmedCityPlacement = true;

    [Header("Plane Filtering")]
    [Tooltip("Fallback half-size in the selected plane's local X/Z axes. Used only when Scan Area Reference Transform is not assigned or has no usable mesh/collider bounds.")]
    [SerializeField] private Vector2 scanHalfExtents = new Vector2(0.75f, 0.75f);
    [SerializeField] private float maxBottomDistanceFromPlane = 0.08f;
    [SerializeField] private float minHeightAbovePlane = 0.02f;
    [SerializeField] private float maxHeightAbovePlane = 0.5f;
    [SerializeField] private float minApproximateSize = 0.02f;
    [SerializeField] private float maxApproximateSize = 0.4f;

    [Header("Debug Objects")]
    [SerializeField] private bool useDebugObjects = true;
    [SerializeField] private Transform debugObjectsRoot;
    [SerializeField] private bool includeInactiveDebugObjects = false;
    [SerializeField] private Vector3 debugObjectFallbackSize = new Vector3(0.06f, 0.06f, 0.06f);

    [Header("Real Spatial Scan")]
    [SerializeField] private bool useRealSpatialScan = true;
    [SerializeField] private bool useMrtkSpatialAwarenessLayer = true;
    [SerializeField] private LayerMask realSpatialLayers;

    [Header("Spatial Observer Control")]
    [SerializeField] private bool keepSpatialObserverRunningInPlacementMode = true;
    [SerializeField] private bool forceColliderSpatialMeshForDetection = true;
    [SerializeField] private SpatialAwarenessMeshDisplayOptions spatialObserverDisplayOptionWhileDetecting =
        SpatialAwarenessMeshDisplayOptions.Occlusion;
    [Tooltip("Hide the spatial mesh after both building candidates are available, then restore the detecting display option when that selection is cleared.")]
    [SerializeField] private bool hideSpatialObserverAfterTwoBuildingCandidates = true;
    [SerializeField] private bool clearSpatialObserverMeshesOnModeEnter = false;
    [SerializeField] private bool restoreSpatialObserverDisplayOptionOnExit = true;
    [SerializeField] private bool suspendSpatialObserverOnExit = false;

    [Header("Spatial Observer Performance")]
    [Tooltip("Limits mesh generation to the tabletop scan volume while tangible-object detection is active.")]
    [SerializeField] private bool focusSpatialObserverOnScanArea = true;
    [Tooltip("Clears queued room-scale mesh work once when the observer is first focused on the scan area.")]
    [SerializeField] private bool clearSpatialObserverMeshesWhenFocusChanges = true;
    [SerializeField, Min(0.05f)] private float spatialObserverUpdateIntervalWhileDetecting = 0.2f;
    [SerializeField, Min(0f)] private float spatialObserverBoundsPadding = 0.1f;
    [Tooltip("Spatial detection uses colliders and does not need the observer's extra CPU normal recalculation.")]
    [SerializeField] private bool disableSpatialObserverNormalRecalculationWhileDetecting = true;
    [Tooltip("Wake refresh scans as soon as MRTK finishes a relevant mesh update instead of waiting for the next polling interval.")]
    [SerializeField] private bool reactToSpatialMeshUpdateEvents = true;
    [SerializeField, Min(0f)] private float spatialMeshUpdateSettleSeconds = 0.04f;
    [Tooltip("Caps event-driven full-grid scans so a burst of updated mesh chunks cannot trigger one scan per chunk.")]
    [SerializeField, Min(0.02f)] private float minimumSpatialScanIntervalSeconds = 0.12f;
    [Tooltip("When mesh events are registered but a provider misses one, allow temporal confirmation to advance after this polling fallback delay.")]
    [SerializeField, Min(0.2f)] private float spatialMeshRevisionFallbackSeconds = 1f;

    [Header("Spatial Scan Refresh")]
    [SerializeField] private bool scanRealSpatialObjectsOverTime = true;
    [SerializeField] private bool resumeSpatialObserverBeforeScan = true;
    [SerializeField] private float spatialObserverWarmupSeconds = 0.35f;
    [SerializeField] private float spatialScanRefreshDurationSeconds = 10f;
    [SerializeField] private float spatialScanRefreshIntervalSeconds = 0.2f;
    [SerializeField] private bool keepBestSpatialScanResult = true;
    [SerializeField] private bool completeScanWhenTargetCandidatesStable = true;
    [SerializeField] private int buildingScanTargetCandidateCount = 2;
    [SerializeField] private int floodScanTargetCandidateCount = 1;
    [SerializeField] private int stableTargetCandidatePasses = 1;

    [Header("Spatial Mesh Difference")]
    [SerializeField] private bool useSpatialMeshDifference = true;
    [SerializeField] private bool autoCaptureBaselineOnModeEnter = true;
    [SerializeField] private bool fallbackToColliderScanWithoutBaseline = true;
    [SerializeField] private bool fallbackToColliderScanWhenDiffFindsNothing = true;
    [Tooltip("After enough distributed tabletop samples are available, fill only small local holes from the nearest measured height.")]
    [SerializeField] private bool fallbackToSelectedPlaneBaseline = true;
    [Tooltip("Only baseline holes within this world-space distance of a measured sample are interpolated. Larger holes remain invalid so later mesh updates cannot appear as objects.")]
    [SerializeField, Min(0f)] private float maximumBaselineHoleFillDistanceWorld = 0.03f;
    [SerializeField, Range(0.01f, 1f)] private float minimumMeasuredBaselineCoverage = 0.5f;
    [SerializeField, Range(0.01f, 1f)] private float minimumMeasuredCoveragePerQuadrant = 0.25f;
    [SerializeField, Min(1)] private int automaticBaselineMaxAttempts = 8;
    [SerializeField, Min(0.1f)] private float automaticBaselineRetryIntervalSeconds = 0.75f;
    [Tooltip("Editor/test fallback only: use normal physics layers when no explicit layer or MRTK spatial-mesh layer is available.")]
    [SerializeField] private bool useDefaultRaycastLayersWhenSpatialObserverUnavailable;
    [SerializeField] private bool clearBaselineOnCancel;
    [SerializeField] private int baselineGridResolutionX = 32;
    [SerializeField] private int baselineGridResolutionZ = 32;
    [SerializeField, Min(0.005f)] private float maximumBaselineSampleSpacingWorld = 0.015f;
    [SerializeField, Min(2)] private int maximumBaselineGridResolution = 128;
    [SerializeField] private float baselineRayHeightAbovePlane = 0.7f;
    [SerializeField] private float baselineRayDepthBelowPlane = 0.25f;
    [SerializeField] private float minSurfaceChangeHeight = 0.025f;
    [SerializeField] private float maxSurfaceChangeHeight = 0.6f;
    [Tooltip("A grid cell must report a valid height change in this many consecutive scans before it can contribute to a candidate. This rejects transient spatial-mesh rebuilds.")]
    [SerializeField, Min(1)] private int requiredConsecutiveChangedSamplePasses = 2;
    [SerializeField, Min(2)] private int minChangedSamplesPerCluster = 2;
    [SerializeField] private float candidateBoundsPadding = 0.015f;
    [SerializeField] private bool logSpatialScanDetails = true;

    [Header("Ignored Virtual Roots")]
    [SerializeField] private Transform[] ignoredRoots;
    [SerializeField] private bool ignoreSelectedPlaneChildren = true;
    [SerializeField] private bool ignoreCityAnchorChildren = true;

    [Header("Candidate Debug Visuals")]
    [SerializeField] private bool createCandidateDebugVisuals = false;
    [SerializeField] private GameObject candidateDebugVisualPrefab;
    [SerializeField] private Transform candidateDebugVisualRoot;
    [SerializeField] private float defaultDebugVisualSize = 0.035f;

    [Header("Spatial Scan Surface Debug")]
    [SerializeField] private bool showSpatialScanSurfaceDebug = false;
    [SerializeField] private Transform spatialScanSurfaceDebugRoot;
    [SerializeField] private Material baselineSurfaceDebugMaterial;
    [SerializeField] private Material changedSurfaceDebugMaterial;
    [SerializeField] private Color baselineSurfaceDebugColor = new Color(0f, 0.6f, 1f, 0.35f);
    [SerializeField] private Color changedSurfaceDebugColor = new Color(1f, 0.35f, 0f, 0.55f);
    [SerializeField] private float surfaceDebugVerticalOffset = 0.004f;

    [Header("Spatial Scan Ray Debug")]
    [SerializeField] private bool showSpatialScanRayDebug = false;
    [SerializeField] private Transform spatialScanRayDebugRoot;
    [SerializeField] private Material spatialScanRayHitDebugMaterial;
    [SerializeField] private Material spatialScanRayMissDebugMaterial;
    [SerializeField] private Color spatialScanRayHitDebugColor = new Color(0.1f, 1f, 0.25f, 0.9f);
    [SerializeField] private Color spatialScanRayMissDebugColor = new Color(1f, 0.05f, 0.05f, 0.5f);
    [SerializeField] private float spatialScanRayDebugDurationSeconds = 5f;
    [SerializeField] private float spatialScanRayHitMarkerSize = 0.015f;
    [SerializeField, Min(1)] private int spatialScanRayDebugStride = 1;

    public event Action<SpatialPlacementMode> ModeChanged;
    public event Action<IReadOnlyList<PhysicalObjectCandidate>, SpatialPlacementMode> CandidatesUpdated;
    public event Action<IReadOnlyList<PhysicalObjectCandidate>, SpatialPlacementMode> CandidatesConfirmed;
    public event Action<bool> BaselineCaptureCompleted;

    public SpatialPlacementMode CurrentMode => currentMode;
    public IReadOnlyList<PhysicalObjectCandidate> CurrentCandidates => currentCandidates;
    public bool HasSpatialBaseline => hasSpatialBaseline;
    public bool IsPreparingAutomaticBaseline => autoBaselineCaptureCoroutine != null;
    public bool IsRefreshingSpatialScan => scanRefreshCoroutine != null;
    public bool IsSuspendedForVisualization => isSuspendedForVisualization;
    public bool CanScanWithoutSpatialBaseline =>
        useDebugObjects ||
        !useRealSpatialScan ||
        !useSpatialMeshDifference ||
        fallbackToColliderScanWithoutBaseline;
    public bool IsCandidateScanReady =>
        currentMode != SpatialPlacementMode.None &&
        !IsPreparingAutomaticBaseline &&
        (hasSpatialBaseline || CanScanWithoutSpatialBaseline);
    public float RecommendedCandidateScanInterval => Mathf.Lerp(
        0.2f,
        0.4f,
        Mathf.InverseLerp(4096f, 16384f, baselineGridX * baselineGridZ));
    public uint SpatialMeshRevision => spatialMeshRevision;
    public bool IsSpatialMeshUpdateSettled =>
        Time.realtimeSinceStartup - lastRelevantSpatialMeshUpdateTime >=
        Mathf.Max(0f, spatialMeshUpdateSettleSeconds);
    public bool UsesSpatialMeshRevisionScanGating =>
        reactToSpatialMeshUpdateEvents &&
        spatialMeshEventsRegistered &&
        useRealSpatialScan &&
        useSpatialMeshDifference &&
        hasSpatialBaseline &&
        !useDebugObjects &&
        !fallbackToColliderScanWhenDiffFindsNothing;
    public bool UseDebugObjects
    {
        get => useDebugObjects;
        set => useDebugObjects = value;
    }

    public Transform DebugObjectsRoot => debugObjectsRoot;
    public Transform SelectedPlaneTransform => selectedPlaneTransform;
    public Transform ScanAreaReferenceTransform => scanAreaReferenceTransform;
    public Transform ScanCenterTransform => GetScanCenterReferenceTransform();
    public Vector3 ScanCenterWorldPosition => GetScanCenterWorldPosition();
    public Vector2 ConfiguredScanHalfExtents => scanHalfExtents;
    public Vector2 ScanHalfExtents => GetScanHalfExtents();
    public Vector2 ScanHalfExtentsWorld => GetWorldScanHalfExtents(GetScanHalfExtents());
    public float MaxBottomDistanceFromPlane => maxBottomDistanceFromPlane;
    public float MaxHeightAbovePlane => maxHeightAbovePlane;

    private readonly List<PhysicalObjectCandidate> currentCandidates = new List<PhysicalObjectCandidate>();
    private readonly Dictionary<Transform, Bounds> realObjectBounds = new Dictionary<Transform, Bounds>();
    private readonly List<ChangedSurfaceSample> changedSamples = new List<ChangedSurfaceSample>();
    private readonly List<int> clusterIndices = new List<int>();
    private readonly Queue<int> clusterQueue = new Queue<int>();
    private readonly List<SpatialScanRayDebugSample> spatialScanRayHitDebugSamples = new List<SpatialScanRayDebugSample>();
    private readonly List<SpatialScanRayDebugSample> spatialScanRayMissDebugSamples = new List<SpatialScanRayDebugSample>();
    private readonly RaycastHit[] spatialRaycastHits = new RaycastHit[32];
    private readonly Dictionary<int, bool> spatialColliderIgnoreCache = new Dictionary<int, bool>();
    private readonly Dictionary<int, int> changedSampleIndexByGridIndex = new Dictionary<int, int>();

    private IMixedRealitySpatialAwarenessMeshObserver spatialMeshObserver;
    private Coroutine scanRefreshCoroutine;
    private int scanRefreshGeneration;
    private Coroutine autoBaselineCaptureCoroutine;
    private bool automaticBaselineAllowsInactiveMode;
    private bool isSuspendedForVisualization;
    private readonly List<Transform> visualizationIgnoredRoots = new List<Transform>();
    private bool isRefreshingSpatialScanPass;
    private bool hasTwoBuildingSelectionCandidates;
    private bool spatialObserverHiddenForBuildingSelection;
    private bool hasSavedSpatialObserverDisplayOption;
    private SpatialAwarenessMeshDisplayOptions savedSpatialObserverDisplayOption;
    private bool hasSavedSpatialObserverPerformanceSettings;
    private bool savedSpatialObserverIsStationary;
    private VolumeType savedSpatialObserverVolumeType;
    private Vector3 savedSpatialObserverOrigin;
    private Quaternion savedSpatialObserverRotation;
    private Vector3 savedSpatialObserverExtents;
    private float savedSpatialObserverUpdateInterval;
    private bool savedSpatialObserverRecalculateNormals;
    private bool spatialMeshEventsRegistered;
    private bool spatialObserverMeshesResetByLatestConfiguration;
    private bool hasSpatialObserverFocusBounds;
    private Bounds spatialObserverFocusBounds;
    private uint spatialMeshRevision;
    private float lastRelevantSpatialMeshUpdateTime = float.NegativeInfinity;
    private bool hasSpatialDifferencePersistenceRevision;
    private uint lastSpatialDifferencePersistenceRevision;
    private float lastSpatialDifferencePersistenceAdvanceTime = float.NegativeInfinity;
    private int scanSequence;
    private int lastSpatialDifferenceRawChangedSampleCount;

    private bool hasSpatialBaseline;
    private int baselineGridX;
    private int baselineGridZ;
    private float[] baselineHeights;
    private bool[] baselineValid;
    private byte[] consecutiveChangedSamplePasses;
    private bool[] visitedChangedSamples;
    private GameObject baselineSurfaceDebugObject;
    private GameObject changedSurfaceDebugObject;
    private Mesh baselineSurfaceDebugMesh;
    private Mesh changedSurfaceDebugMesh;
    private Material runtimeBaselineSurfaceDebugMaterial;
    private Material runtimeChangedSurfaceDebugMaterial;
    private GameObject spatialScanRayHitDebugObject;
    private GameObject spatialScanRayMissDebugObject;
    private Mesh spatialScanRayHitDebugMesh;
    private Mesh spatialScanRayMissDebugMesh;
    private Material runtimeSpatialScanRayHitDebugMaterial;
    private Material runtimeSpatialScanRayMissDebugMaterial;
    private bool isRecordingSpatialScanRayDebugPass;
    private int spatialScanRayDebugSampleCount;
    private struct ChangedSurfaceSample
    {
        public int ix;
        public int iz;
        public float localX;
        public float localZ;
        public float baselineHeight;
        public float currentHeight;
        public float deltaHeight;
    }

    private struct SpatialScanRayDebugSample
    {
        public Vector3 origin;
        public Vector3 end;
        public Vector3 hitPoint;
        public bool hasHit;
    }

    private struct SpatialScanGeometry
    {
        public Vector3 center;
        public Vector3 right;
        public Vector3 up;
        public Vector3 forward;
        public Vector3 planeOrigin;
    }

    private void Awake()
    {
        ResolveMissingReferences();
    }

    private void OnEnable()
    {
        TryRegisterSpatialMeshEvents();

        if (Application.isPlaying &&
            currentMode != SpatialPlacementMode.None &&
            !isSuspendedForVisualization)
        {
            RefreshSpatialObserverForCurrentMode(false);
        }

        if (Application.isPlaying)
            PrepareSpatialBaseline();
    }

    private IEnumerator Start()
    {
        TryRegisterSpatialMeshEvents();

        // Saved placement localization completes asynchronously. Waiting here
        // covers both restored and newly confirmed city placements, while the
        // explicit confirmation callback below starts preparation immediately.
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(0.25f);
        while (isActiveAndEnabled && !IsCityPlacementConfirmed())
            yield return wait;

        if (isActiveAndEnabled)
            PrepareSpatialBaseline();
    }

    private void OnDisable()
    {
        StopAutomaticBaselineCapture();
        StopRefreshingSpatialScan();
        UnregisterSpatialMeshEvents();
        RestoreSpatialObserverPerformanceSettings();
        RestoreSpatialObserverDisplayOption();
    }

    private void OnDestroy()
    {
        StopAutomaticBaselineCapture();
        StopRefreshingSpatialScan();
        UnregisterSpatialMeshEvents();
        RestoreSpatialObserverPerformanceSettings();
        RestoreSpatialObserverDisplayOption();
        ClearBaselineSurfaceDebugVisual();
        ClearChangedSurfaceDebugVisual();
        ClearSpatialScanRayDebugVisual();

        if (runtimeBaselineSurfaceDebugMaterial != null)
            DestroyObject(runtimeBaselineSurfaceDebugMaterial);

        if (runtimeChangedSurfaceDebugMaterial != null)
            DestroyObject(runtimeChangedSurfaceDebugMaterial);

        if (runtimeSpatialScanRayHitDebugMaterial != null)
            DestroyObject(runtimeSpatialScanRayHitDebugMaterial);

        if (runtimeSpatialScanRayMissDebugMaterial != null)
            DestroyObject(runtimeSpatialScanRayMissDebugMaterial);
    }

    public void OnObservationAdded(
        MixedRealitySpatialAwarenessEventData<SpatialAwarenessMeshObject> eventData)
    {
        RecordRelevantSpatialMeshUpdate(eventData?.SpatialObject);
    }

    public void OnObservationUpdated(
        MixedRealitySpatialAwarenessEventData<SpatialAwarenessMeshObject> eventData)
    {
        RecordRelevantSpatialMeshUpdate(eventData?.SpatialObject);
    }

    public void OnObservationRemoved(
        MixedRealitySpatialAwarenessEventData<SpatialAwarenessMeshObject> eventData)
    {
        // A removed chunk has no collider to sample. Its replacement will raise
        // Added/Updated after the new collider is ready.
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
            return;

        if (showSpatialScanSurfaceDebug)
        {
            RebuildBaselineSurfaceDebugVisual();
            RebuildChangedSurfaceDebugVisual();
        }
        else
        {
            ClearBaselineSurfaceDebugVisual();
            ClearChangedSurfaceDebugVisual();
        }

        if (showSpatialScanRayDebug)
            RebuildSpatialScanRayDebugVisual();
        else
            ClearSpatialScanRayDebugVisual();
    }

    public void EnterBuildingPlacingMode()
    {
        SetMode(SpatialPlacementMode.BuildingPlacing);
    }

    public void EnterFloodPlacingMode()
    {
        SetMode(SpatialPlacementMode.FloodPlacing);
    }

    public void SetMode(SpatialPlacementMode mode)
    {
        if (isSuspendedForVisualization)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: mode changes are suspended during Visualization Mode.");
            return;
        }

        ResolveMissingReferences();

        if (currentMode == mode)
        {
            Debug.Log($"SpatialObjectDetectionManager: {mode} is already active; preserving the current baseline and city state.");

            RefreshSpatialObserverForCurrentMode(false);
            if (currentMode != SpatialPlacementMode.None &&
                !hasSpatialBaseline &&
                autoBaselineCaptureCoroutine == null &&
                autoCaptureBaselineOnModeEnter &&
                useRealSpatialScan &&
                useSpatialMeshDifference)
            {
                ScheduleAutomaticBaselineCapture();
            }

            return;
        }

        bool preserveInFlightPrewarm =
            autoBaselineCaptureCoroutine != null &&
            automaticBaselineAllowsInactiveMode &&
            !hasSpatialBaseline;
        if (!preserveInFlightPrewarm)
            StopAutomaticBaselineCapture();
        currentMode = mode;
        hasTwoBuildingSelectionCandidates = false;
        ResetMeshChangePersistence();
        ClearDetectedCandidates();
        ModeChanged?.Invoke(currentMode);

        Debug.Log($"SpatialObjectDetectionManager: mode changed to {currentMode}.");

        RefreshSpatialObserverForCurrentMode(clearSpatialObserverMeshesOnModeEnter);

        if (currentMode != SpatialPlacementMode.None &&
            !hasSpatialBaseline &&
            autoBaselineCaptureCoroutine == null &&
            autoCaptureBaselineOnModeEnter &&
            useRealSpatialScan &&
            useSpatialMeshDifference)
        {
            ScheduleAutomaticBaselineCapture();
        }
    }

    public void SetDebugObjectsRoot(Transform root)
    {
        if (isSuspendedForVisualization)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: debug-root changes are suspended during Visualization Mode.");
            return;
        }

        debugObjectsRoot = root;
        ClearDetectedCandidates();

        Debug.Log(
            debugObjectsRoot != null
                ? $"SpatialObjectDetectionManager: debug objects root set to '{debugObjectsRoot.name}'."
                : "SpatialObjectDetectionManager: debug objects root cleared.");
    }

    public bool CaptureSpatialBaseline()
    {
        StopAutomaticBaselineCapture();
        using (BaselineCaptureProfilerMarker.Auto())
            return CaptureSpatialBaselineInternal(true);
    }

    /// <summary>
    /// Controls spatial-mesh visibility once a spatial building-selection
    /// technique has produced both of its building candidates.
    /// </summary>
    public void SetTwoBuildingSelectionCandidatesAvailable(bool available)
    {
        hasTwoBuildingSelectionCandidates =
            available && currentMode == SpatialPlacementMode.BuildingPlacing;

        if (!useMrtkSpatialAwarenessLayer || currentMode == SpatialPlacementMode.None)
            return;

        IMixedRealitySpatialAwarenessMeshObserver observer = GetSpatialMeshObserver();
        if (observer == null)
            return;

        ApplySpatialObserverDisplayOption(observer);
    }

    /// <summary>
    /// Starts mapping the empty tabletop as soon as city placement is
    /// confirmed, before a tangible-selection mode asks the user to place an
    /// object. Entering a mode later reuses this baseline or restarts capture
    /// if preparation has not completed yet.
    /// </summary>
    public void PrepareSpatialBaseline()
    {
        ResolveMissingReferences();

        if (hasSpatialBaseline || IsPreparingAutomaticBaseline ||
            isSuspendedForVisualization ||
            CanScanWithoutSpatialBaseline ||
            !autoCaptureBaselineOnModeEnter || !useRealSpatialScan ||
            !useSpatialMeshDifference || !IsCityPlacementConfirmed())
        {
            return;
        }

        ScheduleAutomaticBaselineCapture(true);
    }

    private bool CaptureSpatialBaselineInternal(
        bool publishCompletion,
        bool allowWithoutActiveMode = false)
    {
        if (isSuspendedForVisualization)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: baseline capture is suspended during Visualization Mode.");
            if (publishCompletion)
                BaselineCaptureCompleted?.Invoke(false);
            return false;
        }

        ResolveMissingReferences();
        ClearSpatialBaselineData();

        if (!CanScan(allowWithoutActiveMode))
        {
            if (publishCompletion)
                BaselineCaptureCompleted?.Invoke(false);
            return false;
        }

        int layerMask = GetRealSpatialLayerMask();
        if (layerMask == 0)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: cannot capture baseline because real spatial scan layer mask is empty.");
            if (publishCompletion)
                BaselineCaptureCompleted?.Invoke(false);
            return false;
        }

        Vector2 effectiveScanHalfExtents = GetScanHalfExtents();
        Vector2 worldScanHalfExtents = GetWorldScanHalfExtents(effectiveScanHalfExtents);
        SpatialScanGeometry scanGeometry = CreateSpatialScanGeometry();
        float maximumSpacing = Mathf.Max(0.005f, maximumBaselineSampleSpacingWorld);
        int gridResolutionLimit = Mathf.Max(
            maximumBaselineGridResolution,
            Mathf.Max(baselineGridResolutionX, baselineGridResolutionZ));
        baselineGridX = Mathf.Clamp(
            Mathf.Max(
                baselineGridResolutionX,
                Mathf.CeilToInt(worldScanHalfExtents.x * 2f / maximumSpacing) + 1),
            2,
            gridResolutionLimit);
        baselineGridZ = Mathf.Clamp(
            Mathf.Max(
                baselineGridResolutionZ,
                Mathf.CeilToInt(worldScanHalfExtents.y * 2f / maximumSpacing) + 1),
            2,
            gridResolutionLimit);
        baselineHeights = new float[baselineGridX * baselineGridZ];
        baselineValid = new bool[baselineGridX * baselineGridZ];
        consecutiveChangedSamplePasses = new byte[baselineGridX * baselineGridZ];

        int validCount = 0;
        int sampledSurfaceCount = 0;
        int selectedPlaneFallbackCount = 0;

        spatialColliderIgnoreCache.Clear();
        BeginSpatialScanRayDebugPass();

        for (int z = 0; z < baselineGridZ; z++)
        {
            for (int x = 0; x < baselineGridX; x++)
            {
                GetLocalSamplePosition(x, z, baselineGridX, baselineGridZ, effectiveScanHalfExtents, out float localX, out float localZ);
                int index = GetSampleIndex(x, z, baselineGridX);

                if (TrySampleSurfaceHeight(
                    localX,
                    localZ,
                    layerMask,
                    scanGeometry,
                    out float height,
                    out _))
                {
                    baselineHeights[index] = height;
                    baselineValid[index] = true;
                    validCount++;
                    sampledSurfaceCount++;
                }
            }
        }

        EndSpatialScanRayDebugPass();

        int totalSamples = baselineGridX * baselineGridZ;
        int requiredMeasuredSamples = Mathf.Clamp(
            Mathf.CeilToInt(totalSamples * Mathf.Clamp01(minimumMeasuredBaselineCoverage)),
            1,
            totalSamples);
        bool hasSufficientMeasuredCoverage =
            sampledSurfaceCount >= requiredMeasuredSamples &&
            HasDistributedBaselineCoverage();

        // Only close small spatial-mesh holes after coverage is both high and
        // distributed. Nearest-sample interpolation follows a tilted or
        // vertically offset tabletop without inventing a single global height.
        if (hasSufficientMeasuredCoverage && fallbackToSelectedPlaneBaseline)
        {
            selectedPlaneFallbackCount = FillMissingBaselineFromNearestSamples();
            validCount += selectedPlaneFallbackCount;
        }

        hasSpatialBaseline = hasSufficientMeasuredCoverage && validCount > 0;

        if (logSpatialScanDetails)
        {
            Debug.Log(
                $"SpatialObjectDetectionManager: spatial baseline captured. " +
                $"validSamples={validCount}/{totalSamples}, " +
                $"sampledSurface={sampledSurfaceCount}, requiredMeasured={requiredMeasuredSamples}, " +
                $"localHoleFill={selectedPlaneFallbackCount}, " +
                $"maximumHoleFillDistance={maximumBaselineHoleFillDistanceWorld:0.###}m, " +
                $"grid={baselineGridX}x{baselineGridZ}, worldHalfExtents={worldScanHalfExtents}, " +
                $"configuredScanHalfExtents={scanHalfExtents}, effectiveScanHalfExtents={effectiveScanHalfExtents}, " +
                $"selectedPlane='{selectedPlaneTransform?.name}', " +
                $"scanCenter='{ScanCenterTransform?.name}', scanAreaReference='{scanAreaReferenceTransform?.name}'.");
        }

        if (!hasSpatialBaseline)
        {
            Debug.LogWarning(
                "SpatialObjectDetectionManager: baseline capture did not reach the required measured surface coverage. " +
                "Keep the tabletop clear and check spatial mesh colliders/layers or editor test plane colliders.");
        }

        RebuildBaselineSurfaceDebugVisual();
        ClearChangedSurfaceDebugVisual();
        if (publishCompletion)
            BaselineCaptureCompleted?.Invoke(hasSpatialBaseline);

        return hasSpatialBaseline;
    }

    private bool HasDistributedBaselineCoverage()
    {
        if (baselineValid == null || baselineGridX < 2 || baselineGridZ < 2)
            return false;

        float requiredFraction = Mathf.Clamp01(minimumMeasuredCoveragePerQuadrant);
        int splitX = baselineGridX / 2;
        int splitZ = baselineGridZ / 2;

        for (int quadrantZ = 0; quadrantZ < 2; quadrantZ++)
        {
            int startZ = quadrantZ == 0 ? 0 : splitZ;
            int endZ = quadrantZ == 0 ? splitZ : baselineGridZ;
            for (int quadrantX = 0; quadrantX < 2; quadrantX++)
            {
                int startX = quadrantX == 0 ? 0 : splitX;
                int endX = quadrantX == 0 ? splitX : baselineGridX;
                int cellCount = Mathf.Max(1, (endX - startX) * (endZ - startZ));
                int requiredCount = Mathf.Max(1, Mathf.CeilToInt(cellCount * requiredFraction));
                int measuredCount = 0;

                for (int z = startZ; z < endZ; z++)
                {
                    for (int x = startX; x < endX; x++)
                    {
                        if (baselineValid[GetSampleIndex(x, z, baselineGridX)])
                            measuredCount++;
                    }
                }

                if (measuredCount < requiredCount)
                    return false;
            }
        }

        return true;
    }

    private int FillMissingBaselineFromNearestSamples()
    {
        if (baselineValid == null || baselineHeights == null)
            return 0;

        float maximumFillDistance = Mathf.Max(0f, maximumBaselineHoleFillDistanceWorld);
        if (maximumFillDistance <= 0f)
            return 0;

        bool[] measuredMask = (bool[])baselineValid.Clone();
        int filledCount = 0;
        Vector2 worldHalfExtents = GetWorldScanHalfExtents(GetScanHalfExtents());
        float sampleSpacingX = baselineGridX > 1
            ? worldHalfExtents.x * 2f / (baselineGridX - 1)
            : worldHalfExtents.x * 2f;
        float sampleSpacingZ = baselineGridZ > 1
            ? worldHalfExtents.y * 2f / (baselineGridZ - 1)
            : worldHalfExtents.y * 2f;
        float minimumSpacing = Mathf.Max(
            0.0001f,
            Mathf.Min(sampleSpacingX, sampleSpacingZ));
        int maximumRadius = Mathf.Min(
            Mathf.Max(baselineGridX, baselineGridZ),
            Mathf.CeilToInt(maximumFillDistance / minimumSpacing));
        float maximumDistanceSquared = maximumFillDistance * maximumFillDistance;

        for (int index = 0; index < baselineValid.Length; index++)
        {
            if (baselineValid[index])
                continue;

            int x = index % baselineGridX;
            int z = index / baselineGridX;
            int nearestIndex = -1;
            float nearestDistanceSquared = float.MaxValue;

            for (int radius = 1; radius <= maximumRadius && nearestIndex < 0; radius++)
            {
                int minX = Mathf.Max(0, x - radius);
                int maxX = Mathf.Min(baselineGridX - 1, x + radius);
                int minZ = Mathf.Max(0, z - radius);
                int maxZ = Mathf.Min(baselineGridZ - 1, z + radius);

                for (int candidateZ = minZ; candidateZ <= maxZ; candidateZ++)
                {
                    for (int candidateX = minX; candidateX <= maxX; candidateX++)
                    {
                        int dx = candidateX - x;
                        int dz = candidateZ - z;
                        if (Mathf.Abs(dx) != radius && Mathf.Abs(dz) != radius)
                            continue;

                        int candidateIndex = GetSampleIndex(candidateX, candidateZ, baselineGridX);
                        if (!measuredMask[candidateIndex])
                            continue;

                        float worldOffsetX = dx * sampleSpacingX;
                        float worldOffsetZ = dz * sampleSpacingZ;
                        float distanceSquared =
                            worldOffsetX * worldOffsetX + worldOffsetZ * worldOffsetZ;
                        if (distanceSquared > maximumDistanceSquared ||
                            distanceSquared >= nearestDistanceSquared)
                        {
                            continue;
                        }

                        nearestIndex = candidateIndex;
                        nearestDistanceSquared = distanceSquared;
                    }
                }
            }

            if (nearestIndex < 0)
                continue;

            baselineHeights[index] = baselineHeights[nearestIndex];
            baselineValid[index] = true;
            filledCount++;
        }

        return filledCount;
    }

    public void ClearSpatialBaseline()
    {
        if (isSuspendedForVisualization)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: baseline clearing is suspended during Visualization Mode.");
            return;
        }

        StopAutomaticBaselineCapture();
        ClearSpatialBaselineData();
    }

    private void ClearSpatialBaselineData()
    {
        hasSpatialBaseline = false;
        baselineGridX = 0;
        baselineGridZ = 0;
        baselineHeights = null;
        baselineValid = null;
        consecutiveChangedSamplePasses = null;
        lastSpatialDifferenceRawChangedSampleCount = 0;
        ResetSpatialDifferencePersistenceRevision();
        ClearBaselineSurfaceDebugVisual();
        ClearChangedSurfaceDebugVisual();
        ClearSpatialScanRayDebugVisual();
    }

    public List<PhysicalObjectCandidate> ScanForObjects()
    {
        if (isSuspendedForVisualization)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: scanning is suspended during Visualization Mode.");
            return new List<PhysicalObjectCandidate>();
        }

        ResolveMissingReferences();
        ClearDetectedCandidates();

        if (!CanScan())
        {
            CandidatesUpdated?.Invoke(currentCandidates, currentMode);
            return new List<PhysicalObjectCandidate>(currentCandidates);
        }

        if (useDebugObjects)
        {
            int beforeDebug = currentCandidates.Count;
            ScanDebugObjects();
            int debugCount = currentCandidates.Count - beforeDebug;

            if (debugCount == 0 && useRealSpatialScan)
                ScanRealSpatialObjects();
        }
        else if (useRealSpatialScan)
        {
            ScanRealSpatialObjects();
        }

        CandidatesUpdated?.Invoke(currentCandidates, currentMode);
        if (logSpatialScanDetails)
            Debug.Log($"SpatialObjectDetectionManager: detected {currentCandidates.Count} valid object candidate(s).");

        return new List<PhysicalObjectCandidate>(currentCandidates);
    }

    public Coroutine ScanForObjectsOverTime(
        Action<List<PhysicalObjectCandidate>> onPassCompleted = null,
        Action<List<PhysicalObjectCandidate>> onCompleted = null)
    {
        StopRefreshingSpatialScan();

        if (isSuspendedForVisualization)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: scanning is suspended during Visualization Mode.");
            List<PhysicalObjectCandidate> empty = new List<PhysicalObjectCandidate>();
            onPassCompleted?.Invoke(empty);
            onCompleted?.Invoke(empty);
            return null;
        }

        if (!isActiveAndEnabled)
        {
            List<PhysicalObjectCandidate> candidates = ScanForObjects();
            onPassCompleted?.Invoke(candidates);
            onCompleted?.Invoke(candidates);
            return null;
        }

        int generation = ++scanRefreshGeneration;
        scanRefreshCoroutine = StartCoroutine(
            ScanForObjectsOverTimeRoutine(generation, onPassCompleted, onCompleted));
        return scanRefreshCoroutine;
    }

    public void StopRefreshingSpatialScan()
    {
        scanRefreshGeneration++;

        if (scanRefreshCoroutine == null)
        {
            isRefreshingSpatialScanPass = false;
            return;
        }

        StopCoroutine(scanRefreshCoroutine);
        scanRefreshCoroutine = null;
        isRefreshingSpatialScanPass = false;
    }

    /// <summary>
    /// Pauses tangible scanning without clearing its mode, candidates, or captured baseline.
    /// </summary>
    public void SuspendForVisualization(Transform visualizationRoot)
    {
        StopAutomaticBaselineCapture();
        StopRefreshingSpatialScan();
        isSuspendedForVisualization = true;
        AddVisualizationIgnoredRoot(visualizationRoot);
    }

    public void ResumeAfterVisualization(Transform visualizationRoot)
    {
        RemoveVisualizationIgnoredRoot(visualizationRoot);
        isSuspendedForVisualization = false;
        RefreshSpatialObserverForCurrentMode(false);

        if (!hasSpatialBaseline && currentMode != SpatialPlacementMode.None &&
            autoCaptureBaselineOnModeEnter && useRealSpatialScan && useSpatialMeshDifference)
        {
            ScheduleAutomaticBaselineCapture();
        }
    }

    public void AddVisualizationIgnoredRoot(Transform root)
    {
        if (root != null && !visualizationIgnoredRoots.Contains(root))
            visualizationIgnoredRoots.Add(root);
    }

    public void RemoveVisualizationIgnoredRoot(Transform root)
    {
        if (root != null)
            visualizationIgnoredRoots.Remove(root);
    }

    public void ClearDetectedCandidates()
    {
        if (isSuspendedForVisualization)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: candidate clearing is suspended during Visualization Mode.");
            return;
        }

        if (!isRefreshingSpatialScanPass)
            StopRefreshingSpatialScan();

        for (int i = 0; i < currentCandidates.Count; i++)
        {
            GameObject visual = currentCandidates[i]?.debugVisual;
            if (visual != null)
                DestroyObject(visual);
        }

        currentCandidates.Clear();
        CandidatesUpdated?.Invoke(currentCandidates, currentMode);
    }

    public List<PhysicalObjectCandidate> ConfirmCurrentCandidates()
    {
        if (isSuspendedForVisualization)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: candidate confirmation is suspended during Visualization Mode.");
            return new List<PhysicalObjectCandidate>();
        }

        CandidatesConfirmed?.Invoke(currentCandidates, currentMode);
        Debug.Log($"SpatialObjectDetectionManager: confirmed {currentCandidates.Count} candidate(s) for {currentMode}.");
        return new List<PhysicalObjectCandidate>(currentCandidates);
    }

    public void CancelCurrentMode()
    {
        if (isSuspendedForVisualization)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: mode cancellation is suspended during Visualization Mode.");
            return;
        }

        StopAutomaticBaselineCapture();
        StopRefreshingSpatialScan();
        ClearDetectedCandidates();

        if (clearBaselineOnCancel)
            ClearSpatialBaseline();

        hasTwoBuildingSelectionCandidates = false;
        currentMode = SpatialPlacementMode.None;
        ModeChanged?.Invoke(currentMode);
        RefreshSpatialObserverForCurrentMode(false);

        Debug.Log("SpatialObjectDetectionManager: current placement mode cancelled.");
    }

    private void ScheduleAutomaticBaselineCapture(bool allowWithoutActiveMode = false)
    {
        StopAutomaticBaselineCapture();
        automaticBaselineAllowsInactiveMode = allowWithoutActiveMode;

        if (!isActiveAndEnabled)
        {
            if (CanScan(allowWithoutActiveMode))
                CaptureSpatialBaselineInternal(true, allowWithoutActiveMode);
            automaticBaselineAllowsInactiveMode = false;
            return;
        }

        // The coroutine waits for the focused observer only when it was just
        // started or reset. A running, already-focused observer can be sampled
        // immediately.
        autoBaselineCaptureCoroutine = StartCoroutine(
            AutomaticBaselineCaptureRoutine(currentMode, allowWithoutActiveMode));
    }

    private IEnumerator AutomaticBaselineCaptureRoutine(
        SpatialPlacementMode scheduledMode,
        bool allowWithoutActiveMode)
    {
        // Ensure StartCoroutine has assigned its returned handle before this
        // routine can clear that handle on any completion path.
        yield return null;

        uint meshRevisionBeforeResume = spatialMeshRevision;
        bool observerAlreadyReady = ResumeSpatialObserverForScan();

        bool captured = false;
        int attempts = Mathf.Max(1, automaticBaselineMaxAttempts);

        float warmup = Mathf.Max(0f, spatialObserverWarmupSeconds);
        if (!observerAlreadyReady && warmup > 0f)
        {
            yield return WaitForRelevantSpatialMeshUpdateOrTimeout(
                meshRevisionBeforeResume,
                warmup);
        }

        float retryInterval = Mathf.Max(0.1f, automaticBaselineRetryIntervalSeconds);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (isSuspendedForVisualization ||
                (!allowWithoutActiveMode && currentMode != scheduledMode) ||
                !CanScan(allowWithoutActiveMode))
                break;

            // The MRTK provider can appear after mode entry. Re-resolve and
            // configure it on every attempt instead of relying on auto-start.
            ResumeSpatialObserverForScan();
            using (BaselineCaptureProfilerMarker.Auto())
                captured = CaptureSpatialBaselineInternal(false, allowWithoutActiveMode);
            if (captured)
                break;

            if (attempt + 1 < attempts)
            {
                uint meshRevisionAfterAttempt = spatialMeshRevision;
                yield return WaitForRelevantSpatialMeshUpdateOrTimeout(
                    meshRevisionAfterAttempt,
                    retryInterval);
            }
        }

        autoBaselineCaptureCoroutine = null;
        automaticBaselineAllowsInactiveMode = false;
        BaselineCaptureCompleted?.Invoke(captured);
    }

    private void StopAutomaticBaselineCapture()
    {
        if (autoBaselineCaptureCoroutine == null)
            return;

        StopCoroutine(autoBaselineCaptureCoroutine);
        autoBaselineCaptureCoroutine = null;
        automaticBaselineAllowsInactiveMode = false;
    }

    private IEnumerator ScanForObjectsOverTimeRoutine(
        int generation,
        Action<List<PhysicalObjectCandidate>> onPassCompleted,
        Action<List<PhysicalObjectCandidate>> onCompleted)
    {
        // StartCoroutine advances to the first yield before returning its
        // handle. This prevents a synchronously completed routine from leaving
        // an already-finished handle in scanRefreshCoroutine.
        yield return null;

        if (generation != scanRefreshGeneration)
            yield break;

        ResolveMissingReferences();

        if (!ShouldRefreshSpatialScanOverTime())
        {
            List<PhysicalObjectCandidate> immediateCandidates = ScanForObjectsRefreshPass();
            if (generation == scanRefreshGeneration)
                scanRefreshCoroutine = null;
            onPassCompleted?.Invoke(immediateCandidates);
            if (generation != scanRefreshGeneration)
                yield break;
            onCompleted?.Invoke(immediateCandidates);
            yield break;
        }

        uint meshRevisionBeforeResume = spatialMeshRevision;
        bool observerAlreadyReady = ResumeSpatialObserverForScan();

        if (!observerAlreadyReady && spatialObserverWarmupSeconds > 0f)
        {
            yield return WaitForRelevantSpatialMeshUpdateOrTimeout(
                meshRevisionBeforeResume,
                spatialObserverWarmupSeconds);
        }

        float duration = Mathf.Max(0f, spatialScanRefreshDurationSeconds);
        float interval = Mathf.Max(0.02f, spatialScanRefreshIntervalSeconds);
        float endTime = Time.realtimeSinceStartup + duration;
        List<PhysicalObjectCandidate> latestCandidates = null;
        List<PhysicalObjectCandidate> bestCandidates = null;
        int bestCandidateCount = -1;
        int targetCandidateCount = GetTargetCandidateCountForCurrentMode();
        int stableTargetPassCount = 0;
        int passCount = 0;

        do
        {
            latestCandidates = ScanForObjectsRefreshPass();
            onPassCompleted?.Invoke(latestCandidates);
            if (generation != scanRefreshGeneration)
                yield break;
            passCount++;

            if (!keepBestSpatialScanResult ||
                latestCandidates.Count >= bestCandidateCount)
            {
                bestCandidates = CloneCandidateList(latestCandidates);
                bestCandidateCount = latestCandidates.Count;
            }

            if (targetCandidateCount > 0 && latestCandidates.Count >= targetCandidateCount)
                stableTargetPassCount++;
            else
                stableTargetPassCount = 0;

            if (completeScanWhenTargetCandidatesStable &&
                stableTargetPassCount >= Mathf.Max(1, stableTargetCandidatePasses))
            {
                break;
            }

            if (duration <= 0f || Time.realtimeSinceStartup >= endTime)
                break;

            uint meshRevisionAfterPass = spatialMeshRevision;
            float passCompletedAt = Time.realtimeSinceStartup;
            yield return WaitForRelevantSpatialMeshUpdateOrTimeout(
                meshRevisionAfterPass,
                interval,
                minimumSpatialScanIntervalSeconds,
                endTime);

            while (UsesSpatialMeshRevisionScanGating &&
                !ShouldRefreshCandidateScan(meshRevisionAfterPass, passCompletedAt) &&
                Time.realtimeSinceStartup < endTime)
            {
                yield return null;
            }

            if (Time.realtimeSinceStartup >= endTime)
                break;
        }
        while (true);

        if (keepBestSpatialScanResult &&
            bestCandidates != null &&
            bestCandidateCount > (latestCandidates?.Count ?? -1))
        {
            ApplyCandidateSnapshot(bestCandidates);
            if (generation != scanRefreshGeneration)
                yield break;
            latestCandidates = new List<PhysicalObjectCandidate>(currentCandidates);
        }

        if (logSpatialScanDetails)
        {
            Debug.Log(
                $"SpatialObjectDetectionManager: refreshed spatial scan for {passCount} pass(es), " +
                $"finalCandidates={latestCandidates?.Count ?? 0}, bestCandidates={bestCandidateCount}, " +
                $"targetCandidates={targetCandidateCount}, stableTargetPasses={stableTargetPassCount}.");
        }

        if (generation == scanRefreshGeneration)
            scanRefreshCoroutine = null;
        onCompleted?.Invoke(latestCandidates ?? new List<PhysicalObjectCandidate>(currentCandidates));
    }

    private IEnumerator WaitForRelevantSpatialMeshUpdateOrTimeout(
        uint revisionAtStart,
        float timeoutSeconds,
        float minimumWaitSeconds = 0f,
        float maximumWaitUntil = -1f)
    {
        float startedAt = Time.realtimeSinceStartup;
        float timeoutAt = startedAt + Mathf.Max(0f, timeoutSeconds);
        float earliestEventWakeAt = startedAt + Mathf.Clamp(
            minimumWaitSeconds,
            0f,
            Mathf.Max(0f, timeoutSeconds));
        float maximumEventCoalesceAt = startedAt + Mathf.Max(
            Mathf.Max(0f, timeoutSeconds),
            Mathf.Max(0.2f, spatialMeshRevisionFallbackSeconds));
        if (maximumWaitUntil >= 0f)
            maximumEventCoalesceAt = Mathf.Min(maximumEventCoalesceAt, maximumWaitUntil);

        while (true)
        {
            bool receivedRelevantUpdate =
                reactToSpatialMeshUpdateEvents &&
                spatialMeshEventsRegistered &&
                spatialMeshRevision != revisionAtStart;
            bool updateHasSettled =
                Time.realtimeSinceStartup - lastRelevantSpatialMeshUpdateTime >=
                Mathf.Max(0f, spatialMeshUpdateSettleSeconds);

            if (receivedRelevantUpdate && updateHasSettled &&
                Time.realtimeSinceStartup >= earliestEventWakeAt)
                yield break;

            // A provider can stream adjacent chunk events continuously. Keep
            // the debounce finite so warmup and monitoring cannot starve.
            if (receivedRelevantUpdate &&
                Time.realtimeSinceStartup >= maximumEventCoalesceAt)
            {
                yield break;
            }

            if (maximumWaitUntil >= 0f &&
                Time.realtimeSinceStartup >= maximumWaitUntil)
            {
                yield break;
            }

            // Once an update arrives, finish coalescing its chunk events even
            // if the ordinary polling timeout has elapsed. Otherwise a late
            // event can be sampled before its collider bake has settled.
            if (!receivedRelevantUpdate && Time.realtimeSinceStartup >= timeoutAt)
            {
                // Event registration is optional and some providers may not
                // publish an event for every reset. Do not keep charging
                // warmup on later requests.
                spatialObserverMeshesResetByLatestConfiguration = false;
                yield break;
            }

            yield return null;
        }
    }

    private List<PhysicalObjectCandidate> ScanForObjectsRefreshPass()
    {
        isRefreshingSpatialScanPass = true;
        try
        {
            return ScanForObjects();
        }
        finally
        {
            isRefreshingSpatialScanPass = false;
        }
    }

    private int GetTargetCandidateCountForCurrentMode()
    {
        switch (currentMode)
        {
            case SpatialPlacementMode.BuildingPlacing:
                return Mathf.Max(0, buildingScanTargetCandidateCount);

            case SpatialPlacementMode.FloodPlacing:
                return Mathf.Max(0, floodScanTargetCandidateCount);

            default:
                return 0;
        }
    }

    private List<PhysicalObjectCandidate> CloneCandidateList(IReadOnlyList<PhysicalObjectCandidate> source)
    {
        List<PhysicalObjectCandidate> clone = new List<PhysicalObjectCandidate>();
        if (source == null)
            return clone;

        for (int i = 0; i < source.Count; i++)
        {
            PhysicalObjectCandidate candidate = source[i];
            if (candidate == null)
                continue;

            PhysicalObjectCandidate candidateClone = new PhysicalObjectCandidate(
                candidate.id,
                candidate.worldPosition,
                candidate.worldBounds,
                candidate.approximateSize,
                candidate.distanceFromPlane,
                candidate.isValid);
            clone.Add(candidateClone);
        }

        return clone;
    }

    private void ApplyCandidateSnapshot(IReadOnlyList<PhysicalObjectCandidate> candidates)
    {
        isRefreshingSpatialScanPass = true;
        try
        {
            ClearDetectedCandidates();

            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    PhysicalObjectCandidate candidate = candidates[i];
                    if (candidate == null)
                        continue;

                    PhysicalObjectCandidate clone = new PhysicalObjectCandidate(
                        candidate.id,
                        candidate.worldPosition,
                        candidate.worldBounds,
                        candidate.approximateSize,
                        candidate.distanceFromPlane,
                        candidate.isValid);

                    if (createCandidateDebugVisuals)
                        clone.debugVisual = CreateCandidateDebugVisual(clone);

                    currentCandidates.Add(clone);
                }
            }

            CandidatesUpdated?.Invoke(currentCandidates, currentMode);
        }
        finally
        {
            isRefreshingSpatialScanPass = false;
        }
    }

    private bool ShouldRefreshSpatialScanOverTime()
    {
        return scanRealSpatialObjectsOverTime &&
            useRealSpatialScan &&
            Application.isPlaying;
    }

    private bool ResumeSpatialObserverForScan()
    {
        if (!resumeSpatialObserverBeforeScan || !useMrtkSpatialAwarenessLayer)
            return false;

        TryRegisterSpatialMeshEvents();
        IMixedRealitySpatialAwarenessMeshObserver observer = GetSpatialMeshObserver();
        if (observer == null)
            return false;

        bool wasRunning = observer.IsRunning;
        ApplyDetectionSpatialObserverSettings(observer, false);
        if (!observer.IsRunning)
            observer.Resume();

        return wasRunning && !spatialObserverMeshesResetByLatestConfiguration;
    }

    public bool ShouldRefreshCandidateScan(uint revisionAtLastScan, float lastScanRealtime)
    {
        if (!UsesSpatialMeshRevisionScanGating)
            return true;

        bool watchdogElapsed = Time.realtimeSinceStartup - lastScanRealtime >=
            Mathf.Max(0.2f, spatialMeshRevisionFallbackSeconds);
        if (spatialMeshRevision != revisionAtLastScan)
            return IsSpatialMeshUpdateSettled || watchdogElapsed;

        return watchdogElapsed;
    }

    private void RefreshSpatialObserverForCurrentMode(bool clearExistingMeshes)
    {
        if (!useMrtkSpatialAwarenessLayer)
            return;

        IMixedRealitySpatialAwarenessMeshObserver observer = GetSpatialMeshObserver();
        if (observer == null)
            return;

        if (currentMode == SpatialPlacementMode.None)
        {
            RestoreSpatialObserverPerformanceSettings(observer);

            if (restoreSpatialObserverDisplayOptionOnExit)
                RestoreSpatialObserverDisplayOption(observer);
            else
                hasSavedSpatialObserverDisplayOption = false;

            if (suspendSpatialObserverOnExit)
                observer.Suspend();

            return;
        }

        if (!keepSpatialObserverRunningInPlacementMode)
            return;

        ApplyDetectionSpatialObserverSettings(observer, clearExistingMeshes);
        if (!observer.IsRunning)
            observer.Resume();
    }

    private void ApplyDetectionSpatialObserverSettings(
        IMixedRealitySpatialAwarenessMeshObserver observer,
        bool clearExistingMeshes)
    {
        if (observer == null)
            return;

        if (!hasSavedSpatialObserverDisplayOption)
        {
            savedSpatialObserverDisplayOption = observer.DisplayOption;
            hasSavedSpatialObserverDisplayOption = true;
        }

        if (!hasSavedSpatialObserverPerformanceSettings)
        {
            savedSpatialObserverIsStationary = observer.IsStationaryObserver;
            savedSpatialObserverVolumeType = observer.ObserverVolumeType;
            savedSpatialObserverOrigin = observer.ObserverOrigin;
            savedSpatialObserverRotation = observer.ObserverRotation;
            savedSpatialObserverExtents = observer.ObservationExtents;
            savedSpatialObserverUpdateInterval = observer.UpdateInterval;
            savedSpatialObserverRecalculateNormals = observer.RecalculateNormals;
            hasSavedSpatialObserverPerformanceSettings = true;
        }

        bool focusChanged = false;
        hasSpatialObserverFocusBounds = false;
        if (focusSpatialObserverOnScanArea &&
            TryGetSpatialObserverFocusBounds(out Bounds focusBounds))
        {
            spatialObserverFocusBounds = focusBounds;
            hasSpatialObserverFocusBounds = true;
            float focusResetDistance = Mathf.Max(
                0.05f,
                Mathf.Max(0f, spatialObserverBoundsPadding) * 0.5f);
            focusChanged =
                !observer.IsStationaryObserver ||
                observer.ObserverVolumeType != VolumeType.AxisAlignedCube ||
                Vector3.Distance(observer.ObserverOrigin, focusBounds.center) > focusResetDistance ||
                Vector3.Distance(observer.ObservationExtents, focusBounds.size) > focusResetDistance;

            observer.IsStationaryObserver = true;
            observer.ObserverVolumeType = VolumeType.AxisAlignedCube;
            observer.ObserverOrigin = focusBounds.center;
            // MRTK names this property "extents", but the XRSDK provider passes
            // it to SetBoundingVolume as the cube's full side lengths.
            observer.ObservationExtents = focusBounds.size;
        }

        float desiredUpdateInterval = Mathf.Max(0.05f, spatialObserverUpdateIntervalWhileDetecting);
        if (observer.UpdateInterval > desiredUpdateInterval)
            observer.UpdateInterval = desiredUpdateInterval;

        if (disableSpatialObserverNormalRecalculationWhileDetecting)
            observer.RecalculateNormals = false;

        ApplySpatialObserverDisplayOption(observer);

        if (clearExistingMeshes ||
            (clearSpatialObserverMeshesWhenFocusChanges && focusChanged))
        {
            observer.ClearObservations();
            spatialObserverMeshesResetByLatestConfiguration = true;
        }
    }

    private void ApplySpatialObserverDisplayOption(
        IMixedRealitySpatialAwarenessMeshObserver observer)
    {
        if (observer == null)
            return;

        bool hideForCompletedBuildingSelection =
            hideSpatialObserverAfterTwoBuildingCandidates &&
            hasTwoBuildingSelectionCandidates &&
            currentMode == SpatialPlacementMode.BuildingPlacing;

        if (!hideForCompletedBuildingSelection &&
            !spatialObserverHiddenForBuildingSelection &&
            !forceColliderSpatialMeshForDetection)
        {
            return;
        }

        SpatialAwarenessMeshDisplayOptions desiredDisplayOption =
            hideForCompletedBuildingSelection
                ? SpatialAwarenessMeshDisplayOptions.None
                : spatialObserverDisplayOptionWhileDetecting;

        if (observer.DisplayOption != desiredDisplayOption)
            observer.DisplayOption = desiredDisplayOption;

        spatialObserverHiddenForBuildingSelection = hideForCompletedBuildingSelection;
    }

    private void RestoreSpatialObserverPerformanceSettings()
    {
        RestoreSpatialObserverPerformanceSettings(spatialMeshObserver);
    }

    private void RestoreSpatialObserverPerformanceSettings(
        IMixedRealitySpatialAwarenessMeshObserver observer)
    {
        if (!hasSavedSpatialObserverPerformanceSettings || observer == null)
            return;

        observer.IsStationaryObserver = savedSpatialObserverIsStationary;
        observer.ObserverVolumeType = savedSpatialObserverVolumeType;
        observer.ObserverOrigin = savedSpatialObserverOrigin;
        observer.ObserverRotation = savedSpatialObserverRotation;
        observer.ObservationExtents = savedSpatialObserverExtents;
        observer.UpdateInterval = savedSpatialObserverUpdateInterval;
        observer.RecalculateNormals = savedSpatialObserverRecalculateNormals;
        hasSavedSpatialObserverPerformanceSettings = false;
        hasSpatialObserverFocusBounds = false;
        spatialObserverMeshesResetByLatestConfiguration = false;
    }

    private void RestoreSpatialObserverDisplayOption()
    {
        RestoreSpatialObserverDisplayOption(spatialMeshObserver);
    }

    private void RestoreSpatialObserverDisplayOption(
        IMixedRealitySpatialAwarenessMeshObserver observer)
    {
        if (!hasSavedSpatialObserverDisplayOption || observer == null)
            return;

        observer.DisplayOption = savedSpatialObserverDisplayOption;
        spatialObserverHiddenForBuildingSelection = false;
        hasSavedSpatialObserverDisplayOption = false;
    }

    private bool CanScan(bool allowWithoutActiveMode = false)
    {
        if (!allowWithoutActiveMode && currentMode == SpatialPlacementMode.None)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: cannot scan because no placement mode is active.");
            return false;
        }

        if (requireConfirmedCityPlacement && !IsCityPlacementConfirmed())
        {
            Debug.LogWarning("SpatialObjectDetectionManager: city placement must be confirmed before object scanning.");
            return false;
        }

        if (selectedPlaneTransform == null)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: Selected Plane Transform is not assigned.");
            return false;
        }

        return true;
    }

    private bool IsCityPlacementConfirmed()
    {
        if (cityAnchorManager != null)
            return cityAnchorManager.IsConfirmed;

        if (cityPlacementManager != null)
            return cityPlacementManager.HasConfirmed;

        return !requireConfirmedCityPlacement;
    }

    private void ScanDebugObjects()
    {
        if (debugObjectsRoot == null)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: Use Debug Objects is enabled but Debug Objects Root is missing. Falling back to real spatial scan if enabled.");
            return;
        }

        int index = 0;
        foreach (Transform candidateTransform in debugObjectsRoot)
        {
            if (candidateTransform == null)
                continue;

            if (!includeInactiveDebugObjects && !candidateTransform.gameObject.activeInHierarchy)
                continue;

            Bounds bounds = GetWorldBounds(candidateTransform, debugObjectFallbackSize);
            TryAddCandidate($"debug-{scanSequence}-{index}", bounds);
            index++;
        }

        scanSequence++;
    }

    private void ScanRealSpatialObjects()
    {
        if (useSpatialMeshDifference)
        {
            if (hasSpatialBaseline)
            {
                int beforeDiff = currentCandidates.Count;
                using (DifferenceScanProfilerMarker.Auto())
                    ScanSpatialMeshDifferences();
                int diffCount = currentCandidates.Count - beforeDiff;

                if (diffCount > 0 || !fallbackToColliderScanWhenDiffFindsNothing)
                    return;

                if (lastSpatialDifferenceRawChangedSampleCount > 0)
                {
                    if (logSpatialScanDetails)
                    {
                        Debug.Log(
                            "SpatialObjectDetectionManager: mesh changes are present but still awaiting temporal stability; collider fallback is suppressed for this pass.");
                    }

                    return;
                }

                Debug.LogWarning("SpatialObjectDetectionManager: mesh-difference scan found no candidates; trying collider overlap fallback.");
            }
            else
            {
                Debug.LogWarning("SpatialObjectDetectionManager: no spatial baseline exists for mesh difference scan.");

                if (!fallbackToColliderScanWithoutBaseline)
                {
                    if (!IsPreparingAutomaticBaseline)
                        CaptureSpatialBaseline();
                    return;
                }
            }
        }

        ScanColliderOverlapObjects();
    }

    private void ScanSpatialMeshDifferences()
    {
        lastSpatialDifferenceRawChangedSampleCount = 0;
        int layerMask = GetRealSpatialLayerMask();
        if (layerMask == 0)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: spatial mesh difference layer mask is empty.");
            return;
        }

        if (!hasSpatialBaseline || baselineHeights == null || baselineValid == null)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: spatial mesh difference scan requested without a valid baseline.");
            return;
        }

        changedSamples.Clear();
        Vector2 effectiveScanHalfExtents = GetScanHalfExtents();
        SpatialScanGeometry scanGeometry = CreateSpatialScanGeometry();
        EnsureMeshChangePersistenceBuffer();
        int rawChangedSampleCount = 0;
        int requiredStablePasses = Mathf.Clamp(
            requiredConsecutiveChangedSamplePasses,
            1,
            byte.MaxValue);
        bool advancePersistence = ShouldAdvanceSpatialDifferencePersistence();

        spatialColliderIgnoreCache.Clear();
        BeginSpatialScanRayDebugPass();

        for (int z = 0; z < baselineGridZ; z++)
        {
            for (int x = 0; x < baselineGridX; x++)
            {
                int index = GetSampleIndex(x, z, baselineGridX);
                if (!baselineValid[index])
                {
                    if (advancePersistence)
                        consecutiveChangedSamplePasses[index] = 0;
                    continue;
                }

                GetLocalSamplePosition(x, z, baselineGridX, baselineGridZ, effectiveScanHalfExtents, out float localX, out float localZ);

                if (!TrySampleSurfaceHeight(
                    localX,
                    localZ,
                    layerMask,
                    scanGeometry,
                    out float currentHeight,
                    out _))
                {
                    if (advancePersistence)
                        consecutiveChangedSamplePasses[index] = 0;
                    continue;
                }

                float baselineHeight = baselineHeights[index];
                float delta = currentHeight - baselineHeight;

                if (delta < minSurfaceChangeHeight || delta > maxSurfaceChangeHeight)
                {
                    if (advancePersistence)
                        consecutiveChangedSamplePasses[index] = 0;
                    continue;
                }

                rawChangedSampleCount++;
                int stablePassCount = consecutiveChangedSamplePasses[index];
                if (advancePersistence)
                {
                    stablePassCount = Mathf.Min(stablePassCount + 1, requiredStablePasses);
                    consecutiveChangedSamplePasses[index] = (byte)stablePassCount;
                }
                if (stablePassCount < requiredStablePasses)
                    continue;

                changedSamples.Add(new ChangedSurfaceSample
                {
                    ix = x,
                    iz = z,
                    localX = localX,
                    localZ = localZ,
                    baselineHeight = baselineHeight,
                    currentHeight = currentHeight,
                    deltaHeight = delta
                });
            }
        }

        EndSpatialScanRayDebugPass();
        lastSpatialDifferenceRawChangedSampleCount = rawChangedSampleCount;

        if (changedSamples.Count == 0)
        {
            RebuildChangedSurfaceDebugVisual();

            if (logSpatialScanDetails)
            {
                Debug.Log(
                    $"SpatialObjectDetectionManager: mesh-difference scan found " +
                    $"{rawChangedSampleCount} raw changed sample(s), 0 temporally stable sample(s).");
            }

            scanSequence++;
            return;
        }

        RebuildChangedSurfaceDebugVisual();

        int candidateCountBefore = currentCandidates.Count;
        BuildCandidatesFromChangedSamples();
        int added = currentCandidates.Count - candidateCountBefore;

        if (logSpatialScanDetails)
        {
            Debug.Log(
                $"SpatialObjectDetectionManager: mesh-difference scan rawChangedSamples={rawChangedSampleCount}, " +
                $"stableChangedSamples={changedSamples.Count}, requiredStablePasses={requiredStablePasses}, " +
                $"candidatesAdded={added}.");
        }

        scanSequence++;
    }

    private void EnsureMeshChangePersistenceBuffer()
    {
        int requiredLength = Mathf.Max(0, baselineGridX * baselineGridZ);
        if (consecutiveChangedSamplePasses == null ||
            consecutiveChangedSamplePasses.Length != requiredLength)
        {
            consecutiveChangedSamplePasses = new byte[requiredLength];
        }
    }

    private void ResetMeshChangePersistence()
    {
        lastSpatialDifferenceRawChangedSampleCount = 0;
        if (consecutiveChangedSamplePasses != null)
            Array.Clear(consecutiveChangedSamplePasses, 0, consecutiveChangedSamplePasses.Length);

        ResetSpatialDifferencePersistenceRevision();
    }

    private bool ShouldAdvanceSpatialDifferencePersistence()
    {
        float now = Time.realtimeSinceStartup;
        bool eventsExpected = reactToSpatialMeshUpdateEvents && spatialMeshEventsRegistered;
        bool revisionChanged = !hasSpatialDifferencePersistenceRevision ||
            spatialMeshRevision != lastSpatialDifferencePersistenceRevision;
        bool pollingFallbackElapsed =
            now - lastSpatialDifferencePersistenceAdvanceTime >=
            Mathf.Max(0.2f, spatialMeshRevisionFallbackSeconds);

        if (eventsExpected && !revisionChanged && !pollingFallbackElapsed)
            return false;

        hasSpatialDifferencePersistenceRevision = true;
        lastSpatialDifferencePersistenceRevision = spatialMeshRevision;
        lastSpatialDifferencePersistenceAdvanceTime = now;
        return true;
    }

    private void ResetSpatialDifferencePersistenceRevision()
    {
        hasSpatialDifferencePersistenceRevision = false;
        lastSpatialDifferencePersistenceRevision = spatialMeshRevision;
        lastSpatialDifferencePersistenceAdvanceTime = float.NegativeInfinity;
    }

    private void BuildCandidatesFromChangedSamples()
    {
        changedSampleIndexByGridIndex.Clear();
        for (int i = 0; i < changedSamples.Count; i++)
        {
            ChangedSurfaceSample sample = changedSamples[i];
            changedSampleIndexByGridIndex[GetSampleIndex(sample.ix, sample.iz, baselineGridX)] = i;
        }

        if (visitedChangedSamples == null || visitedChangedSamples.Length < changedSamples.Count)
            visitedChangedSamples = new bool[changedSamples.Count];
        else
            Array.Clear(visitedChangedSamples, 0, changedSamples.Count);

        int clusterNumber = 0;

        for (int i = 0; i < changedSamples.Count; i++)
        {
            if (visitedChangedSamples[i])
                continue;

            clusterIndices.Clear();
            clusterQueue.Clear();
            clusterQueue.Enqueue(i);
            visitedChangedSamples[i] = true;

            while (clusterQueue.Count > 0)
            {
                int current = clusterQueue.Dequeue();
                clusterIndices.Add(current);
                ChangedSurfaceSample sample = changedSamples[current];

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;

                        int nx = sample.ix + dx;
                        int nz = sample.iz + dz;

                        if (nx < 0 || nz < 0 || nx >= baselineGridX || nz >= baselineGridZ)
                            continue;

                        if (!changedSampleIndexByGridIndex.TryGetValue(GetSampleIndex(nx, nz, baselineGridX), out int neighborListIndex))
                            continue;

                        if (visitedChangedSamples[neighborListIndex])
                            continue;

                        visitedChangedSamples[neighborListIndex] = true;
                        clusterQueue.Enqueue(neighborListIndex);
                    }
                }
            }

            // One grid cell can jump when a spatial-mesh triangle is rebuilt.
            // A physical object at the configured minimum size should cover at
            // least two adjacent cells at the enforced maximum sample spacing.
            if (clusterIndices.Count < Mathf.Max(2, minChangedSamplesPerCluster))
                continue;

            if (TryBuildBoundsFromCluster(
                clusterIndices,
                out Bounds worldBounds,
                out Vector3 candidatePosition,
                out float approximateSize,
                out float distanceFromPlane))
            {
                TryAddMeshDifferenceCandidate(
                    $"mesh-diff-{scanSequence}-{clusterNumber}",
                    worldBounds,
                    candidatePosition,
                    approximateSize,
                    distanceFromPlane);

                clusterNumber++;
            }
        }
    }

    private bool TryBuildBoundsFromCluster(
        List<int> sampleIndices,
        out Bounds worldBounds,
        out Vector3 projectedWorldPosition,
        out float approximateSize,
        out float distanceFromPlane)
    {
        worldBounds = default;
        projectedWorldPosition = default;
        approximateSize = 0f;
        distanceFromPlane = 0f;

        if (sampleIndices == null || sampleIndices.Count == 0)
            return false;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        float minBaselineHeight = float.MaxValue;
        float maxCurrentHeight = float.MinValue;
        float sumX = 0f;
        float sumZ = 0f;
        float sumBaseline = 0f;

        Vector2 effectiveScanHalfExtents = GetScanHalfExtents();
        float cellX = baselineGridX > 1 ? (effectiveScanHalfExtents.x * 2f) / (baselineGridX - 1) : effectiveScanHalfExtents.x * 2f;
        float cellZ = baselineGridZ > 1 ? (effectiveScanHalfExtents.y * 2f) / (baselineGridZ - 1) : effectiveScanHalfExtents.y * 2f;
        float worldScaleX = selectedPlaneTransform != null
            ? selectedPlaneTransform.TransformVector(Vector3.right).magnitude
            : 1f;
        float worldScaleZ = selectedPlaneTransform != null
            ? selectedPlaneTransform.TransformVector(Vector3.forward).magnitude
            : 1f;
        float halfCellX = cellX * 0.5f + candidateBoundsPadding / Mathf.Max(worldScaleX, 0.0001f);
        float halfCellZ = cellZ * 0.5f + candidateBoundsPadding / Mathf.Max(worldScaleZ, 0.0001f);

        for (int i = 0; i < sampleIndices.Count; i++)
        {
            ChangedSurfaceSample sample = changedSamples[sampleIndices[i]];
            minX = Mathf.Min(minX, sample.localX - halfCellX);
            maxX = Mathf.Max(maxX, sample.localX + halfCellX);
            minZ = Mathf.Min(minZ, sample.localZ - halfCellZ);
            maxZ = Mathf.Max(maxZ, sample.localZ + halfCellZ);
            minBaselineHeight = Mathf.Min(minBaselineHeight, sample.baselineHeight);
            maxCurrentHeight = Mathf.Max(maxCurrentHeight, sample.currentHeight);
            sumX += sample.localX;
            sumZ += sample.localZ;
            sumBaseline += sample.baselineHeight;
        }

        float height = maxCurrentHeight - minBaselineHeight;
        if (height < minHeightAbovePlane || height > maxHeightAbovePlane)
            return false;

        Bounds builtBounds = BuildWorldBoundsFromLocalRanges(
            minX,
            maxX,
            minBaselineHeight,
            maxCurrentHeight,
            minZ,
            maxZ);
        approximateSize = GetApproximateSize(builtBounds);

        if (approximateSize < minApproximateSize || approximateSize > maxApproximateSize)
            return false;

        Vector3 localProjected = new Vector3(
            sumX / sampleIndices.Count,
            0f,
            sumZ / sampleIndices.Count);
        projectedWorldPosition = ScanLocalToWorldPoint(localProjected);
        distanceFromPlane = Mathf.Abs(sumBaseline / sampleIndices.Count);

        if (!IsInsideScanArea(projectedWorldPosition))
            return false;

        worldBounds = builtBounds;
        return true;
    }

    private Bounds BuildWorldBoundsFromLocalRanges(float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        Vector3 first = ScanLocalToWorldPoint(new Vector3(minX, minY, minZ));
        Bounds bounds = new Bounds(first, Vector3.zero);

        bounds.Encapsulate(ScanLocalToWorldPoint(new Vector3(minX, minY, maxZ)));
        bounds.Encapsulate(ScanLocalToWorldPoint(new Vector3(minX, maxY, minZ)));
        bounds.Encapsulate(ScanLocalToWorldPoint(new Vector3(minX, maxY, maxZ)));
        bounds.Encapsulate(ScanLocalToWorldPoint(new Vector3(maxX, minY, minZ)));
        bounds.Encapsulate(ScanLocalToWorldPoint(new Vector3(maxX, minY, maxZ)));
        bounds.Encapsulate(ScanLocalToWorldPoint(new Vector3(maxX, maxY, minZ)));
        bounds.Encapsulate(ScanLocalToWorldPoint(new Vector3(maxX, maxY, maxZ)));

        return bounds;
    }

    private void TryAddMeshDifferenceCandidate(
        string id,
        Bounds bounds,
        Vector3 candidatePosition,
        float approximateSize,
        float distanceFromPlane)
    {
        PhysicalObjectCandidate candidate = new PhysicalObjectCandidate(
            id,
            candidatePosition,
            bounds,
            approximateSize,
            distanceFromPlane,
            true);

        if (createCandidateDebugVisuals)
            candidate.debugVisual = CreateCandidateDebugVisual(candidate);

        currentCandidates.Add(candidate);
    }

    private void ScanColliderOverlapObjects()
    {
        int layerMask = GetRealSpatialLayerMask();
        if (layerMask == 0)
        {
            Debug.LogWarning("SpatialObjectDetectionManager: real spatial scan layer mask is empty.");
            return;
        }

        Vector3 origin = GetScanCenterWorldPosition();
        Quaternion rotation = selectedPlaneTransform.rotation;
        Vector3 up = selectedPlaneTransform.up;
        Vector2 effectiveScanHalfExtents = GetScanHalfExtents();
        Vector2 worldScanHalfExtents = GetWorldScanHalfExtents(effectiveScanHalfExtents);
        float verticalHalfExtent = (maxBottomDistanceFromPlane + maxHeightAbovePlane) * 0.5f;
        Vector3 center = origin + up * (verticalHalfExtent - maxBottomDistanceFromPlane);
        Vector3 halfExtents = new Vector3(worldScanHalfExtents.x, verticalHalfExtent, worldScanHalfExtents.y);

        Collider[] colliders = Physics.OverlapBox(
            center,
            halfExtents,
            rotation,
            layerMask,
            QueryTriggerInteraction.Ignore);

        realObjectBounds.Clear();

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null || ShouldIgnoreTransform(col.transform))
                continue;

            Transform root = col.attachedRigidbody != null
                ? col.attachedRigidbody.transform
                : col.transform;

            if (root == null)
                root = col.transform;

            if (realObjectBounds.TryGetValue(root, out Bounds existing))
            {
                existing.Encapsulate(col.bounds);
                realObjectBounds[root] = existing;
            }
            else
            {
                realObjectBounds[root] = col.bounds;
            }
        }

        int index = 0;
        foreach (KeyValuePair<Transform, Bounds> pair in realObjectBounds)
        {
            TryAddCandidate($"collider-{scanSequence}-{index}", pair.Value);
            index++;
        }

        if (logSpatialScanDetails)
            Debug.Log($"SpatialObjectDetectionManager: collider overlap fallback saw {colliders.Length} collider(s), added {index} bounds group(s).");

        scanSequence++;
    }

    private bool TryAddCandidate(string id, Bounds bounds)
    {
        PhysicalObjectCandidate candidate = BuildCandidate(id, bounds);

        if (!candidate.isValid)
            return false;

        if (createCandidateDebugVisuals)
            candidate.debugVisual = CreateCandidateDebugVisual(candidate);

        currentCandidates.Add(candidate);
        return true;
    }

    private PhysicalObjectCandidate BuildCandidate(string id, Bounds bounds)
    {
        float approximateSize = GetApproximateSize(bounds);
        float bottomDistance = GetMinimumSignedPlaneDistance(bounds);
        float topDistance = GetMaximumSignedPlaneDistance(bounds);
        float heightAbovePlane = Mathf.Max(0f, topDistance);

        bool valid =
            bottomDistance >= -maxBottomDistanceFromPlane &&
            bottomDistance <= maxBottomDistanceFromPlane &&
            heightAbovePlane >= minHeightAbovePlane &&
            heightAbovePlane <= maxHeightAbovePlane &&
            approximateSize >= minApproximateSize &&
            approximateSize <= maxApproximateSize &&
            IsInsideScanArea(bounds.center);

        Vector3 worldPosition = ProjectPointToPlane(bounds.center);

        return new PhysicalObjectCandidate(
            id,
            worldPosition,
            bounds,
            approximateSize,
            Mathf.Abs(bottomDistance),
            valid);
    }

    private bool TrySampleSurfaceHeight(
        float localX,
        float localZ,
        int layerMask,
        SpatialScanGeometry scanGeometry,
        out float signedHeight,
        out Vector3 hitPoint)
    {
        signedHeight = 0f;
        hitPoint = default;

        if (selectedPlaneTransform == null)
            return false;

        Vector3 worldOrigin = ScanLocalToWorldPoint(
            new Vector3(localX, baselineRayHeightAbovePlane, localZ),
            scanGeometry);
        Vector3 direction = -scanGeometry.up;
        float rayLength = baselineRayHeightAbovePlane + baselineRayDepthBelowPlane;
        Vector3 rayEnd = worldOrigin + direction * rayLength;

        int hitCount = Physics.RaycastNonAlloc(
            worldOrigin,
            direction,
            spatialRaycastHits,
            rayLength,
            layerMask,
            QueryTriggerInteraction.Ignore);

        if (hitCount == 0)
        {
            RecordSpatialScanRayDebugSample(worldOrigin, rayEnd, default, false);
            return false;
        }

        bool found = false;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = spatialRaycastHits[i];
            if (hit.collider == null)
                continue;

            if (ShouldIgnoreCollider(hit.collider))
                continue;

            if (hit.distance >= nearestDistance)
                continue;

            nearestDistance = hit.distance;
            signedHeight = Vector3.Dot(
                hit.point - scanGeometry.planeOrigin,
                scanGeometry.up);
            hitPoint = hit.point;
            found = true;
        }

        if (found)
        {
            RecordSpatialScanRayDebugSample(worldOrigin, hitPoint, hitPoint, true);
            return true;
        }

        RecordSpatialScanRayDebugSample(worldOrigin, rayEnd, default, false);
        return false;
    }

    private static int GetSampleIndex(int x, int z, int width)
    {
        return z * width + x;
    }

    private void GetLocalSamplePosition(int x, int z, int gridX, int gridZ, out float localX, out float localZ)
    {
        GetLocalSamplePosition(x, z, gridX, gridZ, GetScanHalfExtents(), out localX, out localZ);
    }

    private static void GetLocalSamplePosition(
        int x,
        int z,
        int gridX,
        int gridZ,
        Vector2 effectiveScanHalfExtents,
        out float localX,
        out float localZ)
    {
        float tx = gridX <= 1 ? 0.5f : x / (float)(gridX - 1);
        float tz = gridZ <= 1 ? 0.5f : z / (float)(gridZ - 1);

        localX = Mathf.Lerp(-effectiveScanHalfExtents.x, effectiveScanHalfExtents.x, tx);
        localZ = Mathf.Lerp(-effectiveScanHalfExtents.y, effectiveScanHalfExtents.y, tz);
    }

    private bool IsInsideScanArea(Vector3 worldPosition)
    {
        Vector3 local = selectedPlaneTransform.InverseTransformPoint(worldPosition) -
            selectedPlaneTransform.InverseTransformPoint(GetScanCenterWorldPosition());
        Vector2 effectiveScanHalfExtents = GetScanHalfExtents();

        return Mathf.Abs(local.x) <= effectiveScanHalfExtents.x &&
            Mathf.Abs(local.z) <= effectiveScanHalfExtents.y;
    }

    private Vector3 ProjectPointToPlane(Vector3 point)
    {
        Vector3 origin = selectedPlaneTransform.position;
        Vector3 up = selectedPlaneTransform.up;
        float signedDistance = Vector3.Dot(point - origin, up);
        return point - up * signedDistance;
    }

    private Vector3 GetScanCenterWorldPosition()
    {
        if (selectedPlaneTransform == null)
        {
            if (useScanAreaReferenceAsCenter && scanAreaReferenceTransform != null)
                return scanAreaReferenceTransform.position;

            return scanCenterTransform != null ? scanCenterTransform.position : Vector3.zero;
        }

        if (useScanAreaReferenceAsCenter &&
            TryGetScanAreaReferenceBounds(out Vector3 referenceCenterWorld, out _))
        {
            return referenceCenterWorld;
        }

        if (scanCenterTransform == null)
            return selectedPlaneTransform.position;

        return ProjectPointToPlane(scanCenterTransform.position);
    }

    private Transform GetScanCenterReferenceTransform()
    {
        if (useScanAreaReferenceAsCenter && scanAreaReferenceTransform != null)
            return scanAreaReferenceTransform;

        return scanCenterTransform != null ? scanCenterTransform : selectedPlaneTransform;
    }

    private Vector2 GetScanHalfExtents()
    {
        if (TryGetScanAreaReferenceBounds(out _, out Vector2 referenceHalfExtents))
            return referenceHalfExtents;

        return new Vector2(
            Mathf.Max(0.001f, Mathf.Abs(scanHalfExtents.x)),
            Mathf.Max(0.001f, Mathf.Abs(scanHalfExtents.y)));
    }

    private Vector2 GetWorldScanHalfExtents(Vector2 localHalfExtents)
    {
        if (selectedPlaneTransform == null)
            return localHalfExtents;

        float worldX = selectedPlaneTransform.TransformVector(Vector3.right * localHalfExtents.x).magnitude;
        float worldZ = selectedPlaneTransform.TransformVector(Vector3.forward * localHalfExtents.y).magnitude;

        return new Vector2(
            Mathf.Max(0.001f, worldX),
            Mathf.Max(0.001f, worldZ));
    }

    private bool TryGetScanAreaReferenceBounds(out Vector3 centerWorldPosition, out Vector2 halfExtents)
    {
        centerWorldPosition = default;
        halfExtents = default;

        if (scanAreaReferenceTransform == null || selectedPlaneTransform == null)
            return false;

        if (!TryGetProjectedReferenceRanges(
            scanAreaReferenceTransform,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ))
        {
            return false;
        }

        float width = maxX - minX;
        float depth = maxZ - minZ;
        if (width <= 0.001f || depth <= 0.001f)
            return false;

        halfExtents = new Vector2(width * 0.5f, depth * 0.5f);
        Vector3 centerLocalOnPlane = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
        centerWorldPosition = selectedPlaneTransform.TransformPoint(centerLocalOnPlane);
        return true;
    }

    private bool TryGetProjectedReferenceRanges(
        Transform referenceRoot,
        out float minX,
        out float maxX,
        out float minZ,
        out float maxZ)
    {
        minX = float.MaxValue;
        maxX = float.MinValue;
        minZ = float.MaxValue;
        maxZ = float.MinValue;

        bool hasBounds = false;
        MeshFilter[] meshFilters = referenceRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            if (meshFilter == null || meshFilter.sharedMesh == null)
                continue;

            AddProjectedBoundsCorners(
                meshFilter.sharedMesh.bounds,
                meshFilter.transform,
                ref hasBounds,
                ref minX,
                ref maxX,
                ref minZ,
                ref maxZ);
        }

        if (hasBounds)
            return true;

        Collider[] colliders = referenceRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null)
                continue;

            AddProjectedWorldBoundsCorners(
                col.bounds,
                ref hasBounds,
                ref minX,
                ref maxX,
                ref minZ,
                ref maxZ);
        }

        return hasBounds;
    }

    private void AddProjectedBoundsCorners(
        Bounds localBounds,
        Transform localToWorld,
        ref bool hasBounds,
        ref float minX,
        ref float maxX,
        ref float minZ,
        ref float maxZ)
    {
        Vector3[] corners = GetBoundsCorners(localBounds);
        for (int i = 0; i < corners.Length; i++)
        {
            AddProjectedWorldPoint(
                localToWorld.TransformPoint(corners[i]),
                ref hasBounds,
                ref minX,
                ref maxX,
                ref minZ,
                ref maxZ);
        }
    }

    private void AddProjectedWorldBoundsCorners(
        Bounds worldBounds,
        ref bool hasBounds,
        ref float minX,
        ref float maxX,
        ref float minZ,
        ref float maxZ)
    {
        Vector3[] corners = GetBoundsCorners(worldBounds);
        for (int i = 0; i < corners.Length; i++)
        {
            AddProjectedWorldPoint(
                corners[i],
                ref hasBounds,
                ref minX,
                ref maxX,
                ref minZ,
                ref maxZ);
        }
    }

    private void AddProjectedWorldPoint(
        Vector3 worldPoint,
        ref bool hasBounds,
        ref float minX,
        ref float maxX,
        ref float minZ,
        ref float maxZ)
    {
        Vector3 local = selectedPlaneTransform.InverseTransformPoint(worldPoint);
        minX = Mathf.Min(minX, local.x);
        maxX = Mathf.Max(maxX, local.x);
        minZ = Mathf.Min(minZ, local.z);
        maxZ = Mathf.Max(maxZ, local.z);
        hasBounds = true;
    }

    private Vector3 ScanLocalToWorldPoint(Vector3 localPoint)
    {
        if (selectedPlaneTransform == null)
            return localPoint;

        return ScanLocalToWorldPoint(localPoint, CreateSpatialScanGeometry());
    }

    private SpatialScanGeometry CreateSpatialScanGeometry()
    {
        if (selectedPlaneTransform == null)
        {
            return new SpatialScanGeometry
            {
                center = GetScanCenterWorldPosition(),
                right = Vector3.right,
                up = Vector3.up,
                forward = Vector3.forward,
                planeOrigin = Vector3.zero
            };
        }

        return new SpatialScanGeometry
        {
            // Resolving the reference bounds can walk a mesh hierarchy. Capture
            // it once per pass instead of doing that work for every grid ray.
            center = GetScanCenterWorldPosition(),
            right = selectedPlaneTransform.TransformVector(Vector3.right),
            up = selectedPlaneTransform.up,
            forward = selectedPlaneTransform.TransformVector(Vector3.forward),
            planeOrigin = selectedPlaneTransform.position
        };
    }

    private static Vector3 ScanLocalToWorldPoint(
        Vector3 localPoint,
        SpatialScanGeometry scanGeometry)
    {
        return scanGeometry.center +
            scanGeometry.right * localPoint.x +
            scanGeometry.up * localPoint.y +
            scanGeometry.forward * localPoint.z;
    }

    private bool TryGetSpatialObserverFocusBounds(out Bounds focusBounds)
    {
        focusBounds = default;
        if (selectedPlaneTransform == null)
            return false;

        Vector2 halfExtents = GetScanHalfExtents();
        float minimumHeight = -Mathf.Max(
            Mathf.Max(0f, baselineRayDepthBelowPlane),
            Mathf.Max(0f, maxBottomDistanceFromPlane));
        float maximumHeight = Mathf.Max(
            Mathf.Max(0f, baselineRayHeightAbovePlane),
            Mathf.Max(maxHeightAbovePlane, maxSurfaceChangeHeight));
        SpatialScanGeometry geometry = CreateSpatialScanGeometry();

        Vector3 first = ScanLocalToWorldPoint(
            new Vector3(-halfExtents.x, minimumHeight, -halfExtents.y),
            geometry);
        focusBounds = new Bounds(first, Vector3.zero);

        for (int y = 0; y < 2; y++)
        {
            float localY = y == 0 ? minimumHeight : maximumHeight;
            for (int z = 0; z < 2; z++)
            {
                float localZ = z == 0 ? -halfExtents.y : halfExtents.y;
                for (int x = 0; x < 2; x++)
                {
                    float localX = x == 0 ? -halfExtents.x : halfExtents.x;
                    focusBounds.Encapsulate(ScanLocalToWorldPoint(
                        new Vector3(localX, localY, localZ),
                        geometry));
                }
            }
        }

        float padding = Mathf.Max(0f, spatialObserverBoundsPadding);
        focusBounds.Expand(Vector3.one * padding * 2f);
        focusBounds.extents = Vector3.Max(focusBounds.extents, Vector3.one * 0.05f);
        return true;
    }

    private float GetMinimumSignedPlaneDistance(Bounds bounds)
    {
        Vector3[] corners = GetBoundsCorners(bounds);
        float best = float.MaxValue;

        for (int i = 0; i < corners.Length; i++)
        {
            float d = GetSignedPlaneDistance(corners[i]);
            if (d < best)
                best = d;
        }

        return best;
    }

    private float GetMaximumSignedPlaneDistance(Bounds bounds)
    {
        Vector3[] corners = GetBoundsCorners(bounds);
        float best = float.MinValue;

        for (int i = 0; i < corners.Length; i++)
        {
            float d = GetSignedPlaneDistance(corners[i]);
            if (d > best)
                best = d;
        }

        return best;
    }

    private float GetSignedPlaneDistance(Vector3 point)
    {
        return Vector3.Dot(point - selectedPlaneTransform.position, selectedPlaneTransform.up);
    }

    private static Vector3[] GetBoundsCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        return new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z)
        };
    }

    private static Bounds GetWorldBounds(Transform root, Vector3 fallbackSize)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (TryBuildBounds(renderers, out Bounds rendererBounds))
            return rendererBounds;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        if (TryBuildBounds(colliders, out Bounds colliderBounds))
            return colliderBounds;

        return new Bounds(root.position, fallbackSize);
    }

    private static bool TryBuildBounds(Renderer[] renderers, out Bounds bounds)
    {
        bounds = default;

        if (renderers == null || renderers.Length == 0)
            return false;

        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private static bool TryBuildBounds(Collider[] colliders, out Bounds bounds)
    {
        bounds = default;

        if (colliders == null || colliders.Length == 0)
            return false;

        bool hasBounds = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null)
                continue;

            if (!hasBounds)
            {
                bounds = col.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(col.bounds);
            }
        }

        return hasBounds;
    }

    private static float GetApproximateSize(Bounds bounds)
    {
        Vector3 size = bounds.size;
        return Mathf.Max(size.x, Mathf.Max(size.y, size.z));
    }

    private GameObject CreateCandidateDebugVisual(PhysicalObjectCandidate candidate)
    {
        GameObject visual;

        if (candidateDebugVisualPrefab != null)
        {
            visual = Instantiate(candidateDebugVisualPrefab, candidate.worldPosition, Quaternion.identity, candidateDebugVisualRoot);
        }
        else
        {
            visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = $"CandidateDebug_{candidate.id}";
            visual.transform.SetParent(candidateDebugVisualRoot, true);
            visual.transform.position = candidate.worldPosition + selectedPlaneTransform.up * 0.03f;
            visual.transform.localScale = Vector3.one * defaultDebugVisualSize;

            Collider col = visual.GetComponent<Collider>();
            if (col != null)
                col.enabled = false;
        }

        return visual;
    }

    private void RebuildBaselineSurfaceDebugVisual()
    {
        if (!showSpatialScanSurfaceDebug || !hasSpatialBaseline || baselineHeights == null || baselineValid == null)
        {
            ClearBaselineSurfaceDebugVisual();
            return;
        }

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        Vector2 effectiveScanHalfExtents = GetScanHalfExtents();

        for (int z = 0; z < baselineGridZ - 1; z++)
        {
            for (int x = 0; x < baselineGridX - 1; x++)
            {
                int i00 = GetSampleIndex(x, z, baselineGridX);
                int i10 = GetSampleIndex(x + 1, z, baselineGridX);
                int i01 = GetSampleIndex(x, z + 1, baselineGridX);
                int i11 = GetSampleIndex(x + 1, z + 1, baselineGridX);

                if (!baselineValid[i00] || !baselineValid[i10] || !baselineValid[i01] || !baselineValid[i11])
                    continue;

                GetLocalSamplePosition(x, z, baselineGridX, baselineGridZ, effectiveScanHalfExtents, out float x00, out float z00);
                GetLocalSamplePosition(x + 1, z, baselineGridX, baselineGridZ, effectiveScanHalfExtents, out float x10, out float z10);
                GetLocalSamplePosition(x, z + 1, baselineGridX, baselineGridZ, effectiveScanHalfExtents, out float x01, out float z01);
                GetLocalSamplePosition(x + 1, z + 1, baselineGridX, baselineGridZ, effectiveScanHalfExtents, out float x11, out float z11);

                int start = vertices.Count;
                vertices.Add(BuildSurfaceDebugWorldPoint(x00, baselineHeights[i00], z00, surfaceDebugVerticalOffset));
                vertices.Add(BuildSurfaceDebugWorldPoint(x10, baselineHeights[i10], z10, surfaceDebugVerticalOffset));
                vertices.Add(BuildSurfaceDebugWorldPoint(x01, baselineHeights[i01], z01, surfaceDebugVerticalOffset));
                vertices.Add(BuildSurfaceDebugWorldPoint(x11, baselineHeights[i11], z11, surfaceDebugVerticalOffset));

                AddDoubleSidedQuadTriangles(triangles, start);
            }
        }

        ApplySurfaceDebugMesh(
            ref baselineSurfaceDebugObject,
            ref baselineSurfaceDebugMesh,
            "SpatialBaselineSurfaceDebug",
            vertices,
            triangles,
            GetSurfaceDebugMaterial(
                baselineSurfaceDebugMaterial,
                ref runtimeBaselineSurfaceDebugMaterial,
                baselineSurfaceDebugColor,
                "Spatial Baseline Surface Debug Material"));
    }

    private void RebuildChangedSurfaceDebugVisual()
    {
        if (!showSpatialScanSurfaceDebug || changedSamples.Count == 0 || baselineGridX <= 1 || baselineGridZ <= 1)
        {
            ClearChangedSurfaceDebugVisual();
            return;
        }

        List<Vector3> vertices = new List<Vector3>(changedSamples.Count * 4);
        List<int> triangles = new List<int>(changedSamples.Count * 6);
        Vector2 effectiveScanHalfExtents = GetScanHalfExtents();
        float cellX = (effectiveScanHalfExtents.x * 2f) / (baselineGridX - 1);
        float cellZ = (effectiveScanHalfExtents.y * 2f) / (baselineGridZ - 1);
        float halfCellX = cellX * 0.5f;
        float halfCellZ = cellZ * 0.5f;

        for (int i = 0; i < changedSamples.Count; i++)
        {
            ChangedSurfaceSample sample = changedSamples[i];
            float minX = sample.localX - halfCellX;
            float maxX = sample.localX + halfCellX;
            float minZ = sample.localZ - halfCellZ;
            float maxZ = sample.localZ + halfCellZ;
            int start = vertices.Count;

            vertices.Add(BuildSurfaceDebugWorldPoint(minX, sample.currentHeight, minZ, surfaceDebugVerticalOffset * 2f));
            vertices.Add(BuildSurfaceDebugWorldPoint(maxX, sample.currentHeight, minZ, surfaceDebugVerticalOffset * 2f));
            vertices.Add(BuildSurfaceDebugWorldPoint(minX, sample.currentHeight, maxZ, surfaceDebugVerticalOffset * 2f));
            vertices.Add(BuildSurfaceDebugWorldPoint(maxX, sample.currentHeight, maxZ, surfaceDebugVerticalOffset * 2f));

            AddDoubleSidedQuadTriangles(triangles, start);
        }

        ApplySurfaceDebugMesh(
            ref changedSurfaceDebugObject,
            ref changedSurfaceDebugMesh,
            "SpatialChangedSurfaceDebug",
            vertices,
            triangles,
            GetSurfaceDebugMaterial(
                changedSurfaceDebugMaterial,
                ref runtimeChangedSurfaceDebugMaterial,
                changedSurfaceDebugColor,
                "Spatial Changed Surface Debug Material"));
    }

    private Vector3 BuildSurfaceDebugWorldPoint(float localX, float localHeight, float localZ, float verticalOffset)
    {
        return ScanLocalToWorldPoint(new Vector3(localX, localHeight + verticalOffset, localZ));
    }

    private static void AddDoubleSidedQuadTriangles(List<int> triangles, int start)
    {
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 1);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start + 3);

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start + 1);
        triangles.Add(start + 3);
        triangles.Add(start + 2);
    }

    private void ApplySurfaceDebugMesh(
        ref GameObject surfaceObject,
        ref Mesh surfaceMesh,
        string objectName,
        List<Vector3> vertices,
        List<int> triangles,
        Material material)
    {
        if (vertices.Count == 0 || triangles.Count == 0)
        {
            ClearSurfaceDebugVisual(ref surfaceObject, ref surfaceMesh);
            return;
        }

        if (surfaceObject == null)
        {
            surfaceObject = new GameObject(objectName);
            surfaceObject.transform.SetParent(spatialScanSurfaceDebugRoot != null ? spatialScanSurfaceDebugRoot : transform, true);
            surfaceObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            surfaceObject.transform.localScale = Vector3.one;
            surfaceObject.AddComponent<MeshFilter>();
            surfaceObject.AddComponent<MeshRenderer>();
        }

        if (surfaceMesh == null)
        {
            surfaceMesh = new Mesh
            {
                name = $"{objectName}Mesh"
            };
        }

        surfaceMesh.Clear();
        surfaceMesh.SetVertices(vertices);
        surfaceMesh.SetTriangles(triangles, 0);
        surfaceMesh.RecalculateNormals();
        surfaceMesh.RecalculateBounds();

        MeshFilter meshFilter = surfaceObject.GetComponent<MeshFilter>();
        if (meshFilter != null)
            meshFilter.sharedMesh = surfaceMesh;

        MeshRenderer meshRenderer = surfaceObject.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            meshRenderer.sharedMaterial = material;

        surfaceObject.SetActive(true);
    }

    private Material GetSurfaceDebugMaterial(Material assignedMaterial, ref Material runtimeMaterial, Color color, string materialName)
    {
        if (assignedMaterial != null)
            return assignedMaterial;

        if (runtimeMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");

            runtimeMaterial = new Material(shader)
            {
                name = materialName
            };
        }

        runtimeMaterial.color = color;
        runtimeMaterial.renderQueue = 3000;
        return runtimeMaterial;
    }

    private void ClearBaselineSurfaceDebugVisual()
    {
        ClearSurfaceDebugVisual(ref baselineSurfaceDebugObject, ref baselineSurfaceDebugMesh);
    }

    private void ClearChangedSurfaceDebugVisual()
    {
        ClearSurfaceDebugVisual(ref changedSurfaceDebugObject, ref changedSurfaceDebugMesh);
    }

    private void ClearSurfaceDebugVisual(ref GameObject surfaceObject, ref Mesh surfaceMesh)
    {
        if (surfaceObject != null)
            DestroyObject(surfaceObject);
        else if (surfaceMesh != null)
            DestroyObject(surfaceMesh);

        surfaceObject = null;
        surfaceMesh = null;
    }

    private void BeginSpatialScanRayDebugPass()
    {
        spatialScanRayDebugSampleCount = 0;
        spatialScanRayHitDebugSamples.Clear();
        spatialScanRayMissDebugSamples.Clear();

        if (!showSpatialScanRayDebug)
        {
            ClearSpatialScanRayDebugVisual();
            isRecordingSpatialScanRayDebugPass = false;
            return;
        }

        isRecordingSpatialScanRayDebugPass = true;
    }

    private void EndSpatialScanRayDebugPass()
    {
        if (!isRecordingSpatialScanRayDebugPass)
            return;

        isRecordingSpatialScanRayDebugPass = false;
        RebuildSpatialScanRayDebugVisual();
    }

    private void RecordSpatialScanRayDebugSample(Vector3 origin, Vector3 end, Vector3 hitPoint, bool hasHit)
    {
        if (!showSpatialScanRayDebug)
            return;

        int sampleIndex = spatialScanRayDebugSampleCount++;
        int stride = Mathf.Max(1, spatialScanRayDebugStride);
        if (sampleIndex % stride != 0)
            return;

        Color color = hasHit ? spatialScanRayHitDebugColor : spatialScanRayMissDebugColor;
        float duration = Mathf.Max(0f, spatialScanRayDebugDurationSeconds);
        Debug.DrawLine(origin, end, color, duration);

        if (hasHit && spatialScanRayHitMarkerSize > 0f)
            DrawSpatialScanRayHitMarker(hitPoint, color, duration);

        if (!isRecordingSpatialScanRayDebugPass)
            return;

        SpatialScanRayDebugSample sample = new SpatialScanRayDebugSample
        {
            origin = origin,
            end = end,
            hitPoint = hitPoint,
            hasHit = hasHit
        };

        if (hasHit)
            spatialScanRayHitDebugSamples.Add(sample);
        else
            spatialScanRayMissDebugSamples.Add(sample);
    }

    private void DrawSpatialScanRayHitMarker(Vector3 hitPoint, Color color, float duration)
    {
        Vector3 right = selectedPlaneTransform != null ? selectedPlaneTransform.right : Vector3.right;
        Vector3 up = selectedPlaneTransform != null ? selectedPlaneTransform.up : Vector3.up;
        Vector3 forward = selectedPlaneTransform != null ? selectedPlaneTransform.forward : Vector3.forward;
        float markerSize = Mathf.Max(0f, spatialScanRayHitMarkerSize);

        Debug.DrawLine(hitPoint - right * markerSize, hitPoint + right * markerSize, color, duration);
        Debug.DrawLine(hitPoint - up * markerSize, hitPoint + up * markerSize, color, duration);
        Debug.DrawLine(hitPoint - forward * markerSize, hitPoint + forward * markerSize, color, duration);
    }

    private void RebuildSpatialScanRayDebugVisual()
    {
        if (!showSpatialScanRayDebug)
        {
            ClearSpatialScanRayDebugVisual();
            return;
        }

        ApplySpatialScanRayDebugMesh(
            ref spatialScanRayHitDebugObject,
            ref spatialScanRayHitDebugMesh,
            "SpatialScanRayHitDebug",
            spatialScanRayHitDebugSamples,
            GetSurfaceDebugMaterial(
                spatialScanRayHitDebugMaterial,
                ref runtimeSpatialScanRayHitDebugMaterial,
                spatialScanRayHitDebugColor,
                "Spatial Scan Ray Hit Debug Material"));

        ApplySpatialScanRayDebugMesh(
            ref spatialScanRayMissDebugObject,
            ref spatialScanRayMissDebugMesh,
            "SpatialScanRayMissDebug",
            spatialScanRayMissDebugSamples,
            GetSurfaceDebugMaterial(
                spatialScanRayMissDebugMaterial,
                ref runtimeSpatialScanRayMissDebugMaterial,
                spatialScanRayMissDebugColor,
                "Spatial Scan Ray Miss Debug Material"));
    }

    private void ApplySpatialScanRayDebugMesh(
        ref GameObject rayObject,
        ref Mesh rayMesh,
        string objectName,
        List<SpatialScanRayDebugSample> samples,
        Material material)
    {
        if (samples.Count == 0)
        {
            ClearSpatialScanRayDebugVisual(ref rayObject, ref rayMesh);
            return;
        }

        List<Vector3> vertices = new List<Vector3>(samples.Count * 8);
        List<int> indices = new List<int>(samples.Count * 8);

        for (int i = 0; i < samples.Count; i++)
        {
            SpatialScanRayDebugSample sample = samples[i];
            AddSpatialScanRayDebugLine(vertices, indices, sample.origin, sample.end);

            if (sample.hasHit && spatialScanRayHitMarkerSize > 0f)
                AddSpatialScanRayDebugHitMarker(vertices, indices, sample.hitPoint);
        }

        if (vertices.Count == 0 || indices.Count == 0)
        {
            ClearSpatialScanRayDebugVisual(ref rayObject, ref rayMesh);
            return;
        }

        if (rayObject == null)
        {
            rayObject = new GameObject(objectName);
            rayObject.transform.SetParent(spatialScanRayDebugRoot != null ? spatialScanRayDebugRoot : transform, true);
            rayObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rayObject.transform.localScale = Vector3.one;
            rayObject.AddComponent<MeshFilter>();
            rayObject.AddComponent<MeshRenderer>();
        }

        if (rayMesh == null)
        {
            rayMesh = new Mesh
            {
                name = $"{objectName}Mesh",
                indexFormat = IndexFormat.UInt32
            };
        }

        rayMesh.Clear();
        rayMesh.SetVertices(vertices);
        rayMesh.SetIndices(indices, MeshTopology.Lines, 0);
        rayMesh.RecalculateBounds();

        MeshFilter meshFilter = rayObject.GetComponent<MeshFilter>();
        if (meshFilter != null)
            meshFilter.sharedMesh = rayMesh;

        MeshRenderer meshRenderer = rayObject.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            meshRenderer.sharedMaterial = material;

        rayObject.SetActive(true);
    }

    private void AddSpatialScanRayDebugHitMarker(List<Vector3> vertices, List<int> indices, Vector3 hitPoint)
    {
        Vector3 right = selectedPlaneTransform != null ? selectedPlaneTransform.right : Vector3.right;
        Vector3 up = selectedPlaneTransform != null ? selectedPlaneTransform.up : Vector3.up;
        Vector3 forward = selectedPlaneTransform != null ? selectedPlaneTransform.forward : Vector3.forward;
        float markerSize = Mathf.Max(0f, spatialScanRayHitMarkerSize);

        AddSpatialScanRayDebugLine(vertices, indices, hitPoint - right * markerSize, hitPoint + right * markerSize);
        AddSpatialScanRayDebugLine(vertices, indices, hitPoint - up * markerSize, hitPoint + up * markerSize);
        AddSpatialScanRayDebugLine(vertices, indices, hitPoint - forward * markerSize, hitPoint + forward * markerSize);
    }

    private static void AddSpatialScanRayDebugLine(List<Vector3> vertices, List<int> indices, Vector3 start, Vector3 end)
    {
        int startIndex = vertices.Count;
        vertices.Add(start);
        vertices.Add(end);
        indices.Add(startIndex);
        indices.Add(startIndex + 1);
    }

    private void ClearSpatialScanRayDebugVisual()
    {
        ClearSpatialScanRayDebugVisual(ref spatialScanRayHitDebugObject, ref spatialScanRayHitDebugMesh);
        ClearSpatialScanRayDebugVisual(ref spatialScanRayMissDebugObject, ref spatialScanRayMissDebugMesh);
        spatialScanRayHitDebugSamples.Clear();
        spatialScanRayMissDebugSamples.Clear();
    }

    private void ClearSpatialScanRayDebugVisual(ref GameObject rayObject, ref Mesh rayMesh)
    {
        if (rayObject != null)
            DestroyObject(rayObject);
        else if (rayMesh != null)
            DestroyObject(rayMesh);

        rayObject = null;
        rayMesh = null;
    }

    private int GetRealSpatialLayerMask()
    {
        int layerMask = realSpatialLayers.value;

        if (useMrtkSpatialAwarenessLayer)
        {
            IMixedRealitySpatialAwarenessMeshObserver observer = GetSpatialMeshObserver();
            if (observer != null)
                layerMask |= observer.MeshPhysicsLayerMask;
        }

        if (layerMask == 0 && useDefaultRaycastLayersWhenSpatialObserverUnavailable)
            layerMask = Physics.DefaultRaycastLayers;

        return layerMask;
    }

    private IMixedRealitySpatialAwarenessMeshObserver GetSpatialMeshObserver()
    {
        TryRegisterSpatialMeshEvents();

        if (spatialMeshObserver == null)
            spatialMeshObserver = CoreServices.GetSpatialAwarenessSystemDataProvider<IMixedRealitySpatialAwarenessMeshObserver>();

        return spatialMeshObserver;
    }

    private void TryRegisterSpatialMeshEvents()
    {
        if (!reactToSpatialMeshUpdateEvents || spatialMeshEventsRegistered)
            return;

        IMixedRealitySpatialAwarenessSystem system = CoreServices.SpatialAwarenessSystem;
        if (system == null)
            return;

        system.RegisterHandler<
            IMixedRealitySpatialAwarenessObservationHandler<SpatialAwarenessMeshObject>>(this);
        spatialMeshEventsRegistered = true;
    }

    private void UnregisterSpatialMeshEvents()
    {
        if (!spatialMeshEventsRegistered)
            return;

        IMixedRealitySpatialAwarenessSystem system = CoreServices.SpatialAwarenessSystem;
        if (system != null)
        {
            system.UnregisterHandler<
                IMixedRealitySpatialAwarenessObservationHandler<SpatialAwarenessMeshObject>>(this);
        }

        spatialMeshEventsRegistered = false;
    }

    private void RecordRelevantSpatialMeshUpdate(SpatialAwarenessMeshObject meshObject)
    {
        if (!reactToSpatialMeshUpdateEvents ||
            (currentMode == SpatialPlacementMode.None && !IsPreparingAutomaticBaseline) ||
            meshObject?.Collider == null)
        {
            return;
        }

        Bounds focusBounds;
        bool hasFocusBounds = hasSpatialObserverFocusBounds;
        if (hasFocusBounds)
        {
            focusBounds = spatialObserverFocusBounds;
        }
        else
        {
            hasFocusBounds = TryGetSpatialObserverFocusBounds(out focusBounds);
        }

        if (hasFocusBounds && !focusBounds.Intersects(meshObject.Collider.bounds))
        {
            return;
        }

        using (RelevantMeshUpdateProfilerMarker.Auto())
        {
            unchecked
            {
                spatialMeshRevision++;
            }

            lastRelevantSpatialMeshUpdateTime = Time.realtimeSinceStartup;
            spatialObserverMeshesResetByLatestConfiguration = false;
        }
    }

    private bool ShouldIgnoreCollider(Collider candidate)
    {
        if (candidate == null)
            return true;

        int instanceId = candidate.GetInstanceID();
        if (spatialColliderIgnoreCache.TryGetValue(instanceId, out bool shouldIgnore))
            return shouldIgnore;

        shouldIgnore = ShouldIgnoreTransform(candidate.transform);
        spatialColliderIgnoreCache[instanceId] = shouldIgnore;
        return shouldIgnore;
    }

    private bool ShouldIgnoreTransform(Transform candidate)
    {
        if (candidate == null)
            return true;

        if (scanAreaReferenceTransform != null &&
            (candidate == scanAreaReferenceTransform || candidate.IsChildOf(scanAreaReferenceTransform)))
        {
            return true;
        }

        if (ignoreSelectedPlaneChildren &&
            selectedPlaneTransform != null &&
            candidate != selectedPlaneTransform &&
            candidate.IsChildOf(selectedPlaneTransform))
        {
            return true;
        }

        if (ignoreCityAnchorChildren && cityAnchorManager != null)
        {
            Transform cityRoot = cityAnchorManager.CityAnchorRoot;
            if (cityRoot != null && candidate != cityRoot && candidate.IsChildOf(cityRoot))
                return true;
        }

        if (candidate.GetComponentInParent<Canvas>() != null)
            return true;

        if (candidate.GetComponentInParent<BuildingMarker>() != null)
            return true;

        if (candidate.GetComponentInParent<FloodSource>() != null)
            return true;

        if (candidate.GetComponentInParent<RouteVisualizer>() != null)
            return true;

        if (candidate.GetComponentInParent<SpatialObjectPreviewPresenter>() != null)
            return true;

        for (int i = visualizationIgnoredRoots.Count - 1; i >= 0; i--)
        {
            Transform ignored = visualizationIgnoredRoots[i];
            if (ignored == null)
            {
                visualizationIgnoredRoots.RemoveAt(i);
                continue;
            }

            if (candidate == ignored || candidate.IsChildOf(ignored))
                return true;
        }

        if (ignoredRoots == null)
            return false;

        for (int i = 0; i < ignoredRoots.Length; i++)
        {
            Transform ignored = ignoredRoots[i];
            if (ignored != null && candidate.IsChildOf(ignored))
                return true;
        }

        return false;
    }

    private void ResolveMissingReferences()
    {
        cityAnchorManager ??= FindFirstObjectByType<CityAnchorManager>(FindObjectsInactive.Include);
        cityPlacementManager ??= FindFirstObjectByType<CityPlacementManager>(FindObjectsInactive.Include);

        if (selectedPlaneTransform == null && cityAnchorManager != null)
            selectedPlaneTransform = cityAnchorManager.CityAnchorRoot;

        if (scanCenterTransform == null && cityPlacementManager != null)
            scanCenterTransform = cityPlacementManager.PlacementPivotTransform;

        if (scanCenterTransform == null && selectedPlaneTransform != null)
            scanCenterTransform = FindChildRecursive(selectedPlaneTransform, "CityCenterPivot");

        if (candidateDebugVisualRoot == null)
            candidateDebugVisualRoot = transform;
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private new static void DestroyObject(UnityEngine.Object go)
    {
        if (go == null)
            return;

        if (Application.isPlaying)
            Destroy(go);
        else
            DestroyImmediate(go);
    }
}
