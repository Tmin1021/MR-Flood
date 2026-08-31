using Microsoft.MixedReality.Toolkit.UI;
using UnityEngine;
using UnityEngine.Rendering;
using Microsoft.MixedReality.Toolkit.UI.BoundsControl;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.Events;
// using UnityEditor.Search;

public class MainContentController : MonoBehaviour
{
    [Header("Sections")]
    [SerializeField] private GameObject floodSection;
    [SerializeField] private GameObject layersSection;
    [SerializeField] private GameObject modelSection;

    [Header("Sliders")]
    [SerializeField] private PinchSlider heightSlider;
    [SerializeField] private PinchSlider sizeSlider;
    [SerializeField] private PinchSlider rotationXSlider;
    [SerializeField] private PinchSlider rotationYSlider;

    [Header("Placement Flow")]
    [SerializeField] private CityPlacementManager cityPlacementManager;
    [SerializeField] private CityAnchorManager cityAnchorManager;
    [SerializeField] private PlaneSelectionManager planeSelectionManager;
    [SerializeField] private SpatialModeUIBridge spatialModeUIBridge;
    [SerializeField] private SpatialObjectDetectionManager spatialObjectDetectionManager;
    [SerializeField] private SpatialBuildingObjectInterpreter spatialBuildingObjectInterpreter;
    [SerializeField] private BuildingSelectionTechniqueController buildingSelectionTechniqueController;
    [SerializeField] private VisualizationModeController visualizationModeController;
    [SerializeField] private GameObject[] placementAdjustmentControls;

    [Header("Placement Flow UI")]
    [SerializeField] private bool autoConfigureThreeButtonFlow = true;
    [SerializeField] private bool keepCitySettingsEnabledAfterConfirmation = true;

    private readonly List<SciFiButtonSpriteBridge> scanButtons = new();
    private readonly List<SciFiButtonSpriteBridge> citySettingsButtons = new();
    private readonly List<SciFiButtonSpriteBridge> confirmPlacementButtons = new();
    private readonly List<GameObject> legacyConfirmSurfaceObjects = new();
    private readonly List<Text> scanButtonLabels = new();
    private int lastPlacementUiState = -1;

    [Header("City Root")]
    [SerializeField] private GameObject cityModel;
    private Transform _cityTransform;

    [Header("Layer Objects")]
    [SerializeField] private GameObject buildings;
    [SerializeField] private GameObject trees;
    [SerializeField] private GameObject intersections;

    [Header("OSM Mode")]
    [SerializeField] private Renderer[] cityRenderers;  // assign parent or children renderers
    [SerializeField] private Material osmMat;
    [SerializeField] private Material bingMat;
    private bool useOsmTerrainTexture = true;

    [Header("Flood")]
    [SerializeField] private GameObject floodSurface;

    [Header("HoloLens Performance")]
    [SerializeField] private bool applyHoloLensPerformanceProfile = true;
    [SerializeField] private bool hideTreesOnHoloLens = true;
    [SerializeField] private bool hideIntersectionsOnHoloLens = true;
    [SerializeField] private bool disableCityShadowsOnHoloLens = true;
    [SerializeField] private bool disableLightShadowsOnHoloLens = true;

    [Header("Height Settings")]
    [SerializeField] private float minHeightOffset = 0f;
    [SerializeField] private float maxHeightOffset = 0.15f;

    [Header("Size Settings")]
    [SerializeField] private float minSizeMultiplier = 1f;
    [SerializeField] private float maxSizeMultiplier = 4f;

    [Header("Rotation Settings")]
    [SerializeField] private float minRotationXDegrees = -45f;
    [SerializeField] private float maxRotationXDegrees = 45f;
    [SerializeField] private float minRotationYDegrees = -180f;
    [SerializeField] private float maxRotationYDegrees = 180f;

    private Vector3 _initialCityLocalPosition;
    private Vector3 _initialCityLocalEulerAngles;
    private Vector3 _initialCityLocalScale;
    private float _cityRotationXDegrees;
    private float _cityRotationYDegrees;
    private PointSelectManager _pointSelectManager;
    private bool assistedLensEntryPending;

