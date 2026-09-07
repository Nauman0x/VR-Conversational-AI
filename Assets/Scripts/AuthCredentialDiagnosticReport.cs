using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

/// <summary>
/// Captures one <see cref="TMP_InputField"/> credential snapshot so Quest sign-in failures can be traced to a single layer.
/// </summary>
public static class AuthCredentialDiagnosticReport
{
    /// <summary>Per-field credential probe used when sign-in reads empty.</summary>
    public readonly struct FieldProbe
    {
        public readonly string FieldName;
        public readonly bool ActiveInHierarchy;
        public readonly bool Interactable;
        public readonly bool IsFocused;
        public readonly int PropertyLength;
        public readonly int SessionLength;
        public readonly int MirrorLength;
        public readonly int InternalLength;
        public readonly int VisualLength;
        public readonly bool VisualMatchesPlaceholder;
        public readonly int KeyboardBufferLength;
        public readonly bool KeyboardTargetsField;
        public readonly string KeyboardTargetName;

        public FieldProbe(
            string fieldName,
            bool activeInHierarchy,
            bool interactable,
            bool isFocused,
            int propertyLength,
            int sessionLength,
            int mirrorLength,
            int internalLength,
            int visualLength,
            bool visualMatchesPlaceholder,
            int keyboardBufferLength,
            bool keyboardTargetsField,
            string keyboardTargetName)
        {
            FieldName = fieldName;
            ActiveInHierarchy = activeInHierarchy;
            Interactable = interactable;
            IsFocused = isFocused;
            PropertyLength = propertyLength;
            SessionLength = sessionLength;
            MirrorLength = mirrorLength;
            InternalLength = internalLength;
            VisualLength = visualLength;
            VisualMatchesPlaceholder = visualMatchesPlaceholder;
            KeyboardBufferLength = keyboardBufferLength;
            KeyboardTargetsField = keyboardTargetsField;
            KeyboardTargetName = keyboardTargetName;
        }
    }

    /// <summary>Builds a probe for <paramref name="field"/> without mutating TMP state.</summary>
    public static FieldProbe ProbeField(TMP_InputField field)
    {
        if (field == null)
        {
            return new FieldProbe(
                "(null)",
                false,
                false,
                false,
                0,
                0,
                0,
                0,
                0,
                false,
                0,
                false,
                string.Empty);
        }

        TmpCredentialFieldUtility.ReadSourceLengths(
            field,
            out int propertyLength,
            out int sessionLength,
            out int mirrorLength,
            out int internalLength,
            out int visualLength,
            out bool visualMatchesPlaceholder);

        QuestMinimalVrKeyboardFallback.TryGetCommittedEditBuffer(field, out string keyboardBuffer);
        int keyboardBufferLength = keyboardBuffer != null ? keyboardBuffer.Length : 0;
        bool keyboardTargetsField = QuestMinimalVrKeyboardFallback.IsTargetingField(field);
        string keyboardTargetName = QuestMinimalVrKeyboardFallback.GetActiveTargetNameOrEmpty();

        return new FieldProbe(
            field.name,
            field.gameObject.activeInHierarchy,
            field.interactable,
            field.isFocused,
            propertyLength,
            sessionLength,
            mirrorLength,
            internalLength,
            visualLength,
            visualMatchesPlaceholder,
            keyboardBufferLength,
            keyboardTargetsField,
            keyboardTargetName);
    }

    /// <summary>Writes a sign-in failure checklist to the Unity console.</summary>
    public static void PublishSignInFailureChecklist(
        GameObject signInPanel,
        TMP_InputField resolvedField,
        string bestPanelText)
    {
        StringBuilder builder = new StringBuilder(640);
        builder.AppendLine("[AuthDiag] Sign-in blocked — checklist:");

        bool panelActive = signInPanel != null && signInPanel.activeInHierarchy;
        builder.Append("  panelActive=").Append(panelActive);

        int fieldCount = signInPanel != null
            ? signInPanel.GetComponentsInChildren<TMP_InputField>(true).Length
            : 0;
        builder.Append(" fields=").Append(fieldCount);

        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        builder.Append(" selected=").Append(selected != null ? selected.name : "(none)");
        builder.Append(" xr=").Append(XRSettings.isDeviceActive);
        builder.Append(" mobile=").Append(Application.isMobilePlatform);
        builder.AppendLine();

        builder.Append("  bestPanelLen=").Append(bestPanelText != null ? bestPanelText.Length : 0);
        builder.AppendLine();

        if (signInPanel != null)
        {
            TMP_InputField[] fields = signInPanel.GetComponentsInChildren<TMP_InputField>(true);
            for (int i = 0; i < fields.Length; i++)
            {
                AppendFieldProbeLine(builder, ProbeField(fields[i]));
            }
        }
        else
        {
            builder.AppendLine("  (signInPanel missing)");
        }

        if (resolvedField != null &&
            (signInPanel == null || !resolvedField.transform.IsChildOf(signInPanel.transform)))
        {
            builder.Append("  resolvedOutsidePanel=");
            AppendFieldProbeLine(builder, ProbeField(resolvedField));
        }

        string report = builder.ToString();
        Debug.LogWarning(report);

        string headline = BuildHeadline(
            resolvedField != null ? ProbeField(resolvedField) : ProbeField(null),
            panelActive,
            fieldCount);
        AuthFlowTrace.Step("UI-002-DIAG", headline, LogType.Warning);
    }

