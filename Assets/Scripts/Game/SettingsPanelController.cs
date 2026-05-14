using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceClimb
{
    /// <summary>
    /// Wires UI controls (toggle for turn mode, sliders for volume + vignette,
    /// toggle for speedrun) to <see cref="SettingsManager"/>. Created and wired
    /// at runtime by the build helper; no inspector setup required if all the
    /// references below are populated.
    /// </summary>
    public class SettingsPanelController : MonoBehaviour
    {
        [SerializeField] Toggle turnModeToggle;        // ON = smooth, OFF = snap
        [SerializeField] TMP_Text turnModeLabel;
        [SerializeField] Slider volumeSlider;
        [SerializeField] TMP_Text volumeValueLabel;
        [SerializeField] Slider vignetteSlider;
        [SerializeField] TMP_Text vignetteValueLabel;
        [SerializeField] Toggle speedrunToggle;

        bool ignoreEvents;          // suppress callback feedback while we sync UI from data

        void OnEnable()
        {
            SyncFromSettings();
        }

        void Start()
        {
            // Subscribe once. The UI is the writer; we keep settings change
            // events for OTHER consumers (HUD, vignette, audio).
            if (turnModeToggle != null) turnModeToggle.onValueChanged.AddListener(OnTurnModeChanged);
            if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            if (vignetteSlider != null) vignetteSlider.onValueChanged.AddListener(OnVignetteChanged);
            if (speedrunToggle != null) speedrunToggle.onValueChanged.AddListener(OnSpeedrunChanged);
            SyncFromSettings();
        }

        void OnDestroy()
        {
            if (turnModeToggle != null) turnModeToggle.onValueChanged.RemoveListener(OnTurnModeChanged);
            if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(OnVolumeChanged);
            if (vignetteSlider != null) vignetteSlider.onValueChanged.RemoveListener(OnVignetteChanged);
            if (speedrunToggle != null) speedrunToggle.onValueChanged.RemoveListener(OnSpeedrunChanged);
        }

        void SyncFromSettings()
        {
            var s = SettingsManager.Instance;
            if (s == null) return;
            ignoreEvents = true;
            if (turnModeToggle != null) turnModeToggle.isOn = s.CurrentTurnMode == SettingsManager.TurnMode.Smooth;
            UpdateTurnLabel();
            if (volumeSlider != null) volumeSlider.value = s.MasterVolume;
            UpdateVolumeLabel(s.MasterVolume);
            if (vignetteSlider != null) vignetteSlider.value = s.VignetteIntensity;
            UpdateVignetteLabel(s.VignetteIntensity);
            if (speedrunToggle != null) speedrunToggle.isOn = s.SpeedrunMode;
            ignoreEvents = false;
        }

        void OnTurnModeChanged(bool isSmooth)
        {
            if (ignoreEvents || SettingsManager.Instance == null) return;
            SettingsManager.Instance.SetTurnMode(isSmooth ? SettingsManager.TurnMode.Smooth : SettingsManager.TurnMode.Snap);
            UpdateTurnLabel();
        }

        void OnVolumeChanged(float v)
        {
            if (ignoreEvents || SettingsManager.Instance == null) return;
            SettingsManager.Instance.SetMasterVolume(v);
            UpdateVolumeLabel(v);
        }

        void OnVignetteChanged(float v)
        {
            if (ignoreEvents || SettingsManager.Instance == null) return;
            SettingsManager.Instance.SetVignetteIntensity(v);
            UpdateVignetteLabel(v);
        }

        void OnSpeedrunChanged(bool on)
        {
            if (ignoreEvents || SettingsManager.Instance == null) return;
            SettingsManager.Instance.SetSpeedrunMode(on);
        }

        void UpdateTurnLabel()
        {
            if (turnModeLabel == null) return;
            bool smooth = turnModeToggle != null && turnModeToggle.isOn;
            // Why two lines: the main word is the current mode, the subtitle explains
            // the trade-off. New VR users default to Snap because continuous rotation
            // is a common motion-sickness trigger.
            turnModeLabel.text = smooth
                ? "Smooth\n<size=58%><color=#A8B2BE>Continuous turning</color></size>"
                : "Snap\n<size=58%><color=#A8B2BE>Step rotation (recommended)</color></size>";
        }

        void UpdateVolumeLabel(float v)
        {
            if (volumeValueLabel != null) volumeValueLabel.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        void UpdateVignetteLabel(float v)
        {
            if (vignetteValueLabel != null) vignetteValueLabel.text = Mathf.RoundToInt(v * 100f) + "%";
        }
    }
}
