using System;
using System.Collections.Generic;
using UnityEngine;

public class CityManager : MonoBehaviour
{
    [Header("Data")]
    // Runtime-generated graph data. Keeping this out of Unity serialization avoids
    // inspector recursion on the cyclic Road/Intersection/Building references.
    [NonSerialized] public List<Road> roads = new List<Road>();
    [NonSerialized] public List<Intersection> intersections = new List<Intersection>();
    [NonSerialized] public List<CityBuilding> buildings = new List<CityBuilding>();

    private readonly Dictionary<string, CityBuilding> buildingsById =
        new Dictionary<string, CityBuilding>(StringComparer.Ordinal);
    private readonly Dictionary<string, Road> roadsById =
        new Dictionary<string, Road>(StringComparer.Ordinal);
    private readonly Dictionary<string, Intersection> intersectionsById =
        new Dictionary<string, Intersection>(StringComparer.Ordinal);

    public int DataRevision { get; private set; }

    public CityBuilding GetBuildingById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (buildingsById.TryGetValue(id, out CityBuilding building))
            return building;

        return buildings.Find(b => b != null && b.id == id);
    }

    public Road GetRoadById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (roadsById.TryGetValue(id, out Road road))
            return road;

        return roads.Find(r => r != null && r.id == id);
    }

    public Intersection GetIntersectionById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (intersectionsById.TryGetValue(id, out Intersection intersection))
            return intersection;

        return intersections.Find(i => i != null && i.id == id);
    }

    /// <summary>
    /// Rebuilds stable-ID indexes after CityBootstrapper replaces runtime data.
    /// Returns false if the source data contains a missing or duplicate ID.
    /// </summary>
    public bool RebuildIdIndexes()
    {
        buildingsById.Clear();
        roadsById.Clear();
        intersectionsById.Clear();

        bool valid = true;
        valid &= IndexItems(buildings, buildingsById, b => b?.id, "building");
        valid &= IndexItems(roads, roadsById, r => r?.id, "road");
        valid &= IndexItems(intersections, intersectionsById, i => i?.id, "intersection");
        DataRevision++;
        return valid;
    }

    private bool IndexItems<T>(
        List<T> items,
        Dictionary<string, T> index,
        Func<T, string> getId,
        string itemType)
        where T : class
    {
        bool valid = true;

        for (int i = 0; i < items.Count; i++)
        {
            T item = items[i];
            if (item == null)
                continue;

            string id = getId(item);
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.LogError($"CityManager: {itemType} at index {i} has no stable ID.");
                valid = false;
                continue;
            }

            if (!index.TryAdd(id, item))
            {
                Debug.LogError($"CityManager: duplicate {itemType} ID '{id}'.");
                valid = false;
            }
        }

        return valid;
    }

    public Intersection GetClosestIntersection(Vector3 worldPos)
    {
        Intersection best = null;
        float bestDist = float.MaxValue;

        foreach (var intersection in intersections)
        {
            if (intersection == null) continue;

            float d = Vector3.Distance(worldPos, intersection.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = intersection;
            }
        }

        return best;
    }

    public CityBuilding GetClosestBuilding(Vector3 worldPos)
    {
        return GetClosestBuilding(worldPos, out _);
    }

    public CityBuilding GetClosestBuilding(Vector3 worldPos, out float distance)
    {
        CityBuilding best = null;
        float bestDist = float.MaxValue;

        foreach (var building in buildings)
        {
            if (building == null) continue;

            float d = Vector3.Distance(worldPos, building.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = building;
            }
        }

        distance = bestDist;
        return best;
    }

    public Road GetClosestRoad(Vector3 worldPos)
    {
        Road best = null;
        float bestDist = float.MaxValue;

        foreach (var road in roads)
        {
            if (road == null || road.start == null || road.end == null) continue;

            Vector3 p = ClosestPointOnSegment(worldPos, road.start.position, road.end.position);
            float d = Vector3.Distance(worldPos, p);

            if (d < bestDist)
            {
                bestDist = d;
                best = road;
            }
        }

        return best;
    }

    public Road GetRoadBetweenPoints(
        Vector3 a,
        Vector3 b,
        float endpointTolerance = 0.05f,
        bool allowNearestFallback = true)
    {
        Road best = null;
        float bestScore = float.MaxValue;
        float tolSqr = endpointTolerance * endpointTolerance * 2f;

        foreach (var road in roads)
        {
            if (road == null || road.start == null || road.end == null) continue;

            Vector3 rs = road.start.position;
            Vector3 re = road.end.position;

            float forwardScore = (rs - a).sqrMagnitude + (re - b).sqrMagnitude;
            float reverseScore = (rs - b).sqrMagnitude + (re - a).sqrMagnitude;
            float score = Mathf.Min(forwardScore, reverseScore);

            if (score < bestScore)
            {
                bestScore = score;
                best = road;
            }
        }

        if (best == null)
            return null;

        if (bestScore <= tolSqr)
            return best;

        return allowNearestFallback ? best : null;
    }

    public void RecalculateRoadLengths()
    {
        foreach (var road in roads)
        {
            if (road == null) continue;
            road.RecalculateLength();
        }
    }

    public void LinkRoadsToIntersections()
    {
        foreach (var i in intersections)
        {
            if (i == null) continue;
            i.connectedRoads.Clear();
        }

        foreach (var road in roads)
        {
            if (road == null) continue;

            road.start?.AddRoad(road);
            road.end?.AddRoad(road);
        }
    }

    private Vector3 ClosestPointOnSegment(Vector3 x, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float denom = Vector3.Dot(ab, ab);

        if (denom < 0.000001f)
            return a;

        float t = Mathf.Clamp01(Vector3.Dot(x - a, ab) / denom);
        return a + t * ab;
    }
}
