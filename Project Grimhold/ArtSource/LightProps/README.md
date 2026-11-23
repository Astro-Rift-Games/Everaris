# Floor light prop flames (Brazier, Candle, Multiple Candles)

Animated flames for the floor light props, built the same way as the wall torch
(`Torch-Lit-Base` + `Torch-Flame-Sheet`): a flameless lit base sprite plus a flame sheet that
`FlameVisual` animates.

| Folder | Content |
| --- | --- |
| `aseprite/` | `<Art>-Flame.aseprite`: `Base` layer (static, for reference) and `Flame` layer (animated), `Flicker` tag, frame durations. |
| `tools/gen_flames.py` | Builds the lit bases and the flame frames from the shipped `<Art>-Lit.png` / `<Art>-Unlit.png`. |
| `previews/` | GIF previews exported from Aseprite. Regenerable, not versioned. |

| Prop | Art | Frames | Rate | Image origin (px) |
| --- | --- | --- | --- | --- |
| Brazier | `Brazier` | 10 | 7 fps | (64, 64.5) |
| Candle | `Candle` | 6 | 8 fps | (64.5, 65.5) |
| Multiple Candles | `Candles` | 8 | 8 fps | (64.5, 67) |

## Rules

- Frame 0 is the flame extracted from `<Art>-Lit.png` (pixels in the 5 fire colours), so
  `<Art>-Lit-Base.png` + frame 0 rebuilds the original lit art pixel-exactly.
- The base keeps every non-fire lit pixel. Under the old flame it shows the unlit pixel re-lit with
  the colour the lit art uses for it; the brazier shows glowing coals instead of dead charcoal.
- Flames sway by row height (bottom anchored), stretch or trim their tip and, on the brazier, drop
  rising embers. The two flames of Multiple Candles move half a cycle apart.
- Every frame is 128x128 at the same canvas coordinates as the lit PNG; sheet sprites pivot on the
  lit image origin, so `FlameVisual` stays at local (0, 0, 0).

## Regenerating

1. `python -I tools/gen_flames.py "<project>/Assets/Art/Props" <out>`: writes `<Art>-Lit-Base.png`,
   `<Art>-Flame-<i>.png` and a reference sheet, and asserts base + frame 0 == lit.
2. In Aseprite, import the base, add a `Flame` layer with one cel per frame, set the frame
   duration (1/7 s brazier, 1/8 s candles) and the `Flicker` tag; save into `aseprite/`.
3. Hide `Base` and export a horizontal sprite sheet to `Assets/Art/Props/<Art>-Flame-Sheet.png`.

`FloorLightPropFlameAnimationTests` pins import settings, slicing, clips, controllers, prefab wiring
and the pixel-exact rebuild.
