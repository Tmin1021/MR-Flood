using UnityEngine;

public static class RouteEvaluator
{
    public static float GetRoadTraversalCost(Road road)
    {
        if (road == null) return Mathf.Infinity;
        if (road.isBlocked) return Mathf.Infinity;

        float baseCost = Mathf.Max(road.length, 0.001f);
        float floodPenalty = 1f + road.riskCost;

        return baseCost * floodPenalty;
    }
}