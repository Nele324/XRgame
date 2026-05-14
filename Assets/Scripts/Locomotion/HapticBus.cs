using System.Collections.Generic;

namespace SpaceClimb
{
    /// <summary>
    /// Static fan-out for haptic pulses. Lets gameplay code fire feedback to
    /// every connected hand without holding a reference to each
    /// <see cref="HandedHaptics"/>. Implementations register/unregister
    /// themselves in OnEnable/OnDisable so the list stays consistent across
    /// scene loads.
    ///
    /// All callers are main-thread (Unity tools), so no synchronization
    /// needed.
    /// </summary>
    public static class HapticBus
    {
        // List instead of HashSet — O(n) Contains is fine since N is at most
        // 2 (left + right hand) and we want stable iteration order.
        static readonly List<HandedHaptics> players = new();

        /// <summary>
        /// Global multiplier applied to every haptic amplitude. Quest controller
        /// amplitudes are clamped to 1, so callers passing 0.3–0.7 benefit most;
        /// strong pulses (already at 1) stay capped. Bumped above 1 so the
        /// average rumble feels meatier without forcing every call site to
        /// re-tune its own amplitude.
        /// </summary>
        public static float Strength { get; set; } = 1.6f;

        public static void Register(HandedHaptics h)
        {
            if (h != null && !players.Contains(h)) players.Add(h);
        }

        public static void Unregister(HandedHaptics h)
        {
            players.Remove(h);
        }

        /// <summary>Pulse every registered hand simultaneously. The global Strength
        /// multiplier is applied inside HandedHaptics.Pulse, so callers pass the
        /// raw amplitude they want and don't have to know about Strength.</summary>
        public static void PulseAll(float amplitude, float duration)
        {
            // Defensive null check inside the loop — a HandedHaptics could be
            // destroyed mid-iteration in pathological cases (e.g. scene unload
            // during a coroutine that fires PulseAll).
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null) players[i].Pulse(amplitude, duration);
        }
    }
}
