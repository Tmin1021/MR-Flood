using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Road
{
    public string id;
    public string displayName;

    [NonSerialized] public Intersection start;
    [NonSerialized] public Intersection end;

    public float length;
    public float elevation;

    [Header("Flood State")]
    public float floodDepth;
    public bool isBlocked;
    public float riskCost;

    [Header("City Relations")]
    [NonSerialized] public List<CityBuilding> connectedBuildings = new List<CityBuilding>();

    [NonSerialized] public RoadMarker marker;

    public Vector3 Center
    {
        get
        {
            if (start == null || end == null) return Vector3.zero;
            return (start.position + end.position) * 0.5f;
        }
    }

    public string DisplayNameOrFallback
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;

            if (!string.IsNullOrWhiteSpace(id))
                return id;

            return "Unknown road";
        }
    }

    public void RecalculateLength()
    {
        if (start != null && end != null)
            length = Vector3.Distance(start.position, end.position);
    }

    public Intersection GetOtherIntersection(Intersection current)
    {
        if (current == start) return end;
        if (current == end) return start;
        return null;
    }

    public Vector3 GetPointAlong(float t)
    {
        if (marker != null && marker.startMarker != null && marker.endMarker != null)
        {
            Vector3 a = marker.startMarker.transform.position;
            Vector3 b = marker.endMarker.transform.position;
            return Vector3.Lerp(a, b, Mathf.Clamp01(t));
        }

        if (start != null && end != null)
            return Vector3.Lerp(start.position, end.position, Mathf.Clamp01(t));

        return Center;
    }

    public Vector3 GetLabelAnchor(float t = 0.5f, float verticalOffset = 0.03f)
    {
        return GetPointAlong(t) + Vector3.up * verticalOffset;
    }

    public float GetNormalizedTForPoint(Vector3 worldPoint)
    {
        Vector3 a;
        Vector3 b;

        if (marker != null && marker.startMarker != null && marker.endMarker != null)
        {
            a = marker.startMarker.transform.position;
            b = marker.endMarker.transform.position;
        }
        else if (start != null && end != null)
        {
            a = start.position;
            b = end.position;
        }
        else
        {
            return 0.5f;
        }

        Vector3 ab = b - a;
        float denom = Vector3.Dot(ab, ab);

        if (denom < 0.000001f)
            return 0.5f;

        return Mathf.Clamp01(Vector3.Dot(worldPoint - a, ab) / denom);
    }

    public Vector3 GetDirection()
    {
        Vector3 a;
        Vector3 b;

        if (marker != null && marker.startMarker != null && marker.endMarker != null)
        {
            a = marker.startMarker.transform.position;
            b = marker.endMarker.transform.position;
        }
        else if (start != null && end != null)
        {
            a = start.position;
            b = end.position;
        }
        else
        {
            return Vector3.forward;
        }

        Vector3 dir = b - a;
        return dir.sqrMagnitude > 0.000001f ? dir.normalized : Vector3.forward;
    }
}
