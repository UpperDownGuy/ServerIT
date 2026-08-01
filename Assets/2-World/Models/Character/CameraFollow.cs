using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Head Sync")]
    [Tooltip("Optional. Automatically found by name or humanoid rig when empty.")]
    public Transform head;
    public Vector3 headPitchAxis = Vector3.right;

    [Header("Mouse Look")]
    [Min(0f)] public float mouseSensitivity = 0.15f;
    [Range(-89f, 89f)] public float minimumPitch = -20f;
    [Range(-89f, 89f)] public float maximumPitch = 80f;

    private PlayerMovement playerMovement;
    private Quaternion startingLocalRotation;
    private Quaternion startingHeadLocalRotation;
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

        if (head == null)
        {
            head = FindHead(target);
        }

        // Preserve the camera placement authored in the scene, but make it
        // relative to the head so body yaw and head pitch move the view as one.
        Transform cameraParent = head != null ? head : target;
        if (transform.parent != cameraParent)
        {
            transform.SetParent(cameraParent, true);
        }

        startingLocalRotation = transform.localRotation;
        if (head != null)
        {
            startingHeadLocalRotation = head.localRotation;
        }
        else
        {
            Debug.LogWarning(
                "CameraFollow could not find a head transform; pitch will only affect the camera.",
                this);
        }

        SetCursorLocked(true);
    }

    private static Transform FindHead(Transform characterRoot)
    {
        Animator animator = characterRoot.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman)
        {
            Transform humanoidHead = animator.GetBoneTransform(HumanBodyBones.Head);
            if (humanoidHead != null)
            {
                return humanoidHead;
            }
        }

        Transform[] descendants = characterRoot.GetComponentsInChildren<Transform>(true);
        foreach (Transform descendant in descendants)
        {
            string candidate = descendant.name.ToLowerInvariant();
            if (candidate == "head" ||
                candidate.EndsWith(":head") ||
                candidate.EndsWith(".head"))
            {
                return descendant;
            }
        }

        return null;
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
    }

    private void LateUpdate()
    {
        if (head != null)
        {
            Vector3 pitchAxis = headPitchAxis.sqrMagnitude > 0.0001f
                ? headPitchAxis.normalized
                : Vector3.right;

            head.localRotation =
                startingHeadLocalRotation *
                Quaternion.AngleAxis(cameraPitch, pitchAxis);

            // The camera inherits both body yaw and head pitch. Its authored
            // head-relative rotation remains unchanged.
            transform.localRotation = startingLocalRotation;
            return;
        }

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
