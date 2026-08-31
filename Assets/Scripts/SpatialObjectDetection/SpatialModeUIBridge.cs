using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpatialModeUIBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpatialObjectDetectionManager detectionManager;
    [SerializeField] private SpatialBuildingObjectInterpreter buildingInterpreter;
    [SerializeField] private BuildingSelectionTechniqueController buildingSelectionTechniqueController;
    [SerializeField] private SpatialFloodObjectInterpreter floodInterpreter;
    [SerializeField] private SpatialObjectPreviewPresenter previewPresenter;
    [SerializeField] private MRNotification notifier;

    [Header("Optional Debug Roots")]
    [SerializeField] private bool switchDebugRootOnModeEnter = true;
    [SerializeField] private Transform buildingDebugObjectsRoot;
    [SerializeField] private Transform floodDebugObjectsRoot;

    private bool visualizationSuppressed;

    private void Awake()
    {
        ResolveMissingReferences();
    }

    public void EnterBuildingPlacingMode()
    {
        if (RejectWhileVisualizationIsActive()) return;

        ResolveMissingReferences();

        if (switchDebugRootOnModeEnter && buildingDebugObjectsRoot != null)
            detectionManager?.SetDebugObjectsRoot(buildingDebugObjectsRoot);

        detectionManager?.EnterBuildingPlacingMode();
        previewPresenter?.ClearAllPreviews();

        bool assistedLens =
            buildingSelectionTechniqueController != null &&
            buildingSelectionTechniqueController.CurrentTechnique ==
            BuildingSelectionTechnique.AssistedLens;

        if (!assistedLens)
        {
            notifier?.Show(
                buildingInterpreter != null &&
                buildingInterpreter.SingleCandidatePreviewEnabled
                    ? "Direct selection active."
                    : "Building selection active."
            );
        }
        else if (detectionManager != null &&
                 detectionManager.IsPreparingAutomaticBaseline)
        {
            notifier?.Show(
                "Preparing Assisted Lens. Keep the scan area clear."
            );
        }
        else if (detectionManager != null &&
                 detectionManager.IsCandidateScanReady)
        {
            notifier?.Show(
                "Hybrid selection ready."
            );
        }
        else
        {
            notifier?.Show(
                "Preparing Assisted Lens. Keep the scan area clear."
            );
        }
    }

    public void ShowAssistedLensPreparationMessage()
    {
        ResolveMissingReferences();

        notifier?.Show(
            "Preparing Assisted Lens. Keep the scan area clear."
        );
    }

    public void ShowAssistedLensBaselineFailureMessage()
    {
        ResolveMissingReferences();

        notifier?.Show(
            "Assisted Lens setup failed. Clear the scan area and retry."
        );
    }

    public void EnterFloodPlacingMode()
    {
        if (RejectWhileVisualizationIsActive()) return;

        ResolveMissingReferences();

        if (switchDebugRootOnModeEnter && floodDebugObjectsRoot != null)
            detectionManager?.SetDebugObjectsRoot(floodDebugObjectsRoot);

        detectionManager?.EnterFloodPlacingMode();
        previewPresenter?.ClearAllPreviews();

        notifier?.Show("Flood placement active.");
    }

    public void ScanCurrentMode()
    {
        if (RejectWhileVisualizationIsActive()) return;

        ResolveMissingReferences();

        if (detectionManager == null)
        {
            Warn("SpatialObjectDetectionManager is not assigned.");
            return;
        }

        if (detectionManager.IsPreparingAutomaticBaseline)
        {
            notifier?.Show("Preparing spatial baseline...");
            return;
        }

        if (detectionManager.CurrentMode ==
                SpatialPlacementMode.BuildingPlacing &&
            buildingSelectionTechniqueController != null &&
            buildingSelectionTechniqueController.CurrentTechnique ==
                BuildingSelectionTechnique.AssistedLens)
        {
            notifier?.Show("Refreshing Assisted Lens...");

            buildingSelectionTechniqueController
                .RequestImmediateLensUpdate();

            return;
        }

        notifier?.Show("Scanning spatial mesh...");

        detectionManager.ScanForObjectsOverTime(
            ShowScanPass,
            CompleteScan
        );
    }

    private void ShowScanPass(
        List<PhysicalObjectCandidate> candidates)
    {
        // Keep raw physical-object candidates hidden.
        // Mode-specific previews are created after interpretation.
        previewPresenter?.ClearCandidatePreviews();
    }

    private void CompleteScan(
        List<PhysicalObjectCandidate> candidates)
    {
        ShowScanPass(candidates);

        if (detectionManager == null)
            return;

        switch (detectionManager.CurrentMode)
        {
            case SpatialPlacementMode.BuildingPlacing:

                if (buildingSelectionTechniqueController != null &&
                    buildingSelectionTechniqueController.CurrentTechnique ==
                        BuildingSelectionTechnique.AssistedLens)
                {
                    buildingSelectionTechniqueController
                        .UpdateLensFromCandidates(candidates);
                }
                else
                {
                    buildingSelectionTechniqueController?
                        .RecordDirectCandidates(candidates);

                    buildingInterpreter?
                        .PreviewBuildingCandidates(candidates);
                }

                break;

            case SpatialPlacementMode.FloodPlacing:

                floodInterpreter?
                    .PreviewFloodCandidates(candidates);

                break;

            default:

                Warn(
                    "Choose Building Placing or Flood Placing before scanning."
                );

                return;
        }

        notifier?.Show(
            $"Scan complete: {candidates?.Count ?? 0} candidate(s)."
        );
    }

    public void CaptureSpatialBaseline()
    {
        if (RejectWhileVisualizationIsActive()) return;

        ResolveMissingReferences();

        if (detectionManager == null)
        {
            Warn("SpatialObjectDetectionManager is not assigned.");
            return;
        }

        bool captured =
            detectionManager.CaptureSpatialBaseline();

        notifier?.Show(
            captured
                ? "Baseline captured."
                : "Baseline capture failed."
        );
    }

    public void ConfirmCurrentMode()
    {
        if (RejectWhileVisualizationIsActive()) return;

        ResolveMissingReferences();

        if (detectionManager == null)
        {
            Warn("SpatialObjectDetectionManager is not assigned.");
            return;
        }

        bool confirmed = false;

        switch (detectionManager.CurrentMode)
        {
            case SpatialPlacementMode.BuildingPlacing:

                if (buildingSelectionTechniqueController != null &&
                    buildingSelectionTechniqueController.CurrentTechnique ==
                        BuildingSelectionTechnique.AssistedLens)
                {
                    confirmed =
                        buildingSelectionTechniqueController
                            .ConfirmAssistedSelection();
                }
                else
                {
                    confirmed =
                        buildingInterpreter != null &&
                        buildingInterpreter
                            .ConfirmBuildingCandidates();
                }

                break;

            case SpatialPlacementMode.FloodPlacing:

                confirmed =
                    floodInterpreter != null &&
                    floodInterpreter
                        .ConfirmFloodCandidates();

                break;

            default:

                Warn("No spatial placement mode is active.");
                break;
        }

        if (confirmed)
            detectionManager.ConfirmCurrentCandidates();
    }

    public void ClearCurrentMode()
    {
        if (RejectWhileVisualizationIsActive()) return;

        ClearCurrentMode(
            true,
            false,
            false
        );
    }

    private void ClearCurrentMode(
        bool showNotification,
        bool preserveConfirmedFloodSources,
        bool preserveConfirmedBuildingRoute)
    {
        ResolveMissingReferences();

        detectionManager?
            .ClearDetectedCandidates();

        buildingInterpreter?
            .ClearSelection();

        buildingSelectionTechniqueController?
            .ClearAssistedSelection(
                preserveConfirmedBuildingRoute
            );

        // Flood Part
        if (preserveConfirmedFloodSources)
        {
            floodInterpreter?
                .ClearFloodCandidatesOnly();
        }
        else
        {
            floodInterpreter?
                .ClearFloodCandidates();
        }

        previewPresenter?
            .ClearAllPreviews();

        if (showNotification)
            notifier?.Show("Selection cleared.");
    }

    public void CancelCurrentMode()
    {
        if (RejectWhileVisualizationIsActive()) return;

        ResolveMissingReferences();

        bool isFloodMode =
            detectionManager != null &&
            detectionManager.CurrentMode ==
                SpatialPlacementMode.FloodPlacing;

        bool isBuildingMode =
            detectionManager != null &&
            detectionManager.CurrentMode ==
                SpatialPlacementMode.BuildingPlacing;

        // Closing a placement panel discards only pending work.
        // Confirmed flood sources and a confirmed assisted-selection
        // route remain available to Visualization Mode.
        ClearCurrentMode(
            false,
            isFloodMode,
            isBuildingMode
        );

        detectionManager?
            .CancelCurrentMode();

        notifier?.Show(
            isFloodMode
                ? "Flood placement cancelled."
                : "Building selection closed."
        );
    }

    public void SetVisualizationSuppressed(
        bool suppressed)
    {
        visualizationSuppressed = suppressed;
    }

    private bool RejectWhileVisualizationIsActive()
    {
        if (!visualizationSuppressed)
            return false;

        Warn(
            "Tangible placement is unavailable while Visualization Mode is active."
        );

        return true;
    }

    private void ResolveMissingReferences()
    {
        detectionManager ??=
            FindFirstObjectByType<SpatialObjectDetectionManager>(
                FindObjectsInactive.Include
            );

        buildingInterpreter ??=
            FindFirstObjectByType<SpatialBuildingObjectInterpreter>(
                FindObjectsInactive.Include
            );

        buildingSelectionTechniqueController ??=
            FindFirstObjectByType<BuildingSelectionTechniqueController>(
                FindObjectsInactive.Include
            );

        floodInterpreter ??=
            FindFirstObjectByType<SpatialFloodObjectInterpreter>(
                FindObjectsInactive.Include
            );

        previewPresenter ??=
            FindFirstObjectByType<SpatialObjectPreviewPresenter>(
                FindObjectsInactive.Include
            );

        notifier ??=
            FindFirstObjectByType<MRNotification>(
                FindObjectsInactive.Include
            );
    }

    private void Warn(string message)
    {
        Debug.LogWarning(
            $"SpatialModeUIBridge: {message}"
        );

        notifier?.Show(message);
    }
}