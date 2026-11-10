"""
The Overflow look's own home walls (2026-11-09, the owner: "improved sky wallpaper and orange peel ... i like the hub
walls"): the two homes' finishes painted the way the hub wall is, in place of the photo-made ones.

    python Tools/Textures/make_overflow_home.py

Writes Assets/Backrooms/Resources/Overflow/Textures/Home/OF_* (MaterialMaps.Homes takes them in the Overflow look; the
Classic look keeps Resources/Textures/Home):
  OF_SkyClouds.png         the blue-sky clouds paper, one repeat 1.6 m (1024 px): flat painted sky in soft patches,
                          chunky cumulus in three flat values (lit tops, cream bodies, blue-grey bellies), a thin
                          darker rim, a little print wear
  OF_OrangePeel_Albedo.png  sprayed drywall, one tile 0.6 m (1024 px): off-white in big soft patches like the hub's
  OF_OrangePeel_Normal.png  plaster, its droplets simplified into bold soft bumps (about 4-8 mm) with the light baked on

Painted as the style sheet says (Docs/StyleSheet.md): big soft shapes cut into a few levels, the darker parts warmer,
a few bold details and plain areas between. Everything tiles.
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'Backrooms', 'Resources', 'Overflow', 'Textures', 'Home')
N = 1024


def blur(a, sx, sy=None):
    sy = sx if sy is None else sy
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    g = np.exp(-2 * (np.pi ** 2) * ((fx * sx) ** 2 + (fy * sy) ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def noise(sx, seed, sy=None):
    r = np.random.default_rng(seed).standard_normal((N, N))
    a = blur(r, sx, sy)
    return (a - a.mean()) / (a.std() + 1e-9)


def painted(sx, seed, levels=4, soft=2.0):
    a = np.clip(noise(sx, seed) / 2.2, -1, 1)
    q = np.round((a + 1) * 0.5 * (levels - 1)) / (levels - 1) * 2 - 1
    return blur(q, soft) if soft > 0 else q


def hexc(h):
    h = h.lstrip('#')
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float64)


def shade(base, v, warm=0.35):
    out = base[None, None, :] * (1 + v[..., None])
    warmth = np.clip(-v, 0, None)[..., None] * warm
    return out * (1 + warmth * np.array([0.05, -0.02, -0.12]))


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def save_rgb(a, name):
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), 'RGB').save(os.path.join(OUT, name))


def save_normal(height, strength, name):
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 0.5 * strength
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 0.5 * strength
    nx, ny, nz = -dx, dy, np.ones_like(height)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    save_rgb(np.stack([nx / l, ny / l, nz / l], -1) * 127.5 + 127.5, name)


def shift(a, dx, dy):
    return np.roll(np.roll(a, dy, 0), dx, 1)


# ---------------- the sky paper ----------------

def cloud(rng, cx, cy, w):
    """One cumulus as puffs: (mask, lit, belly), each a soft 0..1 field, wrapping. A base row of puffs, the middle ones
    tallest, a crown over them; each puff lit on its upper left; the lower part of the cloud in shadow, softly."""
    ys, xs = np.mgrid[0:N, 0:N].astype(np.float64)
    dx = (xs - cx + N / 2) % N - N / 2
    dy = (ys - cy + N / 2) % N - N / 2
    m = np.zeros((N, N)); lit = np.zeros((N, N))
    puffs = []
    k = rng.integers(5, 8)
    for i in range(k):                                       # the base row
        t = (i + 0.5) / k - 0.5
        r = w * (0.13 + 0.1 * (1 - abs(t) * 2) + rng.uniform(-0.02, 0.03))
        puffs.append((t * w * 0.9, -r * 0.5 + rng.uniform(-0.02, 0.02) * w, r))
    for i in range(rng.integers(2, 4)):                      # the crown
        t = rng.uniform(-0.25, 0.25)
        r = w * rng.uniform(0.14, 0.22) * (1 - abs(t))
        puffs.append((t * w, -w * rng.uniform(0.2, 0.32), r))
    for px, py, r in puffs:
        d = np.sqrt((dx - px) ** 2 + (dy - py) ** 2)
        pm = np.clip((r - d) / 1.4 + 0.5, 0, 1)
        m = np.maximum(m, pm)
        dl = np.sqrt((dx - px + r * 0.28) ** 2 + (dy - py + r * 0.32) ** 2)       # its lit cap, upper left
        lit = np.maximum(lit, pm * np.clip((r * 0.62 - dl) / 3.0 + 0.5, 0, 1))
    flat = np.clip((-dy + w * 0.03) / 1.4 + 0.5, 0, 1)        # a flat-ish belly
    m *= flat
    belly = smooth(-w * 0.11, w * 0.02, dy) * m
    return m, lit * m, belly


def sky():
    rng = np.random.default_rng(9)
    sky_c = hexc('#86B4D2')
    v = painted(110, 71, 4, 8) * 0.04 + painted(35, 72, 3, 3) * 0.014
    rgb = shade(sky_c, v, warm=0.15)
    body = np.zeros((N, N)); lit = np.zeros((N, N)); belly = np.zeros((N, N))
    # Clouds scattered by dart throwing (kept apart, all sizes), so no grid shows; they wrap round the tile.
    placed = []
    tries = 0
    while len(placed) < 11 and tries < 4000:
        tries += 1
        pick = rng.choice(3, p=[0.3, 0.4, 0.3])
        w = (rng.uniform(220, 320), rng.uniform(140, 210), rng.uniform(80, 130))[pick]
        cx, cy = rng.uniform(0, N), rng.uniform(0, N)
        ok = True
        for (ox, oy, ow) in placed:
            ddx = (cx - ox + N / 2) % N - N / 2
            ddy = (cy - oy + N / 2) % N - N / 2
            if abs(ddx) < (w + ow) * 0.55 + 30 and abs(ddy) < (w + ow) * 0.22 + 40:
                ok = False
                break
        if ok:
            placed.append((cx, cy, w))
    for cx, cy, w in placed:
        m, l, b = cloud(rng, cx, cy, w)
        body = np.maximum(body, m); lit = np.maximum(lit, l); belly = np.maximum(belly, b)
    cloud_c = hexc('#F2ECDC'); shadow = hexc('#C3CFD6'); light = hexc('#FFFAEC')
    c_rgb = shade(cloud_c, painted(22, 74, 3, 2) * 0.025, warm=0.2)
    a = belly[..., None] * 0.8
    c_rgb = c_rgb * (1 - a) + shadow * a
    a = np.clip(lit * 1.2, 0, 1)[..., None] * 0.75 * (1 - belly[..., None])
    c_rgb = c_rgb * (1 - a) + light * a
    rgb = rgb * (1 - body[..., None]) + c_rgb * body[..., None]
    # A soft painted rim round each cloud, a shade of the sky deeper.
    rim = np.clip(blur(body, 3.0) - body, 0, 1) * 1.8
    rgb = rgb * (1 - np.clip(rim, 0, 1)[..., None] * 0.12)
    wear = np.clip(painted(60, 75, 3, 6), 0, 1) * 0.05
    rgb = rgb * (1 - wear[..., None]) + hexc('#EDE6D2') * wear[..., None]
    save_rgb(rgb, 'OF_SkyClouds.png')


# ---------------- orange peel ----------------

def orange_peel():
    # Bold soft round bumps: the spray's droplets simplified (about 4-8 mm across at 0.6 m a tile), thrown at random,
    # the bigger ones fewer; where they touch they run together.
    rng = np.random.default_rng(81)
    height = np.zeros((N, N))
    for count, sigma, amp in ((2600, 3.2, 1.0), (900, 5.0, 1.3), (5000, 1.8, 0.5)):
        pts = np.zeros((N, N))
        pts[rng.integers(0, N, count), rng.integers(0, N, count)] = amp * rng.uniform(0.6, 1.0, count)
        height += blur(pts, sigma) * sigma * sigma
    height = height / height.max()
    height = np.clip(height * 1.6, 0, 1) ** 0.8                  # flat-topped, soft-sided
    height *= 0.8 + 0.2 * painted(80, 82, 3, 10)                 # thicker spray in patches
    base = hexc('#EEE8DA')
    v = painted(90, 83, 4, 6) * 0.025 + painted(30, 84, 3, 3) * 0.01
    gy = (np.roll(height, 1, 0) - np.roll(height, -1, 0)) * 0.5
    gx = (np.roll(height, 1, 1) - np.roll(height, -1, 1)) * 0.5
    v += np.clip(gy * 0.35 + gx * 0.12, -0.025, 0.025)           # the light baked on, faintly (lit from above)
    v -= (1 - height) * 0.01
    rgb = shade(base, v, warm=0.5)
    save_rgb(rgb, 'OF_OrangePeel_Albedo.png')
    save_normal(blur(height, 0.8), 4.0, 'OF_OrangePeel_Normal.png')


if __name__ == '__main__':
    sky()
    print('made sky')
    orange_peel()
    print('made orange peel')
