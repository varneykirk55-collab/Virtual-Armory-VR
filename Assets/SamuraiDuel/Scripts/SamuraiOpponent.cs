using System;
using UnityEngine;

namespace SamuraiDuel
{
    /// <summary>
    /// Scripted one-on-one samurai. No navmesh or animation clips: it moves on the flat arena floor
    /// and poses its katana procedurally from a handful of authored blade directions.
    ///
    /// Loop: keep spacing → (attack | guard | sidestep). Attacks are telegraphed: wind-up into a
    /// readable pose, a glowing hold with an audio cue, a fast strike (the only frames the blade can
    /// hurt), then recovery. Hits cause hitstun; parries and heavy hits cause a long stagger.
    /// </summary>
    [RequireComponent(typeof(CombatHealth))]
    public class SamuraiOpponent : MonoBehaviour, ISwordHittable
    {
        public enum State
        {
            Inactive,
            Idle,
            Windup,
            Hold,
            Strike,
            Recover,
            Guard,
            Sidestep,
            Hitstun,
            Dead
        }

        [Serializable]
        public struct AttackPattern
        {
            public string name;
            [Tooltip("Blade direction (body space) at the end of the wind-up. Should read clearly as 'the cut comes from here'.")]
            public Vector3 windupDirection;
            [Tooltip("Blade direction (body space) at the end of the cut.")]
            public Vector3 strikeDirection;
        }

        [Header("Rig (filled by the setup tool)")]
        [SerializeField] private Transform body;
        [SerializeField] private Transform swordPivot;
        [SerializeField] private SwordHitDetector katana;
        [SerializeField] private Blocker katanaBlocker;
        [SerializeField] private Transform leftShoulder;
        [SerializeField] private Transform rightShoulder;
        [SerializeField] private Transform leftArm;
        [SerializeField] private Transform rightArm;
        [SerializeField] private Collider[] hurtboxColliders;
        [SerializeField] private Renderer[] flashRenderers;
        [SerializeField] private Material flashMaterial;
        [SerializeField] private Renderer bladeRenderer;
        [SerializeField] private Material telegraphMaterial;

        [Header("Arena")]
        [SerializeField] private Transform arenaCenter;
        [SerializeField] private float arenaRadius = 3.5f;

        [Header("Spacing")]
        [SerializeField] private float moveSpeed = 1.4f;
        [SerializeField] private float strafeSpeed = 0.45f;
        [SerializeField] private float attackRange = 1.45f;
        [SerializeField] private float tooClose = 0.9f;
        [SerializeField] private float turnSpeed = 300f;

        [Header("Attack timing (seconds)")]
        [SerializeField] private float windupTime = 0.55f;
        [SerializeField] private float holdTime = 0.3f;
        [SerializeField] private float strikeTime = 0.2f;
        [SerializeField] private float recoverTime = 0.55f;
        [SerializeField] private float deflectedRecoverTime = 0.8f;
        [SerializeField] private float strikeDamage = 20f;
        [SerializeField, Range(0f, 1f)] private float chainChance = 0.25f;
        [SerializeField] private AttackPattern[] attacks =
        {
            new AttackPattern { name = "Shomen (overhead)", windupDirection = new Vector3(0f, 1f, -0.5f), strikeDirection = new Vector3(0f, -0.55f, 1f) },
            new AttackPattern { name = "Kesa (right shoulder)", windupDirection = new Vector3(0.8f, 0.8f, -0.3f), strikeDirection = new Vector3(-0.7f, -0.5f, 0.8f) },
            new AttackPattern { name = "Gyaku-kesa (left shoulder)", windupDirection = new Vector3(-0.8f, 0.8f, -0.3f), strikeDirection = new Vector3(0.7f, -0.5f, 0.8f) },
            new AttackPattern { name = "Do (horizontal)", windupDirection = new Vector3(1f, 0.1f, -0.3f), strikeDirection = new Vector3(-0.8f, 0f, 0.8f) }
        };

