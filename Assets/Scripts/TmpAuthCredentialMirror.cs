using TMPro;
using UnityEngine;

/// <summary>
/// Keeps a stable credential string for auth <see cref="TMP_InputField"/> instances when Quest/XR focus changes clear <see cref="TMP_InputField.text"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_InputField))]
public sealed class TmpAuthCredentialMirror : MonoBehaviour
{
    [SerializeField] private string committedText = string.Empty;

    private TMP_InputField _field;

    /// <summary>Last non-empty credential captured from value/end-edit events.</summary>
    public string CommittedText => committedText ?? string.Empty;

    private void Awake()
    {
        _field = GetComponent<TMP_InputField>();
        TmpCredentialFieldUtility.PrepareAuthField(_field);
        _field.onValueChanged.AddListener(OnValueChanged);
        _field.onEndEdit.AddListener(OnEndEdit);
        Record(_field.text);
    }

    private void OnDestroy()
    {
        if (_field == null)
        {
            return;
        }

        _field.onValueChanged.RemoveListener(OnValueChanged);
        _field.onEndEdit.RemoveListener(OnEndEdit);
    }

    private void OnValueChanged(string value)
    {
        Record(value);
    }

    private void OnEndEdit(string value)
    {
        Record(value);
    }

    /// <summary>Updates the mirror when an external writer commits text (VR keypad, IME sync).</summary>
    public void RecordExternal(string value)
    {
        Record(value);
    }

    private void Record(string value)
    {
        committedText = value ?? string.Empty;

        if (_field == null)
        {
            _field = GetComponent<TMP_InputField>();
        }

        if (_field == null)
        {
            return;
        }

        TmpCredentialFieldUtility.WriteSession(_field, committedText);
        TmpCredentialFieldUtility.NotifyFieldFocused(_field);
    }

    /// <summary>Returns the mirror on <paramref name="field"/> when present.</summary>
    public static TmpAuthCredentialMirror TryGet(TMP_InputField field)
    {
        return field != null ? field.GetComponent<TmpAuthCredentialMirror>() : null;
    }
}
