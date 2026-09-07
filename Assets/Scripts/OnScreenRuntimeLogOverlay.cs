using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// On-headset log HUD for Quest / mobile / device builds. Hooks Unity logs and shows recent lines on a world canvas.
/// </summary>
[DefaultExecutionOrder(-400)]
public sealed class OnScreenRuntimeLogOverlay : MonoBehaviour
{
    public static OnScreenRuntimeLogOverlay Instance { get; private set; }

    private const int DefaultMaxVisibleLines = 16;
    private const int UiLayer = 5;

    [Header("Visibility")]
    [Tooltip("Master switch for the on-screen log HUD.")]
    [SerializeField] private bool enableOverlay = true;

    [Tooltip("Show HUD in Unity Editor Play mode.")]
    [SerializeField] private bool showInUnityEditor;

    [Tooltip("Show HUD on Quest / mobile / XR device builds.")]
    [SerializeField] private bool enableOnVrAndMobileBuilds;

    [Header("Layout")]
    [SerializeField] private int maxVisibleLines = DefaultMaxVisibleLines;
    [SerializeField] private int maxStoredLines = 80;
    [SerializeField] private int fontSize = 20;

    [Header("Filtering")]
    [Tooltip("When on, only lines containing one of the tags below are shown (reduces Quest HUD noise).")]
    [SerializeField] private bool filterToTaggedLogsOnly = true;

    [SerializeField] private string[] logTagPrefixes =
    {
        "[AvatarAIController]",
        "[VoiceDiag]",
        "[AuthTrace]",
        "[UIManager]",
        "[AuthApiClient]",
        "[ConversationHistoryExporter]",
        "[SAVE-",
        "User transcript",
        "[STATUS]",
        "[EnvironmentTeleporter]",
        "[EnvTeleport]",
    };

    private readonly List<string> _formattedLines = new List<string>();
    private readonly StringBuilder _textBuilder = new StringBuilder(2048);
    private readonly List<(string message, LogType type, string stackTrace)> _pendingLogs =
        new List<(string message, LogType type, string stackTrace)>();

    private TextMeshProUGUI _logLabel;
    private TextMeshProUGUI _authStatusLabel;
    private Canvas _hostCanvas;
    private bool _uiBuilt;
    private bool _ownsFallbackCanvas;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapAfterSceneLoad()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return;
#endif

        if (Instance != null)
        {
            return;
        }

