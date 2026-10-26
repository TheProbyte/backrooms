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
    a = load("Drywall_Photo.jpg")
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


if __name__ == "__main__":
    drywall()
    ceiling()
    duct()
