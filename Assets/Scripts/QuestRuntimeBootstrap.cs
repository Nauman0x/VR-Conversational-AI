using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Applies Quest/Android performance and pacing on device builds automatically (no Inspector setup).
/// PC Editor play mode is untouched.
/// </summary>
public static class QuestRuntimeBootstrap
{
    public static bool IsHeadsetRuntime { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyBeforeSceneLoad()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        IsHeadsetRuntime = true;
        ApplyQualityProfile();
        ApplyFramePacing();
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyAfterSceneLoad()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        ApplyTerrainLod();
        TryRequestDisplayRefreshRate(90f);
        Debug.Log("[QuestBootstrap] Headset profile active (Medium quality, 90 FPS target, terrain LOD).");
#endif
    }

    private static void ApplyQualityProfile()
    {
        const int mediumQualityIndex = 2;
        if (QualitySettings.names.Length > mediumQualityIndex)
        {
            QualitySettings.SetQualityLevel(mediumQualityIndex, applyExpensiveChanges: true);
        }

        QualitySettings.vSyncCount = 0;
        QualitySettings.shadows = ShadowQuality.Disable;
        QualitySettings.antiAliasing = 0;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
        QualitySettings.lodBias = 0.65f;
        QualitySettings.particleRaycastBudget = 64;
        QualitySettings.realtimeReflectionProbes = false;
        QualitySettings.terrainPixelError = 8f;
        QualitySettings.terrainDetailDensityScale = 0.45f;
        QualitySettings.terrainTreeDistance = 1200f;
        QualitySettings.terrainBasemapDistance = 400f;
    }

    private static void ApplyFramePacing()
    {
        Application.targetFrameRate = 90;
        Time.fixedDeltaTime = 1f / 90f;
        QualitySettings.maxQueuedFrames = 2;
    }

    private static void ApplyTerrainLod()
    {
        Terrain[] terrains = Object.FindObjectsOfType<Terrain>(includeInactive: true);
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null)
            {
                continue;
            }

            terrain.heightmapPixelError = 8f;
            terrain.basemapDistance = 400f;
            terrain.detailObjectDistance = 35f;
            terrain.treeDistance = 1200f;
            terrain.treeBillboardDistance = 40f;
        }
    }

    /// <summary>
    /// Requests a display refresh rate via reflection. <c>TryRequestDisplayRefreshRate</c> only exists on
    /// newer Unity XR builds, so we look it up dynamically and skip silently when unavailable. OpenXR
    /// Performance Settings + <see cref="Application.targetFrameRate"/> already drive Quest pacing.
    /// </summary>
    private static void TryRequestDisplayRefreshRate(float hz)
    {
        List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetInstances(displays);

        MethodInfo requestMethod = typeof(XRDisplaySubsystem).GetMethod(
            "TryRequestDisplayRefreshRate",
            BindingFlags.Public | BindingFlags.Instance);

        if (requestMethod == null)
        {
            return;
        }

        object[] args = { hz };
        for (int i = 0; i < displays.Count; i++)
        {
            XRDisplaySubsystem display = displays[i];
            if (display == null || !display.running)
            {
                continue;
            }

            object result = requestMethod.Invoke(display, args);
            if (result is bool ok && ok)
            {
                Debug.Log("[QuestBootstrap] Display refresh rate set to " + hz + " Hz.");
                return;
            }
        }
    }
}
