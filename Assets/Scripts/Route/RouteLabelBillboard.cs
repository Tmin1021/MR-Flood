using UnityEngine;

public class RouteLabelBillboard : MonoBehaviour
{
    public Camera targetCamera;

    private void LateUpdate()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;

        Vector3 toLabel = transform.position - cam.transform.position;
        if (toLabel.sqrMagnitude < 0.000001f) return;

        transform.rotation = Quaternion.LookRotation(toLabel.normalized, Vector3.up);
    }
}