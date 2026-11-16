-- Walk rework builder. Runs inside Aseprite (File > Scripts, or the MCP
-- run_lua tool with `WALK_REWORK_ROOT` set). For each facing it:
--   1. loads the source sheets (copies of the Unity PNGs) for the base body
--      and every equipment set,
--   2. rebuilds each 96x96 frame from the stamps in walk_recipes.lua,
--   3. saves an editable .aseprite (one group per skin, one layer per module,
--      8 frames at 12 fps, "Walk" tag),
--   4. exports every sheet whose pixels changed as a 768x96 PNG that keeps the
--      original file name and frame layout.

local ROOT = WALK_REWORK_ROOT
assert(ROOT, "WALK_REWORK_ROOT is not set")

local FRAME, COUNT, FRAME_SECONDS = 96, 8, 1 / 12
local MODULES = { "Legs", "Body", "LeftHand", "Head", "RightHand" } -- bottom to top, as sorted in NetworkPlayer.prefab
local DIRS = { "S", "N", "SE", "SW", "NE", "NW" }
local MIRROR_OF = { SW = "SE", NW = "NE" }

local SKINS = {
  { name = "Base", parts = { Legs = "Legs", Body = "Body", Head = "Head", LeftHand = "LeftHand", RightHand = "RightHand" } },
  { name = "ArcaneMage", parts = { Legs = "Trousers", Body = "Garb", Head = "Hat", LeftHand = "LeftGlove", RightHand = "RightGlove" } },
  { name = "ArmyRanger", parts = { Legs = "Trousers", Body = "Armor", Head = "Hat", LeftHand = "LeftGlove", RightHand = "RightGlove" } },
  { name = "FireMage", parts = { Legs = "Trousers", Body = "Garb", Head = "Hood", LeftHand = "LeftGlove", RightHand = "RightGlove" } },
  { name = "ForestRanger", parts = { Legs = "Trousers", Body = "LeatherArmor", Head = "Hood", LeftHand = "LeftGlove", RightHand = "RightGlove" } },
  { name = "HeavyArmor", parts = { Legs = "LegPlate", Body = "Breastplate", Head = "Helmet", LeftHand = "LeftGauntlet", RightHand = "RightGauntlet" } },
  { name = "LightArmor", parts = { Legs = "ChainMailTrousers", Body = "ChainMailArmor", Head = "OpenSallet", LeftHand = "LeftGlove", RightHand = "RightGlove" } },
}

local function relPath(skin, module, anim, dir)
  local part = skin.parts[module]
  if skin.name == "Base" then
    return part .. "/" .. anim .. "/Player-" .. part .. "-" .. anim .. "-" .. dir .. "-Sheet.png"
  end
  return "Sets/Sets/" .. skin.name .. "/" .. anim .. "/Set-" .. skin.name .. "-" .. part .. "-" .. anim .. "-" .. dir .. "-Sheet.png"
end

local function mirrorStamp(stamp)
  local out = { a = stamp.a, f = stamp.f, dx = stamp.dx and -stamp.dx or nil, dy = stamp.dy }
  if stamp.r then
    out.r = { FRAME - 1 - stamp.r[3], stamp.r[2], FRAME - 1 - stamp.r[1], stamp.r[4] }
  end
  if stamp.s then
    out.s = {}
    for i, span in ipairs(stamp.s) do
      out.s[i] = { span[1], FRAME - 1 - span[3], FRAME - 1 - span[2] }
    end
  end
  return out
end

local function recipeFor(recipes, dir)
  local source = MIRROR_OF[dir]
  if not source then return recipes[dir] or {} end
  local mirrored = {}
  local own = recipes[dir] or {}
  for module, frames in pairs(own) do mirrored[module] = frames end -- authored for this facing
  for module, frames in pairs(recipes[source] or {}) do
    if not own[module] and module ~= "LeftHand" and module ~= "RightHand" then
    mirrored[module] = {}
    for index, stamps in pairs(frames) do
      local list = {}
      for i, stamp in ipairs(stamps) do list[i] = mirrorStamp(stamp) end
      mirrored[module][index] = list
    end
    end
  end
  return mirrored
end

local sheetCache = {}
local function loadSheet(skin, module, anim, dir)
  local path = ROOT .. "/source/" .. relPath(skin, module, anim, dir)
  if sheetCache[path] == nil then
    local image = Image { fromFile = path }
    assert(image, "missing source sheet: " .. path)
    assert(image.colorMode == ColorMode.RGB, "source sheet is not RGBA: " .. path)
    sheetCache[path] = image
  end
  return sheetCache[path]
