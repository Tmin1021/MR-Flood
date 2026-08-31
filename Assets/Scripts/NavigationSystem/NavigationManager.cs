using System.Collections.Generic;
using UnityEngine;

public class NavigationManager : MonoBehaviour
{
    [Header("References")]
    public CityManager cityManager;
    public FloodManager floodManager;

    [Header("Options")]
    [Tooltip("Refresh nearest-road / nearest-intersection links each time a route is requested.")]
    public bool refreshBuildingLinksBeforeRouting = true;

    [Tooltip("Extra cost for building-to-entry and exit-to-building distance.")]
    public float buildingApproachWeight = 1f;

    public Route CurrentRoute { get; private set; }

    public Route FindRoute(CityBuilding startBuilding, CityBuilding destinationBuilding)
    {
        CurrentRoute = new Route();

        if (cityManager == null || floodManager == null)
            return CurrentRoute;

        if (startBuilding == null || destinationBuilding == null)
            return CurrentRoute;

        if (floodManager.IsBuildingFlooded(startBuilding) || floodManager.IsBuildingFlooded(destinationBuilding))
            return CurrentRoute;

        if (refreshBuildingLinksBeforeRouting)
        {
            RefreshBuildingRoutingLinks(startBuilding);
            RefreshBuildingRoutingLinks(destinationBuilding);
        }

        List<Intersection> startCandidates = GetRoutingCandidates(startBuilding);
        List<Intersection> goalCandidates = GetRoutingCandidates(destinationBuilding);

        if (startCandidates.Count == 0 || goalCandidates.Count == 0)
            return CurrentRoute;

        Route bestRoute = null;
        float bestScore = float.MaxValue;

        foreach (Intersection start in startCandidates)
        {
            if (start == null || start.isBlocked) continue;

            foreach (Intersection goal in goalCandidates)
            {
                if (goal == null || goal.isBlocked) continue;

                Route candidate = AStarRoadPathfinder.FindRoute(start, goal);
                if (candidate == null || !candidate.isValid) continue;

                float approachCost =
                    Vector3.Distance(startBuilding.position, start.position) +
                    Vector3.Distance(destinationBuilding.position, goal.position);

                float totalScore = candidate.totalCost + (approachCost * buildingApproachWeight);

                if (totalScore < bestScore)
                {
                    bestScore = totalScore;
                    bestRoute = candidate;
                }
            }
        }

        CurrentRoute = bestRoute ?? new Route();
        return CurrentRoute;
    }

    public bool IsCurrentRouteStillValid()
    {
        if (CurrentRoute == null || !CurrentRoute.isValid)
            return false;

        if (CurrentRoute.roads != null)
        {
            foreach (Road road in CurrentRoute.roads)
            {
                if (road == null || road.isBlocked)
                    return false;
            }
        }

        if (CurrentRoute.intersections != null)
        {
            foreach (Intersection intersection in CurrentRoute.intersections)
            {
                if (intersection == null || intersection.isBlocked)
                    return false;
            }
        }

        return true;
    }

    private void RefreshBuildingRoutingLinks(CityBuilding building)
    {
        if (building == null || cityManager == null)
            return;

        building.nearestRoad = cityManager.GetClosestRoad(building.position);
        building.nearestIntersection = GetCloserEndpointOfRoad(building.nearestRoad, building.position);

        if (building.nearestIntersection == null)
            building.nearestIntersection = cityManager.GetClosestIntersection(building.position);
    }

    private List<Intersection> GetRoutingCandidates(CityBuilding building)
    {
        List<Intersection> candidates = new List<Intersection>();

        if (building == null)
            return candidates;

        if (building.nearestRoad != null)
        {
            AddCandidate(candidates, building.nearestRoad.start);
            AddCandidate(candidates, building.nearestRoad.end);
        }

        AddCandidate(candidates, building.nearestIntersection);

        if (candidates.Count == 0 && cityManager != null)
            AddCandidate(candidates, cityManager.GetClosestIntersection(building.position));

        return candidates;
    }

    private void AddCandidate(List<Intersection> list, Intersection candidate)
    {
        if (candidate == null) return;
        if (!list.Contains(candidate))
            list.Add(candidate);
    }

    private Intersection GetCloserEndpointOfRoad(Road road, Vector3 worldPos)
    {
        if (road == null) return null;

        Intersection a = road.start;
        Intersection b = road.end;

        if (a != null && b != null)
        {
            float da = Vector3.Distance(worldPos, a.position);
            float db = Vector3.Distance(worldPos, b.position);
            return da <= db ? a : b;
        }

        if (a != null) return a;
        if (b != null) return b;

        return null;
    }
}