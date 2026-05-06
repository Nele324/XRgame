using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Trigger volume placed inside the space station's docking ring. Touching
    /// it with the player rig fires the win flow. The collider should be
    /// generously sized (~1.5m radius) so a player approaching the dock at any
    /// angle reliably triggers it.
    /// </summary>
    public class GoalZone : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            if (GameStateController.Instance == null) return;
            var rb = other.attachedRigidbody;
            if (rb == null) return;
            if (!rb.TryGetComponent<ZeroGRig>(out _)) return;
            GameStateController.Instance.Win();
        }
    }
}
