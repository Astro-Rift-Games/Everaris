# Feature: catacombs-doors

Objective: replace the 20 static door tiles of CatacombsMap with networked DungeonDoor instances that show the map's own double door, animated in Aseprite.
Branch: New-Testing (no commits until authorized). Delivery: ask-on-risk. Test runner: Unity EditMode/PlayMode via Unity CLI.

## Specs

- S1 (L1, L2): "a las puertas habria que hacer las animaciones usando Aseprite MCP". The animation uses the map's double wooden door (Catacombs-Atlas door tiles, 2x4 cells), not the existing single-leaf Door-Open-Sheet.
- S2 (L1): doors gain the existing NetworkDoor behaviour ("no tienen interaccion ni colision"): interaction prompt, blocker, pathfinding refresh, vision/projectile occlusion, Host Migration restore. Reuse NetworkDoor/DungeonDoorPresenter unchanged.
- S3: a CatacombsDoor prefab variant of DungeonDoor carries the new art and controller (Door_Closed, Door_Opening_North, Door_Open_North, Door_Closing_North, 0.4 s like the base).
- S4: a palette brush places a door so the closed sprite reproduces the door tiles pixel-exact on the grid; the 20 doors of the enabled floor are placed and their door tiles removed from Access (17) and Walls (3 arch rows).
- S5: CatacombsSceneCompositionTests stops forbidding NetworkDoor and instead pins 20 CatacombsDoor instances aligned to the grid with no leftover door tiles.
- Out of scope: P1 floor doors (P1_Access, P1_Walls; floor disabled), stairs collision (separate fix), visible South opening (presenter animates North only).
- Assumption: open frames show the doorway floor (Floor tiles exist under every door).

## Tasks

- [x] T1 S1: Aseprite source + Door sheet (closed, 2 intermediate, open) from the atlas door tiles. — route: inline. Evidence: frame 0 == atlas tiles (generator assert); Aseprite-exported sheet == generated frames.
- [x] T2 S3: import, clips, controller, CatacombsDoor variant; EditMode tests. — route: inline. RED 5/6 (missing assets) -> GREEN.
- [x] T3 S4: door brush + place 20 doors, remove door tiles; scene tests. — route: inline. RED scene test (0 doors) -> GREEN 6/6.
- [ ] T4 S2, S5: suites green (see L6); minimap tests pass without regeneration; manual Host/Client check pending for user.

## Log

- L1 (2026-11-23, user): "estaba pensando que ahora mismo las puertas son tiles, no tienen interaccion ni colision, creo que estan en el tilemap de access, lo mismo para las escaleras, deberia aplicar lo mismo para estos assets? no implementes nada aun"
- L2 (2026-11-23, user): "claro las escaleras no tienen colisiones. Y a las puertas habria que hacer las animaciones usando Aseprite MCP" / "Sí, arrancá"
- L3 (user): stairs are decorative for now; they will lead to P1 later.
- L4 evidence: Access holds 31 clusters; the door is 8 atlas tiles (Catacombs-Atlas, rects x 1616-1632, y 1328-1376), 20 doors on the enabled floor (17 fully in Access, 3 with the arch row in Walls). Floor tiles exist under every door; Walls on both sides. Existing DungeonDoor art is a single 32x32 leaf (TASK-276), different from the map's double 32x~46 door. CatacombsMap was made playable in 21792276 with doors explicitly out of scope.
- L5 T1 done: ArtSource/CatacombsDoor (aseprite, tools/gen_door.py, README); Assets/Art/Props/Doors/Catacombs-Door-Sheet.png (128x48, 4 frames 32x48). Decision: the stone-cap top tile row stays a tile; the prefab draws the lower 3 rows, so Walls collision of the 3 doors whose cap row lives in Walls is unchanged. Next: T2 needs the Unity Editor open (closed at this point).
- L6 T2/T3 evidence: Catacombs-Door-Sheet imported like Door-Open-Sheet (PPU 16, Point, uncompressed), 4 sprites 32x48 pivot (16,0); clips Catacombs_Door_* copy base timing/loop/binding with base frame k -> Catacombs frame 3-k; CatacombsDoor.overrideController over DungeonDoor.controller; CatacombsDoor.prefab variant (View sprite set via SerializedObject on LoadPrefabContents; a plain instance edit lost the sprite override); CatacombsDoorBrush (anchor centre, offset (0.5,-0.5)). CatacombsMap: new empty "Doors" tilemap under Grid, 20 painted doors, 120 Access tiles removed (verified against the first door's 6 lower sprites before removal); diff = 43 added objects, only Access and Grid changed. Suites: CatacombsDoorTests 6/6, DungeonDoorStateRules 23/23, OpenDirectionRules 8/8, DungeonDoorAnimation 12/12, DungeonDoorPlayModeTests 19/19 (async), CatacombsRaidScene 5/5, CatacombsMinimapScene 3/3, CollapseRift 7/7, GameplayCamera 4/4, brushes 18/18; CatacombsSceneComposition 7/9 with the 2 pre-existing failures (AmbientDustParticles, sanctuary m_IsActive overrides). Pending: manual Host + Client check in CatacombsMap; commit authorization.
- L7 (2026-11-23, user, with screenshot of the open door): "hay que ajustar la animacion, la puerta queda por delante de lo que seria el marco". Fix: leaves now render under the static frame, recede (60/25/0 % width, lift 6 px, darken up to 55 %) and disappear behind the wall when open. Re-exported from Aseprite (layer order Leaves under Frame); CatacombsDoorTests 6/6, DungeonDoorAnimationTests 12/12.
- L8 (2026-11-23, user): "Bien, commiteá" after reviewing the corrected animation in game. Committed on New-Testing. Still open in T4: an explicit Host + Client multiplayer pass (not reported by the user).
- L9 (2026-11-23, user): "En las puertas si el player se acerca demasiado el arma la traspasa, y no probe pero estoy casi seguro que se puede atacar a lo que este del otro lado de esta manera" / "y porque no lo corregiste?". Fixes: melee target query now occludes by WorldCollision from the attacker's body (abilities unchanged); CatacombsDoor blocker covers the 4-row wall depth (offset 0,2 size 2.125x4) and its interaction trigger reaches 0.75 past both faces; the door sprite became 32x64 and draws the stone cap (40 cap tiles removed from Access); 3 door caps had been painted in Walls (now gone) and the Walls composite geometry was stale until a forced TilemapCollider2D rebuild. Evidence: CatacombsAccessTraversalTests 9/9 (closed door stops a north body at y+4), CatacombsDoorTests 7/7, MeleeAttackTests 18/18, Physics2DAttackTargetQueryTests 21/21.
