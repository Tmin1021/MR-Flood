using Microsoft.MixedReality.Toolkit.Input;
using System.Collections.Generic;
using UnityEngine;

public class BuildingPoint : MonoBehaviour, IMixedRealityFocusHandler, IMixedRealityPointerHandler
{
    [Header("References")]
    [System.NonSerialized] public PointSelectManager manager;
    [System.NonSerialized] public CityBuilding buildingData;

    [Header("Anchor")]
    [SerializeField] private Transform interactionAnchor;

    [Header("Near-Hand Dwell Selection")]
    [SerializeField, Range(2f, 3f)] private float nearDwellSelectionSeconds = 2.5f;

    [Header("Hover Highlight")]
    public Color hoverEmission = Color.yellow * 2f;

    [Header("Flood Highlight")]
    public Color floodedEmission = Color.red * 2f;

    private Renderer[] rends;
    private readonly List<EmissionSlot> emissionSlots = new List<EmissionSlot>();
    private readonly List<IMixedRealityPointer> activeNearPointers = new List<IMixedRealityPointer>(2);
    private MaterialPropertyBlock propertyBlock;
    private bool isHovered;
    private bool lastFloodedState;
    private bool handInteractionEnabled = true;
    private float nearDwellStartedAt = -1f;
    private bool nearDwellTriggered;

