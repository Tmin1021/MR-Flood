using UnityEngine;

[DefaultExecutionOrder(-980)]
[DisallowMultipleComponent]
public class CityPlacementManager : MonoBehaviour
{
    private const string SpatialAnchorName = "city-placement-anchor-v1";

    [Header("References")]
    [SerializeField] private Transform cityAnchorRoot;
    [SerializeField] private Transform placementPivotTransform;
    [SerializeField] private CityAnchorManager cityAnchorManager;
    [SerializeField] private PlaneSelectionManager planeSelectionManager;
    [SerializeField] private MainContentController citySettingsController;
    [SerializeField] private PointSelectManager pointSelectManager;

    [Header("Placement")]
    [SerializeField] private bool hideCityAtStartup = true;
    [SerializeField] private bool useYawOnlyFromPlacementPose = true;
    [SerializeField] private float heightStep = 0.05f;
    [SerializeField] private float rotationStepDegrees = 15f;

    public bool HasPlacementStarted => hasPlacementStarted;
    public bool IsInAdjustmentMode => hasPlacementStarted && !hasConfirmed;
    public bool HasConfirmed => hasConfirmed;
    public bool HasSavedPlacement =>
        hasConfirmed || (!ignoreSavedPlacementForSession && persistence.HasSavedPlacement());
    public Transform PlacementPivotTransform => placementPivotTransform != null ? placementPivotTransform : cityAnchorRoot;

    private Vector3 initialLocalScale = Vector3.one;
    private Vector3 initialWorldPosition;
    private Quaternion initialWorldRotation = Quaternion.identity;
    private Vector3 basePlacementPosition;
    private Quaternion basePlacementRotation = Quaternion.identity;
    private float currentScaleMultiplier = 1f;
    private float currentHeightOffset;
    private float currentPitchOffset;
    private float currentYawOffset;
    private bool useOsmTerrainTexture = true;
    private bool hasPlacementStarted;
    private bool hasConfirmed;
    private bool ignoreSavedPlacementForSession;
    private CityPlacementSaveData pendingRestoreData;
    private CitySpatialAnchorPersistence spatialAnchorPersistence;
    private int persistenceOperationVersion;
    private readonly CityPlacementPersistence persistence = new CityPlacementPersistence();

    private void Awake()
    {
        ResolveMissingReferences();
        CacheInitialScale();
        spatialAnchorPersistence = GetComponent<CitySpatialAnchorPersistence>();
        if (spatialAnchorPersistence == null)
            spatialAnchorPersistence = gameObject.AddComponent<CitySpatialAnchorPersistence>();

        if (cityAnchorRoot == null)
        {
            Debug.LogWarning("CityPlacementManager: CityAnchorRoot is not assigned.");
            return;
        }

        if (!PrepareSavedPlacementRestore())
        {
            if (hideCityAtStartup)
                cityAnchorRoot.gameObject.SetActive(false);

            citySettingsController?.SetPlacementControlsEnabled(false);
        }

        Debug.Log(
            $"CityPlacementManager: startup state. cityVisible={cityAnchorRoot.gameObject.activeSelf}, " +
            $"hasPlacementStarted={hasPlacementStarted}, hasConfirmed={hasConfirmed}.");
    }

    private async void Start()
    {
        if (pendingRestoreData == null)
            return;

        int operationVersion = persistenceOperationVersion;
        CityPlacementSaveData data = pendingRestoreData;
        bool restoredFromSpatialAnchor = false;
        Pose restoredPose = new Pose(data.position, data.rotation);

        if (data.usesSpatialAnchor)
        {
            restoredFromSpatialAnchor =
                await spatialAnchorPersistence.TryRestoreAsync(data.spatialAnchorName);

            if (operationVersion != persistenceOperationVersion)
            {
                spatialAnchorPersistence.ReleaseActiveAnchor();
                return;
            }

            if (!restoredFromSpatialAnchor)
            {
                FailSavedPlacementRestore(
                    "the device-local spatial anchor could not be localized");
                return;
            }

            restoredPose = spatialAnchorPersistence.ActiveAnchorPose;
        }
        else if (spatialAnchorPersistence.IsSupportedRuntime)
        {
            FailSavedPlacementRestore(
                "the save contains only headset-relative coordinates and is unsafe on HoloLens");
            return;
        }

        pendingRestoreData = null;
        ApplyRestoredPlacement(data, restoredPose);
        ResolveMissingReferences();

        if (cityAnchorManager == null ||
            !cityAnchorManager.RestorePlacementConfirmationState() ||
            !cityAnchorManager.CompleteRestoredPlacementInitialization())
        {
            FailSavedPlacementRestore("post-confirmation city initialization failed");
            return;
        }

        Debug.Log(
            $"CityPlacementManager: restore succeeded from '{persistence.SaveFilePath}'. " +
            $"spatialAnchor={restoredFromSpatialAnchor}, position={cityAnchorRoot.position}, " +
            $"rotation={cityAnchorRoot.rotation}, localScale={cityAnchorRoot.localScale}.");
    }