    /// <summary>Short console trace for field focus / keyboard events.</summary>
    public static void PublishFieldEvent(string eventLabel, TMP_InputField field)
    {
        if (field == null)
        {
            AuthFlowTrace.Step("KB-" + eventLabel, eventLabel + " field=(null)", LogType.Warning);
            return;
        }

        FieldProbe probe = ProbeField(field);
        string detail =
            eventLabel + " field=" + probe.FieldName +
            " prop=" + probe.PropertyLength +
            " session=" + probe.SessionLength +
            " kb=" + probe.KeyboardBufferLength +
            " kbTarget=" + probe.KeyboardTargetName;
        AuthFlowTrace.Step("KB-" + eventLabel, detail);
    }

    private static void AppendFieldProbeLine(StringBuilder builder, FieldProbe probe)
    {
        builder.Append("  field=").Append(probe.FieldName);
        builder.Append(" active=").Append(probe.ActiveInHierarchy);
        builder.Append(" interactable=").Append(probe.Interactable);
        builder.Append(" focused=").Append(probe.IsFocused);
        builder.Append(" prop=").Append(probe.PropertyLength);
        builder.Append(" session=").Append(probe.SessionLength);
        builder.Append(" mirror=").Append(probe.MirrorLength);
        builder.Append(" internal=").Append(probe.InternalLength);
        builder.Append(" visual=").Append(probe.VisualLength);
        builder.Append(" visualIsPlaceholder=").Append(probe.VisualMatchesPlaceholder);
        builder.Append(" kbBuf=").Append(probe.KeyboardBufferLength);
        builder.Append(" kbTargets=").Append(probe.KeyboardTargetsField);
        builder.Append(" kbTarget=").Append(probe.KeyboardTargetName);
        builder.AppendLine();
    }

    private static string BuildHeadline(FieldProbe probe, bool panelActive, int fieldCount)
    {
        if (!panelActive)
        {
            return "Diag: sign-in panel inactive.";
        }

        if (fieldCount == 0)
        {
            return "Diag: no TMP fields under sign-in panel.";
        }

        if (probe.VisualMatchesPlaceholder && probe.PropertyLength == 0 && probe.SessionLength == 0)
        {
            return "Diag: placeholder only — open VR keypad and type, or switch keyboard strategy.";
        }

        if (probe.KeyboardBufferLength == 0 && probe.PropertyLength == 0 && probe.SessionLength == 0)
        {
            return "Diag: no committed text — field focus or custom keyboard not writing.";
        }

        if (probe.KeyboardBufferLength > 0 && probe.PropertyLength == 0)
        {
            return "Diag: keyboard buffer has text but TMP property empty — commit path broken.";
        }

        return "Diag: see [AuthDiag] in console for full field probe.";
    }

    /// <summary>On-headset summary when PostgreSQL preflight or Npgsql open fails (Quest auth debugging).</summary>
    public static void PublishPostgresConnectFailureSummary(
        string host,
        int port,
        string rawError,
        NetworkReachability reachability)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            rawError = "(no detail)";
        }

        StringBuilder builder = new StringBuilder(512);
        builder.AppendLine("[AuthDiag] PostgreSQL connect failed — read trace codes in logcat/console:");
        builder.Append("  host=").Append(string.IsNullOrWhiteSpace(host) ? "(missing)" : host);
        builder.Append(" port=").Append(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.Append(" net=").Append(reachability);
        builder.Append(" xr=").Append(XRSettings.isDeviceActive);
        builder.AppendLine();
        builder.Append("  error=").Append(rawError.Length > 220 ? rawError.Substring(0, 220) + "..." : rawError);
        builder.AppendLine();
        builder.AppendLine("  Trace ladder (first FAIL wins):");
        builder.AppendLine("    SignIn-DB-NET-FAIL  → Quest Wi-Fi off / no route");
        builder.AppendLine("    SignIn-DB-DNS-FAIL  → hostname blocked (DNS/VPN/captive portal)");
        builder.AppendLine("    SignIn-DB-TCP-FAIL  → firewall / Aiven IP allowlist / wrong port");
        builder.AppendLine("    SignIn-DB-OPEN-FAIL → TCP ok but Npgsql/SSL/auth failed");
        builder.AppendLine("    SignIn-DB-012       → DB ok; user ID wrong/inactive");

        string report = builder.ToString();
        Debug.LogWarning(report);

        string headline = BuildPostgresFailureHeadline(rawError, reachability);
        AuthFlowTrace.Step("SignIn-DB-DIAG", headline, LogType.Warning);
    }

    private static string BuildPostgresFailureHeadline(string rawError, NetworkReachability reachability)
    {
        if (reachability == NetworkReachability.NotReachable)
        {
            return "Diag: no Wi-Fi route — connect headset to internet.";
        }

        string flat = rawError ?? string.Empty;
        if (flat.IndexOf("DNS", StringComparison.OrdinalIgnoreCase) >= 0 ||
            flat.IndexOf("HostNotFound", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Diag: DNS failed — try another Wi-Fi; disable VPN/private DNS.";
        }

        if (flat.IndexOf("InProgress", StringComparison.OrdinalIgnoreCase) >= 0 ||
            flat.IndexOf("10036", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Diag: socket InProgress — rebuild with latest auth fix; retries should clear this.";
        }

        if (flat.IndexOf("TimedOut", StringComparison.OrdinalIgnoreCase) >= 0 ||
            flat.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Diag: TCP timeout — check Aiven firewall allows Quest public IP.";
        }

        if (flat.IndexOf("ConnectionRefused", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Diag: port refused — verify postgres port in UIManager.";
        }

        if (flat.IndexOf("SSL", StringComparison.OrdinalIgnoreCase) >= 0 ||
            flat.IndexOf("certificate", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Diag: SSL/TLS issue after TCP — check Require SSL setting.";
        }

        if (flat.StartsWith("[NOT_FOUND]", StringComparison.Ordinal))
        {
            return "Diag: DB connected; user ID not found.";
        }

        return "Diag: see [AuthDiag] PostgreSQL connect failed in console.";
    }
}
