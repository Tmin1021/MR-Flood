using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FollowUser : MonoBehaviour
{
    public GameObject user;
    // public Vector3 offset;

    [Header("Rotation")]
    public bool faceUser = true;
    public bool onlyY = true;
    private Transform userTransform;
    // Start is called before the first frame update
    void Start()
    {
        userTransform = user.transform;
    }

    // Update is called once per frame
    void Update()
    {
        if(userTransform == null) return;

        if(faceUser)
        {
            Vector3 dir = transform.position - userTransform.position;

            if(onlyY)
            {
                dir.y = 0;
            }
            if (dir.sqrMagnitude > 0.0001f) // check if dir is too small
            {
                transform.rotation = Quaternion.LookRotation(dir);
            }
        }
    }
}
