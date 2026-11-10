"""The orientation video's soundtrack, made in code: a 1970s library-music bed, two jingles, the sound effects and a tape
finish (wow and flutter, a soft top end, hiss). No samples. Times follow anim.js's SCENES; the voiceover is mixed in later.
python audio.py out.wav"""
import sys
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

SR = 44100
TOTAL = 165.0
N = int(TOTAL * SR)
rng = np.random.default_rng(7)
mus = np.zeros((N, 2))
sfx = np.zeros((N, 2))


def t_(dur):
    return np.arange(int(dur * SR)) / SR


def add(buf, at, sig, gain=1.0, pan=0.0):
    i = int(at * SR)
    if i >= N or i + len(sig) <= 0:
        return
    s = sig[: max(0, N - i)]
    l, r = np.sqrt(0.5 * (1 - pan)), np.sqrt(0.5 * (1 + pan))
    buf[i:i + len(s), 0] += s * gain * l
    buf[i:i + len(s), 1] += s * gain * r


def env(n, a=0.005, d=0.3, curve=4.0):
    t = np.arange(n) / SR
    e = np.minimum(1, t / max(a, 1e-4)) * np.exp(-t * curve / max(d, 1e-3))
    return e


def mx(*xs):
    """Sums sounds of different lengths."""
    out = np.zeros(max(len(x) for x in xs))
    for x in xs:
        out[:len(x)] += x
    return out


def lp(x, hz, order=2):
    return sosfilt(butter(order, hz, 'low', fs=SR, output='sos'), x)


def hp(x, hz, order=2):
    return sosfilt(butter(order, hz, 'high', fs=SR, output='sos'), x)


def bp(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], 'band', fs=SR, output='sos'), x)


def noise(dur):
    return rng.uniform(-1, 1, int(dur * SR))


def mtof(m):
    return 440.0 * 2 ** ((m - 69) / 12)


# ---------------- instruments ----------------
def rhodes(m, dur, vel=1.0):
    t = t_(dur + 0.6)
    f0 = mtof(m)
    s = np.sin(2 * np.pi * f0 * t + 0.6 * np.sin(2 * np.pi * f0 * 14 * t) * np.exp(-t * 18))
    s += 0.35 * np.sin(2 * np.pi * f0 * 2 * t) * np.exp(-t * 3)
    s *= (1 + 0.25 * np.sin(2 * np.pi * 5.2 * t))
    e = np.minimum(1, t / 0.004) * np.exp(-t * 1.6)
    e[int(dur * SR):] *= np.exp(-np.arange(len(e) - int(dur * SR)) / SR * 14)
    return s * e * vel * 0.25


def bass(m, dur, vel=1.0):
    t = t_(dur)
    f0 = mtof(m)
    s = np.sin(2 * np.pi * f0 * t) + 0.3 * np.sin(2 * np.pi * f0 * 2 * t) + 0.12 * np.sign(np.sin(2 * np.pi * f0 * t))
    e = np.minimum(1, t / 0.006) * np.exp(-t * 3.5)
    e[-int(0.02 * SR):] *= np.linspace(1, 0, int(0.02 * SR))
    return lp(s * e, 900) * vel * 0.42


def vibes(m, dur=1.2, vel=1.0):
    t = t_(dur)
    f0 = mtof(m)
    s = np.sin(2 * np.pi * f0 * t) + 0.25 * np.sin(2 * np.pi * f0 * 4 * t) * np.exp(-t * 8)
    s *= 1 + 0.35 * np.sin(2 * np.pi * 6 * t)
    return s * env(len(t), 0.002, dur * 0.7, 3) * vel * 0.22


def brass(ms, dur, vel=1.0):
    t = t_(dur)
    s = np.zeros_like(t)
    for m in ms:
        f0 = mtof(m)
        ph = 2 * np.pi * f0 * t * (1 + 0.002 * np.sin(2 * np.pi * 5 * t))
        s += (2 * ((ph / (2 * np.pi)) % 1) - 1)
    e = np.minimum(1, t / 0.03) * np.where(t < dur - 0.15, 1, np.maximum(0, (dur - t) / 0.15))
    return lp(s * e, 2200) * vel * 0.08