        [Header("Decisions")]
        [SerializeField] private Vector2 decisionDelay = new Vector2(0.5f, 1.1f);
        [SerializeField, Range(0f, 1f)] private float attackWeight = 0.65f;
        [SerializeField, Range(0f, 1f)] private float guardWeight = 0.15f;
        [SerializeField, Range(0f, 1f)] private float reactiveGuardChance = 0.35f;
        [SerializeField] private float threatSpeed = 3f;
        [SerializeField] private Vector2 guardDuration = new Vector2(0.6f, 1.2f);
        [SerializeField, Range(0f, 1f)] private float counterChance = 0.45f;
        [SerializeField] private float sidestepDistance = 1.1f;
        [SerializeField] private float sidestepTime = 0.3f;

        [Header("Taking hits")]
        [SerializeField] private float hitstunTime = 0.4f;
        [SerializeField] private float staggerTime = 1.1f;
        [SerializeField] private float heavyHitDamage = 14f;
        [SerializeField] private float parryBladeSpeed = 2.5f;
        [Tooltip("This many hits inside the window triggers an escape instead of more hitstun.")]
        [SerializeField] private int stunlockHits = 3;
        [SerializeField] private float stunlockWindow = 2.5f;
        [SerializeField, Range(0f, 1f)] private float enrageThreshold = 0.5f;
        [SerializeField] private float enragedTempo = 0.8f;

        // Poses in body space. The katana's blade points along the pivot's +Z.
        private static readonly Vector3 GuardDirection = new Vector3(0f, 0.3f, 1f);
        private static readonly Vector3 BlockDirection = new Vector3(-1f, 0.3f, 0.35f);
        private static readonly Vector3 BlockGripOffset = new Vector3(0.12f, 0.22f, 0.05f);
        private static readonly Vector3 DeadDirection = new Vector3(0.2f, -1f, 0.3f);
        private const float GripReach = 0.22f;

        private CombatHealth health;
        private Transform player;
        private State state = State.Inactive;
        private float stateTime;
        private float stateDuration;

        private Quaternion swordRotation;
        private Quaternion poseFrom;
        private Quaternion poseTo;
        private Vector3 gripOffset;
        private Vector3 gripBase;
        private AttackPattern currentAttack;
        private float tempo = 1f;
        private bool chained;

        private Vector3 moveFrom;
        private Vector3 moveTo;
        private float strafeSign = 1f;
        private float nextStrafeFlip;
        private float nextDecision;
        private float nextThreatCheck;
        private bool attackAfterMove;

        private float lean;
        private float leanTarget;
        private int recentHits;
        private float recentHitsUntil;
        private float flashUntil;
        private Material[][] originalMaterials;
        private Material originalBladeMaterial;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;

        public CombatTeam Team => CombatTeam.Enemy;
        public State CurrentState => state;
        public CombatHealth Health => health;
        public bool IsEnraged => health != null && !health.IsDead && health.Normalized <= enrageThreshold;

        public event Action<HitInfo> TookHit;
        public event Action<HitInfo> BlockedPlayer;
        /// <summary>The player parried (or clashed through) one of our strikes.</summary>
        public event Action Parried;
        /// <summary>The player blocked one of our strikes without parrying it.</summary>
        public event Action StrikeDeflected;
        public event Action Defeated;

        private void Awake()
        {
            health = GetComponent<CombatHealth>();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            if (swordPivot != null)
                gripBase = swordPivot.localPosition;
            swordRotation = PoseRotation(GuardDirection);

            if (flashRenderers != null)
            {
                originalMaterials = new Material[flashRenderers.Length][];
                for (int i = 0; i < flashRenderers.Length; i++)
                    originalMaterials[i] = flashRenderers[i] != null ? flashRenderers[i].sharedMaterials : null;
            }
            if (bladeRenderer != null)
                originalBladeMaterial = bladeRenderer.sharedMaterial;
        }

        private void OnEnable()
        {
            if (katana != null)
            {
                katana.Armed = false;
                katana.Blocked += OnStrikeBlocked;
            }
            if (katanaBlocker != null)
                katanaBlocker.Blocked += OnKatanaStruck;
        }

