using System.Collections.Generic;
using UnityEngine;

public class RouteRoadSpan
{
    public string roadName;
    public int startRoadIndex = -1;
    public int endRoadIndex = -1;
    public readonly List<Road> roads = new List<Road>();

    public float TotalLength
    {
        get
        {
            float sum = 0f;

            foreach (Road road in roads)
            {
                if (road != null)
                    sum += Mathf.Max(road.length, 0f);
            }

            return sum;
        }
    }

    public Vector3 GetAverageAnchor(float verticalOffset)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (Road road in roads)
        {
            if (road == null) continue;
            sum += road.GetLabelAnchor(verticalOffset);
            count++;
        }

        return count > 0 ? sum / count : Vector3.zero;
    }
}
