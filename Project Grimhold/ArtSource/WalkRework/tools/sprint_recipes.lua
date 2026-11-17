-- Sprint recipes. Same stamp format as walk_recipes.lua; every frame is built
-- from the shipped Walk and Idle sheets, so the recipe applies to the base body
-- and to every equipment overlay alike.
--
-- Where the Walk is careful and grounded, the Sprint is a heavy run, the run
-- of someone carrying armor:
--   * a flight phase: on frames 3 and 7 both legs are tucked, so no foot
--     touches the ground line; toward and away from the camera the whole
--     figure also rises one pixel;
--   * one leg carries the weight at a time and sweeps from a full forward
--     reach to a full trail in three frames, landing under the centre line;
--   * weight: torso, head and hands sink one extra pixel when the foot lands
--     (frames 1 and 5), on top of the original bounce and its "up" beat;
--   * the upper body leans one pixel into the direction of travel;
--   * the hands pump at chest height. Toward and away from the camera they
--     swing front to back beside the body along a downward arc; on the
--     diagonals they keep the original path, raised and widened.
--
-- Frame count and order match the Walk: contact on 0 and 4, down on 1 and 5,
-- push-off on 2 and 6, flight on 3 and 7.

local P = dofile(WALK_REWORK_ROOT .. "/tools/walk_recipes.lua")._parts
local sLeg, nLeg, join, seN, seF = P.sLeg, P.nLeg, P.join, P.seN, P.seF

-- Landing frames: everything above the legs sinks this many extra pixels.
local LANDING = { [1] = true, [5] = true }
local LANDING_SINK = 1
-- Flight frames: toward and away from the camera a run shows no forward travel, so the whole
-- figure leaves the ground instead. The diagonals already read as a run without it.
local FLIGHT = { [3] = true, [7] = true }
local FLIGHT_RISE = { S = -1, N = -1 }

-- Copy of a stamp list with every stamp moved by the same offset.
local function moved(stamps, dx, dy)
  local out = {}
  for i, stamp in ipairs(stamps) do
    out[i] = { a = stamp.a, f = stamp.f, r = stamp.r, s = stamp.s,
               dx = (stamp.dx or 0) + dx, dy = (stamp.dy or 0) + dy }
  end
  return out
end

local function airborne(facing, frames)
  for index in pairs(FLIGHT) do frames[index] = moved(frames[index], 0, FLIGHT_RISE[facing]) end
  return frames
end

local R = {}

---------------------------------------------------------------------------
-- South. Seen from the front a run differs from a walk in three ways, all
-- exaggerated here: the planted foot lands under the centre line (one pixel
-- inward), it sweeps two rows per frame (60 -> 58 -> 56) instead of staying
-- put, and both legs tuck under the body while the figure is off the ground.
-- The lifted leg is stamped first so the planted one overlaps it.
---------------------------------------------------------------------------
local function sRun(side, state)
  local x0, x1, lift, high, inward = 43, 47, 0, 1, 1
  if side == "R" then x0, x1, lift, high, inward = 48, 52, 4, 5, -1 end
  local function idle(y0, y1, dy) return { a = "Idle", f = 0, r = { x0, y0, x1, y1 }, dx = inward, dy = dy } end
  if state == "REACH" then      -- contact far ahead, foot on row 60
    return { idle(54, 56, 0), idle(56, 56, 1), idle(56, 58, 2) }
  elseif state == "PLANT" then  -- weight on the leg, foot on row 58
    return { idle(54, 58, 0) }
  elseif state == "PUSH" then   -- foot far behind, row 56
    return { idle(54, 54, 0), idle(57, 58, -2) }
  elseif state == "KNEE" then   -- knee driven toward the camera
    return { { f = high, r = { x0, 54, x1, 56 } } }
  elseif state == "LIFT" then
    return { { f = lift, r = { x0, 54, x1, 56 } } }
  elseif state == "TUCK" then   -- flight: knee up, foot off the ground
    return { { f = lift, r = { x0, 54, x1, 56 } } }
  end
end

R.S = {
  Legs = airborne("S", {
    [0] = join(sRun("R", "LIFT"), sRun("L", "REACH")),  -- contact
    [1] = join(sRun("R", "KNEE"), sRun("L", "PLANT")),  -- landing
    [2] = join(sRun("R", "LIFT"), sRun("L", "PUSH")),   -- push off
    [3] = join(sRun("L", "TUCK"), sRun("R", "TUCK")),   -- flight
    [4] = join(sRun("L", "LIFT"), sRun("R", "REACH")),
    [5] = join(sRun("L", "KNEE"), sRun("R", "PLANT")),
    [6] = join(sRun("L", "LIFT"), sRun("R", "PUSH")),
    [7] = join(sRun("R", "TUCK"), sRun("L", "TUCK")),
  }),
}

