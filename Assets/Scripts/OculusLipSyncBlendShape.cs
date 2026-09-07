using UnityEngine;

public class OculusLipSyncBlendShape : MonoBehaviour
{
    public SkinnedMeshRenderer skinnedMeshRenderer; // Assign in Inspector
    public int[] visemeBlendShapeIndices; // Map viseme indices to blend shape indices
    public OVRLipSyncContext lipSyncContext; // Assign in Inspector
    public Animator avatarAnimator; // Assign in Inspector if you want animation sync
    [Tooltip("Volume for streamed agent TTS. ElevenLabs chunks are normalized; 1.0 is usually clearest on Quest speakers.")]
    [SerializeField] [Range(0f, 1f)] private float playbackVolume = 1f;
    [SerializeField] private float talkReleaseGraceSeconds = 0.14f;

    private bool _animatorParamsCached;
    private bool _hasTalkParam;
    private bool _hasIdleParam;
    private float _talkHoldUntil;

    // PlayLipSync: Accepts an AudioClip and plays it for lip sync.
    // This works for ElevenLabs audio clips too.
    public void PlayLipSync(AudioClip ttsClip)
    {
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = ttsClip;
        audioSource.volume = playbackVolume;
        audioSource.spatialBlend = 0f;
        audioSource.dopplerLevel = 0f;
        audioSource.playOnAwake = false;
        audioSource.Play();
        // OVRLipSyncContext should be set to use this AudioSource for viseme extraction
        if (lipSyncContext != null)
        {
            lipSyncContext.audioSource = audioSource;
        }
        // No manual animation trigger here; Update() will handle it
    }

    public void PlayElevenLabsLipSync(AudioClip agentClip)
    {
        PlayLipSync(agentClip);
    }

    void Start()
    {
        if (lipSyncContext == null)
        {
            lipSyncContext = GetComponent<OVRLipSyncContext>();
        }
        if (lipSyncContext != null)
        {
            lipSyncContext.audioLoopback = true;
        }

        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.Stop();
            audioSource.clip = null;
        }

        if (avatarAnimator != null)
        {
            CacheAnimatorParameters();
            ApplyAnimatorTalkState(false);
        }
    }

    void Update()
    {
        AudioSource audioSource = GetComponent<AudioSource>();
        bool isTalkingRaw = audioSource != null && audioSource.isPlaying;
        if (isTalkingRaw)
        {
            _talkHoldUntil = Time.unscaledTime + Mathf.Max(0f, talkReleaseGraceSeconds);
        }

        bool isTalkingSmoothed = isTalkingRaw || Time.unscaledTime < _talkHoldUntil;
        ApplyAnimatorTalkState(isTalkingSmoothed);

        if (lipSyncContext == null || skinnedMeshRenderer == null || visemeBlendShapeIndices == null)
            return;

        // Get current viseme frame from Oculus LipSync
        OVRLipSync.Frame frame = lipSyncContext.GetCurrentPhonemeFrame();
        if (frame == null || frame.Visemes == null)
            return;

        // Set blend shape weights for each viseme
        for (int i = 0; i < visemeBlendShapeIndices.Length && i < frame.Visemes.Length; i++)
        {
            int blendShapeIndex = visemeBlendShapeIndices[i];
            float weight = frame.Visemes[i] * 0.7f; // Oculus LipSync outputs 0-1, Unity expects 0-100
            if (blendShapeIndex >= 0 && blendShapeIndex < skinnedMeshRenderer.sharedMesh.blendShapeCount)
            {
                skinnedMeshRenderer.SetBlendShapeWeight(blendShapeIndex, weight);
            }
        }

    }

    private void CacheAnimatorParameters()
    {
        if (_animatorParamsCached || avatarAnimator == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = avatarAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type != AnimatorControllerParameterType.Bool)
            {
                continue;
            }

            if (parameter.name == "Talk")
            {
                _hasTalkParam = true;
            }
            else if (parameter.name == "isIdle")
            {
                _hasIdleParam = true;
            }
        }

        _animatorParamsCached = true;
    }

    private void ApplyAnimatorTalkState(bool isTalking)
    {
        if (avatarAnimator == null)
        {
            return;
        }

        CacheAnimatorParameters();

        if (_hasTalkParam)
        {
            avatarAnimator.SetBool("Talk", isTalking);
        }

        if (_hasIdleParam)
        {
            avatarAnimator.SetBool("isIdle", !isTalking);
        }
    }
}
