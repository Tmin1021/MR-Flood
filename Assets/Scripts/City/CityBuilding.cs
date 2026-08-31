using UnityEngine;
using System;


[Serializable]
public class CityBuilding
{
    public string id;
    public string displayName;

    public Vector3 position;
    public float baseHeight;
    [NonSerialized] public BuildingMarker marker;

    [Header("Flood State")]
    public float floodDepth;
    public bool isFlooded;

    [Header("Navigation Links")]
    [NonSerialized] public Road nearestRoad;
    [NonSerialized] public Intersection nearestIntersection;
}
