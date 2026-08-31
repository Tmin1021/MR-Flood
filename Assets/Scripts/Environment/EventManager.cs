using System.Collections;
using System.Collections.Generic;
using Microsoft.MixedReality.Toolkit.UI.BoundsControl;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EventManager : MonoBehaviour
{
    [Header("OSM Mode")]
    [SerializeField] private Renderer[] cityRenderers;  // assign parent or children renderers
    [SerializeField] private Material osmMat;
    [SerializeField] private Material bingMat;
    [SerializeField] private MainContentController citySettingsController;
    [SerializeField] private ButtonDisplay osmButtonDisplay;

    [Header("City Model")]
    [SerializeField] private GameObject cityModel;

    [Header("Nodes")]
    [SerializeField] private Transform nodesParent;
    [SerializeField] private CityPlacementManager cityPlacementManager;
    [SerializeField] private CityAnchorManager cityAnchorManager;
    private bool useOsmTerrainTexture = true;
    private Coroutine buttonStateSync;

    private void Awake()
    {
        cityPlacementManager ??=
            FindFirstObjectByType<CityPlacementManager>(FindObjectsInactive.Include);
        cityAnchorManager ??=
            FindFirstObjectByType<CityAnchorManager>(FindObjectsInactive.Include);
        citySettingsController ??=
            FindFirstObjectByType<MainContentController>(FindObjectsInactive.Include);
    }

    private void Start()
    {
        SyncTerrainTextureStateFromRenderer();
        SetOSMVisible(useOsmTerrainTexture);
    }

    public void ToggleWaterPlane(GameObject waterPlane1)
    {
        if(waterPlane1 == null)
        {
            Debug.Log("No water plane assigned");
            return;
        }

        waterPlane1.SetActive(!waterPlane1.activeSelf);
    }

    public void ToggleOSMMode()
    {
        SyncTerrainTextureStateFromRenderer();
        SetOSMVisible(!useOsmTerrainTexture);
    }

    public void ShowOSM()
    {
        SetOSMVisible(true);
    }

    public void HideOSM()
    {
        SetOSMVisible(false);
    }

    public void SetOSMVisible(bool visible)
    {
        useOsmTerrainTexture = visible;
        citySettingsController ??=
            FindFirstObjectByType<MainContentController>(FindObjectsInactive.Include);

        if (citySettingsController != null)
        {
            citySettingsController.SetTerrainTexture(visible);
        }
        else if (cityRenderers != null && osmMat != null && bingMat != null)
        {
            Material selectedMaterial = visible ? osmMat : bingMat;
            for (int i = 0; i < cityRenderers.Length; i++)
            {
                Renderer renderer = cityRenderers[i];
                if (renderer != null)
                    renderer.sharedMaterial = selectedMaterial;
            }
        }

        osmButtonDisplay?.SetState(visible);
        if (Application.isPlaying && osmButtonDisplay != null)
        {
            if (buttonStateSync != null)
                StopCoroutine(buttonStateSync);
            buttonStateSync = StartCoroutine(SyncOsmButtonVisualAfterClick(visible));
        }
    }

    private void SyncTerrainTextureStateFromRenderer()
    {
        if (cityRenderers == null || osmMat == null)
            return;

        for (int i = 0; i < cityRenderers.Length; i++)
        {
            Renderer renderer = cityRenderers[i];
            if (renderer == null)
                continue;

            useOsmTerrainTexture = renderer.sharedMaterial == osmMat;
            return;
        }
    }

    private IEnumerator SyncOsmButtonVisualAfterClick(bool visible)
    {
        // Some legacy button prefabs also invoke ToggleBackground after this
        // handler. Reapply the authoritative map state once that click finishes.
        yield return null;
        osmButtonDisplay?.SetState(visible);
        buttonStateSync = null;
    }

    public void ToggleBounds()
    {
        if (cityPlacementManager == null && cityAnchorManager == null)
        {
            cityPlacementManager =
                FindFirstObjectByType<CityPlacementManager>(FindObjectsInactive.Include);
            cityAnchorManager =
                FindFirstObjectByType<CityAnchorManager>(FindObjectsInactive.Include);
        }

        if (cityPlacementManager != null || cityAnchorManager != null)
        {
            Debug.Log("EventManager: bounds-based transform editing is disabled in the one-time placement flow.");
            return;
        }

        if (cityModel == null)
        {
            Debug.Log("City model is missing!");
            return;
        }

        var bounds = cityModel.GetComponent<BoundsControl>();
        var boxCollider = cityModel.GetComponent<BoxCollider>();
        if (bounds == null || boxCollider == null)
        {
            Debug.Log("Bounds or Box is missing!");
            return;
        }

        bounds.Active = !bounds.Active;
        boxCollider.enabled = !boxCollider.enabled;
    }

    public void ResetScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    } 

    public void ToggleNodes()
    {
        if (nodesParent == null)
        {
            Debug.Log("Nodes parent is missing!");
            return;
        }

        // var sphereVisuals = new List<GameObject>();

        foreach(GraphNode node in nodesParent.GetComponentsInChildren<GraphNode>(true))
        {
            Transform sphereTf = node.transform.Find("Sphere");
            GameObject sphereGO = sphereTf.gameObject;

            sphereGO.SetActive(!sphereGO.activeSelf);
        }
    }

    public void ShowNodes()
    {
        SetNodesVisible(true);
    }

    public void HideNodes()
    {
        SetNodesVisible(false);
    }

    public void SetNodesVisible(bool visible)
    {
        if (nodesParent == null)
        {
            Debug.Log("Nodes parent is missing!");
            return;
        }

        foreach (GraphNode node in nodesParent.GetComponentsInChildren<GraphNode>(true))
        {
            if (node == null) continue;

            Transform sphereTf = node.transform.Find("Sphere");
            if (sphereTf == null)
            {
                Debug.LogWarning($"Node '{node.name}' does not have a child named Sphere.");
                continue;
            }

            sphereTf.gameObject.SetActive(visible);
        }
    }
}
