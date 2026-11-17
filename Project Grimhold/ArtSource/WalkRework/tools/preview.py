"""Before/After previews for the Walk rework.

Reads the untouched copies in ../source and the reworked sheets in ../png
(falling back to the source for modules the rework did not touch) and writes
contact sheets and animated GIFs to ../previews. Scaling is nearest-neighbour.

    python preview.py sheet <out.png> [skin] [modules]   # contact sheet, all facings
    python preview.py gifs                               # Before/After GIFs per facing
"""
import os
import sys

from PIL import Image

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
FRAME, COUNT = 96, 8
BEFORE_MS = 80   # shipped clips: 12 fps (GIF delays are multiples of 10 ms)
AFTER_MS = 100   # proposed cadence: 10 fps, a 0.8 s cycle instead of 0.67 s
MODULES = ["Legs", "Body", "LeftHand", "Head", "RightHand"]  # NetworkPlayer.prefab sorting order
DIRS = ["S", "SE", "SW", "N", "NE", "NW"]
SKINS = {
    "ArcaneMage": dict(Legs="Trousers", Body="Garb", Head="Hat", LeftHand="LeftGlove", RightHand="RightGlove"),
    "ArmyRanger": dict(Legs="Trousers", Body="Armor", Head="Hat", LeftHand="LeftGlove", RightHand="RightGlove"),
    "FireMage": dict(Legs="Trousers", Body="Garb", Head="Hood", LeftHand="LeftGlove", RightHand="RightGlove"),
    "ForestRanger": dict(Legs="Trousers", Body="LeatherArmor", Head="Hood", LeftHand="LeftGlove", RightHand="RightGlove"),
    "HeavyArmor": dict(Legs="LegPlate", Body="Breastplate", Head="Helmet", LeftHand="LeftGauntlet", RightHand="RightGauntlet"),
    "LightArmor": dict(Legs="ChainMailTrousers", Body="ChainMailArmor", Head="OpenSallet", LeftHand="LeftGlove", RightHand="RightGlove"),
}
BACKGROUND = (70, 70, 80, 255)
CROP = (36, 32, 60, 64)  # region of the 96x96 frame the character occupies


def rel_path(skin, module, direction, anim="Walk"):
    if skin == "Base":
        return f"{module}/{anim}/Player-{module}-{anim}-{direction}-Sheet.png"
    part = SKINS[skin][module]
    return f"Sets/Sets/{skin}/{anim}/Set-{skin}-{part}-{anim}-{direction}-Sheet.png"


def load_frames(skin, module, direction, version):
    rel = rel_path(skin, module, direction)
    path = os.path.join(ROOT, "png", rel)
    if version == "before" or not os.path.exists(path):
        path = os.path.join(ROOT, "source", rel)
    sheet = Image.open(path).convert("RGBA")
    return [sheet.crop((i * FRAME, 0, (i + 1) * FRAME, FRAME)) for i in range(COUNT)]


HANDS = ("LeftHand", "RightHand")


def hands_in_front(module, direction, version, loader=None, per_facing=False):
    """Per frame: is this hand drawn over the body, as the Walk clips sort it in game?

    The shipped clips kept one depth per facing: over the body in S/SE/SW, behind it in
    N/NE/NW. The reworked clips sort each hand by where it is relative to the torso
    (DirectionalAnimationGenerator.ResolveLocomotionHandSortingOrders):
    - S / N: both arms swing along the view axis, so a hand is in front for the camera-side
      (lower) half of its swing and behind for the rest;
    - diagonals: the arm on the camera side of the torso stays in front for the whole cycle
      and the other one stays behind (right hand near in SE/NE, left hand near in SW/NW).
    """
    if version == "before" or per_facing:
        return [not direction.startswith("N")] * COUNT
    if len(direction) == 2:
        near = "RightHand" if direction.endswith("E") else "LeftHand"
        return [module == near] * COUNT
    heights = []
    for frame in (loader or load_frames)("Base", module, direction, version):
        box = frame.getbbox()
        heights.append((box[1] + box[3]) / 2)  # image rows grow downward
    middle = (min(heights) + max(heights)) / 2
    return [height > middle for height in heights]