        private void OnDisable()
        {
            if (katana != null)
                katana.Blocked -= OnStrikeBlocked;
            if (katanaBlocker != null)
                katanaBlocker.Blocked -= OnKatanaStruck;
        }

        // ---- public control ----------------------------------------------------------------------

        public void Activate()
        {
            if (state == State.Inactive)
                EnterIdle(0.6f);
        }

        public void Deactivate()
        {
            if (state != State.Dead)
                Enter(State.Inactive, 0f);
        }

        public void ResetOpponent()
        {
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            health.ResetHealth();
            recentHits = 0;
            lean = leanTarget = 0f;
            if (body != null)
                body.localRotation = Quaternion.identity;
            SetHurtboxesEnabled(true);
            if (katanaBlocker != null)
                katanaBlocker.enabled = true;
            if (katana != null)
            {
                katana.enabled = true;
                katana.Armed = false;
                katana.ResetSweep();
            }
            swordRotation = PoseRotation(GuardDirection);
            SetTelegraph(false);
            Enter(State.Inactive, 0f);
        }

        // ---- main loop ---------------------------------------------------------------------------

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            if (player == null && Camera.main != null)
                player = Camera.main.transform;

            stateTime += dt;
            tempo = IsEnraged ? enragedTempo : 1f;

            switch (state)
            {
                case State.Inactive: TickInactive(dt); break;
                case State.Idle: TickIdle(dt); break;
                case State.Windup: TickWindup(dt); break;
                case State.Hold: TickHold(dt); break;
                case State.Strike: TickStrike(); break;
                case State.Recover: TickRecover(dt); break;
                case State.Guard: TickGuard(dt); break;
                case State.Sidestep: TickSidestep(); break;
                case State.Hitstun: TickHitstun(); break;
                case State.Dead: TickDead(dt); break;
            }

            ClampToArena();
            ApplyPose(dt);
            UpdateFlash();
        }

        private void TickInactive(float dt)
        {
            FacePlayer(dt, turnSpeed * 0.5f);
            BlendPoseTowards(GuardDirection, Vector3.zero, dt, 6f);
        }

        private void TickIdle(float dt)
        {
            FacePlayer(dt, turnSpeed);
            BlendPoseTowards(GuardDirection, Vector3.zero, dt, 8f);

            float distance = FlatDistanceToPlayer(out Vector3 toPlayer);
            Vector3 right = Vector3.Cross(Vector3.up, toPlayer);

            // Hold a spacing just outside sword reach (so the guard blade never sits in the player's
            // face); the wind-up closes the last step.
            float near = attackRange + 0.1f;
            float far = attackRange + 0.35f;
            if (distance > far)
            {
                transform.position += toPlayer * (moveSpeed * dt);
            }
            else if (distance < near)
            {
                float retreat = distance < tooClose ? 0.8f : 0.4f;
                transform.position -= toPlayer * (moveSpeed * retreat * dt);
            }
            else
            {
                if (Time.time >= nextStrafeFlip)
                {
                    strafeSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                    nextStrafeFlip = Time.time + UnityEngine.Random.Range(0.8f, 1.8f);
                }
                transform.position += right * (strafeSign * strafeSpeed * dt);
            }

            if (Time.time >= nextThreatCheck)
            {
                nextThreatCheck = Time.time + 0.25f;
                if (PlayerIsSwinging() && UnityEngine.Random.value < reactiveGuardChance)
                {
                    EnterGuard();
                    return;
                }
            }

            if (Time.time < nextDecision || distance > far + 0.1f)
                return;

            float roll = UnityEngine.Random.value;
            if (roll < attackWeight)
                StartAttack(1f);
            else if (roll < attackWeight + guardWeight)
                EnterGuard();
            else
                StartSidestep(UnityEngine.Random.value < 0.5f, false, UnityEngine.Random.value < 0.5f);
        }

