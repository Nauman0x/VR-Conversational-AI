using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// World-space keypad parented below the XR camera — works without Meta TouchScreenKeyboard / Meta XR Core SDK plumbing.
/// </summary>
/// <remarks>
/// OpenXR-only Quest builds often ignore <see cref="TouchScreenKeyboard.Open"/>; this panel is clickable with XRIT tracked UI rays when
/// the canvas carries <see cref="TrackedDeviceGraphicRaycaster"/>.
/// </remarks>
[DefaultExecutionOrder(200)]
public sealed class QuestMinimalVrKeyboardFallback : MonoBehaviour
{
    public static QuestMinimalVrKeyboardFallback Instance { get; private set; }

    [SerializeField]
    private Vector3 cameraLocalPosition = new Vector3(0f, -0.34f, 0.7f);

    [SerializeField]
    private Vector3 cameraLocalEuler = new Vector3(12f, 0f, 0f);

    /* Tall enough that banner + numeric + letter block + footer do not vertically overlap when layout groups drive heights. */
    [SerializeField]
    private Vector2 canvasPixelSize = new Vector2(1100f, 480f);

    [SerializeField]
    private float canvasUniformScale = 0.00048f;

    private Canvas _canvas;
    private RectTransform _letterRowsHost;
    private TMP_InputField _target;
    private bool _capsLatch;
    private string _committedEditBuffer = string.Empty;

    /* Bump whenever layout rules change — existing DontDestroyOnLoad instances rebuild keyboard UI instead of keeping broken geometry. */
    private const int KeyboardLayoutGeneration = 2;
    private int _appliedLayoutGeneration = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        gameObject.name = nameof(QuestMinimalVrKeyboardFallback);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>Shows or updates the keypad for this TMP field.</summary>
    public static void ShowFor(TMP_InputField targetInputField)
    {
        if (targetInputField == null)
        {
            return;
        }

        if (Instance == null)
        {
            GameObject host = new GameObject(nameof(QuestMinimalVrKeyboardFallback) + "_Bootstrap");
            host.AddComponent<QuestMinimalVrKeyboardFallback>();
        }

        Instance.ShowInternal(targetInputField);
    }

    /// <summary>Hides the keypad when argument matches the field it was editing.</summary>
    public static void HideIfMatches(TMP_InputField candidate)
    {
        if (Instance != null && Instance._target == candidate)
        {
            Instance.HideInternal();
        }
    }

    /// <summary>
    /// True while this keypad is visible and tied to <paramref name="candidate"/> — used to defer TMP deselect-hide when keys steal UI focus.
    /// </summary>
    public bool IsTargeting(TMP_InputField candidate)
    {
        return candidate != null &&
               _canvas != null &&
               _canvas.gameObject.activeInHierarchy &&
               _target == candidate;
    }

    /// <summary>Latest keypad buffer for <paramref name="field"/> when the floating keyboard is editing it.</summary>
    public static bool TryGetCommittedEditBuffer(TMP_InputField field, out string bufferedText)
    {
        bufferedText = string.Empty;
        if (Instance == null || field == null || Instance._target != field)
        {
            return false;
        }

        bufferedText = Instance._committedEditBuffer ?? string.Empty;
        return true;
    }

    /// <summary>True when the floating keypad is open for <paramref name="field"/>.</summary>
    public static bool IsTargetingField(TMP_InputField field)
    {
        return Instance != null &&
               field != null &&
               Instance._target == field &&
               Instance._canvas != null &&
               Instance._canvas.gameObject.activeInHierarchy;
    }

    /// <summary>Name of the TMP field the floating keypad is editing, if any.</summary>
    public static string GetActiveTargetNameOrEmpty()
    {
        return Instance != null && Instance._target != null ? Instance._target.name : string.Empty;
    }

    /// <summary>Root of generated keyboard UI (world-space canvas transform).</summary>
    public Transform KeyboardRoot => _canvas != null ? _canvas.transform : null;

