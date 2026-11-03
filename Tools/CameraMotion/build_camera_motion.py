"""
Builds the camcorder's recorded motion from phone sensor recordings (Docs/CameraMotionCapture.md): the walk (Walk), the
side-steps (StrafeLeft, StrafeRight), standing still (Idle) and the tremor of turning (Look), one JSON each, from the takes
named walk_normal_*, strafe_left_*, strafe_right_*, idle_stand_* and look_* (below: how the walk is made; the side-steps
the same way; standing and turning are seamless loops, by seconds, see build_idle and build_look).

Reads Sensor Logger exports (zip or folder: Gyroscope.csv, Accelerometer.csv, TotalAcceleration.csv, Orientation.csv), finds
where the phone was held and walked straight (not the pick-up, the turn or the set-down), and keeps only the hand's own
motion: the turn of the view is the player's mouse, so the slow look direction is filtered out (rotation below 0.4 Hz,
movement below 0.5 Hz). What is left, the sway, the bob of each step and the tremor, is cut at the footsteps into strides
(left heel strike to the next left heel strike), resampled to a fixed number of samples per stride and written as JSON the
game plays back in step with the player's own feet (HandheldCameraShake).

    python Tools/CameraMotion/build_camera_motion.py Tools/CameraMotion/Recordings
        --out-dir Assets/Backrooms/Resources/CameraMotion [--report] [--only Walk]

Output channels per sample, in the camera's own axes as the game uses them: pitch (degrees, down positive), yaw (degrees,
right positive), roll (degrees, Unity's z), then x right, y up, z forward (metres).
"""
import argparse
import glob
import io
import json
import os
import sys
import zipfile

import numpy as np
from scipy import signal

FS = 100.0                 # resampled rate (Sensor Logger records at 100 Hz)
SAMPLES = 64               # samples per stride in the output
ROT_HP, ROT_LP = 0.4, 15.0  # Hz: rotation kept between these
POS_HP, POS_LP = 0.5, 10.0  # Hz: movement kept between these


# ---------------- Reading ----------------

def read_table(src, name):
    if os.path.isdir(src):
        text = open(os.path.join(src, name + '.csv'), encoding='utf-8').read()
    else:
        with zipfile.ZipFile(src) as z:
            member = next((m for m in z.namelist() if os.path.basename(m) == name + '.csv'), None)
            if member is None:
                raise SystemExit(f'{src}: no {name}.csv')
            text = z.read(member).decode('utf-8')
    lines = [l for l in text.splitlines() if l.strip()]
    header = lines[0].split(',')
    data = np.loadtxt(io.StringIO('\n'.join(lines[1:])), delimiter=',', ndmin=2)
    return {h: data[:, i] for i, h in enumerate(header)}


def read_metadata(src):
    try:
        t = read_table(src, 'Metadata')
        return t
    except Exception:
        return None


def load(src):
    gyro = read_table(src, 'Gyroscope')
    lin = read_table(src, 'Accelerometer')
    tot = read_table(src, 'TotalAcceleration')
    ori = read_table(src, 'Orientation')
    end = min(gyro['seconds_elapsed'][-1], lin['seconds_elapsed'][-1], tot['seconds_elapsed'][-1], ori['seconds_elapsed'][-1])
    start = max(gyro['seconds_elapsed'][0], lin['seconds_elapsed'][0], tot['seconds_elapsed'][0], ori['seconds_elapsed'][0])
    T = np.arange(start, end, 1.0 / FS)

    def vec(tab, keys):
        return np.stack([np.interp(T, tab['seconds_elapsed'], tab[k]) for k in keys], axis=1)

    q = vec(ori, ('qx', 'qy', 'qz', 'qw'))
    q /= np.linalg.norm(q, axis=1, keepdims=True)
    return T, vec(gyro, 'xyz'), vec(lin, 'xyz'), vec(tot, 'xyz'), q


