using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires up the three UI buttons that live on the Canvas inside the Nature scene:
/// Call, Back, and Quit.
///
/// This component is intentionally thin — it delegates all real work to existing
/// systems (<see cref="AvatarAIController"/> and <see cref="SceneTransitionManager"/>)
/// and only handles the button → method binding so the Inspector stays clean.
///
/// Wire-up in the Inspector (Nature scene):
/// 1. Create an empty GameObject on the Canvas called "NatureSceneUI".
/// 2. Attach this script to it.
/// 3. Assign <c>callButton</c>  → the Call Button in the Nature Canvas.
/// 4. Assign <c>backButton</c>  → the Back Button in the Nature Canvas.
/// 5. Assign <c>quitButton</c>  → the Quit Button in the Nature Canvas.
/// 6. (Optional) Assign <c>avatarAIController</c> if there is an
///    <see cref="AvatarAIController"/> in the scene; if left null it will be
///    located automatically with FindObjectOfType.
///
/// Example usage:
/// <code>
/// // All wiring happens automatically in Start(); no manual calls needed.
/// </code>
/// </summary>
public class NatureSceneUI : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    [Header("Nature Scene Buttons")]
    [Tooltip("The Call / End Call toggle button — mirrors the same button in SampleScene.")]
    [SerializeField] private Button callButton;

    [Tooltip("Returns the player to the previous scene (SampleScene by default).")]
    [SerializeField] private Button backButton;

    [Tooltip("Quits the application (stops Play mode in the Editor).")]
    [SerializeField] private Button quitButton;

    [Header("Dependencies")]
    [Tooltip("Leave null to auto-locate via FindObjectOfType.")]
    [SerializeField] private AvatarAIController avatarAIController;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        ResolveAvatarController();
        BindButtons();
    }

    private void OnDestroy()
    {
        UnbindButtons();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds <see cref="AvatarAIController"/> in the scene if not assigned in the Inspector.
    /// Logs a warning when none is found so the developer knows the Call button will be inert.
    /// </summary>
    private void ResolveAvatarController()
    {
        if (avatarAIController != null)
        {
            return;
        }

        avatarAIController = FindObjectOfType<AvatarAIController>(true);

        if (avatarAIController == null)
        {
            Debug.LogWarning(
                "[NatureSceneUI] No AvatarAIController found in the Nature scene. " +
                "The Call button will not function until one is set up.");
        }
    }

    /// <summary>
    /// Registers onClick listeners for all three buttons.
    /// Gracefully skips any button that was not assigned in the Inspector.
    /// </summary>
    private void BindButtons()
    {
        if (callButton != null)
        {
            callButton.onClick.AddListener(OnCallButtonClicked);
        }
        else
        {
            Debug.LogWarning("[NatureSceneUI] Call button not assigned.");
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(OnBackButtonClicked);
        }
        else
        {
            Debug.LogWarning("[NatureSceneUI] Back button not assigned.");
        }

        if (quitButton != null)
        {
            quitButton.onClick.AddListener(OnQuitButtonClicked);
        }
        else
        {
            Debug.LogWarning("[NatureSceneUI] Quit button not assigned.");
        }
    }

    /// <summary>Removes all onClick listeners to prevent memory leaks on destroy.</summary>
    private void UnbindButtons()
    {
        if (callButton != null)
        {
            callButton.onClick.RemoveListener(OnCallButtonClicked);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(OnBackButtonClicked);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(OnQuitButtonClicked);
        }
    }

    // -------------------------------------------------------------------------
    // Button handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Forwards the click to the avatar's conversation toggle.
    /// Guards against a missing controller so the button press is never a hard crash.
    /// </summary>
    private void OnCallButtonClicked()
    {
        if (avatarAIController == null)
        {
            Debug.LogWarning("[NatureSceneUI] Call clicked but AvatarAIController is missing.");
            return;
        }

        avatarAIController.OnCallToggleClicked();
    }

    /// <summary>
    /// Asks the <see cref="SceneTransitionManager"/> to return to the previous scene.
    /// If the singleton is missing (e.g. a cold start directly into Nature in the Editor)
    /// it falls back to loading SampleScene by name.
    /// </summary>
    private void OnBackButtonClicked()
    {
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.LoadPreviousScene();
            return;
        }

        // Fallback: singleton was not present (direct scene start in Editor).
        Debug.LogWarning(
            "[NatureSceneUI] SceneTransitionManager singleton not found. " +
            "Loading SampleScene directly as fallback.");

        UnityEngine.SceneManagement.SceneManager.LoadScene("SampleScene");
    }

    /// <summary>
    /// Asks the <see cref="SceneTransitionManager"/> to quit.
    /// Falls back to Application.Quit() if the singleton is missing.
    /// </summary>
    private void OnQuitButtonClicked()
    {
        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance.QuitApplication();
            return;
        }

        // Fallback.
        Debug.LogWarning("[NatureSceneUI] SceneTransitionManager singleton not found. Quitting directly.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
