using System;
using UnityEngine;

/// <summary>
/// Immutable route/selection state consumed by presentation-only city views.
/// Positions are canonical world positions so each view can map them through its
/// own coordinate root without owning graph or navigation data.
/// </summary>
public sealed class CityVisualizationSnapshot
{
    public bool HasStart { get; }
    public bool HasDestination { get; }
    public bool HasRoute { get; }
    public string StartBuildingId { get; }
    public string DestinationBuildingId { get; }
    public Vector3 StartWorldPosition { get; }
    public Vector3 DestinationWorldPosition { get; }
    public Vector3[] RouteWorldPoints { get; }
    public string[] RouteRoadIds { get; }
    public CityVisualizationLabel[] RouteLabels { get; }

    public CityVisualizationSnapshot(
        bool hasStart,
        bool hasDestination,
        bool hasRoute,
        string startBuildingId,
        string destinationBuildingId,
        Vector3 startWorldPosition,
        Vector3 destinationWorldPosition,
        Vector3[] routeWorldPoints,
        string[] routeRoadIds,
        CityVisualizationLabel[] routeLabels)
    {
        HasStart = hasStart;
        HasDestination = hasDestination;
        HasRoute = hasRoute;
        StartBuildingId = startBuildingId ?? string.Empty;
        DestinationBuildingId = destinationBuildingId ?? string.Empty;
        StartWorldPosition = startWorldPosition;
        DestinationWorldPosition = destinationWorldPosition;
        RouteWorldPoints = routeWorldPoints != null
            ? (Vector3[])routeWorldPoints.Clone()
            : Array.Empty<Vector3>();
        RouteRoadIds = routeRoadIds != null
            ? (string[])routeRoadIds.Clone()
            : Array.Empty<string>();
        RouteLabels = routeLabels != null
            ? (CityVisualizationLabel[])routeLabels.Clone()
            : Array.Empty<CityVisualizationLabel>();
    }
}

public readonly struct CityVisualizationLabel
{
    public string Text { get; }
    public Vector3 WorldPosition { get; }

    public CityVisualizationLabel(string text, Vector3 worldPosition)
    {
        Text = text ?? string.Empty;
        WorldPosition = worldPosition;
    }
}