---------------------------------------------------------------------------
-- North. Forward is up-screen, so the planted foot lands short (row 56) and
-- sweeps toward the camera (58, then 60). Same centre-line landing and tuck.
---------------------------------------------------------------------------
local function nRun(side, state)
  local x0, x1, top, lift, high, pre, inward = 43, 47, 0, 4, 5, 3, 1
  if side == "R" then x0, x1, top, lift, high, pre, inward = 48, 52, 4, 0, 1, 7, -1 end
  local hip = { f = top, r = { x0, 54, x1, 54 }, dx = inward }
  local function idle(y0, y1, dy) return { a = "Idle", f = 0, r = { x0, y0, x1, y1 }, dx = inward, dy = dy } end
  if state == "REACH" then      -- contact far ahead, foot on row 56
    return { hip, idle(57, 58, -2) }
  elseif state == "PLANT" then  -- foot on row 58
    return { hip, idle(55, 58, 0) }
  elseif state == "PUSH" then   -- foot far behind, toward the camera, row 60
    return { hip, idle(55, 55, 0), idle(55, 55, 1), idle(55, 55, 2), idle(56, 58, 2) }
  elseif state == "KNEE" then
    return { { f = high, r = { x0, 54, x1, 56 } } }
  elseif state == "LIFT" then
    return { { f = lift, r = { x0, 54, x1, 56 } } }
  elseif state == "TUCK" then
    return { { f = pre, r = { x0, 54, x1, 56 } } }
  end
end

local nCrotch = { { f = 3, r = { 47, 53, 48, 53 } } } -- visible while the body is on its up beat

R.N = {
  Legs = airborne("N", {
    [0] = join(nRun("R", "LIFT"), nRun("L", "REACH")),
    [1] = join(nRun("R", "KNEE"), nRun("L", "PLANT")),
    [2] = join(nRun("R", "LIFT"), nRun("L", "PUSH")),
    [3] = join(nRun("L", "TUCK"), nRun("R", "TUCK"), nCrotch),
    [4] = join(nRun("L", "LIFT"), nRun("R", "REACH")),
    [5] = join(nRun("L", "KNEE"), nRun("R", "PLANT")),
    [6] = join(nRun("L", "LIFT"), nRun("R", "PUSH")),
    [7] = join(nRun("R", "TUCK"), nRun("L", "TUCK"), nCrotch),
  }),
}

---------------------------------------------------------------------------
-- South-East: the Walk's retimed stride at its full reach. The flight frames
-- pair one leg lifting off with the other still reaching, neither planted.
---------------------------------------------------------------------------
R.SE = {
  Legs = {
    [0] = {
      seF(1), seN(1),
      { f = 0, r = { 43, 57, 43, 57 }, dx = 1 },
      { f = 0, r = { 43, 58, 43, 58 }, dx = 2 },
    },
    [1] = { seN(2) },
    [2] = { seF(2), seN(3) },
    [3] = { seF(4), seN(4) },    -- flight: far leg already lifting, near leg reaching
    [4] = { seF(4), seN(5) },    -- near-leg contact at full reach
    [5] = { seF(6), seN(6) },
    [6] = {
      seF(7),
      -- the hip corner at x44 is left out: the leaning torso no longer covers it
      { f = 7, s = { { 54, 45, 47 }, { 55, 45, 48 }, { 56, 45, 48 }, { 57, 45, 48 } } },
      { f = 7, s = { { 58, 44, 47 }, { 59, 44, 47 } }, dx = 1 },
    },
    [7] = {                      -- flight: far leg reaching, near leg toeing off
      seF(0), seN(1),
      { f = 0, r = { 43, 57, 43, 57 }, dx = 1 },
      { f = 0, r = { 43, 58, 43, 58 }, dx = 2 },
    },
  },
}

---------------------------------------------------------------------------
-- North-East: the Walk's retimed stride with the near leg trailing fully.
---------------------------------------------------------------------------
R.NE = {
  Legs = {
    [0] = { { f = 3 } },
    [1] = {
      { f = 4, s = { { 54, 50, 51 }, { 55, 49, 51 }, { 56, 49, 51 }, { 57, 49, 51 } }, dx = -1 },
      { f = 4, s = { { 53, 44, 45 }, { 54, 44, 48 }, { 55, 43, 48 }, { 56, 43, 48 } } },
    },
    [2] = { { f = 5, s = P.neNear[5] } },
    [3] = { { f = 6 } },
    [4] = { { f = 7 } },
    [5] = {
      { f = 0, s = P.neFar0 },
      { f = 0, s = { { 54, 47, 51 }, { 55, 47, 50 }, { 56, 47, 50 }, { 57, 47, 50 } } },
      { f = 0, s = { { 58, 48, 51 }, { 59, 48, 51 } }, dx = -1 },
    },
    [6] = { { f = 1, s = P.neNear[1] } },
    [7] = { { f = 2 } },         -- full trail
  },
}

