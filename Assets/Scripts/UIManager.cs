using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

public class UIManager : MonoBehaviour
{
    [Header("Auth Panels")]
    [SerializeField] private GameObject signInPanel;
    [SerializeField] private GameObject signUpPanel;
    [SerializeField] private GameObject userIdConfirmationPanel;

    [Header("User ID Confirmation (after sign-up only)")]
    [SerializeField] private TextMeshProUGUI userIdConfirmationDisplayText;
    [SerializeField] private Button dismissUserIdPanelButton;

    [Header("Sign In")]
    [SerializeField] private TMP_InputField signInUserIdField;
    [SerializeField] private Button signInButton;

    [Header("Sign Up")]
    [SerializeField] private TMP_InputField signUpNameField;
    [SerializeField] private TMP_InputField signUpAgeField;
    [SerializeField] private TMP_InputField signUpIntroField;
    [SerializeField] private Button signUpButton;

    [Header("Panel Switch Buttons")]
    [SerializeField] private Button switchToSignUpButton;
    [SerializeField] private Button switchToSignInButton;

    [Header("Status")]
    [SerializeField] private TextMeshProUGUI statusText;

    private TextMeshProUGUI _authDebugStatusText;

    [Header("PostgreSQL Connection")]
    [SerializeField] private string postgresHost = "localhost";
    [SerializeField] private int postgresPort = 5432;
    [SerializeField] private string postgresDatabaseName = string.Empty;
    [SerializeField] private string postgresUsername = string.Empty;
    [SerializeField] private string postgresPassword = string.Empty;
    [SerializeField] private bool postgresRequireSsl = true;
    [SerializeField] private string connectionStringOverride = string.Empty;

    [Header("Auth API (Quest — HTTPS instead of direct PostgreSQL)")]
    [Tooltip("When enabled, sign-in/sign-up/context use authApiBaseUrl (works on Quest). Direct PostgreSQL is fallback for Editor/PC. "
             + "On Quest APK builds, Resources/AuthApiRuntimeConfig.asset overrides these fields when Override Inspector On Device Builds is on.")]
    [SerializeField] private bool useAuthApi = true;

    [SerializeField] private string authApiBaseUrl = "https://vr-proj-production.up.railway.app";
    [SerializeField] private string authApiKey = string.Empty;

    [Tooltip("On Quest/Android, connect using the first resolved IPv4 address instead of the hostname.")]
    [SerializeField] private bool preferIpv4HostOnQuest = true;

    [Header("Behavior")]
    [SerializeField] private bool autoFindUiReferences = true;

    [Tooltip(
        "After sign-up success: hides credential panels and shows User Id Confirmation Panel (if assigned). " +
        "After sign-in success: hides credential panels only — user ID panel stays off.")]
    [SerializeField]
    private bool hideAuthUiOnSuccess = true;

    [Header("Dev bypass (temporary — Quest PostgreSQL auth)")]
    [Tooltip("Skips sign-in UI and PostgreSQL. Turn OFF to test Quest PostgreSQL fixes.")]
    [SerializeField] private bool useDevAuthBypass = false;

    [SerializeField] private string devAuthBypassUserId = "V-010";

    public string CurrentUserId { get; private set; } = string.Empty;

    public bool UseAuthApi => useAuthApi && AuthApiClient.IsConfigured(authApiBaseUrl);

    public string AuthApiBaseUrl => authApiBaseUrl;

    public string AuthApiKey => authApiKey;

    public event Action<string> OnSignedIn;
    public event Action<string> OnSignedUp;

    private TMP_InputField _signInIdSubmitHookTarget;

    private static readonly object AuthDatabaseGate = new object();

    private bool _authSubmitInFlight;

    private float _nextSignInAllowedUnscaledTime;

    /// <summary>Cached on Awake — XRSettings must not be read from background threads (e.g. prompt context fetch).</summary>
    private bool _questLikeRuntime;

    private static bool s_questLikeRuntime;

    /// <summary>Where auth API URL/key came from (scene vs Resources) — shown in Quest HUD logs.</summary>
    private string _authApiConfigSource = "Scene/UIManager";

    private void Awake()
    {
        _questLikeRuntime = Application.isMobilePlatform || XRSettings.isDeviceActive;
        s_questLikeRuntime = _questLikeRuntime;
        ApplyAuthApiRuntimeConfigIfNeeded();
    }

    /// <summary>
    /// Quest APK uses Resources/AuthApiRuntimeConfig (always bundled). Scene Inspector values are easy to forget saving before build.
    /// </summary>
    private void ApplyAuthApiRuntimeConfigIfNeeded()
    {
        AuthApiRuntimeConfig config = AuthApiRuntimeConfig.Instance;
        if (config == null)
        {
            if (!Application.isEditor && useAuthApi)
            {
                Debug.LogWarning(
                    "[UIManager] Resources/AuthApiRuntimeConfig not found. Using scene UIManager values. "
                    + "Create Assets/Resources/AuthApiRuntimeConfig.asset for reliable Quest builds.");
            }

            return;
        }

        if (!config.ShouldOverrideInspectorOnThisBuild())
        {
            return;
        }

        string sceneBaseUrl = authApiBaseUrl;
        useAuthApi = config.useAuthApi;

        if (config.HasUsableBaseUrl())
        {
            authApiBaseUrl = config.baseUrl.Trim().TrimEnd('/');
            _authApiConfigSource = "Resources/AuthApiRuntimeConfig";
        }
        else if (!string.IsNullOrWhiteSpace(sceneBaseUrl))
        {
            authApiBaseUrl = sceneBaseUrl;
            _authApiConfigSource = "Scene/UIManager (Resources baseUrl invalid)";
            Debug.LogWarning(
                "[UIManager] AuthApiRuntimeConfig.baseUrl is invalid; keeping scene URL: " + sceneBaseUrl);
        }

        if (!string.IsNullOrWhiteSpace(config.apiKey))
        {
            authApiKey = config.apiKey;
        }
    }

    private static bool IsQuestLikeRuntime()
    {
        return s_questLikeRuntime;
    }

    /// <summary>
    /// Resolves binding references, then exposes a TMP input usable as an Instantiate template (auth fields stay on deactivated panels otherwise).
    /// </summary>
    public TMP_InputField GetAuthBackedTmpInputCloneTemplateOrNull()
    {
        BindUiReferencesIfNeeded();

        return ResolveSignInUserIdTmp(false) ?? signUpNameField;
    }

    private static string NormalizeCredentialText(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        string trimmed = raw.Trim();
        trimmed = trimmed.Replace("\u200B", string.Empty);
        trimmed = trimmed.Replace("\uFEFF", string.Empty);
        return trimmed.Trim();
    }

    /// <summary>
    /// TMP backing string (<see cref="TMP_InputField.text"/>) versus what <see cref="TMP_InputField.textComponent"/> renders.
    /// Standalone Quest can show typed glyphs while the property still reads empty (VR soft-input / IME suppression quirks).
    /// </summary>
    private static string GetEffectiveNormalizedTmpCredentialText(TMP_InputField field)
    {
        return TmpCredentialFieldUtility.ReadEffectiveCredentialText(field);
    }

    /// <summary>
    /// Selects which <see cref="TMP_InputField"/> collects the login id — prefer one under <see cref="signInPanel"/> that already holds typed text when requested.
    /// </summary>
    private TMP_InputField ResolveSignInUserIdTmp(bool preferTypedVisibleText)
    {
        BindUiReferencesIfNeeded();

        if (signInPanel == null)
        {
            return signInUserIdField;
        }

        TMP_InputField[] scoped = signInPanel.GetComponentsInChildren<TMP_InputField>(true);
        if (scoped == null || scoped.Length == 0)
        {
            return signInUserIdField;
        }

        if (preferTypedVisibleText)
        {
            for (int i = 0; i < scoped.Length; i++)
            {
                TMP_InputField f = scoped[i];
                if (f == null || !f.gameObject.activeInHierarchy || !f.isActiveAndEnabled)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(GetEffectiveNormalizedTmpCredentialText(f)))
                {
                    return f;
                }
            }
        }

        if (scoped.Length == 1)
        {
            return scoped[0];
        }

        if (signInUserIdField != null && Array.IndexOf(scoped, signInUserIdField) >= 0)
        {
            return signInUserIdField;
        }

