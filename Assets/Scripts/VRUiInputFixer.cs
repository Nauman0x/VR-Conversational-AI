using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

[DefaultExecutionOrder(-500)]
public class VRUiInputFixer : MonoBehaviour
{
    [Header("EventSystem")]
    [SerializeField] private bool autoFindEventSystem = true;
    [SerializeField] private EventSystem targetEventSystem;
    [SerializeField] private bool disableStandaloneInputModule = true;

    [Header("Canvas")]
    [SerializeField] private bool ensureTrackedDeviceGraphicRaycaster = true;

    [Tooltip(
        "When enabled, disables only the stock Unity GraphicRaycaster (not subclasses) so TrackedDeviceGraphicRaycaster owns XR rays. " +
        "Leaving this off keeps both raycasters enabled — safer when unsure. Wrong disable order can mute all UI clicks.")]
    [SerializeField]
    private bool disableClassicGraphicRaycasterOnlyOnWorldSpaceCanvas = false;

    [SerializeField] private bool includeInactiveCanvases = true;

    [Header("VR text input")]
    [Tooltip(
        "Quest/Android: XR ray TMP fields use " + nameof(TMP_InputFieldVrKeyboardOpener) + " (floating VR keypad fallback + TouchScreenKeyboard).")]
    [SerializeField] private bool addVrTmpKeyboardHelpers = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = true;

    private void Awake()
    {
        if (autoFindEventSystem && targetEventSystem == null)
        {
            targetEventSystem = EventSystem.current;
            if (targetEventSystem == null)
            {
                targetEventSystem = FindObjectOfType<EventSystem>(true);
            }
        }

        EnsureEventSystemModules();
        TuneEventSystemForTrackedPointers();

        if (ensureTrackedDeviceGraphicRaycaster)
        {
            EnsureCanvasRaycasters();
        }

        /* Must run whether or not we touch canvases — XRI defaults block all UI rays while a 3D interactable stays selected. */
        EnsureXRayInteractorsDeliverUiPointers();

        if (addVrTmpKeyboardHelpers)
        {
            EnsureVrTmpKeyboardOpenersOnInputFields();
        }
    }

    /// <summary>Attaches <see cref="TMP_InputFieldVrKeyboardOpener"/> so TMP fields work on Meta Quest keyboards.</summary>
    private void EnsureVrTmpKeyboardOpenersOnInputFields()
    {
        TMP_InputField[] fields = FindObjectsOfType<TMP_InputField>(includeInactiveCanvases);
        int addedCount = 0;

        for (int i = 0; i < fields.Length; i++)
        {
            TMP_InputField field = fields[i];
            if (field != null &&
                field.gameObject != null &&
                field.GetComponent<TMP_InputFieldVrKeyboardOpener>() == null)
            {
                field.gameObject.AddComponent<TMP_InputFieldVrKeyboardOpener>();
                addedCount++;
            }

            if (field != null)
            {
                TmpCredentialFieldUtility.PrepareAuthField(field);
            }
        }

        if (addedCount > 0)
        {
            Log("Added TMP_InputFieldVrKeyboardOpener on " + addedCount + " input field(s).");
        }
    }

    private void EnsureEventSystemModules()
    {
        if (targetEventSystem == null)
        {
            Log("No EventSystem found.");
            return;
        }

        /*
         XRUITemplates keep XRUIInputModule enabled even in Editor/Desktop with no headset. That paired with disabling
         InputSystemUIInputModule left only XRUIInputModule, which never receives mouse / keyboard submits — TMP fields felt "broken" on PC.
        */
        Type xrUiInputModuleType =
            Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule, Unity.XR.Interaction.Toolkit");
        MonoBehaviour xrUiInputModule =
            xrUiInputModuleType != null ? targetEventSystem.gameObject.GetComponent(xrUiInputModuleType) as MonoBehaviour : null;

        InputSystemUIInputModule inputSystemModule = targetEventSystem.GetComponent<InputSystemUIInputModule>();
        bool xrDevicePresent = XRSettings.isDeviceActive;

        if (xrUiInputModule != null)
        {
            if (xrDevicePresent)
            {
                xrUiInputModule.enabled = true;
                if (inputSystemModule != null && inputSystemModule.enabled)
                {
                    inputSystemModule.enabled = false;
                    Log("Disabled InputSystemUIInputModule (XRUIInputModule owns UI while XR device is active).");
                }
                else if (inputSystemModule == null)
                {
                    Log("XR UI module active — no competing InputSystemUIInputModule to disable.");
                }
            }
            else
            {
                xrUiInputModule.enabled = false;
                Log(
                    "XRUIInputModule disabled (no XR device active) so Unity routes mouse/keyboard through InputSystem on PC/editor.");

                if (inputSystemModule == null)
                {
                    inputSystemModule = targetEventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                    Log("Added InputSystemUIInputModule for desktop/editor UI.");
                }

                inputSystemModule.enabled = true;
                Log("InputSystemUIInputModule enabled for non-XR play mode.");
            }
        }
        else
        {
            if (inputSystemModule == null)
            {
                inputSystemModule = targetEventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                Log("Added InputSystemUIInputModule.");
            }
            else
            {
                Log("InputSystemUIInputModule already present.");
            }
        }

        if (disableStandaloneInputModule)
        {
            StandaloneInputModule standalone = targetEventSystem.gameObject.GetComponent<StandaloneInputModule>();
            if (standalone != null)
            {
                standalone.enabled = false;
                Log("Disabled StandaloneInputModule.");
            }
        }
    }

