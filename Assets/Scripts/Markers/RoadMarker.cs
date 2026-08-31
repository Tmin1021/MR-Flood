using UnityEngine;

public class RoadMarker : MonoBehaviour
{
    public string roadId;
    public string displayName;

    public IntersectionMarker startMarker;
    public IntersectionMarker endMarker;

    public string RoadIdOrFallback => string.IsNullOrWhiteSpace(roadId)
        ? gameObject.name
        : roadId;
}
