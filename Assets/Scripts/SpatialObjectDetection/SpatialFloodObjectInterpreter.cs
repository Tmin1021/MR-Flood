using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SpatialFloodObjectInterpreter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FloodManager floodManager;
    [SerializeField] private SpatialObjectPreviewPresenter previewPresenter;
    [SerializeField] private MRNotification notifier;
    [SerializeField] private Transform floodSourcesRoot;
    [SerializeField] private GameObject floodSourcePrefab;

    [Header("Flood Defaults")]
    [SerializeField] private float defaultRadius = 0.125f;
    [SerializeField] private float defaultIntensity = 1f;
    [SerializeField] private float defaultGrowthRate = 0.02f;
    [SerializeField] private bool autoExpand = false;

    [Header("Assignment")]
    [SerializeField] private bool replaceExistingFloodSourcesOnConfirm = true;
    [SerializeField] private bool clearConfirmedSourcesWhenClearingCandidates = false;

    private readonly List<PhysicalObjectCandidate> pendingCandidates = new List<PhysicalObjectCandidate>();
    private readonly List<FloodSource> dynamicFloodSources = new List<FloodSource>();

    private void Awake()
    {
        ResolveMissingReferences();
    }

    public bool PreviewFloodCandidates(List<PhysicalObjectCandidate> candidates)
    {
        ResolveMissingReferences();
        pendingCandidates.Clear();

        if (candidates != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                PhysicalObjectCandidate candidate = candidates[i];
                if (candidate != null && candidate.isValid)
                    pendingCandidates.Add(candidate);
            }
        }

        if (pendingCandidates.Count == 0)
        {
            Warn("No valid flood object candidates detected.");
            previewPresenter?.ClearFloodPreviews();
            return false;
        }

        previewPresenter?.ShowFloodCandidates(pendingCandidates, defaultRadius);
        notifier?.Show($"Previewing {pendingCandidates.Count} flood candidate(s).");
        return true;
    }

    public bool ConfirmFloodCandidates()
    {
        ResolveMissingReferences();

        if (floodManager == null)
        {
            Warn("FloodManager is not assigned.");
            return false;
        }

        if (pendingCandidates.Count == 0)
        {
            Warn("Preview at least one flood candidate before confirming.");
            return false;
        }

        SyncDynamicFloodSourcesToPendingCandidates();
        FloodSource[] assignedSources = BuildAssignedFloodSources();

        floodManager.SetFloodSources(assignedSources, true);
        previewPresenter?.ClearFloodPreviews();
        previewPresenter?.ShowConfirmedFloodSources(assignedSources);
        notifier?.Show($"Confirmed {pendingCandidates.Count} flood source(s).");

        Debug.Log($"SpatialFloodObjectInterpreter: confirmed {pendingCandidates.Count} flood source(s).");
        return true;
    }

    public void ClearFloodCandidates()
    {
        ClearFloodCandidates(clearConfirmedSourcesWhenRequested: true);
    }

    public void ClearFloodCandidatesOnly()
    {
        ClearFloodCandidates(clearConfirmedSourcesWhenRequested: false);
    }

    private void ClearFloodCandidates(bool clearConfirmedSourcesWhenRequested)
    {
        pendingCandidates.Clear();
        previewPresenter?.ClearFloodPreviews();

        if (clearConfirmedSourcesWhenRequested && clearConfirmedSourcesWhenClearingCandidates)
        {
            DestroyDynamicFloodSources();
            previewPresenter?.ClearConfirmedFloodPreviews();

            if (floodManager != null)
                floodManager.SetFloodSources(System.Array.Empty<FloodSource>(), true);
        }
    }

    private void SyncDynamicFloodSourcesToPendingCandidates()
    {
        while (dynamicFloodSources.Count > pendingCandidates.Count)
        {
            int lastIndex = dynamicFloodSources.Count - 1;
            FloodSource source = dynamicFloodSources[lastIndex];
            dynamicFloodSources.RemoveAt(lastIndex);

            if (source != null)
                DestroyObject(source.gameObject);
        }

        for (int i = 0; i < pendingCandidates.Count; i++)
        {
            FloodSource source = i < dynamicFloodSources.Count
                ? dynamicFloodSources[i]
                : null;

            if (source == null)
            {
                source = CreateFloodSource(i);

                if (i < dynamicFloodSources.Count)
                    dynamicFloodSources[i] = source;
                else
                    dynamicFloodSources.Add(source);
            }

            ConfigureFloodSource(source, pendingCandidates[i], i);
        }
    }

    private FloodSource[] BuildAssignedFloodSources()
    {
        List<FloodSource> assigned = new List<FloodSource>();

        if (!replaceExistingFloodSourcesOnConfirm && floodManager.floodSources != null)
        {
            for (int i = 0; i < floodManager.floodSources.Length; i++)
            {
                FloodSource source = floodManager.floodSources[i];
                if (source != null && !dynamicFloodSources.Contains(source))
                    assigned.Add(source);
            }
        }

        for (int i = 0; i < dynamicFloodSources.Count; i++)
        {
            if (dynamicFloodSources[i] != null)
                assigned.Add(dynamicFloodSources[i]);
        }

        return assigned.ToArray();
    }

    private FloodSource CreateFloodSource(int index)
    {
        GameObject sourceObject;

        if (floodSourcePrefab != null)
            sourceObject = Instantiate(floodSourcePrefab, floodSourcesRoot);
        else
            sourceObject = new GameObject($"SpatialFloodSource_{index}");

        if (floodSourcesRoot != null)
            sourceObject.transform.SetParent(floodSourcesRoot, true);

        FloodSource source = sourceObject.GetComponent<FloodSource>();
        if (source == null)
            source = sourceObject.AddComponent<FloodSource>();

        return source;
    }

    private void ConfigureFloodSource(FloodSource source, PhysicalObjectCandidate candidate, int index)
    {
        if (source == null || candidate == null)
            return;

        source.name = $"SpatialFloodSource_{index}";
        source.transform.position = candidate.worldPosition;
        source.intensity = defaultIntensity;
        source.radius = defaultRadius;
        source.growthRate = defaultGrowthRate;
        source.autoExpand = autoExpand;
        source.usePlanarDistance = true;
    }

    private void DestroyDynamicFloodSources()
    {
        for (int i = 0; i < dynamicFloodSources.Count; i++)
        {
            FloodSource source = dynamicFloodSources[i];
            if (source != null)
                DestroyObject(source.gameObject);
        }

        dynamicFloodSources.Clear();
    }

    private void ResolveMissingReferences()
    {
        floodManager ??= FindFirstObjectByType<FloodManager>(FindObjectsInactive.Include);
        previewPresenter ??= FindFirstObjectByType<SpatialObjectPreviewPresenter>(FindObjectsInactive.Include);
        notifier ??= FindFirstObjectByType<MRNotification>(FindObjectsInactive.Include);

        if (floodSourcesRoot == null)
            floodSourcesRoot = transform;
    }

    private void Warn(string message)
    {
        Debug.LogWarning($"SpatialFloodObjectInterpreter: {message}");
        notifier?.Show(message);
    }

    private static void DestroyObject(GameObject go)
    {
        if (go == null)
            return;

        if (Application.isPlaying)
            Destroy(go);
        else
            DestroyImmediate(go);
    }
}
