using System;
using System.Collections.Generic;
using Microsoft.MixedReality.Toolkit.Input;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Presentation-only localized copy of the canonical city. It owns no building,
/// graph, flood, route, or baseline state; lens targets resolve back to stable
/// canonical building IDs through BuildingSelectionTechniqueController.
/// </summary>
[DisallowMultipleComponent]
public sealed class MagnificationLensView : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int LensCenterWorldId = Shader.PropertyToID("_LensCenterWorld");
    private static readonly int LensUpWorldId = Shader.PropertyToID("_LensUpWorld");
    private static readonly int LensRadiusWorldId = Shader.PropertyToID("_LensRadiusWorld");

    private sealed class RendererCopyRecord
    {
        public MeshRenderer source;
        public MeshRenderer copy;
        public string buildingId;
        public Vector3 canonicalLocalCenter;
        public float canonicalLocalPlanarRadius;
        public MaterialPropertyBlock sourceBlock;
        public Material[] circularClipMaterials;
    }

    private sealed class RoadSegmentRecord
    {
        public Vector3 canonicalLocalStart;
        public Vector3 canonicalLocalEnd;
    }

    private sealed class ContextLabelRecord
    {
        public GameObject instance;
        public bool isBuilding;
        public string buildingId;
        public Vector3 canonicalLocalAnchor;
        public float unscaledCapHeight;
        public readonly List<RoadSegmentRecord> roadSegments = new List<RoadSegmentRecord>();
    }

    private sealed class NearFocusRegistration
    {
        public MagnificationLensBuildingTarget target;
        public IMixedRealityPointer pointer;
        public string buildingId;
    }

    private readonly List<RendererCopyRecord> rendererCopies = new List<RendererCopyRecord>();
    private readonly List<ContextLabelRecord> contextLabels = new List<ContextLabelRecord>();
    private readonly List<NearFocusRegistration> nearFocusRegistrations =
        new List<NearFocusRegistration>(2);
    private readonly List<Material> runtimeMaterials = new List<Material>();
    private readonly HashSet<string> frozenBuildingIds =
        new HashSet<string>(StringComparer.Ordinal);

    private Transform canonicalRoot;
    private CityManager cityManager;
    private PointSelectManager pointSelectManager;
    private BuildingSelectionTechniqueController techniqueController;
    private Transform contentRoot;
    private Transform contextLabelRoot;
    private LineRenderer lensRing;
    private LineRenderer focusConnector;
    private float magnificationFactor;
    private float focusRadiusWorld;
    private float heightOffsetWorld;
    private float towardUserOffsetWorld;
    private float maximumContextRendererExtentWorld;
    private float dwellSelectionTime;
    private float buildingLabelHeightOffsetWorld;
    private float streetLabelHeightOffsetWorld;
    private float buildingLabelCharacterHeightWorld;
    private float streetLabelCharacterHeightWorld;
    private bool showBuildingLabels;
    private bool showStreetLabels;
    private Vector3 currentCanonicalLocalFocus;
    private string hoveredBuildingId;
    private string dwellBuildingId;
    private string selectedStartId;
    private string selectedDestinationId;
    private float dwellStartedAt = -1f;
    private bool dwellSelectionTriggered;
    private int contextDataRevision = -1;
    private bool hasFrozenZone;
    private bool initialized;

    private readonly Color ringColor = new Color(0.1f, 0.95f, 1f, 0.95f);
    private readonly Color hoverColor = new Color(1f, 0.92f, 0.15f, 1f);
    private readonly Color startColor = new Color(0.15f, 1f, 0.25f, 1f);
    private readonly Color destinationColor = new Color(1f, 0.3f, 0.08f, 1f);
    private readonly Color buildingLabelColor = new Color(0.82f, 1f, 1f, 1f);
    private readonly Color streetLabelColor = new Color(1f, 0.62f, 0.15f, 1f);
    private readonly Color labelOutlineColor = new Color(0.015f, 0.025f, 0.05f, 1f);

    public bool IsInitialized => initialized;

    public bool Initialize(
        Transform sourceRoot,
        CityManager manager,
        PointSelectManager selectionManager,
        BuildingSelectionTechniqueController controller,
        float magnification,
        float focusRadius,
        float heightOffset,
        float towardUserOffset,
        float maximumContextExtent,
        float dwellDuration,
        bool enableBuildingLabels,
        bool enableStreetLabels,
        float buildingLabelHeightOffset,
        float streetLabelHeightOffset,
        float buildingCharacterHeight,
        float streetCharacterHeight)
    {
        if (initialized)
            return true;

        if (sourceRoot == null || manager == null || selectionManager == null || controller == null)
        {
            Debug.LogError("MagnificationLensView: canonical root, city manager, selection manager, and technique controller are required.");
            return false;
        }

        canonicalRoot = sourceRoot;
        cityManager = manager;
        pointSelectManager = selectionManager;
        techniqueController = controller;
        magnificationFactor = Mathf.Clamp(magnification, 1.25f, 6f);
        focusRadiusWorld = Mathf.Max(0.02f, focusRadius);
        heightOffsetWorld = Mathf.Max(0f, heightOffset);
        towardUserOffsetWorld = Mathf.Max(0f, towardUserOffset);
        maximumContextRendererExtentWorld = Mathf.Max(0.05f, maximumContextExtent);
        dwellSelectionTime = Mathf.Max(0.1f, dwellDuration);
        showBuildingLabels = enableBuildingLabels;
        showStreetLabels = enableStreetLabels;
        buildingLabelHeightOffsetWorld = Mathf.Max(0f, buildingLabelHeightOffset);
        streetLabelHeightOffsetWorld = Mathf.Max(0f, streetLabelHeightOffset);
        buildingLabelCharacterHeightWorld = Mathf.Max(0.001f, buildingCharacterHeight);
        streetLabelCharacterHeightWorld = Mathf.Max(0.001f, streetCharacterHeight);

        CreatePresentationRoots();
        CreateLensIndicator();
        if (!CopyCanonicalPresentation())
        {
            Debug.LogError("MagnificationLensView: no eligible canonical city renderers were found.");
            return false;
        }

        initialized = true;
        return true;
    }

    private void Update()
    {
        if (!initialized || !gameObject.activeInHierarchy)
            return;

        UpdateNearDwellSelection();
    }

    public void ShowAt(
        Vector3 physicalFocusWorld,
        Vector3 canonicalLocalFocus,
        IReadOnlyList<string> includedBuildingIds)
    {
        if (!initialized || canonicalRoot == null || hasFrozenZone)
            return;

        frozenBuildingIds.Clear();
        if (includedBuildingIds != null)
        {
            for (int i = 0; i < includedBuildingIds.Count; i++)
            {
                string buildingId = includedBuildingIds[i];
                if (!string.IsNullOrWhiteSpace(buildingId))
                    frozenBuildingIds.Add(buildingId);
            }
        }
        hasFrozenZone = true;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        currentCanonicalLocalFocus = canonicalLocalFocus;
        Vector3 up = canonicalRoot.up.sqrMagnitude > 0.000001f
            ? canonicalRoot.up.normalized
            : Vector3.up;
        Vector3 towardUser = Vector3.zero;
        if (Camera.main != null)
        {
            towardUser = Vector3.ProjectOnPlane(
                Camera.main.transform.position - physicalFocusWorld,
                up);

            if (towardUser.sqrMagnitude > 0.000001f)
                towardUser.Normalize();
        }

        Vector3 lensPosition = physicalFocusWorld +
            up * heightOffsetWorld +
            towardUser * towardUserOffsetWorld;

        transform.SetPositionAndRotation(lensPosition, canonicalRoot.rotation);
        transform.localScale = Abs(canonicalRoot.lossyScale);

        contentRoot.localScale = Vector3.one * magnificationFactor;
        contentRoot.localPosition = -canonicalLocalFocus * magnificationFactor;

        float canonicalScale = Mathf.Max(GetUniformWorldScale(canonicalRoot), 0.0001f);
        float localFocusRadius = focusRadiusWorld / canonicalScale;
        float localLensRadius = localFocusRadius * magnificationFactor;
        EnsureContextLabelsCurrent();
        ApplyContextLabelScale(canonicalScale);
        UpdateRing(localLensRadius, canonicalScale);
        UpdateConnector(physicalFocusWorld, lensPosition);
        UpdateCircularClipMaterials(lensPosition, up);
        RefreshVisibleRegion(localFocusRadius);
    }

    public void Hide()
    {
        ClearNearInteractionState();
        hasFrozenZone = false;
        frozenBuildingIds.Clear();
        for (int i = 0; i < contextLabels.Count; i++)
        {
            if (contextLabels[i]?.instance != null)
                contextLabels[i].instance.SetActive(false);
        }

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    public void RefreshSelection(CityVisualizationSnapshot snapshot)
    {
        selectedStartId = snapshot?.StartBuildingId ?? string.Empty;
        selectedDestinationId = snapshot?.DestinationBuildingId ?? string.Empty;
        RefreshBuildingAppearance();
    }

    public bool SelectCanonicalBuilding(string buildingId)
    {
        return techniqueController != null &&
            techniqueController.SelectBuildingFromLens(buildingId);
    }

    private void CreatePresentationRoots()
    {
        GameObject content = new GameObject("Magnified City Content");
        content.transform.SetParent(transform, false);
        contentRoot = content.transform;

        GameObject labels = new GameObject("Context Labels");
        labels.transform.SetParent(contentRoot, false);
        contextLabelRoot = labels.transform;
    }

    private void CreateLensIndicator()
    {
        Material indicatorMaterial = CreateRuntimeMaterial("Assisted Lens Indicator", ringColor);

        GameObject ringObject = new GameObject("Circular Lens Boundary");
        ringObject.transform.SetParent(transform, false);
        lensRing = ringObject.AddComponent<LineRenderer>();
        ConfigureLine(lensRing, indicatorMaterial, false);
        lensRing.loop = true;
        lensRing.positionCount = 64;

        GameObject connectorObject = new GameObject("Lens Focus Connector");
        connectorObject.transform.SetParent(transform, false);
        focusConnector = connectorObject.AddComponent<LineRenderer>();
        ConfigureLine(focusConnector, indicatorMaterial, true);
        focusConnector.positionCount = 2;
    }

    private bool CopyCanonicalPresentation()
    {
        MeshRenderer[] sources = canonicalRoot.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            MeshRenderer source = sources[i];
            MeshFilter sourceFilter = source != null ? source.GetComponent<MeshFilter>() : null;
            if (source == null || sourceFilter == null || sourceFilter.sharedMesh == null || !ShouldCopy(source))
                continue;

            BuildingMarker marker = source.GetComponentInParent<BuildingMarker>();
            RoadMarker roadMarker = source.GetComponentInParent<RoadMarker>();
            bool isTerrain = IsTerrainRenderer(source);
            bool isRoad = roadMarker != null;
            if (marker == null && !isTerrain && !isRoad)
                continue;

            bool isBroadContext = marker == null &&
                GetMaximumPlanarExtent(source.bounds) > maximumContextRendererExtentWorld;
            bool useCircularClip = isTerrain;
            if (isBroadContext && !isTerrain)
                continue;

            GameObject copyObject = new GameObject("Lens " + source.gameObject.name);
            copyObject.layer = source.gameObject.layer;
            copyObject.transform.SetParent(contentRoot, false);
            ApplyCanonicalRelativeTransform(source.transform, copyObject.transform);

            MeshFilter copyFilter = copyObject.AddComponent<MeshFilter>();
            copyFilter.sharedMesh = sourceFilter.sharedMesh;

            MeshRenderer copy = copyObject.AddComponent<MeshRenderer>();
            Material[] circularClipMaterials = useCircularClip
                ? CreateCircularClipMaterials(source.sharedMaterials)
                : null;
            if (useCircularClip && circularClipMaterials == null)
            {
                Destroy(copyObject);
                continue;
            }

            copy.sharedMaterials = circularClipMaterials ?? source.sharedMaterials;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            copy.lightProbeUsage = LightProbeUsage.Off;
            copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            copy.sortingLayerID = source.sortingLayerID;
            copy.sortingOrder = source.sortingOrder;

            MaterialPropertyBlock sourceBlock = new MaterialPropertyBlock();
            source.GetPropertyBlock(sourceBlock);
            copy.SetPropertyBlock(sourceBlock);

            string buildingId = marker != null ? marker.BuildingIdOrFallback : string.Empty;
            Vector3 localCenter = canonicalRoot.InverseTransformPoint(source.bounds.center);
            float localRadius = GetCanonicalLocalPlanarRadius(source.bounds);

            RendererCopyRecord record = new RendererCopyRecord
            {
                source = source,
                copy = copy,
                buildingId = buildingId,
                canonicalLocalCenter = localCenter,
                canonicalLocalPlanarRadius = localRadius,
                sourceBlock = sourceBlock,
                circularClipMaterials = circularClipMaterials
            };
            rendererCopies.Add(record);

            if (!string.IsNullOrWhiteSpace(buildingId))
            {
                BoxCollider collider = copyObject.AddComponent<BoxCollider>();
                collider.center = sourceFilter.sharedMesh.bounds.center;
                collider.size = sourceFilter.sharedMesh.bounds.size;

                if (copyObject.GetComponent<NearInteractionTouchableVolume>() == null)
                    copyObject.AddComponent<NearInteractionTouchableVolume>();

                MagnificationLensBuildingTarget target =
                    copyObject.AddComponent<MagnificationLensBuildingTarget>();
                target.Configure(this, buildingId);
            }
        }

        return rendererCopies.Count > 0;
    }

    public void RegisterNearFocus(
        MagnificationLensBuildingTarget target,
        string buildingId,
        IMixedRealityPointer pointer)
    {
        if (!initialized || target == null || !(pointer is PokePointer) ||
            string.IsNullOrWhiteSpace(buildingId))
        {
            return;
        }

        for (int i = nearFocusRegistrations.Count - 1; i >= 0; i--)
        {
            NearFocusRegistration registration = nearFocusRegistrations[i];
            if (ReferenceEquals(registration.pointer, pointer))
                nearFocusRegistrations.RemoveAt(i);
        }

        nearFocusRegistrations.Add(new NearFocusRegistration
        {
            target = target,
            pointer = pointer,
            buildingId = buildingId
        });

        if (dwellBuildingId != buildingId)
            BeginNearDwell(buildingId);
    }

    public void UnregisterNearFocus(
        MagnificationLensBuildingTarget target,
        string buildingId,
        IMixedRealityPointer pointer)
    {
        for (int i = nearFocusRegistrations.Count - 1; i >= 0; i--)
        {
            NearFocusRegistration registration = nearFocusRegistrations[i];
            if (registration.target == target &&
                (pointer == null || ReferenceEquals(registration.pointer, pointer)))
            {
                nearFocusRegistrations.RemoveAt(i);
            }
        }

        if (dwellBuildingId == buildingId && !HasNearFocusForBuilding(buildingId))
            SelectMostRecentNearFocus();
    }

    private void UpdateNearDwellSelection()
    {
        RemoveInvalidNearFocusRegistrations();

        if (string.IsNullOrWhiteSpace(dwellBuildingId) ||
            dwellSelectionTriggered ||
            dwellStartedAt < 0f ||
            !HasNearFocusForBuilding(dwellBuildingId))
        {
            return;
        }

        if (Time.unscaledTime - dwellStartedAt < dwellSelectionTime)
            return;

        dwellSelectionTriggered = true;
        SelectCanonicalBuilding(dwellBuildingId);
    }

    private void BeginNearDwell(string buildingId)
    {
        dwellBuildingId = buildingId ?? string.Empty;
        dwellStartedAt = string.IsNullOrWhiteSpace(dwellBuildingId)
            ? -1f
            : Time.unscaledTime;
        dwellSelectionTriggered = false;
        hoveredBuildingId = dwellBuildingId;
        RefreshBuildingAppearance();
    }

    private void SelectMostRecentNearFocus()
    {
        if (nearFocusRegistrations.Count == 0)
        {
            ClearNearDwell();
            return;
        }

        NearFocusRegistration registration =
            nearFocusRegistrations[nearFocusRegistrations.Count - 1];
        BeginNearDwell(registration.buildingId);
    }

    private void ClearNearDwell()
    {
        dwellBuildingId = string.Empty;
        dwellStartedAt = -1f;
        dwellSelectionTriggered = false;
        hoveredBuildingId = string.Empty;
        RefreshBuildingAppearance();
    }

    private void ClearNearInteractionState()
    {
        nearFocusRegistrations.Clear();
        ClearNearDwell();
    }

    private bool HasNearFocusForBuilding(string buildingId)
    {
        for (int i = 0; i < nearFocusRegistrations.Count; i++)
        {
            if (nearFocusRegistrations[i].buildingId == buildingId)
                return true;
        }

        return false;
    }

    private void RemoveInvalidNearFocusRegistrations()
    {
        bool removedCurrent = false;
        for (int i = nearFocusRegistrations.Count - 1; i >= 0; i--)
        {
            NearFocusRegistration registration = nearFocusRegistrations[i];
            IMixedRealityPointer pointer = registration.pointer;
            bool invalid = registration.target == null ||
                pointer == null ||
                (pointer is UnityEngine.Object pointerObject && pointerObject == null);

            if (!invalid)
            {
                GameObject focusedObject = pointer.Result?.CurrentPointerTarget;
                Transform targetTransform = registration.target.transform;
                invalid = !pointer.IsActive ||
                    !pointer.IsInteractionEnabled ||
                    focusedObject == null ||
                    (focusedObject.transform != targetTransform &&
                     !focusedObject.transform.IsChildOf(targetTransform));
            }

            if (!invalid)
                continue;

            removedCurrent |= registration.buildingId == dwellBuildingId;
            nearFocusRegistrations.RemoveAt(i);
        }

        if (removedCurrent && !HasNearFocusForBuilding(dwellBuildingId))
            SelectMostRecentNearFocus();
    }

    private void EnsureContextLabelsCurrent()
    {
        if (cityManager == null || contextLabelRoot == null ||
            contextDataRevision == cityManager.DataRevision)
        {
            return;
        }

        RebuildContextLabels();
        contextDataRevision = cityManager.DataRevision;
    }

    private void RebuildContextLabels()
    {
        for (int i = 0; i < contextLabels.Count; i++)
        {
            if (contextLabels[i]?.instance == null)
                continue;

            contextLabels[i].instance.SetActive(false);
            Destroy(contextLabels[i].instance);
        }
        contextLabels.Clear();

        if (showBuildingLabels && cityManager.buildings != null)
        {
            for (int i = 0; i < cityManager.buildings.Count; i++)
            {
                CityBuilding building = cityManager.buildings[i];
                string labelText = BuildBuildingLabel(building);
                if (building == null || string.IsNullOrWhiteSpace(labelText))
                    continue;

                Bounds bounds = default;
                bool hasBounds = building.marker != null &&
                    building.marker.TryGetWorldBounds(out bounds);
                Vector3 anchorWorld = hasBounds
                    ? GetTopAlongDirection(bounds, canonicalRoot.up)
                    : building.position;

                ContextLabelRecord record = CreateContextLabel(
                    "Building Label " + building.id,
                    labelText,
                    buildingLabelColor,
                    true);
                record.buildingId = building.id;
                record.canonicalLocalAnchor = canonicalRoot.InverseTransformPoint(anchorWorld);
                contextLabels.Add(record);
            }
        }

        if (!showStreetLabels || cityManager.roads == null)
            return;

        Dictionary<string, ContextLabelRecord> labelsByStreet =
            new Dictionary<string, ContextLabelRecord>(StringComparer.Ordinal);
        for (int i = 0; i < cityManager.roads.Count; i++)
        {
            Road road = cityManager.roads[i];
            string streetName = GetConfiguredStreetName(road);
            if (road == null || string.IsNullOrWhiteSpace(streetName))
                continue;

            if (!labelsByStreet.TryGetValue(streetName, out ContextLabelRecord record))
            {
                record = CreateContextLabel(
                    "Street Label " + streetName,
                    streetName,
                    streetLabelColor,
                    false);
                labelsByStreet.Add(streetName, record);
                contextLabels.Add(record);
            }

            record.roadSegments.Add(new RoadSegmentRecord
            {
                canonicalLocalStart = canonicalRoot.InverseTransformPoint(road.GetPointAlong(0f)),
                canonicalLocalEnd = canonicalRoot.InverseTransformPoint(road.GetPointAlong(1f))
            });
        }
    }

    private ContextLabelRecord CreateContextLabel(
        string objectName,
        string labelText,
        Color color,
        bool isBuilding)
    {
        GameObject labelObject = new GameObject(objectName);
        labelObject.transform.SetParent(contextLabelRoot, false);

        TextMeshPro text = labelObject.AddComponent<TextMeshPro>();
        text.text = labelText;
        text.fontSize = 1f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.color = color;
        text.raycastTarget = false;
        text.rectTransform.sizeDelta = new Vector2(10f, isBuilding ? 2.4f : 1.6f);
        text.sortingOrder = 20;
        if (text.fontSharedMaterial != null)
        {
            text.outlineColor = labelOutlineColor;
            text.outlineWidth = 0.2f;
        }

        RouteLabelBillboard billboard = labelObject.AddComponent<RouteLabelBillboard>();
        billboard.targetCamera = Camera.main;
        labelObject.SetActive(false);

        return new ContextLabelRecord
        {
            instance = labelObject,
            isBuilding = isBuilding,
            unscaledCapHeight = GetUnscaledCapHeight(text)
        };
    }

    private void RefreshContextLabels(float localFocusRadius)
    {
        Vector2 focus = new Vector2(currentCanonicalLocalFocus.x, currentCanonicalLocalFocus.z);
        float buildingOffset = GetContentLocalDistance(buildingLabelHeightOffsetWorld);
        float streetOffset = GetContentLocalDistance(streetLabelHeightOffsetWorld);

        for (int i = 0; i < contextLabels.Count; i++)
        {
            ContextLabelRecord record = contextLabels[i];
            if (record?.instance == null)
                continue;

            bool visible;
            Vector3 anchor = Vector3.zero;
            if (record.isBuilding)
            {
                visible = showBuildingLabels &&
                    frozenBuildingIds.Contains(record.buildingId);
                anchor = record.canonicalLocalAnchor + Vector3.up * buildingOffset;
            }
            else
            {
                visible = showStreetLabels && TryGetClosestRoadAnchor(
                    record.roadSegments,
                    focus,
                    localFocusRadius,
                    out anchor);
                anchor += Vector3.up * streetOffset;
            }

            record.instance.transform.localPosition = anchor;
            if (record.instance.activeSelf != visible)
                record.instance.SetActive(visible);
        }
    }

    private static bool TryGetClosestRoadAnchor(
        List<RoadSegmentRecord> segments,
        Vector2 focus,
        float focusRadius,
        out Vector3 anchor)
    {
        anchor = Vector3.zero;
        float bestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < segments.Count; i++)
        {
            RoadSegmentRecord segment = segments[i];
            Vector2 start = new Vector2(
                segment.canonicalLocalStart.x,
                segment.canonicalLocalStart.z);
            Vector2 end = new Vector2(
                segment.canonicalLocalEnd.x,
                segment.canonicalLocalEnd.z);
            Vector2 delta = end - start;
            float denominator = Vector2.Dot(delta, delta);
            float t = denominator > 0.000001f
                ? Mathf.Clamp01(Vector2.Dot(focus - start, delta) / denominator)
                : 0f;
            Vector2 closest = start + delta * t;
            float distance = Vector2.Distance(focus, closest);
            if (distance > focusRadius || distance >= bestDistance)
                continue;

            bestDistance = distance;
            anchor = Vector3.Lerp(
                segment.canonicalLocalStart,
                segment.canonicalLocalEnd,
                t);
            found = true;
        }

        return found;
    }

    private void ApplyContextLabelScale(float canonicalScale)
    {
        for (int i = 0; i < contextLabels.Count; i++)
        {
            ContextLabelRecord record = contextLabels[i];
            if (record?.instance == null)
                continue;

            float targetCharacterHeight = record.isBuilding
                ? buildingLabelCharacterHeightWorld
                : streetLabelCharacterHeightWorld;
            float localScale = targetCharacterHeight /
                Mathf.Max(
                    record.unscaledCapHeight * canonicalScale * magnificationFactor,
                    0.0001f);
            record.instance.transform.localScale = Vector3.one * localScale;
        }
    }

    private static float GetUnscaledCapHeight(TextMeshPro text)
    {
        if (text == null || text.font == null)
            return 0.068f;

        float pointSize = Mathf.Max(text.font.faceInfo.pointSize, 0.0001f);
        float projectionScale = text.isOrthographic ? 1f : 0.1f;
        float capHeight =
            text.font.faceInfo.capLine *
            (text.fontSize / pointSize) *
            text.font.faceInfo.scale *
            projectionScale;
        return capHeight > 0.001f ? capHeight : 0.068f;
    }

    private float GetContentLocalDistance(float worldDistance)
    {
        float canonicalScale = Mathf.Max(GetUniformWorldScale(canonicalRoot), 0.0001f);
        return worldDistance / Mathf.Max(canonicalScale * magnificationFactor, 0.0001f);
    }

    private static string BuildBuildingLabel(CityBuilding building)
    {
        if (building == null)
            return string.Empty;

        string displayName = building.displayName?.Trim();
        string id = building.id?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
            return id ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id) ||
            string.Equals(displayName, id, StringComparison.Ordinal))
        {
            return displayName;
        }

        return displayName + "\n" + id;
    }

    private static string GetConfiguredStreetName(Road road)
    {
        if (road == null)
            return string.Empty;

        if (road.marker != null)
            return road.marker.displayName?.Trim() ?? string.Empty;

        return road.displayName?.Trim() ?? string.Empty;
    }

    private static Vector3 GetTopAlongDirection(Bounds bounds, Vector3 upDirection)
    {
        if (upDirection.sqrMagnitude < 0.000001f)
            upDirection = Vector3.up;
        upDirection.Normalize();

        Vector3 extents = bounds.extents;
        float directionalExtent =
            Mathf.Abs(upDirection.x) * extents.x +
            Mathf.Abs(upDirection.y) * extents.y +
            Mathf.Abs(upDirection.z) * extents.z;
        return bounds.center + upDirection * directionalExtent;
    }

    private void RefreshVisibleRegion(float localFocusRadius)
    {
        for (int i = 0; i < rendererCopies.Count; i++)
        {
            RendererCopyRecord record = rendererCopies[i];
            if (record?.source == null || record.copy == null)
                continue;

            bool isBuilding = !string.IsNullOrWhiteSpace(record.buildingId);
            Vector3 inclusionCenter = record.canonicalLocalCenter;
            Vector2 center = new Vector2(inclusionCenter.x, inclusionCenter.z);
            Vector2 focus = new Vector2(currentCanonicalLocalFocus.x, currentCanonicalLocalFocus.z);
            float distance = Vector2.Distance(center, focus);
            bool inside = isBuilding
                ? frozenBuildingIds.Contains(record.buildingId)
                : distance <= localFocusRadius + record.canonicalLocalPlanarRadius;
            bool sourceVisible = record.source.enabled && record.source.gameObject.activeInHierarchy;
            record.copy.gameObject.SetActive(inside && sourceVisible);
        }

        RefreshContextLabels(localFocusRadius);
        RefreshBuildingAppearance();
    }

    private void RefreshBuildingAppearance()
    {
        for (int i = 0; i < rendererCopies.Count; i++)
        {
            RendererCopyRecord record = rendererCopies[i];
            if (record?.source == null || record.copy == null || string.IsNullOrWhiteSpace(record.buildingId))
                continue;

            MaterialPropertyBlock sourceBlock = new MaterialPropertyBlock();
            record.source.GetPropertyBlock(sourceBlock);
            record.sourceBlock = sourceBlock;
            record.copy.SetPropertyBlock(sourceBlock);

            if (record.buildingId == hoveredBuildingId)
                ApplyRendererColor(record.copy, hoverColor);
            else if (record.buildingId == selectedStartId)
                ApplyRendererColor(record.copy, startColor);
            else if (record.buildingId == selectedDestinationId)
                ApplyRendererColor(record.copy, destinationColor);
        }
    }

    private bool ShouldCopy(MeshRenderer source)
    {
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

    private bool IsTerrainRenderer(Renderer source)
    {
        // Do not inspect arbitrary ancestors here: this city is grouped under
        // an aggregate object named "RealWorld Terrain", which also contains
        // every building and road renderer. Actual terrain meshes retain a
        // terrain-specific renderer name (the active one is "TerrainMesh").
        return source != null &&
            source.name.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private Material[] CreateCircularClipMaterials(Material[] sourceMaterials)
    {
        Shader clipShader = Resources.Load<Shader>("MagnificationLensClip");
        if (clipShader == null)
            clipShader = Shader.Find("MRFlood/Magnification Lens Circular Clip");

        if (clipShader == null)
        {
            Debug.LogError("MagnificationLensView: circular clipping shader is missing; terrain cannot be presented safely in the lens.");
            return null;
        }

        int count = Mathf.Max(1, sourceMaterials?.Length ?? 0);
        Material[] clipped = new Material[count];
        for (int i = 0; i < count; i++)
        {
            Material material = new Material(clipShader)
            {
                name = $"Assisted Lens Terrain {i}"
            };
            Material source = sourceMaterials != null && i < sourceMaterials.Length
                ? sourceMaterials[i]
                : null;
            SynchronizeSurfaceMaterial(source, material);
            clipped[i] = material;
            runtimeMaterials.Add(material);
        }

        return clipped;
    }

    private void UpdateCircularClipMaterials(Vector3 lensCenterWorld, Vector3 lensUpWorld)
    {
        float radiusWorld = focusRadiusWorld * magnificationFactor;
        Vector4 center = new Vector4(
            lensCenterWorld.x,
            lensCenterWorld.y,
            lensCenterWorld.z,
            1f);
        Vector4 up = new Vector4(
            lensUpWorld.x,
            lensUpWorld.y,
            lensUpWorld.z,
            0f);

        for (int i = 0; i < rendererCopies.Count; i++)
        {
            RendererCopyRecord record = rendererCopies[i];
            if (record?.circularClipMaterials == null)
                continue;

            Material[] sourceMaterials = record.source != null
                ? record.source.sharedMaterials
                : null;
            for (int materialIndex = 0; materialIndex < record.circularClipMaterials.Length; materialIndex++)
            {
                Material clipped = record.circularClipMaterials[materialIndex];
                if (clipped == null)
                    continue;

                Material source = sourceMaterials != null && materialIndex < sourceMaterials.Length
                    ? sourceMaterials[materialIndex]
                    : null;
                SynchronizeSurfaceMaterial(source, clipped);
                clipped.SetVector(LensCenterWorldId, center);
                clipped.SetVector(LensUpWorldId, up);
                clipped.SetFloat(LensRadiusWorldId, radiusWorld);
            }
        }
    }

    private static void SynchronizeSurfaceMaterial(Material source, Material target)
    {
        if (target == null)
            return;

        Texture texture = null;
        Vector2 textureScale = Vector2.one;
        Vector2 textureOffset = Vector2.zero;
        if (source != null && source.HasProperty(MainTexId))
        {
            texture = source.GetTexture(MainTexId);
            textureScale = source.GetTextureScale(MainTexId);
            textureOffset = source.GetTextureOffset(MainTexId);
        }
        else if (source != null && source.HasProperty(BaseMapId))
        {
            texture = source.GetTexture(BaseMapId);
            textureScale = source.GetTextureScale(BaseMapId);
            textureOffset = source.GetTextureOffset(BaseMapId);
        }

        target.SetTexture(MainTexId, texture);
        target.SetTextureScale(MainTexId, textureScale);
        target.SetTextureOffset(MainTexId, textureOffset);

        Color color = Color.white;
        if (source != null && source.HasProperty(ColorId))
            color = source.GetColor(ColorId);
        else if (source != null && source.HasProperty(BaseColorId))
            color = source.GetColor(BaseColorId);

        target.SetColor(ColorId, color);
    }

    private void ApplyCanonicalRelativeTransform(Transform source, Transform target)
    {
        Matrix4x4 localMatrix = canonicalRoot.worldToLocalMatrix * source.localToWorldMatrix;
        target.localPosition = localMatrix.GetColumn(3);
        target.localRotation = localMatrix.rotation;
        target.localScale = Abs(localMatrix.lossyScale);
    }

    private float GetCanonicalLocalPlanarRadius(Bounds worldBounds)
    {
        Vector3 localCenter = canonicalRoot.InverseTransformPoint(worldBounds.center);
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        float radius = 0f;

        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 corner = canonicalRoot.InverseTransformPoint(new Vector3(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z));
                    radius = Mathf.Max(radius, Vector2.Distance(
                        new Vector2(localCenter.x, localCenter.z),
                        new Vector2(corner.x, corner.z)));
                }
            }
        }

        return radius;
    }

    private void UpdateRing(float localRadius, float canonicalScale)
    {
        if (lensRing == null)
            return;

        for (int i = 0; i < lensRing.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / lensRing.positionCount;
            lensRing.SetPosition(i, new Vector3(
                Mathf.Cos(angle) * localRadius,
                0f,
                Mathf.Sin(angle) * localRadius));
        }

        lensRing.widthMultiplier = 0.004f / Mathf.Max(canonicalScale, 0.0001f);
        if (focusConnector != null)
            focusConnector.widthMultiplier = 0.002f;
    }

    private void UpdateConnector(Vector3 physicalFocusWorld, Vector3 lensPositionWorld)
    {
        if (focusConnector == null)
            return;

        focusConnector.SetPosition(0, physicalFocusWorld);
        focusConnector.SetPosition(1, lensPositionWorld);
    }

    private static void ApplyRendererColor(Renderer renderer, Color color)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor(ColorId, color);
        block.SetColor(BaseColorId, color);
        block.SetColor(EmissionColorId, color * 1.2f);
        renderer.SetPropertyBlock(block);
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

    private static void ConfigureLine(LineRenderer line, Material material, bool useWorldSpace)
    {
        line.useWorldSpace = useWorldSpace;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = material;
        line.startColor = Color.cyan;
        line.endColor = Color.cyan;
    }

    private static float GetMaximumPlanarExtent(Bounds bounds)
    {
        return Mathf.Max(bounds.size.x, bounds.size.z);
    }

    private static float GetUniformWorldScale(Transform target)
    {
        Vector3 scale = Abs(target.lossyScale);
        return (scale.x + scale.y + scale.z) / 3f;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private void OnDestroy()
    {
        ClearNearInteractionState();
        for (int i = 0; i < runtimeMaterials.Count; i++)
        {
            if (runtimeMaterials[i] != null)
                Destroy(runtimeMaterials[i]);
        }
    }
}

