using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// On scene start, snaps a world-space menu in front of the actual runtime
    /// camera at face height. Without this, the menu is anchored at a fixed
    /// world Y while the XR camera ends up wherever the headset's tracked pose
    /// puts it — so a 1.6 m player and a 1.85 m player see the menu at very
    /// different vertical offsets, and with Floor tracking + a non-zero Camera
    /// Y Offset the menu can fall well below the player's view.
    ///
    /// One-shot: runs once in Start, then leaves the canvas alone (so the menu
    /// doesn't slide as the player walks around). Re-enable the component to
    /// re-anchor.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class WorldMenuPlacement : MonoBehaviour
    {
        [Tooltip("Distance in front of the camera (m).")]
        [SerializeField] float distance = 1.7f;
        [Tooltip("Vertical offset from the camera's eye line (m). Small " +
            "positive value reads as 'just above eye level' so menus don't " +
            "feel like they're sliding down the player's chest.")]
        [SerializeField] float verticalOffset = 0.05f;
        [Tooltip("Horizontal offset along the camera's right vector (m).")]
        [SerializeField] float horizontalOffset = 0f;
        [Tooltip("Use the camera's forward projected onto the horizontal " +
            "plane — keeps the menu upright even if the player is tilted at " +
            "spawn.")]
        [SerializeField] bool levelHorizontal = true;
        [Tooltip("Re-anchor for this many seconds after Start. Why: the XR " +
            "camera's tracked pose can take a few frames to settle after the " +
            "headset wakes up — anchoring once at frame 0 can lock onto a " +
            "stale (0,0,0) pose. The component disables itself after this " +
            "window so the menu doesn't follow the player around.")]
        [SerializeField] float settleSeconds = 2f;
        [Tooltip("If the camera Y is below this height (m), assume the headset " +
            "pose hasn't been received yet and substitute this as the eye " +
            "height. Why: with Floor tracking origin, an untracked headset " +
            "reports Y=0, which would anchor the menu near the floor. Using a " +
            "sensible default keeps the menu at a reasonable height even if " +
            "the player puts the headset on after the scene loads.")]
        [SerializeField] float minEyeHeight = 1.0f;
        [Tooltip("Eye height to use when the camera pose is invalid (m).")]
        [SerializeField] float fallbackEyeHeight = 1.65f;

        Camera cam;
        float endTime;

        void Start()
        {
            cam = Camera.main;
            endTime = Time.time + settleSeconds;
            Anchor();
        }

        void LateUpdate()
        {
            if (Time.time >= endTime) { enabled = false; return; }
            Anchor();
        }

        /// <summary>
        /// Re-anchor immediately, e.g. when a world-space pause menu is shown
        /// after the player has walked or turned away from the spawn pose.
        /// </summary>
        public void AnchorNow()
        {
            cam = Camera.main;
            Anchor();
            // Restart the brief settle window so the menu is steady once it's shown.
            endTime = Time.time + settleSeconds;
            enabled = true;
        }

        void Anchor()
        {
            if (cam == null) { cam = Camera.main; if (cam == null) return; }
            Vector3 camPos = cam.transform.position;
            // Floor-tracked rig reports Y=0 before the headset is tracked.
            // Substitute a sensible eye height so the menu doesn't lock low.
            if (camPos.y < minEyeHeight) camPos.y = fallbackEyeHeight;
            Vector3 fwd = cam.transform.forward;
            if (levelHorizontal)
            {
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                fwd.Normalize();
            }
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 pos = camPos
                          + fwd * distance
                          + Vector3.up * verticalOffset
                          + right * horizontalOffset;
            transform.position = pos;
            Vector3 toCam = camPos - transform.position;
            if (levelHorizontal) toCam.y = 0f;
            if (toCam.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        }
    }
}