    public bool BeginPlacement(Pose pose)
    {
        ResolveMissingReferences();

        if (cityAnchorRoot == null)
        {
            Debug.LogWarning("CityPlacementManager: cannot begin placement because CityAnchorRoot is missing.");
            return false;
        }

        if (hasConfirmed)
        {
            Debug.LogWarning("CityPlacementManager: placement was already confirmed and cannot be started again.");
            return false;
        }

        if (hasPlacementStarted)
        {
            Debug.LogWarning("CityPlacementManager: a surface was already selected. Reset the scene to place again.");
            return false;
        }

        basePlacementPosition = pose.position;
        basePlacementRotation = ResolvePlacementRotation(pose.rotation);
        currentScaleMultiplier = 1f;
        currentHeightOffset = 0f;
        currentPitchOffset = 0f;
        currentYawOffset = 0f;

        ApplyCurrentPlacement();

        if (!cityAnchorRoot.gameObject.activeSelf)
            cityAnchorRoot.gameObject.SetActive(true);

        hasPlacementStarted = true;
        planeSelectionManager?.DisableSelection();

        citySettingsController?.ShowModelSection();
        citySettingsController?.SetPlacementControlsEnabled(true);

        Debug.Log(
            $"CityPlacementManager: city shown at {cityAnchorRoot.position} " +
            $"with yaw {cityAnchorRoot.rotation.eulerAngles.y:0.##}.");

        return true;
    }

    public void SetCityScale(float scale)
    {
        if (!CanAdjust())
            return;

        currentScaleMultiplier = Mathf.Max(0.0001f, scale);
        ApplyCurrentPlacement();

        Debug.Log($"CityPlacementManager: size changed to {currentScaleMultiplier:0.##}x.");
    }

    public void SetCityHeightGround()
    {
        SetCityHeight(0f);
    }

    public void SetCityHeight(float heightOffset)
    {
        if (!CanAdjust())
            return;

        currentHeightOffset = heightOffset;
        ApplyCurrentPlacement();

        Debug.Log($"CityPlacementManager: height changed to {currentHeightOffset:0.###}m.");
    }

    public void IncreaseHeight()
    {
        SetCityHeight(currentHeightOffset + heightStep);
    }

    public void DecreaseHeight()
    {
        SetCityHeight(currentHeightOffset - heightStep);
    }

    public void RotateLeft()
    {
        SetYawOffset(currentYawOffset - rotationStepDegrees);
    }

    public void RotateRight()
    {
        SetYawOffset(currentYawOffset + rotationStepDegrees);
    }

    public void ResetRotation()
    {
        if (!CanAdjust())
            return;

        currentPitchOffset = 0f;
        currentYawOffset = 0f;
        ApplyCurrentPlacement();

        Debug.Log("CityPlacementManager: rotation reset.");
    }

    public void ConfirmPlacement()
    {
        if (!CanAdjust())
            return;

        ResolveMissingReferences();

        if (cityAnchorManager == null)
        {
            Debug.LogWarning("CityPlacementManager: CityAnchorManager is not assigned.");
            return;
        }

        if (!cityAnchorManager.ConfirmPlacement())
            return;

        hasConfirmed = true;
        citySettingsController?.SetPlacementControlsEnabled(false);

        int operationVersion = ++persistenceOperationVersion;
        PersistConfirmedPlacementAsync(operationVersion);

        Debug.Log("CityPlacementManager: placement confirmed and locked.");
    }

