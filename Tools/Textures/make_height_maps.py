"""Height and cavity maps for Backrooms/Lit's surface detail (Shaders/BackroomsSurface.hlsl), from the normal maps.

For each tangent-space normal map (green up, as Unity's) listed below:
  - the height is the surface whose slopes the normal map holds, found by integrating them in the Fourier domain
    (Frankot & Chellappa 1988: least squares, exact for a tiling texture), the broad drift removed and the range set from
    the 1st to the 99th percentile (0 = deepest, 1 = top);
  - the cavity is how much of the sky each texel of that relief sees (ambient occlusion of the texture's own relief):
    the elevation of the horizon in 8 directions over a few radii, as a horizon-based AO, 1 = open, 0 = enclosed.
Written as RGB PNG (R height, G cavity, B 0) to Assets/Backrooms/Resources/Textures/Height/<name>_Height.png, at most
1024 x 1024 (the relief does not need more: the camcorder records about 480 lines). Imported linear (not sRGB).

    python Tools/Textures/make_height_maps.py [OrangePeel ...]
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
TEX = os.path.join(ROOT, "Assets", "Backrooms", "Textures")
RES = os.path.join(ROOT, "Assets", "Backrooms", "Resources", "Textures")
OUT = os.path.join(RES, "Height")

# Source normal map (relative to Assets/Backrooms), and how deep the relief is in texels of the output at full strength
# (only shapes the cavity: the parallax depth is set per material at runtime).
MAPS = [
    ("Textures/RefCarpet_Normal.png", 3.0),
    ("Textures/Carpet_Normal.png", 3.0),
    ("Textures/RefCeiling_Normal.png", 2.5),
    ("Textures/CeilingTile_Normal.png", 2.5),
    ("Textures/Wallpaper_Chevron_Normal.png", 1.5),
    ("Textures/Plaster_Normal.png", 1.5),
    ("Textures/Fabric_Normal.png", 2.0),
    ("Textures/FloralSofa/CreamWeave_Normal.png", 2.0),
    ("Textures/Cardboard_Normal.png", 1.5),
    ("Resources/Textures/Photo/Concrete_Normal.png", 2.0),
    ("Resources/Textures/Photo/OldWood_Normal.png", 2.0),
    ("Resources/Textures/Photo/DoorVeneer_Normal.png", 1.0),
    ("Resources/Textures/Home/OrangePeel_Normal.png", 2.0),
]


def load_normal(path):
    a = np.asarray(Image.open(path).convert("RGB"), dtype=np.float64) / 255.0
    n = a * 2.0 - 1.0
    nz = np.clip(n[..., 2], 0.15, 1.0)
    # Slopes in texel units: x along the image's columns (u), y along its rows (rows go down, v goes up; green is up).
    p = -n[..., 0] / nz
    q = n[..., 1] / nz
    return p, q


def integrate(p, q):
    h, w = p.shape
    wx = np.fft.fftfreq(w) * 2.0 * np.pi
    wy = np.fft.fftfreq(h) * 2.0 * np.pi
    WX, WY = np.meshgrid(wx, wy)
    P, Q = np.fft.fft2(p), np.fft.fft2(q)
    # Gradient of a periodic grid by the exact difference operator (matches finite slopes better than i*omega).
    DX, DY = np.exp(1j * WX) - 1.0, np.exp(1j * WY) - 1.0
    denom = np.abs(DX) ** 2 + np.abs(DY) ** 2
    denom[0, 0] = 1.0
    Z = (np.conj(DX) * P + np.conj(DY) * Q) / denom
    Z[0, 0] = 0.0
    # Remove the broad drift (a normal map's low frequencies are unreliable): a gentle high-pass.
    r2 = WX ** 2 + WY ** 2
    cut = (2.0 * np.pi / 48.0) ** 2
    Z *= r2 / (r2 + cut)
    return np.real(np.fft.ifft2(Z))


def resize(a, size):
    im = Image.fromarray(a.astype(np.float32), mode="F")
    return np.asarray(im.resize(size, Image.BICUBIC), dtype=np.float64)


def cavity(height, depth_texels):
    """Horizon-based ambient occlusion of the relief (tiling): for 8 directions and radii up to 12 texels, the highest
    elevation of the horizon; occlusion is the mean sine of it (cosine-weighted hemisphere, as GTAO)."""
    hgt = height * depth_texels
    occ = np.zeros_like(hgt)
    dirs = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)]
    for dx, dy in dirs:
        best = np.zeros_like(hgt)
        dlen = np.hypot(dx, dy)
        for r in (1, 2, 3, 5, 8, 12):
            shifted = np.roll(np.roll(hgt, -dy * r, axis=0), -dx * r, axis=1)
            dist = r * dlen
            slope = (shifted - hgt) / dist
            best = np.maximum(best, slope)
        sin_h = best / np.sqrt(1.0 + best * best)
        occ += sin_h
    occ /= len(dirs)
    return np.clip(1.0 - occ * 1.6, 0.0, 1.0)


def main(only=()):
    os.makedirs(OUT, exist_ok=True)
    for rel, depth in MAPS:
        if only and not any(o in rel for o in only):
            continue
        src = os.path.join(ROOT, "Assets", "Backrooms", rel)
        if not os.path.exists(src):
            print("missing", rel)
            continue
        p, q = load_normal(src)
        z = integrate(p, q)
        h, w = z.shape
        scale = min(1.0, 1024.0 / max(h, w))
        size = (max(4, int(round(w * scale))), max(4, int(round(h * scale))))
        if scale < 1.0:
            z = resize(z, size)
        lo, hi = np.percentile(z, 1.0), np.percentile(z, 99.0)
        height = np.clip((z - lo) / max(hi - lo, 1e-6), 0.0, 1.0)
        cav = cavity(height, depth)
        rgb = np.zeros((size[1], size[0], 3), dtype=np.uint8)
        rgb[..., 0] = np.round(height * 255.0)
        rgb[..., 1] = np.round(cav * 255.0)
        name = os.path.splitext(os.path.basename(rel))[0].replace("_Normal", "") + "_Height.png"
        Image.fromarray(rgb, mode="RGB").save(os.path.join(OUT, name), optimize=True)
        print(f"{name}: {size[0]}x{size[1]}, height mean {height.mean():.2f}, cavity mean {cav.mean():.2f} min {cav.min():.2f}")


if __name__ == "__main__":
    import sys
    main(sys.argv[1:])                                                          # names to redo (all if none)
