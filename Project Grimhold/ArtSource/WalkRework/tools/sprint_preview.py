"""Checks and previews for the Sprint sheets in ../../SprintAnimation/png.

    python sprint_preview.py            # validate, then write GIFs and contact sheets

Left side of every GIF is the current Walk (the sheets in ../png), right side the Sprint.
Both use the in-game hand depth rule (camera-side half of the swing over the body) and each side
loops at its own cadence.
"""
import glob
import os
import sys

import numpy as np
from PIL import Image

import preview as P

SPRINT_ROOT = os.path.normpath(os.path.join(P.ROOT, "..", "SprintAnimation"))
WALK_MS = 100    # Walk as shipped: 10 fps
SPRINT_MS = 70   # proposal: about 14 fps, a 0.56 s cycle
failures = []


def check(condition, message):
    if not condition:
        failures.append(message)


def sprint_path(skin, module, direction):
    return os.path.join(SPRINT_ROOT, "png", P.rel_path(skin, module, direction, "Sprint"))


def load_sprint(skin, module, direction, version=None):
    sheet = Image.open(sprint_path(skin, module, direction)).convert("RGBA")
    return [sheet.crop((i * P.FRAME, 0, (i + 1) * P.FRAME, P.FRAME)) for i in range(P.COUNT)]


def rgba(path):
    return np.array(Image.open(path).convert("RGBA"))


def colors(array):
    return {tuple(c) for c in np.unique(array[array[..., 3] > 0].reshape(-1, 4), axis=0)}


def validate():
    skins = ["Base"] + list(P.SKINS)
    sheets = glob.glob(os.path.join(SPRINT_ROOT, "png", "**", "*.png"), recursive=True)
    check(len(sheets) == len(skins) * len(P.MODULES) * len(P.DIRS), f"expected 210 sheets, found {len(sheets)}")
    for skin in skins:
        for direction in P.DIRS:
            for module in P.MODULES:
                path = sprint_path(skin, module, direction)
                label = os.path.basename(path)
                image = Image.open(path)
                check(image.size == (768, 96) and image.mode == "RGBA", f"{label}: expected 768x96 RGBA")
                out = rgba(path)
                check(set(np.unique(out[..., 3])) <= {0, 255}, f"{label}: semi-transparent pixels")
                # every pixel comes from the same module's Walk or Idle source sheet
                walk = os.path.join(P.ROOT, "source", P.rel_path(skin, module, direction, "Walk"))
                allowed = colors(rgba(walk))
                idle = os.path.join(P.ROOT, "source", P.rel_path(skin, module, direction, "Idle"))
                if os.path.exists(idle):
                    allowed |= colors(rgba(idle))
                check(colors(out) <= allowed, f"{label}: colours outside the source palette")
                if module in P.HANDS:
                    # a hand frame is the shipped hand moved as a whole: same pixels, same count
                    source = rgba(walk)
                    for i in range(P.COUNT):
                        a = out[:, i * 96:(i + 1) * 96]
                        b = source[:, i * 96:(i + 1) * 96]
                        check(int((a[..., 3] > 0).sum()) == int((b[..., 3] > 0).sum()) and colors(a) == colors(b),
                              f"{label} frame {i}: hand is not the shipped sprite moved whole")
            # leg overlays stay inside the base legs, except where their Idle sheet already spills
            if skin != "Base":
                base = rgba(sprint_path("Base", "Legs", direction))[..., 3] > 0
                over = rgba(sprint_path(skin, "Legs", direction))[..., 3] > 0
                idle_base = rgba(os.path.join(P.ROOT, "source", P.rel_path("Base", "Legs", direction, "Idle")))[:, :96, 3] > 0
                idle_over = rgba(os.path.join(P.ROOT, "source", P.rel_path(skin, "Legs", direction, "Idle")))[:, :96, 3] > 0
                check(not (over & ~base).any() or (idle_over & ~idle_base).any(),
                      f"{skin} legs {direction}: overlay pixels outside the base legs")
    for east, west in (("SE", "SW"), ("NE", "NW")):
        for skin in skins:
            for module in ("Legs", "Body", "Head"):
                def mirrored(a, b):
                    return all((np.array(x)[:, ::-1] == np.array(y)).all() for x, y in zip(a, b))

                # some headgear and armor are drawn asymmetric on purpose: only sheets whose
                # shipped Walk is mirrored have to stay mirrored
                shipped = mirrored(P.load_frames(skin, module, east, "before"),
                                   P.load_frames(skin, module, west, "before"))
                check(not shipped or mirrored(load_sprint(skin, module, east), load_sprint(skin, module, west)),
                      f"{skin} {module} {east}/{west}: mirror symmetry lost")
    print("lowest leg row per frame (walk | sprint)")
    for direction in P.DIRS:
        walk = " ".join(str(f.getbbox()[3] - 1) for f in P.load_frames("Base", "Legs", direction, "after"))
        sprint = " ".join(str(f.getbbox()[3] - 1) for f in load_sprint("Base", "Legs", direction))
        print(f"  {direction:2s}  {walk} | {sprint}")


def gifs(scale=8):
    out_dir = os.path.join(SPRINT_ROOT, "previews")
    os.makedirs(out_dir, exist_ok=True)
    for skin in ["Base"] + list(P.SKINS):
        for direction in P.DIRS:
            walk = P.composite(direction, "after", skin)
            sprint = P.composite(direction, "after", skin, loader=load_sprint)
            total = P.COUNT * WALK_MS * SPRINT_MS // 10
            cuts = sorted(set(range(0, total, WALK_MS)) | set(range(0, total, SPRINT_MS))) + [total]
            frames, durations = [], []
            for start, end in zip(cuts, cuts[1:]):
                left = P.on_background(walk[(start // WALK_MS) % P.COUNT], scale)
                right = P.on_background(sprint[(start // SPRINT_MS) % P.COUNT], scale)
                pair = Image.new("RGBA", (left.width * 2 + scale, left.height), (20, 20, 24, 255))
                pair.paste(left, (0, 0))
                pair.paste(right, (left.width + scale, 0))
                frames.append(pair.convert("P", palette=Image.ADAPTIVE, colors=64))
                durations.append(end - start)
            frames[0].save(os.path.join(out_dir, f"Sprint_{direction}_{skin}_WalkVsSprint.gif"), save_all=True,
                           append_images=frames[1:], duration=durations, loop=0, disposal=2)
    print("gifs written to", out_dir)


def contact_sheet(skin="Base", scale=5):
    width, height = (P.CROP[2] - P.CROP[0]) * scale, (P.CROP[3] - P.CROP[1]) * scale
    gap = scale * 2
    sheet = Image.new("RGBA", (P.COUNT * width, len(P.DIRS) * (2 * height + gap)), (20, 20, 24, 255))
    for row, direction in enumerate(P.DIRS):
        for line, frames in enumerate((P.composite(direction, "after", skin),
                                       P.composite(direction, "after", skin, loader=load_sprint))):
            for index, frame in enumerate(frames):
                sheet.paste(P.on_background(frame, scale), (index * width, row * (2 * height + gap) + line * height))
    out = os.path.join(SPRINT_ROOT, "previews", f"Sprint_WalkVsSprint_ContactSheet_{skin}.png")
    sheet.save(out)
    print(out)


if __name__ == "__main__":
    validate()
    if failures:
        print("FAILED")
        for failure in failures:
            print(" -", failure)
        sys.exit(1)
    print("all checks passed")
    gifs()
    contact_sheet("Base")
    contact_sheet("HeavyArmor")