        private void TickWindup(float dt)
        {
            FacePlayer(dt, turnSpeed * 0.35f);
            if (FlatDistanceToPlayer(out Vector3 toPlayer) > attackRange)
                transform.position += toPlayer * (moveSpeed * dt);
            float t = Ease.OutCubic(Progress());
            swordRotation = Quaternion.Slerp(poseFrom, poseTo, t);
            gripOffset = Vector3.zero;
            if (stateTime >= stateDuration)
            {
                Enter(State.Hold, holdTime * tempo);
                DuelAudio.Play(DuelAudio.Sound.Cue, katana != null ? katana.TipPosition : transform.position, 0.7f);
            }
        }

        private void TickHold(float dt)
        {
            FacePlayer(dt, turnSpeed * 0.2f);
            // Small tremble so the raised blade reads as "about to fire".
            float shake = Mathf.Sin(stateTime * 70f) * 1.2f;
            swordRotation = poseTo * Quaternion.Euler(shake, 0f, 0f);
            if (stateTime >= stateDuration)
            {
                poseFrom = poseTo;
                poseTo = PoseRotation(currentAttack.strikeDirection);
                Enter(State.Strike, strikeTime);
                SetTelegraph(false);
                if (katana != null)
                {
                    katana.DisarmOnContact = true;
                    katana.Armed = true;
                }
                DuelAudio.Play(DuelAudio.Sound.Whoosh, transform.position + Vector3.up * 1.3f, 0.9f);
            }
        }

        private void TickStrike()
        {
            // Committed: no turning, so a sidestep during the hold dodges the cut.
            float t = Ease.InQuad(Progress());
            swordRotation = Quaternion.Slerp(poseFrom, poseTo, t);
            transform.position += transform.forward * (0.9f * Time.deltaTime); // step into the cut
            if (stateTime >= stateDuration)
                EndStrike(recoverTime);
        }

        private void EndStrike(float recoverDuration)
        {
            if (katana != null)
                katana.Armed = false;
            poseFrom = swordRotation;
            poseTo = PoseRotation(GuardDirection);
            Enter(State.Recover, recoverDuration * tempo);
        }

        private void TickRecover(float dt)
        {
            FacePlayer(dt, turnSpeed * 0.5f);
            swordRotation = Quaternion.Slerp(poseFrom, poseTo, Ease.InOutQuad(Progress()));
            if (stateTime < stateDuration)
                return;

            if (!chained && UnityEngine.Random.value < chainChance && FlatDistanceToPlayer(out _) < attackRange + 0.4f)
            {
                StartAttack(0.7f);
                chained = true;
            }
            else
            {
                chained = false;
                EnterIdle(UnityEngine.Random.Range(decisionDelay.x, decisionDelay.y));
            }
        }

        private void TickGuard(float dt)
        {
            FacePlayer(dt, turnSpeed * 0.8f);
            BlendPoseTowards(BlockDirection, BlockGripOffset, dt, 18f);
            if (stateTime >= stateDuration)
                EnterIdle(0.3f);
        }

        private void TickSidestep()
        {
            float t = Ease.InOutQuad(Progress());
            transform.position = Vector3.Lerp(moveFrom, moveTo, t);
            FacePlayer(Time.deltaTime, turnSpeed);
            BlendPoseTowards(GuardDirection, Vector3.zero, Time.deltaTime, 8f);
            if (stateTime < stateDuration)
                return;

            if (attackAfterMove && FlatDistanceToPlayer(out _) < attackRange + 0.5f)
                StartAttack(0.8f);
            else
                EnterIdle(UnityEngine.Random.Range(decisionDelay.x, decisionDelay.y) * 0.6f);
        }

        private void TickHitstun()
        {
            float t = Ease.OutCubic(Progress());
            transform.position = Vector3.Lerp(moveFrom, moveTo, t);
            BlendPoseTowards(GuardDirection + Vector3.up * 0.6f, Vector3.zero, Time.deltaTime, 4f);
            leanTarget = Mathf.Lerp(leanTarget, 0f, Time.deltaTime * 2f);
            if (stateTime < stateDuration)
                return;

            if (recentHits >= stunlockHits)
            {
                // Anti-stunlock: back off diagonally instead of eating a fourth free hit.
                recentHits = 0;
                StartSidestep(UnityEngine.Random.value < 0.5f, true, false);
            }
            else
            {
                EnterIdle(0.3f);
            }
        }

