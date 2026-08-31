using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class RouteWorldLabelPresenter : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public GameObject labelPrefab;
    public Transform labelRoot;

    [Header("Layout")]
    public float verticalOffset = 0.03f;
    public float repeatedLabelSpacing = 0.25f;
    public bool mergeConsecutiveSameStreet = true;
    public bool hideBlockedRoadLabels = false;

    private readonly List<GameObject> activeLabels = new List<GameObject>();
    private bool presentationVisible = true;

    public void SetPresentationVisible(bool visible)
    {
        presentationVisible = visible;
        for (int i = 0; i < activeLabels.Count; i++)
        {
            if (activeLabels[i] != null)
                activeLabels[i].SetActive(visible);
        }
    }

    public void ShowRouteLabels(Route route)
    {
        ClearLabels();

        if (labelPrefab == null || route == null || !route.isValid || route.roads == null || route.roads.Count == 0)
            return;

        if (mergeConsecutiveSameStreet)
        {
            List<RouteRoadSpan> spans = RouteRoadSpanBuilder.Build(route);
            foreach (RouteRoadSpan span in spans)
                SpawnLabelsForSpan(route, span);
        }
        else
        {
            for (int i = 0; i < route.roads.Count; i++)
            {
                Road road = route.roads[i];
                if (road == null) continue;
                if (hideBlockedRoadLabels && road.isBlocked) continue;

                SpawnSingleLabel(
                    GetAnchorAlongVisibleRange(route, i, road, 0.5f),
                    road.DisplayNameOrFallback);
            }
        }
    }

    public void ClearLabels()
    {
        for (int i = 0; i < activeLabels.Count; i++)
        {
            if (activeLabels[i] != null)
                Destroy(activeLabels[i]);
        }

        activeLabels.Clear();
    }

    private void SpawnLabelsForSpan(Route route, RouteRoadSpan span)
    {
        if (route == null || span == null || span.roads.Count == 0)
            return;

        float totalLength = GetVisibleSpanLength(route, span);

        if (totalLength <= 0.0001f)
            return;

        if (totalLength <= repeatedLabelSpacing)
        {
            SpawnSingleLabel(
                GetAnchorAlongSpanDistance(route, span, totalLength * 0.5f),
                span.roadName);
            return;
        }

        if (repeatedLabelSpacing <= 0.0001f)
        {
            SpawnSingleLabel(
                GetAnchorAlongSpanDistance(route, span, totalLength * 0.5f),
                span.roadName);
            return;
        }

        float nextSpawnDistance = repeatedLabelSpacing * 0.5f;

        while (nextSpawnDistance < totalLength)
        {
            SpawnSingleLabel(
                GetAnchorAlongSpanDistance(route, span, nextSpawnDistance),
                span.roadName);
            nextSpawnDistance += repeatedLabelSpacing;
        }
    }

    private float GetVisibleSpanLength(Route route, RouteRoadSpan span)
    {
        float total = 0f;

        for (int i = span.startRoadIndex; i <= span.endRoadIndex; i++)
        {
            Road road = route.roads[i];
            if (road == null) continue;
            if (hideBlockedRoadLabels && road.isBlocked) continue;

            Vector2 visibleRange = route.GetVisibleTRange(i);
            total += Mathf.Max(road.length * Mathf.Abs(visibleRange.y - visibleRange.x), 0f);
        }

        return total;
    }

    private Vector3 GetAnchorAlongSpanDistance(Route route, RouteRoadSpan span, float distance)
    {
        float remaining = Mathf.Max(distance, 0f);

        for (int i = span.startRoadIndex; i <= span.endRoadIndex; i++)
        {
            Road road = route.roads[i];
            if (road == null) continue;
            if (hideBlockedRoadLabels && road.isBlocked) continue;

            Vector2 visibleRange = route.GetVisibleTRange(i);
            float visibleLength = Mathf.Max(road.length * Mathf.Abs(visibleRange.y - visibleRange.x), 0f);

            if (visibleLength <= 0.0001f)
                continue;

            if (remaining <= visibleLength)
            {
                float normalized = remaining / visibleLength;
                return GetAnchorAlongVisibleRange(route, i, road, normalized);
            }

            remaining -= visibleLength;
        }

        for (int i = span.endRoadIndex; i >= span.startRoadIndex; i--)
        {
            Road road = route.roads[i];
            if (road == null) continue;
            if (hideBlockedRoadLabels && road.isBlocked) continue;

            return GetAnchorAlongVisibleRange(route, i, road, 0.5f);
        }

        return Vector3.zero;
    }

    private Vector3 GetAnchorAlongVisibleRange(Route route, int roadIndex, Road road, float normalized)
    {
        Vector2 visibleRange = route.GetVisibleTRange(roadIndex);
        float t = Mathf.Lerp(visibleRange.x, visibleRange.y, Mathf.Clamp01(normalized));
        return road.GetLabelAnchor(t, verticalOffset);
    }

    private void SpawnSingleLabel(Vector3 worldPosition, string labelText)
    {
        Transform parent = ResolveSpawnParent();

        GameObject instance = parent != null
            ? Instantiate(labelPrefab, worldPosition, Quaternion.identity, parent)
            : Instantiate(labelPrefab, worldPosition, Quaternion.identity);

        TMP_Text tmp = instance.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
            tmp.text = labelText;
        else
            Debug.LogWarning($"RouteWorldLabelPresenter: No TMP_Text found on label prefab '{instance.name}'.");

        RouteLabelBillboard billboard = instance.GetComponent<RouteLabelBillboard>();
        if (billboard == null)
            billboard = instance.AddComponent<RouteLabelBillboard>();

        billboard.targetCamera = targetCamera;

        activeLabels.Add(instance);
        instance.SetActive(presentationVisible);
    }

    private Transform ResolveSpawnParent()
    {
        if (labelRoot != null && labelRoot.gameObject.scene.IsValid())
            return labelRoot;

        if (gameObject.scene.IsValid())
            return transform;

        return null;
    }
}
