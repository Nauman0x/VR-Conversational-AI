using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Toggles between main and Nature zones in one scene. Positions player and avatar separately,
/// aligns eye height once per teleport, and enables terrain follow only in the forest.
/// </summary>
public class EnvironmentTeleporter : MonoBehaviour
{
    private static readonly Vector3 FallbackNaturePlayerPosition = new Vector3(-265.2f, 4f, 68.8f);
    private static readonly Vector3 FallbackMainPlayerPosition = new Vector3(5.76f, 2.5f, -15.45f);
    private static readonly Vector3 FallbackMainAvatarPosition = new Vector3(3.69f, 2.69f, -14.95f);

    [Header("Scene References")]
    [SerializeField] private Transform avatarRoot;
    [SerializeField] private Transform xrOrigin;

    [Header("Spawn Points")]
    [SerializeField] private Transform mainPlayerSpawn;
    [SerializeField] private Transform mainAvatarSpawn;
    [SerializeField] private Transform naturePlayerSpawn;
    [SerializeField] private Transform natureAvatarSpawn;

    [Header("Conversation Layout")]
    [SerializeField] private float conversationAvatarDistance = 1.75f;
    [SerializeField] private float avatarFeetGroundPadding = 0.02f;

    [Header("Eye-to-eye (legacy, retained for inspector compatibility)")]
    [SerializeField, HideInInspector] private bool calibrateOnStart;
    [SerializeField, HideInInspector] private Transform avatarEyeAnchor;
    [SerializeField, HideInInspector] private float avatarEyeHeightFromRoot = 1.63f;
    [SerializeField, HideInInspector] private float eyeToEyeFineTuneOffset;
    [SerializeField, HideInInspector] private int calibrationFrameDelay = 6;
    [SerializeField, HideInInspector] private bool alignEyeToEye;
    [FormerlySerializedAs("alignAvatarEyesToPlayer")]
    [SerializeField, HideInInspector] private bool alignEyeToEyeLegacy;

    [Header("Forest Behavior")]
    [SerializeField] private bool useConversationStandModeInNature;

    [Header("Rig / Locomotion")]
    [SerializeField] private FixedVrOriginWorldHeight fixedOriginHeight;
    [SerializeField] private VRLocomotionController locomotionController;
    [SerializeField] private AvatarCompanionFollow avatarCompanionFollow;

    [Header("Ground Sampling")]
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float groundRayStartHeight = 48f;
    [SerializeField] private float groundRayMaxDistance = 120f;
    [SerializeField, HideInInspector] private float rigHeightAboveGround;

    [Header("Nature Ambient Audio")]
    [SerializeField] private AudioSource natureAmbientSource;

    private Vector3 _originalXROriginPosition;
    private Vector3 _originalAvatarPosition;
    private bool _originSaved;
    private bool _isInNature;
    private Coroutine _finalizeRoutine;
    private Transform _cachedPlayerEye;

    public bool IsInNature => _isInNature;

    private void Awake()
    {
        AutoBindReferences();
        StopNatureAudio();
    }

    private void Start()
    {
        // Make sure gravity is active in the main (office) environment from the very first frame.
        if (fixedOriginHeight != null)
        {
            fixedOriginHeight.SetGroundFollow(FixedVrOriginWorldHeight.GroundFollowMode.Flat, groundLayers);
        }
    }

    private void OnDestroy()
    {
        StopNatureAudio();
    }

    public void ToggleEnvironment()
    {
        if (_isInNature)
        {
            TeleportToMain();
        }
        else
        {
            TeleportToNature();
        }
    }

