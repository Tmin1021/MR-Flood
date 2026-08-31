using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpatialObjectPreviewPresenter : MonoBehaviour
{
    [Header("Roots")]
    [SerializeField] private Transform previewRoot;

    [Header("Detected Candidate Visuals")]
    [SerializeField] private GameObject candidateMarkerPrefab;
    [SerializeField] private bool showDefaultCandidateMarkers = true;
    [SerializeField] private float candidateMarkerSize = 0.035f;
    [SerializeField] private Vector3 candidateMarkerOffset = new Vector3(0f, 0.035f, 0f);

    [Header("Building Candidate Visuals")]
    [SerializeField] private GameObject buildingMarkerPrefab;
    [SerializeField] private float buildingRingPadding = 0.02f;
    [SerializeField] private float buildingRingHeightOffset = 0.004f;
    [SerializeField] private float buildingRingWidth = 0.006f;
    [SerializeField, Min(12)] private int buildingRingSegments = 64;
    [SerializeField] private Color startBuildingRingColor = new Color(0.15f, 1f, 0.25f, 1f);
    [SerializeField] private Color destinationBuildingRingColor = new Color(1f, 0.3f, 0.1f, 1f);
    [SerializeField, Min(0f)] private float buildingRingBlinkSpeed = 2.5f;
    [SerializeField, Range(0f, 1f)] private float buildingRingMinimumAlpha = 0.2f;

    [Header("Single Target Arrow")]
    [Tooltip("Optional override. When empty, the arrow visual from PointSelectManager is reused.")]
    [SerializeField] private GameObject targetBuildingArrowPrefab;
    [SerializeField] private PointSelectManager pointSelectManager;
    [SerializeField, Min(0f)] private float targetBuildingArrowClearance = 0.02f;
    [SerializeField, Min(0.01f)] private float targetBuildingArrowScaleMultiplier = 1f;

    [Header("Flood Candidate Visuals")]
    [SerializeField] private GameObject floodRadiusPrefab;
    [SerializeField] private float floodPreviewHeightOffset = 0.035f;
    [SerializeField] private Color defaultFloodPreviewColor = new Color(0f, 0.55f, 1f, 0.25f);

    private readonly List<GameObject> candidateVisuals = new List<GameObject>();
    private readonly List<GameObject> buildingVisuals = new List<GameObject>();
    private readonly List<GameObject> floodVisuals = new List<GameObject>();
    private readonly List<GameObject> confirmedFloodVisuals = new List<GameObject>();
    private readonly List<BuildingRingState> buildingRingStates = new List<BuildingRingState>();
    private bool presentationVisible = true;

    private sealed class BuildingRingState
    {
        public LineRenderer line;
        public Material material;
        public Color color;
    }

    private void Awake()
    {
        if (previewRoot == null)
            previewRoot = transform;

        pointSelectManager ??= FindFirstObjectByType<PointSelectManager>(FindObjectsInactive.Include);
    }

    public void SetPresentationVisible(bool visible)
    {
        presentationVisible = visible;
        SetVisualsActive(candidateVisuals, visible);
        SetVisualsActive(buildingVisuals, visible);
        SetVisualsActive(floodVisuals, visible);
        SetVisualsActive(confirmedFloodVisuals, visible);
    }

    private void Update()
    {
        float wave = (Mathf.Sin(Time.time * buildingRingBlinkSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        float alphaMultiplier = Mathf.Lerp(buildingRingMinimumAlpha, 1f, wave);

        for (int i = buildingRingStates.Count - 1; i >= 0; i--)
        {
            BuildingRingState state = buildingRingStates[i];
            if (state == null || state.line == null)
            {
                buildingRingStates.RemoveAt(i);
                continue;
            }

            Color blinkingColor = state.color;
            blinkingColor.a *= alphaMultiplier;
            state.line.startColor = blinkingColor;
            state.line.endColor = blinkingColor;

            if (state.material != null)
                state.material.color = blinkingColor;
        }
    }

    public void ShowDetectedCandidates(IReadOnlyList<PhysicalObjectCandidate> candidates)
    {
        ClearCandidatePreviews();

        if (candidates == null)
            return;

        for (int i = 0; i < candidates.Count; i++)
        {
            PhysicalObjectCandidate candidate = candidates[i];
            if (candidate == null || !candidate.isValid)
                continue;

            GameObject visual = CreateCandidateVisual(candidate, i);
            if (visual != null)
                candidateVisuals.Add(visual);
        }
    }

    public void ShowBuildingCandidates(CityBuilding startBuilding, CityBuilding destinationBuilding)
    {
        ClearBuildingPreviews();

        if (startBuilding != null)
            buildingVisuals.Add(CreateBuildingVisual(startBuilding, "Start Candidate"));

        if (destinationBuilding != null)
            buildingVisuals.Add(CreateBuildingVisual(destinationBuilding, "Destination Candidate"));
    }

    public void ShowSingleBuildingCandidate(CityBuilding targetBuilding)
    {
        ClearBuildingPreviews();

        if (targetBuilding == null)
            return;

        GameObject buildingVisual = CreateBuildingVisual(targetBuilding, "Target Candidate");
        if (buildingVisual != null)
            buildingVisuals.Add(buildingVisual);

        GameObject targetArrow = CreateTargetBuildingArrow(targetBuilding);
        if (targetArrow != null)
            buildingVisuals.Add(targetArrow);
    }

    public void ShowFloodCandidates(IReadOnlyList<PhysicalObjectCandidate> candidates, float radius)
    {
        ClearFloodPreviews();

        if (candidates == null)
            return;

        for (int i = 0; i < candidates.Count; i++)
        {
            PhysicalObjectCandidate candidate = candidates[i];
            if (candidate == null || !candidate.isValid)
                continue;

            GameObject visual = CreateFloodVisual(candidate.worldPosition, radius, i);
            if (visual != null)
                floodVisuals.Add(visual);
        }
    }

    public void ShowConfirmedFloodSources(IReadOnlyList<FloodSource> sources)
    {
        ClearConfirmedFloodPreviews();

        if (sources == null)
            return;

        for (int i = 0; i < sources.Count; i++)
        {
            FloodSource source = sources[i];
            if (source == null)
                continue;

            GameObject visual = CreateFloodVisual(
                source.transform.position,
                source.radius,
                i,
                "ConfirmedFloodZone");

            if (visual != null)
                confirmedFloodVisuals.Add(visual);
        }
    }

    public void ClearAllPreviews()
    {
        ClearCandidatePreviews();
        ClearBuildingPreviews();
        ClearFloodPreviews();
    }

    public void ClearCandidatePreviews()
    {
        DestroyVisuals(candidateVisuals);
    }

    public void ClearBuildingPreviews()
    {
        DestroyVisuals(buildingVisuals);
        DestroyBuildingRingMaterials();
    }

    public void ClearFloodPreviews()
    {
        DestroyVisuals(floodVisuals);
    }

    public void ClearConfirmedFloodPreviews()
    {
        DestroyVisuals(confirmedFloodVisuals);
    }

    private GameObject CreateCandidateVisual(PhysicalObjectCandidate candidate, int index)
    {
        if (candidateMarkerPrefab != null)
        {
            GameObject marker = Instantiate(
                candidateMarkerPrefab,
                candidate.worldPosition + candidateMarkerOffset,
                Quaternion.identity,
                previewRoot);

            marker.name = $"DetectedObjectCandidate_{index}";
            return marker;
        }

        if (!showDefaultCandidateMarkers)
            return null;

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = $"DetectedObjectCandidate_{index}";
        sphere.transform.SetParent(previewRoot, true);
        sphere.transform.position = candidate.worldPosition + candidateMarkerOffset;
        sphere.transform.localScale = Vector3.one * candidateMarkerSize;

        Collider col = sphere.GetComponent<Collider>();
        if (col != null)
            col.enabled = false;

        return sphere;
    }

    private GameObject CreateBuildingVisual(CityBuilding building, string label)
    {
        BuildingMarker marker = building.marker;
        Bounds bounds = default;
        bool hasBounds = marker != null && marker.TryGetWorldBounds(out bounds);
        Vector3 up = marker != null ? marker.VisualRoot.up.normalized : Vector3.up;
        Vector3 right = marker != null ? marker.VisualRoot.right.normalized : Vector3.right;
        Vector3 forward = marker != null ? marker.VisualRoot.forward.normalized : Vector3.forward;
        Vector3 position = building.position + up * buildingRingHeightOffset;
        float radius = hasBounds
            ? Mathf.Max(bounds.extents.x, bounds.extents.z) + buildingRingPadding
            : buildingRingPadding + 0.025f;
        Color ringColor = label.StartsWith("Start")
            ? startBuildingRingColor
            : destinationBuildingRingColor;

        if (buildingMarkerPrefab != null)
        {
            GameObject visual = Instantiate(buildingMarkerPrefab, position, Quaternion.identity, previewRoot);
            visual.name = $"{label}_{GetBuildingName(building)}";
            visual.transform.localScale = Vector3.one * (radius * 2f);
            return visual;
        }

        GameObject root = new GameObject($"{label} Ring_{GetBuildingName(building)}");
        root.transform.SetParent(previewRoot, true);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        LineRenderer line = root.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = Mathf.Max(12, buildingRingSegments);
        line.widthMultiplier = buildingRingWidth;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / line.positionCount;
            Vector3 point = position + right * (Mathf.Cos(angle) * radius) +
                forward * (Mathf.Sin(angle) * radius);
            line.SetPosition(i, point);
        }

        Material material = CreateRingMaterial(ringColor);
        if (material != null)
            line.material = material;

        line.startColor = ringColor;
        line.endColor = ringColor;
        buildingRingStates.Add(new BuildingRingState
        {
            line = line,
            material = material,
            color = ringColor
        });

        return root;
    }

    private GameObject CreateTargetBuildingArrow(CityBuilding building)
    {
        GameObject arrowSource = ResolveTargetBuildingArrowSource();
        if (arrowSource == null || building == null)
        {
            Debug.LogWarning(
                "SpatialObjectPreviewPresenter: no target arrow prefab or PointSelectManager arrow is available.");
            return null;
        }

        BuildingMarker marker = building.marker;
        Vector3 up = marker != null && marker.VisualRoot != null
            ? marker.VisualRoot.up.normalized
            : Vector3.up;
        Vector3 buildingTop = building.position;
        if (marker != null && marker.TryGetWorldBounds(out Bounds buildingBounds))
        {
            buildingTop = buildingBounds.center + up * GetProjectedExtent(buildingBounds.extents, up);
        }

        GameObject arrow = Instantiate(
            arrowSource,
            buildingTop,
            arrowSource.transform.rotation,
            previewRoot);
        arrow.name = $"Target Candidate Arrow_{GetBuildingName(building)}";
        arrow.transform.localScale *= targetBuildingArrowScaleMultiplier;
        arrow.SetActive(true);

        DisableColliders(arrow);
        float arrowHalfExtent = GetVisualHalfExtent(arrow, up);
        arrow.transform.position = buildingTop + up *
            (targetBuildingArrowClearance + arrowHalfExtent);
        arrow.SetActive(presentationVisible);
        return arrow;
    }

    private GameObject ResolveTargetBuildingArrowSource()
    {
        if (targetBuildingArrowPrefab != null)
            return targetBuildingArrowPrefab;

        pointSelectManager ??= FindFirstObjectByType<PointSelectManager>(FindObjectsInactive.Include);
        if (pointSelectManager == null)
            return null;

        return pointSelectManager.arrow2 != null
            ? pointSelectManager.arrow2
            : pointSelectManager.arrow1;
    }

    private static float GetProjectedExtent(Vector3 extents, Vector3 direction)
    {
        return Mathf.Abs(direction.x) * extents.x +
               Mathf.Abs(direction.y) * extents.y +
               Mathf.Abs(direction.z) * extents.z;
    }

    private static float GetVisualHalfExtent(GameObject visual, Vector3 direction)
    {
        if (visual == null)
            return 0f;

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return 0f;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return GetProjectedExtent(bounds.extents, direction);
    }

    private static void DisableColliders(GameObject root)
    {
        if (root == null)
            return;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

    private static Material CreateRingMaterial(Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader == null)
            return null;

        Material material = new Material(shader)
        {
            name = "Runtime Building Candidate Ring",
            color = color
        };
        return material;
    }

    private void DestroyBuildingRingMaterials()
    {
        for (int i = 0; i < buildingRingStates.Count; i++)
        {
            Material material = buildingRingStates[i]?.material;
            if (material == null)
                continue;

            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
        }

        buildingRingStates.Clear();
    }

    private GameObject CreateFloodVisual(
        Vector3 worldPosition,
        float radius,
        int index,
        string namePrefix = "FloodCandidateRadius")
    {
        Vector3 position = worldPosition + Vector3.up * floodPreviewHeightOffset;

        if (floodRadiusPrefab != null)
        {
            GameObject visual = Instantiate(floodRadiusPrefab, position, Quaternion.identity, previewRoot);
            visual.name = $"{namePrefix}_{index}";
            visual.transform.localScale = new Vector3(radius * 2f, visual.transform.localScale.y, radius * 2f);
            return visual;
        }

        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = $"{namePrefix}_{index}";
        disc.transform.SetParent(previewRoot, true);
        disc.transform.position = position;
        disc.transform.localScale = new Vector3(radius * 2f, 0.002f, radius * 2f);

        Collider col = disc.GetComponent<Collider>();
        if (col != null)
            col.enabled = false;

        Renderer renderer = disc.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = new Material(Shader.Find("Standard"));
            material.color = defaultFloodPreviewColor;
            renderer.material = material;
        }

        return disc;
    }

    private static string GetBuildingName(CityBuilding building)
    {
        if (building == null)
            return "Building";

        return string.IsNullOrWhiteSpace(building.displayName)
            ? building.id
            : building.displayName;
    }

    private static void FaceCamera(Transform target)
    {
        if (target == null || Camera.main == null)
            return;

        Vector3 direction = target.position - Camera.main.transform.position;
        if (direction.sqrMagnitude > 0.0001f)
            target.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private static void DestroyVisuals(List<GameObject> visuals)
    {
        if (visuals == null)
            return;

        for (int i = 0; i < visuals.Count; i++)
        {
            GameObject visual = visuals[i];
            if (visual == null)
                continue;

            if (Application.isPlaying)
                Destroy(visual);
            else
                DestroyImmediate(visual);
        }

        visuals.Clear();
    }

    private static void SetVisualsActive(List<GameObject> visuals, bool active)
    {
        for (int i = 0; i < visuals.Count; i++)
        {
            if (visuals[i] != null)
                visuals[i].SetActive(active);
        }
    }
}
