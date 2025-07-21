using UnityEngine;

public class OculusLipSyncBlendShape : MonoBehaviour
{
    public SkinnedMeshRenderer skinnedMeshRenderer; // Assign in Inspector
    public int[] visemeBlendShapeIndices; // Map viseme indices to blend shape indices
    public OVRLipSyncContext lipSyncContext; // Assign in Inspector
    public Animator avatarAnimator; // Assign in Inspector if you want animation sync

    // PlayLipSync: Accepts an AudioClip from TTS and plays it for lip sync
    // This will play the audio and OVRLipSync will animate the blend shapes automatically
    public void PlayLipSync(AudioClip ttsClip)
    {
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = ttsClip;
        audioSource.volume = 1.0f; // Set to maximum volume
        audioSource.Play();
        // OVRLipSyncContext should be set to use this AudioSource for viseme extraction
        if (lipSyncContext != null)
        {
            lipSyncContext.audioSource = audioSource;
        }
        // No manual animation trigger here; Update() will handle it
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
    }

    void Update()
    {
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

        // Animation sync with audio playback
        if (avatarAnimator != null)
        {
            AudioSource audioSource = GetComponent<AudioSource>();
            bool isTalking = audioSource != null && audioSource.isPlaying;
            avatarAnimator.SetBool("Talk", isTalking);
        }
    }
}
