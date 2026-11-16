# Combat game feel (melee + ranged, presentation only)

Baseline: `New-Testing` (working tree; unrelated user asset modifications preserved). No HacknPlan item: this tracker defines scope (user decision, 2026-11-14).
Presentation only; strict TDD; Unity CLI connected Editor (6000.6.8f1) for EditMode/PlayMode. Commits and pushes need explicit authorization (none granted yet).

## Specs

- S0 Scope: "presentation only. No changes to damage, release timing, collision, cooldowns, knockback or any Game Design value." Out of scope: "Client-side prediction of attack start or projectiles." and "New art and audio assets. All new asset fields are optional wiring."
- S1 (T1) "Generalize the melee feedback tag to slot, sequence, weapon catalog index and attack type." "Carry it on the projectile spawn request and store it as `[Networked]` projectile state." "Forward `IsFatal` in the feedback event and RPC." "When no tag exists, degrade to generic feedback without logs."
- S2 (T2) "Tagged ranged hits use the confirmed weapon's optional `HitReaction` and `ImpactParticles`." "Attacker-clock hitstop stays melee-only (`WeaponHitstopTests` keeps ranged = 0)." "Update the \"melee only\" tooltips and the catalog test that forces ranged `HitReaction == null`."
- S3 (T3) "Swing and Shoot play at release, on the shared presentation clock, and are not replayed on resume." "The hit sound comes from the event's confirmed weapon." "Reload is derived from the real reload state." "Remove the global-search fallback."
- S4 (T4) "Optional per-weapon deal-shake override, falling back to `CameraShakeConfig`." "A kill multiplier." "An optional directional kick along `HitDirection`, respecting the accessibility scale and the task-286 aim decoupling."
- S5 (T5) "An expiry event when there was no impact, excluding runner shutdown, with an optional fizzle prefab." "A trail detach component on despawn." "Remove the `[CombatTrace]` logs."
- S6 (T6) "Between acceptance and release, the weapon pose and release VFX use the committed attack direction."
- S7 (T7) "An aim-stance draw-complete presentation event derived from replicated state, with an optional sound and pulse." "A kill style for damage numbers through serialized fields."
- S8 (T8) "`PlayerCombatArchitecture.md` (confirmed feedback, hitstop, hit reaction, ranged presentation) and `GameFeelParticlesArchitecture.md`, which currently excludes ranged."
- S9 (T9, user) Manual: assign optional assets (incl. tuning `CameraShakeConfig` directional kick strength, default 0 = off); Host and Client checks; "Host Migration with projectiles in flight: feedback degrades to generic without errors."
- S10 Acceptance criteria (verbatim):
  - "A confirmed projectile hit from a player weapon reaches the attacker carrying the weapon tag and `IsFatal`. It plays the reaction and particles when the weapon has those profiles assigned."
  - "Melee hitstop and target freeze behave exactly as before."
  - "Swing and Shoot play at release, not at acceptance."
  - "The hit sound matches the event's weapon."
  - "Deal-shake varies per weapon and on kill, and the kick is directional."
  - "Projectile expiry raises a presentation event distinct from impact."
  - "During the ranged windup, the VFX and weapon pose use the committed direction."
  - "Clean compile. The combat EditMode and PlayMode suites pass. New tests were observed failing before implementation."
  - "No unintended `.meta`, prefab or generated-asset changes."

## Tasks

- [x] T1 S1 — weapon attack tag for every impact — gentle-ai-worker mwhz61y0-2-3n43 (with T2) — commit: none (uncommitted)
- [x] T2 S2 — unified impact presentation — gentle-ai-worker mwhz61y0-2-3n43 (with T1) — commit: none (uncommitted)
- [x] T3 S3 — audio at release / confirmed weapon / reload state — gentle-ai-worker mwi024us-3-b4dr — commit: none (uncommitted)
- [x] T4 S4 — weighted directional deal-shake — gentle-ai-worker mwi0uq79-4-md42 — commit: none (uncommitted)
- [x] T5 S5 — projectile expiry, trail detach, trace log removal — gentle-ai-worker mwi1cg2k-5-xmgh + parent test fix + validation mwi1q69s-6-piki — commit: none (uncommitted)
- [x] T6 S6 — ranged windup committed direction — gentle-ai-worker mwi20n6a-7-pn72; reopened by verify (validator regression), fixed by mwi3rp4a-b-rdn9 / mwi3vrmr-c-rtan — commit: none (uncommitted)
- [x] T7 S7 — draw-complete cue, kill damage-number style — gentle-ai-worker mwi2fc5t-8-qsbl — commit: none (uncommitted)
- [x] T8 S8 — architecture docs — gentle-ai-worker mwi2vlsd-9-csbq — commit: none (uncommitted)
- [ ] T9 S9 — manual asset wiring and Host/Client checks — user

## Pending (user-owned wiring, T9)

Nothing below is assigned yet; every field is optional and keeps today's behavior when empty.

