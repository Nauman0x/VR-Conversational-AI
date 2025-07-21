using UnityEngine;
using System.IO;
using UnityEngine.Networking;

public class STTHandler : MonoBehaviour
{
    [HideInInspector]
    public AudioClip recordedClip;
    public event System.Action<string> OnTranscriptReady;
    [Header("Deepgram API Settings")]
    public string apiKey = "YOUR_API_KEY_HERE";

    public void StartRecording()
    {
        Debug.Log("[STT] Recording started.");
        recordedClip = Microphone.Start(null, false, 60, 16000);
    }

    public void StopAndTranscribeWithDeepgram()
    {
        Debug.Log("[STT] Recording stopped. Saving WAV and sending to Deepgram...");
        int micPos = Microphone.GetPosition(null);
        Microphone.End(null);
        if (recordedClip == null)
        {
            Debug.LogError("[STT] No recorded clip!");
            return;
        }
        if (micPos <= 0 || micPos > recordedClip.samples)
        {
            Debug.LogWarning($"[STT] Invalid mic position: {micPos}, samples: {recordedClip.samples}");
            micPos = recordedClip.samples;
        }

        float[] samples = new float[micPos * recordedClip.channels];
        recordedClip.GetData(samples, 0);
        AudioClip trimmedClip = AudioClip.Create("trimmed", micPos, recordedClip.channels, recordedClip.frequency, false);
        trimmedClip.SetData(samples, 0);
        recordedClip = trimmedClip;
        string path = Path.Combine(Application.persistentDataPath, "recorded.wav");
        SaveWav(path, recordedClip);
        Debug.Log($"[STT] WAV saved at: {path}");
        StartCoroutine(TranscribeWithDeepgram(path));
    }

    private void SaveWav(string filename, AudioClip clip)
    {
        var samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        byte[] wav = ConvertAudioClipToWav(samples, clip.channels, clip.frequency);
        File.WriteAllBytes(filename, wav);
        Debug.Log($"[STT] WAV file written: {filename}, bytes: {wav.Length}");
    }

    private byte[] ConvertAudioClipToWav(float[] samples, int channels, int sampleRate)
    {
        int sampleCount = samples.Length;
        int byteCount = sampleCount * 2;
        int headerSize = 44;
        byte[] wav = new byte[headerSize + byteCount];

        System.Text.Encoding.UTF8.GetBytes("RIFF").CopyTo(wav, 0);
        System.BitConverter.GetBytes(headerSize + byteCount - 8).CopyTo(wav, 4);
        System.Text.Encoding.UTF8.GetBytes("WAVE").CopyTo(wav, 8);
        System.Text.Encoding.UTF8.GetBytes("fmt ").CopyTo(wav, 12);
        System.BitConverter.GetBytes(16).CopyTo(wav, 16);
        System.BitConverter.GetBytes((short)1).CopyTo(wav, 20);
        System.BitConverter.GetBytes((short)channels).CopyTo(wav, 22);
        System.BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);
        System.BitConverter.GetBytes(sampleRate * channels * 2).CopyTo(wav, 28);
        System.BitConverter.GetBytes((short)(channels * 2)).CopyTo(wav, 32);
        System.BitConverter.GetBytes((short)16).CopyTo(wav, 34);
        System.Text.Encoding.UTF8.GetBytes("data").CopyTo(wav, 36);
        System.BitConverter.GetBytes(byteCount).CopyTo(wav, 40);

        int offset = headerSize;
        for (int i = 0; i < sampleCount; i++)
        {
            short val = (short)Mathf.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
            wav[offset++] = (byte)(val & 0xff);
            wav[offset++] = (byte)((val >> 8) & 0xff);
        }
        return wav;
    }

    private System.Collections.IEnumerator TranscribeWithDeepgram(string wavPath)
    {
        byte[] audioBytes = File.ReadAllBytes(wavPath);
        using (UnityWebRequest www = new UnityWebRequest("https://api.deepgram.com/v1/listen", "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(audioBytes);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "audio/wav");
            www.SetRequestHeader("Authorization", $"Token {apiKey}");
            yield return www.SendWebRequest();
            Debug.Log("[STT] Deepgram response received.");
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Deepgram STT Error: " + www.error);
                yield break;
            }
            string responseText = www.downloadHandler.text;
            Debug.Log($"[STT] Deepgram raw response: {responseText}");
            string transcript = "";
            int idx = responseText.IndexOf("transcript");
            if (idx != -1)
            {
                int start = responseText.IndexOf(':', idx) + 2;
                int end = responseText.IndexOf('"', start);
                transcript = responseText.Substring(start, end - start);
                Debug.Log($"[STT] Parsed transcript: {transcript}");
                OnTranscriptReady?.Invoke(transcript);
            }
            else
            {
                Debug.LogWarning("[STT] No transcript found in Deepgram response.");
            }
        }
    }
}