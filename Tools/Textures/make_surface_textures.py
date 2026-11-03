"""Surface textures from the player's photos (Tools/Textures/Source):

  Drywall_Photo.jpg     -> Assets/Backrooms/Textures/Plaster_Albedo.png + Plaster_Normal.png
                           (painted drywall walls, and plaster ceilings, which are drywall too)
  CeilingTile_Photo.jpg -> Assets/Backrooms/Textures/RefCeiling_Albedo.png + RefCeiling_Normal.png
                           (the drop ceiling's tiles)
  VentPanel_Photo.webp  -> Assets/Backrooms/Resources/Textures/DuctMetal.png
                           (the inside of the crawl ducts)

The photos' own lighting (a bright corner, a vignette) is divided out, so the game's lights do the shading; each texture
keeps the brightness and colour of the one it replaces (so the materials' tints still give the same overall look).

Scales (from the UVs the chunk builder gives these materials):
  - painted walls: one texture = 2.4 x 2.4 m. The drywall photo covers about half a metre of wall (the orange-peel bumps
    are 2-3 mm), so it is made seamless and repeated 4 x 4 (one repeat = 0.6 m).
  - drop ceiling: one texture = 2.4 x 2.4 m = 4 x 4 tiles of 0.6 m. The tile photo (without its grid rim) is used for
    every tile, turned and flipped differently in each so the ceiling does not repeat; the grid hides the joins.
  - duct: one texture = one 1.1 m section along the duct x one 0.8 m face across it. Two of the photo's panels between
    its riveted strips side by side, five of its ridges top to bottom (so the faces' edges line up).

Normal maps: from the photos' fine relief (luminance with its broad shading removed), tangent space, green up.

    python Tools/Textures/make_surface_textures.py
"""
import os
import numpy as np
from PIL import Image
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(HERE, "Source")
TEX = os.path.join(ROOT, "Assets", "Backrooms", "Textures")
RES = os.path.join(ROOT, "Assets", "Backrooms", "Resources", "Textures")


def load(name):
    return np.asarray(Image.open(os.path.join(SRC, name)).convert("RGB"), dtype=np.float64) / 255.0


def save_rgb(a, path, alpha=False):
    a = np.clip(a, 0.0, 1.0)
    img = (a * 255.0 + 0.5).astype(np.uint8)
    mode = "RGB"
    if alpha:
        img = np.dstack([img, np.full(img.shape[:2], 255, np.uint8)])
        mode = "RGBA"
    Image.fromarray(img, mode).save(path)
    print("wrote", os.path.relpath(path, ROOT), img.shape)


def flatten(a, sigma):
    """Divides out the broad lighting (a blur this wide), keeping the mean brightness."""
    low = np.stack([ndimage.gaussian_filter(a[..., c], sigma, mode="reflect") for c in range(3)], -1)
    mean = a.reshape(-1, 3).mean(0)
    return a / np.maximum(low, 1e-3) * mean


