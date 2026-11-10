"""Paints the style lab suit's 2D textures (2026-11-06). Run with the normal Python (needs Pillow):
    python Tools/Blender/face_textures.py
Writes to Assets/Backrooms/Textures/StyleLab/:
  T_FacePaint_Atlas.png   the painted eyes and mouth: three sets side by side (Tired, Wide, Calm), each in four rows
                          from the top: open, blinking, talking (the mouth open), scared (white-ringed eyes, small
                          pupils, a small open mouth). HazmatAppearance picks the cell through the material's tiling and
                          offset: scale (1/3, 1/4), offset (set/3, 1 - (row + 1)/4).
  T_VisorFog.png          the visor glass: clear but for breath fog at the bottom, a few runs, and a glare streak.
  T_FaceShade.png         soft shading painted on every face: warm cheeks, a shadow under the brows, under the nose
                          and under the lower lip.
  T_FaceStubble.png       stubble on the jaw, chin and upper lip (the Stubble facial hair choice).
  T_LabelFont.png         white letters on clear for the name tape and the back number (DejaVu Sans Mono, a free
                          font that ships with Blender), 8 x 6 cells in the order of LABEL_CHARS.
Every face picture covers a 0.12 x 0.12 m square of the face seen from the front: x -0.06..0.06, z from 0.07 below
the head centre to 0.05 above it (the square the face decals' UVs use). Clear everywhere but the paint, so it works on
any skin tone. Faces follow the usual advice for simple faces: the eyes carry the expression."""
import math, os, random
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'Backrooms', 'Textures', 'StyleLab')
os.makedirs(OUT, exist_ok=True)
FONT = r'F:\Blender\5.2\datafiles\fonts\DejaVuSansMono.woff2'
LABEL_CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-.#' "

SIZE, SS = 1024, 4
PX = SIZE * SS / 0.12
INK = (26, 18, 16, 255)
BAG = (70, 44, 34, 150)
SHINE = (255, 250, 240, 255)


def to_px(x, dz):
    return (x + 0.06) * PX, (0.05 - dz) * PX


def canvas():
    return Image.new('RGBA', (SIZE * SS, SIZE * SS), (0, 0, 0, 0))


def dot(d, p, r, fill):
    d.ellipse((p[0] - r, p[1] - r, p[0] + r, p[1] + r), fill=fill)


def round_line(d, pts, w, fill):
    d.line(pts, fill=fill, width=int(w), joint='curve')
    dot(d, pts[0], w / 2, fill)
    dot(d, pts[-1], w / 2, fill)


def arc_line(d, cx, cy, w, h, a0, a1, lw, fill, steps=24):
    """A curve along the ellipse centred (cx, cy) with radii (w, h), angles a0..a1 (degrees, 0 = right, 90 = down)."""
    pts = [(cx + w * math.cos(math.radians(a0 + (a1 - a0) * i / steps)),
            cy + h * math.sin(math.radians(a0 + (a1 - a0) * i / steps))) for i in range(steps + 1)]
    round_line(d, pts, lw, fill)


def masked_oval(im, box, cut_top=None):
    m = Image.new('L', im.size, 0)
    ImageDraw.Draw(m).ellipse(box, fill=255)
    if cut_top is not None:
        ImageDraw.Draw(m).rectangle((0, 0, im.size[0], cut_top), fill=0)
    im.paste(Image.new('RGBA', im.size, INK), (0, 0), m)


def closed_eye(d, cx, cy, w, lw, sx, droop=0.0):
    """A shut eye: the lid as a gentle downward curve, a short lash at the outer corner."""
    arc_line(d, cx, cy - w * 0.35, w * 1.05, w * 0.55, 15, 165, lw, INK)
    o = cx + sx * w * 1.0
    round_line(d, [(o, cy - w * 0.2 + droop), (o + sx * w * 0.25, cy - w * 0.05 + droop)], lw * 0.8, INK)


SCLERA = (246, 241, 230, 255)


