using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceClimb
{
    /// <summary>
    /// Tiny controller for the MainMenu scene. Spins the Earth diorama for
    /// visual interest and exposes Start / Quit hooks for the world-space
    /// canvas buttons. Settings UI lives in its own controller; this one
    /// stays focused on the start/quit flow.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] string climbScene = "Climb01";
        [SerializeField] Transform earthDiorama;
        [Tooltip("Degrees per second. Slow rotation looks majestic; fast looks like a wobbly toy.")]
        [SerializeField] float earthSpinSpeed = 6f;

        [Header("Panel swap")]
        [Tooltip("Items belonging to the main menu (title, buttons, leaderboard, controls hint). " +
            "Hidden when the settings panel is shown so they don't render through each other.")]
        [SerializeField] GameObject[] mainPanelItems;
        [Tooltip("Settings panel root. Toggled in tandem with mainPanelItems.")]
        [SerializeField] GameObject settingsPanel;

        void Update()
        {
            if (earthDiorama != null)
                earthDiorama.Rotate(Vector3.up, earthSpinSpeed * Time.deltaTime, Space.World);
        }

        /// <summary>Wired to the START CLIMB button. Loads the gameplay scene.</summary>
        public void StartGame()
        {
            SceneManager.LoadScene(climbScene);
        }

        /// <summary>Wired to the QUIT button. Closes the editor in Editor; the app in builds.</summary>
        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Wired to the SETTINGS button. Hides main UI, shows settings panel.</summary>
        public void OpenSettings() => SetSettingsVisible(true);

        /// <summary>Wired to the SettingsPanel's BACK button. Restores main UI.</summary>
        public void CloseSettings() => SetSettingsVisible(false);

        void SetSettingsVisible(bool showSettings)
        {
            if (mainPanelItems != null)
            {
                for (int i = 0; i < mainPanelItems.Length; i++)
                    if (mainPanelItems[i] != null)
                        mainPanelItems[i].SetActive(!showSettings);
            }
            if (settingsPanel != null) settingsPanel.SetActive(showSettings);
        }
    }
}
