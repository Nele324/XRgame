using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Pulsing red emissive ring placed at the altitude where the
    /// <see cref="OutOfBoundsKiller"/> burn boundary fires. It's purely visual
    /// — the kill check still owns the actual game logic. Provides "you can
    /// see the danger before you fall into it" affordance.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class AtmosphereWarning : MonoBehaviour
    {
        [SerializeField] Renderer ringRenderer;
        [SerializeField] Color glowColor = new(1.4f, 0.35f, 0.10f, 1f);
        [SerializeField] float baseIntensity = 1.5f;
        [SerializeField] float peakIntensity = 4f;
        [SerializeField] float pulseSpeed = 0.6f;

        MaterialPropertyBlock mpb;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            if (ringRenderer == null) ringRenderer = GetComponent<Renderer>();
            mpb = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (ringRenderer == null) return;
            // Smooth sine pulse — Earth's "atmosphere boiling" feel.
            float t = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f);
            float intensity = Mathf.Lerp(baseIntensity, peakIntensity, t);
            ringRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionColor, glowColor * intensity);
            ringRenderer.SetPropertyBlock(mpb);
        }
    }
}