def seamless(a):
    """Tiles without a visible join: blended with itself shifted by half, across then down (each pass keeps the other's
    edges matching), the blend kept at full contrast."""
    mean = a.reshape(-1, 3).mean(0)
    for axis in (1, 0):
        n = a.shape[axis]
        w = np.sin(np.pi * (np.arange(n) + 0.5) / n) ** 2
        w = w[None, :, None] if axis == 1 else w[:, None, None]
        rolled = np.roll(a, n // 2, axis)
        out = a * w + rolled * (1 - w)
        a = mean + (out - mean) / np.sqrt(w * w + (1 - w) * (1 - w))      # two unrelated patches blend flatter
    return a


def match(a, target_mean, saturation=1.0):
    """The old texture's mean colour, with the photo's colour variation (less saturated if asked)."""
    lum = a @ np.array([0.2126, 0.7152, 0.0722])
    a = lum[..., None] + (a - lum[..., None]) * saturation
    return a / a.reshape(-1, 3).mean(0) * np.asarray(target_mean) / 255.0


def resample(a, w, h):
    out = []
    for c in range(3):
        ch = Image.fromarray(a[..., c].astype(np.float32), "F").resize((w, h), Image.LANCZOS)
        out.append(np.asarray(ch, dtype=np.float64))
    return np.stack(out, -1)


def normal_map(albedo, strength, fine_sigma, tile=True):
    """Tangent-space normal map from the albedo's fine relief: dark is lower."""
    lum = albedo @ np.array([0.2126, 0.7152, 0.0722])
    mode = "wrap" if tile else "reflect"
    lum = lum - ndimage.gaussian_filter(lum, fine_sigma * 6, mode=mode)       # only the relief, not the broad shading
    h = ndimage.gaussian_filter(lum, fine_sigma, mode=mode)
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5                          # rows go down the image
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5


def drywall():
    # The painted wall photo (2026-10-28; the first drywall photo, Drywall_Photo.jpg, is kept for reference).
    a = load("PaintedWall_Photo.jpg")
    a = flatten(a, 120)
    tile = seamless(a)
    tile = resample(tile, 512, 512)                                             # one repeat = 0.6 m
    # Whatever broad shading is left (the blend, the photo) would show as a grid of 0.6 m squares: removed, wrapping.
    low = np.stack([ndimage.gaussian_filter(tile[..., c], 24, mode="wrap") for c in range(3)], -1)
    tile = tile / np.maximum(low, 1e-3) * tile.reshape(-1, 3).mean(0)
    tile = match(tile, (233.3, 232.2, 229.8), saturation=0.6)
    albedo = np.tile(tile, (4, 4, 1))
    save_rgb(albedo, os.path.join(TEX, "Plaster_Albedo.png"), alpha=True)
    save_rgb(normal_map(albedo, 22.0, 0.8), os.path.join(TEX, "Plaster_Normal.png"), alpha=True)


def ceiling():
    a = load("CeilingTile_Photo.jpg")
    rim = 22                                                                    # the grid's edge in the photo
    a = a[rim:-rim, rim:-rim]
    a = flatten(a, 160)
    tile = resample(a, 512, 512)
    tile = match(tile, (212.6, 212.6, 212.6), saturation=0.3)
    rng = np.random.default_rng(7)
    rows = []
    for j in range(4):
        row = []
        for i in range(4):
            t = np.rot90(tile, int(rng.integers(4)))
            if rng.random() < 0.5:
                t = t[:, ::-1]
            # A slightly different shade per tile (they age differently).
            row.append(t * (1.0 + (rng.random() - 0.5) * 0.04))
        rows.append(np.concatenate(row, 1))
    albedo = np.concatenate(rows, 0)
    save_rgb(albedo, os.path.join(TEX, "RefCeiling_Albedo.png"), alpha=True)
    save_rgb(normal_map(albedo, 14.0, 1.0), os.path.join(TEX, "RefCeiling_Normal.png"), alpha=True)


def duct():
    a = load("VentPanel_Photo.webp")
    a = flatten(a, 200)
    # One panel between the riveted strips (x 360..855), five ridge periods top to bottom (y 60..890).
    panel = a[60:890, 360:855]
    two = np.concatenate([panel, panel], 1)
    out = resample(two, 1024, 1024)
    out = match(out, (140.1, 140.6, 135.3), saturation=0.5)
    save_rgb(out, os.path.join(RES, "DuctMetal.png"))


# ---------------- The second set of photos (2026-10-28) ----------------
#
# Each becomes a tileable colour map with the photo's lighting divided out (the cavities and fibres stay: the ambient
# occlusion is in the colour) and a normal map from its fine relief. Textures the project's materials already reference
# keep their names and the old mean colour (the materials' tints still give the look); the rest go to
# Resources/Textures/Photo for the code that builds those materials at runtime. Most are "detail" maps: mean near white,
# so the material's tint gives the colour as the old generated textures did.

PHOTO = os.path.join(RES, "Photo")


def lowpass(a, sigma, mode="wrap"):
    return np.stack([ndimage.gaussian_filter(a[..., c], sigma, mode=mode) for c in range(a.shape[-1])], -1)


def unflatten_tile(tile, sigma):
    """What broad shading is left in a seamless tile (it would show as a grid when repeated): removed, wrapping."""
    return tile / np.maximum(lowpass(tile, sigma), 1e-3) * tile.reshape(-1, 3).mean(0)


def contrast(a, k):
    """The variation around the mean scaled by k."""
    m = a.reshape(-1, 3).mean(0)
    return m + (a - m) * k


def grid(tile, nx, ny, seed, flips=True):
    """nx x ny copies of a seamless tile, each flipped at random (a flipped seamless tile still meets its neighbours)."""
    rng = np.random.default_rng(seed)
    rows = []
    for j in range(ny):
        row = []
        for i in range(nx):
            t = tile
            if flips and rng.random() < 0.5:
                t = t[:, ::-1]
            if flips and rng.random() < 0.5:
                t = t[::-1, :]
            row.append(t)
        rows.append(np.concatenate(row, 1))
    return np.concatenate(rows, 0)


def detail(a, white=0.92, saturation=0.4):
    """A detail map: the photo's variation around a near-white mean (the material's tint gives the colour)."""
    return match(a, (white * 255.0,) * 3, saturation)


def save_pair(albedo, name, strength, sigma, folder=None):
    folder = folder or PHOTO
    os.makedirs(folder, exist_ok=True)
    save_rgb(albedo, os.path.join(folder, name + "_Albedo.png"), alpha=True)
    n = normal_map(albedo, strength, sigma)
    save_rgb(n, os.path.join(folder, name + "_Normal.png"), alpha=True)
    print("   normal xy std", np.round((n[..., :2] * 255).reshape(-1, 2).std(0), 1))


def carpet():
    """The office carpet (RefCarpet: M_Carpet and M_Ref_Carpet; one texture = 3 m of floor). The photo's pile and specks,
    made seamless as a 0.75 m patch and laid 4 x 4 with random flips; the old texture's slow patchiness on top (so a big
    floor does not show the grid), the old mean (the material's tan tint gives the colour)."""
    a = load("Carpet_Photo.jpg")
    a = flatten(a, 90)
    tile = seamless(a)
    tile = resample(tile, 512, 512)
    # Only the pile and specks (under about 2 cm): any blotch bigger than that would repeat every 0.75 m as a grid.
    tile = unflatten_tile(tile, 8)
    tile = contrast(tile, 0.9)
    floor = grid(tile, 4, 4, 11)
    old = load("RefCarpet_Generated.png")                                        # the generated carpet this replaces
    old = resample(old, 2048, 2048)
    slow = lowpass(old, 40)
    floor = floor * (slow / slow.reshape(-1, 3).mean(0))
    floor = match(floor, (183.0, 183.0, 183.0), saturation=0.35)
    save_pair(floor, "RefCarpet", 9.0, 0.9, TEX)


def cardboard():
    """Boxes (Cardboard: one texture = 0.5 m of box side). The kraft liner's fibres and specks with the old texture's
    corrugation ridges kept in the normal map, the old mean colour."""
    a = load("Kraft_Photo.jpg")
    a = flatten(a, 150)
    tile = seamless(a)
    tile = resample(tile, 1024, 1024)
    tile = unflatten_tile(tile, 40)
    tile = match(tile, (143.1, 109.5, 69.3), saturation=1.0)
    save_rgb(tile, os.path.join(TEX, "Cardboard_Albedo.png"), alpha=True)
    fine = normal_map(tile, 10.0, 0.8)
    old = load("Cardboard_Normal_Generated.png")                                 # the generated corrugation normals
    old = resample(old, 1024, 1024) * 2.0 - 1.0
    f = fine * 2.0 - 1.0
    n = np.stack([old[..., 0] + f[..., 0], old[..., 1] + f[..., 1], old[..., 2] * f[..., 2]], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    save_rgb(n * 0.5 + 0.5, os.path.join(TEX, "Cardboard_Normal.png"), alpha=True)


def paper():
    """The paper back of drywall (and the ceiling tiles' backs): the kraft's fibres as a detail map, about 0.5 m."""
    a = load("Kraft_Photo.jpg")
    a = flatten(a, 150)
    tile = resample(seamless(a), 512, 512)
    tile = unflatten_tile(tile, 24)
    save_pair(detail(grid(tile, 2, 2, 3), 0.9, 0.25), "Paper", 8.0, 0.8)


def galvanized():
    """Galvanised steel (spangle and a little rust): a square map for boxes, conduit and hanger wire (1 m, metre UVs),
    and a tall one for steel studs (whose UVs are 0.5 m across by 2 m along the stud)."""
    a = load("Galvanized_Photo.jpg")
    a = flatten(a, 220)
    tile = resample(seamless(a), 512, 512)                                      # about 0.4 m of sheet
    tile = unflatten_tile(tile, 48)
    sq = detail(grid(tile, 2, 2, 5), 0.86, 0.5)
    save_pair(sq, "Galvanized", 5.0, 1.0)
    tall = detail(grid(tile, 1, 4, 6), 0.86, 0.5)
    save_pair(tall, "GalvanizedStud", 5.0, 1.0)


def pine():
    """Pine studs (0.5 m across by 2 m along the grain: the panels' upright UVs) and the sledgehammer's handle: the
    board photo turned so the grain runs along v, laid four times along it."""
    a = load("Pine_Photo.jpg")
    a = flatten(a, 200)
    a = np.rot90(a)                                                             # grain along the image's height
    tile = resample(seamless(a), 512, 512)
    tile = unflatten_tile(tile, 40)
    # Pale, as a new stud is: the photo's orange latewood lines kept faint (they read as red streaks under the yellow light).
    save_pair(detail(contrast(grid(tile, 1, 4, 8, flips=True), 0.8), 0.94, 0.35), "Pine", 7.0, 0.9)


def end_grain():
    """A sawn or broken stud's end: growth rings and checks (one texture = about 12 cm)."""
    a = load("EndGrain_Photo.jpg")
    a = a[220:1480, 30:1290]                                                     # the end face, without the side
    a = flatten(a, 260)
    tile = resample(seamless(a), 512, 512)
    save_pair(detail(tile, 0.9, 0.6), "EndGrain", 10.0, 1.0)


def old_wood():
    """Old joists and the boards they carry, above the ceiling (metre UVs): weathered, dark, cracked."""
    a = load("OldWood_Photo.jpg")
    a = flatten(a, 160)
    tile = resample(seamless(a), 512, 512)                                      # about 0.5 m
    tile = unflatten_tile(tile, 36)
    save_pair(detail(grid(tile, 2, 2, 9), 0.85, 0.7), "OldWood", 8.0, 1.0)


def flex_duct():
    """The foil flex duct above the ceiling (metre UVs, u along the duct): the ridged band of the photo, seamless along
    the duct, about 0.35 m of it laid three times along 1 m."""
    a = load("FlexDuct_Photo.jpg")
    band = a[280:1200, :]
    band = flatten(band, 160)
    tile = seamless(band)
    tile = resample(tile, 384, 256)
    tile = unflatten_tile(tile, 24)
    out = grid(tile, 3, 4, 10, flips=False)
    save_pair(detail(resample(out, 1024, 1024), 0.9, 0.3), "FlexDuct", 6.0, 0.8)


def door_veneer():
    """The doors' oak veneer (build_door.py: 1 UV unit = 0.5 m, grain along U): a clean strip of the photographed door
    (no knob, grime or scratches), 1.5 m along the grain by 0.5 m across, turned so the grain runs along U. Its own
    honey colour, at the level the doors had."""
    a = load("Door_Photo.jpg")
    strip = a[200:1482, 260:687]                                                # 1.5 m x 0.5 m at about 855 px/m
    strip = flatten(strip, 220)
    strip = np.ascontiguousarray(np.transpose(strip, (1, 0, 2)))                # grain (the photo's height) along U
    tile = seamless(strip)
    tile = resample(tile, 1536, 512)
    tile = unflatten_tile(tile, 48)
    lum = tile @ np.array([0.2126, 0.7152, 0.0722])
    tile = tile * (100.0 / 255.0) / lum.mean()                                 # the doors' old brightness, the photo's hue
    save_pair(tile, "DoorVeneer", 4.0, 0.8)


def concrete():
    """Warehouse floors (floor UVs: one texture = 3 m): the trowelled slab, about 1.5 m, laid 2 x 2 with flips."""
    a = load("Concrete_Photo.jpg")
    a = flatten(a, 260)
    tile = resample(seamless(a), 1024, 1024)
    tile = unflatten_tile(tile, 96)
    floor = grid(tile, 2, 2, 12)
    floor = match(floor, (138.0, 132.0, 122.0), saturation=0.8)
    save_pair(floor, "Concrete", 6.0, 1.0)


def stains():
    """Two water stains from the photographed ceiling tiles, as multiply stamps (white = no change): the stain divided
    by the clean tile around it, the tile's own pits and specks left out. 512 px = one tile."""
    for src, name, rim, keep in (("StainedTileCorner_Photo.jpg", "StainCorner", 30, "tl"), ("StainedTileCentre_Photo.jpg", "StainCentre", 30, "")):
        a = load(src)
        a = a[rim:-rim, rim:-rim]
        lum = a @ np.array([0.2126, 0.7152, 0.0722])
        # The clean tile: a bright percentile of the neighbourhood (stains and pits are darker), smoothed.
        bg = ndimage.percentile_filter(ndimage.zoom(lum, 0.25, order=1), 85, size=31)
        bg = ndimage.gaussian_filter(bg, 6)
        bg = ndimage.zoom(bg, (lum.shape[0] / bg.shape[0], lum.shape[1] / bg.shape[1]), order=1)
        clean = a.reshape(-1, 3)[lum.reshape(-1) > np.percentile(lum, 60)].mean(0)
        lum_clean = clean @ np.array([0.2126, 0.7152, 0.0722])
        ratio = a / (clean * (bg / lum_clean)[..., None])
        ratio = np.clip(ratio, 0.0, 1.0)
        ratio = np.stack([ndimage.median_filter(ratio[..., c], 5) for c in range(3)], -1)   # pits out
        # Only the stain: where the tile is darker over an area (pits are tiny), feathered.
        dark = 1.0 - ratio @ np.array([0.2126, 0.7152, 0.0722])
        area = ndimage.gaussian_filter(dark, 18)
        mask = np.clip((area - 0.025) / 0.04, 0.0, 1.0)
        mask = ndimage.gaussian_filter(mask, 6)
        # The photographed tile's own bevel darkens its edges: faded out, except where the stain runs off the tile.
        h, w = mask.shape
        ramp = np.clip(np.arange(w) / 24.0, 0.0, 1.0)
        edge = np.ones_like(mask)
        if "l" not in keep: edge *= ramp[None, :]
        edge *= ramp[::-1][None, :]
        if "t" not in keep: edge *= ramp[:, None]
        edge *= ramp[::-1][:, None]
        mask *= edge
        stamp = 1.0 - (1.0 - ratio) * mask[..., None]
        stamp = resample(stamp, 512, 512)
        os.makedirs(PHOTO, exist_ok=True)
        save_rgb(stamp, os.path.join(PHOTO, name + ".png"))


# ---------------- The living rooms' orange-peel walls (2026-11-02) ----------------

HOME = os.path.join(RES, "Home")


def hp_seamless(h):
    """Makes a one-channel map tile without a join, keeping its histogram (Heitz & Neyret, "High-Performance By-Example
    Noise using a Histogram-Preserving Blending Operator", HPG 2018, with Burley's 1D lookup): the values are mapped to a
    Gaussian by rank, blended with themselves shifted by half (variance-preserving weights, across then down), and mapped
    back through the original's sorted values. A plain blend of two patches is flatter and its peaks lower."""
    from scipy.special import ndtri
    flat = h.ravel()
    order = np.argsort(flat, kind="stable")
    g = np.empty_like(flat)
    g[order] = ndtri((np.arange(flat.size) + 0.5) / flat.size)
    g = g.reshape(h.shape)
    for axis in (1, 0):
        n = g.shape[axis]
        w = np.sin(np.pi * (np.arange(n) + 0.5) / n) ** 2
        w = w[None, :] if axis == 1 else w[:, None]
        g = (g * w + np.roll(g, n // 2, axis) * (1 - w)) / np.sqrt(w * w + (1 - w) * (1 - w))
    # Back through the original's distribution (by rank, so exactly its histogram).
    out = np.empty_like(flat)
    out[np.argsort(g.ravel(), kind="stable")] = flat[order]
    return out.reshape(h.shape)


def orange_peel():
    """The homes' sprayed orange-peel drywall (MaterialMaps.OrangePeel; one texture = 0.6 m of wall), from the owner's
    photo (OrangePeel_Photo.jpg: about 0.55-0.6 m of wall, droplets 2-5 mm, 6144 px).

    The photo is lit from one side (from the top, a little from the right: its shading's spectrum follows cos^2 of the
    angle to that direction, the signature of raking light on an isotropic relief), so its brightness is the relief's
    slope toward the light, not its height. The height is that shading integrated along the light (shape from shading
    with one known light, in the Fourier domain, regularised where a frequency runs across the light and the photo says
    nothing about it), its slow waviness above about 2 cm kept faint. Then made seamless keeping its histogram, and the
    normal map taken from the height at its real slope (one texel = 0.29 mm). The colour is the paint's: an even
    near-white (the material's tint gives the warm off-white) darkened a little down in the hollows."""
    a = load("OrangePeel_Photo.jpg")
    a = a[: a.shape[0] // 2 * 2, : a.shape[1] // 2 * 2]
    a = (a[0::2, 0::2] + a[1::2, 0::2] + a[0::2, 1::2] + a[1::2, 1::2]) * 0.25          # 3072 px: 0.19 mm a pixel
    lum = a @ np.array([0.2126, 0.7152, 0.0722])
    shade = lum / np.maximum(ndimage.gaussian_filter(lum, 150, mode="reflect"), 1e-3) - 1.0   # the lighting's slow fall-off out
    # Taper the edges so the transform does not see a jump at the border (cropped off after).
    edge = 96
    h0, w0 = shade.shape
    ry = np.clip(np.minimum(np.arange(h0), np.arange(h0)[::-1]) / edge, 0, 1)
    rx = np.clip(np.minimum(np.arange(w0), np.arange(w0)[::-1]) / edge, 0, 1)
    shade = shade * (np.sin(ry * np.pi / 2) ** 2)[:, None] * (np.sin(rx * np.pi / 2) ** 2)[None, :]

    # Toward the light, in image axes (x right, y down): up and a little right (fitted: the axis at 102 degrees).
    t = np.radians(102.0)
    lx, ly = -np.cos(t), -np.sin(t)
    fy = np.fft.fftfreq(h0)[:, None]
    fx = np.fft.fftfreq(w0)[None, :]
    # Lambert at grazing light: shade ~ -grad(h).l, so in frequency S = -i 2pi (f.l) H.
    g = -2j * np.pi * (fx * lx + fy * ly)
    eps = (2 * np.pi * 0.0025) ** 2
    H = np.fft.fft2(shade) * np.conj(g) / (np.abs(g) ** 2 + eps)
    f = np.hypot(fx, fy)
    H *= 1.0 - 0.85 * np.exp(-(f * 110.0) ** 2)                                     # waves over ~2 cm kept faint
    H[0, 0] = 0
    h = np.real(np.fft.ifft2(H))[edge:-edge, edge:-edge]
    s = float(((h - h.mean()) ** 3).mean() / h.std() ** 3)
    print("   height skew", round(s, 3), "(droplets: a few high, rounded tops)")

    h = hp_seamless(h)
    side = 2048
    hi = Image.fromarray(h.astype(np.float32), "F").resize((side, side), Image.LANCZOS)
    h = np.asarray(hi, dtype=np.float64)
    h = ndimage.gaussian_filter(h, 0.7, mode="wrap")                              # the photo's own softness
    h = (h - h.mean()) / h.std()

    texel_mm = 600.0 / side
    relief_mm = 0.17                                                                 # the bumps' height spread (std; a little
    #                                    over the photo's, so the droplets still read on the camcorder's few hundred lines)
    hm = h * relief_mm
    dx = (np.roll(hm, -1, 1) - np.roll(hm, 1, 1)) / (2 * texel_mm)
    dy = (np.roll(hm, -1, 0) - np.roll(hm, 1, 0)) / (2 * texel_mm)                  # rows go down the image
    nx, ny, nz = -dx, dy, np.ones_like(hm)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5
    print("   slope rms", round(float(np.sqrt((dx * dx + dy * dy).mean())), 3))

    # The paint: even, a touch darker in the hollows (dust and the roller's thinner coat).
    hollow = np.clip(ndimage.gaussian_filter(h, 10, mode="wrap") - h, 0, None)
    shade_a = 1.0 - 0.03 * hollow
    albedo = np.repeat((shade_a / shade_a.mean() * 246.85 / 255.0)[..., None], 3, -1)
    os.makedirs(HOME, exist_ok=True)
    save_rgb(albedo, os.path.join(HOME, "OrangePeel_Albedo.png"))
    save_rgb(normal, os.path.join(HOME, "OrangePeel_Normal.png"))


def play_rug():
    """The children's road rug (the owner's tileable design, Source/PlayRug_Design.webp) -> Resources/Textures/Home/
    PlayRug_Albedo.png + PlayRug_Normal.png. One texture = 2.7 x 1.8 m of rug (OfficeChunkBuilder.Rug), so its two-lane
    roads are about 8 cm wide and its cars about 8 cm long, as on real ones made for toy cars.

    The print, a little faded (a rug left in an emptied house). The normal map is the pile, not the picture (a printed
    rug is flat where the ink changes): tufts of about 2 mm (one texel is 1.76 mm), the pile crushed and brushed in
    patches of a few centimetres, and a faint grid of the tufting rows. Made at the texture's own 3:2 size so its
    texels stay square on the rug."""
    a = np.asarray(Image.open(os.path.join(SRC, "PlayRug_Design.webp")).convert("RGB"), dtype=np.float64) / 255.0
    lum = a @ np.array([0.2126, 0.7152, 0.0722])
    albedo = lum[..., None] + (a - lum[..., None]) * 0.88                         # a touch faded
    albedo = albedo * 0.94 + 0.02
    h, w = lum.shape
    rng = np.random.default_rng(29)
    tufts = ndimage.gaussian_filter(rng.standard_normal((h, w)), 0.75, mode="wrap")
    crush = ndimage.gaussian_filter(rng.standard_normal((h, w)), 14.0, mode="wrap")
    rows = np.sin(np.arange(h) * 2 * np.pi / 1.6)[:, None] * np.ones((1, w))     # tufting rows, about 2.8 mm apart
    def unit(x): return (x - x.mean()) / x.std()
    relief = 0.65 * unit(tufts) + 0.25 * unit(crush) + 0.12 * rows                # mm-ish units, scaled below
    texel_mm = 2700.0 / w
    hm = relief * 0.5                                                             # the pile's height spread, mm (std)
    dx = (np.roll(hm, -1, 1) - np.roll(hm, 1, 1)) / (2 * texel_mm)
    dy = (np.roll(hm, -1, 0) - np.roll(hm, 1, 0)) / (2 * texel_mm)
    nx, ny, nz = -dx, dy, np.ones_like(hm)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5
    print("   slope rms", round(float(np.sqrt((dx * dx + dy * dy).mean())), 3))
    # The pile's shade in the print too: tufts a little lighter at their tips.
    albedo = albedo * (1.0 + 0.035 * unit(tufts))[..., None]
    os.makedirs(HOME, exist_ok=True)
    save_rgb(albedo, os.path.join(HOME, "PlayRug_Albedo.png"))
    save_rgb(normal, os.path.join(HOME, "PlayRug_Normal.png"))


def rug_binding():
    """A rug's bound edge (the overlocked yarn sewn round it) -> Resources/Textures/Home/RugBinding_Albedo.png +
    RugBinding_Normal.png. U runs along the edge (one texture = 5 cm: 20 wraps of yarn, 2.5 mm apart, leaning a little),
    V across it from the rug's top, over the rounded edge, to the floor. Grey: the material's tint gives the colour."""
    W, H = 512, 128
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    U, V = np.meshgrid(u, v)
    rng = np.random.default_rng(41)
    phase = (U + 0.06 * V) * 20.0                                                # 20 wraps a repeat, leaning
    jitter = ndimage.gaussian_filter1d(rng.standard_normal(W), 6, mode="wrap")[None, :] * 0.04
    f = np.mod(phase + jitter, 1.0)
    yarn = np.sqrt(np.clip(1.0 - (2.0 * f - 1.0) ** 2, 0.0, 1.0))                # round strands side by side
    fibres = ndimage.gaussian_filter(rng.standard_normal((H, W)), (2.0, 0.6), mode="wrap")
    hgt = yarn * 0.6 + fibres * 0.04                                              # mm
    texel_u = 50.0 / W
    texel_v = 25.0 / H                                                            # the profile is about 2.5 cm round
    dx = (np.roll(hgt, -1, 1) - np.roll(hgt, 1, 1)) / (2 * texel_u)
    dy = (np.roll(hgt, -1, 0) - np.roll(hgt, 1, 0)) / (2 * texel_v)
    nx, ny, nz = -dx, dy, np.ones_like(hgt)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5
    shade = 0.62 + 0.38 * yarn + fibres * 0.05                                    # the gaps between strands darker
    albedo = np.repeat((shade / shade.mean() * 0.85)[..., None], 3, -1)
    os.makedirs(HOME, exist_ok=True)
    save_rgb(albedo, os.path.join(HOME, "RugBinding_Albedo.png"))
    save_rgb(normal, os.path.join(HOME, "RugBinding_Normal.png"))


if __name__ == "__main__":
    import sys
    only = set(sys.argv[1:])
    for f in (drywall, ceiling, duct, carpet, cardboard, paper, galvanized, pine, end_grain, old_wood, flex_duct,
              door_veneer, concrete, stains, orange_peel, play_rug, rug_binding):
        if not only or f.__name__ in only:
            print("--", f.__name__)
            f()
