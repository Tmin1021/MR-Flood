#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildingVisualBaker
{
    private const string RootFolder = "Assets/Generated";
    private const string OutputFolder = "Assets/Generated/BakedBuildings";

    [MenuItem("Tools/City/Bake Selected BuildingMarkers To Mesh Assets")]
    public static void BakeSelectedBuildingMarkers()
    {
        List<BuildingMarker> markers = GetSelectedMarkers();

        if (markers.Count == 0)
        {
            Debug.LogWarning(
                "BuildingVisualBaker: No BuildingMarker found in the current selection."
            );
            return;
        }

        EnsureFolder(RootFolder);
        EnsureFolder(OutputFolder);

        int bakedCount = 0;
        int skippedCount = 0;

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (BuildingMarker marker in markers)
            {
                bool success = BakeMarker(marker);
                if (success) bakedCount++;
                else skippedCount++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log(
            $"BuildingVisualBaker: Finished. Baked {bakedCount} building(s), skipped {skippedCount}."
        );
    }

    private static List<BuildingMarker> GetSelectedMarkers()
    {
        HashSet<BuildingMarker> result = new HashSet<BuildingMarker>();

        foreach (GameObject go in Selection.gameObjects)
        {
            if (go == null) continue;

            BuildingMarker[] found = go.GetComponentsInChildren<BuildingMarker>(true);
            foreach (BuildingMarker marker in found)
            {
                if (marker != null)
                    result.Add(marker);
            }
        }

        return new List<BuildingMarker>(result);
    }

    private static bool BakeMarker(BuildingMarker marker)
    {
        if (marker == null)
            return false;

        Transform sourceRoot = marker.visualRoot;

        if (sourceRoot == null)
        {
            Debug.LogWarning(
                $"BuildingVisualBaker: '{marker.name}' has no visualRoot assigned. Skipped."
            );
            return false;
        }

        if (sourceRoot == marker.transform)
        {
            Debug.LogWarning(
                $"BuildingVisualBaker: '{marker.name}' uses its own root as visualRoot. " +
                $"This script expects a child visual root like 'Volume Visual'. Skipped."
            );
            return false;
        }

        MeshRenderer[] renderers = sourceRoot.GetComponentsInChildren<MeshRenderer>(true);

        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning(
                $"BuildingVisualBaker: '{marker.name}' has no MeshRenderer under visualRoot. Skipped."
            );
            return false;
        }

        GameObject bakedGo = new GameObject("BakedVisual");
        Undo.RegisterCreatedObjectUndo(bakedGo, "Create Baked Visual");

        bakedGo.transform.SetParent(marker.transform, false);
        bakedGo.transform.localPosition = Vector3.zero;
        bakedGo.transform.localRotation = Quaternion.identity;
        bakedGo.transform.localScale = Vector3.one;

        Dictionary<Material, List<CombineInstance>> combinesByMaterial =
            new Dictionary<Material, List<CombineInstance>>();

        List<Material> orderedMaterials = new List<Material>();

        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer == null) continue;

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            Mesh sourceMesh = filter.sharedMesh;
            Material[] mats = renderer.sharedMaterials;
            int subMeshCount = Mathf.Min(sourceMesh.subMeshCount, mats.Length);

            for (int sub = 0; sub < subMeshCount; sub++)
            {
                Material mat = mats[sub];
                if (mat == null) continue;

                if (!combinesByMaterial.TryGetValue(mat, out List<CombineInstance> list))
                {
                    list = new List<CombineInstance>();
                    combinesByMaterial.Add(mat, list);
                    orderedMaterials.Add(mat);
                }

                CombineInstance ci = new CombineInstance
                {
                    mesh = sourceMesh,
                    subMeshIndex = sub,
                    transform = bakedGo.transform.worldToLocalMatrix * renderer.localToWorldMatrix
                };

                list.Add(ci);
            }
        }

        if (orderedMaterials.Count == 0)
        {
            Undo.DestroyObjectImmediate(bakedGo);
            Debug.LogWarning(
                $"BuildingVisualBaker: '{marker.name}' produced no valid mesh data. Skipped."
            );
            return false;
        }

        List<Mesh> temporarySubMeshes = new List<Mesh>();
        List<CombineInstance> finalCombines = new List<CombineInstance>();

        for (int i = 0; i < orderedMaterials.Count; i++)
        {
            Material mat = orderedMaterials[i];
            List<CombineInstance> group = combinesByMaterial[mat];

            Mesh subMesh = new Mesh
            {
                name = $"{SanitizeName(marker.BuildingIdOrFallback)}_Sub_{i}"
            };
            subMesh.indexFormat = IndexFormat.UInt32;
            subMesh.CombineMeshes(group.ToArray(), true, true, false);

            temporarySubMeshes.Add(subMesh);

            finalCombines.Add(new CombineInstance
            {
                mesh = subMesh,
                subMeshIndex = 0,
                transform = Matrix4x4.identity
            });
        }

        Mesh bakedMesh = new Mesh
        {
            name = $"{SanitizeName(marker.BuildingIdOrFallback)}_Baked"
        };
        bakedMesh.indexFormat = IndexFormat.UInt32;
        bakedMesh.CombineMeshes(finalCombines.ToArray(), false, false, false);
        bakedMesh.RecalculateBounds();

        string assetPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{OutputFolder}/{SanitizeName(marker.BuildingIdOrFallback)}_Baked.asset"
        );
        AssetDatabase.CreateAsset(bakedMesh, assetPath);

        foreach (Mesh temp in temporarySubMeshes)
        {
            if (temp != null)
                Object.DestroyImmediate(temp);
        }

        MeshFilter bakedFilter = bakedGo.AddComponent<MeshFilter>();
        MeshRenderer bakedRenderer = bakedGo.AddComponent<MeshRenderer>();

        bakedFilter.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        bakedRenderer.sharedMaterials = orderedMaterials.ToArray();

        Undo.RecordObject(marker, "Assign Baked Visual");
        marker.visualRoot = bakedGo.transform;

        Undo.DestroyObjectImmediate(sourceRoot.gameObject);

        EditorUtility.SetDirty(marker);
        EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);

        return true;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string[] parts = path.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    private static string SanitizeName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Building";

        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            input = input.Replace(c, '_');

        return input.Replace(" ", "_");
    }
}
#endif