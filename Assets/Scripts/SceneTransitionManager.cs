using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Singleton scene-transition manager that survives scene loads.
///
/// Responsibilities:
/// - Load a named scene and remember which scene was active before the jump.
/// - Provide a "go back" action that returns to the scene recorded at transition time.
/// - Provide a clean application-quit path.
///
/// Wire-up:
/// 1. Create an empty GameObject in SampleScene (e.g. "SceneTransitionManager").
/// 2. Attach this script to it.
/// 3. Wire the "Nature" button's onClick → SceneTransitionManager.LoadNatureScene().
///
/// The same singleton persists into the Nature scene automatically via DontDestroyOnLoad,
/// so the Back button there can call SceneTransitionManager.Instance.LoadPreviousScene()
/// and the Quit button can call SceneTransitionManager.Instance.QuitApplication().
///
/// Example usage:
/// <code>
/// // From any UI Button onClick:
/// SceneTransitionManager.Instance.LoadNatureScene();
/// SceneTransitionManager.Instance.LoadPreviousScene();
/// SceneTransitionManager.Instance.QuitApplication();
/// </code>
/// </summary>
public class SceneTransitionManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Singleton
    // -------------------------------------------------------------------------

    /// <summary>Global access point to the single manager instance.</summary>
    public static SceneTransitionManager Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Constants — scene names must exactly match the names in Build Settings.
    // -------------------------------------------------------------------------

    /// <summary>Name of the main/lobby scene.</summary>
    private const string MainSceneName = "SampleScene";

    /// <summary>Name of the outdoor nature environment scene.</summary>
    private const string NatureSceneName = "Nature";

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    /// <summary>
    /// Name of the scene that was active before the most recent transition.
    /// Used by <see cref="LoadPreviousScene"/> to support the Back button.
    /// Defaults to the main scene so Back always has a safe destination.
    /// </summary>
    private string _previousSceneName = MainSceneName;

    /// <summary>
    /// Whether the user has successfully authenticated at least once during this session.
    /// Stamped by <see cref="SetAuthenticated"/> when UIManager fires OnSignedIn / OnSignedUp
    /// in SampleScene. Persists across scene loads so other scenes (e.g. Nature) can skip
    /// the auth gate and show the call button immediately.
    /// </summary>
    public bool IsAuthenticated { get; private set; }

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        /* Enforce singleton: if another instance already exists (e.g. the manager
           was carried across a scene load), destroy this duplicate immediately. */
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Persist this GameObject across all scene loads so the transition
        // manager (and its _previousSceneName state) survives scene switches.
        DontDestroyOnLoad(gameObject);

        // Seed the previous-scene tracker with whichever scene is active at startup.
        _previousSceneName = SceneManager.GetActiveScene().name;
    }

    // -------------------------------------------------------------------------
    // Public API — wire these directly to Button.onClick in the Inspector
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads the Nature outdoor scene.
    /// Records the currently active scene so the Back button can return to it.
    ///
    /// Called by the "Nature" button onClick in SampleScene.
    /// </summary>
    public void LoadNatureScene()
    {
        TransitionTo(NatureSceneName);
    }

    /// <summary>
    /// Loads SampleScene (the main lobby / conversation scene).
    /// Records the currently active scene so subsequent Back actions are correct.
    ///
    /// Called by a "Back to Main" button if ever needed from the Nature scene.
    /// </summary>
    public void LoadMainScene()
    {
        TransitionTo(MainSceneName);
    }

    /// <summary>
    /// Returns to whichever scene was active before the last call to
    /// <see cref="LoadNatureScene"/> or <see cref="LoadMainScene"/>.
    ///
    /// Called by the Back button in the Nature scene.
    /// </summary>
    public void LoadPreviousScene()
    {
        // Guard: if previous equals current, fall back to the main scene
        // to avoid loading the scene on top of itself.
        string destination = _previousSceneName == SceneManager.GetActiveScene().name
            ? MainSceneName
            : _previousSceneName;

        TransitionTo(destination);
    }

    /// <summary>
    /// Marks the session as authenticated. Called by UIManager immediately after a
    /// successful sign-in or sign-up so subsequent scenes can skip the auth gate.
    /// </summary>
    /// <param name="userId">The authenticated user ID (must be non-empty to count as valid).</param>
    public void SetAuthenticated(string userId)
    {
        IsAuthenticated = !string.IsNullOrWhiteSpace(userId);
        Debug.Log($"[SceneTransitionManager] Auth state updated — IsAuthenticated={IsAuthenticated}, userId='{userId}'.");
    }

    /// <summary>
    /// Quits the application.
    /// In the Unity Editor this stops Play mode instead of actually quitting,
    /// which mirrors the runtime behaviour without breaking the editor session.
    ///
    /// Called by the Quit button present in both scenes.
    /// </summary>
    public void QuitApplication()
    {
        Debug.Log("[SceneTransitionManager] Quitting application.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Records the current scene name as "previous", then loads <paramref name="sceneName"/>.
    /// If the scene is not in Build Settings, Unity's SceneManager will log its own error.
    /// Reminder: open File → Build Settings and add both SampleScene and Nature before building.
    /// </summary>
    /// <param name="sceneName">Exact scene name as registered in File → Build Settings.</param>
    private void TransitionTo(string sceneName)
    {
        // Avoid loading a scene on top of itself.
        if (SceneManager.GetActiveScene().name == sceneName)
        {
            Debug.LogWarning($"[SceneTransitionManager] Already in scene '{sceneName}'. Transition ignored.");
            return;
        }

        // Record current scene before we navigate away.
        _previousSceneName = SceneManager.GetActiveScene().name;

        Debug.Log($"[SceneTransitionManager] Transitioning '{_previousSceneName}' → '{sceneName}'.");
        SceneManager.LoadScene(sceneName);
    }
}
