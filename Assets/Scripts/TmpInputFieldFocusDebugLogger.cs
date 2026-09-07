using TMPro;
using UnityEngine;

/// <summary>
/// Logs when a <see cref="TMP_InputField"/> gains or loses focus. For Quest/standalone builds where the Editor Pause button is unavailable.
/// </summary>
/// <remarks>
/// 1. Add this component next to your <see cref="TMP_InputField"/> (or assign the reference).<br/>
/// 2. Enable <see cref="debugLogToConsole"/>.<br/>
/// 3. Make a Development Build and watch output:<br/>
///    <c>adb logcat -s Unity</c> (filter for <c>[TmpInputFocus]</c>) on Quest / Android.<br/>
/// Optional: assign <see cref="statusLabel"/> for a short on-world debug line (assign a TMP_Text under your UI).
/// </remarks>
public sealed class TmpInputFieldFocusDebugLogger : MonoBehaviour
{
    [SerializeField] private TMP_InputField targetField;
    [SerializeField] private bool debugLogToConsole = true;
    [SerializeField] private TMP_Text statusLabel;
    private bool _lastIsFocused;

    /// <summary>Resolves the input field from the same GameObject when none is assigned.</summary>
    private void Awake()
    {
        if (targetField == null)
        {
            targetField = GetComponent<TMP_InputField>();
        }
    }

    /// <summary>Tracks focus transitions and refreshes an optional on-screen status label.</summary>
    private void Update()
    {
        if (targetField == null)
        {
            return;
        }

        bool focused = targetField.isFocused;

        if (focused != _lastIsFocused)
        {
            _lastIsFocused = focused;

            if (debugLogToConsole)
            {
                Debug.Log("[TmpInputFocus] " + targetField.gameObject.name + " isFocused=" + focused);
            }
        }

        if (statusLabel != null)
        {
            statusLabel.text = targetField.gameObject.name + "\nFocus=" + focused + "\nTextLen=" + (targetField.text != null ? targetField.text.Length : 0);
        }
    }
}
