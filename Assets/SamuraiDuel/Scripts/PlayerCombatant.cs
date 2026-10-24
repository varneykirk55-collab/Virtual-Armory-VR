using System;
using UnityEngine;
using UnityEngine.UI;

namespace SamuraiDuel
{
    /// <summary>
    /// The player's side of the duel: a head-and-torso hurtbox that follows the HMD, health,
    /// a short invulnerability window after each hit, both-hand haptics and a red screen flash.
    /// Lives on (or under) the existing XR Origin; nothing in the rig is replaced.
    /// </summary>
    [DefaultExecutionOrder(50)] // position the hurtbox before blades sweep (SwordHitDetector = 100)
    [RequireComponent(typeof(CombatHealth))]
    public class PlayerCombatant : MonoBehaviour, ISwordHittable
    {
        [SerializeField] private Transform head;
        [SerializeField] private CapsuleCollider hurtbox;
        [SerializeField] private float torsoLength = 0.75f;
        [SerializeField] private float invulnerableTime = 0.6f;
        [SerializeField] private Image damageFlash;
        [SerializeField] private float flashAlpha = 0.45f;

        private CombatHealth health;
        private float invulnerableUntil;
        private float flashLevel;

        public CombatTeam Team => CombatTeam.Player;
        public CombatHealth Health => health;
        /// <summary>The duel manager only lets the player be hurt while a fight is running.</summary>
        public bool Vulnerable { get; set; }

        public event Action<HitInfo> TookHit;

        public void Wire(Transform newHead, CapsuleCollider newHurtbox, Image flash)
        {
            head = newHead;
            hurtbox = newHurtbox;
            damageFlash = flash;
        }

        private void Awake()
        {
            health = GetComponent<CombatHealth>();
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
            SetFlash(0f);
        }

        private void LateUpdate()
        {
            if (head != null && hurtbox != null)
            {
                Vector3 flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                float height = torsoLength + hurtbox.radius * 2f;
                hurtbox.height = height;
                // Top of the capsule just above the eyes, slightly behind them (centre of the skull).
                Vector3 top = head.position + Vector3.up * 0.1f - flatForward * 0.06f;
                hurtbox.transform.SetPositionAndRotation(top - Vector3.up * (height * 0.5f), Quaternion.identity);
            }

            if (flashLevel > 0f)
            {
                flashLevel = Mathf.MoveTowards(flashLevel, 0f, Time.deltaTime * 2.5f);
                SetFlash(flashLevel);
            }
        }

        public HitResult ReceiveHit(ref HitInfo hit)
        {
            if (!Vulnerable || health.IsDead || hit.attackerTeam == Team || Time.time < invulnerableUntil)
                return HitResult.Ignored;

            invulnerableUntil = Time.time + invulnerableTime;
            health.ApplyDamage(hit.damage);
            XRHaptics.PulseAll(1f, 0.25f);
            flashLevel = 1f;
            SetFlash(flashLevel);
            TookHit?.Invoke(hit);
            return HitResult.Hit;
        }

        public void ResetPlayer()
        {
            health.ResetHealth();
            invulnerableUntil = 0f;
            flashLevel = 0f;
            SetFlash(0f);
        }

        private void SetFlash(float level)
        {
            if (damageFlash == null)
                return;
            Color c = damageFlash.color;
            c.a = level * flashAlpha;
            damageFlash.color = c;
            damageFlash.enabled = c.a > 0.001f;
        }
    }
}
