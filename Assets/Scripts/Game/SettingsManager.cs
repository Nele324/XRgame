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
        const string KeyBestTime    = "sc_besttime_v1";    // legacy single-best — migrated into top3
        const string KeyTop1        = "sc_top1_v1";
        const string KeyTop2        = "sc_top2_v1";
        const string KeyTop3        = "sc_top3_v1";

        public enum TurnMode { Snap, Smooth }

        public const int LeaderboardSize = 3;

        // ===== Cached values =====
        TurnMode turnMode = TurnMode.Snap;
        float masterVolume = 0.65f;
        float vignetteIntensity = 0.55f;
        bool speedrunMode;
        // Top-N times in ascending order. Slot value 0 means "empty".
        readonly float[] topTimes = new float[LeaderboardSize];

        public TurnMode CurrentTurnMode => turnMode;
        public float MasterVolume => masterVolume;
        public float VignetteIntensity => vignetteIntensity;
        public bool SpeedrunMode => speedrunMode;
        /// <summary>Best time so far, or 0 if no runs recorded.</summary>
        public float BestTime => topTimes[0];
        /// <summary>Read-only snapshot of the top times (length = LeaderboardSize). 0 = empty slot.</summary>
        public System.Collections.Generic.IReadOnlyList<float> TopTimes => topTimes;

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
            topTimes[0] = PlayerPrefs.GetFloat(KeyTop1, 0f);
            topTimes[1] = PlayerPrefs.GetFloat(KeyTop2, 0f);
            topTimes[2] = PlayerPrefs.GetFloat(KeyTop3, 0f);
            // Migration: if the legacy single-best key still has a value and the
            // new top-1 slot is empty, seed top-1 from it so an existing best
            // doesn't vanish on the first launch after upgrade.
            if (topTimes[0] == 0f)
            {
                float legacy = PlayerPrefs.GetFloat(KeyBestTime, 0f);
                if (legacy > 0f) topTimes[0] = legacy;
            }
        }

        void Save()
        {
            PlayerPrefs.SetInt(KeyTurnMode, (int)turnMode);
            PlayerPrefs.SetFloat(KeyVolume, masterVolume);
            PlayerPrefs.SetFloat(KeyVignette, vignetteIntensity);
            PlayerPrefs.SetInt(KeySpeedrun, speedrunMode ? 1 : 0);
            PlayerPrefs.SetFloat(KeyTop1, topTimes[0]);
            PlayerPrefs.SetFloat(KeyTop2, topTimes[1]);
            PlayerPrefs.SetFloat(KeyTop3, topTimes[2]);
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
        /// Records a new run time into the top-N leaderboard if it qualifies.
        /// Returns the 1-based rank it earned (1..LeaderboardSize) or 0 if it
        /// didn't make the cut.
        /// </summary>
        public int TrySetTopTime(float t)
        {
            if (t <= 0f) return 0;
            // Find insertion slot. An empty slot (value == 0) is always beaten.
            int insertAt = -1;
            for (int i = 0; i < topTimes.Length; i++)
            {
                if (topTimes[i] == 0f || t < topTimes[i]) { insertAt = i; break; }
            }
            if (insertAt < 0) return 0;
            // Shift slower times down by one.
            for (int i = topTimes.Length - 1; i > insertAt; i--)
                topTimes[i] = topTimes[i - 1];
            topTimes[insertAt] = t;
            Save();
            OnSettingsChanged?.Invoke();
            return insertAt + 1;
        }

        /// <summary>Backwards-compatible alias — returns true on any qualifying time.</summary>
        public bool TrySetBestTime(float t) => TrySetTopTime(t) > 0;
    }
}
