using System;
using System.Collections.Generic;
using Microsoft.MixedReality.Toolkit.Input;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PointSelectManager : MonoBehaviour
{
    [Header("City Structure")]
    public GameObject housesParent;
    public Transform cityRoot;
    public LineRenderer path;

    [Header("Buttons")]
    public GameObject confirmButton;
    public GameObject resetButton;

    [Header("Hover Tag")]
    public GameObject hoverTag;
    public Text hoverText;
    [SerializeField] private TMP_Text hoverTextTMP;
    public float tagOffset = 0.05f;
    [SerializeField, Min(0.0001f)] private float hoverTagWorldScale = 0.001f;

    [Header("Selection Arrows")]
    public GameObject arrow1;
    public GameObject arrow2;
    [Tooltip("Base clearance from the selected building to the bottom of an arrow at 1x city scale.")]
    public float arrowOffset = 0.02f;
    [SerializeField, Min(0f)] private float minimumArrowClearance = 0.02f;
    [SerializeField, Min(0.01f)] private float minimumArrowVisualScale = 0.75f;
    [SerializeField, Min(0.01f)] private float maximumArrowVisualScale = 1.5f;

    [Header("Selection Controls")]
    [SerializeField, Min(0f)] private float selectionControlsClearance = 0.1f;
    [SerializeField, Min(0f)] private float selectionControlSpacing = 0.02f;
    [SerializeField, Min(0.01f)] private float minimumControlVisualScale = 0.9f;
    [SerializeField, Min(0.01f)] private float maximumControlVisualScale = 1.25f;

    [Header("Near-Hand Selection")]
    [SerializeField, Range(2f, 3f)] private float nearDwellSelectionDuration = 2.5f;

    [Header("Placement Debug")]
    [SerializeField] private bool logPlacementDebug;

    [Header("Route Board")]
    public Text routeBoardText;
    public TMP_Text routeBoardTMP;

    [Header("Route Labels")]
    public RouteWorldLabelPresenter routeWorldLabelPresenter;

    [Header("References")]
    public SimpleGraphManager graph;
    public MRNotification notifier;
    public CityManager cityManager;

    [Header("Route Attachment")]
    [Tooltip("Rebuild graph edges from NodeNeighbors immediately before route calculation so moved/scaled city nodes use current world positions.")]
    public bool rebuildGraphBeforeRoute = true;
    [Tooltip("Maximum distance from a building anchor to the closest graph edge. Keep high for prototype scenes; reduce later for stricter validation.")]
    public float maxBuildingGraphSnapDistance = 999f;
    public bool logRouteDebug = true;

    public event Action<CityVisualizationSnapshot> VisualizationStateChanged;
    public event Action<BuildingSelectionChangedEvent> BuildingSelectionChanged;
    public event Action SelectionCleared;
    public event Action<CityBuilding, CityBuilding> PathConfirmed;
    public bool HasCurrentPath => hasPath;
    public bool CityBuildingHandInteractionEnabled => cityBuildingHandInteractionEnabled;
    public float NearDwellSelectionDuration => nearDwellSelectionDuration;
    public CityBuilding SelectedStartBuilding => startPoint != null ? startPoint.buildingData : null;
    public CityBuilding SelectedDestinationBuilding => destinationPoint != null ? destinationPoint.buildingData : null;

    private readonly Dictionary<BuildingPoint, BuildingMarker> pointToMarker =
        new Dictionary<BuildingPoint, BuildingMarker>();

    private readonly List<GraphNode> currentPathNodes = new List<GraphNode>();

    private BuildingPoint startPoint;
    private BuildingPoint destinationPoint;
    private BuildingPoint hoveredPoint;

    private Route currentRoute = new Route();

    private bool hasPath;
    private bool hasStoredSnaps;
    private bool cityBuildingHandInteractionEnabled = true;
    private bool tabletopPresentationSuppressed;
    private bool cachedConfirmActive;
    private bool cachedResetActive;
    private bool cachedHoverActive;
    private bool cachedArrow1Active;
    private bool cachedArrow2Active;
    private bool cachedPathEnabled;
    private int visualizationStateRevision;
    private int suppressionStartRevision;

    private Vector3 startSnapLocal;
    private Vector3 goalSnapLocal;

    private float initialCityScale = 1f;
    private float dynamicScale = 1f;

    private Vector3 arrow1InitialLocalScale;
    private Vector3 arrow2InitialLocalScale;
    private Vector3 confirmButtonInitialLocalScale;
    private Vector3 resetButtonInitialLocalScale;
    private float arrow1InitialParentScale = 1f;
    private float arrow2InitialParentScale = 1f;
    private float confirmButtonInitialParentScale = 1f;
    private float resetButtonInitialParentScale = 1f;
    private bool visualScalesCached;

    private void Awake()
    {
        ResolveMissingReferences();
    }

    private void Start()
    {
        ResolveMissingReferences();
        CacheCityScale();
        CacheVisualScales();
        ConfigureHoverTag();
        InitializeVisualState();
        SetupBuildingInteractionTargets();
    }

    private void LateUpdate()
    {
        UpdateDynamicScale();

        if (tabletopPresentationSuppressed)
            return;

        UpdateHoverTagPosition();
        UpdateButtonPositions();
        UpdateArrowPositions();

        if (hasPath)
            RedrawCurrentPath();
    }

    #region Initialization

    public void RefreshAfterCityRebuild()
    {
        ResolveMissingReferences();
        CacheCityScale();
        CacheVisualScales();
        SetupBuildingInteractionTargets();
        ResetSelection();
    }

    /// <summary>
    /// Called by city-scale controllers immediately after they alter City Root.
    /// This keeps selection visuals current without adding a per-frame scale poll.
    /// </summary>
    public void RefreshForCityScaleChange()
    {
        UpdateDynamicScale();
        CacheVisualScales();

        if (tabletopPresentationSuppressed)
            return;

        UpdateHoverTagPosition();
        UpdateButtonPositions();
        UpdateArrowPositions();
        LogPlacementState();
    }

    public void SetTabletopPresentationSuppressed(bool suppressed)
    {
        if (tabletopPresentationSuppressed == suppressed)
            return;

        tabletopPresentationSuppressed = suppressed;
        if (suppressed)
        {
            suppressionStartRevision = visualizationStateRevision;
            cachedConfirmActive = confirmButton != null && confirmButton.activeSelf;
            cachedResetActive = resetButton != null && resetButton.activeSelf;
            // Hover/focus is transient and is deliberately cleared when the
            // tangible presentation is suspended.
            cachedHoverActive = false;
            cachedArrow1Active = arrow1 != null && arrow1.activeSelf;
            cachedArrow2Active = arrow2 != null && arrow2.activeSelf;
            cachedPathEnabled = path != null && path.enabled;

            if (confirmButton != null) confirmButton.SetActive(false);
            if (resetButton != null) resetButton.SetActive(false);
            if (hoverTag != null) hoverTag.SetActive(false);
            if (arrow1 != null) arrow1.SetActive(false);
            if (arrow2 != null) arrow2.SetActive(false);
            if (path != null) path.enabled = false;
            return;
        }

        if (path != null)
            path.enabled = cachedPathEnabled;

        if (visualizationStateRevision == suppressionStartRevision)
        {
            if (confirmButton != null) confirmButton.SetActive(cachedConfirmActive);
            if (resetButton != null) resetButton.SetActive(cachedResetActive);
            if (hoverTag != null) hoverTag.SetActive(cachedHoverActive);
            if (arrow1 != null) arrow1.SetActive(cachedArrow1Active);
            if (arrow2 != null) arrow2.SetActive(cachedArrow2Active);
        }
        else
        {
            if (hoverTag != null) hoverTag.SetActive(false);
            RefreshSelectionVisuals();
        }

        if (hasPath)
            RedrawCurrentPath();
    }

    private void ResolveMissingReferences()
    {
        CityBootstrapper bootstrapper =
            FindFirstObjectByType<CityBootstrapper>(FindObjectsInactive.Include);

        if (housesParent == null && bootstrapper != null && bootstrapper.buildingsRoot != null)
            housesParent = bootstrapper.buildingsRoot.gameObject;

        if (cityRoot == null)
        {
            if (bootstrapper != null && bootstrapper.buildingsRoot != null)
                cityRoot = bootstrapper.buildingsRoot.root;
            else if (housesParent != null)
                cityRoot = housesParent.transform.root;
        }

        if (cityManager == null && bootstrapper != null)
            cityManager = bootstrapper.cityManager;

        cityManager ??= FindFirstObjectByType<CityManager>(FindObjectsInactive.Include);
        graph ??= FindFirstObjectByType<SimpleGraphManager>(FindObjectsInactive.Include);
        notifier ??= FindFirstObjectByType<MRNotification>(FindObjectsInactive.Include);
    }

    private void CacheCityScale()
    {
        if (cityRoot != null && !Mathf.Approximately(cityRoot.lossyScale.x, 0f))
            initialCityScale = cityRoot.lossyScale.x;
        else
            initialCityScale = 1f;

        UpdateDynamicScale();
    }

    private void InitializeVisualState()
    {
        dynamicScale = 1f;

        if (path != null)
        {
            path.useWorldSpace = true;
            path.positionCount = 0;
        }

        if (hoverTag != null) hoverTag.SetActive(false);
        if (confirmButton != null) confirmButton.SetActive(false);
        if (resetButton != null) resetButton.SetActive(false);
        if (arrow1 != null) arrow1.SetActive(false);
        if (arrow2 != null) arrow2.SetActive(false);

        ClearRouteBoard();
        routeWorldLabelPresenter?.ClearLabels();
    }

    private void ConfigureHoverTag()
    {
        if (hoverTag == null)
            return;

        hoverText ??= hoverTag.GetComponentInChildren<Text>(true);
        hoverTextTMP ??= hoverTag.GetComponentInChildren<TMP_Text>(true);

        Canvas canvas = hoverTag.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
            return;

        // The legacy scene tag was authored as a zero-scale screen-space canvas.
        // A tag positioned above a world-space building must itself render in world space.
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;

        RectTransform canvasTransform = canvas.transform as RectTransform;
        if (canvasTransform != null && canvasTransform.localScale.sqrMagnitude < 0.000001f)
            canvasTransform.localScale = Vector3.one * hoverTagWorldScale;
    }

    private void UpdateDynamicScale()
    {
        if (cityRoot == null || Mathf.Approximately(initialCityScale, 0f))
        {
            dynamicScale = 1f;
            return;
        }

        float currentScale = cityRoot.lossyScale.x;
        float relativeScale = currentScale / initialCityScale;
        dynamicScale = float.IsNaN(relativeScale) || float.IsInfinity(relativeScale)
            ? 1f
            : Mathf.Max(0.0001f, Mathf.Abs(relativeScale));
    }

    private void CacheVisualScales()
    {
        if (visualScalesCached)
            return;

        CacheVisualScale(arrow1, out arrow1InitialLocalScale, out arrow1InitialParentScale);
        CacheVisualScale(arrow2, out arrow2InitialLocalScale, out arrow2InitialParentScale);
        CacheVisualScale(confirmButton, out confirmButtonInitialLocalScale, out confirmButtonInitialParentScale);
        CacheVisualScale(resetButton, out resetButtonInitialLocalScale, out resetButtonInitialParentScale);
        visualScalesCached = true;
    }

    private static void CacheVisualScale(GameObject visual, out Vector3 localScale, out float parentScale)
    {
        localScale = visual != null ? visual.transform.localScale : Vector3.one;
        parentScale = visual != null ? GetUniformScale(visual.transform.parent) : 1f;
    }

    #endregion

    #region Building Interaction Setup

    public void SetCityBuildingHandInteractionEnabled(bool enabled)
    {
        cityBuildingHandInteractionEnabled = enabled;
        ApplyCityBuildingHandInteractionState();
    }

    private void SetupBuildingInteractionTargets()
    {
        pointToMarker.Clear();

        if (housesParent == null)
        {
            Debug.LogWarning("PointSelectManager: housesParent is not assigned.");
            return;
        }

        BuildingMarker[] markers = housesParent.GetComponentsInChildren<BuildingMarker>(true);

        if (markers == null || markers.Length == 0)
        {
            Debug.LogWarning("PointSelectManager: No BuildingMarker found under housesParent.");
            return;
        }

        foreach (BuildingMarker marker in markers)
        {
            if (marker == null) continue;

            Transform interactionTarget = ResolveInteractionTarget(marker);
            if (interactionTarget == null)
            {
                Debug.LogWarning($"PointSelectManager: Could not resolve interaction target for {marker.name}");
                continue;
            }

            CleanupRedundantInteractionComponents(marker.transform, interactionTarget);

            BuildingPoint point = EnsureInteractionComponents(interactionTarget, marker);
            if (point != null)
                pointToMarker[point] = marker;
        }

        ApplyCityBuildingHandInteractionState();
    }

    private void ApplyCityBuildingHandInteractionState()
    {
        if (housesParent == null)
            return;

        BuildingPoint[] points = housesParent.GetComponentsInChildren<BuildingPoint>(true);
        for (int i = 0; i < points.Length; i++)
            points[i]?.SetHandInteractionEnabled(cityBuildingHandInteractionEnabled);

        NearInteractionTouchableVolume[] touchables =
            housesParent.GetComponentsInChildren<NearInteractionTouchableVolume>(true);
        for (int i = 0; i < touchables.Length; i++)
        {
            if (touchables[i] != null)
                touchables[i].enabled = cityBuildingHandInteractionEnabled;
        }
    }

    private Transform ResolveInteractionTarget(BuildingMarker marker)
    {
        if (marker == null) return null;

        Transform root = marker.VisualRoot;
        if (root == null) return null;

        Renderer ownRenderer = root.GetComponent<Renderer>();
        if (ownRenderer != null)
            return root;

        Renderer childRenderer = root.GetComponentInChildren<Renderer>(true);
        if (childRenderer != null)
            return childRenderer.transform;

        return null;
    }

    private void CleanupRedundantInteractionComponents(Transform buildingRoot, Transform keepTarget)
    {
        if (buildingRoot == null || keepTarget == null) return;

        foreach (Transform t in buildingRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t == keepTarget) continue;

            BuildingPoint point = t.GetComponent<BuildingPoint>();
            if (point != null) Destroy(point);

            NearInteractionTouchableVolume touchable = t.GetComponent<NearInteractionTouchableVolume>();
            if (touchable != null) Destroy(touchable);

            if (t.GetComponent<Renderer>() == null)
            {
                BoxCollider col = t.GetComponent<BoxCollider>();
                if (col != null) Destroy(col);
            }
        }
    }

    private BuildingPoint EnsureInteractionComponents(Transform target, BuildingMarker marker)
    {
        if (target == null) return null;

        BuildingPoint point = target.GetComponent<BuildingPoint>();
        if (point == null)
            point = target.gameObject.AddComponent<BuildingPoint>();

        CityBuilding buildingData = FindCityBuilding(marker);
        point.Configure(this, buildingData, target);

        if (target.GetComponent<Collider>() == null)
            target.gameObject.AddComponent<BoxCollider>();

        if (target.GetComponent<NearInteractionTouchableVolume>() == null)
            target.gameObject.AddComponent<NearInteractionTouchableVolume>();

        return point;
    }

    private CityBuilding FindCityBuilding(BuildingMarker marker)
    {
        if (cityManager == null)
            ResolveMissingReferences();

        if (marker == null || cityManager == null)
            return null;

        return cityManager.GetBuildingById(marker.BuildingIdOrFallback);
    }

    #endregion

    #region Hover UI

    public void ShowTag(BuildingPoint point)
    {
        hoveredPoint = point;

        if (hoveredPoint == null)
            return;

        if (hoverTag != null)
        {
            hoverTag.SetActive(true);
            hoverTag.transform.position =
                GetBuildingTop(hoveredPoint) + GetCityUp() * GetScaledClearance(tagOffset, 0f);
        }

        SetHoverText(BuildHoverText(hoveredPoint));
    }

    public void HideTag(BuildingPoint point)
    {
        if (hoveredPoint != point)
            return;

        hoveredPoint = null;

        if (hoverTag != null)
            hoverTag.SetActive(false);
    }

    private string BuildHoverText(BuildingPoint point)
    {
        if (point == null)
            return string.Empty;

        string label = point.GetDisplayName();

        if (point == startPoint)
            return $"{label}\nStart selected";

        if (point == destinationPoint)
            return $"{label}\nDestination selected";

        if (point.HasActiveNearFocus)
            return $"{label}\nHold steady: {point.NearDwellRemainingSeconds:0.0}s";

        return $"{label}\nTap to select";
    }

    private void UpdateHoverTagPosition()
    {
        if (hoverTag == null || hoveredPoint == null || !hoverTag.activeSelf)
            return;

        hoverTag.transform.position =
            GetBuildingTop(hoveredPoint) + GetCityUp() * GetScaledClearance(tagOffset, 0f);

        if (Camera.main != null)
        {
            hoverTag.transform.rotation = Quaternion.LookRotation(
                Camera.main.transform.position - hoverTag.transform.position
            );
        }

        SetHoverText(BuildHoverText(hoveredPoint));
    }

    private void SetHoverText(string value)
    {
        if (hoverText != null)
            hoverText.text = value;

        if (hoverTextTMP != null)
            hoverTextTMP.text = value;
    }

    #endregion

    #region Selection

    public void SelectPoint(BuildingPoint point)
    {
        TrySelectPoint(point, BuildingSelectionSource.NormalCity);
    }

    public void SelectPoint(BuildingPoint point, BuildingSelectionSource source)
    {
        TrySelectPoint(point, source);
    }

    private bool TrySelectPoint(BuildingPoint point, BuildingSelectionSource source)
    {
        if (point == null)
            return false;

        if (point.IsFlooded())
        {
            notifier?.Show("This building is flooded / unavailable.");
            return false;
        }

        bool isCorrection = startPoint != null &&
            (destinationPoint != null || point == startPoint);

        if (hasPath)
            ClearRouteOnly();

        if (startPoint == null)
        {
            startPoint = point;
        }
        else if (destinationPoint == null && point != startPoint)
        {
            destinationPoint = point;
        }
        else
        {
            startPoint = point;
            destinationPoint = null;
            ClearRouteOnly();
        }

        RefreshSelectionVisuals();
        PublishVisualizationState();
        BuildingSelectionChanged?.Invoke(new BuildingSelectionChangedEvent(
            destinationPoint != null ? destinationPoint.buildingData : startPoint?.buildingData,
            source,
            destinationPoint != null ? 2 : startPoint != null ? 1 : 0,
            isCorrection));
        return true;
    }

    public bool SelectBuildingById(
        string buildingId,
        BuildingSelectionSource source = BuildingSelectionSource.NormalCity)
    {
        ResolveMissingReferences();
        CityBuilding building = cityManager != null
            ? cityManager.GetBuildingById(buildingId)
            : null;
        return SelectBuilding(building, source);
    }

    public bool SelectBuilding(
        CityBuilding building,
        BuildingSelectionSource source = BuildingSelectionSource.NormalCity)
    {
        if (building == null)
            return false;

        // Keep the bool result meaningful for callers such as the Selection Lens:
        // an unavailable building was rejected, so its temporary interaction must
        // remain active instead of being treated as a successful selection.
        if (building.isFlooded)
        {
            notifier?.Show("This building is flooded / unavailable.");
            return false;
        }

        BuildingPoint point = FindBuildingPoint(building);
        if (point == null)
        {
            SetupBuildingInteractionTargets();
            point = FindBuildingPoint(building);
        }

        if (point == null)
        {
            notifier?.Show("Could not connect this building to the selection system.");
            return false;
        }

        return TrySelectPoint(point, source);
    }

    public void ResetSelection()
    {
        startPoint = null;
        destinationPoint = null;
        hoveredPoint = null;

        ClearRouteOnly();

        if (hoverTag != null) hoverTag.SetActive(false);
        if (confirmButton != null) confirmButton.SetActive(false);
        if (resetButton != null) resetButton.SetActive(false);
        if (arrow1 != null) arrow1.SetActive(false);
        if (arrow2 != null) arrow2.SetActive(false);

        PublishVisualizationState();
        SelectionCleared?.Invoke();
    }

    private void RefreshSelectionVisuals()
    {
        UpdateDynamicScale();

        if (confirmButton != null)
            confirmButton.SetActive(destinationPoint != null);

        if (resetButton != null)
            resetButton.SetActive(destinationPoint != null);

        UpdateButtonPositions();
        UpdateArrowPositions();
    }

    #endregion

    #region Buttons / Arrows

    private void UpdateButtonPositions()
    {
        if (destinationPoint == null)
        {
            if (confirmButton != null) confirmButton.SetActive(false);
            if (resetButton != null) resetButton.SetActive(false);
            return;
        }

        Vector3 up = GetCityUp();
        Vector3 top = GetHighestSelectedBuildingTop(up);
        float clearance = GetScaledClearance(selectionControlsClearance, selectionControlsClearance);

        ApplyControlVisualScale(confirmButton, confirmButtonInitialLocalScale, confirmButtonInitialParentScale);
        ApplyControlVisualScale(resetButton, resetButtonInitialLocalScale, resetButtonInitialParentScale);

        if (confirmButton != null && confirmButton.activeSelf)
            confirmButton.transform.position = top + up * (clearance + GetVisualHalfExtent(confirmButton, up));

        if (resetButton != null && resetButton.activeSelf)
        {
            float confirmExtent = GetVisualHalfExtent(confirmButton, up);
            float resetExtent = GetVisualHalfExtent(resetButton, up);
            resetButton.transform.position = top + up *
                (clearance + confirmExtent * 2f + selectionControlSpacing + resetExtent);
        }
    }

    private void UpdateArrowPositions()
    {
        Vector3 up = GetCityUp();
        bool showSelectionArrows = !hasPath;

        if (arrow1 != null)
        {
            if (showSelectionArrows && startPoint != null)
            {
                arrow1.SetActive(true);
                ApplyArrowVisualScale(arrow1, arrow1InitialLocalScale, arrow1InitialParentScale);
                arrow1.transform.position = GetBuildingTop(startPoint) + up * GetArrowClearance(arrow1, up);
            }
            else
            {
                arrow1.SetActive(false);
            }
        }

        if (arrow2 != null)
        {
            if (showSelectionArrows && destinationPoint != null)
            {
                arrow2.SetActive(true);
                ApplyArrowVisualScale(arrow2, arrow2InitialLocalScale, arrow2InitialParentScale);
                arrow2.transform.position = GetBuildingTop(destinationPoint) + up * GetArrowClearance(arrow2, up);
            }
            else
            {
                arrow2.SetActive(false);
            }
        }
    }

    private Vector3 GetCityUp()
    {
        return cityRoot != null && cityRoot.up.sqrMagnitude > 0.000001f
            ? cityRoot.up.normalized
            : Vector3.up;
    }

    private Vector3 GetBuildingTop(BuildingPoint point)
    {
        return point != null ? point.GetTopWorldPosition(GetCityUp()) : Vector3.zero;
    }

    private Vector3 GetHighestSelectedBuildingTop(Vector3 up)
    {
        Vector3 top = GetBuildingTop(destinationPoint);
        if (startPoint == null)
            return top;

        Vector3 startTop = startPoint.GetTopWorldPosition(up);
        return Vector3.Dot(startTop, up) > Vector3.Dot(top, up) ? startTop : top;
    }

    private float GetScaledClearance(float baseClearance, float minimumClearance)
    {
        return Mathf.Max(minimumClearance, Mathf.Max(0f, baseClearance) * dynamicScale);
    }

    private float GetArrowClearance(GameObject arrow, Vector3 up)
    {
        return GetScaledClearance(arrowOffset, minimumArrowClearance) + GetVisualHalfExtent(arrow, up);
    }

    private static float GetVisualHalfExtent(GameObject visual, Vector3 up)
    {
        if (visual == null)
            return 0f;

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return 0f;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        Vector3 extents = bounds.extents;
        return Mathf.Abs(up.x) * extents.x +
               Mathf.Abs(up.y) * extents.y +
               Mathf.Abs(up.z) * extents.z;
    }

    private void ApplyArrowVisualScale(GameObject arrow, Vector3 initialLocalScale, float initialParentScale)
    {
        ApplyVisualScale(
            arrow,
            initialLocalScale,
            initialParentScale,
            minimumArrowVisualScale,
            maximumArrowVisualScale);
    }

    private void ApplyControlVisualScale(GameObject control, Vector3 initialLocalScale, float initialParentScale)
    {
        ApplyVisualScale(
            control,
            initialLocalScale,
            initialParentScale,
            minimumControlVisualScale,
            maximumControlVisualScale);
    }

    private void ApplyVisualScale(
        GameObject visual,
        Vector3 initialLocalScale,
        float initialParentScale,
        float minimumVisualScale,
        float maximumVisualScale)
    {
        if (visual == null)
            return;

        if (!VisualInheritsCityScale(visual.transform))
        {
            visual.transform.localScale = initialLocalScale;
            return;
        }

        float parentScaleRatio = GetUniformScale(visual.transform.parent) /
                                 Mathf.Max(0.0001f, initialParentScale);
        float desiredVisualScale = Mathf.Clamp(
            dynamicScale,
            Mathf.Min(minimumVisualScale, maximumVisualScale),
            Mathf.Max(minimumVisualScale, maximumVisualScale));

        visual.transform.localScale = initialLocalScale *
            (desiredVisualScale / Mathf.Max(0.0001f, parentScaleRatio));
    }

    private bool VisualInheritsCityScale(Transform visual)
    {
        return cityRoot != null && visual != null && visual != cityRoot && visual.IsChildOf(cityRoot);
    }

    private static float GetUniformScale(Transform transform)
    {
        if (transform == null)
            return 1f;

        Vector3 scale = transform.lossyScale;
        return Mathf.Max(0.0001f, (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f);
    }

    private void LogPlacementState()
    {
        if (!logPlacementDebug || cityRoot == null)
            return;

        Vector3 up = GetCityUp();
        Vector3 selectedTop = destinationPoint != null
            ? GetHighestSelectedBuildingTop(up)
            : Vector3.zero;
        Vector3 confirmPosition = confirmButton != null ? confirmButton.transform.position : Vector3.zero;
        Vector3 resetPosition = resetButton != null ? resetButton.transform.position : Vector3.zero;
        Debug.Log(
            $"PointSelectManager placement: initialCityScale={initialCityScale:0.####}, " +
            $"currentCityScale={cityRoot.lossyScale.x:0.####}, relativeScale={dynamicScale:0.##}, " +
            $"baseArrowOffset={arrowOffset:0.###}, arrowClearance={GetArrowClearance(arrow1, up):0.###}, " +
            $"selectedTop={selectedTop}, confirmPosition={confirmPosition}, resetPosition={resetPosition}, " +
            $"controlsInheritCityRoot={VisualInheritsCityScale(confirmButton != null ? confirmButton.transform : null)}, " +
            $"arrowsInheritCityRoot={VisualInheritsCityScale(arrow1 != null ? arrow1.transform : null)}.");
    }

    #endregion

    #region Route Confirmation

    public bool BuildPathBetweenBuildings(
        CityBuilding startBuilding,
        CityBuilding destinationBuilding)
    {
        ResolveMissingReferences();

        if (startBuilding == null || destinationBuilding == null)
        {
            notifier?.Show("Start and destination buildings are required.");
            return false;
        }

        BuildingPoint resolvedStart = FindBuildingPoint(startBuilding);
        BuildingPoint resolvedDestination = FindBuildingPoint(destinationBuilding);

        if (resolvedStart == null || resolvedDestination == null)
        {
            SetupBuildingInteractionTargets();
            resolvedStart = FindBuildingPoint(startBuilding);
            resolvedDestination = FindBuildingPoint(destinationBuilding);
        }

        if (resolvedStart == null || resolvedDestination == null)
        {
            notifier?.Show("Could not connect the detected buildings to the path system.");
            Debug.LogWarning(
                "PointSelectManager: Could not resolve BuildingPoint objects for the spatially detected buildings.");
            return false;
        }

        if (resolvedStart == resolvedDestination)
        {
            notifier?.Show("Start and destination must be different buildings.");
            return false;
        }

        ClearRouteOnly();
        bool replacedExistingSelection = startPoint != null || destinationPoint != null;
        startPoint = resolvedStart;
        destinationPoint = resolvedDestination;
        RefreshSelectionVisuals();
        BuildingSelectionChanged?.Invoke(new BuildingSelectionChangedEvent(
            startBuilding,
            BuildingSelectionSource.DirectPhysical,
            1,
            replacedExistingSelection));
        BuildingSelectionChanged?.Invoke(new BuildingSelectionChangedEvent(
            destinationBuilding,
            BuildingSelectionSource.DirectPhysical,
            2,
            false));
        ConfirmPath();

        return hasPath;
    }

    public void ConfirmPath()
    {
        if (!CanConfirmPath())
            return;

        if (graph.IsBuildingFlooded(startPoint))
        {
            notifier?.Show("Start building is flooded / unavailable.");
            return;
        }

        if (graph.IsBuildingFlooded(destinationPoint))
        {
            notifier?.Show("Destination building is flooded / unavailable.");
            return;
        }

        bool success = TryBuildRoute(
            startPoint,
            destinationPoint,
            out Route builtRoute,
            out List<GraphNode> visualNodes,
            out Vector3 startSnapWorld,
            out Vector3 goalSnapWorld,
            out string failMessage);

        if (!success)
        {
            notifier?.Show(string.IsNullOrWhiteSpace(failMessage)
                ? "No safe route available."
                : failMessage);

            ClearRouteOnly();
            return;
        }

        ApplyRouteResult(builtRoute, visualNodes, startSnapWorld, goalSnapWorld);
        PathConfirmed?.Invoke(startPoint?.buildingData, destinationPoint?.buildingData);
    }

    private BuildingPoint FindBuildingPoint(CityBuilding building)
    {
        if (building == null)
            return null;

        foreach (BuildingPoint point in pointToMarker.Keys)
        {
            if (point == null || point.buildingData == null)
                continue;

            if (ReferenceEquals(point.buildingData, building))
                return point;

            if (!string.IsNullOrWhiteSpace(building.id) &&
                point.buildingData.id == building.id)
            {
                return point;
            }
        }

        return null;
    }

    private bool CanConfirmPath()
    {
        if (graph == null || cityManager == null || path == null)
            ResolveMissingReferences();

        if (startPoint == null || destinationPoint == null)
        {
            notifier?.Show("Select a start and destination building first.");
            return false;
        }

        if (graph == null)
        {
            notifier?.Show("Path graph is not assigned.");
            Debug.LogWarning("PointSelectManager: SimpleGraphManager is not assigned.");
            return false;
        }

        if (cityManager == null)
        {
            notifier?.Show("CityManager is not assigned.");
            Debug.LogWarning("PointSelectManager: CityManager is not assigned.");
            return false;
        }

        if (path == null)
        {
            notifier?.Show("Path LineRenderer is not assigned.");
            Debug.LogWarning("PointSelectManager: path LineRenderer is not assigned.");
            return false;
        }

        graph.EnsureGraphReady(false);
        return true;
    }

    private bool TryBuildRoute(
        BuildingPoint start,
        BuildingPoint goal,
        out Route builtRoute,
        out List<GraphNode> visualNodes,
        out Vector3 startSnapWorld,
        out Vector3 goalSnapWorld,
        out string failMessage)
    {
        builtRoute = new Route();
        visualNodes = new List<GraphNode>();
        startSnapWorld = Vector3.zero;
        goalSnapWorld = Vector3.zero;
        failMessage = string.Empty;

        if (rebuildGraphBeforeRoute)
            graph.EnsureGraphReady(true);
        else
            graph.EnsureGraphReady(false);

        Vector3 startAnchor = start.GetAnchorWorldPosition();
        Vector3 goalAnchor = goal.GetAnchorWorldPosition();

        if (logRouteDebug)
        {
            Debug.Log(
                $"PointSelectManager: building route from '{start.GetDisplayName()}' at {startAnchor} " +
                $"to '{goal.GetDisplayName()}' at {goalAnchor}. " +
                $"Graph nodes={graph.NodeCount}, directedEdges={graph.DirectedEdgeCount}, " +
                $"unblockedDirectedEdges={graph.UnblockedDirectedEdgeCount}.");
        }

        SimpleGraphManager.TempAttachment startAttachment =
            graph.CreateAttachmentNode(startAnchor, maxBuildingGraphSnapDistance, "StartAttach");

        SimpleGraphManager.TempAttachment goalAttachment =
            graph.CreateAttachmentNode(goalAnchor, maxBuildingGraphSnapDistance, "GoalAttach");

        try
        {
            GraphNode startNode = startAttachment?.node;
            GraphNode goalNode = goalAttachment?.node;

            if (startNode == null || goalNode == null)
            {
                string startDebug = startNode == null
                    ? $" Start attach failed: {graph.GetAttachmentDebugInfo(startAnchor)}"
                    : string.Empty;

                string goalDebug = goalNode == null
                    ? $" Goal attach failed: {graph.GetAttachmentDebugInfo(goalAnchor)}"
                    : string.Empty;

                failMessage = "Could not attach building to node graph.";
                Debug.LogWarning($"PointSelectManager: {failMessage}{startDebug}{goalDebug}");
                return false;
            }

            if (startNode.blocked)
            {
                failMessage = "Nearest start attachment is flooded.";
                return false;
            }

            if (goalNode.blocked)
            {
                failMessage = "Nearest destination attachment is flooded.";
                return false;
            }

            List<GraphNode> fullPath = AStarPathfinder.FindPath(startNode, goalNode);

            if (fullPath == null || fullPath.Count == 0)
            {
                failMessage = "No safe route available at current water level.";
                return false;
            }

            visualNodes = ExtractVisualNodes(fullPath, startNode, goalNode);
            builtRoute = BuildRouteFromGraphPath(fullPath, startAttachment, goalAttachment);

            if (builtRoute == null || !builtRoute.isValid)
            {
                failMessage = "Could not map graph path to road route.";
                return false;
            }

            startSnapWorld = startNode.Position;
            goalSnapWorld = goalNode.Position;
            return true;
        }
        finally
        {
            startAttachment?.Cleanup();
            goalAttachment?.Cleanup();
        }
    }

    private void ApplyRouteResult(
        Route builtRoute,
        List<GraphNode> visualNodes,
        Vector3 startSnapWorld,
        Vector3 goalSnapWorld)
    {
        currentRoute = builtRoute ?? new Route();

        currentPathNodes.Clear();
        if (visualNodes != null)
            currentPathNodes.AddRange(visualNodes);

        if (cityRoot != null)
        {
            startSnapLocal = cityRoot.InverseTransformPoint(startSnapWorld);
            goalSnapLocal = cityRoot.InverseTransformPoint(goalSnapWorld);
        }
        else
        {
            startSnapLocal = startSnapWorld;
            goalSnapLocal = goalSnapWorld;
        }

        hasStoredSnaps = true;
        hasPath = true;

        // Selection arrows identify pending endpoints. Once the route is
        // confirmed, the route and endpoint labels become the presentation.
        UpdateArrowPositions();
        RedrawCurrentPath();
        UpdateRouteBoard();
        routeWorldLabelPresenter?.ShowRouteLabels(currentRoute);
        PublishVisualizationState();
    }

    #endregion

    #region Route Building

    private List<GraphNode> ExtractVisualNodes(List<GraphNode> fullPath, GraphNode startTemp, GraphNode goalTemp)
    {
        List<GraphNode> realNodes = new List<GraphNode>();

        if (fullPath == null)
            return realNodes;

        foreach (GraphNode node in fullPath)
        {
            if (node == null) continue;
            if (node == startTemp || node == goalTemp) continue;
            realNodes.Add(node);
        }

        return realNodes;
    }

    private Route BuildRouteFromGraphPath(
        List<GraphNode> fullPath,
        SimpleGraphManager.TempAttachment startAttachment,
        SimpleGraphManager.TempAttachment goalAttachment)
    {
        Route route = new Route();

        if (fullPath == null || fullPath.Count == 0 || cityManager == null)
            return route;

        List<GraphNode> realNodes = ExtractVisualNodes(fullPath, startAttachment?.node, goalAttachment?.node);

        Road startRoad = ResolveAttachmentRoad(startAttachment);
        AddRoadIfNeeded(route.roads, startRoad);

        for (int i = 0; i < realNodes.Count - 1; i++)
        {
            GraphNode a = realNodes[i];
            GraphNode b = realNodes[i + 1];

            if (a == null || b == null)
                continue;

            Road matchedRoad = cityManager.GetRoadBetweenPoints(a.Position, b.Position);
            AddRoadIfNeeded(route.roads, matchedRoad);
        }

        Road endRoad = ResolveAttachmentRoad(goalAttachment);
        AddRoadIfNeeded(route.roads, endRoad);

        foreach (GraphNode node in realNodes)
        {
            if (node == null) continue;

            Intersection intersection = cityManager.GetClosestIntersection(node.Position);
            if (intersection != null && !route.intersections.Contains(intersection))
                route.intersections.Add(intersection);
        }

        route.totalCost = 0f;
        foreach (Road road in route.roads)
        {
            if (road != null)
                route.totalCost += RouteEvaluator.GetRoadTraversalCost(road);
        }

        route.ResetVisibleRanges();
        ApplyVisibleRoadRanges(route, startAttachment, goalAttachment, realNodes);

        route.isValid = route.roads.Count > 0 || realNodes.Count > 0;
        return route;
    }

    private void ApplyVisibleRoadRanges(
        Route route,
        SimpleGraphManager.TempAttachment startAttachment,
        SimpleGraphManager.TempAttachment goalAttachment,
        List<GraphNode> realNodes)
    {
        if (route == null || route.roads == null || route.roads.Count == 0)
            return;

        Road firstRoad = route.roads[0];
        Road lastRoad = route.roads[route.roads.Count - 1];

        if (route.roads.Count == 1 && firstRoad != null)
        {
            float startT = startAttachment != null
                ? firstRoad.GetNormalizedTForPoint(startAttachment.snappedWorldPosition)
                : 0f;

            float endT = goalAttachment != null
                ? firstRoad.GetNormalizedTForPoint(goalAttachment.snappedWorldPosition)
                : 1f;

            route.SetVisibleTRange(0, startT, endT);
            return;
        }

        if (firstRoad != null && startAttachment != null)
        {
            Vector3 nextPoint = realNodes != null && realNodes.Count > 0
                ? realNodes[0].Position
                : firstRoad.end != null ? firstRoad.end.position : startAttachment.snappedWorldPosition;

            float startT = firstRoad.GetNormalizedTForPoint(startAttachment.snappedWorldPosition);
            float endT = firstRoad.GetNormalizedTForPoint(nextPoint);
            route.SetVisibleTRange(0, startT, endT);
        }

        int lastIndex = route.roads.Count - 1;
        if (lastRoad != null && goalAttachment != null)
        {
            Vector3 previousPoint = realNodes != null && realNodes.Count > 0
                ? realNodes[realNodes.Count - 1].Position
                : lastRoad.start != null ? lastRoad.start.position : goalAttachment.snappedWorldPosition;

            float startT = lastRoad.GetNormalizedTForPoint(previousPoint);
            float endT = lastRoad.GetNormalizedTForPoint(goalAttachment.snappedWorldPosition);
            route.SetVisibleTRange(lastIndex, startT, endT);
        }
    }

    private Road ResolveAttachmentRoad(SimpleGraphManager.TempAttachment attachment)
    {
        if (attachment == null || cityManager == null)
            return null;

        GraphNode geometricA = attachment.closestEdgeA ?? attachment.edgeA;
        GraphNode geometricB = attachment.closestEdgeB ?? attachment.edgeB;

        if (geometricA == null || geometricB == null)
            return null;

        return cityManager.GetRoadBetweenPoints(
            geometricA.Position,
            geometricB.Position);
    }

    private void AddRoadIfNeeded(List<Road> roads, Road road)
    {
        if (roads == null || road == null)
            return;

        if (roads.Count > 0 && roads[roads.Count - 1] == road)
            return;

        roads.Add(road);
    }

    #endregion

    #region Path Drawing

    private void RedrawCurrentPath()
    {
        if (!hasPath || !hasStoredSnaps || startPoint == null || destinationPoint == null || path == null)
            return;

        List<Vector3> points = BuildCurrentPathWorldPoints();

        path.useWorldSpace = true;
        path.positionCount = points.Count;

        for (int i = 0; i < points.Count; i++)
            path.SetPosition(i, points[i]);
    }

    private List<Vector3> BuildCurrentPathWorldPoints()
    {
        List<Vector3> points = new List<Vector3>();
        if (!hasPath || !hasStoredSnaps || startPoint == null || destinationPoint == null)
            return points;

        points.Add(startPoint.GetTopWorldPosition());

        Vector3 startSnapWorld = cityRoot != null
            ? cityRoot.TransformPoint(startSnapLocal)
            : startSnapLocal;

        Vector3 goalSnapWorld = cityRoot != null
            ? cityRoot.TransformPoint(goalSnapLocal)
            : goalSnapLocal;

        AddPointIfFarEnough(points, startSnapWorld);

        foreach (GraphNode node in currentPathNodes)
        {
            if (node == null) continue;
            AddPointIfFarEnough(points, node.Position);
        }

        AddPointIfFarEnough(points, goalSnapWorld);
        AddPointIfFarEnough(points, destinationPoint.GetTopWorldPosition());
        return points;
    }

    public CityVisualizationSnapshot CaptureVisualizationSnapshot()
    {
        bool hasStart = startPoint != null;
        bool hasDestination = destinationPoint != null;
        CityBuilding startBuilding = hasStart ? startPoint.buildingData : null;
        CityBuilding destinationBuilding = hasDestination ? destinationPoint.buildingData : null;
        List<Vector3> pathPoints = BuildCurrentPathWorldPoints();
        List<string> roadIds = new List<string>();
        List<CityVisualizationLabel> labels = new List<CityVisualizationLabel>();

        if (hasPath && currentRoute != null && currentRoute.roads != null)
        {
            string previousLabel = null;
            for (int i = 0; i < currentRoute.roads.Count; i++)
            {
                Road road = currentRoute.roads[i];
                if (road == null)
                    continue;

                roadIds.Add(road.id ?? string.Empty);
                string label = road.DisplayNameOrFallback;
                if (label == previousLabel)
                    continue;

                Vector2 visibleRange = currentRoute.GetVisibleTRange(i);
                float midpoint = (visibleRange.x + visibleRange.y) * 0.5f;
                labels.Add(new CityVisualizationLabel(label, road.GetLabelAnchor(midpoint)));
                previousLabel = label;
            }
        }

        return new CityVisualizationSnapshot(
            hasStart,
            hasDestination,
            hasPath && pathPoints.Count > 1,
            startBuilding?.id,
            destinationBuilding?.id,
            hasStart ? startPoint.GetTopWorldPosition() : Vector3.zero,
            hasDestination ? destinationPoint.GetTopWorldPosition() : Vector3.zero,
            pathPoints.ToArray(),
            roadIds.ToArray(),
            labels.ToArray());
    }

    /// <summary>
    /// Clears a displayed route as soon as canonical flood state makes one of
    /// its endpoints, roads, or intersections unsafe.
    /// </summary>
    public bool ValidateCurrentRouteAgainstFlood()
    {
        if (!hasPath)
            return true;

        bool invalid = startPoint == null || destinationPoint == null ||
            startPoint.IsFlooded() || destinationPoint.IsFlooded();

        if (!invalid && currentRoute != null && currentRoute.roads != null)
        {
            for (int i = 0; i < currentRoute.roads.Count; i++)
            {
                if (currentRoute.roads[i] != null && currentRoute.roads[i].isBlocked)
                {
                    invalid = true;
                    break;
                }
            }
        }

        if (!invalid && currentRoute != null && currentRoute.intersections != null)
        {
            for (int i = 0; i < currentRoute.intersections.Count; i++)
            {
                if (currentRoute.intersections[i] != null && currentRoute.intersections[i].isBlocked)
                {
                    invalid = true;
                    break;
                }
            }
        }

        if (!invalid)
            return true;

        ClearRouteOnly();
        notifier?.Show("The current route is no longer safe after the flood update.");
        return false;
    }

    private void PublishVisualizationState()
    {
        visualizationStateRevision++;
        VisualizationStateChanged?.Invoke(CaptureVisualizationSnapshot());
    }

    private void AddPointIfFarEnough(List<Vector3> points, Vector3 point, float minDist = 0.0001f)
    {
        if (points.Count == 0)
        {
            points.Add(point);
            return;
        }

        if (Vector3.Distance(points[points.Count - 1], point) > minDist)
            points.Add(point);
    }

    #endregion

    #region Route Board

    private void UpdateRouteBoard()
    {
        string startName = GetMarkerDisplayName(startPoint);
        string destinationName = GetMarkerDisplayName(destinationPoint);

        string content = RouteInstructionBuilder.BuildBoardText(
            currentRoute,
            startName,
            destinationName);

        if (routeBoardText != null)
            routeBoardText.text = content;

        if (routeBoardTMP != null)
            routeBoardTMP.text = content;
    }

    private void ClearRouteBoard()
    {
        if (routeBoardText != null)
            routeBoardText.text = string.Empty;

        if (routeBoardTMP != null)
            routeBoardTMP.text = string.Empty;
    }

    private string GetMarkerDisplayName(BuildingPoint point)
    {
        if (point != null &&
            pointToMarker.TryGetValue(point, out BuildingMarker marker) &&
            marker != null)
        {
            return marker.DisplayNameOrFallback;
        }

        return point != null ? point.name : "Building";
    }

    #endregion

    #region Route Clearing

    private void ClearRouteOnly()
    {
        hasPath = false;
        hasStoredSnaps = false;

        currentPathNodes.Clear();
        currentRoute = new Route();

        if (path != null)
            path.positionCount = 0;

        ClearRouteBoard();
        routeWorldLabelPresenter?.ClearLabels();
        PublishVisualizationState();
    }

    #endregion
}
