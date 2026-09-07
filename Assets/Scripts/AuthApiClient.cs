using System;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// HTTPS client for the small auth/history API (Quest-safe — no direct PostgreSQL).
/// </summary>
public static class AuthApiClient
{
    [Serializable]
    private class SignInRequest
    {
        public string user_id;
    }

    [Serializable]
    private class SignUpRequest
    {
        public string full_name;
        public int age;
        public string intro_text;
    }

    [Serializable]
    private class UserIdRequest
    {
        public string user_id;
    }

    [Serializable]
    private class ConversationHistoryRequest
    {
        public string user_id;
        public string conversation_id;
        public string turn_timestamp_iso;
        public string summary;
        public string raw_transcript_json;
    }

    [Serializable]
    private class OkResponse
    {
        public bool ok;
        public string error;
        public string user_id;
    }

    [Serializable]
    private class ContextResponse
    {
        public bool ok;
        public string error;
        public ContextPayload context;
    }

    [Serializable]
    private class ContextPayload
    {
        public string user_id;
        public string full_name;
        public int age;
        public string intro_text;
    }

    public static bool IsConfigured(string baseUrl)
    {
        string normalized = NormalizeBaseUrl(baseUrl);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.IndexOf("YOUR_API_HOST", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("example.com", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerator SignInCoroutine(
        string baseUrl,
        string apiKey,
        string userId,
        Action<bool, string> onComplete)
    {
        if (onComplete == null)
        {
            yield break;
        }

        string url = NormalizeBaseUrl(baseUrl) + "/auth/sign-in";
        string body = JsonUtility.ToJson(new SignInRequest { user_id = userId ?? string.Empty });

        yield return SendJsonCoroutine(url, apiKey, "POST", body, (success, responseText, httpCode) =>
        {
            if (!success)
            {
                onComplete(false, FormatHttpError(httpCode, responseText));
                return;
            }

            OkResponse response = ParseJson<OkResponse>(responseText);
            if (response != null && response.ok && !string.IsNullOrWhiteSpace(response.user_id))
            {
                onComplete(true, string.Empty);
                return;
            }

            onComplete(false, response?.error ?? "Sign-in failed.");
        });
    }

    public static IEnumerator SignUpCoroutine(
        string baseUrl,
        string apiKey,
        string fullName,
        int age,
        string introText,
        Action<bool, string, string> onComplete)
    {
        if (onComplete == null)
        {
            yield break;
        }

        string url = NormalizeBaseUrl(baseUrl) + "/auth/sign-up";
        string body = JsonUtility.ToJson(new SignUpRequest
        {
            full_name = fullName ?? string.Empty,
            age = age,
            intro_text = introText ?? string.Empty
        });

        yield return SendJsonCoroutine(url, apiKey, "POST", body, (success, responseText, httpCode) =>
        {
            if (!success)
            {
                onComplete(false, string.Empty, FormatHttpError(httpCode, responseText));
                return;
            }

            OkResponse response = ParseJson<OkResponse>(responseText);
            if (response != null && response.ok && !string.IsNullOrWhiteSpace(response.user_id))
            {
                onComplete(true, response.user_id, string.Empty);
                return;
            }

            onComplete(false, string.Empty, response?.error ?? "Sign-up failed.");
        });
    }

    public static IEnumerator UpdateLastLoginCoroutine(
        string baseUrl,
        string apiKey,
        string userId,
        Action<bool> onComplete)
    {
        string url = NormalizeBaseUrl(baseUrl) + "/auth/last-login";
        string body = JsonUtility.ToJson(new UserIdRequest { user_id = userId ?? string.Empty });

        yield return SendJsonCoroutine(url, apiKey, "POST", body, (success, responseText, httpCode) =>
        {
            onComplete?.Invoke(success && (ParseJson<OkResponse>(responseText)?.ok ?? false));
        });
    }

    public static bool TryGetUserContext(
        string baseUrl,
        string apiKey,
        string userId,
        int historyLimit,
        out string contextJson,
        out string error)
    {
        contextJson = string.Empty;
        error = string.Empty;

        int safeLimit = Mathf.Clamp(historyLimit, 1, 100);
        string url = NormalizeBaseUrl(baseUrl) +
                     "/users/" + UnityWebRequest.EscapeURL(userId ?? string.Empty) +
                     "/context?limit=" + safeLimit.ToString(CultureInfo.InvariantCulture);

        using UnityWebRequest request = BuildRequest(url, apiKey, "GET", null);
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();

        while (!operation.isDone)
        {
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            error = FormatHttpError(request.responseCode, request.downloadHandler?.text);
            return false;
        }

        string responseText = request.downloadHandler?.text ?? string.Empty;
        if (TryExtractContextJson(responseText, out contextJson, out error))
        {
            return true;
        }

        return false;
    }

    public static IEnumerator SaveConversationHistoryCoroutine(
        string baseUrl,
        string apiKey,
        string userId,
        string conversationId,
        string turnTimestampIso,
        string summary,
        string rawTranscriptJson,
        Action<bool, string> onComplete)
    {
        string url = NormalizeBaseUrl(baseUrl) + "/conversation-history";
        string body = JsonUtility.ToJson(new ConversationHistoryRequest
        {
            user_id = userId ?? string.Empty,
            conversation_id = conversationId ?? string.Empty,
            turn_timestamp_iso = turnTimestampIso ?? string.Empty,
            summary = summary ?? string.Empty,
            raw_transcript_json = rawTranscriptJson ?? "{}"
        });

        yield return SendJsonCoroutine(url, apiKey, "POST", body, (success, responseText, httpCode) =>
        {
            if (onComplete == null)
            {
                return;
            }

            if (!success)
            {
                onComplete(false, FormatHttpError(httpCode, responseText));
                return;
            }

            OkResponse response = ParseJson<OkResponse>(responseText);
            onComplete(response != null && response.ok, response?.error ?? string.Empty);
        });
    }

    public static Task<(bool ok, string error)> SaveConversationHistoryOnMainThreadAsync(
        string baseUrl,
        string apiKey,
        string userId,
        string conversationId,
        string turnTimestampIso,
        string summary,
        string rawTranscriptJson)
    {
        var completion = new TaskCompletionSource<(bool, string)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        MainThreadDispatcher.RunCoroutine(
            SaveConversationHistoryCoroutine(
                baseUrl,
                apiKey,
                userId,
                conversationId,
                turnTimestampIso,
                summary,
                rawTranscriptJson,
                (ok, error) => completion.TrySetResult((ok, error ?? string.Empty))));

        return completion.Task;
    }

    private static IEnumerator SendJsonCoroutine(
        string url,
        string apiKey,
        string method,
        string jsonBody,
        Action<bool, string, long> onComplete)
    {
        using UnityWebRequest request = BuildRequest(url, apiKey, method, jsonBody);
        yield return request.SendWebRequest();

        bool success = request.result == UnityWebRequest.Result.Success;
        onComplete?.Invoke(
            success,
            request.downloadHandler?.text ?? string.Empty,
            request.responseCode);
    }

    private static UnityWebRequest BuildRequest(string url, string apiKey, string method, string jsonBody)
    {
        UnityWebRequest request = method == "GET"
            ? UnityWebRequest.Get(url)
            : new UnityWebRequest(url, method);

        if (method != "GET")
        {
            byte[] payload = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.SetRequestHeader("Content-Type", "application/json");
        }

        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = 30;

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.SetRequestHeader("X-API-Key", apiKey.Trim());
        }

        return request;
    }

    private static bool TryExtractContextJson(string responseText, out string contextJson, out string error)
    {
        contextJson = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(responseText))
        {
            error = "Empty context response.";
            return false;
        }

        ContextResponse wrapper = ParseJson<ContextResponse>(responseText);
        if (wrapper != null && wrapper.ok)
        {
            int contextIndex = responseText.IndexOf("\"context\"", StringComparison.Ordinal);
            if (contextIndex >= 0)
            {
                int objectStart = responseText.IndexOf('{', contextIndex);
                if (objectStart >= 0 && TryExtractJsonObject(responseText, objectStart, out contextJson))
                {
                    return !string.IsNullOrWhiteSpace(contextJson);
                }
            }
        }

        OkResponse fail = ParseJson<OkResponse>(responseText);
        error = fail?.error ?? "No context data found for user.";
        return false;
    }

    private static bool TryExtractJsonObject(string text, int startIndex, out string jsonObject)
    {
        jsonObject = string.Empty;
        int depth = 0;

        for (int i = startIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    jsonObject = text.Substring(startIndex, i - startIndex + 1);
                    return true;
                }
            }
        }

        return false;
    }

    private static string FormatHttpError(long httpCode, string responseText)
    {
        OkResponse parsed = ParseJson<OkResponse>(responseText);
        if (!string.IsNullOrWhiteSpace(parsed?.error))
        {
            return parsed.error;
        }

        if (httpCode == 401)
        {
            return "Invalid API key.";
        }

        if (httpCode == 404)
        {
            return "User ID not found or inactive.";
        }

        if (httpCode >= 500)
        {
            return "Server error (" + httpCode.ToString(CultureInfo.InvariantCulture) + ").";
        }

        if (!string.IsNullOrWhiteSpace(responseText) && responseText.Length < 240)
        {
            return responseText;
        }

        return "Request failed (" + httpCode.ToString(CultureInfo.InvariantCulture) + ").";
    }

    private static T ParseJson<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<T>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        return baseUrl.Trim().TrimEnd('/');
    }
}
