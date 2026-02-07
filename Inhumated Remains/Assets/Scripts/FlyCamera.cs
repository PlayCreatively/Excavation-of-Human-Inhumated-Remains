using UnityEngine;

[DefaultExecutionOrder(-100)]
public class FlyCamera : MonoBehaviour
{
	[Header("Movement Settings")]
	public float movementSpeed = 10.0f;
	public float fastMovementMul = 2.0f;
	public float freeLookSensitivity = 3.0f;

	[Header("Input Settings")]
	public KeyCode forwardKey = KeyCode.W;
	public KeyCode backwardKey = KeyCode.S;
	public KeyCode leftKey = KeyCode.A;
	public KeyCode rightKey = KeyCode.D;
	public KeyCode upKey = KeyCode.E;
	public KeyCode downKey = KeyCode.Q;
	public KeyCode boostKey = KeyCode.LeftShift;

	private bool looking = false;

	void Update()
	{
		var fastMode = Input.GetKey(boostKey);
		var currentSpeed = fastMode ? movementSpeed * fastMovementMul : movementSpeed;

		// Rotation
		if (Input.GetMouseButtonDown(0))
		{
			StartLooking();
		}
		else if (Input.GetKeyDown(KeyCode.Escape))
		{
			StopLooking();
		}

		if (looking)
		{
			float newRotationX = transform.localEulerAngles.y + Input.GetAxis("Mouse X") * freeLookSensitivity;
			float newRotationY = transform.localEulerAngles.x - Input.GetAxis("Mouse Y") * freeLookSensitivity;
			transform.localEulerAngles = new Vector3(newRotationY, newRotationX, 0f);
		}

		// Movement
		Vector3 moveDirection = Vector3.zero;

		if (Input.GetKey(forwardKey)) moveDirection += transform.forward;
		if (Input.GetKey(backwardKey)) moveDirection -= transform.forward;
		if (Input.GetKey(leftKey)) moveDirection -= transform.right;
		if (Input.GetKey(rightKey)) moveDirection += transform.right;
		if (Input.GetKey(upKey)) moveDirection += Vector3.up;
		if (Input.GetKey(downKey)) moveDirection -= Vector3.up;

		transform.position += moveDirection * currentSpeed * Time.deltaTime;

		// Mouse Scroll Movement
		float axis = Input.GetAxis("Mouse ScrollWheel");
		if (axis != 0)
		{
			movementSpeed += axis;
		}
	}

	void StartLooking()
	{
		looking = true;
		Cursor.visible = false;
		Cursor.lockState = CursorLockMode.Locked;
	}

	void StopLooking()
	{
		looking = false;
		Cursor.visible = true;
		Cursor.lockState = CursorLockMode.None;
	}
}
