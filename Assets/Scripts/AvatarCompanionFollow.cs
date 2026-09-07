using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.AI;

[DefaultExecutionOrder(-500)]
public class AvatarCompanionFollow : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform playerTarget;
    [SerializeField] private OculusLipSyncBlendShape lipSyncBlendShape;
    [SerializeField] private Animator avatarAnimator;
    [SerializeField] private bool autoFindPlayerTarget = true;
    [SerializeField] private float autoFindTargetIntervalSeconds = 0.5f;

    [Header("Distance")]
    [SerializeField] private float minDistanceFromPlayer = 1.5f;
    [SerializeField] private float maxDistanceFromPlayer = 2.0f;
    [SerializeField] private float preferredDistanceFromPlayer = 1.7f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 1.1f;
    [SerializeField] private float turnSpeedDegrees = 360f;
    [SerializeField] private bool lockToInitialHeight = true;
    [SerializeField] private bool useNavMeshAgentIfAvailable = true;
    [SerializeField] private bool useCharacterControllerIfAvailable = true;

    [Header("Ground Snapping")]
    [Tooltip("Snaps the avatar Y to the terrain surface via a downward raycast each frame. " +
             "Enable this on terrains with varying height so the avatar follows hills and " +
             "valleys instead of floating at a fixed Y. Takes priority over Lock To Initial " +
             "Height when both are enabled. Only active when NavMeshAgent is not on a NavMesh.")]
    [SerializeField] private bool snapToGround = true;

    [Tooltip("Layers that count as 'ground' for the snap raycast. Default checks all layers.")]
    [SerializeField] private LayerMask groundSnapLayers = ~0;

    [Tooltip("Height above the avatar's current position where the snap ray originates. " +
             "Increase if the avatar can spawn partially below the terrain.")]
    [SerializeField] private float groundRaycastOriginOffset = 3f;

    [Tooltip("Maximum downward distance the ray travels beyond the origin offset.")]
    [SerializeField] private float groundRaycastMaxDistance = 10f;

    [Tooltip("How quickly (Lerp factor per second) the Y smooths toward the ground surface. " +
             "0 = instant snap each frame. Higher values give smoother but slower settling.")]
    [SerializeField] private float groundSnapSmoothSpeed = 6f;

    [Tooltip("Ignore ground height changes smaller than this (reduces terrain micro-jitter on Quest).")]
    [SerializeField] private float groundSnapDeadband = 0.035f;

    [Tooltip("SmoothDamp time for Y in conversation-stand mode (forest).")]
    [SerializeField] private float conversationGroundSmoothTime = 0.22f;

    [Tooltip("Extra Y added above the raycast hit point. Use a small positive value " +
             "if the avatar's root pivot sits exactly at its feet (e.g. 0.02).")]
    [SerializeField] private float groundSnapVerticalOffset = 0f;

    [Header("NavMesh Follow")]
    [SerializeField] private bool useSmartNavDestination = true;
    [SerializeField] private float navSampleRadius = 1.25f;
    [SerializeField] private float navRepathIntervalSeconds = 0.2f;
    [SerializeField] private float navDestinationUpdateDistance = 0.2f;

    [Header("Behavior")]
    [SerializeField] private bool pauseWhileAvatarIsSpeaking = true;
    [SerializeField] private bool alwaysFacePlayerWhenClose = true;

    [Header("Animation")]
    [SerializeField] private bool driveWalkAnimation = true;
    [SerializeField] private string walkBoolParam = "Walk";
    [SerializeField] private string[] walkBoolFallbackParams = { "isWalking", "IsWalking", "Walking" };
    [SerializeField] private bool triggerWalkOnFollowStart = true;
    [SerializeField] private string walkTriggerParam = "WalkTrigger";
    [SerializeField] private string[] walkTriggerFallbackParams = { "Walk", "StartWalk" };
    [SerializeField] private bool logWalkAnimationWarnings = true;
    [SerializeField] private float walkStartDistanceBuffer = 0.1f;
    [SerializeField] private float walkStopDistanceBuffer = 0.1f;
    [SerializeField] private float minWalkVelocity = 0.05f;
    [SerializeField] private float walkStateHoldSeconds = 0.2f;

    [Header("Collision")]
    [SerializeField] private bool ensureRootCapsuleCollider = true;
    [SerializeField] private float colliderHeight = 1.65f;
    [SerializeField] private float colliderRadius = 0.28f;
    [SerializeField] private float colliderCenterY = 0.82f;
    [SerializeField] private LayerMask movementBlockerLayers = ~0;
    [SerializeField] private float blockerProbePadding = 0.02f;

    [Header("Conversation Stand (forest)")]
    [Tooltip("When enabled by EnvironmentTeleporter: avatar stands still, faces player, no follow jitter.")]
    [SerializeField] private bool conversationStandMode;

    [SerializeField] private float conversationTurnSpeedDegrees = 120f;

    [Header("Debug")]
    [SerializeField] private bool verboseDebugLogs;

    private bool _walkParamCached;
    private bool _hasWalkParam;
    private string _resolvedWalkParam = string.Empty;
    private bool _walkTriggerParamCached;
    private bool _hasWalkTriggerParam;
    private string _resolvedWalkTriggerParam = string.Empty;
    private bool _wasWalking;
    private float _walkHoldUntil;
    private Vector3 _previousPosition;
    private float _lockedY;
    private NavMeshAgent _navAgent;
    private CharacterController _characterController;
    private NavMeshPath _navPath;
    private float _nextNavRepathAt;
    private Vector3 _lastNavDestination;
    private static readonly float[] CandidateFollowAngles = { 0f, -35f, 35f, -70f, 70f, -110f, 110f, 180f };
    private static bool _loggedNavMeshUnavailable;
    private float _groundYVelocity;
    private int _groundSnapFrame;
    private Transform _playerFollowRoot;
    private XROrigin _cachedXrOrigin;
    private float _navStuckTimer;
    private Vector3 _navStuckCheckPosition;
    private float _nextNavRecoveryAt;
    private float _nextPlayerTargetRefreshAt;
    private float _followFailureTimer;
    private bool _questDirectFollowProfileApplied;
    private static readonly RaycastHit[] _blockerHitBuffer = new RaycastHit[8];
    private static readonly Vector3[] _sidestepDirections = { Vector3.zero, Vector3.right, Vector3.left };

    public bool ConversationStandMode => conversationStandMode;

    public void SetConversationStandMode(bool enabled)
    {
        conversationStandMode = enabled;

        if (enabled)
        {
            StopAgentMovement();
            if (_navAgent != null)
            {
                _navAgent.enabled = false;
            }
        }

        _groundYVelocity = 0f;
        _walkHoldUntil = 0f;
    }

    /// <summary>Forest terrain has no NavMesh — use transform/CharacterController follow instead.</summary>
    public void PrepareForTerrainFollow()
    {
        ApplyQuestDirectFollowProfileIfNeeded();

        if (_navAgent != null)
        {
            _navAgent.enabled = false;
        }
    }

    private void Awake()
    {
        _navAgent = GetComponent<NavMeshAgent>();
        if (_navAgent != null)
        {
            _navAgent.enabled = false;
        }

        if (playerTarget == null)
        {
            _cachedXrOrigin = FindObjectOfType<XROrigin>();
            if (_cachedXrOrigin != null && _cachedXrOrigin.Camera != null)
            {
                playerTarget = _cachedXrOrigin.Camera.transform;
                _playerFollowRoot = _cachedXrOrigin.transform;
            }
            else if (Camera.main != null)
            {
                playerTarget = Camera.main.transform;
                _playerFollowRoot = playerTarget.root;
            }
        }
        else if (_playerFollowRoot == null)
        {
            _playerFollowRoot = playerTarget.root;
        }

        if (lipSyncBlendShape == null)
        {
            lipSyncBlendShape = GetComponent<OculusLipSyncBlendShape>();
        }

        if (avatarAnimator == null && lipSyncBlendShape != null)
        {
            avatarAnimator = lipSyncBlendShape.avatarAnimator;
        }

        _navAgent = GetComponent<NavMeshAgent>();
        _characterController = GetComponent<CharacterController>();
        _navPath = new NavMeshPath();

        if (_navAgent != null)
        {
            /*
             Agent stays disabled until a valid NavMesh is found (prevents console spam on terrain-only zones).
            */
            _navAgent.enabled = false;
            _navAgent.speed = Mathf.Max(0.01f, moveSpeed);
            _navAgent.updateRotation = false;
            _navAgent.autoRepath = true;
            _navAgent.autoBraking = true;
            _navAgent.angularSpeed = Mathf.Max(180f, turnSpeedDegrees);
            _navAgent.obstacleAvoidanceType =
#if UNITY_ANDROID && !UNITY_EDITOR
                ObstacleAvoidanceType.LowQualityObstacleAvoidance;
#else
                ObstacleAvoidanceType.HighQualityObstacleAvoidance;
#endif

            if (useNavMeshAgentIfAvailable)
            {
                float probeRadius = Mathf.Max(2.5f, navSampleRadius);
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit navProbeHit, probeRadius, NavMesh.AllAreas))
                {
                    _navAgent.enabled = true;
                    _navAgent.Warp(navProbeHit.position);
                }
                else if (!_loggedNavMeshUnavailable)
                {
                    _loggedNavMeshUnavailable = true;
                    Debug.LogWarning(
                        "[AvatarCompanionFollow] No NavMesh found within " + probeRadius.ToString("F1") +
                        "m of the avatar — NavMeshAgent stayed disabled (prevents \"Failed to create agent because there is no valid NavMesh\"). " +
                        "Bake the scene (Window > AI > Navigation) or disable Use Nav Mesh Agent If Available; CharacterController / transform fallback still moves the avatar.");
                }
            }
        }

        EnsureRootCollision();
        ApplyQuestDirectFollowProfileIfNeeded();
        _lockedY = transform.position.y;
        _previousPosition = transform.position;
        CacheWalkParam();
        CacheWalkTriggerParam();
    }

    private void Update()
    {
        RefreshPlayerTargetIfNeeded();

        if (playerTarget == null)
        {
            if (verboseDebugLogs)
            {
                Debug.LogWarning("[AvatarCompanionFollow] Player target is missing. Assign player camera transform.");
            }
            return;
        }

        bool isSpeaking = pauseWhileAvatarIsSpeaking && IsAvatarSpeaking();
        Vector3 followPosition = GetPlayerFollowPosition();
        Vector3 toPlayer = followPosition - transform.position;
        Vector3 toPlayerFlat = new Vector3(toPlayer.x, 0f, toPlayer.z);
        float flatDistance = toPlayerFlat.magnitude;

        // Resume follow once the player is farther than the preferred conversation distance.
        float followStartDistance = preferredDistanceFromPlayer + Mathf.Max(0f, walkStartDistanceBuffer);
        float stopDistanceForWalk = preferredDistanceFromPlayer + Mathf.Max(0f, walkStopDistanceBuffer);
        bool shouldMove = !conversationStandMode && !isSpeaking && flatDistance > followStartDistance;

        if (conversationStandMode)
        {
            StopAgentMovement();
            if (flatDistance > 0.001f)
            {
                RotateToward(toPlayerFlat.normalized, conversationTurnSpeedDegrees);
            }

            ApplyWalkAnimation(false);
            _previousPosition = transform.position;
            return;
        }

        if (verboseDebugLogs && Time.frameCount % 90 == 0)
        {
            Debug.Log("[AvatarCompanionFollow] distance=" + flatDistance.ToString("F2") + ", speaking=" + isSpeaking + ", moving=" + shouldMove);
        }

        if (shouldMove)
        {
            bool usedNavMesh = MoveTowardPreferredDistance(toPlayerFlat, flatDistance, followPosition);
            UpdateNavStuckTracking(usedNavMesh, shouldMove, flatDistance, followStartDistance);
            UpdateDirectFollowFailureTracking(shouldMove, flatDistance, followStartDistance);
        }
        else if (alwaysFacePlayerWhenClose && flatDistance > 0.001f)
        {
            StopAgentMovement();
            RotateToward(toPlayerFlat.normalized);
            _followFailureTimer = 0f;
        }
        else
        {
            StopAgentMovement();
            _navStuckTimer = 0f;
            _followFailureTimer = 0f;
        }

        bool shouldWalkAnim = DetermineWalkAnimationState(isSpeaking, flatDistance, stopDistanceForWalk, shouldMove);
        ApplyWalkAnimation(shouldWalkAnim);

        _previousPosition = transform.position;
    }

    private void LateUpdate()
    {
        if (snapToGround)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _groundSnapFrame++;
            if (_groundSnapFrame % 2 == 0)
#endif
            {
                ApplyGroundSnapSmooth();
            }
        }
        else if (lockToInitialHeight && (_navAgent == null || !_navAgent.enabled))
        {
            Vector3 position = transform.position;
            position.y = _lockedY;
            transform.position = position;
        }
    }

    private bool DetermineWalkAnimationState(bool isSpeaking, float flatDistance, float stopDistanceForWalk, bool shouldMove)
    {
        if (isSpeaking)
        {
            _walkHoldUntil = 0f;
            return false;
        }

        bool isActuallyMoving = IsActuallyMoving();
        bool hasMeaningfulPendingPath = false;
        if (_navAgent != null && _navAgent.enabled && _navAgent.isOnNavMesh)
        {
            if (_navAgent.pathPending)
            {
                hasMeaningfulPendingPath = true;
            }
            else if (_navAgent.hasPath && _navAgent.pathStatus == NavMeshPathStatus.PathComplete)
            {
                float remaining = _navAgent.remainingDistance;
                if (!float.IsInfinity(remaining) && !float.IsNaN(remaining))
                {
                    hasMeaningfulPendingPath = remaining > (_navAgent.stoppingDistance + 0.08f);
                }
            }
        }

        bool wantsWalk = isActuallyMoving || (shouldMove && hasMeaningfulPendingPath);

        if (!shouldMove && flatDistance <= stopDistanceForWalk)
        {
            wantsWalk = false;
        }

        if (wantsWalk)
        {
            _walkHoldUntil = Time.time + Mathf.Max(0f, walkStateHoldSeconds);
            return true;
        }

        return Time.time < _walkHoldUntil;
    }

    private bool IsActuallyMoving()
    {
        float velocityThreshold = Mathf.Max(0.001f, minWalkVelocity);

        if (_navAgent != null && _navAgent.enabled && _navAgent.isOnNavMesh)
        {
            if (_navAgent.velocity.sqrMagnitude > velocityThreshold * velocityThreshold)
            {
                return true;
            }

            if (_navAgent.pathPending)
            {
                return true;
            }

            if (_navAgent.hasPath && _navAgent.remainingDistance > Mathf.Max(_navAgent.stoppingDistance + 0.05f, preferredDistanceFromPlayer))
            {
                return true;
            }
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float displacementSpeed = Vector3.Distance(transform.position, _previousPosition) / dt;
        return displacementSpeed > velocityThreshold;
    }

    public void SetPlayerTarget(Transform target)
    {
        playerTarget = target;
        _playerFollowRoot = target != null ? target.root : null;
    }

    private bool MoveTowardPreferredDistance(Vector3 toPlayerFlat, float flatDistance, Vector3 followPosition)
    {
        if (flatDistance < 0.001f)
        {
            return false;
        }

        Vector3 toPlayerDir = toPlayerFlat / flatDistance;
        float stopDistance = Mathf.Clamp(preferredDistanceFromPlayer, minDistanceFromPlayer, maxDistanceFromPlayer);
        Vector3 desiredPosition = followPosition - toPlayerDir * stopDistance;

        if (CanUseNavMeshAgentForFollow(flatDistance))
        {
            _navAgent.speed = Mathf.Max(0.01f, moveSpeed);
            _navAgent.stoppingDistance = 0.05f;
            Vector3 navDestination = desiredPosition;
            if (useSmartNavDestination)
            {
                navDestination = SelectBestNavDestination(followPosition, stopDistance, toPlayerDir);
            }

            bool shouldRepath = Time.time >= _nextNavRepathAt
                || (_lastNavDestination - navDestination).sqrMagnitude > navDestinationUpdateDistance * navDestinationUpdateDistance
                || !_navAgent.hasPath
                || (_navAgent.hasPath && !_navAgent.pathPending && _navAgent.pathStatus != NavMeshPathStatus.PathComplete);

            if (shouldRepath)
            {
                _navAgent.SetDestination(navDestination);
                _lastNavDestination = navDestination;
                _nextNavRepathAt = Time.time + Mathf.Max(0.05f, navRepathIntervalSeconds);
            }

            RotateToward(toPlayerDir);
            return true;
        }

        if (_navAgent != null && _navAgent.enabled && _navAgent.hasPath)
        {
            _navAgent.ResetPath();
        }

        float moveAmount = Mathf.Max(0f, flatDistance - stopDistance);
        float step = Mathf.Min(moveAmount, moveSpeed * Time.deltaTime);
        Vector3 delta = toPlayerDir * step;

        if (TryApplyDirectMove(delta))
        {
            RotateToward(toPlayerDir);
            return false;
        }

        if (verboseDebugLogs)
        {
            Debug.Log("[AvatarCompanionFollow] Movement blocked by obstacle.");
        }

        return false;
    }

    private bool TryApplyDirectMove(Vector3 delta)
    {
        if (delta.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        Vector3 flatDelta = new Vector3(delta.x, 0f, delta.z);
        if (flatDelta.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        float step = flatDelta.magnitude;
        Vector3 forward = flatDelta / step;

        for (int i = 0; i < _sidestepDirections.Length; i++)
        {
            Vector3 candidate = i == 0
                ? forward * step
                : (forward + _sidestepDirections[i] * 0.65f).normalized * step;

            if (!CanApplyDirectMove(candidate))
            {
                continue;
            }

            ApplyDirectMove(candidate);
            return true;
        }

        return false;
    }

    private bool CanApplyDirectMove(Vector3 delta)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return delta.sqrMagnitude > 0.000001f;
#else
        return !IsMovementBlocked(delta);
#endif
    }

    private void ApplyDirectMove(Vector3 delta)
    {
        if (useCharacterControllerIfAvailable && _characterController != null && _characterController.enabled)
        {
            _characterController.Move(delta);
            return;
        }

        Vector3 nextPosition = transform.position + delta;

        if (lockToInitialHeight && !snapToGround)
        {
            nextPosition.y = _lockedY;
        }

        transform.position = nextPosition;
    }

    private Vector3 SelectBestNavDestination(Vector3 playerPosition, float stopDistance, Vector3 toPlayerDir)
    {
        Vector3 fallback = playerPosition - toPlayerDir * stopDistance;
        Vector3 fromPlayerToAvatar = transform.position - playerPosition;
        fromPlayerToAvatar.y = 0f;
        if (fromPlayerToAvatar.sqrMagnitude < 0.001f)
        {
            fromPlayerToAvatar = -toPlayerDir;
        }

        fromPlayerToAvatar.Normalize();

        float bestScore = float.MaxValue;
        Vector3 bestPoint = fallback;
        bool found = false;

        for (int i = 0; i < CandidateFollowAngles.Length; i++)
        {
            float angle = CandidateFollowAngles[i];
            Vector3 radial = Quaternion.Euler(0f, angle, 0f) * fromPlayerToAvatar;
            Vector3 candidate = playerPosition + radial * stopDistance;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, Mathf.Max(0.1f, navSampleRadius), NavMesh.AllAreas))
            {
                continue;
            }

            if (!_navAgent.CalculatePath(hit.position, _navPath) || _navPath.status != NavMeshPathStatus.PathComplete)
            {
                continue;
            }

            float score = GetPathLength(_navPath) + Vector3.Distance(transform.position, hit.position) * 0.35f;
            if (score < bestScore)
            {
                bestScore = score;
                bestPoint = hit.position;
                found = true;
            }
        }

        if (found)
        {
            return bestPoint;
        }

        if (NavMesh.SamplePosition(fallback, out NavMeshHit fallbackHit, Mathf.Max(0.1f, navSampleRadius), NavMesh.AllAreas))
        {
            return fallbackHit.position;
        }

        return fallback;
    }

    private static float GetPathLength(NavMeshPath path)
    {
        if (path == null || path.corners == null || path.corners.Length < 2)
        {
            return 0f;
        }

        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++)
        {
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }

        return length;
    }

    private bool IsMovementBlocked(Vector3 delta)
    {
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return false;
        }

        float radius = colliderRadius;
        float height = colliderHeight;
        Vector3 centerWorld = transform.TransformPoint(new Vector3(0f, colliderCenterY, 0f));

        if (_characterController != null)
        {
            radius = _characterController.radius;
            height = _characterController.height;
            centerWorld = transform.TransformPoint(_characterController.center);
        }

        float halfHeight = Mathf.Max(radius, height * 0.5f - radius);
        Vector3 up = transform.up;
        Vector3 point1 = centerWorld + up * halfHeight;
        Vector3 point2 = centerWorld - up * halfHeight;
        Vector3 direction = delta / distance;
        float castDistance = distance + Mathf.Max(0f, blockerProbePadding);

        int hitCount = Physics.CapsuleCastNonAlloc(
            point1,
            point2,
            Mathf.Max(0.01f, radius),
            direction,
            _blockerHitBuffer,
            castDistance,
            movementBlockerLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = _blockerHitBuffer[i].collider;
            if (hitCollider == null || IsSelfCollider(hitCollider) || IsPlayerCollider(hitCollider))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /* -------------------------------------------------------------------------
     * GROUND SNAPPING
     * Casts a ray straight down from above the avatar to find the terrain or
     * ground mesh surface.  The avatar's Y is then smoothly lerped toward that
     * surface so it follows hills and valleys naturally.
     *
     * Why not just use NavMeshAgent?  The Nature terrain may not have a baked
     * NavMesh.  In that case NavMeshAgent falls back to disabled, and the avatar
     * needs an independent way to stay on the ground.
     * ------------------------------------------------------------------------- */

    private void SnapToGround()
    {
        ApplyGroundSnapSmooth(instant: true);
    }

    private void ApplyGroundSnapSmooth(bool instant = false)
    {
        if (_navAgent != null && _navAgent.enabled && _navAgent.isOnNavMesh && !conversationStandMode)
        {
            return;
        }

        if (!TrySampleGroundY(out float groundY))
        {
            return;
        }

        Vector3 pos = transform.position;
        float currentY = pos.y;
        float delta = groundY - currentY;

        if (!instant && Mathf.Abs(delta) < groundSnapDeadband)
        {
            return;
        }

        if (instant || groundSnapSmoothSpeed <= 0f)
        {
            pos.y = groundY;
        }
        else if (conversationStandMode)
        {
            pos.y = Mathf.SmoothDamp(currentY, groundY, ref _groundYVelocity, conversationGroundSmoothTime);
        }
        else
        {
            pos.y = Mathf.Lerp(currentY, groundY, groundSnapSmoothSpeed * Time.deltaTime);
        }

        transform.position = pos;
        _lockedY = pos.y;
    }

    private bool TrySampleGroundY(out float groundY)
    {
        groundY = transform.position.y;

        Vector3 rayOrigin = transform.position + Vector3.up * groundRaycastOriginOffset;
        float totalDistance = groundRaycastOriginOffset + Mathf.Max(0f, groundRaycastMaxDistance);

        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            totalDistance,
            groundSnapLayers,
            QueryTriggerInteraction.Ignore);

        if (hits.Length == 0)
        {
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider != null && hits[i].collider.transform.IsChildOf(transform))
            {
                continue;
            }

            groundY = hits[i].point.y + groundSnapVerticalOffset;
            return true;
        }

        return false;
    }

    /// <summary>Re-baseline follow/ground state after the avatar is teleported to a new zone.</summary>
    public void RepositionAfterTeleport(bool skipNavMeshReenable = false)
    {
        ApplyQuestDirectFollowProfileIfNeeded();
        _lockedY = transform.position.y;
        _previousPosition = transform.position;
        _nextNavRepathAt = 0f;
        _lastNavDestination = transform.position;
        _followFailureTimer = 0f;

        if (_navAgent != null)
        {
            _navAgent.enabled = false;

#if !(UNITY_ANDROID && !UNITY_EDITOR)
            if (!skipNavMeshReenable && useNavMeshAgentIfAvailable && !conversationStandMode)
            {
                float probeRadius = Mathf.Max(2.5f, navSampleRadius);
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit navProbeHit, probeRadius, NavMesh.AllAreas))
                {
                    _navAgent.enabled = true;
                    _navAgent.Warp(navProbeHit.position);
                }
            }
