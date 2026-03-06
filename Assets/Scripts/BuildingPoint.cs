using System.Collections;
using System.Collections.Generic;
using Microsoft.MixedReality.Toolkit.Input;
using UnityEngine;

public class BuildingPoint : MonoBehaviour, IMixedRealityFocusHandler, IMixedRealityPointerHandler
{
    public PointSelectManager manager;
    public bool select = false;
    public GraphNode anchor;
    Renderer rend;


    // Start is called before the first frame update
    void Awake()
    {
        rend = GetComponentInChildren<Renderer>();
    }

    public Vector3 GetTopWorldPosition()
    {
        if (rend == null) return transform.position;
        var b = rend.bounds;
        return new Vector3(b.center.x, b.max.y, b.center.z);
    }

    public void OnFocusEnter(FocusEventData eventData)
    {
        manager.ShowTag(this);
        Debug.Log("Focus ENTER on " + gameObject.name);
    }

    public void OnFocusExit(FocusEventData eventData)
    {
        manager.HideTag(this);
        Debug.Log("Focus EXIT on " + gameObject.name);
    }

    public void OnPointerClicked(MixedRealityPointerEventData eventData)
    {
        if (manager != null) manager.SelectPoint(this);
    }

    public void OnPointerDown(MixedRealityPointerEventData eventData) { }
    public void OnPointerUp(MixedRealityPointerEventData eventData) { }
    public void OnPointerDragged(MixedRealityPointerEventData eventData) { }
}
