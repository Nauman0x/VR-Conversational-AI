using UnityEngine;

/// <summary>
/// Plays a looping walking sound while the XR Origin is actively moving horizontally
/// and stops it the moment the player releases the stick.
/// </summary>
/// <remarks>
/// Driven by horizontal displacement of the rig transform (not CharacterController.velocity,
/// which is stale after we stop calling Move()). A short hold timer keeps the loop from
/// chattering between motion frames, and a hysteresis band ensures we never start the sound
/// from micro-jitter caused by gravity/terrain follow.
/// </remarks>
[RequireComponent(typeof(CharacterController))]
public class PlayerFootstepAudio : MonoBehaviour
{
    [Header("Audio")]
    [Tooltip("AudioSource with the walking sound clip. Enable Loop on it in the Inspector.")]
    [SerializeField] private AudioSource walkingAudioSource;

    [Header("Detection")]
    [Tooltip("Start the sound once horizontal speed exceeds this (m/s).")]
    [SerializeField] private float startMovementSpeed = 0.35f;

    [Tooltip("Stop the sound once horizontal speed drops below this (m/s). Should be lower than startMovementSpeed.")]
    [SerializeField] private float stopMovementSpeed = 0.10f;

    [Tooltip("Smooth the per-frame speed estimate over this many seconds before comparing to thresholds.")]
    [SerializeField] private float speedSmoothingSeconds = 0.08f;

    [Tooltip("Keep the loop playing for this many seconds after speed drops, to avoid stutter between locomotion frames.")]
    [SerializeField] private float playLingerSeconds = 0.08f;

    private Vector3 _previousRootWorldPosition;
    private bool _hasPreviousRootWorldPosition;
    private float _smoothedHorizontalSpeed;
    private float _stopAfterTime;

    private void OnEnable()
    {
        _previousRootWorldPosition = transform.position;
        _hasPreviousRootWorldPosition = true;
        _smoothedHorizontalSpeed = 0f;
        _stopAfterTime = 0f;

        if (walkingAudioSource != null && walkingAudioSource.isPlaying)
        {
            walkingAudioSource.Stop();
        }
    }

    private void LateUpdate()
    {
        if (walkingAudioSource == null)
        {
            return;
        }

        float instantSpeed = EstimatePlanarSpeedFromRootMotion();

        // Exponential smoothing — kills the single-frame zero gaps that locomotion stick
        // updates can introduce while still reacting quickly when the player releases.
        float smoothingAlpha = speedSmoothingSeconds > 0.0001f
            ? 1f - Mathf.Exp(-Time.deltaTime / speedSmoothingSeconds)
            : 1f;
        _smoothedHorizontalSpeed = Mathf.Lerp(_smoothedHorizontalSpeed, instantSpeed, smoothingAlpha);

        bool isPlaying = walkingAudioSource.isPlaying;

        if (isPlaying)
        {
            if (_smoothedHorizontalSpeed >= stopMovementSpeed)
            {
                _stopAfterTime = Time.time + Mathf.Max(0f, playLingerSeconds);
                return;
            }

            if (Time.time >= _stopAfterTime)
            {
                walkingAudioSource.Stop();
            }
            return;
        }

        if (_smoothedHorizontalSpeed >= startMovementSpeed)
        {
            walkingAudioSource.Play();
            _stopAfterTime = Time.time + Mathf.Max(0f, playLingerSeconds);
        }
    }

    /// <summary>Horizontal displacement of the origin root, expressed as m/s.</summary>
    private float EstimatePlanarSpeedFromRootMotion()
    {
        Vector3 current = transform.position;

        if (!_hasPreviousRootWorldPosition)
        {
            _previousRootWorldPosition = current;
            _hasPreviousRootWorldPosition = true;
            return 0f;
        }

        Vector3 delta = current - _previousRootWorldPosition;
        delta.y = 0f;

        float dt = Time.deltaTime;
        float speed = dt > Mathf.Epsilon ? delta.magnitude / dt : 0f;
        _previousRootWorldPosition = current;

        // Teleports can spike one frame; cap so we don't treat them as prolonged walking noise.
        return Mathf.Min(speed, 8f);
    }
}