    public void SetTerrainTexturePreference(bool useOsm)
    {
        useOsmTerrainTexture = useOsm;

        // Before confirmation the preference is included in the upcoming save.
        // After confirmation, update only the JSON settings; the spatial anchor
        // itself remains valid and does not need to be recreated.
        if (!hasConfirmed || !persistence.TryLoad(out CityPlacementSaveData data))
            return;

        data.useOsmTerrain = useOsmTerrainTexture;
        data.version = CityPlacementPersistence.CurrentVersion;
        persistence.Save(data);
    }

    private bool CanAdjust()
    {
        if (cityAnchorRoot == null)
        {
            Debug.LogWarning("CityPlacementManager: CityAnchorRoot is not assigned.");
            return false;
        }

        if (!hasPlacementStarted)
        {
            Debug.LogWarning("CityPlacementManager: placement has not started yet.");
            return false;
        }

        if (hasConfirmed)
        {
            Debug.LogWarning("CityPlacementManager: placement is locked. Reset the scene to move the city again.");
            return false;
        }

        return true;
    }

    public void SetPitchOffset(float pitchOffsetDegrees)
    {
        if (!CanAdjust())
            return;

        currentPitchOffset = pitchOffsetDegrees;
        ApplyCurrentPlacement();

        Debug.Log($"CityPlacementManager: pitch changed to {currentPitchOffset:0.##} degrees.");
    }

    public void SetYawOffset(float yawOffsetDegrees)
    {
        if (!CanAdjust())
            return;

        currentYawOffset = yawOffsetDegrees;
        ApplyCurrentPlacement();

        Debug.Log($"CityPlacementManager: rotation changed to {currentYawOffset:0.##} degrees.");
    }

    private void ApplyCurrentPlacement()
    {
        if (cityAnchorRoot == null)
            return;

        cityAnchorRoot.rotation = ResolveCurrentRotation();
        cityAnchorRoot.localScale = initialLocalScale * currentScaleMultiplier;

        Vector3 pivotTargetPosition = basePlacementPosition + Vector3.up * currentHeightOffset;
        cityAnchorRoot.position = pivotTargetPosition;

        if (placementPivotTransform != null)
            cityAnchorRoot.position += pivotTargetPosition - placementPivotTransform.position;

        pointSelectManager?.RefreshForCityScaleChange();
    }

    private Quaternion ResolvePlacementRotation(Quaternion poseRotation)
    {
        if (!useYawOnlyFromPlacementPose)
            return poseRotation;

        return Quaternion.Euler(0f, poseRotation.eulerAngles.y, 0f);
    }

    private Quaternion ResolveCurrentRotation()
    {
        if (useYawOnlyFromPlacementPose)
        {
            float yaw = basePlacementRotation.eulerAngles.y + currentYawOffset;
            return Quaternion.Euler(currentPitchOffset, yaw, 0f);
        }

        return Quaternion.Euler(currentPitchOffset, currentYawOffset, 0f) * basePlacementRotation;
    }

    private void ResolveMissingReferences()
    {
        cityAnchorManager ??=
            FindFirstObjectByType<CityAnchorManager>(FindObjectsInactive.Include);
        planeSelectionManager ??=
            FindFirstObjectByType<PlaneSelectionManager>(FindObjectsInactive.Include);
        citySettingsController ??=
            FindFirstObjectByType<MainContentController>(FindObjectsInactive.Include);
        pointSelectManager ??=
            FindFirstObjectByType<PointSelectManager>(FindObjectsInactive.Include);

        if (cityAnchorRoot == null && cityAnchorManager != null)
            cityAnchorRoot = cityAnchorManager.CityAnchorRoot;

        if (cityAnchorRoot == null)
        {
            if (pointSelectManager != null && pointSelectManager.cityRoot != null)
                cityAnchorRoot = pointSelectManager.cityRoot;
        }

        if (cityAnchorRoot == null)
        {
            CityBootstrapper cityBootstrapper =
                FindFirstObjectByType<CityBootstrapper>(FindObjectsInactive.Include);

            if (cityBootstrapper != null)
            {
                if (cityBootstrapper.buildingsRoot != null)
                    cityAnchorRoot = cityBootstrapper.buildingsRoot.root;
                else if (cityBootstrapper.roadsRoot != null)
                    cityAnchorRoot = cityBootstrapper.roadsRoot.root;
                else if (cityBootstrapper.intersectionsRoot != null)
                    cityAnchorRoot = cityBootstrapper.intersectionsRoot.root;
            }
        }

        if (placementPivotTransform == null && cityAnchorRoot != null)
            placementPivotTransform = FindChildRecursive(cityAnchorRoot, "CityCenterPivot");
    }

