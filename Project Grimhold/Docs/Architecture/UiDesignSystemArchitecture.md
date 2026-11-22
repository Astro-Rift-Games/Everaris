# UI Design System Architecture

**Status:** Foundations drafted on 2026-11-21 for review. **Not implemented.** Nothing in Unity, in
the atlas or in existing canvases follows this document yet. Existing canvases, the Charcoal atlas
bindings and current component sizes are provisional and are not design constraints.

Every rule below carries one of three labels:

| Label | Meaning |
| --- | --- |
| **Approved** | Already decided and validated. Changing it needs an explicit new decision. |
| **Proposed** | Defined here and awaiting review. Treat as the default until accepted or replaced. |
| **Pending** | Deliberately left open. It needs real layouts, Game Design input or an asset test. |

## Scope

Shared foundations for a modular, reusable medieval pixel-art UI: visual language, spacing, type,
colour roles, interaction states, family conventions and pixel-art composition rules.

Out of scope: the design of individual components, final sizes per component family, screen layouts
(including Inventory + Equipment), the scaling strategy, and world rendering.

Related documents:

- `UiScalingArchitecture.md` owns the 960×540 logical resolution and integer scaling. Every size in
  this document is in canvas units, where one unit is one art pixel.
- `UiFeedbackArchitecture.md` owns animated feedback. Its current scale-based hover and press
  feedback conflicts with the rules here; see Known conflicts.
- The live Game Design document *UI - Auditoría Funcional de Interfaces MVP* owns what each screen
  must show and do, and defines the process UI.1 audit, UI.2 wireframes, UI.3 visual direction and
  UI.4 asset production. This document only fixes foundations so that later stages share one
  language; it does not replace the UI.3 deliverable, which follows validated wireframes.

## Art reference

**Approved.** The 3D button v4 (`button_normal`, `highlighted`, `pressed`, `selected`, `disabled`;
80×32 canvas; 9-slice centre `(12, 11, 56, 4)`) is the artistic reference for materials, lighting,
palette use and state language.

It is a reference for *language*, not for *geometry*. Other controls do not inherit its dimensions,
its front thickness, its corner brackets or its press travel unless their family convention says so.

## Visual language

### Materials

**Proposed.** Three materials, each tied to one palette ramp. A material never borrows the
highlight or shadow of another ramp.

| Material | Used for | Ramp (dark to light) |
| --- | --- | --- |
| Forged bronze | Structure: frames, rims, hardware, tracks | `4D2D2A` `673E2A` `946039` `B37C48` `CDA160` `E7BE79` `F0DEB0` |
| Burgundy enamel | Faces the player acts on or reads from | `250B19` `3C1025` `4D1730` `58182D` `65213C` `81324D` |
| Dark well | Recessed interiors and backgrounds | `13070F` `1A0C16` `25101C` `2E1321` `361626` `3D202E` |

`100A11` is the only outline colour.

### Lighting

**Approved** (from the button): one light, top-left, shared by the whole interface.

**Proposed** consequences: top and left edges are lit, bottom and right edges are shaded; highlights
are one pixel wide; no component has its own light source, glow direction or gradient; a state may
change how much light a surface returns, never where the light comes from.

### Depth

**Proposed.** Four depth levels. A component belongs to exactly one.

| Level | Reads as | Construction | Families |
| --- | --- | --- | --- |
| Recessed | A hole that holds something | Inner shadow on top and left, lit lip on bottom and right | Slots, input fields, bar tracks |
| Flat | A surface | No bevel | Panel interiors, tooltips |
| Raised | A fixed frame or plate | Bevel, no front thickness | Panel frames, tabs, headers |
| Pressable | A block that can go down | Bevel, visible front thickness and a contact shadow | Buttons |

Visible front thickness is reserved for pressable controls. It is the affordance that says "this
moves when you click it", so nothing decorative may imitate it.

### Contrast

**Proposed.**

- Every free-standing component has a continuous one-pixel `100A11` outline.
- Interactive faces are at least two ramp steps away from the surface they sit on.
- Text keeps its role colour on every state except disabled; state changes act on the component,
  not on the legibility of its label.
- Disabled is expressed by lower contrast and saturation, never by transparency alone.

### Edges and ornament

**Proposed.**

- Corners are chamfered by one or two pixels. No curves larger than that, no anti-aliasing.
- Hardware (rivets, corner brackets, grooves) sits only on structural corners and rims.
- Ornament follows hierarchy: panel frames and pressable controls may carry corner hardware; slots,
  inputs and bars carry none or a single minimal accent.
- Ornament never enters the content area or the 9-slice centre, and never appears in only one state.
- A new motif is added to the system, not to a single component. If only one component would use
  it, it does not exist.

## Spacing and interaction sizes

**Proposed.** The base unit is 4 canvas units (8 screen pixels at 1080p).

