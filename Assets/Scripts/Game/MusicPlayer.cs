using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// In-game soundtrack player. Drives a single AudioSource with a looping
    /// music clip and fades the volume up at scene start so the track doesn't
    /// snap in on top of the launch SFX. Subscribes to SettingsManager so the
    /// master volume slider in the Settings menu affects music in real time.
    ///
    /// Persistence: this is a singleton with DontDestroyOnLoad so the track
    /// keeps playing through level resets (death → retry reloads Climb01).
    /// A duplicate MusicPlayer in the freshly loaded scene destroys itself.
    /// When the player returns to a non-gameplay scene (e.g. MainMenu), the
    /// scene-change hook stops playback and clears the instance.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class MusicPlayer : MonoBehaviour
    {
        public static MusicPlayer Instance { get; private set; }
        [Tooltip("Scenes where the music should stay playing. If the active " +
            "scene changes to one not in this list, the music stops and this " +
            "object is destroyed — so going back to the MainMenu silences the " +
            "track instead of letting it leak into menu audio.")]
        [SerializeField] string[] gameplayScenes = { "Climb01" };

        [SerializeField] AudioSource source;
        [Tooltip("Music volume relative to master. Music sits under SFX so a value below 1 is normal.")]
        [Range(0f, 1f)][SerializeField] float musicMix = 0.85f;
        [Tooltip("Seconds to ramp the music from silent to its target volume on start.")]
        [SerializeField] float fadeInSeconds = 1.5f;
        [Tooltip("Volume floor used when the master setting is 0. Prevents the music " +
            "from being inaudible just because the player hasn't touched the master " +
            "slider — they can still hear the track and the slider lowers it.")]
        [Range(0f, 1f)][SerializeField] float minVolume = 0.2f;

        float targetVolume;
        float startTime;
        bool started;

        void Reset()
        {
            source = GetComponent<AudioSource>();
        }

        void Awake()
        {
            // Singleton: if another MusicPlayer already exists (i.e. this is a
            // duplicate from a scene reload), inherit nothing and quietly self-
            // destruct so the original's playback continues uninterrupted.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (source == null) source = GetComponent<AudioSource>();
            source.loop = true;
            source.spatialBlend = 0f;
            source.playOnAwake = false;
            ApplyVolumeFromSettings();
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        void Start()
        {
            startTime = Time.time;
            source.volume = 0f;
            if (source.clip != null)
            {
                source.Play();
                started = true;
            }
            else
            {
                Debug.LogWarning("[MusicPlayer] No AudioClip assigned — music will not play.");
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            }
        }

        void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
        {
            // If the player has left a gameplay scene (e.g. went back to the
            // MainMenu), stop the track and clean up — music should belong to
            // the climb context, not bleed into menu audio.
            if (System.Array.IndexOf(gameplayScenes, to.name) < 0)
            {
                if (source != null) source.Stop();
                Destroy(gameObject);
            }
        }

        void Update()
        {
            if (!started) return;
            float t = fadeInSeconds <= 0f ? 1f : Mathf.Clamp01((Time.time - startTime) / fadeInSeconds);
            source.volume = targetVolume * t;
        }

        void OnEnable()
        {
            SettingsManager.OnSettingsChanged += ApplyVolumeFromSettings;
        }

        void OnDisable()
        {
            SettingsManager.OnSettingsChanged -= ApplyVolumeFromSettings;
        }

        void ApplyVolumeFromSettings()
        {
            float master = SettingsManager.Instance != null ? SettingsManager.Instance.MasterVolume : 0.65f;
            // Master volume scales music linearly. master=0 must produce
            // silence — a previous "minimum audible" floor here meant dragging
            // the slider to 0 still left the track at ~17%, which felt broken.
            targetVolume = master * musicMix;
        }
    }
}