def rotate(q, v):
    """Rotates device-frame vectors v by unit quaternions q (x, y, z, w): device to world."""
    u, w = q[:, :3], q[:, 3:4]
    t = 2.0 * np.cross(u, v)
    return v + w * t + np.cross(u, t)


# ---------------- Filters ----------------

def band_integrate(x, order, hp, lp):
    """Integrates x `order` times in the frequency domain, keeping hp..lp Hz (8th-power roll-offs); reflect padding."""
    n = len(x)
    pad = int(3 * FS)
    xp = np.pad(signal.detrend(x), pad, mode='reflect')
    N = len(xp)
    f = np.fft.rfftfreq(N, 1.0 / FS)
    X = np.fft.rfft(xp)
    H = np.zeros(len(f), dtype=complex)
    nz = f > 0
    H[nz] = 1.0 / (2j * np.pi * f[nz]) ** order
    H *= 1.0 / np.sqrt(1.0 + (hp / np.maximum(f, 1e-6)) ** 8)
    H *= 1.0 / np.sqrt(1.0 + (f / lp) ** 8)
    return np.fft.irfft(X * H, N)[pad:pad + n]


def smooth(x, seconds):
    k = max(1, int(seconds * FS))
    w = np.hanning(k + 2)[1:-1]
    w /= w.sum()
    return np.convolve(np.pad(x, (k, k), mode='edge'), w, mode='same')[k:-k]


def runs(mask):
    """(start, end) index pairs of the True runs of mask."""
    d = np.diff(np.concatenate([[0], mask.astype(int), [0]]))
    return list(zip(np.where(d == 1)[0], np.where(d == -1)[0]))


# ---------------- One recording ----------------

