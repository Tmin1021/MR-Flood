using Microsoft.MixedReality.Toolkit.UI.BoundsControl;
using UnityEngine;

[DefaultExecutionOrder(-990)]
[DisallowMultipleComponent]
public class CityAnchorManager : MonoBehaviour
{
    public static bool IsManualPlacementFlowActive => activeInstance != null;

    [Header("References")]
    [SerializeField] private Transform cityAnchorRoot;
    [SerializeField] private CityBootstrapper cityBootstrapper;
    [SerializeField] private CityManager cityManager;
    [SerializeField] private FloodManager floodManager;
    [SerializeField] private NavigationManager navigationManager;
    [SerializeField] private PointSelectManager pointSelectManager;
    [SerializeField] private SimpleGraphManager simpleGraphManager;
    [SerializeField] private RouteVisualizer routeVisualizer;
    [SerializeField] private PlaneSelectionManager planeSelectionManager;
    [SerializeField] private MainContentController citySettingsController;

    [Header("Interaction Gates")]
    [SerializeField] private GameObject[] disableUntilConfirmedObjects;
    [SerializeField] private MonoBehaviour[] disableUntilConfirmedBehaviours;

    [Header("Legacy Anchors")]
    [SerializeField] private bool disableLegacyAnchorBehavioursOnStartup = true;
    [SerializeField] private MonoBehaviour[] legacyAnchorBehavioursToDisable;

    public bool IsConfirmed { get; private set; }
    public Transform CityAnchorRoot => cityAnchorRoot;

    private static CityAnchorManager activeInstance;
    private bool confirmedInitializationComplete;

    private void Awake()
    {
        activeInstance = this;

        ResolveMissingReferences();
        PrepareStartupState();

        Debug.Log(
            $"CityAnchorManager: startup state. " +
            $"cityAssigned={cityAnchorRoot != null}, bootstrapperAssigned={cityBootstrapper != null}, " +
            $"floodAssigned={floodManager != null}, pointSelectAssigned={pointSelectManager != null}.");
    }

    private void OnDestroy()
    {
        if (activeInstance == this)
            activeInstance = null;
    }

    public bool ConfirmPlacement()
    {
        ResolveMissingReferences();

        if (IsConfirmed)
        {
            Debug.LogWarning("CityAnchorManager: placement is already confirmed.");
            return false;
        }

        if (cityAnchorRoot == null)
        {
            Debug.LogWarning("CityAnchorManager: CityAnchorRoot is not assigned.");
            return false;
        }

        if (cityBootstrapper == null)
        {
            Debug.LogWarning("CityAnchorManager: CityBootstrapper is not assigned.");
            return false;
        }

        IsConfirmed = true;
        return CompleteConfirmedPlacementInitialization();
    }

    /// <summary>
    /// Restores the confirmation gate during Awake without building world-space city data yet.
    /// CityPlacementManager completes initialization from its early Start after all Awake calls.
    /// </summary>
    public bool RestorePlacementConfirmationState()
    {
        ResolveMissingReferences();

        if (cityAnchorRoot == null)
        {
            Debug.LogWarning("CityAnchorManager: restored placement has no CityAnchorRoot.");
            return false;
        }

        if (cityBootstrapper == null)
        {
            Debug.LogWarning("CityAnchorManager: restored placement has no CityBootstrapper.");
            return false;
        }

        IsConfirmed = true;
        confirmedInitializationComplete = false;
        LockConfirmedPlacementControls();
        Debug.Log("CityAnchorManager: restored confirmation state; runtime initialization is deferred.");
        return true;
    }

    public bool CompleteRestoredPlacementInitialization()
    {
        if (!IsConfirmed)
        {
            Debug.LogWarning(
                "CityAnchorManager: cannot initialize a restored placement before confirmation state is restored.");
            return false;
        }

        return CompleteConfirmedPlacementInitialization();
    }

