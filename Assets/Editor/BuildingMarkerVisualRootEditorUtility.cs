#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildingMarkerVisualRootEditorUtility
{
    public static int AutoAssignForMarkers(
        IEnumerable<BuildingMarker> markers,
        string preferredChildName = "Volume Visual",
        bool includeInactive = true)
    {
        if (markers == null) return 0;

        int changedCount = 0;

        foreach (BuildingMarker marker in markers)
        {
            if (marker == null) continue;

            Transform candidate = FindBestVisualRoot(
                marker.transform,
                preferredChildName,
                includeInactive);

            if (candidate == null)
                candidate = marker.transform;

            if (marker.visualRoot == candidate)
                continue;

            Undo.RecordObject(marker, "Auto Assign Building Visual Root");
            marker.visualRoot = candidate;
            EditorUtility.SetDirty(marker);

            if (marker.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);

            changedCount++;
        }

        return changedCount;
    }

    public static BuildingMarker[] GetAllMarkersInOpenScenes()
    {
        return UnityEngine.Object.FindObjectsByType<BuildingMarker>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
    }

    public static BuildingMarker[] GetMarkersUnderCurrentSelection(bool includeInactive = true)
    {
        HashSet<BuildingMarker> results = new HashSet<BuildingMarker>();

        foreach (GameObject go in Selection.gameObjects)
        {
            if (go == null) continue;

            BuildingMarker[] found = go.GetComponentsInChildren<BuildingMarker>(includeInactive);
            foreach (BuildingMarker marker in found)
            {
                if (marker != null)
                    results.Add(marker);
            }
        }

        BuildingMarker[] array = new BuildingMarker[results.Count];
        results.CopyTo(array);
        return array;
    }

    public static Transform FindBestVisualRoot(
        Transform root,
        string preferredChildName = "Volume Visual",
        bool includeInactive = true)
    {
        if (root == null) return null;

        Transform namedMatch = FindDeepChildByName(root, preferredChildName, includeInactive);
        if (namedMatch != null)
            return namedMatch;

        Renderer rendererMatch = FindFirstRendererInChildren(root, includeInactive);
        if (rendererMatch != null)
            return rendererMatch.transform;

        return root;
    }

    private static Transform FindDeepChildByName(
        Transform parent,
        string targetName,
        bool includeInactive)
    {
        if (parent == null || string.IsNullOrWhiteSpace(targetName))
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);

            if ((includeInactive || child.gameObject.activeInHierarchy) &&
                string.Equals(child.name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            Transform nested = FindDeepChildByName(child, targetName, includeInactive);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static Renderer FindFirstRendererInChildren(Transform root, bool includeInactive)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive);

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;

            // Prefer a child renderer over the marker root itself when possible
            if (r.transform != root)
                return r;
        }

        if (renderers.Length > 0)
            return renderers[0];

        return null;
    }
}
#endif