    /// <summary>
    /// Reduces XR pointer chatter being classified as drag instead of tap; jitter was breaking buttons and TMP focus.
    /// </summary>
    private void TuneEventSystemForTrackedPointers()
    {
        if (targetEventSystem == null)
        {
            return;
        }

        const int vrPixelDragTolerance = 28;
        targetEventSystem.pixelDragThreshold =
            Mathf.Max(targetEventSystem.pixelDragThreshold, vrPixelDragTolerance);

        XRUIInputModule xrTrackedUiModule = targetEventSystem.gameObject.GetComponent<XRUIInputModule>();
        if (xrTrackedUiModule != null)
        {
            const float easedTrackedDragMultiplier = 2.25f;
            xrTrackedUiModule.trackedDeviceDragThresholdMultiplier = Mathf.Max(
                xrTrackedUiModule.trackedDeviceDragThresholdMultiplier,
                easedTrackedDragMultiplier);
        }

        Log("EventSystem pixelDragThreshold≥" + vrPixelDragTolerance +
            "; XR tracked drag multiplier≥2.25 (more forgiving XR “click” vs drag).");
    }

    private void EnsureCanvasRaycasters()
    {
        Type trackedRaycasterType = Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        if (trackedRaycasterType == null)
        {
            Log("TrackedDeviceGraphicRaycaster type not found. Ensure XR Interaction Toolkit is installed.");
            return;
        }

        Canvas[] canvases = FindObjectsOfType<Canvas>(includeInactiveCanvases);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null)
            {
                continue;
            }

            bool isWorldSpace = canvas.renderMode == RenderMode.WorldSpace;

            if (isWorldSpace && ensureTrackedDeviceGraphicRaycaster && canvas.GetComponent(trackedRaycasterType) == null)
            {
                // TrackedDeviceGraphicRaycaster must run Awake before OnDisable — AddComponent on an
                // inactive GameObject skips Awake until enabled, which breaks XRI's internal dictionary on teardown.
                GameObject canvasObject = canvas.gameObject;
                bool restoreInactive = !canvasObject.activeSelf;
                if (restoreInactive)
                {
                    canvasObject.SetActive(true);
                }

                canvasObject.AddComponent(trackedRaycasterType);
                Log("Added TrackedDeviceGraphicRaycaster on world canvas: " + canvas.name);

                if (restoreInactive)
                {
                    canvasObject.SetActive(false);
                }
            }

            GraphicRaycaster[] raycasters = canvas.GetComponents<GraphicRaycaster>();
            if (raycasters.Length == 0)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
                Log("Added GraphicRaycaster (no GraphicRaycaster on canvas): " + canvas.name);
                raycasters = canvas.GetComponents<GraphicRaycaster>();
            }

            bool trackedPresent = canvas.GetComponent(trackedRaycasterType) != null;

            for (int r = 0; r < raycasters.Length; r++)
            {
                GraphicRaycaster singleRaycaster = raycasters[r];
                if (singleRaycaster == null)
                {
                    continue;
                }

                /*
                 TrackedDeviceGraphicRaycaster inherits GraphicRaycaster. Disabling whatever GetComponent<GraphicRaycaster>()
                 returns first may disable the subclass and silence every tracked UI ray — recover by always forcing subclasses enabled.
                */
                if (singleRaycaster.GetType() != typeof(GraphicRaycaster))
                {
                    singleRaycaster.enabled = true;
                    continue;
                }

                bool turnOffClassic = disableClassicGraphicRaycasterOnlyOnWorldSpaceCanvas && isWorldSpace && trackedPresent;
                singleRaycaster.enabled = !turnOffClassic;
            }
        }
    }

    /// <summary>
    /// XRRayInteractor.UpdateUIModel resets the UI payload while <see cref="XRRayInteractor.hasSelection"/> is true unless
    /// <see cref="XRRayInteractor.blockUIOnInteractableSelection"/> is false. Users then see no poke/click response on canvases until deselect.
    /// </summary>
    private void EnsureXRayInteractorsDeliverUiPointers()
    {
        XRRayInteractor[] rays = FindObjectsOfType<XRRayInteractor>(includeInactiveCanvases);
        if (rays == null || rays.Length == 0)
        {
            return;
        }

        for (int i = 0; i < rays.Length; i++)
        {
            XRRayInteractor rayInteractor = rays[i];
            if (rayInteractor == null)
            {
                continue;
            }

            rayInteractor.enableUIInteraction = true;
            rayInteractor.blockUIOnInteractableSelection = false;
        }

        Log("XR UI rays: ensured " + rays.Length + " XRRayInteractor(s) keep sending UI clicks (enableUIInteraction, blockUIOnInteractableSelection=false).");
    }

    private void Log(string message)
    {
        if (!verboseLogs)
        {
            return;
        }

        Debug.Log("[VRUiInputFixer] " + message);
    }
}
