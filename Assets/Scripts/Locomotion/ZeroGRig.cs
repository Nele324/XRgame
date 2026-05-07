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
        // Saved across a pause so the player keeps their drift on resume.
        // Without this, opening the menu mid-flight would zero velocity and
        // the player would wake up stationary — punishing during a precise
        // approach to the docking goal.
        Vector3 frozenLinearVel;
        Vector3 frozenAngularVel;

        public int ActiveGrabberCount => active.Count;
        public Rigidbody Body => body;

        void Awake()
        {
            // Auto-resolve serialized refs so the component is robust to setups
            // where the inspector wiring was lost (e.g. prefab override mistakes).
            if (xrOrigin == null) xrOrigin = transform;
            if (body == null) body = GetComponent<Rigidbody>();
            lastFramePos = body.position;
            // Zero-G enforcement. The rig prefab can carry non-zero damping from
            // authored defaults; in true zero-G the player must drift forever
            // without losing momentum, so we pin these every Awake instead of
            // hoping the inspector is correct.
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            // Continuous detection prevents tunneling at high drift speeds. Without
            // this, hitting a small asteroid at 8 m/s with a 90 Hz fixed step can
            // produce a "skip through" if the rig moves more than a collider-width
            // per step. Setting it BEFORE any kinematic flag flip avoids a Unity
            // warning in 2022+.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // Hard cap so a runaway impulse can't NaN the rig. Well above the
            // gameplay-level maxDriftSpeed (8 m/s) so it only catches catastrophes.
            body.maxLinearVelocity = 50f;
            // Lock physics tick to controller refresh; matters in VR because at
            // 72/90/120 Hz any mismatch shows up as jittery body-anchor drift.
            if (fixedTimestep > 0f) Time.fixedDeltaTime = fixedTimestep;
        }

        void Start()
        {
            // Snap to spawn AND keep the rig kinematic for the first half-second
            // of play. Reason: the runtime asteroid regen instantiates ~120 new
            // bodies at scene-load time. Even with conservative spacing, with
            // randomly-rotated convex meshes there's a non-zero chance one
            // ends up just barely overlapping the rig's capsule — and the
            // resulting depenetration impulse is large enough to fling the
            // player off the map before Awake's velocity cap can clamp it.
            // A kinematic body ignores impulses entirely, so we ride out the
            // first few physics steps in safe-mode, then transition to dynamic
            // once the field has settled.
            var lm = FindAnyObjectByType<LevelManager>();
            Vector3 sp = lm != null && lm.SpawnPoint != null ? lm.SpawnPoint.position : transform.position;
            body.position = sp;
            transform.position = sp;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            posDeltaBuffer.Clear();
            lastFramePos = body.position;
            spawnSettleEndTime = Time.time + 0.5f;
            body.isKinematic = true;
        }

        float spawnSettleEndTime = -1f;

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
            // Don't stomp velocity while frozen — that would lose the saved
            // drift the player had pre-pause. The body is already kinematic
            // during freeze so we just track the grab in `active`.
            if (!frozen)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
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
            // While frozen, defer all body changes — Resume reapplies the saved
            // velocity. Without this skip, releasing during pause would flip
            // the body to dynamic with zero launch (no posDeltaBuffer accumulates
            // while frozen) and erase the saved drift.
            if (frozen) return;
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

        /// <summary>Pause-style freeze: stashes velocity and locks the body kinematic.
        /// On unfreeze, restores the saved velocity if no grab is active. Pair with
        /// FreezePhysics(false) to resume — the player keeps the drift they had pre-pause.</summary>
        public void FreezePhysics(bool freeze)
        {
            if (freeze)
            {
                if (!frozen)
                {
                    // Capture once. Repeated FreezePhysics(true) calls during a
                    // single pause must not overwrite the saved velocity with the
                    // zeroed body velocity from a previous freeze pass.
                    frozenLinearVel = body.linearVelocity;
                    frozenAngularVel = body.angularVelocity;
                }
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                frozen = true;
            }
            else
            {
                // If a grab is still active (e.g. paused mid-climb, resumed while
                // still holding), keep the body kinematic — that's what holds the
                // anchor under the hand. Otherwise restore the player's drift.
                bool grabbing = active.Count > 0;
                body.isKinematic = grabbing;
                if (!grabbing)
                {
                    body.linearVelocity = frozenLinearVel;
                    body.angularVelocity = frozenAngularVel;
                }
                frozenLinearVel = Vector3.zero;
                frozenAngularVel = Vector3.zero;
                frozen = false;
            }
        }

        void FixedUpdate()
        {
            if (frozen) return;

            // Spawn-settle window. While active, keep the rig kinematic so any
            // depenetration impulses from the just-spawned asteroid field bounce
            // harmlessly off. Once the window expires we hand control back to
            // dynamic physics. Don't touch velocity — kinematic bodies log a
            // warning if you try to set it.
            if (spawnSettleEndTime > 0f)
            {
                if (Time.time < spawnSettleEndTime) return;
                spawnSettleEndTime = -1f;
                body.isKinematic = false;
            }

            // Crash guard. Severe interpenetration (e.g. a yank that pushes the
            // rig deep into an asteroid before continuous detection catches up)
            // can produce NaN/Inf velocities. If we let those propagate into
            // MovePosition, the rig's transform NaNs and the headset render
            // freezes — what users see as a hard crash. Zero out and recover.
            if (!IsFinite(body.linearVelocity) || !IsFinite(body.angularVelocity))
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            // Hard velocity clamp. body.maxLinearVelocity isn't reliable across
            // all Unity versions/platforms (a depenetration impulse can briefly
            // exceed it before the engine clamps), so we enforce our own ceiling
            // in code. 30 m/s is well past the gameplay maxDriftSpeed (8) but
            // any value above it almost certainly came from a spawn-time
            // collision rather than legitimate flight — zero it before the
            // physics step integrates the player out of the level.
            const float panicSpeedSqr = 30f * 30f;
            if (body.linearVelocity.sqrMagnitude > panicSpeedSqr)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

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
                // Cap per-step correction. MovePosition on a kinematic body does
                // NOT do continuous collision detection — a hard yank can push
                // the rig 1+ meters in a single FixedUpdate, straight through any
                // asteroid in the way. 0.3m at 90 Hz = 27 m/s, well past any
                // natural arm motion (~2 m/s peak), so this only kicks in on
                // pathological inputs and prevents the resulting deep penetration
                // that's been crashing the editor.
                const float maxStepCorrection = 0.30f;
                float corrSqr = correction.sqrMagnitude;
                if (corrSqr > maxStepCorrection * maxStepCorrection)
                    correction *= maxStepCorrection / Mathf.Sqrt(corrSqr);
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

        static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

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
