# Samurai Duel – Architecture Notes & Change Plan

## 1. What exists today (audit)

| System | Where | How it actually works |
|---|---|---|
| XR rig | `SampleScene` → `XR Origin (XR Rig)` (tag `Player`) | XRI 2.6.3 action-based rig: `XROrigin`, `LocomotionSystem`, `ActionBasedContinuousMoveProvider` (1 m/s, gravity), `ActionBasedSnapTurnProvider` (30°), `CharacterControllerDriver` + `CharacterController`, `InputActionManager`. |
| Hands | `Left/Right hand controller` (tags `lehtHand` / `rightHand`) | `ActionBasedController` + `XRDirectInteractor` each; hand model prefab (`Assets/VR_Hands`) animated by `hand_animation.cs` (grip/trigger → Animator). **No ray interactor → world-space UI buttons cannot be pressed.** |
| Sword | 3× `SM_PolygonPrototype_Prop_Sword_01` instances | Plain `XRGrabInteractable`, Movement Type = **Velocity Tracking**, throw on detach, convex `MeshCollider`, `Rigidbody`. One copy tagged `Weapon`. **No swing detection, velocity buffer, damage or hit registration code exists.** |
| Shield | 2× `SM_Wep_Shield_04` | Plain `XRGrabInteractable` + collider. **Blocking is purely physical**; there is no block logic. |
| Training dummies | 2× `training_dummy` prefab + `trainingDummy.cs` | `OnCollisionEnter` with a `Weapon`-tagged object → Animator `Death` trigger. One-hit, no health. |
| Pressure plates | 2× floor tiles with trigger + `dummyTrigger.cs` | `OnTriggerEnter` from `Player` tag (the CharacterController) → `ActivateDummy()` on its targets. |
| Pistol | `SM_Wep_WaterPistol_01` + `fire.cs` | Spawns a bullet with velocity on activate. |
| Health / score / haptics / feedback | — | **None.** |
| Unused | `grabbing2hands.cs` | Two-hand attach-point grab; not referenced by the scene. |

Quest settings: Oculus XR loader enabled for Android, Quest 2 targeted, Multiview, Vulkan+GLES3, Linear colour.
**Gaps:** Mono backend + ARMv7 only (Quest needs ARM64/IL2CPP), physics fixed step 0.02 s (50 Hz, coarse for velocity-tracked swords on a 72/90 Hz headset).

## 2. Design

Everything new lives in `Assets/SamuraiDuel/`. No existing prefab, rig or interaction component is replaced.

```
 Player sword (existing XRGrabInteractable)          Samurai katana (new, kinematic)
   + SwordHitDetector (team Player)                     + SwordHitDetector (team Enemy, armed only during strike frames)
   + Blocker          (team Player)                     + Blocker          (team Enemy)
           │  per-frame swept sphere casts along N blade points (no tunnelling)
           ▼
   Blocker of other team hit?  ──yes──► BLOCK / CLASH (parry if the blocker is moving fast) → haptics + clang
           │ no
   Hurtbox of other team hit?  ──yes──► ISwordHittable.ReceiveHit(HitInfo) → Hit / Blocked / Ignored
                                          ├─ SamuraiOpponent (CombatHealth, hitstun / stagger / guard)
                                          ├─ PlayerCombatant (CombatHealth, haptics + red flash)
                                          └─ trainingDummy (existing; now also registers sweeps)
```

* **Hit registration**: `SwordHitDetector` samples 6 points from blade base to tip, keeps a short velocity buffer, and each frame sphere-casts every point from last frame's position to this frame's. Fast swings can't tunnel through, and a hit lands where the blade visibly passes. Damage scales with the speed of the point that connected (min ~1.6 m/s).
* **Haptics**: through the `XRBaseControllerInteractor` that is holding the item: hit, block, parry, and taking damage (both hands).
* **Samurai AI**: a small state machine with no navmesh or pathfinding: approach / keep spacing, telegraphed swings (wind-up → glowing hold → fast strike → recover), reactive guard, sidesteps, hitstun, heavy stagger on parry, anti-stunlock escape, and an enraged faster tempo under 50% HP. Built from primitives, so no new art is needed.
* **Duel loop**: `DuelManager` handles Pre-duel → Countdown → Fight → Victory/Defeat. Start or restart by stepping on the red plate (this reuses the existing `dummyTrigger` pressure-plate script) or pressing **A/X**. Holding A/X for 1 s restarts mid-fight. Swords and shields are returned to their original spots on restart. There's also score, a combo counter and a saved best score.

## 3. File plan

### New
| File | Purpose |
|---|---|
| `Scripts/CombatTypes.cs` | `CombatTeam`, `HitInfo`, `HitResult`, `ISwordHittable` |
| `Scripts/CombatHealth.cs` | HP + damaged/died/reset events |
| `Scripts/Hurtbox.cs` | Trigger collider → owner `ISwordHittable`, damage multiplier |
| `Scripts/Blocker.cs` | Marks shield / blade as a blocking surface, parry detection, haptics |
| `Scripts/SwordHitDetector.cs` | Swept blade hit registration + velocity buffer + haptics |
| `Scripts/XRHaptics.cs` | Helper for sending haptics to whoever holds an interactable / all controllers |
| `Scripts/DuelAudio.cs` | Procedurally generated clang / thud / whoosh / gong (no audio assets) with a small pooled AudioSource set |
| `Scripts/SamuraiOpponent.cs` | Opponent state machine + procedural animation |
| `Scripts/PlayerCombatant.cs` | Player hurtbox following the HMD, HP, damage flash |
| `Scripts/DuelManager.cs` | Duel states, score/combo, restart, weapon reset |
| `Scripts/DuelHUD.cs` | World-space HP bars / status / score UI (+ billboard) |
| `Scripts/DuelArenaBuilder.cs` | Builds the samurai rig, HUD and start plate (used by the editor menu) |
| `Editor/SamuraiDuelSetup.cs` | `Tools ▸ Samurai Duel ▸ …` menu: build the duel into the open scene, apply Quest 2 build settings |

### Modified (small, backwards compatible)
| File | Change |
|---|---|
| `Assets/dummyTrigger.cs` | Adds an optional `UnityEvent onPlayerEnter` and null-safe dummy activation. The duel start plate reuses this component. |
| `Assets/trainingDummy.cs` | Implements `ISwordHittable` so the dummies also react to swept sword hits. The existing collision path is kept. |
| `README.md` | Samurai Duel section + build steps |

`SampleScene.unity` is **not** hand-edited. The editor menu adds the duel objects (with Undo), so the 1.1 MB scene YAML is never at risk.
