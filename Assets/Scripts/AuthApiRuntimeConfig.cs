using UnityEngine;

/// <summary>
/// Device build auth API settings loaded from Resources (always included in APK).
/// On Quest/Android this overrides stale UIManager Inspector values when enabled.
/// </summary>
[CreateAssetMenu(fileName = "AuthApiRuntimeConfig", menuName = "Conversational AI/Auth API Runtime Config")]
public sealed class AuthApiRuntimeConfig : ScriptableObject
{
    private const string ResourceName = "AuthApiRuntimeConfig";

    private static AuthApiRuntimeConfig _cached;

    [Header("Quest / device HTTPS auth API")]
    [Tooltip("When enabled, sign-in uses the Railway API instead of direct PostgreSQL.")]
    public bool useAuthApi = true;

    [Tooltip("HTTPS base URL with no trailing slash, e.g. https://your-app.up.railway.app")]
    public string baseUrl = "https://vr-proj-production.up.railway.app";

    [Tooltip("Same value as Railway environment variable API_KEY (sent as X-API-Key).")]
    public string apiKey = string.Empty;

    [Header("Build behavior")]
    [Tooltip("On Quest/mobile APK builds, always use this asset instead of UIManager scene values.")]
    public bool overrideInspectorOnDeviceBuilds = true;

    public static AuthApiRuntimeConfig Instance
    {
        get
        {
            if (_cached == null)
            {
                _cached = Resources.Load<AuthApiRuntimeConfig>(ResourceName);
            }

            return _cached;
        }
    }

    public static void ClearCacheForTests()
    {
        _cached = null;
    }

    /// <summary>True when this asset should replace UIManager Inspector values on device builds.</summary>
    public bool ShouldOverrideInspectorOnThisBuild()
    {
        return overrideInspectorOnDeviceBuilds && !Application.isEditor;
    }

    public bool HasUsableBaseUrl()
    {
        return AuthApiClient.IsConfigured(baseUrl);
    }
}
