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
    }
}