    public void QuitApplication()
    {
        Debug.Log("[EnvironmentTeleporter] Quitting application.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void TeleportToNature()
    {
        SaveOriginStateIfNeeded();

        Transform playerSpawn = naturePlayerSpawn;
        Vector3 playerPos = ResolvePlayerPosition(playerSpawn, FallbackNaturePlayerPosition);
        Quaternion playerFacing = playerSpawn != null ? playerSpawn.rotation : Quaternion.identity;

        // Snap rig Y to terrain so we don't drop in from above and trigger huge gravity catch-up.
        float rigGroundY = SampleGroundY(playerPos.x, playerPos.z, playerPos.y, xrOrigin);
        Vector3 rigPos = new Vector3(playerPos.x, rigGroundY, playerPos.z);

        Vector3 avatarPos = ResolveAvatarPosition(
            natureAvatarSpawn,
            playerPos,
            playerFacing,
            FallbackNaturePlayerPosition + playerFacing * Vector3.forward * conversationAvatarDistance);

        float avatarGroundY = SampleGroundY(avatarPos.x, avatarPos.z, avatarPos.y, avatarRoot);
        avatarPos.y = avatarGroundY + avatarFeetGroundPadding;

        ApplyZoneTeleport(avatarPos, rigPos, true);
        _isInNature = true;
        StartNatureAudio();

        Debug.Log("[EnvironmentTeleporter] Nature teleport rig=" + rigPos + " avatar=" + avatarPos);
    }

    private void TeleportToMain()
    {
        Vector3 rigPos = _originSaved
            ? new Vector3(_originalXROriginPosition.x, _originalXROriginPosition.y, _originalXROriginPosition.z)
            : ResolvePlayerPosition(mainPlayerSpawn, FallbackMainPlayerPosition);

        Vector3 avatarPos = _originSaved
            ? _originalAvatarPosition
            : ResolveAvatarPosition(mainAvatarSpawn, rigPos, Quaternion.identity, FallbackMainAvatarPosition);

        if (!_originSaved)
        {
            float avatarGroundY = SampleGroundY(avatarPos.x, avatarPos.z, avatarPos.y, avatarRoot);
            avatarPos.y = avatarGroundY + avatarFeetGroundPadding;
        }

        ApplyZoneTeleport(avatarPos, rigPos, false);
        _isInNature = false;
        StopNatureAudio();

        Debug.Log("[EnvironmentTeleporter] Main teleport rig=" + rigPos + " avatar=" + avatarPos);
    }

    private void ApplyZoneTeleport(Vector3 avatarWorldPos, Vector3 rigWorldPos, bool isNature)
    {
        if (xrOrigin != null)
        {
            ForceSetPosition(xrOrigin, rigWorldPos, isNature ? naturePlayerSpawn : mainPlayerSpawn);
        }

        ForceSetPosition(avatarRoot, avatarWorldPos, isNature ? natureAvatarSpawn : mainAvatarSpawn);

        if (avatarCompanionFollow != null)
        {
            avatarCompanionFollow.SetConversationStandMode(isNature && useConversationStandModeInNature);
            if (isNature)
            {
                avatarCompanionFollow.PrepareForTerrainFollow();
            }

            avatarCompanionFollow.RepositionAfterTeleport(skipNavMeshReenable: isNature);
        }

        FaceAvatarTowardPlayer();
        ConfigureRigHeightForZone(isNature);
        ResetLocomotionState();

        if (_finalizeRoutine != null)
        {
            StopCoroutine(_finalizeRoutine);
        }

        _finalizeRoutine = StartCoroutine(FinalizeTeleportNextFrame(isNature));
    }

    private IEnumerator FinalizeTeleportNextFrame(bool isNature)
    {
        yield return null;

        FaceAvatarTowardPlayer();

        if (avatarCompanionFollow != null)
        {
            avatarCompanionFollow.RepositionAfterTeleport(skipNavMeshReenable: isNature);
        }

        ConfigureRigHeightForZone(isNature);
        ResetLocomotionState();
        _finalizeRoutine = null;
    }

    private void ConfigureRigHeightForZone(bool isNature)
    {
        if (fixedOriginHeight == null)
        {
            return;
        }

        fixedOriginHeight.SetGroundFollow(
            isNature
                ? FixedVrOriginWorldHeight.GroundFollowMode.Terrain
                : FixedVrOriginWorldHeight.GroundFollowMode.Flat,
            groundLayers);
    }

    private void FaceAvatarTowardPlayer()
    {
        if (avatarRoot == null)
        {
            return;
        }

        Transform playerEye = ResolvePlayerEyeTransform();
        if (playerEye == null)
        {
            return;
        }

        Vector3 toPlayer = playerEye.position - avatarRoot.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f)
        {
            return;
        }

        avatarRoot.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
    }

    private Transform ResolvePlayerEyeTransform()
    {
        if (_cachedPlayerEye != null)
        {
            return _cachedPlayerEye;
        }

        if (Camera.main != null)
        {
            _cachedPlayerEye = Camera.main.transform;
            return _cachedPlayerEye;
        }

        if (xrOrigin != null)
        {
            Camera cam = xrOrigin.GetComponentInChildren<Camera>(true);
            if (cam != null)
            {
                _cachedPlayerEye = cam.transform;
                return _cachedPlayerEye;
            }
        }

        return null;
    }

    private void SaveOriginStateIfNeeded()
    {
        if (_originSaved)
        {
            return;
        }

        _originSaved = true;

        if (xrOrigin != null)
        {
            _originalXROriginPosition = xrOrigin.position;
        }

        if (avatarRoot != null)
        {
            _originalAvatarPosition = avatarRoot.position;
        }
    }

    private static Vector3 ResolvePlayerPosition(Transform spawn, Vector3 fallback)
    {
        return spawn != null ? spawn.position : fallback;
    }

    private Vector3 ResolveAvatarPosition(Transform avatarSpawn, Vector3 playerPos, Quaternion playerFacing, Vector3 fallback)
    {
        if (avatarSpawn != null)
        {
            return avatarSpawn.position;
        }

        Vector3 forward = playerFacing * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }
        else
        {
            forward.Normalize();
        }

        if (fallback != FallbackMainAvatarPosition && fallback != FallbackNaturePlayerPosition)
        {
            return fallback;
        }

        return playerPos + forward * conversationAvatarDistance;
    }