def kick():
    t = t_(0.35)
    fr = 50 + 90 * np.exp(-t * 30)
    return np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t * 9) * 0.6


def snare():
    t = t_(0.22)
    return (bp(noise(0.22), 1200, 6000)[:len(t)] * 0.5 + np.sin(2 * np.pi * 190 * t) * 0.3) * np.exp(-t * 22) * 0.45


def hat(open_=False):
    d = 0.18 if open_ else 0.05
    return hp(noise(d), 7000) * np.exp(-t_(d) * (18 if open_ else 70)) * 0.18


def pad(ms, dur, vel=1.0):
    t = t_(dur)
    s = np.zeros_like(t)
    for m in ms:
        for det in (-0.12, 0.0, 0.11):
            s += np.sin(2 * np.pi * mtof(m + det) * t)
    e = np.minimum(1, t / 2.5) * np.minimum(1, np.maximum(0, (dur - t) / 2.5))
    return lp(s * e, 1400) * vel * 0.05


# ---------------- the music ----------------
BPM = 112
BEAT = 60 / BPM
BAR = 4 * BEAT
# F major: Fmaj7, Dm7, Gm7, C7 (I vi ii V), then Bbmaj7, Am7, Gm7, C7
CHORDS = [[53, 57, 60, 64], [50, 53, 57, 60], [55, 58, 62, 65], [48, 52, 55, 58],
          [58, 62, 65, 69], [57, 60, 64, 67], [55, 58, 62, 65], [48, 52, 55, 58]]
ROOTS = [41, 38, 43, 36, 46, 45, 43, 36]
MELODY = [  # (beat offset in the 8-bar phrase, midi, length in beats)
    (0, 72, 1), (1, 76, 1), (2, 79, 2), (4, 77, 1), (5, 74, 1), (6, 72, 2), (8, 70, 1), (9, 74, 1), (10, 77, 2),
    (12, 76, 1.5), (13.5, 74, 0.5), (14, 72, 2), (16, 74, 1), (17, 77, 1), (18, 81, 2), (20, 79, 1), (21, 76, 1),
    (22, 72, 2), (24, 70, 1), (25, 74, 1), (26, 79, 1.5), (27.5, 77, 0.5), (28, 76, 2), (30, 72, 2)]


