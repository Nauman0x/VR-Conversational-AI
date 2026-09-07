using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Determines how TMP fields behave on standalone Quest/Android XR builds.
/// </summary>
public enum TmpQuestKeyboardUiStrategy
{
    /// <summary>Camera-anchored qwerty fallback only (recommended for OpenXR-only projects).</summary>
    MinimalFloatingQwertyOnly,

    /// <summary>Attempt <see cref="TouchScreenKeyboard"/> first, then the floating qwerty fallback.</summary>
    TrySystemKeyboardThenMinimalFallback,

    /// <summary>Only Unity OS keyboard attempts — no qwerty fallback.</summary>
    SystemKeyboardAttemptsOnly,
}

/// <summary>
/// Makes <see cref="TMP_InputField"/> usable from XR controller rays on Quest and attempts the Android system keyboard.
/// </summary>
/// <remarks>
/// XR hits often land on the nested TextMeshPro graphic; focus/selection may never reach the root where this helper lives.
/// We disable raycasts on TMP_Text / TMP_SubMeshUI (and caret Images) so the field&apos;s background Image receives the press first,
/// and we handle pointer confirmation explicitly. When XRRayInteractor still favors plain Button hits over the TMP root, a nearly transparent
/// <see cref="Button"/> child (<c>VrRayCatchOverlay</c>) receives the raycast and forwards focus here. In immersive VR, <see cref="TouchScreenKeyboard.isSupported"/> is frequently false even though
/// <see cref="TouchScreenKeyboard.Open"/> can still work — we no longer skip the open attempt when the flag reads false.
/// On standalone Quest/OpenXR the OS overlay often fails — use <see cref="TmpQuestKeyboardUiStrategy.MinimalFloatingQwertyOnly"/> for a built-in keypad.
/// </remarks>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(TMP_InputField))]
public sealed class TMP_InputFieldVrKeyboardOpener :
    MonoBehaviour,
    ISelectHandler,
    IPointerDownHandler,
    IPointerClickHandler,
    IDeselectHandler
{
    private const float KeyboardRefocusCooldownSeconds = 0.28f;
    private const string VrRayCatchOverlayName = "VrRayCatchOverlay";

    [SerializeField]
    [Tooltip(
        "Spawn a full-area nearly-invisible Button on top so XR clicks use the same Selectable path as working UI buttons, then focus this TMP field.")]
    private bool useVrRayCatchOverlay = true;

    [SerializeField]
    [Tooltip(
        "On Quest/Android in VR, Unity must open TouchScreenKeyboard for the Meta shell keyboard to appear. " +
        "Default on. Turn off if the IME causes Application.pause / stutter and use a Bluetooth keyboard instead.")]
    private bool openSystemTouchKeyboardInXr = true;

    [SerializeField]
    [Range(0, 5)]
    [Tooltip(
        "Meta Quest builds (especially Quest 3) sometimes ignore the first TouchScreenKeyboard.Open; extra delayed opens improve reliability.")]
    private int questKeyboardExtraOpenAttempts = 2;

    [SerializeField]
    [Tooltip("Unscaled delay before each extra TouchScreenKeyboard.Open on Quest.")]
    private float questKeyboardRetryDelaySeconds = 0.12f;

    [SerializeField]
    private TmpQuestKeyboardUiStrategy questKeyboardUiStrategy =
        TmpQuestKeyboardUiStrategy.MinimalFloatingQwertyOnly;

    [SerializeField]
    [Tooltip(
        "In Play Mode without XR, docks the keypad under Camera.main so you can verify rays and layout from the Editor Game view.")]
    private bool previewFloatingKeyboardInUnityEditorPlayMode = false;

    private TMP_InputField _inputField;
    private TouchScreenKeyboard _keyboard;
    private Coroutine _openCoroutine;
    private float _nextSoftKeyboardRefocusUnscaledTime;
    private Button _vrRayCatchButton;

    private static bool _loggedSkippingOsKeyboardForXr;
    private static bool _loggedQuestKeyboardMetaSetupHint;
    private static bool _loggedAndroidNonXrKeyboardNull;

    private void Awake()
    {
        _inputField = GetComponent<TMP_InputField>();
        ApplyMobileSoftKeyboardPolicyForXr();

        /* Route controller rays to the input&apos;s main graphic (usually the Image) instead of the nested TMP text mesh. */
        EnsureRaycastHitsFieldChrome();
    }

#if UNITY_EDITOR
    /// <summary>Syncs <see cref="TMP_InputField.shouldHideSoftKeyboard"/> when toggling <see cref="openSystemTouchKeyboardInXr"/> in the Inspector during Play Mode.</summary>
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (_inputField == null)
        {
            TryGetComponent(out _inputField);
        }

        if (_inputField != null)
        {
            ApplyMobileSoftKeyboardPolicyForXr();
        }
    }