    private static readonly RaycastHit[] _groundHitBuffer = new RaycastHit[16];

    private float SampleGroundY(float worldX, float worldZ, float fallbackY)
    {
        return SampleGroundY(worldX, worldZ, fallbackY, null);
    }

    private float SampleGroundY(float worldX, float worldZ, float fallbackY, Transform ignoreRoot)
    {
        Vector3 rayOrigin = new Vector3(worldX, fallbackY + groundRayStartHeight, worldZ);

        int hitCount = Physics.RaycastNonAlloc(
            rayOrigin,
            Vector3.down,
            _groundHitBuffer,
            groundRayMaxDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        if (hitCount == 0)
        {
            Debug.LogWarning("[EnvironmentTeleporter] Ground raycast missed at (" + worldX + ", " + worldZ + "). Using fallback Y=" + fallbackY);
            return fallbackY;
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

            if (ignoreRoot != null && hitCollider.transform.IsChildOf(ignoreRoot))
            {
                continue;
            }

            float candidateY = hit.point.y;
            if (!found || candidateY > bestY)
            {
                bestY = candidateY;
                found = true;
            }
        }

        if (!found)
        {
            Debug.LogWarning("[EnvironmentTeleporter] Ground raycast had only ignored hits at (" + worldX + ", " + worldZ + "). Using fallback Y=" + fallbackY);
            return fallbackY;
        }

        return bestY;
    }

    private void AutoBindReferences()
    {
        if (xrOrigin != null)
        {
            fixedOriginHeight ??= xrOrigin.GetComponent<FixedVrOriginWorldHeight>();
            locomotionController ??= xrOrigin.GetComponent<VRLocomotionController>();
        }

        if (avatarRoot != null)
        {
            avatarCompanionFollow ??= avatarRoot.GetComponent<AvatarCompanionFollow>();
        }
    }

    private void ResetLocomotionState()
    {
        locomotionController?.ResetAfterTeleport();
    }

    private void StartNatureAudio()
    {
        if (!natureAmbientSource || natureAmbientSource.isPlaying)
        {
            return;
        }

        natureAmbientSource.Play();
    }

    private void StopNatureAudio()
    {
        if (!natureAmbientSource)
        {
            return;
        }

        natureAmbientSource.Stop();
    }

    private static void ForceSetPosition(Transform target, Vector3 worldPosition, Transform optionalRotationSource)
    {
        if (target == null)
        {
            return;
        }

        CharacterController characterController = target.GetComponent<CharacterController>();
        if (characterController != null)
        {
            characterController.enabled = false;
        }

        UnityEngine.AI.NavMeshAgent navMeshAgent = target.GetComponent<UnityEngine.AI.NavMeshAgent>();
        bool hadNavAgent = navMeshAgent != null;
        if (navMeshAgent != null && navMeshAgent.enabled)
        {
            navMeshAgent.enabled = false;
        }

        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        target.position = worldPosition;

        if (optionalRotationSource != null)
        {
            target.rotation = optionalRotationSource.rotation;
        }

        if (hadNavAgent && navMeshAgent != null)
        {
            const float sampleRadius = 2.5f;
            if (UnityEngine.AI.NavMesh.SamplePosition(
                    worldPosition,
                    out UnityEngine.AI.NavMeshHit navHit,
                    sampleRadius,
                    UnityEngine.AI.NavMesh.AllAreas))
            {
                navMeshAgent.enabled = true;
                navMeshAgent.Warp(navHit.position);
            }
        }

        if (characterController != null)
        {
            characterController.enabled = true;
        }
    }
}
