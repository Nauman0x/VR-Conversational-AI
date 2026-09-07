using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Snapshots auth TMP values on pointer down so Enter / submit buttons do not clear them before <see cref="UIManager"/> reads.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class AuthSubmitPointerCapture : MonoBehaviour, IPointerDownHandler
{
    [SerializeField] private GameObject credentialPanelRoot;

    /// <summary>Assigns the panel whose <see cref="TMPro.TMP_InputField"/> children should be snapshotted before submit.</summary>
    public void ConfigurePanelRoot(GameObject panelRoot)
    {
        credentialPanelRoot = panelRoot;
    }

    /// <inheritdoc />
    public void OnPointerDown(PointerEventData eventData)
    {
        string panelName = credentialPanelRoot != null ? credentialPanelRoot.name : "(null)";
        AuthFlowTrace.Step("UI-CAP-001", "submit pointer down panel=" + panelName);
        TmpCredentialFieldUtility.CaptureCredentialFieldsBeforeSubmit(credentialPanelRoot);
    }
}