    public void ResetPlacementConfirmationState()
    {
        ResolveMissingReferences();

        IsConfirmed = false;
        confirmedInitializationComplete = false;

        UnlockCityTransformControls();

        if (floodManager != null)
            floodManager.autoUpdate = false;

        if (navigationManager != null)
            navigationManager.enabled = false;

        // Clear only the stale world-space line; route/navigation data is left intact.
        routeVisualizer?.ClearRoute();
        if (routeVisualizer != null)
            routeVisualizer.enabled = false;

        // Preserve current selection data while interaction is gated during replacement.
        if (pointSelectManager != null)
            pointSelectManager.enabled = false;

        SetGatedObjectsActive(false);
        SetGatedBehavioursEnabled(false);
        citySettingsController?.SetPlacementControlsEnabled(false);

        Debug.Log("CityAnchorManager: placement confirmation state cleared.");
    }

    private bool CompleteConfirmedPlacementInitialization()
    {
        if (confirmedInitializationComplete)
            return true;

        ResolveMissingReferences();

        if (cityAnchorRoot == null || cityBootstrapper == null)
        {
            Debug.LogWarning(
                "CityAnchorManager: confirmed placement initialization is missing the city root or bootstrapper.");
            return false;
        }

        LockConfirmedPlacementControls();
        SetGatedObjectsActive(true);
        SetGatedBehavioursEnabled(true);

        Debug.Log("CityAnchorManager: BuildCity called.");
        cityBootstrapper.BuildCity();

        if (simpleGraphManager != null)
        {
            simpleGraphManager.BuildGraphFromNeighbors();

            if (simpleGraphManager.flood != null)
                simpleGraphManager.UpdateFloodBlocking(simpleGraphManager.flood.position.y);
        }
        else
        {
            Debug.LogWarning("CityAnchorManager: SimpleGraphManager is not assigned.");
        }

        if (floodManager != null)
        {
            floodManager.UpdateFloodState();
            floodManager.autoUpdate = true;
        }
        else
        {
            Debug.LogWarning("CityAnchorManager: FloodManager is not assigned.");
        }

        if (navigationManager != null)
            navigationManager.enabled = true;
        else
            Debug.LogWarning("CityAnchorManager: NavigationManager is not assigned.");

        routeVisualizer?.ClearRoute();
        if (routeVisualizer != null)
            routeVisualizer.enabled = true;

        if (pointSelectManager != null)
        {
            pointSelectManager.RefreshAfterCityRebuild();
            pointSelectManager.enabled = true;
            pointSelectManager.ResetSelection();
        }
        else
        {
            Debug.LogWarning("CityAnchorManager: PointSelectManager is not assigned.");
        }

        confirmedInitializationComplete = true;
        Debug.Log("CityAnchorManager: placement confirmed, flood/navigation enabled, and plane detection disabled.");
        return true;
    }

    private void LockConfirmedPlacementControls()
    {
        LockCityTransformControls();
        citySettingsController?.SetPlacementControlsEnabled(false);
        planeSelectionManager?.DisableSelection();
        if (planeSelectionManager != null)
            planeSelectionManager.enabled = false;
    }

    private void PrepareStartupState()
    {
        if (cityBootstrapper != null)
            cityBootstrapper.buildOnStart = false;
        else
            Debug.LogWarning("CityAnchorManager: CityBootstrapper is missing. Auto-build cannot be disabled.");

        if (floodManager != null)
            floodManager.autoUpdate = false;
        else
            Debug.LogWarning("CityAnchorManager: FloodManager is missing. autoUpdate could not be disabled.");

        if (pointSelectManager != null)
        {
            pointSelectManager.ResetSelection();
            pointSelectManager.enabled = false;
        }
        else
        {
            Debug.LogWarning("CityAnchorManager: PointSelectManager is missing. Selection could not be disabled.");
        }

        if (navigationManager != null)
            navigationManager.enabled = false;
        else
            Debug.LogWarning("CityAnchorManager: NavigationManager is missing. Navigation could not be disabled.");

        routeVisualizer?.ClearRoute();
        if (routeVisualizer != null)
            routeVisualizer.enabled = false;

        SetGatedObjectsActive(false);
        SetGatedBehavioursEnabled(false);
        citySettingsController?.SetPlacementControlsEnabled(false);

        if (disableLegacyAnchorBehavioursOnStartup)
            DisableLegacyAnchorBehaviours();
    }

