# Player Walk rework (staging)

Reworked Walk leg cycles for the modular player, built from the shipped PNGs
(no `.aseprite` sources existed). Nothing under `Assets/` was modified; this
folder lives outside `Assets/`, so Unity does not import it.

| Folder | Content |
| --- | --- |
| `source/` | Untouched copies of the Unity sheets used as input (Walk, plus Idle legs). |
| `png/` | 210 reworked sheets (legs, body, head and both hands; base + 6 sets), same relative path and file name as under `Assets/Art/Character/`. |
| `aseprite/` | `Walk_<facing>.aseprite`: one group per skin (Base + 6 sets), one layer per module, 8 frames at 12 fps, `Walk_<facing>` tag, hidden `REF Legs (original)` layer. |
| `previews/` | Before/After GIFs per facing and skin (left = before at the shipped 12 fps, right = after at the proposed 10 fps) and contact sheets. |
| `hand_grip_offsets.csv` | Per facing, hand and frame: how far the hand moved, in pixels and in Unity units (16 PPU, Y up). |
| `tools/` | `walk_recipes.lua` (the edit, as data), `walk_rework.lua` (Aseprite builder), `preview.py`, `validate.py`, `apply_to_unity.py`. Also `sprint_recipes.lua` and `sprint_preview.py`, which build `../SprintAnimation` from the same sources. |

## Integration contract (verified against the project)

- Sheets stay 768x96 RGBA, 8 frames of 96x96, same frame order, hard alpha, source palette only.
- Sprite slicing, pivots and PPU come from the existing `.meta` files, which are not touched:
  replace a PNG in place and keep its `.meta`.
- Hand sheets ARE modified (swing halved), so the PNGs alone are not a drop-in: the Walk
  clips key `WeaponPose`, `OffHandGrip` and `MainHandGrip` on each hand frame, and weapons
  detach from the hand until those keys follow. See "Applying it in Unity".
- Equipment maps one overlay sprite per base sprite and frame, so every leg change is applied
  with the same recipe to the six trouser overlays.
- SW and NW remain exact mirrors of SE and NE.

## What changed

Direction: a careful, grounded walk for a dark dungeon crawler, not a bouncy march.
Frame count, order and timing are unchanged.

- **Torso and head, every facing** - the "up" beat (frames 3 and 7: body stretched one row,
  head raised) now uses the neutral frame. Only the one-pixel dip when the weight lands
  remains, so vertical travel is halved. Applied to every armor and headgear overlay.
- **Hands, every facing** - the swing is halved around the middle of its path (about six
  pixels of travel down to three), so the arms stay close to the body. Each frame is the
  original hand sprite moved by whole pixels; gloves and gauntlets move identically.
- **Cadence (proposal, not an art change)** - 10 fps instead of 12: the cycle takes 0.8 s
  instead of 0.67 s. It only exists in the preview GIFs until the clips are changed.
- **S / N legs** - the old cycle showed one stick leg at a time with the foot line jumping
  58-59-57-57. Now both feet are visible, the planted leg reuses the Idle leg and foot, the
  foot line stays on row 58 for the whole cycle (short steps, no forward reach), the lifted
  leg keeps the original foreshortened stub, and each leg is planted four frames and swings
  four. Left-foot/right-hand opposition follows the existing hand swing.
- **SE / SW legs** - cycle rotated one frame so contact falls on the neutral body frame and
  the body dips over the planted leg; far leg no longer snaps from front to back in one frame
  (it is hidden while crossing behind the near leg); near-leg toe-off outline closed; near
  contact foot pulled in one pixel and stance foot straightened so it slides back evenly.
- **NE / NW legs** - poses retimed (rotated three frames) so each leg gets a four-frame stance
  instead of five and three; far leg hidden on its two crossing frames; near foot straightened
  and its trail shortened one pixel.

## Regenerating

In Aseprite, with `WALK_REWORK_ROOT` set to this folder, run `tools/walk_rework.lua`
(`File > Scripts`, or the Aseprite MCP `run_lua` tool). Then:

```
python tools/validate.py
python tools/preview.py gifs
```

## Applied to the project (uncommitted)

`python tools/apply_to_unity.py` did the following; it rebuilds clips and controller from
git HEAD, so it can be re-run safely.

1. Copied the 210 sheets over `Assets/Art/Character/` (no `.meta` touched).
2. Moved the grip keys of the six `Walk_<facing>.anim` clips by the offset of the hand they
   follow (`WeaponPose` and `OffHandGrip` with LeftHand, `MainHandGrip` with RightHand), in
   both `m_PositionCurves` and the per-axis copy under `m_EditorCurves`.
3. Cadence: `m_TimeScale` 0.8333333 on the 18 children of the `Walk`, `LeftHand-Walk` and
   `RightHand-Walk` blend trees in `Character.controller` (the 12 fps clips play at 10 fps,
   0.8 s per cycle). The clips themselves keep their 12 fps keys.

4. Hand depth in back facings: with the shorter swing the hands sat inside the torso
   silhouette and, at their constant order -2, were hidden for most of the cycle. The
   generator now keys each hand per drawn frame in Walk N/NE/NW: in front of the body
   (LeftHand 22, RightHand 30) for the camera-side half of its swing, -2 for the rest.
   This lives in `DirectionalAnimationGenerator` (`ResolveWalkSwingSortingOrders`,
   `WriteMainHandWalkSorting`) and is documented in `PlayerCombatArchitecture.md`.
   After re-running `apply_to_unity.py`, run `Tools/Animations/Generate Weapon Pose
   Locomotion` in the Editor: the script rebuilds the clips from git HEAD and does not
   write these depth keys itself.

Checked headless: `DirectionalAnimationGeneratorTests`, `PlayerAnimatorViewTests` and
`ArmorSetCatalogTests` (EditMode, 224 tests) pass.

## Not verified / pending

- Not seen in Play Mode: sorting, equipment overlays, weapons and shields in hand, and
  Idle <-> Walk transitions still need eyes on the running game. The full EditMode and
  PlayMode suites were not run.
- A slower cycle with the same movement speed makes the feet slide more over the floor;
  whether 10 fps still reads right has to be judged in game against the real walk speed.
- Hand phase is opposite between S/N and the diagonals in the shipped art, so the leg phase
  (which follows the hands) flips when turning between them. Pre-existing.
- Attack, Idle and Knockback clips were not touched; any of them that blends from the Walk
  hand positions should be checked once the grips move.
- Enemy Walk sheets (skeletons, slimes) are out of scope.
