using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages switching between two avatar GameObjects at runtime.
///
/// Each avatar root is expected to contain an <see cref="AvatarAIController"/>. When the user
/// taps a selection button the currently-active avatar's call is gracefully stopped, that avatar
/// root is deactivated, and the chosen avatar root is activated.
///
/// Wire-up in the Inspector:
/// 1. Assign <c>mikeAvatarRoot</c>  → the Mike NPC root GameObject.
/// 2. Assign <c>annaAvatarRoot</c>  → the Anna NPC root GameObject.
/// 3. Assign <c>mikeButton</c>      → the UI Button that selects Mike.
/// 4. Assign <c>annaButton</c>      → the UI Button that selects Anna.
/// 5. (Optional) Assign per-button highlight/normal colors and labels.
///
/// Example usage:
/// <code>
/// // Triggered automatically by Button.onClick events wired in Inspector or by code.
/// avatarSwitcher.SelectMike();
/// avatarSwitcher.SelectAnna();
/// </code>
/// </summary>
public class AvatarSwitcher : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    [Header("Avatar Roots")]
    [Tooltip("Root GameObject of the Mike avatar (must contain AvatarAIController).")]
    [SerializeField] private GameObject mikeAvatarRoot;

    [Tooltip("Root GameObject of the Anna avatar (must contain AvatarAIController).")]
    [SerializeField] private GameObject annaAvatarRoot;

    [Header("Selection Buttons")]
    [Tooltip("Button that selects Mike.")]
    [SerializeField] private Button mikeButton;

    [Tooltip("Button that selects Anna.")]
    [SerializeField] private Button annaButton;

    [Header("Button Visuals")]
    [Tooltip("Color applied to the button image when the avatar is active.")]
    [SerializeField] private Color activeButtonColor = new Color(0.25f, 0.65f, 1f);

    [Tooltip("Color applied to the button image when the avatar is inactive.")]
    [SerializeField] private Color inactiveButtonColor = new Color(1f, 1f, 1f, 0.55f);

    [Header("Startup")]
    [Tooltip("Which avatar is shown when the scene loads.")]
    [SerializeField] private StartingAvatar defaultAvatar = StartingAvatar.Mike;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    /// <summary>Tracks which avatar is currently shown.</summary>
    private StartingAvatar _activeAvatar;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        RegisterButtonListeners();

        // Activate the default avatar and deactivate the other without calling StopConversation
        // since no call is running yet at startup.
        _activeAvatar = defaultAvatar;

        bool mikeIsDefault = defaultAvatar == StartingAvatar.Mike;
        SetAvatarActive(mikeAvatarRoot, mikeIsDefault);
        SetAvatarActive(annaAvatarRoot, !mikeIsDefault);

        RefreshButtonVisuals();
    }

    private void OnDestroy()
    {
        UnregisterButtonListeners();
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Switches to the Mike avatar.
    /// If Mike is already active this is a no-op.
    /// Any active call on Anna is stopped before the switch.
    /// </summary>
    public void SelectMike()
    {
        if (_activeAvatar == StartingAvatar.Mike)
        {
            return;
        }

        SwitchTo(StartingAvatar.Mike);
    }

    /// <summary>
    /// Switches to the Anna avatar.
    /// If Anna is already active this is a no-op.
    /// Any active call on Mike is stopped before the switch.
    /// </summary>
    public void SelectAnna()
    {
        if (_activeAvatar == StartingAvatar.Anna)
        {
            return;
        }

        SwitchTo(StartingAvatar.Anna);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Core switching logic.
    /// Stops the outgoing avatar's conversation, deactivates its root,
    /// activates the incoming avatar's root, then updates button visuals.
    /// </summary>
    private void SwitchTo(StartingAvatar target)
    {
        // Resolve which root is leaving and which is arriving.
        GameObject outgoingRoot = _activeAvatar == StartingAvatar.Mike ? mikeAvatarRoot : annaAvatarRoot;
        GameObject incomingRoot = target == StartingAvatar.Mike ? mikeAvatarRoot : annaAvatarRoot;

        // Gracefully stop any in-progress ElevenLabs call on the outgoing avatar
        // before deactivating it — avoids orphaned WebSocket connections.
        if (outgoingRoot != null)
        {
            AvatarAIController outgoingController = outgoingRoot.GetComponent<AvatarAIController>();
            if (outgoingController != null)
            {
                outgoingController.StopConversation();
            }
        }

        SetAvatarActive(outgoingRoot, false);
        SetAvatarActive(incomingRoot, true);

        _activeAvatar = target;
        RefreshButtonVisuals();

        Debug.Log($"[AvatarSwitcher] Switched to {target}.");
    }

    /// <summary>Activates or deactivates an avatar root, guarding against null.</summary>
    private static void SetAvatarActive(GameObject avatarRoot, bool active)
    {
        if (avatarRoot != null)
        {
            avatarRoot.SetActive(active);
        }
    }

    /// <summary>
    /// Updates button tint colors and TMP labels to reflect the currently active avatar.
    /// Buttons stay interactable at all times so users can always switch.
    /// </summary>
    private void RefreshButtonVisuals()
    {
        bool mikeActive = _activeAvatar == StartingAvatar.Mike;

        ApplyButtonVisual(mikeButton, isActive: mikeActive);
        ApplyButtonVisual(annaButton, isActive: !mikeActive);
    }

    /// <summary>
    /// Tints the button's <see cref="Image"/> to reflect active/inactive state,
    /// and appends " ✓" to the button label when active.
    /// </summary>
    private void ApplyButtonVisual(Button button, bool isActive)
    {
        if (button == null)
        {
            return;
        }

        // Tint the button background image.
        Image buttonImage = button.GetComponent<Image>();
        if (buttonImage != null)
        {
            buttonImage.color = isActive ? activeButtonColor : inactiveButtonColor;
        }

        // Update the TMP label if one exists as a direct child.
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label == null)
        {
            return;
        }

        // Preserve the base name by stripping any previous checkmark suffix before re-adding it.
        string baseName = label.text.Replace(" ✓", string.Empty).Trim();
        label.text = isActive ? $"{baseName} ✓" : baseName;
    }

    private void RegisterButtonListeners()
    {
        if (mikeButton != null)
        {
            mikeButton.onClick.AddListener(SelectMike);
        }

        if (annaButton != null)
        {
            annaButton.onClick.AddListener(SelectAnna);
        }
    }

    private void UnregisterButtonListeners()
    {
        if (mikeButton != null)
        {
            mikeButton.onClick.RemoveListener(SelectMike);
        }

        if (annaButton != null)
        {
            annaButton.onClick.RemoveListener(SelectAnna);
        }
    }

    // -------------------------------------------------------------------------
    // Supporting types
    // -------------------------------------------------------------------------

    /// <summary>Identifies which avatar is currently selected.</summary>
    private enum StartingAvatar
    {
        Mike,
        Anna
    }
}
