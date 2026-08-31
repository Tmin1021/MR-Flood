using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Intersection
{
    public string id;
    public Vector3 position;

    [Header("Flood State")]
    public float floodDepth;
    public bool isBlocked;

    [Header("Connections")]
    [NonSerialized] public List<Road> connectedRoads = new List<Road>();

    public void AddRoad(Road road)
    {
        if (road == null) return;
        if (!connectedRoads.Contains(road))
            connectedRoads.Add(road);
    }
}
