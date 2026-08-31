#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class BuildingMarkerVisualRootToolWindow : EditorWindow
{
    private string preferredChildName = "Volume Visual";
    private bool includeInactive = true;

    [MenuItem("Tools/City/Building Visual Root Tool")]
    public static void Open()
    {
        GetWindow<BuildingMarkerVisualRootToolWindow>("Building Visual Root Tool");
    }

    private void OnGUI()
    {
        GUILayout.Space(8);
        EditorGUILayout.LabelField("Building Marker Visual Root Tool", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "This tool assigns BuildingMarker.visualRoot in the editor.\n\n" +
            "Priority:\n" +
            "1. Child named exactly 'Volume Visual'\n" +
            "2. First child with a Renderer\n" +
            "3. Marker object itself",
            MessageType.Info);

        GUILayout.Space(6);

        preferredChildName = EditorGUILayout.TextField("Preferred Child Name", preferredChildName);
        includeInactive = EditorGUILayout.Toggle("Include Inactive", includeInactive);

        GUILayout.Space(10);

        if (GUILayout.Button("Assign For Selected Hierarchies", GUILayout.Height(30)))
        {
            BuildingMarker[] markers =
                BuildingMarkerVisualRootEditorUtility.GetMarkersUnderCurrentSelection(includeInactive);

            if (markers == null || markers.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Building Visual Root Tool",
                    "No BuildingMarker found under the current selection.",
                    "OK");
                return;
            }

            int changed = BuildingMarkerVisualRootEditorUtility.AutoAssignForMarkers(
                markers,
                preferredChildName,
                includeInactive);

            Debug.Log($"Building Visual Root Tool: Assigned {changed} marker(s) from current selection.");
        }

        if (GUILayout.Button("Assign For All Building Markers In Open Scenes", GUILayout.Height(30)))
        {
            BuildingMarker[] markers =
                BuildingMarkerVisualRootEditorUtility.GetAllMarkersInOpenScenes();

            if (markers == null || markers.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Building Visual Root Tool",
                    "No BuildingMarker found in open scenes.",
                    "OK");
                return;
            }

            int changed = BuildingMarkerVisualRootEditorUtility.AutoAssignForMarkers(
                markers,
                preferredChildName,
                includeInactive);

            Debug.Log($"Building Visual Root Tool: Assigned {changed} marker(s) in open scenes.");
        }

        GUILayout.Space(8);

        if (GUILayout.Button("Select All BuildingMarkers In Scene"))
        {
            BuildingMarker[] markers =
                BuildingMarkerVisualRootEditorUtility.GetAllMarkersInOpenScenes();

            GameObject[] gos = new GameObject[markers.Length];
            for (int i = 0; i < markers.Length; i++)
                gos[i] = markers[i].gameObject;

            Selection.objects = gos;
        }
    }
}
#endif