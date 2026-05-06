using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Optional damage system. Currently DISABLED in scene — flip the component
    /// checkbox on PlayerRig to enable. Subscribes to <see cref="ZeroGRig.OnImpact"/>
    /// so any collision exceeding <see cref="minImpactForDamage"/> deducts HP.
    /// At zero HP, fires <see cref="GameStateController.Die"/> with Hazard cause.
    ///
    /// Designed so a future health-bar UI can hook the OnHealthChanged event
    /// without further changes here.
    /// </summary>
    public class HealthSystem : MonoBehaviour
    {
        [SerializeField] ZeroGRig rig;
        [SerializeField] float maxHealth = 100f;
        [Tooltip("HP per (m/s) above the impact threshold.")]
        [SerializeField] float damagePerImpactUnit = 6f;
        [Tooltip("Below this relative speed, impacts are bumps and don't damage.")]
        [SerializeField] float minImpactForDamage = 2f;

        public float Health { get; private set; }
        public float MaxHealth => maxHealth;
        public bool IsDead => Health <= 0f;

        /// <summary>Fired whenever Health changes. Args: (current, max).</summary>
        public System.Action<float, float> OnHealthChanged;

        void Awake()
        {
            Health = maxHealth;
            if (rig == null) rig = GetComponentInParent<ZeroGRig>();
            // Subscribe regardless of `enabled` — the field acts as a feature
            // flag at the *component* level, but this Awake runs only when
            // enabled is true (Awake doesn't fire for disabled components).
            if (rig != null) rig.OnImpact += HandleImpact;
        }

        void OnDestroy()
        {
            // Always unsubscribe — prevents the rig from holding a stale
            // delegate reference if HealthSystem is destroyed mid-scene.
            if (rig != null) rig.OnImpact -= HandleImpact;
        }

        void HandleImpact(float relativeSpeed)
        {
            if (relativeSpeed < minImpactForDamage) return;
            float dmg = (relativeSpeed - minImpactForDamage) * damagePerImpactUnit;
            ApplyDamage(dmg);
        }

        public void ApplyDamage(float amount)
        {
            if (amount <= 0f || IsDead) return;
            Health = Mathf.Max(0f, Health - amount);
            OnHealthChanged?.Invoke(Health, maxHealth);
            if (IsDead && GameStateController.Instance != null)
                GameStateController.Instance.Die(DeathCause.Hazard);
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || IsDead) return;
            Health = Mathf.Min(maxHealth, Health + amount);
            OnHealthChanged?.Invoke(Health, maxHealth);
        }
    }
}
