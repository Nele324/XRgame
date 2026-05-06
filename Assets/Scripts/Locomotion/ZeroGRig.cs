using System.Collections.Generic;
using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// The player's "body" in zero-G. While no hand is grabbing, the rig is a
    /// dynamic Rigidbody capped at <see cref="maxDriftSpeed"/>. While at least
    /// one hand is grabbing, the rig becomes kinematic and is moved each
    /// FixedUpdate so the grabbed anchor point stays glued to the controller —
    /// this is what makes the climb feel rigid in VR (no spring oscillation).
    ///
    /// When grabbing a non-kinematic asteroid the displacement is split between
    /// rig and asteroid by mass: heavy asteroids barely move (player swings on
    /// them), light ones are dragged along with the player (throwable).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ZeroGRig : MonoBehaviour
    {
        [SerializeField] Rigidbody body;
        [SerializeField] Transform xrOrigin;

        [Header("Drift")]
        [Tooltip("Hard cap on free-flight speed (m/s). Prevents runaway momentum on hard releases.")]
        [SerializeField] float maxDriftSpeed = 8f;

        [Header("Release")]
        [Tooltip("Number of FixedUpdate samples averaged for the launch velocity on release.")]
        [SerializeField] int velocityBufferFrames = 5;
        [Tooltip("Multiplier on the launch velocity. 1 = exact average; >1 = punchier release.")]
        [SerializeField] float releaseVelocityScale = 1f;

        [Header("Performance")]
        [Tooltip("Forces a fixed timestep matched to the headset refresh rate so the body correction stays in lockstep with controller poses. 0 = leave at project default.")]
        [SerializeField] float fixedTimestep = 1f / 90f;

        [Header("Impact Feedback")]
        [SerializeField] float minImpactSpeed = 1.5f;
        [SerializeField] float maxImpactSpeed = 8f;
        [SerializeField] float impactHapticDuration = 0.18f;

        /// <summary>Fired on collision with relative speed above the impact threshold. Argument is relative speed in m/s. HealthSystem subscribes to this for damage.</summary>
        public System.Action<float> OnImpact;

        readonly List<ZeroGGrabber> active = new();
        readonly Queue<Vector3> posDeltaBuffer = new();
        Vector3 lastFramePos;
        bool frozen;

        public int ActiveGrabberCount => active.Count;
        public Rigidbody Body => body;

        void Awake()
        {
            // Auto-resolve serialized refs so the component is robust to setups
            // where the inspector wiring was lost (e.g. prefab override mistakes).
            if (xrOrigin == null) xrOrigin = transform;
            if (body == null) body = GetComponent<Rigidbody>();
            lastFramePos = body.position;
            // Lock physics tick to controller refresh; matters in VR because at
            // 72/90/120 Hz any mismatch shows up as jittery body-anchor drift.
            if (fixedTimestep > 0f) Time.fixedDeltaTime = fixedTimestep;
        }

        /// <summary>
        /// Called by a grabber when the grip is pressed. Switches the rig to
        /// kinematic so the FixedUpdate correction can drive position
        /// directly. Resetting velocities prevents pre-grab drift from
        /// "ghosting" through the grab.
        /// </summary>
        public void RegisterGrabber(ZeroGGrabber g)
        {
            if (active.Contains(g)) return;
            active.Add(g);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            posDeltaBuffer.Clear();
            lastFramePos = body.position;
        }

        /// <summary>
        /// Called when the last grip releases. Re-enables dynamic physics and
        /// gives the body the average velocity it had during the last few
        /// FixedUpdates — that's what makes "yank then release" actually fling
        /// the player.
        /// </summary>
        public void UnregisterGrabber(ZeroGGrabber g)
        {
            if (!active.Remove(g)) return;
            if (active.Count > 0) return;       // still holding with the other hand
            Vector3 launch = EstimateReleaseVelocity();
            body.isKinematic = false;
            body.linearVelocity = launch;
        }

        /// <summary>Average position-delta velocity over the buffered FixedUpdates.</summary>
        public Vector3 EstimateReleaseVelocity()
        {
            if (posDeltaBuffer.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (var d in posDeltaBuffer) sum += d;
            return (sum / posDeltaBuffer.Count / Time.fixedDeltaTime) * releaseVelocityScale;
        }

        /// <summary>Force-place the rig (e.g. respawn). Drops any active grabs and clears velocity.</summary>
        public void TeleportTo(Vector3 spawnPos)
        {
            active.Clear();
            body.isKinematic = false;
            body.position = spawnPos;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            posDeltaBuffer.Clear();
            lastFramePos = spawnPos;
        }

        /// <summary>Pause-style freeze: zeroes velocity and locks the body kinematic. Pair with FreezePhysics(false) to resume.</summary>
        public void FreezePhysics(bool freeze)
        {
            frozen = freeze;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = freeze;
        }

        void FixedUpdate()
        {
            if (frozen) return;

            if (active.Count > 0)
            {
                // For each grabber, compute the displacement that would put the
                // anchor exactly under the hand. Sum-then-average means two-handed
                // grabbing on different asteroids picks the midpoint — natural
                // and avoids fighting between hands.
                Vector3 rigSum = Vector3.zero;
                foreach (var g in active)
                {
                    Vector3 toAnchor = g.AnchorWorldPos - g.HandWorldPos;
                    Rigidbody asteroid = g.GrabbedRigidbody;
                    if (asteroid != null && !asteroid.isKinematic)
                    {
                        // Mass-aware split. With player mass m_p and asteroid mass m_a:
                        //   playerShare   = m_a / (m_p + m_a)
                        //   asteroidShare = m_p / (m_p + m_a)
                        // i.e. the lighter object moves more — Newtonian momentum
                        // sharing approximated by displacement, since we're driving
                        // both bodies kinematically each step.
                        float total = body.mass + asteroid.mass;
                        float playerShare = asteroid.mass / total;
                        float asteroidShare = body.mass / total;
                        rigSum += toAnchor * playerShare;
                        asteroid.MovePosition(asteroid.position - toAnchor * asteroidShare);
                    }
                    else
                    {
                        // Static or kinematic asteroid (drifting): full rig pull,
                        // asteroid is unaffected. Player rides whatever path the
                        // asteroid is on.
                        rigSum += toAnchor;
                    }
                }
                Vector3 correction = rigSum / active.Count;
                body.MovePosition(body.position + correction);
            }
            else
            {
                // Free flight — clamp to max drift to keep VR comfortable.
                Vector3 v = body.linearVelocity;
                if (v.sqrMagnitude > maxDriftSpeed * maxDriftSpeed)
                    body.linearVelocity = v.normalized * maxDriftSpeed;
            }

            // Always sample our own deltas so EstimateReleaseVelocity has data.
            Vector3 frameDelta = body.position - lastFramePos;
            posDeltaBuffer.Enqueue(frameDelta);
            if (posDeltaBuffer.Count > velocityBufferFrames) posDeltaBuffer.Dequeue();
            lastFramePos = body.position;
        }

        void OnCollisionEnter(Collision collision)
        {
            // Threshold + scale to amplitude in [0.25, 1.0]. Below threshold we
            // don't trigger feedback at all — keeps soft brushes from buzzing.
            float speed = collision.relativeVelocity.magnitude;
            if (speed < minImpactSpeed) return;
            float t = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, speed);
            float amplitude = Mathf.Lerp(0.25f, 1f, t);
            HapticBus.PulseAll(amplitude, impactHapticDuration);
            AudioCues.PlayImpact(amplitude);
            OnImpact?.Invoke(speed);
        }
    }
}
