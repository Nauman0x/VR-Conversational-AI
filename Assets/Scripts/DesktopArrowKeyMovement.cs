using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Provides desktop keyboard + mouse-drag locomotion for the XR Origin when a
/// VR headset is not connected.  Attach this to the same GameObject as
/// <see cref="CharacterController"/> (i.e. the XR Origin root).
/// </summary>
/// <remarks>
/// Controls:
///   W / Up Arrow    – move forward
///   S / Down Arrow  – move backward
///   A               – strafe left
///   D               – strafe right
///   Left Arrow      – turn left
///   Right Arrow     – turn right
///   Hold Right Mouse Button + drag – look (yaw + pitch)
///   PageUp / PageDown – look up / look down (keyboard pitch fallback)
/// </remarks>
[RequireComponent(typeof(CharacterController))]
public class DesktopArrowKeyMovement : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The transform whose local pitch is driven by vertical mouse look. " +
             "Auto-assigned to Camera.main if left empty.")]
    [SerializeField] private Transform viewTransform;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2.2f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Turning (Keyboard)")]
    [Tooltip("Degrees per second when using Left/Right arrow keys to yaw.")]
    [SerializeField] private float turnSpeedDegrees = 120f;

    [Header("Mouse-Drag Look")]
    [Tooltip("Enables look control via mouse drag.")]
    [SerializeField] private bool enableMouseLook = true;

    [Tooltip("Mouse button that must be held to drag-look. " +
             "0 = left, 1 = right, 2 = middle.")]
    [SerializeField] private int mouseDragButton = 1;

    [Tooltip("Horizontal and vertical mouse sensitivity multiplier.")]
    [SerializeField] private float mouseLookSensitivity = 2f;

    [Tooltip("Degrees per second used for PageUp / PageDown pitch.")]
    [SerializeField] private float keyboardLookSpeedDegrees = 90f;

    [SerializeField] private float minPitchDegrees = -75f;
    [SerializeField] private float maxPitchDegrees = 75f;

    [Tooltip("Lock the OS cursor while the drag button is held, " +
             "preventing it from wandering off-screen. Disable if you " +
             "need the cursor to stay visible for UI interaction.")]
    [SerializeField] private bool lockCursorWhileDragging = true;

    [Header("Desktop Camera Height Override")]
    [Tooltip(
        "Desktop only: pins Main Camera local Y when no headset is active so the idle XR driver " +
        "does not snap the view to floor height. Ignored automatically when XRSettings.isDeviceActive.")]
    [SerializeField] private bool overrideCameraHeight = true;

    [Tooltip("Local Y position the camera is locked to when Override Camera Height is enabled. " +
             "Represents eye height relative to the XR Origin root.")]
    [SerializeField] private float cameraLocalHeight = 3.8f;

    // -----------------------------------------------------------------------

    private CharacterController _characterController;
    private float _verticalVelocity;

    // Keyboard locomotion conflicts with VRLocomotionController when both Move the same CharacterController every frame (double gravity).
    private bool _desktopLocomotionEnabled;

    /// <summary>Current camera pitch, kept in [-180, 180] range.</summary>
    private float _pitchDegrees;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _desktopLocomotionEnabled = !XRSettings.isDeviceActive;

        // Fall back to the scene's main camera if no view transform was assigned.
        if (viewTransform == null && Camera.main != null)
        {
            viewTransform = Camera.main.transform;
        }

        if (viewTransform != null)
        {
            _pitchDegrees = NormalizePitch(viewTransform.localEulerAngles.x);
        }

        /*
         When the height override is active on desktop, disable TrackedPoseDriver so
         we can pin local camera Y. Never do that while a headset is running.
         */
        if (overrideCameraHeight && _desktopLocomotionEnabled)
        {
            DisableTrackedPoseDriverOnView();
        }
    }

    private void Update()
    {
        if (!_desktopLocomotionEnabled)
        {
            return;
        }

        HandleMovement();
        HandleYaw();
        HandlePitch();
        HandleCursorLock();
    }

    private void OnDisable()
    {
        // Always release the cursor when this component is disabled so the
        // application does not get stuck with a locked / hidden cursor.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /* -------------------------------------------------------------------------
     * TRACKED POSE DRIVER SUPPRESSION
     * The XR system attaches a TrackedPoseDriver (or legacy equivalent) to the
     * Main Camera.  That component writes the camera's WORLD position every
     * frame, overriding any localPosition lock we set in LateUpdate.
     * When overrideCameraHeight is enabled we disable every TrackedPoseDriver
     * found on the viewTransform so nothing fights the fixed height.
     * The components are re-enabled only if this script is destroyed, keeping
     * the session clean if you switch back to VR at runtime.
     * ------------------------------------------------------------------------- */
    private void DisableTrackedPoseDriverOnView()
    {
        if (!overrideCameraHeight || viewTransform == null)
        {
            return;
        }

        MonoBehaviour[] behaviours = viewTransform.GetComponents<MonoBehaviour>();

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
            {
                continue;
            }

            // Match both the new Input System driver and the legacy XR driver by
            // checking the type name so we don't need a hard assembly reference.
            string typeName = behaviour.GetType().FullName ?? string.Empty;
            if (typeName.Contains("TrackedPoseDriver"))
            {
                behaviour.enabled = false;
                Debug.Log("[DesktopArrowKeyMovement] Disabled " + typeName +
                          " on camera for desktop height lock.");
            }
        }
    }

    private void LateUpdate()
    {
        if (!_desktopLocomotionEnabled)
        {
            return;
        }

        // LateUpdate runs after the XR subsystem has repositioned the camera,
        // so writing the local Y here reliably wins over XR tracking resets.
        EnforceCameraHeight();
    }

    /* -------------------------------------------------------------------------
     * CAMERA HEIGHT LOCK
     * Pins the view transform local Y to cameraLocalHeight every LateUpdate.
     * This prevents the XR subsystem from resetting it to 0 when no headset
     * is connected.  Disable overrideCameraHeight in the Inspector before
     * switching back to real VR hardware.
     * ------------------------------------------------------------------------- */
    private void EnforceCameraHeight()
    {
        if (!overrideCameraHeight || viewTransform == null || !_desktopLocomotionEnabled)
        {
            return;
        }

        Vector3 localPos = viewTransform.localPosition;

        // Only write when the value has actually drifted to avoid unnecessary
        // dirty-marking of the transform every frame.
        if (!Mathf.Approximately(localPos.y, cameraLocalHeight))
        {
            localPos.y = cameraLocalHeight;
            viewTransform.localPosition = localPos;
        }
    }

    /* -------------------------------------------------------------------------
     * MOVEMENT
     * W / Up Arrow   → forward
     * S / Down Arrow → backward
     * A              → strafe left
     * D              → strafe right
     * ------------------------------------------------------------------------- */
    private void HandleMovement()
    {
        float forwardAxis = 0f;
        float strafeAxis = 0f;

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            forwardAxis += 1f;
        }

        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            forwardAxis -= 1f;
        }

        if (Input.GetKey(KeyCode.A))
        {
            strafeAxis -= 1f;
        }

        if (Input.GetKey(KeyCode.D))
        {
            strafeAxis += 1f;
        }

        // Derive movement axes from the view direction so "forward" always
        // matches where the camera is looking horizontally.
        Vector3 forward = viewTransform != null ? viewTransform.forward : transform.forward;
        Vector3 right   = viewTransform != null ? viewTransform.right   : transform.right;

        // Flatten both axes so slope geometry does not affect horizontal intent.
        forward.y = 0f;
        right.y   = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 moveDirection = forward * (forwardAxis * moveSpeed)
                              + right   * (strafeAxis  * moveSpeed);

        // Apply gravity.
        if (_characterController.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }

        _verticalVelocity += gravity * Time.deltaTime;
        moveDirection.y = _verticalVelocity;

        _characterController.Move(moveDirection * Time.deltaTime);
    }

    /* -------------------------------------------------------------------------
     * YAW (horizontal rotation)
     * Left / Right Arrow keys → fixed-speed turn
     * Mouse X while drag button held → smooth mouse yaw
     * ------------------------------------------------------------------------- */
    private void HandleYaw()
    {
        float turnAxis = 0f;

        if (Input.GetKey(KeyCode.LeftArrow))
        {
            turnAxis -= 1f;
        }

        if (Input.GetKey(KeyCode.RightArrow))
        {
            turnAxis += 1f;
        }

        // Only accumulate mouse yaw while the designated button is held.
        if (enableMouseLook && Input.GetMouseButton(mouseDragButton))
        {
            turnAxis += Input.GetAxis("Mouse X") * mouseLookSensitivity;
        }

        if (Mathf.Abs(turnAxis) < 0.01f)
        {
            return;
        }

        // Rotate the rig root so the CharacterController stays aligned with
        // the facing direction; the camera pitch is handled separately.
        float deltaYaw = turnAxis * turnSpeedDegrees * Time.deltaTime;
        transform.Rotate(0f, deltaYaw, 0f, Space.World);
    }

    /* -------------------------------------------------------------------------
     * PITCH (vertical look)
     * PageUp / PageDown        → keyboard pitch at fixed speed
     * Mouse Y while drag held  → smooth mouse pitch, clamped to [min, max]
     * ------------------------------------------------------------------------- */
    private void HandlePitch()
    {
        if (viewTransform == null)
        {
            return;
        }

        float pitchInput = 0f;

        if (Input.GetKey(KeyCode.PageUp))
        {
            pitchInput += 1f;
        }

        if (Input.GetKey(KeyCode.PageDown))
        {
            pitchInput -= 1f;
        }

        // Mouse Y is positive when moving up; invert so dragging up looks up.
        if (enableMouseLook && Input.GetMouseButton(mouseDragButton))
        {
            pitchInput += -Input.GetAxis("Mouse Y") * mouseLookSensitivity;
        }

        if (Mathf.Abs(pitchInput) < 0.01f)
        {
            return;
        }

        _pitchDegrees += pitchInput * keyboardLookSpeedDegrees * Time.deltaTime;
        _pitchDegrees = Mathf.Clamp(_pitchDegrees, minPitchDegrees, maxPitchDegrees);

        Vector3 localAngles = viewTransform.localEulerAngles;
        localAngles.x = _pitchDegrees;
        viewTransform.localEulerAngles = localAngles;
    }

    /* -------------------------------------------------------------------------
     * CURSOR LOCK
     * Hides and locks the OS cursor while dragging so it cannot wander
     * off-screen during a long look gesture.
     * ------------------------------------------------------------------------- */
    private void HandleCursorLock()
    {
        if (!enableMouseLook || !lockCursorWhileDragging)
        {
            return;
        }

        bool dragging = Input.GetMouseButton(mouseDragButton);
        Cursor.lockState = dragging ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !dragging;
    }

    /// <summary>
    /// Normalises an Euler angle that Unity may return in [0, 360] to the
    /// signed [-180, 180] range so pitch clamping works correctly.
    /// </summary>
    private static float NormalizePitch(float angle)
    {
        if (angle > 180f)
        {
            angle -= 360f;
        }

        return angle;
    }
}