def band(start, end, drums=True, melody=True, intensity=1.0):
    bar = 0
    t0 = start
    while t0 < end - 0.05:
        ci = bar % 8
        ch, root = CHORDS[ci], ROOTS[ci]
        # electric piano: chord on 1, a push on the and of 2
        for m in ch:
            add(mus, t0, rhodes(m, BEAT * 1.4, 0.8), 0.9, -0.25)
            add(mus, t0 + BEAT * 2.5, rhodes(m, BEAT * 1.2, 0.55), 0.9, -0.25)
        # bass: root, fifth, approach
        for b, m, ln in ((0, root, 1.5), (1.5, root + 7, 0.5), (2, root + 12, 1), (3, root + 7 if ci % 2 else root + 5, 1)):
            add(mus, t0 + b * BEAT, bass(m, ln * BEAT * 0.95), 1.0, 0.0)
        if drums:
            for b in range(4):
                if b in (0, 2):
                    add(mus, t0 + b * BEAT, kick(), 0.8 * intensity)
                if b in (1, 3):
                    add(mus, t0 + b * BEAT, snare(), 0.55 * intensity, 0.1)
                for h in (0, 0.5):
                    add(mus, t0 + (b + h) * BEAT, hat(open_=(b == 3 and h == 0.5)), 0.8, 0.35)
        if melody:
            for off, m, ln in MELODY:
                if int(off // 4) == ci:
                    at = t0 + (off - ci * 4) * BEAT
                    if at < end:
                        add(mus, at, vibes(m, ln * BEAT + 0.6), 0.8, 0.3)
        t0 += BAR
        bar += 1


def jingle(at, ending=False):
    # a rising vibraphone arpeggio and a brass chord stab
    for i, m in enumerate((65, 69, 72, 77)):
        add(mus, at + i * 0.11, vibes(m + 12, 1.6, 1.1), 1.2, -0.2 + i * 0.13)
    add(mus, at + 0.45, brass([53, 57, 60, 65], 1.6 if not ending else 3.2, 1.2), 1.1)
    add(mus, at + 0.45, kick(), 0.9)
    if ending:
        add(mus, at + 0.45, pad([41, 53, 57, 60, 64], 4.0, 2.0), 1.0)


band(3.0, 89.9)
jingle(2.55)
# the closing bell to the stamp: a low, uneasy drone, a clock, a few low notes
add(mus, 90.4, pad([38, 45, 50, 53], 28.0, 1.4), 1.0)
for i, m in enumerate((38, 41, 37, 38, 44, 41)):
    add(mus, 93 + i * 4.1, rhodes(m, 2.0, 0.9), 0.7, -0.3)
for i in range(int((90.0 - 88.0) / 0.5)):
    add(sfx, 88.0 + i * 0.5, bp(noise(0.03), 2500, 6000) * np.exp(-t_(0.03) * 120), 0.5, -0.4)
# back to the band for the recovery bay, building to the end
band(123.0, 142.0, drums=True, melody=True, intensity=0.8)
band(142.0, 147.3, drums=True, melody=False, intensity=1.2)
jingle(147.0, ending=True)

# ---------------- sound effects ----------------
def thud(depth=1.0, dur=0.25):
    t = t_(dur)
    fr = 70 + 60 * np.exp(-t * 25)
    return (np.sin(2 * np.pi * np.cumsum(fr) / SR) * 0.7 + lp(noise(dur), 900)[:len(t)] * 0.4) * np.exp(-t * 14) * depth


def pop(pitch=1.0):
    t = t_(0.12)
    fr = 500 * pitch + 900 * pitch * np.exp(-t * 60)
    return np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t * 30) * 0.5


def boing(pitch=1.0):
    t = t_(0.35)
    fr = 220 * pitch * (1 + 0.6 * np.exp(-t * 6) * np.sin(2 * np.pi * 14 * t))
    return np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t * 8) * 0.35


def whoosh(dur=0.4, lo=400, hi=3000):
    n = noise(dur)
    e = np.sin(np.pi * np.linspace(0, 1, len(n))) ** 2
    return bp(n, lo, hi) * e * 0.5


def stamp():
    return mx(thud(1.2, 0.3), hp(noise(0.08), 1500) * np.exp(-t_(0.08) * 50) * 0.5)


def click():
    return mx(hp(noise(0.012), 2000) * np.exp(-t_(0.012) * 300) * 0.8, np.sin(2 * np.pi * 1800 * t_(0.012)) * 0.3 * np.exp(-t_(0.012) * 300))


def bell(dur=2.6):
    t = t_(dur)
    s = np.zeros_like(t)
    for f, a in ((1250, 1.0), (2950, 0.5), (4100, 0.3), (5600, 0.2)):
        s += a * np.sin(2 * np.pi * f * t)
    trill = (np.sin(2 * np.pi * 22 * t) > 0).astype(float)
    return s * trill * np.minimum(1, t / 0.01) * np.minimum(1, (dur - t) / 0.2) * 0.12


def step(heavy=1.0):
    return mx(lp(noise(0.06), 1200) * np.exp(-t_(0.06) * 60) * 0.35 * heavy, thud(0.15, 0.06))


def scrape(dur):
    n = bp(noise(dur), 300, 2200)
    n *= 1 + 0.5 * np.sin(2 * np.pi * 9 * t_(dur))
    return n * np.minimum(1, np.minimum(t_(dur) / 0.05, (dur - t_(dur)) / 0.05)) * 0.3


