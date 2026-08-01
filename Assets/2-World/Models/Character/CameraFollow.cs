using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Mouse Look")]
    [Min(0f)] public float mouseSensitivity = 0.15f;
    [Range(-89f, 89f)] public float minimumPitch = -20f;
    [Range(-89f, 89f)] public float maximumPitch = 80f;

    private PlayerMovement playerMovement;
    private Quaternion startingLocalRotation;
    private float cameraPitch;
    private bool wantsCursorLocked;

    private void Awake()
    {
        if (target == null)
        {
            PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
            if (player != null)
            {
                target = player.transform;
            }
        }

        if (target != null)
        {
            // Imported character models contain animated child transforms. Always
            // follow and rotate the stable Rigidbody root instead of one of them.
            playerMovement = target.GetComponentInParent<PlayerMovement>();
            if (playerMovement != null)
            {
                target = playerMovement.transform;
            }
        }

        if (target == null)
        {
            enabled = false;
            Debug.LogWarning("CameraFollow needs a target.", this);
            return;
        }

        // Parenting preserves the exact camera placement authored in the scene
        // and gives the camera the Rigidbody's interpolated motion for free.
        if (!transform.IsChildOf(target))
        {
            transform.SetParent(target, true);
        }

        startingLocalRotation = transform.localRotation;
        SetCursorLocked(true);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
            return;
        }

        if (!wantsCursorLocked)
        {
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                SetCursorLocked(true);
            }

            return;
        }

        if (mouse == null)
        {
            return;
        }

        Vector2 lookDelta = mouse.delta.ReadValue() * mouseSensitivity;
        if (float.IsNaN(lookDelta.x) || float.IsInfinity(lookDelta.x) ||
            float.IsNaN(lookDelta.y) || float.IsInfinity(lookDelta.y))
        {
            return;
        }

        if (playerMovement != null)
        {
            playerMovement.AddLookYaw(lookDelta.x);
        }

        cameraPitch = Mathf.Clamp(
            cameraPitch - lookDelta.y,
            minimumPitch,
            maximumPitch);

        transform.localRotation =
            startingLocalRotation *
            Quaternion.AngleAxis(cameraPitch, Vector3.right);
    }

    private void OnDisable()
    {
        SetCursorLocked(false);
    }

    private void SetCursorLocked(bool isLocked)
    {
        wantsCursorLocked = isLocked;
        Cursor.lockState = isLocked
            ? CursorLockMode.Locked
            : CursorLockMode.None;
        Cursor.visible = !isLocked;
    }
}
