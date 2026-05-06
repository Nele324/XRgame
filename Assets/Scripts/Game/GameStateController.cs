using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace SpaceClimb
{
    public enum DeathCause { OutOfBounds, Hazard, Earth }

    /// <summary>
    /// Master gameplay state machine. Owns the run timer, the win/death/pause
    /// flow, scene transitions, and the death-cause string lookup. Singleton
    /// so death/win triggers can poke it from anywhere without inspector
    /// wiring.
    ///
    /// State transitions:
    ///   running ─→ paused ─→ running (via menu button)
    ///   running ─→ dying  (terminal: panel shown, awaits Retry / Main Menu)
    ///   running ─→ winning (terminal: panel shown after the win animation)
    /// </summary>
    public class GameStateController : MonoBehaviour
    {
        public static GameStateController Instance { get; private set; }

        [Header("Refs")]
        [SerializeField] ZeroGRig rig;
        [SerializeField] LevelManager level;
        [SerializeField] CanvasGroup fadeCanvas;
        [SerializeField] Image fadeImage;
        [SerializeField] GameObject winPanel;
        [SerializeField] GameObject deathPanel;
        [SerializeField] GameObject pausePanel;
        [SerializeField] TMP_Text winTimeLabel;
        [SerializeField] TMP_Text winBestLabel;
        [SerializeField] TMP_Text deathCauseLabel;
        [SerializeField] WinAnimation winAnimation;        // optional — falls back to plain fade if missing
        [SerializeField] float fadeDuration = 0.5f;

        [Header("Input")]
        [SerializeField] InputActionReference pauseAction;

        [Header("Scenes")]
        [SerializeField] string mainMenuScene = "MainMenu";
        [SerializeField] string climbScene = "Climb01";

        bool isDying;
        bool hasWon;
        bool isPaused;
        float runStartTime;
        float runEndTime;

        public bool IsRunActive => !isDying && !hasWon && !isPaused;
        /// <summary>Seconds elapsed since the run started, or final time if the run ended.</summary>
        public float RunElapsedSeconds => hasWon ? (runEndTime - runStartTime) : (Time.time - runStartTime);

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            HideAllPanels();
            if (fadeCanvas != null) fadeCanvas.alpha = 0f;
            runStartTime = Time.time;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            // Bind pause to whatever input action ref is wired (XRI menu button by default).
            if (pauseAction != null && pauseAction.action != null)
            {
                pauseAction.action.performed += OnPausePressed;
                pauseAction.action.Enable();
            }
        }

        void OnDisable()
        {
            if (pauseAction != null && pauseAction.action != null)
                pauseAction.action.performed -= OnPausePressed;
        }

        void HideAllPanels()
        {
            if (winPanel != null) winPanel.SetActive(false);
            if (deathPanel != null) deathPanel.SetActive(false);
            if (pausePanel != null) pausePanel.SetActive(false);
        }

        // ===== Pause =====

        void OnPausePressed(InputAction.CallbackContext _)
        {
            if (hasWon || isDying) return;
            if (isPaused) Resume();
            else Pause();
        }

        public void Pause()
        {
            if (isPaused || hasWon || isDying) return;
            isPaused = true;
            if (rig != null) rig.FreezePhysics(true);
            if (pausePanel != null) pausePanel.SetActive(true);
        }

        public void Resume()
        {
            if (!isPaused) return;
            isPaused = false;
            if (pausePanel != null) pausePanel.SetActive(false);
            if (rig != null) rig.FreezePhysics(false);
        }

        // ===== Death =====

        public void Die(DeathCause cause)
        {
            if (isDying || hasWon) return;
            isDying = true;
            StartCoroutine(DeathRoutine(cause));
        }

        IEnumerator DeathRoutine(DeathCause cause)
        {
            if (rig != null) rig.FreezePhysics(true);
            HapticBus.PulseAll(1f, 0.4f);
            AudioCues.PlayDeath();
            yield return Fade(1f);
            yield return new WaitForSeconds(0.15f);

            if (deathPanel != null)
            {
                deathPanel.SetActive(true);
                if (deathCauseLabel != null)
                    deathCauseLabel.text = CauseToText(cause);
            }
            // Fade back to partial darkness so the death panel reads against a darkened world.
            yield return Fade(0.55f);
        }

        // ===== Win =====

        public void Win()
        {
            if (hasWon || isDying) return;
            hasWon = true;
            runEndTime = Time.time;
            StartCoroutine(WinRoutine());
        }

        IEnumerator WinRoutine()
        {
            if (rig != null) rig.FreezePhysics(true);
            HapticBus.PulseAll(0.6f, 0.25f);
            AudioCues.PlayWin();

            // Play the docking-port glide if it's wired; otherwise just fade.
            if (winAnimation != null)
                yield return winAnimation.Play();

            yield return Fade(0.45f);
            if (winPanel != null) winPanel.SetActive(true);

            float runTime = RunElapsedSeconds;
            if (winTimeLabel != null) winTimeLabel.text = "Time: " + FormatTime(runTime);

            // Best-time tracking. Speedrun mode is the toggle but we record the
            // time regardless so casual runs can still set a baseline.
            bool isRecord = false;
            float prevBest = 0f;
            if (SettingsManager.Instance != null)
            {
                prevBest = SettingsManager.Instance.BestTime;
                isRecord = SettingsManager.Instance.TrySetBestTime(runTime);
            }
            if (winBestLabel != null)
            {
                if (isRecord)
                    winBestLabel.text = "<color=#FFC857>NEW RECORD!</color>";
                else if (prevBest > 0f)
                    winBestLabel.text = "Best: " + FormatTime(prevBest);
                else
                    winBestLabel.text = "";
            }
        }

        // ===== Helpers =====

        IEnumerator Fade(float target)
        {
            if (fadeCanvas == null) yield break;
            float start = fadeCanvas.alpha;
            float t = 0f;
            while (t < fadeDuration)
            {
                // unscaledDeltaTime so the fade is unaffected by Time.timeScale (the pause
                // mechanic doesn't pause timeScale right now, but this keeps us safe).
                t += Time.unscaledDeltaTime;
                fadeCanvas.alpha = Mathf.Lerp(start, target, t / fadeDuration);
                yield return null;
            }
            fadeCanvas.alpha = target;
        }

        static string FormatTime(float t)
        {
            int min = Mathf.FloorToInt(t / 60f);
            float sec = t - min * 60f;
            return $"{min:00}:{sec:00.00}";
        }

        static string CauseToText(DeathCause cause) => cause switch
        {
            DeathCause.OutOfBounds => "YOU DRIFTED INTO THE VOID",
            DeathCause.Hazard      => "CRUSHED AGAINST DEBRIS",
            DeathCause.Earth       => "BURNED IN THE ATMOSPHERE",
            _                      => "YOU DIED",
        };

        // ===== Scene transitions (wired to UI buttons) =====

        public void Retry() => SceneManager.LoadScene(climbScene);
        public void GoToMainMenu() => SceneManager.LoadScene(mainMenuScene);

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
