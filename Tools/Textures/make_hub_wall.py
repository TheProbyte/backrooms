"""
The hub's wall (2026-11-07, Docs/HubPlan.md): the owner's mock-up wall as one painted texture.

    python Tools/Textures/make_hub_wall.py

Writes Assets/Backrooms/Resources/Hub/OF_HubWall_Albedo.png and OF_HubWall_Normal.png: 512 x 1708 px, 1.2 m wide (one
cell, tiling sideways) and 4 m tall from the floor up (HubMaterials maps it by the height above the floor):

    0.00-0.10 m  a dark skirting board
    0.10-0.95 m  wainscot: dark walnut boards 0.15 m wide with V-grooves, worn lighter at the top, dirtier low down
    0.95-1.00 m  the cap rail, a lighter rounded moulding
    1.00-1.15 m  the company stripe: orange, gold, avocado, with thin dark lines between
    1.15-4.00 m  painted cream plaster, faded, in big soft patches; a little grime above the stripe

Painted as the style sheet says (Docs/StyleSheet.md): big soft shapes cut into a few levels, the darker parts warmer,
a few bold details and plain areas between, dirt where it gathers. The normal map is soft (grooves, the rail, the
stripe's edges).
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'Backrooms', 'Resources', 'Hub')

W, H = 512, 1708                       # 1.2 m x 4.0 m
PX = H / 4.0                           # pixels per metre (vertical)
PXU = W / 1.2                          # pixels per metre (horizontal)


def blur(a, sx, sy=None):
    sy = sx if sy is None else sy
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    g = np.exp(-2 * (np.pi ** 2) * ((fx * sx) ** 2 + (fy * sy) ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def noise(sx, seed, sy=None):
    r = np.random.default_rng(seed).standard_normal((H, W))
    a = blur(r, sx, sy)
    return (a - a.mean()) / (a.std() + 1e-9)


def painted(sx, seed, levels=4, soft=2.0, sy=None):
    a = np.clip(noise(sx, seed, sy) / 2.2, -1, 1)
    q = np.round((a + 1) * 0.5 * (levels - 1)) / (levels - 1) * 2 - 1
    return blur(q, soft) if soft > 0 else q


def hexc(h):
    h = h.lstrip('#')
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float64)


def shade(base, v, warm=0.35):
    out = base[None, None, :] * (1 + v[..., None])
    warmth = np.clip(-v, 0, None)[..., None] * warm
    return out * (1 + warmth * np.array([0.05, -0.02, -0.12]))


def smooth(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0, 1)
    return t * t * (3 - 2 * t)


def main():
    os.makedirs(OUT, exist_ok=True)
    rows = np.arange(H)[:, None].repeat(W, 1)
    y = (H - 1 - rows) / PX                       # metres above the floor (image row 0 is the top)
    cols = np.arange(W)[None, :].repeat(H, 0)
    u = cols / PXU                                # metres along the wall

    rgb = np.zeros((H, W, 3))
    height = np.zeros((H, W))

    # ---- cream plaster above (1.15 m up) ----
    plaster = hexc('#D8C79C')
    patches = painted(70, 11, 4, 5) * 0.045 + painted(26, 12, 3, 3) * 0.02
    fade = smooth(1.15, 3.6, y) * 0.03                                 # a touch lighter higher up, sun-faded
    grime = (1 - smooth(1.15, 1.6, y)) * (0.06 + 0.04 * np.clip(noise(18, 13), 0, None))   # hands and chair backs
    rgb_plaster = shade(plaster, patches + fade - grime)
    height += (painted(9, 14, 3, 1.5) * 0.15) * (y > 1.15)

    # ---- wainscot boards (0.10-0.95 m) ----
    wood = hexc('#5A3A22')
    board_w = 0.15
    board = np.floor(u / board_w).astype(int)
    tone = np.random.default_rng(21).uniform(-0.07, 0.07, board.max() + 2)[board]       # each board its own shade
    grain = blur(np.random.default_rng(22).standard_normal((H, W)), 0.6, 28)            # long vertical grain
    grain = grain / (grain.std() + 1e-9)
    wpatch = painted(40, 23, 4, 3, 90) * 0.05
    along = (u % board_w) / board_w
    groove = np.exp(-((along - 0.0) / 0.035) ** 2) + np.exp(-((along - 1.0) / 0.035) ** 2)      # V-groove at each board edge
    wear_top = smooth(0.7, 0.95, y) * 0.07                                                      # rubbed lighter under the rail
    dirt_low = (1 - smooth(0.1, 0.45, y)) * (0.12 + 0.05 * np.clip(noise(14, 24), 0, None))     # scuffs and mop water
    v_wood = tone + grain * 0.03 + wpatch + wear_top - dirt_low - groove * 0.35
    rgb_wood = shade(wood, v_wood, warm=0.25)
    height_wood = -groove * 1.2 + grain * 0.04

    # ---- skirting (0-0.10 m) ----
    skirt = hexc('#3B2A1E')
    v_sk = painted(30, 31, 3, 2) * 0.05 - (1 - smooth(0.0, 0.03, y)) * 0.15
    rgb_skirt = shade(skirt, v_sk)
    height_skirt = smooth(0.085, 0.1, y) * -0.6 + 0.3

    # ---- cap rail (0.95-1.00 m) ----
    rail = hexc('#7A5232')
    r_t = (y - 0.95) / 0.05
    bulge = np.sin(np.clip(r_t, 0, 1) * np.pi)
    rgb_rail = shade(rail, (bulge - 0.5) * 0.25 + painted(20, 41, 3, 2) * 0.04)
    height_rail = bulge * 1.4

    # ---- the stripe (1.00-1.15 m): orange, gold, avocado ----
    orange, gold, avocado, line = hexc('#D9661F'), hexc('#E8B23A'), hexc('#7C8A2E'), hexc('#3B2A1E')
    sp = painted(35, 51, 3, 2) * 0.04 - (np.clip(noise(10, 52), 1.2, None) - 1.2) * 0.25     # faded, a few rubbed spots
    band = np.where(y < 1.05, 0, np.where(y < 1.10, 1, 2))
    stripe_cols = np.stack([orange, gold, avocado])[band]
    rgb_stripe = stripe_cols * (1 + sp[..., None])
    for edge in (1.0, 1.05, 1.10, 1.15):                                                # thin dark lines between the bands
        m = np.exp(-((y - edge) / 0.0045) ** 2)[..., None]
        rgb_stripe = rgb_stripe * (1 - m * 0.75) + line * (m * 0.75)
    height_stripe = np.zeros((H, W))
    for edge in (1.0, 1.15):
        height_stripe -= np.exp(-((y - edge) / 0.004) ** 2) * 0.5

    # ---- put together ----
    def mask(lo, hi):
        return ((y >= lo) & (y < hi))[..., None]
    rgb = np.where(mask(0, 0.10), rgb_skirt, rgb)
    rgb = np.where(mask(0.10, 0.95), rgb_wood, rgb)
    rgb = np.where(mask(0.95, 1.00), rgb_rail, rgb)
    rgb = np.where(mask(1.00, 1.15), rgb_stripe, rgb)
    rgb = np.where(mask(1.15, 10), rgb_plaster, rgb)
    h2 = height
    h2 = np.where(mask(0, 0.10)[..., 0], height_skirt, h2)
    h2 = np.where(mask(0.10, 0.95)[..., 0], height_wood, h2)
    h2 = np.where(mask(0.95, 1.00)[..., 0], height_rail, h2)
    h2 = np.where(mask(1.00, 1.15)[..., 0], height_stripe, h2)
    # A shadow line under the rail, where the light is cut off.
    under = np.exp(-((y - 0.94) / 0.008) ** 2)[..., None]
    rgb = rgb * (1 - under * 0.35)

    Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), 'RGB').save(os.path.join(OUT, 'OF_HubWall_Albedo.png'))

    # Normal map (OpenGL, green up), soft.
    hs = blur(h2, 0.8)
    dx = (np.roll(hs, -1, 1) - np.roll(hs, 1, 1)) * 0.5 * 2.0
    dy = (np.roll(hs, -1, 0) - np.roll(hs, 1, 0)) * 0.5 * 2.0
    nx, ny, nz = -dx, dy, np.ones_like(hs)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    nrm = np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5
    Image.fromarray(np.clip(nrm * 255, 0, 255).astype(np.uint8), 'RGB').save(os.path.join(OUT, 'OF_HubWall_Normal.png'))
    print('wrote', OUT)


if __name__ == '__main__':
    main()
