-- Walk rework recipes.
--
-- Every new frame is described as an ordered list of stamps. A stamp copies a
-- masked region of a SOURCE frame (same module, same skin) to the new frame
-- with an optional pixel offset. Because a stamp only relocates existing
-- pixels, the same recipe is valid for the base body module and for every
-- equipment overlay that mirrors it (trousers, armor, hats...).
--
-- Stamp fields:
--   a  = source animation sheet ("Walk" by default, or "Idle")
--   f  = source frame index (0-based)
--   r  = { x0, y0, x1, y1 } inclusive rectangle in frame coordinates, or
--   s  = { { y, x0, x1 }, ... } inclusive row spans
--        (no r/s = the whole 96x96 frame)
--   dx, dy = offset applied when writing (default 0)
--
-- SE and NE are authored; SW and NW are produced by mirroring the recipe
-- (the original SW/NW sheets are exact mirrors of SE/NE).

local R = {}

---------------------------------------------------------------------------
-- South. L = screen-left leg (x43..47), R = screen-right leg (x48..52).
-- Planted legs reuse the Idle leg (with its foot) so Walk and Idle share the
-- same ground line (row 58). Lifted legs reuse the foreshortened Walk stubs.
---------------------------------------------------------------------------
local function sLeg(side, state)
  local x0, x1 = 43, 47
  local lift, high = 0, 1
  if side == "R" then x0, x1 = 48, 52; lift, high = 4, 5 end
  if state == "MID" then      -- planted under the body, foot on row 58
    return { { a = "Idle", f = 0, r = { x0, 54, x1, 58 } } }
  elseif state == "EXT" then  -- contact: reaching forward, foot on row 59
    return {
      { a = "Idle", f = 0, r = { x0, 54, x1, 56 } },
      { a = "Idle", f = 0, r = { x0, 56, x1, 58 }, dy = 1 },
    }
  elseif state == "BACK" then -- trailing behind the body, foot on row 57
    return {
      { a = "Idle", f = 0, r = { x0, 54, x1, 55 } },
      { a = "Idle", f = 0, r = { x0, 57, x1, 58 }, dy = -1 },
    }
  elseif state == "LIFT" then -- passing, knee toward the camera
    return { { f = lift, r = { x0, 54, x1, 56 } } }
  elseif state == "HIGH" then -- just lifted on the body-down frame
    return { { f = high, r = { x0, 54, x1, 56 } } }
  end
end

