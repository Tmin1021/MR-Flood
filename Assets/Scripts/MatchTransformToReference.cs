using UnityEngine;

public class MatchTransformToReference : MonoBehaviour
{
    public Transform referenceObject;
    public Transform targetObject;
    public bool copyScale = true;
    public bool copyRotation = true;
    public bool copyPosition = true;

    [ContextMenu("Match To Reference")]
    public void MatchToReference()
    {
        if (referenceObject == null || targetObject == null) return;

        if (copyScale)
            targetObject.localScale = referenceObject.localScale;

        if (copyRotation)
            targetObject.rotation = referenceObject.rotation; // world rotation

        if (copyPosition)
            targetObject.position = referenceObject.position; // world position
    }
}