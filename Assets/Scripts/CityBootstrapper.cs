using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class CityBootstrapper : MonoBehaviour
{
    [Header("References")]
    public CityManager cityManager;

    [Header("Scene Roots")]
    public Transform intersectionsRoot;
    public Transform roadsRoot;
    public Transform buildingsRoot;

    [Header("Options")]
    [FormerlySerializedAs("buildOnAwake")]
    public bool buildOnStart = false;
    public bool clearExistingData = true;
    public bool autoLinkNearestRoadToBuildings = true;
    public bool autoLinkNearestIntersectionToBuildings = true;

    [Header("Debug")]
    public bool logSummary = true;

    public bool HasBuiltCity { get; private set; }
    public int BuildRevision { get; private set; }
    public event Action<int> CityBuilt;

    private void Start()
    {
        if (!buildOnStart)
        {
            Debug.Log("CityBootstrapper: buildOnStart is false. Waiting for placement confirmation before building.");
            return;
        }

        Debug.LogWarning("CityBootstrapper: buildOnStart is true. Auto-building on Start.");
        BuildCity();
    }

    [ContextMenu("Build City")]
    public void BuildCity()
    {
        Debug.Log("CityBootstrapper: BuildCity called.");

        if (buildOnStart && HasBuiltCity)
            Debug.LogWarning("CityBootstrapper: BuildCity was already run once while buildOnStart is enabled.");

        if (cityManager == null)
        {
            Debug.LogError("CityBootstrapper: CityManager is not assigned.");
            return;
        }

        if (clearExistingData)
        {
            cityManager.roads.Clear();
            cityManager.intersections.Clear();
            cityManager.buildings.Clear();
        }

        Dictionary<IntersectionMarker, Intersection> intersectionMap = BuildIntersections();
        BuildRoads(intersectionMap);
        BuildBuildings();

        cityManager.LinkRoadsToIntersections();
        cityManager.RecalculateRoadLengths();
        RefreshBuildingLinks();
        bool idsAreValid = cityManager.RebuildIdIndexes();
        HasBuiltCity = true;
        BuildRevision++;
        CityBuilt?.Invoke(BuildRevision);

        if (!idsAreValid)
            Debug.LogError("CityBootstrapper: city was built, but stable-ID validation failed. Visualization synchronization will ignore ambiguous items.");

        if (logSummary)
        {
            Debug.Log(
                $"CityBootstrapper: Built city with " +
                $"{cityManager.intersections.Count} intersections, " +
                $"{cityManager.roads.Count} roads, " +
                $"{cityManager.buildings.Count} buildings."
            );
        }
    }

    private Dictionary<IntersectionMarker, Intersection> BuildIntersections()
    {
        Dictionary<IntersectionMarker, Intersection> map = new Dictionary<IntersectionMarker, Intersection>();

        if (intersectionsRoot == null)
        {
            Debug.LogWarning("CityBootstrapper: Intersections Root is not assigned.");
            return map;
        }

        IntersectionMarker[] markers = intersectionsRoot.GetComponentsInChildren<IntersectionMarker>(true);

        for (int i = 0; i < markers.Length; i++)
        {
            IntersectionMarker marker = markers[i];
            if (marker == null) continue;

            string id = marker.IntersectionIdOrFallback;

            Intersection intersection = new Intersection
            {
                id = id,
                position = marker.transform.position,
                floodDepth = 0f,
                isBlocked = false,
                connectedRoads = new List<Road>()
            };

            cityManager.intersections.Add(intersection);
            map[marker] = intersection;
        }

        return map;
    }

    private void BuildRoads(Dictionary<IntersectionMarker, Intersection> intersectionMap)
    {
        if (roadsRoot == null)
        {
            Debug.LogWarning("CityBootstrapper: Roads Root is not assigned.");
            return;
        }

        RoadMarker[] markers = roadsRoot.GetComponentsInChildren<RoadMarker>(true);

        for (int i = 0; i < markers.Length; i++)
        {
            RoadMarker marker = markers[i];
            if (marker == null) continue;

            if (marker.startMarker == null || marker.endMarker == null)
            {
                Debug.LogWarning($"CityBootstrapper: RoadMarker '{marker.name}' is missing start or end marker.");
                continue;
            }

            if (!intersectionMap.TryGetValue(marker.startMarker, out Intersection startIntersection))
            {
                Debug.LogWarning($"CityBootstrapper: Start intersection not found for road '{marker.name}'.");
                continue;
            }

            if (!intersectionMap.TryGetValue(marker.endMarker, out Intersection endIntersection))
            {
                Debug.LogWarning($"CityBootstrapper: End intersection not found for road '{marker.name}'.");
                continue;
            }

            string id = marker.RoadIdOrFallback;

            string displayName = string.IsNullOrWhiteSpace(marker.displayName)
                ? marker.gameObject.name
                : marker.displayName;

            Road road = new Road
            {
                id = id,
                displayName = displayName,
                start = startIntersection,
                end = endIntersection,
                elevation = marker.transform.position.y,
                floodDepth = 0f,
                isBlocked = false,
                riskCost = 0f,
                connectedBuildings = new List<CityBuilding>(),
                marker = marker
            };

            road.RecalculateLength();
            cityManager.roads.Add(road);
        }
    }

    private void BuildBuildings()
    {
        if (buildingsRoot == null)
        {
            Debug.LogWarning("CityBootstrapper: Buildings Root is not assigned.");
            return;
        }

        BuildingMarker[] markers = buildingsRoot.GetComponentsInChildren<BuildingMarker>(true);

        if (markers == null || markers.Length == 0)
        {
            Debug.LogWarning("CityBootstrapper: No BuildingMarker found under Buildings Root.");
            return;
        }

        for (int i = 0; i < markers.Length; i++)
        {
            BuildingMarker marker = markers[i];
            if (marker == null) continue;

            CityBuilding building = new CityBuilding
            {
                id = marker.BuildingIdOrFallback,
                displayName = marker.DisplayNameOrFallback,
                position = marker.GetRepresentativeWorldPosition(),
                baseHeight = marker.GetBaseWorldY(),
                marker = marker,
                floodDepth = 0f,
                isFlooded = false,
                nearestRoad = null,
                nearestIntersection = null
            };

            cityManager.buildings.Add(building);
        }
    }

    private void RefreshBuildingLinks()
    {
        foreach (Road road in cityManager.roads)
        {
            if (road == null) continue;

            if (road.connectedBuildings == null)
                road.connectedBuildings = new List<CityBuilding>();
            else
                road.connectedBuildings.Clear();
        }

        foreach (CityBuilding building in cityManager.buildings)
        {
            if (building == null) continue;

            if (autoLinkNearestRoadToBuildings)
                building.nearestRoad = cityManager.GetClosestRoad(building.position);

            building.nearestIntersection = ResolveBestEntryIntersection(building);

            if (building.nearestRoad != null)
            {
                if (building.nearestRoad.connectedBuildings == null)
                    building.nearestRoad.connectedBuildings = new List<CityBuilding>();

                if (!building.nearestRoad.connectedBuildings.Contains(building))
                    building.nearestRoad.connectedBuildings.Add(building);
            }
        }
    }

    private Intersection ResolveBestEntryIntersection(CityBuilding building)
    {
        if (building == null) return null;

        Road road = building.nearestRoad;
        if (road != null)
        {
            Intersection a = road.start;
            Intersection b = road.end;

            if (a != null && b != null)
            {
                float da = Vector3.Distance(building.position, a.position);
                float db = Vector3.Distance(building.position, b.position);
                return da <= db ? a : b;
            }

            if (a != null) return a;
            if (b != null) return b;
        }

        if (autoLinkNearestIntersectionToBuildings)
            return cityManager.GetClosestIntersection(building.position);

        return null;
    }
}