    private void CacheInitialScale()
    {
        if (cityAnchorRoot == null)
            return;

        initialLocalScale = cityAnchorRoot.localScale;
        initialWorldPosition = cityAnchorRoot.position;
        initialWorldRotation = cityAnchorRoot.rotation;
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

    public bool CancelPlacement()
    {
        ResolveMissingReferences();

        if (hasConfirmed)
        {
            Debug.LogWarning("CityPlacementManager: rescan is ignored because placement is finalized.");
            return false;
        }

        hasPlacementStarted = false;
        currentScaleMultiplier = 1f;
        currentHeightOffset = 0f;
        currentPitchOffset = 0f;
        currentYawOffset = 0f;

        if (cityAnchorRoot != null)
        {
            cityAnchorRoot.SetPositionAndRotation(initialWorldPosition, initialWorldRotation);
            cityAnchorRoot.localScale = initialLocalScale;
            cityAnchorRoot.gameObject.SetActive(false);
        }

        citySettingsController?.SetPlacementControlsEnabled(false);
        Debug.Log("CityPlacementManager: temporary city placement cancelled.");
        return true;
    }

    public void ResetSavedCityPlacement()
    {
        ResolveMissingReferences();
        ignoreSavedPlacementForSession = true;

        if (!persistence.Delete())
        {
            Debug.LogWarning(
                "CityPlacementManager: placement reset will continue, but the save file could not be deleted.");
        }

        persistenceOperationVersion++;
        pendingRestoreData = null;
        hasConfirmed = false;
        hasPlacementStarted = false;
        ResetPlacementValues();

        cityAnchorManager?.ResetPlacementConfirmationState();
        spatialAnchorPersistence?.ReleaseActiveAnchor();
        DeleteSpatialAnchorAsync();

        if (cityAnchorRoot != null)
        {
            cityAnchorRoot.SetPositionAndRotation(initialWorldPosition, initialWorldRotation);
            cityAnchorRoot.localScale = initialLocalScale;
            cityAnchorRoot.gameObject.SetActive(false);
        }

        citySettingsController?.SetPlacementControlsEnabled(false);

        if (planeSelectionManager != null)
        {
            planeSelectionManager.enabled = true;
            planeSelectionManager.ResetSelection();
            planeSelectionManager.EnableSelection();
        }

        citySettingsController?.RefreshPlacementState();
        Debug.Log("CityPlacementManager: placement reset completed; surface selection is active.");
    }

    private bool PrepareSavedPlacementRestore()
    {
        if (!persistence.TryLoad(out CityPlacementSaveData data))
            return false;

        pendingRestoreData = data;
        persistenceOperationVersion++;
        cityAnchorRoot.gameObject.SetActive(false);
        planeSelectionManager?.DisableSelection();
        citySettingsController?.SetPlacementControlsEnabled(false);
        ignoreSavedPlacementForSession = false;
        Debug.Log(
            $"CityPlacementManager: saved placement found at '{persistence.SaveFilePath}'; " +
            "waiting for physical spatial-anchor localization.");
        return true;
    }

    private void ApplyRestoredPlacement(CityPlacementSaveData data, Pose pose)
    {
        cityAnchorRoot.SetPositionAndRotation(pose.position, pose.rotation);
        cityAnchorRoot.localScale = data.localScale;
        cityAnchorRoot.gameObject.SetActive(true);

        basePlacementPosition = pose.position;
        basePlacementRotation = pose.rotation;
        currentScaleMultiplier = 1f;
        currentHeightOffset = 0f;
        currentPitchOffset = 0f;
        currentYawOffset = 0f;
        useOsmTerrainTexture = data.useOsmTerrain;
        hasPlacementStarted = true;
        hasConfirmed = true;

        planeSelectionManager?.DisableSelection();
        citySettingsController?.SetPlacementControlsEnabled(false);
        citySettingsController?.SetTerrainTexture(useOsmTerrainTexture);
    }

    private async void PersistConfirmedPlacementAsync(int operationVersion)
    {
        Pose pose = new Pose(cityAnchorRoot.position, cityAnchorRoot.rotation);
        bool useSpatialAnchor = spatialAnchorPersistence.IsSupportedRuntime;

        if (useSpatialAnchor)
        {
            bool anchorSaved =
                await spatialAnchorPersistence.TryPersistAsync(SpatialAnchorName, pose);

            if (operationVersion != persistenceOperationVersion)
            {
                if (anchorSaved)
                    await spatialAnchorPersistence.DeleteAsync(SpatialAnchorName);
                return;
            }

            if (!anchorSaved)
            {
                persistence.Delete();
                Debug.LogWarning(
                    "CityPlacementManager: placement remains confirmed for this session, but it was not " +
                    "saved because the HoloLens spatial anchor could not be persisted.");
                return;
            }
        }

        CityPlacementSaveData data = new CityPlacementSaveData
        {
            hasConfirmedPlacement = true,
            position = pose.position,
            rotation = pose.rotation,
            localScale = cityAnchorRoot.localScale,
            useOsmTerrain = useOsmTerrainTexture,
            usesSpatialAnchor = useSpatialAnchor,
            spatialAnchorName = useSpatialAnchor ? SpatialAnchorName : null
        };

        if (!persistence.Save(data))
        {
            if (useSpatialAnchor)
                await spatialAnchorPersistence.DeleteAsync(SpatialAnchorName);
            return;
        }

        ignoreSavedPlacementForSession = false;
        Debug.Log(
            $"CityPlacementManager: save succeeded at '{persistence.SaveFilePath}'. " +
            $"spatialAnchor={useSpatialAnchor}, position={data.position}, " +
            $"rotation={data.rotation}, localScale={data.localScale}.");

        if (!useSpatialAnchor)
        {
            Debug.LogWarning(
                "CityPlacementManager: running outside HoloLens; this Editor/platform save uses only " +
                "numeric coordinates. HoloLens deployments require the device-local OpenXR anchor.");
        }
    }

    private async void DeleteSpatialAnchorAsync()
    {
        if (spatialAnchorPersistence != null)
            await spatialAnchorPersistence.DeleteAsync(SpatialAnchorName);
    }

    private void FailSavedPlacementRestore(string reason)
    {
        Debug.LogWarning(
            $"CityPlacementManager: saved placement restore failed because {reason}. " +
            "The saved placement is being rejected so a new physical placement can be created.");

        pendingRestoreData = null;
        persistenceOperationVersion++;
        ignoreSavedPlacementForSession = true;
        persistence.Delete();
        DeleteSpatialAnchorAsync();

        hasPlacementStarted = false;
        hasConfirmed = false;
        ResetPlacementValues();
        cityAnchorManager?.ResetPlacementConfirmationState();

        cityAnchorRoot.SetPositionAndRotation(initialWorldPosition, initialWorldRotation);
        cityAnchorRoot.localScale = initialLocalScale;
        cityAnchorRoot.gameObject.SetActive(false);
        citySettingsController?.SetPlacementControlsEnabled(false);

        if (planeSelectionManager != null)
        {
            planeSelectionManager.enabled = true;
            planeSelectionManager.ReturnToInitialSelectionState();
        }

        citySettingsController?.RefreshPlacementState();
    }

    private void ResetPlacementValues()
    {
        basePlacementPosition = initialWorldPosition;
        basePlacementRotation = initialWorldRotation;
        currentScaleMultiplier = 1f;
        currentHeightOffset = 0f;
        currentPitchOffset = 0f;
        currentYawOffset = 0f;
    }

}
