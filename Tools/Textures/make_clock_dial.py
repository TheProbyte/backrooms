"""
The office wall clock's printed dial (Tools/Blender/build_furniture.py wall_clock, slot ClockDial): an off-white face,
minute ticks round the rim, heavier hour ticks, the numerals 1-12 in a plain sans, a small QUARTZ under the centre.
1024 x 1024 (the dial's 30 cm across the texture).

    python Tools/Textures/make_clock_dial.py Assets/Backrooms/Textures/ClockDial.png
"""
import math
import sys
from PIL import Image, ImageDraw, ImageFont

S = 1024
out = sys.argv[1] if len(sys.argv) > 1 else "ClockDial.png"
im = Image.new("RGB", (S, S), (236, 233, 224))
d = ImageDraw.Draw(im)
c = S / 2
R = S / 2 - 6
# A faint ring near the rim (the dial's printed edge), the minute track.
d.ellipse((c - R, c - R, c + R, c + R), outline=(70, 70, 70), width=4)
for m in range(60):
    a = math.radians(m * 6 - 90)
    hour = m % 5 == 0
    r0, r1 = R - (58 if hour else 28), R - 12
    w = 13 if hour else 4
    d.line((c + math.cos(a) * r0, c + math.sin(a) * r0, c + math.cos(a) * r1, c + math.sin(a) * r1), fill=(25, 25, 25), width=w)
try:
    font = ImageFont.truetype("arial.ttf", 96)
    small = ImageFont.truetype("arial.ttf", 28)
except OSError:
    font = small = ImageFont.load_default()
for h in range(1, 13):
    a = math.radians(h * 30 - 90)
    r = R - 125
    x, y = c + math.cos(a) * r, c + math.sin(a) * r
    t = str(h)
    bb = d.textbbox((0, 0), t, font=font)
    d.text((x - (bb[2] - bb[0]) / 2 - bb[0], y - (bb[3] - bb[1]) / 2 - bb[1]), t, fill=(20, 20, 20), font=font)
bb = d.textbbox((0, 0), "QUARTZ", font=small)
d.text((c - (bb[2] - bb[0]) / 2, c + 120), "QUARTZ", fill=(90, 90, 90), font=small)
im.save(out)                           # (a PNG's top row is the texture's top: 12 up, no flip)
print("wrote", out)
