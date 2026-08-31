using System.Collections.Generic;
using UnityEngine;

public class RouteVisualizer : MonoBehaviour
{
    [Header("References")]
    public LineRenderer lineRenderer;

    [Header("Offsets")]
    public float verticalOffset = 0.05f;

    [Header("Options")]
    public bool includeStartAndDestinationBuildings = true;

    public void DrawRoute(Route route, CityBuilding startBuilding, CityBuilding destinationBuilding)
    {
        if (lineRenderer == null)
            return;

        if (route == null || !route.isValid)
        {
            ClearRoute();
            return;
        }

        List<Vector3> points = new List<Vector3>();

        if (includeStartAndDestinationBuildings && startBuilding != null)
            AddPointIfFarEnough(points, startBuilding.position + Vector3.up * verticalOffset);

        if (route.intersections != null && route.intersections.Count > 0)
        {
            for (int i = 0; i < route.intersections.Count; i++)
            {
                Intersection intersection = route.intersections[i];
                if (intersection == null) continue;

                AddPointIfFarEnough(points, intersection.position + Vector3.up * verticalOffset);
            }
        }

        if (includeStartAndDestinationBuildings && destinationBuilding != null)
            AddPointIfFarEnough(points, destinationBuilding.position + Vector3.up * verticalOffset);

        if (points.Count < 2)
        {
            ClearRoute();
            return;
        }

        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = points.Count;

        for (int i = 0; i < points.Count; i++)
            lineRenderer.SetPosition(i, points[i]);
    }

    public void ClearRoute()
    {
        if (lineRenderer == null) return;
        lineRenderer.positionCount = 0;
    }

    private void AddPointIfFarEnough(List<Vector3> points, Vector3 point, float minDistance = 0.0001f)
    {
        if (points.Count == 0)
        {
            points.Add(point);
            return;
        }

        if (Vector3.Distance(points[points.Count - 1], point) > minDistance)
            points.Add(point);
    }
}