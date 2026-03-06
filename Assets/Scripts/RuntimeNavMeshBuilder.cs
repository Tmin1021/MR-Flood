using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public class RuntimeNavMeshBuilder : MonoBehaviour
{
    public NavMeshSurface surface;
    public float buildDelay = 5f;

    void Start()
    {
        Invoke(nameof(Build), buildDelay);
    }

    void Build()
    {
        surface.BuildNavMesh();
        Debug.Log("NavMesh built from spatial mesh");
    }
}
