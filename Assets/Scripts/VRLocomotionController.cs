using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(150)]
public class VRLocomotionController : MonoBehaviour
{
    [Header("XR Rig")]
    [SerializeField] private Transform headTransform;
    [SerializeField] private FixedVrOriginWorldHeight originHeight;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 1.6f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float stickDeadzone = 0.2f;

    [Header("Terrain (Quest)")]
    [Tooltip("Smaller step on hills reduces CharacterController hitching on TerrainCollider.")]
    [SerializeField] private float natureStepOffset = 0.12f;
    [SerializeField] private float mainStepOffset = 0.3f;

    [Header("Turning")]
    [SerializeField] private bool useSnapTurn = true;
    [SerializeField] private float snapTurnDegrees = 30f;
    [SerializeField] private float snapTurnCooldownSeconds = 0.25f;
    [SerializeField] private bool useSmoothTurn = false;
    [SerializeField] private float smoothTurnSpeedDegrees = 90f;

    [Header("Conversation Placement")]
    [SerializeField] private Transform seatAnchor;
    [SerializeField] private Transform avatarLookTarget;
    [SerializeField] private bool placeAtSeatOnStart = true;
    [SerializeField] private float standingEyeHeight = 1.65f;

    private CharacterController _characterController;
    private float _verticalVelocity;
    private float _nextSnapTurnAllowedAt;
    private bool _questLocomotionProfileApplied;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();

        if (originHeight == null)
        {
            originHeight = GetComponent<FixedVrOriginWorldHeight>();
        }