    private void Awake()
    {
        ResolvePlacementReferences();
        if (spatialObjectDetectionManager != null)
            spatialObjectDetectionManager.BaselineCaptureCompleted += HandleAssistedLensBaselinePrepared;

        if (cityModel != null)
        {
            _cityTransform = cityModel.GetComponent<Transform>();
            _initialCityLocalPosition = _cityTransform.localPosition;
            _initialCityLocalEulerAngles = _cityTransform.localEulerAngles;
            _initialCityLocalScale = _cityTransform.localScale;
        }

        SyncTerrainTextureStateFromRenderer();

        _pointSelectManager = FindAnyObjectByType<PointSelectManager>();
    }

    private void Start()
    {
        ApplyHoloLensPerformanceProfileIfNeeded();

        if (autoConfigureThreeButtonFlow)
            ConfigureThreeButtonPlacementFlow();

        if (heightSlider != null)
            heightSlider.OnValueUpdated.AddListener(OnUpdateHeightSlider);

        if (sizeSlider != null)
            sizeSlider.OnValueUpdated.AddListener(OnUpdateSizeSlider);

        if (rotationXSlider != null)
            rotationXSlider.OnValueUpdated.AddListener(OnUpdateRotationXSlider);

        if (rotationYSlider != null)
            rotationYSlider.OnValueUpdated.AddListener(OnUpdateRotationYSlider);

        RefreshPlacementButtonState(true);
    }

    private void LateUpdate()
    {
        KeepLegacySurfaceConfirmationHidden();
        RefreshPlacementButtonState(false);
    }

    private void OnDestroy()
    {
        if (spatialObjectDetectionManager != null)
            spatialObjectDetectionManager.BaselineCaptureCompleted -= HandleAssistedLensBaselinePrepared;

        if (heightSlider != null)
            heightSlider.OnValueUpdated.RemoveListener(OnUpdateHeightSlider);

        if (sizeSlider != null)
            sizeSlider.OnValueUpdated.RemoveListener(OnUpdateSizeSlider);

        if (rotationXSlider != null)
            rotationXSlider.OnValueUpdated.RemoveListener(OnUpdateRotationXSlider);

        if (rotationYSlider != null)
            rotationYSlider.OnValueUpdated.RemoveListener(OnUpdateRotationYSlider);
    }

    public void ShowFloodSection()
    {
        if (floodSection != null) floodSection.SetActive(true);
        if (layersSection != null) layersSection.SetActive(false);
        if (modelSection != null) modelSection.SetActive(false);
    }

    public void ShowLayersSection()
    {
        if (floodSection != null) floodSection.SetActive(false);
        if (layersSection != null) layersSection.SetActive(true);
        if (modelSection != null) modelSection.SetActive(false);
    }

    public void ShowModelSection()
    {
        if (floodSection != null) floodSection.SetActive(false);
        if (layersSection != null) layersSection.SetActive(false);
        if (modelSection != null) modelSection.SetActive(true);
    }

    public void ShowBuildings()
    {
        if (buildings != null)
            buildings.SetActive(!buildings.activeSelf);
    }

    public void ShowTrees()
    {
        if (trees != null)
            trees.SetActive(!trees.activeSelf);
    }

    public void ShowIntersections()
    {
        if (intersections != null)
            intersections.SetActive(!intersections.activeSelf);
    }

    public void ShowFlood()
    {
        if(floodSurface != null)
            floodSurface.SetActive(!floodSurface.activeSelf);
    }
    public void ToggleTerrainTexture()
    {
        SetTerrainTexture(!useOsmTerrainTexture);
    }

    /// <summary>
    /// Applies a specific terrain provider rather than inferring it from the
    /// current renderer material. This keeps the menu switch and the city model
    /// synchronized even when the model is re-enabled during placement.
    /// </summary>
    public void SetTerrainTexture(bool useOsm)
    {
        useOsmTerrainTexture = useOsm;

        if (cityRenderers != null && osmMat != null && bingMat != null)
        {
            Material selectedMaterial = useOsm ? osmMat : bingMat;
            for (int i = 0; i < cityRenderers.Length; i++)
            {
                Renderer renderer = cityRenderers[i];
                if (renderer != null)
                    renderer.sharedMaterial = selectedMaterial;
            }
        }

        ResolvePlacementReferences();
        cityPlacementManager?.SetTerrainTexturePreference(useOsmTerrainTexture);
        SyncTerrainTextureToggleVisual();
    }