def rumble(dur, hz=120):
    n = lp(noise(dur), hz * 3)
    return (n * 0.6 + np.sin(2 * np.pi * hz * t_(dur)) * 0.15) * np.minimum(1, np.minimum(t_(dur) / 0.1, (dur - t_(dur)) / 0.1)) * 0.5


def gliss(dur, f0, f1, wave='sin', gain=0.2):
    t = t_(dur)
    fr = f0 * (f1 / f0) ** (t / dur)
    ph = 2 * np.pi * np.cumsum(fr) / SR
    s = np.sin(ph) if wave == 'sin' else 2 * ((ph / (2 * np.pi)) % 1) - 1
    return s * np.sin(np.pi * t / dur) * gain


def beep(f=1000, dur=0.12):
    return np.sign(np.sin(2 * np.pi * f * t_(dur))) * 0.08 * np.minimum(1, (dur - t_(dur)) / 0.01)


def static(dur):
    return hp(noise(dur), 300) * 0.35


# title (0-8)
add(sfx, 0.0, click(), 1.0)
add(sfx, 0.0, static(0.8) * np.linspace(1, 0.2, int(0.8 * SR)), 0.8)
add(sfx, 1.0, whoosh(0.6, 300, 2500), 0.9)
add(sfx, 1.85, thud(1.1), 1.0)
for i, at in enumerate((2.25, 2.4, 2.55)):
    add(sfx, at + 0.1, pop(1 + i * 0.25), 0.8, -0.3)
add(sfx, 2.4, whoosh(0.5, 600, 4000), 0.7, 0.4)
for i, at in enumerate((4.2, 4.35, 4.5, 4.65)):
    add(sfx, at, pop(0.8 + i * 0.12), 0.6, -0.6 + i * 0.4)
for i in range(3):
    add(sfx, 5.33 + i * 0.333, boing(1.2), 0.7)
# hired (8-16)
for i in range(3):
    add(sfx, 8.4 + i * 0.8, boing(1.0 + i * 0.1), 0.8, -0.3)
add(sfx, 10.4, whoosh(0.5), 0.5)
add(sfx, 11.2, mx(thud(0.8), gliss(0.25, 600, 300, 'sin', 0.1)), 1.0)
# he turns round to us, and the grin
add(sfx, 13.2, whoosh(0.22, 900, 4000), 0.6)
add(sfx, 14.2, pop(1.3), 0.5)
# full (16-27)
for i in range(10):
    add(sfx, 17.2 + i * 0.55 + 0.1, pop(0.7 + (i % 4) * 0.15), 0.7, -0.4 + (i % 3) * 0.3)
add(sfx, 22.0, mx(thud(1.4, 0.4), hp(noise(0.15), 800) * np.exp(-t_(0.15) * 30) * 0.6), 1.1, 0.5)
for i in range(9):
    add(sfx, 22.2 + i * 0.12 + 0.3, thud(0.6, 0.15), 0.8, 0.5)
add(sfx, 24.4, gliss(1.0, 110, 70, 'saw', 0.06), 1.0, -0.3)
add(sfx, 25.2, stamp(), 1.1)
# more world (27-40)
for i in range(8):
    add(sfx, 27.1 + i * 0.36, step(), 0.7)
