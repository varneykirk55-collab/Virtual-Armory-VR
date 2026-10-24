using UnityEngine;

namespace SamuraiDuel
{
    public enum CombatTeam
    {
        Player,
        Enemy,
        Neutral
    }

    public enum HitResult
    {
        Ignored,
        Hit,
        Blocked
    }

    /// <summary>Everything a receiver needs to know about one blade contact.</summary>
    public struct HitInfo
    {
        public SwordHitDetector source;
        public CombatTeam attackerTeam;
        public Vector3 point;
        /// <summary>World-space velocity of the blade point that connected.</summary>
        public Vector3 velocity;
        public float speed;
        public float damage;
        public Collider collider;
        /// <summary>True when the receiving blocker was moving fast enough to count as a parry.</summary>
        public bool isParry;

        public Vector3 Direction => speed > 0.0001f ? velocity / speed : Vector3.zero;
        public Vector3 AttackerPosition => source != null ? source.transform.position : point;
    }

    /// <summary>Anything a sword can strike: the samurai, the player, the existing training dummies.</summary>
    public interface ISwordHittable
    {
        CombatTeam Team { get; }
        HitResult ReceiveHit(ref HitInfo hit);
    }
}
