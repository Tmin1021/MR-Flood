using System.Collections.Generic;
using UnityEngine;

public class SimpleGraphManager : MonoBehaviour
{
    public Transform nodesParent;
    public Transform buildingsParent;

    private readonly List<GraphNode> nodes = new List<GraphNode>();

    [Header("Flood")]
    public Transform flood;
    [SerializeField] private FloodManager floodManager;
    public bool autoUpdateFlood = true;
    [Tooltip("Enable this only when the graph should be blocked by a simple water-height plane. Keep it off when FloodManager/FloodSource is used or when there is no active flood.")]
    [SerializeField] private bool useFloodHeightBlocking = false;
    [SerializeField] private bool useFloodSourceBlocking = true;

    [Header("Graph Setup")]
    [SerializeField] private bool autoResolveNodesParent = true;
    [SerializeField] private bool rebuildGraphOnAttachment = true;

    [Header("Debug")]
    public bool logGraphWarnings = true;
    [SerializeField] private bool logGraphSummary = true;

    private float waterLevel = float.NegativeInfinity;
    private bool hasBuiltGraph;

    public int NodeCount => nodes.Count;
    public int DirectedEdgeCount => CountDirectedEdges(includeBlockedEdges: true);
    public int UnblockedDirectedEdgeCount => CountDirectedEdges(includeBlockedEdges: false);

    public class TempAttachment
    {
        public GraphNode node;
        // Endpoints connected to the temporary node. Either can be null when
        // the corresponding endpoint of the closest road is blocked.
        public GraphNode edgeA;
        public GraphNode edgeB;
        // The geometric edge selected for snapping. These remain populated even
        // when only one endpoint can be connected, so route visuals can still
        // resolve the actual road that the building is on.
        public GraphNode closestEdgeA;
        public GraphNode closestEdgeB;
        public Vector3 snappedWorldPosition;
        public float snappedT;
        public List<(GraphNode from, GraphEdge edge)> addedBackEdges = new();

        public void Cleanup()
        {
            foreach (var (from, edge) in addedBackEdges)
            {
                if (from != null && edge != null)
                    from.edges.Remove(edge);
            }

            addedBackEdges.Clear();

            if (node != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(node.gameObject);
                else
                    Object.DestroyImmediate(node.gameObject);
            }
        }
    }

    private void Awake()
    {
        ResolveMissingReferences();
        BuildGraphFromNeighbors();
        RefreshFloodBlockingState();
    }

    private void Update()
    {
        if (!autoUpdateFlood)
            return;

        if (useFloodSourceBlocking && floodManager != null && floodManager.HasActiveFloodSources)
        {
            UpdateFloodSourceBlocking();
            return;
        }

        if (!useFloodHeightBlocking || flood == null)
        {
            ClearFloodBlocking();
            return;
        }

        float newWaterLevel = flood.position.y;
        if (!Mathf.Approximately(newWaterLevel, waterLevel))
            UpdateFloodBlocking(newWaterLevel);
    }

    public void EnsureGraphReady(bool forceRebuild = false)
    {
        ResolveMissingReferences();

        if (forceRebuild || !hasBuiltGraph || nodes.Count == 0 || CountDirectedEdges(true) == 0)
            BuildGraphFromNeighbors();
        else
            RefreshFloodBlockingState();
    }

