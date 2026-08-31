using System.Collections.Generic;
using UnityEngine;

public static class AStarRoadPathfinder
{
    public static Route FindRoute(Intersection start, Intersection goal)
    {
        Route emptyRoute = new Route();

        if (start == null || goal == null) return emptyRoute;
        if (start.isBlocked || goal.isBlocked) return emptyRoute;

        List<Intersection> open = new List<Intersection> { start };
        HashSet<Intersection> closed = new HashSet<Intersection>();

        Dictionary<Intersection, Intersection> cameFrom = new Dictionary<Intersection, Intersection>();
        Dictionary<Intersection, Road> cameByRoad = new Dictionary<Intersection, Road>();

        Dictionary<Intersection, float> gScore = new Dictionary<Intersection, float>
        {
            [start] = 0f
        };

        Dictionary<Intersection, float> fScore = new Dictionary<Intersection, float>
        {
            [start] = Heuristic(start, goal)
        };

        while (open.Count > 0)
        {
            Intersection current = GetLowestF(open, fScore);

            if (current == goal)
                return ReconstructRoute(goal, cameFrom, cameByRoad, gScore[goal]);

            open.Remove(current);
            closed.Add(current);

            foreach (var road in current.connectedRoads)
            {
                if (road == null || road.isBlocked) continue;

                Intersection neighbor = road.GetOtherIntersection(current);
                if (neighbor == null || neighbor.isBlocked) continue;
                if (closed.Contains(neighbor)) continue;

                float tentativeG = gScore[current] + RouteEvaluator.GetRoadTraversalCost(road);

                if (!gScore.TryGetValue(neighbor, out float knownG) || tentativeG < knownG)
                {
                    cameFrom[neighbor] = current;
                    cameByRoad[neighbor] = road;

                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, goal);

                    if (!open.Contains(neighbor))
                        open.Add(neighbor);
                }
            }
        }

        return emptyRoute;
    }

    private static float Heuristic(Intersection a, Intersection b)
    {
        return Vector3.Distance(a.position, b.position);
    }

    private static Intersection GetLowestF(List<Intersection> open, Dictionary<Intersection, float> fScore)
    {
        Intersection best = open[0];
        float bestValue = fScore.TryGetValue(best, out float value) ? value : float.MaxValue;

        for (int i = 1; i < open.Count; i++)
        {
            Intersection current = open[i];
            float currentValue = fScore.TryGetValue(current, out float fv) ? fv : float.MaxValue;

            if (currentValue < bestValue)
            {
                best = current;
                bestValue = currentValue;
            }
        }

        return best;
    }

    private static Route ReconstructRoute(
        Intersection goal,
        Dictionary<Intersection, Intersection> cameFrom,
        Dictionary<Intersection, Road> cameByRoad,
        float totalCost)
    {
        Route route = new Route();
        route.totalCost = totalCost;
        route.isValid = true;

        List<Intersection> reversedIntersections = new List<Intersection> { goal };
        List<Road> reversedRoads = new List<Road>();

        Intersection current = goal;

        while (cameFrom.TryGetValue(current, out Intersection previous))
        {
            reversedIntersections.Add(previous);

            if (cameByRoad.TryGetValue(current, out Road road))
                reversedRoads.Add(road);

            current = previous;
        }

        reversedIntersections.Reverse();
        reversedRoads.Reverse();

        route.intersections = reversedIntersections;
        route.roads = reversedRoads;

        return route;
    }
}