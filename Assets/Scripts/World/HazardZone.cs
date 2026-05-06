using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Trigger volume that kills the player on entry. Used for the Earth
    /// "atmosphere burn" and any other geometric danger zones. Per-asteroid
    /// hazard contact is handled inside <see cref="Asteroid"/> instead, since
    /// asteroids need solid (non-trigger) colliders for grabbing.
    /// </summary>
    public class HazardZone : MonoBehaviour
    {
        [SerializeField] DeathCause cause = DeathCause.Hazard;

        void OnTriggerEnter(Collider other)
        {
            if (GameStateController.Instance == null) return;
            // attachedRigidbody walks the parent chain — so the player rig's
            // capsule collider will return the rig's Rigidbody here.
            var rb = other.attachedRigidbody;
            if (rb == null) return;
            // TryGetComponent avoids the array allocation that GetComponent does
            // when it has to search; same semantics, less GC.
            if (!rb.TryGetComponent<ZeroGRig>(out _)) return;
            GameStateController.Instance.Die(cause);
        }
    }
}
