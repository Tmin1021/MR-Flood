using UnityEngine;

public class MatchModelScale : MonoBehaviour
{
    public Transform referenceRoot; // the model with the correct size
    public Transform targetRoot;    // the model you want to resize

    [ContextMenu("Match Target To Reference")]
    public void MatchTargetToReference()
    {
        if (referenceRoot == null || targetRoot == null) return;

        Bounds refBounds = GetBounds(referenceRoot);
        Bounds targetBounds = GetBounds(targetRoot);

        float scaleX = refBounds.size.x / targetBounds.size.x;
        float scaleZ = refBounds.size.z / targetBounds.size.z;

        // Use uniform scale so the map keeps its proportions
        float uniformScale = (scaleX + scaleZ) * 0.5f;

        targetRoot.localScale *= uniformScale;

        Debug.Log($"Applied scale factor: {uniformScale}");
    }

    private Bounds GetBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds b = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        return b;
    }
}