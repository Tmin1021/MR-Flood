using System.Collections.Generic;
using UnityEngine;

public class GraphNode : MonoBehaviour
{
    [Header("Flood")]
    public bool blocked = false;

    [Tooltip("Optional height offset if your pivots are not on the ground.")]
    public float heightOffset = 0f;

    [System.NonSerialized] public readonly List<GraphEdge> edges = new List<GraphEdge>();

    public Vector3 Position => transform.position;
    public float EffectiveHeight => transform.position.y + heightOffset;
}

[System.Serializable]
public class GraphEdge
{
    public GraphNode to;
    public float cost;
    public bool blocked;

    public GraphEdge(GraphNode to, float cost, bool blocked = false)
    {
        this.to = to;
        this.cost = cost;
        this.blocked = blocked;
    }
}