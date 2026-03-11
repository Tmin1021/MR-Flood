using System.Collections;
using System.Collections.Generic;
using Microsoft.MixedReality.Toolkit.UI.BoundsControl;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EventManager : MonoBehaviour
{
    //public GameObject waterPlane1;
    [Header("OSM Mode")]
    [SerializeField] private Renderer[] cityRenderers;  // assign parent or children renderers
    [SerializeField] private Material osmMat;
    [SerializeField] private Material bingMat;
    [SerializeField] private GameObject cityModel;

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
        if (cityRenderers == null || cityRenderers.Length == 0 || !osmMat || !bingMat) return;

        foreach (var r in cityRenderers)
        {
            if (!r) continue;
            var current = r.sharedMaterial;
            r.sharedMaterial = (current == osmMat) ? bingMat : osmMat;
        }
    }

    public void ToggleBounds()
    {
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
        SceneManager.LoadScene("Flood");
    } 
}
