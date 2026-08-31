using UnityEngine;

public class BuildingMarker : MonoBehaviour
{
    [Header("Identity")]
    public string buildingId;
    public string displayName;

    [Header("Visual")]
    [Tooltip("Assign the actual visual object of this building here. If left empty, the root object is used.")]
    public Transform visualRoot;

    public string BuildingIdOrFallback =>
        string.IsNullOrWhiteSpace(buildingId) ? gameObject.name : buildingId;

    public string DisplayNameOrFallback =>
        string.IsNullOrWhiteSpace(displayName) ? BuildingIdOrFallback : displayName;

    public Transform VisualRoot => visualRoot != null ? visualRoot : transform;

    public bool TryGetWorldBounds(out Bounds bounds)
    {
        Renderer[] rends = VisualRoot.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            bounds.Encapsulate(rends[i].bounds);

        return true;
    }

    public float GetBaseWorldY()
    {
        Renderer[] rends = VisualRoot.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
            return transform.position.y;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);

        return b.min.y;
    }

    public Vector3 GetRepresentativeWorldPosition()
    {
        Renderer[] rends = VisualRoot.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
            return transform.position;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);

        return new Vector3(b.center.x, b.min.y, b.center.z);
    }

    public Vector3 GetTopWorldPosition()
    {
        Renderer[] rends = VisualRoot.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
            return transform.position;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);

        return new Vector3(b.center.x, b.max.y, b.center.z);
    }
}