    private void DisableLegacyAnchorBehaviours()
    {
        DisableBehaviours(legacyAnchorBehavioursToDisable);
        DisableBehaviours(FindObjectsByType<CityModelAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        DisableBehaviours(FindObjectsByType<TrackedMarkerCityAnchorAdapter>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        // DisableBehaviours(FindObjectsByType<MRUKQRCodeCityAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None));
    }

    private void DisableBehaviours<T>(T[] behaviours) where T : Behaviour
    {
        if (behaviours == null)
            return;

        for (int i = 0; i < behaviours.Length; i++)
        {
            T behaviour = behaviours[i];
            if (behaviour == null || behaviour == this)
                continue;

            behaviour.enabled = false;
        }
    }

    private void LockCityTransformControls()
    {
        SetCityTransformControlsEnabled(false);
    }

    private void UnlockCityTransformControls()
    {
        SetCityTransformControlsEnabled(true);
    }

    private void SetCityTransformControlsEnabled(bool isEnabled)
    {
        if (cityAnchorRoot == null)
            return;

        BoundsControl[] boundsControls = cityAnchorRoot.GetComponentsInChildren<BoundsControl>(true);
        for (int i = 0; i < boundsControls.Length; i++)
        {
            BoundsControl boundsControl = boundsControls[i];
            if (boundsControl == null)
                continue;

            if (isEnabled)
            {
                boundsControl.enabled = true;
                boundsControl.Active = true;
            }
            else
            {
                boundsControl.Active = false;
                boundsControl.enabled = false;
            }

            Collider boundsCollider = boundsControl.GetComponent<Collider>();
            if (boundsCollider != null)
                boundsCollider.enabled = isEnabled;
        }
    }

    private void SetGatedObjectsActive(bool isActive)
    {
        if (disableUntilConfirmedObjects == null)
            return;

        for (int i = 0; i < disableUntilConfirmedObjects.Length; i++)
        {
            GameObject go = disableUntilConfirmedObjects[i];
            if (go != null)
                go.SetActive(isActive);
        }
    }

    private void SetGatedBehavioursEnabled(bool isEnabled)
    {
        if (disableUntilConfirmedBehaviours == null)
            return;

        for (int i = 0; i < disableUntilConfirmedBehaviours.Length; i++)
        {
            MonoBehaviour behaviour = disableUntilConfirmedBehaviours[i];
            if (behaviour != null)
                behaviour.enabled = isEnabled;
        }
    }

    private void ResolveMissingReferences()
    {
        cityBootstrapper ??= FindFirstObjectByType<CityBootstrapper>(FindObjectsInactive.Include);
        cityManager ??= FindFirstObjectByType<CityManager>(FindObjectsInactive.Include);
        floodManager ??= FindFirstObjectByType<FloodManager>(FindObjectsInactive.Include);
        navigationManager ??= FindFirstObjectByType<NavigationManager>(FindObjectsInactive.Include);
        pointSelectManager ??= FindFirstObjectByType<PointSelectManager>(FindObjectsInactive.Include);
        simpleGraphManager ??= FindFirstObjectByType<SimpleGraphManager>(FindObjectsInactive.Include);
        routeVisualizer ??= FindFirstObjectByType<RouteVisualizer>(FindObjectsInactive.Include);
        planeSelectionManager ??= FindFirstObjectByType<PlaneSelectionManager>(FindObjectsInactive.Include);
        citySettingsController ??= FindFirstObjectByType<MainContentController>(FindObjectsInactive.Include);

        if (cityAnchorRoot == null && pointSelectManager != null && pointSelectManager.cityRoot != null)
            cityAnchorRoot = pointSelectManager.cityRoot;

        if (cityAnchorRoot == null && cityBootstrapper != null)
        {
            Transform candidateRoot = null;

            if (cityBootstrapper.buildingsRoot != null)
                candidateRoot = cityBootstrapper.buildingsRoot.root;
            else if (cityBootstrapper.roadsRoot != null)
                candidateRoot = cityBootstrapper.roadsRoot.root;
            else if (cityBootstrapper.intersectionsRoot != null)
                candidateRoot = cityBootstrapper.intersectionsRoot.root;

            cityAnchorRoot = candidateRoot;
        }
    }
}
