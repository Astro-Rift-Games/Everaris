# UI Scaling Architecture

**Status:** Decision approved on 2026-11-21. **Not implemented.** No canvas, prefab, scene, script or
project setting follows this document yet; existing canvases keep their provisional configuration
until a dedicated task migrates them.

## Context

The pixel-art UI is being rebuilt from scratch. The canvases that exist today are provisional: most
use `Scale With Screen Size` with a 1920×1080 reference, and the main menu uses 1600×900. At 1080p
the main menu scales by 1.2, so one art pixel covers 1.2 screen pixels and rows alternate between
one and two screen pixels. The first component of the new atlas, the 80×32 3D button with five
states and a 9-slice centre of `(12, 11, 56, 4)`, loses its hardware detail at that scale and is
pixel-exact only at integer scales.

## Decision

1. **Logical reference resolution: 960×540.** UI art and layouts are authored against it. One art
   pixel equals one canvas unit.
2. **Integer scaling only.** The UI scale is the largest integer that fits the screen:
   `scale = max(1, floor(min(screenWidth / 960, screenHeight / 540)))`.
3. **The canvas grows; sprites never stretch.** When the screen is larger than `960×540 × scale`,
   the canvas gains usable area (`screen / scale` units per axis). Components keep their size.
4. **Adaptive layouts.** Every screen must fit inside 960×540. Extra area is margin: HUD elements
   anchor to edges and corners, panels stay centred or anchored. No component is resized
   non-uniformly to fill space, other than through its 9-slice.
5. **UI and world scale independently.** The UI renders at native screen resolution with its own
   integer scale. World rendering (camera size, world pixels per unit, any future pixel-perfect
   camera) is a separate decision and must not be coupled to the UI scale.
6. **Ready for a configurable scale.** The scale is a single value owned by one place, so a future
   player setting can override the automatic value. An override stays an integer between 1 and the
   automatic maximum.
7. **No fractional scale by default.** A fractional scale may only ever exist as an explicit,
   opt-in setting; it is never the automatic behaviour.

## Resulting scale and canvas area

| Screen | Scale | Canvas area (units) | Size relative to design |
| --- | --- | --- | --- |
| 1280×720 | ×1 | 1280×720 | 75% |
| 1280×800 | ×1 | 1280×800 | 75% |
| 1366×768 | ×1 | 1366×768 | 70% |
| 1600×900 | ×1 | 1600×900 | 60% |
| 1920×1080 | ×2 | 960×540 | 100% |
| 1920×1200 | ×2 | 960×600 | 100% |
| 2560×1080 | ×2 | 1280×540 | 100% |
| 2560×1440 | ×2 | 1280×720 | 75% |
| 3440×1440 | ×2 | 1720×720 | 75% |
| 3840×2160 | ×4 | 960×540 | 100% |

"Size relative to design" is the on-screen size of a component compared with a screen where
960×540 fills the display exactly. Below 100% the UI is smaller than designed and the layout has
more room. This is the accepted cost of prioritising pixel-perfect rendering.

## Authoring rules

- **Sprites:** point filtering, no compression artefacts, one art pixel per canvas unit, no
  per-image pixels-per-unit multiplier other than 1.
- **Rects:** integer sizes and integer positions in canvas units. Prefer even sizes for centred
  elements so centring does not produce half-unit offsets.
- **9-slice:** borders are part of the art and scale with the integer factor. A sliced element must
  not be smaller than its borders (the 3D button needs at least 24×28).
- **Motion:** no non-integer scale animation on pixel-art components. Position offsets move in whole
  canvas units.
- **Typography:** pixel fonts are used at integer multiples of their native size (alagard: 16) and
  must rasterise on the pixel grid.
- **Mixed density:** an asset drawn at a different pixel density than the UI (for example 16×16
  loot icons shown at 2:1 inside a 64×64 slot) is scaled by an integer factor only.

## Reference measurements

Taken on 2026-11-21 with an in-memory Play Mode overlay (nothing was added to the project):

- At 2 and 3 screen pixels per art pixel, all five button states and six 9-slice sizes matched the
  source sprites with zero differing pixels.
- At the current 1.2 scale the same sprites render with uneven rows and columns.
- The button label must be inset to the face (9 art pixels at the bottom, 1 at the top) and move
  down 2 art pixels while pressed; uGUI does not do this by itself.

## Known conflicts with the current implementation

These are recorded, not resolved, by this document:

- Every existing canvas uses a fractional-capable `Scale With Screen Size` configuration.
- `UiButtonFeedback` scales controls to 1.05 on hover and 0.95 on press (see
  `UiFeedbackArchitecture.md`), which breaks the pixel grid on pixel-art buttons.
- alagard is imported as a dynamic SDF font and used at arbitrary sizes.
- The default window is 960×540 and not resizable; there is no resolution or UI scale setting.
- `RaidMainHudArchitecture.md` and `ExtractionArchitecture.md` describe HUD behaviour that assumes
  the HUD is not pixel-perfect.

## Open decisions

- UI Design System foundations: spacing unit, type scale, slot and panel sizes, colour roles.
- Inventory + Equipment proportions at 960×540, validated before more atlas assets are produced.
- A secondary small pixel font for dense text, and the bitmap import of alagard.
- Minimum supported resolution, default window size and whether the window is resizable.
- Where the scale value lives and how a player setting overrides it.
- Whether the world moves to integer scaling.

## Validation criteria for the future implementation

- Game view captures at 720p, 1080p, 1440p and 4K match the source sprites scaled with nearest
  neighbour, pixel for pixel.
- Inventory + Equipment fits inside 960×540 with no overlapping text.
- The same layouts remain correct at 16:10, 1600×900 and ultrawide.
- The smallest text is legible at 720p.
