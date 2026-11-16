"""Structural checks for the reworked Walk sheets in ../png.

Run from anywhere:  python validate.py
Exits non-zero when a check fails.
"""
import glob
import hashlib
import os
import sys

import numpy as np
from PIL import Image

import preview as P

ROOT = P.ROOT
UNITY_ART = os.path.normpath(os.path.join(ROOT, "..", "..", "Assets", "Art", "Character"))
failures = []


def check(condition, message):
    if not condition:
        failures.append(message)


def rgba(path):
    return np.array(Image.open(path).convert("RGBA"))


def colors(array):
    opaque = array[array[..., 3] > 0]
    return {tuple(c) for c in np.unique(opaque.reshape(-1, 4), axis=0)}


def md5(path):
    with open(path, "rb") as handle:
        return hashlib.md5(handle.read()).hexdigest()


outputs = sorted(glob.glob(os.path.join(ROOT, "png", "**", "*.png"), recursive=True))
print("reworked sheets:", len(outputs))

# 1. Format, hard alpha, palette, and that the source copies still match Unity.
for path in outputs:
    rel = os.path.relpath(path, os.path.join(ROOT, "png"))
    image = Image.open(path)
    check(image.size == (768, 96) and image.mode == "RGBA", f"{rel}: expected 768x96 RGBA, got {image.size} {image.mode}")
    out = rgba(path)
    check(set(np.unique(out[..., 3])) <= {0, 255}, f"{rel}: semi-transparent pixels")
    source = os.path.join(ROOT, "source", rel)
    allowed = colors(rgba(source))
    idle = source.replace("-Walk-", "-Idle-").replace(os.sep + "Walk" + os.sep, os.sep + "Idle" + os.sep)
    if os.path.exists(idle):
        allowed |= colors(rgba(idle))
    check(colors(out) <= allowed, f"{rel}: colours outside the source palette {colors(out) - allowed}")
    unity = os.path.join(UNITY_ART, rel)
    check(os.path.exists(unity), f"{rel}: no matching Unity asset")
    # before the rework is applied Unity holds the original; afterwards, the reworked sheet
    check(md5(unity) in (md5(source), md5(path)), f"{rel}: Unity asset is neither the original nor the reworked sheet")

# 2. Sheets the rework does not touch (Idle legs used as a pixel source) are unchanged in Unity.
for source in glob.glob(os.path.join(ROOT, "source", "**", "*.png"), recursive=True):
    rel = os.path.relpath(source, os.path.join(ROOT, "source"))
    if not os.path.exists(os.path.join(ROOT, "png", rel)):
        check(md5(os.path.join(UNITY_ART, rel)) == md5(source), f"{rel}: Unity original changed")

# 3. Equipment overlays keep fitting the reworked base silhouette. Leg overlays may
#    only spill where their own Idle sheet already spills (boot heels drawn one
#    pixel wider than the bare foot); body overlays must not spill more than before.
def frame0_mask(skin, module, direction, anim):
    sheet = rgba(os.path.join(ROOT, "source", P.rel_path(skin, module, direction, anim)))
    return sheet[:, :96, 3] > 0


print("\noverlay pixels outside the base silhouette (before -> after)")
for direction in P.DIRS:
    for module in ("Legs", "Body", "Head"):
        base_rel = P.rel_path("Base", module, direction)
        base_out = os.path.join(ROOT, "png", base_rel)
        if not os.path.exists(base_out):
            continue
        base_new = rgba(base_out)[..., 3] > 0
        base_old = rgba(os.path.join(ROOT, "source", base_rel))[..., 3] > 0
        for skin in P.SKINS:
            rel = P.rel_path(skin, module, direction)
            new_path = os.path.join(ROOT, "png", rel)
            old = rgba(os.path.join(ROOT, "source", rel))[..., 3] > 0
            new = rgba(new_path)[..., 3] > 0 if os.path.exists(new_path) else old
            spill_new, spill_old = int((new & ~base_new).sum()), int((old & ~base_old).sum())
            if spill_new != spill_old and module == "Legs":
                print(f"  {direction:2s} {skin:12s} {module}: {spill_old} -> {spill_new}")
            if module == "Legs":
                idle_spills = bool((frame0_mask(skin, module, direction, "Idle")
                                    & ~frame0_mask("Base", module, direction, "Idle")).any())
                check(spill_new == 0 or idle_spills, f"{rel}: {spill_new} overlay pixels outside the base legs")
            else:
                # garments are drawn slightly wider than the body, so compare frame by frame:
                # no reworked frame may spill more than the worst original frame did
                per_new = [int((new & ~base_new)[:, i * 96:(i + 1) * 96].sum()) for i in range(8)]
                per_old = [int((old & ~base_old)[:, i * 96:(i + 1) * 96].sum()) for i in range(8)]
                check(max(per_new) <= max(per_old), f"{rel}: overlay spill per frame grew {per_old} -> {per_new}")