        private void TickDead(float dt)
        {
            leanTarget = -80f;
            BlendPoseTowards(DeadDirection, Vector3.zero, dt, 3f);
        }

        // ---- transitions -------------------------------------------------------------------------

        private void Enter(State next, float duration)
        {
            if (state == State.Strike && next != State.Strike && katana != null)
                katana.Armed = false;
            if (state == State.Windup || state == State.Hold)
                SetTelegraph(false);
            state = next;
            stateTime = 0f;
            stateDuration = Mathf.Max(0.01f, duration);
        }

        private void EnterIdle(float delay)
        {
            Enter(State.Idle, 0f);
            nextDecision = Time.time + delay * (IsEnraged ? 0.7f : 1f);
        }

        private void StartAttack(float speedScale)
        {
            if (attacks == null || attacks.Length == 0)
            {
                EnterIdle(1f);
                return;
            }

            currentAttack = attacks[UnityEngine.Random.Range(0, attacks.Length)];
            poseFrom = swordRotation;
            poseTo = PoseRotation(currentAttack.windupDirection);
            Enter(State.Windup, windupTime * tempo * speedScale);
            SetTelegraph(true);
        }

        private void EnterGuard()
        {
            Enter(State.Guard, UnityEngine.Random.Range(guardDuration.x, guardDuration.y));
        }

        private void StartSidestep(bool toRight, bool backwards, bool attackAfter)
        {
            FlatDistanceToPlayer(out Vector3 toPlayer);
            Vector3 right = Vector3.Cross(Vector3.up, toPlayer);
            Vector3 offset = right * (toRight ? 1f : -1f);
            if (backwards)
                offset = (offset - toPlayer * 1.2f).normalized;

            moveFrom = transform.position;
            moveTo = ClampPoint(moveFrom + offset * sidestepDistance);
            attackAfterMove = attackAfter;
            Enter(State.Sidestep, sidestepTime);
        }

        private void EnterHitstun(Vector3 attackerPosition, bool heavy)
        {
            Vector3 away = transform.position - attackerPosition;
            away.y = 0f;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;

            moveFrom = transform.position;
            moveTo = ClampPoint(moveFrom + away * (heavy ? 0.55f : 0.25f));
            leanTarget = heavy ? -28f : -12f;
            Enter(State.Hitstun, heavy ? staggerTime : hitstunTime);
        }

        private void Die()
        {
            Enter(State.Dead, 0f);
            SetHurtboxesEnabled(false);
            if (katana != null)
            {
                katana.Armed = false;
                katana.enabled = false;
            }
            if (katanaBlocker != null)
                katanaBlocker.enabled = false;
            DuelAudio.Play(DuelAudio.Sound.Gong, transform.position + Vector3.up, 0.8f);
            Defeated?.Invoke();
        }

        // ---- combat callbacks ----------------------------------------------------------------------

        public HitResult ReceiveHit(ref HitInfo hit)
        {
            if (state == State.Dead || hit.attackerTeam == Team)
                return HitResult.Ignored;

            // Before the duel starts the samurai simply parries everything (feedback, no damage).
            if (state == State.Inactive)
            {
                Flash(bladeRenderer);
                return HitResult.Blocked;
            }

            if (state == State.Guard && IsFrontal(hit.AttackerPosition))
            {
                Flash(bladeRenderer);
                BlockedPlayer?.Invoke(hit);
                if (UnityEngine.Random.value < counterChance)
                    StartAttack(0.6f);
                return HitResult.Blocked;
            }

            health.ApplyDamage(hit.damage);
            Flash(null);
            TookHit?.Invoke(hit);

            if (health.IsDead)
            {
                Die();
                return HitResult.Hit;
            }

            if (Time.time > recentHitsUntil)
                recentHits = 0;
            recentHits++;
            recentHitsUntil = Time.time + stunlockWindow;

            // Hyper armour on active frames: trading blows is allowed, but the cut still lands.
            if (state != State.Strike)
                EnterHitstun(hit.AttackerPosition, hit.damage >= heavyHitDamage);
            return HitResult.Hit;
        }