    [ContextMenu("Rebuild Graph From Node Neighbors")]
    public void BuildGraphFromNeighbors()
    {
        foreach (var node in nodes)
        {
            if (node != null)
                node.edges.Clear();
        }

        nodes.Clear();
        ResolveMissingReferences();

        if (nodesParent == null)
        {
            Debug.LogWarning("SimpleGraphManager: nodesParent is not assigned and could not be resolved.");
            hasBuiltGraph = false;
            return;
        }

        GraphNode[] foundNodes = nodesParent.GetComponentsInChildren<GraphNode>(true);
        nodes.AddRange(foundNodes);

        HashSet<GraphNode> validNodes = new HashSet<GraphNode>(nodes);

        foreach (var node in nodes)
        {
            if (node == null) continue;
            node.edges.Clear();
        }

        int nodesWithNeighborComponent = 0;
        int skippedExternalNeighbors = 0;

        foreach (var node in nodes)
        {
            if (node == null) continue;

            NodeNeighbors nn = node.GetComponent<NodeNeighbors>();
            if (nn == null) continue;

            nodesWithNeighborComponent++;

            foreach (var neighbor in nn.neighbors)
            {
                if (neighbor == null || neighbor == node)
                    continue;

                if (!validNodes.Contains(neighbor))
                {
                    skippedExternalNeighbors++;
                    if (logGraphWarnings)
                    {
                        Debug.LogWarning(
                            $"SimpleGraphManager: '{node.name}' references '{neighbor.name}', " +
                            $"but that neighbor is not under nodesParent."
                        );
                    }
                    continue;
                }

                AddEdge(node, neighbor);

                if (nn.bidirectional)
                    AddEdge(neighbor, node);
            }
        }

        hasBuiltGraph = nodes.Count > 0;
        RefreshFloodBlockingState();

        if (logGraphSummary)
        {
            Debug.Log(
                $"SimpleGraphManager: graph rebuild complete. " +
                $"nodes={nodes.Count}, nodesWithNodeNeighbors={nodesWithNeighborComponent}, " +
                $"directedEdges={CountDirectedEdges(true)}, unblockedDirectedEdges={CountDirectedEdges(false)}, " +
                $"skippedExternalNeighbors={skippedExternalNeighbors}, " +
                $"useFloodHeightBlocking={useFloodHeightBlocking}, " +
                $"useFloodSourceBlocking={useFloodSourceBlocking}."
            );
        }

        if (nodes.Count == 0)
            Debug.LogWarning("SimpleGraphManager: no GraphNode objects were found under nodesParent.");
        else if (CountDirectedEdges(true) == 0)
            Debug.LogWarning("SimpleGraphManager: no graph edges were built. Check NodeNeighbors components and their neighbor lists.");
        else if (CountDirectedEdges(false) == 0)
            Debug.LogWarning("SimpleGraphManager: graph edges exist, but all are blocked. Check flood/water-level blocking settings.");
    }

    public void AddEdge(GraphNode from, GraphNode to)
    {
        if (from == null || to == null || from == to)
            return;

        for (int i = 0; i < from.edges.Count; i++)
        {
            if (from.edges[i].to == to)
                return;
        }

        float distance = Vector3.Distance(from.Position, to.Position);
        from.edges.Add(new GraphEdge(to, distance));
    }

    public float GetClosestDistance(Vector3 worldPos)
    {
        EnsureGraphReady(false);

        float bestDist = float.MaxValue;

        foreach (var node in nodes)
        {
            if (node == null) continue;

            float d = Vector3.Distance(node.Position, worldPos);
            if (d < bestDist)
                bestDist = d;
        }

        return bestDist;
    }

    public void UpdateFloodBlocking(float newWaterLevel)
    {
        waterLevel = newWaterLevel;

        if (!useFloodHeightBlocking)
        {
            ClearFloodBlocking();
            return;
        }

        foreach (var n in nodes)
        {
            if (n == null) continue;
            n.blocked = n.EffectiveHeight < waterLevel;
        }
    }

    public void UpdateFloodSourceBlocking()
    {
        if (floodManager == null || !floodManager.HasActiveFloodSources)
        {
            ClearFloodBlocking();
            return;
        }

        waterLevel = float.NegativeInfinity;

        foreach (var n in nodes)
        {
            if (n == null) continue;

            n.blocked = floodManager.IsWorldPointFlooded(n.Position);

            for (int i = 0; i < n.edges.Count; i++)
            {
                GraphEdge edge = n.edges[i];
                if (edge == null || edge.to == null)
                    continue;

                edge.blocked =
                    n.blocked ||
                    floodManager.IsWorldPointFlooded(edge.to.Position) ||
                    floodManager.IsWorldSegmentFlooded(n.Position, edge.to.Position);
            }
        }
    }

    public void ClearFloodBlocking()
    {
        waterLevel = float.NegativeInfinity;

        foreach (var n in nodes)
        {
            if (n == null) continue;
            n.blocked = false;

            for (int i = 0; i < n.edges.Count; i++)
            {
                if (n.edges[i] != null)
                    n.edges[i].blocked = false;
            }
        }
    }

