"""Builds the Catacombs double-door frames from the map's door tiles.

Usage: python -I gen_door.py <atlas.png> <out_dir>
The door occupies 2x4 tiles in the atlas (x 1616-1647, y 1328-1391 bottom-origin). The top tile row
(stone cap) stays a tile in the map; frames cover the lower 3 rows (32x48): the static brick arch
corners plus two leaves hinged on their outer edges that swing North (away from the viewer).
Frame 0 is the closed door, identical to the tiles.
"""
import os
import sys

from PIL import Image

FRAME_COLORS = {(41, 41, 46), (52, 56, 62), (23, 23, 25)}  # brick corners + arch outline (static)
LEAF_W = 16
OPEN_FRACTIONS = [1.0, 0.6, 0.25, 0.0]  # closed, opening 1, opening 2, open (leaves behind the wall)
MAX_LIFT = 6  # px the free edge rises as the leaf swings away (North)
SHADOW = (20, 20, 26)  # leaves darken toward this colour as they recede from the light
MAX_DARKEN = 0.55


CAP_ROWS = 16  # top tile row: the stone cap, part of the static frame


def load_door(atlas_path):
    atlas = Image.open(atlas_path).convert("RGBA")
    h = atlas.size[1]
    return atlas.crop((1616, h - 1392, 1648, h - 1328))  # all 4 tile rows (cap + door): 32x64, top-origin


def split(door):
    frame = Image.new("RGBA", door.size, (0, 0, 0, 0))
    leaves = Image.new("RGBA", door.size, (0, 0, 0, 0))
    for y in range(door.size[1]):
        for x in range(door.size[0]):
            p = door.getpixel((x, y))
            if p[3] == 0:
                continue
            static = y < CAP_ROWS or p[:3] in FRAME_COLORS
            (frame if static else leaves).putpixel((x, y), p)
    return frame, leaves


def darken(p, amount):
    return tuple(round(c + (s - c) * amount) for c, s in zip(p[:3], SHADOW)) + (p[3],)


def swing(leaves, fraction):
    out = Image.new("RGBA", leaves.size, (0, 0, 0, 0))
    if fraction <= 0.0:
        return out  # fully open: both leaves are behind the wall
    width = max(1, round(LEAF_W * fraction))
    lift_total = round(MAX_LIFT * (1.0 - fraction))
    shade = MAX_DARKEN * (1.0 - fraction)
    for side in (0, 1):  # 0 = left leaf hinged at x=0, 1 = right leaf hinged at x=31
        for i in range(width):  # i = distance from hinge in the swung leaf
            src_i = min(LEAF_W - 1, int(i * LEAF_W / width))
            sx = src_i if side == 0 else 31 - src_i
            dx = i if side == 0 else 31 - i
            lift = round(lift_total * (i / max(1, width - 1)))
            for y in range(leaves.size[1]):
                p = leaves.getpixel((sx, y))
                if p[3] and 0 <= y - lift:
                    out.putpixel((dx, y - lift), darken(p, shade) if shade > 0 else p)
    return out


def main(atlas_path, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    door = load_door(atlas_path)
    frame, leaves = split(door)
    frames = []
    for fraction in OPEN_FRACTIONS:
        # Leaves first, frame on top: a swinging leaf goes behind the wall, never over the brick or arch.
        img = swing(leaves, fraction)
        img.alpha_composite(frame)
        frames.append(img)
    assert list(frames[0].getdata()) == list(door.getdata()), "closed frame must equal the door tiles"
    frame.save(os.path.join(out_dir, "Catacombs-Door-Frame.png"))
    sheet = Image.new("RGBA", (32 * len(frames), 64), (0, 0, 0, 0))
    for i, img in enumerate(frames):
        img.save(os.path.join(out_dir, f"Catacombs-Door-{i}.png"))
        sheet.paste(img, (32 * i, 0))
    sheet.save(os.path.join(out_dir, "Catacombs-Door-Sheet.png"))
    print("frames", len(frames), "closed == tiles OK")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