        return PreferUserIdScopedField(scoped) ?? signInUserIdField;
    }

    private void RefreshSignInUserIdSubmitHook()
    {
        TMP_InputField next = ResolveSignInUserIdTmp(preferTypedVisibleText: true);
        if (next == null)
        {
            next = ResolveSignInUserIdTmp(preferTypedVisibleText: false);
        }

        if (next == null)
        {
            return;
        }

        if (_signInIdSubmitHookTarget != next)
        {
            if (_signInIdSubmitHookTarget != null)
            {
                _signInIdSubmitHookTarget.onSubmit.RemoveListener(OnSignInInputSubmitted);
                _signInIdSubmitHookTarget.onEndEdit.RemoveListener(OnSignInEndEdit);
            }

            next.onSubmit.RemoveListener(OnSignInInputSubmitted);
            next.onSubmit.AddListener(OnSignInInputSubmitted);
            next.onEndEdit.RemoveListener(OnSignInEndEdit);
            next.onEndEdit.AddListener(OnSignInEndEdit);

            signInUserIdField = next;
            _signInIdSubmitHookTarget = next;
        }
    }

    private void OnSignInEndEdit(string submittedValue)
    {
        if (_signInIdSubmitHookTarget == null)
        {
            return;
        }

        TmpCredentialFieldUtility.WriteSession(_signInIdSubmitHookTarget, submittedValue);
    }

    private void Start()
    {
        BindUiReferencesIfNeeded();
        HideUserIdConfirmationPanelInternal();
        RegisterUiEvents();

        if (useDevAuthBypass && !string.IsNullOrWhiteSpace(devAuthBypassUserId))
        {
            StartCoroutine(ApplyDevAuthBypassDeferred());
            return;
        }

        SetAuthButtonsVisible(true);
        ShowSignInPanel();
        SetStatus("Sign in or create a new account.");
        LogAuthApiBootConfig();
        AuthFlowTrace.Step(
            "BOOT-001",
            "Auth UI wired signInButton=" + (signInButton != null) +
            " signUpButton=" + (signUpButton != null) +
            " signInPanel=" + (signInPanel != null) +
            " signUpPanel=" + (signUpPanel != null));
    }

    private void LogAuthApiBootConfig()
    {
        AuthFlowTrace.Step(
            "BOOT-API",
            "source=" + _authApiConfigSource
            + " useAuthApi=" + useAuthApi
            + " configured=" + UseAuthApi
            + " baseUrl=" + (string.IsNullOrWhiteSpace(authApiBaseUrl) ? "(empty)" : authApiBaseUrl)
            + " hasApiKey=" + !string.IsNullOrWhiteSpace(authApiKey)
            + " questLike=" + _questLikeRuntime);

        if (!useAuthApi)
        {
            return;
        }

        if (!UseAuthApi)
        {
            const string hint = "Set Auth Api Base Url on UI Handler (https://your-app.up.railway.app, no trailing slash).";
            SetStatus(hint);
            Debug.LogError("[UIManager] Auth API base URL is missing or invalid. " + hint);
            OnScreenRuntimeLogOverlay.SetAuthStatus("Auth API URL not set");
            return;
        }

        if (string.IsNullOrWhiteSpace(authApiKey))
        {
            const string hint = "Set Auth Api Key on UI Handler (same value as Railway API_KEY).";
            SetStatus(hint);
            Debug.LogWarning("[UIManager] Auth API key is empty. " + hint);
            OnScreenRuntimeLogOverlay.SetAuthStatus("Auth API key missing");
        }
    }

    private IEnumerator ApplyDevAuthBypassDeferred()
    {
        yield return null;

        ApplyDevAuthBypass();
    }

    private void ApplyDevAuthBypass()
    {
        CurrentUserId = devAuthBypassUserId.Trim();
        HideUserIdConfirmationPanelInternal();
        SetPanelState(signInVisible: false, signUpVisible: false);
        SetAuthButtonsVisible(false);
        SetStatus("Signed in as " + CurrentUserId + " (dev bypass — no database check).");

        AuthFlowTrace.Step("DEV-AUTH", "bypass active user_id=" + CurrentUserId + " panels=hidden");
        Debug.Log("[UIManager] Dev auth bypass: signed in as " + CurrentUserId + " without PostgreSQL.");
        OnSignedIn?.Invoke(CurrentUserId);
    }

    private void OnDestroy()
    {
        UnregisterUiEvents();
    }

    public void ShowSignInPanel()
    {
        HideUserIdConfirmationPanelInternal();
        SetPanelState(signInVisible: true, signUpVisible: false);
        SetAuthButtonsVisible(true);
        RefreshSignInUserIdSubmitHook();
    }

    public void ShowSignUpPanel()
    {
        HideUserIdConfirmationPanelInternal();
        SetPanelState(signInVisible: false, signUpVisible: true);
        SetAuthButtonsVisible(true);
    }

    public void OnSwitchToSignInClicked()
    {
        ShowSignInPanel();
        SetStatus("Sign in with your user ID.");
    }

    public void OnSwitchToSignUpClicked()
    {
        ShowSignUpPanel();
        SetStatus("Create a new profile.");
    }

    private void OnSignInInputSubmitted(string submittedValue)
    {
        if (_signInIdSubmitHookTarget != null)
        {
            TmpCredentialFieldUtility.WriteSession(_signInIdSubmitHookTarget, submittedValue);
        }
    }

    public void OnSignUpClicked()
    {
        AuthFlowTrace.Step("SignUp-UI-001", "Sign-up tapped.");

        if (_authSubmitInFlight)
        {
            AuthFlowTrace.Step("SignUp-UI-007", "blocked: auth database request already in flight.", LogType.Warning);
            SetStatus("Sign-in or sign-up is already running. Wait for the database response.");
            return;
        }

        TmpCredentialFieldUtility.CaptureCredentialFieldsBeforeSubmit(signUpPanel);
        PrepareAuthCredentialFields();

        string fullName = GetEffectiveNormalizedTmpCredentialText(signUpNameField);
        string ageText = GetEffectiveNormalizedTmpCredentialText(signUpAgeField);
        string introText = GetEffectiveNormalizedTmpCredentialText(signUpIntroField);

        if (string.IsNullOrWhiteSpace(fullName))
        {
            AuthFlowTrace.Step("SignUp-UI-002", "blocked: empty full name.", LogType.Warning);
            SetStatus("Enter your name.");
            return;
        }

        if (string.IsNullOrWhiteSpace(ageText) || !int.TryParse(ageText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int age))
        {
            AuthFlowTrace.Step("SignUp-UI-003", "blocked: invalid age textLen=" + (ageText != null ? ageText.Length : 0), LogType.Warning);
            SetStatus("Enter a valid age.");
            return;
        }

        if (age < 5 || age > 120)
        {
            AuthFlowTrace.Step("SignUp-UI-004", "blocked: age out of range age=" + age, LogType.Warning);
            SetStatus("Age must be between 5 and 120.");
            return;
        }

        if (string.IsNullOrWhiteSpace(introText))
        {
            AuthFlowTrace.Step("SignUp-UI-005", "blocked: empty intro.", LogType.Warning);
            SetStatus("Enter a short intro.");
            return;
        }

        AuthFlowTrace.Step(
            "SignUp-UI-006",
            "resolved nameLen=" + fullName.Length + " age=" + age + " introLen=" + introText.Length);

        int attemptId = AuthFlowTrace.BeginAttempt("SignUp");
        _authSubmitInFlight = true;

        if (UseAuthApi)
        {
            StartCoroutine(SignUpViaApiCoroutine(attemptId, fullName, age, introText));
            return;
        }

        try
        {
            if (!TrySignUp(fullName, age, introText, out string userId, out string error))
            {
                AuthFlowTrace.Step(
                    "SignUp-A099",
                    "attempt=" + attemptId + " success=false error=\"" + (error ?? string.Empty) + "\"",
                    LogType.Error);
                SetStatus(FormatAuthDatabaseError(error));
                return;
            }

            AuthFlowTrace.Step("SignUp-A099", "attempt=" + attemptId + " success=true userIdLen=" + userId.Length);
            CompleteSignUpSuccess(userId);
        }
        finally
        {
            _authSubmitInFlight = false;
        }
    }

    private IEnumerator SignUpViaApiCoroutine(int attemptId, string fullName, int age, string introText)
    {
        AuthFlowTrace.Step("SignUp-API-001", "POST sign-up baseUrl=" + authApiBaseUrl);

        bool succeeded = false;
        string userId = string.Empty;
        string error = string.Empty;

        yield return AuthApiClient.SignUpCoroutine(
            authApiBaseUrl,
            authApiKey,
            fullName,
            age,
            introText,
            (ok, returnedUserId, apiError) =>
            {
                succeeded = ok;
                userId = returnedUserId ?? string.Empty;
                error = apiError ?? string.Empty;
            });

        try
        {
            if (!succeeded)
            {
                AuthFlowTrace.Step(
                    "SignUp-A099",
                    "attempt=" + attemptId + " success=false error=\"" + error + "\"",
                    LogType.Error);
                SetStatus(FormatAuthDatabaseError(error));
                yield break;
            }

            AuthFlowTrace.Step("SignUp-A099", "attempt=" + attemptId + " success=true userIdLen=" + userId.Length);
            CompleteSignUpSuccess(userId);
        }
        finally
        {
            _authSubmitInFlight = false;
        }
    }

    private void CompleteSignUpSuccess(string userId)
    {
        CurrentUserId = userId;
        TMP_InputField signInWritable = ResolveSignInUserIdTmp(preferTypedVisibleText: false);
        if (signInWritable != null)
        {
            signInWritable.text = userId;
        }

        Debug.Log("[UIManager] Sign-up success. Active user_id=" + userId + ".");
        SetStatus($"Sign-up complete. Your user ID is {userId}");
        OnSignedUp?.Invoke(userId);

        if (hideAuthUiOnSuccess)
        {
            PresentAuthenticatedUserIdConfirmation(userId);
        }
    }

    public void OnSignInClicked()
    {
        if (useDevAuthBypass)
        {
            ApplyDevAuthBypass();
            return;
        }

        AuthFlowTrace.Step("UI-001", "Sign-in tapped.");
        SetStatus("Sign-in tapped.");
        TmpCredentialFieldUtility.CaptureCredentialFieldsBeforeSubmit(signInPanel);
        PrepareAuthCredentialFields();
        RefreshSignInUserIdSubmitHook();

        string userId = TmpCredentialFieldUtility.ReadBestCredentialTextOnPanel(signInPanel);

        if (string.IsNullOrWhiteSpace(userId))
        {
            TMP_InputField readSrc = ResolveSignInUserIdTmp(preferTypedVisibleText: true);
            readSrc ??= ResolveSignInUserIdTmp(preferTypedVisibleText: false);
            TmpCredentialFieldUtility.FlushPendingVrKeyboardEdits(readSrc);
            userId = GetEffectiveNormalizedTmpCredentialText(readSrc);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            TMP_InputField readSrc = ResolveSignInUserIdTmp(preferTypedVisibleText: true);
            readSrc ??= ResolveSignInUserIdTmp(preferTypedVisibleText: false);
            TmpCredentialFieldUtility.FlushPendingVrKeyboardEdits(readSrc);
            AuthCredentialDiagnosticReport.PublishSignInFailureChecklist(signInPanel, readSrc, userId);
            AuthFlowTrace.Step("UI-002", "blocked: empty user id after credential resolve.", LogType.Warning);
            return;
        }

        AuthFlowTrace.Step(
            "UI-003",
            "resolved userIdLen=" + userId.Length +
            " panelActive=" + (signInPanel != null && signInPanel.activeInHierarchy));

        if (_authSubmitInFlight)
        {
            AuthFlowTrace.Step("UI-004", "blocked: sign-in already in flight.", LogType.Warning);
            SetStatus("Sign-in already running. Wait for the database response.");
            return;
        }

        if (Time.unscaledTime < _nextSignInAllowedUnscaledTime)
        {
            AuthFlowTrace.Step("UI-005", "blocked: debounced duplicate submit.", LogType.Warning);
            return;
        }

        _nextSignInAllowedUnscaledTime = Time.unscaledTime + 1.5f;
        _authSubmitInFlight = true;
        StartCoroutine(SignInAfterCredentialResolvedCoroutine(userId));
    }

    /// <summary>Sign-in via Auth API (Quest) or direct PostgreSQL (Editor fallback).</summary>
    private IEnumerator SignInAfterCredentialResolvedCoroutine(string userId)
    {
        int attemptId = AuthFlowTrace.BeginAttempt("SignIn");
        SetAuthSubmitInteractable(false);
        SetStatus("Signing in...");

        yield return null;
        yield return new WaitForSecondsRealtime(0.2f);

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        bool signInSucceeded = false;
        string signInError = string.Empty;

        if (UseAuthApi)
        {
            AuthFlowTrace.Step("SignIn-API-001", "POST sign-in baseUrl=" + authApiBaseUrl);
            yield return AuthApiClient.SignInCoroutine(
                authApiBaseUrl,
                authApiKey,
                userId,
                (ok, apiError) =>
                {
                    signInSucceeded = ok;
                    signInError = apiError ?? string.Empty;
                });
        }
        else
        {
            int maxSignInAttempts = IsQuestLikeRuntime() ? 5 : 3;

            for (int signInAttempt = 1; signInAttempt <= maxSignInAttempts; signInAttempt++)
            {
                if (signInAttempt > 1)
                {
                    AuthFlowTrace.Step(
                        "SignIn-RETRY",
                        "retrying after transient socket overlap attempt=" + signInAttempt,
                        LogType.Warning);
                    yield return new WaitForSecondsRealtime(
                        signInAttempt > 2 ? 1.5f : 0.85f);
                }

                signInSucceeded = TrySignIn(userId, out signInError);
                if (signInSucceeded || !IsTransientAuthSocketProgressError(signInError))
                {
                    break;
                }
            }
        }

        stopwatch.Stop();

        try
        {
            AuthFlowTrace.Step(
                "SignIn-A099",
                "attempt=" + attemptId +
                " success=" + signInSucceeded +
                " elapsed_ms=" + stopwatch.ElapsedMilliseconds +
                " error=\"" + (signInError ?? string.Empty) + "\"",
                signInSucceeded ? LogType.Log : LogType.Error);

            if (!signInSucceeded)
            {
                string formattedError = FormatAuthDatabaseError(signInError);
                SetStatus(formattedError);
                if (!UseAuthApi)
                {
                    AuthCredentialDiagnosticReport.PublishPostgresConnectFailureSummary(
                        NormalizePostgresHost(postgresHost),
                        postgresPort,
                        signInError,
                        Application.internetReachability);
                }

                yield break;
            }

            CurrentUserId = userId;
            Debug.Log("[UIManager] Sign-in success. Active user_id=" + userId + ".");
            SetStatus($"Signed in as {userId}");
            OnSignedIn?.Invoke(userId);
            StartCoroutine(DeferredUpdateLastLoginCoroutine(userId));

            if (hideAuthUiOnSuccess)
            {
                HideUserIdConfirmationPanelInternal();
                SetPanelState(false, false);
                SetAuthButtonsVisible(false);
            }
        }
        finally
        {
            _authSubmitInFlight = false;
            SetAuthSubmitInteractable(true);
        }
    }

    /// <summary>Writes last_login_at after sign-in succeeds so the auth path only needs one PostgreSQL round trip.</summary>
    private IEnumerator DeferredUpdateLastLoginCoroutine(string userId)
    {
        yield return new WaitForSecondsRealtime(1.5f);

        if (string.IsNullOrWhiteSpace(userId))
        {
            yield break;
        }

        if (UseAuthApi)
        {
            AuthFlowTrace.Step("SignIn-API-DEFER", "writing last_login_at userIdLen=" + userId.Length);
            yield return AuthApiClient.UpdateLastLoginCoroutine(authApiBaseUrl, authApiKey, userId, null);
            yield break;
        }

        if (!TryCreateConnectionString(out _, out string connectionString))
        {
            yield break;
        }

        AuthFlowTrace.Step("SignIn-DB-DEFER", "writing last_login_at userIdLen=" + userId.Length);
        TryExecuteNonQuery(
            connectionString,
            "UPDATE users SET last_login_at = NOW() WHERE user_id = @user_id;",
            new Dictionary<string, object> { { "@user_id", userId } },
            out _);
    }

    private void SetAuthSubmitInteractable(bool interactable)
    {
        if (signInButton != null)
        {
            signInButton.interactable = interactable;
        }

        if (signUpButton != null)
        {
            signUpButton.interactable = interactable;
        }
    }

    private static string FormatAuthDatabaseError(string rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "Sign-in failed. Database returned no details.";
        }

        if (rawError.StartsWith("[NOT_FOUND]", StringComparison.Ordinal))
        {
            return rawError.Substring("[NOT_FOUND]".Length).Trim();
        }

        if (rawError.StartsWith("[CONNECT]", StringComparison.Ordinal))
        {
            return "The headset could not open PostgreSQL before checking your user ID. A wrong ID would show the same message until connection works. "
                + rawError.Substring("[CONNECT]".Length).Trim();
        }

        return "Sign-in failed: " + rawError;
    }

    /// <summary>Returns true when Npgsql reports a non-blocking socket overlap that can clear on a single retry.</summary>
    private static bool IsTransientAuthSocketProgressError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.IndexOf("in progress", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("InProgress", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("10035", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("10036", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("WOULDBLOCK", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("EINPROGRESS", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Hides sign-in / sign-up panels and shows the user-id confirmation panel (new accounts only).
    /// </summary>
    private void PresentAuthenticatedUserIdConfirmation(string userId)
    {
        HideUserIdConfirmationPanelInternal();

        SetPanelState(false, false);
        SetAuthButtonsVisible(false);

        if (userIdConfirmationDisplayText != null)
        {
            userIdConfirmationDisplayText.text = "Your User ID:\n<size=+8><b>" + userId + "</b></size>";
        }

        if (userIdConfirmationPanel != null)
        {
            userIdConfirmationPanel.SetActive(true);
        }
    }

    private void HideUserIdConfirmationPanelInternal()
    {
        if (userIdConfirmationPanel != null)
        {
            userIdConfirmationPanel.SetActive(false);
        }
    }

    /// <summary>Runs when Continue / Done on the confirmation panel closes it (optional).</summary>
    private void OnDismissUserIdPanelClicked()
    {
        HideUserIdConfirmationPanelInternal();
    }

    public bool TrySignUp(string fullName, int age, string introText, out string userId, out string error)
    {
        userId = string.Empty;
        error = string.Empty;
        AuthFlowTrace.Step(
            "SignUp-DB-001",
            "TrySignUp enter nameLen=" + (fullName != null ? fullName.Length : 0) +
            " age=" + age +
            " introLen=" + (introText != null ? introText.Length : 0));

        if (!TryCreateConnectionString(out string connectionStringError, out string connectionString))
        {
            error = connectionStringError;
            AuthFlowTrace.Step("SignUp-DB-002", "connectionString rejected: " + connectionStringError, LogType.Error);
            return false;
        }

        AuthFlowTrace.LogPostgresTarget(
            "SignUp-DB-003",
            NormalizePostgresHost(postgresHost),
            postgresPort,
            postgresDatabaseName,
            postgresUsername,
            postgresRequireSsl);
        AuthFlowTrace.Step(
            "SignUp-DB-003a",
            "connectionStringOverride=" + !string.IsNullOrWhiteSpace(connectionStringOverride));

        if (!TryExecuteScalar(
                connectionString,
                "INSERT INTO users (full_name, age, intro_text) VALUES (@full_name, @age, @intro_text) RETURNING user_id;",
                new Dictionary<string, object>
                {
                    { "@full_name", fullName },
                    { "@age", age },
                    { "@intro_text", introText }
                },
                out object result,
                out string dbError,
                "SignUp-DB"))
        {
            error = dbError;
            return false;
        }

        userId = Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userId))
        {
            error = "Sign-up succeeded, but no user ID was returned.";
            AuthFlowTrace.Step("SignUp-DB-012", error, LogType.Error);
            return false;
        }

        AuthFlowTrace.Step("SignUp-DB-015", "INSERT complete userIdLen=" + userId.Length);
        return true;
    }

    public bool TrySignIn(string userId, out string error)
    {
        error = string.Empty;
        AuthFlowTrace.Step("SignIn-DB-001", "TrySignIn enter userIdLen=" + (userId != null ? userId.Length : 0));

        if (!TryCreateConnectionString(out string connectionStringError, out string connectionString))
        {
            error = connectionStringError;
            AuthFlowTrace.Step("SignIn-DB-002", "connectionString rejected: " + connectionStringError, LogType.Error);
            return false;
        }

        AuthFlowTrace.LogPostgresTarget(
            "SignIn-DB-003",
            NormalizePostgresHost(postgresHost),
            postgresPort,
            postgresDatabaseName,
            postgresUsername,
            postgresRequireSsl);
        AuthFlowTrace.Step(
            "SignIn-DB-003a",
            "connectionStringOverride=" + !string.IsNullOrWhiteSpace(connectionStringOverride));

        string normalizedHost = NormalizePostgresHost(postgresHost);
        if (!TryRunPostgresConnectPreflight(normalizedHost, postgresPort, out string preflightError))
        {
            error = preflightError;
            return false;
        }

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        lock (AuthDatabaseGate)
        {
            AuthFlowTrace.Step(
                "SignIn-DB-004",
                "database gate acquired thread=" + System.Threading.Thread.CurrentThread.ManagedThreadId +
                " ms=" + stopwatch.ElapsedMilliseconds);

            if (!TryGetNpgsqlConnectionType(out Type connectionType, out string typeError))
            {
                error = typeError;
                AuthFlowTrace.Step("SignIn-DB-005", "Npgsql type missing: " + typeError, LogType.Error);
                return false;
            }

            AuthFlowTrace.Step("SignIn-DB-006", "Npgsql type=" + connectionType.FullName);

            object connection = null;
            object command = null;

            try
            {
                if (!TryOpenFreshNpgsqlConnection(
                        connectionType,
                        connectionString,
                        "SignIn-DB",
                        stopwatch,
                        out connection,
                        out error))
                {
                    if (!string.IsNullOrWhiteSpace(error) && !error.StartsWith("[", StringComparison.Ordinal))
                    {
                        error = "[CONNECT] " + error;
                    }

                    return false;
                }

                command = InvokeZeroArgMethod(connectionType, connection, "CreateCommand");
                if (command == null)
                {
                    error = "Could not create PostgreSQL command.";
                    AuthFlowTrace.Step("SignIn-DB-009", error, LogType.Error);
                    return false;
                }

                SetCommandText(command, "SELECT user_id FROM users WHERE user_id = @user_id AND is_active = TRUE LIMIT 1;");
                AddParameters(command, new Dictionary<string, object> { { "@user_id", userId } });
                AuthFlowTrace.Step("SignIn-DB-010", "executing SELECT ms=" + stopwatch.ElapsedMilliseconds);
                object result = InvokeZeroArgMethod(command.GetType(), command, "ExecuteScalar");
                AuthFlowTrace.Step(
                    "SignIn-DB-011",
                    "SELECT complete resultNull=" + (result == null || result == DBNull.Value) +
                    " ms=" + stopwatch.ElapsedMilliseconds);

                string matchedUserId = Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(matchedUserId))
                {
                    error = "[NOT_FOUND] User ID not found or inactive.";
                    AuthFlowTrace.Step("SignIn-DB-012", error, LogType.Warning);
                    return false;
                }

                AuthFlowTrace.Step("SignIn-DB-015", "sign-in verified total_ms=" + stopwatch.ElapsedMilliseconds);
                return true;
            }
            catch (TargetInvocationException exception)
            {
                error = "[CONNECT] " + ExtractExceptionMessage(exception);
                AuthFlowTrace.StepException("SignIn-DB-ERR", "failure total_ms=" + stopwatch.ElapsedMilliseconds, exception);
                return false;
            }
            catch (Exception exception)
            {
                error = "[CONNECT] " + exception.Message;
                AuthFlowTrace.StepException("SignIn-DB-ERR", "failure total_ms=" + stopwatch.ElapsedMilliseconds, exception);
                return false;
            }
            finally
            {
                DisposeIfPossible(command);
                AbortNpgsqlConnection(connectionType, connection);
                AuthFlowTrace.Step("SignIn-DB-016", "connection disposed total_ms=" + stopwatch.ElapsedMilliseconds);
            }
        }
    }

    public bool TryBuildConversationPromptContext(string userId, int historyLimit, out string contextJson, out string error)
    {
        contextJson = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(userId))
        {
            error = "User ID is missing.";
            return false;
        }

        if (useDevAuthBypass)
        {
            contextJson = "{\"user_id\":\"" + userId + "\",\"dev_bypass\":true,\"history\":[]}";
            Debug.Log("[UIManager] Prompt context skipped (dev bypass). user_id=" + userId + ".");
            return true;
        }

        if (UseAuthApi)
        {
            return AuthApiClient.TryGetUserContext(
                authApiBaseUrl,
                authApiKey,
                userId,
                historyLimit,
                out contextJson,
                out error);
        }

        if (!TryCreateConnectionString(out string connectionStringError, out string connectionString))
        {
            error = connectionStringError;
            return false;
        }

        int safeLimit = Mathf.Clamp(historyLimit, 1, 100);

        const string sql = @"
SELECT json_build_object(
    'user_id', u.user_id,
    'full_name', u.full_name,
    'age', u.age,
    'intro_text', u.intro_text,
    'history', COALESCE(
        (
            SELECT json_agg(
                json_build_object(
                    'timestamp', h.turn_timestamp_iso,
                    'summary', h.summary
                )
                ORDER BY h.turn_timestamp_iso DESC
            )
            FROM (
                SELECT turn_timestamp_iso, summary
                FROM conversation_history
                WHERE user_id = u.user_id
                ORDER BY turn_timestamp_iso DESC
                LIMIT @history_limit
            ) h
        ),
        '[]'::json
    )
)::text
FROM users u
WHERE u.user_id = @user_id
LIMIT 1;";

        if (!TryExecuteScalar(connectionString, sql,
                new Dictionary<string, object>
                {
                    { "@user_id", userId },
                    { "@history_limit", safeLimit }
                },
                out object result, out string dbError))
        {
            error = dbError;
            return false;
        }

        contextJson = Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(contextJson))
        {
            error = "No context data found for user.";
            return false;
        }

        Debug.Log("[UIManager] Prompt context query success. user_id=" + userId + ", history_limit=" + safeLimit + ", json_chars=" + contextJson.Length + ".");

        return true;
    }

    public bool TryCreateConnectionStringForExternalUse(out string connectionString, out string error)
    {
        return TryCreateConnectionString(out error, out connectionString);
    }

    private void RegisterUiEvents()
    {
        if (switchToSignUpButton != null)
        {
            switchToSignUpButton.onClick.AddListener(OnSwitchToSignUpClicked);
        }

        if (switchToSignInButton != null)
        {
            switchToSignInButton.onClick.AddListener(OnSwitchToSignInClicked);
        }

        if (signUpButton != null)
        {
            signUpButton.onClick.AddListener(OnSignUpClicked);
            EnsureSubmitPointerCapture(signUpButton, signUpPanel);
        }

        if (signInButton != null)
        {
            signInButton.onClick.AddListener(OnSignInClicked);
            EnsureSubmitPointerCapture(signInButton, signInPanel);
        }

        RefreshSignInUserIdSubmitHook();

        if (dismissUserIdPanelButton != null)
        {
            dismissUserIdPanelButton.onClick.AddListener(OnDismissUserIdPanelClicked);
        }
    }

    private static void EnsureSubmitPointerCapture(Button button, GameObject panelRoot)
    {
        if (button == null || panelRoot == null)
        {
            return;
        }

        AuthSubmitPointerCapture capture = button.GetComponent<AuthSubmitPointerCapture>();
        if (capture == null)
        {
            capture = button.gameObject.AddComponent<AuthSubmitPointerCapture>();
        }

        capture.ConfigurePanelRoot(panelRoot);
    }

    private void UnregisterUiEvents()
    {
        if (switchToSignUpButton != null)
        {
            switchToSignUpButton.onClick.RemoveListener(OnSwitchToSignUpClicked);
        }

        if (switchToSignInButton != null)
        {
            switchToSignInButton.onClick.RemoveListener(OnSwitchToSignInClicked);
        }

        if (signUpButton != null)
        {
            signUpButton.onClick.RemoveListener(OnSignUpClicked);
        }

        if (signInButton != null)
        {
            signInButton.onClick.RemoveListener(OnSignInClicked);
        }

        if (_signInIdSubmitHookTarget != null)
        {
            _signInIdSubmitHookTarget.onSubmit.RemoveListener(OnSignInInputSubmitted);
            _signInIdSubmitHookTarget.onEndEdit.RemoveListener(OnSignInEndEdit);
            _signInIdSubmitHookTarget = null;
        }

        if (dismissUserIdPanelButton != null)
        {
            dismissUserIdPanelButton.onClick.RemoveListener(OnDismissUserIdPanelClicked);
        }
    }

    private void SetPanelState(bool signInVisible, bool signUpVisible)
    {
        if (signInPanel != null)
        {
            signInPanel.SetActive(signInVisible);
        }

        if (signUpPanel != null)
        {
            signUpPanel.SetActive(signUpVisible);
        }
    }

    private void SetAuthButtonsVisible(bool visible)
    {
        SetButtonVisible(signInButton, visible);
        SetButtonVisible(signUpButton, visible);
        SetButtonVisible(switchToSignInButton, visible);
        SetButtonVisible(switchToSignUpButton, visible);
    }

    private static void SetButtonVisible(Button button, bool visible)
    {
        if (button != null && button.gameObject != null)
        {
            button.gameObject.SetActive(visible);
        }
    }

    private void SetStatus(string message)
    {
        Debug.Log("[UIManager] " + message);

        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void BindUiReferencesIfNeeded()
    {
        AutoCorrectSwappedAuthPanelsIfNeeded();

        if (autoFindUiReferences)
        {
            if (signInPanel == null)
            {
                signInPanel = FindPanelByName("SignIn");
            }

            if (signUpPanel == null)
            {
                signUpPanel = FindPanelByName("SignUp");
            }

            if (userIdConfirmationPanel == null)
            {
                GameObject byExactName = GameObject.Find("UserIdPanel") ?? GameObject.Find("UserIDPanel");
                userIdConfirmationPanel = byExactName != null ? byExactName : FindPanelByName("UserId");
            }

            if (dismissUserIdPanelButton == null)
            {
                dismissUserIdPanelButton = FindDismissButtonForUserIdPanel();
            }

            if (userIdConfirmationDisplayText == null && userIdConfirmationPanel != null)
            {
                userIdConfirmationDisplayText =
                    userIdConfirmationPanel.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (switchToSignUpButton == null)
            {
                switchToSignUpButton = FindButtonByTextOrName("Sign Up", "switchToSignUpButton");
            }

            if (switchToSignInButton == null)
            {
                switchToSignInButton = FindButtonByTextOrName("Sign In", "switchToSignInButton");
            }

            if (signUpButton == null)
            {
                signUpButton = FindButtonByTextOrName("Create", "signUpButton");
            }

            if (signInButton == null)
            {
                signInButton = FindButtonByTextOrName("Sign In", "signInButton");
            }

            if (signUpNameField == null)
            {
                signUpNameField = FindInputFieldByHints("Name", "Full Name");
            }

            if (signUpAgeField == null)
            {
                signUpAgeField = FindInputFieldByHints("Age");
            }

            if (signUpIntroField == null)
            {
                signUpIntroField = FindInputFieldByHints("Intro", "About");
            }

            /*
             Prefer any TMP living under signInPanel — scene mis-labeling and Quest/XR ray focus make global searches brittle.
            */
            if (signInUserIdField == null && signInPanel != null)
            {
                TMP_InputField[] scoped = signInPanel.GetComponentsInChildren<TMP_InputField>(true);
                signInUserIdField = PreferUserIdScopedField(scoped);
            }

            if (signInUserIdField == null)
            {
                signInUserIdField = FindInputFieldByHints("User ID", "User Id", "UserId");
            }

            if (statusText == null)
            {
                statusText = FindStatusLabel();
            }
        }

        PrepareAuthCredentialFields();
    }

    /// <summary>Quest/XR: keep auth TMP values when focus moves to Enter / panel buttons.</summary>
    private void PrepareAuthCredentialFields()
    {
        TmpCredentialFieldUtility.PrepareAuthField(signInUserIdField);
        TmpCredentialFieldUtility.PrepareAuthField(signUpNameField);
        TmpCredentialFieldUtility.PrepareAuthField(signUpAgeField);
        TmpCredentialFieldUtility.PrepareAuthField(signUpIntroField);

        if (signInPanel != null)
        {
            TMP_InputField[] signInFields = signInPanel.GetComponentsInChildren<TMP_InputField>(true);
            for (int i = 0; i < signInFields.Length; i++)
            {
                TmpCredentialFieldUtility.PrepareAuthField(signInFields[i]);
            }
        }

        if (signUpPanel != null)
        {
            TMP_InputField[] signUpFields = signUpPanel.GetComponentsInChildren<TMP_InputField>(true);
            for (int i = 0; i < signUpFields.Length; i++)
            {
                TmpCredentialFieldUtility.PrepareAuthField(signUpFields[i]);
            }
        }
    }

    /// <summary>
    /// If <c>signInPanel</c> contains several TMP inputs and <c>signUpPanel</c> has only one, references were crossed in the scene.
    /// Swapping restores sign-in reads from the same field the player types into (fixes endless "Enter your user ID").
    /// </summary>
    private void AutoCorrectSwappedAuthPanelsIfNeeded()
    {
        if (signInPanel == null || signUpPanel == null)
        {
            return;
        }

        TMP_InputField[] signInTmps = signInPanel.GetComponentsInChildren<TMP_InputField>(true);
        TMP_InputField[] signUpTmps = signUpPanel.GetComponentsInChildren<TMP_InputField>(true);
        int signInCount = signInTmps != null ? signInTmps.Length : 0;
        int signUpCount = signUpTmps != null ? signUpTmps.Length : 0;

        /*
         This project’s layout: exactly one TMP on sign-in (user id) and multiple on sign-up (name / age / intro).
         Reversed counts mean Panel vs Panel (1) were dragged onto the wrong UIManager slots in the Inspector.
        */
        if (signInCount >= 3 && signUpCount == 1)
        {
            GameObject swappedIn = signInPanel;
            signInPanel = signUpPanel;
            signUpPanel = swappedIn;

            if (_signInIdSubmitHookTarget != null)
            {
                _signInIdSubmitHookTarget.onSubmit.RemoveListener(OnSignInInputSubmitted);
                _signInIdSubmitHookTarget = null;
            }

            signInUserIdField = null;
            Debug.LogWarning(
                "[UIManager] signInPanel and signUpPanel looked swapped by TMP_InputField count; swapped at runtime.");
        }
    }

    /// <summary>
    /// Locate Continue / OK on the user-id confirmation panel first so another canvas button is not mistaken for dismiss.
    /// </summary>
    private Button FindDismissButtonForUserIdPanel()
    {
        Button scoped = TryFindDismissButtonUnderRoot(userIdConfirmationPanel);
        if (scoped != null)
        {
            return scoped;
        }

        return FindButtonByTextOrName("Continue", "dismissUserId") ??
               FindButtonByTextOrName("OK", "okUserId") ??
               FindButtonByTextOrName("Got it", "gotItUserId");
    }

    private static Button TryFindDismissButtonUnderRoot(GameObject root)
    {
        if (root == null)
        {
            return null;
        }

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button == null)
            {
                continue;
            }

            if (button.name.IndexOf("dismissUserId", StringComparison.OrdinalIgnoreCase) >= 0 ||
                button.name.IndexOf("okUserId", StringComparison.OrdinalIgnoreCase) >= 0 ||
                button.name.IndexOf("gotItUserId", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return button;
            }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            string labelText = label != null ? label.text : string.Empty;
            if (labelText.IndexOf("Continue", StringComparison.OrdinalIgnoreCase) >= 0 ||
                labelText.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                labelText.IndexOf("Got it", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return button;
            }
        }

        return null;
    }

    private GameObject FindPanelByName(string panelNameHint)
    {
        GameObject[] objects = FindObjectsOfType<GameObject>(true);
        foreach (GameObject obj in objects)
        {
            if (obj != null && obj.name.IndexOf(panelNameHint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return obj;
            }
        }

        return null;
    }

    private Button FindButtonByTextOrName(string textHint, string nameHint)
    {
        Button[] buttons = FindObjectsOfType<Button>(true);
        Button fallback = null;

        foreach (Button button in buttons)
        {
            if (button == null)
            {
                continue;
            }

            if (button.name.IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return button;
            }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null && label.text.IndexOf(textHint, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return button;
            }

            fallback ??= button;
        }

        return fallback;
    }

    private static TMP_InputField PreferUserIdScopedField(TMP_InputField[] underPanel)
    {
        if (underPanel == null || underPanel.Length == 0)
        {
            return null;
        }

        if (underPanel.Length == 1)
        {
            return underPanel[0];
        }

        TMP_InputField bestGuess = null;
        for (int i = 0; i < underPanel.Length; i++)
        {
            TMP_InputField field = underPanel[i];
            if (field == null)
            {
                continue;
            }

            string fieldName = field.name ?? string.Empty;
            TMP_Text placeholder = field.placeholder as TMP_Text;
            string placeholderText = placeholder != null ? placeholder.text ?? string.Empty : string.Empty;

            if (MatchesUserIdHints(fieldName, placeholderText))
            {
                return field;
            }

            if (placeholderText.IndexOf("user", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bestGuess ??= field;
            }
        }

        return bestGuess ?? underPanel[0];
    }

    private static bool MatchesUserIdHints(string fieldName, string placeholderText)
    {
        string[] needles = new[] { "User ID", "User Id", "UserId", "user_id", "userid" };
        for (int n = 0; n < needles.Length; n++)
        {
            string needle = needles[n];
            if (fieldName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                placeholderText.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private TMP_InputField FindInputFieldByHints(params string[] hints)
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

            for (int i = 0; i < hints.Length; i++)
            {
                string hint = hints[i];
                if (fieldName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    placeholderText.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return field;
                }
            }
        }

        return fields.Length > 0 ? fields[0] : null;
    }

    private TextMeshProUGUI FindStatusLabel()
    {
        TextMeshProUGUI[] texts = FindObjectsOfType<TextMeshProUGUI>(true);

        foreach (TextMeshProUGUI text in texts)
        {
            if (text != null && text.name.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return text;
            }
        }

        return null;
    }

    private bool TryCreateConnectionString(out string error, out string connectionString)
    {
        error = string.Empty;

        if (!string.IsNullOrWhiteSpace(connectionStringOverride))
        {
            connectionString = ApplyQuestNpgsqlConnectionDefaults(connectionStringOverride);
            return true;
        }

        string normalizedHost = NormalizePostgresHost(postgresHost);
        if (string.IsNullOrWhiteSpace(normalizedHost))
        {
            connectionString = string.Empty;
            error = "PostgreSQL host is missing.";
            return false;
        }

        if (!IsValidPostgresHost(normalizedHost, out string hostError))
        {
            connectionString = string.Empty;
            error = hostError;
            return false;
        }

        if (string.IsNullOrWhiteSpace(postgresDatabaseName))
        {
            connectionString = string.Empty;
            error = "PostgreSQL database name is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(postgresUsername))
        {
            connectionString = string.Empty;
            error = "PostgreSQL username is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(postgresPassword))
        {
            connectionString = string.Empty;
            error = "PostgreSQL password is missing.";
            return false;
        }

        bool questLike = IsQuestLikeRuntime();
        if (questLike && preferIpv4HostOnQuest &&
            TryResolvePrimaryIpv4Host(normalizedHost, out string ipv4Host))
        {
            AuthFlowTrace.Step(
                "SignIn-DB-IPV4",
                "Host resolved to IPv4 " + ipv4Host + " (was " + normalizedHost + ")");
            normalizedHost = ipv4Host;
        }

        int connectTimeoutSec = questLike ? 20 : 60;
        int commandTimeoutSec = questLike ? 20 : 60;
        int keepaliveSec = questLike ? 10 : 30;
        string sslMode = postgresRequireSsl ? "Require" : "Disable";
        connectionString = string.Format(
            CultureInfo.InvariantCulture,
            "Host={0};Port={1};Database={2};Username={3};Password={4};SSL Mode={5};Trust Server Certificate=true;Timeout={6};Command Timeout={7};Pooling=false;Keepalive={8};Max Auto Prepare=0;",
            normalizedHost,
            postgresPort,
            postgresDatabaseName,
            postgresUsername,
            postgresPassword,
            sslMode,
            connectTimeoutSec,
            commandTimeoutSec,
            keepaliveSec);

        connectionString = ApplyQuestNpgsqlConnectionDefaults(connectionString);
        return true;
    }

    /// <summary>Applies Quest-safe Npgsql settings so pooled sockets are not reused across auth attempts.</summary>
    private static string ApplyQuestNpgsqlConnectionDefaults(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        if (connectionString.IndexOf("Pooling=", StringComparison.OrdinalIgnoreCase) < 0)
        {
            connectionString += ";Pooling=false";
        }

        if (connectionString.IndexOf("Max Auto Prepare=", StringComparison.OrdinalIgnoreCase) < 0)
        {
            connectionString += ";Max Auto Prepare=0";
        }

        bool questLike = IsQuestLikeRuntime();
        int defaultTimeout = questLike ? 20 : 30;
        int defaultKeepalive = questLike ? 10 : 30;

        if (connectionString.IndexOf("Timeout=", StringComparison.OrdinalIgnoreCase) < 0)
        {
            connectionString += ";Timeout=" + defaultTimeout.ToString(CultureInfo.InvariantCulture);
        }

        if (connectionString.IndexOf("Command Timeout=", StringComparison.OrdinalIgnoreCase) < 0)
        {
            connectionString += ";Command Timeout=" + defaultTimeout.ToString(CultureInfo.InvariantCulture);
        }

        if (connectionString.IndexOf("Keepalive=", StringComparison.OrdinalIgnoreCase) < 0)
        {
            connectionString += ";Keepalive=" + defaultKeepalive.ToString(CultureInfo.InvariantCulture);
        }

        if (connectionString.IndexOf("Trust Server Certificate=", StringComparison.OrdinalIgnoreCase) < 0)
        {
            connectionString += ";Trust Server Certificate=true";
        }

        return SanitizeNpgsqlConnectionStringForQuest(connectionString);
    }

    /// <summary>
    /// Quest/Android Npgsql builds are often older than the Editor copy and reject Npgsql 6+ keywords
    /// (Multiplexing, etc.) even when PC auth works with the same C# source.
    /// </summary>
    private static string SanitizeNpgsqlConnectionStringForQuest(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        string[] unsupportedKeys =
        {
            "Multiplexing",
            "Include Error Detail",
            "Channel Binding",
            "Cancellation Timeout"
        };

        string[] parts = connectionString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(parts.Length);
        bool strippedAny = false;

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.Length == 0)
            {
                continue;
            }

            int equalsIndex = part.IndexOf('=');
            string key = equalsIndex >= 0 ? part.Substring(0, equalsIndex).Trim() : part;

            bool remove = false;
            for (int k = 0; k < unsupportedKeys.Length; k++)
            {
                if (string.Equals(key, unsupportedKeys[k], StringComparison.OrdinalIgnoreCase))
                {
                    remove = true;
                    strippedAny = true;
                    break;
                }
            }

            if (!remove)
            {
                kept.Add(part);
            }
        }

        if (strippedAny)
        {
            AuthFlowTrace.Step(
                "SignIn-DB-SAN",
                "removed Quest-unsupported Npgsql connection keywords (Multiplexing, etc.).",
                LogType.Warning);
        }

        return string.Join(";", kept);
    }

    private static bool TryResolvePrimaryIpv4Host(string host, out string ipv4Host)
    {
        ipv4Host = string.Empty;

        if (string.IsNullOrWhiteSpace(host) || IPAddress.TryParse(host, out _))
        {
            return false;
        }

        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(host);
            if (addresses == null)
            {
                return false;
            }

            for (int i = 0; i < addresses.Length; i++)
            {
                IPAddress address = addresses[i];
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    ipv4Host = address.ToString();
                    return true;
                }
            }
        }
        catch (Exception exception)
        {
            AuthFlowTrace.Step(
                "SignIn-DB-IPV4-WARN",
                "IPv4 resolve skipped: " + exception.Message,
                LogType.Warning);
        }

        return false;
    }

    /// <summary>DNS + TCP checks before Npgsql — especially important on Quest where async Npgsql errors are opaque.</summary>
    private static bool TryRunPostgresConnectPreflight(string host, int port, out string error)
    {
        error = string.Empty;
        NetworkReachability reachability = Application.internetReachability;

        AuthFlowTrace.Step(
            "SignIn-DB-NET",
            "internetReachability=" + reachability +
            " platform=" + Application.platform +
            " xr=" + IsQuestLikeRuntime());

        if (reachability == NetworkReachability.NotReachable)
        {
            error = "[CONNECT] Headset has no network route (NotReachable). Connect Quest Wi-Fi, then retry.";
            AuthFlowTrace.Step("SignIn-DB-NET-FAIL", error, LogType.Error);
            return false;
        }

        if (!TryProbePostgresDns(host, out string resolvedIps, out string dnsError))
        {
            error = "[CONNECT] DNS lookup failed for " + host + ": " + dnsError +
                    " Hint: guest/captive Wi-Fi, private DNS, or VPN often blocks cloud DB hostnames on Quest.";
            AuthFlowTrace.Step("SignIn-DB-DNS-FAIL", error, LogType.Error);
            return false;
        }

        AuthFlowTrace.Step("SignIn-DB-DNS", "host=" + host + " ips=" + resolvedIps);

        int tcpTimeoutMs = IsQuestLikeRuntime() ? 12000 : 8000;
        if (!TryProbePostgresTcpReachability(host, port, tcpTimeoutMs, out string tcpError))
        {
            error = "[CONNECT] " + tcpError;
            AuthFlowTrace.Step("SignIn-DB-TCP-FAIL", error, LogType.Error);
            return false;
        }

        if (IsQuestLikeRuntime())
        {
            // Quest/Android: brief pause after TCP probe so Npgsql does not immediately collide with InProgress (10036).
            System.Threading.Thread.Sleep(400);
        }

        return true;
    }

    private static bool TryProbePostgresDns(string host, out string resolvedSummary, out string error)
    {
        resolvedSummary = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(host))
        {
            error = "host is empty.";
            return false;
        }

        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(host);
            if (addresses == null || addresses.Length == 0)
            {
                error = "no IP addresses returned.";
                return false;
            }

            StringBuilder builder = new StringBuilder();
            int limit = Mathf.Min(addresses.Length, 4);
            for (int i = 0; i < limit; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(addresses[i]);
            }

            if (addresses.Length > limit)
            {
                builder.Append("+");
                builder.Append((addresses.Length - limit).ToString(CultureInfo.InvariantCulture));
            }

            resolvedSummary = builder.ToString();
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
    }

    /// <summary>Checks that the headset can open a TCP socket to PostgreSQL before Npgsql runs.</summary>
    private static bool TryProbePostgresTcpReachability(string host, int port, int timeoutMs, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(host))
        {
            error = "PostgreSQL host is missing.";
            return false;
        }

        if (port <= 0 || port > 65535)
        {
            error = "PostgreSQL port is invalid.";
            return false;
        }

        TcpClient client = null;

        try
        {
            client = new TcpClient();
            IAsyncResult connectResult = client.BeginConnect(host, port, null, null);
            if (!connectResult.AsyncWaitHandle.WaitOne(timeoutMs))
            {
                error = "TCP timeout reaching " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }

            client.EndConnect(connectResult);
            AuthFlowTrace.Step(
                "SignIn-DB-TCP",
                "tcp ok host=" + host + " port=" + port.ToString(CultureInfo.InvariantCulture));
            return true;
        }
        catch (SocketException socketException)
        {
            error = "TCP SocketException " + socketException.SocketErrorCode +
                    " (" + ((int)socketException.SocketErrorCode).ToString(CultureInfo.InvariantCulture) + ") reaching " +
                    host + ":" + port.ToString(CultureInfo.InvariantCulture) + ": " + socketException.Message +
                    " | " + DescribeSocketErrorHint(socketException.SocketErrorCode);
            return false;
        }
        catch (Exception exception)
        {
            error = "TCP failed reaching " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + ": " +
                    exception.GetType().Name + ": " + exception.Message;
            return false;
        }
        finally
        {
            if (client != null)
            {
                client.Close();
            }
        }
    }

    private static string DescribeSocketErrorHint(SocketError code)
    {
        switch (code)
        {
            case SocketError.HostNotFound:
            case SocketError.NoData:
                return "DNS/name resolution failed on this device.";
            case SocketError.TimedOut:
                return "Firewall, wrong port, or Aiven IP allowlist blocking Quest Wi-Fi.";
            case SocketError.ConnectionRefused:
                return "Host reachable but nothing listening on that port (wrong port or DB down).";
            case SocketError.NetworkUnreachable:
            case SocketError.HostUnreachable:
                return "Quest Wi-Fi has no route to the internet or DB network.";
            case SocketError.InProgress:
                return "Non-blocking connect still in flight on Quest — auth will retry automatically.";
            case SocketError.WouldBlock:
                return "Socket would block — auth will retry automatically.";
            case SocketError.ConnectionReset:
                return "Connection dropped — often TLS/firewall or idle timeout.";
            default:
                return "See SignIn-DB-TCP-FAIL / SignIn-DB-OPEN-FAIL in device logs.";
        }
    }

    private static string NormalizePostgresHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        string normalized = host.Trim();
        normalized = normalized.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("Host=", string.Empty, StringComparison.OrdinalIgnoreCase);
        normalized = normalized.TrimEnd('/');

        return normalized;
    }

    private static bool IsValidPostgresHost(string host, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(host))
        {
            error = "PostgreSQL host is missing.";
            return false;
        }

        if (host.Contains(" ", StringComparison.Ordinal) || host.Contains("/", StringComparison.Ordinal) || host.Contains("?", StringComparison.Ordinal))
        {
            error = "PostgreSQL host contains invalid characters. Enter only the hostname, not a URL or connection string prefix.";
            return false;
        }

        if (!host.Contains(".", StringComparison.Ordinal))
        {
            error = "PostgreSQL host does not look valid. Enter the full DNS host name.";
            return false;
        }

        return true;
    }

    private bool TryExecuteScalar(
        string connectionString,
        string sql,
        Dictionary<string, object> parameters,
        out object result,
        out string error,
        string authTraceFlowPrefix = null)
    {
        result = null;
        error = string.Empty;
        bool trace = !string.IsNullOrWhiteSpace(authTraceFlowPrefix);
        System.Diagnostics.Stopwatch stopwatch = trace ? System.Diagnostics.Stopwatch.StartNew() : null;

        lock (AuthDatabaseGate)
        {
            if (trace)
            {
                AuthFlowTrace.Step(
                    authTraceFlowPrefix + "-004",
                    "database gate acquired thread=" + System.Threading.Thread.CurrentThread.ManagedThreadId +
                    " ms=" + stopwatch.ElapsedMilliseconds);
            }

            if (!TryGetNpgsqlConnectionType(out Type connectionType, out string typeError))
            {
                error = typeError;
                if (trace)
                {
                    AuthFlowTrace.Step(authTraceFlowPrefix + "-005", "Npgsql type missing: " + typeError, LogType.Error);
                }

                return false;
            }

            if (trace)
            {
                AuthFlowTrace.Step(authTraceFlowPrefix + "-006", "Npgsql type=" + connectionType.FullName);
            }

            object connection = null;
            object command = null;

            try
            {
                if (trace)
                {
                    if (!TryOpenFreshNpgsqlConnection(
                            connectionType,
                            connectionString,
                            authTraceFlowPrefix,
                            stopwatch,
                            out connection,
                            out error))
                    {
                        return false;
                    }
                }
                else
                {
                    connection = Activator.CreateInstance(connectionType);
                    SetPropertyValue(connectionType, connection, "ConnectionString", connectionString);
                    if (!TryEstablishNpgsqlConnection(connectionType, connection, out error))
                    {
                        return false;
                    }
                }

                command = InvokeZeroArgMethod(connectionType, connection, "CreateCommand");
                if (command == null)
                {
                    error = "Could not create PostgreSQL command.";
                    if (trace)
                    {
                        AuthFlowTrace.Step(authTraceFlowPrefix + "-009", error, LogType.Error);
                    }

                    return false;
                }

                SetCommandText(command, sql);
                AddParameters(command, parameters);
                if (trace)
                {
                    AuthFlowTrace.Step(authTraceFlowPrefix + "-010", "executing scalar ms=" + stopwatch.ElapsedMilliseconds);
                }

                result = InvokeZeroArgMethod(command.GetType(), command, "ExecuteScalar");
                if (trace)
                {
                    AuthFlowTrace.Step(
                        authTraceFlowPrefix + "-011",
                        "scalar complete resultNull=" + (result == null || result == DBNull.Value) +
                        " ms=" + stopwatch.ElapsedMilliseconds);
                }

                return true;
            }
            catch (TargetInvocationException exception)
            {
                error = ExtractExceptionMessage(exception);
                if (trace)
                {
                    AuthFlowTrace.StepException(
                        authTraceFlowPrefix + "-ERR",
                        "failure total_ms=" + stopwatch.ElapsedMilliseconds,
                        exception);
                }

                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (trace)
                {
                    AuthFlowTrace.StepException(
                        authTraceFlowPrefix + "-ERR",
                        "failure total_ms=" + stopwatch.ElapsedMilliseconds,
                        exception);
                }

                return false;
            }
            finally
            {
                DisposeIfPossible(command);
                AbortNpgsqlConnection(connectionType, connection);
                if (trace)
                {
                    AuthFlowTrace.Step(
                        authTraceFlowPrefix + "-016",
                        "connection disposed total_ms=" + stopwatch.ElapsedMilliseconds);
                }
            }
        }
    }

    private bool TryExecuteNonQuery(string connectionString, string sql, Dictionary<string, object> parameters, out string error)
    {
        error = string.Empty;

        lock (AuthDatabaseGate)
        {
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
                if (!TryEstablishNpgsqlConnection(connectionType, connection, out error))
                {
                    return false;
                }

                command = InvokeZeroArgMethod(connectionType, connection, "CreateCommand");
                if (command == null)
                {
                    error = "Could not create PostgreSQL command.";
                    return false;
                }

                SetCommandText(command, sql);
                AddParameters(command, parameters);
                InvokeZeroArgMethod(command.GetType(), command, "ExecuteNonQuery");
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
                AbortNpgsqlConnection(connectionType, connection);
            }
        }
    }

    private static bool TryOpenFreshNpgsqlConnection(
        Type connectionType,
        string connectionString,
        string traceStepPrefix,
        System.Diagnostics.Stopwatch stopwatch,
        out object connection,
        out string error)
    {
        connection = null;
        error = string.Empty;
        bool questLike = IsQuestLikeRuntime();
        int maxAttempts = questLike ? 8 : 5;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            object candidate = null;

            try
            {
                if (attempt > 1)
                {
                    int delayMs = questLike ? 280 * attempt * attempt : 120 * attempt * attempt;
                    AuthFlowTrace.Step(
                        traceStepPrefix + "-OPEN-BACKOFF",
                        "ms=" + delayMs + " before attempt=" + attempt,
                        LogType.Warning);
                    System.Threading.Thread.Sleep(delayMs);
                }

                candidate = Activator.CreateInstance(connectionType);
                SetPropertyValue(connectionType, candidate, "ConnectionString", connectionString);

                if (attempt == 1)
                {
                    AuthFlowTrace.Step(traceStepPrefix + "-007", "opening connection ms=" + stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    AuthFlowTrace.Step(
                        traceStepPrefix + "-OPEN-RETRY",
                        "open attempt=" + attempt + " ms=" + stopwatch.ElapsedMilliseconds,
                        LogType.Warning);
                }

                if (!TryEstablishNpgsqlConnection(connectionType, candidate, out error))
                {
                    AbortNpgsqlConnection(connectionType, candidate);

                    if (attempt < maxAttempts && IsTransientAuthSocketProgressError(error))
                    {
                        AuthFlowTrace.Step(
                            traceStepPrefix + "-OPEN-RETRY",
                            "transient socket overlap attempt=" + attempt + " detail=\"" + error + "\"",
                            LogType.Warning);
                        continue;
                    }

                    AuthFlowTrace.Step(traceStepPrefix + "-OPEN-FAIL", error, LogType.Error);
                    return false;
                }

                AuthFlowTrace.Step(traceStepPrefix + "-008", "connection open ok ms=" + stopwatch.ElapsedMilliseconds);
                connection = candidate;
                return true;
            }
            catch (TargetInvocationException exception)
            {
                error = ExtractExceptionMessage(exception);
                AbortNpgsqlConnection(connectionType, candidate);

                if (attempt < maxAttempts && IsTransientAuthSocketProgressError(error))
                {
                    AuthFlowTrace.Step(
                        traceStepPrefix + "-OPEN-RETRY",
                        "transient socket overlap attempt=" + attempt + " detail=\"" + error + "\"",
                        LogType.Warning);
                    continue;
                }

                AuthFlowTrace.StepException(
                    "SignIn-DB-ERR",
                    traceStepPrefix + " open failed total_ms=" + stopwatch.ElapsedMilliseconds,
                    exception);
                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                AbortNpgsqlConnection(connectionType, candidate);
                AuthFlowTrace.StepException(
                    "SignIn-DB-ERR",
                    traceStepPrefix + " open failed total_ms=" + stopwatch.ElapsedMilliseconds,
                    exception);
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Opens Npgsql via OpenAsync on a worker thread so the Unity main thread is never blocked (Quest/Android).
    /// </summary>
    private static bool TryEstablishNpgsqlConnection(Type connectionType, object connection, out string error)
    {
        return TryInvokeNpgsqlConnectionOpenAsyncOnWorkerThread(connectionType, connection, out error);
    }

    private const int NpgsqlOpenAsyncTimeoutMs = 35000;

    private static bool TryInvokeNpgsqlConnectionOpenAsyncOnWorkerThread(
        Type connectionType,
        object connection,
        out string error)
    {
        error = string.Empty;
        Exception captured = null;

        Thread worker = new Thread(() =>
        {
            try
            {
                InvokeConnectionOpenAsync(connectionType, connection);
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });

        worker.IsBackground = true;
        worker.Name = "NpgsqlAuthOpenAsync";
        worker.Start();

        if (!worker.Join(NpgsqlOpenAsyncTimeoutMs))
        {
            error = "Npgsql OpenAsync timed out on worker thread after " +
                    NpgsqlOpenAsyncTimeoutMs.ToString(CultureInfo.InvariantCulture) + "ms.";
            AuthFlowTrace.Step("SignIn-DB-OPEN-TIMEOUT", error, LogType.Error);
            return false;
        }

        if (captured != null)
        {
            error = ExtractExceptionMessage(captured);
            LogType level = IsTransientAuthSocketProgressError(error) ? LogType.Warning : LogType.Error;
            AuthFlowTrace.Step("SignIn-DB-OPEN-FAIL", "Npgsql OpenAsync failed: " + error, level);
            return false;
        }

        AuthFlowTrace.Step("SignIn-DB-OPEN-ASYNC", "Npgsql OpenAsync ok on worker thread.");
        return true;
    }

    private static void InvokeConnectionOpenAsync(Type connectionType, object connection)
    {
        MethodInfo openAsync = connectionType.GetMethod(
            "OpenAsync",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(CancellationToken) },
            null);

        object taskResult;
        if (openAsync != null)
        {
            taskResult = openAsync.Invoke(connection, new object[] { CancellationToken.None });
        }
        else
        {
            openAsync = connectionType.GetMethod(
                "OpenAsync",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);

            if (openAsync == null)
            {
                throw new InvalidOperationException("NpgsqlConnection.OpenAsync was not found.");
            }

            taskResult = openAsync.Invoke(connection, null);
        }

        WaitForReflectionTask(taskResult, NpgsqlOpenAsyncTimeoutMs);
    }

    private static void WaitForReflectionTask(object taskResult, int timeoutMs)
    {
        if (taskResult == null)
        {
            throw new InvalidOperationException("OpenAsync returned null.");
        }

        if (taskResult is Task task)
        {
            if (!task.Wait(timeoutMs))
            {
                throw new TimeoutException(
                    "Npgsql OpenAsync timed out after " +
                    timeoutMs.ToString(CultureInfo.InvariantCulture) + "ms.");
            }

            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ??
                      new InvalidOperationException("Npgsql OpenAsync failed.");
            }

            return;
        }

        MethodInfo asTask = taskResult.GetType().GetMethod(
            "AsTask",
            BindingFlags.Instance | BindingFlags.Public);

        if (asTask != null && asTask.Invoke(taskResult, null) is Task converted)
        {
            if (!converted.Wait(timeoutMs))
            {
                throw new TimeoutException(
                    "Npgsql OpenAsync timed out after " +
                    timeoutMs.ToString(CultureInfo.InvariantCulture) + "ms.");
            }

            if (converted.IsFaulted)
            {
                throw converted.Exception?.GetBaseException() ??
                      new InvalidOperationException("Npgsql OpenAsync failed.");
            }

            return;
        }

        throw new InvalidOperationException(
            "OpenAsync return type not supported: " + taskResult.GetType().FullName);
    }

    private static void AbortNpgsqlConnection(Type connectionType, object connection)
    {
        if (connectionType == null || connection == null)
        {
            return;
        }

        try
        {
            CloseNpgsqlConnection(connectionType, connection);
        }
        catch (Exception exception)
        {
            AuthFlowTrace.Step(
                "SignIn-DB-CLOSE-WARN",
                "close failed: " + exception.Message,
                LogType.Warning);
        }

        try
        {
            DisposeIfPossible(connection);
        }
        catch (Exception exception)
        {
            AuthFlowTrace.Step(
                "SignIn-DB-DISPOSE-WARN",
                "dispose failed: " + exception.Message,
                LogType.Warning);
        }
    }

    private static void CloseNpgsqlConnection(Type connectionType, object connection)
    {
        if (connectionType == null || connection == null)
        {
            return;
        }

        MethodInfo closeMethod = connectionType.GetMethod(
            "Close",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);

        closeMethod?.Invoke(connection, null);
    }

    private static void SetCommandText(object command, string sql)
    {
        SetPropertyValue(command.GetType(), command, "CommandText", sql);
    }

    private static void AddParameters(object command, Dictionary<string, object> parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return;
        }

        object parameterCollection = GetPropertyValue(command.GetType(), command, "Parameters");
        if (parameterCollection == null)
        {
            return;
        }

        Type collectionType = parameterCollection.GetType();
        MethodInfo addWithValue = null;
        MethodInfo addObject = null;
        bool preferCreateParameterPath = IsQuestLikeRuntime();

        MethodInfo[] methods = collectionType.GetMethods();
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method.Name == "AddWithValue")
            {
                ParameterInfo[] methodParameters = method.GetParameters();
                if (methodParameters.Length == 2 &&
                    methodParameters[0].ParameterType == typeof(string))
                {
                    addWithValue = method;
                    break;
                }
            }
        }

        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method.Name == "Add" && method.GetParameters().Length == 1)
            {
                addObject = method;
                break;
            }
        }

        foreach (KeyValuePair<string, object> pair in parameters)
        {
            object value = pair.Value ?? DBNull.Value;

            if (!preferCreateParameterPath && addWithValue != null)
            {
                addWithValue.Invoke(parameterCollection, new object[] { pair.Key, value });
                continue;
            }

            if (addObject != null)
            {
                object parameter = InvokeZeroArgMethod(command.GetType(), command, "CreateParameter");
                if (parameter == null)
                {
                    continue;
                }

                ParameterInfo[] parameterInfos = addObject.GetParameters();
                if (parameterInfos.Length == 1 && parameterInfos[0].ParameterType.IsInstanceOfType(parameter))
                {
                    SetPropertyValue(parameter.GetType(), parameter, "ParameterName", pair.Key);
                    SetPropertyValue(parameter.GetType(), parameter, "Value", value);

                    addObject.Invoke(parameterCollection, new object[] { parameter });
                }
            }
        }
    }

    private static bool TryGetNpgsqlConnectionType(out Type connectionType, out string error)
    {
        connectionType = null;
        error = string.Empty;

        const string fullTypeName = "Npgsql.NpgsqlConnection";

        connectionType = Type.GetType(fullTypeName + ", Npgsql", false);
        if (connectionType != null)
        {
            return true;
        }

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Assembly assembly = assemblies[i];
            Type candidate = assembly.GetType(fullTypeName, false);
            if (candidate != null)
            {
                connectionType = candidate;
                return true;
            }
        }

        error = "Npgsql assembly was not found. Add Npgsql.dll to Assets/Plugins.";
        return false;
    }

    private static void DisposeIfPossible(object disposable)
    {
        if (disposable is IDisposable managedDisposable)
        {
            managedDisposable.Dispose();
        }
    }

    private static string ExtractExceptionMessage(Exception exception)
    {
        if (exception == null)
        {
            return "Unknown PostgreSQL error.";
        }

        if (exception is TargetInvocationException targetInvocationException &&
            targetInvocationException.InnerException != null)
        {
            return ExtractExceptionMessage(targetInvocationException.InnerException);
        }

        if (exception is SocketException socketException)
        {
            return "SocketException " + socketException.SocketErrorCode +
                   " (" + ((int)socketException.SocketErrorCode).ToString(CultureInfo.InvariantCulture) + "): " +
                   socketException.Message + " | " + DescribeSocketErrorHint(socketException.SocketErrorCode);
        }

        if (exception.InnerException != null)
        {
            string inner = ExtractExceptionMessage(exception.InnerException);
            if (!string.IsNullOrWhiteSpace(inner))
            {
                return exception.GetType().Name + ": " + exception.Message + " <= " + inner;
            }
        }

        return string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name + ": Unknown PostgreSQL error."
            : exception.GetType().Name + ": " + exception.Message;
    }

    private static object InvokeZeroArgMethod(Type targetType, object target, string methodName)
    {
        if (targetType == null || target == null || string.IsNullOrWhiteSpace(methodName))
        {
            return null;
        }

        MethodInfo[] methods = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) || method.IsGenericMethodDefinition)
            {
                continue;
            }

            if (method.GetParameters().Length == 0)
            {
                return method.Invoke(target, null);
            }
        }

        return null;
    }

    private static object GetPropertyValue(Type targetType, object target, string propertyName)
    {
        PropertyInfo property = FindProperty(targetType, propertyName, requireWritable: false);
        if (property == null || target == null)
        {
            return null;
        }

        return property.GetValue(target, null);
    }

    private static void SetPropertyValue(Type targetType, object target, string propertyName, object value)
    {
        PropertyInfo property = FindProperty(targetType, propertyName, requireWritable: true);
        if (property == null || target == null)
        {
            return;
        }

        property.SetValue(target, value, null);
    }

    private static PropertyInfo FindProperty(Type targetType, string propertyName, bool requireWritable)
    {
        if (targetType == null || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        PropertyInfo[] properties = targetType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            if (!string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            {
                continue;
            }

            if (requireWritable && !property.CanWrite)
            {
                continue;
            }

            return property;
        }

        return null;
    }
}