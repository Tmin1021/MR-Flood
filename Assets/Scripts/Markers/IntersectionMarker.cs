using UnityEngine;

public class IntersectionMarker : MonoBehaviour
{
    public string intersectionId;

    public string IntersectionIdOrFallback => string.IsNullOrWhiteSpace(intersectionId)
        ? BuildHierarchyFallbackId()
        : intersectionId;

    private string BuildHierarchyFallbackId()
    {
        // Scene object names are more stable than GetComponentsInChildren traversal
        // order and keep legacy scenes usable until explicit IDs are authored.
        string value = gameObject.name;
        Transform current = transform.parent;
        int depth = 0;

        while (current != null && depth < 4)
        {
            value = current.name + "/" + value;
            current = current.parent;
            depth++;
        }

        return "I_" + StableHash(value).ToString("X8");
    }

    private static uint StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619;
            }

            return hash;
        }
    }
}
