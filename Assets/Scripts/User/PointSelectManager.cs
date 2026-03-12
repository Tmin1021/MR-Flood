using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PointSelectManager : MonoBehaviour
{
    [Header("City Structure")]
    public LineRenderer path;
    public GameObject housesParent;
    public Transform cityRoot;

    [Header("Buttons")]
    public GameObject confirmButton;
    public GameObject resetButton;

    [Header("Hover Tag")]
    public GameObject hoverTag;
    public Text hoverText;
    public float tagOffset = 0.05f;

    [Header("Arrow")]
    public GameObject arrow1;   // points to A
    public GameObject arrow2;   // points to B
    public float arrowOffset = 0.02f;

    [Header("References")]
    public SimpleGraphManager graph;
    public MRNotification notifier;

    private BuildingPoint A;
    private BuildingPoint B;
    private BuildingPoint hoverOn;

    private float initScale = 1f;
    private float dynamicScale = 1f;

    private bool hasPath = false;

    private List<GraphNode> currentPathNodes = new List<GraphNode>();
    private Vector3 startSnapLocal;
    private Vector3 goalSnapLocal;
    private bool hasStoredSnaps = false;

    void Start()
    {
        if (cityRoot != null && !Mathf.Approximately(cityRoot.lossyScale.x, 0f))
            initScale = cityRoot.lossyScale.x;
        else
            initScale = 1f;

        dynamicScale = 1f;

        if (path != null)
        {
            path.useWorldSpace = true;
            path.positionCount = 0;
        }

        if (hoverTag) hoverTag.SetActive(false);

        if (confirmButton) confirmButton.SetActive(false);
        if (resetButton) resetButton.SetActive(false);
        
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
                t.gameObject.AddComponent<BoxCollider>();
        }
    }

    void Update()
    {
        if (cityRoot != null && !Mathf.Approximately(initScale, 0f))
            dynamicScale = cityRoot.lossyScale.x / initScale;
        else
            dynamicScale = 1f;
    }

    void LateUpdate()
    {
        UpdateHoverTag();
        UpdateButtons();
        UpdateSelectedArrows();

        if (hasPath)
            RedrawCurrentPath();
    }

    private void UpdateHoverTag()
    {
        if (hoverTag == null) return;

        if (hoverTag.activeSelf && hoverOn != null)
        {
            hoverTag.transform.position =
                hoverOn.GetTopWorldPosition() + Vector3.up * tagOffset * dynamicScale;

            if (Camera.main != null)
            {
                hoverTag.transform.rotation = Quaternion.LookRotation(
                    Camera.main.transform.position - hoverTag.transform.position
                );
            }
        }
    }

    private void UpdateButtons()
    {
        if (B == null) return;

        if (confirmButton != null && confirmButton.activeSelf)
        {
            confirmButton.transform.position =
                B.GetTopWorldPosition() + Vector3.up * tagOffset * 2f * dynamicScale;
        }

        if (resetButton != null && resetButton.activeSelf)
        {
            resetButton.transform.position =
                B.GetTopWorldPosition() + Vector3.up * tagOffset * 3f * dynamicScale;
        }
    }

    public void ShowTag(BuildingPoint b)
    {
        hoverOn = b;
        if (hoverOn == null) return;

        if (hoverTag != null)
        {
            hoverTag.SetActive(true);
            hoverTag.transform.position =
                b.GetTopWorldPosition() + Vector3.up * tagOffset * dynamicScale;
        }

        if (hoverText != null)
        {
            if (b == A) hoverText.text = "Start selected";
            else if (b == B) hoverText.text = "Destination selected";
            else hoverText.text = "Tap to select";
        }
    }

    public void HideTag(BuildingPoint b)
    {
        if (hoverOn != b) return;

        if (hoverTag != null) hoverTag.SetActive(false);
        hoverOn = null;
    }

    public void SelectPoint(BuildingPoint b)
    {
        if (b == null) return;

        // selecting again after a completed path starts a new selection
        if (hasPath)
        {
            ClearCurrentPathOnly();
        }

        if (A == null)
        {
            A = b;
        }
        else if (B == null && A != b)
        {
            B = b;

            if (confirmButton != null)
            {
                confirmButton.transform.position =
                    b.GetTopWorldPosition() + Vector3.up * tagOffset * 2f * dynamicScale;
                confirmButton.SetActive(true);
            }

            if (resetButton != null)
            {
                resetButton.transform.position =
                    b.GetTopWorldPosition() + Vector3.up * tagOffset * 3f * dynamicScale;
                resetButton.SetActive(true);
            }
        }
        else
        {
            // restart selection with new A
            A = b;
            B = null;

            ClearCurrentPathOnly();

            if (confirmButton != null) confirmButton.SetActive(false);
            if (resetButton != null) resetButton.SetActive(false);
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
                arrow1.transform.position =
                    A.GetTopWorldPosition() + Vector3.up * arrowOffset * dynamicScale;
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
                arrow2.transform.position =
                    B.GetTopWorldPosition() + Vector3.up * arrowOffset * dynamicScale;
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

        if (graph.IsBuildingFlooded(A))
        {
            notifier?.Show("Start building is flooded / unavailable.");
            return;
        }

        if (graph.IsBuildingFlooded(B))
        {
            notifier?.Show("Destination building is flooded / unavailable.");
            return;
        }

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
            ClearCurrentPathOnly();
            startAtt.Cleanup();
            goalAtt.Cleanup();
            return;
        }

        currentPathNodes.Clear();

        foreach (var n in nodePath)
        {
            if (n == null) continue;
            if (n == startNode || n == goalNode) continue; // temp nodes, do not store
            currentPathNodes.Add(n);
        }

        if (cityRoot != null)
        {
            startSnapLocal = cityRoot.InverseTransformPoint(startNode.Position);
            goalSnapLocal = cityRoot.InverseTransformPoint(goalNode.Position);
        }
        else
        {
            startSnapLocal = startNode.Position;
            goalSnapLocal = goalNode.Position;
        }

        hasStoredSnaps = true;

        startAtt.Cleanup();
        goalAtt.Cleanup();

        hasPath = true;
        RedrawCurrentPath();
    }

    private void RedrawCurrentPath()
    {
        if (!hasPath || !hasStoredSnaps || A == null || B == null || path == null) return;

        var points = new List<Vector3>();

        points.Add(A.GetTopWorldPosition());

        Vector3 startSnapWorld = cityRoot != null
            ? cityRoot.TransformPoint(startSnapLocal)
            : startSnapLocal;

        Vector3 goalSnapWorld = cityRoot != null
            ? cityRoot.TransformPoint(goalSnapLocal)
            : goalSnapLocal;

        AddPointIfFarEnough(points, startSnapWorld);

        foreach (var n in currentPathNodes)
        {
            if (n == null) continue;
            AddPointIfFarEnough(points, n.Position);
        }

        AddPointIfFarEnough(points, goalSnapWorld);
        AddPointIfFarEnough(points, B.GetTopWorldPosition());

        path.useWorldSpace = true;
        path.positionCount = points.Count;

        for (int i = 0; i < points.Count; i++)
            path.SetPosition(i, points[i]);
    }

    private void AddPointIfFarEnough(List<Vector3> points, Vector3 p, float minDist = 0.0001f)
    {
        if (points.Count == 0)
        {
            points.Add(p);
            return;
        }

        if (Vector3.Distance(points[points.Count - 1], p) > minDist)
            points.Add(p);
    }

    private void ClearCurrentPathOnly()
    {
        hasPath = false;
        hasStoredSnaps = false;
        currentPathNodes.Clear();

        if (path != null)
            path.positionCount = 0;
    }

    public void ResetSelection()
    {
        A = null;
        B = null;
        hoverOn = null;

        ClearCurrentPathOnly();

        if (hoverTag != null) hoverTag.SetActive(false);

        if (arrow1 != null) arrow1.SetActive(false);
        if (arrow2 != null) arrow2.SetActive(false);

        if (confirmButton != null) confirmButton.SetActive(false);
        if (resetButton != null) resetButton.SetActive(false);
    }
}