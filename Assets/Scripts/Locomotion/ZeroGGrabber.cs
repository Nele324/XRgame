using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceClimb
{
    /// <summary>
    /// Per-hand grabber. Polls a small overlap sphere around the controller for
    /// grabbable colliders, locks an "anchor" point on the surface when the grip
    /// is pressed, and reports back to <see cref="ZeroGRig"/> so the rig can do
    /// its mass-aware position correction. Also drives proximity haptics, the
    /// grab-indicator visual, and on release grants the asteroid an exit
    /// velocity sampled from the last few fixed steps (so light asteroids don't
    /// stop dead — they keep the momentum the player gave them).
    /// </summary>
    public class ZeroGGrabber : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] ZeroGRig rig;
        [SerializeField] Transform handTransform;
        [SerializeField] HandedHaptics haptics;
        [SerializeField] Renderer grabIndicator;
        [SerializeField] Color grabIndicatorProximity = new(0.4f, 0.85f, 1.0f, 1f);
        [SerializeField] Color grabIndicatorGrabbed = new(1.0f, 0.85f, 0.35f, 1f);
        [SerializeField] float grabIndicatorScale = 0.05f;

        [Header("Grab Detection")]
        [Tooltip("Asteroid layer — what we can grab onto.")]
        [SerializeField] LayerMask grabbableMask;
        [SerializeField] float grabRadius = 0.18f;
        [SerializeField] float proximityRadius = 0.32f;

        [Header("Input")]
        [SerializeField] InputActionAsset actionsAsset;
        [SerializeField] string actionMapName = "XRI Right Interaction";
        [SerializeField] string actionName = "Select";

        [Header("Haptics")]
        [Range(0f, 1f)][SerializeField] float grabHaptic = 0.85f;
        [SerializeField] float grabHapticDuration = 0.12f;
        [Range(0f, 1f)][SerializeField] float releaseHaptic = 0.4f;
        [SerializeField] float releaseHapticDuration = 0.06f;
        [Range(0f, 1f)][SerializeField] float proximityHaptic = 0.25f;
        [SerializeField] float proximityHapticDuration = 0.03f;
        [Tooltip("Continuous low rumble while gripping. 0 = off.")]
        [Range(0f, 1f)][SerializeField] float gripRumble = 0.12f;
        [SerializeField] float gripRumbleInterval = 0.18f;

        [Header("Release momentum")]
        [Tooltip("Frames of grabbed-surface position deltas averaged to estimate the asteroid's exit velocity on release.")]
        [SerializeField] int releaseSampleFrames = 5;

        // Grabbed state. grabbedSurface is the Transform of the collider we hit;
        // grabbedLocalPos is the click point in that transform's local space —
        // we recompute the world anchor each frame so the player follows the
        // surface even if it moves or rotates (drifting / spinning asteroids).
        Transform grabbedSurface;
        Vector3 grabbedLocalPos;
        Rigidbody grabbedBody;          // cached so we don't search the parent chain each tick
        Asteroid grabbedAsteroid;       // cached asteroid component; null when grabbing non-asteroid colliders
        bool isGrabbed;

        // Proximity state. We highlight the nearest grabbable in proximity range
        // before the player presses grip — a small VR affordance that "this is
        // grabbable, point and click."
        Transform proximitySurface;
        Vector3 proximityLocalPos;
        bool hasProximity;

        // Input action that drives grab/release. Listened to via performed/canceled.
        InputAction gripAction;

        // Reusable buffer for OverlapSphereNonAlloc so we don't allocate each frame.
        readonly Collider[] hits = new Collider[8];
        float lastRumbleTime;
        Material grabIndicatorMat;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        // Surface-position buffer for the release-momentum trick. Captured in
        // FixedUpdate so deltas are wall-clock consistent.
        readonly System.Collections.Generic.Queue<Vector3> surfaceDeltaBuffer = new();
        Vector3 lastSurfaceWorldPos;

        /// <summary>Fired once on every successful grab (any hand). Tutorial uses this to dismiss itself.</summary>
        public static event System.Action AnyGrabbed;

        public bool IsGrabbed => isGrabbed;
        public Vector3 HandWorldPos => handTransform != null ? handTransform.position : transform.position;
        public Vector3 AnchorWorldPos => isGrabbed && grabbedSurface != null
            ? grabbedSurface.TransformPoint(grabbedLocalPos)
            : HandWorldPos;
        /// <summary>Cached Rigidbody on or above the grabbed collider; null when not grabbed or surface is static.</summary>
        public Rigidbody GrabbedRigidbody => grabbedBody;

        void Reset()
        {
            // Reasonable defaults when the component is first added in editor.
            handTransform = transform;
            rig = GetComponentInParent<ZeroGRig>();
            haptics = GetComponent<HandedHaptics>();
        }

        void Awake()
        {
            EnsureGrabIndicator();
            if (grabIndicator != null) grabIndicator.gameObject.SetActive(false);
        }

        void EnsureGrabIndicator()
        {
            // Auto-spawn a tiny emissive sphere if none was wired in the inspector.
            // Useful default so haptics + visuals work out of the box.
            if (grabIndicator != null) return;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "GrabIndicator";
            var col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);
            sphere.transform.SetParent(transform.parent, false);
            sphere.transform.localScale = Vector3.one * grabIndicatorScale;
            grabIndicator = sphere.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            grabIndicatorMat = new Material(shader);
            grabIndicatorMat.SetColor("_BaseColor", Color.black);
            grabIndicatorMat.SetColor("_Color", Color.black);
            grabIndicatorMat.EnableKeyword("_EMISSION");
            grabIndicator.sharedMaterial = grabIndicatorMat;
        }

        void OnEnable()
        {
            // Resolve and bind the grip action by name. Resolved lazily so the
            // input asset can be a project asset — works in any scene.
            if (actionsAsset == null) return;
            var map = actionsAsset.FindActionMap(actionMapName);
            if (map == null) { Debug.LogWarning($"ZeroGGrabber: action map '{actionMapName}' not found.", this); return; }
            gripAction = map.FindAction(actionName);
            if (gripAction == null) { Debug.LogWarning($"ZeroGGrabber: action '{actionName}' not found in '{actionMapName}'.", this); return; }
            gripAction.performed += OnGripDown;
            gripAction.canceled += OnGripUp;
            gripAction.Enable();
        }

        void OnDisable()
        {
            // Unsubscribe to avoid leaks. We do NOT call gripAction.Disable() because
            // the action lives on a shared asset — disabling it would break other consumers.
            if (gripAction == null) return;
            gripAction.performed -= OnGripDown;
            gripAction.canceled -= OnGripUp;
            gripAction = null;
        }

        void OnDestroy()
        {
            if (grabIndicatorMat != null) Destroy(grabIndicatorMat);
        }

        void Update()
        {
            if (handTransform == null) return;

            if (isGrabbed)
            {
                UpdateGrabIndicator(true);
                EmitGripRumble();
                return;
            }

            // ===== Proximity scan =====
            // OverlapSphereNonAlloc avoids per-frame GC. Layer mask filters to
            // grabbables only; if unset we fall back to AllLayers which is
            // permissive but safe (the resulting hit still needs a collider).
            int mask = grabbableMask.value == 0 ? Physics.AllLayers : grabbableMask.value;
            int count = Physics.OverlapSphereNonAlloc(handTransform.position, proximityRadius, hits, mask, QueryTriggerInteraction.Ignore);

            float bestDistSq = float.PositiveInfinity;
            Collider best = null;
            Vector3 bestPoint = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                var col = hits[i];
                if (col == null) continue;
                // ClosestPoint gives us the actual surface point — accurate for
                // both convex MeshColliders and primitive colliders.
                Vector3 cp = col.ClosestPoint(handTransform.position);
                float d = (cp - handTransform.position).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; best = col; bestPoint = cp; }
            }

            if (best != null)
            {
                proximitySurface = best.transform;
                proximityLocalPos = best.transform.InverseTransformPoint(bestPoint);
                if (!hasProximity)
                {
                    hasProximity = true;
                    if (haptics != null) haptics.Pulse(proximityHaptic, proximityHapticDuration);
                }
                UpdateGrabIndicator(false);
            }
            else if (hasProximity)
            {
                hasProximity = false;
                proximitySurface = null;
                if (grabIndicator != null) grabIndicator.gameObject.SetActive(false);
            }
        }

        void FixedUpdate()
        {
            // Track the grabbed surface's world position over time so we can grant
            // a non-kinematic asteroid an exit velocity on release. Kinematic
            // bodies (e.g. drifting asteroids) ignore linearVelocity, so this is
            // a no-op for them and we don't bother computing it.
            if (!isGrabbed || grabbedSurface == null) return;
            Vector3 currentSurfacePos = grabbedSurface.TransformPoint(grabbedLocalPos);
            Vector3 delta = currentSurfacePos - lastSurfaceWorldPos;
            surfaceDeltaBuffer.Enqueue(delta);
            while (surfaceDeltaBuffer.Count > releaseSampleFrames) surfaceDeltaBuffer.Dequeue();
            lastSurfaceWorldPos = currentSurfacePos;
        }

        void UpdateGrabIndicator(bool grabbed)
        {
            if (grabIndicator == null) return;
            Vector3 worldPoint = grabbed
                ? (grabbedSurface != null ? grabbedSurface.TransformPoint(grabbedLocalPos) : HandWorldPos)
                : (proximitySurface != null ? proximitySurface.TransformPoint(proximityLocalPos) : HandWorldPos);
            grabIndicator.transform.position = worldPoint;
            if (!grabIndicator.gameObject.activeSelf) grabIndicator.gameObject.SetActive(true);
            if (grabIndicatorMat != null)
                grabIndicatorMat.SetColor(EmissionColor, grabbed ? grabIndicatorGrabbed * 4f : grabIndicatorProximity * 1.6f);
        }

        void EmitGripRumble()
        {
            // Subtle continuous rumble while gripping — sells the "you're holding on" feel.
            // Throttled by gripRumbleInterval so we don't spam haptic events.
            if (haptics == null || gripRumble <= 0f) return;
            if (Time.time - lastRumbleTime < gripRumbleInterval) return;
            lastRumbleTime = Time.time;
            haptics.Pulse(gripRumble, gripRumbleInterval * 0.9f);
        }

        void OnGripDown(InputAction.CallbackContext ctx)
        {
            if (rig == null || handTransform == null) return;
            if (isGrabbed) return;

            // Tighter sphere for the actual grab — proximity is a wider hint zone.
            int mask = grabbableMask.value == 0 ? Physics.AllLayers : grabbableMask.value;
            int count = Physics.OverlapSphereNonAlloc(handTransform.position, grabRadius, hits, mask, QueryTriggerInteraction.Ignore);

            float bestDistSq = float.PositiveInfinity;
            Collider best = null;
            Vector3 bestPoint = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                var col = hits[i];
                if (col == null) continue;
                Vector3 cp = col.ClosestPoint(handTransform.position);
                float d = (cp - handTransform.position).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; best = col; bestPoint = cp; }
            }

            if (best == null) return;

            grabbedSurface = best.transform;
            grabbedLocalPos = best.transform.InverseTransformPoint(bestPoint);
            grabbedBody = best.attachedRigidbody;       // cache; null = static surface
            grabbedAsteroid = best.GetComponentInParent<Asteroid>();
            if (grabbedAsteroid != null) grabbedAsteroid.NotifyGrabbed();
            isGrabbed = true;

            // Reset the surface-tracking buffer for the new grab.
            surfaceDeltaBuffer.Clear();
            lastSurfaceWorldPos = grabbedSurface.TransformPoint(grabbedLocalPos);

            rig.RegisterGrabber(this);

            if (haptics != null) haptics.Pulse(grabHaptic, grabHapticDuration);
            AudioCues.PlayGrab();
            lastRumbleTime = Time.time;
            AnyGrabbed?.Invoke();
        }

        void OnGripUp(InputAction.CallbackContext ctx)
        {
            if (!isGrabbed) return;

            // Snapshot what we'll need before clearing state — once isGrabbed is
            // false the surface refs may be wiped by other code paths.
            Rigidbody asteroid = grabbedBody;
            Vector3 asteroidExitVelocity = EstimateSurfaceVelocity();

            if (grabbedAsteroid != null) grabbedAsteroid.NotifyReleased();
            grabbedAsteroid = null;
            isGrabbed = false;
            grabbedSurface = null;
            grabbedBody = null;
            surfaceDeltaBuffer.Clear();

            if (rig != null) rig.UnregisterGrabber(this);

            // Grant the asteroid the velocity it had while we were pulling on it.
            // Skipping kinematic bodies (drifting asteroids) since linearVelocity
            // is ignored on them — they'd just snap back to their drift target.
            if (asteroid != null && !asteroid.isKinematic)
                asteroid.linearVelocity = asteroidExitVelocity;

            if (haptics != null) haptics.Pulse(releaseHaptic, releaseHapticDuration);
            AudioCues.PlayRelease();
            if (grabIndicator != null) grabIndicator.gameObject.SetActive(false);
        }

        Vector3 EstimateSurfaceVelocity()
        {
            // Average per-step delta divided by fixedDeltaTime gives world velocity.
            // Returns zero if we have no samples (e.g. released same frame as grabbed).
            if (surfaceDeltaBuffer.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (var d in surfaceDeltaBuffer) sum += d;
            return (sum / surfaceDeltaBuffer.Count) / Time.fixedDeltaTime;
        }
    }
}
