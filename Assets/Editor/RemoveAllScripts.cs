using UnityEditor;
using UnityEngine;

public static class RemoveAllScripts
{
    [MenuItem("GameObject/Cleanup/Remove ALL Scripts From Selected Object And Children", false, 49)]
    [MenuItem("Tools/Cleanup/Remove ALL Scripts From Selected Object And Children")]
    private static void RemoveAllScriptsFromSelected()
    {
        GameObject root = Selection.activeGameObject;

        if (root == null)
        {
            Debug.LogWarning("Please select a GameObject first.");
            return;
        }

        bool confirm = EditorUtility.DisplayDialog(
            "Remove ALL Scripts?",
            $"This will remove ALL script components from:\n\n{root.name}\n\nand all of its children.\n\nBuilt-in components like Transform, MeshRenderer, MeshFilter, Collider, Material, etc. will stay.",
            "Remove Scripts",
            "Cancel"
        );

        if (!confirm)
            return;

        Undo.RegisterFullObjectHierarchyUndo(root, "Remove All Scripts From Hierarchy");

        int removedScripts = 0;
        int removedMissingScripts = 0;

        Transform[] allChildren = root.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in allChildren)
        {
            GameObject go = child.gameObject;

            // Remove missing scripts first
            int missing = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            removedMissingScripts += missing;

            // Remove normal script components
            MonoBehaviour[] scripts = go.GetComponents<MonoBehaviour>();

            for (int i = scripts.Length - 1; i >= 0; i--)
            {
                if (scripts[i] == null)
                    continue;

                Undo.DestroyObjectImmediate(scripts[i]);
                removedScripts++;
            }

            if (missing > 0)
                EditorUtility.SetDirty(go);
        }

        Debug.Log(
            $"Removed {removedScripts} script component(s) and {removedMissingScripts} missing script component(s) from '{root.name}' and children."
        );
    }
}