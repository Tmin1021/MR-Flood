using System.Collections.Generic;

public static class RouteRoadSpanBuilder
{
    public static List<RouteRoadSpan> Build(Route route)
    {
        List<RouteRoadSpan> spans = new List<RouteRoadSpan>();

        if (route == null || !route.isValid || route.roads == null || route.roads.Count == 0)
            return spans;

        RouteRoadSpan currentSpan = null;
        string lastName = null;

        for (int i = 0; i < route.roads.Count; i++)
        {
            Road road = route.roads[i];
            if (road == null) continue;

            string currentName = road.DisplayNameOrFallback;

            if (currentSpan == null || currentName != lastName)
            {
                currentSpan = new RouteRoadSpan
                {
                    roadName = currentName,
                    startRoadIndex = i
                };

                spans.Add(currentSpan);
                lastName = currentName;
            }

            currentSpan.roads.Add(road);
            currentSpan.endRoadIndex = i;
        }

        return spans;
    }
}
