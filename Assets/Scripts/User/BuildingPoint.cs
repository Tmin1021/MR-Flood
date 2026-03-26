using Microsoft.MixedReality.Toolkit.Input;
using UnityEngine;

public class BuildingPoint : MonoBehaviour, IMixedRealityFocusHandler, IMixedRealityPointerHandler
{
    public PointSelectManager manager;

    [Header("Anchor")]
    [SerializeField] private Transform interactionAnchor;

    [Header("Hover Highlight")]
    public Color hoverEmission = Color.yellow * 2f;

    [Header("Flood Highlight")]
    public Color floodedEmission = Color.red * 2f;

    [Header("Flood")]
    public Transform floodTransform;

    private Renderer[] rends;
    private Color[] originalEmissionColors;
    private float waterLevel;

    public Transform AnchorTransform => interactionAnchor != null ? interactionAnchor : transform;

    void Awake()
    {
        CacheRenderers();
    }

    void Update()
    {
        if(floodTransform) waterLevel = floodTransform.position.y;
    }

    public void Configure(PointSelectManager newManager, Transform anchor = null)
    {
        manager = newManager;

        if (anchor != null)
            interactionAnchor = anchor;

        CacheRenderers();
    }

    public Vector3 GetAnchorWorldPosition()
    {
        return AnchorTransform.position;
    }

    public float GetAnchorWorldY()
    {
        return AnchorTransform.position.y;
    }
    public float GetBaseWorldY()
    {
        if (rends == null || rends.Length == 0)
            return GetAnchorWorldY();

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            b.Encapsulate(rends[i].bounds);

        return b.min.y;
    }

    public bool IsFlooded()
    {
        return waterLevel >= GetBaseWorldY();
    }

    public Vector3 GetTopWorldPosition()
    {
        if (rends == null || rends.Length == 0) return GetAnchorWorldPosition();

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
                if(!isFlooded())
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", hoverEmission);
                }
                else // the building flooded
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", floodedEmission);
                }
            }
            else
            {
                mat.SetColor("_EmissionColor", originalEmissionColors[i]);
            }
        }
    }

    public void OnPointerClicked(MixedRealityPointerEventData eventData)
    {
        if (!(eventData.Pointer is PokePointer))
        {
            manager?.SelectPoint(this);
        }
    }

    public void OnPointerDown(MixedRealityPointerEventData eventData)
    {
        if (eventData.Pointer is PokePointer)
        {
            manager?.SelectPoint(this);
            eventData.Use();
        }
    }

    public void OnPointerUp(MixedRealityPointerEventData eventData) { }
    public void OnPointerDragged(MixedRealityPointerEventData eventData) { }

    private void CacheRenderers()
    {
        rends = GetComponentsInChildren<Renderer>(true);
        originalEmissionColors = new Color[rends.Length];

        for (int i = 0; i < rends.Length; i++)
        {
            Material mat = rends[i].material;
            if (mat.HasProperty("_EmissionColor"))
                originalEmissionColors[i] = mat.GetColor("_EmissionColor");
        }
    }
}
