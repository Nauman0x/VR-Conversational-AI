using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Simple "rig sits on the ground" gravity for the XR Origin. On Quest it raycasts
/// down each frame, settles the rig pivot so the CharacterController capsule's bottom
/// rests on the ground, and uses a fixed <see cref="CameraYOffset"/> so the player's
/// eye level is consistent regardless of physical height.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class FixedVrOriginWorldHeight : MonoBehaviour
{
    public enum GroundFollowMode
    {
        Off,
        Flat,
        Terrain,
    }

    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private Transform headTransform;

    [Tooltip("Camera height above the rig pivot, applied via XROrigin.CameraYOffset. " +
             "User asked for ~1.5 m eye level regardless of physical height.")]
    [SerializeField] private float fixedCameraYOffset = 1.5f;

    [Tooltip("Force Device tracking origin so the camera stays at fixedCameraYOffset above the rig " +
             "instead of the user's real floor-relative head height.")]
    [SerializeField] private bool useDeviceTrackingOrigin = true;

    [Header("Ground gravity")]
    [SerializeField] private LayerMask terrainFollowGroundLayers = ~0;

    [Tooltip("Ray starts this far ABOVE the rig (small value so it doesn't catch ceilings/roofs).")]
    [SerializeField] private float terrainFollowRayStartHeight = 0.4f;

    [SerializeField] private float terrainFollowRayMaxDistance = 60f;

    [Tooltip("SmoothDamp time for terrain mode (forest). Lower = snappier, higher = floatier.")]
    [SerializeField] private float terrainFollowSmoothTime = 0.18f;

    [Tooltip("Only re-write rig Y when off-target by more than this many metres. " +
             "Lets CharacterController.Move() handle small adjustments without us fighting it.")]
    [SerializeField] private float groundSnapDeadband = 0.03f;

    private GroundFollowMode _followMode = GroundFollowMode.Flat;
    private float _cachedGroundY;
    private int _rayFrameCounter;
    private float _terrainFollowYSpeed;
    private float _capsuleBottomLocalY;
    private bool _capsuleBottomCached;
    private CharacterController _characterController;
    private float _lastFixedOriginWorldHeight;

    /// <summary>True while ground gravity is driving rig Y (VRLocomotion skips its own gravity).</summary>
    public bool HandlesVerticalPosition => _followMode != GroundFollowMode.Off;

    public bool TerrainFollowMode => _followMode == GroundFollowMode.Terrain;

    public float FixedCameraYOffset => fixedCameraYOffset;

    public float FixedOriginWorldHeight => _lastFixedOriginWorldHeight;

    private void Awake()
    {
        if (xrOrigin == null)
        {
            xrOrigin = GetComponent<XROrigin>();
        }

        if (headTransform == null && xrOrigin != null && xrOrigin.Camera != null)
        {
            headTransform = xrOrigin.Camera.transform;
        }

        if (headTransform == null && Camera.main != null)
        {
            headTransform = Camera.main.transform;
        }

        _characterController = GetComponent<CharacterController>();
        CacheCapsuleBottomOffset();

        if (xrOrigin != null)
        {
            xrOrigin.RequestedTrackingOriginMode = useDeviceTrackingOrigin
                ? XROrigin.TrackingOriginMode.Device
                : XROrigin.TrackingOriginMode.Floor;
            xrOrigin.CameraYOffset = fixedCameraYOffset;
        }
    }

    private void LateUpdate()
    {
        if (_followMode == GroundFollowMode.Off || !XRSettings.isDeviceActive)
        {
            return;
        }

        Vector3 rigPosition = transform.position;

        int rayInterval = _followMode == GroundFollowMode.Flat ? 8 : 2;
        _rayFrameCounter++;
        if (_rayFrameCounter % rayInterval == 0)
        {
            _cachedGroundY = SampleGroundY(rigPosition.x, rigPosition.z, rigPosition.y);
        }

        // Target rig pivot so the CharacterController capsule's bottom sits on the ground.
        // capsuleBottomLocalY is negative when CC.center.y < CC.height/2, so we ADD it back.
        float targetRigY = _cachedGroundY - _capsuleBottomLocalY;

        // Deadband: skip writes when already close enough so CC.Move() can settle naturally
        // without our gravity write fighting it every frame (was breaking horizontal movement).
        float deltaY = targetRigY - rigPosition.y;
        if (Mathf.Abs(deltaY) < groundSnapDeadband && _followMode == GroundFollowMode.Flat)
        {
            _lastFixedOriginWorldHeight = rigPosition.y;
            return;
        }

        float newY;
        if (_followMode == GroundFollowMode.Terrain)
        {
            newY = Mathf.SmoothDamp(
                rigPosition.y,
                targetRigY,
                ref _terrainFollowYSpeed,
                Mathf.Max(0.02f, terrainFollowSmoothTime));
        }
        else
        {
            newY = targetRigY;
        }

        // Y-only adjustment. Don't toggle CharacterController enabled here — that was
        // resetting CC's internal grounded state every frame and blocking CC.Move().
        rigPosition.y = newY;
        transform.position = rigPosition;
        _lastFixedOriginWorldHeight = newY;
    }

    /// <summary>Switches between flat (office) and terrain (forest) ground modes.</summary>
    public void SetGroundFollow(GroundFollowMode mode, LayerMask layers)
    {
        _followMode = mode;
        terrainFollowGroundLayers = layers;
        _terrainFollowYSpeed = 0f;
        _rayFrameCounter = 0;

        if (mode != GroundFollowMode.Off)
        {
            Vector3 rigPosition = transform.position;
            _cachedGroundY = SampleGroundY(rigPosition.x, rigPosition.z, rigPosition.y);
        }
    }

    /// <summary>One-shot rig Y after teleport. Gravity then takes over next frame.</summary>
    public void ApplyOneShotWorldHeight(float worldHeightY)
    {
        _lastFixedOriginWorldHeight = worldHeightY;

        Vector3 rigPosition = transform.position;
        rigPosition.y = worldHeightY - _capsuleBottomLocalY;
        ApplyRigWorldPosition(rigPosition);
    }

    /// <summary>Legacy compatibility used by older scripts.</summary>
    public void SetFixedWorldHeight(float worldHeightY, bool lockHeight = true)
    {
        ApplyOneShotWorldHeight(worldHeightY);
    }

    /// <summary>Legacy compatibility used by older scripts.</summary>
    public void SetTerrainFollowMode(bool enabled, float heightAboveGround, LayerMask groundLayers)
    {
        SetGroundFollow(enabled ? GroundFollowMode.Terrain : GroundFollowMode.Flat, groundLayers);
    }

    /// <summary>Legacy no-op (calibration removed).</summary>
    public void Calibrate(float headLocalY, float avatarStandingHeight)
    {
        // Intentionally empty — height is no longer per-user calibrated. Camera offset is fixed.
    }

    public bool IsCalibrated => true;

    public void ResetPostureReference()
    {
        // No-op: nothing to recapture anymore.
    }

    public float GetHeadLocalHeight()
    {
        if (headTransform == null)
        {
            return fixedCameraYOffset;
        }

        return transform.InverseTransformPoint(headTransform.position).y;
    }

    private void CacheCapsuleBottomOffset()
    {
        if (_capsuleBottomCached)
        {
            return;
        }

        if (_characterController == null)
        {
            _characterController = GetComponent<CharacterController>();
        }

        if (_characterController != null)
        {
            // Capsule bottom in rig-local space, accounting for skinWidth so we sit just above the
            // ground instead of penetrating (which was stalling CC.Move() horizontally).
            _capsuleBottomLocalY = _characterController.center.y
                                    - _characterController.height * 0.5f
                                    + _characterController.skinWidth;
        }
        else
        {
            _capsuleBottomLocalY = 0f;
        }

        _capsuleBottomCached = true;
    }

    private void ApplyRigWorldPosition(Vector3 worldPosition)
    {
        CharacterController characterController = _characterController;
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        transform.position = worldPosition;

        if (characterController != null)
        {
            characterController.enabled = true;
        }
    }

    private static readonly RaycastHit[] _groundHitBuffer = new RaycastHit[8];

    private float SampleGroundY(float worldX, float worldZ, float fallbackY)
    {
        Vector3 rayOrigin = new Vector3(worldX, fallbackY + terrainFollowRayStartHeight, worldZ);
        int hitCount = Physics.RaycastNonAlloc(
            rayOrigin,
            Vector3.down,
            _groundHitBuffer,
            terrainFollowRayMaxDistance + terrainFollowRayStartHeight,
            terrainFollowGroundLayers,
            QueryTriggerInteraction.Ignore);

        if (hitCount == 0)
        {
            return fallbackY + _capsuleBottomLocalY;
        }

        float bestY = float.NegativeInfinity;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _groundHitBuffer[i];
            Collider hitCollider = hit.collider;

            if (hitCollider == null)
            {
                continue;
            }

            if (IsSelfCollider(hitCollider))
            {
                continue;
            }

            float candidateY = hit.point.y;

            if (candidateY > rayOrigin.y - 0.01f)
            {
                continue;
            }

            if (!found || candidateY > bestY)
            {
                bestY = candidateY;
                found = true;
            }
        }

        if (!found)
        {
            return fallbackY + _capsuleBottomLocalY;
        }

        return bestY;
    }

    private bool IsSelfCollider(Collider candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        Transform t = candidate.transform;
        return t == transform || t.IsChildOf(transform);
    }
}
