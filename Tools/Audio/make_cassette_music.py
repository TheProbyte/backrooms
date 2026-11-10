"""
The music on the cassette found in the vent rooms ("lunch break mix", side A, 2026-11-04): an original piece, composed
here and synthesized from scratch (no samples): a late-1990s easy-listening instrumental at 84 bpm in D major, its
groove lightly swung: an electric piano comping (two-operator FM, like a tine piano), a round finger bass, brushes
(a soft kick, a brushed snare, a shaker), a warm pad in the bridge and a vibraphone carrying the tune.

    python Tools/Audio/make_cassette_music.py Assets/Backrooms/Resources/Audio/Cassette_LunchBreakMix.wav

Mono, 24 kHz, 16-bit: a cassette's bandwidth (the game's tape filter adds the rest: wow, flutter, hiss, the roll-off).
A second and a half of blank tape before it starts and six after it ends (the deck plays on into hiss).
"""
import sys
import wave
import numpy as np

SR = 24000
BPM = 84.0
BEAT = 60.0 / BPM
BAR = 4 * BEAT
LEAD_IN, TAIL = 1.5, 6.0
rng = np.random.default_rng(1997)


def midi(n):
    return 440.0 * 2.0 ** ((n - 69) / 12.0)


def swing(beat):
    """Off-beat eighths a little late (a light swing)."""
    f = beat % 1.0
    return beat - f + (0.58 if abs(f - 0.5) < 1e-6 else f)


def env(n, a, tau, rel, sus_len):
    t = np.arange(n) / SR
    e = np.minimum(1.0, t / max(a, 1e-4)) * np.exp(-t / tau)
    r = np.clip((t - sus_len) / rel, 0.0, 1.0)
    return e * (1.0 - r)