        EnsureHeadTransform();
    }

    private void Start()
    {
        EnsureHeadTransform();

        if (placeAtSeatOnStart && XRSettings.isDeviceActive && seatAnchor != null)
        {
            PlaceAtSeatFacingAvatar();
        }
    }

    private void LateUpdate()
    {
        if (!XRSettings.isDeviceActive)
        {
            return;
        }

        EnsureHeadTransform();
        if (headTransform == null)
        {
            return;
        }

        ApplyQuestLocomotionProfileIfNeeded();
        ApplyTerrainLocomotionProfile();
        HandleMovement();
        HandleTurning();

        if (Input.GetKeyDown(KeyCode.F))
        {
            PlaceAtSeatFacingAvatar();
        }
    }

    private void Update()
    {
        if (XRSettings.isDeviceActive)
        {
            return;
        }

        if (headTransform == null)
        {
            EnsureHeadTransform();
        }

        if (headTransform == null)
        {
            return;
        }

        HandleMovement();
    }

    private void ApplyQuestLocomotionProfileIfNeeded()
    {
        if (_questLocomotionProfileApplied || _characterController == null)
        {
            return;
        }

        _questLocomotionProfileApplied = true;
        _characterController.skinWidth = 0.05f;
        _characterController.minMoveDistance = 0f;
        stickDeadzone = Mathf.Max(stickDeadzone, 0.22f);
    }

    private void EnsureHeadTransform()
    {
        if (headTransform != null)
        {
            return;
        }

        XROrigin xrOrigin = GetComponent<XROrigin>();
        if (xrOrigin != null && xrOrigin.Camera != null)
        {
            headTransform = xrOrigin.Camera.transform;
            return;
        }

        if (Camera.main != null)
        {
            headTransform = Camera.main.transform;
        }
    }

    private void ApplyTerrainLocomotionProfile()
    {
        if (_characterController == null)
        {
            return;
        }

        bool terrainFollow = originHeight != null && originHeight.TerrainFollowMode;
        _characterController.stepOffset = terrainFollow ? natureStepOffset : mainStepOffset;
    }

    public void PlaceAtSeatFacingAvatar()
    {
        if (seatAnchor == null)
        {
            Debug.LogWarning("[VRLocomotionController] Seat anchor is not assigned.");
            return;
        }

        EnsureHeadTransform();
        if (headTransform == null)
        {
            return;
        }

        Vector3 desiredGroundPosition = seatAnchor.position;
        Vector3 headOffset = headTransform.position - transform.position;

        if (headOffset.y > 0.01f)
        {
            desiredGroundPosition.y = seatAnchor.position.y + standingEyeHeight - headOffset.y;
        }

        headOffset.y = 0f;
        ApplyRigPosition(desiredGroundPosition - headOffset);

        if (avatarLookTarget != null)
        {
            Vector3 lookDirection = avatarLookTarget.position - headTransform.position;
            lookDirection.y = 0f;

            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                float targetYaw = Mathf.Atan2(lookDirection.x, lookDirection.z) * Mathf.Rad2Deg;
                float currentYaw = transform.eulerAngles.y;
                transform.Rotate(0f, targetYaw - currentYaw, 0f, Space.World);
            }
        }
    }

    private void HandleMovement()
    {
        Vector2 leftStick = ReadPrimary2DAxis(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller);

        if (leftStick.magnitude < stickDeadzone)
        {
            leftStick = Vector2.zero;
        }

        Vector3 headForward = headTransform.forward;
        Vector3 headRight = headTransform.right;
        headForward.y = 0f;
        headRight.y = 0f;

        if (headForward.sqrMagnitude > 0.0001f)
        {
            headForward.Normalize();
        }

        if (headRight.sqrMagnitude > 0.0001f)
        {
            headRight.Normalize();
        }

        Vector3 moveDirection = (headForward * leftStick.y + headRight * leftStick.x) * moveSpeed;

        if (XRSettings.isDeviceActive)
        {
            // Quest: only horizontal movement via CharacterController so walls + furniture block the rig.
            // Vertical (gravity / terrain follow) is handled by FixedVrOriginWorldHeight in LateUpdate.
            moveDirection.y = 0f;
            _verticalVelocity = 0f;

            if (moveDirection.sqrMagnitude > 0f && _characterController != null && _characterController.enabled)
            {
                _characterController.Move(moveDirection * Time.deltaTime);
            }

            return;
        }

        if (_characterController.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }

        _verticalVelocity += gravity * Time.deltaTime;
        moveDirection.y = _verticalVelocity;

        if (moveDirection.sqrMagnitude > 0f)
        {
            _characterController.Move(moveDirection * Time.deltaTime);
        }
    }

    /// <summary>Used by teleport / seat placement: hard-set rig position (skips colliders intentionally).</summary>
    private void ApplyRigPosition(Vector3 worldPosition)
    {
        if (_characterController != null)
        {
            _characterController.enabled = false;
        }

        transform.position = worldPosition;

        if (_characterController != null)
        {
            _characterController.enabled = true;
        }
    }

    public void ResetAfterTeleport()
    {
        _verticalVelocity = 0f;
        originHeight?.ResetPostureReference();
    }

    private void HandleTurning()
    {
        Vector2 rightStick = ReadPrimary2DAxis(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller);

        if (Mathf.Abs(rightStick.x) < stickDeadzone)
        {
            return;
        }

        if (useSmoothTurn)
        {
            float deltaYaw = rightStick.x * smoothTurnSpeedDegrees * Time.deltaTime;
            transform.Rotate(0f, deltaYaw, 0f, Space.World);
            return;
        }

        if (!useSnapTurn || Time.time < _nextSnapTurnAllowedAt)
        {
            return;
        }

        float direction = Mathf.Sign(rightStick.x);
        transform.Rotate(0f, direction * snapTurnDegrees, 0f, Space.World);
        _nextSnapTurnAllowedAt = Time.time + snapTurnCooldownSeconds;
    }

    private static Vector2 ReadPrimary2DAxis(InputDeviceCharacteristics characteristics)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(
            (characteristics & InputDeviceCharacteristics.Left) != 0 ? XRNode.LeftHand : XRNode.RightHand);

        if (!device.isValid)
        {
            return Vector2.zero;
        }

        if (device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis))
        {
            return axis;
        }

        return Vector2.zero;
    }
}
