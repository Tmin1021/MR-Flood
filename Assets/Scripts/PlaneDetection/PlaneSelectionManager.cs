using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Physics;
using Microsoft.MixedReality.Toolkit.SpatialAwareness;
using Microsoft.MixedReality.Toolkit.Utilities;
using UnityEngine;

[DisallowMultipleComponent]
public class PlaneSelectionManager : MonoBehaviour, IMixedRealityPointerHandler
{
    [Header("References")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("Optional editor-only fallback when no simulated hand ray is available.")]
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private CityPlacementManager cityPlacementManager;

    [Header("Spatial Awareness")]
    [SerializeField] private bool startSelectionEnabled = false;
    [SerializeField] private bool useMrtkSpatialAwareness = true;
    [SerializeField] private bool followHandRayContinuously = true;
    [SerializeField] private bool resumeSpatialObserverOnEnable = true;
    [SerializeField] private bool suspendSpatialObserverAfterSelection = false;
    [SerializeField] private SpatialAwarenessMeshDisplayOptions meshDisplayWhileScanning =
        SpatialAwarenessMeshDisplayOptions.Occlusion;
    [SerializeField] private SpatialAwarenessMeshDisplayOptions meshDisplayAfterSelection =
        SpatialAwarenessMeshDisplayOptions.None;

    [Header("Selection")]
    [SerializeField] private Handedness preferredHandedness = Handedness.Right;
    [SerializeField] private bool allowEitherHand = true;
    [SerializeField] private bool useCameraFallbackInEditor = true;
    [SerializeField] private LayerMask fallbackPlacementLayers = Physics.DefaultRaycastLayers;
    [SerializeField] private float maxRayDistance = 10f;
    [SerializeField] private bool allowPrimaryInputSelection = true;
    [SerializeField] private bool requireHorizontalSurface = true;
    [SerializeField] [Range(0f, 1f)] private float minimumSurfaceUpDot = 0.75f;
    [SerializeField] private bool requireSurfaceBelowHead = false;
    [SerializeField] private float minimumVerticalDropBelowHead = 0.1f;

    [Header("Visual Prompts")]
    [SerializeField] private GameObject[] scanningVisuals;
    [SerializeField] private GameObject[] confirmationVisuals;
    [SerializeField] private Transform candidateMarker;
    [SerializeField] private float candidateMarkerVerticalOffset = 0.01f;

    public bool IsSelectionEnabled => selectionEnabled;
    public bool HasSelectedSurface => hasSelectedSurface;
    public bool HasCandidatePose => hasCandidatePose;
    public Pose CandidatePose => candidatePose;

    private bool selectionEnabled;
    private bool hasSelectedSurface;
    private bool hasCandidatePose;
    private bool warnedAboutMissingSpatialObserver;
    private bool pointerHandlerRegistered;
    private int selectionEnabledFrame = -1;
    private Pose candidatePose;
    private IMixedRealityPointer activeHandPointer;
    private IMixedRealitySpatialAwarenessMeshObserver spatialMeshObserver;

    private void Awake()
    {
        ResolveMissingReferences();
        DisableCandidateMarkerColliders();
        bool hasSavedPlacement =
            cityPlacementManager != null && cityPlacementManager.HasSavedPlacement;
        selectionEnabled = startSelectionEnabled && !hasSavedPlacement;
        RefreshSpatialObserverStateForSelection(selectionEnabled);
        SetScanningVisualsActive(selectionEnabled);
        SetConfirmationVisualsActive(false);
        SetCandidateMarkerActive(false);

        Debug.Log(
            $"PlaneSelectionManager: startup state. " +
            $"selectionEnabled={selectionEnabled}, hasSelectedSurface={hasSelectedSurface}, " +
            $"useMrtkSpatialAwareness={useMrtkSpatialAwareness}.");
    }

    private void OnEnable()
    {
        RegisterPointerHandler();
    }

    private void Start()
    {
        RegisterPointerHandler();
    }

    private void OnDisable()
    {
        UnregisterPointerHandler();
    }

    private void Update()
    {
        if (!selectionEnabled || hasSelectedSurface)
            return;

        if (followHandRayContinuously)
            UpdateCandidateFromHandRay();

#if UNITY_EDITOR
        if (allowPrimaryInputSelection && Input.GetMouseButtonDown(0))
            TryConfirmCurrentHandRayPose();
#endif
    }

