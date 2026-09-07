using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public sealed class ConversationTranscriptEntry
{
    public string speaker;
    public string text;
    public string timestampIso;
}

public class ConversationHistoryExporter : MonoBehaviour
{
    private const string OpenAiChatCompletionsEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string ElevenLabsConversationEndpointTemplate = "https://api.elevenlabs.io/v1/convai/conversations/{0}";
    private static readonly HttpClient HttpClient = new HttpClient();

    [Header("Dependencies")]
    [SerializeField] private UIManager uiManager;
    [SerializeField] private bool autoFindUiManager = true;

    [Header("OpenAI (conversation summary)")]
    [FormerlySerializedAs("geminiApiKey")]
    [SerializeField] private string openAiApiKey = string.Empty;

    [FormerlySerializedAs("geminiModelName")]
    [SerializeField] private string openAiModel = "gpt-4o-mini";

    [Header("ElevenLabs Transcript Source")]
    [SerializeField] private string elevenLabsApiKey = string.Empty;
    [SerializeField] private bool preferElevenLabsTranscriptFetch = true;
    [SerializeField] private string elevenLabsConversationEndpointTemplate = ElevenLabsConversationEndpointTemplate;

    [Header("Database")]
    [SerializeField] private string conversationHistoryTableName = "conversation_history";

    private void Awake()
    {
        BindDependenciesIfNeeded();
    }

