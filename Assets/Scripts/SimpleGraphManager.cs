using System.Collections;
using System.Collections.Generic;
// using System.Numerics;
using JetBrains.Annotations;
using NUnit.Framework.Constraints;
using Unity.VisualScripting;
using UnityEngine;

public class SimpleGraphManager : MonoBehaviour
{
    public Transform nodesParent;
    public Transform buildingsParent;
    private List<GraphNode> nodes = new List<GraphNode>();

    [Header("Flood")]
    public Transform flood;
    private float waterLevel;
    public bool autoUpdateFlood = true;

    public class TempAttachment
    {
        public GraphNode node;
        public List<(GraphNode from, GraphEdge edge)> addedBackEdges = new();

        public void Cleanup()
        {
            foreach (var (from, edge) in addedBackEdges)
            {
                if (from != null && edge != null)
                    from.edges.Remove(edge);
            }

            if (node != null)
                Object.Destroy(node.gameObject);
        }
    }

    void Awake()
    {
        BuildGraphFromNeighbors();
        UpdateFloodBlocking(waterLevel);
    }
    // private void Start() {
    //     Debug.Log(SmallestDistanceFromNodeToBuilding());
    // }

    // Update is called once per frame
    void Update()
    {
        if(flood) waterLevel = flood.position.y;
        Debug.Log("Water:" + waterLevel);
        // Water (Global Position):
        // - Lowest:-0.116
        // - Middle:-0.106
        // - Highest:-0.096

        if(autoUpdateFlood)
        {
            UpdateFloodBlocking(waterLevel);
        }
    }

    public void BuildGraphFromNeighbors()
    {
        // for rebuild at runtime
        foreach (var node in nodes)
        {
            if (node != null) node.edges.Clear();
        }
        nodes.Clear();

        if(nodesParent == null)
        {
            Debug.Log("Assign parent of nodes!");
            return;
        }

        for (int i = 0; i < nodesParent.childCount; i++) {
            Transform t = nodesParent.GetChild(i);
            var node = t.GetComponent<GraphNode>();
            if (node == null) {
                node = t.AddComponent<GraphNode>();
                Debug.Log("T deo co graph node");
            }
            nodes.Add(node);
        }

        foreach(var node in nodes)
        {
            var nn = node.GetComponent<NodeNeighbors>();
            if(nn == null) continue;

            foreach(var neighbor in nn.neighbors)
            {
                if(neighbor == node || neighbor == null) continue;
                AddEdge(node, neighbor);

                if(nn.bidirectional == true)
                {
                    AddEdge(neighbor, node); // redundant but whatever
                }
            }
        }  
    }

    public void AddEdge(GraphNode from, GraphNode to) 
    {
        for(int i = 0; i < from.edges.Count; i++)
        {
            if(from.edges[i].to == to) return; // check duplicate
        }
        var distance = Vector3.Distance(from.Position, to.Position);
        from.edges.Add(new GraphEdge(to, distance));
    }

    // public GraphNode GetClosestNode(Vector3 worldPos)
    // {
    //     GraphNode closestNode = null;
    //     float bestDist = float.MaxValue;
        
    //     foreach(var node in nodes)
    //     {
    //         float d = Vector3.Distance(node.Position, worldPos);
    //         if(d <= bestDist)
    //         {
    //             closestNode = node;
    //             bestDist = d;
    //         }
    //     }
    //     return closestNode;
    // }

    public float GetClosestDistance(Vector3 worldPos)
    {
        float bestDist = float.MaxValue;
        
        foreach(var node in nodes)
        {
            float d = Vector3.Distance(node.Position, worldPos);
            if(d <= bestDist)
            {
                bestDist = d;
            }
        }
        return bestDist;
    }

    public void UpdateFloodBlocking(float newWaterLevel)
    {
        waterLevel = newWaterLevel;

        foreach (var n in nodes)
        {
            n.blocked = (n.EffectiveHeight < waterLevel);
            Debug.Log("Node " + n.name + ": " + n.EffectiveHeight);
        }
    }

    public bool IsBuildingFlooded(BuildingPoint b)
    {
        if (b == null) return true;
        return b.transform.position.y < waterLevel;
    }

    private float SmallestDistanceFromNodeToBuilding()
    {
        if(nodesParent == null) return 0.0f;
        float res = float.MaxValue;
        foreach(Transform b in buildingsParent.GetComponentsInChildren<Transform>())
        {
            float currentDist = GetClosestDistance(b.position);
            if(res >= currentDist)
            {
                res = currentDist;
            }
        }
        
        return res;
    }

    static void ClosestPointOnSegment(Vector3 x, Vector3 a, Vector3 b, out Vector3 p, out float t)
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

    bool TryGetClosestEdge(Vector3 worldPos, out GraphNode a, out GraphNode b, out Vector3 p, out float t, out float dist)
    {
        a = null; b = null; p = default; t = 0f; dist = float.MaxValue;

        float bestSqr = float.MaxValue;

        foreach (var n in nodes)
        {
            foreach (var e in n.edges)
            {
                var m = e.to;
                if (m == null) continue;
                if (n.blocked || m.blocked || e.blocked) continue;

                // Avoid duplicate directed edges (because you often add bidirectional edges)
                if (n.GetInstanceID() > m.GetInstanceID()) continue;

                ClosestPointOnSegment(worldPos, n.Position, m.Position, out Vector3 cp, out float tt);
                float dSqr = (worldPos - cp).sqrMagnitude;

                if (dSqr < bestSqr)
                {
                    bestSqr = dSqr;
                    a = n; b = m;
                    p = cp; t = tt;
                }
            }
        }

        if (a == null || b == null) return false;
        dist = Mathf.Sqrt(bestSqr);
        return true;
    }

    public TempAttachment CreateAttachmentNode(Vector3 anchorPos, float maxSnapDist = 999f, string name = "AttachNode")
    {
        if (!TryGetClosestEdge(anchorPos, out var A, out var B, out var P, out float t, out float d))
            return null;

        if (d > maxSnapDist) return null;

        // Create temp node at projected point P
        var go = new GameObject(name);
        go.transform.position = P;
        var attach = go.AddComponent<GraphNode>();

        // Flood state for this node (since it isn't in 'nodes' list)
        attach.blocked = (attach.EffectiveHeight < waterLevel);

        float len = Vector3.Distance(A.Position, B.Position);
        float costToA = t * len;
        float costToB = (1f - t) * len;

        // P -> A, P -> B (so start can leave)
        attach.edges.Add(new GraphEdge(A, costToA));
        attach.edges.Add(new GraphEdge(B, costToB));

        // A -> P, B -> P (so goal can be reached)
        var backA = new GraphEdge(attach, costToA);
        var backB = new GraphEdge(attach, costToB);
        A.edges.Add(backA);
        B.edges.Add(backB);

        var ta = new TempAttachment { node = attach };
        ta.addedBackEdges.Add((A, backA));
        ta.addedBackEdges.Add((B, backB));
        return ta;
    }
}
