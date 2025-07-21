using UnityEngine;
using System;

public class AvatarAIController : MonoBehaviour
{
    public STTHandler sttHandler;
    public LLMHandler llmHandler;
    public CartesiaTTSHandler ttsHandler;
    public OculusLipSyncBlendShape lipSyncBlendShape;

    public Animator avatarAnimator; // Assign in Inspector

    // VAD state
    private bool isRecording = false;
    private float silenceTimer = 0f;
    private float silenceThreshold = 0.05f; // Less sensitive
    private float silenceDuration = 2.0f;   // Stop after 2 seconds of silence
    private float vadWarmupTime = 1.0f;     // Ignore VAD for first second
    private float recordingTime = 0f;
    private float pipelineStartTime = 0f;

    private void Start()
    {
        if (ttsHandler != null)
        {
            ttsHandler.OnTTSReady += OnGreetingPlayback;
            ttsHandler.SynthesizeAndPlay("Hello! How are you today? Tell me about your day.");
        }
    }

    // Handle greeting playback, then start conversation pipeline
    private void OnGreetingPlayback(AudioClip clip)
    {
        if (lipSyncBlendShape != null)
            lipSyncBlendShape.PlayLipSync(clip);
        if (ttsHandler != null)
            ttsHandler.OnTTSReady -= OnGreetingPlayback;
        // Wait for greeting audio to finish, then start listening automatically
        StartCoroutine(StartListeningAfterGreeting(clip.length));
    }

    private System.Collections.IEnumerator StartListeningAfterGreeting(float delay)
    {
        yield return new WaitForSeconds(delay);
        StartConversation();
    }

    // Call this to start the full pipeline
    public void StartConversation()
    {
        pipelineStartTime = Time.realtimeSinceStartup;
        Debug.Log($"[LATENCY] Pipeline started at {pipelineStartTime:F2}s");
        if (sttHandler != null)
            sttHandler.OnTranscriptReady += OnSTTTranscript;
        if (llmHandler != null)
            llmHandler.OnReplyReady += OnLLMReply;
        if (ttsHandler != null)
            ttsHandler.OnTTSReady += OnTTSPlayback;
        if (sttHandler != null)
            sttHandler.StartRecording();
        isRecording = true;
        silenceTimer = 0f;
        recordingTime = 0f;
    }

    private void OnSTTTranscript(string transcript)
    {
        float sttTime = Time.realtimeSinceStartup - pipelineStartTime;
        Debug.Log($"[LATENCY] STT transcript received after {sttTime:F2}s");
        isRecording = false;
        if (llmHandler != null)
            llmHandler.GenerateContent(transcript);
        if (sttHandler != null)
            sttHandler.OnTranscriptReady -= OnSTTTranscript;
    }

    private void OnLLMReply(string reply)
    {
        float llmTime = Time.realtimeSinceStartup - pipelineStartTime;
        Debug.Log($"[LATENCY] LLM reply received after {llmTime:F2}s");
        if (ttsHandler != null)
            ttsHandler.SynthesizeAndPlay(reply);
        if (llmHandler != null)
            llmHandler.OnReplyReady -= OnLLMReply;
    }

    private void OnTTSPlayback(AudioClip clip)
    {
        float ttsTime = Time.realtimeSinceStartup - pipelineStartTime;
        Debug.Log($"[LATENCY] TTS playback started after {ttsTime:F2}s");
        // Switch to talking animation
        if (avatarAnimator != null)
        {
            avatarAnimator.SetBool("isIdle", false);
            avatarAnimator.SetBool("Talk", true);
        }
        if (lipSyncBlendShape != null)
            lipSyncBlendShape.PlayLipSync(clip);
        // Schedule transition back to idle after audio finishes
        StartCoroutine(TransitionToIdleAfterClip(clip));
        if (ttsHandler != null)
            ttsHandler.OnTTSReady -= OnTTSPlayback;
    }

    void Update()
    {
        if (isRecording && sttHandler != null && sttHandler.recordedClip != null)
        {
            recordingTime += Time.deltaTime;
            int micPos = Microphone.GetPosition(null);
            int sampleCount = 128;
            if (micPos < sampleCount || sttHandler.recordedClip.samples < sampleCount) return; // Not enough data yet
            float[] samples = new float[sampleCount];
            int startPos = Mathf.Max(0, micPos - sampleCount);
            sttHandler.recordedClip.GetData(samples, startPos);
            float maxVolume = 0f;
            foreach (float sample in samples)
                maxVolume = Mathf.Max(maxVolume, Mathf.Abs(sample));

            Debug.Log($"[VAD] Time: {recordingTime:F2}s, MaxVolume: {maxVolume:F4}, SilenceTimer: {silenceTimer:F2}");

            // Ignore VAD for first second
            if (recordingTime < vadWarmupTime)
                return;

            if (maxVolume < silenceThreshold)
                silenceTimer += Time.deltaTime;
            else
                silenceTimer = 0f;

            if (silenceTimer > silenceDuration)
            {
                isRecording = false;
                recordingTime = 0f;
                if (sttHandler != null)
                    sttHandler.StopAndTranscribeWithDeepgram();
            }
        }
    }

    private System.Collections.IEnumerator TransitionToIdleAfterClip(AudioClip clip)
    {
        yield return new WaitForSeconds(clip.length);
        if (avatarAnimator != null)
        {
            avatarAnimator.SetBool("isIdle", true);
            avatarAnimator.SetBool("Talk", false);
        }
        // Start listening again for next user input
        StartConversation();
    }
}