    public async Task<bool> ExportConversationHistoryAsync(
        string userId,
        string conversationId,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        IReadOnlyList<ConversationTranscriptEntry> transcriptEntries)
    {
        BindDependenciesIfNeeded();

        if (uiManager == null)
        {
            LogSaveFail(conversationId, userId, "UIManager is missing on ConversationHistoryExporter.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            userId = uiManager.CurrentUserId;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            LogSaveFail(conversationId, userId, "user_id is empty — sign in before ending the call.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(conversationId))
        {
            LogSaveFail(conversationId, userId, "conversation_id is empty.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(openAiApiKey))
        {
            LogSaveFail(
                conversationId,
                userId,
                "OpenAI API key is missing. Assign Open Ai Api Key on ConversationHistoryExporter.");
            return false;
        }

        if (uiManager.UseAuthApi && !AuthApiClient.IsConfigured(uiManager.AuthApiBaseUrl))
        {
            LogSaveFail(
                conversationId,
                userId,
                "Auth API is not configured on UIManager (Auth Api Base Url).");
            return false;
        }

        string persistPath = uiManager.UseAuthApi ? "auth_api" : "postgres_direct";
        int entryCount = transcriptEntries != null ? transcriptEntries.Count : 0;

        Debug.Log(
            "[ConversationHistoryExporter][SAVE-START] user_id=" + userId +
            " conversation_id=" + conversationId +
            " entries=" + entryCount +
            " persist=" + persistPath + ".");

        string transcriptJson = BuildTranscriptJson(userId, conversationId, startedAt, endedAt, transcriptEntries);
        string transcriptText = BuildTranscriptText(transcriptEntries);

        if (preferElevenLabsTranscriptFetch)
        {
            try
            {
                string remoteTranscriptJson = await FetchConversationJsonFromElevenLabsAsync(conversationId).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(remoteTranscriptJson))
                {
                    transcriptJson = remoteTranscriptJson;

                    string parsedRemoteText = BuildTranscriptTextFromElevenLabsJson(remoteTranscriptJson);
                    if (!string.IsNullOrWhiteSpace(parsedRemoteText))
                    {
                        transcriptText = parsedRemoteText;
                    }
                    else
                    {
                        transcriptText = remoteTranscriptJson;
                    }

                    Debug.Log("[ConversationHistoryExporter] Loaded transcript from ElevenLabs for conversation " + conversationId + ".");
                }
                else
                {
                    Debug.LogWarning("[ConversationHistoryExporter] ElevenLabs transcript fetch returned empty payload. Using local transcript buffer.");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[ConversationHistoryExporter] ElevenLabs transcript fetch failed. Using local transcript buffer. Reason: " + exception.Message);
            }
        }

        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            LogSaveFail(conversationId, userId, "transcript text is empty after ElevenLabs fetch and local buffer.");
            return false;
        }

        Debug.Log(
            "[ConversationHistoryExporter][SAVE-OPENAI] Requesting summary. user_id=" + userId +
            " conversation_id=" + conversationId +
            " transcript_chars=" + transcriptText.Length + ".");

        string summaryText;

        try
        {
            summaryText = await RequestOpenAiSummaryAsync(transcriptText).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogSaveFail(
                conversationId,
                userId,
                "OpenAI summary request failed: " + exception.Message,
                exception);
            return false;
        }

        if (string.IsNullOrWhiteSpace(summaryText))
        {
            LogSaveFail(conversationId, userId, "OpenAI returned an empty summary.");
            return false;
        }

        Debug.Log(
            "[ConversationHistoryExporter][SAVE-OPENAI-OK] summary_chars=" + summaryText.Length +
            " conversation_id=" + conversationId + ".");

        bool saved;
        string saveError = string.Empty;

        Debug.Log(
            "[ConversationHistoryExporter][SAVE-DB] Writing history via " + persistPath +
            " conversation_id=" + conversationId + ".");

        if (uiManager.UseAuthApi)
        {
            (saved, saveError) = await AuthApiClient.SaveConversationHistoryOnMainThreadAsync(
                uiManager.AuthApiBaseUrl,
                uiManager.AuthApiKey,
                userId,
                conversationId,
                endedAt.ToString("o"),
                summaryText,
                transcriptJson).ConfigureAwait(false);
        }
        else
        {
            if (!uiManager.TryCreateConnectionStringForExternalUse(out string connectionString, out string connectionError))
            {
                LogSaveFail(conversationId, userId, "PostgreSQL connection string error: " + connectionError);
                return false;
            }

            saved = TryInsertConversationHistory(
                connectionString,
                userId,
                conversationId,
                endedAt.UtcDateTime,
                summaryText,
                transcriptJson,
                out saveError);
        }

        if (!saved)
        {
            LogSaveFail(
                conversationId,
                userId,
                "database write failed (" + persistPath + "): " + saveError);
            return false;
        }

        LogSaveOk(conversationId, userId, persistPath, summaryText.Length, transcriptText.Length);
        return true;
    }

    private static void LogSaveOk(
        string conversationId,
        string userId,
        string persistPath,
        int summaryChars,
        int transcriptChars)
    {
        Debug.Log(
            "[ConversationHistoryExporter][SAVE-OK] Conversation history saved. user_id=" + userId +
            " conversation_id=" + conversationId +
            " persist=" + persistPath +
            " summary_chars=" + summaryChars +
            " transcript_chars=" + transcriptChars + ".");
    }

    private static void LogSaveFail(string conversationId, string userId, string reason, Exception exception = null)
    {
        string message =
            "[ConversationHistoryExporter][SAVE-FAIL] Conversation history NOT saved. user_id=" +
            (string.IsNullOrWhiteSpace(userId) ? "(empty)" : userId) +
            " conversation_id=" + (string.IsNullOrWhiteSpace(conversationId) ? "(empty)" : conversationId) +
            " reason=" + reason;

        if (exception != null)
        {
            Debug.LogError(message + "\n" + exception);
        }
        else
        {
            Debug.LogError(message);
        }
    }

    private void BindDependenciesIfNeeded()
    {
        if (!autoFindUiManager)
        {
            return;
        }

        if (uiManager == null)
        {
            uiManager = FindObjectOfType<UIManager>(true);
        }
    }

    private async Task<string> RequestOpenAiSummaryAsync(string transcriptText)
    {
        if (transcriptText != null && transcriptText.Length > 95000)
        {
            transcriptText = transcriptText.Substring(0, 95000).TrimEnd() +
                "... [truncated for OpenAI token limit]";
        }

        string systemPrompt =
            "You are an assistant that reads call transcripts. Extract any information shared by the USER only. " +
            "Also summarize the full conversation between the user and the agent, including what they talked about and how the agent responded. " +
            "Return ONLY valid JSON with this shape: {\"summary\":\"...\",\"important_user_facts\":[\"...\"]}. " +
            "Keep the summary concise but make sure it preserves important details for future conversations. " +
            "Ignore everything the agent says except as needed for the conversation summary.";

        string escapedSystem = EscapeForJsonPayload(systemPrompt);
        string escapedUser = EscapeForJsonPayload("Transcript:\n" + (transcriptText ?? string.Empty));
        string model = string.IsNullOrWhiteSpace(openAiModel) ? "gpt-4o-mini" : openAiModel.Trim();

        string requestBody =
            "{\"model\":\"" + EscapeForJsonPayload(model) + "\",\"temperature\":0.2,\"messages\":[{\"role\":\"system\",\"content\":\"" +
            escapedSystem +
            "\"},{\"role\":\"user\",\"content\":\"" +
            escapedUser +
            "\"}]}";

        string responseJson = await SendOpenAiChatCompletionAsync(requestBody).ConfigureAwait(false);

        OpenAiChatCompletionResponseEnvelope envelope = JsonUtility.FromJson<OpenAiChatCompletionResponseEnvelope>(responseJson);
        string rawContent = envelope != null && envelope.choices != null && envelope.choices.Length > 0 &&
            envelope.choices[0].message != null
                ? envelope.choices[0].message.content ?? string.Empty
                : string.Empty;

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            throw new InvalidOperationException("OpenAI response did not contain message content.");
        }

        string normalizedText = NormalizeJsonText(rawContent);

        GeminiSummaryResponse summaryResponse = null;
        try
        {
            summaryResponse = JsonUtility.FromJson<GeminiSummaryResponse>(normalizedText);
        }
        catch
        {
            summaryResponse = null;
        }

        if (summaryResponse != null && !string.IsNullOrWhiteSpace(summaryResponse.summary))
        {
            return summaryResponse.summary.Trim();
        }

        string extractedSummary = ExtractJsonStringValue(normalizedText, "summary");
        if (!string.IsNullOrWhiteSpace(extractedSummary))
        {
            return extractedSummary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(normalizedText))
        {
            return normalizedText.Trim();
        }

        return BuildLocalFallbackSummary(transcriptText ?? string.Empty);
    }

    private static string EscapeForJsonPayload(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(value.Length + 16);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private async Task<string> SendOpenAiChatCompletionAsync(string jsonBody)
    {
        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, OpenAiChatCompletionsEndpoint))
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openAiApiKey.Trim());