end

local function spansOf(stamp)
  if stamp.s then return stamp.s end
  local rect = stamp.r or { 0, 0, FRAME - 1, FRAME - 1 }
  local spans = {}
  for y = rect[2], rect[4] do spans[#spans + 1] = { y, rect[1], rect[3] } end
  return spans
end

local function buildFrame(skin, module, dir, index, stamps)
  local frame = Image(FRAME, FRAME, ColorMode.RGB)
  frame:clear()
  if not stamps then
    stamps = { { f = index } } -- untouched frame: straight copy of the source
  end
  for _, stamp in ipairs(stamps) do
    local sheet = loadSheet(skin, module, stamp.a or "Walk", dir)
    local dx, dy = stamp.dx or 0, stamp.dy or 0
    local spans = spansOf(stamp)
    if not stamp.r and not stamp.s then
      -- whole-frame copy: one blit instead of 9216 pixel reads
      local crop = Image(sheet, Rectangle(stamp.f * FRAME, 0, FRAME, FRAME))
      frame:drawImage(crop, Point(dx, dy))
      spans = {}
    end
    for _, span in ipairs(spans) do
      local y = span[1]
      for x = span[2], span[3] do
        local color = sheet:getPixel(stamp.f * FRAME + x, y)
        if app.pixelColor.rgbaA(color) ~= 0 then
          frame:drawPixel(x + dx, y + dy, color)
        end
      end
    end
  end
  return frame
end

local function exportSheet(frames, path)
  local sheet = Image(FRAME * COUNT, FRAME, ColorMode.RGB)
  sheet:clear()
  for index = 0, COUNT - 1 do
    sheet:drawImage(frames[index], Point(index * FRAME, 0))
  end
  sheet:saveAs(path)
end

local function addLayer(sprite, group, name, frames)
  local layer = sprite:newLayer()
  layer.name = name
  if group then layer.parent = group end
  for index = 0, COUNT - 1 do
    sprite:newCel(layer, index + 1, frames[index], Point(0, 0))
  end
  return layer
end

local function buildDirection(recipes, dir)
  local recipe = recipeFor(recipes, dir)
  local sprite = Sprite(FRAME, FRAME, ColorMode.RGB)
  for _ = 2, COUNT do sprite:newEmptyFrame() end
  for index = 1, COUNT do sprite.frames[index].duration = FRAME_SECONDS end
  local starter = sprite.layers[1]

  -- reference: untouched legs of the base body, hidden by default
  local reference = {}
  for index = 0, COUNT - 1 do reference[index] = buildFrame(SKINS[1], "Legs", dir, index, nil) end
  local refLayer = addLayer(sprite, nil, "REF Legs (original)", reference)
  refLayer.isVisible = false

  local exported = 0
  for _, skin in ipairs(SKINS) do
    local group = sprite:newGroup()
    group.name = skin.name
    group.isVisible = (skin.name == "Base")
    for _, module in ipairs(MODULES) do
      local frames = {}
      local moduleRecipe = recipe[module]
      for index = 0, COUNT - 1 do
        frames[index] = buildFrame(skin, module, dir, index, moduleRecipe and moduleRecipe[index])
      end
      addLayer(sprite, group, skin.parts[module], frames)
      -- export only sheets whose pixels actually changed (some overlays already
      -- had the corrected frame, so the recipe is a no-op for them)
      local changed = false
      if moduleRecipe then
        for index = 0, COUNT - 1 do
          if not frames[index]:isEqual(buildFrame(skin, module, dir, index, nil)) then changed = true end
        end
      end
      if changed then
        local out = ROOT .. "/png/" .. relPath(skin, module, "Walk", dir)
        app.fs.makeAllDirectories(app.fs.filePath(out))
        exportSheet(frames, out)
        exported = exported + 1
      end
    end
  end

  sprite:deleteLayer(starter)
  local tag = sprite:newTag(1, COUNT)
  tag.name = "Walk_" .. dir
  app.fs.makeAllDirectories(ROOT .. "/aseprite")
  sprite:saveAs(ROOT .. "/aseprite/Walk_" .. dir .. ".aseprite")
  sprite:close()
  return exported
end

local recipes = dofile(ROOT .. "/tools/walk_recipes.lua")
local total = 0
for _, dir in ipairs(WALK_REWORK_DIRS or DIRS) do
  local count = buildDirection(recipes, dir)
  total = total + count
  print(dir .. ": " .. count .. " sheets exported")
end
print("total sheets exported: " .. total)
