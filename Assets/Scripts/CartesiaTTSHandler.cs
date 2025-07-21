using UnityEngine;
using System.IO;
using UnityEngine.Networking;
using System.Text;

public class CartesiaTTSHandler : MonoBehaviour
{
    public event System.Action<AudioClip> OnTTSReady;
    [Header("Cartesia API Settings")]
    public string apiKey = "YOUR_API_KEY_HERE";
    public string voiceId = "b8b49b88-c1af-4647-b02d-b18db1b8ded0";
    public string modelId = "sonic-2";

    public void SynthesizeAndPlay(string text = null)
    {
        if (string.IsNullOrEmpty(text))
            text = "Hello, this is a test of Cartesia TTS.";
        Debug.Log($"[TTS] Using text: {text}");
        StartCoroutine(SynthesizeCoroutine(text));
    }

    public void SynthesizeResponse(string responseText)
    {
        SynthesizeAndPlay(responseText);
    }

    private System.Collections.IEnumerator SynthesizeCoroutine(string text)
    {
        Debug.Log($"[TTS] [Step 1] Synthesizing: {text}");
        string url = "https://api.cartesia.ai/tts/bytes";
        string wavPath = Path.Combine(Application.persistentDataPath, "cartesia_tts.wav");
        string jsonBody = $"{{\"transcript\":\"{EscapeJson(text)}\",\"model_id\":\"{modelId}\",\"voice\":{{\"mode\":\"id\",\"id\":\"{voiceId}\"}},\"output_format\":{{\"container\":\"wav\",\"encoding\":\"pcm_s16le\",\"sample_rate\":44100}},\"rate\":0.4,\"clarity\":1.0,\"volume\":1.0}}";
        Debug.Log($"[TTS] [Step 2] JSON body: {jsonBody}");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Cartesia-Version", "2025-04-16");
            www.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            www.SetRequestHeader("Content-Type", "application/json");
            Debug.Log("[TTS] [Step 3] Sending request to Cartesia API...");
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[TTS] [Step 4] Cartesia API Error: {www.error}");
                yield break;
            }
            Debug.Log("[TTS] [Step 4] Received audio bytes from Cartesia.");
            byte[] audioBytes = www.downloadHandler.data;
            Debug.Log($"[TTS] [Step 4.1] Audio byte length: {audioBytes.Length}");
            File.WriteAllBytes(wavPath, audioBytes);
            Debug.Log($"[TTS] [Step 5] WAV saved at: {wavPath}, bytes: {audioBytes.Length}");
            yield return StartCoroutine(PlayWav(wavPath));
        }
    }

    private System.Collections.IEnumerator PlayWav(string wavPath)
    {
        Debug.Log($"[TTS] [Step 6] Loading WAV for playback: {wavPath}");
        byte[] wavData = File.ReadAllBytes(wavPath);
        AudioClip clip = WavUtility.ToAudioClip(wavData, "CartesiaTTS");
        if (clip == null)
        {
            Debug.LogError("[TTS] Custom WAV loader failed to parse audio.");
            yield break;
        }
        OnTTSReady?.Invoke(clip);
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = clip;
        audioSource.Play();
        Debug.Log("[TTS] [Step 7] Playback started (custom loader).");
        yield break;
    }

    public static class WavUtility
    {
        public static AudioClip ToAudioClip(byte[] wavFile, string clipName)
        {
            int channels = System.BitConverter.ToInt16(wavFile, 22);
            int sampleRate = System.BitConverter.ToInt32(wavFile, 24);
            int byteRate = System.BitConverter.ToInt32(wavFile, 28);
            int bitsPerSample = System.BitConverter.ToInt16(wavFile, 34);
            Debug.Log($"[WavUtility] Header info: channels={channels}, sampleRate={sampleRate}, byteRate={byteRate}, bitsPerSample={bitsPerSample}");
            if (bitsPerSample != 16)
            {
                Debug.LogError($"[WavUtility] Only 16-bit PCM WAV supported. Found: {bitsPerSample}");
                return null;
            }
            int dataStartIndex = -1;
            int dataSize = -1;
            for (int i = 12; i < wavFile.Length - 8; )
            {
                string chunkId = Encoding.ASCII.GetString(wavFile, i, 4);
                uint chunkSize = System.BitConverter.ToUInt32(wavFile, i + 4);
                Debug.Log($"[WavUtility] Found chunk: '{chunkId}' at {i}, raw size bytes: [{wavFile[i+4]}, {wavFile[i+5]}, {wavFile[i+6]}, {wavFile[i+7]}], size={chunkSize}");
                if (chunkId.ToLower() == "data")
                {
                    dataStartIndex = i + 8;
                    dataSize = (int)chunkSize;
                    if (dataSize <= 0 || chunkSize == 0xFFFFFFFF)
                    {
                        dataSize = wavFile.Length - dataStartIndex;
                        Debug.LogWarning($"[WavUtility] Invalid 'data' chunk size detected. Using remaining bytes: {dataSize}");
                    }
                    break;
                }
                i += 8 + (int)chunkSize;
            }
            if (dataStartIndex == -1 || dataSize == -1)
            {
                Debug.LogError("[WavUtility] 'data' chunk not found in WAV file.");
                return null;
            }
            Debug.Log($"[WavUtility] Found 'data' chunk at {dataStartIndex}, size={dataSize}");
            int sampleCount = dataSize / 2;
            Debug.Log($"[WavUtility] Sample count: {sampleCount}");
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = System.BitConverter.ToInt16(wavFile, dataStartIndex + i * 2);
                samples[i] = sample / 32768f;
            }
            Debug.Log($"[WavUtility] Creating AudioClip: length={sampleCount / channels}, channels={channels}, sampleRate={sampleRate}");
            AudioClip audioClip = AudioClip.Create(clipName, sampleCount / channels, channels, sampleRate, false);
            audioClip.SetData(samples, 0);
            return audioClip;
        }
    }

    private string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