---------------------------------------------------------------------------
-- Torso and head. { dx, dy } leans each one into the direction of travel on
-- every frame; landing frames sink. Toward or away from the camera the torso
-- cannot show a lean, so only the head moves. On the diagonals head and torso
-- move together: an extra pixel on the head uncovers bare neck under armor.
-- West facings mirror the east offsets.
---------------------------------------------------------------------------
local lean = {
  S = { Body = { 0, 0 }, Head = { 0, 1 } },
  N = { Body = { 0, 0 }, Head = { 0, -1 } },
  SE = { Body = { 1, 0 }, Head = { 1, 0 } },
  NE = { Body = { 1, 0 }, Head = { 1, 0 } },
}
for facing, parts in pairs(lean) do
  for module, offset in pairs(parts) do
    local frames = {}
    for index = 0, 7 do
      frames[index] = { { f = index, dx = offset[1], dy = offset[2] + (LANDING[index] and LANDING_SINK or 0)
                                                + (FLIGHT[index] and FLIGHT_RISE[facing] or 0) } }
    end
    R[facing][module] = frames
  end
end
-- the shipped base body repeats the neutral frame on NE frame 7 while the head is on its up beat
R.NE.Body[7][1].f = 3

---------------------------------------------------------------------------
-- Hands, { dx, dy } per frame 0..7 applied to the shipped hand sprite.
--   S / N: a running arm swings front to back, not out to the side. The hand
--     stays beside the body (the Idle hand column), travels the full height
--     of the swing and dips at mid-swing, the bottom of the arc; there and at
--     the low end of the swing it tucks one pixel toward the body.
--   SE / NE: the original path widened by about a third around its middle,
--     raised two pixels to chest height and carried forward with the lean.
-- Landing frames sink with the torso. West facings: the left hand mirrors the
-- east right hand and vice versa.
---------------------------------------------------------------------------
local handOffsets = {
  S = { LeftHand = { { 2, -1 }, { 3, -1 }, { 2, -1 }, { -1, -1 }, { 1, -1 }, { 3, -3 }, { 1, -1 }, { -1, 1 } },
       RightHand = { { -1, -1 }, { -3, -3 }, { -1, -1 }, { 1, 1 }, { -2, -1 }, { -3, -1 }, { -2, -1 }, { 1, -1 } } },
  N = { LeftHand = { { -2, -1 }, { -3, -1 }, { -2, -1 }, { 1, -1 }, { -1, -1 }, { -3, -3 }, { -1, -1 }, { 1, 1 } },
       RightHand = { { 1, -1 }, { 3, -3 }, { 1, -1 }, { -1, 1 }, { 2, -1 }, { 3, -1 }, { 2, -1 }, { -1, -1 } } },
  SE = { LeftHand = { { 1, -3 }, { 0, -3 }, { 1, -3 }, { 2, -2 }, { 2, -1 }, { 1, -1 }, { 2, -1 }, { 2, -2 } },
       RightHand = { { 1, -1 }, { 2, -1 }, { 1, -1 }, { 0, -2 }, { 0, -3 }, { 0, -3 }, { 0, -3 }, { 0, -2 } } },
  NE = { LeftHand = { { 1, -3 }, { 2, -3 }, { 1, -3 }, { 0, -2 }, { 0, -1 }, { 1, -1 }, { 0, -1 }, { 0, -2 } },
       RightHand = { { 1, -1 }, { 0, -1 }, { 1, -1 }, { 2, -2 }, { 2, -3 }, { 2, -3 }, { 2, -3 }, { 2, -2 } } },
}
local function mirrorOffsets(offsets)
  local out = {}
  for index, offset in ipairs(offsets) do out[index] = { -offset[1], offset[2] } end
  return out
end
for west, east in pairs({ SW = "SE", NW = "NE" }) do
  handOffsets[west] = {
    LeftHand = mirrorOffsets(handOffsets[east].RightHand),
    RightHand = mirrorOffsets(handOffsets[east].LeftHand),
  }
end
for facing, hands in pairs(handOffsets) do
  R[facing] = R[facing] or {}
  for module, offsets in pairs(hands) do
    local frames = {}
    for index, offset in ipairs(offsets) do
      local frame = index - 1
      -- Hands sink on landing but do not rise on the flight frames: at mid-swing they hang at the
      -- bottom of the arc, in the camera-side half of the swing, where they cover the sides of the torso.
      frames[frame] = { { f = frame, dx = offset[1], dy = offset[2] + (LANDING[frame] and LANDING_SINK or 0) } }
    end
    R[facing][module] = frames
  end
end

return R
