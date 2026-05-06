using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceClimb
{
    public class ComfortVignette : MonoBehaviour
    {
        [SerializeField] ZeroGRig rig;
        [SerializeField] Volume volume;
        [Tooltip("Peak vignette darkness when the player is at speedAtMax. Settings UI overrides this at runtime.")]
        [Range(0f, 1f)][SerializeField] float maxIntensity = 0.55f;
        [Tooltip("Speed (m/s) at which the vignette reaches maxIntensity.")]
        [SerializeField] float speedAtMax = 7f;
        [Tooltip("How quickly the vignette interpolates toward its target. Higher = snappier.")]
        [SerializeField] float smoothing = 6f;

        Vignette vignette;
        float current;

        public float MaxIntensity => maxIntensity;
        /// <summary>Programmatic setter for the cap (used by SettingsApplier when the user adjusts the slider).</summary>
        public void SetMaxIntensity(float value) => maxIntensity = Mathf.Clamp01(value);

        void Awake()
        {
            // Auto-resolve to the parent rig — convenient when the component
            // is dropped onto the camera, which is parented under PlayerRig.
            if (rig == null) rig = GetComponentInParent<ZeroGRig>();
            if (volume != null && volume.profile != null)
                volume.profile.TryGet(out vignette);
        }

        void OnEnable()  { SettingsManager.OnSettingsChanged += SyncFromSettings; SyncFromSettings(); }
        void OnDisable() { SettingsManager.OnSettingsChanged -= SyncFromSettings; }

        void SyncFromSettings()
        {
            if (SettingsManager.Instance != null) maxIntensity = SettingsManager.Instance.VignetteIntensity;
        }

        void Update()
        {
            if (rig == null || rig.Body == null || vignette == null) return;
            float speed = rig.Body.linearVelocity.magnitude;
            float target = Mathf.Clamp01(speed / speedAtMax) * maxIntensity;
            current = Mathf.Lerp(current, target, Time.deltaTime * smoothing);
            vignette.intensity.value = current;
        }
    }
}
