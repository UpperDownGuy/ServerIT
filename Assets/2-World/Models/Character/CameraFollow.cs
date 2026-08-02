using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Head Sync")]
    [Tooltip("Optional. Automatically found by name or humanoid rig when empty.")]
    public Transform head;
    public Vector3 headPitchAxis = Vector3.right;

    [Header("First Person Visibility")]
    [Tooltip("Hides only the local character's head geometry so it cannot cover the camera view.")]
    public bool hideFirstPersonHead = true;
    [Tooltip("Name of the separate head mesh in the imported character model.")]
    public string firstPersonHeadMeshName = "Cube.003";

    [Header("Camera Light")]
    [Tooltip("Prevents lights attached to this camera from casting the local player onto walls.")]
    public bool excludePlayerFromCameraLight = true;
    public string localPlayerLightLayer = "LocalPlayer";
    [Tooltip("Uses camera-child lights as stable first-person spot lights instead of rotating world lights.")]
    public bool configureCameraLightsAsSpot = true;
    [Min(0f)] public float cameraLightIntensity = 5f;
    [Min(0f)] public float cameraLightRange = 85f;
    [Range(1f, 179f)] public float cameraLightSpotAngle = 90f;
    [Range(0f, 179f)] public float cameraLightInnerSpotAngle = 65f;

    [Header("Mouse Look")]
    [Min(0f)] public float mouseSensitivity = 0.15f;
    [Range(-89f, 89f)] public float minimumPitch = -20f;
    [Range(-89f, 89f)] public float maximumPitch = 80f;

    private PlayerMovement playerMovement;
    private Quaternion startingLocalRotation;
    private Quaternion startingHeadLocalRotation;
    private float cameraPitch;
    private bool wantsCursorLocked;
    private Renderer[] hiddenFirstPersonRenderers;
    private ShadowCastingMode[] originalShadowCastingModes;
    private Renderer[] localPlayerRenderers;
    private int[] originalRendererLayers;
    private Light[] cameraLights;
    private int[] originalLightCullingMasks;

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

        // Keep the camera on the stable Rigidbody root. Imported head bones can
        // contain animation, non-uniform scale, and delayed pose updates that
        // warp or invert a child camera.
        if (transform.parent != target)
        {
            transform.SetParent(target, true);
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

        ConfigureFirstPersonVisibility();
        ConfigureCameraLights();
        ConfigureCameraLightExclusion();

        SetCursorLocked(true);
    }

    private void ConfigureCameraLights()
    {
        cameraLights = GetComponentsInChildren<Light>(true);
        if (!configureCameraLightsAsSpot)
        {
            return;
        }

        foreach (Light cameraLight in cameraLights)
        {
            cameraLight.type = LightType.Spot;
            cameraLight.intensity = cameraLightIntensity;
            cameraLight.range = cameraLightRange;
            cameraLight.spotAngle = cameraLightSpotAngle;
            cameraLight.innerSpotAngle = Mathf.Min(
                cameraLightInnerSpotAngle,
                cameraLightSpotAngle);
            cameraLight.bounceIntensity = 1f;
            cameraLight.shadows = LightShadows.Soft;
            cameraLight.shadowBias = 0.05f;
            cameraLight.shadowNormalBias = 0.4f;
            cameraLight.shadowNearPlane = 0.2f;

            // A spot light at the optical center follows yaw and pitch without
            // moving the world's primary illumination as the player looks.
            cameraLight.transform.localPosition = Vector3.zero;
            cameraLight.transform.localRotation = Quaternion.identity;
        }
    }

    private void ConfigureCameraLightExclusion()
    {
        if (!excludePlayerFromCameraLight)
        {
            return;
        }

        int localPlayerLayer = LayerMask.NameToLayer(localPlayerLightLayer);
        if (localPlayerLayer < 0)
        {
            Debug.LogWarning(
                $"CameraFollow could not find the layer '{localPlayerLightLayer}'.",
                this);
            return;
        }

        localPlayerRenderers = target.GetComponentsInChildren<Renderer>(true);
        originalRendererLayers = new int[localPlayerRenderers.Length];
        for (int i = 0; i < localPlayerRenderers.Length; i++)
        {
            originalRendererLayers[i] = localPlayerRenderers[i].gameObject.layer;
            localPlayerRenderers[i].gameObject.layer = localPlayerLayer;
        }

        int localPlayerMask = 1 << localPlayerLayer;
        if (cameraLights == null)
        {
            cameraLights = GetComponentsInChildren<Light>(true);
        }
        originalLightCullingMasks = new int[cameraLights.Length];
        for (int i = 0; i < cameraLights.Length; i++)
        {
            originalLightCullingMasks[i] = cameraLights[i].cullingMask;
            cameraLights[i].cullingMask &= ~localPlayerMask;
        }
    }

    private void ConfigureFirstPersonVisibility()
    {
        if (!hideFirstPersonHead || string.IsNullOrWhiteSpace(firstPersonHeadMeshName))
        {
            return;
        }

        Renderer[] characterRenderers = target.GetComponentsInChildren<Renderer>(true);
        Renderer headRenderer = null;

        foreach (Renderer characterRenderer in characterRenderers)
        {
            if (characterRenderer.name == firstPersonHeadMeshName)
            {
                headRenderer = characterRenderer;
                break;
            }
        }

        if (headRenderer == null)
        {
            Debug.LogWarning(
                $"CameraFollow could not find the first-person head mesh '{firstPersonHeadMeshName}'.",
                this);
            return;
        }

        // The eyes and mouth are separate sibling meshes in this model. Select
        // them by their centers being inside the head bounds so the entire face
        // is hidden without affecting the torso, arms, or legs.
        Bounds headBounds = headRenderer.bounds;
        headBounds.Expand(headBounds.size * 0.25f);

        List<Renderer> renderersToHide = new List<Renderer>();
        foreach (Renderer characterRenderer in characterRenderers)
        {
            if (characterRenderer == headRenderer ||
                headBounds.Contains(characterRenderer.bounds.center))
            {
                renderersToHide.Add(characterRenderer);
            }
        }

        hiddenFirstPersonRenderers = renderersToHide.ToArray();
        originalShadowCastingModes = new ShadowCastingMode[hiddenFirstPersonRenderers.Length];

        for (int i = 0; i < hiddenFirstPersonRenderers.Length; i++)
        {
            originalShadowCastingModes[i] = hiddenFirstPersonRenderers[i].shadowCastingMode;
            hiddenFirstPersonRenderers[i].shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
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
        if (target != null && transform.parent != target)
        {
            // Also repairs the hierarchy immediately after a Play Mode script
            // reload from versions that parented the camera to the head.
            transform.SetParent(target, true);
            startingLocalRotation = transform.localRotation;
        }

        if (head != null)
        {
            Vector3 pitchAxis = headPitchAxis.sqrMagnitude > 0.0001f
                ? headPitchAxis.normalized
                : Vector3.right;

            head.localRotation =
                startingHeadLocalRotation *
                Quaternion.AngleAxis(cameraPitch, pitchAxis);
        }

        // Camera pitch stays in the stable body-root space. Body yaw is inherited
        // directly from the Rigidbody, so camera and character turn together.
        transform.localRotation =
            startingLocalRotation *
            Quaternion.AngleAxis(cameraPitch, Vector3.right);
    }

    private void OnDisable()
    {
        SetCursorLocked(false);
    }

    private void OnDestroy()
    {
        RestoreFirstPersonVisibility();
        RestoreCameraLightExclusion();
    }

    private void RestoreFirstPersonVisibility()
    {
        if (hiddenFirstPersonRenderers == null || originalShadowCastingModes == null)
        {
            return;
        }

        for (int i = 0; i < hiddenFirstPersonRenderers.Length; i++)
        {
            if (hiddenFirstPersonRenderers[i] != null)
            {
                hiddenFirstPersonRenderers[i].shadowCastingMode = originalShadowCastingModes[i];
            }
        }
    }

    private void RestoreCameraLightExclusion()
    {
        if (localPlayerRenderers != null && originalRendererLayers != null)
        {
            for (int i = 0; i < localPlayerRenderers.Length; i++)
            {
                if (localPlayerRenderers[i] != null)
                {
                    localPlayerRenderers[i].gameObject.layer = originalRendererLayers[i];
                }
            }
        }

        if (cameraLights != null && originalLightCullingMasks != null)
        {
            for (int i = 0; i < cameraLights.Length; i++)
            {
                if (cameraLights[i] != null)
                {
                    cameraLights[i].cullingMask = originalLightCullingMasks[i];
                }
            }
        }
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
