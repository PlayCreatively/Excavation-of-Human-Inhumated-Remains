using UnityEngine;

public class Rotator : MonoBehaviour
{
    public float rotationSpeed = 10f;

    void Update()
    {
        transform.eulerAngles += rotationSpeed * Time.deltaTime * Vector3.up;
    }
}
