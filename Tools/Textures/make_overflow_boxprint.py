"""The Overflow look's box print atlas (2026-11-06, Docs/StyleSheet.md): the same places as the old BoxPrint_Atlas.png
(Generation/BoxPrintAtlas: shipping label, address label, this-side-up arrows, fragile stamp, maker's mark, the seam
line), drawn in the company's print: bold type, one or two colours, the amber and the stripe, a pictogram. The ink is a
little worn (as printed on card), the labels' corners rounded.

    python Tools/Textures/make_overflow_boxprint.py

Writes Assets/Backrooms/Textures/Overflow/OF_BoxPrint_Atlas.png (1024 x 512, transparent where there is no print).
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Backrooms", "Textures", "Overflow", "OF_BoxPrint_Atlas.png")
W, H = 1024, 512

AMBER = (240, 162, 30)
ORANGE, GOLD, AVO = (217, 102, 31), (232, 178, 58), (124, 138, 46)
BROWN = (59, 42, 30)
INK = (46, 34, 26)
CREAM = (236, 228, 206)
RED = (196, 52, 34)


def font(size):
    for f in ("bahnschrift.ttf", "arialbd.ttf", "segoeuib.ttf"):
        try:
            fn = ImageFont.truetype(os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", f), size)
            if f == "bahnschrift.ttf":
                try:
                    fn.set_variation_by_name("Bold")
                except Exception:
                    pass
            return fn
        except OSError:
            continue
    return ImageFont.load_default()


def box(r):
    """A BoxPrintAtlas rect (UV, origin bottom left) in pixels (origin top left)."""
    x, y, w, h = r
    return int(x * W), int((1 - y - h) * H), int((x + w) * W), int((1 - y) * H)


img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(img)


def text_c(cx, cy, s, size, fill, d=d):
    f = font(size)
    l, t, r, b = d.textbbox((0, 0), s, font=f)
    d.text((cx - (r - l) / 2 - l, cy - (b - t) / 2 - t), s, font=f, fill=fill)


def stripe(x0, y0, x1, h):
    for i, c in enumerate((ORANGE, GOLD, AVO)):
        d.rectangle((x0, y0 + i * h, x1, y0 + (i + 1) * h - 1), fill=c)


# The shipping label: cream, an amber head with the company's name, the stripe, chunky lines, a bold barcode.
x0, y0, x1, y1 = box((0.008, 0.62, 0.292, 0.37))
d.rounded_rectangle((x0, y0, x1, y1), 14, fill=CREAM, outline=BROWN, width=5)
d.rounded_rectangle((x0 + 10, y0 + 10, x1 - 10, y0 + 56), 8, fill=AMBER)
text_c((x0 + x1) / 2, y0 + 33, "OVERFLOW", 38, BROWN)
stripe(x0 + 10, y0 + 62, x1 - 10, 5)
for i, w in enumerate((0.85, 0.6, 0.72)):
    d.rounded_rectangle((x0 + 18, y0 + 88 + i * 18, x0 + 18 + (x1 - x0 - 36) * w, y0 + 98 + i * 18), 4, fill=INK)
rng = np.random.default_rng(7)
bx = x0 + 18
while bx < x0 + 200:
    bw = int(rng.choice([4, 6, 9]))
    d.rectangle((bx, y1 - 52, bx + bw, y1 - 14), fill=INK)
    bx += bw + int(rng.choice([4, 6]))
d.rounded_rectangle((x1 - 76, y1 - 56, x1 - 18, y1 - 14), 6, outline=INK, width=5)
text_c(x1 - 47, y1 - 35, "A3", 24, INK)

# The address label: cream, the stripe down its left, "TO" and chunky lines.
x0, y0, x1, y1 = box((0.318, 0.62, 0.195, 0.37))
d.rounded_rectangle((x0, y0, x1, y1), 12, fill=CREAM, outline=BROWN, width=5)
for i, c in enumerate((ORANGE, GOLD, AVO)):
    d.rectangle((x0 + 10 + i * 7, y0 + 10, x0 + 16 + i * 7, y1 - 10), fill=c)
text_c(x0 + 58, y0 + 30, "TO", 30, BROWN)
for i, w in enumerate((0.7, 0.45, 0.62, 0.5, 0.35)):
    d.rounded_rectangle((x0 + 40, y0 + 58 + i * 23, x0 + 40 + (x1 - x0 - 60) * w, y0 + 70 + i * 23), 5, fill=INK)

# This side up: two chunky rounded arrows over a bar.
x0, y0, x1, y1 = box((0.53, 0.62, 0.195, 0.37))
for cx in (x0 + (x1 - x0) * 0.3, x0 + (x1 - x0) * 0.7):
    d.polygon([(cx, y0 + 18), (cx - 46, y0 + 78), (cx + 46, y0 + 78)], fill=BROWN)
    d.rounded_rectangle((cx - 16, y0 + 70, cx + 16, y1 - 40), 6, fill=BROWN)
d.rounded_rectangle((x0 + 12, y1 - 26, x1 - 12, y1 - 10), 6, fill=BROWN)

# Fragile: a red rounded stamp with the word and a cracked glass.
x0, y0, x1, y1 = box((0.742, 0.62, 0.25, 0.2))
d.rounded_rectangle((x0 + 4, y0 + 4, x1 - 4, y1 - 4), 16, outline=RED, width=9)
text_c((x0 + x1) / 2 + 30, (y0 + y1) / 2, "FRAGILE", 42, RED)
gx, gy = x0 + 44, (y0 + y1) / 2
d.polygon([(gx - 20, gy - 32), (gx + 20, gy - 32), (gx + 13, gy + 2), (gx - 13, gy + 2)], fill=RED)
d.rectangle((gx - 3, gy, gx + 3, gy + 24), fill=RED)
d.rounded_rectangle((gx - 16, gy + 22, gx + 16, gy + 30), 3, fill=RED)
d.line([(gx - 4, gy - 30), (gx + 3, gy - 18), (gx - 3, gy - 8)], fill=CREAM, width=4)

# The maker's mark: the company's: an amber disc, three waves spilling over its rim, the name under it.
x0, y0, x1, y1 = box((0.008, 0.12, 0.235, 0.45))
cx, cy, r = (x0 + x1) / 2, y0 + 92, 78
d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=AMBER, outline=BROWN, width=8)
for i, c in enumerate((ORANGE, GOLD, AVO)):
    yy = cy - 18 + i * 22
    pts = [(cx - r * 0.78 + k * 4, yy + 7 * np.sin(k * 0.22)) for k in range(int(r * 1.56 / 4) + 1)]
    d.line(pts, fill=c, width=13)
text_c(cx, y1 - 22, "OVERFLOW", 40, BROWN)

# The seam between the flaps: a dark line.
x0, y0, x1, y1 = box((0.5, 0.1, 0.4, 0.03))
d.rectangle((x0, y0 + 3, x1, y1 - 3), fill=(40, 30, 22))

# Worn ink: tiny dropouts and a soft edge, as print on card.
a = np.array(img).astype(np.float32)
noise = rng.random((H, W))
blob = np.array(Image.fromarray((rng.random((H // 8, W // 8)) * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC)) / 255.0
wear = (noise < 0.04) | ((blob > 0.78) & (noise < 0.35))
a[..., 3] = np.where(wear & (a[..., 3] > 0), a[..., 3] * 0.35, a[..., 3])
out = Image.fromarray(a.clip(0, 255).astype(np.uint8), "RGBA")
alpha = out.getchannel("A").filter(ImageFilter.GaussianBlur(0.6))
out.putalpha(alpha)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
out.save(OUT)
print("wrote", OUT)
