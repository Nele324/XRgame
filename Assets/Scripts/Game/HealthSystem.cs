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
        [Tooltip("Flat damage per qualifying impact. Default 12.5 means the " +
            "player can soak 8 hard hits before dying at the default 100 HP.")]
        [SerializeField] float damagePerHit = 12.5f;
        [Tooltip("Below this relative speed (m/s), impacts are bumps and don't " +
            "damage. 4 m/s is roughly half the rig's max drift cap — soft " +
            "brushes pass through, committed slams cost HP.")]
        [SerializeField] float minImpactForDamage = 4f;
        [Tooltip("Seconds between damaging hits. Without a cooldown, a single " +
            "physical impact often produces two or three OnCollisionEnter " +
            "events as the rig bounces and re-contacts before separating, " +
            "eating multiple HP per visual hit.")]
        [SerializeField] float hitCooldown = 0.5f;

        float lastHitTime = -999f;

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
            if (Time.time < lastHitTime + hitCooldown) return;
            lastHitTime = Time.time;
            ApplyDamage(damagePerHit);
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
