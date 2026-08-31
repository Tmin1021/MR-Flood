using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpatialBuildingObjectInterpreter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CityManager cityManager;
    [SerializeField] private FloodManager floodManager;
    [SerializeField] private SpatialObjectPreviewPresenter previewPresenter;
    [SerializeField] private MRNotification notifier;
    [SerializeField] private PointSelectManager pointSelectManager;
    [SerializeField] private BuildingSelectionTechniqueController selectionTechniqueController;
    [SerializeField] private SpatialObjectDetectionManager detectionManager;

    [Header("Matching")]
    [SerializeField] private float maxBuildingMatchDistance = 0.25f;
    [SerializeField] private bool useTwoClosestCandidatesWhenMoreThanTwo = false;

    public CityBuilding startBuildingCandidate { get; private set; }
    public CityBuilding destinationBuildingCandidate { get; private set; }
    public CityBuilding TargetBuildingCandidate { get; private set; }
    public bool SingleCandidatePreviewEnabled { get; private set; }

    private readonly List<PhysicalObjectCandidate> workingCandidates = new List<PhysicalObjectCandidate>();

    private void Awake()
    {
        ResolveMissingReferences();
    }

    public bool PreviewBuildingCandidates(List<PhysicalObjectCandidate> candidates)
    {
        ResolveMissingReferences();
        ClearSelection();

        if (selectionTechniqueController != null &&
            selectionTechniqueController.CurrentTechnique == BuildingSelectionTechnique.AssistedLens)
        {
            Debug.Log("SpatialBuildingObjectInterpreter: Assisted Lens candidate bypassed nearest-building interpretation.");
            return false;
        }

        if (cityManager == null)
        {
            Warn("SpatialBuildingObjectInterpreter: CityManager is not assigned.");
            return false;
        }

        workingCandidates.Clear();
        AddValidCandidates(candidates, workingCandidates);

        if (SingleCandidatePreviewEnabled && workingCandidates.Count == 1)
            return PreviewSingleBuildingCandidate(workingCandidates[0]);

        if (!TryReduceToTwoCandidates(workingCandidates))
            return false;

        if (!TryMatchCandidate(workingCandidates[0], out CityBuilding start))
            return false;

        if (!TryMatchCandidate(workingCandidates[1], out CityBuilding destination))
            return false;

        if (start == destination)
        {
            Warn("Both physical objects matched the same building. Move them farther apart.");
            return false;
        }

        startBuildingCandidate = start;
        destinationBuildingCandidate = destination;

        previewPresenter?.ShowBuildingCandidates(startBuildingCandidate, destinationBuildingCandidate);
        detectionManager?.SetTwoBuildingSelectionCandidatesAvailable(true);
        notifier?.Show($"Building candidates: {GetBuildingName(start)} to {GetBuildingName(destination)}");
        return true;
    }

    /// <summary>
    /// Enables the single-target Direct variant used by a dedicated UI action.
    /// The normal Direct route flow remains a two-candidate interaction.
    /// </summary>
    public void SetSingleCandidatePreviewEnabled(bool enabled)
    {
        if (SingleCandidatePreviewEnabled == enabled)
            return;

        SingleCandidatePreviewEnabled = enabled;
        ClearSelection();
    }

    public bool ConfirmBuildingCandidates()
    {
        ResolveMissingReferences();
        if (selectionTechniqueController != null &&
            selectionTechniqueController.CurrentTechnique == BuildingSelectionTechnique.AssistedLens)
        {
            Warn("Assisted Lens buildings must be selected by hand before confirmation.");
            return false;
        }

        if (startBuildingCandidate == null || destinationBuildingCandidate == null)
        {
            Warn(SingleCandidatePreviewEnabled && TargetBuildingCandidate != null
                ? "The target building is previewed. Route confirmation still requires two building candidates."
                : "Select two valid building candidates before confirming.");
            return false;
        }

        ResolveMissingReferences();

        if (pointSelectManager == null)
        {
            Warn("PointSelectManager is not assigned.");
            return false;
        }

        if (!pointSelectManager.BuildPathBetweenBuildings(
                startBuildingCandidate,
                destinationBuildingCandidate))
        {
            Warn("Could not generate a route between the selected buildings.");
            return false;
        }

        notifier?.Show("Route generated from detected buildings.");
        Debug.Log(
            $"SpatialBuildingObjectInterpreter: confirmed start '{GetBuildingName(startBuildingCandidate)}' " +
            $"and destination '{GetBuildingName(destinationBuildingCandidate)}', then generated the route.");

        return true;
    }

    public void SwapStartDestination()
    {
        CityBuilding oldStart = startBuildingCandidate;
        startBuildingCandidate = destinationBuildingCandidate;
        destinationBuildingCandidate = oldStart;

        previewPresenter?.ShowBuildingCandidates(startBuildingCandidate, destinationBuildingCandidate);
        notifier?.Show("Start and destination candidates swapped.");
    }

    public void ClearSelection()
    {
        startBuildingCandidate = null;
        destinationBuildingCandidate = null;
        TargetBuildingCandidate = null;
        workingCandidates.Clear();
        previewPresenter?.ClearBuildingPreviews();
        detectionManager?.SetTwoBuildingSelectionCandidatesAvailable(false);
    }

    private bool PreviewSingleBuildingCandidate(PhysicalObjectCandidate candidate)
    {
        if (!TryMatchCandidate(candidate, out CityBuilding target))
            return false;

        TargetBuildingCandidate = target;
        previewPresenter?.ShowSingleBuildingCandidate(TargetBuildingCandidate);
        notifier?.Show($"Target building candidate: {GetBuildingName(target)}");
        return true;
    }

    private bool TryReduceToTwoCandidates(List<PhysicalObjectCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            Warn("No physical object candidates detected.");
            return false;
        }

        if (candidates.Count == 1)
        {
            Warn("Only one physical object candidate detected. Place two objects for start and destination.");
            return false;
        }

        if (candidates.Count == 2)
            return true;

        if (!useTwoClosestCandidatesWhenMoreThanTwo)
        {
            Warn("More than two object candidates detected. Remove extras or enable two-closest selection.");
            return false;
        }

        candidates.Sort(CompareCandidateBuildingDistance);
        candidates.RemoveRange(2, candidates.Count - 2);
        return true;
    }

    private int CompareCandidateBuildingDistance(PhysicalObjectCandidate a, PhysicalObjectCandidate b)
    {
        float da = GetClosestBuildingDistance(a);
        float db = GetClosestBuildingDistance(b);
        return da.CompareTo(db);
    }

    private float GetClosestBuildingDistance(PhysicalObjectCandidate candidate)
    {
        if (candidate == null || cityManager == null)
            return float.MaxValue;

        cityManager.GetClosestBuilding(candidate.worldPosition, out float distance);
        return distance;
    }

    private bool TryMatchCandidate(PhysicalObjectCandidate candidate, out CityBuilding building)
    {
        building = null;

        if (candidate == null || !candidate.isValid)
        {
            Warn("Invalid physical object candidate ignored.");
            return false;
        }

        building = cityManager.GetClosestBuilding(candidate.worldPosition, out float distance);
        if (building == null)
        {
            Warn("No buildings are available for matching.");
            return false;
        }

        if (distance > maxBuildingMatchDistance)
        {
            Warn($"Object is too far from the nearest building ({distance:0.00}m).");
            building = null;
            return false;
        }

        if (floodManager != null && floodManager.IsBuildingFlooded(building))
        {
            Warn($"Matched building '{GetBuildingName(building)}' is flooded.");
            building = null;
            return false;
        }

        return true;
    }

    private static void AddValidCandidates(
        List<PhysicalObjectCandidate> source,
        List<PhysicalObjectCandidate> destination)
    {
        if (source == null || destination == null)
            return;

        for (int i = 0; i < source.Count; i++)
        {
            PhysicalObjectCandidate candidate = source[i];
            if (candidate != null && candidate.isValid)
                destination.Add(candidate);
        }
    }

    private void ResolveMissingReferences()
    {
        cityManager ??= FindFirstObjectByType<CityManager>(FindObjectsInactive.Include);
        floodManager ??= FindFirstObjectByType<FloodManager>(FindObjectsInactive.Include);
        previewPresenter ??= FindFirstObjectByType<SpatialObjectPreviewPresenter>(FindObjectsInactive.Include);
        notifier ??= FindFirstObjectByType<MRNotification>(FindObjectsInactive.Include);
        pointSelectManager ??= FindFirstObjectByType<PointSelectManager>(FindObjectsInactive.Include);
        selectionTechniqueController ??=
            FindFirstObjectByType<BuildingSelectionTechniqueController>(FindObjectsInactive.Include);
        detectionManager ??=
            FindFirstObjectByType<SpatialObjectDetectionManager>(FindObjectsInactive.Include);
    }

    private void Warn(string message)
    {
        Debug.LogWarning($"SpatialBuildingObjectInterpreter: {message}");
        notifier?.Show(message);
    }

    private static string GetBuildingName(CityBuilding building)
    {
        if (building == null)
            return "Building";

        return string.IsNullOrWhiteSpace(building.displayName)
            ? building.id
            : building.displayName;
    }
}