public sealed class MagnificationLensBuildingTarget : MonoBehaviour,
    IMixedRealityFocusHandler,
    IMixedRealityPointerHandler
{
    private MagnificationLensView owner;
    private string canonicalBuildingId;

    public void Configure(MagnificationLensView lensOwner, string buildingId)
    {
        owner = lensOwner;
        canonicalBuildingId = buildingId;
    }

    public void OnFocusEnter(FocusEventData eventData)
    {
        if (eventData?.Pointer is PokePointer)
            owner?.RegisterNearFocus(this, canonicalBuildingId, eventData.Pointer);
    }

    public void OnFocusExit(FocusEventData eventData)
    {
        if (eventData?.Pointer is PokePointer)
            owner?.UnregisterNearFocus(this, canonicalBuildingId, eventData.Pointer);
    }

    public void OnPointerClicked(MixedRealityPointerEventData eventData)
    {
        // Lens buildings are dwell-only. Consume both near and far pointer clicks
        // so a hand ray cannot race the near-hand dwell selection.
        eventData?.Use();
    }

    public void OnPointerDown(MixedRealityPointerEventData eventData) { eventData?.Use(); }
    public void OnPointerUp(MixedRealityPointerEventData eventData) { eventData?.Use(); }
    public void OnPointerDragged(MixedRealityPointerEventData eventData) { eventData?.Use(); }

    private void OnDisable()
    {
        owner?.UnregisterNearFocus(this, canonicalBuildingId, null);
    }
}
