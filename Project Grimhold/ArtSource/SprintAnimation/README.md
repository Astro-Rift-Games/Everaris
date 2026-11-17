# Player Sprint animation (staging, art only)

Dedicated Sprint sheets for the modular player, built from the shipped Walk and Idle
sheets with the same stamp-recipe pipeline as `../WalkRework`. The integration is described at the end.

| Folder | Content |
| --- | --- |
| `png/` | 210 sheets (`...-Sprint-<facing>-Sheet.png`): legs, body, head and both hands, base body plus the six equipment sets. 768x96, eight 96x96 frames, same layout as Walk. |
| `aseprite/` | `Sprint_<facing>.aseprite`: one group per skin, one layer per module, `Sprint_<facing>` tag. |
| `previews/` | Walk vs Sprint GIFs per facing and skin (left = Walk at 10 fps, right = Sprint at about 14 fps) and contact sheets. Regenerable. |

The recipe is `../WalkRework/tools/sprint_recipes.lua`.

## How it differs from the Walk

A heavy run, the run of someone carrying armor.

- Flight phase: on frames 3 and 7 both legs are tucked, so no foot touches the ground line.
  In S and N, where a run shows no forward travel, the whole figure also rises one pixel.
- S / N legs: the planted foot lands under the centre line (one pixel inward) and sweeps two
  rows per frame, from row 60 to 56 (S) or 56 to 60 (N); the Walk keeps it on row 58.
- Diagonal legs: the Walk's retimed stride at full reach.
- Weight: torso, head and hands sink one extra pixel when the foot lands (frames 1 and 5),
  on top of the original bounce and its stretched "up" beat.
- Lean: the upper body moves one pixel into the direction of travel on the diagonals; toward
  or away from the camera only the head moves. Head and torso lean together because an
  extra pixel on the head uncovers bare neck under full armor.
- Hands pump at chest height. In S and N they swing front to back beside the body along a
  downward arc instead of opening sideways; on the diagonals they keep the original path,
  raised two pixels and about a third wider. Each frame is the shipped hand sprite moved whole.
- Hand depth uses the Walk's per-frame swing rule (camera-side half of the swing over the body).
  The hands do not rise on the flight frames, so at mid-swing they cover the sides of the torso.

Checked with the heaviest set: HeavyArmor shows no bare-skin pixel in any frame of S, N, SE, NE.

## Regenerating

In Aseprite, run `../WalkRework/tools/walk_rework.lua` with:

```
WALK_REWORK_ROOT    = <path to ArtSource/WalkRework>
WALK_REWORK_ANIM    = "Sprint"
WALK_REWORK_RECIPES = WALK_REWORK_ROOT .. "/tools/sprint_recipes.lua"
WALK_REWORK_OUT     = <path to ArtSource/SprintAnimation>
```

Then `python ../WalkRework/tools/sprint_preview.py` validates the sheets and rewrites the
previews.

## Applied to the project (uncommitted)

`python ../WalkRework/tools/apply_sprint_to_unity.py` copies the sheets to
`Assets/Art/Character/**/Sprint/` with the Walk slicing, creates the Sprint clips as copies of
the Walk clips pointing at the Sprint sheets, and appends the Sprint mappings to the 24
equipment definitions. Grips, hand depth and the controller's Walk/Sprint trees were then
authored through the Editor; `Docs/Architecture/PlayerCombatArchitecture.md` describes the
result.

## Not verified

- Play Mode in Raid and Town, with and without weapons and shields.
- The cadence (Sprint trees at time scale 0.78, about 14 fps) against the real sprint speed.
