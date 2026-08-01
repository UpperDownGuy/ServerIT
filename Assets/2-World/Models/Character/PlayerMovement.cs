using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 15f;
    public float sprintMultiplier = 1.75f;

    [Header("Jumping")]
    public float jumpVelocity = 22f;
    [Min(1f)] public float airGravityMultiplier = 5f;
    [Min(0f)] public float coyoteTime = 0.12f;
    [Min(0f)] public float jumpBufferTime = 0.12f;

    private Rigidbody playerRigidbody;
    private Vector2 movementInput;
    private bool isSprinting;
    private float desiredYaw;
    private float lastGroundedTime = float.NegativeInfinity;
    private float jumpPressedTime = float.NegativeInfinity;
    private float ignoreGroundUntil;

    private void Awake()
    {
        playerRigidbody = GetComponent<Rigidbody>();
        CapsuleCollider playerCollider = GetComponent<CapsuleCollider>();

        // The player collider is a solid, rotation-invariant wall boundary.
        playerCollider.enabled = true;
        playerCollider.isTrigger = false;
        playerCollider.providesContacts = true;

        playerRigidbody.detectCollisions = true;
        playerRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        playerRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        playerRigidbody.constraints |=
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
        desiredYaw = playerRigidbody.rotation.eulerAngles.y;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            movementInput = Vector2.zero;
            isSprinting = false;
            return;
        }

        float horizontal =
            (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) -
            (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);

        float vertical =
            (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) -
            (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);

        movementInput = Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
        isSprinting = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            jumpPressedTime = Time.time;
        }
    }

    private void FixedUpdate()
    {
        // Build a fresh unit quaternion from the yaw instead of repeatedly
        // multiplying quaternions, which can accumulate enough drift for
        // Rigidbody.MoveRotation to reject the result.
        Quaternion desiredRotation = Quaternion.AngleAxis(desiredYaw, Vector3.up);
        Vector3 forward = desiredRotation * Vector3.forward;
        Vector3 right = desiredRotation * Vector3.right;

        Vector3 movement = Vector3.ClampMagnitude(
            forward * movementInput.y + right * movementInput.x,
            1f);

        float currentSpeed = speed * (isSprinting ? sprintMultiplier : 1f);
        Vector3 velocity = playerRigidbody.linearVelocity;
        velocity.x = movement.x * currentSpeed;
        velocity.z = movement.z * currentSpeed;

        bool hasBufferedJump = Time.time - jumpPressedTime <= jumpBufferTime;
        bool canJump = Time.time - lastGroundedTime <= coyoteTime;
        if (hasBufferedJump && canJump)
        {
            // Fixed launch velocity plus heavier gravity creates a quick,
            // predictable Roblox-style arcade jump.
            velocity.y = jumpVelocity;

            jumpPressedTime = float.NegativeInfinity;
            lastGroundedTime = float.NegativeInfinity;
            ignoreGroundUntil = Time.time + 0.1f;
        }
        else if (!canJump)
        {
            velocity += Physics.gravity *
                (airGravityMultiplier - 1f) *
                Time.fixedDeltaTime;
        }

        playerRigidbody.linearVelocity = velocity;
        playerRigidbody.MoveRotation(desiredRotation);
    }

    public void AddLookYaw(float yawDegrees)
    {
        if (float.IsNaN(yawDegrees) || float.IsInfinity(yawDegrees))
        {
            return;
        }

        desiredYaw = Mathf.Repeat(desiredYaw + yawDegrees, 360f);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (Time.time < ignoreGroundUntil)
        {
            return;
        }

        foreach (ContactPoint contact in collision.contacts)
        {
            if (Vector3.Dot(contact.normal, Vector3.up) > 0.55f)
            {
                lastGroundedTime = Time.time;
                return;
            }
        }
    }
}
