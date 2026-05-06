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

        public static void Register(HandedHaptics h)
        {
            if (h != null && !players.Contains(h)) players.Add(h);
        }

        public static void Unregister(HandedHaptics h)
        {
            players.Remove(h);
        }

        /// <summary>Pulse every registered hand simultaneously.</summary>
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
