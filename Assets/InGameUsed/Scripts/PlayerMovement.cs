using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    public Transform cameraTransform;   // drag your Main Camera here

    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 8f;
    public float jumpHeight = 1.2f;
    public float gravity = -20f;
    public float acceleration = 12f;    // higher = snappier

    [Header("Rotation")]
    public bool faceCameraDirection = true;  // true = Fortnite style
    public float turnSpeed = 12f;

    private CharacterController controller;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void Update()
    {
        if (GameData.GamePause) return;
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        // ---- Input (WASD) ----
        float h = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float v = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        Vector3 input = new Vector3(h, 0f, v).normalized;

        bool sprint = kb.leftShiftKey.isPressed;
        bool jumpPressed = kb.spaceKey.wasPressedThisFrame;

        // ---- Camera-relative direction ----
        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 moveDir = camForward * input.z + camRight * input.x;

        // ---- Horizontal movement ----
        float targetSpeed = sprint ? sprintSpeed : walkSpeed;
        Vector3 targetVelocity = moveDir * targetSpeed;
        horizontalVelocity = Vector3.Lerp(horizontalVelocity, targetVelocity, acceleration * Time.deltaTime);

        // ---- Gravity & jump ----
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        if (controller.isGrounded && jumpPressed)
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * Time.deltaTime;

        // ---- Apply ----
        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        // ---- Rotate player ----
        if (faceCameraDirection)
        {
            if (camForward.sqrMagnitude > 0.001f)
                RotateTowards(camForward);
        }
        else if (moveDir.sqrMagnitude > 0.001f)
        {
            RotateTowards(moveDir);
        }
    }

    private void RotateTowards(Vector3 direction)
    {
        Quaternion target = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
    }
}   