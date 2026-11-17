# Player Sprint animation

Objective: give the modular player a dedicated Sprint animation instead of the Walk played faster.
Branch: `New-Testing` (no commits without explicit authorization). Delivery: `ask-on-risk`.
Test runner: Unity EditMode through the open Editor (`unity command test run --mode editor`).

## Specs

- S1. Sprint uses its own sheets ("Sí, hojas propias"): 210 sheets under
  `Assets/Art/Character/**/Sprint/`, sliced like the Walk sheets (eight 96x96 frames).
- S2. The approved art is the staging build in `Project Grimhold/ArtSource/SprintAnimation`
  ("Sí, integralo"), unchanged by the integration.
- S3. While the player sprints, the body and both hand layers play the Sprint clips for the
  current facing; otherwise they play the Walk clips. No existing transition changes.
- S4. Held weapons and shields stay in the drawn hands during Sprint (grip keys follow the
  Sprint hand frames).
- S5. Hands and held items sort correctly in Walk and Sprint ("la sorting layer de las manos y de las
  manos con el arma no estan bien. Tanto en walk como en sprint"): Sprint hands use the Walk's per-frame
  swing depth, and a held weapon or shield stays on the side of the body its hand is on.
- S6. Every equipment piece of the six sets shows its Sprint sprite on every Sprint frame.
- S7. Walk, Idle and attack presentation keep their current behavior.

Assumptions (not requested, chosen to keep scope small): Sprint reuses the Walk frame count and
12 fps key grid; its cadence is a blend-tree time scale, tuned against the 1.5x sprint speed.

## Tasks

- [x] T1 (S1, S2) inline — import sheets with slicing metadata. 210 sheets + metas.
- [x] T2 (S3, S4, S5) inline — Sprint clips: body, LeftHand, RightHand, six facings.
- [x] T3 (S3) inline — controller: `SprintBlend` parameter and Walk/Sprint locomotion trees.
- [x] T4 (S3) inline — `IsSprinting` on the presentation seam drives `SprintBlend`.
- [x] T5 (S6) inline — equipment sprite mappings for the six sets.
- [x] T6 (S3-S7) inline — tests, architecture note, full related suites.
- [x] T7 (S5) inline — shared swing depth for Sprint; held items follow their hand.

## Log

- L1 (2026-11-16, user): "hay que crear las animaciones de sprint." / "Sí, hojas propias" /
  "Sí, integralo".
- L2: design choice — each Movement state keeps its state and transitions; its motion becomes a
  1D tree on `SprintBlend` (0 = Walk tree, 1 = Sprint tree), so S7 holds by construction.
- L3 (2026-11-16): implemented, uncommitted. Evidence in the open Editor (EditMode): RED 23 failing
  Sprint tests before assets and code existed; GREEN afterwards. Suites: filter "Sprint" 35/35,
  DirectionalAnimationGeneratorTests 208/208, PlayerAnimatorViewTests 50/50, ArmorSetCatalogTests 6/6,
  OffHandRetargetingTests 84/84, ShieldDefend 61/61, AttackPreview 144/144, PlayerArmor 29/29,
  PlayerKnockbackAnimationTests 6/6, PlayerPresentationStateSeamTests 36/36, PlayerMovementRulesTests 30/30.
- L4: assets come from `ArtSource/WalkRework/tools/apply_sprint_to_unity.py` (sheets, metas, clip copies,
  1440 equipment mappings in 24 definitions); grips, depth and controller trees were authored through the
  Editor API. ArmorSetCatalogTests totals moved from 108/3240/540 to 156/4680/780.
- L5: not verified — Play Mode (Raid and Town), weapons and shields in hand while sprinting, cadence
  against the real sprint speed, full EditMode and PlayMode suites. Next step: Play Mode review.
- L6 (2026-11-16, user): "ok se ve bien, pero hay que la sorting layer de las manos y de las manos con el
  arma no estan bien. Tanto en walk como en sprint". S5 rewritten; T7 added.
- L7: cause — the weapon kept the facing's depth while Walk moved its hand across the body, and Sprint
  hands kept one depth per facing. Fix: `PlayerWeaponPresentationMath.ResolveHeldItemSortingOrder` (hand
  above Body -> item front order, else back order), used by `PlayerWeaponPresenter` for hand-held Main Hand
  items and for Off Hand items outside an Off Hand attack overlay; generator swing depth covers Sprint
  (`WriteMainHandSwingSorting`). Sprint hand art no longer rises on the flight frames so HeavyArmor shows 0
  bare pixels under the swing rule in all six facings.
- L8: evidence (EditMode, open Editor): RED 25 failing; GREEN. DirectionalAnimationGeneratorTests 202/202,
  PlayerWeaponPresentationMathTests 42/42, PlayerAnimatorViewTests 50/50, ArmorSetCatalogTests 6/6,
  filter "OffHand" 205/205, ShieldDefend 61/61, AttackPreview 144/144, PlayerArmor 29/29,
  PlayerKnockbackAnimationTests 6/6, PlayerPresentationStateSeamTests 36/36. Not verified: Play Mode with
  weapons and shields, PlayMode suites.
- L9 (2026-11-16, user): "En SE, NE y NW se sigue viendo mal. El arma y la mano no se posicionan de forma
  coherente. Analiza las animaciones y lo vas a entender."
- L10: analysis — on diagonals the near hand is drawn 10-12 of its 12 pixels over the torso, and the
  swing-height rule put it (and its weapon) behind the body for 5 of 8 frames. Rule changed to
  `ResolveLocomotionHandSortingOrders`: S/N keep the swing rule; diagonals keep the near hand (Main Hand in
  SE/NE, Off Hand in SW/NW) in front and the far hand behind for the whole cycle. HeavyArmor still shows 0
  bare pixels in all six facings of Walk and Sprint.
- L11: evidence (EditMode, open Editor): RED 22 failing on diagonals; GREEN. DirectionalAnimationGeneratorTests
  212/212, PlayerWeaponPresentationMathTests 42/42, PlayerAnimatorViewTests 50/50, ArmorSetCatalogTests 6/6,
  filter "OffHand" 205/205, ShieldDefend 61/61, AttackPreview 144/144, PlayerArmor 29/29,
  PlayerKnockbackAnimationTests 6/6, PlayerPresentationStateSeamTests 36/36. Not verified: Play Mode.