def onepole(x, fc):
    a = np.exp(-2 * np.pi * fc / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc = (1 - a) * x[i] + a * acc
        y[i] = acc
    return y


def lowpass_fast(x, fc):
    """A one-pole low-pass done with scipy if there (fast), else by hand."""
    try:
        from scipy.signal import lfilter
        a = np.exp(-2 * np.pi * fc / SR)
        return lfilter([1 - a], [1, -a], x)
    except Exception:
        return onepole(x, fc)


def highpass_fast(x, fc):
    return x - lowpass_fast(x, fc)


def epiano(f, dur, vel):
    n = int((dur + 1.6) * SR)
    t = np.arange(n) / SR
    idx = 1.6 * np.exp(-t / 0.35) + 0.25
    mod = np.sin(2 * np.pi * f * t) * idx
    tone = np.sin(2 * np.pi * f * t + mod)
    tine = np.sin(2 * np.pi * f * 14.0 * t) * np.exp(-t / 0.03) * 0.12
    e = env(n, 0.003, 1.4 if f < 400 else 0.9, 0.12, dur)
    trem = 1.0 + 0.08 * np.sin(2 * np.pi * 4.7 * t)
    return (tone + tine) * e * trem * vel


def bass(f, dur, vel):
    n = int((dur + 0.25) * SR)
    t = np.arange(n) / SR
    tone = np.sin(2 * np.pi * f * t) + 0.35 * np.sin(2 * np.pi * 2 * f * t) * np.exp(-t / 0.15) + 0.1 * np.sin(2 * np.pi * 3 * f * t) * np.exp(-t / 0.08)
    e = env(n, 0.006, 0.7, 0.06, dur)
    thump = np.exp(-t / 0.01) * 0.3 * np.sin(2 * np.pi * 900 * t)
    return (tone * e + thump * e) * vel


def vibes(f, dur, vel):
    n = int((dur + 2.2) * SR)
    t = np.arange(n) / SR
    tone = np.sin(2 * np.pi * f * t) + 0.25 * np.sin(2 * np.pi * 4.0 * f * t) * np.exp(-t / 0.25) + 0.08 * np.sin(2 * np.pi * 10.0 * f * t) * np.exp(-t / 0.05)
    e = np.exp(-t / 1.6) * np.minimum(1.0, t / 0.002)
    damp = np.clip((t - dur - 0.05) / 0.25, 0.0, 1.0)                  # the pedal let up after the note
    e = e * (1.0 - 0.85 * damp)
    motor = 1.0 + 0.22 * np.sin(2 * np.pi * 5.2 * t)
    return tone * e * motor * vel


def pad(freqs, dur, vel):
    n = int((dur + 1.2) * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for f in freqs:
        for det in (-0.006, 0.0, 0.007):
            ph = (f * (1 + det) * t + rng.random()) % 1.0
            out += (2 * ph - 1)
    out = lowpass_fast(lowpass_fast(out, 900.0), 1500.0)
    e = np.minimum(1.0, t / 0.7) * np.clip(1.0 - (t - dur) / 1.0, 0.0, 1.0)
    return out * e * vel / (len(freqs) * 3)


def kick(vel):
    n = int(0.35 * SR)
    t = np.arange(n) / SR
    f = 45 + 80 * np.exp(-t / 0.04)
    ph = np.cumsum(2 * np.pi * f / SR)
    return np.sin(ph) * np.exp(-t / 0.18) * vel


def brush(vel, long=False):
    n = int((0.45 if long else 0.25) * SR)
    t = np.arange(n) / SR
    no = rng.standard_normal(n)
    no = highpass_fast(lowpass_fast(no, 6500.0), 900.0)
    e = np.minimum(1.0, t / (0.02 if long else 0.004)) * np.exp(-t / (0.16 if long else 0.07))
    return no * e * vel * 0.9


def shaker(vel):
    n = int(0.06 * SR)
    t = np.arange(n) / SR
    no = highpass_fast(rng.standard_normal(n), 4500.0)
    return no * np.minimum(1.0, t / 0.008) * np.exp(-t / 0.022) * vel * 0.7


# ------------------------------------------------------------------------------------------------- the score

# chords: (bass root midi, voicing midi)
CH = {
    "Dmaj9": (50, [66, 69, 73, 76]), "Bm9": (47, [62, 66, 69, 73]), "Em9": (40, [67, 71, 74, 78]), "A13": (45, [67, 71, 73, 78]),
    "F#m7": (42, [66, 69, 73, 76]), "B7b9": (47, [63, 69, 72, 75]), "A9sus": (45, [67, 71, 74, 76]), "A7": (45, [67, 73, 76, 79]),
    "Gmaj9": (43, [66, 69, 71, 74]), "Em7": (40, [67, 71, 74, 76]), "Dmaj7": (50, [66, 69, 73, 76]), "B7": (47, [63, 69, 71, 75]),
}
A_CHORDS = [["Dmaj9"], ["Bm9"], ["Em9"], ["A13"], ["F#m7"], ["B7b9"], ["Em9"], ["A9sus", "A7"]]
B_CHORDS = [["Gmaj9"], ["F#m7"], ["Em7"], ["Dmaj7"], ["Gmaj9"], ["F#m7", "B7"], ["Em9"], ["A9sus", "A7"]]

# melodies: per bar, (beat, midi, beats)
MEL_A = [
    [(0, 78, 1.5), (1.5, 76, 0.5), (2, 74, 1), (3, 69, 1)],
    [(0, 71, 1), (1, 73, 0.5), (1.5, 74, 1.5), (3, 78, 1)],
    [(0, 79, 2), (2, 78, 0.5), (2.5, 76, 0.5), (3, 74, 1)],
    [(0, 73, 1.5), (1.5, 71, 0.5), (2, 69, 2)],
    [(0, 69, 0.5), (0.5, 73, 0.5), (1, 76, 1), (2, 78, 1), (3, 76, 1)],
    [(0, 75, 1), (1, 78, 1), (2, 81, 1.5), (3.5, 79, 0.5)],
    [(0, 78, 1.5), (1.5, 76, 0.5), (2, 71, 1), (3, 74, 1)],
    [(0, 76, 2), (2, 73, 1.5)],
]
MEL_A2 = MEL_A[:4] + [
    [(0, 76, 0.5), (0.5, 78, 0.5), (1, 81, 1.5), (2.5, 78, 0.5), (3, 76, 1)],
    [(0, 75, 1.5), (1.5, 72, 0.5), (2, 75, 1), (3, 78, 1)],
    [(0, 79, 1), (1, 78, 0.5), (1.5, 76, 0.5), (2, 74, 1), (3, 71, 1)],
    [(0, 73, 1), (1, 76, 1), (2, 74, 2)],
]
MEL_B = [
    [(0, 74, 0.5), (0.5, 78, 1), (1.5, 81, 1.5), (3, 79, 1)],
    [(0, 76, 1.5), (1.5, 73, 0.5), (2, 69, 2)],
    [(0, 67, 0.5), (0.5, 71, 0.5), (1, 74, 1), (2, 76, 1.5), (3.5, 74, 0.5)],
    [(0, 73, 2), (2, 69, 1), (3, 66, 1)],
    [(0, 71, 1), (1, 74, 1), (2, 78, 1), (3, 81, 1)],
    [(0, 81, 1), (1, 78, 1), (2, 75, 1), (3, 78, 1)],
    [(0, 79, 1.5), (1.5, 78, 0.5), (2, 76, 1), (3, 71, 1)],
    [(0, 74, 1), (1, 76, 3)],
]

# the form: (section, chords, melody or None, groove level 0 intro .. 2 full, pad)
FORM = [("intro", A_CHORDS[:4], None, 0, False), ("A", A_CHORDS, MEL_A, 2, False), ("A2", A_CHORDS, MEL_A2, 2, False),
        ("B", B_CHORDS, MEL_B, 2, True), ("A", A_CHORDS, MEL_A, 2, False), ("break", A_CHORDS, None, 1, True),
        ("A2", A_CHORDS, MEL_A2, 2, False), ("outro", [["Dmaj9"], ["Dmaj9"], ["Gmaj9"], ["Dmaj9"]], None, 1, True)]


def render():
    bars = sum(len(c) for _, c, _, _, _ in FORM)
    total = LEAD_IN + bars * BAR + TAIL + 3.0
    out = np.zeros(int(total * SR) + SR)

    def put(sig, t0, gain=1.0):
        i = int(t0 * SR)
        j = min(len(out), i + len(sig))
        if j > i:
            out[i:j] += sig[:j - i] * gain

    bar_i = 0
    for (name, chords, mel, level, use_pad) in FORM:
        for b, bar_chords in enumerate(chords):
            t_bar = LEAD_IN + bar_i * BAR
            fade = 1.0
            if name == "outro":
                fade = 1.0 - 0.18 * b
            # the chord(s) of the bar: each over its share of the bar
            share = 4.0 / len(bar_chords)
            for k, cname in enumerate(bar_chords):
                root, voicing = CH[cname]
                c0 = k * share
                # the piano comps: on the beat and on the "and" of two (soft, a little spread)
                for (cb, cd, cv) in ((c0, min(1.5, share), 0.16), (c0 + 2.5, 1.0, 0.11)) if share >= 4 else ((c0, share * 0.75, 0.15),):
                    if cb >= c0 + share:
                        continue
                    for j, n in enumerate(voicing):
                        put(epiano(midi(n), cd * BEAT, cv * (0.9 + 0.2 * rng.random())), t_bar + swing(cb) * BEAT + j * 0.009, fade)
                if use_pad:
                    put(pad([midi(n) for n in voicing], share * BEAT, 0.09 if level < 2 else 0.06), t_bar + c0 * BEAT, fade)
                if level >= 1:
                    # the bass: root, fifth, octave, a walk into the next
                    nxt_root = root
                    pattern = [(0.0, root, 1.4, 0.55), (1.5, root + 7, 0.45, 0.4), (2.0, root + 12, 0.9, 0.42), (3.0, root + 7, 0.9, 0.42)]
                    if share < 4:
                        pattern = [(0.0, root, share * 0.7, 0.55), (share * 0.75, root + 7, share * 0.2, 0.38)]
                    for (bb, n, d, v) in pattern:
                        put(bass(midi(n - 12 if n > 52 else n), d * BEAT, v), t_bar + (c0 + swing(bb)) * BEAT, fade)
            # the drums
            if level >= 1:
                for beat in (0.0, 2.5):
                    put(kick(0.55 if beat == 0 else 0.35), t_bar + swing(beat) * BEAT, fade)
                if level >= 2:
                    for beat in (1.0, 3.0):
                        put(brush(0.24, long=True), t_bar + beat * BEAT, fade)
                    if rng.random() < 0.3:
                        put(brush(0.1), t_bar + swing(3.5) * BEAT, fade)
            for e in range(8):
                beat = e * 0.5
                v = (0.22 if e % 2 == 0 else 0.14) * (0.7 if level == 0 else 1.0)
                put(shaker(v * (0.85 + 0.3 * rng.random())), t_bar + swing(beat) * BEAT + rng.normal(0, 0.004), fade)
            # the tune
            if mel is not None:
                for (beat, n, d) in mel[b]:
                    put(vibes(midi(n), d * BEAT, 0.26 * (0.9 + 0.2 * rng.random())), t_bar + swing(beat) * BEAT + rng.normal(0, 0.006), fade)
            bar_i += 1
    # the last chord rings out
    t_end = LEAD_IN + bar_i * BAR
    for j, n in enumerate(CH["Dmaj9"][1]):
        put(epiano(midi(n), 2.5, 0.15), t_end + j * 0.03)
    put(vibes(midi(74), 2.5, 0.22), t_end + 0.12)
    put(bass(midi(38), 2.0, 0.5), t_end)
    # master: a little room (two short echoes), gentle saturation, level
    room = np.zeros_like(out)
    for d, g in ((0.031, 0.18), (0.057, 0.12), (0.089, 0.08), (0.13, 0.05)):
        k = int(d * SR)
        room[k:] += out[:-k] * g
    mix = out + lowpass_fast(room, 3500.0)
    mix = highpass_fast(mix, 35.0)
    mix = mix / (np.max(np.abs(mix)) or 1.0) * 1.1
    mix = np.tanh(mix * 0.9) / np.tanh(0.9)           # (gentle: only the loudest hits round off)
    end = int((t_end + 4.0 + TAIL) * SR)
    mix = mix[:end]
    lead = int(LEAD_IN * SR)
    mix[:lead] = 0.0
    peak = np.max(np.abs(mix)) or 1.0
    mix = mix / peak * 0.89
    return mix


def save(path, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else "Cassette_LunchBreakMix.wav"
    x = render()
    save(path, x)
    print("wrote", path, "%.1f s" % (len(x) / SR))
