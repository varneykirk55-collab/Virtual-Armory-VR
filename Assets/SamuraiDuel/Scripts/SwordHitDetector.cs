using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace SamuraiDuel
{
    /// <summary>
    /// Swept blade hit registration shared by the player's swords and the samurai's katana.
    ///
    /// Every frame, N points spaced from blade base to tip are sphere-cast from where they were last
    /// frame to where they are now. A fast swing therefore cannot tunnel through a target between
    /// physics steps, and a hit is registered exactly where the blade visibly passes.
    /// If an opposing Blocker (shield / blade) is crossed in the same frame as a hurtbox, the
    /// block wins.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the samurai has posed its arm in Update
    public class SwordHitDetector : MonoBehaviour
    {
        private const int MaxSamples = 10;
        private const int VelocityBufferSize = 5;

        [Header("Ownership")]
        [SerializeField] private CombatTeam team = CombatTeam.Player;
        [Tooltip("Only strike while held by an XR interactor (player weapons).")]
        [SerializeField] private bool requireHeld = true;

        [Header("Blade geometry (auto-estimated from the mesh if left empty)")]
        [SerializeField] private Transform bladeBase;
        [SerializeField] private Transform bladeTip;
        [SerializeField, Range(2, MaxSamples)] private int samples = 6;
        [SerializeField] private float castRadius = 0.025f;
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Damage")]
        [Tooltip("Blade point speed (m/s) below which contacts are ignored. Stops resting/pushing from counting.")]
        [SerializeField] private float minHitSpeed = 1.6f;
        [SerializeField] private float fullDamageSpeed = 6f;
        [SerializeField] private float minDamage = 4f;
        [SerializeField] private float maxDamage = 18f;
        [Tooltip("If > 0, every hit does this much damage and the speed gate is skipped (AI blades).")]
        [SerializeField] private float fixedDamage;
        [Tooltip("Seconds before the same target can be hit again by this blade.")]
        [SerializeField] private float retargetCooldown = 0.35f;

        [Header("Haptics (held weapons only)")]
        [SerializeField] private float hitHapticAmplitude = 0.75f;
        [SerializeField] private float blockHapticAmplitude = 0.5f;

        private static readonly List<SwordHitDetector> all = new List<SwordHitDetector>();
        private static readonly Dictionary<Collider, Hurtbox> hurtboxCache = new Dictionary<Collider, Hurtbox>();
        private static readonly Dictionary<Collider, Blocker> blockerCache = new Dictionary<Collider, Blocker>();
        private static readonly RaycastHit[] castBuffer = new RaycastHit[24];

        private readonly Vector3[] previous = new Vector3[MaxSamples];
        private readonly Vector3[] current = new Vector3[MaxSamples];
        private readonly Vector3[] tipHistory = new Vector3[VelocityBufferSize];
        private readonly float[] tipTimes = new float[VelocityBufferSize];
        private readonly Dictionary<int, float> cooldowns = new Dictionary<int, float>();
        private int tipHead;
        private int tipCount;
        private bool hasPrevious;
        private bool armed = true;
        private XRBaseInteractable interactable;

        public static IReadOnlyList<SwordHitDetector> All => all;

        public CombatTeam Team => team;
        public bool IsHeld => interactable != null && interactable.isSelected;
        public XRBaseInteractable Interactable => interactable;
        public Vector3 TipPosition => bladeTip != null ? bladeTip.position : transform.position;
        public Vector3 BasePosition => bladeBase != null ? bladeBase.position : transform.position;
        /// <summary>Tip velocity averaged over the last few frames.</summary>
        public Vector3 TipVelocity { get; private set; }
        public float TipSpeed => TipVelocity.magnitude;

        /// <summary>When false, the blade is tracked but never strikes (AI uses this for active frames).</summary>
        public bool Armed
        {
            get => armed;
            set
            {
                if (value && !armed)
                    cooldowns.Clear();
                armed = value;
            }
        }

        /// <summary>Disarm automatically after the first hit or block (one hit per AI swing).</summary>
        public bool DisarmOnContact { get; set; }

        public event Action<HitInfo> Hit;
        public event Action<HitInfo> Blocked;

        public void Configure(CombatTeam newTeam, bool needsHolding, Transform newBase, Transform newTip, float damageOverride)
        {
            team = newTeam;
            requireHeld = needsHolding;
            bladeBase = newBase;
            bladeTip = newTip;
            fixedDamage = damageOverride;
            ResetSweep();
        }

        /// <summary>Forget last frame's positions (call after teleporting the blade).</summary>
        public void ResetSweep()
        {
            hasPrevious = false;
            tipCount = 0;
            TipVelocity = Vector3.zero;
        }

        private void Awake()
        {
            interactable = GetComponentInParent<XRBaseInteractable>();
            if (bladeBase == null || bladeTip == null)
                CreateBladeMarkers();
        }

        private void OnEnable()
        {
            all.Add(this);
            ResetSweep();
            if (interactable != null)
            {
                interactable.selectEntered.AddListener(OnGrabChanged);
                interactable.selectExited.AddListener(OnGrabChanged);
            }
        }

        private void OnDisable()
        {
            all.Remove(this);
            if (interactable != null)
            {
                interactable.selectEntered.RemoveListener(OnGrabChanged);
                interactable.selectExited.RemoveListener(OnGrabChanged);
            }
        }

        private void OnGrabChanged(BaseInteractionEventArgs _) => ResetSweep();

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            int count = Mathf.Clamp(samples, 2, MaxSamples);
            Vector3 basePos = BasePosition;
            Vector3 tipPos = TipPosition;
            for (int i = 0; i < count; i++)
                current[i] = Vector3.Lerp(basePos, tipPos, i / (float)(count - 1));

            RecordTip(tipPos);

            bool canStrike = armed && (!requireHeld || IsHeld);
            if (hasPrevious && canStrike)
                Sweep(count, dt);

            Array.Copy(current, previous, count);
            hasPrevious = true;
        }

        private void RecordTip(Vector3 tipPos)
        {
            float now = Time.time;
            tipHistory[tipHead] = tipPos;
            tipTimes[tipHead] = now;
            tipHead = (tipHead + 1) % VelocityBufferSize;
            tipCount = Mathf.Min(tipCount + 1, VelocityBufferSize);
            if (tipCount < 2)
                return;

            int oldest = (tipHead - tipCount + VelocityBufferSize) % VelocityBufferSize;
            float span = now - tipTimes[oldest];
            TipVelocity = span > 0.0001f ? (tipPos - tipHistory[oldest]) / span : Vector3.zero;
        }

        private void Sweep(int count, float dt)
        {
            Blocker bestBlocker = null;
            Hurtbox bestHurtbox = null;
            HitInfo blockHit = default;
            HitInfo hurtHit = default;
            float bestHurtSpeed = 0f;

            for (int i = 0; i < count; i++)
            {
                Vector3 from = previous[i];
                Vector3 delta = current[i] - from;
                float distance = delta.magnitude;
                if (distance < 0.0005f)
                    continue;

                float speed = distance / dt;
                if (fixedDamage <= 0f && speed < minHitSpeed)
                    continue;

                Vector3 direction = delta / distance;
                int hits = Physics.SphereCastNonAlloc(from, castRadius, direction, castBuffer, distance,
                    hitMask, QueryTriggerInteraction.Collide);

                float nearest = float.MaxValue;
                Blocker segmentBlocker = null;
                Hurtbox segmentHurtbox = null;
                Collider segmentCollider = null;
                Vector3 segmentPoint = current[i];

                for (int h = 0; h < hits; h++)
                {
                    RaycastHit rh = castBuffer[h];
                    if (rh.distance >= nearest || IsOwnCollider(rh.collider))
                        continue;

                    Blocker blocker = GetBlocker(rh.collider);
                    Hurtbox hurtbox = blocker == null ? GetHurtbox(rh.collider) : null;
                    if (blocker != null && (blocker.Team == team || !blocker.CanBlock))
                        continue;
                    if (blocker == null && (hurtbox == null || hurtbox.Owner == null || hurtbox.Owner.Team == team))
                        continue;

                    nearest = rh.distance;
                    segmentBlocker = blocker;
                    segmentHurtbox = hurtbox;
                    segmentCollider = rh.collider;
                    // Overlap at sweep start reports a zero point.
                    segmentPoint = rh.point == Vector3.zero && rh.distance == 0f ? from : rh.point;
                }

                if (segmentBlocker != null && bestBlocker == null)
                {
                    bestBlocker = segmentBlocker;
                    blockHit = MakeHit(segmentPoint, direction * speed, speed, segmentCollider);
                }
                else if (segmentHurtbox != null && speed > bestHurtSpeed)
                {
                    bestHurtbox = segmentHurtbox;
                    bestHurtSpeed = speed;
                    hurtHit = MakeHit(segmentPoint, direction * speed, speed, segmentCollider);
                }
            }

            if (bestBlocker != null)
                HandleBlocker(bestBlocker, blockHit);
            else if (bestHurtbox != null)
                HandleHurtbox(bestHurtbox, hurtHit);
        }

        private HitInfo MakeHit(Vector3 point, Vector3 velocity, float speed, Collider col)
        {
            return new HitInfo
            {
                source = this,
                attackerTeam = team,
                point = point,
                velocity = velocity,
                speed = speed,
                collider = col,
                damage = ComputeDamage(speed)
            };
        }

        private float ComputeDamage(float speed)
        {
            if (fixedDamage > 0f)
                return fixedDamage;
            float t = Mathf.InverseLerp(minHitSpeed, fullDamageSpeed, speed);
            return Mathf.Lerp(minDamage, maxDamage, t);
        }

        private void HandleBlocker(Blocker blocker, HitInfo hit)
        {
            if (!TryConsumeCooldown(blocker.GetInstanceID()))
                return;

            blocker.ResolveBlock(ref hit);
            DuelAudio.Play(DuelAudio.Sound.Clang, hit.point, hit.isParry ? 1f : 0.75f);
            if (interactable != null)
                XRHaptics.PulseHolders(interactable, blockHapticAmplitude, 0.12f);
            Blocked?.Invoke(hit);
            if (DisarmOnContact)
                armed = false;
        }

        private void HandleHurtbox(Hurtbox hurtbox, HitInfo hit)
        {
            ISwordHittable owner = hurtbox.Owner;
            int key = owner is UnityEngine.Object unityOwner ? unityOwner.GetInstanceID() : hurtbox.GetInstanceID();
            if (!TryConsumeCooldown(key))
                return;

            hit.damage *= hurtbox.DamageMultiplier;
            HitResult result = owner.ReceiveHit(ref hit);
            switch (result)
            {
                case HitResult.Hit:
                    DuelAudio.Play(DuelAudio.Sound.Flesh, hit.point, Mathf.Lerp(0.5f, 1f, hit.damage / Mathf.Max(1f, maxDamage)));
                    if (interactable != null)
                        XRHaptics.PulseHolders(interactable, hitHapticAmplitude * Mathf.Lerp(0.6f, 1f,
                            Mathf.InverseLerp(minHitSpeed, fullDamageSpeed, hit.speed)), 0.1f);
                    Hit?.Invoke(hit);
                    break;
                case HitResult.Blocked:
                    DuelAudio.Play(DuelAudio.Sound.Clang, hit.point, 0.8f);
                    if (interactable != null)
                        XRHaptics.PulseHolders(interactable, blockHapticAmplitude, 0.12f);
                    Blocked?.Invoke(hit);
                    break;
                default:
                    return;
            }

            if (DisarmOnContact)
                armed = false;
        }

        private bool TryConsumeCooldown(int key)
        {
            float now = Time.time;
            if (cooldowns.TryGetValue(key, out float readyAt) && now < readyAt)
                return false;
            cooldowns[key] = now + retargetCooldown;
            return true;
        }

        private bool IsOwnCollider(Collider col)
        {
            Transform root = interactable != null ? interactable.transform : transform;
            return col.transform == root || col.transform.IsChildOf(root);
        }

        private static Hurtbox GetHurtbox(Collider col)
        {
            if (!hurtboxCache.TryGetValue(col, out Hurtbox hurtbox))
            {
                hurtbox = col.GetComponent<Hurtbox>();
                hurtboxCache[col] = hurtbox;
            }
            return hurtbox;
        }

        private static Blocker GetBlocker(Collider col)
        {
            if (!blockerCache.TryGetValue(col, out Blocker blocker))
            {
                blocker = col.GetComponentInParent<Blocker>();
                // A blocker only owns colliders on its own object or below it, never a parent's hurtboxes.
                if (blocker != null && !col.transform.IsChildOf(blocker.transform))
                    blocker = null;
                blockerCache[col] = blocker;
            }
            return blocker;
        }

        private void CreateBladeMarkers()
        {
            Transform handleHint = (interactable as XRGrabInteractable)?.attachTransform;
            if (!EstimateBlade(transform, handleHint, out Vector3 baseLocal, out Vector3 tipLocal))
            {
                baseLocal = Vector3.zero;
                tipLocal = Vector3.forward * 0.8f;
            }

            if (bladeBase == null)
                bladeBase = CreateMarker("BladeBase", baseLocal);
            if (bladeTip == null)
                bladeTip = CreateMarker("BladeTip", tipLocal);
        }

        private Transform CreateMarker(string markerName, Vector3 localPosition)
        {
            var marker = new GameObject(markerName).transform;
            marker.SetParent(transform, false);
            marker.localPosition = localPosition;
            return marker;
        }

        /// <summary>
        /// Guesses blade base/tip from the combined mesh bounds: the longest axis is the blade, and the
        /// end farther from the handle hint (attach point, or the pivot) is the tip.
        /// </summary>
        public static bool EstimateBlade(Transform root, Transform handleHint, out Vector3 baseLocal, out Vector3 tipLocal)
        {
            baseLocal = tipLocal = Vector3.zero;
            var filters = root.GetComponentsInChildren<MeshFilter>();
            bool hasBounds = false;
            Bounds bounds = default;
            Matrix4x4 toRoot = root.worldToLocalMatrix;

            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null)
                    continue;
                Bounds mb = filter.sharedMesh.bounds;
                Matrix4x4 m = toRoot * filter.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!hasBounds) { bounds = new Bounds(p, Vector3.zero); hasBounds = true; }
                    else bounds.Encapsulate(p);
                }
            }

            if (!hasBounds)
                return false;

            Vector3 size = bounds.size;
            Vector3 axis = size.x >= size.y && size.x >= size.z ? Vector3.right
                : size.y >= size.z ? Vector3.up : Vector3.forward;
            float halfLength = Vector3.Dot(bounds.extents, axis);
            Vector3 endA = bounds.center - axis * halfLength;
            Vector3 endB = bounds.center + axis * halfLength;

            Vector3 handle = handleHint != null ? root.InverseTransformPoint(handleHint.position) : Vector3.zero;
            bool bIsTip = (endB - handle).sqrMagnitude >= (endA - handle).sqrMagnitude;
            Vector3 tip = bIsTip ? endB : endA;
            Vector3 pommel = bIsTip ? endA : endB;

            // The guard sits roughly a quarter of the way up from the pommel on most swords.
            baseLocal = Vector3.Lerp(pommel, tip, 0.25f);
            tipLocal = tip;
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (bladeBase == null || bladeTip == null)
                return;
            Gizmos.color = team == CombatTeam.Player ? Color.cyan : Color.red;
            Gizmos.DrawLine(bladeBase.position, bladeTip.position);
            Gizmos.DrawWireSphere(bladeTip.position, castRadius);
            Gizmos.DrawWireSphere(bladeBase.position, castRadius);
        }
    }
}
