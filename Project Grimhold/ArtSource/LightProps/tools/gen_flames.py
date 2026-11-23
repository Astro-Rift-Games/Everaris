"""Builds lit-base sprites and flame animation frames for Brazier, Candle and Candles.

Usage: python -I gen_flames.py <props_dir> <out_dir>
Frame 0 of every flame is the original flame extracted from <Prop>-Lit.png, so
base + frame 0 reproduces the lit sprite pixel-exactly.
"""
import math
import os
import sys
from collections import Counter

from PIL import Image

FIRE = [(161, 49, 22), (194, 82, 36), (211, 138, 63), (225, 173, 82), (238, 201, 136)]
FIRE_SET = set(FIRE)
SIZE = 128


def load(path):
    return Image.open(path).convert("RGBA")


def split_lit(lit, unlit, ember_rows=None):
    """Returns (base image, flame pixels dict). Fire pixels become the flame; under them the
    base shows the unlit pixel re-lit with the colour the lit art uses for it elsewhere."""
    relight = Counter()
    for y in range(SIZE):
        for x in range(SIZE):
            lp, up = lit.getpixel((x, y)), unlit.getpixel((x, y))
            if lp[3] and up[3] and lp[:3] not in FIRE_SET:
                relight[(up, lp)] += 1
    mapping = {}
    for (u, l), n in relight.most_common():
        mapping.setdefault(u, l)

    base = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    flame = {}
    for y in range(SIZE):
        for x in range(SIZE):
            lp, up = lit.getpixel((x, y)), unlit.getpixel((x, y))
            if lp[3] and lp[:3] in FIRE_SET:
                flame[(x, y)] = lp
                if up[3]:
                    under = mapping.get(up, up)
                    if ember_rows and y >= ember_rows[0]:
                        under = ember_rows[1](up)
                    base.putpixel((x, y), under)
            elif lp[3]:
                base.putpixel((x, y), lp)
    return base, flame


def deform(flame, t, frames, amp, stretch, phase=0.0, region=None):
    """Sways rows by height (bottom anchored) and stretches the tip."""
    pts = {p: c for p, c in flame.items() if region is None or region(p)}
    rest = {p: c for p, c in flame.items() if p not in pts}
    if not pts:
        return dict(flame)
    ys = [y for _, y in pts]
    top, bottom = min(ys), max(ys)
    h = max(1, bottom - top)
    w = 2 * math.pi * t / frames + phase
    sway = math.sin(w) - math.sin(phase)
    tip = round(stretch * (math.sin(2 * w + 0.7) - math.sin(2 * phase + 0.7)))
    out = dict(rest)
    for (x, y), c in pts.items():
        k = (bottom - y) / h
        dx = round(amp * (k ** 1.4) * sway)
        out[(x + dx, y)] = c
    if tip > 0:
        tops = sorted(p for p in out if p[1] == top and (region is None or region(p)))
        for x, y in tops[:1]:
            out[(x, y - 1)] = FIRE[0]
    elif tip < 0:
        for p in [p for p in list(out) if p[1] == top and (region is None or region(p))]:
            del out[p]
    return out


def add_ember(frame, t, start, path):
    if 0 <= t - start < len(path):
        frame[path[t - start]] = FIRE[1]


def render(pixels):
    im = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    for (x, y), c in pixels.items():
        im.putpixel((x, y), c if len(c) == 4 else c + (255,))
    return im


def main(props, out):
    os.makedirs(out, exist_ok=True)

    def coals(up):
        lum = sum(up[:3])
        return FIRE[0] + (255,) if lum > 150 else (110, 36, 20, 255)

    specs = {
        # name: (frames, sway amp, tip stretch, ember rows (y >= row) recolour, extra)
        "Brazier": dict(frames=10, ember_from=57),
        "Candle": dict(frames=6),
        "Candles": dict(frames=8),
    }
    for name, spec in specs.items():
        lit = load(os.path.join(props, f"{name}-Lit.png"))
        unlit = load(os.path.join(props, f"{name}-Unlit.png"))
        ember = (spec["ember_from"], coals) if "ember_from" in spec else None
        base, flame = split_lit(lit, unlit, ember)
        base.save(os.path.join(out, f"{name}-Lit-Base.png"))

        frames = []
        n = spec["frames"]
        for t in range(n):
            if name == "Brazier":
                f = deform(flame, t, n, amp=2.0, stretch=1.0, region=lambda p: p[1] <= 62)
                add_ember(f, t, 2, [(66, 51), (67, 49), (67, 47)])
                add_ember(f, t, 6, [(61, 50), (60, 48), (60, 46)])
            elif name == "Candle":
                f = deform(flame, t, n, amp=1.0, stretch=1.0, region=lambda p: p[1] <= 59)
            else:
                f = deform(flame, t, n, amp=1.0, stretch=1.0, region=lambda p: p[0] >= 61)
                f = deform(f, t, n, amp=1.0, stretch=1.0, phase=math.pi, region=lambda p: p[0] < 61 and p[1] >= 53)
            if t == 0:
                assert f == flame, f"{name} frame 0 must equal the original flame"
            frames.append(render(f))

        sheet = Image.new("RGBA", (SIZE * n, SIZE), (0, 0, 0, 0))
        for i, fr in enumerate(frames):
            sheet.paste(fr, (i * SIZE, 0))
            fr.save(os.path.join(out, f"{name}-Flame-{i}.png"))
        sheet.save(os.path.join(out, f"{name}-Flame-Sheet.png"))

        # base + frame 0 must rebuild the lit art exactly
        rebuilt = base.copy()
        rebuilt.alpha_composite(frames[0])
        assert list(rebuilt.getdata()) == list(lit.getdata()), f"{name}: base + frame 0 != lit"
        print(name, "frames", n, "flame px", len(flame), "base+frame0 == lit OK")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
