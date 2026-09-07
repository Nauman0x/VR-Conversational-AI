using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Commits and reads credential text from <see cref="TMP_InputField"/> on Quest/XR where focus loss and soft-input quirks empty <see cref="TMP_InputField.text"/>.
/// </summary>
public static class TmpCredentialFieldUtility
{
    private static readonly FieldInfo TmpSerializedTextField =
        typeof(TMP_InputField).GetField("m_Text", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly Dictionary<int, string> SessionByFieldId = new Dictionary<int, string>();

    private static TMP_InputField _lastFocusedCredentialField;

    /// <summary>Disables reset-on-deactivate and ensures a <see cref="TmpAuthCredentialMirror"/> exists.</summary>
    public static void PrepareAuthField(TMP_InputField field)
    {
        if (field == null)
        {
            return;
        }

        field.resetOnDeActivation = false;

        if (field.GetComponent<TmpAuthCredentialMirror>() == null)
        {
            field.gameObject.AddComponent<TmpAuthCredentialMirror>();
        }
    }

    /// <summary>Records the field the player is actively editing (VR keypad, focus, or value change).</summary>
    public static void NotifyFieldFocused(TMP_InputField field)
    {
        if (field == null)
        {
            return;
        }

        _lastFocusedCredentialField = field;
    }

    /// <summary>Flushes VR keypad buffers and snapshots every TMP under <paramref name="panelRoot"/> before submit buttons steal focus.</summary>
    public static void CaptureCredentialFieldsBeforeSubmit(GameObject panelRoot)
    {
        if (panelRoot == null)
        {
            AuthFlowTrace.Step("UI-CAP-002", "capture skipped: panelRoot null", LogType.Warning);
            return;
        }

        TMP_InputField[] fields = panelRoot.GetComponentsInChildren<TMP_InputField>(true);
        for (int i = 0; i < fields.Length; i++)
        {
            TMP_InputField field = fields[i];
            if (field == null)
            {
                continue;
            }

            FlushPendingVrKeyboardEdits(field);
            SnapshotFieldIntoSession(field);
        }

        TMP_InputField eventSystemField = TryGetEventSystemSelectedInputField();
        if (eventSystemField != null && eventSystemField.transform.IsChildOf(panelRoot.transform))
        {
            FlushPendingVrKeyboardEdits(eventSystemField);
            SnapshotFieldIntoSession(eventSystemField);
            NotifyFieldFocused(eventSystemField);
        }

        if (_lastFocusedCredentialField != null &&
            _lastFocusedCredentialField.transform.IsChildOf(panelRoot.transform))
        {
            FlushPendingVrKeyboardEdits(_lastFocusedCredentialField);
            SnapshotFieldIntoSession(_lastFocusedCredentialField);
        }

        AuthFlowTrace.Step(
            "UI-CAP-002",
            "captured panel=" + panelRoot.name +
            " fields=" + fields.Length +
            " bestLen=" + ReadBestCredentialTextOnPanel(panelRoot).Length);
    }

    /// <summary>Returns the longest non-placeholder credential string on <paramref name="panelRoot"/>.</summary>
    public static string ReadBestCredentialTextOnPanel(GameObject panelRoot)
    {
        if (panelRoot == null)
        {
            return string.Empty;
        }

        string best = string.Empty;
        TMP_InputField[] fields = panelRoot.GetComponentsInChildren<TMP_InputField>(true);
        for (int i = 0; i < fields.Length; i++)
        {
            string candidate = ReadEffectiveCredentialText(fields[i]);
            if (candidate.Length > best.Length)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Writes <paramref name="value"/> into TMP backing state, mesh, listeners, session store, and the credential mirror.</summary>
    public static void CommitText(TMP_InputField field, string value)
    {
        if (field == null)
        {
            return;
        }

        string normalized = NormalizeCredentialText(value);
        field.SetTextWithoutNotify(normalized);
        field.ForceLabelUpdate();

        if (field.onValueChanged != null)
        {
            field.onValueChanged.Invoke(normalized);
        }

        WriteSession(field, normalized);
        NotifyFieldFocused(field);

        TmpAuthCredentialMirror mirror = TmpAuthCredentialMirror.TryGet(field);
        if (mirror != null)
        {
            mirror.RecordExternal(normalized);
        }
    }

    /// <summary>Flushes any active VR keypad buffer into <paramref name="field"/> before auth validation.</summary>
    public static void FlushPendingVrKeyboardEdits(TMP_InputField field)
    {
        if (field == null)
        {
            return;
        }

        if (QuestMinimalVrKeyboardFallback.TryGetCommittedEditBuffer(field, out string buffered) &&
            !string.IsNullOrWhiteSpace(buffered))
        {
            CommitText(field, buffered);
        }
    }

    /// <summary>Resolves the credential string auth code should validate (session, property, mirror, keypad, internal TMP, then visible text).</summary>
    public static string ReadEffectiveCredentialText(TMP_InputField field)
    {
        if (field == null)
        {
            return string.Empty;
        }

        FlushPendingVrKeyboardEdits(field);

        if (TryReadSession(field, out string fromSession) && !string.IsNullOrWhiteSpace(fromSession))
        {
            return fromSession;
        }

        string fromProperty = NormalizeCredentialText(field.text);
        if (!string.IsNullOrWhiteSpace(fromProperty))
        {
            return fromProperty;
        }

        TmpAuthCredentialMirror mirror = TmpAuthCredentialMirror.TryGet(field);
        if (mirror != null)
        {
            string fromMirror = NormalizeCredentialText(mirror.CommittedText);
            if (!string.IsNullOrWhiteSpace(fromMirror))
            {
                return fromMirror;
            }
        }

        if (TryReadInternalSerializedText(field, out string internalSerialized))
        {
            string fromInternal = NormalizeCredentialText(internalSerialized);
            if (!string.IsNullOrWhiteSpace(fromInternal) && !IsPlaceholderEquivalent(field, fromInternal))
            {
                return fromInternal;
            }
        }

        if (TryReadTextComponentDistinctFromPlaceholder(field, out string fromVisual))
        {
            return fromVisual;
        }

        return string.Empty;
    }

    /// <summary>Non-mutating length probe for auth diagnostics (property, session, mirror, internal TMP, visible mesh).</summary>
    public static void ReadSourceLengths(
        TMP_InputField field,
        out int propertyLength,
        out int sessionLength,
        out int mirrorLength,
        out int internalLength,
        out int visualLength,
        out bool visualMatchesPlaceholder)
    {
        propertyLength = 0;
        sessionLength = 0;
        mirrorLength = 0;
        internalLength = 0;
        visualLength = 0;
        visualMatchesPlaceholder = false;

        if (field == null)
        {
            return;
        }

        propertyLength = NormalizeCredentialText(field.text).Length;

        if (TryReadSession(field, out string fromSession))
        {
            sessionLength = NormalizeCredentialText(fromSession).Length;
        }

        TmpAuthCredentialMirror mirror = TmpAuthCredentialMirror.TryGet(field);
        if (mirror != null)
        {
            mirrorLength = NormalizeCredentialText(mirror.CommittedText).Length;
        }

        if (TryReadInternalSerializedText(field, out string internalSerialized))
        {
            internalLength = NormalizeCredentialText(internalSerialized).Length;
        }

        TMP_Text textComponent = field.textComponent;
        if (textComponent != null)
        {
            string visual = NormalizeCredentialText(textComponent.text);
            visualLength = visual.Length;
            visualMatchesPlaceholder = IsPlaceholderEquivalent(field, visual);
        }
    }

    /// <summary>Stores the latest committed credential for <paramref name="field"/> without touching TMP.</summary>
    public static void WriteSession(TMP_InputField field, string value)
    {
        if (field == null)
        {
            return;
        }

        SessionByFieldId[field.GetInstanceID()] = NormalizeCredentialText(value);
    }

    private static void SnapshotFieldIntoSession(TMP_InputField field)
    {
        if (field == null)
        {
            return;
        }

        string snapshot = ReadCredentialSourcesWithoutFlush(field);
        if (!string.IsNullOrWhiteSpace(snapshot))
        {
            WriteSession(field, snapshot);
        }
    }

    private static string ReadCredentialSourcesWithoutFlush(TMP_InputField field)
    {
        if (TryReadSession(field, out string fromSession) && !string.IsNullOrWhiteSpace(fromSession))
        {
            return fromSession;
        }

        string fromProperty = NormalizeCredentialText(field.text);
        if (!string.IsNullOrWhiteSpace(fromProperty))
        {
            return fromProperty;
        }

        TmpAuthCredentialMirror mirror = TmpAuthCredentialMirror.TryGet(field);
        if (mirror != null)
        {
            string fromMirror = NormalizeCredentialText(mirror.CommittedText);
            if (!string.IsNullOrWhiteSpace(fromMirror))
            {
                return fromMirror;
            }
        }

        if (TryReadInternalSerializedText(field, out string internalSerialized))
        {
            string fromInternal = NormalizeCredentialText(internalSerialized);
            if (!string.IsNullOrWhiteSpace(fromInternal) && !IsPlaceholderEquivalent(field, fromInternal))
            {
                return fromInternal;
            }
        }

        if (TryReadTextComponentDistinctFromPlaceholder(field, out string fromVisual))
        {
            return fromVisual;
        }

        return string.Empty;
    }

    private static bool TryReadSession(TMP_InputField field, out string value)
    {
        value = string.Empty;
        if (field == null)
        {
            return false;
        }

        return SessionByFieldId.TryGetValue(field.GetInstanceID(), out value);
    }

    private static TMP_InputField TryGetEventSystemSelectedInputField()
    {
        if (EventSystem.current == null)
        {
            return null;
        }

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        if (selected == null)
        {
            return null;
        }

        return selected.GetComponent<TMP_InputField>() ??
               selected.GetComponentInParent<TMP_InputField>();
    }

    private static bool TryReadInternalSerializedText(TMP_InputField field, out string text)
    {
        text = string.Empty;
        if (TmpSerializedTextField == null)
        {
            return false;
        }

        text = TmpSerializedTextField.GetValue(field) as string ?? string.Empty;
        return true;
    }

    private static bool TryReadTextComponentDistinctFromPlaceholder(TMP_InputField field, out string normalized)
    {
        normalized = string.Empty;
        if (field == null)
        {
            return false;
        }

        TMP_Text textComponent = field.textComponent;
        if (textComponent == null || !textComponent.enabled)
        {
            return false;
        }

        string candidate = NormalizeCredentialText(textComponent.text);
        if (string.IsNullOrWhiteSpace(candidate) || IsPlaceholderEquivalent(field, candidate))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static bool IsPlaceholderEquivalent(TMP_InputField field, string candidate)
    {
        if (field == null || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        TMP_Text placeholder = field.placeholder as TMP_Text;
        if (placeholder == null)
        {
            return false;
        }

        string placeholderText = NormalizeCredentialText(placeholder.text);
        return !string.IsNullOrEmpty(placeholderText) &&
               string.Equals(candidate, placeholderText, StringComparison.OrdinalIgnoreCase);
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
}
