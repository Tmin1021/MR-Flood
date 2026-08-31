using UnityEditor;
using UnityEngine;

public static class MissingScriptCleanerTool
{
    [MenuItem("Tools/Cleanup/Remove Missing Scripts From Selected Object And Children")]
    public static void RemoveMissingScripts()
    {
        GameObject selected = Selection.activeGameObject;

        if (selected == null)
        {
            Debug.LogWarning("Please select a GameObject first.");
            return;
        }

        int removedCount = 0;

        Transform[] children = selected.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
            removedCount += removed;

            if (removed > 0)
            {
                EditorUtility.SetDirty(child.gameObject);
            }
        }

        Debug.Log($"Removed {removedCount} missing script component(s) from '{selected.name}' and its children.");

        AssetDatabase.SaveAssets();
    }
}