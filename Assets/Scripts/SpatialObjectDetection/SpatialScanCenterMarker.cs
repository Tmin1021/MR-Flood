using UnityEngine;

[DisallowMultipleComponent]
public class SpatialScanCenterMarker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpatialObjectDetectionManager detectionManager;

    [Tooltip("Optional. Assign your city model root / city anchor root here so the marker appears inside the city model hierarchy.")]
    [SerializeField] private Transform cityModelRoot;

    [Header("Marker")]
    [SerializeField] private bool createOnStart = true;
    [SerializeField] private bool followCenterEveryFrame = true;
    [SerializeField] private bool matchSphereDiameterToScanArea = true;
    [SerializeField] private float sphereDiameter = 1.5f;
    [SerializeField] private float verticalOffset = 0.03f;
    [SerializeField] private Color markerColor = Color.magenta;
    [SerializeField] private string markerName = "SCAN_CENTER_MARKER";

    private GameObject marker;

    private void Start()
    {
        ResolveMissingReferences();

        if (createOnStart)
            ShowScanCenterMarker();
    }

    private void Update()
    {
        if (!followCenterEveryFrame || marker == null)
            return;

        UpdateMarkerPose();
    }

    [ContextMenu("Show Scan Center Marker")]
    public void ShowScanCenterMarker()
    {
        ResolveMissingReferences();

        Transform scanCenter = GetScanCenterTransform();
        if (scanCenter == null)
        {
            Debug.LogWarning("SpatialScanCenterMarker: Cannot show marker because no scan center is available.");
            return;
        }

        if (marker == null)
            marker = CreateMarkerObject();

        UpdateMarkerPose();

        Debug.Log(
            $"SpatialScanCenterMarker: marker placed at scan center = {detectionManager.ScanCenterWorldPosition}. " +
            $"Transform name = '{scanCenter.name}'."
        );
    }

    [ContextMenu("Hide Scan Center Marker")]
    public void HideScanCenterMarker()
    {
        if (marker == null)
            return;

        if (Application.isPlaying)
            Destroy(marker);
        else
            DestroyImmediate(marker);

        marker = null;
    }

    private GameObject CreateMarkerObject()
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = markerName;

        // Parent under the city model only for hierarchy organization.
        // worldPositionStays = true keeps the marker at the exact world position.
        if (cityModelRoot != null)
            sphere.transform.SetParent(cityModelRoot, true);
        else
            sphere.transform.SetParent(transform, true);

        sphere.transform.localScale = GetMarkerScale(sphere.transform);

        Collider col = sphere.GetComponent<Collider>();
        if (col != null)
            col.enabled = false;

        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = markerColor;
            renderer.material = mat;
        }

        return sphere;
    }

    private void UpdateMarkerPose()
    {
        Transform scanCenter = GetScanCenterTransform();
        if (scanCenter == null || marker == null)
            return;

        marker.transform.position = detectionManager.ScanCenterWorldPosition + scanCenter.up * verticalOffset;
        marker.transform.rotation = scanCenter.rotation;
        marker.transform.localScale = GetMarkerScale(marker.transform);
    }

    private Transform GetScanCenterTransform()
    {
        if (detectionManager == null)
            return null;

        return detectionManager.ScanCenterTransform;
    }

    private Vector3 GetMarkerScale(Transform markerTransform)
    {
        if (!matchSphereDiameterToScanArea || detectionManager == null)
            return Vector3.one * sphereDiameter;

        Vector2 scanHalfExtents = detectionManager.ScanHalfExtentsWorld;
        float worldDiameter = Mathf.Max(0.001f, Mathf.Max(scanHalfExtents.x, scanHalfExtents.y) * 2f);
        Transform parent = markerTransform != null ? markerTransform.parent : null;

        if (parent == null)
            return Vector3.one * worldDiameter;

        Vector3 parentScale = parent.lossyScale;
        return new Vector3(
            worldDiameter / Mathf.Max(0.001f, Mathf.Abs(parentScale.x)),
            worldDiameter / Mathf.Max(0.001f, Mathf.Abs(parentScale.y)),
            worldDiameter / Mathf.Max(0.001f, Mathf.Abs(parentScale.z)));
    }

    private void ResolveMissingReferences()
    {
        detectionManager ??= FindFirstObjectByType<SpatialObjectDetectionManager>(FindObjectsInactive.Include);

        if (cityModelRoot == null && detectionManager != null && detectionManager.SelectedPlaneTransform != null)
            cityModelRoot = detectionManager.SelectedPlaneTransform;
    }
}