    public void EnableSelection()
    {
        ResolveMissingReferences();

        if (cityPlacementManager != null && cityPlacementManager.HasSavedPlacement)
        {
            Debug.LogWarning(
                "PlaneSelectionManager: selection cannot restart until the saved city placement is reset.");
            return;
        }

        selectionEnabled = true;
        hasSelectedSurface = false;
        hasCandidatePose = false;
        selectionEnabledFrame = Time.frameCount;
        SetCandidateMarkerActive(false);
        SetConfirmationVisualsActive(false);
        RefreshSpatialObserverStateForSelection(true);
        SetScanningVisualsActive(true);

        Debug.Log("PlaneSelectionManager: hand-ray surface selection enabled.");
    }

    public void DisableSelection()
    {
        selectionEnabled = false;
        hasCandidatePose = false;
        RefreshSpatialObserverStateForSelection(false);
        SetScanningVisualsActive(false);
        SetConfirmationVisualsActive(false);
        SetCandidateMarkerActive(false);

        Debug.Log("PlaneSelectionManager: plane detection mode disabled.");
    }

    public void ResetSelection()
    {
        hasSelectedSurface = false;
        hasCandidatePose = false;
        SetCandidateMarkerActive(false);
        SetConfirmationVisualsActive(false);
    }

    public void ReturnToInitialSelectionState()
    {
        ResetSelection();

        if (startSelectionEnabled)
            EnableSelection();
        else
            DisableSelection();
    }

    public void ConfirmCurrentCandidate()
    {
        if (!hasCandidatePose)
        {
            Debug.LogWarning("PlaneSelectionManager: there is no candidate surface to confirm.");
            return;
        }

        ResolveMissingReferences();

        if (cityPlacementManager == null)
        {
            Debug.LogWarning("PlaneSelectionManager: CityPlacementManager is not assigned.");
            return;
        }

        Debug.Log(
            $"PlaneSelectionManager: plane selected at position {candidatePose.position} " +
            $"with rotation {candidatePose.rotation.eulerAngles}.");

        if (!cityPlacementManager.BeginPlacement(candidatePose))
            return;

        hasSelectedSurface = true;
        hasCandidatePose = false;
        RefreshSpatialObserverStateForSelection(false);
        DisableSelection();
    }

    public void RejectCurrentCandidate()
    {
        hasSelectedSurface = false;
        hasCandidatePose = false;
        SetCandidateMarkerActive(false);
        SetConfirmationVisualsActive(false);
        SetScanningVisualsActive(selectionEnabled);

        Debug.Log("PlaneSelectionManager: current surface cleared.");
    }

    public void TrySelectCurrentSurface()
    {
        UpdateCandidateFromHandRay();
    }

    private void UpdateCandidateFromHandRay()
    {
        if (!selectionEnabled)
            return;

        ResolveMissingReferences();
        RefreshSpatialObserverStateForSelection(true);

        if (TryGetActiveHandPointer(out IMixedRealityPointer pointer))
        {
            if (TryRaycastHandPointer(pointer, out RaycastHit handHit, out Vector3 handRayDirection) &&
                IsHitValidForCandidate(handHit))
            {
                ShowCandidate(BuildPoseFromHit(handHit, handRayDirection));
            }
            else
            {
                ClearCandidate();
            }

            return;
        }

#if UNITY_EDITOR
        if (useCameraFallbackInEditor &&
            TryCreateEditorFallbackRay(out Ray fallbackRay) &&
            TryRaycastSurface(fallbackRay, maxRayDistance, out RaycastHit fallbackHit) &&
            IsHitValidForCandidate(fallbackHit))
        {
            ShowCandidate(BuildPoseFromHit(fallbackHit, fallbackRay.direction));
            return;
        }
#endif

        ClearCandidate();
    }

    private bool TryGetActiveHandPointer(out IMixedRealityPointer pointer)
    {
        if (IsUsableHandPointer(activeHandPointer))
        {
            pointer = activeHandPointer;
            return true;
        }

        if (TryFindHandPointer(preferredHandedness, out pointer))
        {
            activeHandPointer = pointer;
            return true;
        }

        if (allowEitherHand &&
            TryFindHandPointer(Handedness.Any, out pointer))
        {
            activeHandPointer = pointer;
            return true;
        }

        activeHandPointer = null;
        pointer = null;
        return false;
    }

    private static bool TryFindHandPointer(
        Handedness handedness,
        out IMixedRealityPointer pointer)
    {
        foreach (LinePointer candidate in
                 PointerUtils.GetPointers<LinePointer>(handedness, InputSourceType.Hand))
        {
            if (!IsUsableHandPointer(candidate))
                continue;

            pointer = candidate;
            return true;
        }

        pointer = null;
        return false;
    }