    private void SyncTerrainTextureStateFromRenderer()
    {
        if (cityRenderers == null || osmMat == null)
            return;

        for (int i = 0; i < cityRenderers.Length; i++)
        {
            Renderer renderer = cityRenderers[i];
            if (renderer == null)
                continue;

            useOsmTerrainTexture = renderer.sharedMaterial == osmMat;
            return;
        }
    }

    private void SyncTerrainTextureToggleVisual()
    {
        SciFiSwitchToggleBridge[] toggles =
            FindObjectsByType<SciFiSwitchToggleBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < toggles.Length; i++)
        {
            SciFiSwitchToggleBridge toggle = toggles[i];
            if (toggle != null && HasTerrainTextureListener(toggle.onTurnOn) && HasTerrainTextureListener(toggle.onTurnOff))
                toggle.SetState(useOsmTerrainTexture);
        }
    }

    private bool HasTerrainTextureListener(UnityEvent action)
    {
        if (action == null)
            return false;

        for (int i = 0; i < action.GetPersistentEventCount(); i++)
        {
            if (action.GetPersistentTarget(i) == this &&
                action.GetPersistentMethodName(i) == nameof(SetTerrainTexture))
            {
                return true;
            }
        }

        return false;
    }

    public void ShowBounds()
    {
        if (cityPlacementManager != null || cityAnchorManager != null)
        {
            Debug.Log("MainContentController: bounds-based transform editing is disabled in the one-time placement flow.");
            return;
        }

        if (cityModel == null)
        {
            Debug.Log("City model is missing!");
            return;
        }

        var bounds = cityModel.GetComponent<BoundsControl>();
        var boxCollider = cityModel.GetComponent<BoxCollider>();
        if (bounds == null || boxCollider == null)
        {
            Debug.Log("Bounds or Box is missing!");
            return;
        }

        bounds.Active = !bounds.Active;
        boxCollider.enabled = !boxCollider.enabled;
    }

    private void ApplyHoloLensPerformanceProfileIfNeeded()
    {
        if (!applyHoloLensPerformanceProfile || !IsHoloLensRuntime())
            return;

        if (disableCityShadowsOnHoloLens && _cityTransform != null)
            DisableShadowsRecursive(_cityTransform);

        if (disableLightShadowsOnHoloLens)
            DisableLightShadows();

        if (hideTreesOnHoloLens && trees != null)
            trees.SetActive(false);

        if (hideIntersectionsOnHoloLens && intersections != null)
            intersections.SetActive(false);
    }

    private static bool IsHoloLensRuntime()
    {
        RuntimePlatform platform = Application.platform;
        return platform == RuntimePlatform.WSAPlayerX86
            || platform == RuntimePlatform.WSAPlayerX64
            || platform == RuntimePlatform.WSAPlayerARM;
    }

    private static void DisableShadowsRecursive(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private static void DisableLightShadows()
    {
        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null)
                continue;

            light.shadows = LightShadows.None;
        }
    }

    private void OnUpdateHeightSlider(SliderEventData data)
    {
        ApplyHeight(data.NewValue);
    }

    private void ApplyHeight(float t)
    {
        SetCityHeight(Mathf.Lerp(minHeightOffset, maxHeightOffset, Mathf.Clamp01(t)));
    }

    private void OnUpdateSizeSlider(SliderEventData data)
    {
        ApplySize(data.NewValue);
    }

    private void ApplySize(float t)
    {
        SetCityScale(Mathf.Lerp(minSizeMultiplier, maxSizeMultiplier, Mathf.Clamp01(t)));
    }

    private void OnUpdateRotationXSlider(SliderEventData data)
    {
        SetCityRotationXNormalized(data.NewValue);
    }

    private void OnUpdateRotationYSlider(SliderEventData data)
    {
        SetCityRotationYNormalized(data.NewValue);
    }

    public void SetPlacementControlsEnabled(bool enabled)
    {
        if (heightSlider != null)
            heightSlider.enabled = enabled;

        if (sizeSlider != null)
            sizeSlider.enabled = enabled;

        if (rotationXSlider != null)
            rotationXSlider.enabled = enabled;

        if (rotationYSlider != null)
            rotationYSlider.enabled = enabled;

        if (placementAdjustmentControls == null)
            return;

        for (int i = 0; i < placementAdjustmentControls.Length; i++)
        {
            if (placementAdjustmentControls[i] != null)
                placementAdjustmentControls[i].SetActive(enabled);
        }
    }

    public void SetCityScale1x()
    {
        SetCityScale(1f);
    }

    public void SetCityScale2x()
    {
        SetCityScale(2f);
    }

    public void SetCityScale3x()
    {
        SetCityScale(3f);
    }

    public void SetCityScale4x()
    {
        SetCityScale(4f);
    }

    public void SetCityHeightGround()
    {
        SetCityHeight(0f);
    }

    public void SetCityHeight005()
    {
        SetCityHeight(0.05f);
    }

    public void SetCityHeight010()
    {
        SetCityHeight(0.10f);
    }

    public void SetCityHeight015()
    {
        SetCityHeight(0.15f);
    }

    public void SetCityHeightNormalized(float t)
    {
        ApplyHeight(t);
    }

    public void SetCitySizeNormalized(float t)
    {
        ApplySize(t);
    }

    public void SetCityHeight(float heightOffset)
    {
        if (TryApplyPlacementHeight(heightOffset))
            return;

        if (_cityTransform == null)
            return;

        Vector3 pos = _initialCityLocalPosition;
        pos.y += heightOffset;
        _cityTransform.localPosition = pos;
    }

    public void SetCityScale(float multiplier)
    {
        if (TryApplyPlacementScale(multiplier))
            return;

        if (_cityTransform == null)
            return;

        _cityTransform.localScale = _initialCityLocalScale * multiplier;
        _pointSelectManager?.RefreshForCityScaleChange();
    }

    public void SetCityRotationXNormalized(float t)
    {
        SetCityRotationX(Mathf.Lerp(minRotationXDegrees, maxRotationXDegrees, Mathf.Clamp01(t)));
    }

    public void SetCityRotationYNormalized(float t)
    {
        SetCityRotationY(Mathf.Lerp(minRotationYDegrees, maxRotationYDegrees, Mathf.Clamp01(t)));
    }

    public void SetCityRotationX(float degrees)
    {
        if (TryApplyPlacementRotationX(degrees))
            return;

        _cityRotationXDegrees = degrees;
        ApplyFallbackRotation();
    }

    public void SetCityRotationY(float degrees)
    {
        if (TryApplyPlacementRotationY(degrees))
            return;

        _cityRotationYDegrees = degrees;
        ApplyFallbackRotation();
    }

    public void IncreaseCityHeight()
    {
        cityPlacementManager?.IncreaseHeight();
    }

    public void DecreaseCityHeight()
    {
        cityPlacementManager?.DecreaseHeight();
    }

    public void RotateCityLeft()
    {
        cityPlacementManager?.RotateLeft();
    }

    public void RotateCityRight()
    {
        cityPlacementManager?.RotateRight();
    }

    public void ResetCityRotation()
    {
        if (TryResetPlacementRotation())
            return;

        _cityRotationXDegrees = 0f;
        _cityRotationYDegrees = 0f;
        ApplyFallbackRotation();
    }

    public void ConfirmCityPlacement()
    {
        ResolvePlacementReferences();
        cityPlacementManager?.ConfirmPlacement();
        spatialObjectDetectionManager?.PrepareSpatialBaseline();
        SetTerrainTexture(useOsmTerrainTexture);
        RefreshPlacementButtonState(true);
    }

    /// <summary>Unity/MRTK button entry point for resetting only city placement.</summary>
    public void ResetCityPlacement()
    {
        ResolvePlacementReferences();
        assistedLensEntryPending = false;
        spatialObjectDetectionManager?.ClearSpatialBaseline();
        cityPlacementManager?.ResetSavedCityPlacement();
        RefreshPlacementButtonState(true);
    }

    public void RefreshPlacementState()
    {
        RefreshPlacementButtonState(true);
    }

    public void EnterPlaneDetectionMode()
    {
        ResolvePlacementReferences();
        assistedLensEntryPending = false;

        if (cityPlacementManager != null && cityPlacementManager.HasSavedPlacement)
        {
            cityPlacementManager.ResetSavedCityPlacement();
            RefreshPlacementButtonState(true);
            return;
        }

        cityPlacementManager?.CancelPlacement();
        planeSelectionManager?.ResetSelection();
        planeSelectionManager?.EnableSelection();
        RefreshPlacementButtonState(true);
    }

    public void ExitPlaneDetectionMode()
    {
        ResolvePlacementReferences();
        planeSelectionManager?.DisableSelection();
    }

    public void ConfirmDetectedSurface()
    {
        planeSelectionManager?.ConfirmCurrentCandidate();
        RefreshPlacementButtonState(true);
    }

    public void RejectDetectedSurface()
    {
        EnterPlaneDetectionMode();
    }

    public void EnterBuildingPlacingMode()
    {
        ResolvePlacementReferences();
        assistedLensEntryPending = false;
        spatialModeUIBridge?.EnterBuildingPlacingMode();
    }

    public void EnterFloodPlacingMode()
    {
        ResolvePlacementReferences();
        assistedLensEntryPending = false;
        spatialModeUIBridge?.EnterFloodPlacingMode();
    }

    public void SetDirectBuildingSelectionTechnique()
    {
        ResolvePlacementReferences();
        assistedLensEntryPending = false;
        spatialBuildingObjectInterpreter?.SetSingleCandidatePreviewEnabled(false);
        buildingSelectionTechniqueController?.SetDirectTechnique();
        spatialModeUIBridge?.EnterBuildingPlacingMode();
    }

    /// <summary>
    /// Unity/MRTK button action for the single-target Direct technique. It enters
    /// Direct mode when needed and immediately scans for the one physical object
    /// used to preview the matched target building.
    /// </summary>
    public void SetSingleCandidateDirectBuildingSelectionTechnique()
    {
        ResolvePlacementReferences();
        assistedLensEntryPending = false;
        spatialBuildingObjectInterpreter?.SetSingleCandidatePreviewEnabled(true);
        buildingSelectionTechniqueController?.SetDirectTechnique();

        if (spatialObjectDetectionManager == null ||
            spatialObjectDetectionManager.CurrentMode != SpatialPlacementMode.BuildingPlacing)
        {
            spatialModeUIBridge?.EnterBuildingPlacingMode();
        }

        spatialModeUIBridge?.ScanCurrentMode();
    }

    public void SetAssistedLensBuildingSelectionTechnique()
    {
        ResolvePlacementReferences();
        spatialBuildingObjectInterpreter?.SetSingleCandidatePreviewEnabled(false);
        buildingSelectionTechniqueController?.SetAssistedLensTechnique();

        if (spatialObjectDetectionManager != null &&
            !spatialObjectDetectionManager.HasSpatialBaseline &&
            !spatialObjectDetectionManager.CanScanWithoutSpatialBaseline)
        {
            assistedLensEntryPending = true;
            spatialModeUIBridge?.ShowAssistedLensPreparationMessage();
            spatialObjectDetectionManager.PrepareSpatialBaseline();
            return;
        }

        assistedLensEntryPending = false;
        spatialModeUIBridge?.EnterBuildingPlacingMode();
    }

    private void HandleAssistedLensBaselinePrepared(bool succeeded)
    {
        if (!assistedLensEntryPending)
            return;

        assistedLensEntryPending = false;
        if (!succeeded)
        {
            spatialModeUIBridge?.ShowAssistedLensBaselineFailureMessage();
            return;
        }

        if (buildingSelectionTechniqueController != null &&
            buildingSelectionTechniqueController.CurrentTechnique == BuildingSelectionTechnique.AssistedLens)
        {
            spatialModeUIBridge?.EnterBuildingPlacingMode();
        }
    }

    public void ScanSpatialObjects()
    {
        ResolvePlacementReferences();
        spatialModeUIBridge?.ScanCurrentMode();
    }

    public void CaptureSpatialObjectBaseline()
    {
        ResolvePlacementReferences();
        spatialModeUIBridge?.CaptureSpatialBaseline();
    }

    public void ConfirmSpatialPlacementMode()
    {
        ResolvePlacementReferences();
        spatialModeUIBridge?.ConfirmCurrentMode();
    }

    public void ClearSpatialPlacementMode()
    {
        ResolvePlacementReferences();
        spatialModeUIBridge?.ClearCurrentMode();
    }

    public void CancelSpatialPlacementMode()
    {
        ResolvePlacementReferences();
        assistedLensEntryPending = false;
        spatialModeUIBridge?.CancelCurrentMode();
    }

    public void EnterVisualizationMode()
    {
        ResolvePlacementReferences();
        assistedLensEntryPending = false;
        visualizationModeController?.EnterVisualizationMode();
    }

    public void ExitVisualizationMode()
    {
        ResolvePlacementReferences();
        visualizationModeController?.ExitVisualizationMode();
    }

    public void ToggleVisualizationMode()
    {
        ResolvePlacementReferences();
        visualizationModeController?.ToggleVisualizationMode();
    }

    public void ResetVisualizationTransform()
    {
        ResolvePlacementReferences();
        visualizationModeController?.ResetVisualizationTransform();
    }

    private bool TryApplyPlacementHeight(float heightOffset)
    {
        ResolvePlacementReferences();

        if (cityPlacementManager == null)
            return false;

        cityPlacementManager.SetCityHeight(heightOffset);
        return true;
    }

    private bool TryApplyPlacementScale(float multiplier)
    {
        ResolvePlacementReferences();

        if (cityPlacementManager == null)
            return false;

        cityPlacementManager.SetCityScale(multiplier);
        return true;
    }

    private bool TryApplyPlacementRotationX(float degrees)
    {
        ResolvePlacementReferences();

        if (cityPlacementManager == null)
            return false;

        cityPlacementManager.SetPitchOffset(degrees);
        return true;
    }

    private bool TryApplyPlacementRotationY(float degrees)
    {
        ResolvePlacementReferences();

        if (cityPlacementManager == null)
            return false;

        cityPlacementManager.SetYawOffset(degrees);
        return true;
    }

    private bool TryResetPlacementRotation()
    {
        ResolvePlacementReferences();

        if (cityPlacementManager == null)
            return false;

        cityPlacementManager.ResetRotation();
        return true;
    }

    private void ApplyFallbackRotation()
    {
        if (_cityTransform == null)
            return;

        Vector3 euler = _initialCityLocalEulerAngles;
        euler.x += _cityRotationXDegrees;
        euler.y += _cityRotationYDegrees;
        _cityTransform.localRotation = Quaternion.Euler(euler);
    }

    private void ResolvePlacementReferences()
    {
        cityPlacementManager ??=
            FindFirstObjectByType<CityPlacementManager>(FindObjectsInactive.Include);
        cityAnchorManager ??=
            FindFirstObjectByType<CityAnchorManager>(FindObjectsInactive.Include);
        planeSelectionManager ??=
            FindFirstObjectByType<PlaneSelectionManager>(FindObjectsInactive.Include);
        spatialModeUIBridge ??=
            FindFirstObjectByType<SpatialModeUIBridge>(FindObjectsInactive.Include);
        spatialObjectDetectionManager ??=
            FindFirstObjectByType<SpatialObjectDetectionManager>(FindObjectsInactive.Include);
        spatialBuildingObjectInterpreter ??=
            FindFirstObjectByType<SpatialBuildingObjectInterpreter>(FindObjectsInactive.Include);
        buildingSelectionTechniqueController ??=
            FindFirstObjectByType<BuildingSelectionTechniqueController>(FindObjectsInactive.Include);
        visualizationModeController ??= VisualizationModeController.Instance ??
            FindFirstObjectByType<VisualizationModeController>(FindObjectsInactive.Include);
    }

    private void ConfigureThreeButtonPlacementFlow()
    {
        scanButtons.Clear();
        citySettingsButtons.Clear();
        confirmPlacementButtons.Clear();
        legacyConfirmSurfaceObjects.Clear();
        scanButtonLabels.Clear();

        SciFiButtonSpriteBridge[] bridges =
            FindObjectsByType<SciFiButtonSpriteBridge>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bridges.Length; i++)
        {
            SciFiButtonSpriteBridge bridge = bridges[i];
            if (bridge == null)
                continue;

            if (HasPersistentMethod(bridge, nameof(EnterPlaneDetectionMode)))
                scanButtons.Add(bridge);
            else if (HasPersistentMethod(bridge, nameof(ConfirmCityPlacement)))
                confirmPlacementButtons.Add(bridge);
            else if (HasPersistentMethod(bridge, nameof(ConfirmDetectedSurface)))
            {
                legacyConfirmSurfaceObjects.Add(bridge.gameObject);
                bridge.gameObject.SetActive(false);
            }
            else if (bridge.name.StartsWith("CitySettingButton"))
                citySettingsButtons.Add(bridge);
        }

        Text[] labels = FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < labels.Length; i++)
        {
            Text label = labels[i];
            if (label == null)
                continue;

            Transform card = label.transform.parent;
            if (card == null)
                continue;

            switch (label.text.Trim())
            {
                case "Start Surface Scan":
                case "Rescan":
                    scanButtonLabels.Add(label);
                    break;

                case "Use This Surface":
                    GameObject legacyCard =
                        FindPlacementCardRoot(label.transform, "Step 2").gameObject;
                    if (!legacyConfirmSurfaceObjects.Contains(legacyCard))
                        legacyConfirmSurfaceObjects.Add(legacyCard);
                    legacyCard.SetActive(false);
                    break;

                case "Open City Settings":
                    RenameStepLabel(
                        FindPlacementCardRoot(label.transform, "Step 3"),
                        "Step 3",
                        "Step 2");
                    break;

                case "Confirm City Placement":
                    RenameStepLabel(
                        FindPlacementCardRoot(label.transform, "Step 4"),
                        "Step 4",
                        "Step 3");
                    break;
            }
        }
    }

    private void KeepLegacySurfaceConfirmationHidden()
    {
        for (int i = 0; i < legacyConfirmSurfaceObjects.Count; i++)
        {
            if (legacyConfirmSurfaceObjects[i] != null &&
                legacyConfirmSurfaceObjects[i].activeSelf)
            {
                legacyConfirmSurfaceObjects[i].SetActive(false);
            }
        }
    }

    private static bool HasPersistentMethod(SciFiButtonSpriteBridge bridge, string methodName)
    {
        if (bridge.onClickAction == null)
            return false;

        for (int i = 0; i < bridge.onClickAction.GetPersistentEventCount(); i++)
        {
            if (bridge.onClickAction.GetPersistentMethodName(i) == methodName)
                return true;
        }

        return false;
    }

    private static Transform FindPlacementCardRoot(Transform start, string stepLabel)
    {
        Transform current = start;
        while (current != null)
        {
            Text[] labels = current.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] != null && labels[i].text.Trim() == stepLabel)
                    return current;
            }

            current = current.parent;
        }

        return start.parent != null ? start.parent : start;
    }

    private static void RenameStepLabel(Transform card, string oldText, string newText)
    {
        Text[] labels = card.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] != null && labels[i].text.Trim() == oldText)
                labels[i].text = newText;
        }
    }

    private void RefreshPlacementButtonState(bool force)
    {
        ResolvePlacementReferences();

        bool confirmed = cityPlacementManager != null && cityPlacementManager.HasConfirmed;
        bool adjustment = cityPlacementManager != null && cityPlacementManager.IsInAdjustmentMode;
        bool scanning = planeSelectionManager != null && planeSelectionManager.IsSelectionEnabled;

        int state = confirmed ? 3 : adjustment ? 2 : scanning ? 1 : 0;
        if (!force && state == lastPlacementUiState)
            return;

        lastPlacementUiState = state;

        SetButtonsInteractable(scanButtons, true);
        SetButtonsInteractable(citySettingsButtons, adjustment || (confirmed && keepCitySettingsEnabledAfterConfirmation));
        SetButtonsInteractable(confirmPlacementButtons, adjustment);

        string scanLabel = state == 0 ? "Start Surface Scan" : "Rescan";
        for (int i = 0; i < scanButtonLabels.Count; i++)
        {
            if (scanButtonLabels[i] != null)
                scanButtonLabels[i].text = scanLabel;
        }
    }

    private static void SetButtonsInteractable(
        List<SciFiButtonSpriteBridge> buttons,
        bool interactable)
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            if (buttons[i] != null)
                buttons[i].SetInteractable(interactable);
        }
    }
}
