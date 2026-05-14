using UnityEngine;

namespace SpaceClimb
{
    public enum AsteroidWeight { Heavy, Medium, Light }

    /// <summary>
    /// Three motion profiles. Static is a normal solid handhold. Spinning rotates
    /// in place so the player swings around its anchor when grabbed. Drifting
    /// translates back and forth along an axis so the player rides it across
    /// gaps. Behavior is layered on top of weight (mass + drag).
    /// </summary>
    public enum AsteroidBehavior { Static, Spinning, Drifting }

    /// <summary>
    /// Authoritative state for every asteroid in the climb. Owns its Rigidbody
    /// configuration (mass tier + behavior), hazard glow, and impact-death.
    /// The procedural generator sets <see cref="weight"/>, <see cref="behavior"/>,
    /// and behavior parameters before parenting the instance into the scene.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Asteroid : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] bool isHazard;
        [SerializeField] AsteroidWeight weight = AsteroidWeight.Heavy;
        [SerializeField] AsteroidBehavior behavior = AsteroidBehavior.Static;

        [Header("Mass tiers (auto-applied on Awake)")]
        // Mass + linear/angular damping per weight class. These are tuned so the
        // mass-aware grab split in ZeroGRig produces the right "feel": Heavy is
        // a fixed handhold, Medium drifts a little when pulled, Light is throwable.
        [SerializeField] float heavyMass = 200f;
        [SerializeField] float heavyDrag = 4f;
        [SerializeField] float heavyAngularDrag = 6f;
        [SerializeField] float mediumMass = 20f;
        [SerializeField] float mediumDrag = 1.5f;
        [SerializeField] float mediumAngularDrag = 2.5f;
        [SerializeField] float lightMass = 3f;
        [SerializeField] float lightDrag = 0.6f;
        [SerializeField] float lightAngularDrag = 1f;

        [Header("Spinning behavior")]
        [Tooltip("Rotation axis (object-local). Will be normalized.")]
        [SerializeField] Vector3 spinAxis = Vector3.up;
        [Tooltip("Spin rate in degrees per second. Keep gentle (<60) for VR comfort.")]
        [SerializeField] float spinDegPerSec = 30f;

        [Header("Drifting behavior")]
        [Tooltip("Direction the asteroid oscillates along (world space).")]
        [SerializeField] Vector3 driftAxis = Vector3.right;
        [Tooltip("Peak displacement from the start position (meters).")]
        [SerializeField] float driftAmplitude = 3f;
        [Tooltip("Cycles per second. 0.05 = 20s for a full back-and-forth.")]
        [SerializeField] float driftFrequency = 0.08f;
        [Tooltip("Phase offset in [0,1]. Lets multiple drifters desync visually.")]
        [SerializeField] float driftPhase01;

        [Header("Hazard visual")]
        [SerializeField] Color hazardEmission = new(1.6f, 0.10f, 0.02f, 1f);
        [SerializeField] float hazardEmissionIntensity = 5.0f;
        [SerializeField] float hazardPulseSpeed = 1.6f;
        [Tooltip("Spawned as a child of every hazard at Awake — gives the rock " +
            "a clear red glow at distance, when the emission alone is hard to " +
            "see against the bright skybox. Range scales with the asteroid's " +
            "world bounds so big hazards illuminate further.")]
        [SerializeField] bool spawnHazardLight = true;
        [SerializeField] float hazardLightIntensity = 4f;
        [SerializeField] float hazardLightRangeBase = 6f;

        [Header("Hazard damage")]
        [Tooltip("HP drained per tick while the player is holding or pressed against this hazard.")]
        [SerializeField] float hazardTickDamage = 5f;
        [Tooltip("Seconds between damage ticks. 1s gives the player time to release before dying.")]
        [SerializeField] float hazardTickInterval = 1f;

        Rigidbody body;
        Vector3 driftStartPos;
        float driftTimeOffset;
        Renderer[] hazardRenderers;
        MaterialPropertyBlock hazardMpb;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        // Hazard damage state. grabberCount is incremented/decremented by
        // ZeroGGrabber when this hazard is grabbed/released. playerInBodyContact
        // tracks rig-body collisions via OnCollisionEnter/Exit. Either source
        // accrues hazard time; the timer drains 5 HP per second of total
        // exposure, regardless of whether the player is holding, leaning on,
        // or both.
        int grabberCount;
        bool playerInBodyContact;
        float hazardContactAccumulated;
        HealthSystem cachedRigHealth;

        public bool IsHazard => isHazard;
        public AsteroidWeight Weight => weight;
        public AsteroidBehavior Behavior => behavior;
        public Rigidbody Body => body;