    private void ShowInternal(TMP_InputField targetInputField)
    {
        EnsureKeyboardUiBuilt();

        bool preserveCaseState =
            _canvas != null &&
            _canvas.gameObject.activeInHierarchy &&
            _target == targetInputField;

        _target = targetInputField;
        if (!preserveCaseState)
        {
            _capsLatch = false;
        }

        _committedEditBuffer = TmpCredentialFieldUtility.ReadEffectiveCredentialText(_target);
        TmpCredentialFieldUtility.NotifyFieldFocused(_target);
        AuthCredentialDiagnosticReport.PublishFieldEvent("keyboardOpen", _target);

        if (!HasUsableCamera())
        {
            Debug.LogWarning(
                "[QuestMinimalVrKeyboardFallback] Cannot show keyboard — tag your XR camera GameObject \"MainCamera\" or assign Camera.main.");
            return;
        }

        ReparentToMainXrCamera();
        RebuildLetterRows();
        _canvas.gameObject.SetActive(true);
        SyncCaretFromTarget();
    }

    private void HideInternal()
    {
        if (_target != null)
        {
            TmpCredentialFieldUtility.CommitText(_target, _committedEditBuffer);
        }

        _target = null;
        _committedEditBuffer = string.Empty;
        if (_canvas != null)
        {
            _canvas.gameObject.SetActive(false);
        }
    }

    private void EnsureKeyboardUiBuilt()
    {
        if (_canvas != null && _appliedLayoutGeneration == KeyboardLayoutGeneration)
        {
            return;
        }

        if (_canvas != null)
        {
            Destroy(_canvas.gameObject);
            _canvas = null;
            _letterRowsHost = null;
        }

        BuildUiHierarchy();
        _appliedLayoutGeneration = KeyboardLayoutGeneration;
    }

    private static bool HasUsableCamera()
    {
        return Camera.main != null;
    }

    private void ReparentToMainXrCamera()
    {
        if (_canvas == null || !HasUsableCamera())
        {
            return;
        }

        Transform cam = Camera.main.transform;
        _canvas.transform.SetParent(cam, false);
        _canvas.transform.localPosition = cameraLocalPosition;
        _canvas.transform.localRotation = Quaternion.Euler(cameraLocalEuler);
        float s = Mathf.Max(0.0001f, canvasUniformScale);
        _canvas.transform.localScale = new Vector3(s, s, s);
    }

    /// <summary>Build keys, layout groups, raycaster.</summary>
    private void BuildUiHierarchy()
    {
        GameObject canvasGo = new GameObject("MinimalVrKeyboardCanvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = 32700;
        canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();

        RectTransform crt = canvasGo.GetComponent<RectTransform>();
        crt.sizeDelta = canvasPixelSize;
        crt.pivot = new Vector2(0.5f, 0f);
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);

        Image panelBackdrop = canvasGo.AddComponent<Image>();
        panelBackdrop.color = new Color(0.06f, 0.07f, 0.09f, 0.94f);
        /*
         Consume rays across empty keypad areas so XR hits do not pass through and deselect the TMP field /
         deactivate the keypad before a key fires.
        */
        panelBackdrop.raycastTarget = true;

        VerticalLayoutGroup rootLayout = crt.gameObject.AddComponent<VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(10, 10, 8, 8);
        rootLayout.spacing = 6f;
        rootLayout.childAlignment = TextAnchor.UpperCenter;
        rootLayout.childControlWidth = true;
        rootLayout.childForceExpandWidth = true;
        /*
         REQUIRED: Without childControlHeight, flexible/min heights on nested rows are ignored and footer rows overlap letters.
        */
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandHeight = false;

        LayoutElement crtLe = crt.gameObject.AddComponent<LayoutElement>();
        crtLe.minWidth = canvasPixelSize.x;
        crtLe.minHeight = canvasPixelSize.y;

        TMP_Text banner = CreateTMP("_Hint", crt, "VR keyboard — tap letters (Caps / Space / ← / Done)", 18f);
        banner.alignment = TextAlignmentOptions.Top;
        LayoutElement bannerLe = banner.gameObject.AddComponent<LayoutElement>();
        bannerLe.preferredHeight = 28f;

        string[] numericRowChars = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "_" };
        CreateHorizontalRow("RowNumbers", crt, numericRowChars, evenlyDistributeKeyWidth: true);

