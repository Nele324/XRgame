using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Polls the player rig's position on a fixed cadence and kills the run
    /// when it leaves the climb cylinder defined by (climbAxisOrigin,
    /// climbAxisDirection, maxRadialDistance) or the Y range [minY, maxY].
    /// Also drives a haptic "approach warning" pulse as the player nears the
    /// boundary, so going out of bounds never feels arbitrary.
    /// </summary>
    public class OutOfBoundsKiller : MonoBehaviour
    {
        [Header("Climb cylinder")]
        [SerializeField] Vector3 climbAxisOrigin = Vector3.zero;
        [SerializeField] Vector3 climbAxisDirection = Vector3.up;
        [SerializeField] float maxRadialDistance = 25f;
        [SerializeField] float minY = -25f;
        [SerializeField] float maxY = 100f;
        [Tooltip("Seconds between bounds checks. 0.1 is plenty — a 90Hz position update isn't useful here.")]
        [SerializeField] float checkInterval = 0.1f;

        [Header("Drift Warning")]
        [Tooltip("Fraction of bounds at which haptic warning starts (0..1). 0.75 = warn for the last quarter of approach.")]
        [Range(0.5f, 0.99f)][SerializeField] float warningThreshold = 0.75f;
        [SerializeField] float warningPulseInterval = 0.4f;
        [SerializeField] float warningPulseDuration = 0.08f;

        float lastCheck;
        float lastWarningPulse;

        void Update()
        {
            // Throttle to the configured interval — saves a small amount of work
            // and makes the pulse cadence stable regardless of frame rate.
            if (Time.time - lastCheck < checkInterval) return;
            lastCheck = Time.time;
            if (GameStateController.Instance == null) return;

            Vector3 pos = transform.position;

            // Y-axis danger: scale to [0, 1] toward whichever bound applies.
            // We guard against a zero divisor (would happen only with bad config).
            float yNorm;
            if (pos.y < 0f) yNorm = Mathf.Abs(minY) > 1e-4f ? pos.y / minY : 0f;
            else            yNorm = Mathf.Abs(maxY) > 1e-4f ? pos.y / maxY : 0f;

            // Radial danger: project the player onto the climb axis, take the
            // perpendicular distance, normalize against the cylinder radius.
            Vector3 axisDir = climbAxisDirection.sqrMagnitude > 1e-4f ? climbAxisDirection.normalized : Vector3.up;
            Vector3 toPlayer = pos - climbAxisOrigin;
            float along = Vector3.Dot(toPlayer, axisDir);
            Vector3 radial = toPlayer - axisDir * along;
            float radialNorm = maxRadialDistance > 1e-4f ? radial.magnitude / maxRadialDistance : 0f;

            // Pick whichever bound the player is closer to.
            float dangerNorm = Mathf.Max(yNorm, radialNorm);

            if (dangerNorm >= 1f)
            {
                GameStateController.Instance.Die(DeathCause.OutOfBounds);
                return;
            }

            // Inside the warning zone: pulse with intensity ramping from 0.3
            // (just past threshold) to 0.9 (right at the boundary).
            if (dangerNorm >= warningThreshold && Time.time - lastWarningPulse >= warningPulseInterval)
            {
                lastWarningPulse = Time.time;
                float intensity = Mathf.InverseLerp(warningThreshold, 1f, dangerNorm);
                HapticBus.PulseAll(0.3f + intensity * 0.6f, warningPulseDuration);
            }
        }
    }
}
