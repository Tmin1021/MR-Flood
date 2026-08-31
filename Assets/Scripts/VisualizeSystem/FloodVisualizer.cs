using UnityEngine;

public class FloodVisualizer : MonoBehaviour
{
    [Header("References")]
    public CityManager cityManager;

    [Header("Optional Debug")]
    public bool drawRoadLines = true;
    public float roadYOffset = 0.03f;

    private void OnDrawGizmos()
    {
        if (!drawRoadLines || cityManager == null || cityManager.roads == null) return;

        foreach (var road in cityManager.roads)
        {
            if (road == null || road.start == null || road.end == null) continue;

            if (road.isBlocked)
                Gizmos.color = Color.red;
            else if (road.floodDepth > 0.25f)
                Gizmos.color = Color.yellow;
            else
                Gizmos.color = Color.green;

            Vector3 a = road.start.position + Vector3.up * roadYOffset;
            Vector3 b = road.end.position + Vector3.up * roadYOffset;
            Gizmos.DrawLine(a, b);
        }
    }
}