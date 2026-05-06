using System.Collections;
using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Plays a short "pulled into the docking port" sequence when the goal
    /// zone fires. Translates the rig into the target point over a short
    /// duration while ramping the docking ring's emission to peak. Notably
    /// does NOT rotate the rig — forced rotation is the #1 cause of motion
    /// sickness in VR. Player keeps full head freedom throughout.
    /// </summary>
    public class WinAnimation : MonoBehaviour
    {
        [SerializeField] ZeroGRig rig;
        [SerializeField] Transform dockingTarget;          // where to glide the player to
        [SerializeField] Renderer dockingRingRenderer;     // emission gets boosted during animation
        [SerializeField] float duration = 1.6f;
        [SerializeField] AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] Color peakEmission = new(0.5f, 0.85f, 1.4f, 1f);
        [SerializeField] float peakEmissionIntensity = 6f;

        MaterialPropertyBlock mpb;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        bool playing;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
        }

        /// <summary>Returns a coroutine that the GameStateController can yield on before showing the win panel.</summary>
        public IEnumerator Play()
        {
            if (playing || rig == null || dockingTarget == null) yield break;
            playing = true;

            // Snapshot current state.
            Vector3 startPos = rig.transform.position;
            Vector3 endPos = dockingTarget.position;

            // Lock physics so the lerp can drive position deterministically
            // without the FixedUpdate body correction fighting us.
            rig.FreezePhysics(true);

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float n = Mathf.Clamp01(t / duration);
                float k = ease.Evaluate(n);
                rig.transform.position = Vector3.Lerp(startPos, endPos, k);

                // Ramp the docking ring's glow as we approach.
                if (dockingRingRenderer != null)
                {
                    dockingRingRenderer.GetPropertyBlock(mpb);
                    mpb.SetColor(EmissionColor, peakEmission * (peakEmissionIntensity * k));
                    dockingRingRenderer.SetPropertyBlock(mpb);
                }
                yield return null;
            }

            // Snap to exact end position to avoid drift from accumulated unscaled delta.
            rig.transform.position = endPos;
            playing = false;
        }
    }
}