    private static bool IsUsableHandPointer(IMixedRealityPointer pointer)
    {
        return pointer != null &&
               pointer.IsActive &&
               pointer.IsInteractionEnabled &&
               pointer.InputSourceParent != null &&
               pointer.InputSourceParent.SourceType == InputSourceType.Hand &&
               pointer.Rays != null &&
               pointer.Rays.Length > 0;
    }

    private bool TryRaycastHandPointer(
        IMixedRealityPointer pointer,
        out RaycastHit hit,
        out Vector3 hitRayDirection)
    {
        float remainingDistance = maxRayDistance;
        RayStep[] raySteps = pointer.Rays;

        for (int i = 0; i < raySteps.Length && remainingDistance > 0f; i++)
        {
            RayStep step = raySteps[i];
            if (step.Direction.sqrMagnitude <= 0.0001f)
                continue;

            float stepDistance = Mathf.Min(step.Length, remainingDistance);
            if (stepDistance <= 0f)
                continue;

            Ray ray = new Ray(step.Origin, step.Direction);
            if (TryRaycastSurface(ray, stepDistance, out hit))
            {
                hitRayDirection = step.Direction;
                return true;
            }

            remainingDistance -= stepDistance;
        }

        hit = default;
        hitRayDirection = Vector3.forward;
        return false;
    }

    private bool TryCreateEditorFallbackRay(out Ray ray)
    {
        Transform origin = rayOrigin;
        Camera cameraToUse = targetCamera != null ? targetCamera : Camera.main;

        if (origin != null)
        {
            ray = new Ray(origin.position, origin.forward);
            return true;
        }

        if (cameraToUse != null)
        {
            ray = cameraToUse.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return true;
        }

        ray = default;
        return false;
    }

    private bool TryRaycastSurface(Ray ray, float rayDistance, out RaycastHit hit)
    {
        if (useMrtkSpatialAwareness)
        {
            IMixedRealitySpatialAwarenessMeshObserver observer = GetSpatialMeshObserver();
            if (observer != null)
            {
                int layerMask = observer.MeshPhysicsLayerMask;
                if (((int)fallbackPlacementLayers) != 0)
                    layerMask |= fallbackPlacementLayers.value;

                if (Physics.Raycast(ray, out hit, rayDistance, layerMask, QueryTriggerInteraction.Ignore))
                    return true;
            }
            else if (!warnedAboutMissingSpatialObserver)
            {
                warnedAboutMissingSpatialObserver = true;
                Debug.LogWarning(
                    "PlaneSelectionManager: MRTK spatial awareness mesh observer was not found. " +
                    "Falling back to normal collider raycasts.");
            }
        }

        return Physics.Raycast(
            ray,
            out hit,
            rayDistance,
            fallbackPlacementLayers,
            QueryTriggerInteraction.Ignore);
    }

    private bool IsHitValidForCandidate(RaycastHit hit)
    {
        if (requireHorizontalSurface)
        {
            float upDot = Vector3.Dot(hit.normal.normalized, Vector3.up);
            if (upDot < minimumSurfaceUpDot)
                return false;
        }

        if (requireSurfaceBelowHead)
        {
            Vector3 headPosition = GetHeadPosition();
            float verticalDrop = headPosition.y - hit.point.y;
            if (verticalDrop < minimumVerticalDropBelowHead)
                return false;
        }

        return true;
    }

    private Pose BuildPoseFromHit(RaycastHit hit, Vector3 rayDirection)
    {
        Vector3 forward = Vector3.ProjectOnPlane(GetReferenceForward(rayDirection), Vector3.up);

        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;

        Quaternion rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        return new Pose(hit.point, rotation);
    }

    private Vector3 GetReferenceForward(Vector3 fallbackDirection)
    {
        Camera cameraToUse = targetCamera != null ? targetCamera : Camera.main;

        if (cameraToUse != null)
            return cameraToUse.transform.forward;

        if (rayOrigin != null)
            return rayOrigin.forward;

        return fallbackDirection;
    }

    private void ResolveMissingReferences()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        cityPlacementManager ??=
            FindFirstObjectByType<CityPlacementManager>(FindObjectsInactive.Include);

