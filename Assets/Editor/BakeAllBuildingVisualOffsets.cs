using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BakeAllBuildingVisualOffsets
{
    private const string MenuPath =
        "Tools/Transform/Bake All Building Visual Offsets";

    private const string VisualObjectName = "Volume Visual";

    [MenuItem(MenuPath)]
    private static void Execute()
    {
        Transform buildingsRoot = Selection.activeTransform;

        if (buildingsRoot == null)
        {
            Debug.LogWarning(
                "BakeAllBuildingVisualOffsets: Select the Buildings parent.");
            return;
        }

        List<Object> undoObjects = new List<Object>();
        List<Transform> buildingParents = new List<Transform>();
        List<Transform> volumeVisuals = new List<Transform>();

        int missingVisualCount = 0;

        for (int i = 0; i < buildingsRoot.childCount; i++)
        {
            Transform building = buildingsRoot.GetChild(i);

            if (building == null)
                continue;

            Transform volumeVisual = FindDirectOrNestedChild(
                building,
                VisualObjectName);

            if (volumeVisual == null)
            {
                missingVisualCount++;

                Debug.LogWarning(
                    $"BakeAllBuildingVisualOffsets: " +
                    $"'{building.name}' has no child named " +
                    $"'{VisualObjectName}'.",
                    building);

                continue;
            }

            buildingParents.Add(building);
            volumeVisuals.Add(volumeVisual);

            undoObjects.Add(building);
            undoObjects.Add(volumeVisual);
        }

        if (buildingParents.Count == 0)
        {
            Debug.LogWarning(
                $"BakeAllBuildingVisualOffsets: No valid buildings with " +
                $"'{VisualObjectName}' were found under '{buildingsRoot.name}'.");
            return;
        }

        Undo.RecordObjects(
            undoObjects.ToArray(),
            "Bake All Building Visual Offsets");

        int processedCount = 0;

        for (int i = 0; i < buildingParents.Count; i++)
        {
            Transform building = buildingParents[i];
            Transform volumeVisual = volumeVisuals[i];

            if (building == null || volumeVisual == null)
                continue;

            Vector3 originalVisualWorldPosition = volumeVisual.position;

            /*
             * Move the building parent to the visual's current world position.
             * This preserves correctness even if Buildings or another ancestor
             * has rotation or scale.
             */
            building.position = originalVisualWorldPosition;

            /*
             * The visual is now located at the building parent's origin.
             */
            volumeVisual.localPosition = Vector3.zero;

            PrefabUtility.RecordPrefabInstancePropertyModifications(building);
            PrefabUtility.RecordPrefabInstancePropertyModifications(volumeVisual);

            EditorUtility.SetDirty(building);
            EditorUtility.SetDirty(volumeVisual);

            processedCount++;
        }

        Debug.Log(
            $"BakeAllBuildingVisualOffsets: processed {processedCount} " +
            $"building(s) under '{buildingsRoot.name}'. " +
            $"Missing '{VisualObjectName}': {missingVisualCount}.",
            buildingsRoot);
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateExecute()
    {
        return Selection.activeTransform != null;
    }

    private static Transform FindDirectOrNestedChild(
        Transform root,
        string targetName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);

            if (child.name == targetName)
                return child;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDirectOrNestedChild(
                root.GetChild(i),
                targetName);

            if (found != null)
                return found;
        }

        return null;
    }
}