add(sfx, 30.3, mx(click(), thud(0.4, 0.08)), 1.0, 0.3)
add(sfx, 30.36, beep(880, 0.1), 0.6, 0.3)
add(sfx, 30.5, beep(1320, 0.14), 0.6, 0.3)
add(sfx, 30.6, gliss(1.4, 300, 1200, 'sin', 0.12), 1.0, 0.3)
add(sfx, 30.9, mx(thud(1.0, 0.5), whoosh(0.6, 100, 800)), 1.0, 0.3)
add(sfx, 31.3, rumble(1.9, 60), 1.0)
hum = (np.sin(2 * np.pi * 120 * t_(6.0)) + 0.4 * np.sin(2 * np.pi * 240 * t_(6.0))) * 0.03
add(sfx, 34.0, hum * np.minimum(1, np.minimum(t_(6.0) / 1.0, (6.0 - t_(6.0)) / 1.0)), 1.0)
# leasing (40-52)
add(sfx, 40.6, mx(scrape(0.9) * 0.8, whoosh(0.9, 200, 1500)), 0.9)
add(sfx, 41.55, thud(0.8, 0.2), 0.9)
for i in range(30):
    add(sfx, 40.3 + i * 0.385, thud(0.25, 0.08), 0.6, -0.6)
# seams (52-68)
add(sfx, 52.0, rumble(16.0, 50) * 0.25, 1.0, 0.4)
for i in range(3):
    add(sfx, 54.5 + i * 0.35, lp(beep(1050, 0.15), 1200), 0.8, 0.5)
add(sfx, 56.0, lp(sum(np.sin(2 * np.pi * f * t_(0.6)) for f in (2093, 2637, 3136)) * np.exp(-t_(0.6) * 6) * 0.08, 1800), 1.0, 0.5)
for i in range(4):
    add(sfx, 57.4 + i * 0.16, lp(bp(noise(0.08), 500, 3000) * np.exp(-t_(0.08) * 40), 1500), 0.8, 0.5)
# he scampers round to the back of the cart
for i in range(6):
    add(sfx, 57.8 + i * 0.14, step(0.7), 0.6, -0.3)
add(sfx, 58.4, scrape(2.0), 0.8)
add(sfx, 60.3, thud(0.7, 0.2), 0.8, 0.3)
for i in range(5):
    add(sfx, 61.0 + i * 1.1, mx(scrape(0.75) * 0.6, gliss(0.75, 500, 300, 'sin', 0.03)), 0.8, 0.3)
    add(sfx, 61.95 + i * 1.1, thud(0.7, 0.2), 0.9, 0.1)
# contracts (68-88)
add(sfx, 68.8, hp(noise(0.5), 2000) * np.sin(np.pi * np.linspace(0, 1, int(0.5 * SR))) * 0.12, 1.0, -0.5)
add(sfx, 71.1, whoosh(0.25, 800, 4000), 0.35)
for i in range(12):
    add(sfx, 71.3 + i * 0.34, step(0.8), 0.6)
add(sfx, 72.0, rumble(3.4, 90) * 0.6, 0.8)
add(sfx, 75.4, scrape(0.3) * 0.5, 0.8, 0.5)
add(sfx, 75.8, mx(vibes(96, 0.8, 1.0), vibes(91, 0.8, 0.8)), 0.8, 0.5)
for at in (79.2, 79.9, 80.6):
    add(sfx, at, whoosh(0.25, 800, 5000), 0.6)
    add(sfx, at + 0.22, stamp() * 0.8, 1.0)
add(sfx, 83.0, boing(1.4), 0.7, 0.6)
add(sfx, 85.6, gliss(0.8, 1400, 200, 'sin', 0.16), 1.0, -0.2)
add(sfx, 86.4, mx(thud(1.2, 0.35), hp(noise(0.2), 500) * np.exp(-t_(0.2) * 25) * 0.5), 1.0, -0.2)
# closing (88-106)
add(sfx, 90.0, bell(2.6), 1.0, -0.4)
# the worker jumps at the bell, winds up on the spot, then zips off to the door
add(sfx, 90.15, boing(1.5), 0.7, 0.2)
for i in range(10):
    add(sfx, 91.2 + i * 0.05, step(0.6), 0.6, 0.2)
add(sfx, 91.7, whoosh(0.7, 500, 5000), 0.9, 0.3)
for i in range(7):
    add(sfx, 91.7 + i * 0.1, step(0.8), 0.7, 0.3)
