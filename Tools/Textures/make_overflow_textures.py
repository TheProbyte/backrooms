"""
The Overflow look's painted textures for the halls and the warehouse (2026-11-06, Docs/StyleRollout.md, Docs/StyleSheet.md).

    python Tools/Textures/make_overflow_textures.py

Writes Assets/Backrooms/Textures/Overflow/OF_<name>_Albedo.png and OF_<name>_Normal.png. Every texture tiles.

How they are painted (the style sheet's rules):
  - big soft shapes first: low-frequency noise cut into a few flat levels and softened again, so it reads as brush
    patches, not photo grain
  - a small hue shift with value: the darker patches a little warmer and browner
  - only a few bold small details (ceiling tile fissures, carpet flecks, a crack or two in the slab), and plain areas
    left between them
  - dirt baked where it gathers: in the tile grooves, the slab's joints, round the knots
  - colours at about the old textures' average brightness, so the light grid's bounce light stays the same
  - normal maps soft and shallow (used at 0.2-0.6)

Carpet and ceiling tiles are near neutral, coloured by their materials (as the old ones), so the copies the
world tints from them (home carpets, odd tiles, stained tiles) come out right.

Scale of each texture (as the world maps it): wallpaper 0.6 m, carpet 3 m, ceiling tiles 2.4 m (4 x 4 tiles of 0.6 m),
plaster 2.4 m, concrete 3 m, wood and cardboard as the models map them.
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'Backrooms', 'Textures', 'Overflow')


# ---------------- tiling noise ----------------

def blur(a, sx, sy=None):
    """Gaussian blur that wraps round the edges (through the FFT), so whatever it makes tiles."""
    sy = sx if sy is None else sy
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    g = np.exp(-2 * (np.pi ** 2) * ((fx * sx) ** 2 + (fy * sy) ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def noise(n, sx, seed, sy=None):
    """Soft tiling noise with features about sx (and sy) pixels across, mean 0, deviation 1."""
    r = np.random.default_rng(seed).standard_normal((n, n))
    a = blur(r, sx, sy)
    return (a - a.mean()) / (a.std() + 1e-9)


def painted(n, sx, seed, levels=4, soft=2.0, sy=None):
    """Brush patches: soft noise cut into a few flat levels, the steps softened again. -1..1."""
    a = noise(n, sx, seed, sy)
    a = np.clip(a / 2.2, -1, 1)
    q = np.round((a + 1) * 0.5 * (levels - 1)) / (levels - 1) * 2 - 1
    return blur(q, soft) if soft > 0 else q


def shade(base, v, warm=0.35):
    """The base colour (0..255) made lighter or darker by v (a fraction), the darker parts a little warmer."""
    base = np.array(base, dtype=np.float64)
    out = base[None, None, :] * (1 + v[..., None])
    warmth = np.clip(-v, 0, None)[..., None] * warm
    out = out * (1 + warmth * np.array([0.05, -0.02, -0.12]))
    return out


def save_rgb(a, name):
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), 'RGB').save(os.path.join(OUT, name))


def save_normal(height, strength, name):
    """A tangent-space normal map (OpenGL, green up) from a height field, wrapping round the edges."""
    dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * 0.5 * strength
    dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * 0.5 * strength
    nx, ny, nz = -dx, dy, np.ones_like(height)        # image rows run down, v runs up
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    rgb = np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5
    save_rgb(rgb * 255, name)


def disc(a, cx, cy, r, value, n, mode='set', soft=1.0):
    """Paints a soft round dab into a (wrapping), mode 'set' or 'add'."""
    x0, x1 = int(cx - r - 2), int(cx + r + 3)
    y0, y1 = int(cy - r - 2), int(cy + r + 3)
    ys, xs = np.mgrid[y0:y1, x0:x1]
    d = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2)
    m = np.clip((r - d) / soft + 0.5, 0, 1)
    yy, xx = ys % n, xs % n
    if mode == 'add':
        np.add.at(a, (yy, xx), m * value)
    else:
        cur = a[yy, xx]
        a[yy, xx] = cur * (1 - m) + value * m


def stroke(a, pts, width, value, n, mode='set'):
    for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
        steps = int(max(abs(x1 - x0), abs(y1 - y0)) / max(width * 0.4, 0.5)) + 1
        for t in np.linspace(0, 1, steps):
            disc(a, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, width * 0.5, value, n, mode)


def walk(rng, x, y, length, step, turn, heading=None):
    h = rng.uniform(0, 2 * np.pi) if heading is None else heading
    pts = [(x, y)]
    for _ in range(int(length / step)):
        h += rng.normal(0, turn)
        x += np.cos(h) * step
        y += np.sin(h) * step
        pts.append((x, y))
    return pts


# ---------------- the textures ----------------

def wallpaper():
    """Level 0's wallpaper, simplified: one broad lighter stripe a tile with a column of bold chevrons, a darker gap
    between with a pair of pin stripes, painted patches over it. One tile = 0.6 m (1024 px). (No roll seam here: the
    world adds the 53 cm roll seams itself, OfficeChunkBuilder.Paper.)"""
    n = 1024
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float64)
    u = xs / n
    v = 0 * xs
    band = blur(np.clip((0.3 - np.abs(u - 0.5)) / 0.01, 0, 1), 3)
    v += band * 0.05
    for c in (0.06, 0.94):
        v -= np.clip((0.004 - np.abs(u - c)) / 0.003, 0, 1) * 0.07
    # chevrons: a V every 192 px (11 cm) down the band's middle, pointing up
    cy = (ys % 192) / 192.0
    du = np.abs(u - 0.5)
    chev = np.clip((0.06 - np.abs(cy - 0.3 - du * 2.0)) / 0.015, 0, 1) * (du < 0.15)
    motif = blur(chev, 1.5)
    v += motif * 0.085
    v += painted(n, 90, 11, levels=4, soft=6) * 0.035 + painted(n, 260, 12, levels=3, soft=18) * 0.03
    v += noise(n, 1.4, 13) * 0.008
    rgb = shade((214, 197, 112), v, warm=0.6)
    save_rgb(rgb, 'OF_Wallpaper_Albedo.png')
    save_normal(blur(motif * 1.0 + band * 0.3, 1.5), 2.0, 'OF_Wallpaper_Normal.png')


def carpet():
    """The mustard carpet: big soft mottling in a few tones and painted flecks. One tile = 3 m (1024 px)."""
    n = 1024
    rng = np.random.default_rng(21)
    v = painted(n, 55, 22, levels=4, soft=4) * 0.034 + painted(n, 170, 23, levels=3, soft=12) * 0.03
    fleck = np.zeros((n, n))
    for _ in range(5200):
        disc(fleck, rng.uniform(0, n), rng.uniform(0, n), rng.uniform(0.8, 1.9), rng.choice([-1.0, 1.0]) * rng.uniform(0.5, 1.0), n, 'set', 0.8)
    v += fleck * 0.07
    pile = noise(n, 0.9, 24)
    v += pile * 0.012
    # Light and neutral (a little warm in the dark patches): the material's colour makes it mustard, and the homes'
    # carpets (MaterialMaps.HomeCarpets) tint the same texture their own shades.
    rgb = shade((200, 200, 200), v, warm=0.5)
    save_rgb(rgb, 'OF_Carpet_Albedo.png')
    save_normal(pile * 0.5 + blur(fleck, 0.8) * 0.6 + painted(n, 55, 22, 4, 4) * 0.6, 1.2, 'OF_Carpet_Normal.png')


def ceiling_tiles():
    """Acoustic tiles, 4 x 4 (256 px = 0.6 m each): a dark groove round each, a bevel (light top-left, dark
    bottom-right), a few bold fissure dots and short worms, each tile a shade of its own, the grooves dirtier."""
    n, t = 1024, 256
    rng = np.random.default_rng(31)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float64)
    lx, ly = xs % t, ys % t
    edge = np.minimum(np.minimum(lx, t - 1 - lx), np.minimum(ly, t - 1 - ly))
    v = np.zeros((n, n))
    height = np.clip(edge / 7.0, 0, 1)                 # the bevel down into the groove
    # each tile a shade of its own (one yellowed now and then)
    tint = np.zeros((n, n, 3))
    for ty in range(4):
        for tx in range(4):
            s = rng.normal(0, 0.025)
            v[ty * t:(ty + 1) * t, tx * t:(tx + 1) * t] += s
            if rng.random() < 0.18:
                tint[ty * t:(ty + 1) * t, tx * t:(tx + 1) * t] = [0.0, -0.02, -0.07]
    # bevel light: top and left edges catch it, bottom and right fall away
    bev = (edge < 7) & (edge >= 2)
    top_left = (np.minimum(lx, ly) == edge)
    v += np.where(bev & top_left, 0.06, 0) - np.where(bev & ~top_left, 0.07, 0)
    v -= np.where(edge < 2, 0.32, 0)                   # the groove
    v -= np.clip(1 - edge / 22.0, 0, 1) * 0.04         # dirt gathered toward the groove
    # fissures: bold dots and short worms, sparse, darker, pitted
    pits = np.zeros((n, n))
    for ty in range(4):
        for tx in range(4):
            for _ in range(rng.integers(26, 42)):
                x = tx * t + rng.uniform(14, t - 14)
                y = ty * t + rng.uniform(14, t - 14)
                if rng.random() < 0.8:
                    disc(pits, x, y, rng.uniform(1.4, 2.8), 1.0, n)
                else:
                    stroke(pits, walk(rng, x, y, rng.uniform(6, 14), 2.0, 0.6), rng.uniform(1.6, 2.4), 1.0, n)
    v -= pits * 0.13
    v += painted(n, 70, 32, levels=3, soft=5) * 0.018
    # Near neutral: the material's colour makes it warm off-white (the stained, yellowed and other batches tint it too).
    rgb = shade((222, 222, 220), v, warm=0.4) * (1 + tint)
    save_rgb(rgb, 'OF_CeilingTile_Albedo.png')
    save_normal(height * 1.6 - pits * 0.8 + noise(n, 1.2, 33) * 0.05, 1.6, 'OF_CeilingTile_Normal.png')


def plaster():
    """Painted plaster for walls and ceilings (2.4 m a tile): roller laps up and down, broad patches, a light
    near-white tinted by each material's colour."""
    n = 1024
    laps = painted(n, 26, 41, levels=5, soft=5, sy=220) * 0.02
    v = laps + painted(n, 150, 42, levels=4, soft=12) * 0.03
    stip = noise(n, 1.1, 43)
    v += stip * 0.006
    rgb = shade((236, 230, 216), v, warm=0.5)
    save_rgb(rgb, 'OF_Plaster_Albedo.png')
    save_normal(stip * 0.35 + laps * 8, 1.0, 'OF_Plaster_Normal.png')


