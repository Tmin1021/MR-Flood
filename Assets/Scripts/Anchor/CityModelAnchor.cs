using UnityEngine;

public class CityModelAnchor : MonoBehaviour
{
    [Header("Objects")]
    public Transform cityRootToMove;          // Usually CityAnchorRoot
    public Transform virtualReferencePoint;   // A child point inside the virtual city

    [Header("Project Systems")]
    public CityBootstrapper cityBootstrapper;
    public SimpleGraphManager graphManager;
    public FloodManager floodManager;
    public PointSelectManager pointSelectManager;

    [Header("Enable After Anchor")]
    public MonoBehaviour[] enableAfterAnchor;

    [Header("Options")]
    public bool lockAfterFirstAnchor = true;
    public bool rebuildAfterAnchor = true;

    private bool anchored;
    private bool hasLoggedManualPlacementOverride;

    public void AnchorToTransform(Transform physicalAnchor)
    {
        if (physicalAnchor == null)
            return;

        AnchorToPose(physicalAnchor.position, physicalAnchor.rotation);
    }

    public void AnchorToPose(Vector3 physicalPosition, Quaternion physicalRotation)
    {
        if (CityAnchorManager.IsManualPlacementFlowActive)
        {
            LogManualPlacementOverrideOnce();
            return;
        }

        if (lockAfterFirstAnchor && anchored)
            return;

        if (cityRootToMove == null)
        {
            Debug.LogError("CityModelAnchor: cityRootToMove is not assigned.");
            return;
        }

        if (virtualReferencePoint == null)
        {
            cityRootToMove.SetPositionAndRotation(physicalPosition, physicalRotation);
        }
        else
        {
            Quaternion rotationDelta =
                physicalRotation * Quaternion.Inverse(virtualReferencePoint.rotation);

            cityRootToMove.rotation = rotationDelta * cityRootToMove.rotation;

            Vector3 positionDelta = physicalPosition - virtualReferencePoint.position;
            cityRootToMove.position += positionDelta;
        }

        anchored = true;

        if (rebuildAfterAnchor) RebuildProjectData();
    }

    public void RebuildProjectData()
    {
        if (CityAnchorManager.IsManualPlacementFlowActive)
        {
            LogManualPlacementOverrideOnce();
            return;
        }

        if (cityBootstrapper != null)
            cityBootstrapper.BuildCity();

        if (graphManager != null)
            graphManager.BuildGraphFromNeighbors();

        if (floodManager != null)
            floodManager.UpdateFloodState();

        if (pointSelectManager != null)
            pointSelectManager.RefreshAfterCityRebuild();

        if (enableAfterAnchor != null)
        {
            foreach (MonoBehaviour behaviour in enableAfterAnchor)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = true;
                }
            }
        }
    }

    public void UnlockForRecalibration()
    {
        anchored = false;
    }

    private void LogManualPlacementOverrideOnce()
    {
        if (hasLoggedManualPlacementOverride)
            return;

        hasLoggedManualPlacementOverride = true;
        Debug.Log("CityModelAnchor: ignored because manual plane placement flow is active.");
    }
}
