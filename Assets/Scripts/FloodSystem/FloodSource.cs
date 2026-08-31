using UnityEngine;

public class FloodSource : MonoBehaviour
{
    [Header("Flood Source")]
    public float intensity = 1f;
    public float radius = 5f;
    public float growthRate = 0.25f;

    [Header("Optional")]
    public bool autoExpand = true;
    public bool usePlanarDistance = true;

    public bool ContainsPoint(Vector3 targetPosition)
    {
        return GetDistanceToPoint(targetPosition) <= radius;
    }

    public bool IntersectsSegment(Vector3 a, Vector3 b)
    {
        return GetDistanceToSegment(a, b) <= radius;
    }

    public float GetInfluence(Vector3 targetPosition)
    {
        float d = GetDistanceToPoint(targetPosition);
        if (d > radius) return 0f;

        float normalized = 1f - (d / radius);
        return intensity * normalized;
    }

    public float GetDistanceToPoint(Vector3 targetPosition)
    {
        Vector3 delta = targetPosition - transform.position;
        if (usePlanarDistance)
            delta.y = 0f;

        return delta.magnitude;
    }

    public float GetDistanceToSegment(Vector3 a, Vector3 b)
    {
        Vector3 sourcePosition = transform.position;

        if (usePlanarDistance)
        {
            sourcePosition.y = 0f;
            a.y = 0f;
            b.y = 0f;
        }

        Vector3 ab = b - a;
        float denom = Vector3.Dot(ab, ab);

        if (denom < 0.000001f)
            return Vector3.Distance(sourcePosition, a);

        float t = Mathf.Clamp01(Vector3.Dot(sourcePosition - a, ab) / denom);
        Vector3 closest = a + ab * t;
        return Vector3.Distance(sourcePosition, closest);
    }

    public void Advance(float deltaTime)
    {
        if (!autoExpand) return;
        radius += growthRate * deltaTime;
    }
}