| Token | Units | Use |
| --- | --- | --- |
| `space-0` | 2 | Gaps inside a component, grid gutters between slots |
| `space-1` | 4 | Tight gaps between related elements |
| `space-2` | 8 | Default padding and gap between controls |
| `space-3` | 12 | Padding inside framed panels |
| `space-4` | 16 | Separation between groups |
| `space-5` | 24 | Separation between panels |
| `space-6` | 32 | Screen margins and major sections |

Rules:

- All sizes and positions are integers; sizes are even, so centring never lands on half a unit.
- Minimum pointer target: 24×24 units. Adjacent targets are separated by at least `space-0`, and by
  `space-1` when either one is a pressable control.
- A sliced component is never smaller than its borders. For the button that is 24×28.
- The content box of a component is measured from its face, not from its sprite canvas. Shadows,
  front thickness and outlines sit outside the content box.

**Pending:** whether 24×24 is comfortable at ×1 scale (1280×720 and 1600×900, see
`UiScalingArchitecture.md`), and whether gamepad navigation needs larger targets.

## Typography

**Approved:** pixel fonts are used only at integer multiples of their native size and rasterise on
the pixel grid (`UiScalingArchitecture.md`).

**Proposed** roles:

| Role | Use | Font and size |
| --- | --- | --- |
| Display | Screen titles, game title | alagard 32 (48 for the main title only) |
| Title | Panel and section headers | alagard 16 |
| Label | Text on buttons and tabs | alagard 16 |
| Body | Descriptions, tooltips, dialogue | Secondary small pixel font |
| Numeric | Stats, quantities, prices, timers | Secondary small pixel font, fixed-width digits |
| Caption | Hints, key prompts, secondary data | Secondary small pixel font |

Rules:

- No scaled, faux-bold or faux-italic text. Emphasis uses role or colour.
- No text with a capital height under 7 units, because at ×1 scale that is 7 screen pixels.
- Text sits on the pixel grid: integer position, no rotation, no animated scale.
- Text readability on busy surfaces uses a one-pixel dark offset shadow, not an outline or a glow.
- A label on a pressable control is centred on the face and travels with it when pressed.
- Layouts must survive longer strings; nothing is sized to an English or Spanish word.

**Pending:** choice of the secondary font (alagard at 16 allows roughly 33 text lines per screen and
is too large for dense stats); bitmap import of alagard; coverage of Spanish characters in both
fonts; how many numeric widths the HUD needs.

## Colour roles

**Proposed.** Roles are assigned from the existing 40-colour atlas palette. A role is used for its
meaning only; decoration does not borrow role colours.

| Role | Meaning | Palette |
| --- | --- | --- |
| Structure | Frames and hardware | Forged bronze ramp |
| Face | Interactive or readable surface | Burgundy enamel ramp |
| Well | Recessed interior | Dark well ramp |
| Text primary | Main text | `F0DEB0` |
| Text secondary | Supporting text | `CDA160` |
| Text disabled | Unavailable text | `673E2A` |
| Focus | Where navigation input currently is | Teal `23777A`, lit `50ACA4`, dark `194C50` |
| Selection | What the player has chosen | Gold `E1BE72`, lit `F0DEB0`, dark `A17A39` |
| Positive | Success, gain, requirement met | Green `33765A`, lit `77B594` |
| Negative | Error, loss, requirement missing | Red `942B48`, lit `C24B63` |
| Caution | Risk, limited time, pending cost | Amber `C7A45A`, dark `795827` |
| Information | Neutral notices and links | Blue `2B6B86`, lit `5AA6BC` |

Rules:

- Colour is never the only carrier of meaning. Every role pairs with a shape, icon, position or text.
- Focus teal and Selection gold are reserved. No rarity, resource or decorative element uses them.
- Feedback colours appear as accents, fills or text, not as full component recolours.

**Pending:**

- Negative red sits close to the burgundy face, and Caution amber close to bronze. Both need a test
  on real components before acceptance.
- Rarity colours and resource colours (health, mana, stamina) belong to Game Design and are not
  defined here. They must not collide with Focus or Selection.

## Interaction states

**Approved** (from the button validation): hover raises luminosity without changing geometry;
pressed lowers the face by 2 units with reduced front thickness and a shorter contact shadow, on an
unchanged canvas; disabled lowers contrast and saturation and keeps the structure readable.

**Approved** (2026-11-21): the button's fifth sprite, named `selected` in the atlas, represents
navigation focus, not persistent selection. Its teal inlay is the reference for the Focus role.
Command buttons have no persistent selection state.

**Proposed.** Each state owns one visual channel. Channels are independent, so states can combine
without inventing new sprites for every combination.

