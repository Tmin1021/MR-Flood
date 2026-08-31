using UnityEngine;

public static class FloodEvaluator
{
    public static float EvaluateRoadFloodDepth(Road road, FloodSource[] sources)
    {
        if (road == null || sources == null) return 0f;

        float total = 0f;
        Vector3 start = road.start != null ? road.start.position : road.Center;
        Vector3 end = road.end != null ? road.end.position : road.Center;

        foreach (var source in sources)
        {
            if (source == null) continue;
            if (source.IntersectsSegment(start, end))
                total += Mathf.Max(source.intensity, 1f);
        }

        return total;
    }

    public static float EvaluateIntersectionFloodDepth(Intersection intersection, FloodSource[] sources)
    {
        if (intersection == null || sources == null) return 0f;

        float total = 0f;

        foreach (var source in sources)
        {
            if (source == null) continue;
            if (source.ContainsPoint(intersection.position))
                total += Mathf.Max(source.intensity, 1f);
        }

        return total;
    }

    public static float EvaluateBuildingFloodDepth(CityBuilding building, FloodSource[] sources)
    {
        if (building == null || sources == null) return 0f;

        float total = 0f;

        foreach (var source in sources)
        {
            if (source == null) continue;
            if (source.ContainsPoint(building.position))
                total += Mathf.Max(source.intensity, 1f);
        }

        return total;
    }
}
