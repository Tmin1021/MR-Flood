#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class NodeSnapTools
{
    [MenuItem("Tools/Graph/Snap Selected Nodes To Ground %#g")]
    static void SnapSelected()
    {
        int groundLayer = LayerMask.NameToLayer("Ground"); // Assigned the layer Ground
        if (groundLayer == -1)
        {
            Debug.LogError("Layer 'Ground' not found. Create it and assign your mesh to it.");
            return;
        }

        var mask = 1 << groundLayer;

        foreach (var obj in Selection.gameObjects)
        {
            Undo.RecordObject(obj.transform, "Snap Node To Ground");

            // Cast from above downwards
            Vector3 origin = obj.transform.position + Vector3.up * 500f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2000f, mask))
            {
                obj.transform.position = hit.point; // + Vector3.up * offset if you want
            }
            else
            {
                Debug.LogWarning($"No ground hit for {obj.name}. Is the mesh on Ground layer and has MeshCollider?");
            }
        }
    }
}
#endif