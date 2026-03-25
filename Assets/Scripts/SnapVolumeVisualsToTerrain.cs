using UnityEngine;

public class SnapVolumeVisualsToTerrain : MonoBehaviour
{
    [Header("References")]
    public Transform buildingsRoot;

    [Tooltip("Layer of the OLD mesh-terrain / ground collider")]
    public LayerMask terrainMask;

    [Header("Find target objects")]
    public string targetName = "Volume Visual";
    public bool includeInactive = true;

    [Header("Raycast")]
    public float castHeight = 500f;
    public float extraOffset = 0.01f;

    [Header("Sampling")]
    [Tooltip("Use center + 4 footprint corners. Better for large buildings.")]
    public bool sampleFootprint = true;

    [ContextMenu("Snap All Volume Visuals To Terrain")]
    public void SnapAll()
    {
        if (buildingsRoot == null)
        {
            Debug.LogError("Buildings root is not assigned.");
            return;
        }

        int total = 0;
        int snapped = 0;

        foreach (Transform t in buildingsRoot.GetComponentsInChildren<Transform>(includeInactive))
        {
            if (t == buildingsRoot) continue;
            if (t.name != targetName) continue;

            total++;

            if (SnapOne(t))
                snapped++;
            else
                Debug.LogWarning($"Could not snap: {GetHierarchyPath(t)}");
        }

        Debug.Log($"Snap finished. Snapped {snapped}/{total} objects.");
    }

    public bool SnapOne(Transform visualRoot)
    {
        if (visualRoot == null) return false;

        if (!TryGetCombinedBounds(visualRoot, out Bounds bounds))
            return false;

        if (!TryGetGroundY(bounds, out float groundY))
            return false;

        // Move only in world Y so X/Z stay exactly where they are
        float deltaY = (groundY + extraOffset) - bounds.min.y;
        visualRoot.position += new Vector3(0f, deltaY, 0f);

        return true;
    }

    bool TryGetGroundY(Bounds bounds, out float groundY)
    {
        groundY = 0f;

        Vector3[] samples;

        if (sampleFootprint)
        {
            samples = new Vector3[]
            {
                new Vector3(bounds.center.x, 0f, bounds.center.z), // center
                new Vector3(bounds.min.x,   0f, bounds.min.z),     // corner
                new Vector3(bounds.min.x,   0f, bounds.max.z),
                new Vector3(bounds.max.x,   0f, bounds.min.z),
                new Vector3(bounds.max.x,   0f, bounds.max.z)
            };
        }
        else
        {
            samples = new Vector3[]
            {
                new Vector3(bounds.center.x, 0f, bounds.center.z)
            };
        }

        bool hitAny = false;
        float highestY = float.NegativeInfinity;

        for (int i = 0; i < samples.Length; i++)
        {
            Vector3 origin = new Vector3(samples[i].x, bounds.max.y + castHeight, samples[i].z);

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, castHeight * 2f, terrainMask, QueryTriggerInteraction.Ignore))
            {
                hitAny = true;
                if (hit.point.y > highestY)
                    highestY = hit.point.y;
            }
        }

        if (!hitAny) return false;

        groundY = highestY;
        return true;
    }

    bool TryGetCombinedBounds(Transform root, out Bounds bounds)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);

        if (renderers == null || renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return true;
    }

    string GetHierarchyPath(Transform t)
    {
        if (t == null) return "(null)";

        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}