def composite(direction, version, skin="Base", modules=MODULES, loader=None, per_facing_depth=False):
    """Base body with the optional equipment set drawn over each module, in game depth order.

    `loader(skin, module, direction, version)` returns the eight frames; it defaults to the Walk sheets.
    `per_facing_depth` keeps the hands on one side of the body for the whole cycle (over it in
    S/SE/SW, behind it in N/NE/NW) instead of the Walk's per-frame swing depth.
    """
    load = loader or load_frames
    frames = [Image.new("RGBA", (FRAME, FRAME)) for _ in range(COUNT)]

    def draw(module, wanted_front):
        front = (hands_in_front(module, direction, version, load, per_facing_depth)
                 if module in HANDS else [False] * COUNT)
        layers = [load("Base", module, direction, version)]
        if skin != "Base":
            layers.append(load(skin, module, direction, version))
        for layer in layers:
            for index, frame in enumerate(layer):
                if module not in HANDS or front[index] == wanted_front:
                    frames[index].alpha_composite(frame)

    for module in modules:          # hands behind the body go under everything
        if module in HANDS:
            draw(module, False)
    for module in modules:
        if module not in HANDS:
            draw(module, False)
    for module in modules:          # hands in front go over everything
        if module in HANDS:
            draw(module, True)
    return frames


def on_background(frame, scale):
    crop = frame.crop(CROP)
    tile = Image.new("RGBA", crop.size, BACKGROUND)
    tile.alpha_composite(crop)
    return tile.resize((tile.width * scale, tile.height * scale), Image.NEAREST)


def contact_sheet(out, skin="Base", modules=MODULES, scale=5, dirs=DIRS):
    width, height = (CROP[2] - CROP[0]) * scale, (CROP[3] - CROP[1]) * scale
    gap = scale * 2
    sheet = Image.new("RGBA", (COUNT * width, len(dirs) * (2 * height + gap)), (20, 20, 24, 255))
    for row, direction in enumerate(dirs):
        for line, version in enumerate(("before", "after")):
            for index, frame in enumerate(composite(direction, version, skin, modules)):
                sheet.paste(on_background(frame, scale), (index * width, row * (2 * height + gap) + line * height))
    sheet.save(out)
    print(out, sheet.size)


def gifs(scale=8):
    out_dir = os.path.join(ROOT, "previews")
    os.makedirs(out_dir, exist_ok=True)
    for skin in ["Base"] + list(SKINS):
        for direction in DIRS:
            before = composite(direction, "before", skin)
            after = composite(direction, "after", skin)
            # Both sides loop at their own cadence, so the timeline is cut wherever either
            # side changes frame and runs until both loops line up again.
            total = COUNT * BEFORE_MS * AFTER_MS // 20
            cuts = sorted(set(range(0, total, BEFORE_MS)) | set(range(0, total, AFTER_MS))) + [total]
            frames, durations = [], []
            for start, end in zip(cuts, cuts[1:]):
                left = on_background(before[(start // BEFORE_MS) % COUNT], scale)
                right = on_background(after[(start // AFTER_MS) % COUNT], scale)
                pair = Image.new("RGBA", (left.width * 2 + scale, left.height), (20, 20, 24, 255))
                pair.paste(left, (0, 0))
                pair.paste(right, (left.width + scale, 0))
                frames.append(pair.convert("P", palette=Image.ADAPTIVE, colors=64))
                durations.append(end - start)
            name = f"Walk_{direction}_{skin}_BeforeAfter.gif"
            frames[0].save(os.path.join(out_dir, name), save_all=True, append_images=frames[1:],
                           duration=durations, loop=0, disposal=2)
    print("gifs written to", out_dir)


if __name__ == "__main__":
    command = sys.argv[1]
    if command == "sheet":
        skin = sys.argv[3] if len(sys.argv) > 3 else "Base"
        modules = sys.argv[4].split(",") if len(sys.argv) > 4 else MODULES
        dirs = sys.argv[5].split(",") if len(sys.argv) > 5 else DIRS
        contact_sheet(sys.argv[2], skin, modules, dirs=dirs)
    elif command == "gifs":
        gifs()
