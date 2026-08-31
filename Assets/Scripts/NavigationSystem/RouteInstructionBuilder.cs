using System.Collections.Generic;
using System.Text;

public static class RouteInstructionBuilder
{
    public static List<string> BuildInstructions(Route route)
    {
        List<string> instructions = new List<string>();

        if (route == null || !route.isValid || route.roads == null || route.roads.Count == 0)
        {
            instructions.Add("No safe route available.");
            return instructions;
        }

        List<string> streetNames = BuildStreetNames(route);

        instructions.Add("Route found.");

        for (int i = 0; i < streetNames.Count; i++)
        {
            if (i == 0)
                instructions.Add($"Proceed along {streetNames[i]}.");
            else
                instructions.Add($"Continue on {streetNames[i]}.");
        }

        instructions.Add("Destination reached.");
        return instructions;
    }

    public static string BuildBoardText(Route route, string startName, string destinationName)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine($"From: {startName}");
        sb.AppendLine($"To: {destinationName}");
        sb.AppendLine();

        if (route == null || !route.isValid || route.roads == null || route.roads.Count == 0)
        {
            sb.AppendLine("No safe route available.");
            return sb.ToString();
        }

        sb.AppendLine("Road segments used:");
        for (int i = 0; i < route.roads.Count; i++)
        {
            Road road = route.roads[i];
            sb.AppendLine($"{i + 1}. {GetRoadLabel(road)}");
        }

        sb.AppendLine();
        sb.AppendLine("Street names in order:");

        List<string> streetNames = BuildStreetNames(route);
        for (int i = 0; i < streetNames.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {streetNames[i]}");
        }

        return sb.ToString();
    }

    private static List<string> BuildStreetNames(Route route)
    {
        List<string> names = new List<string>();
        string lastName = null;

        if (route == null || route.roads == null)
            return names;

        foreach (Road road in route.roads)
        {
            string currentName = GetRoadName(road);

            if (currentName != lastName)
            {
                names.Add(currentName);
                lastName = currentName;
            }
        }

        return names;
    }

    private static string GetRoadName(Road road)
    {
        if (road == null) return "Unknown road";
        return road.DisplayNameOrFallback;
    }

    private static string GetRoadLabel(Road road)
    {
        if (road == null) return "Unknown road";

        string name = GetRoadName(road);

        if (string.IsNullOrWhiteSpace(road.id))
            return name;

        return $"{name} [{road.id}]";
    }
}