- Ranged weapons: optional `HitReaction` profiles. Leave `ImpactParticles` unassigned when the projectile character-impact particles (magic-weapon-feel M2) are the desired single burst.
- Audio: `Attack`/`Block` clips for `RangedWeaponAudioConfig` (character impacts fall back to `Hit` when `Attack` is missing).
- Camera shake: directional kick strength in `CameraShakeConfig` (default 0 = off); optional per-weapon `DealShake` overrides.
- Aim stance: add `AimStanceDrawPresenter` to the player prefab with an optional draw-complete sound and pulse target.
- Projectiles: `ProjectileExpiryParticlePresenter` fizzle particles and `ProjectileTrailDetacher` trail references on projectile prefabs.
- Host and Client checks: ranged reaction feel, release audio sync, kick and kill feedback, fizzle and trail, bow pose during windup; Host Migration with projectiles in flight (feedback must degrade to generic without errors).

## Possible improvements

- Remove the `CombatPresentationEvent` melee compatibility members/constructor once the remaining test fixtures use the generalized tag.
- Kill feedback (1.5x shake, kill damage-number style) also applies to untagged kills from abilities and traps; restrict to weapon kills if that reads wrong in play.
- Client-side prediction of attack start and projectiles stays out of scope; Input Authority clients keep the known latency.
- Pre-existing unrelated EditMode failures to triage separately: `PhysicalProfileSlotTests` (3), `SessionCompositionConfigurationTests.SharedInventoryPrefab_PreservesCompleteSerializedUiContract` (30 vs 38), `RaidLootOriginWordCountTests` NetworkPlayer budget (1927 >= 1884), `TownEquipmentMutationEndpointTests` `RESERVATION_PENDING`; `RemoteCommitRetryTests` is timing-flaky in full runs.

## Evidence

