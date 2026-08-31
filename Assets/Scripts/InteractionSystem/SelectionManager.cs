using UnityEngine;

public class SelectionManager : MonoBehaviour
{
    [Header("References")]
    public NavigationManager navigationManager;
    public RouteVisualizer routeVisualizer;
    public MRNotification notifier;

    private CityBuilding startBuilding;
    private CityBuilding destinationBuilding;

    public void SelectBuilding(CityBuilding building)
    {
        if (building == null) return;

        if (startBuilding == null)
        {
            startBuilding = building;
            notifier?.Show($"Start selected: {GetBuildingName(building)}");
            return;
        }

        if (destinationBuilding == null && building != startBuilding)
        {
            destinationBuilding = building;
            notifier?.Show($"Destination selected: {GetBuildingName(building)}");
            return;
        }

        startBuilding = building;
        destinationBuilding = null;
        routeVisualizer?.ClearRoute();
        notifier?.Show($"Start reselected: {GetBuildingName(building)}");
    }

    public void ConfirmRoute()
    {
        if (startBuilding == null || destinationBuilding == null)
        {
            notifier?.Show("Please select start and destination.");
            return;
        }

        Route route = navigationManager.FindRoute(startBuilding, destinationBuilding);

        if (route == null || !route.isValid)
        {
            notifier?.Show("No safe route available.");
            routeVisualizer?.ClearRoute();
            return;
        }

        routeVisualizer?.DrawRoute(route, startBuilding, destinationBuilding);
        notifier?.Show("Route generated.");
    }

    public void ResetSelection()
    {
        startBuilding = null;
        destinationBuilding = null;
        routeVisualizer?.ClearRoute();
        notifier?.Show("Selection reset.");
    }

    private string GetBuildingName(CityBuilding building)
    {
        if (building == null) return "Unknown";
        if (!string.IsNullOrWhiteSpace(building.displayName)) return building.displayName;
        return building.id;
    }
}