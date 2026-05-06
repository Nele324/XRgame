using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Cheap per-renderer blinking emission, used for the station's nav lights
    /// and docking-port pulse. Drives the renderer via MaterialPropertyBlock
    /// so multiple lights can share a single material asset without breaking
    /// SRP batching.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class BlinkingLight : MonoBehaviour
    {
        [SerializeField] Color emissionColor = Color.red;
        [SerializeField] float emissionIntensity = 4f;
        [Tooltip("Period in seconds for one full on→off→on cycle.")]
        [SerializeField] float blinkInterval = 1.0f;
        [Tooltip("Fraction of the interval the light is fully on. The remaining time it's at the dim baseline.")]
        [SerializeField] float blinkDuration = 0.18f;
        [Tooltip("Phase offset in seconds. Use to desync paired lights (e.g. red & green nav lights).")]
        [SerializeField] float phaseOffset;
        [Tooltip("Baseline emission while 'off'. 0 = fully dark; ~0.05 keeps a faint coal glow.")]
        [Range(0f, 0.5f)][SerializeField] float offIntensity = 0.05f;

        Renderer rend;
        MaterialPropertyBlock mpb;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            rend = GetComponent<Renderer>();
            mpb = new MaterialPropertyBlock();
        }

        void Update()
        {
            // Time within the current cycle; on for the first blinkDuration, dim afterwards.
            float cycle = Mathf.Repeat(Time.time + phaseOffset, blinkInterval);
            float on = cycle < blinkDuration ? 1f : offIntensity;
            rend.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionColor, emissionColor * emissionIntensity * on);
            rend.SetPropertyBlock(mpb);
        }
    }
}