        // Setters used by the procedural generator (sets values before scene is saved).
        public void SetWeight(AsteroidWeight w)   { weight = w; ApplyWeight(); }
        public void SetHazard(bool hazard)
        {
            isHazard = hazard;
            // The visual + light setup lives in Awake's hazard branch, which has
            // already run by the time the generator flags an asteroid via
            // AssignOnPathHazards. Without this re-init, hazard rocks stayed
            // visually identical to normal ones — same mesh, no red glow, no
            // point light — so the player had no warning before contact.
            if (hazard && body != null && hazardRenderers == null)
            {
                hazardRenderers = GetComponentsInChildren<Renderer>();
                hazardMpb = new MaterialPropertyBlock();
                // The shared asteroid material has _EMISSION stripped (URP/Lit
                // does this whenever the baseline _EmissionColor is black). Without
                // the keyword the shader never samples MPB-driven emission, so the
                // red glow we set later is invisible. Promote each renderer to a
                // per-instance material copy and enable the keyword on it. Per-
                // instance is fine here — there are only a handful of hazards.
                for (int i = 0; i < hazardRenderers.Length; i++)
                {
                    var r = hazardRenderers[i];
                    if (r == null) continue;
                    var instanced = r.material;     // creates per-instance copy
                    instanced.EnableKeyword("_EMISSION");
                    instanced.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                ApplyHazardEmission(1f);
                if (spawnHazardLight) SpawnHazardLight();
            }
        }
        public void SetBehavior(AsteroidBehavior b, Vector3 axis, float magnitude)
        {
            behavior = b;
            if (b == AsteroidBehavior.Spinning) { spinAxis = axis; spinDegPerSec = magnitude; }
            else if (b == AsteroidBehavior.Drifting)
            {
                driftAxis = axis;
                driftAmplitude = magnitude;
                // Re-capture the drift origin from the CURRENT transform. Without
                // this, drifters oscillate around the prefab's default origin
                // (0,0,0) — Awake fires inside Instantiate before the generator
                // has had a chance to move the GameObject to its placement
                // position, so ConfigureBehavior captured driftStartPos at
                // (0,0,0). FixedUpdate then snaps the asteroid back near origin
                // each tick. Setting driftStartPos here fixes that.
                driftStartPos = transform.position;
            }
            // Re-run the rigidbody/kinematic setup. Awake's ConfigureBehavior
            // ran with the prefab's default (Static / kinematic=true), so
            // without this, Spinning asteroids stay kinematic and FixedUpdate's
            // angularVelocity assignment has no physics effect — i.e. spinners
            // never visibly spin. Drifting needs the start-pos re-capture above
            // anyway, and Static is a no-op since it was already configured.
            ConfigureBehavior();
        }

        void Reset()
        {
            // Defaults applied when the component is first added in editor.
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            ApplyWeight();
            ConfigureBehavior();

            if (isHazard)
            {
                hazardRenderers = GetComponentsInChildren<Renderer>();
                hazardMpb = new MaterialPropertyBlock();
                ApplyHazardEmission(1f);
                if (spawnHazardLight) SpawnHazardLight();
            }
        }

        void SpawnHazardLight()
        {
            // Range derived from any renderer's bounds so giant hazards
            // illuminate further than handhold-sized ones. Falls back to a
            // sensible default when bounds aren't ready yet.
            float radius = 1f;
            if (hazardRenderers != null && hazardRenderers.Length > 0)
                radius = Mathf.Max(0.5f, hazardRenderers[0].bounds.extents.magnitude);
            var go = new GameObject("HazardLight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.18f, 0.05f);
            light.intensity = hazardLightIntensity;
            light.range = hazardLightRangeBase + radius * 2.5f;
            light.shadows = LightShadows.None;       // VR perf — point shadows are expensive
            light.renderMode = LightRenderMode.Auto;
        }

        /// <summary>
        /// Pushes the weight tier into the Rigidbody. Safe to call any time
        /// (also called by the editor generator). Always disables gravity since
        /// every asteroid lives in zero-G.
        /// </summary>
        public void ApplyWeight()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            switch (weight)
            {
                case AsteroidWeight.Heavy:
                    body.mass = heavyMass;
                    body.linearDamping = heavyDrag;
                    body.angularDamping = heavyAngularDrag;
                    break;
                case AsteroidWeight.Medium:
                    body.mass = mediumMass;
                    body.linearDamping = mediumDrag;
                    body.angularDamping = mediumAngularDrag;
                    break;
                case AsteroidWeight.Light:
                    body.mass = lightMass;
                    body.linearDamping = lightDrag;
                    body.angularDamping = lightAngularDrag;
                    break;
            }
        }

        /// <summary>
        /// Set up Rigidbody for the chosen behavior. Must run after ApplyWeight
        /// since both touch isKinematic / damping.
        /// </summary>
        void ConfigureBehavior()
        {
            if (body == null) body = GetComponent<Rigidbody>();

            switch (behavior)
            {
                case AsteroidBehavior.Static:
                    // ALL Static asteroids are kinematic. Reason: dynamic-vs-
                    // dynamic contact between adjacent placed rocks causes
                    // visible micro-jiggle and visual interpenetration ("rocks
                    // grinding on each other"), which ruins the stillness of a
                    // zero-G handhold field. Kinematic anchors get the player
                    // a full rig-pull on grab — same gameplay feel as Heavy was
                    // before — and they can't drift or be pushed by neighbors.
                    // Throwable rocks would need a dedicated AsteroidWeight
                    // tier or a separate "Loose" behavior; defer until needed.
                    body.isKinematic = true;
                    break;
                case AsteroidBehavior.Spinning:
                    // Non-kinematic so we can drive angularVelocity. Zero angular damping
                    // so we don't fight ourselves; we explicitly maintain spin in FixedUpdate.
                    body.isKinematic = false;
                    body.angularDamping = 0f;
                    break;
                case AsteroidBehavior.Drifting:
                    // Kinematic — we drive position via MovePosition. Mass-aware grab in
                    // ZeroGRig falls back to "full rig pull" for kinematic asteroids,
                    // so the player rides along.
                    body.isKinematic = true;
                    driftStartPos = transform.position;
                    driftTimeOffset = driftPhase01 * (1f / Mathf.Max(0.0001f, driftFrequency));
                    break;
            }
        }

        void FixedUpdate()
        {
            if (body == null) return;
            switch (behavior)
            {
                case AsteroidBehavior.Spinning:
                    // Maintain target angular velocity each step regardless of grabs/collisions.
                    body.angularVelocity = spinAxis.normalized * (spinDegPerSec * Mathf.Deg2Rad);
                    break;
                case AsteroidBehavior.Drifting:
                    // Sinusoidal oscillation about the start position along driftAxis.
                    float t = Time.time + driftTimeOffset;
                    float s = Mathf.Sin(t * driftFrequency * 2f * Mathf.PI);
                    Vector3 target = driftStartPos + driftAxis.normalized * (s * driftAmplitude);
                    body.MovePosition(target);
                    break;
            }
        }

        void Update()
        {
            // Hazard pulse — perlin noise gives an organic "menacing" pulse rather than uniform sine.
            if (!isHazard || hazardRenderers == null) return;
            float t = 0.6f + 0.4f * Mathf.PerlinNoise(Time.time * hazardPulseSpeed, transform.position.y * 0.1f);
            ApplyHazardEmission(t);

            // Tick damage while the player is in any form of contact. Accumulate
            // delta time and fire one damage event per interval — this gives the
            // player a clear "leave the rock or die" beat instead of melting them
            // a hundredth of HP per frame.
            bool exposed = grabberCount > 0 || playerInBodyContact;
            if (!exposed) { hazardContactAccumulated = 0f; return; }
            hazardContactAccumulated += Time.deltaTime;
            if (hazardContactAccumulated >= hazardTickInterval)
            {
                hazardContactAccumulated -= hazardTickInterval;
                ApplyHazardTick();
            }
        }

        void ApplyHazardTick()
        {
            if (cachedRigHealth == null)
            {
                var rig = UnityEngine.Object.FindFirstObjectByType<ZeroGRig>();
                if (rig != null) cachedRigHealth = rig.GetComponent<HealthSystem>();
            }
            if (cachedRigHealth != null) cachedRigHealth.ApplyDamage(hazardTickDamage);
            // Punchy feedback so the player feels the hit even with no health bar visible.
            HapticBus.PulseAll(0.95f, 0.18f);
            AudioCues.PlayHazard();
        }

        /// <summary>Called by ZeroGGrabber when a hand grabs this hazard. Drives the tick-damage loop.</summary>
        public void NotifyGrabbed() { if (isHazard) grabberCount++; }
        /// <summary>Called by ZeroGGrabber when a hand releases this hazard.</summary>
        public void NotifyReleased() { if (isHazard) grabberCount = Mathf.Max(0, grabberCount - 1); }

        void ApplyHazardEmission(float pulse)
        {
            Color emission = hazardEmission * hazardEmissionIntensity * pulse;
            for (int i = 0; i < hazardRenderers.Length; i++)
            {
                var r = hazardRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(hazardMpb);
                hazardMpb.SetColor(EmissionColor, emission);
                r.SetPropertyBlock(hazardMpb);
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!isHazard) return;
            var other = collision.rigidbody;
            if (other == null) return;
            if (other.GetComponent<ZeroGRig>() == null) return;
            playerInBodyContact = true;
            // Sting on real hits only — quiet brush contacts.
            if (collision.relativeVelocity.sqrMagnitude > 1f)
                AudioCues.PlayHazard();
        }

        void OnCollisionExit(Collision collision)
        {
            if (!isHazard) return;
            var other = collision.rigidbody;
            if (other == null) return;
            if (other.GetComponent<ZeroGRig>() == null) return;
            playerInBodyContact = false;
            // Reset accumulator so re-touching the rock doesn't get a partial-second head start.
            hazardContactAccumulated = 0f;
        }
    }
}