def scared_face(kind):
    """Eyes wide with fright (white, a ring round them, small pupils) and a small open mouth, for any set."""
    im = canvas()
    d = ImageDraw.Draw(im)
    s = PX
    for sx in (-1, 1):
        cx, cy = to_px(sx * 0.034, 0.012)
        w, h = 0.0135 * s, 0.0158 * s
        d.ellipse((cx - w, cy - h, cx + w, cy + h), fill=SCLERA, outline=INK, width=int(0.0026 * s))
        r = 0.0047 * s
        dot(d, (cx + sx * w * 0.08, cy - h * 0.05), r, INK)
        dot(d, (cx - w * 0.25, cy - h * 0.35), 0.0017 * s, SHINE)
    cx, cy = to_px(0.0, -0.051)
    d.ellipse((cx - 0.0065 * s, cy - 0.0058 * s, cx + 0.0065 * s, cy + 0.0058 * s), fill=INK)
    return im.resize((SIZE, SIZE), Image.LANCZOS)


def face_set(kind, frame='open'):
    if frame == 'scared':
        return scared_face(kind)
    blink = frame == 'blink'
    im = canvas()
    d = ImageDraw.Draw(im)
    s = PX
    for sx in (-1, 1):
        cx, cy = to_px(sx * 0.034, 0.010)
        if kind == 'Tired':
            w, h = 0.0128 * s, 0.0138 * s
            lw = 0.0033 * s
            lid_y = cy - h * 0.25
            if blink:
                closed_eye(d, cx, cy + h * 0.15, w, lw, sx, droop=w * 0.15)
            else:
                masked_oval(im, (cx - w, cy - h, cx + w, cy + h), cut_top=lid_y)
                x0, x1 = cx - w * 1.25, cx + w * 1.25
                outer, inner = (x1, x0) if sx > 0 else (x0, x1)
                round_line(d, [(inner, lid_y - lw * 0.2), (cx, lid_y - lw * 0.6), (outer, lid_y + lw * 0.9)], lw, INK)
                dot(d, (cx - w * 0.3, lid_y + h * 0.3), 0.0027 * s, SHINE)
            arc_line(d, cx, cy + h * 0.7, w * 1.1, h * 0.55, 25, 155, 0.0017 * s, BAG)
        elif kind == 'Wide':
            w, h = 0.0122 * s, 0.0158 * s
            lw = 0.0026 * s
            if blink:
                closed_eye(d, cx, cy, w, lw * 1.2, sx)
            else:
                masked_oval(im, (cx - w, cy - h, cx + w, cy + h))
                arc_line(d, cx, cy + h * 0.15, w * 1.18, h * 1.18, 200, 340, lw, INK)   # the upper lash line
                dot(d, (cx - w * 0.32, cy - h * 0.38), 0.0034 * s, SHINE)
                dot(d, (cx + w * 0.3, cy + h * 0.3), 0.0014 * s, SHINE)
        else:   # Calm
            w, h = 0.0135 * s, 0.0095 * s
            lw = 0.0034 * s
            lid_y = cy - h * 0.55
            if blink:
                arc_line(d, cx, cy - h * 0.6, w * 1.15, h * 0.85, 20, 160, lw, INK)
            else:
                masked_oval(im, (cx - w, cy - h, cx + w, cy + h), cut_top=lid_y)
                round_line(d, [(cx - w * 1.2, lid_y + lw * 0.3), (cx + w * 1.2, lid_y + lw * 0.3)], lw, INK)
                dot(d, (cx - w * 0.3, lid_y + h * 0.55), 0.0025 * s, SHINE)
            round_line(d, [(cx - w * 0.7, cy + h * 1.6), (cx + w * 0.7, cy + h * 1.6)], 0.0013 * s, BAG)
    # the mouth (open while talking)
    if frame == 'talk':
        cx, cy = to_px(0.0, -0.051)
        d.ellipse((cx - 0.0105 * s, cy - 0.0062 * s, cx + 0.0105 * s, cy + 0.0072 * s), fill=INK)
        d.ellipse((cx - 0.0055 * s, cy + 0.0018 * s, cx + 0.0055 * s, cy + 0.0062 * s), fill=(150, 64, 58, 255))   # the tongue
    elif kind == 'Tired':
        cx, cy = to_px(0.0, -0.0505)
        arc_line(d, cx, cy + 0.0048 * s, 0.0158 * s, 0.0048 * s, 205, 335, 0.0031 * s, INK)
    elif kind == 'Wide':
        cx, cy = to_px(0.0, -0.05)
        d.ellipse((cx - 0.0062 * s, cy - 0.0036 * s, cx + 0.0062 * s, cy + 0.0036 * s), fill=INK)
    else:
        a, b = to_px(-0.015, -0.049), to_px(0.015, -0.049)
        round_line(d, [a, b], 0.0034 * s, INK)
    return im.resize((SIZE, SIZE), Image.LANCZOS)