def concrete():
    """The warehouse slab (3 m a tile, the joints at its edges): broad trowelled patches, a few dark stains, two cracks,
    the saw-cut joint with dirt in it, a fine aggregate speckle."""
    n = 1024
    rng = np.random.default_rng(51)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float64)
    v = painted(n, 80, 52, levels=5, soft=6) * 0.05 + painted(n, 240, 53, levels=3, soft=20) * 0.05
    # stains: a few soft dark blots
    stain = np.zeros((n, n))
    for _ in range(6):                                  # each a few overlapping blots, not a circle
        cx, cy, r = rng.uniform(0, n), rng.uniform(0, n), rng.uniform(16, 40)
        for _ in range(rng.integers(3, 6)):
            disc(stain, cx + rng.normal(0, r * 0.8), cy + rng.normal(0, r * 0.5), r * rng.uniform(0.4, 0.9), 1.0, n, 'set', 10)
    v -= blur(stain, 6) * 0.07
    # cracks
    crack = np.zeros((n, n))
    for _ in range(2):
        stroke(crack, walk(rng, rng.uniform(0, n), rng.uniform(0, n), rng.uniform(160, 300), 6, 0.35), 1.6, 1.0, n)
    v -= crack * 0.22
    # the saw-cut joints along the tile's edges (every 3 m), dirt either side
    dist = np.minimum(np.minimum(xs, n - 1 - xs), np.minimum(ys, n - 1 - ys))
    joint = np.clip((2.6 - dist) / 1.0, 0, 1)
    v -= joint * 0.4 + np.clip(1 - dist / 16, 0, 1) * 0.06
    speck = np.zeros((n, n))
    for _ in range(3800):
        disc(speck, rng.uniform(0, n), rng.uniform(0, n), rng.uniform(0.6, 1.3), rng.choice([-1.0, 1.0]), n, 'set', 0.7)
    v += speck * 0.04
    rgb = shade((158, 149, 132), v, warm=0.4)
    save_rgb(rgb, 'OF_Concrete_Albedo.png')
    save_normal(-joint * 2.0 - crack * 1.2 + speck * 0.15 + painted(n, 80, 52, 5, 6) * 0.3, 1.0, 'OF_Concrete_Normal.png')