#endif

    /// <summary>
    /// While XR is active, hides the Android IME when <see cref="openSystemTouchKeyboardInXr"/> is off so TMP does not fight our explicit open path.
    /// </summary>
    private void ApplyMobileSoftKeyboardPolicyForXr()
    {
        bool prefersFloatingQwertyMinimal =
            questKeyboardUiStrategy == TmpQuestKeyboardUiStrategy.MinimalFloatingQwertyOnly;

        bool hideOsKeyboardWhileFocused =
            Application.isMobilePlatform &&
            XRSettings.isDeviceActive &&
            (!openSystemTouchKeyboardInXr || prefersFloatingQwertyMinimal);

        _inputField.shouldHideSoftKeyboard = hideOsKeyboardWhileFocused;
    }

    private void Start()
    {
        if (useVrRayCatchOverlay)
        {
            EnsureVrRayCatchOverlay();
        }

        EnsureRaycastHitsFieldChrome();
    }

    private void OnDisable()
    {
        if (_vrRayCatchButton != null)
        {
            _vrRayCatchButton.onClick.RemoveListener(OnVrRayCatchClicked);
            _vrRayCatchButton = null;
        }

        StopOpenRoutine();
        _keyboard = null;
        QuestMinimalVrKeyboardFallback.HideIfMatches(_inputField);
    }

    /// <summary>
    /// Stops TMP text meshes (and TMP sub meshes) from eating XR raycasts; keeps chrome Images (scrollbar, viewport) untouched.
    /// </summary>
    private void EnsureRaycastHitsFieldChrome()
    {
        TMP_Text[] textLayers = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < textLayers.Length; i++)
        {
            TMP_Text candidate = textLayers[i];
            if (candidate != null)
            {
                candidate.raycastTarget = false;
            }
        }

        TMP_SubMeshUI[] subMeshLayers = GetComponentsInChildren<TMP_SubMeshUI>(true);
        for (int i = 0; i < subMeshLayers.Length; i++)
        {
            TMP_SubMeshUI candidate = subMeshLayers[i];
            if (candidate != null)
            {
                candidate.raycastTarget = false;
            }
        }

        Image[] caretLikeImages = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < caretLikeImages.Length; i++)
        {
            Image caretCandidate = caretLikeImages[i];
            if (caretCandidate == null)
            {
                continue;
            }

            string objectName = caretCandidate.gameObject.name;
            if (objectName.IndexOf("Caret", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            caretCandidate.raycastTarget = false;
        }

        if (_inputField.placeholder is Graphic placeholderGraphic)
        {
            placeholderGraphic.raycastTarget = false;
        }

        Graphic chrome = ResolveRayReceiverGraphic();
        if (chrome != null)
        {
            chrome.raycastTarget = true;
        }
    }

    /// <summary>
    /// Uses an Image-backed target when TMP mistakenly points <see cref="TMP_InputField.targetGraphic"/> at the text mesh.
    /// </summary>
    private Graphic ResolveRayReceiverGraphic()
    {
        Graphic configured = _inputField.targetGraphic;

        if (configured != null && configured is Image configuredImage && configuredImage.enabled && configuredImage.gameObject.activeInHierarchy)
        {
            return configuredImage;
        }

        Image rootImage = GetComponent<Image>();
        if (rootImage != null && rootImage.enabled && rootImage.gameObject.activeInHierarchy)
        {
            _inputField.targetGraphic = rootImage;
            return rootImage;
        }

        for (int childIndex = 0; childIndex < transform.childCount; childIndex++)
        {
            Image directChildImage = transform.GetChild(childIndex).GetComponent<Image>();
            if (directChildImage == null || !directChildImage.enabled || !directChildImage.gameObject.activeInHierarchy)
            {
                continue;
            }

            string childName = directChildImage.gameObject.name;
            if (childName.IndexOf("Caret", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            _inputField.targetGraphic = directChildImage;
            return directChildImage;
        }

        /*
         TMP sometimes leaves targetGraphic on the TMP_Text graphic. Returning it would re-enable text raycasts in the chrome step and
         reintroduces the XR "ray hits invisible quads first" trap, so deliberately return null unless a non-text Graphic exists.
        */
        if (configured != null && configured is TMP_Text)
        {
            return null;
        }

        return configured;
    }

    /// <inheritdoc />
    public void OnSelect(BaseEventData _)
    {
        BeginTypingSessionFromVr();
    }

    /// <inheritdoc />
    public void OnPointerDown(PointerEventData eventData)
    {
        HandleVrPointer(eventData);
    }

    /// <inheritdoc />
    public void OnPointerClick(PointerEventData eventData)
    {
        HandleVrPointer(eventData);
    }

    /// <inheritdoc />
    public void OnDeselect(BaseEventData _)
    {
        /*
         While our generated keypad stays open for this field, TMP reliably deselects on every controller tap.
         Hiding immediately used to deactivate the keypad before touches applied; dismissal is Done / another field /
         OnDisable instead.
        */
        if (QuestMinimalVrKeyboardFallback.Instance != null &&
            QuestMinimalVrKeyboardFallback.Instance.IsTargeting(_inputField))
        {
            return;
        }

        QuestMinimalVrKeyboardFallback.HideIfMatches(_inputField);
    }

    /// <summary>Confirms selection + caret for XR pointer paths that skip ISelectHandler ordering.</summary>
    private void HandleVrPointer(PointerEventData eventData)
    {
        ApplyVrFocusAndKeyboard(eventData);
    }

    /// <summary>
    /// Spans a transparent <see cref="Button"/> so XRRayInteractor hits the same event path as your working canvas buttons, then forwards focus to this field.
    /// </summary>
    private void EnsureVrRayCatchOverlay()
    {
        Transform existing = transform.Find(VrRayCatchOverlayName);
        if (existing != null)
        {
            Button button = existing.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveListener(OnVrRayCatchClicked);
                button.onClick.AddListener(OnVrRayCatchClicked);
                _vrRayCatchButton = button;
            }

            return;
        }

        GameObject overlayObject = new GameObject(VrRayCatchOverlayName);
        overlayObject.layer = gameObject.layer;
        RectTransform rect = overlayObject.AddComponent<RectTransform>();
        overlayObject.transform.SetParent(transform, false);
        rect.SetAsLastSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;

        Image image = overlayObject.AddComponent<Image>();
        /* Non-zero alpha: zero-alpha images are occasionally culled from raycasts depending on settings. */
        image.color = new Color(1f, 1f, 1f, 0.02f);
        image.raycastTarget = true;

        Button buttonSelectable = overlayObject.AddComponent<Button>();
        buttonSelectable.transition = Selectable.Transition.None;
        buttonSelectable.targetGraphic = image;
        Navigation navigation = buttonSelectable.navigation;
        navigation.mode = Navigation.Mode.None;
        buttonSelectable.navigation = navigation;
        buttonSelectable.interactable = true;
        buttonSelectable.onClick.AddListener(OnVrRayCatchClicked);
        _vrRayCatchButton = buttonSelectable;

        Canvas.ForceUpdateCanvases();
    }

    /// <summary>Invoked by <c>VrRayCatchOverlay</c>; uses one-arg selection since there is no originating <see cref="PointerEventData"/>.</summary>
    private void OnVrRayCatchClicked()
    {
        ApplyVrFocusAndKeyboard(null);
    }

    /// <summary>Selects this TMP field, activates the caret, and queues the soft keyboard coroutine when allowed.</summary>
    /// <param name="eventData">Ray interactor payload when available; otherwise <see langword="null"/> for overlay button clicks.</param>
    private void ApplyVrFocusAndKeyboard(PointerEventData eventData)
    {
        if (_inputField == null || !_inputField.interactable)
        {
            return;
        }

        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject)
        {
            if (eventData != null)
            {
                EventSystem.current.SetSelectedGameObject(gameObject, eventData);
            }
            else
            {
                EventSystem.current.SetSelectedGameObject(gameObject);
            }
        }

        _inputField.Select();
        _inputField.ActivateInputField();

        TmpCredentialFieldUtility.NotifyFieldFocused(_inputField);
        AuthCredentialDiagnosticReport.PublishFieldEvent("fieldFocus", _inputField);
        BeginTypingSessionFromVr();
    }

    private void LateUpdate()
    {
        if (!Application.isMobilePlatform || _inputField == null || !_inputField.interactable)
        {
            return;
        }

        if (_keyboard != null && TouchScreenKeyboard.visible && !_inputField.isFocused)
        {
            if (Time.unscaledTime < _nextSoftKeyboardRefocusUnscaledTime)
            {
                return;
            }

            _nextSoftKeyboardRefocusUnscaledTime = Time.unscaledTime + KeyboardRefocusCooldownSeconds;

            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != _inputField.gameObject)
            {
                EventSystem.current.SetSelectedGameObject(_inputField.gameObject);
            }

            _inputField.ActivateInputField();
        }
    }

    private void Update()
    {
        if (!Application.isMobilePlatform || _inputField == null || _keyboard == null)
        {
            return;
        }

        TouchScreenKeyboard.Status keyboardStatus = _keyboard.status;

        if (keyboardStatus == TouchScreenKeyboard.Status.Done ||
            keyboardStatus == TouchScreenKeyboard.Status.Canceled)
        {
            _keyboard = null;
            return;
        }

        if (_keyboard.text != _inputField.text)
        {
            TmpCredentialFieldUtility.CommitText(_inputField, _keyboard.text);
        }
    }

    /// <summary>Queues focus coroutine and OS keyboard on supported players.</summary>
    private void BeginTypingSessionFromVr()
    {
        if (_inputField == null)
        {
            return;
        }

        if (ShouldExposeFloatingQwertyImmediately())
        {
            if (QuestMinimalVrKeyboardFallback.Instance != null &&
                QuestMinimalVrKeyboardFallback.Instance.IsTargeting(_inputField))
            {
                return;
            }

            StopOpenRoutine();
            _openCoroutine = null;
            QuestMinimalVrKeyboardFallback.ShowFor(_inputField);
            return;
        }

        if (_openCoroutine != null)
        {
            return;
        }

        if (IsKeyboardLikelyStillOnScreen(_keyboard))
        {
            return;
        }

        StopOpenRoutine();
        _openCoroutine = StartCoroutine(OpenKeyboardAfterFocusCoroutine());
    }

    /// <summary>True when the generated VR keypad should open instead of chasing TouchScreenKeyboard.</summary>
    private bool ShouldExposeFloatingQwertyImmediately()
    {
        if (questKeyboardUiStrategy != TmpQuestKeyboardUiStrategy.MinimalFloatingQwertyOnly)
        {
            return false;
        }

        if (Application.isEditor && previewFloatingKeyboardInUnityEditorPlayMode && Camera.main != null)
        {
            return true;
        }

        return QuestMinimalVrKeyboardFallback.IsStandaloneAndroidXrRuntime();
    }

    private static bool IsKeyboardLikelyStillOnScreen(TouchScreenKeyboard candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        TouchScreenKeyboard.Status status = candidate.status;
        if (status == TouchScreenKeyboard.Status.Done ||
            status == TouchScreenKeyboard.Status.Canceled)
        {
            return false;
        }

        return TouchScreenKeyboard.visible;
    }

    private IEnumerator OpenKeyboardAfterFocusCoroutine()
    {
        _nextSoftKeyboardRefocusUnscaledTime = Time.unscaledTime + KeyboardRefocusCooldownSeconds;

        /*
         Selection and ActivateInputField already ran from ApplyVrFocusAndKeyboard / TMP. Re-setting here duplicates work and triggers
         EventSystem: "Attempting to select ... while already selecting an object."
        */
        yield return new WaitForEndOfFrame();
        yield return null;

        if (_inputField == null || !_inputField.isActiveAndEnabled || !_inputField.interactable)
        {
            _openCoroutine = null;
            yield break;
        }

        OpenSystemKeyboardSnapshot(logKeyboardNullFailures: false);
        yield return null;

        bool attemptedOsOverlay =
            Application.isMobilePlatform &&
            XRSettings.isDeviceActive &&
            openSystemTouchKeyboardInXr &&
            questKeyboardUiStrategy != TmpQuestKeyboardUiStrategy.MinimalFloatingQwertyOnly;

        for (int retry = 0;
             attemptedOsOverlay &&
             retry < questKeyboardExtraOpenAttempts;
             retry++)
        {
            if (!ShouldAttemptAnotherQuestKeyboardOpen())
            {
                break;
            }

            yield return new WaitForSecondsRealtime(Mathf.Max(0.02f, questKeyboardRetryDelaySeconds));

            if (_inputField == null || !_inputField.isActiveAndEnabled || !_inputField.interactable)
            {
                break;
            }

            _inputField.ActivateInputField();
            OpenSystemKeyboardSnapshot(logKeyboardNullFailures: false);
            yield return null;
        }

        if (_keyboard == null &&
            Application.isMobilePlatform &&
            questKeyboardUiStrategy == TmpQuestKeyboardUiStrategy.TrySystemKeyboardThenMinimalFallback &&
            QuestMinimalVrKeyboardFallback.IsStandaloneAndroidXrRuntime())
        {
            QuestMinimalVrKeyboardFallback.ShowFor(_inputField);
        }

        bool shouldWarnOsKeyboardFailures =
            questKeyboardUiStrategy == TmpQuestKeyboardUiStrategy.TrySystemKeyboardThenMinimalFallback ||
            questKeyboardUiStrategy == TmpQuestKeyboardUiStrategy.SystemKeyboardAttemptsOnly;

        if (_keyboard == null &&
            Application.isMobilePlatform &&
            openSystemTouchKeyboardInXr &&
            shouldWarnOsKeyboardFailures)
        {
            if (XRSettings.isDeviceActive)
            {
                if (!_loggedQuestKeyboardMetaSetupHint)
                {
                    _loggedQuestKeyboardMetaSetupHint = true;
                    Debug.LogWarning(
                        "[TMP_InputFieldVrKeyboardOpener] TouchScreenKeyboard never returned a handle after retries on Quest. " +
                        "Either switch Quest Keyboard UI Strategy on this component to Minimal Floating Qwerty Only, or install Meta XR Core SDK, " +
                        "enable Focus Awareness, and enable \"Require System Keyboard\" on OVR Manager per " +
                        "https://developers.meta.com/horizon/documentation/unity/unity-keyboard-overlay/");
                }
            }
            else
            {
                if (!_loggedAndroidNonXrKeyboardNull)
                {
                    _loggedAndroidNonXrKeyboardNull = true;
                    Debug.LogWarning(
                        "[TMP_InputFieldVrKeyboardOpener] TouchScreenKeyboard.Open returned null on Android (XR not active). " +
                        "Check IME permissions or pair a Bluetooth keyboard.");
                }
            }
        }

        _openCoroutine = null;
    }

    /// <summary>Returns true when an additional <see cref="TouchScreenKeyboard.Open"/> may help on Quest-class devices.</summary>
    private bool ShouldAttemptAnotherQuestKeyboardOpen()
    {
        if (!Application.isMobilePlatform || !openSystemTouchKeyboardInXr)
        {
            return false;
        }

        if (!XRSettings.isDeviceActive)
        {
            return false;
        }

        if (_inputField == null || !_inputField.isFocused)
        {
            return false;
        }

        if (_keyboard == null)
        {
            return true;
        }

        return !TouchScreenKeyboard.visible;
    }

    /// <summary>Calls <see cref="TouchScreenKeyboard.Open"/> with types compatible with Meta&apos;s keyboard overlay guidance.</summary>
    /// <param name="logKeyboardNullFailures">When false, retries stay quiet; the coroutine logs once at the end.</param>
    private void OpenSystemKeyboardSnapshot(bool logKeyboardNullFailures = true)
    {
        if (!Application.isMobilePlatform)
        {
            return;
        }

        if (XRSettings.isDeviceActive && !openSystemTouchKeyboardInXr)
        {
            if (!_loggedSkippingOsKeyboardForXr)
            {
                _loggedSkippingOsKeyboardForXr = true;
                Debug.Log(
                    "[TMP_InputFieldVrKeyboardOpener] Not opening TouchScreenKeyboard while XR is active (avoids common Quest/IME " +
                    "Application.pause). Focus and caret remain; enable openSystemTouchKeyboardInXr on this component to opt in.");
            }

            return;
        }

        bool shouldUseMultiline = _inputField.lineType == TMP_InputField.LineType.MultiLineNewline;

        string placeholderHintText = string.Empty;
        if (_inputField.placeholder is TMP_Text placeholderTextComponent)
        {
            placeholderHintText = placeholderTextComponent.text;
        }

        int unityKeyboardCharacterCap = _inputField.characterLimit > 0 ? _inputField.characterLimit : 0;

        /*
         Quest immersive often reports TouchScreenKeyboard.isSupported == false; Open can still surface the Meta shell keyboard.
         Do not early-return on isSupported — only skip when we are not on a mobile player.
        */
        _keyboard = TouchScreenKeyboard.Open(
            _inputField.text,
            TouchScreenKeyboardType.Default,
            autocorrection: true,
            multiline: shouldUseMultiline,
            secure: _inputField.inputType == TMP_InputField.InputType.Password,
            alert: false,
            textPlaceholder: placeholderHintText,
            characterLimit: unityKeyboardCharacterCap);

        if (_keyboard == null && logKeyboardNullFailures)
        {
            Debug.LogWarning(
                "[TMP_InputFieldVrKeyboardOpener] TouchScreenKeyboard.Open returned null. Field stays focused for Bluetooth keyboard; " +
                "if you need the system keyboard in VR, enable the Meta / Android keyboard plugin for your OpenXR build.");
        }
        else if (_keyboard != null && !TouchScreenKeyboard.isSupported)
        {
            Debug.Log(
                "[TMP_InputFieldVrKeyboardOpener] TouchScreenKeyboard.isSupported is false, but Open returned a handle — trying Quest shell keyboard.");
        }
    }

    private void StopOpenRoutine()
    {
        if (_openCoroutine != null)
        {
            StopCoroutine(_openCoroutine);
            _openCoroutine = null;
        }
    }
}