    public bool IsBuildingFlooded(BuildingPoint b)
    {
        if (b == null) return true;
        return b.IsFlooded();
    }

    private static void ClosestPointOnSegment(Vector3 x, Vector3 a, Vector3 b, out Vector3 p, out float t)
    {
        Vector3 ab = b - a;
        float denom = Vector3.Dot(ab, ab);

        if (denom < 1e-6f)
        {
            t = 0f;
            p = a;
            return;
        }

        t = Mathf.Clamp01(Vector3.Dot(x - a, ab) / denom);
        p = a + t * ab;
    }

    private bool TryGetClosestEdge(
        Vector3 worldPos,
        out GraphNode a,
        out GraphNode b,
        out GraphEdge edge,
        out Vector3 p,
        out float t,
        out float dist)
    {
        a = null;
        b = null;
        edge = null;
        p = default;
        t = 0f;
        dist = float.MaxValue;

        float bestSqr = float.MaxValue;

        foreach (var n in nodes)
        {
            if (n == null) continue;

            foreach (var e in n.edges)
            {
                if (e == null) continue;

                var m = e.to;
                if (m == null) continue;

                // Ignore temporary attachment edges when searching for a new attachment.
                if (!nodes.Contains(m)) continue;

                ClosestPointOnSegment(worldPos, n.Position, m.Position, out Vector3 cp, out float tt);
                float dSqr = (worldPos - cp).sqrMagnitude;

                if (dSqr < bestSqr)
                {
                    bestSqr = dSqr;
                    a = n;
                    b = m;
                    edge = e;
                    p = cp;
                    t = tt;
                }
            }
        }

        if (a == null || b == null || edge == null)
            return false;

        dist = Mathf.Sqrt(bestSqr);
        return true;
    }

    public string GetAttachmentDebugInfo(Vector3 anchorPos)
    {
        EnsureGraphReady(false);

        string summary =
            $"nodes={nodes.Count}, directedEdges={CountDirectedEdges(true)}, " +
            $"unblockedDirectedEdges={CountDirectedEdges(false)}, " +
            $"useFloodHeightBlocking={useFloodHeightBlocking}, " +
            $"useFloodSourceBlocking={useFloodSourceBlocking}, " +
            $"waterLevel={waterLevel:0.###}";

        bool hasClosestEdge = TryGetClosestEdge(
            anchorPos,
            out GraphNode anyA,
            out GraphNode anyB,
            out GraphEdge anyEdge,
            out Vector3 anyPoint,
            out _,
            out float anyDist);

        if (!hasClosestEdge)
            return summary + "; no graph edge exists. Check NodeNeighbors.";

        bool canConnectToA = !anyEdge.blocked && !anyA.blocked;
        bool canConnectToB = !anyEdge.blocked && !anyB.blocked;

        return summary +
            $"; closestGeometricEdge={anyA.name}->{anyB.name}, projectedPosition={anyPoint}, " +
            $"snapDistance={anyDist:0.###}, graphEdgeBlocked={anyEdge.blocked}, " +
            $"aBlocked={anyA.blocked}, bBlocked={anyB.blocked}, " +
            $"aUsable={canConnectToA}, bUsable={canConnectToB}";
    }