#endif
        }

        _groundYVelocity = 0f;

        if (snapToGround)
        {
            ApplyGroundSnapSmooth(instant: true);
        }
    }

    private void StopAgentMovement()
    {
        if (_navAgent == null || !_navAgent.enabled || !_navAgent.isOnNavMesh)
        {
            return;
        }

        if (_navAgent.hasPath)
        {
            _navAgent.ResetPath();
        }
    }

    private void RefreshPlayerTargetIfNeeded()
    {
        if (Time.time < _nextPlayerTargetRefreshAt)
        {
            return;
        }

        _nextPlayerTargetRefreshAt = Time.time + Mathf.Max(0.1f, autoFindTargetIntervalSeconds);

        if (_cachedXrOrigin == null)
        {
            _cachedXrOrigin = FindObjectOfType<XROrigin>();
        }

        if (_cachedXrOrigin != null && _cachedXrOrigin.Camera != null)
        {
            playerTarget = _cachedXrOrigin.Camera.transform;
            _playerFollowRoot = _cachedXrOrigin.transform;
            return;
        }

        if (!autoFindPlayerTarget)
        {
            return;
        }

        if (playerTarget != null && playerTarget.gameObject.activeInHierarchy)
        {
            if (_playerFollowRoot == null)
            {
                _playerFollowRoot = playerTarget.root;
            }
            return;
        }

        if (Camera.main != null)
        {
            playerTarget = Camera.main.transform;
            _playerFollowRoot = playerTarget.root;
            return;
        }

        Camera anyCamera = FindObjectOfType<Camera>(true);
        if (anyCamera != null)
        {
            playerTarget = anyCamera.transform;
            _playerFollowRoot = playerTarget.root;
        }
    }

    private bool CanUseNavMeshAgentForFollow(float flatDistance)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return false;
