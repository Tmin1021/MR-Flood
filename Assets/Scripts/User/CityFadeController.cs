using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.MixedReality.Toolkit.Input;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class CityFadeController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cityVisualRoot;
    [SerializeField] private SpatialObjectDetectionManager spatialObjectDetectionManager;

    [Header("Opacity")]
    [SerializeField, Range(0f, 1f)] private float normalOpacity = 1f;
    [SerializeField, Range(0f, 1f)] private float fadedOpacity = 0.2f;
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.2f;
    [SerializeField, Min(0f)] private float fadeInDuration = 0.3f;
    [SerializeField, Min(0f)] private float restoreDelay = 0.35f;

    [Header("Material Compatibility")]
    [Tooltip("Creates one temporary transparent material per unique shared city material. Material assets are not modified.")]
    [SerializeField] private bool createRuntimeTransparentVariants = true;
    [Tooltip("Also dims base color and emission so unsupported opaque shaders still show a city-wide visual response.")]
    [SerializeField, Range(0f, 1f)] private float fadedBrightness = 0.65f;

    [Header("Cleanup")]
    [SerializeField, Min(0.05f)] private float staleRequestCheckInterval = 0.25f;

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int CustomModeId = Shader.PropertyToID("_CustomMode");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

    private readonly HashSet<NearFocusKey> nearFocusRequests = new HashSet<NearFocusKey>();
    private readonly List<NearFocusKey> staleRequests = new List<NearFocusKey>(4);
    private readonly List<MaterialSlotState> materialSlots = new List<MaterialSlotState>();
    private readonly Dictionary<Renderer, List<MaterialSlotState>> slotsByRenderer =
        new Dictionary<Renderer, List<MaterialSlotState>>();
    private readonly Dictionary<Renderer, Material[]> originalMaterialsByRenderer =
        new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Renderer, Material[]> fadeMaterialsByRenderer =
        new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Material, Material> fadeMaterialByOriginal =
        new Dictionary<Material, Material>();

    private MaterialPropertyBlock propertyBlock;
    private float currentOpacity = 1f;
    private float animationStartOpacity = 1f;
    private float targetOpacity = 1f;
    private float animationStartTime;
    private float animationDuration;
    private float restoreAtTime = -1f;
    private float nextStaleRequestCheckTime;
    private bool isAnimating;
    private bool isSubscribed;
    private bool featureAvailable;
    private bool missingReferenceWarningLogged;
    private bool transparentVariantsActive;

    public bool IsFaded => currentOpacity < normalOpacity - 0.001f;
    public bool HasActiveNearFocus => nearFocusRequests.Count > 0;
    public float CurrentOpacity => currentOpacity;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        currentOpacity = normalOpacity;
        targetOpacity = normalOpacity;

        ResolveReferencesOnce();
        RefreshRenderers();
    }

    private void OnEnable()
    {
        SubscribeToModeChanges();
    }

    private void Start()
    {
        // A city may be constructed by another component during Start.
        if (featureAvailable && materialSlots.Count == 0)
            RefreshRenderers();
    }

    private void Update()
    {
        float now = Time.unscaledTime;

        if (nearFocusRequests.Count > 0 && now >= nextStaleRequestCheckTime)
        {
            nextStaleRequestCheckTime = now + staleRequestCheckInterval;
            CleanupInvalidRegistrations();
        }

        if (restoreAtTime >= 0f && nearFocusRequests.Count == 0 && now >= restoreAtTime)
        {
            restoreAtTime = -1f;
            BeginTransition(normalOpacity, fadeInDuration);
        }

        if (!isAnimating)
            return;

        float t = animationDuration <= 0f
            ? 1f
            : Mathf.Clamp01((now - animationStartTime) / animationDuration);

        currentOpacity = Mathf.Lerp(animationStartOpacity, targetOpacity, t);
        ApplyOpacityToCachedRenderers();

        if (t < 1f)
            return;

        currentOpacity = targetOpacity;
        isAnimating = false;

        if (Mathf.Approximately(currentOpacity, normalOpacity))
        {
            SetTransparentVariantsActive(false);
            Debug.Log("CityFadeController: restored normal state.");
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromModeChanges();
        nearFocusRequests.Clear();
        RestoreImmediately();
    }

    private void OnDestroy()
    {
        UnsubscribeFromModeChanges();
        RestoreImmediately();
        RestoreOriginalMaterialAssignments();
        DestroyRuntimeMaterialVariants();
    }

    public void RegisterNearFocus(BuildingPoint building, IMixedRealityPointer pointer)
    {
        if (!featureAvailable || building == null || !(pointer is PokePointer) || !IsPlacementModeActive())
            return;

        NearFocusKey key = new NearFocusKey(building, pointer);
        if (!nearFocusRequests.Add(key))
            return;

        restoreAtTime = -1f;
        nextStaleRequestCheckTime = Time.unscaledTime + staleRequestCheckInterval;

        if (nearFocusRequests.Count == 1)
        {
            BeginTransition(fadedOpacity, fadeOutDuration);
            Debug.Log("CityFadeController: entered faded state.");
        }
    }

    public void UnregisterNearFocus(BuildingPoint building, IMixedRealityPointer pointer)
    {
        if (building == null || pointer == null)
            return;

        if (nearFocusRequests.Remove(new NearFocusKey(building, pointer)))
            ScheduleRestoreIfUnfocused();
    }

    public void UnregisterBuilding(BuildingPoint building)
    {
        if (building == null && ReferenceEquals(building, null))
            return;

        staleRequests.Clear();
        foreach (NearFocusKey request in nearFocusRequests)
        {
            if (ReferenceEquals(request.Building, building))
                staleRequests.Add(request);
        }

        RemoveCollectedRequests();
    }

    public void ClearAllNearFocusRequests()
    {
        nearFocusRequests.Clear();
        ScheduleRestoreIfUnfocused();
    }

    public void CleanupInvalidRegistrations()
    {
        staleRequests.Clear();

        foreach (NearFocusKey request in nearFocusRequests)
        {
            if (!IsRequestValid(request))
                staleRequests.Add(request);
        }

        RemoveCollectedRequests();
    }

    public void RefreshRenderers()
    {
        if (cityVisualRoot == null)
        {
            featureAvailable = false;
            WarnMissingReferenceOnce("City Visual Root is not assigned and could not be resolved.");
            return;
        }

        RestoreOriginalMaterialAssignments();
        DestroyRuntimeMaterialVariants();
        materialSlots.Clear();
        slotsByRenderer.Clear();

        Renderer[] renderers = cityVisualRoot.GetComponentsInChildren<Renderer>(true);
        Debug.Log("Renderers detected: " + renderers.Length);
        int unsupportedMaterialCount = 0;

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            Material[] assignedMaterials = null;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material originalMaterial = materials[materialIndex];
                if (originalMaterial == null)
                    continue;

                Material fadeMaterial = GetOrCreateFadeMaterial(originalMaterial);
                if (fadeMaterial != originalMaterial)
                {
                    if (assignedMaterials == null)
                        assignedMaterials = (Material[])materials.Clone();

                    assignedMaterials[materialIndex] = fadeMaterial;
                }

                MaterialSlotState slot = new MaterialSlotState(renderer, materialIndex, fadeMaterial);
                if (!slot.HasAnySupportedProperty)
                {
                    unsupportedMaterialCount++;
                    continue;
                }

                materialSlots.Add(slot);

                if (!slotsByRenderer.TryGetValue(renderer, out List<MaterialSlotState> rendererSlots))
                {
                    rendererSlots = new List<MaterialSlotState>(materials.Length);
                    slotsByRenderer.Add(renderer, rendererSlots);
                }

                rendererSlots.Add(slot);
            }

            if (assignedMaterials != null)
            {
                originalMaterialsByRenderer.Add(renderer, materials);
                fadeMaterialsByRenderer.Add(renderer, assignedMaterials);
            }
        }

        featureAvailable = spatialObjectDetectionManager != null && materialSlots.Count > 0;
        SetTransparentVariantsActive(
            currentOpacity < normalOpacity - 0.001f || targetOpacity < normalOpacity - 0.001f);
        ApplyOpacityToCachedRenderers();

        // Re-compose any hover/flood state which existed before this refresh.
        BuildingPoint[] buildingPoints = cityVisualRoot.GetComponentsInChildren<BuildingPoint>(true);
        for (int i = 0; i < buildingPoints.Length; i++)
            buildingPoints[i]?.ReapplyVisualState();

        Debug.Log(
            $"CityFadeController: initialized with {renderers.Length} renderer(s) and " +
            $"{materialSlots.Count} supported material slot(s); " +
            $"created {CountRuntimeMaterialVariants()} shared transparent material variant(s).");

        if (unsupportedMaterialCount > 0)
        {
            Debug.LogWarning(
                $"CityFadeController: skipped {unsupportedMaterialCount} unsupported material slot(s). " +
                "Supported color properties are _Color, _BaseColor, and _EmissionColor.");
        }
    }

    public void RestoreImmediately()
    {
        bool wasFaded = IsFaded || targetOpacity < normalOpacity - 0.001f;
        restoreAtTime = -1f;
        isAnimating = false;
        targetOpacity = normalOpacity;
        currentOpacity = normalOpacity;
        ApplyOpacityToCachedRenderers();
        SetTransparentVariantsActive(false);

        if (wasFaded)
            Debug.Log("CityFadeController: restored normal state.");
    }

    public bool SetSemanticEmission(
        Renderer renderer,
        int materialIndex,
        bool hasOverride,
        Color emissionColor)
    {
        if (renderer == null ||
            !slotsByRenderer.TryGetValue(renderer, out List<MaterialSlotState> rendererSlots))
        {
            return false;
        }

        for (int i = 0; i < rendererSlots.Count; i++)
        {
            MaterialSlotState slot = rendererSlots[i];
            if (slot.MaterialIndex != materialIndex || !slot.HasEmission)
                continue;

            slot.HasSemanticEmissionOverride = hasOverride;
            slot.SemanticEmission = emissionColor;
            ApplySlot(slot);
            return true;
        }

        return false;
    }

    [ContextMenu("Test Fade")]
    private void TestFade()
    {
        if (!Application.isPlaying)
        {
            currentOpacity = fadedOpacity;
            targetOpacity = fadedOpacity;
            ApplyOpacityToCachedRenderers();
            return;
        }

        restoreAtTime = -1f;
        BeginTransition(fadedOpacity, fadeOutDuration);
    }

    [ContextMenu("Test Restore")]
    private void TestRestore()
    {
        if (!Application.isPlaying)
        {
            RestoreImmediately();
            return;
        }

        BeginTransition(normalOpacity, fadeInDuration);
    }

    private void ResolveReferencesOnce()
    {
        spatialObjectDetectionManager ??=
            FindFirstObjectByType<SpatialObjectDetectionManager>(FindObjectsInactive.Include);

        if (cityVisualRoot == null)
        {
            CityAnchorManager anchorManager =
                FindFirstObjectByType<CityAnchorManager>(FindObjectsInactive.Include);
            if (anchorManager != null)
                cityVisualRoot = anchorManager.CityAnchorRoot;
        }

        if (cityVisualRoot == null)
        {
            PointSelectManager pointSelectManager =
                FindFirstObjectByType<PointSelectManager>(FindObjectsInactive.Include);
            if (pointSelectManager != null)
                cityVisualRoot = pointSelectManager.cityRoot;
        }

        if (spatialObjectDetectionManager == null)
            WarnMissingReferenceOnce("SpatialObjectDetectionManager is not assigned and could not be resolved.");
    }

    private void SubscribeToModeChanges()
    {
        if (isSubscribed || spatialObjectDetectionManager == null)
            return;

        spatialObjectDetectionManager.ModeChanged += HandleModeChanged;
        isSubscribed = true;
    }

    private void UnsubscribeFromModeChanges()
    {
        if (!isSubscribed || spatialObjectDetectionManager == null)
            return;

        spatialObjectDetectionManager.ModeChanged -= HandleModeChanged;
        isSubscribed = false;
    }

    private void HandleModeChanged(SpatialPlacementMode mode)
    {
        if (mode == SpatialPlacementMode.BuildingPlacing || mode == SpatialPlacementMode.FloodPlacing)
            return;

        nearFocusRequests.Clear();
        RestoreImmediately();
    }

    private bool IsPlacementModeActive()
    {
        if (spatialObjectDetectionManager == null)
            return false;

        SpatialPlacementMode mode = spatialObjectDetectionManager.CurrentMode;
        return mode == SpatialPlacementMode.BuildingPlacing ||
               mode == SpatialPlacementMode.FloodPlacing;
    }

    private bool IsRequestValid(NearFocusKey request)
    {
        BuildingPoint building = request.Building;
        IMixedRealityPointer pointer = request.Pointer;

        if (building == null || !building.isActiveAndEnabled || pointer == null)
            return false;

        if (pointer is Object pointerObject && pointerObject == null)
            return false;

        if (!pointer.IsActive || !pointer.IsInteractionEnabled)
            return false;

        GameObject focusedObject = pointer.Result?.CurrentPointerTarget;
        return focusedObject != null &&
               (focusedObject == building.gameObject || focusedObject.transform.IsChildOf(building.transform));
    }

    private void RemoveCollectedRequests()
    {
        if (staleRequests.Count == 0)
            return;

        for (int i = 0; i < staleRequests.Count; i++)
            nearFocusRequests.Remove(staleRequests[i]);

        staleRequests.Clear();
        ScheduleRestoreIfUnfocused();
    }

    private void ScheduleRestoreIfUnfocused()
    {
        if (nearFocusRequests.Count > 0)
            return;

        restoreAtTime = Time.unscaledTime + restoreDelay;
    }

    private void BeginTransition(float newTargetOpacity, float duration)
    {
        targetOpacity = Mathf.Clamp01(newTargetOpacity);

        if (targetOpacity < normalOpacity - 0.001f)
            SetTransparentVariantsActive(true);

        animationStartOpacity = currentOpacity;
        animationStartTime = Time.unscaledTime;
        animationDuration = Mathf.Max(0f, duration);
        isAnimating = !Mathf.Approximately(currentOpacity, targetOpacity);

        if (!isAnimating)
        {
            currentOpacity = targetOpacity;
            ApplyOpacityToCachedRenderers();
        }
    }

    private void ApplyOpacityToCachedRenderers()
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        for (int i = 0; i < materialSlots.Count; i++)
            ApplySlot(materialSlots[i]);
    }

    private void ApplySlot(MaterialSlotState slot)
    {
        if (slot.Renderer == null)
            return;

        slot.Renderer.GetPropertyBlock(propertyBlock, slot.MaterialIndex);

        float brightness = GetBrightnessMultiplier();

        if (slot.HasColor)
        {
            Color color = slot.OriginalColor;
            color.r *= brightness;
            color.g *= brightness;
            color.b *= brightness;
            color.a *= currentOpacity;
            propertyBlock.SetColor(ColorId, color);
        }

        if (slot.HasBaseColor)
        {
            Color color = slot.OriginalBaseColor;
            color.r *= brightness;
            color.g *= brightness;
            color.b *= brightness;
            color.a *= currentOpacity;
            propertyBlock.SetColor(BaseColorId, color);
        }

        if (slot.HasEmission)
        {
            Color emission = slot.HasSemanticEmissionOverride
                ? slot.SemanticEmission
                : slot.OriginalEmission;
            propertyBlock.SetColor(EmissionColorId, emission * brightness);
        }

        slot.Renderer.SetPropertyBlock(propertyBlock, slot.MaterialIndex);
        propertyBlock.Clear();
    }

    private float GetBrightnessMultiplier()
    {
        if (Mathf.Approximately(normalOpacity, fadedOpacity))
            return currentOpacity < normalOpacity ? fadedBrightness : 1f;

        float fadeProgress = Mathf.InverseLerp(normalOpacity, fadedOpacity, currentOpacity);
        return Mathf.Lerp(1f, fadedBrightness, fadeProgress);
    }

    private Material GetOrCreateFadeMaterial(Material originalMaterial)
    {
        if (!createRuntimeTransparentVariants || originalMaterial == null)
            return originalMaterial;

        if (fadeMaterialByOriginal.TryGetValue(originalMaterial, out Material existingMaterial))
            return existingMaterial;

        if (!CanConfigureTransparentFade(originalMaterial))
        {
            fadeMaterialByOriginal.Add(originalMaterial, originalMaterial);
            return originalMaterial;
        }

        Material fadeMaterial = new Material(originalMaterial)
        {
            name = originalMaterial.name + " (City Fade Runtime)",
            hideFlags = HideFlags.DontSave
        };

        bool wasOpaque = IsOpaqueMaterial(originalMaterial);
        if (wasOpaque)
            NormalizeUnusedOpaqueAlpha(fadeMaterial);

        ConfigureTransparentFade(fadeMaterial);
        fadeMaterialByOriginal.Add(originalMaterial, fadeMaterial);
        return fadeMaterial;
    }

    private static bool CanConfigureTransparentFade(Material material)
    {
        return material != null &&
               (material.HasProperty(ColorId) || material.HasProperty(BaseColorId)) &&
               material.HasProperty(SrcBlendId) &&
               material.HasProperty(DstBlendId) &&
               material.HasProperty(ZWriteId);
    }

    private static bool IsOpaqueMaterial(Material material)
    {
        if (material.HasProperty(ModeId))
            return material.GetFloat(ModeId) < 1f;

        string renderType = material.GetTag("RenderType", false, string.Empty);
        return material.renderQueue < (int)RenderQueue.Transparent &&
               renderType != "Transparent";
    }

    private static void NormalizeUnusedOpaqueAlpha(Material material)
    {
        if (material.HasProperty(ColorId))
        {
            Color color = material.GetColor(ColorId);
            color.a = 1f;
            material.SetColor(ColorId, color);
        }

        if (material.HasProperty(BaseColorId))
        {
            Color color = material.GetColor(BaseColorId);
            color.a = 1f;
            material.SetColor(BaseColorId, color);
        }
    }

    private static void ConfigureTransparentFade(Material material)
    {
        if (material.HasProperty(ModeId))
            material.SetFloat(ModeId, 2f);

        if (material.HasProperty(CustomModeId))
            material.SetFloat(CustomModeId, 2f);

        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt(SrcBlendId, (int)BlendMode.SrcAlpha);
        material.SetInt(DstBlendId, (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt(ZWriteId, 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private void RestoreOriginalMaterialAssignments()
    {
        foreach (KeyValuePair<Renderer, Material[]> assignment in originalMaterialsByRenderer)
        {
            if (assignment.Key != null)
                assignment.Key.sharedMaterials = assignment.Value;
        }

        originalMaterialsByRenderer.Clear();
        fadeMaterialsByRenderer.Clear();
        transparentVariantsActive = false;
    }

    private void SetTransparentVariantsActive(bool active)
    {
        if (transparentVariantsActive == active)
            return;

        Dictionary<Renderer, Material[]> assignments = active
            ? fadeMaterialsByRenderer
            : originalMaterialsByRenderer;

        foreach (KeyValuePair<Renderer, Material[]> assignment in assignments)
        {
            if (assignment.Key != null)
                assignment.Key.sharedMaterials = assignment.Value;
        }

        transparentVariantsActive = active;
    }

    private void DestroyRuntimeMaterialVariants()
    {
        foreach (KeyValuePair<Material, Material> pair in fadeMaterialByOriginal)
        {
            Material material = pair.Value;
            if (material == null || material == pair.Key)
                continue;

            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
        }

        fadeMaterialByOriginal.Clear();
    }

    private int CountRuntimeMaterialVariants()
    {
        int count = 0;
        foreach (KeyValuePair<Material, Material> pair in fadeMaterialByOriginal)
        {
            if (pair.Value != null && pair.Value != pair.Key)
                count++;
        }

        return count;
    }

    private void WarnMissingReferenceOnce(string message)
    {
        if (missingReferenceWarningLogged)
            return;

        missingReferenceWarningLogged = true;
        Debug.LogWarning($"CityFadeController: {message} Fading is disabled; city interaction remains available.");
    }

    private readonly struct NearFocusKey : System.IEquatable<NearFocusKey>
    {
        public BuildingPoint Building { get; }
        public IMixedRealityPointer Pointer { get; }

        public NearFocusKey(BuildingPoint building, IMixedRealityPointer pointer)
        {
            Building = building;
            Pointer = pointer;
        }

        public bool Equals(NearFocusKey other)
        {
            return ReferenceEquals(Building, other.Building) && ReferenceEquals(Pointer, other.Pointer);
        }

        public override bool Equals(object obj)
        {
            return obj is NearFocusKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int buildingHash = ReferenceEquals(Building, null) ? 0 : RuntimeHelpers.GetHashCode(Building);
                int pointerHash = ReferenceEquals(Pointer, null) ? 0 : RuntimeHelpers.GetHashCode(Pointer);
                return (buildingHash * 397) ^ pointerHash;
            }
        }
    }

    private sealed class MaterialSlotState
    {
        public Renderer Renderer { get; }
        public int MaterialIndex { get; }
        public bool HasColor { get; }
        public bool HasBaseColor { get; }
        public bool HasEmission { get; }
        public Color OriginalColor { get; }
        public Color OriginalBaseColor { get; }
        public Color OriginalEmission { get; }
        public bool HasAnySupportedProperty => HasColor || HasBaseColor || HasEmission;
        public bool HasSemanticEmissionOverride { get; set; }
        public Color SemanticEmission { get; set; }

        public MaterialSlotState(Renderer renderer, int materialIndex, Material material)
        {
            Renderer = renderer;
            MaterialIndex = materialIndex;
            HasColor = material.HasProperty(ColorId);
            HasBaseColor = material.HasProperty(BaseColorId);
            HasEmission = material.HasProperty(EmissionColorId);
            OriginalColor = HasColor ? material.GetColor(ColorId) : Color.white;
            OriginalBaseColor = HasBaseColor ? material.GetColor(BaseColorId) : Color.white;
            OriginalEmission = HasEmission ? material.GetColor(EmissionColorId) : Color.black;
            SemanticEmission = OriginalEmission;
        }
    }
}