FRAMES = ['open', 'blink', 'talk', 'scared']


def atlas():
    sets = ['Tired', 'Wide', 'Calm']
    im = Image.new('RGBA', (SIZE * 3, SIZE * len(FRAMES)), (0, 0, 0, 0))
    for i, k in enumerate(sets):
        for r, f in enumerate(FRAMES):
            im.paste(face_set(k, f), (i * SIZE, r * SIZE))
    im.save(os.path.join(OUT, 'T_FacePaint_Atlas.png'))


def visor_fog():
    """The visor glass's picture (its UVs run across the window seen from the front, v up): mostly clear, breath fog
    rising from the bottom edge, a few runs where it has gathered, and a soft glare streak top left."""
    n = 512
    rnd = random.Random(11)
    im = Image.new('RGBA', (n, n), (51, 56, 56, 64))           # clear: the glass's tint, alpha 0.25 of the material's
    fog = Image.new('L', (n, n), 0)
    df = ImageDraw.Draw(fog)
    df.ellipse((-n * 0.25, n * 0.72, n * 1.25, n * 1.45), fill=255)
    for _ in range(26):
        x, y, r = rnd.uniform(0, n), rnd.uniform(n * 0.7, n), rnd.uniform(18, 52)
        df.ellipse((x - r, y - r * 0.6, x + r, y + r * 0.6), fill=rnd.randint(120, 255))
    fog = soft(fog, 22)
    for _ in range(5):                                         # runs where the fog gathered
        x = rnd.uniform(n * 0.15, n * 0.85)
        y0 = rnd.uniform(n * 0.62, n * 0.78)
        ImageDraw.Draw(fog).line([(x, y0), (x + rnd.uniform(-4, 4), n)], fill=150, width=3)
    fog = soft(fog, 2)
    white = Image.new('RGBA', (n, n), (255, 255, 255, 255))
    a = fog.point(lambda v: int(64 + v * (191 / 255)))
    mix = Image.composite(white, im, fog)
    mix.putalpha(a)
    glare = Image.new('L', (n, n), 0)
    ImageDraw.Draw(glare).polygon([(n * 0.08, n * 0.05), (n * 0.22, n * 0.05), (n * 0.1, n * 0.45), (n * 0.02, n * 0.45)], fill=170)
    glare = soft(glare, 10)
    g = Image.new('RGBA', (n, n), (255, 255, 255, 0))
    g.putalpha(glare)
    mix.alpha_composite(g)
    mix.save(os.path.join(OUT, 'T_VisorFog.png'))


def soft(im, radius):
    return im.filter(ImageFilter.GaussianBlur(radius))