def wood():
    """Painted wood (pallets, wood trim, the dark wood floor: each tints it): long grain streaks across, cut into a few
    flat tones, a few knots with dirt round them. 512 px."""
    n = 512
    rng = np.random.default_rng(61)
    grain = painted(n, 70, 62, levels=5, soft=1.5, sy=2.2) * 0.07
    fine = noise(n, 18, 63, sy=0.9) * 0.025
    v = grain + fine + painted(n, 120, 64, levels=3, soft=10) * 0.04
    knots = np.zeros((n, n))
    for _ in range(3):
        cx, cy = rng.uniform(0, n), rng.uniform(0, n)
        for r, val in ((9, 0.5), (5.5, 1.0)):
            disc(knots, cx, cy, r, val, n, 'set', 2)
    v -= blur(knots, 1.2) * 0.22
    rgb = shade((150, 112, 72), v, warm=0.3)
    save_rgb(rgb, 'OF_Wood_Albedo.png')
    save_normal(grain * 6 + fine * 8 - knots * 0.6, 1.0, 'OF_Wood_Normal.png')


def cardboard():
    """Kraft board (at the material's 2 x 2 tiling): soft patches, a few scuffs and creases, the flutes faint under the
    liner. 1024 px."""
    n = 1024
    rng = np.random.default_rng(71)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float64)
    v = painted(n, 60, 72, levels=4, soft=5) * 0.04 + painted(n, 200, 73, levels=3, soft=16) * 0.035
    flutes = np.sin(ys / n * 2 * np.pi * 128) * 0.5 + 0.5
    v += (flutes - 0.5) * 0.012
    scuff = np.zeros((n, n))
    for _ in range(9):
        stroke(scuff, walk(rng, rng.uniform(0, n), rng.uniform(0, n), rng.uniform(30, 90), 5, 0.25), rng.uniform(2, 5), 1.0, n)
    v -= blur(scuff, 1.5) * 0.06
    v += noise(n, 1.0, 74) * 0.008
    rgb = shade((146, 110, 68), v, warm=0.3)
    save_rgb(rgb, 'OF_Cardboard_Albedo.png')
    save_normal(flutes * 0.25 - scuff * 0.4, 1.0, 'OF_Cardboard_Normal.png')