def analyse(src, report, step_rms=0.35):
    """The clean strides of straight walking in one recording. step_rms: how strong the steps' up-down rhythm must be
    (m/s^2 rms) to count as walking: side-steps are gentler than walking forward (0.3-0.6 against 0.5 and up)."""
    T, gyro, lin, tot, q = load(src)
    grav = tot - lin                                         # the reaction to gravity: points up, in the phone's axes
    gn = grav / np.maximum(np.linalg.norm(grav, axis=1, keepdims=True), 1e-6)

    # Which phone axis was up while it was held (landscape: x; portrait: y), from the middle of the recording.
    mid = slice(len(T) // 4, 3 * len(T) // 4)
    axes = {'+x': np.array([1., 0, 0]), '-x': np.array([-1., 0, 0]), '+y': np.array([0., 1, 0]), '-y': np.array([0., -1, 0])}
    up_name = max(axes, key=lambda k: (gn[mid] @ axes[k]).mean())
    U = axes[up_name]
    F = np.array([0., 0, -1])                                # the back camera looks away from the screen
    R = np.cross(F, U)                                       # right = forward x up (right-handed phone axes)

    held = (gn @ U) > 0.8

    # The world frame from the phone's orientation (checked: gravity should come out straight up).
    up_world = rotate(q, grav)
    check = np.median(up_world[held, 2] / np.linalg.norm(up_world[held], axis=1))
    if check < 0.95:
        raise SystemExit(f'{src}: the orientation does not line up with gravity ({check:.2f})')
    lin_w = rotate(q, lin)

    # Heading: where the camera pointed, levelled, smoothed (the walking direction, not the hand's sway).
    fw = rotate(q, np.repeat(F[None], len(T), 0))
    heading = np.unwrap(np.arctan2(fw[:, 0], fw[:, 1]))
    head_s = smooth(heading, 1.5)
    fwd_h = np.stack([np.sin(head_s), np.cos(head_s), np.zeros_like(head_s)], 1)
    right_h = np.stack([np.cos(head_s), -np.sin(head_s), np.zeros_like(head_s)], 1)
    acc_h = np.stack([(lin_w * right_h).sum(1), lin_w[:, 2], (lin_w * fwd_h).sum(1)], 1)     # right, up, forward

    # Turning on the spot (about the vertical), and walking (the up-down rhythm of the steps).
    yaw_rate = smooth(np.gradient(heading, 1.0 / FS), 0.4)
    turning = np.abs(yaw_rate) > 0.8
    turning = np.convolve(turning.astype(float), np.ones(int(2.0 * FS)), mode='same') > 0      # 1 s either side
    vert = signal.sosfiltfilt(signal.butter(4, [0.8, 6.0], 'bandpass', fs=FS, output='sos'), acc_h[:, 1])
    walking = np.sqrt(smooth(vert ** 2, 2.0)) > step_rms     # still on a table: 0.03; soft steps: 0.5 and up
    ok = held & walking & ~turning

    segments = []
    for a, b in runs(ok):
        a, b = a + int(1.5 * FS), b - int(1.5 * FS)          # not the first and last steps of a stretch
        if b - a >= int(6 * FS):
            segments.append((a, b))
    if report:
        print(f'{os.path.basename(src)}: {T[-1] - T[0]:.1f} s, phone up axis {up_name}, held {held.mean() * 100:.0f} %, '
              f'orientation check {check:.3f}')
        for a, b in runs(turning & held):
            turned = np.degrees(heading[min(b, len(T) - 1)] - heading[a])
            print(f'  turn {T[a]:.1f}-{T[b - 1]:.1f} s ({turned:+.0f} deg)')

    # The camera's own rotation (Unity's camera axes: pitch down +, yaw right +, roll z): the gyroscope's right-handed
    # rates about right, up and forward, negated.
    w_cam = np.stack([-(gyro @ R), -(gyro @ U), -(gyro @ F)], 1)

    out = []
    for a, b in segments:
        ext = slice(max(0, a - int(FS)), min(len(T), b + int(FS)))                  # a second either side, cut off after
        rot = np.stack([np.degrees(band_integrate(w_cam[ext, i], 1, ROT_HP, ROT_LP)) for i in range(3)], 1)
        pos = np.stack([band_integrate(acc_h[ext, i], 2, POS_HP, POS_LP) for i in range(3)], 1)
        off = a - ext.start
        rot, pos = rot[off:off + (b - a)], pos[off:off + (b - a)]
        v = signal.sosfiltfilt(signal.butter(4, 5.0, 'lowpass', fs=FS, output='sos'), acc_h[a:b, 1])

        # Steps: the up-down rhythm's period (autocorrelation), then the heel strikes (the upward push as the foot takes
        # the weight).
        ac = np.correlate(v - v.mean(), v - v.mean(), 'full')[len(v) - 1:]
        lo, hi = int(0.35 * FS), int(0.85 * FS)
        step = (lo + np.argmax(ac[lo:hi])) / FS
        peaks, _ = signal.find_peaks(v, distance=int(0.65 * step * FS), prominence=0.35 * v.std())
        gaps = np.diff(peaks) / FS
        # Which foot: the body moves over the foot that takes the weight, so the step after a right heel strike leans
        # right (the stride-rate part of the sideways movement). Feet alternate, so the evidence of every step in an
        # unbroken run of steps decides together which of them are the left ones.
        lateral = signal.sosfiltfilt(signal.butter(2, [0.5 / (2 * step), 1.5 / (2 * step)], 'bandpass', fs=FS, output='sos'),
                                     pos[:, 0])
        n_step = int(step * FS)
        evidence = np.array([lateral[p:p + n_step].mean() for p in peaks])
        side = np.zeros(len(peaks))
        start = 0
        for i in range(1, len(peaks) + 1):
            if i == len(peaks) or gaps[i - 1] > 1.5 * step:                          # a missed step breaks the run
                idx = np.arange(start, i)
                parity = np.where((idx - start) % 2 == 0, 1.0, -1.0)
                sign = 1.0 if (evidence[idx] * parity).sum() >= 0 else -1.0
                side[idx] = parity * sign
                start = i
        agree = np.mean(np.sign(evidence) == side) if len(side) else 0
        strides = []
        for i in range(len(peaks) - 2):
            p0, pm, p1 = peaks[i], peaks[i + 1], peaks[i + 2]
            if side[i] > 0 or side[i + 1] < 0 or side[i + 2] > 0:
                continue
            dur = (p1 - p0) / FS
            if not (1.6 * step < dur < 2.4 * step):
                continue
            phase = np.arange(SAMPLES) / SAMPLES
            src_i = p0 + phase * (p1 - p0)
            chans = np.concatenate([rot, pos], 1)
            sample = np.stack([np.interp(src_i, np.arange(len(chans)), chans[:, c]) for c in range(6)], 1)
            strides.append({'data': sample, 'end': chans[min(p1, len(chans) - 1)], 'seconds': dur,
                            'right': (pm - p0) / (p1 - p0), 'from': p0, 'to': p1})
        # Strides back to back in the recording follow on from each other; elsewhere (a skipped stride, the end of a
        # stretch) the loop eases across.
        for k, s in enumerate(strides):
            s['next'] = k + 1 < len(strides) and strides[k + 1]['from'] == s['to']
        if report:
            print(f'  straight walk {T[a]:.1f}-{T[b]:.1f} s: step {step:.3f} s ({60 / step:.0f} steps/min), '
                  f'{len(peaks)} steps, sway agrees with the feet {agree * 100:.0f} %, {len(strides)} strides kept')
            print(f'    rotation rms pitch {rot[:, 0].std():.2f} yaw {rot[:, 1].std():.2f} roll {rot[:, 2].std():.2f} deg, '
                  f'movement rms x {pos[:, 0].std() * 100:.2f} y {pos[:, 1].std() * 100:.2f} z {pos[:, 2].std() * 100:.2f} cm, '
                  f'up-down peak to peak {np.ptp(pos[:, 1]) * 100:.1f} cm')
        out.append(strides)
    return out


# ---------------- Building the loop ----------------

def build(paths, report, step_rms=0.35):
    seq = []
    for p in paths:
        for strides in analyse(p, report, step_rms):
            seq.extend(strides)
    if len(seq) < 4:
        raise SystemExit('Not enough clean strides of straight walking found.')
    # Where a stride is not followed by its own next stride (a stretch ends, or the loop wraps), its end is eased onto the
    # next stride's start over its second half, so the loop has no jumps.
    data = np.stack([s['data'] for s in seq])                                   # strides, samples, 6
    phase = np.arange(SAMPLES) / SAMPLES
    ease = np.clip((phase - 0.5) / 0.5, 0, 1)
    ease = ease * ease * (3 - 2 * ease)
    for k, s in enumerate(seq):
        nxt = data[(k + 1) % len(seq), 0]
        follows = s['next'] and k + 1 < len(seq)
        if not follows:
            data[k] += (nxt - s['end'])[None, :] * ease[:, None]
    seconds = np.array([s['seconds'] for s in seq])
    right = float(np.median([s['right'] for s in seq]))
    if report:
        print(f'loop: {len(seq)} strides, {seconds.sum():.1f} s, stride {seconds.mean():.3f} s '
              f'({120 / seconds.mean():.0f} steps/min), right foot at {right:.2f} of the stride')
    return {
        'channels': 'pitch,yaw,roll (deg); x,y,z (m)',
        'samplesPerStride': SAMPLES,
        'strides': len(seq),
        'strideSeconds': float(seconds.mean()),
        'rightFootPhase': right,
        'data': [round(float(v), 6) for v in data.reshape(-1)],
    }


# ---------------- Standing and turning (continuous loops) ----------------

def camera_rates(src):
    """One recording on a common clock: times, the gyroscope's rates about the camera's right, up and forward (rad/s,
    right-handed), the phone's up axis in the hand, the unit gravity reaction (phone axes), the orientation, the linear
    acceleration and the camera's axes in the phone (right, up, forward)."""
    T, gyro, lin, tot, q = load(src)
    grav = tot - lin
    gn = grav / np.maximum(np.linalg.norm(grav, axis=1, keepdims=True), 1e-6)
    mid = slice(len(T) // 4, 3 * len(T) // 4)
    axes = {'+x': np.array([1., 0, 0]), '-x': np.array([-1., 0, 0]), '+y': np.array([0., 1, 0]), '-y': np.array([0., -1, 0])}
    U = axes[max(axes, key=lambda k: (gn[mid] @ axes[k]).mean())]
    F = np.array([0., 0, -1])
    R = np.cross(F, U)
    return T, np.stack([gyro @ R, gyro @ U, gyro @ F], 1), U, gn, q, lin, (R, U, F)


def held_span(gn, U, rates, lo):
    """The longest stretch the phone was in the hand (its up axis near up, and not lying still on the table), less the
    pick-up and the set-down."""
    held = (gn @ U) > lo
    held &= np.sqrt(smooth((rates ** 2).sum(1), 0.5)) > 0.003                # on the table: about 0.001 rad/s
    a, b = max(runs(held), key=lambda r: r[1] - r[0])
    return a + int(2.5 * FS), b - int(2.5 * FS)


def loop_crossfade(x, seconds):
    """A seamless loop of x (samples, channels): its last `seconds` blended into its start, equal power (it is noise)."""
    n = max(1, int(seconds * FS))
    t = np.linspace(0, np.pi / 2, n)[:, None]
    y = x[:-n].copy()
    y[:n] = x[:n] * np.sin(t) + x[-n:] * np.cos(t)
    return y - y.mean(0)


def as_clip(x, kind, extra=None):
    """A continuous loop in the strides format, one 'stride' a second (CameraMotionClip plays it by seconds)."""
    per = int(FS)
    n = (len(x) // per) * per
    if n < len(x):                       # whole seconds: the cut-off tail is blended over the start so the loop stays seamless
        k = len(x) - n
        w = np.linspace(0, 1, k)[:, None]
        x = x.copy()
        x[:k] = x[:k] * w + x[n:] * (1 - w)
        x = x[:n]
    clip = {
        'channels': 'pitch,yaw,roll (deg); x,y,z (m)', 'kind': kind,
        'samplesPerStride': per, 'strides': n // per, 'strideSeconds': 1.0, 'rightFootPhase': 0.5,
        'data': [round(float(v), 5) for v in x.reshape(-1)],
    }
    clip.update(extra or {})
    return clip


def build_idle(paths, report):
    """Standing still with the camera up: the hand's slow wander, its corrections and its tremor, as one seamless loop.
    Rotation kept from 0.05 Hz (a still hand's drift; the look direction is not moving), movement from 0.25 Hz (below
    that the accelerometer's double integral is mostly its own noise)."""
    parts = []
    for p in paths:
        T, w, U, gn, q, lin, (R, _, F) = camera_rates(p)
        a, b = held_span(gn, U, w, 0.8)
        # (Filtered within the held stretch alone: the pick-up's big swing would ring through a 0.05 Hz filter.)
        ext, off = slice(a, b), 0
        rot = np.stack([np.degrees(band_integrate(-w[ext, i], 1, 0.05, ROT_LP)) for i in range(3)], 1)[off:off + b - a]
        # Movement in the camera's level frame (right, up, forward, from its heading).
        lin_w = rotate(q, lin)
        fw = rotate(q, np.repeat(F[None], len(T), 0))
        head = smooth(np.unwrap(np.arctan2(fw[:, 0], fw[:, 1])), 1.5)
        right_h = np.stack([np.cos(head), -np.sin(head), np.zeros_like(head)], 1)
        fwd_h = np.stack([np.sin(head), np.cos(head), np.zeros_like(head)], 1)
        acc = np.stack([(lin_w * right_h).sum(1), lin_w[:, 2], (lin_w * fwd_h).sum(1)], 1)
        pos = np.stack([band_integrate(acc[ext, i], 2, 0.25, POS_LP) for i in range(3)], 1)[off:off + b - a]
        if report:
            print(f'{os.path.basename(p)}: standing {T[a] - T[0]:.1f}-{T[b] - T[0]:.1f} s; rotation rms pitch {rot[:, 0].std():.2f} '
                  f'yaw {rot[:, 1].std():.2f} roll {rot[:, 2].std():.2f} deg, movement rms x {pos[:, 0].std() * 100:.2f} '
                  f'y {pos[:, 1].std() * 100:.2f} z {pos[:, 2].std() * 100:.2f} cm')
        parts.append(np.concatenate([rot, pos], 1))
    # Takes one after another, each joined by a short blend, the whole made a loop.
    x = parts[0]
    for y in parts[1:]:
        n = int(1.0 * FS)
        t = np.linspace(0, np.pi / 2, n)[:, None]
        x = np.concatenate([x[:-n], x[-n:] * np.cos(t) + y[:n] * np.sin(t), y[n:]])
    x = loop_crossfade(x, 2.0)
    if report:
        print(f'idle loop: {len(x) / FS:.1f} s')
    return as_clip(x, 'idle')


TREMOR_BAND = (3.0, ROT_LP)          # Hz: the hand's tremor and jolts, apart from the turn itself


def build_look(paths, report):
    """
    Turning the camera by hand. The mouse does the turn itself, so what is kept is what comes with it:
     - the tremor (3-15 Hz): how strong it is at each turn speed (turnSpeeds, deg/s; turnTremor, rms degrees of the three
       axes together), and its texture: the tremor over its own envelope (one degree rms), played on top at the strength
       the turn speed gives;
    Measured but not kept: the roll that goes with a turn (reported only). In the first takes (2026-11-03) it grew the
    longer after the turn it was looked for, so it follows where the camera has turned to rather than the turn, and the
    fast take's whips were tipped on purpose; the game keeps its own small lean into a turn. The overshoot when a turn
    stops cannot be told apart from where the hand meant to stop, so it is not measured either.
    """
    lp = lambda x, fc: signal.sosfiltfilt(signal.butter(2, fc, 'lowpass', fs=FS, output='sos'), x, axis=0)
    textures, speeds, envs, yrs, rolls = [], [], [], [], []
    for p in paths:
        T, w, U, gn, q, lin, _ = camera_rates(p)
        a, b = held_span(gn, U, w, 0.5)
        ext, off = slice(a, b), 0
        cam = -w[ext]                                       # Unity's camera axes: pitch down, yaw right, roll (right side up)
        trem = np.stack([np.degrees(band_integrate(cam[:, i], 1, *TREMOR_BAND)) for i in range(3)], 1)[off:off + b - a]
        yaw_rate = np.degrees(lp(cam[:, 1], 2.0))[off:off + b - a]
        roll = np.degrees(band_integrate(cam[:, 2], 1, 0.1, 5.0))[off:off + b - a]
        env = np.sqrt(np.maximum(lp((trem ** 2).sum(1), 1.0), 1e-6))
        # The texture: the tremor over its envelope, which is floored (where it is near nothing, at the edges of the
        # filter, the quotient blew up to hundreds of degrees) and cut a second in from each end.
        cut = slice(int(FS), len(env) - int(FS))
        tex = trem[cut] / np.maximum(env[cut], 0.5 * np.median(env))[:, None]
        tex *= 1.0 / np.sqrt(((tex ** 2).sum(1)).mean())                     # one degree rms
        textures.append(loop_crossfade(tex, 1.0))
        speeds.append(np.abs(yaw_rate)); envs.append(env); yrs.append(yaw_rate); rolls.append(roll)
        if report:
            print(f'{os.path.basename(p)}: in the hand {T[a] - T[0]:.1f}-{T[b] - T[0]:.1f} s, turn speed median '
                  f'{np.median(np.abs(yaw_rate)):.0f} deg/s, 90th percentile {np.percentile(np.abs(yaw_rate), 90):.0f}')
    sp, ev = np.concatenate(speeds), np.concatenate(envs)
    edges = [0, 10, 30, 60, 120, 240, 600]
    pts_s, pts_t = [], []
    for lo, hi in zip(edges[:-1], edges[1:]):
        m = (sp >= lo) & (sp < hi)
        if m.sum() > 100:
            pts_s.append(round(float(np.median(sp[m])), 1)); pts_t.append(float(np.sqrt((ev[m] ** 2).mean())))
    pts_t = [round(float(v), 4) for v in np.maximum.accumulate(pts_t)]      # never less with a faster turn
    # The roll (reported): the lag that lines it up best with the turn, and the gain there (all the takes together).
    best = None
    for lag in range(0, int(0.4 * FS) + 1, 5):
        xs = np.concatenate([y[:len(y) - lag] for y in yrs]); ys = np.concatenate([r[lag:] for r in rolls])
        c = np.corrcoef(xs, ys)[0, 1]
        if best is None or c > best[0]:
            best = (c, lag, float(np.polyfit(xs, ys, 1)[0]))
    corr, lag, gain = best
    if report:
        for s_, t_ in zip(pts_s, pts_t):
            print(f'  turning {s_:5.0f} deg/s: tremor {t_:.2f} deg rms')
        print(f'  roll (not kept): {gain * 100:+.2f} deg per 100 deg/s of turn, {lag / FS:.2f} s behind it '
              f'(correlation {corr:+.2f})')
    x = np.concatenate(textures)
    x = np.concatenate([x, np.zeros_like(x)], 1)                            # (no movement channels)
    return as_clip(x, 'look', {'turnSpeeds': pts_s, 'turnTremor': pts_t})


# What each kind of take becomes: (file name prefix, resource name, builder).
KINDS = [
    ('walk_normal', 'Walk', lambda ps, r: build(ps, r)),
    ('strafe_left', 'StrafeLeft', lambda ps, r: build(ps, r, step_rms=0.2)),
    ('strafe_right', 'StrafeRight', lambda ps, r: build(ps, r, step_rms=0.2)),
    ('idle_stand', 'Idle', build_idle),
    ('look_', 'Look', build_look),
]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('recordings', nargs='?', default='Tools/CameraMotion/Recordings', help='the folder of takes')
    ap.add_argument('--out-dir', default=None, help='where the clips go (Assets/Backrooms/Resources/CameraMotion)')
    ap.add_argument('--only', default=None, help='build only this one (Walk, StrafeLeft, StrafeRight, Idle, Look)')
    ap.add_argument('--report', action='store_true')
    a = ap.parse_args()
    files = sorted(glob.glob(os.path.join(a.recordings, '*.zip')) +
                   [d for d in glob.glob(os.path.join(a.recordings, '*')) if os.path.isdir(d)])
    for prefix, name, builder in KINDS:
        if a.only and a.only != name:
            continue
        paths = [p for p in files if os.path.basename(p).startswith(prefix)]
        if not paths:
            continue
        if a.report:
            print(f'---- {name} ({len(paths)} takes)')
        clip = builder(paths, a.report)
        clip['sources'] = [os.path.basename(p) for p in paths]
        if a.out_dir:
            os.makedirs(a.out_dir, exist_ok=True)
            out = os.path.join(a.out_dir, name + '.json')
            with open(out, 'w', encoding='utf-8', newline='\n') as f:
                json.dump(clip, f, separators=(',', ':'))
            print(f'wrote {out} ({os.path.getsize(out) // 1024} KB)')


if __name__ == '__main__':
    main()