| State | Meaning | Lifetime | Channel | Must not |
| --- | --- | --- | --- | --- |
| Hover | The pointer is over it | While the pointer stays | Luminosity | Change hue, geometry or size |
| Pressed | It is being activated | While held | Geometry and depth | Change hue; move the canvas |
| Navigation focus | Keyboard or gamepad input goes here | Until navigation moves; one element at a time | Teal accent ring or inlay | Fill the face; resemble Selection |
| Persistent selection | The player chose this; application state | Until the choice changes; several may exist | Gold frame and brighter face | Depend on the pointer or the event system |
| Disabled | It cannot be used now | While the rule holds | Contrast and saturation | Use hue coding; hide the structure |

Rules:

- Precedence: Disabled, then Pressed; Focus and Selection combine with each other and with Hover.
- A selected element that also has focus shows both the gold treatment and the teal accent.
- A disabled element shows neither hover nor pressed, and keeps selection if it had one.
- States change on the frame the input arrives. No state is communicated by scale animation.
- Families that cannot be pressed (panels, bars) define no pressed state rather than faking one.

**Pending:**

- Whether the button's `selected` sprite is renamed to match its meaning (see the approved note
  below). Until then the atlas name stays as it is.
- Whether the focus accent appears after a mouse click or only during keyboard and gamepad
  navigation.
- The gold Selection treatment has no validated asset yet; it needs a tab and a slot test.

## Family conventions

**Proposed.** General conventions only. Final sizes and exact sprites are defined per family later.

| Family | Depth | Material | States | Notes |
| --- | --- | --- | --- | --- |
| Buttons | Pressable | Bronze frame, burgundy face | Hover, Pressed, Focus, Disabled | The only family with front thickness. Label on the face. One command per button. |
| Panels | Raised frame, flat interior | Bronze frame, dark interior | None | Non-interactive. Corner hardware allowed. Interior padding `space-3`. |
| Slots | Recessed | Bronze lip, dark well | Hover, Focus, Selection, Disabled | Content is an icon at an integer scale. No front thickness. Empty and filled share the frame. |
| Tabs | Raised | Bronze, burgundy face | Hover, Focus, Selection, Disabled | The active tab is a persistent selection and reads as joined to its panel. |
| Inputs | Recessed | Bronze lip, dark well | Hover, Focus, Disabled | Focus also means "typing here". Text uses Body. |
| Bars | Recessed track, flat fill | Bronze track, role-coloured fill | Disabled only | The fill grows in whole units, in flat colour steps, with no gradients. |
| Tooltips | Flat | Dark interior, thin bronze edge | None | Never interactive, never covers the element it describes. |

Shared rules:

- All states of a component share canvas size, pivot and 9-slice.
- A family has one frame construction. Variants change size or content, not material or lighting.
- A component is added only when no existing family can express it.

## Pixel-art composition and 9-slice

**Approved** (from `UiScalingArchitecture.md` and the button validation): one art pixel per canvas
unit, integer sizes and positions, point filtering, no non-integer scale, and 9-slice borders that
scale with the integer factor.

**Proposed:**

- Colours come from the atlas palette. The contact shadow (`100A11` at 59% opacity) is the only
  translucent colour; no other partial transparency and no anti-aliasing.
- A 9-slice centre is one flat colour. Top and bottom strips are constant along X; left and right
  strips are constant along Y. Any detail that would break this lives in a corner.
- Slices stretch; they do not tile. Textures or dithering are therefore not allowed in strips.
- When part of a component moves between states, the moving art must stay inside the borders so
  the shared slice still holds. The button's top border is 11 for this reason.
- Elements in a row share a baseline and a face height, not just a canvas height.
- Icons are drawn on the pixel grid and shown at integer multiples of their source size.

Acceptance checklist for any new component:

1. It uses the three materials and the single light.
2. It belongs to one depth level and one family.
3. Its states use only their own channels.
4. Its 9-slice passes the flat-centre and constant-strip rule in every state.
5. Its sizes are even integers and it respects minimum target and minimum slice size.
6. A Game view capture at an integer scale matches the source sprite pixel for pixel.

## Known conflicts

Recorded, not resolved, by this document:

- `UiButtonFeedback` scales controls to 1.05 on hover and selection and 0.95 on press. That breaks
  the pixel grid and duplicates the Pressed channel.
- Panel transitions that use a 0.96 hidden scale animate pixel art at a non-integer scale.
- The Charcoal atlas and its bindings predate these rules. Its button uses a different slice and no
  front thickness.
- Atlas v14 holds components drawn before these foundations (slots, tabs, inputs, bars). They are
  candidates for review, not references.

## Decisions to validate with real layouts

- Inventory + Equipment proportions at 960×540: slot size, grid gutter and panel padding.
- Whether `space-3` panel padding and `space-0` grid gutters hold at Inventory density.
- Secondary font choice and the smallest legible text at ×1 scale.
- Selection gold against bronze frames, and Focus teal against burgundy, in a full grid of slots.
- Negative and Caution colours on real components.
- Rarity and resource colour treatment, with Game Design.
- Target sizes for gamepad navigation.
- How much ornament a dense screen tolerates before it competes with content.