        /// <summary>Our strike was stopped by the player's shield or blade.</summary>
        private void OnStrikeBlocked(HitInfo hit)
        {
            if (state != State.Strike)
                return;

            if (hit.isParry)
            {
                Parried?.Invoke();
                EnterHitstun(transform.position + transform.forward, true);
            }
            else
            {
                // Deflected: the blade bounces back up and recovery is slower — a safe opening.
                StrikeDeflected?.Invoke();
                swordRotation = Quaternion.Slerp(swordRotation, PoseRotation(GuardDirection + Vector3.up), 0.6f);
                EndStrike(deflectedRecoverTime);
            }
        }

        /// <summary>The player's blade hit our katana.</summary>
        private void OnKatanaStruck(HitInfo hit)
        {
            if (state == State.Strike && hit.speed >= parryBladeSpeed)
            {
                Parried?.Invoke();
                EnterHitstun(hit.AttackerPosition, true);
            }
        }

        // ---- helpers -----------------------------------------------------------------------------

        private bool PlayerIsSwinging()
        {
            Vector3 chest = transform.position + Vector3.up * 1.2f;
            var blades = SwordHitDetector.All;
            for (int i = 0; i < blades.Count; i++)
            {
                SwordHitDetector blade = blades[i];
                if (blade.Team == CombatTeam.Player && blade.IsHeld && blade.TipSpeed > threatSpeed &&
                    (blade.TipPosition - chest).sqrMagnitude < 1.9f * 1.9f)
                    return true;
            }
            return false;
        }

        private bool IsFrontal(Vector3 attackerPosition)
        {
            Vector3 to = attackerPosition - transform.position;
            to.y = 0f;
            return to.sqrMagnitude < 0.0001f || Vector3.Angle(transform.forward, to) < 80f;
        }

        private float FlatDistanceToPlayer(out Vector3 direction)
        {
            if (player == null)
            {
                direction = transform.forward;
                return float.MaxValue;
            }
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            direction = distance > 0.0001f ? to / distance : transform.forward;
            return distance;
        }

