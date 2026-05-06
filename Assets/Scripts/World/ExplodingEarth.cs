using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Slow, organic emission pulse on the Earth model below the player.
    /// Visually communicates "this thing is hot and dangerous" — paired with
    /// the <see cref="HazardZone"/> trigger that actually kills the player.
    /// Uses Perlin noise instead of a sine wave so the pulse feels natural,
    /// not metronomic.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class ExplodingEarth : MonoBehaviour
    {
        [SerializeField] Renderer earthRenderer;
        [SerializeField] Color baseEmission = new(0.4f, 0.10f, 0.05f, 1f);
        [SerializeField] Color peakEmission = new(1.0f, 0.45f, 0.15f, 1f);
        [Tooltip("Speed of the Perlin pulse. Lower = slower, more menacing.")]
        [SerializeField] float pulseSpeed = 0.6f;
        [SerializeField] float emissionIntensity = 3.5f;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock mpb;

        void Awake()
        {
            if (earthRenderer == null) earthRenderer = GetComponent<Renderer>();
            mpb = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (earthRenderer == null) return;
            // PerlinNoise returns 0..1; we lerp the emission color between
            // base and peak then scale by the intensity multiplier.
            float t = Mathf.PerlinNoise(Time.time * pulseSpeed, 0f);
            Color emission = Color.Lerp(baseEmission, peakEmission, t) * emissionIntensity;
            earthRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionColor, emission);
            earthRenderer.SetPropertyBlock(mpb);
        }
    }
}