local function join(...)
  local out = {}
  for _, list in ipairs({ ... }) do
    for _, stamp in ipairs(list) do out[#out + 1] = stamp end
  end
  return out
end

R.S = {
  Legs = {
    [0] = join(sLeg("L", "MID"), sLeg("R", "BACK")),  -- contact (short, cautious step)
    [1] = join(sLeg("L", "MID"), sLeg("R", "HIGH")),  -- down
    [2] = join(sLeg("L", "MID"), sLeg("R", "LIFT")),  -- passing
    [3] = join(sLeg("L", "BACK"), sLeg("R", "MID")),  -- up
    [4] = join(sLeg("R", "MID"), sLeg("L", "BACK")),
    [5] = join(sLeg("R", "MID"), sLeg("L", "HIGH")),
    [6] = join(sLeg("R", "MID"), sLeg("L", "LIFT")),
    [7] = join(sLeg("R", "BACK"), sLeg("L", "MID")),
  },
}

---------------------------------------------------------------------------
-- North. Forward is up-screen, so the forward leg is the SHORT one and the
-- trailing leg is the long one (toward the camera).
---------------------------------------------------------------------------
local function nLeg(side, state)
  local x0, x1 = 43, 47
  local top, lift, high, pre = 0, 4, 5, 3
  if side == "R" then x0, x1 = 48, 52; top, lift, high, pre = 4, 0, 1, 7 end
  local hip = { f = top, r = { x0, 54, x1, 54 } }
  if state == "MID" then
    return { hip, { a = "Idle", f = 0, r = { x0, 55, x1, 58 } } }
  elseif state == "FWD" then  -- contact ahead of the body, foot on row 57
    return {
      hip,
      { a = "Idle", f = 0, r = { x0, 55, x1, 55 } },
      { a = "Idle", f = 0, r = { x0, 57, x1, 58 }, dy = -1 },
    }
  elseif state == "TRAIL" then -- trailing toward the camera, foot on row 59
    return {
      hip,
      { a = "Idle", f = 0, r = { x0, 55, x1, 55 } },
      { a = "Idle", f = 0, r = { x0, 55, x1, 58 }, dy = 1 },
    }
  elseif state == "LIFT" then
    return { { f = lift, r = { x0, 54, x1, 56 } } }
  elseif state == "HIGH" then
    return { { f = high, r = { x0, 54, x1, 56 } } }
  elseif state == "PRE" then  -- reaching forward just before contact
    return { { f = pre, r = { x0, 54, x1, 56 } } }
  end
end

R.N = {
  Legs = {
    [0] = join(nLeg("L", "FWD"), nLeg("R", "MID")),
    [1] = join(nLeg("L", "MID"), nLeg("R", "HIGH")),
    [2] = join(nLeg("L", "MID"), nLeg("R", "LIFT")),
    [3] = join(nLeg("L", "MID"), nLeg("R", "PRE")),
    [4] = join(nLeg("R", "FWD"), nLeg("L", "MID")),
    [5] = join(nLeg("R", "MID"), nLeg("L", "HIGH")),
    [6] = join(nLeg("R", "MID"), nLeg("L", "LIFT")),
    [7] = join(nLeg("R", "MID"), nLeg("L", "PRE")),
  },
}

---------------------------------------------------------------------------
-- South-East. Near leg = light, far leg = dark. Per-leg masks of the source
-- frames (the shared outline column belongs to both masks; far is stamped
-- first, near on top).
---------------------------------------------------------------------------
local seNear = {
  [0] = { { 55, 44, 47 }, { 56, 43, 47 }, { 57, 43, 46 }, { 58, 43, 46 } },
  [1] = { { 54, 43, 45 }, { 55, 43, 47 }, { 56, 43, 47 }, { 57, 44, 46 }, { 58, 45, 46 } },
  [2] = { { 54, 46, 50 }, { 55, 46, 50 }, { 56, 46, 50 }, { 57, 47, 51 } },
  [3] = { { 53, 50, 51 }, { 54, 49, 51 }, { 55, 48, 52 }, { 56, 49, 53 } },
  [4] = { { 54, 49, 51 }, { 55, 47, 52 }, { 56, 49, 52 }, { 57, 50, 53 } },
  [5] = { { 54, 49, 52 }, { 55, 47, 52 }, { 56, 49, 52 }, { 57, 50, 53 }, { 58, 50, 53 }, { 59, 51, 54 } },
  [6] = { { 54, 48, 50 }, { 55, 47, 50 }, { 56, 47, 50 }, { 57, 47, 50 }, { 58, 48, 51 }, { 59, 48, 51 } },
  [7] = { { 54, 44, 47 }, { 55, 45, 48 }, { 56, 45, 48 }, { 57, 45, 48 }, { 58, 44, 47 }, { 59, 44, 47 } },
}
local seFar = {
  [0] = { { 54, 49, 52 }, { 55, 48, 53 }, { 56, 49, 52 } },
  [1] = { { 54, 49, 52 }, { 55, 48, 52 }, { 56, 49, 53 }, { 57, 50, 53 } },
  [2] = { { 54, 43, 45 }, { 55, 43, 46 }, { 56, 44, 46 }, { 57, 45, 47 } },
  [3] = { { 54, 44, 45 }, { 55, 44, 47 }, { 56, 44, 47 }, { 57, 44, 46 } },
  [4] = { { 54, 43, 45 }, { 55, 43, 46 }, { 56, 43, 46 } },
  [6] = { { 54, 43, 45 }, { 55, 44, 47 }, { 56, 45, 47 } },
  [7] = { { 54, 49, 51 }, { 55, 49, 52 }, { 56, 48, 51 } },
}
local function seN(f) return { f = f, s = seNear[f] } end
local function seF(f) return { f = f, s = seFar[f] } end

R.SE = {
  Legs = {
    -- far-leg contact; near leg toes off (outline closed on the two open toe pixels)
    [0] = {
      seF(1), seN(1),
      { f = 0, r = { 43, 57, 43, 57 }, dx = 1 },
      { f = 0, r = { 43, 58, 43, 58 }, dx = 2 },
    },
    [1] = { seN(2) },            -- near leg passes in front, far leg hidden under the body
    [2] = { seF(2), seN(3) },
    [3] = { seF(3), seN(4) },
    [4] = {                      -- near-leg contact, foot pulled in one pixel (shorter step)
      seF(4),
      { f = 5, s = { { 54, 49, 52 }, { 55, 47, 52 }, { 56, 49, 52 }, { 57, 50, 53 }, { 58, 50, 53 } } },
      { f = 5, s = { { 59, 51, 54 } }, dx = -1 },
    },
    [5] = { seF(6), seN(6) },
    [6] = {                      -- near foot straightened so the stance slides evenly
      seF(7),
      { f = 7, s = { { 54, 44, 47 }, { 55, 45, 48 }, { 56, 45, 48 }, { 57, 45, 48 } } },
      { f = 7, s = { { 58, 44, 47 }, { 59, 44, 47 } }, dx = 1 },
    },
    [7] = { seF(0), seN(0) },
  },
}

---------------------------------------------------------------------------
-- North-East. Near leg = light, far leg = dark. The original poses are
-- kept but retimed (rotated three frames) so each leg gets a four-frame
-- stance, and the far leg is hidden on the two frames where it crosses
-- behind the near leg instead of snapping from one side to the other.
---------------------------------------------------------------------------
local neNear = {
  [0] = { { 54, 47, 51 }, { 55, 47, 50 }, { 56, 47, 50 }, { 57, 47, 50 }, { 58, 48, 51 }, { 59, 48, 51 } },
  [1] = { { 54, 45, 48 }, { 55, 45, 48 }, { 56, 45, 48 }, { 57, 45, 48 }, { 58, 44, 47 }, { 59, 44, 47 } },
  [5] = { { 54, 45, 48 }, { 55, 45, 49 }, { 56, 45, 49 }, { 57, 45, 49 } },
}
local neFar0 = { { 54, 44, 47 }, { 55, 44, 47 }, { 56, 45, 47 } }

R.NE = {
  Legs = {
    [0] = { { f = 3 } },         -- far-leg contact
    [1] = {                      -- far leg starts travelling back under the body
      { f = 4, s = { { 54, 50, 51 }, { 55, 49, 51 }, { 56, 49, 51 }, { 57, 49, 51 } }, dx = -1 },
      { f = 4, s = { { 53, 44, 45 }, { 54, 44, 48 }, { 55, 43, 48 }, { 56, 43, 48 } } },
    },
    [2] = { { f = 5, s = neNear[5] } }, -- near leg passes, far leg hidden
    [3] = { { f = 6 } },
    [4] = { { f = 7 } },         -- near-leg contact
    [5] = {                      -- near foot straightened so the stance slides evenly
      { f = 0, s = neFar0 },
      { f = 0, s = { { 54, 47, 51 }, { 55, 47, 50 }, { 56, 47, 50 }, { 57, 47, 50 } } },
      { f = 0, s = { { 58, 48, 51 }, { 59, 48, 51 } }, dx = -1 },
    },
    [6] = { { f = 1, s = neNear[1] } }, -- far leg swings behind the planted near leg
    [7] = {                      -- near leg trails one pixel less (shorter step)
      { f = 2, s = { { 54, 50, 52 }, { 55, 50, 52 }, { 56, 49, 51 } } },
      { f = 2, s = { { 54, 43, 48 }, { 55, 43, 49 }, { 56, 43, 46 } } },
      { f = 2, s = { { 57, 42, 45 }, { 58, 42, 45 }, { 59, 42, 45 } }, dx = 1 },
    },
  },
}

---------------------------------------------------------------------------
-- Torso and head, every facing: the "up" beat (frames 3 and 7, body stretched
-- one row and head raised) is replaced by the neutral frame. The walk keeps
-- only the one-pixel dip when the weight lands, which reads as a careful,
-- grounded step instead of a bounce.
---------------------------------------------------------------------------
for _, facing in ipairs({ "S", "N", "SE", "NE" }) do
  R[facing].Body = { [3] = { { f = 0 } }, [7] = { { f = 0 } } }
  R[facing].Head = { [3] = { { f = 0 } }, [7] = { { f = 0 } } }
end

---------------------------------------------------------------------------
-- Hands, every facing: the swing is halved around the middle of its path so
-- the arms stay close to the body. Each frame is the original hand sprite
-- moved by a whole-pixel offset { dx, dy } (frames 0..7), so gloves and
-- gauntlets follow with the same recipe.
--
-- Hands swap sides when a facing is mirrored (the left hand of SW is the
-- mirror of the right hand of SE), so west offsets are derived below.
--
-- Moving a hand changes its opaque-pixel centroid: the Walk clips must have
-- their WeaponPose / OffHandGrip / MainHandGrip keys regenerated afterwards.
---------------------------------------------------------------------------
local handOffsets = {
  S = { LeftHand = { { 0, -1 }, { 1, -1 }, { 0, -1 }, { -1, 0 }, { 0, 2 }, { 1, 1 }, { 0, 2 }, { -1, 1 } },
       RightHand = { { 1, 2 }, { 0, 1 }, { 1, 2 }, { 1, 1 }, { 0, -1 }, { -1, -1 }, { 0, -1 }, { 1, 0 } } },
  N = { LeftHand = { { 0, -1 }, { -1, -1 }, { 0, -1 }, { 1, 0 }, { 1, 2 }, { 0, 1 }, { 1, 2 }, { 1, 1 } },
       RightHand = { { 0, 2 }, { 1, 1 }, { 0, 2 }, { -1, 1 }, { 0, -1 }, { 1, -1 }, { 0, -1 }, { -1, 0 } } },
  SE = { LeftHand = { { 0, 1 }, { 1, 1 }, { 0, 1 }, { -1, 0 }, { -1, -1 }, { 0, -1 }, { -1, -1 }, { -1, 0 } },
       RightHand = { { 0, -1 }, { -2, -1 }, { 0, -1 }, { 1, 0 }, { 2, 1 }, { 1, 1 }, { 2, 1 }, { 1, 0 } } },
  NE = { LeftHand = { { 0, 1 }, { -1, 1 }, { 0, 1 }, { 1, 0 }, { 1, -1 }, { 1, -1 }, { 1, -1 }, { 1, 0 } },
       RightHand = { { 0, -1 }, { 2, -1 }, { 0, -1 }, { -1, 0 }, { -2, 1 }, { -1, 1 }, { -2, 1 }, { -1, 0 } } },
}
-- West facings: the left hand mirrors the east right hand and vice versa.
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
      frames[index - 1] = { { f = index - 1, dx = offset[1], dy = offset[2] } }
    end
    R[facing][module] = frames
  end
end

return R
