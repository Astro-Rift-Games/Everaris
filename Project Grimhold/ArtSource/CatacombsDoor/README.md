# Catacombs double door

Opening animation for the double wooden door painted in CatacombsMap (`Access` tilemap), used by the
`CatacombsDoor` variant of `DungeonDoor`.

| Folder | Content |
| --- | --- |
| `aseprite/Catacombs-Door.aseprite` | 32x48, `Leaves` layer under the `Frame` layer (static brick corners and arch outline); tags `Opening_North` (forward) and `Closing_North` (reverse), 0.1 s per frame. |
| `tools/gen_door.py` | Builds the frames from the door tiles of `Assets/Art/Tilesets/Catacombs/Catacombs-Atlas.png`. |
| `previews/` | GIF exported from Aseprite. Regenerable, not versioned. |

## Rules

- The door is 2x4 tiles in the atlas (x 1616-1647, y 1328-1391, bottom origin). The top tile row
  (stone cap) stays a tile in the map; the frames cover the lower 3 rows (32x48).
- Frame 0 is the closed door and equals the atlas tiles pixel-exactly, so a closed door looks
  identical to the old tiles.
- Each leaf (16 px) swings North on its outer hinge, behind the wall: the `Leaves` layer sits under
  `Frame`, so a leaf never covers the brick corners or the arch outline. Frames keep 100 %, 60 %,
  25 % and 0 % of its width, lift the free edge up to 6 px and darken it up to 55 % toward the
  shadow colour as it recedes; the open frame shows only the empty arch, with the map's Floor tiles
  showing through.
- `Assets/Art/Props/Doors/Catacombs-Door-Sheet.png` is the horizontal sheet exported from Aseprite
  (4 frames, 128x48).

## Regenerating

1. `python -I tools/gen_door.py "<project>/Assets/Art/Tilesets/Catacombs/Catacombs-Atlas.png" <out>`
   writes the frames and asserts frame 0 == tiles.
2. In Aseprite, import the static frame, add a `Leaves` layer with one cel per frame, 0.1 s per frame,
   the two tags; save into `aseprite/`.
3. Export a horizontal sprite sheet to `Assets/Art/Props/Doors/Catacombs-Door-Sheet.png`.
