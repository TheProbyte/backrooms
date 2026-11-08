"""
The real world's surfaces (2026-11-07, Docs/ContractFloorsPlan.md "Walk-in seams"): what the places through the seams are
made of, so they read at a glance as somewhere that is not the Backrooms. Cool, clean, hard: white and blue-grey where the
Backrooms are cream and yellow, painted block and polished slab where they are wallpaper and carpet.

    python Tools/Textures/make_real_surfaces.py

Writes Assets/Backrooms/Resources/Real/ (RealMaterials maps them):

    RW_Block    a painted concrete block wall, 1.2 m wide x 4.8 m tall from the floor: 400 x 200 mm blocks in running bond,
                blue-grey gloss paint to 1.2 m, a thin red line, white above (a warehouse's or a print shop's wall)
    RW_Drywall  a shop's wall, 1.2 x 4.8 m: smooth white, a black vinyl cove base 100 mm high
    RW_Polished a polished concrete slab, 2.4 m square: mottled cool grey, burnished, saw cuts on a 2.4 m grid
    RW_Vinyl    vinyl composition tile, 3 m square: 300 mm tiles in two greys and white, speckled
    RW_Deck     a painted steel roof deck seen from below, 1.2 m square: ribs every 300 mm, white
    RW_Plaster  a shop's smooth white plaster ceiling, 1.2 m square

Painted like the rest (Docs/StyleSheet.md): big soft shapes cut into a few levels, a few bold details, dirt where it
gathers; but cleaner than anything in the Backrooms.
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'Backrooms', 'Resources', 'Real')


def blur(a, sx, sy=None):
    sy = sx if sy is None else sy
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    g = np.exp(-2 * (np.pi ** 2) * ((fx * sx) ** 2 + (fy * sy) ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def noise(shape, sx, seed, sy=None):
    r = np.random.default_rng(seed).standard_normal(shape)
    a = blur(r, sx, sy)
    return (a - a.mean()) / (a.std() + 1e-9)


def painted(shape, sx, seed, levels=4, soft=2.0, sy=None):
    a = np.clip(noise(shape, sx, seed, sy) / 2.2, -1, 1)
    q = np.round((a + 1) * 0.5 * (levels - 1)) / (levels - 1) * 2 - 1
    return blur(q, soft) if soft > 0 else q


def hexc(h):
    h = h.lstrip('#')
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float64)


def shade(base, v):
    return base[None, None, :] * (1 + v[..., None])


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def save(name, rgb, height, strength=2.0):
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), 'RGB').save(os.path.join(OUT, name + '_Albedo.png'))
    hs = blur(height, 0.8)
    dx = (np.roll(hs, -1, 1) - np.roll(hs, 1, 1)) * 0.5 * strength
    dy = (np.roll(hs, -1, 0) - np.roll(hs, 1, 0)) * 0.5 * strength
    nx, ny, nz = -dx, dy, np.ones_like(hs)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    nrm = np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5
    Image.fromarray(np.clip(nrm * 255, 0, 255).astype(np.uint8), 'RGB').save(os.path.join(OUT, name + '_Normal.png'))
    print('wrote', name)


def wall_grid(W, H, width_m, height_m):
    rows = np.arange(H)[:, None].repeat(W, 1)
    cols = np.arange(W)[None, :].repeat(H, 0)
    y = (H - 1 - rows) / (H / height_m)            # metres above the floor (row 0 is the top)
    u = cols / (W / width_m)                       # metres along the wall
    return u, y


def block():
    W, H = 512, 2048                               # 1.2 x 4.8 m
    u, y = wall_grid(W, H, 1.2, 4.8)
    course = np.floor(y / 0.2).astype(int)
    shift = (course % 2) * 0.2
    along = (u + shift) % 0.4
    up = y % 0.2
    joint = np.minimum(np.minimum(along, 0.4 - along), np.minimum(up, 0.2 - up))
    mortar = 1 - smooth(0.003, 0.009, joint)                                      # struck joints, 10 mm
    bid = (np.floor((u + shift) / 0.4).astype(int) * 31 + course * 17) % 97
    tone = np.random.default_rng(3).uniform(-0.025, 0.025, 97)[bid]               # each block takes the paint a bit differently
    pores = np.clip(noise((H, W), 0.7, 4), 1.4, None) - 1.4                       # the block's pits under the paint
    low, high = hexc('#5D7487'), hexc('#ECEFEF')
    stripe = hexc('#B3362B')
    v = tone + painted((H, W), 60, 5, 3, 4) * 0.025 - pores * 0.12 - mortar * 0.10
    dirt = (1 - smooth(0.0, 0.35, y)) * (0.08 + 0.05 * np.clip(noise((H, W), 12, 6), 0, None))   # scuffed by pallets and boots
    rgb = np.where((y < 1.2)[..., None], shade(low, v - dirt), shade(high, v * 0.8))
    m = ((y >= 1.2) & (y < 1.25))[..., None]
    rgb = np.where(m, shade(stripe, v * 0.6), rgb)
    height = -mortar * 1.0 - pores * 0.5
    save('RW_Block', rgb, height, 2.5)


def drywall():
    W, H = 256, 1024
    u, y = wall_grid(W, H, 1.2, 4.8)
    white = hexc('#F1F2EF')
    v = painted((H, W), 40, 11, 3, 4) * 0.012 + noise((H, W), 1.2, 12) * 0.006           # roller stipple
    rgb = shade(white, v)
    base = (y < 0.1)
    cove = hexc('#2B2C2E')
    rgb = np.where(base[..., None], shade(cove, painted((H, W), 20, 13, 3, 2) * 0.05), rgb)
    top_edge = np.exp(-((y - 0.1) / 0.004) ** 2)[..., None]
    rgb = rgb * (1 - top_edge * 0.3)
    scuff = (1 - smooth(0.1, 0.4, y)) * np.clip(noise((H, W), 6, 14) - 1.2, 0, None) * 0.15
    rgb = rgb * (1 - scuff[..., None])
    height = noise((H, W), 1.0, 15) * 0.05 + base * 0.6
    save('RW_Drywall', rgb, height, 1.5)


def polished():
    N = 512                                         # 2.4 m
    grey = hexc('#9AA0A3')
    v = painted((N, N), 50, 21, 4, 6) * 0.05 + noise((N, N), 6, 22) * 0.02
    aggregate = np.clip(noise((N, N), 0.9, 23) - 1.6, 0, None) * 0.25             # the stone showing where it was ground
    trowel = blur(np.random.default_rng(24).standard_normal((N, N)), 30, 4)
    trowel = trowel / (trowel.std() + 1e-9) * 0.015
    rgb = shade(grey, v + aggregate + trowel)
    px = np.arange(N)
    cut = np.minimum(np.minimum(px, N - 1 - px), 9999)
    line = np.exp(-(cut / 1.2) ** 2)
    joint = np.maximum(line[None, :], line[:, None])
    rgb = rgb * (1 - joint[..., None] * 0.55)
    height = -joint * 1.5 + noise((N, N), 2, 25) * 0.03
    save('RW_Polished', rgb, height, 2.0)


def vinyl():
    N = 640                                         # 3 m, 10 x 10 tiles of 300 mm
    t = N // 10
    rows = np.arange(N)[:, None].repeat(N, 1)
    cols = np.arange(N)[None, :].repeat(N, 0)
    ti, tj = cols // t, rows // t
    rng = np.random.default_rng(31)
    pick = rng.choice(3, size=(10, 10), p=[0.55, 0.35, 0.10])
    pick[(np.arange(10)[:, None] + np.arange(10)[None, :]) % 2 == 0] = np.where(pick[(np.arange(10)[:, None] + np.arange(10)[None, :]) % 2 == 0] == 2, 2, 0)
    cols3 = np.stack([hexc('#E3E4DF'), hexc('#B8BCBD'), hexc('#5F6D7A')])
    base = cols3[pick[tj, ti]]
    speck = np.random.default_rng(32).random((N, N))
    v = np.where(speck > 0.93, -0.12, np.where(speck < 0.04, 0.06, 0.0)) + painted((N, N), 25, 33, 3, 3) * 0.02
    flow = blur(np.random.default_rng(34).standard_normal((N, N)), 8, 2)
    v += flow / (flow.std() + 1e-9) * 0.015
    rgb = base * (1 + v[..., None])
    gu, gv = cols % t, rows % t
    seam = np.minimum(np.minimum(gu, t - 1 - gu), np.minimum(gv, t - 1 - gv))
    s = np.exp(-(seam / 0.8) ** 2)
    rgb = rgb * (1 - s[..., None] * 0.3)
    height = -s * 0.8
    save('RW_Vinyl', rgb, height, 1.5)


def deck():
    N = 256                                         # 1.2 m: four ribs
    cols = np.arange(N)[None, :].repeat(N, 0)
    u = cols / (N / 1.2)
    ph = (u % 0.3) / 0.3
    # The trapezoid profile seen from below: a flat pan, a slope, the rib's flat top, a slope.
    prof = np.where(ph < 0.45, 0.0, np.where(ph < 0.55, (ph - 0.45) / 0.1, np.where(ph < 0.85, 1.0, np.where(ph < 0.95, 1 - (ph - 0.85) / 0.1, 0.0))))
    slope = ((ph >= 0.45) & (ph < 0.55)) | ((ph >= 0.85) & (ph < 0.95))
    white = hexc('#E6E9EA')
    v = painted((N, N), 30, 41, 3, 3) * 0.02 - slope * 0.10 + prof * 0.03
    rgb = shade(white, v)
    save('RW_Deck', rgb, prof * 3.0, 3.0)


def plaster():
    N = 256
    white = hexc('#F2F3F1')
    v = painted((N, N), 40, 51, 3, 4) * 0.01 + noise((N, N), 1.0, 52) * 0.005
    save('RW_Plaster', shade(white, v), noise((N, N), 1.0, 53) * 0.03, 1.0)


if __name__ == '__main__':
    block(); drywall(); polished(); vinyl(); deck(); plaster()
