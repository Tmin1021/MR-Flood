using System;
using System.Collections.Generic;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.UI;
using Microsoft.MixedReality.Toolkit.UI.BoundsControl;
using Microsoft.MixedReality.Toolkit.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Presentation-only city. It copies shared meshes/materials, never canonical
/// behaviours, and renders canonical state in its own movable coordinate frame.
/// </summary>
[DisallowMultipleComponent]
public sealed class VisualizationCityView : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private sealed class BuildingRendererRecord
    {
        public string id;
        public Renderer sourceRenderer;
        public Renderer renderer;
        public MaterialPropertyBlock originalBlock;
    }

    private sealed class MeshRendererRecord
    {
        public MeshRenderer source;
        public MeshRenderer copy;
    }

    private sealed class RoadRendererRecord
    {
        public string id;
        public LineRenderer line;
    }

    private readonly List<BuildingRendererRecord> buildingRenderers = new List<BuildingRendererRecord>();
    private readonly List<MeshRendererRecord> meshRenderers = new List<MeshRendererRecord>();
    private readonly List<RoadRendererRecord> roadRenderers = new List<RoadRendererRecord>();
    private readonly List<GameObject> floodVisuals = new List<GameObject>();
    private readonly List<GameObject> selectionLabels = new List<GameObject>();
    private readonly List<GameObject> routeLabels = new List<GameObject>();
    private readonly List<GameObject> blockedRoadLabels = new List<GameObject>();
    private readonly List<Material> runtimeMaterials = new List<Material>();

    private Transform canonicalRoot;
    private CityManager cityManager;
    private FloodManager floodManager;
    private Transform geometryRoot;
    private Transform roadStateRoot;
    private Transform floodRoot;
    private Transform routeRoot;
    private Transform labelRoot;
    private LineRenderer routeLine;
    private Material floodMaterial;
    private Material blockedRoadMaterial;
    private Material routeMaterial;
    private ObjectManipulator objectManipulator;
    private BoundsControl boundsControl;
    private BoxCollider interactionCollider;
    private CityVisualizationSnapshot routeSnapshot;
    private Vector3 initialRootScale = Vector3.one;
    private bool initialized;

    [Header("Scale-Relative Presentation")]
    [SerializeField, Min(0.001f)] private float routeLocalWidth = 0.01f;
    [SerializeField, Min(0.001f)] private float blockedRoadLocalWidth = 0.007f;
    [SerializeField, Min(0.001f)] private float routeLabelLocalScale = 0.035f;

    private readonly Color floodedBuildingColor = new Color(0.1f, 0.45f, 1f, 1f);
    private readonly Color startBuildingColor = new Color(0.15f, 1f, 0.25f, 1f);
    private readonly Color destinationBuildingColor = new Color(1f, 0.35f, 0.08f, 1f);
    // Saturated neon orange stays distinct from the blue flood overlay, cyan
    // route line, and red blocked-road labels.
    private readonly Color routeLabelColor = new Color(1f, 0.28f, 0.01f, 1f);
    private readonly Color blockedRoadLabelColor = new Color(1f, 0.12f, 0.05f, 1f);
    private readonly Color labelOutlineColor = new Color(0.02f, 0.04f, 0.08f, 1f);

    public Transform CanonicalRoot => canonicalRoot;
    public bool IsInitialized => initialized;

    public bool Initialize(Transform sourceRoot, CityManager manager, FloodManager floods)
    {
        if (initialized)
            return true;

        if (sourceRoot == null || manager == null)
        {
            Debug.LogError("VisualizationCityView: canonical root and CityManager are required.");
            return false;
        }

        canonicalRoot = sourceRoot;
        cityManager = manager;
        floodManager = floods;

        CreatePresentationRoots();
        CreateMaterials();

        if (!CopyCanonicalMeshPresentation(out Bounds localBounds))
        {
            Debug.LogError("VisualizationCityView: no eligible city mesh renderers were found.");
            return false;
        }

        CreateRoadStatePresentation();
        CreateRoutePresentation();
        ConfigureManipulation(localBounds);
        initialized = true;
        return true;
    }

    public void AlignWithCanonical()
    {
        if (canonicalRoot == null)
            return;

        transform.SetPositionAndRotation(canonicalRoot.position, canonicalRoot.rotation);
        transform.localScale = Abs(canonicalRoot.lossyScale);
        initialRootScale = transform.localScale;
    }

    public void SetManipulationEnabled(bool value)
    {
        if (objectManipulator != null)
            objectManipulator.enabled = value;

        if (boundsControl != null)
        {
            boundsControl.enabled = value;
            boundsControl.Active = value;
        }

        if (interactionCollider != null)
            interactionCollider.enabled = value;
    }

    public void RefreshAll(CityVisualizationSnapshot snapshot)
    {
        RefreshSourcePresentation();
        ApplyRouteSnapshot(snapshot);
        RefreshFloodSources();
    }

    private void RefreshSourcePresentation()
    {
        for (int i = 0; i < meshRenderers.Count; i++)
        {
            MeshRendererRecord record = meshRenderers[i];
            if (record?.source == null || record.copy == null)
                continue;

            record.copy.sharedMaterials = record.source.sharedMaterials;
            record.copy.enabled = record.source.enabled;
        }

        RefreshSourceVisibility();

        for (int i = 0; i < buildingRenderers.Count; i++)
        {
            BuildingRendererRecord record = buildingRenderers[i];
            if (record?.sourceRenderer == null || record.renderer == null)
                continue;

            MaterialPropertyBlock original = new MaterialPropertyBlock();
            record.sourceRenderer.GetPropertyBlock(original);
            record.originalBlock = original;
            record.renderer.SetPropertyBlock(original);
        }
    }

    public void RefreshCityState()
    {
        if (cityManager == null)
            return;

        string startId = routeSnapshot?.StartBuildingId ?? string.Empty;
        string destinationId = routeSnapshot?.DestinationBuildingId ?? string.Empty;

        for (int i = 0; i < buildingRenderers.Count; i++)
        {
            BuildingRendererRecord record = buildingRenderers[i];
            if (record?.renderer == null)
                continue;

            CityBuilding building = cityManager.GetBuildingById(record.id);
            bool isStart = !string.IsNullOrEmpty(startId) && record.id == startId;
            bool isDestination = !string.IsNullOrEmpty(destinationId) && record.id == destinationId;

            if (building != null && building.isFlooded)
                ApplyRendererColor(record, floodedBuildingColor);
            else if (isStart)
                ApplyRendererColor(record, startBuildingColor);
            else if (isDestination)
                ApplyRendererColor(record, destinationBuildingColor);
            else
                record.renderer.SetPropertyBlock(record.originalBlock);
        }

        for (int i = 0; i < roadRenderers.Count; i++)
        {
            RoadRendererRecord record = roadRenderers[i];
            Road road = cityManager.GetRoadById(record.id);
            if (record.line != null)
                record.line.enabled = road != null && road.isBlocked;
        }

        RebuildBlockedRoadLabels();
    }

    public void RefreshFloodSources()
    {
        FloodSource[] sources = floodManager != null && floodManager.floodSources != null
            ? floodManager.floodSources
            : Array.Empty<FloodSource>();

        EnsureFloodVisualCount(sources.Length);

        for (int i = 0; i < floodVisuals.Count; i++)
        {
            GameObject visual = floodVisuals[i];
            FloodSource source = i < sources.Length ? sources[i] : null;
            bool visible = source != null;
            visual.SetActive(visible);

            if (!visible)
                continue;

            visual.transform.localPosition = canonicalRoot.InverseTransformPoint(source.transform.position) +
                Vector3.up * GetCanonicalLocalDistance(0.002f);

            float localRadius = GetCanonicalLocalDistance(Mathf.Max(source.radius, 0f));
            float localThickness = GetCanonicalLocalDistance(0.003f);
            visual.transform.localScale = new Vector3(
                localRadius * 2f,
                localThickness * 0.5f,
                localRadius * 2f);

            Renderer floodRenderer = visual.GetComponent<Renderer>();
            if (floodRenderer != null)
            {
                Color sourceColor = new Color(0f, 0.5f, 1f, Mathf.Lerp(0.18f, 0.5f, Mathf.Clamp01(source.intensity)));
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                block.SetColor(ColorId, sourceColor);
                block.SetColor(BaseColorId, sourceColor);
                floodRenderer.SetPropertyBlock(block);
            }
        }
    }

    public void ApplyRouteSnapshot(CityVisualizationSnapshot snapshot)
    {
        routeSnapshot = snapshot;
        Vector3[] points = snapshot?.RouteWorldPoints ?? Array.Empty<Vector3>();
        bool showRoute = snapshot != null && snapshot.HasRoute && points.Length > 1;

        routeLine.enabled = showRoute;
        routeLine.positionCount = showRoute ? points.Length : 0;
        for (int i = 0; i < points.Length && showRoute; i++)
            routeLine.SetPosition(i, canonicalRoot.InverseTransformPoint(points[i]));

        RebuildSelectionLabels(snapshot);
        RebuildRouteLabels(snapshot?.RouteLabels ?? Array.Empty<CityVisualizationLabel>());
        RefreshCityState();
    }

    private void LateUpdate()
    {
        if (!initialized)
            return;

        EnforceUniformRelativeScale();

        // Keep presentation dimensions in visualization-local space. Scaling the
        // visualization root now scales the route and labels with the city.
        routeLine.widthMultiplier = routeLocalWidth;
        for (int i = 0; i < roadRenderers.Count; i++)
        {
            if (roadRenderers[i].line != null)
                roadRenderers[i].line.widthMultiplier = blockedRoadLocalWidth;
        }

        for (int i = 0; i < routeLabels.Count; i++)
            SetLocalScale(routeLabels[i], routeLabelLocalScale);

        for (int i = 0; i < selectionLabels.Count; i++)
            SetLocalScale(selectionLabels[i], routeLabelLocalScale);

        for (int i = 0; i < blockedRoadLabels.Count; i++)
            SetLocalScale(blockedRoadLabels[i], routeLabelLocalScale);
    }

    private void CreatePresentationRoots()
    {
        geometryRoot = CreateChild("Visual City Geometry");
        roadStateRoot = CreateChild("Road State Overlay");
        floodRoot = CreateChild("Flood Overlay");
        routeRoot = CreateChild("Route Overlay");
        labelRoot = CreateChild("Route Labels");
    }

    private Transform CreateChild(string childName)
    {
        GameObject child = new GameObject(childName);
        child.transform.SetParent(transform, false);
        return child.transform;
    }

    private void CreateMaterials()
    {
        floodMaterial = CreateRuntimeMaterial("Visualization Flood", new Color(0f, 0.5f, 1f, 0.32f));
        blockedRoadMaterial = CreateRuntimeMaterial("Visualization Blocked Road", new Color(1f, 0.12f, 0.05f, 1f));
        routeMaterial = CreateRuntimeMaterial("Visualization Route", new Color(0.1f, 1f, 1f, 1f));
    }

    private Material CreateRuntimeMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            return null;

        Material material = new Material(shader)
        {
            name = materialName,
            color = color
        };
        runtimeMaterials.Add(material);
        return material;
    }

    private bool CopyCanonicalMeshPresentation(out Bounds localBounds)
    {
        MeshRenderer[] sourceRenderers = canonicalRoot.GetComponentsInChildren<MeshRenderer>(true);
        bool hasBounds = false;
        localBounds = new Bounds(Vector3.zero, Vector3.zero);

        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            MeshRenderer source = sourceRenderers[i];
            MeshFilter sourceFilter = source != null ? source.GetComponent<MeshFilter>() : null;
            if (source == null || sourceFilter == null || sourceFilter.sharedMesh == null || !ShouldCopy(source))
                continue;

            GameObject copyObject = new GameObject(source.gameObject.name);
            copyObject.layer = gameObject.layer;
            copyObject.transform.SetParent(geometryRoot, false);
            ApplyCanonicalRelativeTransform(source.transform, copyObject.transform);

            MeshFilter copyFilter = copyObject.AddComponent<MeshFilter>();
            copyFilter.sharedMesh = sourceFilter.sharedMesh;

            MeshRenderer copy = copyObject.AddComponent<MeshRenderer>();
            copy.sharedMaterials = source.sharedMaterials;
            copy.enabled = source.enabled;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            copy.lightProbeUsage = LightProbeUsage.Off;
            copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            copy.sortingLayerID = source.sortingLayerID;
            copy.sortingOrder = source.sortingOrder;
            meshRenderers.Add(new MeshRendererRecord { source = source, copy = copy });

            MaterialPropertyBlock original = new MaterialPropertyBlock();
            source.GetPropertyBlock(original);
            copy.SetPropertyBlock(original);

            BuildingMarker buildingMarker = source.GetComponentInParent<BuildingMarker>();
            if (buildingMarker != null)
            {
                buildingRenderers.Add(new BuildingRendererRecord
                {
                    id = buildingMarker.BuildingIdOrFallback,
                    sourceRenderer = source,
                    renderer = copy,
                    originalBlock = original
                });
            }

            EncapsulateRendererBounds(source.bounds, ref localBounds, ref hasBounds);
        }

        return hasBounds;
    }

    private bool ShouldCopy(MeshRenderer source)
    {
        // Inactive model layers (notably Trees on HoloLens) still need a copy so
        // they can become visible if the source layer is enabled later.
        Transform current = source.transform;
        while (current != null && current != canonicalRoot)
        {
            if (current.name == "Arrows" || current.name == "LinePath" ||
                current.name == "ScanArea" || current.name == "CityCenterPivot")
            {
                return false;
            }

            if (current.GetComponent<GraphNode>() != null)
                return false;

            current = current.parent;
        }

        return true;
    }

    public void RefreshSourceVisibility()
    {
        // VisualizationModeController temporarily disables every source Renderer,
        // so use hierarchy activation for live model-layer visibility. Individual
        // Renderer.enabled values are captured before suppression in RefreshSourcePresentation.
        for (int i = 0; i < meshRenderers.Count; i++)
        {
            MeshRendererRecord record = meshRenderers[i];
            if (record?.source == null || record.copy == null)
                continue;

            bool sourceIsActive = record.source.gameObject.activeInHierarchy;
            if (record.copy.gameObject.activeSelf != sourceIsActive)
                record.copy.gameObject.SetActive(sourceIsActive);
        }
    }

    private void ApplyCanonicalRelativeTransform(Transform source, Transform target)
    {
        Matrix4x4 localMatrix = canonicalRoot.worldToLocalMatrix * source.localToWorldMatrix;
        target.localPosition = localMatrix.GetColumn(3);
        target.localRotation = localMatrix.rotation;
        target.localScale = Abs(localMatrix.lossyScale);
    }

    private void EncapsulateRendererBounds(Bounds worldBounds, ref Bounds localBounds, ref bool hasBounds)
    {
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 world = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                    Vector3 local = canonicalRoot.InverseTransformPoint(world);
                    if (!hasBounds)
                    {
                        localBounds = new Bounds(local, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(local);
                    }
                }
            }
        }
    }

    private void CreateRoadStatePresentation()
    {
        for (int i = 0; i < cityManager.roads.Count; i++)
        {
            Road road = cityManager.roads[i];
            if (road == null || road.start == null || road.end == null || string.IsNullOrWhiteSpace(road.id))
                continue;

            GameObject lineObject = new GameObject("Road State " + road.id);
            lineObject.transform.SetParent(roadStateRoot, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            ConfigureLine(line, blockedRoadMaterial);
            line.widthMultiplier = blockedRoadLocalWidth;
            line.positionCount = 2;
            Vector3 localOffset = Vector3.up * GetCanonicalLocalDistance(0.004f);
            line.SetPosition(0, canonicalRoot.InverseTransformPoint(road.start.position) + localOffset);
            line.SetPosition(1, canonicalRoot.InverseTransformPoint(road.end.position) + localOffset);
            line.enabled = road.isBlocked;
            roadRenderers.Add(new RoadRendererRecord { id = road.id, line = line });
        }
    }

    private void CreateRoutePresentation()
    {
        GameObject lineObject = new GameObject("Current Route");
        lineObject.transform.SetParent(routeRoot, false);
        routeLine = lineObject.AddComponent<LineRenderer>();
        ConfigureLine(routeLine, routeMaterial);
        routeLine.widthMultiplier = routeLocalWidth;
        routeLine.enabled = false;
    }

    private void ConfigureLine(LineRenderer line, Material material)
    {
        line.useWorldSpace = false;
        line.loop = false;
        line.numCapVertices = 3;
        line.numCornerVertices = 3;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = material;
    }

    private void ConfigureManipulation(Bounds localBounds)
    {
        interactionCollider = gameObject.AddComponent<BoxCollider>();
        interactionCollider.center = localBounds.center;
        interactionCollider.size = Vector3.Max(localBounds.size, Vector3.one * 0.01f);

        gameObject.AddComponent<NearInteractionGrabbable>();
        MinMaxScaleConstraint scaleConstraint = gameObject.AddComponent<MinMaxScaleConstraint>();
        scaleConstraint.RelativeToInitialState = true;
        scaleConstraint.ScaleMinimum = 0.5f;
        scaleConstraint.ScaleMaximum = 12f;

        RotationAxisConstraint rotationConstraint = gameObject.AddComponent<RotationAxisConstraint>();
        rotationConstraint.ConstraintOnRotation = AxisFlags.XAxis | AxisFlags.ZAxis;
        rotationConstraint.UseLocalSpaceForConstraint = false;

        objectManipulator = gameObject.AddComponent<ObjectManipulator>();
        objectManipulator.TwoHandedManipulationType = TransformFlags.Move | TransformFlags.Rotate | TransformFlags.Scale;

        boundsControl = gameObject.AddComponent<BoundsControl>();
        boundsControl.Target = gameObject;
        boundsControl.BoundsOverride = interactionCollider;
        boundsControl.Active = true;
    }

    private void EnsureFloodVisualCount(int count)
    {
        while (floodVisuals.Count < count)
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Flood Source " + floodVisuals.Count;
            disc.transform.SetParent(floodRoot, false);
            Collider collider = disc.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }

            Renderer renderer = disc.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = floodMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            floodVisuals.Add(disc);
        }

        for (int i = count; i < floodVisuals.Count; i++)
            floodVisuals[i].SetActive(false);
    }

    private void RebuildRouteLabels(CityVisualizationLabel[] labels)
    {
        for (int i = 0; i < routeLabels.Count; i++)
            Destroy(routeLabels[i]);
        routeLabels.Clear();

        for (int i = 0; i < labels.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(labels[i].Text))
                continue;

            GameObject labelObject = new GameObject("Route Label " + i);
            labelObject.transform.SetParent(labelRoot, false);
            labelObject.transform.localPosition = canonicalRoot.InverseTransformPoint(labels[i].WorldPosition);
            labelObject.transform.localScale = Vector3.one * routeLabelLocalScale;

            ConfigureLabel(labelObject, labels[i].Text, routeLabelColor);
            routeLabels.Add(labelObject);
        }
    }

    private void RebuildSelectionLabels(CityVisualizationSnapshot snapshot)
    {
        for (int i = 0; i < selectionLabels.Count; i++)
            Destroy(selectionLabels[i]);
        selectionLabels.Clear();

        if (snapshot == null)
            return;

        if (snapshot.HasStart)
        {
            CreateSelectionLabel(
                "START\n" + GetBuildingDisplayName(snapshot.StartBuildingId),
                snapshot.StartWorldPosition,
                startBuildingColor,
                "Start Building Label");
        }

        if (snapshot.HasDestination)
        {
            CreateSelectionLabel(
                "DESTINATION\n" + GetBuildingDisplayName(snapshot.DestinationBuildingId),
                snapshot.DestinationWorldPosition,
                destinationBuildingColor,
                "Destination Building Label");
        }
    }

    private void CreateSelectionLabel(
        string text,
        Vector3 canonicalWorldPosition,
        Color color,
        string objectName)
    {
        GameObject labelObject = new GameObject(objectName);
        labelObject.transform.SetParent(labelRoot, false);
        labelObject.transform.localPosition =
            canonicalRoot.InverseTransformPoint(canonicalWorldPosition) +
            Vector3.up * GetCanonicalLocalDistance(0.018f);
        labelObject.transform.localScale = Vector3.one * routeLabelLocalScale;

        ConfigureLabel(labelObject, text, color);
        selectionLabels.Add(labelObject);
    }

    private string GetBuildingDisplayName(string buildingId)
    {
        CityBuilding building = !string.IsNullOrWhiteSpace(buildingId) && cityManager != null
            ? cityManager.GetBuildingById(buildingId)
            : null;

        if (building != null && !string.IsNullOrWhiteSpace(building.displayName))
            return building.displayName;

        return !string.IsNullOrWhiteSpace(buildingId) ? buildingId : "Building";
    }

    private void RebuildBlockedRoadLabels()
    {
        for (int i = 0; i < blockedRoadLabels.Count; i++)
            Destroy(blockedRoadLabels[i]);
        blockedRoadLabels.Clear();

        if (cityManager?.roads == null)
            return;

        for (int i = 0; i < cityManager.roads.Count; i++)
        {
            Road road = cityManager.roads[i];
            if (road == null || !road.isBlocked)
                continue;

            GameObject labelObject = new GameObject("Blocked Road Label " + road.id);
            labelObject.transform.SetParent(labelRoot, false);
            labelObject.transform.localPosition = canonicalRoot.InverseTransformPoint(road.GetLabelAnchor()) +
                Vector3.up * GetCanonicalLocalDistance(0.006f);
            labelObject.transform.localScale = Vector3.one * routeLabelLocalScale;

            ConfigureLabel(labelObject, "BLOCKED\n" + road.DisplayNameOrFallback, blockedRoadLabelColor);
            blockedRoadLabels.Add(labelObject);
        }
    }

    private void ConfigureLabel(GameObject labelObject, string labelText, Color color)
    {
        TextMeshPro text = labelObject.AddComponent<TextMeshPro>();
        text.text = labelText;
        text.fontSize = 1f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.color = color;
        // TextMeshPro can be created before a font material is assigned on some
        // devices. Applying an outline in that state throws a NullReferenceException.
        if (text.fontSharedMaterial != null)
        {
            text.outlineColor = labelOutlineColor;
            text.outlineWidth = 0.2f;
        }
        text.rectTransform.sizeDelta = new Vector2(12f, 2f);

        RouteLabelBillboard billboard = labelObject.AddComponent<RouteLabelBillboard>();
        billboard.targetCamera = Camera.main;
    }

    private void ApplyRendererColor(BuildingRendererRecord record, Color color)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        record.renderer.GetPropertyBlock(block);
        block.SetColor(ColorId, color);
        block.SetColor(BaseColorId, color);
        block.SetColor(EmissionColorId, color * 1.25f);
        record.renderer.SetPropertyBlock(block);
    }

    private float GetCanonicalLocalDistance(float worldDistance)
    {
        float scale = Mathf.Max(GetUniformWorldScale(canonicalRoot), 0.0001f);
        return worldDistance / scale;
    }

    private static float GetUniformWorldScale(Transform target)
    {
        Vector3 scale = Abs(target.lossyScale);
        return (scale.x + scale.y + scale.z) / 3f;
    }

    private void EnforceUniformRelativeScale()
    {
        Vector3 scale = Abs(transform.localScale);
        Vector3 baseline = new Vector3(
            Mathf.Max(initialRootScale.x, 0.0001f),
            Mathf.Max(initialRootScale.y, 0.0001f),
            Mathf.Max(initialRootScale.z, 0.0001f));
        float multiplier = (scale.x / baseline.x + scale.y / baseline.y + scale.z / baseline.z) / 3f;
        multiplier = Mathf.Clamp(multiplier, 0.5f, 12f);
        transform.localScale = baseline * multiplier;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private static void SetLocalScale(GameObject target, float localScale)
    {
        if (target != null)
            target.transform.localScale = Vector3.one * localScale;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < runtimeMaterials.Count; i++)
        {
            if (runtimeMaterials[i] != null)
                Destroy(runtimeMaterials[i]);
        }
    }
}
