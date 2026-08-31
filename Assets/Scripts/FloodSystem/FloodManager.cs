using UnityEngine;

public class FloodManager : MonoBehaviour
{
    private const float SourceFloatChangeThreshold = 0.0001f;
    private const float SourcePositionChangeThresholdSqr = 0.000001f;

    [Header("References")]
    public CityManager cityManager;
    public FloodSource[] floodSources;

    [Header("Thresholds")]
    public float roadBlockedThreshold = 0.8f;
    public float intersectionBlockedThreshold = 1.0f;
    public float buildingFloodedThreshold = 0.8f;

    [Header("Auto Update")]
    public bool autoUpdate = true;

    private bool hasAppliedFloodState;
    private FloodSource[] cachedSources = System.Array.Empty<FloodSource>();
    private float[] cachedIntensities = System.Array.Empty<float>();
    private float[] cachedRadii = System.Array.Empty<float>();
    private Vector3[] cachedPositions = System.Array.Empty<Vector3>();

    public bool HasActiveFloodSources => HasAnyFloodSources();
    public int FloodRevision { get; private set; }
    public int FloodSourcesRevision { get; private set; }
    public event System.Action<int> FloodStateChanged;
    public event System.Action<int> FloodSourcesChanged;

    private void Update()
    {
        if (!autoUpdate)
            return;

        float dt = Time.deltaTime;
        bool sourceAdvanced = AdvanceSources(dt);
        bool sourceStateChanged = HaveFloodSourcesChanged();

        if (!hasAppliedFloodState || sourceAdvanced || sourceStateChanged)
            UpdateFloodState();
    }

    public void UpdateFloodState()
    {
        if (cityManager == null)
            return;

        if (!HasAnyFloodSources())
        {
            ClearFloodState();
            hasAppliedFloodState = true;
            CacheFloodSourceState();
            PublishFloodStateChanged();
            return;
        }

        UpdateRoads();
        UpdateIntersections();
        UpdateBuildings();

        hasAppliedFloodState = true;
        CacheFloodSourceState();
        PublishFloodStateChanged();
    }

    private void PublishFloodStateChanged()
    {
        FloodRevision++;
        FloodStateChanged?.Invoke(FloodRevision);
    }

    public void SetFloodSources(FloodSource[] sources, bool updateImmediately = true)
    {
        floodSources = sources ?? System.Array.Empty<FloodSource>();
        hasAppliedFloodState = false;
        FloodSourcesRevision++;
        FloodSourcesChanged?.Invoke(FloodSourcesRevision);

        if (updateImmediately)
            UpdateFloodState();
        else
            CacheFloodSourceState();
    }

    private void UpdateRoads()
    {
        foreach (var road in cityManager.roads)
        {
            if (road == null) continue;

            road.floodDepth = FloodEvaluator.EvaluateRoadFloodDepth(road, floodSources);
            road.isBlocked = road.floodDepth >= roadBlockedThreshold;
            road.riskCost = road.isBlocked ? 99999f : road.floodDepth;
        }
    }

    private void UpdateIntersections()
    {
        foreach (var intersection in cityManager.intersections)
        {
            if (intersection == null) continue;

            intersection.floodDepth = FloodEvaluator.EvaluateIntersectionFloodDepth(intersection, floodSources);
            intersection.isBlocked = intersection.floodDepth >= intersectionBlockedThreshold;
        }
    }

    private void UpdateBuildings()
    {
        foreach (var building in cityManager.buildings)
        {
            if (building == null) continue;

            building.floodDepth = FloodEvaluator.EvaluateBuildingFloodDepth(building, floodSources);
            building.isFlooded = building.floodDepth >= buildingFloodedThreshold;
        }
    }

    public bool IsBuildingFlooded(CityBuilding building)
    {
        return building == null || building.isFlooded;
    }

    public bool IsWorldPointFlooded(Vector3 worldPosition)
    {
        if (!HasAnyFloodSources())
            return false;

        for (int i = 0; i < floodSources.Length; i++)
        {
            FloodSource source = floodSources[i];
            if (source != null && source.ContainsPoint(worldPosition))
                return true;
        }

        return false;
    }

    public bool IsWorldSegmentFlooded(Vector3 startWorldPosition, Vector3 endWorldPosition)
    {
        if (!HasAnyFloodSources())
            return false;

        for (int i = 0; i < floodSources.Length; i++)
        {
            FloodSource source = floodSources[i];
            if (source != null && source.IntersectsSegment(startWorldPosition, endWorldPosition))
                return true;
        }

        return false;
    }

    private bool AdvanceSources(float dt)
    {
        bool changed = false;

        if (floodSources == null)
            return false;

        foreach (FloodSource source in floodSources)
        {
            if (source == null || !source.autoExpand)
                continue;

            float before = source.radius;
            source.Advance(dt);

            if (!Mathf.Approximately(before, source.radius))
                changed = true;
        }

        return changed;
    }

    private bool HasAnyFloodSources()
    {
        if (floodSources == null || floodSources.Length == 0)
            return false;

        for (int i = 0; i < floodSources.Length; i++)
        {
            if (floodSources[i] != null)
                return true;
        }

        return false;
    }

    private bool HaveFloodSourcesChanged()
    {
        int currentCount = floodSources?.Length ?? 0;

        if (currentCount != cachedSources.Length)
            return true;

        for (int i = 0; i < currentCount; i++)
        {
            FloodSource source = floodSources[i];

            if (cachedSources[i] != source)
                return true;

            if (source == null)
                continue;

            if (Mathf.Abs(cachedIntensities[i] - source.intensity) > SourceFloatChangeThreshold)
                return true;

            if (Mathf.Abs(cachedRadii[i] - source.radius) > SourceFloatChangeThreshold)
                return true;

            if ((cachedPositions[i] - source.transform.position).sqrMagnitude > SourcePositionChangeThresholdSqr)
                return true;
        }

        return false;
    }

    private void CacheFloodSourceState()
    {
        if (floodSources == null || floodSources.Length == 0)
        {
            cachedSources = System.Array.Empty<FloodSource>();
            cachedIntensities = System.Array.Empty<float>();
            cachedRadii = System.Array.Empty<float>();
            cachedPositions = System.Array.Empty<Vector3>();
            return;
        }

        int count = floodSources.Length;

        if (cachedSources.Length != count)
        {
            cachedSources = new FloodSource[count];
            cachedIntensities = new float[count];
            cachedRadii = new float[count];
            cachedPositions = new Vector3[count];
        }

        for (int i = 0; i < count; i++)
        {
            FloodSource source = floodSources[i];
            cachedSources[i] = source;

            if (source == null)
            {
                cachedIntensities[i] = 0f;
                cachedRadii[i] = 0f;
                cachedPositions[i] = Vector3.zero;
                continue;
            }

            cachedIntensities[i] = source.intensity;
            cachedRadii[i] = source.radius;
            cachedPositions[i] = source.transform.position;
        }
    }

    private void ClearFloodState()
    {
        foreach (Road road in cityManager.roads)
        {
            if (road == null) continue;
            road.floodDepth = 0f;
            road.isBlocked = false;
            road.riskCost = 0f;
        }

        foreach (Intersection intersection in cityManager.intersections)
        {
            if (intersection == null) continue;
            intersection.floodDepth = 0f;
            intersection.isBlocked = false;
        }

        foreach (CityBuilding building in cityManager.buildings)
        {
            if (building == null) continue;
            building.floodDepth = 0f;
            building.isFlooded = false;
        }
    }
}
