using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SamuraiDuel
{
    /// <summary>
    /// Duel flow: PreDuel → Countdown → Fighting → Victory / Defeat → (restart).
    /// Start/restart: step on the start plate (existing dummyTrigger pressure-plate script) or press A/X.
    /// Holding A/X for a second restarts mid-fight. Also tracks score, combo and best score.
    /// </summary>
    public class DuelManager : MonoBehaviour
    {
        public enum Phase
        {
            PreDuel,
            Countdown,
            Fighting,
            Victory,
            Defeat
        }

        [SerializeField] private SamuraiOpponent samurai;
        [SerializeField] private PlayerCombatant player;
        [SerializeField] private DuelHUD hud;
        [SerializeField] private float countdownSeconds = 3f;
        [SerializeField] private float comboWindow = 2.5f;
        [SerializeField] private float holdToRestartSeconds = 1f;
        [Tooltip("Return dropped / thrown player weapons to where they started on each restart.")]
        [SerializeField] private bool resetWeaponsOnRestart = true;

        private const string BestScoreKey = "SamuraiDuel.BestScore";

        private Phase phase = Phase.PreDuel;
        private float phaseTime;
        private int score;
        private int bestScore;
        private int combo;
        private float lastHitTime = -10f;
        private float restartHeld;
        private InputAction restartAction;
        private readonly List<WeaponHome> weaponHomes = new List<WeaponHome>();

        private struct WeaponHome
        {
            public Rigidbody body;
            public Vector3 position;
            public Quaternion rotation;
            public UnityEngine.XR.Interaction.Toolkit.XRBaseInteractable interactable;
        }

        public Phase CurrentPhase => phase;

        public void Wire(SamuraiOpponent newSamurai, PlayerCombatant newPlayer, DuelHUD newHud)
        {
            samurai = newSamurai;
            player = newPlayer;
            hud = newHud;
        }

        private void Awake()
        {
            bestScore = PlayerPrefs.GetInt(BestScoreKey, 0);

            restartAction = new InputAction("Duel Restart", InputActionType.Button);
            restartAction.AddBinding("<XRController>{RightHand}/primaryButton"); // A
            restartAction.AddBinding("<XRController>{LeftHand}/primaryButton");  // X
            restartAction.AddBinding("<Keyboard>/r");                            // editor / device simulator
        }

        private void OnEnable()
        {
            restartAction.Enable();
        }

        private void OnDisable()
        {
            restartAction.Disable();
        }

        private void Start()
        {
            // Subscribed here rather than OnEnable: every Awake (which creates the Health refs) has run.
            if (samurai != null)
            {
                samurai.TookHit += OnSamuraiHit;
                samurai.Parried += OnParry;
                samurai.StrikeDeflected += OnPlayerBlocked;
                samurai.BlockedPlayer += OnSamuraiBlocked;
                samurai.Defeated += OnSamuraiDefeated;
            }
            if (player != null)
            {
                player.TookHit += OnPlayerHit;
                player.Health.Died += OnPlayerDied;
            }

            RecordWeaponHomes();
            EnterPreDuel();
        }

        private void OnDestroy()
        {
            restartAction?.Dispose();
            if (samurai != null)
            {
                samurai.TookHit -= OnSamuraiHit;
                samurai.Parried -= OnParry;
                samurai.StrikeDeflected -= OnPlayerBlocked;
                samurai.BlockedPlayer -= OnSamuraiBlocked;
                samurai.Defeated -= OnSamuraiDefeated;
            }
            if (player != null)
            {
                player.TookHit -= OnPlayerHit;
                if (player.Health != null)
                    player.Health.Died -= OnPlayerDied;
            }
        }

        private void Update()
        {
            phaseTime += Time.deltaTime;
            HandleRestartInput();

            if (phase == Phase.Countdown)
            {
                int remaining = Mathf.CeilToInt(countdownSeconds - phaseTime);
                if (remaining > 0)
                {
                    if (hud != null) hud.SetStatus(remaining.ToString());
                }
                else
                {
                    BeginFight();
                }
            }
            else if (phase == Phase.Fighting && combo > 0 && Time.time - lastHitTime > comboWindow)
            {
                combo = 0;
                if (hud != null) hud.SetCombo(0);
            }

            if (hud != null)
            {
                if (samurai != null)
                    hud.SetSamuraiHealth(samurai.Health.Normalized);
                if (player != null)
                    hud.SetPlayerHealth(player.Health.Normalized);
            }
        }

        /// <summary>Hooked to the start plate's onPlayerEnter event.</summary>
        public void RequestStart()
        {
            if (phase == Phase.PreDuel || phase == Phase.Victory || phase == Phase.Defeat)
                StartCountdown();
        }

        private void HandleRestartInput()
        {
            if (phase == Phase.Fighting || phase == Phase.Countdown)
            {
                if (restartAction.IsPressed())
                {
                    restartHeld += Time.deltaTime;
                    if (restartHeld >= holdToRestartSeconds)
                    {
                        restartHeld = 0f;
                        StartCountdown();
                    }
                }
                else
                {
                    restartHeld = 0f;
                }
            }
            else if (restartAction.WasPressedThisFrame())
            {
                StartCountdown();
            }
        }

        // ---- phases ------------------------------------------------------------------------------

        private void EnterPreDuel()
        {
            SetPhase(Phase.PreDuel);
            if (player != null)
                player.Vulnerable = false;
            if (hud != null) hud.SetStatus("Pick up a sword (and shield)\nStep on the red plate or press A to duel");
            if (hud != null) hud.SetScore(0, bestScore);
            if (hud != null) hud.SetCombo(0);
        }

        private void StartCountdown()
        {
            SetPhase(Phase.Countdown);
            score = 0;
            combo = 0;
            if (resetWeaponsOnRestart)
                ResetWeapons();
            if (samurai != null) samurai.ResetOpponent();
            if (player != null)
            {
                player.ResetPlayer();
                player.Vulnerable = false;
            }
            if (hud != null) hud.SetScore(score, bestScore);
            if (hud != null) hud.SetCombo(0);
        }

        private void BeginFight()
        {
            SetPhase(Phase.Fighting);
            if (player != null)
                player.Vulnerable = true;
            if (samurai != null) samurai.Activate();
            if (hud != null) hud.SetStatus("Defeat the samurai\nHold A for 1s to restart");
            if (hud != null) hud.Callout("HAJIME!", new Color(1f, 0.85f, 0.3f), 1.2f);
            DuelAudio.Play(DuelAudio.Sound.Gong, samurai != null ? samurai.transform.position : transform.position, 0.8f);
        }

        private void OnSamuraiDefeated()
        {
            if (phase != Phase.Fighting)
                return;
            SetPhase(Phase.Victory);
            if (player != null)
            {
                player.Vulnerable = false;
                score += Mathf.RoundToInt(player.Health.Current * 5f);
            }
            score += Mathf.Max(0, 1000 - Mathf.RoundToInt(TimeSinceFightStart() * 10f));
            SaveBest();
            if (hud != null) hud.SetScore(score, bestScore);
            if (hud != null) hud.SetStatus("VICTORY\nPress A or step on the plate to duel again");
            if (hud != null) hud.Callout("VICTORY", new Color(1f, 0.85f, 0.3f), 3f);
        }

        private void OnPlayerDied()
        {
            if (phase != Phase.Fighting)
                return;
            SetPhase(Phase.Defeat);
            if (samurai != null) samurai.Deactivate();
            SaveBest();
            if (hud != null) hud.SetScore(score, bestScore);
            if (hud != null) hud.SetStatus("DEFEATED\nPress A or step on the plate to try again");
            if (hud != null) hud.Callout("DEFEATED", new Color(0.9f, 0.2f, 0.2f), 3f);
            DuelAudio.Play(DuelAudio.Sound.Gong, samurai != null ? samurai.transform.position : transform.position, 0.6f, 0.7f);
        }

        // ---- scoring -----------------------------------------------------------------------------

        private void OnSamuraiHit(HitInfo hit)
        {
            if (phase != Phase.Fighting)
                return;
            combo = Time.time - lastHitTime <= comboWindow ? combo + 1 : 1;
            lastHitTime = Time.time;
            float multiplier = 1f + 0.25f * (combo - 1);
            score += Mathf.RoundToInt(hit.damage * 10f * multiplier);
            if (hud != null) hud.SetScore(score, bestScore);
            if (hud != null) hud.SetCombo(combo);
        }

        private void OnParry()
        {
            if (phase != Phase.Fighting)
                return;
            score += 150;
            if (hud != null) hud.SetScore(score, bestScore);
            if (hud != null) hud.Callout("PARRY!", new Color(0.4f, 0.9f, 1f));
        }

        private void OnPlayerBlocked()
        {
            if (phase != Phase.Fighting)
                return;
            score += 50;
            if (hud != null) hud.SetScore(score, bestScore);
            if (hud != null) hud.Callout("Blocked", Color.white, 0.6f);
        }

        private void OnSamuraiBlocked(HitInfo hit)
        {
            if (phase == Phase.Fighting)
                if (hud != null) hud.Callout("Guarded", new Color(1f, 0.6f, 0.4f), 0.5f);
        }

        private void OnPlayerHit(HitInfo hit)
        {
            combo = 0;
            if (hud != null) hud.SetCombo(0);
        }

        // ---- helpers -----------------------------------------------------------------------------

        private float fightStartTime;

        private float TimeSinceFightStart() => Time.time - fightStartTime;

        private void SetPhase(Phase next)
        {
            phase = next;
            phaseTime = 0f;
            restartHeld = 0f;
            if (next == Phase.Fighting)
                fightStartTime = Time.time;
        }

        private void SaveBest()
        {
            if (score <= bestScore)
                return;
            bestScore = score;
            PlayerPrefs.SetInt(BestScoreKey, bestScore);
            PlayerPrefs.Save();
        }

        private void RecordWeaponHomes()
        {
            weaponHomes.Clear();
            var seen = new HashSet<Rigidbody>();
            foreach (var blade in FindObjectsOfType<SwordHitDetector>())
                if (blade.Team == CombatTeam.Player)
                    AddHome(blade.GetComponentInParent<Rigidbody>(), seen);
            foreach (var blocker in FindObjectsOfType<Blocker>())
                if (blocker.Team == CombatTeam.Player)
                    AddHome(blocker.GetComponentInParent<Rigidbody>(), seen);
        }

        private void AddHome(Rigidbody body, HashSet<Rigidbody> seen)
        {
            if (body == null || !seen.Add(body))
                return;
            weaponHomes.Add(new WeaponHome
            {
                body = body,
                position = body.position,
                rotation = body.rotation,
                interactable = body.GetComponent<UnityEngine.XR.Interaction.Toolkit.XRBaseInteractable>()
            });
        }

        private void ResetWeapons()
        {
            foreach (var home in weaponHomes)
            {
                if (home.body == null || (home.interactable != null && home.interactable.isSelected))
                    continue;
                home.body.velocity = Vector3.zero;
                home.body.angularVelocity = Vector3.zero;
                home.body.transform.SetPositionAndRotation(home.position, home.rotation);
                home.body.position = home.position;
                home.body.rotation = home.rotation;
                foreach (var blade in home.body.GetComponentsInChildren<SwordHitDetector>())
                    blade.ResetSweep();
            }
        }
    }
}