add(sfx, 93.0, gliss(1.5, 120, 40, 'saw', 0.05), 1.0)
add(sfx, 91.9, gliss(0.3, 200, 260, 'saw', 0.03), 1.0, 0.5)
add(sfx, 93.2, thud(1.1, 0.4), 1.0, 0.5)
# the light banks go off one by one: a heavy relay clunk each
for at in (98.2, 99.0, 99.8):
    add(sfx, at, mx(thud(0.9, 0.3), lp(hp(noise(0.05), 600), 3000) * np.exp(-t_(0.05) * 60) * 0.6), 1.0, 0.1)
# in the dark, the boxes drag across the floor
for at, ln in ((99.85, 0.35), (100.95, 0.8), (102.6, 0.9)):
    add(sfx, at, lp(scrape(ln), 900) * 0.9, 0.9, 0.2)
# three flickers: the tubes buzz on for a moment; the boxes have moved
for at, ln in ((100.2, 0.65), (101.9, 0.65), (103.7, 0.8)):
    fl = (np.sin(2 * np.pi * 120 * t_(ln)) + 0.5 * np.sign(np.sin(2 * np.pi * 240 * t_(ln)))) * 0.05
    add(sfx, at, fl, 1.0)
    add(sfx, at + 0.02, thud(0.5, 0.4), 0.8, 0.3)
# phenomenon (106-118)
hum2 = (np.sin(2 * np.pi * 120 * t_(2.2)) * 0.03)
add(sfx, 106.0, hum2, 1.0, -0.4)
add(sfx, 108.2, click(), 1.0, -0.4)
add(sfx, 110.0, scrape(0.6), 0.9, 0.3)
add(sfx, 111.0, thud(1.0, 0.3), 1.0, 0.3)
add(sfx, 114.4, stamp(), 1.1)
add(sfx, 116.2, stamp() * 0.7, 1.0)
# returned (118-132)
for i in range(5):
    add(sfx, 118.1 + i * 0.36, step(), 0.6)
# the toe catches the box (a scuff), the stagger, down flat, the box ahead, the bonk, the hat
add(sfx, 119.5, mx(lp(scrape(0.15), 1500) * 0.8, thud(0.35, 0.06)), 0.9)
add(sfx, 119.62, whoosh(0.4, 300, 2200), 0.7)
add(sfx, 120.2, mx(thud(1.3, 0.4), gliss(0.4, 300, 120, 'saw', 0.05)), 1.1)
add(sfx, 120.35, thud(0.6, 0.15), 0.7, 0.4)
add(sfx, 120.45, mx(boing(0.7), thud(0.6, 0.15)), 1.0)
add(sfx, 120.7, mx(click(), click() * 0.6), 0.6, 0.5)
for i in range(6):
    add(sfx, 120.6 + i * 0.25, vibes(96 + (i % 3) * 3, 0.4, 0.6), 0.5, -0.5 + i * 0.2)
add(sfx, 121.8, mx(gliss(1.1, 900, 150, 'sin', 0.08), whoosh(1.1, 200, 1200) * 0.5), 1.0)
add(sfx, 123.6, mx(rumble(1.2, 160), bp(noise(1.2), 2000, 5000) * 0.08 * (np.sin(2 * np.pi * 30 * t_(1.2)) > 0)), 0.9, 0.4)
add(sfx, 124.8, thud(1.0, 0.3), 1.0, 0.4)
for i in range(6):
    add(sfx, 125.0 + i * 0.3, step(0.9), 0.7)
add(sfx, 127.2, mx(hp(noise(0.06), 600) * np.exp(-t_(0.06) * 40) * 0.9, thud(0.4, 0.1)), 1.0, 0.3)
add(sfx, 128.8, mx(click(), thud(0.3, 0.08)), 1.0, -0.6)
add(sfx, 129.0, boing(0.9), 0.6, 0.2)
for i in range(4):
    add(sfx, 130.3 + i * 0.32, step(0.7), 0.6, -0.2 - i * 0.1)