        GameObject host = new GameObject(nameof(OnScreenRuntimeLogOverlay));
        host.AddComponent<OnScreenRuntimeLogOverlay>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (!ShouldShowOverlay())
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Application.logMessageReceivedThreaded += OnUnityLogReceivedThreaded;
        StartCoroutine(BuildHudWhenWorldCanvasReady());
    }

    private void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= OnUnityLogReceivedThreaded;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    public static void SetAuthStatus(string message)
    {
        if (Instance == null || Instance._authStatusLabel == null)
        {
            return;
        }

        Instance._authStatusLabel.text = message ?? string.Empty;
    }

    public static void Publish(string message, LogType type = LogType.Log)
    {
        if (Instance == null)
        {
            return;
        }

        if (!Instance._uiBuilt)
        {
            lock (Instance._pendingLogs)
            {
                Instance._pendingLogs.Add((message, type, string.Empty));
            }

            return;
        }

        Instance.HandleLogLine(message, type, string.Empty);
    }

    private bool ShouldShowOverlay()
    {
        if (!enableOverlay)
        {
            return false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        return false;
#endif

        if (Application.isEditor)
        {
            return showInUnityEditor;
        }

        return enableOnVrAndMobileBuilds;
    }

    private IEnumerator BuildHudWhenWorldCanvasReady()
    {
        const float timeoutSeconds = 12f;
        float elapsed = 0f;

        while (_hostCanvas == null && elapsed < timeoutSeconds)
        {
            _hostCanvas = FindPreferredWorldCanvas();
            if (_hostCanvas != null)
            {
                break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_hostCanvas == null)
        {
            _hostCanvas = CreateFallbackCameraCanvas();
            _ownsFallbackCanvas = _hostCanvas != null;
        }

        if (_hostCanvas == null)
        {
            Debug.LogWarning("[OnScreenRuntimeLogOverlay] No Canvas available; HUD not created.");
            yield break;
        }

        BuildHudUnderCanvas(_hostCanvas.transform);
        _uiBuilt = true;
        FlushPendingLogs();
        AppendLine("Runtime HUD ready on canvas: " + _hostCanvas.name, LogType.Log);
        SetAuthStatus("Device log HUD active");
        RefreshLogLabelImmediate();
    }

    private void FlushPendingLogs()
    {
        lock (_pendingLogs)
        {
            for (int i = 0; i < _pendingLogs.Count; i++)
            {
                (string message, LogType type, string stackTrace) pending = _pendingLogs[i];
                HandleLogLine(pending.message, pending.type, pending.stackTrace);
            }

            _pendingLogs.Clear();
        }
    }

    private static Canvas FindPreferredWorldCanvas()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        Canvas best = null;
        int bestChildCount = -1;

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas candidate = canvases[i];
            if (candidate == null || candidate.renderMode != RenderMode.WorldSpace)
            {
                continue;
            }

            int childCount = candidate.transform.childCount;
            if (childCount > bestChildCount)
            {
                best = candidate;
                bestChildCount = childCount;
            }
        }

        return best;
    }

    private static Canvas CreateFallbackCameraCanvas()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Camera[] cameras = FindObjectsOfType<Camera>(true);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].enabled)
                {
                    cam = cameras[i];
                    break;
                }
            }
        }

        if (cam == null)
        {
            return null;
        }

        GameObject canvasObject = new GameObject("RuntimeLogOverlay_FallbackCanvas");
        canvasObject.layer = UiLayer;
        canvasObject.transform.SetParent(cam.transform, false);
        canvasObject.transform.localPosition = new Vector3(0f, -0.22f, 0.55f);
        canvasObject.transform.localRotation = Quaternion.identity;
        canvasObject.transform.localScale = Vector3.one * 0.00115f;

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(920f, 520f);

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        return canvas;
    }

    private void BuildHudUnderCanvas(Transform canvasTransform)
    {
        GameObject hudRoot = new GameObject("RuntimeAuthHud");
        hudRoot.layer = UiLayer;
        hudRoot.transform.SetParent(canvasTransform, false);
        hudRoot.transform.SetAsLastSibling();

        RectTransform hudRect = hudRoot.AddComponent<RectTransform>();
        if (_ownsFallbackCanvas)
        {
            hudRect.anchorMin = new Vector2(0f, 0f);
            hudRect.anchorMax = new Vector2(1f, 1f);
            hudRect.offsetMin = Vector2.zero;
            hudRect.offsetMax = Vector2.zero;
        }
        else
        {
            hudRect.anchorMin = new Vector2(0f, 0f);
            hudRect.anchorMax = new Vector2(1f, 0.32f);
            hudRect.offsetMin = new Vector2(24f, 24f);
            hudRect.offsetMax = new Vector2(-24f, -24f);
        }

        Image backdrop = hudRoot.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.82f);
        backdrop.raycastTarget = false;

        GameObject authStatusObject = new GameObject("AuthStatus");
        authStatusObject.layer = UiLayer;
        authStatusObject.transform.SetParent(hudRoot.transform, false);

        RectTransform authStatusRect = authStatusObject.AddComponent<RectTransform>();
        authStatusRect.anchorMin = new Vector2(0f, 0.62f);
        authStatusRect.anchorMax = new Vector2(1f, 1f);
        authStatusRect.offsetMin = new Vector2(12f, 0f);
        authStatusRect.offsetMax = new Vector2(-12f, -6f);

        _authStatusLabel = authStatusObject.AddComponent<TextMeshProUGUI>();
        ConfigureLabel(_authStatusLabel, 24, new Color(1f, 0.92f, 0.35f, 1f), TextAlignmentOptions.TopLeft);

        GameObject logObject = new GameObject("RuntimeLogText");
        logObject.layer = UiLayer;
        logObject.transform.SetParent(hudRoot.transform, false);

        RectTransform logRect = logObject.AddComponent<RectTransform>();
        logRect.anchorMin = new Vector2(0f, 0f);
        logRect.anchorMax = new Vector2(1f, 0.6f);
        logRect.offsetMin = new Vector2(12f, 8f);
        logRect.offsetMax = new Vector2(-12f, -4f);

        _logLabel = logObject.AddComponent<TextMeshProUGUI>();
        ConfigureLabel(_logLabel, fontSize, Color.white, TextAlignmentOptions.BottomLeft);
    }

    private static void ConfigureLabel(TextMeshProUGUI label, int size, Color color, TextAlignmentOptions alignment)
    {
        label.font = ResolveFontAsset();
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        label.text = string.Empty;
    }

    private static TMP_FontAsset ResolveFontAsset()
    {
        if (TMP_Settings.defaultFontAsset != null)
        {
            return TMP_Settings.defaultFontAsset;
        }

        return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

    private void OnUnityLogReceivedThreaded(string condition, string stackTrace, LogType type)
    {
        if (!_uiBuilt)
        {
            lock (_pendingLogs)
            {
                _pendingLogs.Add((condition, type, stackTrace));
            }

            return;
        }

        _mainThreadLogs.Enqueue((condition, type, stackTrace));
    }

    private readonly Queue<(string message, LogType type, string stackTrace)> _mainThreadLogs =
        new Queue<(string message, LogType type, string stackTrace)>();

    private bool _logLabelDirty;
    private float _nextLogLabelRefreshAt;
    private const float QuestLogRefreshIntervalSeconds = 0.25f;

    private void Update()
    {
        while (_mainThreadLogs.Count > 0)
        {
            (string message, LogType type, string stackTrace) next;
            lock (_mainThreadLogs)
            {
                if (_mainThreadLogs.Count == 0)
                {
                    break;
                }

                next = _mainThreadLogs.Dequeue();
            }

            HandleLogLine(next.message, next.type, next.stackTrace);
        }

        FlushDirtyLogLabelIfNeeded();
    }

    private void LateUpdate()
    {
        FlushDirtyLogLabelIfNeeded();
    }

    private void FlushDirtyLogLabelIfNeeded()
    {
        if (!_logLabelDirty || Time.unscaledTime < _nextLogLabelRefreshAt)
        {
            return;
        }

        _logLabelDirty = false;
        RefreshLogLabelImmediate();
    }

    private void HandleLogLine(string condition, LogType type, string stackTrace)
    {
        if (!PassesLogFilter(condition, type))
        {
            return;
        }

        AppendLine(condition, type);

        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            AppendStackTrace(stackTrace);
        }

        RefreshLogLabel();
    }

    private bool PassesLogFilter(string condition, LogType type)
    {
        if (!filterToTaggedLogsOnly)
        {
            return true;
        }

        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            return true;
        }

        if (string.IsNullOrEmpty(condition) || logTagPrefixes == null || logTagPrefixes.Length == 0)
        {
            return true;
        }

        for (int i = 0; i < logTagPrefixes.Length; i++)
        {
            string prefix = logTagPrefixes[i];
            if (!string.IsNullOrEmpty(prefix) && condition.IndexOf(prefix, System.StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private void AppendLine(string message, LogType type)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string sanitized = SanitizeForDisplay(message);
        _formattedLines.Add(FormatPrefix(type) + sanitized);

        int overflow = _formattedLines.Count - Mathf.Max(1, maxStoredLines);
        if (overflow > 0)
        {
            _formattedLines.RemoveRange(0, overflow);
        }
    }

    private void AppendStackTrace(string stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return;
        }

        _formattedLines.Add("<color=#FF9E9E>" + SanitizeForDisplay(stackTrace) + "</color>");

        int overflow = _formattedLines.Count - Mathf.Max(1, maxStoredLines);
        if (overflow > 0)
        {
            _formattedLines.RemoveRange(0, overflow);
        }
    }

    private void RefreshLogLabel()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_logLabel == null)
        {
            return;
        }

        _logLabelDirty = true;
        _nextLogLabelRefreshAt = Time.unscaledTime + QuestLogRefreshIntervalSeconds;
#else
        RefreshLogLabelImmediate();
#endif
    }

    private void RefreshLogLabelImmediate()
    {
        if (_logLabel == null)
        {
            return;
        }

        _textBuilder.Clear();
        int startIndex = Mathf.Max(0, _formattedLines.Count - Mathf.Max(1, maxVisibleLines));
        for (int i = startIndex; i < _formattedLines.Count; i++)
        {
            if (_textBuilder.Length > 0)
            {
                _textBuilder.Append('\n');
            }

            _textBuilder.Append(_formattedLines[i]);
        }

        _logLabel.text = _textBuilder.ToString();
    }

    private static string FormatPrefix(LogType type)
    {
        switch (type)
        {
            case LogType.Error:
            case LogType.Exception:
            case LogType.Assert:
                return "<color=#FF6B6B>[ERR] </color>";
            case LogType.Warning:
                return "<color=#FFD166>[WRN] </color>";
            default:
                return "<color=#9BE7FF>[LOG] </color>";
        }
    }

    private static string SanitizeForDisplay(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        return raw.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}
