using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rotator : MonoBehaviour
{
    [SerializeField] private float speed;
    void Update()
    {
        this.transform.Rotate(Vector3.up,Time.deltaTime*speed,Space.World);
    }

    private void OnMouseDown()
    {
        FirestoreDemo.instance.messageChannel.Send("Cube clicked!");
    }
}
