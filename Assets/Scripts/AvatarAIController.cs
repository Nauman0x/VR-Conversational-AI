using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class AvatarAIController : MonoBehaviour
{
    private const string ElevenLabsConversationUrl = "wss://api.elevenlabs.io/v1/convai/conversation";
    private const string ElevenLabsSignedUrlEndpoint = "https://api.elevenlabs.io/v1/convai/conversation/get-signed-url?agent_id={0}";

    [Header("Avatar References")]
    [SerializeField] private OculusLipSyncBlendShape lipSyncBlendShape;
    [SerializeField] private Animator avatarAnimator;

    [Header("ElevenLabs Config")]
    [SerializeField] private string elevenLabsApiKey = string.Empty;
    [SerializeField] private string elevenLabsAgentId = string.Empty;
    [SerializeField] private string elevenLabsVoiceId = string.Empty;
    [SerializeField] private bool autoConnectOnStart;
    [SerializeField] private bool useSignedUrlWhenApiKeyPresent = true;

    [Header("Auth Gate")]
    [SerializeField] private UIManager uiManager;
    [SerializeField] private bool requireAuthBeforeCall = true;
    [SerializeField] private bool showCallButtonOnlyAfterAuth = true;
    [SerializeField] private float callToggleDebounceSeconds = 0.35f;

    [Header("Prompt Context")]
    [SerializeField] private bool appendDatabaseContextAtConversationStart = true;
    [SerializeField] private int promptContextHistoryLimit = 20;
    [SerializeField] private bool logPromptContextPreview = true;
    [SerializeField] private int promptContextPreviewMaxChars = 300;

    [Header("Voice Input")]
    [SerializeField] private bool enableVoiceInput = true;
    [SerializeField] private int audioSampleRate = 48000;
    [SerializeField] private float voiceInputCheckInterval = 0.01f;
    [SerializeField] private float targetVoiceInputRms = 0.08f;
    [SerializeField] private float microphoneGain = 8f;
    [SerializeField] private float silenceFloorRms = 0.006f;
    [SerializeField] private float interruptionRmsThreshold = 0.03f;
    [SerializeField] private float interruptionConfidenceWindowSeconds = 0.2f;
    [SerializeField] private float bargeInRmsThreshold = 0.05f;
    [SerializeField] private float bargeInHoldSeconds = 0.18f;
    [SerializeField] private bool verboseVoiceLogging = false;

    [Header("Optional UI")]
    [SerializeField] private TMP_InputField userMessageInput;
    [SerializeField] private Button sendButton;
    [SerializeField] private Button callToggleButton;
    [SerializeField] private TMP_Text callToggleButtonLabel;
    [SerializeField] private Text callToggleButtonLegacyLabel;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Call Button Sprites")]
    [Tooltip("PNG shown on the button when no call is active.")]
    [SerializeField] private Sprite startCallSprite;
    [Tooltip("PNG shown on the button while a call is connecting or connected.")]
    [SerializeField] private Sprite endCallSprite;

    // Cached reference to the Image component on the call toggle button; resolved once on first use.
    private Image _callToggleButtonImage;

    [Header("Conversation History")]
    [SerializeField] private ConversationHistoryExporter conversationHistoryExporter;

    private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
    private readonly ConcurrentQueue<string> _audioPlaybackQueue = new ConcurrentQueue<string>();
    private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);

    private bool _isConnecting;
    private bool _isConnected;
    private bool _isReadyForText;
    private bool _initiationSent;
    private string _conversationId = string.Empty;
    private string _statusLine = string.Empty;

    private ClientWebSocket _webSocket;
    private CancellationTokenSource _conversationCancellation;
    private Task _receiveTask;
    private string _lastSocketCloseReason = string.Empty;

    private bool _isRecordingVoice;
    private float _voiceTimeSinceLastSend;
    private AudioClip _voiceClip;
    private int _voiceClipPosition;
    private int _agentOutputSampleRate = 48000;
    private readonly ConcurrentQueue<byte[]> _pendingAudioChunks = new ConcurrentQueue<byte[]>();
    private int _audioSendLoopActive;
    private long _lastStrongVoiceTicksUtc;
    private float _bargeInAccumulatedSeconds;
    private bool _audioPlaybackLoopRunning;
    private bool _isAuthenticatedForCalling;
    private float _nextCallToggleAllowedAt;
    private bool _animatorParamsCached;
    private bool _hasTalkParam;
    private bool _hasIdleParam;
    private readonly List<ConversationTranscriptEntry> _conversationTranscriptEntries = new List<ConversationTranscriptEntry>();
    private DateTimeOffset _conversationStartedAt;
    private DateTimeOffset _conversationEndedAt;
    private bool _conversationExportAttempted;
    private bool _conversationContextSent;

    private void Start()
    {
        AutoBindConversationUi();
        BindAuthState();

        if (avatarAnimator != null)
        {
            CacheAnimatorParameters();
            ApplyAnimatorTalkState(false);
        }

        if (sendButton != null)
        {
            sendButton.onClick.AddListener(OnSendClicked);
        }

        if (callToggleButton != null)
        {
            callToggleButton.onClick.AddListener(OnCallToggleClicked);
        }

        UpdateCallToggleButtonUi();

        SetStatus("ElevenLabs voice conversation ready.");

        if (autoConnectOnStart && CanStartCallNow())
        {
            StartConversation();
        }
    }

    private void Update()
    {
        while (_mainThreadActions.TryDequeue(out Action action))
        {
            action?.Invoke();
        }

        if (_audioPlaybackQueue.Count > 0 && !_audioPlaybackLoopRunning)
        {
            StartCoroutine(PlayQueuedAgentAudioRoutine());
        }

        if (enableVoiceInput && _isConnected && _isReadyForText)
        {
            if (!_isRecordingVoice)
            {
                StartVoiceRecording();
            }

            _voiceTimeSinceLastSend += Time.deltaTime;
            if (_voiceTimeSinceLastSend >= voiceInputCheckInterval)
            {
                _voiceTimeSinceLastSend = 0f;
                StreamVoiceAudioChunk();
            }
        }
        else if (_isRecordingVoice)
        {
            StopVoiceRecording();
        }

        if (avatarAnimator != null && !IsAgentSpeaking())
        {
            ApplyAnimatorTalkState(false);
        }
    }

    private void OnDestroy()
    {
        if (sendButton != null)
        {
            sendButton.onClick.RemoveListener(OnSendClicked);
        }

        if (callToggleButton != null)
        {
            callToggleButton.onClick.RemoveListener(OnCallToggleClicked);
        }

        if (uiManager != null)
        {
            uiManager.OnSignedIn -= OnAuthSucceeded;
            uiManager.OnSignedUp -= OnAuthSucceeded;
        }

        _ = DisconnectAgentAsync("Controller destroyed.", false);
    }

    public void StartConversation()
    {
        if (!CanStartCallNow())
        {
            SetStatus("Sign in or sign up first, then press Start Call.");
            UpdateCallToggleButtonUi();
            return;
        }

        if (_isConnecting || _isConnected)
        {
            return;
        }

        ResetConversationTranscript();
        _isConnecting = true;
        UpdateCallToggleButtonUi();
        StartCoroutine(ConnectToAgentRoutine());
    }

    public void StopConversation()
    {
        UpdateCallToggleButtonUi();
        _ = DisconnectAgentAsync("Conversation stopped.", true);
    }

    public void OnCallToggleClicked()
    {
        if (Time.unscaledTime < _nextCallToggleAllowedAt)
        {
            return;
        }

        _nextCallToggleAllowedAt = Time.unscaledTime + Mathf.Max(0.05f, callToggleDebounceSeconds);

        if (!CanStartCallNow())
        {
            SetStatus("Sign in or sign up first, then press Start Call.");
            UpdateCallToggleButtonUi();
            return;
        }

        if (_isConnecting)
        {
            SetStatus("Connecting...");
            UpdateCallToggleButtonUi();
            return;
        }

        if (_isConnected)
        {
            StopConversation();
            return;
        }

        StartConversation();
    }

    private IEnumerator ConnectToAgentRoutine()
    {
        if (!TryGetElevenLabsConfig(out string configError))
        {
            _isConnecting = false;
            UpdateCallToggleButtonUi();
            SetStatus(configError);
            yield break;
        }

        SetStatus("Resolving ElevenLabs agent connection...");

        string conversationUrl = BuildDirectConversationUrl(elevenLabsAgentId);

        if (useSignedUrlWhenApiKeyPresent && !string.IsNullOrWhiteSpace(elevenLabsApiKey))
        {
            string signedUrl = string.Empty;
            string signedUrlError = string.Empty;

            yield return ResolveSignedUrlRoutine(resolvedUrl => signedUrl = resolvedUrl, error => signedUrlError = error);

            if (!string.IsNullOrWhiteSpace(signedUrl))
            {
                conversationUrl = signedUrl;
                Debug.Log("[AvatarAIController] Using signed ElevenLabs WebSocket URL.");
            }
            else if (!string.IsNullOrWhiteSpace(signedUrlError))
            {
                Debug.LogWarning("[AvatarAIController] Signed URL lookup failed: " + signedUrlError);
            }
        }

        _ = ConnectToAgentAsync(conversationUrl);
    }

    private IEnumerator ResolveSignedUrlRoutine(Action<string> onSuccess, Action<string> onError)
    {
        string endpoint = string.Format(CultureInfo.InvariantCulture, ElevenLabsSignedUrlEndpoint, UnityWebRequest.EscapeURL(elevenLabsAgentId));

        using (UnityWebRequest request = UnityWebRequest.Get(endpoint))
        {
            request.SetRequestHeader("xi-api-key", elevenLabsApiKey.Trim());
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke("Signed URL request failed: " + request.error);
                yield break;
            }

            string signedUrl = ExtractJsonString(request.downloadHandler.text, "signed_url");
            if (string.IsNullOrWhiteSpace(signedUrl))
            {
                onError?.Invoke("Signed URL response did not include signed_url.");
                yield break;
            }

            onSuccess?.Invoke(signedUrl);
        }
    }

    private async Task ConnectToAgentAsync(string conversationUrl)
    {
        try
        {
            _conversationCancellation?.Cancel();
            _conversationCancellation?.Dispose();
            _conversationCancellation = new CancellationTokenSource();

            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

            _lastSocketCloseReason = string.Empty;

            PostStatus("Opening ElevenLabs WebSocket...");
            Debug.Log("[AvatarAIController] Connecting to: " + conversationUrl);

            await _webSocket.ConnectAsync(new Uri(conversationUrl), _conversationCancellation.Token).ConfigureAwait(false);

            _isConnected = true;
            _isConnecting = false;
            _isReadyForText = false;
            _initiationSent = false;
            EnqueueMainThread(UpdateCallToggleButtonUi);

            PostStatus("Connected. Waiting for metadata...");
            _receiveTask = ReceiveAgentMessagesAsync(_conversationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            _isConnecting = false;
            _isConnected = false;
            _isReadyForText = false;
            EnqueueMainThread(UpdateCallToggleButtonUi);
            PostStatus("Connection canceled.");
            Debug.Log("[AvatarAIController] Connection canceled before completion.");
        }
        catch (Exception exception)
        {
            _isConnecting = false;
            _isConnected = false;
            _isReadyForText = false;
            EnqueueMainThread(UpdateCallToggleButtonUi);
            PostStatus("Connection failed: " + exception.Message);
            Debug.LogError("[AvatarAIController] Connection failed: " + exception);
        }
    }

    private async Task ReceiveAgentMessagesAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8192];

        using (MemoryStream messageStream = new MemoryStream())
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        WebSocketCloseStatus? closeStatus = _webSocket.CloseStatus;
                        string closeDescription = _webSocket.CloseStatusDescription;
                        _lastSocketCloseReason = "Server closed socket"
                            + (closeStatus.HasValue ? " (" + closeStatus.Value + ")" : string.Empty)
                            + (!string.IsNullOrWhiteSpace(closeDescription) ? ": " + closeDescription : ".");

                        Debug.LogWarning("[AvatarAIController] " + _lastSocketCloseReason);
                        break;
                    }

                    if (result.Count > 0)
                    {
                        messageStream.Write(buffer, 0, result.Count);
                    }

                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    string message = Encoding.UTF8.GetString(messageStream.ToArray());
                    messageStream.SetLength(0);
                    await HandleIncomingMessageAsync(message, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[AvatarAIController] Receive loop canceled.");
            }
            catch (Exception exception)
            {
                Debug.LogError("[AvatarAIController] Receive loop error: " + exception);
                PostStatus("Receive error: " + exception.Message);
            }
            finally
            {
                _isConnected = false;
                _isReadyForText = false;
                _isConnecting = false;
                EnqueueMainThread(UpdateCallToggleButtonUi);

                if (string.IsNullOrWhiteSpace(_lastSocketCloseReason))
                {
                    _lastSocketCloseReason = cancellationToken.IsCancellationRequested
                        ? "Disconnected: conversation canceled locally."
                        : "Disconnected: socket closed without a close description.";
                }

                await ExportConversationIfNeededAsync("socket-close").ConfigureAwait(false);

                PostStatus(_lastSocketCloseReason);
            }
        }
    }

    private async Task HandleIncomingMessageAsync(string rawMessage, CancellationToken cancellationToken)
    {
        string type = ExtractJsonString(rawMessage, "type");
        if (string.IsNullOrWhiteSpace(type))
        {
            return;
        }

        if (verboseVoiceLogging || (type != "ping" && type != "audio" && type != "agent_audio_chunk"))
        {
            Debug.Log("[AvatarAIController] RX: " + type);
        }

        switch (type)
        {
            case "conversation_initiation_metadata":
            {
                _conversationId = ExtractJsonString(rawMessage, "conversation_id");
                string agentOutputFormat = ExtractJsonString(rawMessage, "agent_output_audio_format");
                string userInputFormat = ExtractJsonString(rawMessage, "user_input_audio_format");

                if (TryParsePcmSampleRate(agentOutputFormat, out int parsedAgentOutputRate))
                {
                    _agentOutputSampleRate = parsedAgentOutputRate;
                }

                _conversationStartedAt = DateTimeOffset.UtcNow;
                _conversationEndedAt = default;
                _conversationTranscriptEntries.Clear();

                if (TryParsePcmSampleRate(userInputFormat, out int parsedUserInputRate) && parsedUserInputRate > 0 && audioSampleRate != parsedUserInputRate)
                {
                    audioSampleRate = parsedUserInputRate;
                    if (_isRecordingVoice)
                    {
                        _mainThreadActions.Enqueue(() =>
                        {
                            StopVoiceRecording();
                            StartVoiceRecording();
                        });
                    }
                }

                PostStatus("Metadata received for conversation " + _conversationId + ".");

                if (!_initiationSent)
                {
                    await SendConversationInitiationAsync(cancellationToken).ConfigureAwait(false);
                }

                break;
            }
            case "agent_response":
            {
                string agentText = ExtractJsonString(rawMessage, "agent_response");
                if (!string.IsNullOrWhiteSpace(agentText))
                {
                    Debug.Log("[AvatarAIController] Agent text response: " + agentText);
                    AddTranscriptEntry("agent", agentText);
                }

                break;
            }
            case "audio":
            case "agent_audio_chunk":
            {
                string audioData = ExtractJsonString(rawMessage, "audio_base_64");
                if (string.IsNullOrWhiteSpace(audioData))
                {
                    audioData = ExtractJsonString(rawMessage, "audio_chunk");
                }
                if (string.IsNullOrWhiteSpace(audioData))
                {
                    audioData = ExtractJsonString(rawMessage, "audio");
                }

                if (!string.IsNullOrWhiteSpace(audioData))
                {
                    _audioPlaybackQueue.Enqueue(audioData);
                }

                break;
            }
            case "user_transcript":
            {
                string userTranscript = ExtractJsonString(rawMessage, "user_transcript");
                if (!string.IsNullOrWhiteSpace(userTranscript))
                {
                    Debug.Log("[AvatarAIController] User transcript: " + userTranscript);
                    AddTranscriptEntry("user", userTranscript);
                }

                break;
            }
            case "ping":
            {
                long pingEventId = ExtractJsonLong(rawMessage, "event_id");
                string pong = "{\"type\":\"pong\",\"event_id\":" + pingEventId.ToString(CultureInfo.InvariantCulture) + "}";
                await SendSocketMessageAsync(pong, cancellationToken).ConfigureAwait(false);
                break;
            }
            case "interruption":
            {
                bool likelyRealBargeIn = HadRecentStrongVoiceActivity();

                if (!likelyRealBargeIn)
                {
                    break;
                }

                _mainThreadActions.Enqueue(() =>
                {
                    while (_audioPlaybackQueue.TryDequeue(out _))
                    {
                    }

                    StopAllPlayback();
                });
                break;
            }
            case "error":
            {
                Debug.LogError("[AvatarAIController] Server error: " + rawMessage);
                string serverErrorMessage = ExtractJsonString(rawMessage, "message");
                if (string.IsNullOrWhiteSpace(serverErrorMessage))
                {
                    serverErrorMessage = ExtractJsonString(rawMessage, "error");
                }

                PostStatus(!string.IsNullOrWhiteSpace(serverErrorMessage)
                    ? "ElevenLabs error: " + serverErrorMessage
                    : "ElevenLabs returned an error.");
                break;
            }
            default:
            {
                if (verboseVoiceLogging)
                {
                    Debug.Log("[AvatarAIController] Event: " + type);
                }

                break;
            }
        }
    }

    private async Task SendConversationInitiationAsync(CancellationToken cancellationToken)
    {
        if (_initiationSent)
        {
            return;
        }

        string voiceId = ResolveVoiceId();

        if (string.IsNullOrWhiteSpace(voiceId))
        {
            PostStatus("Missing ElevenLabs voice ID.");
            return;
        }

        string payload = "{"
            + "\"type\":\"conversation_initiation_client_data\"," 
            + "\"conversation_config_override\":{"
            + "\"tts\":{\"voice_id\":\"" + EscapeJson(voiceId) + "\"},"
            + "\"user_input_audio_format\":\"pcm_" + audioSampleRate.ToString(CultureInfo.InvariantCulture) + "\""
            + "}}";

        await SendSocketMessageAsync(payload, cancellationToken).ConfigureAwait(false);
        _initiationSent = true;
        _isReadyForText = true;

        await SendDatabaseContextToAgentAsync(cancellationToken).ConfigureAwait(false);
        PostStatus("Connected. Start speaking...");
    }

    private async Task SendTextToAgentAsync(string userText)
    {
        if (!_isConnected || !_isReadyForText || _webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            SetStatus("ElevenLabs connection is not ready yet.");
            return;
        }

        if (string.IsNullOrWhiteSpace(userText))
        {
            SetStatus("Enter a message first.");
            return;
        }

        await SendUserMessageAsync(userText, CancellationToken.None).ConfigureAwait(false);
        PostStatus("Message sent. Waiting for ElevenLabs response...");
    }

    private async Task SendUserMessageAsync(string userText, CancellationToken cancellationToken)
    {
        string payload = "{\"type\":\"user_message\",\"text\":\"" + EscapeJson(userText) + "\"}";
        await SendSocketMessageAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendDatabaseContextToAgentAsync(CancellationToken cancellationToken)
    {
        if (!appendDatabaseContextAtConversationStart || _conversationContextSent)
        {
            return;
        }

        if (uiManager == null)
        {
            Debug.LogWarning("[AvatarAIController] Prompt context skipped: UIManager is missing.");
            return;
        }

        string userId = uiManager.CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            Debug.LogWarning("[AvatarAIController] Prompt context skipped: user_id is empty.");
            return;
        }

        if (!uiManager.TryBuildConversationPromptContext(userId, promptContextHistoryLimit, out string contextJson, out string contextError))
        {
            Debug.LogWarning("[AvatarAIController] Prompt context fetch failed for user_id=" + userId + ". Reason: " + contextError);
            return;
        }

        Debug.Log("[AvatarAIController] Prompt context fetched. user_id=" + userId + ", history_limit=" + promptContextHistoryLimit + ", context_chars=" + contextJson.Length + ".");

        if (logPromptContextPreview)
        {
            int safeMax = Mathf.Max(40, promptContextPreviewMaxChars);
            string preview = contextJson.Length > safeMax ? contextJson.Substring(0, safeMax) + "..." : contextJson;
            Debug.Log("[AvatarAIController] Prompt context preview: " + preview);
        }

        string contextPrompt =
            "Context for this user. Use it to personalize your responses, but do not read this context verbatim to the user.\n"
            + contextJson;

        await SendUserMessageAsync(contextPrompt, cancellationToken).ConfigureAwait(false);
        _conversationContextSent = true;
        Debug.Log("[AvatarAIController] Prompt context sent for user_id=" + userId + ", conversation_id=" + _conversationId + ", payload_chars=" + contextPrompt.Length + ".");
    }

    private async Task SendSocketMessageAsync(string message, CancellationToken cancellationToken)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            return;
        }

        byte[] payloadBytes = Encoding.UTF8.GetBytes(message);

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _webSocket.SendAsync(new ArraySegment<byte>(payloadBytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task DisconnectAgentAsync(string reason, bool exportConversation)
    {
        _isReadyForText = false;
        _isConnecting = false;

        StopVoiceRecording();
        StopAllPlayback();

        if (_conversationCancellation != null)
        {
            _conversationCancellation.Cancel();
        }

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.CloseReceived)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[AvatarAIController] WebSocket close failed: " + exception.Message);
            }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
            }
        }

        _isConnected = false;
        _initiationSent = false;
        EnqueueMainThread(UpdateCallToggleButtonUi);

        if (exportConversation)
        {
            _conversationEndedAt = DateTimeOffset.UtcNow;
            await ExportConversationIfNeededAsync("end-call-button").ConfigureAwait(false);
        }

        PostStatus("ElevenLabs connection closed.");
        _conversationTranscriptEntries.Clear();
        _conversationId = string.Empty;
    }

    private void AutoBindConversationUi()
    {
        if (uiManager == null)
        {
            uiManager = FindObjectOfType<UIManager>(true);
        }

        if (userMessageInput == null)
        {
            userMessageInput = FindConversationInputField();
        }

        if (sendButton == null)
        {
            sendButton = FindConversationButton();
        }

        if (callToggleButton == null)
        {
            callToggleButton = FindCallToggleButton();
        }

        if (callToggleButtonLabel == null && callToggleButton != null)
        {
            callToggleButtonLabel = callToggleButton.GetComponentInChildren<TMP_Text>(true);
        }

        if (callToggleButtonLegacyLabel == null && callToggleButton != null)
        {
            callToggleButtonLegacyLabel = callToggleButton.GetComponentInChildren<Text>(true);
        }

        if (statusText == null)
        {
            statusText = FindStatusLabel();
        }

        if (lipSyncBlendShape == null)
        {
            lipSyncBlendShape = FindObjectOfType<OculusLipSyncBlendShape>(true);
        }

        if (conversationHistoryExporter == null)
        {
            conversationHistoryExporter = FindObjectOfType<ConversationHistoryExporter>(true);
        }
    }

    private TMP_InputField FindConversationInputField()
    {
        TMP_InputField[] fields = FindObjectsOfType<TMP_InputField>(true);

        foreach (TMP_InputField field in fields)
        {
            if (field == null)
            {
                continue;
            }

            string fieldName = field.name ?? string.Empty;
            TMP_Text placeholder = field.placeholder as TMP_Text;
            string placeholderText = placeholder != null ? placeholder.text : string.Empty;

            if (placeholderText.IndexOf("Enter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                placeholderText.IndexOf("Message", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fieldName.IndexOf("Conversation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return field;
            }
        }

        return fields.Length > 0 ? fields[0] : null;
    }

    private Button FindConversationButton()
    {
        Button[] buttons = FindObjectsOfType<Button>(true);
        Button fallback = null;

        foreach (Button button in buttons)
        {
            if (button == null)
            {
                continue;
            }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            string buttonText = label != null ? label.text : string.Empty;

            if (buttonText.IndexOf("Send", StringComparison.OrdinalIgnoreCase) >= 0 ||
                buttonText.IndexOf("Submit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                buttonText.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return button;
            }

            if (fallback == null)
            {
                fallback = button;
            }
        }

        return fallback;
    }

    private Button FindCallToggleButton()
    {
        Button[] buttons = FindObjectsOfType<Button>(true);

        foreach (Button button in buttons)
        {
            if (button == null)
            {
                continue;
            }

            if (button == sendButton)
            {
                continue;
            }

            string buttonName = button.name ?? string.Empty;
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            string buttonText = label != null ? label.text : string.Empty;

            bool looksLikeCallToggle =
                buttonName.IndexOf("Call", StringComparison.OrdinalIgnoreCase) >= 0 ||
                buttonText.IndexOf("Call", StringComparison.OrdinalIgnoreCase) >= 0 ||
                buttonText.IndexOf("Connect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                buttonText.IndexOf("Hang", StringComparison.OrdinalIgnoreCase) >= 0 ||
                buttonText.IndexOf("End", StringComparison.OrdinalIgnoreCase) >= 0;

            if (looksLikeCallToggle)
            {
                return button;
            }
        }

        return null;
    }

    /// <summary>
    /// Refreshes the call toggle button's visibility and sprite to reflect the current call state.
    /// Swaps between <see cref="startCallSprite"/> and <see cref="endCallSprite"/> when both are
    /// assigned. Falls back to text-label switching when sprites are not configured, preserving
    /// backward-compatible behaviour.
    /// </summary>
    private void UpdateCallToggleButtonUi()
    {
        if (callToggleButton == null)
        {
            return;
        }

        bool showButton = !showCallButtonOnlyAfterAuth || _isAuthenticatedForCalling || !requireAuthBeforeCall;
        callToggleButton.gameObject.SetActive(showButton);

        if (!showButton)
        {
            return;
        }

        bool isActiveCall = _isConnected || _isConnecting;

        /* -----------------------------------------------------------------------
         * SPRITE SWAP — preferred path when both sprites are wired up in Inspector.
         * Resolves the button's Image component once and caches it for reuse.
         * ----------------------------------------------------------------------- */
        if (startCallSprite != null && endCallSprite != null)
        {
            if (_callToggleButtonImage == null)
            {
                _callToggleButtonImage = callToggleButton.GetComponent<Image>();
            }

            if (_callToggleButtonImage != null)
            {
                _callToggleButtonImage.sprite = isActiveCall ? endCallSprite : startCallSprite;
            }
        }
        else
        {
            /* -------------------------------------------------------------------
             * TEXT FALLBACK — used when sprites are not assigned in the Inspector.
             * ------------------------------------------------------------------- */
            string label = isActiveCall ? "End Call" : "Start Call";

            if (callToggleButtonLabel == null)
            {
                callToggleButtonLabel = callToggleButton.GetComponentInChildren<TMP_Text>(true);
            }

            if (callToggleButtonLegacyLabel == null)
            {
                callToggleButtonLegacyLabel = callToggleButton.GetComponentInChildren<Text>(true);
            }

            if (callToggleButtonLabel != null)
            {
                callToggleButtonLabel.text = label;
            }

            if (callToggleButtonLegacyLabel != null)
            {
                callToggleButtonLegacyLabel.text = label;
            }
        }

        callToggleButton.interactable = true;
    }

    private void BindAuthState()
    {
        if (uiManager != null)
        {
            uiManager.OnSignedIn -= OnAuthSucceeded;
            uiManager.OnSignedUp -= OnAuthSucceeded;
            uiManager.OnSignedIn += OnAuthSucceeded;
            uiManager.OnSignedUp += OnAuthSucceeded;
            _isAuthenticatedForCalling = !string.IsNullOrWhiteSpace(uiManager.CurrentUserId);
        }
        else
        {
            _isAuthenticatedForCalling = !requireAuthBeforeCall;
        }

        UpdateCallToggleButtonUi();
    }

    private void OnAuthSucceeded(string userId)
    {
        _isAuthenticatedForCalling = !string.IsNullOrWhiteSpace(userId);
        UpdateCallToggleButtonUi();
    }

    private bool CanStartCallNow()
    {
        return !requireAuthBeforeCall || _isAuthenticatedForCalling;
    }

    private void ResetConversationTranscript()
    {
        _conversationTranscriptEntries.Clear();
        _conversationStartedAt = DateTimeOffset.UtcNow;
        _conversationEndedAt = default;
        _conversationExportAttempted = false;
        _conversationContextSent = false;
    }

    private async Task ExportConversationIfNeededAsync(string trigger)
    {
        if (_conversationExportAttempted)
        {
            return;
        }

        _conversationExportAttempted = true;

        if (conversationHistoryExporter == null || string.IsNullOrWhiteSpace(_conversationId))
        {
            Debug.LogWarning("[AvatarAIController] Export skipped (" + trigger + "). exporter_missing=" + (conversationHistoryExporter == null) + ", conversation_id_empty=" + string.IsNullOrWhiteSpace(_conversationId) + ".");
            return;
        }

        string userId = uiManager != null ? uiManager.CurrentUserId : string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
        {
            PostStatus("Conversation summary skipped: user is not signed in.");
            Debug.LogWarning("[AvatarAIController] Export skipped (" + trigger + ") because user_id is empty.");
            return;
        }

        if (_conversationEndedAt == default)
        {
            _conversationEndedAt = DateTimeOffset.UtcNow;
        }

        Debug.Log("[AvatarAIController] Exporting conversation summary (" + trigger + "). user_id=" + userId + ", conversation_id=" + _conversationId + ", local_entries=" + _conversationTranscriptEntries.Count + ".");
        PostStatus("Saving conversation summary...");

        bool saved = await conversationHistoryExporter.ExportConversationHistoryAsync(
            userId,
            _conversationId,
            _conversationStartedAt,
            _conversationEndedAt,
            _conversationTranscriptEntries).ConfigureAwait(false);

        PostStatus(saved ? "Conversation summary saved." : "Conversation summary could not be saved.");
        Debug.Log("[AvatarAIController] Export result (" + trigger + ") for conversation_id=" + _conversationId + ": " + (saved ? "saved" : "failed") + ".");
    }

    private void AddTranscriptEntry(string speaker, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _conversationTranscriptEntries.Add(new ConversationTranscriptEntry
        {
            speaker = speaker,
            text = text,
            timestampIso = DateTimeOffset.UtcNow.ToString("o")
        });
    }

    private TextMeshProUGUI FindStatusLabel()
    {
        TextMeshProUGUI[] texts = FindObjectsOfType<TextMeshProUGUI>(true);

        foreach (TextMeshProUGUI text in texts)
        {
            if (text == null)
            {
                continue;
            }

            string textName = text.name ?? string.Empty;
            if (textName.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return text;
            }
        }

        return null;
    }

    private void OnSendClicked()
    {
        string userText = GetUserText();

        if (string.IsNullOrWhiteSpace(userText))
        {
            SetStatus("Enter a message first.");
            return;
        }

        _ = SendTextToAgentAsync(userText.Trim());
        ClearUserText();
    }

    private bool TryGetElevenLabsConfig(out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(elevenLabsApiKey))
        {
            error = "ElevenLabs API key is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(elevenLabsAgentId))
        {
            error = "ElevenLabs agent ID is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(elevenLabsVoiceId))
        {
            error = "ElevenLabs voice ID is missing.";
            return false;
        }

        return true;
    }

    private string ResolveVoiceId()
    {
        if (!string.IsNullOrWhiteSpace(elevenLabsVoiceId))
        {
            return elevenLabsVoiceId;
        }

        return string.Empty;
    }

    private static string BuildDirectConversationUrl(string agentId)
    {
        return ElevenLabsConversationUrl + "?agent_id=" + UnityWebRequest.EscapeURL(agentId);
    }

    private string GetUserText()
    {
        return userMessageInput != null ? userMessageInput.text : string.Empty;
    }

    private void ClearUserText()
    {
        if (userMessageInput != null)
        {
            userMessageInput.text = string.Empty;
        }
    }

    private void EnqueueMainThread(Action action)
    {
        if (action != null)
        {
            _mainThreadActions.Enqueue(action);
        }
    }

    private void PostStatus(string value)
    {
        EnqueueMainThread(() => SetStatus(value));
    }

    private void SetStatus(string value)
    {
        _statusLine = value;

        if (statusText != null)
        {
            statusText.text = value;
        }

        Debug.Log("[STATUS] " + value);
    }

    private void StartVoiceRecording()
    {
        if (_isRecordingVoice)
        {
            return;
        }

        try
        {
            if (Microphone.devices.Length == 0)
            {
                SetStatus("No microphone found.");
                return;
            }

            _isRecordingVoice = true;
            _voiceClipPosition = 0;
            _voiceClip = Microphone.Start(null, true, 300, audioSampleRate);
            SetStatus("Recording voice... Speak naturally.");
        }
        catch (Exception exception)
        {
            SetStatus("Failed to start microphone: " + exception.Message);
            _isRecordingVoice = false;
        }
    }

    private void StopVoiceRecording()
    {
        if (!_isRecordingVoice)
        {
            return;
        }

        try
        {
            Microphone.End(null);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[AvatarAIController] Microphone stop error: " + exception.Message);
        }

        _isRecordingVoice = false;
        _voiceClip = null;
    }

    private void StreamVoiceAudioChunk()
    {
        if (_voiceClip == null || !_isRecordingVoice)
        {
            return;
        }

        int channels = Mathf.Max(1, _voiceClip.channels);
        int currentPosition = Microphone.GetPosition(null);
        if (currentPosition < 0)
        {
            return;
        }

        int samplesToRead;
        if (currentPosition >= _voiceClipPosition)
        {
            samplesToRead = currentPosition - _voiceClipPosition;
        }
        else
        {
            samplesToRead = (_voiceClip.samples - _voiceClipPosition) + currentPosition;
        }

        if (samplesToRead <= 0)
        {
            return;
        }

        float[] rawInterleaved = ReadMicrophoneInterleavedSamples(_voiceClipPosition, samplesToRead, channels);
        if (rawInterleaved == null || rawInterleaved.Length == 0)
        {
            return;
        }

        float[] monoAudioData = DownmixToMono(rawInterleaved, channels);
        if (monoAudioData.Length == 0)
        {
            return;
        }

        float rms = 0f;
        for (int i = 0; i < monoAudioData.Length; i++)
        {
            rms += monoAudioData[i] * monoAudioData[i];
        }
        rms = Mathf.Sqrt(rms / monoAudioData.Length);

        bool isAgentSpeaking = IsAgentSpeaking();
        float chunkDurationSeconds = (float)samplesToRead / Mathf.Max(1, audioSampleRate);

        if (isAgentSpeaking)
        {
            if (rms >= bargeInRmsThreshold)
            {
                _bargeInAccumulatedSeconds += chunkDurationSeconds;
            }
            else
            {
                _bargeInAccumulatedSeconds = 0f;
            }

            if (_bargeInAccumulatedSeconds < bargeInHoldSeconds)
            {
                byte[] silenceWhileAgentSpeaking = new byte[monoAudioData.Length * 2];
                QueueLatestAudioChunkForSend(silenceWhileAgentSpeaking);
                _voiceClipPosition = currentPosition;
                return;
            }
        }
        else
        {
            _bargeInAccumulatedSeconds = 0f;
        }

        if (rms < silenceFloorRms)
        {
            byte[] silenceChunk = new byte[monoAudioData.Length * 2];
            _voiceClipPosition = currentPosition;
            QueueLatestAudioChunkForSend(silenceChunk);
            return;
        }

        if (rms >= interruptionRmsThreshold)
        {
            Interlocked.Exchange(ref _lastStrongVoiceTicksUtc, DateTime.UtcNow.Ticks);
        }

        float adaptiveGain = rms > 0.0001f ? targetVoiceInputRms / rms : microphoneGain;
        float effectiveGain = Mathf.Clamp(adaptiveGain, 1f, microphoneGain);

        float[] amplifiedData = new float[monoAudioData.Length];
        for (int i = 0; i < monoAudioData.Length; i++)
        {
            amplifiedData[i] = Mathf.Clamp(monoAudioData[i] * effectiveGain, -1f, 1f);
        }

        byte[] pcmBytes = ConvertFloatToPcm16(amplifiedData);
        _voiceClipPosition = currentPosition;

        if (verboseVoiceLogging)
        {
            Debug.Log("[AvatarAIController] Audio chunk sent: " + pcmBytes.Length + " bytes");
        }

        QueueLatestAudioChunkForSend(pcmBytes);
    }

    private bool HadRecentStrongVoiceActivity()
    {
        long ticks = Interlocked.Read(ref _lastStrongVoiceTicksUtc);
        if (ticks <= 0)
        {
            return false;
        }

        double elapsedSeconds = new TimeSpan(DateTime.UtcNow.Ticks - ticks).TotalSeconds;
        return elapsedSeconds >= 0d && elapsedSeconds <= interruptionConfidenceWindowSeconds;
    }

    private void QueueLatestAudioChunkForSend(byte[] pcmBytes)
    {
        if (pcmBytes == null || pcmBytes.Length == 0)
        {
            return;
        }

        _pendingAudioChunks.Enqueue(pcmBytes);

        if (Interlocked.CompareExchange(ref _audioSendLoopActive, 1, 0) == 0)
        {
            _ = DrainLatestAudioChunksAsync();
        }
    }

    private async Task DrainLatestAudioChunksAsync()
    {
        try
        {
            while (true)
            {
                if (!_pendingAudioChunks.TryDequeue(out byte[] nextChunk))
                {
                    break;
                }

                if (nextChunk == null)
                {
                    continue;
                }

                await SendAudioToAgentAsync(nextChunk).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _audioSendLoopActive, 0);

            if (!_pendingAudioChunks.IsEmpty && Interlocked.CompareExchange(ref _audioSendLoopActive, 1, 0) == 0)
            {
                _ = DrainLatestAudioChunksAsync();
            }
        }
    }

    private float[] ReadMicrophoneInterleavedSamples(int startSampleFrame, int sampleFramesToRead, int channels)
    {
        float[] result = new float[sampleFramesToRead * channels];

        if (startSampleFrame + sampleFramesToRead <= _voiceClip.samples)
        {
            _voiceClip.GetData(result, startSampleFrame);
            return result;
        }

        int tailFrames = _voiceClip.samples - startSampleFrame;
        int headFrames = sampleFramesToRead - tailFrames;

        float[] tail = new float[tailFrames * channels];
        float[] head = new float[headFrames * channels];

        _voiceClip.GetData(tail, startSampleFrame);
        _voiceClip.GetData(head, 0);

        Buffer.BlockCopy(tail, 0, result, 0, tail.Length * sizeof(float));
        Buffer.BlockCopy(head, 0, result, tail.Length * sizeof(float), head.Length * sizeof(float));

        return result;
    }

    private static float[] DownmixToMono(float[] interleavedSamples, int channels)
    {
        if (channels <= 1)
        {
            return interleavedSamples;
        }

        int frameCount = interleavedSamples.Length / channels;
        float[] mono = new float[frameCount];

        for (int frame = 0; frame < frameCount; frame++)
        {
            float sum = 0f;
            int baseIndex = frame * channels;
            for (int ch = 0; ch < channels; ch++)
            {
                sum += interleavedSamples[baseIndex + ch];
            }

            mono[frame] = sum / channels;
        }

        return mono;
    }

    private static bool TryParsePcmSampleRate(string pcmFormat, out int sampleRate)
    {
        sampleRate = 0;
        if (string.IsNullOrWhiteSpace(pcmFormat) || !pcmFormat.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(pcmFormat.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleRate);
    }

    private static byte[] ConvertFloatToPcm16(float[] floatData)
    {
        byte[] pcmBytes = new byte[floatData.Length * 2];

        for (int i = 0; i < floatData.Length; i++)
        {
            float sample = Mathf.Clamp(floatData[i], -1f, 1f);
            short pcmValue = (short)(sample < 0 ? sample * 32768f : sample * 32767f);
            pcmBytes[i * 2] = (byte)(pcmValue & 0xFF);
            pcmBytes[i * 2 + 1] = (byte)((pcmValue >> 8) & 0xFF);
        }

        return pcmBytes;
    }

    private async Task SendAudioToAgentAsync(byte[] pcmAudioData)
    {
        if (!_isConnected || _webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            return;
        }

        if (pcmAudioData == null || pcmAudioData.Length < 100)
        {
            return;
        }

        try
        {
            string base64Audio = Convert.ToBase64String(pcmAudioData);
            string jsonMessage = "{\"type\":\"user_audio_chunk\",\"user_audio_chunk\":\"" + base64Audio + "\"}";
            await SendSocketMessageAsync(jsonMessage, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[AvatarAIController] Error sending audio: " + exception.Message);
        }
    }

    private IEnumerator PlayQueuedAgentAudioRoutine()
    {
        _audioPlaybackLoopRunning = true;

        while (_audioPlaybackQueue.TryDequeue(out string base64Audio))
        {
            AudioClip agentClip = null;

            try
            {
                if (string.IsNullOrWhiteSpace(base64Audio))
                {
                    continue;
                }

                byte[] pcmBytes = Convert.FromBase64String(base64Audio);
                if (pcmBytes.Length < 160)
                {
                    continue;
                }

                agentClip = BuildAudioClipFromPcm16(pcmBytes, _agentOutputSampleRate, 1);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AvatarAIController] Audio decode error: " + ex.Message);
            }

            if (agentClip != null)
            {
                PlayAgentClip(agentClip);

                float clipDuration = Mathf.Max(0.01f, agentClip.length);
                yield return new WaitForSeconds(clipDuration);
            }
        }

        _audioPlaybackLoopRunning = false;
    }

    private void PlayAgentClip(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }

        if (avatarAnimator != null)
        {
            ApplyAnimatorTalkState(true);
        }

        if (lipSyncBlendShape != null)
        {
            lipSyncBlendShape.PlayLipSync(clip);
        }
        else
        {
            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.dopplerLevel = 0f;
            audioSource.clip = clip;
            audioSource.Play();
        }

        if (!string.Equals(_statusLine, "Agent speaking...", StringComparison.Ordinal))
        {
            SetStatus("Agent speaking...");
        }
    }

    private void StopAllPlayback()
    {
        if (lipSyncBlendShape != null)
        {
            AudioSource lipSyncSource = lipSyncBlendShape.GetComponent<AudioSource>();
            if (lipSyncSource != null)
            {
                lipSyncSource.Stop();
            }
        }

        AudioSource ownSource = GetComponent<AudioSource>();
        if (ownSource != null)
        {
            ownSource.Stop();
        }

        if (avatarAnimator != null)
        {
            ApplyAnimatorTalkState(false);
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

    private bool IsAgentSpeaking()
    {
        if (lipSyncBlendShape != null)
        {
            AudioSource lipSyncSource = lipSyncBlendShape.GetComponent<AudioSource>();
            if (lipSyncSource != null && lipSyncSource.isPlaying)
            {
                return true;
            }
        }

        AudioSource ownSource = GetComponent<AudioSource>();
        return ownSource != null && ownSource.isPlaying;
    }

    private static AudioClip BuildAudioClipFromPcm16(byte[] pcmData, int sampleRate, int channels)
    {
        int sampleCount = pcmData.Length / 2;
        float[] samples = new float[sampleCount];
        float peak = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            short value = BitConverter.ToInt16(pcmData, i * 2);
            float sample = value / 32768f;
            samples[i] = sample;

            float absSample = Mathf.Abs(sample);
            if (absSample > peak)
            {
                peak = absSample;
            }
        }

        if (peak > 0.92f)
        {
            float normalizeGain = 0.92f / peak;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = Mathf.Clamp(samples[i] * normalizeGain, -1f, 1f);
            }
        }

        int frames = sampleCount / channels;
        AudioClip clip = AudioClip.Create("AgentAudio", frames, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    private static string ExtractJsonString(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        string marker = "\"" + key + "\"";
        int markerIndex = json.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return string.Empty;
        }

        int colonIndex = json.IndexOf(':', markerIndex + marker.Length);
        if (colonIndex < 0)
        {
            return string.Empty;
        }

        int i = colonIndex + 1;
        while (i < json.Length && char.IsWhiteSpace(json[i]))
        {
            i++;
        }

        if (i >= json.Length || json[i] != '\"')
        {
            return string.Empty;
        }

        i++;
        StringBuilder sb = new StringBuilder();
        bool escaping = false;

        for (; i < json.Length; i++)
        {
            char c = json[i];
            if (escaping)
            {
                switch (c)
                {
                    case '\"': sb.Append('\"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    default: sb.Append(c); break;
                }

                escaping = false;
                continue;
            }

            if (c == '\\')
            {
                escaping = true;
                continue;
            }

            if (c == '\"')
            {
                return sb.ToString();
            }

            sb.Append(c);
        }

        return string.Empty;
    }

    private static long ExtractJsonLong(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
        {
            return 0;
        }

        string marker = "\"" + key + "\"";
        int markerIndex = json.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return 0;
        }

        int colonIndex = json.IndexOf(':', markerIndex + marker.Length);
        if (colonIndex < 0)
        {
            return 0;
        }

        int i = colonIndex + 1;
        while (i < json.Length && (char.IsWhiteSpace(json[i]) || json[i] == '"'))
        {
            i++;
        }

        int start = i;
        while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '-'))
        {
            i++;
        }

        if (i <= start)
        {
            return 0;
        }

        if (long.TryParse(json.Substring(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
        {
            return value;
        }

        return 0;
    }
}
