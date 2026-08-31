using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public sealed class BuildingSelectionTechniqueController : MonoBehaviour
{
    private const string LiveBuildingPagePath =
        "HandMenu_Large_WorldLock_On_GrabAndPull/MenuContent/MainControlSlate/ContentRoot/BuildingDetection_V1Blue";
    private const string BuildingPageUnderSlatePath = "ContentRoot/BuildingDetection_V1Blue";

    private sealed class TechniqueSelectorUiBinding
    {
        public RectTransform root;
        public GameObject directButton;
        public GameObject assistedButton;
        public Text directLabel;
        public Text assistedLabel;
        public Text scanLabel;
        public string originalScanLabel;
        public SciFiButtonSpriteBridge scanBridge;
    }

    [Header("References")]
    [SerializeField] private SpatialObjectDetectionManager detectionManager;
    [SerializeField] private SpatialBuildingObjectInterpreter buildingInterpreter;
    [SerializeField] private PointSelectManager pointSelectManager;
    [SerializeField] private CityAnchorManager cityAnchorManager;
    [SerializeField] private CityManager cityManager;
    [SerializeField] private VisualizationModeController visualizationModeController;
    [SerializeField] private MRNotification notifier;

    [Header("Technique")]
    [SerializeField] private BuildingSelectionTechnique currentTechnique = BuildingSelectionTechnique.Direct;

    [Header("Assisted Lens Tracking")]
    [SerializeField, Min(0.1f)] private float spatialDifferenceUpdateInterval = 0.2f;
    [SerializeField, Min(0.02f)] private float minimumEventDrivenUpdateInterval = 0.12f;
    [SerializeField, Min(0f)] private float missingCandidatePersistence = 0.9f;
    [Tooltip("Extra candidate-level verification after the mesh-change detector's own temporal stability check.")]
    [SerializeField, Min(1)] private int candidateVerificationPasses = 1;
    [SerializeField, Min(0.005f)] private float candidateVerificationAssociationRadius = 0.04f;
    [SerializeField, Min(0.1f)] private float candidateVerificationMaximumGap = 1.5f;

    [Header("Assisted Lens Presentation")]
    [SerializeField, Range(1.25f, 6f)] private float magnificationFactor = 3f;
    [Tooltip("World-space distance from the detected candidate. A building is included only when its center is within this radius.")]
    [SerializeField, Min(0.01f)] private float candidateZoneRadius = 0.05f;
    [SerializeField, Min(0f)] private float lensHeightOffset = 0.12f;
    [SerializeField, Min(0f)] private float lensTowardUserOffset = 0.035f;
    [Tooltip("Non-building renderers broader than this are omitted, except recognized terrain surfaces which are circularly clipped to the lens.")]
    [SerializeField, Min(0.05f)] private float maximumContextRendererExtent = 0.5f;

    [Header("Assisted Lens Near Selection")]
    [SerializeField, Min(0.1f)] private float dwellSelectionTime = 2.5f;
    [Tooltip("After selection, the physical candidate that created the lens must leave this radius before it can create another lens.")]
    [SerializeField, Min(0.005f)] private float selectedCandidateReleaseDistance = 0.03f;
    [Tooltip("A dismissed candidate must remain absent for this long. This prevents one missing spatial-mesh frame from reopening the lens.")]
    [SerializeField, Min(0f)] private float selectedCandidateReleasePersistence = 0.9f;

    [Header("Assisted Lens Context Labels")]
    [SerializeField] private bool showBuildingLabels = true;
    [SerializeField] private bool showStreetLabels = true;
    [SerializeField, Min(0f)] private float buildingLabelHeightOffset = 0.03f;
    [SerializeField, Min(0f)] private float streetLabelHeightOffset = 0.018f;
    [Tooltip("Target world-space capital-letter height in meters.")]
    [SerializeField, Min(0.001f)] private float buildingLabelCharacterHeight = 0.008f;
    [Tooltip("Target world-space capital-letter height in meters.")]
    [SerializeField, Min(0.001f)] private float streetLabelCharacterHeight = 0.007f;

    [Header("Scene UI")]
    [SerializeField] private bool createTechniqueSelector = true;
    [SerializeField] private Vector2 techniqueSelectorAnchoredPosition = new Vector2(0f, -310f);
    [SerializeField] private Vector2 techniqueButtonSpacing = new Vector2(420f, 0f);

    [Header("Study Instrumentation")]
    [SerializeField] private bool beginTrialOnBuildingModeEnter = true;
    [SerializeField] private bool logCompletedTrialAsJson = true;

    public BuildingSelectionTechnique CurrentTechnique => currentTechnique;
    public float MagnificationFactor => magnificationFactor;
    public BuildingSelectionTrialRecord CurrentTrial => currentTrial;
    public IReadOnlyList<string> FrozenLensBuildingIds => frozenLensBuildingIds;
    public bool IsAssistedLensActive =>
        currentTechnique == BuildingSelectionTechnique.AssistedLens &&
        detectionManager != null &&
        detectionManager.isActiveAndEnabled &&
        detectionManager.CurrentMode == SpatialPlacementMode.BuildingPlacing;
    public bool IsDirectBuildingPlacementActive =>
        currentTechnique == BuildingSelectionTechnique.Direct &&
        detectionManager != null &&
        detectionManager.CurrentMode == SpatialPlacementMode.BuildingPlacing;

    public event Action<BuildingSelectionTechnique> TechniqueChanged;
    public event Action<BuildingSelectionTrialRecord> TrialStarted;
    public event Action<BuildingSelectionTrialRecord> TrialUpdated;
    public event Action<BuildingSelectionTrialRecord> TrialCompleted;

    private Coroutine assistedMonitoringCoroutine;
    private MagnificationLensView lensView;
    private BuildingSelectionTrialRecord currentTrial;
    private bool subscribed;
    private bool visualizationWasActive;
    private bool hasFrozenLensZone;
    private Vector3 frozenLensFocusWorld;
    private float lastReliableCandidateTime = float.NegativeInfinity;
    private bool hasDismissedLensCandidate;
    private Vector3 dismissedLensCandidateWorld;
    private float dismissedLensCandidateLastSeenTime = float.NegativeInfinity;
    private Vector3 latestLensCandidateWorld;
    private bool hasPendingLensCandidate;
    private Vector3 pendingLensCandidateWorld;
    private float pendingLensCandidateLastSeenTime = float.NegativeInfinity;
    private int pendingLensCandidatePasses;
    private readonly List<string> frozenLensBuildingIds = new List<string>();

    private RectTransform selectorRoot;
    private GameObject directTechniqueButton;
    private GameObject assistedTechniqueButton;
    private Text directTechniqueLabel;
    private Text assistedTechniqueLabel;
    private Text scanObjectsLabel;
    private string originalScanObjectsLabel;
    private SciFiButtonSpriteBridge scanObjectsBridge;
    private readonly List<TechniqueSelectorUiBinding> selectorUiBindings = new();

    private void Awake()
    {
        ResolveMissingReferences();
    }

    private void OnEnable()
    {
        ResolveMissingReferences();
        Subscribe();
        RefreshCityBuildingHandInteraction();

        if (IsAssistedLensActive)
            StartAssistedMonitoring();
    }

    private void Start()
    {
        ResolveMissingReferences();
        if (subscribed)
        {
            Unsubscribe();
            Subscribe();
        }

        visualizationWasActive = VisualizationModeController.Instance != null &&
            VisualizationModeController.Instance.IsVisualizationModeActive;

        if (createTechniqueSelector)
            EnsureTechniqueSelector();

        RefreshTechniqueUi();
        RefreshCityBuildingHandInteraction();

        if (IsAssistedLensActive)
            StartAssistedMonitoring();
    }

    private void Update()
    {
        bool visualizationActive = VisualizationModeController.Instance != null &&
            VisualizationModeController.Instance.IsVisualizationModeActive;
        if (visualizationActive == visualizationWasActive)
            return;

        if (visualizationActive)
            HandleVisualizationEntering();
        else
            HandleVisualizationExited();
    }

    private void OnDisable()
    {
        StopAssistedMonitoring();
        HideLensAndClearFocus();
        detectionManager?.SetTwoBuildingSelectionCandidatesAvailable(false);
        pointSelectManager?.SetCityBuildingHandInteractionEnabled(true);
        Unsubscribe();
    }

    private void OnDestroy()
    {
        if (detectionManager != null && lensView != null)
            detectionManager.RemoveVisualizationIgnoredRoot(lensView.transform);

        if (lensView != null)
            Destroy(lensView.gameObject);
    }

    public void SetDirectTechnique()
    {
        SetBuildingSelectionTechnique(BuildingSelectionTechnique.Direct);
    }

    public void SetAssistedLensTechnique()
    {
        SetBuildingSelectionTechnique(BuildingSelectionTechnique.AssistedLens);
    }

    public void SetBuildingSelectionTechnique(BuildingSelectionTechnique technique)
    {
        ResolveMissingReferences();

        if (currentTechnique == technique)
        {
            RefreshTechniqueUi();
            RefreshCityBuildingHandInteraction();

            if (technique == BuildingSelectionTechnique.AssistedLens && IsAssistedLensActive)
                StartAssistedMonitoring();

            return;
        }

        StopAssistedMonitoring();
        detectionManager?.StopRefreshingSpatialScan();
        buildingInterpreter?.ClearSelection();
        ClearDismissedLensCandidate();

        if (pointSelectManager != null && !pointSelectManager.HasCurrentPath)
            pointSelectManager.ResetSelection();

        HideLensAndClearFocus();
        detectionManager?.ClearDetectedCandidates();

        currentTechnique = technique;
        RefreshCityBuildingHandInteraction();
        TechniqueChanged?.Invoke(currentTechnique);
        RefreshTechniqueUi();

        if (detectionManager != null &&
            detectionManager.CurrentMode == SpatialPlacementMode.BuildingPlacing)
        {
            BeginTrial();

            if (currentTechnique == BuildingSelectionTechnique.AssistedLens)
                StartAssistedMonitoring();
        }

        if (currentTechnique == BuildingSelectionTechnique.Direct)
        {
            notifier?.Show("Direct selection: physical objects select the two buildings.");
        }
        else if (detectionManager != null &&
            detectionManager.CurrentMode == SpatialPlacementMode.BuildingPlacing &&
            !detectionManager.IsPreparingAutomaticBaseline &&
            detectionManager.IsCandidateScanReady)
        {
            notifier?.Show("Assisted Lens ready: place or move one physical object to aim the lens.");
        }
        else
        {
            notifier?.Show("Assisted Lens selected. Keep the tabletop clear while its baseline is prepared.");
        }
    }

    public BuildingSelectionTrialRecord BeginTrial()
    {
        currentTrial = new BuildingSelectionTrialRecord
        {
            trialId = Guid.NewGuid().ToString("N"),
            technique = currentTechnique,
            trialStartTime = Time.realtimeSinceStartupAsDouble,
            magnificationFactor = magnificationFactor
        };

        TrialStarted?.Invoke(currentTrial);
        return currentTrial;
    }

    public bool ConfirmAssistedSelection()
    {
        ResolveMissingReferences();

        if (!IsAssistedLensActive || pointSelectManager == null)
            return false;

        pointSelectManager.ConfirmPath();
        return pointSelectManager.HasCurrentPath;
    }

    public void ClearAssistedSelection()
    {
        ClearAssistedSelection(false);
    }

    public void ClearAssistedSelection(bool preserveConfirmedRoute)
    {
        if (currentTechnique != BuildingSelectionTechnique.AssistedLens)
            return;

        if (!preserveConfirmedRoute || pointSelectManager == null || !pointSelectManager.HasCurrentPath)
            pointSelectManager?.ResetSelection();

        lensView?.RefreshSelection(pointSelectManager?.CaptureVisualizationSnapshot());

        if (detectionManager != null &&
            detectionManager.CurrentMode == SpatialPlacementMode.BuildingPlacing)
        {
            BeginTrial();
        }
    }

    public void RequestImmediateLensUpdate()
    {
        if (!IsAssistedLensActive || detectionManager == null ||
            detectionManager.IsSuspendedForVisualization)
        {
            return;
        }

        if (detectionManager.IsPreparingAutomaticBaseline || !detectionManager.IsCandidateScanReady)
        {
            notifier?.Show(detectionManager.IsPreparingAutomaticBaseline
                ? "Preparing the Assisted Lens baseline. Keep the tabletop clear for a moment."
                : "Assisted Lens has no tabletop baseline. Press the lens button again to retry.");
            return;
        }

        UpdateLensFromCandidates(detectionManager.ScanForObjects());
    }

    public void UpdateLensFromCandidates(IReadOnlyList<PhysicalObjectCandidate> candidates)
    {
        if (!IsAssistedLensActive)
            return;

        PhysicalObjectCandidate candidate = ChooseLensCandidate(candidates);
        if (candidate == null)
        {
            ResetCandidateVerification();
            if (Time.unscaledTime - lastReliableCandidateTime > missingCandidatePersistence)
                HideLensAndClearFocus();
            return;
        }

        // Once a candidate creates a lens, neither its focus nor its building
        // membership may drift with later spatial scans. The scans below are
        // used only to determine whether a physical candidate is still present.
        if (hasFrozenLensZone)
        {
            lastReliableCandidateTime = Time.unscaledTime;
            return;
        }

        if (!IsCandidateTemporallyVerified(candidate))
            return;

        lastReliableCandidateTime = Time.unscaledTime;

        ResolveMissingReferences();
        if (cityAnchorManager == null || cityAnchorManager.CityAnchorRoot == null || !EnsureLensView())
            return;

        Vector3 target = candidate.worldPosition;
        frozenLensFocusWorld = target;
        latestLensCandidateWorld = candidate.worldPosition;
        Transform canonicalRoot = cityAnchorManager.CityAnchorRoot;
        Vector3 canonicalLocalFocus = canonicalRoot.InverseTransformPoint(frozenLensFocusWorld);
        if (!CaptureFrozenBuildingList(candidate, canonicalRoot))
            return;

        hasFrozenLensZone = true;
        lensView.ShowAt(
            frozenLensFocusWorld,
            canonicalLocalFocus,
            frozenLensBuildingIds);
        lensView.RefreshSelection(pointSelectManager?.CaptureVisualizationSnapshot());

        if (currentTrial != null)
        {
            currentTrial.latestPhysicalFocusWorld = target;
            currentTrial.latestLensFocusLocal = canonicalLocalFocus;
            TrialUpdated?.Invoke(currentTrial);
        }
    }

    public void RecordDirectCandidates(IReadOnlyList<PhysicalObjectCandidate> candidates)
    {
        if (currentTechnique != BuildingSelectionTechnique.Direct || currentTrial == null)
            return;

        if (candidates == null)
        {
            currentTrial.directPhysicalCandidatePositions = Array.Empty<Vector3>();
        }
        else
        {
            List<Vector3> positions = new List<Vector3>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] != null && candidates[i].isValid)
                    positions.Add(candidates[i].worldPosition);
            }

            currentTrial.directPhysicalCandidatePositions = positions.ToArray();
        }

        TrialUpdated?.Invoke(currentTrial);
    }

    public bool SelectBuildingFromLens(string buildingId)
    {
        if (!IsAssistedLensActive || pointSelectManager == null || string.IsNullOrWhiteSpace(buildingId))
            return false;

        bool selected = pointSelectManager.SelectBuildingById(
            buildingId,
            BuildingSelectionSource.Lens);
        if (!selected)
            return false;

        if (hasFrozenLensZone)
        {
            dismissedLensCandidateWorld = latestLensCandidateWorld;
            hasDismissedLensCandidate = true;
            dismissedLensCandidateLastSeenTime = Time.unscaledTime;
        }

        // The canonical selection above completes first. Hiding this presentation
        // then restores the normal city without changing its authoritative state.
        HideLensAndClearFocus();
        return true;
    }

    private void StartAssistedMonitoring()
    {
        if (assistedMonitoringCoroutine != null || !isActiveAndEnabled || !IsAssistedLensActive)
            return;

        assistedMonitoringCoroutine = StartCoroutine(AssistedMonitoringRoutine());
    }

    private void StopAssistedMonitoring()
    {
        if (assistedMonitoringCoroutine == null)
            return;

        StopCoroutine(assistedMonitoringCoroutine);
        assistedMonitoringCoroutine = null;
    }

    private IEnumerator AssistedMonitoringRoutine()
    {
        // Let StartCoroutine publish its handle before this routine can finish
        // or perform an expensive scan in the caller's frame.
        yield return null;

        while (IsAssistedLensActive)
        {
            if (detectionManager != null &&
                !detectionManager.IsSuspendedForVisualization &&
                !detectionManager.IsPreparingAutomaticBaseline &&
                detectionManager.IsCandidateScanReady &&
                !detectionManager.IsRefreshingSpatialScan)
            {
                UpdateLensFromCandidates(detectionManager.ScanForObjects());
            }
            else if (detectionManager == null ||
                detectionManager.IsPreparingAutomaticBaseline ||
                !detectionManager.IsCandidateScanReady)
            {
                HideLensAndClearFocus();
            }

            float interval = Mathf.Max(0.1f, spatialDifferenceUpdateInterval);
            if (detectionManager != null)
                interval = Mathf.Max(interval, detectionManager.RecommendedCandidateScanInterval);

            uint meshRevisionAfterPass = detectionManager != null
                ? detectionManager.SpatialMeshRevision
                : 0u;
            float waitStartedAt = Time.realtimeSinceStartup;
            float timeoutAt = waitStartedAt + interval;
            float earliestEventWakeAt = waitStartedAt + Mathf.Min(
                interval,
                Mathf.Max(0.02f, minimumEventDrivenUpdateInterval));
            while (IsAssistedLensActive && Time.realtimeSinceStartup < timeoutAt)
            {
                bool relevantMeshChanged =
                    detectionManager != null &&
                    detectionManager.SpatialMeshRevision != meshRevisionAfterPass &&
                    detectionManager.IsSpatialMeshUpdateSettled &&
                    Time.realtimeSinceStartup >= earliestEventWakeAt;
                if (relevantMeshChanged)
                    break;

                yield return null;
            }

            while (IsAssistedLensActive &&
                detectionManager != null &&
                detectionManager.UsesSpatialMeshRevisionScanGating &&
                !detectionManager.ShouldRefreshCandidateScan(
                    meshRevisionAfterPass,
                    waitStartedAt))
            {
                yield return null;
            }
        }

        assistedMonitoringCoroutine = null;
    }

    private PhysicalObjectCandidate ChooseLensCandidate(IReadOnlyList<PhysicalObjectCandidate> candidates)
    {
        PhysicalObjectCandidate best = null;
        float bestScore = float.MaxValue;
        PhysicalObjectCandidate fallback = null;
        float fallbackScore = float.MaxValue;
        bool dismissedCandidateStillPresent = false;

        if (candidates == null)
        {
            ReleaseDismissedCandidateAfterStableAbsence(false);
            return null;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            PhysicalObjectCandidate candidate = candidates[i];
            if (candidate == null || !candidate.isValid)
                continue;

            if (hasDismissedLensCandidate &&
                Vector3.Distance(candidate.worldPosition, dismissedLensCandidateWorld) <=
                Mathf.Max(selectedCandidateReleaseDistance, candidateVerificationAssociationRadius))
            {
                dismissedCandidateStillPresent = true;
                dismissedLensCandidateLastSeenTime = Time.unscaledTime;
                continue;
            }

            if (hasFrozenLensZone)
            {
                float associationRadius = Mathf.Max(
                    selectedCandidateReleaseDistance,
                    candidateVerificationAssociationRadius);
                float activeCandidateScore =
                    (candidate.worldPosition - latestLensCandidateWorld).sqrMagnitude;
                if (activeCandidateScore > associationRadius * associationRadius ||
                    activeCandidateScore >= bestScore)
                    continue;

                best = candidate;
                bestScore = activeCandidateScore;
                continue;
            }

            float fallbackCandidateScore = -candidate.approximateSize;
            if (fallbackCandidateScore < fallbackScore)
            {
                fallback = candidate;
                fallbackScore = fallbackCandidateScore;
            }

            float score = hasPendingLensCandidate
                ? (candidate.worldPosition - pendingLensCandidateWorld).sqrMagnitude
                : fallbackCandidateScore;
            if (hasPendingLensCandidate &&
                score > candidateVerificationAssociationRadius * candidateVerificationAssociationRadius)
            {
                continue;
            }

            if (score >= bestScore)
                continue;

            best = candidate;
            bestScore = score;
        }

        ReleaseDismissedCandidateAfterStableAbsence(dismissedCandidateStillPresent);

        // Assisted mode is designed around moving one tangible object. Do not
        // allow an unrelated/noisy candidate to open another lens until the
        // selected tangible has been continuously absent for the release time.
        if (hasDismissedLensCandidate)
            return null;

        return best ?? fallback;
    }

    private bool IsCandidateTemporallyVerified(PhysicalObjectCandidate candidate)
    {
        if (candidate == null)
            return false;

        int requiredPasses = Mathf.Max(1, candidateVerificationPasses);
        if (requiredPasses == 1)
            return true;

        float now = Time.unscaledTime;
        Vector3 candidatePosition = candidate.worldPosition;
        float associationRadius = Mathf.Max(0.005f, candidateVerificationAssociationRadius);
        bool continuesPendingCandidate = hasPendingLensCandidate &&
            now - pendingLensCandidateLastSeenTime <= Mathf.Max(0.1f, candidateVerificationMaximumGap) &&
            Vector3.Distance(candidatePosition, pendingLensCandidateWorld) <= associationRadius;

        if (!continuesPendingCandidate)
        {
            hasPendingLensCandidate = true;
            pendingLensCandidateWorld = candidatePosition;
            pendingLensCandidateLastSeenTime = now;
            pendingLensCandidatePasses = 1;
            return false;
        }

        pendingLensCandidatePasses++;
        pendingLensCandidateLastSeenTime = now;
        pendingLensCandidateWorld = Vector3.Lerp(
            pendingLensCandidateWorld,
            candidatePosition,
            1f / pendingLensCandidatePasses);
        return pendingLensCandidatePasses >= requiredPasses;
    }

    private void ResetCandidateVerification()
    {
        hasPendingLensCandidate = false;
        pendingLensCandidateWorld = Vector3.zero;
        pendingLensCandidateLastSeenTime = float.NegativeInfinity;
        pendingLensCandidatePasses = 0;
    }

    private void ReleaseDismissedCandidateAfterStableAbsence(bool isPresent)
    {
        if (!hasDismissedLensCandidate || isPresent)
            return;

        if (Time.unscaledTime - dismissedLensCandidateLastSeenTime <
            Mathf.Max(0f, selectedCandidateReleasePersistence))
        {
            return;
        }

        ClearDismissedLensCandidate();
    }

    private bool EnsureLensView()
    {
        if (lensView != null && lensView.IsInitialized)
            return true;

        ResolveMissingReferences();
        if (cityAnchorManager == null || cityAnchorManager.CityAnchorRoot == null ||
            cityManager == null || pointSelectManager == null)
        {
            return false;
        }

        GameObject lensObject = new GameObject("Assisted Magnification Lens");
        lensView = lensObject.AddComponent<MagnificationLensView>();

        bool initialized = lensView.Initialize(
            cityAnchorManager.CityAnchorRoot,
            cityManager,
            pointSelectManager,
            this,
            magnificationFactor,
            candidateZoneRadius,
            lensHeightOffset,
            lensTowardUserOffset,
            maximumContextRendererExtent,
            dwellSelectionTime,
            showBuildingLabels,
            showStreetLabels,
            buildingLabelHeightOffset,
            streetLabelHeightOffset,
            buildingLabelCharacterHeight,
            streetLabelCharacterHeight);

        if (!initialized)
        {
            Destroy(lensObject);
            lensView = null;
            return false;
        }

        detectionManager?.AddVisualizationIgnoredRoot(lensView.transform);
        lensView.Hide();
        return true;
    }

    private void HideLensAndClearFocus()
    {
        lensView?.Hide();
        hasFrozenLensZone = false;
        frozenLensFocusWorld = Vector3.zero;
        latestLensCandidateWorld = Vector3.zero;
        frozenLensBuildingIds.Clear();
        lastReliableCandidateTime = float.NegativeInfinity;
        ResetCandidateVerification();
    }

    private bool CaptureFrozenBuildingList(
        PhysicalObjectCandidate candidate,
        Transform canonicalRoot)
    {
        frozenLensBuildingIds.Clear();
        if (candidate == null || canonicalRoot == null ||
            cityManager?.buildings == null || cityManager.buildings.Count == 0)
        {
            Debug.LogWarning("Assisted Lens cannot freeze a building list until city building data is available.");
            return false;
        }

        Vector3 planeRight = canonicalRoot.right.normalized;
        Vector3 planeForward = canonicalRoot.forward.normalized;
        HashSet<string> includedIds = new HashSet<string>(StringComparer.Ordinal);
        int unmeasurableBuildingCount = 0;

        for (int i = 0; i < cityManager.buildings.Count; i++)
        {
            CityBuilding building = cityManager.buildings[i];
            if (building?.marker == null || string.IsNullOrWhiteSpace(building.id) ||
                !building.marker.TryGetWorldBounds(out _))
            {
                unmeasurableBuildingCount++;
                continue;
            }

            Vector3 buildingCenter = building.marker.GetRepresentativeWorldPosition();
            bool centerInsideCandidateZone = PlanarDistance(
                candidate.worldPosition,
                buildingCenter,
                planeRight,
                planeForward) <= candidateZoneRadius;

            // Zone membership is deliberately independent of the detector's
            // change bounds. Spatial-mesh rebuild noise can make those bounds
            // much wider than the physical object and previously admitted
            // buildings outside the visible/calibrated radius.
            if (centerInsideCandidateZone && includedIds.Add(building.id))
            {
                frozenLensBuildingIds.Add(building.id);
            }
        }

        frozenLensBuildingIds.Sort(StringComparer.Ordinal);
        Debug.Assert(
            unmeasurableBuildingCount == 0,
            "Assisted Lens invariant failed: one or more city buildings had no stable ID or renderer bounds.");
        Debug.Log(
            $"Assisted Lens froze {frozenLensBuildingIds.Count} building(s) for candidate " +
            $"'{candidate.id}' inside the {candidateZoneRadius:0.###}m center-radius zone; " +
            $"measurable buildings={cityManager.buildings.Count - unmeasurableBuildingCount}/" +
            $"{cityManager.buildings.Count}; " +
            $"IDs=[{string.Join(", ", frozenLensBuildingIds)}].");
        if (frozenLensBuildingIds.Count == 0)
        {
            Debug.LogWarning(
                "Assisted Lens ignored a verified physical candidate because no building center was inside its configured zone.");
        }

        return unmeasurableBuildingCount == 0 && frozenLensBuildingIds.Count > 0;
    }

    private static float PlanarDistance(
        Vector3 a,
        Vector3 b,
        Vector3 planeRight,
        Vector3 planeForward)
    {
        Vector3 delta = a - b;
        float x = Vector3.Dot(delta, planeRight);
        float z = Vector3.Dot(delta, planeForward);
        return Mathf.Sqrt(x * x + z * z);
    }

    private void ClearDismissedLensCandidate()
    {
        hasDismissedLensCandidate = false;
        dismissedLensCandidateWorld = Vector3.zero;
        dismissedLensCandidateLastSeenTime = float.NegativeInfinity;
    }

    private void HandleModeChanged(SpatialPlacementMode mode)
    {
        RefreshCityBuildingHandInteraction();
        ClearDismissedLensCandidate();
        if (mode == SpatialPlacementMode.BuildingPlacing)
        {
            if (beginTrialOnBuildingModeEnter)
                BeginTrial();

            if (currentTechnique == BuildingSelectionTechnique.AssistedLens)
                StartAssistedMonitoring();
        }
        else
        {
            StopAssistedMonitoring();
            HideLensAndClearFocus();
        }

        RefreshTechniqueUi();
    }

    private void RefreshCityBuildingHandInteraction()
    {
        pointSelectManager?.SetCityBuildingHandInteractionEnabled(!IsDirectBuildingPlacementActive);
    }

    private void HandleBaselineCaptureCompleted(bool succeeded)
    {
        if (!IsAssistedLensActive || detectionManager == null)
            return;

        ClearDismissedLensCandidate();

        if (!succeeded && !detectionManager.CanScanWithoutSpatialBaseline)
        {
            HideLensAndClearFocus();
            notifier?.Show("Assisted Lens could not map the tabletop. Keep the scan area clear and press the lens button to retry.");
            return;
        }

        notifier?.Show("Assisted Lens ready: place or move one physical object to aim the lens.");
        StartAssistedMonitoring();
    }

    private void HandleBuildingSelectionChanged(BuildingSelectionChangedEvent selection)
    {
        if (detectionManager == null ||
            detectionManager.CurrentMode != SpatialPlacementMode.BuildingPlacing)
        {
            return;
        }

        bool usesSpatialBuildingTechnique =
            selection.Source == BuildingSelectionSource.DirectPhysical ||
            selection.Source == BuildingSelectionSource.Lens;
        if (usesSpatialBuildingTechnique)
        {
            detectionManager.SetTwoBuildingSelectionCandidatesAvailable(
                selection.SelectionCount >= 2);
        }

        if (currentTrial == null || currentTrial.technique != currentTechnique)
            BeginTrial();

        double now = Time.realtimeSinceStartupAsDouble;
        if (selection.SelectionCount <= 1)
        {
            if (currentTrial.firstBuildingSelectionTime < 0d)
                currentTrial.firstBuildingSelectionTime = now;

            currentTrial.firstBuildingId = selection.Building?.id ?? string.Empty;
            currentTrial.firstSelectionSource = selection.Source;
            currentTrial.secondBuildingId = string.Empty;
            currentTrial.secondBuildingSelectionTime = -1d;
        }
        else
        {
            if (currentTrial.secondBuildingSelectionTime < 0d)
                currentTrial.secondBuildingSelectionTime = now;

            currentTrial.secondBuildingId = selection.Building?.id ?? string.Empty;
            currentTrial.secondSelectionSource = selection.Source;
        }

        if (selection.IsCorrection)
            currentTrial.selectionCorrections++;

        lensView?.RefreshSelection(pointSelectManager?.CaptureVisualizationSnapshot());
        TrialUpdated?.Invoke(currentTrial);
    }

    private void HandleSelectionCleared()
    {
        detectionManager?.SetTwoBuildingSelectionCandidatesAvailable(false);
        lensView?.RefreshSelection(pointSelectManager?.CaptureVisualizationSnapshot());
    }

    private void HandlePathConfirmed(CityBuilding start, CityBuilding destination)
    {
        if (detectionManager == null ||
            detectionManager.CurrentMode != SpatialPlacementMode.BuildingPlacing)
        {
            return;
        }

        if (currentTrial == null)
            BeginTrial();

        currentTrial.confirmationTime = Time.realtimeSinceStartupAsDouble;
        currentTrial.firstBuildingId = start?.id ?? currentTrial.firstBuildingId;
        currentTrial.secondBuildingId = destination?.id ?? currentTrial.secondBuildingId;
        TrialUpdated?.Invoke(currentTrial);
        TrialCompleted?.Invoke(currentTrial);

        if (logCompletedTrialAsJson)
            Debug.Log("BuildingSelectionTrial: " + JsonUtility.ToJson(currentTrial));
    }

    private void HandleVisualizationEntering()
    {
        visualizationWasActive = true;
        StopAssistedMonitoring();
        HideLensAndClearFocus();
    }

    private void HandleVisualizationExited()
    {
        visualizationWasActive = false;
        if (IsAssistedLensActive)
            StartAssistedMonitoring();
    }

    private void Subscribe()
    {
        if (subscribed)
            return;

        ResolveMissingReferences();
        if (detectionManager != null)
        {
            detectionManager.ModeChanged += HandleModeChanged;
            detectionManager.BaselineCaptureCompleted += HandleBaselineCaptureCompleted;
        }
        if (pointSelectManager != null)
        {
            pointSelectManager.BuildingSelectionChanged += HandleBuildingSelectionChanged;
            pointSelectManager.SelectionCleared += HandleSelectionCleared;
            pointSelectManager.PathConfirmed += HandlePathConfirmed;
        }
        if (visualizationModeController != null)
        {
            visualizationModeController.VisualizationModeEntering += HandleVisualizationEntering;
            visualizationModeController.VisualizationModeExited += HandleVisualizationExited;
        }

        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (detectionManager != null)
        {
            detectionManager.ModeChanged -= HandleModeChanged;
            detectionManager.BaselineCaptureCompleted -= HandleBaselineCaptureCompleted;
        }
        if (pointSelectManager != null)
        {
            pointSelectManager.BuildingSelectionChanged -= HandleBuildingSelectionChanged;
            pointSelectManager.SelectionCleared -= HandleSelectionCleared;
            pointSelectManager.PathConfirmed -= HandlePathConfirmed;
        }
        if (visualizationModeController != null)
        {
            visualizationModeController.VisualizationModeEntering -= HandleVisualizationEntering;
            visualizationModeController.VisualizationModeExited -= HandleVisualizationExited;
        }

        subscribed = false;
    }

    private void ResolveMissingReferences()
    {
        detectionManager ??= FindFirstObjectByType<SpatialObjectDetectionManager>(FindObjectsInactive.Include);
        buildingInterpreter ??= FindFirstObjectByType<SpatialBuildingObjectInterpreter>(FindObjectsInactive.Include);
        pointSelectManager ??= FindFirstObjectByType<PointSelectManager>(FindObjectsInactive.Include);
        cityAnchorManager ??= FindFirstObjectByType<CityAnchorManager>(FindObjectsInactive.Include);
        cityManager ??= FindFirstObjectByType<CityManager>(FindObjectsInactive.Include);
        visualizationModeController ??= VisualizationModeController.Instance ??
            FindFirstObjectByType<VisualizationModeController>(FindObjectsInactive.Include);
        notifier ??= FindFirstObjectByType<MRNotification>(FindObjectsInactive.Include);
    }

    private void EnsureTechniqueSelector()
    {
        if (selectorRoot != null)
            return;

        GameObject mixedRealitySceneContent = GameObject.Find("MixedRealitySceneContent");
        Transform buildingPage = mixedRealitySceneContent != null
            ? mixedRealitySceneContent.transform.Find(LiveBuildingPagePath)
            : null;

        if (buildingPage == null)
        {
            Debug.LogWarning("BuildingSelectionTechniqueController: live Building Placing page was not found; technique API remains available without the selector UI.");
            return;
        }

        Transform background = buildingPage.Find("BG");
        Transform template = buildingPage.Find("BG/Content/ScanObjectsButton");
        if (background == null || template == null)
        {
            Debug.LogWarning("BuildingSelectionTechniqueController: live Building Placing button template was not found.");
            return;
        }

        Transform existingSelector = background.Find("SelectionTechniqueSelector");
        if (existingSelector != null)
        {
            selectorRoot = existingSelector as RectTransform;
            if (selectorRoot == null)
            {
                Debug.LogWarning("BuildingSelectionTechniqueController: SelectionTechniqueSelector must use a RectTransform.");
                return;
            }
        }
        else
        {
            GameObject selectorObject = new GameObject("SelectionTechniqueSelector", typeof(RectTransform));
            selectorObject.layer = buildingPage.gameObject.layer;
            selectorRoot = selectorObject.GetComponent<RectTransform>();
            selectorRoot.SetParent(background, false);
            selectorRoot.anchorMin = new Vector2(0.5f, 1f);
            selectorRoot.anchorMax = new Vector2(0.5f, 1f);
            selectorRoot.pivot = new Vector2(0.5f, 1f);
            selectorRoot.anchoredPosition = techniqueSelectorAnchoredPosition;
            selectorRoot.sizeDelta = new Vector2(900f, 220f);

            CreateSelectorHeader(template);
            directTechniqueButton = CreateTechniqueButton(
                template,
                "DirectTechniqueButton",
                -techniqueButtonSpacing * 0.5f,
                "DIRECT",
                SetDirectTechnique,
                out directTechniqueLabel);
            assistedTechniqueButton = CreateTechniqueButton(
                template,
                "AssistedLensTechniqueButton",
                techniqueButtonSpacing * 0.5f,
                "ASSISTED LENS",
                SetAssistedLensTechnique,
                out assistedTechniqueLabel);
        }

        BindTechniqueSelector(buildingPage);
    }

    [ContextMenu("Build/Bind Technique Selector In Scene")]
    public void BuildOrBindTechniqueSelectorInScene()
    {
        ResolveMissingReferences();
        selectorRoot = null;
        EnsureTechniqueSelector();
        RefreshTechniqueUi();
    }

    private void BindTechniqueSelector(Transform buildingPage)
    {
        selectorUiBindings.Clear();
        TechniqueSelectorUiBinding primary = RegisterTechniqueSelector(buildingPage, selectorRoot);
        if (primary != null)
        {
            directTechniqueButton = primary.directButton;
            assistedTechniqueButton = primary.assistedButton;
            directTechniqueLabel = primary.directLabel;
            assistedTechniqueLabel = primary.assistedLabel;
            scanObjectsBridge = primary.scanBridge;
            scanObjectsLabel = primary.scanLabel;
            originalScanObjectsLabel = primary.originalScanLabel;
        }

        BindAdditionalActiveTechniqueSelectors(buildingPage);
    }

    private TechniqueSelectorUiBinding RegisterTechniqueSelector(
        Transform buildingPage,
        RectTransform root)
    {
        if (buildingPage == null || root == null)
            return null;

        for (int i = 0; i < selectorUiBindings.Count; i++)
        {
            if (selectorUiBindings[i].root == root)
                return selectorUiBindings[i];
        }

        Transform directButtonTransform = root.Find("DirectTechniqueButton");
        Transform assistedButtonTransform = root.Find("AssistedLensTechniqueButton");
        Transform scanButton = buildingPage.Find("BG/Content/ScanObjectsButton");
        var binding = new TechniqueSelectorUiBinding
        {
            root = root,
            directButton = directButtonTransform != null ? directButtonTransform.gameObject : null,
            assistedButton = assistedButtonTransform != null ? assistedButtonTransform.gameObject : null,
            directLabel = FindDirectChildText(root, "DirectTechniqueButtonLabel"),
            assistedLabel = FindDirectChildText(root, "AssistedLensTechniqueButtonLabel"),
            scanBridge = scanButton != null
                ? scanButton.GetComponentInChildren<SciFiButtonSpriteBridge>(true)
                : null,
            scanLabel = scanButton != null ? FindButtonTitle(scanButton) : null
        };
        binding.originalScanLabel = binding.scanLabel != null &&
            binding.scanLabel.text != "Lens Auto-Tracking"
                ? binding.scanLabel.text
                : "Scan Objects";

        BindTechniqueButton(binding.directButton, SetDirectTechnique);
        BindTechniqueButton(binding.assistedButton, SetAssistedLensTechnique);
        selectorUiBindings.Add(binding);
        return binding;
    }

    private void BindAdditionalActiveTechniqueSelectors(Transform primaryBuildingPage)
    {
        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform slate = transforms[i];
            if (slate == null || slate.name != "MainControlSlate" || !slate.gameObject.activeInHierarchy)
                continue;

            Transform buildingPage = slate.Find(BuildingPageUnderSlatePath);
            if (buildingPage == null || buildingPage == primaryBuildingPage)
                continue;

            RectTransform root = buildingPage.Find("BG/SelectionTechniqueSelector") as RectTransform;
            if (root != null)
                RegisterTechniqueSelector(buildingPage, root);
        }
    }

    private static Text FindDirectChildText(Transform root, string childName)
    {
        Transform child = root != null ? root.Find(childName) : null;
        return child != null ? child.GetComponent<Text>() : null;
    }

    private static void BindTechniqueButton(GameObject button, UnityAction action)
    {
        if (button == null)
            return;

        SciFiButtonSpriteBridge bridge = button.GetComponentInChildren<SciFiButtonSpriteBridge>(true);
        if (bridge != null)
        {
            bridge.onClickAction ??= new UnityEvent();
            bridge.onClickAction.RemoveListener(action);
            bridge.onClickAction.AddListener(action);
            bridge.SetInteractable(true);
            return;
        }

        Button unityButton = button.GetComponent<Button>();
        if (unityButton != null)
        {
            unityButton.onClick ??= new Button.ButtonClickedEvent();
            unityButton.onClick.RemoveListener(action);
            unityButton.onClick.AddListener(action);
        }
    }

    private void CreateSelectorHeader(Transform template)
    {
        Text source = FindButtonTitle(template);
        GameObject headerObject = new GameObject("SelectionTechniqueTitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        headerObject.layer = selectorRoot.gameObject.layer;
        RectTransform rect = headerObject.GetComponent<RectTransform>();
        rect.SetParent(selectorRoot, false);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, 12f);
        rect.sizeDelta = new Vector2(700f, 70f);

        Text title = headerObject.GetComponent<Text>();
        if (source != null)
        {
            title.font = source.font;
            title.material = source.material;
            title.color = source.color;
            title.fontStyle = FontStyle.Bold;
        }

        title.text = "SELECTION TECHNIQUE";
        title.fontSize = 34;
        title.alignment = TextAnchor.MiddleCenter;
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        title.verticalOverflow = VerticalWrapMode.Overflow;
    }

    private GameObject CreateTechniqueButton(
        Transform template,
        string objectName,
        Vector2 anchoredPosition,
        string label,
        UnityAction action,
        out Text title)
    {
        GameObject button = Instantiate(template.gameObject, selectorRoot, false);
        button.name = objectName;

        RectTransform rect = button.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition + new Vector2(0f, -58f);
        rect.localScale = new Vector3(0.55f, 0.22f, 1f);

        SetChildActive(button.transform, "Btn_Map1 (1)/Your map", false);
        SetChildActive(button.transform, "Btn_Map1 (1)/LevelStars", false);
        SetChildActive(button.transform, "Btn_Map1 (1)/LockMask", false);

        Text sourceTitle = FindButtonTitle(button.transform);
        if (sourceTitle != null)
            sourceTitle.transform.parent.gameObject.SetActive(false);
        title = CreateTechniqueButtonLabel(sourceTitle, objectName + "Label", anchoredPosition, label);

        SciFiButtonSpriteBridge bridge = button.GetComponentInChildren<SciFiButtonSpriteBridge>(true);
        if (bridge != null)
            bridge.onClickAction = new UnityEvent();
        else
        {
            Button unityButton = button.GetComponent<Button>();
            if (unityButton != null)
                unityButton.onClick = new Button.ButtonClickedEvent();
        }

        BindTechniqueButton(button, action);

        return button;
    }

    private Text CreateTechniqueButtonLabel(
        Text source,
        string objectName,
        Vector2 buttonPosition,
        string label)
    {
        GameObject labelObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.layer = selectorRoot.gameObject.layer;
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(selectorRoot, false);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = buttonPosition + new Vector2(0f, -140f);
        rect.sizeDelta = new Vector2(360f, 100f);

        Text text = labelObject.GetComponent<Text>();
        if (source != null)
        {
            text.font = source.font;
            text.material = source.material;
            text.color = source.color;
        }

        text.text = label;
        text.fontSize = 34;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 18;
        text.resizeTextMaxSize = 34;
        text.raycastTarget = false;
        return text;
    }

    private void RefreshTechniqueUi()
    {
        if (createTechniqueSelector && selectorRoot == null)
            EnsureTechniqueSelector();

        bool assisted = currentTechnique == BuildingSelectionTechnique.AssistedLens;
        if (selectorUiBindings.Count == 0)
        {
            ApplyTechniqueButtonState(directTechniqueButton, directTechniqueLabel, "DIRECT", !assisted);
            ApplyTechniqueButtonState(assistedTechniqueButton, assistedTechniqueLabel, "ASSISTED LENS", assisted);

            if (scanObjectsLabel != null)
                scanObjectsLabel.text = assisted ? "Lens Auto-Tracking" : originalScanObjectsLabel;
            scanObjectsBridge?.SetInteractable(!assisted);
            return;
        }

        for (int i = 0; i < selectorUiBindings.Count; i++)
        {
            TechniqueSelectorUiBinding binding = selectorUiBindings[i];
            ApplyTechniqueButtonState(binding.directButton, binding.directLabel, "DIRECT", !assisted);
            ApplyTechniqueButtonState(binding.assistedButton, binding.assistedLabel, "ASSISTED LENS", assisted);
            if (binding.scanLabel != null)
            {
                binding.scanLabel.text = assisted
                    ? "Lens Auto-Tracking"
                    : binding.originalScanLabel;
            }

            binding.scanBridge?.SetInteractable(!assisted);
        }
    }

    private static void ApplyTechniqueButtonState(GameObject button, Text label, string text, bool selected)
    {
        if (button == null)
            return;

        SciFiButtonSpriteBridge bridge = button.GetComponentInChildren<SciFiButtonSpriteBridge>(true);
        Image image = bridge != null ? bridge.targetImage : button.GetComponentInChildren<Image>(true);
        if (image != null)
            image.color = selected
                ? new Color(0.15f, 0.95f, 1f, 1f)
                : new Color(0.42f, 0.58f, 0.8f, 0.82f);

        if (label != null)
            label.text = selected ? text + "  [SELECTED]" : text;
    }

    private static Text FindButtonTitle(Transform button)
    {
        if (button == null)
            return null;

        Transform titleTransform = button.Find("Btn_Map1 (1)/TitleName/Text");
        if (titleTransform == null)
            titleTransform = button.Find("TitleName/Text");
        if (titleTransform != null)
            return titleTransform.GetComponent<Text>();

        Text[] texts = button.GetComponentsInChildren<Text>(true);
        return texts != null && texts.Length > 0 ? texts[0] : null;
    }

    private static void SetChildActive(Transform root, string childName, bool active)
    {
        Transform child = root != null ? root.Find(childName) : null;
        if (child != null)
            child.gameObject.SetActive(active);
    }
}