        private void FacePlayer(float dt, float degreesPerSecond)
        {
            if (player == null)
                return;
            FlatDistanceToPlayer(out Vector3 direction);
            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, degreesPerSecond * dt);
        }

        private void ClampToArena()
        {
            transform.position = ClampPoint(transform.position);
        }

        private Vector3 ClampPoint(Vector3 point)
        {
            if (arenaCenter == null)
                return point;
            Vector3 center = arenaCenter.position;
            Vector3 offset = point - center;
            offset.y = 0f;
            if (offset.magnitude > arenaRadius)
                offset = offset.normalized * arenaRadius;
            return new Vector3(center.x + offset.x, center.y, center.z + offset.z);
        }

        private float Progress() => Mathf.Clamp01(stateTime / stateDuration);

        private static Quaternion PoseRotation(Vector3 bladeDirection)
        {
            Vector3 dir = bladeDirection.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.back : Vector3.up;
            return Quaternion.LookRotation(dir, up);
        }

        private void BlendPoseTowards(Vector3 direction, Vector3 grip, float dt, float sharpness)
        {
            float k = 1f - Mathf.Exp(-sharpness * dt);
            swordRotation = Quaternion.Slerp(swordRotation, PoseRotation(direction), k);
            gripOffset = Vector3.Lerp(gripOffset, grip, k);
        }

        private void ApplyPose(float dt)
        {
            if (body != null)
            {
                lean = Mathf.Lerp(lean, leanTarget, 1f - Mathf.Exp(-10f * dt));
                if (state != State.Hitstun && state != State.Dead)
                    leanTarget = Mathf.Lerp(leanTarget, 0f, 1f - Mathf.Exp(-6f * dt));
                body.localRotation = Quaternion.Euler(lean, 0f, 0f);
            }

            if (swordPivot == null)
                return;

            Vector3 bladeDirection = swordRotation * Vector3.forward;
            swordPivot.localRotation = swordRotation;
            swordPivot.localPosition = gripBase + bladeDirection * GripReach + gripOffset;

            UpdateArm(rightShoulder, rightArm, swordPivot.position + swordPivot.forward * 0.07f);
            UpdateArm(leftShoulder, leftArm, swordPivot.position - swordPivot.forward * 0.07f);
        }

        private static void UpdateArm(Transform shoulder, Transform arm, Vector3 hand)
        {
            if (shoulder == null || arm == null)
                return;
            Vector3 from = shoulder.position;
            Vector3 span = hand - from;
            float length = span.magnitude;
            if (length < 0.001f)
                return;
            arm.position = from + span * 0.5f;
            arm.rotation = Quaternion.FromToRotation(Vector3.up, span / length);
            Vector3 scale = arm.localScale;
            // Unity's capsule primitive is 2 units tall; compensate for any parent scale.
            float parentScale = arm.parent != null ? arm.parent.lossyScale.y : 1f;
            arm.localScale = new Vector3(scale.x, length * 0.5f / Mathf.Max(0.0001f, parentScale), scale.z);
        }

        private void SetHurtboxesEnabled(bool enabled)
        {
            if (hurtboxColliders == null)
                return;
            foreach (var col in hurtboxColliders)
                if (col != null)
                    col.enabled = enabled;
        }

        private void SetTelegraph(bool on)
        {
            if (bladeRenderer == null || telegraphMaterial == null)
                return;
            bladeRenderer.sharedMaterial = on ? telegraphMaterial : originalBladeMaterial;
        }

        private void Flash(Renderer only)
        {
            if (flashMaterial == null)
                return;
            flashUntil = Time.time + 0.08f;
            if (only != null)
            {
                only.sharedMaterial = flashMaterial;
                return;
            }
            if (flashRenderers == null)
                return;
            foreach (var r in flashRenderers)
            {
                if (r == null)
                    continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = flashMaterial;
                r.sharedMaterials = mats;
            }
        }

        private void UpdateFlash()
        {
            if (flashUntil <= 0f || Time.time < flashUntil)
                return;
            flashUntil = 0f;
            if (flashRenderers != null && originalMaterials != null)
            {
                for (int i = 0; i < flashRenderers.Length; i++)
                    if (flashRenderers[i] != null && originalMaterials[i] != null)
                        flashRenderers[i].sharedMaterials = originalMaterials[i];
            }
            bool telegraphing = state == State.Windup || state == State.Hold;
            SetTelegraph(telegraphing);
            if (!telegraphing && bladeRenderer != null && originalBladeMaterial != null)
                bladeRenderer.sharedMaterial = originalBladeMaterial;
        }

        private static class Ease
        {
            public static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
            public static float InQuad(float t) => t * t;
            public static float InOutQuad(float t) => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
        }

        /// <summary>Used by the builder so the rig can be wired without reflection.</summary>
        public void Wire(Transform newBody, Transform pivot, SwordHitDetector blade, Blocker blocker,
            Transform lShoulder, Transform rShoulder, Transform lArm, Transform rArm, Collider[] hurtboxes,
            Renderer[] flashTargets, Material flashMat, Renderer blade3D, Material telegraphMat, Transform center, float radius)
        {
            body = newBody;
            swordPivot = pivot;
            katana = blade;
            katanaBlocker = blocker;
            leftShoulder = lShoulder;
            rightShoulder = rShoulder;
            leftArm = lArm;
            rightArm = rArm;
            hurtboxColliders = hurtboxes;
            flashRenderers = flashTargets;
            flashMaterial = flashMat;
            bladeRenderer = blade3D;
            telegraphMaterial = telegraphMat;
            arenaCenter = center;
            arenaRadius = radius;
            katana.Configure(CombatTeam.Enemy, false, blade.transform.Find("BladeBase"), blade.transform.Find("BladeTip"), strikeDamage);
        }
    }
}