# 4. West facings remain exact mirrors of the east facings.
for east, west in (("SE", "SW"), ("NE", "NW")):
    for skin in ["Base"] + list(P.SKINS):
        for module in ("Legs", "Body", "Head"):
            a = P.load_frames(skin, module, east, "after")
            b = P.load_frames(skin, module, west, "after")
            a0 = P.load_frames(skin, module, east, "before")
            b0 = P.load_frames(skin, module, west, "before")
            was_mirror = all((np.array(x)[:, ::-1] == np.array(y)).all() for x, y in zip(a0, b0))
            is_mirror = all((np.array(x)[:, ::-1] == np.array(y)).all() for x, y in zip(a, b))
            check(is_mirror or not was_mirror, f"{skin} {module} {east}/{west}: mirror symmetry lost")

# 5. Hands: every frame is the original hand sprite moved by whole pixels (same pixels,
#    same shape), gloves move exactly like the bare hand, and west hands stay mirrors of
#    the opposite east hand. The offsets are written out because the Walk clips key the
#    weapon grips on each hand frame and must be shifted by the same amount.
def hand_offsets(skin, module, direction):
    offsets = []
    for before, after in zip(P.load_frames(skin, module, direction, "before"),
                             P.load_frames(skin, module, direction, "after")):
        b, a = before.getbbox(), after.getbbox()
        dx, dy = a[0] - b[0], a[1] - b[1]
        moved = Image.new("RGBA", before.size)
        moved.paste(before.crop(b), (b[0] + dx, b[1] + dy))
        check(np.array_equal(np.array(moved), np.array(after)), f"{skin} {module} {direction}: frame is not a pure translation")
        offsets.append((dx, dy))
    return offsets


grip_lines = ["facing,hand,frame,dx_px,dy_px,grip_dx_units,grip_dy_units"]
print("\nhand offsets per frame (pixels)")
for direction in P.DIRS:
    for module in ("LeftHand", "RightHand"):
        base = hand_offsets("Base", module, direction)
        for skin in P.SKINS:
            check(hand_offsets(skin, module, direction) == base, f"{skin} {module} {direction}: glove does not follow the hand")
        print(f"  {direction:2s} {module:9s} {base}")
        for index, (dx, dy) in enumerate(base):
            grip_lines.append(f"{direction},{module},{index},{dx},{dy},{dx / 16},{-dy / 16}")
for east, west in (("SE", "SW"), ("NE", "NW")):
    for skin in ["Base"] + list(P.SKINS):
        for east_hand, west_hand in (("LeftHand", "RightHand"), ("RightHand", "LeftHand")):
            def mirrored(version):
                a = P.load_frames(skin, east_hand, east, version)
                b = P.load_frames(skin, west_hand, west, version)
                return all((np.array(x)[:, ::-1] == np.array(y)).all() for x, y in zip(a, b))

            # silhouettes mirror; shading does not in the shipped art, so compare alpha
            def alpha_mirrored(version):
                a = P.load_frames(skin, east_hand, east, version)
                b = P.load_frames(skin, west_hand, west, version)
                return all((np.array(x)[:, ::-1, 3] == np.array(y)[..., 3]).all() for x, y in zip(a, b))

            check(mirrored("after") or not mirrored("before"),
                  f"{skin} {east} {east_hand} / {west} {west_hand}: mirror symmetry lost")
            check(alpha_mirrored("after") or not alpha_mirrored("before"),
                  f"{skin} {east} {east_hand} / {west} {west_hand}: mirrored silhouette lost")
with open(os.path.join(ROOT, "hand_grip_offsets.csv"), "w", newline="\n") as handle:
    handle.write("\n".join(grip_lines) + "\n")

# 6. Foot line per frame (lowest opaque row of the base legs), before -> after.
print("\nlowest leg row per frame (before | after)")
for direction in P.DIRS:
    rows = []
    for version in ("before", "after"):
        frames = P.load_frames("Base", "Legs", direction, version)
        rows.append(" ".join(str(f.getbbox()[3] - 1) for f in frames))
    print(f"  {direction:2s}  {rows[0]} | {rows[1]}")

print()
if failures:
    print("FAILED")
    for failure in failures:
        print(" -", failure)
    sys.exit(1)
print("all checks passed")