- T1/T2: new `Core/WeaponAttackTag.cs`; tag carried ProjectileSpawnRequest → 4 `[Networked]` ints on NetworkProjectile → DamageRequest → RecordResolvedDamage; IsFatal forwarded via RPC; render drain split (reaction+particles for any weapon impact, hitstop/freeze melee-only). RED: WeaponAttackTagTests 0/3; controller PlayMode 15/17 (`TaggedRangedImpact_PlaysOptionalReactionAndParticlesWithoutHitstop` failed). GREEN: EditMode Combat 569/569, Character 91/91, Player.Presentation 97/97; PlayMode PlayerCombatNetworkControllerPlayModeTests 18/18, Abilities 184/184; 0 compiler errors (9 retained console errors are expected negative-test logs). Deviation: kept old melee constructor/members as compatibility shim for existing fixtures. Host Migration: `CopyStateFrom` copies the new projectile state; no restorer change.
- T3: new `Audio/WeaponAudioCue.cs` (pure release/reload one-shot decisions); `WeaponAudioPresenter` plays Swing/Shoot at release on the shared clock (legacy + hand timelines), no replay on resume, hit sound from event weapon (untagged → generic), runner-scoped registry (global searches removed), Reload from `HandAttacks[i].Reload` (`Reloading`/`ReloadEndTick`) start instead of 0.25 s; legacy strategies have no reload state so no Reload cue. RED: cue 1/7, audio 1/4. GREEN: EditMode Audio 7/7, Combat 569/569, Player.Presentation 97/97; PlayMode WeaponAudioPresenterTests 4/4, controller 20/20; 0 compiler errors. Asset `RangedWeaponAudioConfig` untouched (Attack/Block keys are T9).
- T4: new `Combat/WeaponDealShake.cs` (optional per-weapon override on `WeaponDefinition`, default off), `Player/Presentation/CombatShakeWeights.cs` (pure weighting); `CameraShakeConfig` kill multiplier default 1.5, directional kick strength default 0 (off until tuned — T9); `CameraShakeController` additive bounded kick through `ShakeOffset` (aim-compensated, AccessibilityScale, MaxOffset). RED: CameraShakeDealTests 0/11, aim kick 1/2, disabled-kick 0/1. GREEN: CameraShake 20/20, EditMode Player 316/316, Combat 569/569, PlayMode controller 20/20; 0 compiler errors.
- T5: worker mwi1cg2k-5-xmgh added `NetworkProjectile.Expired` + `[Networked] ExpiryConsumed` (proxy can't infer range expiry), shutdown/migration exclusion (`!hasState || !runner.IsRunning || runner.IsShutdown`), `ProjectileExpiryParticlePresenter`, `ProjectileTrailDetacher`, removed all `[CombatTrace]` (config errors kept one-shot). RED 0/9; GREEN 8/9 (fizzle test's `LogAssert.NoUnexpectedReceived` caught unrelated Fusion startup warning); worker correction limit exhausted. Parent fixed the test inline (typed access, scoped log capture). Validation delegated to worker (context backstop). Validation: 0 compiler errors/warnings; PlayMode ProjectilePresentation 9/9, Abilities 184/184, controller 20/20, Audio 4/4; EditMode Combat 569/569. Trail detacher keeps a per-despawn particle array allocation (not a frame/tick hot path).
- T6: new `Player/Presentation/CommittedAimDirection.cs` (pure window resolver); `PlayerAnimatorView`, `PlayerWeaponPresenter` (independent Off Hand), `PlayerAttackVfxPresenter` use the accepted direction from acceptance until release on the confirmed clock; live aim on idle/after release/cancel; melee unchanged; no new networked state. RED: `RangedWindup_PoseAndReleaseVfxKeepAcceptedAim` (6) and `RangedWindup_CancellationReturnsWeaponToLiveAim` (2) failed (180° vs <0.01°). GREEN: EditMode Player 332/332, Combat 569/569; PlayMode controller 20/20; 0 compiler errors.
- T7: `PlayerMovementNetworkController.AimStanceDrawCompleted` (render-only) via pure `Player/Movement/AimStanceDrawCue.cs` (one cue per `AimStanceStartTick`, re-armed by new start, >100 ms late completion silently consumed incl. late join/resume, no replay on rewind); `Player/Presentation/AimStanceDrawPresenter.cs` (optional spatial sound via AudioManager on every peer + scale pulse, null-safe); `CombatFeedbackPresenter` serialized kill color (1,.75,.2,1) and scale 1.35× for `IsFatal`, pooled entries reset. RED: draw 2/6, style 0/2. GREEN: EditMode Player 340/340, Combat 569/569, Audio 7/7; PlayMode controller 21/21, Presentation 244/244; 0 compiler errors. Deviation: damage-number pool tests use reflection on a private seam.
- T8: `PlayerCombatArchitecture.md` (confirmed feedback, melee hitstop, weapon hit reaction, weapon impact particles, ranged flow, presentation clock, release audio/committed aim/draw completion, projectile expiry/trails, weighted deal-shake, damage visuals, aim modes, known limitations) and `GameFeelParticlesArchitecture.md` (ranged no longer excluded); +117/−37; `git diff --check` clean; referenced types grep-verified.
- Verify (gentle-ai-verify mwi2w24g-a-kdan, Risk item 3): full PlayMode 864/864; full EditMode 7207/7214. Failure caused by this feature (probable): `AttackPreviewHoldingContractTests.ReleaseAndCastOrigins_AcceptTheirResolvedAuthoredPoses` (LightCrossbow N VfxOrigin) — T6 used world-space `Atan2(_attack.Direction)` instead of Animator-root pivot space. 6 unrelated failures (PhysicalProfileSlotTests ×3, SessionCompositionConfigurationTests shared inventory 30 vs 38, RaidLootOriginWordCountTests NetworkPlayer 1927 ≥ 1884 — no player `[Networked]` change in this diff, TownEquipmentMutationEndpointTests RESERVATION_PENDING). Shared-code consumers unchanged (trailing optional params only); S0 no gameplay values changed; Host Migration copy path covers new projectile state. Advisories: melee reaction now gated on AppliedDamage>0; untagged kills also get kill style/shake (per S4/S7 wording); `??` on UnityEngine.Object in WeaponAudioPresenter.
- Fix round 1 (mwi3rp4a-b-rdn9, partial): finding 1 RED reproduced (AttackPreviewHoldingContractTests 6/7); finding 2 no change needed (`RecordResolvedDamage` rejects zero/non-applied damage; no other ConfirmedImpact producer); finding 3 destroyed-config audio test added, RED not observed because the Editor connection dropped (Editor still running, PID 4496, ready again). Resumed as mwi3vrmr-c-rtan.
- Fix round 2 (mwi3vrmr-c-rtan): live and committed VFX share `PlayerWeaponPresenter.ResolveAttackVfxPivotAngle` (authored holding basis); T6 seam test expectation corrected from world axis to authored basis. `WeaponAudioPresenter` uses explicit Unity null check. RED→GREEN: AttackPreviewHoldingContractTests 6/7→7/7; Audio PlayMode 4/5→5/5. Runs: EditMode AttackAnimationValidator 171/171, Player 340/340, Combat 569/569, Audio 7/7; PlayMode controller 21/21, Audio 5/5, Presentation 244/244; 0 compiler errors. Full EditMode suite not rerun after this fix (6 unrelated pre-existing failures from verify remain, baseline not run).

## Log

- L1 (user, original request, verbatim summary source: the "Combat game feel (melee + ranged, presentation only)" brief of 2026-11-15, including Objective, Approved contract, Verified starting state, Routing and checks, Tasks T1–T9, Acceptance criteria, Manual checklist; Evidence "(none yet)"). Specs above quote it verbatim.
- L3 Routing: tasks run sequentially (single shared Unity Editor for compile/test; parallel writers would collide). T1+T2 delegated as one unit (context backstop).
- L2 Exploration delegated to `gentle-ai-explore` (evidence budget fired). Unity Editor connected and ready (port 7800, no compile errors).
