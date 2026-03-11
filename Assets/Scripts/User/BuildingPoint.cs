using Microsoft.MixedReality.Toolkit.Input;
using UnityEngine;

public class BuildingPoint : MonoBehaviour, IMixedRealityFocusHandler, IMixedRealityPointerHandler
{
    public PointSelectManager manager;
    // public GraphNode anchor;

    [Header("Hover Highlight")]
    public Color hoverEmission = Color.yellow * 2f;

    private Renderer[] rends;
    private Color[] originalEmissionColors;

    void Awake()
    {
        rends = GetComponentsInChildren<Renderer>(true);
        originalEmissionColors = new Color[rends.Length];

        for (int i = 0; i < rends.Length; i++)
        {
            Material mat = rends[i].material;
            if (mat.HasProperty("_EmissionColor"))
            {
                originalEmissionColors[i] = mat.GetColor("_EmissionColor");
            }
        }
    }

    public Vector3 GetTopWorldPosition()
    {
        if (rends == null || rends.Length == 0) return transform.position;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);

        return new Vector3(b.center.x, b.max.y, b.center.z);
    }

    public void OnFocusEnter(FocusEventData eventData)
    {
        SetHoverHighlight(true);
        manager?.ShowTag(this);
    }

    public void OnFocusExit(FocusEventData eventData)
    {
        SetHoverHighlight(false);
        manager?.HideTag(this);
    }

    private void SetHoverHighlight(bool on)
    {
        if (rends == null) return;

        for (int i = 0; i < rends.Length; i++)
        {
            Material mat = rends[i].material;
            if (!mat.HasProperty("_EmissionColor")) continue;

            if (on)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", hoverEmission);
            }
            else
            {
                mat.SetColor("_EmissionColor", originalEmissionColors[i]);
            }
        }
    }

    public void OnPointerClicked(MixedRealityPointerEventData eventData)
    {
        manager?.SelectPoint(this);
    }

    public void OnPointerDown(MixedRealityPointerEventData eventData) { }
    public void OnPointerUp(MixedRealityPointerEventData eventData) { }
    public void OnPointerDragged(MixedRealityPointerEventData eventData) { }
}