using UnityEngine;
using UnityEngine.XR;

public class VRConversationControls : MonoBehaviour
{
    [SerializeField] private AvatarAIController avatarAIController;
    [SerializeField] private VRLocomotionController locomotionController;
    [SerializeField] private bool repositionAtConversationStart = true;
    [SerializeField] private bool allowKeyboardFallback = true;
    [SerializeField] private KeyCode keyboardToggleCallKey = KeyCode.C;

    private bool _lastPrimaryButtonPressed;

    private void Awake()
    {
        if (avatarAIController == null)
        {
            avatarAIController = FindObjectOfType<AvatarAIController>(true);
        }

        if (locomotionController == null)
        {
            locomotionController = FindObjectOfType<VRLocomotionController>(true);
        }
    }

    private void Update()
    {
        bool keyboardTriggered = allowKeyboardFallback && Input.GetKeyDown(keyboardToggleCallKey);
        bool controllerTriggered = ReadPrimaryButtonDown();

        if (!keyboardTriggered && !controllerTriggered)
        {
            return;
        }

        if (avatarAIController == null)
        {
            Debug.LogWarning("[VRConversationControls] AvatarAIController not found.");
            return;
        }

        if (repositionAtConversationStart && locomotionController != null)
        {
            locomotionController.PlaceAtSeatFacingAvatar();
        }

        avatarAIController.OnCallToggleClicked();
    }

    private bool ReadPrimaryButtonDown()
    {
        InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        bool isPressed = false;

        if (leftHand.isValid && leftHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool leftPressed) && leftPressed)
        {
            isPressed = true;
        }

        if (rightHand.isValid && rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool rightPressed) && rightPressed)
        {
            isPressed = true;
        }

        bool pressedDown = isPressed && !_lastPrimaryButtonPressed;
        _lastPrimaryButtonPressed = isPressed;
        return pressedDown;
    }
}
