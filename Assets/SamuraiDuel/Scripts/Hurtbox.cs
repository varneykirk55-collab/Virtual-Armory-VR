using UnityEngine;

namespace SamuraiDuel
{
    /// <summary>
    /// A (usually trigger) collider that forwards blade hits to the ISwordHittable above it.
    /// Several hurtboxes can share one owner (head / torso / legs) with different multipliers.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hurtbox : MonoBehaviour
    {
        [SerializeField] private float damageMultiplier = 1f;
        [SerializeField] private MonoBehaviour ownerOverride;

        private ISwordHittable owner;

        public float DamageMultiplier => damageMultiplier;

        public ISwordHittable Owner
        {
            get
            {
                if (owner == null)
                    owner = ownerOverride as ISwordHittable ?? GetComponentInParent<ISwordHittable>();
                return owner;
            }
        }

        public void Configure(float multiplier, MonoBehaviour newOwner)
        {
            damageMultiplier = multiplier;
            ownerOverride = newOwner;
            owner = null;
        }
    }
}
