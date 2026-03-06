#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class TerrainConverter : EditorWindow
{
    // Increase this to reduce mesh density (1 = full res, 2 = half, 4 = quarter)
    private const int Step = 2;

    [MenuItem("Tools/Terrain/Convert Selected Terrain To Mesh")]
    private static void ConvertSelectedTerrainToMesh()
    {
        var terrain = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<Terrain>() : null;
        if (!terrain) { Debug.LogError("Select a Terrain object in the Hierarchy first."); return; }

        TerrainData td = terrain.terrainData;
        int hmRes = td.heightmapResolution;

        int w = Mathf.Max(2, (hmRes - 1) / Step + 1);
        int h = Mathf.Max(2, (hmRes - 1) / Step + 1);

        float[,] heights = td.GetHeights(0, 0, hmRes, hmRes);
        Vector3 size = td.size;

        var vertices = new Vector3[w * h];
        var uvs = new Vector2[w * h];
        var tris = new int[(w - 1) * (h - 1) * 6];

        // Build vertices in local space (same pivot as terrain: bottom-left corner)
        int vi = 0;
        for (int z = 0; z < h; z++)
        {
            int hz = Mathf.Min(hmRes - 1, z * Step);
            float zPos = (hz / (float)(hmRes - 1)) * size.z;

            for (int x = 0; x < w; x++)
            {
                int hx = Mathf.Min(hmRes - 1, x * Step);
                float xPos = (hx / (float)(hmRes - 1)) * size.x;

                float yPos = heights[hz, hx] * size.y;

                vertices[vi] = new Vector3(xPos, yPos, zPos);
                uvs[vi] = new Vector2(x / (float)(w - 1), z / (float)(h - 1));
                vi++;
            }
        }

        // Triangles
        int ti = 0;
        for (int z = 0; z < h - 1; z++)
        {
            for (int x = 0; x < w - 1; x++)
            {
                int i0 = z * w + x;
                int i1 = i0 + 1;
                int i2 = i0 + w;
                int i3 = i2 + 1;

                tris[ti++] = i0; tris[ti++] = i2; tris[ti++] = i1;
                tris[ti++] = i1; tris[ti++] = i2; tris[ti++] = i3;
            }
        }

        var mesh = new Mesh();
        mesh.indexFormat = (vertices.Length > 65535)
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Create mesh object aligned to terrain (same position)
        var go = new GameObject($"TerrainMesh_{terrain.name}");
        Undo.RegisterCreatedObjectUndo(go, "Create Terrain Mesh");

        go.transform.SetParent(terrain.transform.parent, worldPositionStays: true);
        go.transform.position = terrain.transform.position;
        go.transform.rotation = terrain.transform.rotation;
        go.transform.localScale = Vector3.one;

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mc = go.AddComponent<MeshCollider>();

        mf.sharedMesh = mesh;
        mc.sharedMesh = mesh;

        // Material: copy terrain material if present (otherwise use Standard)
        Material mat = terrain.materialTemplate;
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            mat.name = "TerrainMesh_Mat";
        }
        mr.sharedMaterial = mat;

        Debug.Log("Terrain converted to mesh. You can now scale the mesh normally.");

        // Optional: disable original terrain
        terrain.gameObject.SetActive(false);
    }

    [MenuItem("Tools/Terrain/Create Aligned Quad From Selected Terrain")]
    private static void CreateAlignedQuad()
    {
        var terrain = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<Terrain>() : null;
        if (!terrain) { Debug.LogError("Select a Terrain object in the Hierarchy first."); return; }

        TerrainData td = terrain.terrainData;
        Vector3 size = td.size;
        Vector3 pos = terrain.transform.position;

        // Quad pivot is center; Terrain pivot is bottom-left -> offset by half size
        Vector3 center = pos + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Undo.RegisterCreatedObjectUndo(quad, "Create Aligned Quad");

        quad.name = $"TerrainQuad_{terrain.name}";
        quad.transform.SetParent(terrain.transform.parent, worldPositionStays: true);
        quad.transform.position = center + Vector3.up * 0.01f; // tiny lift to avoid z-fighting
        quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lay flat on XZ
        quad.transform.localScale = new Vector3(size.x, size.z, 1f); // Quad is 1x1

        Debug.Log("Aligned quad created. Assign your baked/satellite texture material to it.");
    }
}
#endif
