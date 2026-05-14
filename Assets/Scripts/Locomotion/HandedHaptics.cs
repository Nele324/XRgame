using UnityEngine;
using UnityEngine.XR;

namespace SpaceClimb
{
    public enum Handedness { Left, Right }

    /// <summary>
    /// Direct-to-device haptics for one hand. Bypasses XRI's
    /// <c>HapticImpulsePlayer</c> entirely — that component's input-action
    /// reference can become stale across XRI versions, which silently breaks
    /// haptics. Resolving the device by XRNode is rock-solid: it asks the OS
    /// "which controller is currently the left/right hand?" and forwards the
    /// pulse directly.
    ///
    /// Self-registers with <see cref="HapticBus"/> on enable, so global cues
    /// (impacts, drift warnings) can pulse all hands at once without a
    /// dependency graph.
    /// </summary>
    public class HandedHaptics : MonoBehaviour
    {
        [SerializeField] Handedness hand = Handedness.Right;
        [Tooltip("Per-hand attenuation. 0 = no haptics, 1 = full intensity. Useful if you want one hand subtler than the other.")]
        [Range(0f, 1f)][SerializeField] float amplitudeScale = 1f;

        InputDevice device;
        bool deviceValid;

        void OnEnable()
        {
            // Devices can hot-plug (especially in Quest with controller sleep);
            // we re-resolve on every connect/disconnect event.
            InputDevices.deviceConnected += OnDeviceConnected;
            InputDevices.deviceDisconnected += OnDeviceDisconnected;
            ResolveDevice();
            HapticBus.Register(this);
        }

        void OnDisable()
        {
            InputDevices.deviceConnected -= OnDeviceConnected;
            InputDevices.deviceDisconnected -= OnDeviceDisconnected;
            HapticBus.Unregister(this);
        }

        void OnDeviceConnected(InputDevice _) => ResolveDevice();
        void OnDeviceDisconnected(InputDevice _) => ResolveDevice();

        void ResolveDevice()
        {
            // GetDeviceAtXRNode returns an "invalid" device struct if the hand
            // isn't tracked yet — Pulse() handles that case below.
            XRNode node = hand == Handedness.Left ? XRNode.LeftHand : XRNode.RightHand;
            device = InputDevices.GetDeviceAtXRNode(node);
            deviceValid = device.isValid;
        }

        /// <summary>
        /// Fire a haptic impulse on this hand. Safe to call any time — drops
        /// silently if the device isn't tracked or doesn't support haptics.
        /// </summary>
        public void Pulse(float amplitude, float duration)
        {
            if (amplitude <= 0f || duration <= 0f) return;
            if (!deviceValid)
            {
                // Try once more in case the device just connected; some headsets
                // don't fire deviceConnected immediately for the first frame.
                ResolveDevice();
                if (!deviceValid) return;
            }
            // TryGetHapticCapabilities is cheap and the only way to know if the
            // controller supports impulses (some hand-tracking-only devices don't).
            if (!device.TryGetHapticCapabilities(out var caps) || !caps.supportsImpulse) return;
            // Apply per-hand and global strength multipliers. Quest clamps to 1
            // internally; we clamp here too so very small inputs don't get lost
            // to subnormal float behavior.
            float final = Mathf.Clamp01(amplitude * amplitudeScale * HapticBus.Strength);
            device.SendHapticImpulse(0u, final, duration);
        }
    }
}