add(sfx, 131.5, gliss(0.3, 200, 260, 'saw', 0.03), 0.8, -0.5)
# camera (132-142)
add(sfx, 132.2, whoosh(0.6, 200, 1500), 0.8)
add(sfx, 133.2, beep(1800, 0.08), 1.0)
add(sfx, 133.32, beep(2400, 0.12), 1.0)
add(sfx, 133.3, lp(noise(8.0), 300) * 0.04, 1.0)
add(sfx, 137.0, stamp(), 1.0, -0.3)
add(sfx, 139.4, stamp(), 1.0, 0.2)
# slogan (142-152)
for at in (142.3, 143.5, 144.8):
    add(sfx, at, whoosh(0.25, 800, 5000), 0.6)
    add(sfx, at + 0.22, stamp() * 0.9, 1.0)
for i in range(16):
    add(sfx, 142.0 + i * 0.33, step(0.7), 0.5, -0.8 + i * 0.1)
# the tape clicks off
add(sfx, 152.0, mx(gliss(0.45, 3000, 80, 'sin', 0.12), click()), 1.0)
add(sfx, 152.5, mx(thud(0.6, 0.15), click()), 1.0)
# the hidden take: a burst of noise and a low, dragged tone
add(sfx, 161.8, static(0.3) * 0.8, 1.0)
add(sfx, 162.1, lp(noise(2.1), 220) * 0.35 + (np.sin(2 * np.pi * 55 * t_(2.1)) * 0.05), 1.0)
add(sfx, 163.0, bp(noise(0.3), 800, 3000) * 0.08 * (np.sin(2 * np.pi * 18 * t_(0.3)) > 0), 1.0)
add(sfx, 163.3, gliss(0.7, 90, 60, 'saw', 0.12), 1.0)
add(sfx, 164.0, static(0.3) * 0.6, 1.0)
add(sfx, 164.3, mx(thud(0.5, 0.1), click()), 0.8, 0.3)
add(sfx, 164.7, click(), 1.0)

# the stripe wipes between sections
for c in (8, 16, 27, 52, 68, 88, 118, 132, 142):
    add(sfx, c - 0.22, whoosh(0.45, 300, 3500), 0.55, 0.0)
# the contract close-up: the card, then the APPROVED stamp
add(sfx, 69.0, whoosh(0.25, 800, 4000), 0.4)
add(sfx, 69.62, stamp() * 0.8, 1.0)
# into the viewfinder: two little beeps
add(sfx, 140.42, beep(2000, 0.06), 0.7)
add(sfx, 140.56, beep(2600, 0.08), 0.7)
# ---------------- the mix and the tape ----------------
mix = mus * 0.55 + sfx * 0.8
# wow and flutter: a slowly wandering delay
t = np.arange(N) / SR
d = 0.0016 * np.sin(2 * np.pi * 0.55 * t) + 0.00025 * np.sin(2 * np.pi * 6.3 * t)
src = np.clip(t - d, 0, TOTAL - 1 / SR) * SR
for ch in range(2):
    mix[:, ch] = np.interp(src, np.arange(N), mix[:, ch])
    mix[:, ch] = lp(mix[:, ch], 9500, 2)
    mix[:, ch] = hp(mix[:, ch], 45, 2)
# hiss while the tape plays (not on the blue screen)
hiss = hp(rng.normal(0, 1, (N, 2)), 3000, 2) * 0.006
playing = ((t < 152.5) | ((t > 161.8) & (t < 164.7))).astype(float)
playing = np.convolve(playing, np.ones(2000) / 2000, mode='same')
mix += hiss * playing[:, None]
# soft saturation and level
mix = np.tanh(mix * 1.6) / 1.6
peak = np.max(np.abs(mix))
mix = mix / peak * 0.89
wavfile.write(sys.argv[1], SR, (mix * 32767).astype(np.int16))
print('wrote', sys.argv[1], f'{TOTAL:.1f}s peak-normalised')
