using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a click sound when the Button on this GameObject is pressed.
/// Attach to any Button GameObject and assign a clip in the Inspector.
/// A shared AudioSource is created automatically at runtime if none is assigned.
/// </summary>
[RequireComponent(typeof(Button))]
public class ButtonClickSound : MonoBehaviour
{
    [Tooltip("The sound to play on click.")]
    [SerializeField] private AudioClip clickClip;

    [Tooltip("Optional AudioSource to play through. Leave empty to auto-create one.")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("Volume of the click sound.")]
    [SerializeField] private float volume = 1f;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }

        GetComponent<Button>().onClick.AddListener(PlayClick);
    }

    private void OnDestroy()
    {
        GetComponent<Button>()?.onClick.RemoveListener(PlayClick);
    }

    private void PlayClick()
    {
        if (audioSource == null || clickClip == null)
        {
            return;
        }

        audioSource.PlayOneShot(clickClip, volume);
    }
}
