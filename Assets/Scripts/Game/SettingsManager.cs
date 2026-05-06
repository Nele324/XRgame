using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Single source of truth for player-tunable options. Persisted via
    /// <see cref="PlayerPrefs"/> (one of Unity's built-in primitives — no
    /// external save system needed). Other systems read SettingsManager
    /// at startup and re-read on the OnSettingsChanged event.
    ///
    /// Lives across scene loads via DontDestroyOnLoad so settings persist
    /// without a save trip when the player navigates Main Menu → Climb01.
    ///
    /// DefaultExecutionOrder is set very low so this Awake runs before any
    /// consumer's Awake — guarantees SettingsManager.Instance is non-null when
    /// other systems read it during their own Awake.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class SettingsManager : MonoBehaviour
    {
        public static SettingsManager Instance { get; private set; }

        // ===== Persisted keys (versioned to allow migration if we change layout) =====
        const string KeyTurnMode    = "sc_turnmode_v1";
        const string KeyVolume      = "sc_volume_v1";
        const string KeyVignette    = "sc_vignette_v1";
        const string KeySpeedrun    = "sc_speedrun_v1";
        const string KeyBestTime    = "sc_besttime_v1";

        public enum TurnMode { Snap, Smooth }

        // ===== Cached values =====
        TurnMode turnMode = TurnMode.Snap;
        float masterVolume = 0.65f;
        float vignetteIntensity = 0.55f;
        bool speedrunMode;
        float bestTime;             // 0 = no record yet

        public TurnMode CurrentTurnMode => turnMode;
        public float MasterVolume => masterVolume;
        public float VignetteIntensity => vignetteIntensity;
        public bool SpeedrunMode => speedrunMode;
        public float BestTime => bestTime;

        /// <summary>Fired any time a setting changes. Subscribers re-read whatever they care about.</summary>
        public static event System.Action OnSettingsChanged;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Load();
        }

        void Load()
        {
            turnMode = (TurnMode)PlayerPrefs.GetInt(KeyTurnMode, (int)TurnMode.Snap);
            masterVolume = PlayerPrefs.GetFloat(KeyVolume, 0.65f);
            vignetteIntensity = PlayerPrefs.GetFloat(KeyVignette, 0.55f);
            speedrunMode = PlayerPrefs.GetInt(KeySpeedrun, 0) != 0;
            bestTime = PlayerPrefs.GetFloat(KeyBestTime, 0f);
        }

        void Save()
        {
            PlayerPrefs.SetInt(KeyTurnMode, (int)turnMode);
            PlayerPrefs.SetFloat(KeyVolume, masterVolume);
            PlayerPrefs.SetFloat(KeyVignette, vignetteIntensity);
            PlayerPrefs.SetInt(KeySpeedrun, speedrunMode ? 1 : 0);
            PlayerPrefs.SetFloat(KeyBestTime, bestTime);
            PlayerPrefs.Save();
        }

        // ===== Mutators (use these from Settings UI) =====

        public void SetTurnMode(TurnMode mode)
        {
            if (turnMode == mode) return;
            turnMode = mode;
            Save();
            OnSettingsChanged?.Invoke();
        }

        public void SetMasterVolume(float v)
        {
            v = Mathf.Clamp01(v);
            if (Mathf.Approximately(masterVolume, v)) return;
            masterVolume = v;
            Save();
            OnSettingsChanged?.Invoke();
        }

        public void SetVignetteIntensity(float v)
        {
            v = Mathf.Clamp01(v);
            if (Mathf.Approximately(vignetteIntensity, v)) return;
            vignetteIntensity = v;
            Save();
            OnSettingsChanged?.Invoke();
        }

        public void SetSpeedrunMode(bool on)
        {
            if (speedrunMode == on) return;
            speedrunMode = on;
            Save();
            OnSettingsChanged?.Invoke();
        }

        /// <summary>
        /// Records a new best time only if better than the current best (or if there is none).
        /// Returns true if it was a record.
        /// </summary>
        public bool TrySetBestTime(float t)
        {
            if (t <= 0f) return false;
            if (bestTime > 0f && t >= bestTime) return false;
            bestTime = t;
            Save();
            OnSettingsChanged?.Invoke();
            return true;
        }
    }
}
