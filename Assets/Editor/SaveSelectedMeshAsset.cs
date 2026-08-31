using UnityEngine;
using UnityEditor;
using System.IO;

public static class SaveSelectedMeshAsset
{
    [MenuItem("Tools/Mesh/Save Selected Mesh As Asset")]
    public static void SaveMesh()
    {
        GameObject selected = Selection.activeGameObject;

        if (selected == null)
        {
            Debug.LogError("No GameObject selected.");
            return;
        }

        MeshFilter meshFilter = selected.GetComponent<MeshFilter>();
        MeshCollider meshCollider = selected.GetComponent<MeshCollider>();

        Mesh sourceMesh = null;

        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            sourceMesh = meshFilter.sharedMesh;
        }
        else if (meshCollider != null && meshCollider.sharedMesh != null)
        {
            sourceMesh = meshCollider.sharedMesh;
        }

        if (sourceMesh == null)
        {
            Debug.LogError("Selected object has no mesh in MeshFilter or MeshCollider.");
            return;
        }

        string folderPath = "Assets/Generated/Meshes";

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string assetPath = $"{folderPath}/{selected.name}_SavedMesh.asset";
        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

        Mesh meshCopy = Object.Instantiate(sourceMesh);
        meshCopy.name = selected.name + "_SavedMesh";

        AssetDatabase.CreateAsset(meshCopy, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (meshFilter != null)
            meshFilter.sharedMesh = meshCopy;

        if (meshCollider != null)
            meshCollider.sharedMesh = meshCopy;

        EditorUtility.SetDirty(selected);

        Debug.Log($"Saved mesh asset to: {assetPath}");
    }
}