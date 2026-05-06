using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Centralized one-shot SFX dispatcher. Holds the AudioClips for every
    /// gameplay event, plus a 2D AudioSource for non-positional cues. The
    /// static Play* helpers are null-safe so gameplay code can fire-and-forget
    /// without checking whether audio has booted yet.
    ///
    /// Settings integration: subscribes to <see cref="SettingsManager"/> for
    /// volume changes — the slider in Settings updates this immediately.
    /// </summary>
    public class AudioCues : MonoBehaviour
    {
        public static AudioCues Instance { get; private set; }

        [SerializeField] AudioSource source;
        [SerializeField] AudioClip grabClip;
        [SerializeField] AudioClip releaseClip;
        [SerializeField] AudioClip impactClip;
        [SerializeField] AudioClip hazardClip;
        [SerializeField] AudioClip winClip;
        [SerializeField] AudioClip deathClip;
        [SerializeField] AudioClip ambientLoop;
        [SerializeField] AudioSource ambientSource;

        [Range(0f, 1f)][SerializeField] float masterVolume = 0.65f;
        [Tooltip("Ambient volume scales with master, this is an additional reduction so ambient sits under SFX.")]
        [Range(0f, 1f)][SerializeField] float ambientMix = 0.3f;

        public float MasterVolume => masterVolume;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // Auto-build a 2D AudioSource for one-shots if none was wired. 2D
            // (spatialBlend=0) is intentional — global cues like grab/release
            // should sound the same regardless of where they "happened".
            if (source == null)
            {
                var go = new GameObject("OneShotAudio");
                go.transform.SetParent(transform, false);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }
            // Pull persisted settings now if SettingsManager is up (low execution
            // order makes that the common case). Otherwise we just use serialized defaults.
            ApplySettings();

            if (ambientSource != null && ambientLoop != null)
            {
                ambientSource.clip = ambientLoop;
                ambientSource.loop = true;
                ambientSource.volume = masterVolume * ambientMix;
                ambientSource.Play();
            }
        }

        void OnEnable()
        {
            // Subscribe so changes from the settings UI propagate immediately.
            SettingsManager.OnSettingsChanged += ApplySettings;
        }

        void OnDisable()
        {
            SettingsManager.OnSettingsChanged -= ApplySettings;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void ApplySettings()
        {
            if (SettingsManager.Instance != null)
                masterVolume = SettingsManager.Instance.MasterVolume;
            if (ambientSource != null) ambientSource.volume = masterVolume * ambientMix;
        }

        /// <summary>Programmatic volume setter used by SettingsApplier for live updates.</summary>
        public void SetMasterVolume(float volume)
        {
            masterVolume = Mathf.Clamp01(volume);
            if (ambientSource != null) ambientSource.volume = masterVolume * ambientMix;
        }

        // Internal one-shot worker — handles null clips and applies a small
        // pitch jitter so repeated SFX don't sound identical.
        void PlayClip(AudioClip clip, float volume = 1f, float pitchJitter = 0.05f)
        {
            if (clip == null || source == null) return;
            float p = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.pitch = p;
            source.PlayOneShot(clip, volume * masterVolume);
        }

        // ===== Static convenience accessors =====
        // Null-Instance safe so gameplay code doesn't need to gate on "is the
        // audio system loaded yet?". The ?. operator short-circuits cleanly.

        public static void PlayGrab()    { Instance?.PlayClip(Instance.grabClip,    0.7f); }
        public static void PlayRelease() { Instance?.PlayClip(Instance.releaseClip, 0.5f); }
        public static void PlayImpact(float amount) { Instance?.PlayClip(Instance.impactClip, Mathf.Clamp01(amount) * 0.9f, 0.15f); }
        public static void PlayHazard()  { Instance?.PlayClip(Instance.hazardClip, 1f); }
        public static void PlayWin()     { Instance?.PlayClip(Instance.winClip,    1f, 0f); }
        public static void PlayDeath()   { Instance?.PlayClip(Instance.deathClip,  1f, 0f); }
    }
}
