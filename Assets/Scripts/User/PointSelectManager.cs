using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class PointSelectManager : MonoBehaviour
{
    public LineRenderer path;
    public GameObject housesParent;
    public GameObject hoverTag;
    public GameObject confirmButton;
    public GameObject resetButton;
    public Text hoverText;
    public float tagOffset = 0.05f;
    public SimpleGraphManager graph;
    public MRNotification notifier;

    [Header("Arrow")]
    public GameObject arrow1;   // points to A
    public GameObject arrow2;   // points to B
    private float arrowOffset = 0.02f;

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

        if (hoverTag) hoverTag.SetActive(false);
        if (arrow1) arrow1.SetActive(false);
        if (arrow2) arrow2.SetActive(false);

        if (housesParent == null) return;

        foreach (Transform t in housesParent.GetComponentsInChildren<Transform>(true))
        {
            if (t == housesParent.transform) continue;

            if (t.GetComponentInChildren<Renderer>() == null) continue;

            var bp = t.GetComponent<BuildingPoint>();
            if (bp == null) bp = t.gameObject.AddComponent<BuildingPoint>();
            bp.manager = this;

            if (t.GetComponent<Collider>() == null)
            {
                t.gameObject.AddComponent<BoxCollider>();
            }
        }
    }

    void Update()
    {
        if (!ifClick) return;
        if (A == null || B == null || path == null) return;
    }

    void LateUpdate()
    {
        if (hoverTag && hoverTag.activeSelf && Camera.main)
        {
            hoverTag.transform.rotation =
                Quaternion.LookRotation(Camera.main.transform.position - hoverTag.transform.position);
        }

        // keep arrows following building if needed
        UpdateSelectedArrows();
    }

    public void ShowTag(BuildingPoint b)
    {
        hoverOn = b;
        if (hoverOn == null) return;

        if (hoverTag)
        {
            hoverTag.SetActive(true);
            hoverTag.transform.position = b.GetTopWorldPosition() + Vector3.up * tagOffset;
        }

        if (hoverText)
        {
            if (b == A) hoverText.text = "Start selected";
            else if (b == B) hoverText.text = "Destination selected";
            else hoverText.text = "Tap to select";
        }
    }

    public void HideTag(BuildingPoint b)
    {
        if (hoverOn != b) return;

        if (hoverTag) hoverTag.SetActive(false);
        hoverOn = null;
    }

    public void SelectPoint(BuildingPoint b)
    {
        if (A == null)
        {
            A = b;
        }
        else if (B == null && A != b)
        {
            B = b;

            confirmButton.transform.position = b.GetTopWorldPosition() + Vector3.up * tagOffset * 2f;
            confirmButton.SetActive(true);

            resetButton.transform.position = b.GetTopWorldPosition() + Vector3.up * tagOffset * 3f;
            resetButton.SetActive(true);
        }
        else
        {
            A = b;
            B = null;

            if (path) path.positionCount = 0;
            confirmButton.SetActive(false);
            resetButton.SetActive(false);
        }

        UpdateSelectedArrows();
    }

    private void UpdateSelectedArrows()
    {
        if (arrow1 != null)
        {
            if (A != null)
            {
                arrow1.SetActive(true);
                arrow1.transform.position = A.GetTopWorldPosition() + Vector3.up * arrowOffset;
            }
            else
            {
                arrow1.SetActive(false);
            }
        }

        if (arrow2 != null)
        {
            if (B != null)
            {
                arrow2.SetActive(true);
                arrow2.transform.position = B.GetTopWorldPosition() + Vector3.up * arrowOffset;
            }
            else
            {
                arrow2.SetActive(false);
            }
        }
    }

    public void ConfirmPath()
    {
        if (A == null || B == null || path == null || graph == null) return;

        if (graph.IsBuildingFlooded(A)) { notifier?.Show("Start building is flooded / unavailable."); return; }
        if (graph.IsBuildingFlooded(B)) { notifier?.Show("Destination building is flooded / unavailable."); return; }

        var startAtt = graph.CreateAttachmentNode(A.transform.position, name: "StartAttach");
        var goalAtt = graph.CreateAttachmentNode(B.transform.position, name: "GoalAttach");

        var startNode = startAtt?.node;
        var goalNode = goalAtt?.node;

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

        var points = new List<Vector3>(nodePath.Count + 2);
        points.Add(A.GetTopWorldPosition());
        foreach (var n in nodePath) points.Add(n.Position);
        points.Add(B.GetTopWorldPosition());

        startAtt.Cleanup();
        goalAtt.Cleanup();

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

        if (arrow1) arrow1.SetActive(false);
        if (arrow2) arrow2.SetActive(false);

        ifClick = false;
        confirmButton.SetActive(false);
        resetButton.SetActive(false);
    }
}