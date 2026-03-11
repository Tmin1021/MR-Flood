using System.Collections.Generic;
using UnityEngine;

public static class AStarPathfinder
{
    public static List<GraphNode> FindPath(GraphNode start, GraphNode goal)
    {
        var result = new List<GraphNode>();

        if (start == null || goal == null) return result;
        if (start.blocked || goal.blocked) return result;

        var open = new List<GraphNode> { start };
        var cameFrom = new Dictionary<GraphNode, GraphNode>();

        var g = new Dictionary<GraphNode, float> { [start] = 0f };
        var f = new Dictionary<GraphNode, float> { [start] = Heuristic(start, goal) };

        while (open.Count > 0)
        {
            var current = LowestF(open, f);

            if (current == goal)
                return Reconstruct(cameFrom, current);

            open.Remove(current);

            foreach (var e in current.edges)
            {
                if (e.blocked) continue;

                var nb = e.to;
                if (nb == null || nb.blocked) continue;

                float tentative = g[current] + e.cost;

                if (!g.TryGetValue(nb, out float known) || tentative < known)
                {
                    cameFrom[nb] = current;
                    g[nb] = tentative;
                    f[nb] = tentative + Heuristic(nb, goal);

                    if (!open.Contains(nb))
                        open.Add(nb);
                }
            }
        }

        return result;
    }

    static float Heuristic(GraphNode a, GraphNode b)
        => Vector3.Distance(a.Position, b.Position);

    static GraphNode LowestF(List<GraphNode> open, Dictionary<GraphNode, float> f)
    {
        GraphNode best = open[0];
        float bestScore = f.TryGetValue(best, out var v) ? v : float.MaxValue;

        for (int i = 1; i < open.Count; i++)
        {
            var n = open[i];
            float s = f.TryGetValue(n, out var fv) ? fv : float.MaxValue;

            if (s < bestScore)
            {
                best = n;
                bestScore = s;
            }
        }
        return best;
    }

    static List<GraphNode> Reconstruct(Dictionary<GraphNode, GraphNode> cameFrom, GraphNode current)
    {
        var total = new List<GraphNode> { current };

        while (cameFrom.TryGetValue(current, out var prev))
        {
            current = prev;
            total.Add(current);
        }

        total.Reverse();
        return total;
    }
}