    public Transform AnchorTransform => interactionAnchor != null ? interactionAnchor : transform;
    public bool HandInteractionEnabled => handInteractionEnabled;
    public bool HasActiveNearFocus => activeNearPointers.Count > 0;
    public float NearDwellSelectionSeconds => nearDwellSelectionSeconds;
    public float NearDwellRemainingSeconds => !HasActiveNearFocus || nearDwellStartedAt < 0f
        ? nearDwellSelectionSeconds
        : Mathf.Max(0f, nearDwellSelectionSeconds - (Time.unscaledTime - nearDwellStartedAt));

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        CacheRenderers();
        lastFloodedState = IsFlooded();
    }

    private void Update()
    {
        bool flooded = IsFlooded();
        if (flooded != lastFloodedState)
        {
            lastFloodedState = flooded;
            ApplyEmissionState();
        }

        UpdateNearDwellSelection();
    }

    public void Configure(
        PointSelectManager newManager,
        CityBuilding building,
        Transform anchor = null)
    {
        manager = newManager;
        buildingData = building;

        if (newManager != null)
            nearDwellSelectionSeconds = newManager.NearDwellSelectionDuration;

        if (anchor != null)
            interactionAnchor = anchor;

        CacheRenderers();
        lastFloodedState = IsFlooded();
        ApplyEmissionState();
    }

    public Vector3 GetAnchorWorldPosition()
    {
        return AnchorTransform.position;
    }

    public void SetHandInteractionEnabled(bool enabled)
    {
        if (handInteractionEnabled == enabled)
            return;

        handInteractionEnabled = enabled;
        if (enabled)
            return;

        UnregisterAllNearPointers();
        SetHoverHighlight(false);
        manager?.HideTag(this);
    }

    public Vector3 GetTopWorldPosition()
    {
        return GetTopWorldPosition(Vector3.up);
    }

    /// <summary>
    /// Returns the point at the top of this building's rendered bounds in the supplied
    /// world-space direction. Renderer bounds already include the current city scale.
    /// </summary>
    public Vector3 GetTopWorldPosition(Vector3 upDirection)
    {
        if (upDirection.sqrMagnitude < 0.000001f)
            upDirection = Vector3.up;

        upDirection.Normalize();

        if (rends == null || rends.Length == 0)
            return GetAnchorWorldPosition();

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);

        Vector3 extents = b.extents;
        float directionalExtent =
            Mathf.Abs(upDirection.x) * extents.x +
            Mathf.Abs(upDirection.y) * extents.y +
            Mathf.Abs(upDirection.z) * extents.z;

        return b.center + upDirection * directionalExtent;
    }

    public float GetBaseWorldY()
    {
        if (rends == null || rends.Length == 0)
            return transform.position.y;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);

        return b.min.y;
    }

    public bool IsFlooded()
    {
        return buildingData != null && buildingData.isFlooded;
    }

    public string GetDisplayName()
    {
        if (buildingData == null) return gameObject.name;
        if (!string.IsNullOrWhiteSpace(buildingData.displayName)) return buildingData.displayName;
        if (!string.IsNullOrWhiteSpace(buildingData.id)) return buildingData.id;
        return gameObject.name;
    }

    public void OnFocusEnter(FocusEventData eventData)
    {
        if (!handInteractionEnabled)
            return;

        if (eventData != null && eventData.Pointer is PokePointer)
        {
            bool wasUnfocused = activeNearPointers.Count == 0;
            AddActiveNearPointer(eventData.Pointer);

            if (wasUnfocused && activeNearPointers.Count > 0)
                StartNearDwell();
        }

        SetHoverHighlight(true);
        manager?.ShowTag(this);
    }

    public void OnFocusExit(FocusEventData eventData)
    {
        if (!handInteractionEnabled)
            return;

        SetHoverHighlight(false);
        manager?.HideTag(this);

        if (eventData != null && eventData.Pointer is PokePointer)
        {
            RemoveActiveNearPointer(eventData.Pointer);

            if (activeNearPointers.Count == 0)
                CancelNearDwell();
        }
        else
        {
            RemoveInvalidLocalPointers();
        }
    }

    public void SetHoverHighlight(bool highlighted)
    {
        isHovered = highlighted;
        ApplyEmissionState();
    }

    private void ApplyEmissionState()
    {
        if (emissionSlots.Count == 0) return;

        bool flooded = IsFlooded();
        bool hasOverride = flooded || isHovered;
        Color semanticEmission = flooded ? floodedEmission : hoverEmission;

        for (int i = 0; i < emissionSlots.Count; i++)
        {
            EmissionSlot slot = emissionSlots[i];

            slot.Renderer.GetPropertyBlock(propertyBlock, slot.MaterialIndex);
            propertyBlock.SetColor(
                "_EmissionColor",
                hasOverride ? semanticEmission : slot.OriginalEmission);
            slot.Renderer.SetPropertyBlock(propertyBlock, slot.MaterialIndex);
            propertyBlock.Clear();
        }
    }

    public void ReapplyVisualState()
    {
        ApplyEmissionState();
    }

    public void OnPointerClicked(MixedRealityPointerEventData eventData)
    {
        if (!handInteractionEnabled)
            return;

        if (!(eventData.Pointer is PokePointer))
            manager?.SelectPoint(this);
    }

    public void OnPointerDown(MixedRealityPointerEventData eventData)
    {
        if (!handInteractionEnabled)
            return;

        if (eventData.Pointer is PokePointer)
            eventData.Use();
    }

    public void OnPointerUp(MixedRealityPointerEventData eventData) { }
    public void OnPointerDragged(MixedRealityPointerEventData eventData) { }

    private void OnDisable()
    {
        UnregisterAllNearPointers();
    }

    private void OnDestroy()
    {
        UnregisterAllNearPointers();
    }

    private void CacheRenderers()
    {
        rends = GetComponentsInChildren<Renderer>(true);
        emissionSlots.Clear();

        for (int i = 0; i < rends.Length; i++)
        {
            Renderer renderer = rends[i];
            Material[] materials = renderer.sharedMaterials;

            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null || !material.HasProperty("_EmissionColor"))
                    continue;

                emissionSlots.Add(new EmissionSlot(
                    renderer,
                    materialIndex,
                    material.GetColor("_EmissionColor")));
            }
        }
    }

    private void AddActiveNearPointer(IMixedRealityPointer pointer)
    {
        for (int i = 0; i < activeNearPointers.Count; i++)
        {
            if (ReferenceEquals(activeNearPointers[i], pointer))
                return;
        }

        activeNearPointers.Add(pointer);
    }

    private void RemoveActiveNearPointer(IMixedRealityPointer pointer)
    {
        for (int i = activeNearPointers.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(activeNearPointers[i], pointer))
                activeNearPointers.RemoveAt(i);
        }
    }

    private void RemoveInvalidLocalPointers()
    {
        for (int i = activeNearPointers.Count - 1; i >= 0; i--)
        {
            IMixedRealityPointer pointer = activeNearPointers[i];
            bool invalid = pointer == null ||
                           (pointer is Object pointerObject && pointerObject == null);

            if (!invalid)
            {
                GameObject focusedObject = pointer.Result?.CurrentPointerTarget;
                invalid = !pointer.IsActive ||
                          !pointer.IsInteractionEnabled ||
                          focusedObject == null ||
                          (focusedObject != gameObject && !focusedObject.transform.IsChildOf(transform));
            }

            if (invalid)
                activeNearPointers.RemoveAt(i);
        }

        if (activeNearPointers.Count == 0)
            CancelNearDwell();
    }

    private void UnregisterAllNearPointers()
    {
        activeNearPointers.Clear();
        CancelNearDwell();
    }

    private void StartNearDwell()
    {
        nearDwellStartedAt = Time.unscaledTime;
        nearDwellTriggered = false;
    }

    private void CancelNearDwell()
    {
        nearDwellStartedAt = -1f;
        nearDwellTriggered = false;
    }

    private void UpdateNearDwellSelection()
    {
        if (nearDwellTriggered ||
            !handInteractionEnabled ||
            nearDwellStartedAt < 0f ||
            activeNearPointers.Count == 0 ||
            manager == null ||
            !manager.isActiveAndEnabled)
            return;

        if (Time.unscaledTime - nearDwellStartedAt < nearDwellSelectionSeconds)
            return;

        nearDwellTriggered = true;
        manager.SelectPoint(this);
    }

    private sealed class EmissionSlot
    {
        public Renderer Renderer { get; }
        public int MaterialIndex { get; }
        public Color OriginalEmission { get; }

        public EmissionSlot(Renderer renderer, int materialIndex, Color originalEmission)
        {
            Renderer = renderer;
            MaterialIndex = materialIndex;
            OriginalEmission = originalEmission;
        }
    }
}