def shade():
    s = PX / SS     # drawn at the final size; the blur is the point here
    def px(x, dz): return (x + 0.06) * s, (0.05 - dz) * s
    out = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
    def layer(color, shapes, blur):
        m = Image.new('L', (SIZE, SIZE), 0)
        dm = ImageDraw.Draw(m)
        for (x, dz, rx, rz, a) in shapes:
            cx, cy = px(x, dz)
            dm.ellipse((cx - rx * s, cy - rz * s, cx + rx * s, cy + rz * s), fill=a)
        m = soft(m, blur)
        col = Image.new('RGBA', (SIZE, SIZE), color)
        col.putalpha(m)
        out.alpha_composite(col)
    layer((205, 95, 72, 255), [(-0.047, -0.022, 0.017, 0.012, 80), (0.047, -0.022, 0.017, 0.012, 80)], 22)   # cheeks
    layer((60, 34, 24, 255), [(-0.034, 0.026, 0.022, 0.008, 45), (0.034, 0.026, 0.022, 0.008, 45)], 16)      # under the brows
    layer((60, 34, 24, 255), [(0.0, -0.032, 0.012, 0.004, 90)], 8)                                           # under the nose
    layer((60, 34, 24, 255), [(0.0, -0.062, 0.016, 0.004, 60)], 10)                                          # under the lip
    out.save(os.path.join(OUT, 'T_FaceShade.png'))


def stubble():
    rnd = random.Random(7)
    s = PX
    im = canvas()
    # where it grows: the jaw and chin (an arc of the lower face) and the upper lip
    mask = Image.new('L', im.size, 0)
    dm = ImageDraw.Draw(mask)
    cx, cy = to_px(0.0, -0.01)
    dm.ellipse((cx - 0.075 * s, cy - 0.07 * s, cx + 0.075 * s, cy + 0.066 * s), fill=255)
    cx2, cy2 = to_px(0.0, 0.0)
    dm.ellipse((cx2 - 0.075 * s, cy2 - 0.08 * s, cx2 + 0.075 * s, cy2 + 0.042 * s), fill=0)    # leave the cheeks
    lx, ly = to_px(0.0, -0.034)
    dm.ellipse((lx - 0.026 * s, ly - 0.007 * s, lx + 0.026 * s, ly + 0.008 * s), fill=255)   # upper lip
    mx, my = to_px(0.0, -0.05)
    dm.ellipse((mx - 0.017 * s, my - 0.004 * s, mx + 0.017 * s, my + 0.004 * s), fill=0)     # not on the mouth
    mask = soft(mask, 10 * SS)
    tint = Image.new('RGBA', im.size, (45, 32, 26, 255))
    tint.putalpha(mask.point(lambda v: int(v * 0.13)))
    im.alpha_composite(tint)
    d = ImageDraw.Draw(im)
    mpx = mask.load()
    W = im.size[0]
    for _ in range(3500):
        x, y = rnd.uniform(0, W - 1), rnd.uniform(0, W - 1)
        a = mpx[int(x), int(y)]
        if a > 40 and rnd.random() < a / 255:
            r = rnd.uniform(0.0003, 0.00048) * s
            dot(d, (x, y), r, (38, 27, 22, int(60 + 70 * a / 255)))
    im.resize((SIZE, SIZE), Image.LANCZOS).save(os.path.join(OUT, 'T_FaceStubble.png'))


def label_font():
    cols, rows, cell = 8, 6, 128
    im = Image.new('RGBA', (cols * cell, rows * cell), (255, 255, 255, 0))
    f = ImageFont.truetype(FONT, 104)
    d = ImageDraw.Draw(im)
    for i, ch in enumerate(LABEL_CHARS):
        c, r = i % cols, i // cols
        x, y = c * cell + cell / 2, r * cell + cell / 2
        d.text((x, y), ch, font=f, fill=(255, 255, 255, 255), anchor='mm', stroke_width=5, stroke_fill=(255, 255, 255, 255))
    im.save(os.path.join(OUT, 'T_LabelFont.png'))


if __name__ == '__main__':
    atlas()
    visor_fog()
    shade()
    stubble()
    label_font()
    print('wrote the face atlas, shade, stubble and label font to', OUT)
