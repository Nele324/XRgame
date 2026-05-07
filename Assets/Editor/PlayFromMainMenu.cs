using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceClimb.EditorTools
{
    /// <summary>
    /// Pins the editor's Play Mode entry point to the MainMenu scene so pressing Play
    /// always boots through the menu, regardless of which scene is active in the
    /// Hierarchy. Without this, hitting Play from inside Climb01 skips the menu and
    /// drops you straight into gameplay — which is what bit us on the headset.
    /// Toggle off via the menu item if you ever want to test a single scene in
    /// isolation again.
    /// </summary>
    [InitializeOnLoad]
    static class PlayFromMainMenu
    {
        const string MainMenuPath = "Assets/Scenes/MainMenu.unity";
        const string PrefKey = "SpaceClimb.PlayFromMainMenu";
        const string MenuPath = "SpaceClimb/Always Start Play From MainMenu";

        static PlayFromMainMenu()
        {
            EditorApplication.delayCall += Apply;
        }

        static void Apply()
        {
            bool enabled = EditorPrefs.GetBool(PrefKey, true);
            Menu.SetChecked(MenuPath, enabled);

            if (!enabled)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuPath);
            if (scene == null)
            {
                Debug.LogWarning($"[PlayFromMainMenu] Could not find {MainMenuPath} — playModeStartScene not set.");
                return;
            }
            EditorSceneManager.playModeStartScene = scene;
        }

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            bool next = !EditorPrefs.GetBool(PrefKey, true);
            EditorPrefs.SetBool(PrefKey, next);
            Apply();
        }
    }
}
