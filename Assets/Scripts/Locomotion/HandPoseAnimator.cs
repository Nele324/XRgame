using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceClimb
{
    /// <summary>
    /// Drives the finger bones of a skinned hand mesh from the controller's grip
    /// and trigger inputs. Without this the XRI Starter Asset hand model is
    /// frozen in an open pose — the wrist tracks but the fingers don't, which
    /// reads as broken hand tracking. With it the hand closes to a fist on
    /// grip (so grabbing a handhold actually looks like a grab) and the index
    /// curls independently on trigger.
    ///
    /// Bones are auto-resolved by name (e.g. "L_IndexProximal"); just drop the
    /// component on Hand_L / Hand_R and set the matching <see cref="bonePrefix"/>.
    /// </summary>
    public class HandPoseAnimator : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] InputActionAsset actionsAsset;
        [SerializeField] string actionMapName = "XRI Left Interaction";
        [Tooltip("Action driving thumb/middle/ring/pinky curl. Use the analog " +
            "value action ('Select Value') so the curl is smooth, not binary.")]
        [SerializeField] string gripActionName = "Select Value";
        [Tooltip("Action driving the index finger curl. Typically 'Activate Value' (0-1 trigger).")]
        [SerializeField] string triggerActionName = "Activate Value";

        [Header("Bone discovery")]
        [Tooltip("Prefix on bone names. Hand_L uses 'L_', Hand_R uses 'R_'.")]
        [SerializeField] string bonePrefix = "L_";

        [Header("Curl pose")]
        [Tooltip("Local axis the joints rotate around to flex. Most humanoid " +
            "rigs use local X; flip the sign if the fingers bend the wrong way.")]
        [SerializeField] Vector3 curlAxis = new Vector3(1f, 0f, 0f);
        [Tooltip("Degrees of flexion at each joint when fully closed. Per-joint " +
            "tuning matters: the proximal hinge does ~70°, the middle (PIP) " +
            "joint does ~100°, the tip (DIP) ~70° — that's a tight realistic fist.")]
        [SerializeField] float fingerProximalAngle = 70f;
        [SerializeField] float fingerIntermediateAngle = 100f;
        [SerializeField] float fingerDistalAngle = 70f;
        [Tooltip("Thumb gets less flexion than the fingers — it tucks rather than " +
            "fully closes, even on a tight grip.")]
        [SerializeField] float thumbProximalAngle = 30f;
        [SerializeField] float thumbDistalAngle = 30f;

        [Header("Smoothing")]
        [Tooltip("Higher = snappier. 12-20 feels right for VR — fast enough to " +
            "match button intent, slow enough to avoid pop on instant releases.")]
        [SerializeField] float curlSpeed = 16f;

        // Per-finger joint chains, in order proximal → intermediate → distal.
        // Thumb has only proximal/distal — anatomy gives it one fewer phalanx.
        Transform[] indexJoints, middleJoints, ringJoints, littleJoints, thumbJoints;

        // Open-pose local rotations cached at Awake. These are the "rest" we
        // lerp away from when curling — capturing them at runtime means the
        // animator works regardless of how the rig was authored.
        Quaternion[] indexOpenRot, middleOpenRot, ringOpenRot, littleOpenRot, thumbOpenRot;

        InputAction gripAction;
        InputAction triggerAction;

        float gripCurl;
        float indexCurl;

        void Awake()
        {
            FindBones();
            CacheOpenPose();
            ResolveActions();
        }

        void OnEnable()
        {
            gripAction?.Enable();
            triggerAction?.Enable();
        }

        // OnDisable doesn't .Disable() — the actions live on a shared asset and
        // disabling them would break other consumers (grabber, locomotion, etc).

        void Update()
        {
            float targetGrip = ReadValue(gripAction);
            float targetTrigger = ReadValue(triggerAction);

            // Frame-rate-independent exponential smoothing. At curlSpeed=16 the
            // curl reaches ~63% of target in 1/16 s, ~95% in 3/16 s — feels
            // responsive without being instantaneous.
            float t = 1f - Mathf.Exp(-curlSpeed * Time.deltaTime);
            gripCurl = Mathf.Lerp(gripCurl, targetGrip, t);
            indexCurl = Mathf.Lerp(indexCurl, targetTrigger, t);

            // Index follows trigger; the others (and thumb) follow grip. This
            // is the natural mapping for VR controllers — the trigger is your
            // index finger, the grip surface is what your other three fingers
            // wrap around.
            ApplyFingerCurl(indexJoints, indexOpenRot, indexCurl);
            ApplyFingerCurl(middleJoints, middleOpenRot, gripCurl);
            ApplyFingerCurl(ringJoints, ringOpenRot, gripCurl);
            ApplyFingerCurl(littleJoints, littleOpenRot, gripCurl);
            ApplyThumbCurl(thumbJoints, thumbOpenRot, gripCurl);
        }

        static float ReadValue(InputAction action)
        {
            if (action == null) return 0f;
            // Try the analog read first; fall back to button-pressed for
            // boolean-bound actions (so we still curl on legacy bindings).
            try { return Mathf.Clamp01(action.ReadValue<float>()); }
            catch { return action.IsPressed() ? 1f : 0f; }
        }

        void ApplyFingerCurl(Transform[] joints, Quaternion[] openRot, float curl)
        {
            if (joints == null || openRot == null) return;
            float[] angles = { fingerProximalAngle, fingerIntermediateAngle, fingerDistalAngle };
            for (int i = 0; i < joints.Length && i < angles.Length; i++)
            {
                if (joints[i] == null) continue;
                Quaternion delta = Quaternion.AngleAxis(angles[i] * curl, curlAxis);
                joints[i].localRotation = openRot[i] * delta;
            }
        }

        void ApplyThumbCurl(Transform[] joints, Quaternion[] openRot, float curl)
        {
            if (joints == null || openRot == null) return;
            float[] angles = { thumbProximalAngle, thumbDistalAngle };
            for (int i = 0; i < joints.Length && i < angles.Length; i++)
            {
                if (joints[i] == null) continue;
                Quaternion delta = Quaternion.AngleAxis(angles[i] * curl, curlAxis);
                joints[i].localRotation = openRot[i] * delta;
            }
        }

        void FindBones()
        {
            indexJoints = FindFingerJoints("Index", new[] { "Proximal", "Intermediate", "Distal" });
            middleJoints = FindFingerJoints("Middle", new[] { "Proximal", "Intermediate", "Distal" });
            ringJoints = FindFingerJoints("Ring", new[] { "Proximal", "Intermediate", "Distal" });
            littleJoints = FindFingerJoints("Little", new[] { "Proximal", "Intermediate", "Distal" });
            thumbJoints = FindFingerJoints("Thumb", new[] { "Proximal", "Distal" });
        }

        Transform[] FindFingerJoints(string finger, string[] segments)
        {
            var result = new Transform[segments.Length];
            int found = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                string boneName = bonePrefix + finger + segments[i];
                result[i] = FindRecursively(transform, boneName);
                if (result[i] != null) found++;
            }
            if (found == 0)
                Debug.LogWarning(
                    $"HandPoseAnimator: no '{finger}' bones found with prefix '{bonePrefix}' under {name}.",
                    this);
            return result;
        }

        static Transform FindRecursively(Transform root, string targetName)
        {
            if (root.name == targetName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindRecursively(root.GetChild(i), targetName);
                if (found != null) return found;
            }
            return null;
        }

        void CacheOpenPose()
        {
            indexOpenRot = CacheRots(indexJoints);
            middleOpenRot = CacheRots(middleJoints);
            ringOpenRot = CacheRots(ringJoints);
            littleOpenRot = CacheRots(littleJoints);
            thumbOpenRot = CacheRots(thumbJoints);
        }

        static Quaternion[] CacheRots(Transform[] joints)
        {
            if (joints == null) return System.Array.Empty<Quaternion>();
            var arr = new Quaternion[joints.Length];
            for (int i = 0; i < joints.Length; i++)
                arr[i] = joints[i] != null ? joints[i].localRotation : Quaternion.identity;
            return arr;
        }

        void ResolveActions()
        {
            if (actionsAsset == null) return;
            var map = actionsAsset.FindActionMap(actionMapName);
            if (map == null)
            {
                Debug.LogWarning($"HandPoseAnimator: action map '{actionMapName}' not found.", this);
                return;
            }
            gripAction = map.FindAction(gripActionName);
            triggerAction = map.FindAction(triggerActionName);
            if (gripAction == null) Debug.LogWarning($"HandPoseAnimator: action '{gripActionName}' not found in '{actionMapName}'.", this);
            if (triggerAction == null) Debug.LogWarning($"HandPoseAnimator: action '{triggerActionName}' not found in '{actionMapName}'.", this);
        }
    }
}