            using (HttpResponseMessage response = await HttpClient.SendAsync(request).ConfigureAwait(false))
            {
                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("OpenAI chat completion failed: " + (int)response.StatusCode + " / " + responseBody);
                }

                return responseBody;
            }
        }
    }

    private async Task<string> FetchConversationJsonFromElevenLabsAsync(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(elevenLabsApiKey))
        {
            throw new InvalidOperationException("ElevenLabs API key is missing on ConversationHistoryExporter.");
        }

        string endpointTemplate = string.IsNullOrWhiteSpace(elevenLabsConversationEndpointTemplate)
            ? ElevenLabsConversationEndpointTemplate
            : elevenLabsConversationEndpointTemplate;

        string url = string.Format(CultureInfo.InvariantCulture, endpointTemplate, Uri.EscapeDataString(conversationId));

        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
        {
            request.Headers.TryAddWithoutValidation("xi-api-key", elevenLabsApiKey.Trim());
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using (HttpResponseMessage response = await HttpClient.SendAsync(request).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Debug.Log("[ConversationHistoryExporter] ElevenLabs transcript not found yet for conversation_id=" + conversationId + ". Using local transcript buffer.");
                        return string.Empty;
                    }

                    throw new InvalidOperationException("ElevenLabs transcript request failed: " + (int)response.StatusCode + " / " + body);
                }

                return body;
            }
        }
    }

    private static string BuildTranscriptTextFromElevenLabsJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        string directTranscript = ExtractJsonStringValue(json, "transcript");
        if (!string.IsNullOrWhiteSpace(directTranscript))
        {
            return directTranscript.Trim();
        }

        StringBuilder builder = new StringBuilder();
        int searchIndex = 0;

        while (true)
        {
            int textIndex = IndexOfAny(json, searchIndex, "\"user_transcript\"", "\"agent_response\"", "\"text\"");
            if (textIndex < 0)
            {
                break;
            }

            int keyStart = json.LastIndexOf('"', textIndex);
            int keyEnd = json.IndexOf('"', keyStart + 1);
            string key = keyStart >= 0 && keyEnd > keyStart ? json.Substring(keyStart + 1, keyEnd - keyStart - 1) : "text";

            int colonIndex = json.IndexOf(':', textIndex);
            if (colonIndex < 0)
            {
                break;
            }

            int valueQuote = json.IndexOf('"', colonIndex + 1);
            if (valueQuote < 0)
            {
                break;
            }

            StringBuilder valueBuilder = new StringBuilder();
            bool escaped = false;
            int i;
            for (i = valueQuote + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (escaped)
                {
                    valueBuilder.Append(c);
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    break;
                }

                valueBuilder.Append(c);
            }

            string value = valueBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                string speaker = key.IndexOf("agent", StringComparison.OrdinalIgnoreCase) >= 0 ? "Agent" : "User";
                builder.Append(DateTimeOffset.UtcNow.ToString("o"));
                builder.Append(' ');
                builder.Append(speaker);
                builder.Append(": ");
                builder.Append(value);
                builder.AppendLine();
            }

            searchIndex = i + 1;
        }

        return builder.ToString().Trim();
    }

    private static int IndexOfAny(string source, int startIndex, params string[] needles)
    {
        int bestIndex = -1;

        for (int i = 0; i < needles.Length; i++)
        {
            string needle = needles[i];
            if (string.IsNullOrEmpty(needle))
            {
                continue;
            }

            int idx = source.IndexOf(needle, startIndex, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && (bestIndex < 0 || idx < bestIndex))
            {
                bestIndex = idx;
            }
        }

        return bestIndex;
    }

    private bool TryInsertConversationHistory(
        string connectionString,
        string userId,
        string conversationId,
        DateTime endedAtUtc,
        string summaryText,
        string rawTranscriptJson,
        out string error)
    {
        error = string.Empty;

        if (!TryGetNpgsqlConnectionType(out Type connectionType, out string typeError))
        {
            error = typeError;
            return false;
        }

        object connection = null;
        object command = null;

        try
        {
            connection = Activator.CreateInstance(connectionType);
            SetPropertyValue(connectionType, connection, "ConnectionString", connectionString);
            InvokeZeroArgMethod(connectionType, connection, "Open");

            command = InvokeZeroArgMethod(connectionType, connection, "CreateCommand");
            if (command == null)
            {
                error = "Could not create PostgreSQL command.";
                return false;
            }

            string updateSql =
                "UPDATE " + conversationHistoryTableName + " " +
                "SET turn_timestamp_iso = @turn_timestamp_iso, summary = @summary, raw_transcript_json = CAST(@raw_transcript_json AS jsonb) " +
                "WHERE user_id = @user_id AND conversation_id = @conversation_id;";

            SetCommandText(command, updateSql);
            AddParameters(command, new Dictionary<string, object>
            {
                { "@user_id", userId },
                { "@conversation_id", conversationId },
                { "@turn_timestamp_iso", endedAtUtc },
                { "@summary", summaryText },
                { "@raw_transcript_json", rawTranscriptJson }
            });

            int rowsUpdated = ConvertToInt(InvokeZeroArgMethod(command.GetType(), command, "ExecuteNonQuery"));
            if (rowsUpdated > 0)
            {
                Debug.Log("[ConversationHistoryExporter] Updated existing conversation history row. user_id=" + userId + ", conversation_id=" + conversationId + ".");
                return true;
            }

            DisposeIfPossible(command);
            command = InvokeZeroArgMethod(connectionType, connection, "CreateCommand");
            if (command == null)
            {
                error = "Could not create PostgreSQL command for insert.";
                return false;
            }

            string sql =
                "INSERT INTO " + conversationHistoryTableName + " (user_id, conversation_id, turn_timestamp_iso, summary, raw_transcript_json) " +
                "VALUES (@user_id, @conversation_id, @turn_timestamp_iso, @summary, CAST(@raw_transcript_json AS jsonb));";

            SetCommandText(command, sql);
            AddParameters(command, new Dictionary<string, object>
            {
                { "@user_id", userId },
                { "@conversation_id", conversationId },
                { "@turn_timestamp_iso", endedAtUtc },
                { "@summary", summaryText },
                { "@raw_transcript_json", rawTranscriptJson }
            });

            InvokeZeroArgMethod(command.GetType(), command, "ExecuteNonQuery");
            Debug.Log("[ConversationHistoryExporter] Inserted new conversation history row. user_id=" + userId + ", conversation_id=" + conversationId + ".");
            return true;
        }
        catch (TargetInvocationException exception)
        {
            error = ExtractExceptionMessage(exception);
            return false;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            DisposeIfPossible(command);
            DisposeIfPossible(connection);
        }
    }

    private string BuildTranscriptJson(string userId, string conversationId, DateTimeOffset startedAt, DateTimeOffset endedAt, IReadOnlyList<ConversationTranscriptEntry> transcriptEntries)
    {
        ConversationTranscriptEnvelope envelope = new ConversationTranscriptEnvelope
        {
            user_id = userId,
            conversation_id = conversationId,
            started_at_iso = startedAt.ToString("o"),
            ended_at_iso = endedAt.ToString("o"),
            entries = new ConversationTranscriptEntry[transcriptEntries.Count]
        };

        for (int i = 0; i < transcriptEntries.Count; i++)
        {
            ConversationTranscriptEntry entry = transcriptEntries[i];
            envelope.entries[i] = new ConversationTranscriptEntry
            {
                speaker = entry.speaker,
                text = entry.text,
                timestampIso = entry.timestampIso
            };
        }

        return JsonUtility.ToJson(envelope);
    }

    private static string BuildTranscriptText(IReadOnlyList<ConversationTranscriptEntry> transcriptEntries)
    {
        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < transcriptEntries.Count; i++)
        {
            ConversationTranscriptEntry entry = transcriptEntries[i];
            if (entry == null)
            {
                continue;
            }

            string speaker = string.IsNullOrWhiteSpace(entry.speaker) ? "unknown" : entry.speaker.Trim();
            string timestamp = string.IsNullOrWhiteSpace(entry.timestampIso) ? DateTimeOffset.UtcNow.ToString("o") : entry.timestampIso.Trim();
            string text = string.IsNullOrWhiteSpace(entry.text) ? string.Empty : entry.text.Trim();

            if (text.Length == 0)
            {
                continue;
            }

            builder.Append(timestamp);
            builder.Append(' ');
            builder.Append(char.ToUpperInvariant(speaker[0]) + speaker.Substring(1).ToLowerInvariant());
            builder.Append(": ");
            builder.Append(text);
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeJsonText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNewLine = trimmed.IndexOf('\n');
            int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

            if (firstNewLine >= 0 && lastFence > firstNewLine)
            {
                trimmed = trimmed.Substring(firstNewLine + 1, lastFence - firstNewLine - 1).Trim();
            }
        }

        return trimmed;
    }

    private static string ExtractJsonStringValue(string jsonText, string key)
    {
        if (string.IsNullOrWhiteSpace(jsonText) || string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        string quotedKey = "\"" + key + "\"";
        int keyIndex = jsonText.IndexOf(quotedKey, StringComparison.OrdinalIgnoreCase);
        if (keyIndex < 0)
        {
            return string.Empty;
        }

        int colonIndex = jsonText.IndexOf(':', keyIndex + quotedKey.Length);
        if (colonIndex < 0)
        {
            return string.Empty;
        }

        int firstQuoteIndex = jsonText.IndexOf('"', colonIndex + 1);
        if (firstQuoteIndex < 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        bool isEscaped = false;

        for (int i = firstQuoteIndex + 1; i < jsonText.Length; i++)
        {
            char current = jsonText[i];

            if (isEscaped)
            {
                builder.Append(current);
                isEscaped = false;
                continue;
            }

            if (current == '\\')
            {
                isEscaped = true;
                continue;
            }

            if (current == '"')
            {
                break;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static string BuildLocalFallbackSummary(string transcriptText)
    {
        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            return "Conversation summary unavailable.";
        }

        string compact = transcriptText.Replace("\r", string.Empty).Trim();
        string[] lines = compact.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        List<string> userSnippets = new List<string>();
        List<string> agentSnippets = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.IndexOf("User:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                userSnippets.Add(TrimAfterColon(line));
            }
            else if (line.IndexOf("Agent:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                agentSnippets.Add(TrimAfterColon(line));
            }
        }

        string userText = userSnippets.Count > 0 ? userSnippets[0] : compact;
        string agentText = agentSnippets.Count > 0 ? agentSnippets[0] : string.Empty;
        string summary = "User discussed: " + TruncateText(userText, 220);

        if (!string.IsNullOrWhiteSpace(agentText))
        {
            summary += "; agent responded: " + TruncateText(agentText, 180);
        }

        return summary;
    }

    private static string TrimAfterColon(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        int colonIndex = text.IndexOf(':');
        if (colonIndex < 0 || colonIndex + 1 >= text.Length)
        {
            return text.Trim();
        }

        return text.Substring(colonIndex + 1).Trim();
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || maxLength <= 0)
        {
            return string.Empty;
        }

        string trimmed = text.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed.Substring(0, maxLength).TrimEnd() + "...";
    }

    private static bool TryGetNpgsqlConnectionType(out Type connectionType, out string error)
    {
        error = string.Empty;
        connectionType = Type.GetType("Npgsql.NpgsqlConnection, Npgsql");

        if (connectionType != null)
        {
            return true;
        }

        error = "Could not find Npgsql.NpgsqlConnection. Make sure the Npgsql package is installed.";
        return false;
    }

    private static object InvokeZeroArgMethod(Type type, object instance, string methodName)
    {
        MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo method = null;

        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo candidate = methods[i];
            if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal) || candidate.GetParameters().Length != 0)
            {
                continue;
            }

            method = candidate;
            break;
        }

        if (method == null)
        {
            return null;
        }

        return method.Invoke(instance, null);
    }

    private static void SetPropertyValue(Type type, object instance, string propertyName, object value)
    {
        PropertyInfo property = FindProperty(type, propertyName, requireWritable: true, requireReadable: false);
        if (property != null && property.CanWrite)
        {
            property.SetValue(instance, value);
        }
    }

    private static void SetCommandText(object command, string sql)
    {
        PropertyInfo property = FindProperty(command.GetType(), "CommandText", requireWritable: true, requireReadable: false);
        if (property != null && property.CanWrite)
        {
            property.SetValue(command, sql);
        }
    }

    private static void AddParameters(object command, Dictionary<string, object> parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return;
        }

        object parameterCollection = GetPropertyValue(command, "Parameters");
        if (parameterCollection == null)
        {
            return;
        }

        Type collectionType = parameterCollection.GetType();
        MethodInfo createParameterMethod = null;
        MethodInfo[] commandMethods = command.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < commandMethods.Length; i++)
        {
            MethodInfo candidate = commandMethods[i];
            if (string.Equals(candidate.Name, "CreateParameter", StringComparison.Ordinal) && candidate.GetParameters().Length == 0)
            {
                createParameterMethod = candidate;
                break;
            }
        }

        MethodInfo addMethod = null;
        MethodInfo[] addCandidates = collectionType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < addCandidates.Length; i++)
        {
            MethodInfo candidate = addCandidates[i];
            if (!string.Equals(candidate.Name, "Add", StringComparison.Ordinal))
            {
                continue;
            }

            ParameterInfo[] candidateParams = candidate.GetParameters();
            if (candidateParams.Length == 1)
            {
                addMethod = candidate;
                break;
            }
        }

        foreach (KeyValuePair<string, object> parameter in parameters)
        {
            object newParameter = createParameterMethod != null ? createParameterMethod.Invoke(command, null) : null;
            if (newParameter == null)
            {
                continue;
            }

            SetParameterProperty(newParameter, "ParameterName", parameter.Key);
            SetParameterProperty(newParameter, "Value", parameter.Value ?? DBNull.Value);

            if (addMethod != null)
            {
                addMethod.Invoke(parameterCollection, new[] { newParameter });
            }
        }
    }

    private static object GetPropertyValue(object instance, string propertyName)
    {
        if (instance == null)
        {
            return null;
        }

        PropertyInfo property = FindProperty(instance.GetType(), propertyName, requireWritable: false, requireReadable: true);
        return property != null ? property.GetValue(instance) : null;
    }

    private static void SetParameterProperty(object parameter, string propertyName, object value)
    {
        PropertyInfo property = FindProperty(parameter.GetType(), propertyName, requireWritable: true, requireReadable: false);
        if (property != null && property.CanWrite)
        {
            property.SetValue(parameter, value);
        }
    }

    private static PropertyInfo FindProperty(Type type, string propertyName, bool requireWritable, bool requireReadable)
    {
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo candidate = properties[i];
            if (!string.Equals(candidate.Name, propertyName, StringComparison.Ordinal))
            {
                continue;
            }

            if (requireWritable && !candidate.CanWrite)
            {
                continue;
            }

            if (requireReadable && !candidate.CanRead)
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static void DisposeIfPossible(object instance)
    {
        if (instance is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static string ExtractExceptionMessage(Exception exception)
    {
        if (exception == null)
        {
            return string.Empty;
        }

        Exception current = exception;
        while (current.InnerException != null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }

    private static int ConvertToInt(object value)
    {
        if (value == null)
        {
            return 0;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    [Serializable]
    private sealed class OpenAiChatCompletionResponseEnvelope
    {
        public OpenAiChoiceEnvelope[] choices;
    }

    [Serializable]
    private sealed class OpenAiChoiceEnvelope
    {
        public OpenAiMessageEnvelope message;
    }

    [Serializable]
    private sealed class OpenAiMessageEnvelope
    {
        public string content;
    }

    [Serializable]
    private sealed class GeminiSummaryResponse
    {
        public string summary;
        public string[] important_user_facts;
    }

    [Serializable]
    private sealed class ConversationTranscriptEnvelope
    {
        public string user_id;
        public string conversation_id;
        public string started_at_iso;
        public string ended_at_iso;
        public ConversationTranscriptEntry[] entries;
    }
}
