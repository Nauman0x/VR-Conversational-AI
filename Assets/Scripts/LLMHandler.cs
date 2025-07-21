using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System.Collections;

public class LLMHandler : MonoBehaviour
{
    public event System.Action<string> OnReplyReady;
  
    [Header("Groq API Settings")]
    public string apiKey = "YOUR_API_KEY_HERE";
    public string apiUrl = "https://api.groq.com/openai/v1/chat/completions";

    public void GenerateContent(string prompt)
    {
        if (string.IsNullOrEmpty(prompt))
        {
            Debug.LogWarning("[LLM] Empty prompt received, skipping LLM call.");
            return;
        }
        string rules = "You are a conversational AI agent who asks users about their well being. DO NOT INCLUDE ASTERISKS OR ANY SPECIAL CHARACTERS IN YOUR OUTPUT. You're integrated with a text-to-speech engine, converting your words to a human-like voice. Don't use any special characters in your output as it sounds bad when using that with our Text-to-speech engine. Keep the conversation engaging. Don't hallucinate, if the information to a direct question isn't included then you don't have the information to answer the question.";
        string fullPrompt = rules + " " + prompt;
        Debug.Log($"[LLM] GenerateContent called with: {fullPrompt}");
        StartCoroutine(GenerateContentCoroutine(fullPrompt));
    }

    private IEnumerator GenerateContentCoroutine(string prompt)
    {
        Debug.Log($"[LLM] Sending prompt: {prompt}");
        string jsonBody = $"{{\"model\": \"llama3-8b-8192\", \"messages\": [{{\"role\": \"user\", \"content\": \"{EscapeJson(prompt)}\"}}]}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        UnityWebRequest www = new UnityWebRequest(apiUrl, "POST");
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        Debug.Log("[LLM] Sending request to Groq API...");
        yield return www.SendWebRequest();
        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[LLM] Groq API Error: {www.error}");
            yield break;
        }
        string responseText = www.downloadHandler.text;
        Debug.Log($"[LLM] Groq raw response: {responseText}");
        string reply = ParseGroqReply(responseText);
     
        if (!string.IsNullOrEmpty(reply))
        {
            reply = reply.Replace("\\n", " ").Replace("\n", " ").Replace("\r", " ");
        }
        Debug.Log($"[LLM] Parsed reply: {reply}");
    
        OnReplyReady?.Invoke(reply);
    }
    private string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }

   
    private string ParseGroqReply(string responseJson)
    {
        int contentIndex = responseJson.IndexOf("\"content\"");
        if (contentIndex == -1) return "[No reply found]";
        int colonIndex = responseJson.IndexOf(':', contentIndex);
        if (colonIndex == -1) return "[No reply found]";
        int quoteStart = responseJson.IndexOf('"', colonIndex + 1);
        int quoteEnd = responseJson.IndexOf('"', quoteStart + 1);
        if (quoteStart == -1 || quoteEnd == -1) return "[No reply found]";
        return responseJson.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
    }
}

