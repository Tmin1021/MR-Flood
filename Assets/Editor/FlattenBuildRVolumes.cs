using UnityEditor;
using UnityEngine;

public static class FlattenBuildRVolumes
{
    [MenuItem("Tools/City/Flatten Volume Hierarchy")]
    public static void FlattenSelectedRoot()
    {
        Transform root = Selection.activeTransform;

        if (root == null)
        {
            Debug.LogError("Select the Buildings root first.");
            return;
        }

        int buildingCount = 0;
        int flattenedCount = 0;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        for (int i = 0; i < root.childCount; i++)
        {
            Transform buildingRoot = root.GetChild(i);
            if (buildingRoot == null) continue;

            buildingCount++;

            Transform volume = buildingRoot.Find("Volume");
            if (volume == null) continue;

            // Move all children of Volume to the building root
            while (volume.childCount > 0)
            {
                Transform child = volume.GetChild(0);

                Undo.SetTransformParent(child, buildingRoot, "Move child out of Volume");
                child.SetParent(buildingRoot, true); // preserve world transform
            }

            Undo.DestroyObjectImmediate(volume.gameObject);
            flattenedCount++;

            EditorUtility.SetDirty(buildingRoot.gameObject);
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log(
            $"FlattenBuildRVolumes: scanned {buildingCount} buildings, flattened {flattenedCount} Volume objects."
        );
    }
}