using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Translates SettingsManager values into actual Unity behavior. Lives in
    /// the gameplay scene (one per scene that wants settings applied) and
    /// targets the rig's controllers, audio, vignette. Re-applies on every
    /// settings-change event so toggling a slider in the pause menu propagates
    /// without a scene reload.
    /// </summary>
    public class SettingsApplier : MonoBehaviour
    {
        [SerializeField] AudioCues audioCues;
        [SerializeField] ComfortVignette vignette;
        [Tooltip("ControllerInputActionManager components on each controller. Their smoothTurnEnabled flag is what we toggle.")]
        [SerializeField] MonoBehaviour[] controllerActionManagers;

        // Reflection cache so we don't pay for FieldInfo lookups every settings-change.
        System.Reflection.PropertyInfo smoothTurnProp;

        void OnEnable()
        {
            SettingsManager.OnSettingsChanged += Apply;
            Apply();
        }

        void OnDisable()
        {
            SettingsManager.OnSettingsChanged -= Apply;
        }

        void Apply()
        {
            var s = SettingsManager.Instance;
            if (s == null) return;

            if (audioCues != null) audioCues.SetMasterVolume(s.MasterVolume);
            if (vignette != null) vignette.SetMaxIntensity(s.VignetteIntensity);
            ApplyTurnMode(s.CurrentTurnMode == SettingsManager.TurnMode.Smooth);
        }

        void ApplyTurnMode(bool smooth)
        {
            // ControllerInputActionManager is a Sample script (not core API), so
            // we touch it via reflection. Cached on first use.
            if (controllerActionManagers == null) return;
            for (int i = 0; i < controllerActionManagers.Length; i++)
            {
                var c = controllerActionManagers[i];
                if (c == null) continue;
                if (smoothTurnProp == null)
                    smoothTurnProp = c.GetType().GetProperty("smoothTurnEnabled");
                if (smoothTurnProp != null)
                    smoothTurnProp.SetValue(c, smooth);
            }
        }
    }
}