def fabric():
    """Upholstery (the sofas, armchairs, the lamp's shade): big soft patches, a bold coarse weave (threads every 8 px,
    over and under), lighter worn patches. Near neutral: each fabric's material gives its colour. 512 px."""
    n = 512
    y, x = np.mgrid[0:n, 0:n]
    v = painted(n, 40, 31, levels=4, soft=3) * 0.04
    warp = (np.sin(x * 2 * np.pi / 8) * 0.5 + 0.5) ** 2
    weft = (np.sin(y * 2 * np.pi / 8) * 0.5 + 0.5) ** 2
    over = (((x // 8) + (y // 8)) % 2).astype(np.float64)
    weave = np.where(over > 0.5, warp, weft) - 0.4
    v += weave * 0.05
    worn = np.clip(painted(n, 70, 32, levels=3, soft=6), 0, None) * 0.05
    v += worn
    rgb = shade((205, 205, 205), v, warm=0.5)
    save_rgb(rgb, 'OF_Fabric_Albedo.png')
    save_normal(blur(weave, 0.8) * 1.2 + painted(n, 40, 31, 4, 3) * 0.3, 1.0, 'OF_Fabric_Normal.png')


if __name__ == '__main__':
    import sys
    only = set(sys.argv[1:])
    for f in (wallpaper, carpet, ceiling_tiles, plaster, concrete, wood, cardboard, fabric):
        if only and f.__name__ not in only:
            continue
        f()
        print('made', f.__name__)