#else
        if (!useNavMeshAgentIfAvailable || _navAgent == null || !_navAgent.enabled)
        {
            return false;
        }

        if (!_navAgent.isOnNavMesh)
        {
            TryRecoverNavMeshAgent();
            return _navAgent.isOnNavMesh && _navStuckTimer < 0.6f;
        }

        if (_navStuckTimer >= 0.6f && flatDistance > maxDistanceFromPlayer)
        {
            return false;
        }

        if (_navAgent.hasPath
            && !_navAgent.pathPending
            && _navAgent.pathStatus != NavMeshPathStatus.PathComplete
            && _navAgent.velocity.sqrMagnitude < 0.001f)
        {
            return false;
        }

        return true;
#endif
    }

    private void UpdateNavStuckTracking(bool usedNavMesh, bool shouldMove, float flatDistance, float startDistance)
    {
        if (!shouldMove || !usedNavMesh)
        {
            _navStuckTimer = 0f;
            _navStuckCheckPosition = transform.position;
            return;
        }

        Vector3 currentFlat = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 previousFlat = new Vector3(_navStuckCheckPosition.x, 0f, _navStuckCheckPosition.z);
        float moved = Vector3.Distance(currentFlat, previousFlat);
        bool agentMoving = _navAgent != null && _navAgent.velocity.sqrMagnitude > 0.0004f;

        if (moved > 0.03f || agentMoving)
        {
            _navStuckTimer = 0f;
            _navStuckCheckPosition = transform.position;
            return;
        }

        if (flatDistance > startDistance)
        {
            _navStuckTimer += Time.deltaTime;
            if (_navStuckTimer >= 0.6f)
            {
                TryRecoverNavMeshAgent(force: true);
            }
        }
    }

    private void TryRecoverNavMeshAgent(bool force = false)
    {
        if (_navAgent == null || (!force && Time.time < _nextNavRecoveryAt))
        {
            return;
        }

        _nextNavRecoveryAt = Time.time + 1.5f;

        float probeRadius = Mathf.Max(2.5f, navSampleRadius);
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, probeRadius, NavMesh.AllAreas))
        {
            return;
        }

        if (!_navAgent.enabled)
        {
            _navAgent.enabled = true;
        }

        if (_navAgent.hasPath)
        {
            _navAgent.ResetPath();
        }

        _navAgent.Warp(hit.position);
        _navStuckTimer = 0f;
        _nextNavRepathAt = 0f;
    }

    private void ApplyQuestDirectFollowProfileIfNeeded()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_questDirectFollowProfileApplied)
        {
            return;
        }

        _questDirectFollowProfileApplied = true;
        useNavMeshAgentIfAvailable = false;
        pauseWhileAvatarIsSpeaking = false;

        if (_navAgent != null)
        {
            _navAgent.enabled = false;
        }
