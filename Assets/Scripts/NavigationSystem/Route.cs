using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Route
{
    public List<Road> roads = new List<Road>();
    public List<Vector2> roadVisibleTRanges = new List<Vector2>();
    public List<Intersection> intersections = new List<Intersection>();
    public float totalCost;
    public bool isValid;

    public void Clear()
    {
        roads.Clear();
        roadVisibleTRanges.Clear();
        intersections.Clear();
        totalCost = 0f;
        isValid = false;
    }

    public void ResetVisibleRanges()
    {
        roadVisibleTRanges.Clear();

        for (int i = 0; i < roads.Count; i++)
            roadVisibleTRanges.Add(new Vector2(0f, 1f));
    }

    public Vector2 GetVisibleTRange(int roadIndex)
    {
        if (roadIndex < 0 || roadIndex >= roads.Count)
            return new Vector2(0f, 1f);

        if (roadVisibleTRanges == null || roadVisibleTRanges.Count != roads.Count)
            return new Vector2(0f, 1f);

        return roadVisibleTRanges[roadIndex];
    }

    public void SetVisibleTRange(int roadIndex, float startT, float endT)
    {
        if (roadIndex < 0 || roadIndex >= roads.Count)
            return;

        if (roadVisibleTRanges == null)
            roadVisibleTRanges = new List<Vector2>();

        while (roadVisibleTRanges.Count < roads.Count)
            roadVisibleTRanges.Add(new Vector2(0f, 1f));

        roadVisibleTRanges[roadIndex] = new Vector2(
            Mathf.Clamp01(startT),
            Mathf.Clamp01(endT));
    }
}