        if (useMrtkSpatialAwareness && spatialMeshObserver == null)
            spatialMeshObserver = CoreServices.GetSpatialAwarenessSystemDataProvider<IMixedRealitySpatialAwarenessMeshObserver>();
    }

    private void ShowCandidate(Pose pose)
    {
        candidatePose = pose;
        hasCandidatePose = true;

        SetScanningVisualsActive(true);
        SetConfirmationVisualsActive(false);
        SetCandidateMarkerPose(pose);
    }

    private void ClearCandidate()
    {
        if (!hasCandidatePose && (candidateMarker == null || !candidateMarker.gameObject.activeSelf))
            return;

        hasCandidatePose = false;
        SetCandidateMarkerActive(false);
        SetConfirmationVisualsActive(false);
    }

    private void SetScanningVisualsActive(bool visible)
    {
        if (scanningVisuals == null)
            return;

        for (int i = 0; i < scanningVisuals.Length; i++)
        {
            if (scanningVisuals[i] != null)
                scanningVisuals[i].SetActive(visible);
        }
    }

    private void SetConfirmationVisualsActive(bool visible)
    {
        if (confirmationVisuals == null)
            return;

        for (int i = 0; i < confirmationVisuals.Length; i++)
        {
            if (confirmationVisuals[i] != null)
                confirmationVisuals[i].SetActive(visible);
        }
    }

    private void SetCandidateMarkerPose(Pose pose)
    {
        if (candidateMarker == null)
            return;

        candidateMarker.SetPositionAndRotation(
            pose.position + Vector3.up * candidateMarkerVerticalOffset,
            pose.rotation);
        candidateMarker.gameObject.SetActive(true);
    }

    private void SetCandidateMarkerActive(bool visible)
    {
        if (candidateMarker != null)
            candidateMarker.gameObject.SetActive(visible);
    }

    private void DisableCandidateMarkerColliders()
    {
        if (candidateMarker == null)
            return;

        Collider[] colliders = candidateMarker.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

    private IMixedRealitySpatialAwarenessMeshObserver GetSpatialMeshObserver()
    {
        if (!useMrtkSpatialAwareness)
            return null;

        if (spatialMeshObserver == null)
            spatialMeshObserver = CoreServices.GetSpatialAwarenessSystemDataProvider<IMixedRealitySpatialAwarenessMeshObserver>();

        return spatialMeshObserver;
    }

    private void RefreshSpatialObserverStateForSelection(bool isSelecting)
    {
        IMixedRealitySpatialAwarenessMeshObserver observer = GetSpatialMeshObserver();
        if (observer == null)
            return;

        if (isSelecting)
        {
            if (resumeSpatialObserverOnEnable)
                observer.Resume();

            observer.DisplayOption = meshDisplayWhileScanning;
        }
        else
        {
            observer.DisplayOption = meshDisplayAfterSelection;

            if (suspendSpatialObserverAfterSelection)
                observer.Suspend();
        }
    }

    private Vector3 GetHeadPosition()
    {
        Camera cameraToUse = targetCamera != null ? targetCamera : Camera.main;
        if (cameraToUse != null)
            return cameraToUse.transform.position;

        if (rayOrigin != null)
            return rayOrigin.position;

        return Vector3.zero;
    }

    private void TryConfirmCurrentHandRayPose(IMixedRealityPointer confirmingPointer = null)
    {
        if (!allowPrimaryInputSelection || !selectionEnabled || hasSelectedSurface)
            return;

        if (Time.frameCount <= selectionEnabledFrame)
            return;

        if (IsUsableHandPointer(confirmingPointer))
            activeHandPointer = confirmingPointer;

        UpdateCandidateFromHandRay();
        if (hasCandidatePose)
            ConfirmCurrentCandidate();
    }

    private void RegisterPointerHandler()
    {
        if (pointerHandlerRegistered || CoreServices.InputSystem == null)
            return;

        CoreServices.InputSystem.RegisterHandler<IMixedRealityPointerHandler>(this);
        pointerHandlerRegistered = true;
    }

    private void UnregisterPointerHandler()
    {
        if (!pointerHandlerRegistered)
            return;

        if (CoreServices.InputSystem != null)
            CoreServices.InputSystem.UnregisterHandler<IMixedRealityPointerHandler>(this);

        pointerHandlerRegistered = false;
    }

    public void OnPointerClicked(MixedRealityPointerEventData eventData)
    {
        TryConfirmCurrentHandRayPose(eventData.Pointer);
    }

    public void OnPointerDown(MixedRealityPointerEventData eventData)
    {
        if (IsUsableHandPointer(eventData.Pointer))
            activeHandPointer = eventData.Pointer;
    }

    public void OnPointerDragged(MixedRealityPointerEventData eventData)
    {
    }

    public void OnPointerUp(MixedRealityPointerEventData eventData)
    {
    }
}