        _letterRowsHost = new GameObject("_LetterRows").AddComponent<RectTransform>();
        _letterRowsHost.SetParent(crt, false);
        StretchFullWidth(_letterRowsHost);
        VerticalLayoutGroup vl = _letterRowsHost.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 6f;
        vl.childAlignment = TextAnchor.MiddleCenter;
        vl.childControlWidth = true;
        vl.childForceExpandWidth = true;
        vl.childControlHeight = true;
        vl.childForceExpandHeight = false;

        LayoutElement letterHostLe = _letterRowsHost.gameObject.AddComponent<LayoutElement>();
        /* Fixed block (3 qwerty rows + optional . /@) taller than overlapping footer when flex was ignored */
        letterHostLe.preferredHeight = 218f;

        GameObject footer = new GameObject("_Footer");
        footer.transform.SetParent(crt, false);
        HorizontalLayoutGroup foot = footer.AddComponent<HorizontalLayoutGroup>();
        foot.spacing = 8f;
        foot.childForceExpandHeight = false;
        foot.childControlHeight = true;
        foot.childControlWidth = false;
        LayoutElement footLe = footer.AddComponent<LayoutElement>();
        footLe.preferredHeight = 48f;
        StretchFullWidth(footer.GetComponent<RectTransform>());

        CreateFooterButton("[ Caps ]", foot.transform, ToggleCapsLatch);
        CreateFooterButton(@"[ ← ] Backspace", foot.transform, EraseOne);
        CreateFooterButton(@"[ _____ ] Space", foot.transform, () => AppendText(" "));
        CreateFooterButton("[ ✓ Done ]", foot.transform, HideInternal);

