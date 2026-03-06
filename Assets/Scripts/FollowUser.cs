using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FollowUser : MonoBehaviour
{
    public GameObject user;
    public Vector3 offset;
    private Transform userTransform;
    // Start is called before the first frame update
    void Start()
    {
        userTransform = user.transform;
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 tmp = userTransform.position;

        tmp += offset;

        gameObject.transform.position = tmp;
    }
}