    public TempAttachment CreateAttachmentNode(
        Vector3 anchorPos,
        float maxSnapDist = 999f,
        string name = "AttachNode")
    {
        EnsureGraphReady(rebuildGraphOnAttachment);

        if (!TryGetClosestEdge(anchorPos, out var A, out var B, out var selectedEdge, out var P, out float t, out float d))
        {
            if (logGraphWarnings)
                Debug.LogWarning($"SimpleGraphManager: cannot create '{name}'. {GetAttachmentDebugInfo(anchorPos)}");

            return null;
        }

        if (d > maxSnapDist)
        {
            if (logGraphWarnings)
            {
                Debug.LogWarning(
                    $"SimpleGraphManager: cannot create '{name}'. Closest graph edge is too far. " +
                    $"distance={d:0.###}, maxSnapDist={maxSnapDist:0.###}. {GetAttachmentDebugInfo(anchorPos)}");
            }

            return null;
        }

        bool edgeIsUsable = !selectedEdge.blocked;
        bool canConnectToA = !A.blocked;
        bool canConnectToB = !B.blocked;

        if (!edgeIsUsable)
        {
            if (logGraphWarnings)
            {
                Debug.LogWarning(
                    $"SimpleGraphManager: cannot create '{name}'. The closest GraphEdge is blocked. " +
                    $"edge={A.name}->{B.name}, projectedPosition={P}, snapDistance={d:0.###}, " +
                    $"aBlocked={A.blocked}, bBlocked={B.blocked}.");
            }

            return null;
        }

        if (!canConnectToA && !canConnectToB)
        {
            if (logGraphWarnings)
            {
                Debug.LogWarning(
                    $"SimpleGraphManager: cannot create '{name}'. Both endpoints of the closest graph edge are blocked. " +
                    $"edge={A.name}->{B.name}, projectedPosition={P}, snapDistance={d:0.###}.");
            }

            return null;
        }

        var go = new GameObject(name);
        go.transform.position = P;

        var attach = go.AddComponent<GraphNode>();
        attach.blocked =
            (useFloodSourceBlocking && floodManager != null && floodManager.IsWorldPointFlooded(P)) ||
            (useFloodHeightBlocking && attach.EffectiveHeight < waterLevel);

        float len = Vector3.Distance(A.Position, B.Position);
        float costToA = t * len;
        float costToB = (1f - t) * len;

        var ta = new TempAttachment
        {
            node = attach,
            edgeA = canConnectToA ? A : null,
            edgeB = canConnectToB ? B : null,
            closestEdgeA = A,
            closestEdgeB = B,
            snappedWorldPosition = P,
            snappedT = t
        };

        if (canConnectToA)
        {
            attach.edges.Add(new GraphEdge(A, costToA));

            var backA = new GraphEdge(attach, costToA);
            A.edges.Add(backA);
            ta.addedBackEdges.Add((A, backA));
        }

        if (canConnectToB)
        {
            attach.edges.Add(new GraphEdge(B, costToB));

            var backB = new GraphEdge(attach, costToB);
            B.edges.Add(backB);
            ta.addedBackEdges.Add((B, backB));
        }

        if (logGraphSummary)
        {
            Debug.Log(
                $"SimpleGraphManager: created attachment '{name}' at {P}. " +
                $"Closest edge: {A.name}->{B.name}. Edge blocked: {selectedEdge.blocked}. " +
                $"A blocked: {A.blocked}. B blocked: {B.blocked}. " +
                $"Connected endpoints: {(canConnectToA ? A.name : string.Empty)}" +
                $"{(canConnectToA && canConnectToB ? ", " : string.Empty)}" +
                $"{(canConnectToB ? B.name : string.Empty)}. " +
                $"snapDistance={d:0.###}, t={t:0.###}.");
        }

        return ta;
    }

    private void ResolveMissingReferences()
    {
        floodManager ??= FindFirstObjectByType<FloodManager>(FindObjectsInactive.Include);

        if (nodesParent != null || !autoResolveNodesParent)
            return;

        GraphNode firstNode = FindFirstObjectByType<GraphNode>(FindObjectsInactive.Include);
        if (firstNode != null)
        {
            nodesParent = firstNode.transform.parent != null
                ? firstNode.transform.parent
                : firstNode.transform;

            Debug.Log($"SimpleGraphManager: auto-resolved nodesParent to '{nodesParent.name}'.");
        }
    }

    private void RefreshFloodBlockingState()
    {
        if (useFloodSourceBlocking && floodManager != null && floodManager.HasActiveFloodSources)
            UpdateFloodSourceBlocking();
        else if (useFloodHeightBlocking && flood != null)
            UpdateFloodBlocking(flood.position.y);
        else
            ClearFloodBlocking();
    }

    private int CountDirectedEdges(bool includeBlockedEdges)
    {
        int count = 0;

        for (int i = 0; i < nodes.Count; i++)
        {
            GraphNode node = nodes[i];
            if (node == null || node.edges == null)
                continue;

            for (int j = 0; j < node.edges.Count; j++)
            {
                GraphEdge edge = node.edges[j];
                if (edge == null || edge.to == null)
                    continue;

                if (!includeBlockedEdges && (node.blocked || edge.to.blocked || edge.blocked))
                    continue;

                count++;
            }
        }

        return count;
    }
}
