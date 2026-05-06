using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Generates synthesized AudioClips at runtime and assigns them to the
    /// <see cref="AudioCues"/> sibling. Intended as a zero-asset fallback so
    /// the game ships with usable SFX without requiring a sound pack. Any
    /// AudioClip the user assigns directly in the AudioCues inspector wins
    /// over the procedural fallback.
    ///
    /// Runs at execution order -100 so its Awake fires before AudioCues.Awake,
    /// which means the ambient loop is in place before AudioCues tries to
    /// play it.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class ProceduralAudioClips : MonoBehaviour
    {
        [SerializeField] AudioCues target;

        [Header("Synth")]
        [Tooltip("22050 Hz is plenty for short SFX; 44100 doubles memory but adds nothing audible for percussive synth tones.")]
        [SerializeField] int sampleRate = 22050;

        void Awake()
        {
            // Auto-resolve to a sibling AudioCues — keeps the scene setup
            // simple (drop both components on the same GameObject).
            if (target == null) target = GetComponent<AudioCues>();
            if (target == null) return;

            // Synth definitions tuned by ear. Keep these short and punchy:
            // long synthesized clips sound cheap; short ones read as "blip".
            AssignIfMissing("grabClip",    () => Synth(0.10f, 240f, 90f,  8f,  0.10f, WaveType.Sine));
            AssignIfMissing("releaseClip", () => Synth(0.07f, 140f, 60f,  14f, 0.05f, WaveType.Sine));
            AssignIfMissing("impactClip",  () => Synth(0.15f, 90f,  50f,  6f,  0.55f, WaveType.Square));
            AssignIfMissing("hazardClip",  () => Synth(0.45f, 720f, 180f, 2f,  0.20f, WaveType.Saw));
            AssignIfMissing("winClip",     () => SynthArpeggio(new []{ 523.25f, 659.25f, 783.99f, 1046.5f }, 0.13f));
            AssignIfMissing("deathClip",   () => Synth(0.6f,  220f, 35f,  1.6f, 0.10f, WaveType.Saw));
            AssignIfMissing("ambientLoop", () => SynthAmbient(8f));
        }

        // Reflection lets us write to the private serialized fields on
        // AudioCues without exposing setters. The fields stay private —
        // this is private-by-convention from another file in the same assembly.
        void AssignIfMissing(string fieldName, System.Func<AudioClip> factory)
        {
            var field = typeof(AudioCues).GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null) return;
            if (field.GetValue(target) != null) return;
            field.SetValue(target, factory());
        }

        enum WaveType { Sine, Square, Saw, Triangle }

        /// <summary>
        /// One-shot tone. <paramref name="freqStart"/> ramps to <paramref name="freqEnd"/>
        /// across the duration; <paramref name="decay"/> shapes an exponential
        /// envelope (higher = faster fade); <paramref name="noise"/> mixes in
        /// uniform noise (good for impacts and gritty hazards).
        /// </summary>
        AudioClip Synth(float duration, float freqStart, float freqEnd, float decay, float noise, WaveType type)
        {
            int samples = Mathf.CeilToInt(duration * sampleRate);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float n = (float)i / samples;          // 0..1 normalized progress
                float freq = Mathf.Lerp(freqStart, freqEnd, n);
                float env = Mathf.Exp(-decay * n);     // exponential decay envelope
                float phase = freq * t;
                // Different wave shapes give very different "characters":
                // sine = pure tone, square = chiptune punch, saw = harsh buzz, triangle = soft beep.
                float w = type switch
                {
                    WaveType.Sine => Mathf.Sin(phase * 2f * Mathf.PI),
                    WaveType.Square => Mathf.Sign(Mathf.Sin(phase * 2f * Mathf.PI)),
                    WaveType.Saw => 2f * (phase - Mathf.Floor(phase + 0.5f)),
                    WaveType.Triangle => 2f * Mathf.Abs(2f * (phase - Mathf.Floor(phase + 0.5f))) - 1f,
                    _ => 0f,
                };
                float ns = (Random.value * 2f - 1f) * noise;
                data[i] = (w * (1f - noise) + ns) * env;
            }
            var clip = AudioClip.Create("synth", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Tiny ascending-arpeggio jingle — used for the win cue.</summary>
        AudioClip SynthArpeggio(float[] freqs, float noteDuration)
        {
            int notes = freqs.Length;
            int samplesPerNote = Mathf.CeilToInt(noteDuration * sampleRate);
            int totalSamples = samplesPerNote * notes;
            var data = new float[totalSamples];
            for (int n = 0; n < notes; n++)
            {
                for (int i = 0; i < samplesPerNote; i++)
                {
                    float t = (float)i / sampleRate;
                    float frac = (float)i / samplesPerNote;
                    // Bell-curve envelope per note (Mathf.Sin(π·x) peaks at 0.5).
                    float env = Mathf.Sin(frac * Mathf.PI);
                    float w = Mathf.Sin(freqs[n] * t * 2f * Mathf.PI);
                    data[n * samplesPerNote + i] = w * env * 0.7f;
                }
            }
            var clip = AudioClip.Create("arp", totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// Ambient drone built from layered low-frequency sines with slow LFOs
        /// for movement. Crossfaded at the boundaries so it loops cleanly.
        /// </summary>
        AudioClip SynthAmbient(float duration)
        {
            int samples = Mathf.CeilToInt(duration * sampleRate);
            var data = new float[samples];
            float[] freqs = { 55f, 82.4f, 110f, 164.8f };  // A1, E2, A2, E3 — a stable harmonic stack
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float v = 0f;
                for (int k = 0; k < freqs.Length; k++)
                {
                    // Slow per-partial LFO so the drone breathes.
                    float lfo = 1f + 0.05f * Mathf.Sin(t * 0.3f * (k + 1));
                    v += Mathf.Sin(freqs[k] * lfo * t * 2f * Mathf.PI) * (0.18f - k * 0.03f);
                }
                v += (Random.value * 2f - 1f) * 0.04f;     // hint of noise for texture
                data[i] = v * 0.4f;
            }
            // Crossfade head and tail so the loop seam is inaudible.
            int fade = sampleRate / 4;
            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;
                data[i] *= k;
                data[samples - 1 - i] *= k;
            }
            var clip = AudioClip.Create("ambient", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
