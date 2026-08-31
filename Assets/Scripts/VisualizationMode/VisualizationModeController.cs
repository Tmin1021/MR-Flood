using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the transition between the authoritative tabletop presentation and an
/// independently manipulable, presentation-only city view.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class VisualizationModeController : MonoBehaviour
{
    private struct RendererState
    {
        public Renderer target;
        public bool enabled;
    }

    private struct ColliderState
    {
        public Collider target;
        public bool enabled;
    }

    private struct BehaviourState
    {
        public Behaviour target;
        public bool enabled;
    }

    private sealed class TabletopVisibilityState
    {
        private readonly List<RendererState> renderers = new List<RendererState>();
        private readonly List<ColliderState> colliders = new List<ColliderState>();
        private readonly List<BehaviourState> behaviours = new List<BehaviourState>();
        private CityFadeController fadeController;
        private bool fadeWasEnabled;
        private RouteWorldLabelPresenter routeLabels;
        private SpatialObjectPreviewPresenter spatialPreviews;
        private bool captured;

        public void CaptureAndHide(
            Transform canonicalRoot,
            CityFadeController fade,
            RouteWorldLabelPresenter labels,
            SpatialObjectPreviewPresenter previews)
        {
            if (captured || canonicalRoot == null)
                return;

            fadeController = fade;
            routeLabels = labels;
            spatialPreviews = previews;

            if (fadeController != null)
            {
                fadeWasEnabled = fadeController.enabled;
                fadeController.RestoreImmediately();
                fadeController.enabled = false;
            }

            Renderer[] foundRenderers = canonicalRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < foundRenderers.Length; i++)
            {
                Renderer renderer = foundRenderers[i];
                if (renderer == null)
                    continue;

                renderers.Add(new RendererState { target = renderer, enabled = renderer.enabled });
                renderer.enabled = false;
            }

            Collider[] foundColliders = canonicalRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < foundColliders.Length; i++)
            {
                Collider collider = foundColliders[i];
                if (collider == null)
                    continue;

                colliders.Add(new ColliderState { target = collider, enabled = collider.enabled });
                collider.enabled = false;
            }

            MonoBehaviour[] foundBehaviours = canonicalRoot.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < foundBehaviours.Length; i++)
            {
                MonoBehaviour behaviour = foundBehaviours[i];
                if (behaviour == null || !IsTabletopInputBehaviour(behaviour))
                    continue;

                if (behaviour is BuildingPoint buildingPoint)
                    buildingPoint.SetHoverHighlight(false);

                behaviours.Add(new BehaviourState { target = behaviour, enabled = behaviour.enabled });
                behaviour.enabled = false;
            }

            routeLabels?.SetPresentationVisible(false);
            spatialPreviews?.SetPresentationVisible(false);
            captured = true;
        }

        public void Restore()
        {
            if (!captured)
                return;

            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i].target != null)
                    renderers[i].target.enabled = renderers[i].enabled;
            }

            for (int i = 0; i < colliders.Count; i++)
            {
                if (colliders[i].target != null)
                    colliders[i].target.enabled = colliders[i].enabled;
            }

            for (int i = 0; i < behaviours.Count; i++)
            {
                if (behaviours[i].target != null)
                    behaviours[i].target.enabled = behaviours[i].enabled;
            }

            routeLabels?.SetPresentationVisible(true);
            spatialPreviews?.SetPresentationVisible(true);

            if (fadeController != null)
            {
                fadeController.enabled = fadeWasEnabled;
                fadeController.RestoreImmediately();
            }

            renderers.Clear();
            colliders.Clear();
            behaviours.Clear();
            captured = false;
        }

        private static bool IsTabletopInputBehaviour(MonoBehaviour behaviour)
        {
            if (behaviour is BuildingPoint)
                return true;

            string typeName = behaviour.GetType().Name;
            return typeName == "NearInteractionTouchableVolume" ||
                typeName == "NearInteractionGrabbable" ||
                typeName == "ObjectManipulator";
        }
    }

    [Header("Canonical References")]
    [SerializeField] private CityAnchorManager cityAnchorManager;
    [SerializeField] private CityBootstrapper cityBootstrapper;
    [SerializeField] private CityManager cityManager;
    [SerializeField] private FloodManager floodManager;
    [SerializeField] private PointSelectManager pointSelectManager;
    [SerializeField] private SpatialObjectDetectionManager spatialDetectionManager;
    [SerializeField] private SpatialModeUIBridge spatialModeUIBridge;
    [SerializeField] private RouteWorldLabelPresenter routeWorldLabelPresenter;
    [SerializeField] private SpatialObjectPreviewPresenter spatialPreviewPresenter;
    [SerializeField] private CityFadeController cityFadeController;
    [SerializeField] private MRNotification notifier;

    [Header("Synchronization")]
    [SerializeField, Min(0.02f)] private float continuousStateSyncInterval = 0.1f;
    [SerializeField] private bool enableEditorKeyboardShortcut = true;
    [SerializeField] private KeyCode editorToggleKey = KeyCode.F8;

    private readonly TabletopVisibilityState tabletopVisibility = new TabletopVisibilityState();
    private VisualizationCityView visualizationView;
    private CityVisualizationSnapshot latestRouteSnapshot;
    private bool stateDirty;
    private bool subscribed;
    private float nextStateSyncTime;
    private Vector3 lastVisualizationPosition;
    private Quaternion lastVisualizationRotation;
    private Vector3 lastVisualizationScale;

    public static VisualizationModeController Instance { get; private set; }
    public bool IsVisualizationModeActive { get; private set; }
    public Transform VisualizationRoot => visualizationView != null ? visualizationView.transform : null;

    public event Action VisualizationModeEntering;
    public event Action VisualizationModeEntered;
    public event Action VisualizationModeExiting;
    public event Action VisualizationModeExited;
    public event Action<Transform> VisualizationTransformChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeController()
    {
        if (FindFirstObjectByType<VisualizationModeController>(FindObjectsInactive.Include) != null)
            return;

        CityAnchorManager anchor = FindFirstObjectByType<CityAnchorManager>(FindObjectsInactive.Include);
        if (anchor == null)
            return;

        GameObject controllerObject = new GameObject("Visualization Mode System");
        controllerObject.AddComponent<VisualizationModeController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveReferences();
        Subscribe();
    }

    private void Update()
    {
#if UNITY_EDITOR
        if (enableEditorKeyboardShortcut && Input.GetKeyDown(editorToggleKey))
            ToggleVisualizationMode();
#endif

        if (!IsVisualizationModeActive)
            return;

        if (cityAnchorManager == null || !cityAnchorManager.IsConfirmed)
        {
            ExitVisualizationMode();
            return;
        }

        if (Time.unscaledTime >= nextStateSyncTime)
        {
            visualizationView.RefreshSourceVisibility();

            if (stateDirty)
            {
                visualizationView.RefreshCityState();
                visualizationView.RefreshFloodSources();
                stateDirty = false;
            }

            nextStateSyncTime = Time.unscaledTime + continuousStateSyncInterval;
        }

        PublishTransformChangeIfNeeded();
    }

    public bool EnterVisualizationMode()
    {
        if (IsVisualizationModeActive)
            return true;

        ResolveReferences();
        Subscribe();

        if (cityAnchorManager == null || !cityAnchorManager.IsConfirmed)
            return Fail("Confirm the tabletop city before entering Visualization Mode.");

        if (cityBootstrapper == null || !cityBootstrapper.HasBuiltCity)
            return Fail("The canonical city has not been built yet.");

        if (cityManager == null || cityAnchorManager.CityAnchorRoot == null)
            return Fail("Visualization Mode is missing the canonical city references.");

        VisualizationModeEntering?.Invoke();

        // The presentation copy must capture the normal material state, not a
        // transient near-focus fade that happened to be active on entry.
        cityFadeController?.RestoreImmediately();

        if (!EnsureVisualizationView())
            return Fail("Could not construct the presentation-only city view.");

        latestRouteSnapshot = pointSelectManager != null
            ? pointSelectManager.CaptureVisualizationSnapshot()
            : null;

        visualizationView.AlignWithCanonical();
        visualizationView.RefreshAll(latestRouteSnapshot);
        spatialDetectionManager?.SuspendForVisualization(visualizationView.transform);
        spatialModeUIBridge?.SetVisualizationSuppressed(true);
        pointSelectManager?.SetTabletopPresentationSuppressed(true);

        tabletopVisibility.CaptureAndHide(
            cityAnchorManager.CityAnchorRoot,
            cityFadeController,
            routeWorldLabelPresenter,
            spatialPreviewPresenter);

        visualizationView.gameObject.SetActive(true);
        visualizationView.SetManipulationEnabled(true);
        IsVisualizationModeActive = true;
        stateDirty = false;
        nextStateSyncTime = Time.unscaledTime + continuousStateSyncInterval;
        CacheVisualizationTransform();
        notifier?.Show("Visualization Mode enabled. Move, rotate, or enlarge the city.");
        VisualizationModeEntered?.Invoke();
        return true;
    }

    public void ExitVisualizationMode()
    {
        if (!IsVisualizationModeActive)
            return;

        VisualizationModeExiting?.Invoke();

        if (visualizationView != null)
        {
            visualizationView.SetManipulationEnabled(false);
            visualizationView.gameObject.SetActive(false);
        }

        tabletopVisibility.Restore();
        pointSelectManager?.SetTabletopPresentationSuppressed(false);
        spatialDetectionManager?.ResumeAfterVisualization(
            visualizationView != null ? visualizationView.transform : null);
        spatialModeUIBridge?.SetVisualizationSuppressed(false);

        IsVisualizationModeActive = false;
        notifier?.Show("Visualization Mode disabled. Tabletop interaction restored.");
        VisualizationModeExited?.Invoke();
    }

    public void ToggleVisualizationMode()
    {
        if (IsVisualizationModeActive)
            ExitVisualizationMode();
        else
            EnterVisualizationMode();
    }

    public void ResetVisualizationTransform()
    {
        if (visualizationView == null)
            return;

        visualizationView.AlignWithCanonical();
        CacheVisualizationTransform();
        VisualizationTransformChanged?.Invoke(visualizationView.transform);
    }

    public void SetVisualizationScale(float multiplier)
    {
        if (visualizationView == null || cityAnchorManager == null || cityAnchorManager.CityAnchorRoot == null)
            return;

        float value = Mathf.Clamp(multiplier, 0.5f, 12f);
        visualizationView.transform.localScale = Abs(cityAnchorManager.CityAnchorRoot.lossyScale) * value;
    }

    private bool EnsureVisualizationView()
    {
        if (visualizationView != null && visualizationView.IsInitialized)
            return true;

        GameObject root = new GameObject("Visualization City Root");
        root.SetActive(false);
        visualizationView = root.AddComponent<VisualizationCityView>();

        if (!visualizationView.Initialize(cityAnchorManager.CityAnchorRoot, cityManager, floodManager))
        {
            Destroy(root);
            visualizationView = null;
            return false;
        }

        return true;
    }

    private void ResolveReferences()
    {
        cityAnchorManager ??= FindFirstObjectByType<CityAnchorManager>(FindObjectsInactive.Include);
        cityBootstrapper ??= FindFirstObjectByType<CityBootstrapper>(FindObjectsInactive.Include);
        cityManager ??= FindFirstObjectByType<CityManager>(FindObjectsInactive.Include);
        floodManager ??= FindFirstObjectByType<FloodManager>(FindObjectsInactive.Include);
        pointSelectManager ??= FindFirstObjectByType<PointSelectManager>(FindObjectsInactive.Include);
        spatialDetectionManager ??= FindFirstObjectByType<SpatialObjectDetectionManager>(FindObjectsInactive.Include);
        spatialModeUIBridge ??= FindFirstObjectByType<SpatialModeUIBridge>(FindObjectsInactive.Include);
        routeWorldLabelPresenter ??= FindFirstObjectByType<RouteWorldLabelPresenter>(FindObjectsInactive.Include);
        spatialPreviewPresenter ??= FindFirstObjectByType<SpatialObjectPreviewPresenter>(FindObjectsInactive.Include);
        cityFadeController ??= FindFirstObjectByType<CityFadeController>(FindObjectsInactive.Include);
        notifier ??= FindFirstObjectByType<MRNotification>(FindObjectsInactive.Include);
    }

    private void Subscribe()
    {
        if (subscribed)
            return;

        ResolveReferences();
        if (cityBootstrapper != null)
            cityBootstrapper.CityBuilt += HandleCityBuilt;
        if (floodManager != null)
        {
            floodManager.FloodStateChanged += HandleFloodChanged;
            floodManager.FloodSourcesChanged += HandleFloodChanged;
        }
        if (pointSelectManager != null)
            pointSelectManager.VisualizationStateChanged += HandleRouteChanged;

        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (cityBootstrapper != null)
            cityBootstrapper.CityBuilt -= HandleCityBuilt;
        if (floodManager != null)
        {
            floodManager.FloodStateChanged -= HandleFloodChanged;
            floodManager.FloodSourcesChanged -= HandleFloodChanged;
        }
        if (pointSelectManager != null)
            pointSelectManager.VisualizationStateChanged -= HandleRouteChanged;

        subscribed = false;
    }

    private void HandleCityBuilt(int revision)
    {
        stateDirty = true;
    }

    private void HandleFloodChanged(int revision)
    {
        pointSelectManager?.ValidateCurrentRouteAgainstFlood();
        stateDirty = true;
    }

    private void HandleRouteChanged(CityVisualizationSnapshot snapshot)
    {
        latestRouteSnapshot = snapshot;
        if (IsVisualizationModeActive && visualizationView != null)
            visualizationView.ApplyRouteSnapshot(snapshot);
    }

    private void CacheVisualizationTransform()
    {
        if (visualizationView == null)
            return;

        Transform target = visualizationView.transform;
        lastVisualizationPosition = target.position;
        lastVisualizationRotation = target.rotation;
        lastVisualizationScale = target.localScale;
    }

    private void PublishTransformChangeIfNeeded()
    {
        if (visualizationView == null)
            return;

        Transform target = visualizationView.transform;
        bool changed = (target.position - lastVisualizationPosition).sqrMagnitude > 0.000001f ||
            Quaternion.Angle(target.rotation, lastVisualizationRotation) > 0.01f ||
            (target.localScale - lastVisualizationScale).sqrMagnitude > 0.000001f;

        if (!changed)
            return;

        CacheVisualizationTransform();
        VisualizationTransformChanged?.Invoke(target);
    }

    private bool Fail(string message)
    {
        Debug.LogWarning("VisualizationModeController: " + message);
        notifier?.Show(message);
        return false;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private void OnDestroy()
    {
        if (IsVisualizationModeActive)
            ExitVisualizationMode();

        Unsubscribe();
        if (Instance == this)
            Instance = null;
    }
}
