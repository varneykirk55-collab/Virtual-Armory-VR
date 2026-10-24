using System;
using UnityEngine;

namespace SamuraiDuel
{
    public class CombatHealth : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;

        public float Max => maxHealth;
        public float Current { get; private set; }
        public float Normalized => maxHealth > 0f ? Current / maxHealth : 0f;
        public bool IsDead => Current <= 0f;

        /// <summary>(amount actually removed, remaining health)</summary>
        public event Action<float, float> Damaged;
        public event Action Died;
        public event Action Restored;

        private void Awake()
        {
            Current = maxHealth;
        }

        public void SetMax(float value)
        {
            maxHealth = Mathf.Max(1f, value);
            Current = maxHealth;
        }

        public float ApplyDamage(float amount)
        {
            if (IsDead || amount <= 0f)
                return 0f;

            float applied = Mathf.Min(amount, Current);
            Current -= applied;
            Damaged?.Invoke(applied, Current);
            if (IsDead)
                Died?.Invoke();
            return applied;
        }

        public void ResetHealth()
        {
            Current = maxHealth;
            Restored?.Invoke();
        }
    }
}
