using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace SamuraiDuel
{
    /// <summary>
    /// Marks a shield or blade as a blocking surface. When an opposing blade's sweep reaches one of this
    /// object's colliders before it reaches a hurtbox, the strike is blocked. If the blocker itself is
    /// moving fast at that moment (a swing into the incoming blade), it counts as a parry.
    /// </summary>
    public class Blocker : MonoBehaviour
    {
        [SerializeField] private CombatTeam team = CombatTeam.Player;
        [Tooltip("Speed (m/s) this blocker must be moving at to turn a block into a parry.")]
        [SerializeField] private float parrySpeed = 2.2f;
        [Tooltip("Only block while held. Leave off so a shield leaning on the table still stops a blade.")]
        [SerializeField] private bool requireHeld;
        [SerializeField] private float blockHapticAmplitude = 0.6f;
        [SerializeField] private float parryHapticAmplitude = 1f;

        private XRBaseInteractable interactable;
        private Vector3 lastPosition;
        private Vector3 smoothedVelocity;

        public CombatTeam Team => team;
        public float Speed => smoothedVelocity.magnitude;
        public bool IsHeld => interactable != null && interactable.isSelected;
        public bool CanBlock => isActiveAndEnabled && (!requireHeld || IsHeld);

        /// <summary>Raised after this blocker stopped a blade. isParry is already resolved.</summary>
        public event Action<HitInfo> Blocked;

        public void Configure(CombatTeam newTeam, bool blockOnlyWhenHeld)
        {
            team = newTeam;
            requireHeld = blockOnlyWhenHeld;
        }

        private void Awake()
        {
            interactable = GetComponentInParent<XRBaseInteractable>();
            lastPosition = transform.position;
        }

        private void OnEnable()
        {
            lastPosition = transform.position;
            smoothedVelocity = Vector3.zero;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            Vector3 position = transform.position;
            Vector3 velocity = (position - lastPosition) / dt;
            lastPosition = position;
            // Light smoothing so one jittery tracking frame does not register as a parry.
            smoothedVelocity = Vector3.Lerp(smoothedVelocity, velocity, 0.5f);
        }

        /// <summary>Called by the attacking SwordHitDetector. Returns true if it was a parry.</summary>
        public bool ResolveBlock(ref HitInfo hit)
        {
            hit.isParry = Speed >= parrySpeed;

            if (interactable != null)
                XRHaptics.PulseHolders(interactable, hit.isParry ? parryHapticAmplitude : blockHapticAmplitude,
                    hit.isParry ? 0.2f : 0.12f);

            Blocked?.Invoke(hit);
            return hit.isParry;
        }
    }
}
