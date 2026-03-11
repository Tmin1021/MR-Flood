using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class PointSelectManager : MonoBehaviour
{
    // Start is called before the first frame update
    public LineRenderer path;
    public GameObject housesParent;
    public GameObject hoverTag; // need to show on the hover building
    public GameObject confirmButton;
    public GameObject resetButton;
    public Text hoverText;
    public float tagOffset = 0.05f; // tag above how much with building ?
    public SimpleGraphManager graph;
    public MRNotification notifier;

    private BuildingPoint A;
    private BuildingPoint B;
    private BuildingPoint hoverOn;
    private bool ifClick = false;
    void Start()
    {
        if (path)
        {
            path.useWorldSpace = true;
            path.positionCount = 0;
        }
        if (hoverTag) hoverTag.SetActive(false); // by default when starting.

        if(housesParent == null) return;
        foreach (Transform t in housesParent.GetComponentsInChildren<Transform>(true))
        {
            if (t == housesParent.transform) continue;

            // only add if it looks like a building object (has renderer)
            if (t.GetComponentInChildren<Renderer>() == null) continue;

            var bp = t.GetComponent<BuildingPoint>();
            if (bp == null) bp = t.gameObject.AddComponent<BuildingPoint>();
            bp.manager = this;

            // IMPORTANT: must have a collider to be hit
            if (t.GetComponent<Collider>() == null)
            {
                // simple collider (fast)
                t.gameObject.AddComponent<BoxCollider>();
            }
        }

    }

    void Update()
    {
        if (!ifClick) return;
        if (A == null || B == null || path == null) return;
    }

    // Update is called once per frame
    void LateUpdate()
    {
        if (hoverTag && hoverTag.activeSelf && Camera.main)
        {
            hoverTag.transform.rotation = Quaternion.LookRotation(Camera.main.transform.position - hoverTag.transform.position);
        }
    }

    public void ShowTag(BuildingPoint b)
    {
        hoverOn = b;
        if(hoverOn == null) return;

        hoverTag.SetActive(true);
        hoverTag.transform.position = b.transform.position + Vector3.up * tagOffset; // get the building position

        if (hoverText) hoverText.text = (b == A || b == B) ? "Selected" : "Tap to select";
    }

    public void HideTag(BuildingPoint b)
    {
        if(hoverOn != b) return;
        if(hoverOn != A && hoverOn != B && hoverTag) hoverTag.SetActive(false);

        hoverOn = null;
    }
    
    public void SelectPoint(BuildingPoint b)
    {
        if(A == null) 
        {
            A = b;
        }
        else if (B == null && A != b)
        {
            B = b;
            
            confirmButton.transform.position = b.transform.position + Vector3.up * tagOffset * 2;
            confirmButton.SetActive(true);

            resetButton.transform.position = b.transform.position + Vector3.up * tagOffset * 3;
            resetButton.SetActive(true);
        }
        else
        {
            A = b;
            B = null;
            if(path) path.positionCount = 0;
            confirmButton.SetActive(false);
            resetButton.SetActive(false);
        }
    }

    public void ConfirmPath()
    {
        if (A == null || B == null || path == null || graph == null) return;

        if(notifier == null) {Debug.Log("?");}
        else Debug.Log("@");

        if (graph.IsBuildingFlooded(A)) { notifier?.Show("Start building is flooded / unavailable."); return; }
        if (graph.IsBuildingFlooded(B)) { notifier?.Show("Destination building is flooded / unavailable."); return; }

        var startAtt = graph.CreateAttachmentNode(A.transform.position, name: "StartAttach");
        var goalAtt  = graph.CreateAttachmentNode(B.transform.position, name: "GoalAttach");

        var startNode = startAtt?.node;
        var goalNode  = goalAtt?.node;

        if (startNode == null || goalNode == null)
        {
            notifier?.Show("Could not attach building to road network.");
            startAtt?.Cleanup();
            goalAtt?.Cleanup();
            return;
        }

        if (startNode.blocked)
        {
            notifier?.Show("Nearest start attachment is flooded.");
            startAtt.Cleanup();
            goalAtt.Cleanup();
            return;
        }
        if (goalNode.blocked)
        {
            notifier?.Show("Nearest destination attachment is flooded.");
            startAtt.Cleanup();
            goalAtt.Cleanup();
            return;
        }

        var nodePath = AStarPathfinder.FindPath(startNode, goalNode);

        if (nodePath == null || nodePath.Count == 0)
        {
            notifier?.Show("No safe route available at current water level.");
            path.positionCount = 0;
            startAtt.Cleanup();
            goalAtt.Cleanup();
            return;
        }

        // Copy positions FIRST (because we will destroy temp nodes next)
        var points = new List<Vector3>(nodePath.Count + 2);
        points.Add(A.GetTopWorldPosition());
        foreach (var n in nodePath) points.Add(n.Position);
        points.Add(B.GetTopWorldPosition());

        // Now safe to cleanup temp attachments
        startAtt.Cleanup();
        goalAtt.Cleanup();

        // Draw
        path.useWorldSpace = true;
        path.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++)
            path.SetPosition(i, points[i]);

        ifClick = true;
    }

    public void ResetSelection()
    {
        A = null;
        B = null;
        if (hoverTag) hoverTag.SetActive(false);
        if (path) path.positionCount = 0;

        ifClick = false;
        confirmButton.SetActive(false);
        resetButton.SetActive(false);
    }
}