#endif
    }

    private Vector3 GetPlayerFollowPosition()
    {
        if (_cachedXrOrigin == null)
        {
            _cachedXrOrigin = FindObjectOfType<XROrigin>();
        }

        if (_cachedXrOrigin != null)
        {
            return _cachedXrOrigin.transform.position;
        }

        if (playerTarget != null)
        {
            return playerTarget.position;
        }

        return transform.position;
    }

    private void UpdateDirectFollowFailureTracking(bool shouldMove, float flatDistance, float followStartDistance)
    {
        if (!shouldMove || flatDistance <= followStartDistance)
        {
            _followFailureTimer = 0f;
            return;
        }

        float moved = Vector3.Distance(
            new Vector3(transform.position.x, 0f, transform.position.z),
            new Vector3(_previousPosition.x, 0f, _previousPosition.z));

        if (moved > 0.02f)
        {
            _followFailureTimer = 0f;
            return;
        }

        _followFailureTimer += Time.deltaTime;
        if (_followFailureTimer < 1.25f)
        {
            return;
        }

        Vector3 followPosition = GetPlayerFollowPosition();
        Vector3 toPlayerFlat = followPosition - transform.position;
        toPlayerFlat.y = 0f;
        if (toPlayerFlat.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float stopDistance = Mathf.Clamp(preferredDistanceFromPlayer, minDistanceFromPlayer, maxDistanceFromPlayer);
        Vector3 catchUpPosition = followPosition - toPlayerFlat.normalized * stopDistance;
        catchUpPosition.y = transform.position.y;

        if (_navAgent != null && _navAgent.enabled)
        {
            _navAgent.ResetPath();
            _navAgent.enabled = false;
        }

        transform.position = catchUpPosition;
        _followFailureTimer = 0f;
        _navStuckTimer = 0f;
        _previousPosition = transform.position;

        if (snapToGround)
        {
            ApplyGroundSnapSmooth(instant: true);
        }
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

    private bool IsPlayerCollider(Collider candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        Transform t = candidate.transform;

        if (_playerFollowRoot != null)
        {
            return t == _playerFollowRoot || t.IsChildOf(_playerFollowRoot);
        }

        if (playerTarget != null)
        {
            return t == playerTarget || t.IsChildOf(playerTarget);
        }

        return false;
    }

    private void EnsureRootCollision()
    {
        if (!ensureRootCapsuleCollider)
        {
            return;
        }

        Collider existingCollider = GetComponent<Collider>();
        if (existingCollider != null)
        {
            existingCollider.isTrigger = false;
            return;
        }

        if (_characterController != null)
        {
            _characterController.height = Mathf.Max(0.2f, colliderHeight);
            _characterController.radius = Mathf.Max(0.05f, colliderRadius);
            _characterController.center = new Vector3(0f, colliderCenterY, 0f);
            return;
        }

        CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.height = Mathf.Max(0.2f, colliderHeight);
        capsule.radius = Mathf.Max(0.05f, colliderRadius);
        capsule.center = new Vector3(0f, colliderCenterY, 0f);
        capsule.isTrigger = false;

        if (verboseDebugLogs)
        {
            Debug.Log("[AvatarCompanionFollow] Added root CapsuleCollider for collision.");
        }
    }

    private void RotateToward(Vector3 lookDirection)
    {
        RotateToward(lookDirection, turnSpeedDegrees);
    }

    private void RotateToward(Vector3 lookDirection, float speedDegrees)
    {
        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            speedDegrees * Time.deltaTime);
    }

    private bool IsAvatarSpeaking()
    {
        if (lipSyncBlendShape != null)
        {
            AudioSource source = lipSyncBlendShape.GetComponent<AudioSource>();
            if (source != null && source.clip != null && source.isPlaying)
            {
                return true;
            }
        }

        AudioSource ownSource = GetComponent<AudioSource>();
        return ownSource != null && ownSource.clip != null && ownSource.isPlaying;
    }

    private void CacheWalkParam()
    {
        if (_walkParamCached || avatarAnimator == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = avatarAnimator.parameters;

        if (TryFindWalkParam(parameters, walkBoolParam, out string resolvedPrimary))
        {
            _hasWalkParam = true;
            _resolvedWalkParam = resolvedPrimary;
            _walkParamCached = true;
            return;
        }

        if (walkBoolFallbackParams != null)
        {
            for (int i = 0; i < walkBoolFallbackParams.Length; i++)
            {
                string candidate = walkBoolFallbackParams[i];
                if (TryFindWalkParam(parameters, candidate, out string resolvedFallback))
                {
                    _hasWalkParam = true;
                    _resolvedWalkParam = resolvedFallback;
                    break;
                }
            }
        }

        if (!_hasWalkParam && logWalkAnimationWarnings)
        {
            Debug.LogWarning("[AvatarCompanionFollow] Walk animation param not found. Add a bool named '" + walkBoolParam + "' in Animator, or configure walkBoolFallbackParams.");
        }

        _walkParamCached = true;
    }

    private void CacheWalkTriggerParam()
    {
        if (_walkTriggerParamCached || avatarAnimator == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = avatarAnimator.parameters;

        if (TryFindTriggerParam(parameters, walkTriggerParam, out string resolvedPrimary))
        {
            _hasWalkTriggerParam = true;
            _resolvedWalkTriggerParam = resolvedPrimary;
            _walkTriggerParamCached = true;
            return;
        }

        if (walkTriggerFallbackParams != null)
        {
            for (int i = 0; i < walkTriggerFallbackParams.Length; i++)
            {
                string candidate = walkTriggerFallbackParams[i];
                if (TryFindTriggerParam(parameters, candidate, out string resolvedFallback))
                {
                    _hasWalkTriggerParam = true;
                    _resolvedWalkTriggerParam = resolvedFallback;
                    break;
                }
            }
        }

        _walkTriggerParamCached = true;
    }

    private static bool TryFindWalkParam(AnimatorControllerParameter[] parameters, string name, out string resolvedName)
    {
        resolvedName = string.Empty;
        if (string.IsNullOrWhiteSpace(name) || parameters == null)
        {
            return false;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == name)
            {
                resolvedName = parameter.name;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindTriggerParam(AnimatorControllerParameter[] parameters, string name, out string resolvedName)
    {
        resolvedName = string.Empty;
        if (string.IsNullOrWhiteSpace(name) || parameters == null)
        {
            return false;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == name)
            {
                resolvedName = parameter.name;
                return true;
            }
        }

        return false;
    }

    private void ApplyWalkAnimation(bool shouldWalk)
    {
        if (!driveWalkAnimation || avatarAnimator == null)
        {
            _wasWalking = shouldWalk;
            return;
        }

        CacheWalkParam();
        if (_hasWalkParam)
        {
            avatarAnimator.SetBool(_resolvedWalkParam, shouldWalk);
        }

        if (triggerWalkOnFollowStart)
        {
            CacheWalkTriggerParam();
            if (_hasWalkTriggerParam && shouldWalk && !_wasWalking)
            {
                avatarAnimator.SetTrigger(_resolvedWalkTriggerParam);
            }
        }

        _wasWalking = shouldWalk;
    }
}
