using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public static class SaveHierarchyAsPrefabWithMeshes
{
    [MenuItem("Tools/Mesh/Save Selected Hierarchy As Prefab With Meshes")]
    public static void SaveSelectedHierarchy()
    {
        GameObject root = Selection.activeGameObject;

        if (root == null)
        {
            Debug.LogError("No GameObject selected. Please select the parent object first.");
            return;
        }

        string safeRootName = MakeSafeFileName(root.name);

        string baseFolder = $"Assets/Generated/PrefabBake/{safeRootName}";
        string meshFolder = $"{baseFolder}/Meshes";
        string materialFolder = $"{baseFolder}/Materials";
        string prefabFolder = "Assets/Prefabs";

        Directory.CreateDirectory(baseFolder);
        Directory.CreateDirectory(meshFolder);
        Directory.CreateDirectory(materialFolder);
        Directory.CreateDirectory(prefabFolder);

        Dictionary<Mesh, Mesh> savedMeshes = new Dictionary<Mesh, Mesh>();
        Dictionary<Material, Material> savedMaterials = new Dictionary<Material, Material>();

        int meshCount = 0;
        int materialCount = 0;

        // Save MeshFilter meshes
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter mf in meshFilters)
        {
            if (mf.sharedMesh == null) continue;

            Mesh savedMesh = SaveMeshIfNeeded(
                mf.sharedMesh,
                $"{mf.gameObject.name}_Mesh",
                meshFolder,
                savedMeshes,
                ref meshCount
            );

            mf.sharedMesh = savedMesh;
            EditorUtility.SetDirty(mf);
        }

        // Save MeshCollider meshes
        MeshCollider[] meshColliders = root.GetComponentsInChildren<MeshCollider>(true);
        foreach (MeshCollider mc in meshColliders)
        {
            if (mc.sharedMesh == null) continue;

            Mesh savedMesh = SaveMeshIfNeeded(
                mc.sharedMesh,
                $"{mc.gameObject.name}_ColliderMesh",
                meshFolder,
                savedMeshes,
                ref meshCount
            );

            mc.sharedMesh = savedMesh;
            EditorUtility.SetDirty(mc);
        }

        // Save scene-only materials
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            Material[] mats = r.sharedMaterials;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;

                mats[i] = SaveMaterialIfNeeded(
                    mats[i],
                    $"{r.gameObject.name}_Mat_{i}",
                    materialFolder,
                    savedMaterials,
                    ref materialCount
                );
            }

            r.sharedMaterials = mats;
            EditorUtility.SetDirty(r);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string prefabPath = $"{prefabFolder}/{safeRootName}.prefab";
        prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);

        bool success;
        PrefabUtility.SaveAsPrefabAssetAndConnect(
            root,
            prefabPath,
            InteractionMode.UserAction,
            out success
        );

        if (success)
        {
            Debug.Log(
                $"Prefab created successfully:\n{prefabPath}\n\n" +
                $"Saved meshes: {meshCount}\n" +
                $"Saved materials: {materialCount}"
            );
        }
        else
        {
            Debug.LogError("Failed to create prefab.");
        }
    }

    private static Mesh SaveMeshIfNeeded(
        Mesh source,
        string name,
        string folder,
        Dictionary<Mesh, Mesh> cache,
        ref int count)
    {
        if (source == null) return null;

        string existingPath = AssetDatabase.GetAssetPath(source);

        // Already a real asset
        if (!string.IsNullOrEmpty(existingPath))
            return source;

        if (cache.TryGetValue(source, out Mesh existingSavedMesh))
            return existingSavedMesh;

        Mesh meshCopy = Object.Instantiate(source);
        meshCopy.name = MakeSafeFileName(name);

        string assetPath = $"{folder}/{meshCopy.name}.asset";
        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

        AssetDatabase.CreateAsset(meshCopy, assetPath);

        cache[source] = meshCopy;
        count++;

        return meshCopy;
    }

    private static Material SaveMaterialIfNeeded(
        Material source,
        string name,
        string folder,
        Dictionary<Material, Material> cache,
        ref int count)
    {
        if (source == null) return null;

        string existingPath = AssetDatabase.GetAssetPath(source);

        // Already a real material asset
        if (!string.IsNullOrEmpty(existingPath))
            return source;

        if (cache.TryGetValue(source, out Material existingSavedMaterial))
            return existingSavedMaterial;

        Material materialCopy = Object.Instantiate(source);
        materialCopy.name = MakeSafeFileName(name);

        string assetPath = $"{folder}/{materialCopy.name}.mat";
        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

        AssetDatabase.CreateAsset(materialCopy, assetPath);

        cache[source] = materialCopy;
        count++;

        return materialCopy;
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name.Replace(" ", "_");
    }
}