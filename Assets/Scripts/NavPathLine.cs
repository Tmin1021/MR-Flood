using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(LineRenderer))]
public class NavPathLine : MonoBehaviour
{
    [Header("References")]
    public Transform user;          // Main Camera (or XR rig camera)
    public Transform target;        // NavTarget

    [Header("Line Settings")]
    public float yOffset = 0.05f;   // lift line above floor a bit
    public float updateInterval = 0.2f;
    public float startForwardOffset = 0.3f; // start line slightly in front of user

    private LineRenderer lr;
    private NavMeshPath path;
    private float timer;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.useWorldSpace = true;
        path = new NavMeshPath();
    }

    void Update()
    {
        if (user == null || target == null) return;

        timer += Time.deltaTime;
        if (timer < updateInterval) return;
        timer = 0f;

        // Start point slightly in front of the user to avoid line going under/behind you
        Vector3 start = user.position + user.forward * startForwardOffset;

        // Snap start & end to nearest NavMesh positions
        Vector3 navStart, navEnd;

        if (!TryGetNearestNavPoint(start, out navStart) ||
            !TryGetNearestNavPoint(target.position, out navEnd))
        {
            lr.positionCount = 0; // hide line if no navmesh
            return;
        }

        bool ok = NavMesh.CalculatePath(navStart, navEnd, NavMesh.AllAreas, path);
        if (!ok || path.corners == null || path.corners.Length < 2)
        {
            lr.positionCount = 0;
            return;
        }

        // Draw corners with a small Y offset
        lr.positionCount = path.corners.Length;
        for (int i = 0; i < path.corners.Length; i++)
        {
            Vector3 p = path.corners[i];
            p.y += yOffset;
            lr.SetPosition(i, p);
        }
    }

    bool TryGetNearestNavPoint(Vector3 pos, out Vector3 result)
    {
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
        {
            result = hit.position;
            return true;
        }
        result = Vector3.zero;
        return false;
    }
}