        _canvas.gameObject.SetActive(false);
    }

    private void CreateHorizontalRow(string rowName, Transform parent, IReadOnlyList<string> glyphs, bool evenlyDistributeKeyWidth)
    {
        GameObject row = new GameObject(rowName);
        row.transform.SetParent(parent, false);
        HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 4f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childForceExpandHeight = false;
        h.childControlHeight = true;
        if (evenlyDistributeKeyWidth)
        {
            h.childControlWidth = true;
            h.childForceExpandWidth = true;
        }
        else
        {
            h.childControlWidth = false;
            h.childForceExpandWidth = false;
        }

        LayoutElement rowLe = row.AddComponent<LayoutElement>();
        rowLe.preferredHeight = 42f;
        StretchFullWidth(row.GetComponent<RectTransform>());

        for (int i = 0; i < glyphs.Count; i++)
        {
            string g = glyphs[i];
            KeyButton(row.transform, g, () => AppendText(g), evenlyDistributeKeyWidth);
        }
    }

    private void RebuildLetterRows()
    {
        if (_letterRowsHost == null)
        {
            return;
        }

        for (int c = _letterRowsHost.childCount - 1; c >= 0; c--)
        {
            Destroy(_letterRowsHost.GetChild(c).gameObject);
        }

        string[] rows = GetLetterRowTemplates();
        for (int r = 0; r < rows.Length; r++)
        {
            string rowChars = ExpandCase(rows[r]);
            List<string> list = new List<string>(rowChars.Length);
            for (int i = 0; i < rowChars.Length; i++)
            {
                list.Add(rowChars.Substring(i, 1));
            }

            CreateHorizontalRow("Letters_" + r, _letterRowsHost, list, evenlyDistributeKeyWidth: true);
        }

        Transform lastRowTransform = _letterRowsHost.childCount > 0 ? _letterRowsHost.GetChild(_letterRowsHost.childCount - 1).transform : null;
        if (lastRowTransform != null)
        {
            KeyButton(lastRowTransform, ".", () => AppendText("."), evenlyExpandWidth: true);
            KeyButton(lastRowTransform, "@", () => AppendText("@"), evenlyExpandWidth: true);
        }
    }

    private static string[] GetLetterRowTemplates()
    {
        return new[]
        {
            "qwertyuiop",
            "asdfghjkl",
            "zxcvbnm",
        };
    }

    private string ExpandCase(string lowers)
    {
        if (!_capsLatch)
        {
            return lowers;
        }

        return lowers.ToUpperInvariant();
    }

    private void ToggleCapsLatch()
    {
        _capsLatch = !_capsLatch;
        RebuildLetterRows();
    }

    private void EraseOne()
    {
        if (_target == null)
        {
            return;
        }

        string t = _committedEditBuffer ?? string.Empty;
        if (t.Length <= 0)
        {
            return;
        }

        _committedEditBuffer = t.Substring(0, t.Length - 1);
        TmpCredentialFieldUtility.CommitText(_target, _committedEditBuffer);
        SyncCaretFromTarget();
    }

    private void AppendText(string segment)
    {
        if (_target == null || string.IsNullOrEmpty(segment))
        {
            return;
        }

        string t = _committedEditBuffer ?? TmpCredentialFieldUtility.ReadEffectiveCredentialText(_target);

        /*
         Honour character caps on single glyphs so Caps still matters for punctuation rows that delegate here.
        */
        if (segment.Length == 1 && _capsLatch && char.IsLetter(segment, 0))
        {
            segment = segment.ToUpperInvariant();
        }

        if (_target.characterLimit > 0 &&
            (t.Length + segment.Length > _target.characterLimit))
        {
            return;
        }

        _committedEditBuffer = t + segment;
        TmpCredentialFieldUtility.CommitText(_target, _committedEditBuffer);
        AuthCredentialDiagnosticReport.PublishFieldEvent("kbKey", _target);
        SyncCaretFromTarget();
    }

    private void SyncCaretFromTarget()
    {
        if (_target == null)
        {
            return;
        }

        _target.caretPosition = _committedEditBuffer.Length;
        /*
         Selecting the TMP on each stroke can reorder UI focus awkwardly under XRIT; activating alone keeps caret in sync with length.
        */
        _target.ActivateInputField();
    }

    private static void StretchFullWidth(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
        rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
        rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
        rt.offsetMax = new Vector2(0f, rt.offsetMax.y);
    }

    private static void StretchFullAnchors(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static TMP_Text CreateTMP(string name, Transform parent, string textBody, float size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        TextMeshProUGUI txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = textBody;
        txt.fontSize = size;
        txt.color = Color.white;
        txt.raycastTarget = false;
        txt.enableWordWrapping = false;
        return txt;
    }

    private void KeyButton(Transform row, string label, UnityAction action, bool evenlyExpandWidth)
    {
        GameObject go = new GameObject("Key_" + label);
        go.transform.SetParent(row, false);
        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.21f, 0.23f, 0.92f);
        bg.raycastTarget = true;

        MinimalVrKeyboardKey tap = go.AddComponent<MinimalVrKeyboardKey>();
        tap.Assign(action);

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minHeight = 40f;
        if (evenlyExpandWidth)
        {
            le.flexibleWidth = 1f;
            le.minWidth = 28f;
        }
        else
        {
            le.minWidth = Mathf.Max(32f, 18f + 9f * label.Length);
        }

        GameObject lbl = new GameObject("Lbl");
        lbl.transform.SetParent(go.transform, false);
        TextMeshProUGUI txt = lbl.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.fontSize = 22f;
        txt.color = Color.white;
        txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        RectTransform lrt = lbl.GetComponent<RectTransform>();
        StretchFullAnchors(lrt);
    }

    private void CreateFooterButton(string caption, Transform row, UnityAction onClick)
    {
        KeyButton(row, caption, onClick, evenlyExpandWidth: false);

        Transform last = row.GetChild(row.childCount - 1);
        LayoutElement footerLe = last.GetComponent<LayoutElement>();
        if (caption.Contains("Space", System.StringComparison.Ordinal))
        {
            footerLe.flexibleWidth = 1f;
            footerLe.minWidth = 220f;
        }
        else
        {
            footerLe.preferredWidth = Mathf.Max(110f, 18f * caption.Length * 0.45f);
        }
    }

    /// <summary>True when APK runs on headset with XR running.</summary>
    public static bool IsStandaloneAndroidXrRuntime()
    {
        return Application.isMobilePlatform && XRSettings.isDeviceActive;
    }
}

/// <summary>
/// Listens on <see cref="IPointerDownHandler"/> because XR taps often cancel <see cref="Button.onClick"/>.
/// </summary>
internal sealed class MinimalVrKeyboardKey : MonoBehaviour, IPointerDownHandler
{
    private UnityAction _action;

    internal void Assign(UnityAction action)
    {
        _action = action;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        /*
         Consume the press phase so jitter does not have to satisfy Button click / drag thresholds.
        */
        _action?.Invoke();
    }
}
