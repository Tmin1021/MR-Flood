using System;
using UnityEngine;

[Serializable]
public class PhysicalObjectCandidate
{
    public string id;
    public Vector3 worldPosition;
    public Bounds worldBounds;
    public float approximateSize;
    public float distanceFromPlane;
    public bool isValid;
    public GameObject debugVisual;

    public PhysicalObjectCandidate()
    {
    }

    public PhysicalObjectCandidate(
        string id,
        Vector3 worldPosition,
        Bounds worldBounds,
        float approximateSize,
        float distanceFromPlane,
        bool isValid)
    {
        this.id = id;
        this.worldPosition = worldPosition;
        this.worldBounds = worldBounds;
        this.approximateSize = approximateSize;
        this.distanceFromPlane = distanceFromPlane;
        this.isValid = isValid;
    }
}
