// Overflow orientation video: every frame drawn in code (flat 1970s corporate film, isometric rooms), 4:3.
// renderFrame(n) draws frame n at 24 fps. Characters move "on twos" (12 poses a second); camera moves and fast
// actions run on ones. Scene lengths live in SCENES: retime them to the voiceover once it exists.
'use strict';

const FPS = 24;
const VW = 720, VH = 540;   // the picture, 4:3
const C = {
  ink: '#2E1F17', cream: '#F3E7CC', gold: '#E0A12E', orange: '#CF5F26', avo: '#7D8A3C', teal: '#2E6B66',
  ctop: '#DDB06A', cfront: '#C4914F', cside: '#9C6E36', tape: '#EADCB8', lite: '#FFF8DA',
  hatDark: '#B07A1E', hatShade: '#C98A20', hatLight: '#F2C866', choc: '#4A2E1F',
  // the backrooms
  // Level 0 as the game draws it in the Overflow look (2026-11-07, Docs/StyleSheet.md): mustard wallpaper with lighter
  // stripes and chevrons, a darker mustard carpet, wooden doors
  carpet: '#A88C4C', carpet2: '#9F8345', ywall: '#DCC470', yleft: '#CDB35E', yside: '#B39A4B',
  ystripe: '#E8D488', ystripeL: '#DCC676', ychev: '#EEDD98', ychevL: '#E2CE82',
};
const P = new URLSearchParams(location.search);
const CAPTIONS = P.get('captions') === '1';
const BG = `<rect width="${VW}" height="${VH}" fill="${C.cream}"/>`;

// ---------- maths ----------
const clamp = (x, a = 0, b = 1) => Math.max(a, Math.min(b, x));
const lerp = (a, b, t) => a + (b - a) * t;
const E = {
  lin: t => t,
  inOut: t => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2),
  out: t => 1 - Math.pow(1 - t, 3),
  in: t => t * t * t,
  back: t => { const c1 = 1.9, c3 = c1 + 1; return 1 + c3 * Math.pow(t - 1, 3) + c1 * Math.pow(t - 1, 2); },
  bounce: t => {
    const n = 7.5625, d = 2.75;
    if (t < 1 / d) return n * t * t;
    if (t < 2 / d) return n * (t -= 1.5 / d) * t + 0.75;
    if (t < 2.5 / d) return n * (t -= 2.25 / d) * t + 0.9375;
    return n * (t -= 2.625 / d) * t + 0.984375;
  },
};
const k = (t, a, b, e = E.inOut) => e(clamp((t - a) / (b - a)));
const two = t => Math.floor(t * 12) / 12;               // on twos
const rnd = s => { const x = Math.sin(s * 12.9898 + 78.233) * 43758.5453; return x - Math.floor(x); };
const f = n => (Math.round(n * 10) / 10).toString();
const hop = (t, period) => Math.abs(Math.sin((t / period) * Math.PI));   // 0..1..0 per hop
let NOW = 0;   // the tape time of the frame being drawn, for idle motion
// a puff of dust where something lands (q 0..1 over the puff's life)
const dust = (x, y, q, sc = 1) => q <= 0 || q >= 1 ? '' : [-1, 1].map(d => `<circle cx="${f(x + d * (12 + q * 26) * sc)}" cy="${f(y - q * 6 * sc)}" r="${f((4 + q * 9) * sc)}" fill="#E9DCC0" opacity="${f(0.85 * (1 - q))}"/>`).join('')
  + `<circle cx="${f(x)}" cy="${f(y - (4 + q * 10) * sc)}" r="${f((3 + q * 6) * sc)}" fill="#E9DCC0" opacity="${f(0.6 * (1 - q))}"/>`;

// ---------- isometric ----------
class Iso {
  constructor(ox, oy, s = 1) { this.ox = ox; this.oy = oy; this.s = s; }
  p(x, y, z) { return [this.ox + (x - y) * this.s, this.oy + ((x + y) / 2 - z) * this.s]; }
  poly(pts, fill, extra = '') { return `<polygon points="${pts.map(q => this.p(...q).map(f).join(',')).join(' ')}" fill="${fill}"${extra}/>`; }
  box(x, y, z, w, d, h, top, left, right, extra = '') {
    return this.poly([[x, y + d, z], [x + w, y + d, z], [x + w, y + d, z + h], [x, y + d, z + h]], left, extra)
      + this.poly([[x + w, y, z], [x + w, y + d, z], [x + w, y + d, z + h], [x + w, y, z + h]], right, extra)
      + this.poly([[x, y, z + h], [x + w, y, z + h], [x + w, y + d, z + h], [x, y + d, z + h]], top, extra);
  }
  carton(x, y, z, w, d, h, extra = '') {
    return (z < 6 ? this.poly([[x + 2, y + 2, 0.05], [x + w + 5, y + 2, 0.05], [x + w + 5, y + d + 5, 0.05], [x + 2, y + d + 5, 0.05]], 'rgba(46,31,23,0.16)') : '') + this.box(x, y, z, w, d, h, C.ctop, C.cfront, C.cside, extra)
      + this.poly([[x, y + d / 2 - 0.08 * d, z + h], [x + w, y + d / 2 - 0.08 * d, z + h], [x + w, y + d / 2 + 0.08 * d, z + h], [x, y + d / 2 + 0.08 * d, z + h]], C.tape, extra);
  }
  yplane(x, y, z) { const [tx, ty] = this.p(x, y, z); const s = this.s; return `<g transform="matrix(${s} ${s * 0.5} 0 ${s} ${f(tx)} ${f(ty)})">`; }
  xplane(x, y, z) { const [tx, ty] = this.p(x, y, z); const s = this.s; return `<g transform="matrix(${s} ${-s * 0.5} 0 ${s} ${f(tx)} ${f(ty)})">`; }
}
// a camera that puts world point (wx, wy, wz) at screen point (sx, sy), at scale s
const isoAt = (wx, wy, wz, sx, sy, s) => { const [px, py] = new Iso(0, 0, s).p(wx, wy, wz); return new Iso(sx - px, sy - py, s); };
// the usual room shot: the room centred, a little smaller than life so the 4:3 frame has a margin
const roomCam = (W, D, s = 0.8, sy = 292) => isoAt(W / 2, D / 2, 0, VW / 2, sy, s);

const stroke = (pts, w, color = C.ink, extra = '') =>
  `<path d="M${pts.map(q => q.map(f).join(' ')).join(' L')}" fill="none" stroke="${color}" stroke-width="${f(w)}" stroke-linecap="round" stroke-linejoin="round"${extra}/>`;
const text = (x, y, s, size, fill, o = {}) =>
  `<text x="${f(x)}" y="${f(y)}" fill="${fill}" font-family="${o.mono ? 'VT323, monospace' : 'Jost, sans-serif'}" font-weight="${o.weight || 900}" font-style="${o.italic === false ? 'normal' : 'italic'}" font-size="${size}" letter-spacing="${o.ls ?? 3}" text-anchor="${o.anchor || 'middle'}"${o.extra || ''}>${s}</text>`;
const STAR = '0,-14 3.5,-4.9 13.3,-4.3 5.7,1.9 8.2,11.3 0,6 -8.2,11.3 -5.7,1.9 -13.3,-4.3 -3.5,-4.9';
const star = (x, y, sc, fill = C.gold) => `<g transform="translate(${f(x)} ${f(y)}) scale(${sc.toFixed(2)})"><polygon points="${STAR}" fill="${fill}" transform="scale(1.25)"/><polygon points="${STAR}" fill="#F7D27A" transform="scale(0.55) translate(-2 -2)"/></g>`;
const stripes = (x, y, w, hh = 10, gap = 4) =>
  `<rect x="${f(x)}" y="${f(y)}" width="${f(w)}" height="${hh}" fill="${C.orange}"/><rect x="${f(x)}" y="${f(y + hh + gap)}" width="${f(w)}" height="${hh}" fill="${C.gold}"/><rect x="${f(x)}" y="${f(y + 2 * (hh + gap))}" width="${f(w)}" height="${hh}" fill="${C.avo}"/>`;

// ---------- the crew: flat 1970s sign figures ----------
// o: aL, aR raw arm rotations (deg, clockwise), lL, lR legs, lean, bob, mirror, hat, item ('clip', 'envelope')
function picto(px, py, s, o = {}) {
  const sx = o.mirror ? -s : s;
  const hat = o.hat || C.gold;
  // standing still is never quite still: a slow breath and a little sway, on twos, different for each figure
  const still = o.bob === undefined && o.lL === undefined && o.idle !== false;
  const lean = (o.lean || 0) + (still && o.lean === undefined ? Math.sin(two(NOW) * 0.9 + px * 0.05) * 1.6 : 0);
  const bob = (o.bob || 0) + (still ? -1.8 * Math.abs(Math.sin(two(NOW) * 1.9 + px * 0.037)) : 0), hx = o.headX || 0;
  let item = '';
  if (o.item === 'clip') item = `<g transform="rotate(${o.aL || 0} -34 -134)"><rect x="-50" y="-80" width="36" height="46" rx="3" fill="${C.cfront}"/><rect x="-45" y="-73" width="26" height="34" fill="${C.lite}"/></g>`;
  if (o.item === 'envelope') item = `<g transform="rotate(${-(o.aR ? -o.aR : 0)} 34 -134)"><rect x="18" y="-80" width="40" height="26" fill="${C.lite}"/><path d="M18 -80 L38 -66 L58 -80" fill="none" stroke="${C.cside}" stroke-width="3"/></g>`;
  return `<g transform="translate(${f(px)} ${f(py)}) scale(${sx} ${s})">`
    + (o.shadow === false ? '' : `<ellipse cx="0" cy="0" rx="34" ry="9" fill="rgba(46,31,23,0.18)"/>`)
    + `<g fill="${o.ink || C.ink}">`
    + `<rect x="-23" y="-70" width="20" height="70" rx="4" transform="rotate(${o.lL || 0} -13 -66)"/>`
    + `<rect x="3" y="-70" width="20" height="70" rx="4" transform="rotate(${o.lR || 0} 13 -66)"/>`
    + `<g transform="translate(0 ${f(bob)}) rotate(${lean} 0 -66)">`
    + `<rect x="-25" y="-140" width="50" height="78" rx="12"/>`
    + `<circle cx="${hx}" cy="-168" r="21"/>`
    + `<rect x="-41" y="-136" width="14" height="66" rx="7" transform="rotate(${o.aL || 0} -34 -134)"/>`
    + `<rect x="27" y="-136" width="14" height="66" rx="7" transform="rotate(${o.aR || 0} 34 -134)"/>`
    + (o.noHat ? '' : `<rect x="${-31 + hx}" y="-177" width="62" height="7" rx="3.5" fill="${C.hatDark}"/>`
    + `<path d="M${-23 + hx} -174 A23 23 0 0 1 ${23 + hx} -174 Z" fill="${hat}"/>`
    + `<rect x="${-4 + hx}" y="-196" width="8" height="22" rx="4" fill="${C.hatLight}"/>`)
    + (o.sticker ? `<g transform="translate(0 -104) rotate(-10) scale(${f(o.sticker)})"><circle r="21" fill="${C.orange}" stroke="${C.cream}" stroke-width="4"/>${text(0, 2.5, 'RETURNED', 7, C.cream, { ls: 0.2 })}</g>` : '')
    + item + `</g></g></g>`;
}
const walkPose = (ph, amp = 26) => ({ lL: amp * Math.sin(ph), lR: -amp * Math.sin(ph), aL: -amp * 0.9 * Math.sin(ph), aR: -amp * 0.9 * Math.sin(ph) * -1, bob: -Math.abs(Math.cos(ph)) * 4, lean: 0 });
const runPose = ph => ({ lL: 48 * Math.sin(ph), lR: -48 * Math.sin(ph), aL: 70 * Math.sin(ph) + 30, aR: -70 * Math.sin(ph) + 30, bob: -Math.abs(Math.cos(ph)) * 8, lean: -16, headX: -8 });

// ---------- the hard hat ----------
function hardhat(cx, cy, S, o = {}) {
  const hat = o.color || C.gold;
  const rd = 0.3 * S, ryT = 0.27 * S, brx = 0.44 * S, bry = 0.22 * S, fx = -0.07 * S, fy = 0.035 * S;
  const brim = `<ellipse cx="${f(fx)}" cy="${f(fy)}" rx="${f(brx)}" ry="${f(bry)}" fill="${C.hatDark}"/>`;
  if (o.flip) return `<g transform="translate(${f(cx)} ${f(cy)}) rotate(${o.rot || 0})"><path d="M${f(-rd)} 0 A${f(rd)} ${f(ryT)} 0 0 0 ${f(rd)} 0 Z" fill="${hat}"/>${brim}<ellipse cx="0" cy="0" rx="${f(rd * 0.85)}" ry="${f(rd * 0.42)}" fill="#5A4334"/></g>`;
  return `<g transform="translate(${f(cx)} ${f(cy)}) rotate(${o.rot || 0})">${brim}`
    + `<path d="M${f(-rd)} 0 A${f(rd)} ${f(ryT)} 0 0 1 ${f(rd)} 0 A${f(rd)} ${f(rd / 2)} 0 0 1 ${f(-rd)} 0 Z" fill="${hat}"/>`
    + `<path d="M0 ${f(-ryT)} A${f(rd)} ${f(ryT)} 0 0 1 ${f(rd)} 0 A${f(rd)} ${f(rd / 2)} 0 0 1 0 ${f(rd / 2)} Q${f(rd * 0.4)} ${f(-ryT * 0.35)} 0 ${f(-ryT)} Z" fill="${C.hatShade}" opacity="0.55"/>`
    + `<path d="M${f(0.05 * S)} ${f(-ryT)} Q${f(-rd * 0.55)} ${f(-ryT * 0.8)} ${f(-rd * 0.72)} ${f(rd * 0.32)}" fill="none" stroke="${C.hatLight}" stroke-width="${f(0.07 * S)}" stroke-linecap="round"/>`
    + `<ellipse cx="${f(-rd * 0.35)}" cy="${f(-ryT * 0.55)}" rx="${f(0.05 * S)}" ry="${f(0.03 * S)}" fill="#FFF1C4"/></g>`;
}

// ---------- Boxley: a real box, arms and legs joined to him ----------
// o: z (lift), squash (1 = normal, <1 flatter), armL/armR (deg from hanging: 0 down, 180 up), eyes ('open','closed','bars','x','wide'),
//    look (-1..1), mouth ('smile','flat','wavy','o','grin'), hat ('on','tilt','off'), sticker (0..1 scale), stars (phase or null),
//    seatZ, stride (walk phase or null), legs (0..1 length), shadow,
//    face: which way he faces: 'y' (down-left, the default), 'x' (down-right), '-x' (up-left, back to us), '-y' (up-right, back to us)
function boxley(I, bx, by, S, o = {}) {
  const sq = o.squash ?? (o.stride == null && !o.z ? 1 + 0.014 * Math.sin(two(NOW) * 2.6) : 1);   // breathing
  const w = S * (1 + (1 - sq) * 0.45), d = w, h = S * sq;
  const legLen = 0.42 * S * (o.legs ?? 1);
  const z = o.z || 0;
  const bz = (o.seatZ ?? legLen) + z;
  const lw = 0.17 * S, L = 0.5 * S;
  const face = o.face || 'y';
  // his own frame: u across him (0 = the side we cannot see, w = the side we can), v back to front (d = his front)
  const Wd = (u, v) => face === 'y' ? [bx + u, by + v] : face === 'x' ? [bx + v, by + u] : face === '-x' ? [bx + d - v, by + u] : [bx + u, by + d - v];
  const Pt = (u, v, zz) => I.p(...Wd(u, v), zz);
  const rect = (u0, v0, du, dv) => { const a = Wd(u0, v0), b = Wd(u0 + du, v0 + dv); return [Math.min(a[0], b[0]), Math.min(a[1], b[1]), Math.abs(b[0] - a[0]), Math.abs(b[1] - a[1])]; };
  const out = [];
  if (o.shadow !== false && o.seatZ === undefined) {
    const [cx, cy] = I.p(bx + w / 2, by + d / 2, 0);
    const sh = 1 / (1 + z / 60);
    out.push(`<ellipse cx="${f(cx)}" cy="${f(cy + 4 * I.s)}" rx="${f(w * 0.95 * sh * I.s)}" ry="${f(w * 0.45 * sh * I.s)}" fill="rgba(46,31,23,0.18)"/>`);
  }
  // legs
  if (o.seatZ === undefined) {
    [0.2, 0.62].forEach((fx, i) => {
      const st = o.stride == null ? 0 : Math.sin(o.stride + i * Math.PI) * 0.12 * S;
      const lift = o.stride == null ? 0 : Math.max(0, Math.cos(o.stride + i * Math.PI)) * 0.08 * S;
      const u = fx * w, v = d - 0.26 * S + st;
      const [lx, ly, lwd, ldd] = rect(u, v, 0.16 * S, 0.16 * S);
      out.push(I.box(lx, ly, bz - legLen + lift, lwd, ldd, legLen - lift, '#4A3427', C.ink, '#3B281D'));
      const [fx2, fy2, fw2, fd2] = rect(u - 0.02 * S, v + 0.02 * S, 0.2 * S, 0.3 * S);
      out.push(I.box(fx2, fy2, bz - legLen + lift, fw2, fd2, 0.07 * S, '#4A3427', C.ink, '#3B281D'));
    });
  } else if (o.seatLegs !== false) {
    [0.22, 0.64].forEach(fx => {
      const u0 = fx * w + 0.08 * S;
      out.push(stroke([Pt(u0, d - 0.05 * S, bz + 0.02 * S), Pt(u0, d + 0.18 * S, bz - 0.02 * S), Pt(u0, d + 0.22 * S, bz - 0.5 * S)], lw * I.s));
    });
  }
  const arm = (left, a) => {
    a = a * Math.PI / 180;
    const bend = 12 * Math.PI / 180;
    const u0 = left ? 0 : w, sg = left ? -1 : 1, v0 = left ? d * 0.75 : d * 0.5, z0 = bz + h * 0.55;
    return stroke([Pt(u0, v0, z0), Pt(u0 + sg * L * 0.55 * Math.sin(a - bend), v0, z0 - L * 0.55 * Math.cos(a - bend)), Pt(u0 + sg * L * Math.sin(a), v0, z0 - L * Math.cos(a))], lw * I.s);
  };
  out.push(arm(true, o.armL ?? 20));
  out.push(I.box(bx, by, bz, w, d, h, C.ctop, C.cfront, C.cside));
  const tw = 0.07 * S, top = bz + h;
  out.push(`<polygon points="${[Pt(0, d / 2 - tw, top), Pt(w, d / 2 - tw, top), Pt(w, d / 2 + tw, top), Pt(0, d / 2 + tw, top)].map(q => q.map(f).join(',')).join(' ')}" fill="${C.tape}"/>`);
  out.push(`<polygon points="${[Pt(w, d / 2 - tw, top), Pt(w, d / 2 + tw, top), Pt(w, d / 2 + tw, bz + h * 0.72), Pt(w, d / 2 - tw, bz + h * 0.72)].map(q => q.map(f).join(',')).join(' ')}" fill="${C.tape}"/>`);
  // the face, on whichever front we can see
  if (face === 'y' || face === 'x') out.push((face === 'y' ? I.yplane(bx, by + d, bz + h) : I.xplane(bx + d, by + d, bz + h)) + boxFace(w, h, S, o) + '</g>');
  out.push(arm(false, o.armR ?? 20));
  // hat
  const [hx, hy] = I.p(bx + w / 2, by + d / 2, bz + h);
  if ((o.hat || 'on') !== 'off') out.push(hardhat(hx + (o.hat === 'tilt' ? 0.1 * S * I.s : 0), hy + 0.02 * S * I.s + (o.hatHop || 0), S * I.s, { rot: o.hat === 'tilt' ? -14 : (o.hatRot || 0) }));
  if (o.stars != null) {
    const oy = hy - 0.42 * S * I.s, rx = 0.62 * S * I.s, ry = 0.24 * S * I.s;
    out.push(`<ellipse cx="${f(hx)}" cy="${f(oy)}" rx="${f(rx)}" ry="${f(ry)}" fill="none" stroke="${C.gold}" stroke-width="${f(Math.max(2, 0.035 * S * I.s))}" opacity="0.55"/>`);
    [[200, 0.75], [290, 1.0], [20, 0.8], [110, 1.25]].forEach(([ang, big]) => {
      const a = (ang + o.stars * 360) * Math.PI / 180;
      out.push(star(hx + rx * Math.cos(a), oy + ry * Math.sin(a), Math.max(0.0125 * S * I.s, 0.95) * big * (0.85 + 0.25 * Math.sin(a))));
    });
  }
  return out.join('');
}

// Boxley's face in its own flat coordinates: (0,0) top left of the front, w wide, h high
function boxFace(w, h, S, o = {}) {
  const out = [];
  const lk = (o.look || 0) * 0.07 * w;
  const ew = 0.06 * S, eh = 0.075 * S, sw = 0.05 * S;
  const ex = [0.32 * w + lk, 0.68 * w + lk], ey = 0.42 * h;
  let eyes = o.eyes || 'open';
  if (eyes === 'open' && !o.noBlink && (two(NOW) * 0.31 + S * 0.013) % 1 < 0.035) eyes = 'closed';   // a blink every few seconds
  ex.forEach(x => {
    if (eyes === 'open') out.push(`<ellipse cx="${f(x)}" cy="${f(ey)}" rx="${f(ew)}" ry="${f(eh)}" fill="${C.ink}"/>`);
    if (eyes === 'wide') out.push(`<ellipse cx="${f(x)}" cy="${f(ey)}" rx="${f(ew * 1.5)}" ry="${f(eh * 1.5)}" fill="${C.lite}"/><ellipse cx="${f(x + lk * 0.3)}" cy="${f(ey)}" rx="${f(ew * 0.8)}" ry="${f(eh * 0.8)}" fill="${C.ink}"/>`);
    if (eyes === 'closed') out.push(`<path d="M${f(x - ew * 1.2)} ${f(ey)} Q${f(x)} ${f(ey + eh * 1.1)} ${f(x + ew * 1.2)} ${f(ey)}" fill="none" stroke="${C.ink}" stroke-width="${f(sw * 0.9)}" stroke-linecap="round"/>`);
    if (eyes === 'bars') out.push(`<rect x="${f(x - ew * 1.5)}" y="${f(ey - sw / 2)}" width="${f(ew * 3)}" height="${f(sw)}" rx="2" fill="${C.ink}"/>`);
    if (eyes === 'x') { const e = 0.07 * S; out.push(`<path d="M${f(x - e)} ${f(ey - e)} L${f(x + e)} ${f(ey + e)} M${f(x + e)} ${f(ey - e)} L${f(x - e)} ${f(ey + e)}" fill="none" stroke="${C.ink}" stroke-width="${f(sw * 0.9)}" stroke-linecap="round"/>`); }
  });
  const m = o.mouth || 'smile', mx = lk;
  if (m === 'smile') out.push(`<path d="M${f(0.3 * w + mx)} ${f(0.64 * h)} Q${f(0.5 * w + mx)} ${f(0.82 * h)} ${f(0.7 * w + mx)} ${f(0.64 * h)}" fill="none" stroke="${C.ink}" stroke-width="${f(sw)}" stroke-linecap="round"/>`);
  if (m === 'grin') out.push(`<path d="M${f(0.28 * w + mx)} ${f(0.6 * h)} Q${f(0.5 * w + mx)} ${f(0.92 * h)} ${f(0.72 * w + mx)} ${f(0.6 * h)} Z" fill="${C.ink}"/>`);
  if (m === 'flat') out.push(`<rect x="${f(0.36 * w + mx)}" y="${f(0.68 * h)}" width="${f(0.28 * w)}" height="${f(sw)}" rx="2" fill="${C.ink}"/>`);
  if (m === 'o') out.push(`<ellipse cx="${f(0.5 * w + mx)}" cy="${f(0.7 * h)}" rx="${f(0.06 * S)}" ry="${f(0.08 * S)}" fill="${C.ink}"/>`);
  if (m === 'wavy') out.push(`<path d="M${f(0.26 * w)} ${f(0.72 * h)} Q${f(0.36 * w)} ${f(0.62 * h)} ${f(0.46 * w)} ${f(0.72 * h)} Q${f(0.56 * w)} ${f(0.82 * h)} ${f(0.66 * w)} ${f(0.72 * h)}" fill="none" stroke="${C.ink}" stroke-width="${f(sw * 0.9)}" stroke-linecap="round"/>`);
  if (o.sticker) {
    const r = 0.2 * S * o.sticker;
    out.push(`<g transform="translate(${f(0.8 * w)} ${f(0.3 * h)}) rotate(-8)"><circle r="${f(r)}" fill="${C.orange}"/>${text(0, r * 0.14, 'RETURNED', r * 0.36, C.cream, { ls: 0.3 })}</g>`);
  }
  return out.join('');
}
// Boxley flat and straight on, for the flat cards: his feet on the same ground line (gy) as the crew.
// armL is his right arm (on our left), armR his left; angles as boxley()'s (0 hanging, 180 straight up)
function boxleyFront(cx, gy, S, o = {}) {
  const sq = o.squash ?? (o.stride == null && !o.z ? 1 + 0.014 * Math.sin(two(NOW) * 2.6) : 1), w = S * (1 + (1 - sq) * 0.45), h = S * sq, legLen = 0.42 * S, z = o.z || 0;
  const bb = gy - legLen - z, top = bb - h, lw = 0.17 * S, L = 0.5 * S, lid = 0.16 * h;
  const out = [];
  const sh = 1 / (1 + z / 60);
  out.push(`<ellipse cx="${f(cx)}" cy="${f(gy)}" rx="${f(w * 0.62 * sh)}" ry="${f(w * 0.1 * sh)}" fill="rgba(46,31,23,0.18)"/>`);
  [-0.27, 0.11].forEach((fx, i) => {
    const lift = o.stride == null ? 0 : Math.max(0, Math.sin(o.stride + i * Math.PI)) * 0.1 * S;
    const lx = cx + fx * w, ll = legLen - lift;
    out.push(`<rect x="${f(lx)}" y="${f(bb)}" width="${f(0.16 * S)}" height="${f(ll)}" fill="${C.ink}"/>`
      + `<rect x="${f(lx + (i ? 0 : -0.08 * S))}" y="${f(bb + ll - 0.08 * S)}" width="${f(0.24 * S)}" height="${f(0.08 * S)}" rx="${f(0.03 * S)}" fill="${C.ink}"/>`);
  });
  const arm = (side, a) => {
    a = a * Math.PI / 180; const bend = 12 * Math.PI / 180, sx = cx + side * w / 2, sy = bb - h * 0.45;
    return stroke([[sx, sy], [sx + side * L * 0.55 * Math.sin(a - bend), sy + L * 0.55 * Math.cos(a - bend)], [sx + side * L * Math.sin(a), sy + L * Math.cos(a)]], lw);
  };
  out.push(arm(-1, o.armL ?? 20), arm(1, o.armR ?? 20));
  // the top seen just from above, taped across its seam, then the front with his face
  const ti = 0.12 * w;
  out.push(`<polygon points="${f(cx - w / 2)},${f(top)} ${f(cx + w / 2)},${f(top)} ${f(cx + w / 2 - ti)},${f(top - lid)} ${f(cx - w / 2 + ti)},${f(top - lid)}" fill="${C.ctop}"/>`);
  out.push(`<polygon points="${f(cx - w / 2 + ti * 0.42)},${f(top - lid * 0.36)} ${f(cx + w / 2 - ti * 0.42)},${f(top - lid * 0.36)} ${f(cx + w / 2 - ti * 0.58)},${f(top - lid * 0.64)} ${f(cx - w / 2 + ti * 0.58)},${f(top - lid * 0.64)}" fill="${C.tape}"/>`);
  out.push(`<rect x="${f(cx - w / 2)}" y="${f(top)}" width="${f(w)}" height="${f(h)}" fill="${C.cfront}"/>`);
  out.push(`<g transform="translate(${f(cx - w / 2)} ${f(top)})">${boxFace(w, h, S, o)}</g>`);
  out.push(hardhat(cx, top - lid * 0.5 + (o.hatHop || 0), S, {}));
  return out.join('');
}

// ---------- rooms ----------
// the floor slab runs under the walls' thickness, so the walls and floor meet as one piece at every corner
function room(I, W, D, floor = '#D8C397', slab = 14, t = 8) {
  let o = '';
  if (slab > 0.5) o += I.poly([[-t, D, 0], [W, D, 0], [W, D, -slab], [-t, D, -slab]], '#8A6A45')
    + I.poly([[W, -t, 0], [W, D, 0], [W, D, -slab], [W, -t, -slab]], '#6E5236');
  return o + I.poly([[0, 0, 0], [W, 0, 0], [W, D, 0], [0, D, 0]], floor);
}
function wallX(I, D, H, fill, t = 8, cap = '#FFF6DE', end = '#E2D2A8') {
  return I.poly([[0, 0, 0], [0, D, 0], [0, D, H], [0, 0, H]], fill)
    + I.poly([[-t, 0, H], [0, 0, H], [0, D, H], [-t, D, H]], cap)
    + I.poly([[-t, D, 0], [0, D, 0], [0, D, H], [-t, D, H]], end);
}
function wallY(I, W, H, fill, hole = null, t = 8, cap = '#FFF6DE', end = '#E2D2A8') {
  let d = `M0 0 L${W} 0 L${W} ${-H} L0 ${-H} Z`;
  if (hole && hole[1] - hole[0] > 0.5 && hole[2] > 0.5) d += ` M${f(hole[0])} 0 L${f(hole[0])} ${f(-hole[2])} L${f(hole[1])} ${f(-hole[2])} L${f(hole[1])} 0 Z`;
  return I.yplane(0, 0, 0) + `<path d="${d}" fill="${fill}" fill-rule="evenodd"/></g>`
    // the top runs over the corner post, closing the gap where the two walls meet
    + I.poly([[-t, -t, H], [W, -t, H], [W, 0, H], [-t, 0, H]], cap)
    + I.poly([[W, -t, 0], [W, 0, 0], [W, 0, H], [W, -t, H]], end);
}
const stripesY = (I, x0, x1, zs) => [C.orange, C.gold, C.avo].map((c, i) => I.poly([[x0, 0, zs - i * 7], [x1, 0, zs - i * 7], [x1, 0, zs - i * 7 - 5], [x0, 0, zs - i * 7 - 5]], c)).join('');
const stripesX = (I, y0, y1, zs) => [C.orange, C.gold, C.avo].map((c, i) => I.poly([[0, y0, zs - i * 7], [0, y1, zs - i * 7], [0, y1, zs - i * 7 - 5], [0, y0, zs - i * 7 - 5]], c)).join('');
function officeRoom(I, W, D, H, o = {}) {
  return room(I, W, D, o.floor) + wallX(I, D, H, o.lwall || '#EADBB5') + stripesX(I, 0, D, o.sz || 60)
    + wallY(I, W, H, o.rwall || C.cream, o.hole) + (o.hole ? stripesY(I, 0, o.hole[0], o.sz || 60) + stripesY(I, o.hole[1], W, o.sz || 60) : stripesY(I, 0, W, o.sz || 60));
}
// the wallpaper's chevrons, as the game's: small pale carets between the stripes, in rows, every other row half a step over
function chevrons(x0, x1, y0, y1, col) {
  let o = '';
  for (let y = y0 + 14, r = 0; y < y1 - 10; y += 22, r++)
    for (let x = x0 + 18 + (r % 2) * 8; x < x1 - 4; x += 16)
      o += `<path d="M${x - 4} ${y + 3}L${x} ${y}L${x + 4} ${y + 3}" fill="none" stroke="${col}" stroke-width="1.6"/>`;
  return o;
}
// a room out in the backrooms: carpet, yellow wallpaper, a doorway on to more of it
function yellowRoom(I, W, D, H, o = {}) {
  const out = [room(I, W, D, C.carpet, 14)];
  for (let i = 0; i * 60 < W; i++) for (let j = 0; j * 60 < D; j++) if ((i + j) % 2 === 0)
    out.push(I.poly([[i * 60, j * 60, 0.1], [Math.min(i * 60 + 60, W), j * 60, 0.1], [Math.min(i * 60 + 60, W), Math.min(j * 60 + 60, D), 0.1], [i * 60, Math.min(j * 60 + 60, D), 0.1]], C.carpet2));
  // a pool of light from the ceiling panels
  const [lx, ly] = I.p(W * 0.45, D * 0.5, 0);
  out.push(`<ellipse cx="${f(lx)}" cy="${f(ly)}" rx="${f(150 * I.s)}" ry="${f(75 * I.s)}" fill="#C9AE62" opacity="0.5"/>`);
  out.push(wallX(I, D, H, C.yleft, 8, '#F2E4B4', C.yside));
  let lines = '';
  for (let x = 10; x < D; x += 16) lines += `<rect x="${x}" y="0" width="3" height="${H}" fill="${C.ystripeL}"/>`;
  lines += chevrons(0, D, 0, H, C.ychevL);
  const dw = o.door || [96, 156];   // the doorway on the left wall, measured along it
  out.push(I.xplane(0, D, H) + lines + `<rect x="0" y="${H - 6}" width="${D}" height="6" fill="#8E7438"/>`
    + `<rect x="${dw[0]}" y="${H - 84}" width="${dw[1] - dw[0]}" height="84" fill="#5E3A24"/><rect x="${dw[0] + 10}" y="${H - 74}" width="${dw[1] - dw[0] - 20}" height="74" fill="#8A5534"/><rect x="${dw[0] + 16}" y="${H - 66}" width="${dw[1] - dw[0] - 32}" height="26" fill="#7A4A2E"/><rect x="${dw[0] + 16}" y="${H - 34}" width="${dw[1] - dw[0] - 32}" height="28" fill="#7A4A2E"/><circle cx="${dw[1] - 18}" cy="${H - 38}" r="3" fill="#D9B45A"/></g>`);
  out.push(wallY(I, W, H, C.ywall, null, 8, '#F2E4B4', C.yside));
  lines = '';
  for (let x = 8; x < W; x += 16) lines += `<rect x="${x}" y="${-H}" width="3" height="${H}" fill="${C.ystripe}"/>`;
  lines += chevrons(0, W, -H, 0, C.ychev);
  out.push(I.yplane(0, 0, 0) + lines + `<rect x="0" y="-6" width="${W}" height="6" fill="#9A7F3E"/></g>`);
  return out.join('');
}

// the backrooms: one endless lattice of yellow walls, laid over any set of areas so they all join up
// floors: [[x0,x1,y0,y1]...] carpet; walls: areas the walls are clipped to; H: wall height; rise 0..1 grows them
const hexRGB = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16));
const mixHex = (a, b, t) => '#' + hexRGB(a).map((v, i) => Math.round(lerp(v, hexRGB(b)[i], t)).toString(16).padStart(2, '0')).join('');
function backrooms(I, floors, walls, H0, rise = 1, opt = {}) {
  if (rise <= 0) return '';
  const H = H0 * rise, s = I.s;
  const vis = (x, y, w, d, hh) => {
    const sx0 = I.ox + (x - (y + d)) * s, sx1 = I.ox + ((x + w) - y) * s;
    const sy0 = I.oy + ((x + y) / 2 - hh) * s, sy1 = I.oy + ((x + w + y + d) / 2) * s;
    return sx1 > -20 && sx0 < VW + 20 && sy1 > -20 && sy0 < VH + 20;
  };
  const o = [`<g opacity="${f(clamp(rise * 1.5))}">`];
  floors.forEach(([x0, x1, y0, y1]) => o.push(I.poly([[x0, y0, 0], [x1, y0, 0], [x1, y1, 0], [x0, y1, 0]], C.carpet)));
  // carpet tiles, one per cell
  floors.forEach(([x0, x1, y0, y1]) => {
    for (let kk = Math.floor((-150 - y1) / 110) - 1; kk <= Math.ceil((-150 - y0) / 110) + 1; kk++) {
      const off = ((kk % 2) + 2) % 2 ? 60 : 0;
      for (let j = Math.floor(x0 / 120) - 1; j <= Math.ceil(x1 / 120) + 1; j++) {
        const x = j * 120 + off + 30, y = -110 - kk * 110 + 30;
        if (x < x0 || x + 40 > x1 || y < y0 || y + 40 > y1 || !vis(x, y, 40, 40, 0)) continue;
        o.push(I.poly([[x, y, 0.1], [x + 40, y, 0.1], [x + 40, y + 40, 0.1], [x, y + 40, 0.1]], '#B39652'));
        // every other cell has a ceiling panel lit above it: a soft pool of light on the carpet
        if (opt.pools !== false && ((j + kk) % 2 + 2) % 2 === 0) { const [lx, ly] = I.p(x + 20, y + 20, 0); o.push(`<ellipse cx="${f(lx)}" cy="${f(ly)}" rx="${f(52 * s)}" ry="${f(26 * s)}" fill="#CDB26A" opacity="0.5"/>`); }
      }
    }
  });
  const list = [];
  const add = (x, y, w, d) => walls.forEach(([x0, x1, y0, y1]) => {
    const ax = Math.max(x, x0), bx = Math.min(x + w, x1), ay = Math.max(y, y0), by = Math.min(y + d, y1);
    if (bx - ax > 0.5 && by - ay > 0.5 && vis(ax, ay, bx - ax, by - ay, H)) list.push([ax, ay, bx - ax, by - ay]);
  });
  const Y0 = Math.min(...walls.map(r => r[2])), Y1 = Math.max(...walls.map(r => r[3]));
  const X0 = Math.min(...walls.map(r => r[0])), X1 = Math.max(...walls.map(r => r[1]));
  for (let kk = Math.floor((-150 - Y1) / 110) - 1; kk <= Math.ceil((-150 - Y0) / 110) + 2; kk++) {
    const y = -150 - kk * 110, odd = ((kk % 2) + 2) % 2, off = odd ? 60 : 0;
    for (let j = Math.floor(X0 / 120) - 1; j <= Math.ceil(X1 / 120) + 1; j++) {
      add(j * 120 - off, y, 80, 8);
      if ((j + odd) % 2 === 0) add(j * 120 + 90 - off, y - 102, 8, 102);
    }
  }
  list.sort((a, b) => (a[0] + a[2] + a[1] + a[3]) - (b[0] + b[2] + b[1] + b[3]));
  list.forEach(([x, y, w, d]) => {
    // further from the middle of the picture, the walls fade into a pale haze
    let hz = 0;
    if (opt.fog) { const [cx, cy] = I.p(x + w / 2, y + d / 2, H / 2); hz = clamp((Math.hypot(cx - opt.fog[0], (cy - opt.fog[1]) * 1.3) - 170) / 420) * 0.6; }
    const c = col => hz > 0.01 ? mixHex(col, '#F8EFCB', hz) : col;
    o.push(I.box(x, y, 0, w, d, H, c(C.ywall), c(C.yleft), c(C.yside)));
  });
  o.push('</g>');
  return o.join('');
}
function cart(I, x, y, z = 0, len = 110, wid = 60) {
  const o = [];
  [[x + 6, y + 6], [x + len - 14, y + 6], [x + 6, y + wid - 14], [x + len - 14, y + wid - 14]].forEach(([wx, wy]) => {
    const [cx, cy] = I.p(wx + 4, wy + 4, z + 4);
    o.push(`<ellipse cx="${f(cx)}" cy="${f(cy)}" rx="${f(6 * I.s)}" ry="${f(5 * I.s)}" fill="${C.ink}"/>`);
  });
  o.push(I.box(x, y, z + 10, len, wid, 8, '#7FA39A', C.teal, '#1F4C48'));
  o.push(stroke([I.p(x, y + 6, z + 18), I.p(x, y + 6, z + 60), I.p(x, y + wid - 6, z + 60), I.p(x, y + wid - 6, z + 18)], 5 * I.s, '#1F4C48'));
  return o.join('');
}
// a door in the right-hand wall (the y = 0 wall). open 0..1. The opening's outline on screen is returned too,
// so a figure walking through can be clipped to it.
function hubDoor(I, x0, open = 0, o = {}) {
  const wd = o.wd ?? 58, hd = o.hd ?? 84, depth = o.depth ?? 8, label = o.label ?? 'HUB', lsz = o.labelSize ?? 15;
  const x1 = x0 + wd, out = [];
  out.push(I.poly([[x0 + 4, 0, 0.2], [x1 - 4, 0, 0.2], [x1 + 30, 120, 0.2], [x0 - 20, 120, 0.2]], '#FFF4C8', ` opacity="${f(0.55 * open)}"`));
  out.push(I.yplane(x0 - 6, 0, hd + 6) + `<rect x="0" y="0" width="${wd + 12}" height="${hd + 6}" fill="#1F4C48"/></g>`);
  out.push(I.poly([[x0, -depth, 0], [x1, -depth, 0], [x1, -depth, hd], [x0, -depth, hd]], open > 0.02 ? '#FFF4C8' : C.teal));
  out.push(I.poly([[x0, -depth, 0], [x0, 0, 0], [x0, 0, hd], [x0, -depth, hd]], '#173B38'));
  // the panel swings in (narrows) as it opens
  const pw = wd * (1 - 0.85 * open);
  out.push(I.yplane(x0, -depth, hd) + `<rect x="0" y="0" width="${f(pw)}" height="${hd}" fill="${C.teal}"/>`
    + (pw > 30 ? `<rect x="10" y="10" width="${f(pw - 20)}" height="26" fill="#FFF4C8"/><rect x="6" y="46" width="${f(pw - 12)}" height="5" rx="2.5" fill="${C.gold}"/><rect x="4" y="${hd - 14}" width="${f(pw - 8)}" height="12" fill="#3E7C75"/>` : '') + `</g>`);
  if (label) {
    out.push(I.box(x0 - 4, 0, hd + 12, wd + 8, 10, 22, '#E07A44', C.orange, '#A9471B'));
    out.push(I.yplane(x0 - 4, 10, hd + 34) + text((wd + 8) / 2, 11 + lsz * 0.4, label, lsz, C.cream, { ls: lsz > 12 ? 3 : 1.2 }) + '</g>');
  }
  out.push(I.poly([[x0 - 2, 6, 0.4], [x1 + 2, 6, 0.4], [x1 + 2, 34, 0.4], [x0 - 2, 34, 0.4]], C.avo));
  const clip = [I.p(x0, 0, 0), I.p(x1, 0, 0), I.p(x1, 0, hd), I.p(x0, 0, hd)];
  return { svg: out.join(''), clip };
}
// a doorway in the left-hand wall (the x = 0 wall), from y0 to y0 + wd along it; lit inside. Returns its outline for clipping.
function leftDoor(I, y0, wd = 56, hd = 84, label = '') {
  const out = [];
  out.push(I.xplane(0, y0 + wd + 6, hd + 6) + `<rect x="0" y="0" width="${wd + 12}" height="${hd + 6}" fill="#1F4C48"/></g>`);
  out.push(I.xplane(0, y0 + wd, hd) + `<rect x="0" y="0" width="${wd}" height="${hd}" fill="#FFF4C8"/><rect x="0" y="${hd - 10}" width="${wd}" height="10" fill="#F2E3B0"/></g>`);
  if (label) out.push(I.xplane(0, y0 + wd + 24, hd + 28) + `<rect x="0" y="0" width="${wd + 48}" height="18" fill="${C.orange}"/>` + text((wd + 48) / 2, 12.5, label, 9, C.cream, { ls: 0.6 }) + '</g>');
  out.push(I.poly([[4, y0 - 4, 0.4], [40, y0 - 4, 0.4], [40, y0 + wd + 4, 0.4], [4, y0 + wd + 4, 0.4]], C.avo));
  return { svg: out.join(''), clip: [I.p(0, y0, 0), I.p(0, y0 + wd, 0), I.p(0, y0 + wd, hd), I.p(0, y0, hd)] };
}
// a crew member lying flat: the figure turned about its feet (rot -90 puts the head to the left)
const lying = (px, py, s, rot, o = {}) => `<g transform="rotate(${f(rot)} ${f(px)} ${f(py)})">${picto(px, py, s, { ...o, shadow: false })}</g>`;
// stars going round a knocked-out head
function dizzy(hx, hy, R, phase) {
  const out = [`<ellipse cx="${f(hx)}" cy="${f(hy)}" rx="${f(R)}" ry="${f(R * 0.38)}" fill="none" stroke="${C.gold}" stroke-width="2" opacity="0.55"/>`];
  [[200, 0.75], [290, 1.0], [20, 0.8], [110, 1.2]].forEach(([ang, big]) => {
    const a = (ang + phase * 360) * Math.PI / 180;
    out.push(star(hx + R * Math.cos(a), hy + R * 0.38 * Math.sin(a), Math.max(0.6, R / 50) * big * (0.85 + 0.25 * Math.sin(a))));
  });
  return out.join('');
}
const mark = (x, y, ch, size = 30) => text(x, y, ch, size, C.orange, { ls: 0, extra: ` stroke="${C.cream}" stroke-width="3" paint-order="stroke"` });
// ---------- the hub's furniture: the same few things in every room of it ----------
const shadowFoot = (I, x, y, w, d) => I.poly([[x + 2, y + 2, 0.05], [x + w + 5, y + 2, 0.05], [x + w + 5, y + d + 5, 0.05], [x + 2, y + d + 5, 0.05]], 'rgba(46,31,23,0.16)');
function cooler(I, x, y) {
  return shadowFoot(I, x, y, 20, 20) + I.box(x, y, 0, 20, 20, 40, '#F3E7CC', '#E2D2A8', '#CDBB8E')
    + I.box(x + 20, y + 8, 24, 3, 4, 4, '#E07A44', C.orange, '#A9471B')
    + I.box(x + 4, y + 4, 40, 12, 12, 4, '#BFB290', '#A39C8A', '#8A8473')
    + I.box(x + 3, y + 3, 44, 14, 14, 22, '#CFE6E0', '#9FC2B6', '#7FA39A')
    + I.poly([[x + 5, y + 5, 52], [x + 7, y + 5, 52], [x + 7, y + 5, 62], [x + 5, y + 5, 62]], '#FFFFFF', ' opacity="0.5"');
}
function lockers(I, y0, n) {   // standing against the left wall, doors facing into the room
  let o = '';
  for (let i = 0; i < n; i++) {
    const y = y0 + i * 22;
    o += I.box(0, y, 0, 18, 22, 78, '#7FA39A', '#3E7C75', C.teal) + I.xplane(18, y + 22, 78)
      + `<rect x="3" y="6" width="16" height="2" fill="#1F4C48"/><rect x="3" y="10" width="16" height="2" fill="#1F4C48"/><rect x="3" y="14" width="16" height="2" fill="#1F4C48"/><rect x="15" y="36" width="3" height="10" rx="1.5" fill="${C.gold}"/><rect x="0" y="0" width="1.5" height="78" fill="#1F4C48"/></g>`;
  }
  return o;
}
function plant(I, x, y) {
  // a terracotta pot with a rolled rim and dark soil, and a fan of long pointed leaves that sway a little
  const [cx, cy] = I.p(x + 11, y + 11, 25), s = I.s, sw = Math.sin(two(NOW) * 1.3 + x) * 2.5;
  const leaf = (ang, len, wid, col) => `<path d="M0 0 Q${f(wid)} ${f(-len * 0.45)} 0 ${f(-len)} Q${f(-wid)} ${f(-len * 0.45)} 0 0 Z" fill="${col}" transform="translate(${f(cx)} ${f(cy)}) rotate(${f(ang + sw)}) scale(${f(s)})"/>`
    + `<path d="M0 0 L0 ${f(-len * 0.85)}" stroke="rgba(46,31,23,0.25)" stroke-width="1.2" fill="none" transform="translate(${f(cx)} ${f(cy)}) rotate(${f(ang + sw)}) scale(${f(s)})"/>`;
  const back = [[-62, 40, 9], [-30, 50, 10], [8, 54, 10], [44, 46, 9], [70, 36, 8]].map(([a, l, w]) => leaf(a, l, w, '#5E6A2C')).join('');
  const front = [[-46, 42, 10], [-14, 48, 11], [24, 44, 10], [56, 38, 9]].map(([a, l, w]) => leaf(a, l, w, C.avo)).join('');
  return shadowFoot(I, x, y, 22, 22)
    + I.box(x + 2, y + 2, 0, 18, 18, 20, '#E07A44', '#C4622F', '#A9471B')
    + I.box(x, y, 18, 22, 22, 6, '#E8935F', '#D06A36', '#B5521F')
    + I.poly([[x + 3, y + 3, 24.1], [x + 19, y + 3, 24.1], [x + 19, y + 19, 24.1], [x + 3, y + 19, 24.1]], '#4A3427')
    + back + front;
}
const rug = (I, x0, y0, x1, y1) => [['#C9895A', 0], ['#D99A66', 6], ['#C9895A', 12]].map(([c, d], i) => I.poly([[x0 + d, y0 + d, 0.12 + i * 0.01], [x1 - d, y0 + d, 0.12 + i * 0.01], [x1 - d, y1 - d, 0.12 + i * 0.01], [x0 + d, y1 - d, 0.12 + i * 0.01]], c)).join('');
function hazard(I, x0, x1, y, dpt = 10) {
  let o = I.poly([[x0, y, 0.13], [x1, y, 0.13], [x1, y + dpt, 0.13], [x0, y + dpt, 0.13]], C.gold);
  for (let x = x0; x < x1 - 8; x += 16) o += I.poly([[x, y, 0.14], [x + 8, y, 0.14], [x + 14, y + dpt, 0.14], [x + 6, y + dpt, 0.14]], C.ink);
  return o;
}
// a school wall clock: dark rim, cream face, twelve ticks, hands; drawn about (0, 0), radius r
function clockFace(r, minDeg, hourDeg, secDeg = null) {
  let o = `<circle r="${f(r + 2)}" fill="rgba(46,31,23,0.18)" transform="translate(2 3)"/><circle r="${f(r)}" fill="${C.choc}"/><circle r="${f(r * 0.86)}" fill="${C.lite}"/>`;
  for (let i = 0; i < 12; i++) {
    const big = i % 3 === 0;
    o += `<rect x="${f(-r * (big ? 0.05 : 0.03))}" y="${f(-r * 0.82)}" width="${f(r * (big ? 0.1 : 0.06))}" height="${f(r * (big ? 0.17 : 0.1))}" fill="${C.ink}" transform="rotate(${i * 30})"/>`;
  }
  o += `<rect x="${f(-r * 0.05)}" y="${f(-r * 0.46)}" width="${f(r * 0.1)}" height="${f(r * 0.52)}" rx="${f(r * 0.05)}" fill="${C.ink}" transform="rotate(${f(hourDeg)})"/>`;
  o += `<rect x="${f(-r * 0.035)}" y="${f(-r * 0.7)}" width="${f(r * 0.07)}" height="${f(r * 0.78)}" rx="${f(r * 0.035)}" fill="${C.ink}" transform="rotate(${f(minDeg)})"/>`;
  if (secDeg !== null) o += `<rect x="${f(-r * 0.012)}" y="${f(-r * 0.74)}" width="${f(r * 0.024)}" height="${f(r * 0.9)}" fill="${C.orange}" transform="rotate(${f(secDeg)})"/>`;
  o += `<circle r="${f(r * 0.09)}" fill="${C.orange}"/><path d="M${f(-r * 0.55)} ${f(-r * 0.5)} A${f(r * 0.75)} ${f(r * 0.75)} 0 0 1 ${f(r * 0.2)} ${f(-r * 0.72)}" fill="none" stroke="#FFFFFF" stroke-width="${f(r * 0.06)}" stroke-linecap="round" opacity="0.5"/>`;
  return o;
}
// a small wall clock on the right-hand wall, reading near the end of the day
const wallClock = (I, x, z) => I.yplane(x, 0, z) + `<g transform="translate(15 15)">${clockFace(15, two(NOW) * 6, 135, two(NOW) * 120)}</g></g>`;
const clipPoly = (id, pts) => `<clipPath id="${id}"><polygon points="${pts.map(q => q.map(f).join(',')).join(' ')}"/></clipPath>`;

// ---------- scenes: each draws its own frame from local time t (seconds) ----------
const SCENES = [];
const scene = (name, dur, line, draw) => SCENES.push({ name, dur, line, draw });

// 0. title card: the logo, the name, the crew either side, Boxley in the middle
function titleCard(t, o = {}) {
  const out = [BG];
  [0, 1, 2].forEach(i => {
    const p = o.done ? 1 : k(t, 1.0 + i * 0.12, 1.6 + i * 0.12, E.out);
    out.push(`<rect x="${f(-VW + VW * p)}" y="${344 + i * 19}" width="${VW}" height="14" fill="${[C.orange, C.gold, C.avo][i]}"/>`);
  });
  out.push(`<rect x="0" y="505" width="${VW}" height="35" fill="${C.choc}"/>`);
  // the logo: a box drops in, three smaller ones pop out of it
  const drop = o.done ? 1 : k(t, 1.5, 2.2, E.bounce);
  out.push(`<g transform="translate(288 ${f(22 - 260 * (1 - drop))})">`);
  [[44, 18, 30, -14, C.orange, 2.25], [78, 6, 26, 12, C.gold, 2.4], [62, 38, 24, 0, C.avo, 2.55]].forEach(([x, y, s, r, c, at]) => {
    const p = o.done ? 1 : k(t, at, at + 0.35, E.back);
    out.push(`<rect x="${x}" y="${f(y + 40 * (1 - p))}" width="${s}" height="${s}" fill="${c}" transform="rotate(${r} ${x + s / 2} ${y + s / 2}) " opacity="${p > 0.01 ? 1 : 0}"/>`);
  });
  out.push(`<polygon points="20,66 0,44 50,40 60,66" fill="#DDB06A"/><polygon points="124,66 144,44 94,40 84,66" fill="#DDB06A"/><rect x="20" y="66" width="104" height="70" fill="${C.cfront}"/><rect x="64" y="66" width="16" height="22" fill="${C.tape}"/></g>`);
  const wm = o.done ? 1 : k(t, 2.4, 3.0, E.back);
  out.push(`<g transform="translate(${f(400 * (1 - wm))} 0)" opacity="${wm > 0 ? 1 : 0}">${text(VW / 2, 236, 'OVERFLOW', 92, C.ink, { ls: 4 })}</g>`);
  const tg = o.done ? 1 : k(t, 3.1, 3.7);
  out.push(`<g opacity="${f(tg)}">${text(VW / 2, 274, 'There’s always more room.', 24, C.ink, { weight: 500, ls: 0 })}</g>`);
  const bn = o.done ? 1 : k(t, 3.9, 4.3);
  out.push(`<g opacity="${f(bn)}">${text(VW / 2, 306, o.banner || 'EMPLOYEE ORIENTATION · TAPE 1 OF 1', 14, C.orange, { weight: 700, italic: false, ls: 6 })}</g>`);
  // the crew pop up and wave, two each side, spread out; Boxley hops into the middle
  const tt = two(t);
  [[84, 4.2, 'wave'], [194, 4.35, 'stand'], [526, 4.5, 'both'], [636, 4.65, 'wave2']].forEach(([x, at, pose], i) => {
    const p = o.done ? 1 : k(tt, at, at + 0.4, E.back);
    if (p <= 0) return;
    const wv = Math.sin((tt - at) * 7 + i) * 18;
    const po = pose === 'wave' ? { aR: -135 + wv } : pose === 'both' ? { aL: 135 - wv, aR: -135 + wv } : pose === 'wave2' ? { aL: 115 + wv } : {};
    out.push(picto(x, 505 + 200 * (1 - p), 0.7, { ...po, mirror: x > VW / 2 }));
  });
  const bp = o.done ? 1 : k(t, 5.0, 6.0, E.lin);
  if (bp > 0) {
    const landed = bp >= 1, lift = landed ? 0 : hop(bp, 1 / 3) * 40;
    if (!o.done) out.push(dust(VW / 2, 505, k(t, 6.0, 6.5)));
    out.push(boxleyFront(lerp(-60, VW / 2, bp), 505, 62, { z: lift, armL: landed ? 150 + Math.sin(tt * 7) * 15 : 45, armR: landed ? 20 : 45, squash: landed ? 1 : 1 - 0.08 * (1 - hop(bp, 1 / 3)), stride: landed ? null : bp * 18 }));
  }
  return out.join('');
}
scene('title', 8, '', t => titleCard(t));

// 1. you've been hired: Boxley comes in, hops into a seat facing the TV, then turns to us
function chair(I, x, y) {
  return [I.box(x + 19, y + 19, 0, 6, 6, 26, '#4A3427', C.ink, '#3B281D'), I.box(x, y, 26, 44, 44, 6, '#E07A44', C.orange, '#A9471B')].join('');
}
const chairBack = (I, x, y) => I.box(x + 38, y, 32, 6, 44, 22, '#E07A44', C.orange, '#A9471B');
scene('hired', 8, 'Congratulations. You’ve been hired. Sit down. (beat) You’re already sitting down. Good.', t => {
  const tt = two(t);
  const zm = k(t, 0, 8, E.inOut);
  const W = 340, D = 320, H = 112;
  const I = isoAt(lerp(170, 205, zm), lerp(160, 165, zm), 0, VW / 2, lerp(300, 306, zm), lerp(0.86, 0.96, zm));
  const out = [BG, room(I, W, D), wallX(I, D, H, '#EADBB5'), stripesX(I, 0, D, 62), wallY(I, W, H, C.cream), stripesY(I, 0, W, 62)];
  // a poster on the right wall, and the door he came in by
  out.push(I.yplane(150, 0, 100) + `<rect x="0" y="0" width="86" height="54" fill="${C.teal}"/>` + text(43, 24, 'OVERFLOW', 12, C.cream, { ls: 1 }) + `<rect x="10" y="32" width="66" height="4" fill="${C.orange}"/><rect x="10" y="38" width="66" height="4" fill="${C.gold}"/><rect x="10" y="44" width="66" height="4" fill="${C.avo}"/></g>`);
  out.push(hubDoor(I, 272, 1 - k(t, 2.0, 2.5), { label: 'TRAINING', wd: 54, labelSize: 10 }).svg);
  // the TV on its trolley against the left wall, its screen facing the seats
  out.push(I.box(14, 122, 0, 36, 50, 34, '#7FA39A', C.teal, '#1F4C48'));
  out.push(I.box(16, 124, 34, 32, 46, 30, '#5A4334', '#3B281D', C.ink));
  out.push(stroke([I.p(30, 145, 64), I.p(22, 132, 92)], 2.4 * I.s) + stroke([I.p(30, 150, 64), I.p(34, 166, 90)], 2.4 * I.s));
  const roll = (t * 30) % 46;
  out.push(I.xplane(48, 166, 61) + `<rect x="0" y="0" width="38" height="24" fill="#9FC2B6"/>`
    + `<rect x="3" y="12" width="32" height="3" fill="${C.orange}"/><rect x="3" y="16" width="32" height="3" fill="${C.gold}"/><rect x="3" y="20" width="32" height="3" fill="${C.avo}"/>`
    + `<rect x="13" y="4" width="12" height="7" fill="${C.cfront}"/>` + [C.orange, C.gold, C.avo].map((c, i) => { const q = ((t * 0.7) + i * 0.12) % 1, up = Math.min(1, q * 4); return `<rect x="${14 + i * 3.4}" y="${f(5 - up * 4)}" width="3" height="3" fill="${c}"/>`; }).join('')
    + `<rect x="0" y="${f(roll * 0.5)}" width="38" height="2" fill="#FFF" opacity="0.35"/></g>`);
  out.push(cooler(I, 0, 266));
  // two rows of seats facing the TV; Boxley's is the middle one of the back row
  const seats = [[110, 70], [110, 140], [110, 210], [190, 70], [190, 140], [190, 210]].sort((a, b) => (a[0] + a[1]) - (b[0] + b[1]));
  const mine = [190, 140];
  const S = 38;
  let bx, by, z = 0, sq = 1, seat, face = 'y', look = 0;
  if (tt < 2.4) { const p = tt / 2.4; bx = lerp(278, 252, p); by = lerp(6, 142, p); z = hop(tt, 0.8) * 24; sq = 1 - 0.1 * (1 - hop(tt, 0.8)); }
  else if (tt < 3.2) { const p = k(tt, 2.4, 3.2, E.out); bx = lerp(252, mine[0] + 2, p); by = lerp(142, mine[1] + 3, p); z = Math.sin(p * Math.PI) * 52 + p * 32; face = p > 0.5 ? '-x' : 'y'; }
  else { bx = mine[0] + 2; by = mine[1] + 3; seat = 32; sq = tt < 3.5 ? 0.85 : tt < 4.0 ? 1.08 : 1; face = '-x'; }
  // the beat, then he turns round to us
  if (tt >= 5.2) { face = 'y'; look = -0.5; sq = tt < 5.35 ? 0.93 : 1; }
  const bo = boxley(I, bx, by, S, { z: seat ? 0 : z, seatZ: seat, seatLegs: face !== 'y', squash: sq, face, look, armL: tt > 6.6 ? 150 + Math.sin(tt * 8) * 10 : tt >= 3.2 ? -14 : 40, armR: tt >= 3.2 ? -14 : 40, eyes: tt > 5.6 && tt < 5.8 ? 'closed' : 'open', mouth: tt > 6.2 ? 'grin' : 'smile' });
  const inSeat = tt >= 2.4;
  const crew = { '110,70': 0.3, '110,210': 1.1 };
  seats.forEach(([x, y]) => {
    out.push(chair(I, x, y));
    const ck = x + ',' + y;
    if (crew[ck] !== undefined) {
      const [sx, sy] = I.p(x + 20, y + 22, 32), scl = 0.4 * I.s;
      out.push(picto(sx, sy + 66 * scl, scl, { lL: 80, lR: 80, shadow: false, headX: tt > 5.5 ? 5 : -3, bob: -1.2 * Math.abs(Math.sin(two(t) * 1.7 + crew[ck] * 3)) }));
    }
    if (x === mine[0] && y === mine[1] && inSeat) {
      const low = (seat ? 32 : z) < 56;
      if (low) out.push(bo, chairBack(I, x, y)); else out.push(chairBack(I, x, y), bo);
    } else out.push(chairBack(I, x, y));
  });
  if (!inSeat) out.push(bo);
  // dust as he lands from each hop down the aisle
  if (tt > 0.8 && tt < 2.6) { const q = ((tt % 0.8) / 0.4); const [dx, dy] = I.p(bx + S / 2, by + S / 2, 0); out.push(dust(dx, dy, q, I.s * 0.8)); }
  out.push(plant(I, 304, 284));
  return out.join('');
});

// 2. the world is full
function globe(t, tt, r) {
  const out = [`<defs><clipPath id="gclip"><circle r="${r}"/></clipPath></defs>`];
  out.push(`<circle r="${r + 6}" fill="${C.ink}"/><circle r="${r}" fill="${C.teal}"/>`);
  out.push(`<g clip-path="url(#gclip)">`);
  // the land slides round as the world turns
  const lands = sx => `<g transform="translate(${f(sx)} 0)">`
    + `<path d="M-120 -80 Q-80 -122 -24 -104 Q14 -84 -6 -50 Q-30 -20 -70 -34 Q-96 -18 -118 -40 Q-138 -60 -120 -80 Z" fill="${C.gold}"/>`
    + `<path d="M20 -6 Q64 -36 104 -4 Q124 40 92 78 Q60 112 34 86 Q12 60 24 34 Q8 16 20 -6 Z" fill="${C.avo}"/>`
    + `<path d="M-130 30 Q-96 14 -70 44 Q-58 86 -96 108 Q-134 96 -130 30 Z" fill="${C.avo}"/>`
    + `<path d="M120 -110 Q150 -120 168 -96 Q160 -70 132 -74 Q110 -90 120 -110 Z" fill="${C.gold}"/>`
    + `<path d="M-30 110 Q10 100 30 124 Q0 140 -36 130 Z" fill="${C.gold}"/></g>`;
  const sx = -((t * 14) % 340);
  out.push(lands(sx), lands(sx + 340));
  // latitude and longitude, like a classroom globe
  [[0, 0.3], [-0.5, 0.22], [0.5, 0.22]].forEach(([yy, ry]) => out.push(`<ellipse cx="0" cy="${f(yy * r)}" rx="${f(r * Math.sqrt(1 - yy * yy))}" ry="${f(r * ry)}" fill="none" stroke="${C.cream}" stroke-width="2" opacity="0.28"/>`));
  [0.35, 0.75].forEach(rx => out.push(`<ellipse rx="${f(r * rx)}" ry="${r}" fill="none" stroke="${C.cream}" stroke-width="2" opacity="0.22"/>`));
  // light from the upper left, shade lower right
  out.push(`<circle cx="${f(r * 0.28)}" cy="${f(r * 0.3)}" r="${r}" fill="none" stroke="${C.ink}" stroke-width="${f(r * 0.5)}" opacity="0.18"/>`);
  out.push(`<path d="M${f(-r * 0.72)} ${f(-r * 0.2)} A${f(r * 0.8)} ${f(r * 0.8)} 0 0 1 ${f(-r * 0.15)} ${f(-r * 0.78)}" fill="none" stroke="#FFF8DA" stroke-width="${f(r * 0.09)}" stroke-linecap="round" opacity="0.5"/>`);
  out.push(`</g>`);
  return out.join('');
}
const flatBox = s => {
  const h = s / 2, dx = s * 0.26, dy = -s * 0.2, sk = `stroke="${C.ink}" stroke-width="2" stroke-linejoin="round"`;
  const P = (x, fr) => `${f(x + dx * fr)},${f(-h + dy * fr)}`;
  return `<polygon points="${-h},${-h} ${h},${-h} ${f(h + dx)},${f(-h + dy)} ${f(-h + dx)},${f(-h + dy)}" fill="${C.ctop}" ${sk}/>`
    + `<polygon points="${h},${-h} ${f(h + dx)},${f(-h + dy)} ${f(h + dx)},${f(h + dy)} ${h},${h}" fill="${C.cside}" ${sk}/>`
    + `<rect x="${-h}" y="${-h}" width="${s}" height="${s}" fill="${C.cfront}" ${sk}/>`
    // the lid seam, taped, and the tape running down the side
    + `<polygon points="${P(-h, 0.36)} ${P(h, 0.36)} ${P(h, 0.64)} ${P(-h, 0.64)}" fill="${C.tape}"/>`
    + `<polygon points="${P(h, 0.36)} ${P(h, 0.64)} ${f(h + dx * 0.64)},${f(-h + dy * 0.64 + s * 0.32)} ${f(h + dx * 0.36)},${f(-h + dy * 0.36 + s * 0.32)}" fill="${C.tape}"/>`
    // a shipping label and THIS WAY UP arrows printed on the front
    + `<rect x="${f(-h + s * 0.12)}" y="${f(-h + s * 0.18)}" width="${f(s * 0.42)}" height="${f(s * 0.26)}" fill="${C.lite}"/>`
    + `<rect x="${f(-h + s * 0.17)}" y="${f(-h + s * 0.25)}" width="${f(s * 0.3)}" height="${f(s * 0.04)}" fill="${C.cside}"/><rect x="${f(-h + s * 0.17)}" y="${f(-h + s * 0.33)}" width="${f(s * 0.22)}" height="${f(s * 0.04)}" fill="${C.cside}"/>`
    + `<path d="M${f(h - s * 0.3)} ${f(h - s * 0.12)} v${f(-s * 0.2)} m${f(-s * 0.06)} ${f(s * 0.07)} l${f(s * 0.06)} ${f(-s * 0.07)} l${f(s * 0.06)} ${f(s * 0.07)} M${f(h - s * 0.15)} ${f(h - s * 0.12)} v${f(-s * 0.2)} m${f(-s * 0.06)} ${f(s * 0.07)} l${f(s * 0.06)} ${f(-s * 0.07)} l${f(s * 0.06)} ${f(s * 0.07)}" fill="none" stroke="#7A5530" stroke-width="${f(Math.max(1.2, s * 0.035))}" stroke-linecap="round" stroke-linejoin="round"/>`;
};
scene('full', 11, 'Here’s the problem. The world is full. Every shelf, every closet, every storage unit your uncle swore he’d clear out. Full.', t => {
  const tt = two(t);
  const out = [BG, stripes(0, 470, VW, 10, 4)];
  const gx = 196, gy = 262, r = 132;
  const grow = k(t, 0, 0.6, E.back);
  const strain = 1 + 0.06 * k(t, 1.5, 8.5) + (tt > 8.5 ? 0.03 * Math.sin(tt * 30) * (1 - k(tt, 8.5, 9.5)) : 0);
  const sx = strain * (1 + 0.02 * Math.sin(tt * 9)), sy = strain * (1 - 0.02 * Math.sin(tt * 9));
  // its shadow on the floor
  out.push(`<ellipse cx="${gx}" cy="468" rx="${f(110 * grow)}" ry="12" fill="rgba(46,31,23,0.18)"/>`);
  out.push(`<g transform="translate(${gx} ${gy}) scale(${f(sx * grow)} ${f(sy * grow)})">`);
  // boxes punch out through the surface all round and stay stuck there, half in, half out
  const spots = [-100, -55, -20, 25, 60, 150, 205, 250, -150, 100, 290, 175];
  const behind = [], front = [];
  spots.forEach((deg, i) => {
    const at = 1.2 + i * 0.5;
    const p = k(tt, at, at + 0.3, E.back);
    if (p <= 0) return;
    const a = deg * Math.PI / 180, s = 30 + (i % 3) * 8, out_ = r * (0.86 + 0.16 * p);
    const wob = tt > at + 0.3 ? Math.sin(tt * 9 + i) * 3 : 0;
    const g = `<g transform="translate(${f(Math.cos(a) * out_)} ${f(Math.sin(a) * out_)}) rotate(${f(deg + 90 + wob + ((i * 37) % 30 - 15))}) scale(${f(p)})">${flatBox(s)}</g>`;
    (i % 3 === 2 ? behind : front).push(g);
  });
  out.push(behind.join(''), globe(t, tt, r), front.join(''));
  out.push('</g>');
  // sweat flies off as it strains
  for (let i = 0; i < 4; i++) {
    const at = 6.4 + i * 0.6, p = k(t, at, at + 0.5, E.out);
    if (p <= 0 || p >= 1) continue;
    const side = i % 2 ? 1 : -1;
    const x = gx + side * (r + 20 + p * 40), y = gy - r * 0.6 - p * 30 + p * p * 60;
    out.push(`<path d="M${f(x)} ${f(y - 10)} Q${f(x + 7)} ${f(y + 2)} ${f(x)} ${f(y + 6)} Q${f(x - 7)} ${f(y + 2)} ${f(x)} ${f(y - 10)} Z" fill="#9FC2B6" opacity="${f(1 - p)}"/>`);
  }
  // the closet: it rattles, bulges, then bursts and the boxes pour out on to the floor
  const cx0 = 452, cy0 = 160, cw = 156, ch = 310;
  const rattle = tt > 4.6 && tt < 6.0 ? Math.sin(tt * 40) * (1 + k(tt, 4.6, 6.0) * 2) : 0;
  out.push(text(cx0 + cw / 2, cy0 - 24, 'CLOSET', 18, C.ink, { ls: 4, italic: false, weight: 700 }));
  out.push(`<rect x="${cx0 - 12}" y="${cy0 - 12}" width="${cw + 24}" height="${ch + 12}" fill="${C.choc}"/><rect x="${cx0}" y="${cy0}" width="${cw}" height="${ch}" fill="#2B1C14"/>`);
  // what's inside: packed to the top. When the door goes, the stack gives way, top rows first, and the boxes
  // pour out of their own places: arc, tumble, land with a squash, bounce once, a puff of dust
  const door = k(t, 6.0, 6.3, E.back);
  const slotXY = (row, col) => [cx0 + 8 + col * 48 + (row % 2) * 6 + 23, cy0 + ch - 48 - row * 44 + 23];
  const falling = [];
  for (let row = 6; row >= 2; row--) for (let col = 0; col < 3; col++) falling.push([row, col]);
  const pile = [[352, 0], [396, 0], [440, 0], [484, 0], [528, 0], [572, 0], [616, 0], [662, 0], [374, 1], [420, 1], [470, 1], [520, 1], [600, 1], [446, 2], [548, 2]];
  const startAt = i => 6.12 + i * 0.075 + (i > 8 ? 0.12 : 0);
  if (door >= 0.02) {
    const dw = lerp(cw, 30, door);
    // swung open against the wall: its foot stays on the floor line, so the boxes in front read as standing in front of it
    out.push(`<polygon points="${cx0},${cy0} ${f(cx0 - dw)},${f(cy0 - 22 * door)} ${f(cx0 - dw)},${cy0 + ch} ${cx0},${cy0 + ch}" fill="${C.teal}"/><rect x="${f(cx0 - dw)}" y="${f(cy0 - 22 * door)}" width="5" height="${f(ch + 22 * door)}" fill="#3E7C75"/><circle cx="${f(cx0 - dw + 12)}" cy="${cy0 + 150}" r="6" fill="${C.gold}"/>`);
  }
  if (door > 0) {
    for (let row = 0; row < 7; row++) for (let col = 0; col < 3; col++) {
      const fi = falling.findIndex(([r, c]) => r === row && c === col);
      if (fi >= 0 && tt >= startAt(fi)) continue;
      const [x, y] = slotXY(row, col);
      const lean = fi >= 0 && tt > startAt(fi) - 0.25 ? Math.sin(tt * 40 + fi) * 3 : 0;   // the ones about to go wobble
      out.push(`<g transform="translate(${f(x)} ${f(y)}) rotate(${((row * 3 + col) * 23) % 10 - 5 + lean})">${flatBox(46)}</g>`);
    }
  }
  falling.forEach(([row, col], i) => {
    const at = startAt(i), dur = 0.5 + (i % 3) * 0.07;
    if (tt < at) return;
    const p = clamp((tt - at) / dur);
    const [x0, y0] = slotXY(row, col), [px, layer] = pile[i];
    const s = 44, yl = 470 - s / 2 - layer * 40;
    const x = lerp(x0, px, E.out(p)), y = y0 + (yl - y0) * p * p - Math.sin(p * Math.PI) * (20 + row * 6);
    const rEnd = ((i * 53) % 30) - 15, side = px < x0 ? -1 : 1;
    let rot = rEnd * p + Math.sin(p * Math.PI) * 40 * side, sx = 1, sy = 1, hopY = 0;
    const tb = tt - at - dur;
    if (tb >= 0) {
      if (tb < 0.07) { sx = 1.14; sy = 0.84; }
      else if (tb < 0.35) hopY = -Math.abs(Math.sin((tb - 0.07) / 0.28 * Math.PI)) * 10 * (layer ? 0.5 : 1);
      if (tb < 0.45 && layer === 0) {
        const q = tb / 0.45;
        out.push([-1, 1].map(d => `<circle cx="${f(px + d * (16 + q * 22))}" cy="${f(468 - q * 8)}" r="${f(5 + q * 10)}" fill="#E9DCC0" opacity="${f(0.85 * (1 - q))}"/>`).join(''));
      }
    }
    out.push(`<g transform="translate(${f(x)} ${f(y + hopY + (s / 2) * (1 - sy))}) rotate(${f(rot)}) scale(${f(sx)} ${f(sy)})">${flatBox(s)}</g>`);
  });
  // the door, bulging and rattling, then swung open against the frame
  if (door < 0.02) {
    const bulge = k(tt, 4.6, 6.0) * 6;
    out.push(`<g transform="rotate(${f(rattle * 0.5)} ${cx0} ${cy0 + ch})"><path d="M${cx0} ${cy0} L${cx0 + cw} ${cy0} Q${f(cx0 + cw + bulge)} ${cy0 + ch / 2} ${cx0 + cw} ${cy0 + ch} L${cx0} ${cy0 + ch} Z" fill="${C.teal}"/>`
      + `<rect x="${cx0 + 14}" y="${cy0 + 16}" width="${cw - 28}" height="120" fill="none" stroke="#3E7C75" stroke-width="5"/><rect x="${cx0 + 14}" y="${cy0 + 160}" width="${cw - 28}" height="130" fill="none" stroke="#3E7C75" stroke-width="5"/>`
      + `<circle cx="${cx0 + cw - 18}" cy="${cy0 + 150}" r="7" fill="${C.gold}"/></g>`);
    if (tt > 5.2) out.push(`<rect x="${cx0 + cw - 2}" y="${cy0 + 20}" width="5" height="${ch - 40}" fill="#FFF4C8" opacity="${f(0.5 + 0.5 * Math.sin(tt * 30))}"/>`);
  }
  // FULL. stamped across the world
  const st = k(t, 9.2, 9.45, E.back);
  if (st > 0) out.push(`<g transform="translate(${gx} 290) rotate(-8) scale(${f(lerp(2.2, 1, st))})" opacity="${f(clamp(st * 2))}"><rect x="-120" y="-50" width="240" height="96" fill="${C.cream}" stroke="${C.orange}" stroke-width="10"/>${text(0, 30, 'FULL.', 78, C.orange, { ls: 4 })}</g>`);
  return out.join('');
});

// 3. behind the world, more world. The backrooms rise all round the room, then the camera pulls back:
// it goes on in every direction, off every edge of the picture
const MW = { W: 262, D: 258, H: 96 };
const BR_BACK_FLOORS = [[-2400, 2400, -2400, 0], [-2400, 0, 0, 2400]];
const BR_FRONT_FLOORS = [[0, 2400, MW.D, 2400], [MW.W, 2400, 0, MW.D]];
const BR_BACK_WALLS = [[-2400, 2400, -2400, -142], [-2400, -40, -30, MW.D + 10]];
const BR_FRONT_WALLS = [[-2400, 2400, MW.D + 60, 2400], [MW.W + 60, 2400, 120, MW.D + 10]];
function moreWorld(cam, holeP, rise, hall = () => '', front = () => '', press = 1) {
  const { W, D, H } = MW, hc = 147, hw = 35 * holeP, hz = 78 * holeP;
  const I = isoAt(W / 2, D / 2, 0, cam.sx, cam.sy, cam.s);
  const out = [BG];
  out.push(backrooms(I, BR_BACK_FLOORS.concat(BR_FRONT_FLOORS), BR_BACK_WALLS, H, rise, { fog: [cam.sx, cam.sy - 30] }));
  // the hall behind the hole is lit, so the way through glows
  if (holeP > 0.01) { const [gx, gy] = I.p(147, -60, 0); out.push(`<ellipse cx="${f(gx)}" cy="${f(gy)}" rx="${f(80 * I.s)}" ry="${f(40 * I.s)}" fill="#FFF6D0" opacity="${f(0.75 * holeP)}"/>`); }
  out.push(hall(I));
  out.push(room(I, W, D, '#D8C397', 14 * (1 - clamp(rise * 1.5))), wallX(I, D, H, '#EADBB5'), stripesX(I, 0, D, 62));
  out.push(I.xplane(0, 200, 88) + `<rect x="0" y="0" width="84" height="46" fill="${C.teal}"/>` + text(42, 29, 'OVERFLOW', 12, C.cream, { ls: 1 }) + '</g>');
  out.push(wallY(I, W, H, C.cream, holeP > 0.01 ? [hc - hw, hc + hw, hz] : null));
  out.push(holeP > 0.01 ? stripesY(I, 0, hc - hw, 62) + stripesY(I, hc + hw, W, 62) : stripesY(I, 0, W, 62));
  // the access button on the wall: a red button on a striped plate; lit once it has been pressed
  const lit = press > 0.5;
  out.push(I.yplane(204, 0, 64) + `<rect x="0" y="0" width="30" height="38" rx="3" fill="${C.ink}"/>`
    + [0, 1, 2, 3].map(i => `<rect x="${3 + i * 7}" y="3" width="4" height="6" fill="${C.gold}"/>`).join('')
    + (lit ? `<circle cx="15" cy="23" r="12" fill="#FF6A4A" opacity="0.45"/>` : '')
    + `<circle cx="15" cy="${f(23 + (press > 0 && press < 1 ? 1.5 : 0))}" r="8" fill="${lit ? '#FF5A3A' : '#C8321E'}"/><circle cx="12.5" cy="20.5" r="2.5" fill="#FFB8A8" opacity="0.8"/>`
    + text(15, 36, 'PUSH', 6, C.cream, { ls: 0.5 }) + '</g>');
  [[30, 160, 0, 48, 48, 38], [90, 190, 0, 48, 48, 38], [36, 164, 38, 42, 42, 34]].forEach(b => out.push(I.carton(...b)));
  // the wall block off to the right stands behind the hall in front of the room, so it goes down before what's in that
  // hall; the row nearest us goes over everything
  out.push(backrooms(I, [], [BR_FRONT_WALLS[1]], H, rise, { fog: [cam.sx, cam.sy - 30], pools: false }));
  out.push(front(I));
  out.push(backrooms(I, [], [BR_FRONT_WALLS[0]], H, rise, { fog: [cam.sx, cam.sy - 30], pools: false }));
  return { I, out };
}
const mwCam = pull => ({ sx: VW / 2, sy: lerp(318, 330, pull), s: lerp(1, 0.6, pull) });
scene('moreworld', 13, 'Here’s the solution. Behind the world, there’s more world. Hallways. Offices. Carpet as far as the eye can see. Nobody’s using it. (beat) So we are.', t => {
  const tt = two(t);
  const holeP = k(t, 3.9, 4.8, E.back);
  const pull = k(t, 6.8, 12.6, E.inOut);
  let px, po;
  const { I, out } = moreWorld(mwCam(pull), holeP, k(t, 4.3, 6.2, E.back), () => '', I => {
    // ripples spread out over the wall round the opening: on the wall, so behind the worker standing in front of it
    let rip = '';
    for (let r = 0; r < 3; r++) {
      const p = clamp((t - 3.6 - r * 0.25) / 1.4);
      if (p <= 0 || p >= 1) continue;
      const g = 10 + p * 70;
      rip += I.yplane(0, 0, 0) + `<rect x="${f(147 - 35 - g)}" y="${f(-78 - g)}" width="${f(70 + 2 * g)}" height="${f(78 + g)}" rx="${f(10 + g / 3)}" fill="none" stroke="${C.gold}" stroke-width="${f(5 * (1 - p) + 1)}" opacity="${f(1 - p)}"/></g>`;
    }
    // the worker walks up to the button, presses it, and jumps back as the wall opens
    if (tt < 3.0) { const p = tt / 3.0; px = I.p(lerp(200, 214, p), lerp(150, 22, p), 0); po = walkPose(tt * 9); }
    else if (tt < 3.9) { px = I.p(214, 22, 0); po = { aR: tt < 3.15 ? -60 : tt < 3.6 ? -100 : -40 }; }
    else { const q = k(tt, 3.9, 4.4, E.out); px = I.p(214 + 8 * q, 22 + 34 * q, 0); px = [px[0], px[1] - hop(clamp((tt - 3.9) / 0.5), 1) * 14]; po = tt < 5.4 ? { aL: 130, aR: -130 } : { aR: -20 }; }
    const [dx, dy] = I.p(222, 56, 0);
    return rip + picto(px[0], px[1], 0.42 * I.s, po) + dust(dx, dy, k(tt, 4.4, 4.8), I.s * 0.8);
  }, k(t, 3.3, 3.45));
  return out.join('');
});

// 4. now leasing: the same shot carries on (no cut in the place), drifting along the hall behind the room
scene('leasing', 12, 'Overflow rents that space to people who need it. Storage today. Apartments tomorrow. And you, my friend, are how the stuff gets there.', t => {
  const tt = two(t);
  const end = mwCam(1);
  const cam = { sx: end.sx - t * 7, sy: end.sy + t * 1.5, s: end.s + t * 0.004 };
  const { out } = moreWorld(cam, 1, 1, () => '', I => {
    const o = [];
    // the worker is still by the hole they opened; two more come through it after the goods, one points, one has the clipboard
    const [wx, wy] = I.p(222, 56, 0);
    o.push(picto(wx, wy, 0.42 * I.s, { aR: -20 }));
    const hole = [I.p(112, 0, 0), I.p(182, 0, 0), I.p(182, 0, 78), I.p(112, 0, 78)];
    [[0.8, 196, 74, 'a'], [1.8, 232, 116, 'b']].forEach(([at, x1, y1, id]) => {
      const p = k(tt, at, at + 1.6, E.lin);
      if (p <= 0) return;
      const x = lerp(147, x1, p), y = lerp(-46, y1, p);
      const [sx, sy] = I.p(x, y, 0);
      const po = p < 1 ? walkPose(tt * 9, 22) : id === 'a' ? { aR: tt > 6 ? -110 : -10 } : { aL: tt > 9 ? 120 + Math.sin(tt * 7) * 15 : 70, item: 'clip' };
      const fig = picto(sx, sy, 0.42 * I.s, { ...po, item: id === 'b' ? 'clip' : po.item, shadow: y > 0 });
      o.push(y < 4 ? `<defs>${clipPoly('hole' + id, hole)}</defs><g clip-path="url(#hole${id})">${fig}</g>` : fig);
    });
    // a line of goods hopping in single file along the open hall in front of the room: storage, then a home
    const kinds = ['c', 'c', 'c', 'sofa', 'c', 'lamp', 'c', 'c', 'bed', 'c'];
    const ly = MW.D + 22;
    for (let i = kinds.length - 1; i >= 0; i--) {
      const x = -420 + tt * 110 - i * 70;
      if (x < -700 || x > 1100) continue;
      const z = Math.abs(Math.sin((tt * 2.6 + i * 0.5) * Math.PI)) * 16;
      // a LEASED tag pops on to each thing as it passes the room
      const tg = k(x, 40, 90, E.back);
      let tagSvg = '';
      if (tg > 0) { const [tx, ty] = I.p(x + 17, ly + 17, z + 46); tagSvg = (`<g transform="translate(${f(tx)} ${f(ty)}) rotate(-8) scale(${f(tg * I.s * 1.2)})"><path d="M0 14 L0 6" stroke="${C.choc}" stroke-width="1.5"/><rect x="-22" y="-9" width="44" height="15" rx="2" fill="${C.orange}"/>${text(0, 2.5, 'LEASED', 8, C.cream, { ls: 1 })}</g>`); }
      if (kinds[i] === 'c') o.push(I.carton(x, ly, z, 34, 34, 28));
      if (kinds[i] === 'sofa') o.push(I.box(x, ly - 2, z, 54, 36, 14, '#E07A44', C.orange, '#A9471B'), I.box(x, ly - 2, z + 14, 54, 10, 18, '#E07A44', C.orange, '#A9471B'), I.box(x, ly - 2, z + 14, 8, 36, 10, '#E07A44', C.orange, '#A9471B'), I.box(x + 46, ly - 2, z + 14, 8, 36, 10, '#E07A44', C.orange, '#A9471B'));
      if (kinds[i] === 'lamp') o.push(I.box(x + 8, ly + 8, z, 18, 18, 3, '#4A3427', C.ink, '#3B281D'), I.box(x + 15, ly + 15, z + 3, 4, 4, 36, '#4A3427', C.ink, '#3B281D'), I.box(x + 5, ly + 5, z + 39, 24, 24, 14, '#FFF4C8', '#F2DE9A', '#D9C27A'));
      if (kinds[i] === 'bed') o.push(I.box(x, ly - 4, z, 60, 40, 12, '#E9DCC0', '#D2C4A4', '#BFB290'), I.box(x, ly - 4, z + 12, 60, 40, 6, C.avo, '#6A7633', '#56602A'), I.box(x, ly - 4, z + 12, 14, 40, 10, C.lite, '#EDE3C2', '#D9CFAE'));
      if (tagSvg) o.push(tagSvg);
    }
    return o.join('');
  });
  // the banner eases down into the frame from above on its ropes, swings a little and settles
  const dr = k(t, 0.3, 1.4, E.back);
  const bw = 520, bh = 118, bx = (VW - bw) / 2, by = lerp(-bh - 40, 46, dr);
  const sway = t > 1.4 ? Math.sin((t - 1.4) * 6) * 2.4 * Math.exp(-(t - 1.4) * 1.6) : 0;
  if (dr > 0) {
    out.push(`<g transform="rotate(${f(sway)} ${VW / 2} 0)">`);
    if (by > 0) out.push(`<path d="M${bx + 30} 0 L${bx + 30} ${f(by)} M${bx + bw - 30} 0 L${bx + bw - 30} ${f(by)}" stroke="${C.choc}" stroke-width="3"/>`);
    out.push(`<g transform="translate(${f(bx)} ${f(by)})"><rect x="-8" y="-8" width="${bw + 16}" height="10" rx="5" fill="${C.choc}"/>`
      + `<rect x="0" y="0" width="${bw}" height="${bh}" fill="${C.orange}"/>`
      + text(bw / 2, 64, 'NOW LEASING', 46, C.cream, { ls: 5 }) + text(bw / 2, 94, 'STORAGE · APARTMENTS · MORE', 14, C.cream, { weight: 700, italic: false, ls: 5 })
      + `<rect x="0" y="${bh - 4}" width="${bw}" height="6" fill="${C.gold}"/></g></g>`);
  }
  return out.join('');
});

// 5. seams: out in the backrooms, a thin patch of wall with the real world behind it. Boxley listens at it,
// then pushes a chute in, and the goods come through on to his cart.
scene('seams', 16, 'Out there, the walls get thin. We call those seams. Put your ear to one and you’ll hear a warehouse. A print shop. Somebody’s lunch break. Push a chute in, and the goods come to you.', t => {
  const tt = two(t);
  const W = 360, D = 330, H = 120;
  // push in on Boxley listening at the seam, back out for the chute
  const zin = k(t, 0.4, 4.6, E.inOut) * (1 - k(t, 5.5, 6.4, E.inOut));
  const I = isoAt(lerp(W / 2, 236, zin), lerp(D / 2, 40, zin), lerp(0, 40, zin), VW / 2, lerp(296, 300, zin), lerp(0.8, 1.15, zin));
  const out = [BG, yellowRoom(I, W, D, H)];
  // the seam: the wallpaper stretched over it bends and breathes; light flickers through a jagged crack and dust
  // drifts out. When the chute goes in, a ripple runs over the wall and the crack opens, elastic, on a warehouse.
  const sc = 190, zc = 68;
  const open = k(tt, 6.8, 7.8, E.back) + (tt > 7.8 ? Math.sin((tt - 7.8) * 9) * 0.08 * Math.exp(-(tt - 7.8) * 2.5) : 0);
  const br = Math.sin(t * 2.2) * 3 + Math.sin(t * 3.7) * 1.5;
  let blob = '';
  for (let i = 0; i < 28; i++) { const a = i / 28 * 2 * Math.PI, r = 1 + 0.05 * Math.sin(a * 3 + t * 2) + br * 0.012; blob += `${f(sc + Math.cos(a) * 58 * r)},${f(-zc + Math.sin(a) * 62 * r)} `; }
  let bent = '';
  for (let x = 8; x < 360; x += 16) {
    if (x < sc - 66 || x > sc + 66) continue;
    const dx = (x - sc) / 60, bend = dx * (12 + br * 2) * Math.max(0, 1 - Math.abs(dx) * 0.5);
    bent += `<path d="M${x + 1.5} ${-zc - 66} Q${f(x + 1.5 + bend * 2)} ${-zc} ${x + 1.5} ${-zc + 66}" fill="none" stroke="#E8D58A" stroke-width="3"/>`;
  }
  const crack = [];
  for (let i = 0; i <= 10; i++) { const zz = -zc - 46 + i * 9.2; crack.push(`${f(sc + (rnd(i * 7 + Math.floor(t * 12)) - 0.5) * 7 + Math.sin(i * 1.7) * 3)},${f(zz)}`); }
  const glow = 0.55 + 0.35 * rnd(Math.floor(t * 12) * 3.1);
  const half = 3 + 42 * clamp(open, 0, 1.2);
  const lens = `M${sc} -118 Q${f(sc + half)} -${zc} ${sc} -18 Q${f(sc - half)} -${zc} ${sc} -118 Z`;
  out.push(I.yplane(0, 0, 0)
    + `<defs><clipPath id="seamblob"><polygon points="${blob}"/></clipPath><clipPath id="seamclip"><path d="${lens}"/></clipPath></defs>`
    + `<polygon points="${blob}" fill="${C.ywall}"/><g clip-path="url(#seamblob)">${bent}</g>`
    + `<polygon points="${blob}" fill="#FFF6CC" opacity="${f(0.28 + 0.1 * Math.sin(t * 2.2))}"/>`
    + `<polyline points="${crack.join(' ')}" fill="none" stroke="#FFF4C8" stroke-width="9" opacity="${f(0.35 * glow)}" stroke-linejoin="round"/>`
    + `<polyline points="${crack.join(' ')}" fill="none" stroke="#FFFDF0" stroke-width="2.6" opacity="${f(glow)}" stroke-linejoin="round"/>`
    + (open > 0.03 ? `<path d="${lens}" fill="#FFF8DA" stroke="#FFF4C8" stroke-width="${f(5 + 2 * Math.sin(tt * 8))}"/>`
      + `<g clip-path="url(#seamclip)"><g transform="translate(${f(br * 0.6)} 0)"><rect x="${sc - 54}" y="-122" width="108" height="108" fill="#6F8C99"/><rect x="${sc - 54}" y="-40" width="108" height="26" fill="#5D7784"/>`
      + `<rect x="${sc - 40}" y="-122" width="5" height="104" fill="${C.orange}"/><rect x="${sc + 30}" y="-122" width="5" height="104" fill="${C.orange}"/>`
      + `<rect x="${sc - 40}" y="-84" width="75" height="4" fill="${C.gold}"/><rect x="${sc - 40}" y="-50" width="75" height="4" fill="${C.gold}"/>`
      + `<rect x="${sc - 32}" y="-102" width="16" height="18" fill="${C.cfront}"/><rect x="${sc - 12}" y="-98" width="14" height="14" fill="${C.ctop}"/><rect x="${sc + 8}" y="-68" width="18" height="18" fill="${C.cfront}"/><rect x="${sc - 30}" y="-66" width="14" height="16" fill="${C.ctop}"/>`
      + `<circle cx="${sc + 18}" cy="-108" r="3.5" fill="#FFB43A" opacity="${f(Math.floor(t * 3) % 2 ? 1 : 0.25)}"/></g></g>` : '')
    + `</g>`);
  // the ripple when the chute touches the wall
  const rip = k(tt, 6.6, 7.4, E.out);
  if (rip > 0 && rip < 1) out.push(I.yplane(0, 0, 0) + `<ellipse cx="${sc}" cy="${-zc}" rx="${f(40 + rip * 100)}" ry="${f(44 + rip * 60)}" fill="none" stroke="#FFF4C8" stroke-width="${f(5 * (1 - rip) + 1)}" opacity="${f(1 - rip)}"/></g>`);
  // dust drifting out through the crack
  const [sx, sy] = I.p(sc, 0, zc);
  for (let i = 0; i < 9; i++) {
    const q = (t * 0.35 + i / 9) % 1;
    out.push(`<circle cx="${f(sx + (rnd(i * 3.3) - 0.5) * 26 - q * 46)}" cy="${f(sy + (rnd(i * 5.1) - 0.5) * 70 + q * 24)}" r="${f(1.2 + rnd(i) * 1.4)}" fill="#FFF8DA" opacity="${f(Math.sin(q * Math.PI) * 0.8)}"/>`);
  }
  // the sound coming through
  for (let r = 0; r < 3; r++) {
    const q = ((t * 0.7 + r / 3) % 1);
    out.push(`<path d="M${f(sx + 10 + q * 30)} ${f(sy - 30 - q * 40)} A${f(30 + q * 70)} ${f(40 + q * 80)} 0 0 1 ${f(sx + 10 + q * 30)} ${f(sy + 30 + q * 40)}" fill="none" stroke="${C.gold}" stroke-width="${f(5 - 3 * q)}" opacity="${f(0.8 * (1 - q) * clamp(1 - open))}"/>`);
  }
  [['BEEP', 2.5], ['KA-CHING', 4.0], ['MUNCH', 5.2]].forEach(([w, at]) => {
    const q = k(t, at, at + 1.5, E.out);
    if (q <= 0 || q >= 1) return;
    out.push(`<g opacity="${f(1 - q * q)}" transform="translate(${f(sx - 40 - q * 60)} ${f(sy - 50 - q * 60)}) rotate(-10)">${text(0, 0, w, 20, C.orange, { ls: 2 })}</g>`);
  });
  // the chute: its high end goes into the seam, its low end over the cart
  const push = k(tt, 6.6, 8.4, E.inOut);
  const cx0 = 162, cw = 56, len = 140;
  const ya = lerp(50, -6, push), yb = ya + len, za = 70, zb = 40;
  const chute = (z0, z1, top, side) => I.poly([[cx0, ya, za + z0], [cx0 + cw, ya, za + z0], [cx0 + cw, yb, zb + z0], [cx0, yb, zb + z0]], top)
    + I.poly([[cx0 + cw, ya, za + z0], [cx0 + cw, yb, zb + z0], [cx0 + cw, yb, zb + z1], [cx0 + cw, ya, za + z1]], side);
  [[cx0 + 4, yb - 10], [cx0 + cw - 10, yb - 10]].forEach(([x, y]) => out.push(I.box(x, y, 0, 6, 6, zb, '#5A4334', C.ink, '#3B281D')));
  out.push(chute(0, -5, '#C9C2B0', '#8A8473'));
  out.push(I.poly([[cx0 - 3, ya, za], [cx0, ya, za], [cx0, yb, zb], [cx0 - 3, yb, zb]], '#A39C8A') + I.poly([[cx0 - 3, ya, za + 14], [cx0, ya, za + 14], [cx0, yb, zb + 14], [cx0 - 3, yb, zb + 14]], '#A39C8A'));
  out.push(cart(I, cx0 - 20, yb + 4, 0, 100, 70));
  // cartons come out of the seam, slide down the chute and drop on to the cart, stacking
  const slots = [[cx0 - 12, yb + 14, 18], [cx0 + 32, yb + 14, 18], [cx0 - 12, yb + 14, 46], [cx0 + 32, yb + 14, 46], [cx0 + 10, yb + 14, 74]];
  for (let i = 0; i < 5; i++) {
    const at = 9.0 + i * 1.1;
    const p = k(tt, at, at + 0.75, E.in), drop = k(tt, at + 0.75, at + 1.0, E.bounce);
    if (p <= 0) continue;
    const [sx2, sy2, sz2] = slots[i];
    if (drop <= 0) out.push(I.carton(cx0 + 11, lerp(ya, yb - 30, p), lerp(za, zb, p) + 1, 34, 30, 26));
    else out.push(I.carton(lerp(cx0 + 11, sx2, drop), lerp(yb - 30, sy2, drop), lerp(zb + 1, sz2, drop), 34, 30, 26));
  }
  // Boxley: at the wall from the start, a hand cupped to his head, listening and reacting; then round to the
  // back of the cart, pushes it all in, and steps aside to watch
  const S = 48;
  let bxp, byp, face = 'y', look = -0.8, eyes = 'open', mouth = 'smile', armL = 160, armR = 20, stride = null, hatHop = 0;
  const bump = at => (tt > at && tt < at + 0.25 ? -6 : 0);
  if (tt < 5.8) {
    bxp = 250; byp = 8;
    if (tt > 2.5 && tt < 3.6) { eyes = 'wide'; mouth = 'o'; }
    else if (tt > 4.0 && tt < 5.0) { mouth = 'grin'; }
    else if (tt > 5.2) { mouth = 'wavy'; look = -0.3; }
    hatHop = bump(2.5) + bump(4.0) + bump(5.2);
  } else if (tt < 6.3) { const p = k(tt, 5.8, 6.3, E.lin); bxp = 250; byp = lerp(8, 262, p); stride = tt * 14; armL = 30; look = 0; }
  else if (tt < 6.6) { const p = k(tt, 6.3, 6.6, E.lin); bxp = lerp(250, 165, p); byp = 262; stride = tt * 14; armL = 30; face = '-x'; }
  else if (tt < 8.4) { bxp = 165; byp = yb + 72; face = '-y'; armL = 95; armR = 95; stride = tt * 10; }
  else { const p = k(tt, 8.4, 9.2, E.out); bxp = lerp(165, 290, p); byp = lerp(yb + 72, 232, p); stride = p < 1 ? tt * 10 : null; armL = 20; look = tt > 9.4 ? -0.7 : 0; eyes = tt > 10 && tt < 10.2 ? 'closed' : 'open'; mouth = tt > 12 ? 'grin' : 'smile'; }
  out.push(boxley(I, bxp, byp, S, { face, look, eyes, mouth, armL, armR, stride, hatHop }));
  return out.join('');
});

// 6. read, do, collect
scene('contracts', 20, 'Every day, the board has contracts. Store this. Ship that. Furnish these. You read it, you do it, you come back here and collect your pay at the window. Read. Do. Collect. Even an intern can remember three words. (beat) Our last intern remembered two.', t => {
  const tt = two(t);
  if (t >= 1.0 && t < 3.2) {
    // close on the board: one contract card, read, and stamped
    const out = [`<rect width="${VW}" height="${VH}" fill="#B98A55"/>`];
    for (let i = 0; i < 120; i++) out.push(`<circle cx="${f(rnd(i * 3.1) * VW)}" cy="${f(rnd(i * 7.7) * VH)}" r="${f(1 + rnd(i) * 2.2)}" fill="#A27744" opacity="0.6"/>`);
    [[-70, 40, -6], [610, 70, 5], [20, 400, 4], [580, 390, -4]].forEach(([x, y, r]) => out.push(`<g transform="translate(${x} ${y}) rotate(${r})"><rect width="170" height="210" fill="${C.lite}"/><rect x="20" y="30" width="120" height="6" fill="${C.ink}" opacity="0.5"/><rect x="20" y="50" width="90" height="6" fill="${C.ink}" opacity="0.5"/><rect x="20" y="70" width="110" height="6" fill="${C.ink}" opacity="0.5"/><circle cx="85" cy="12" r="7" fill="${C.orange}"/></g>`));
    const zin = lerp(1.1, 1, k(t, 1.0, 1.45, E.out)) + 0.05 * k(t, 1.45, 3.2, E.inOut), sway = Math.sin(t * 5) * 0.6;
    const line = (y, a, b) => text(-120, y, a, 22, C.ink, { mono: true, italic: false, weight: 400, ls: 1, anchor: 'start' }) + text(-10, y, b, 24, C.ink, { mono: true, italic: false, weight: 400, ls: 1, anchor: 'start' });
    let card = `<rect x="-150" y="-185" width="300" height="370" fill="${C.lite}"/><rect x="-150" y="-185" width="300" height="56" fill="${C.teal}"/>`
      + text(0, -147, 'CONTRACT', 26, C.cream, { ls: 4 }) + stripes(-150, -129, 300, 5, 2)
      + line(-80, 'No.', '0417') + line(-40, 'STORE', '40 BOXES') + line(0, 'WHERE', 'LEVEL 0 · BAY C') + line(40, 'BY', 'CLOSING') + line(80, 'PAY', '120 CR')
      + `<circle cx="0" cy="-196" r="11" fill="${C.orange}"/><circle cx="-3" cy="-199" r="3.5" fill="#F4A77E"/>`;
    const st = k(t, 1.6, 1.75, E.back);
    if (st > 0) card += `<g transform="translate(40 136) rotate(-12) scale(${f(lerp(2, 1, st))})" opacity="${f(clamp(st * 2))}"><rect x="-82" y="-24" width="164" height="44" fill="none" stroke="${C.orange}" stroke-width="5"/>${text(0, 10, 'APPROVED', 26, C.orange, { ls: 3 })}</g>`;
    out.push(`<g transform="translate(${VW / 2} ${VH / 2 + 6}) rotate(${f(-2 + sway)}) scale(${f(zin)})">${card}</g>`);
    return out.join('');
  }
  if (t < 10.5) {
    const W = 360, D = 330, H = 112;
    const I = roomCam(W, D);
    const out = [BG, officeRoom(I, W, D, H, { sz: 30, floor: '#E9DDBE' })];
    out.push(rug(I, 150, 150, 300, 290), hazard(I, 226, 326, 50), lockers(I, 284, 2), cooler(I, 318, 130), wallClock(I, 120, 92));
    // the contract board on the left wall
    out.push(I.xplane(0, 260, 104) + `<rect x="0" y="0" width="150" height="70" fill="#B98A55"/><rect x="0" y="0" width="150" height="70" fill="none" stroke="${C.choc}" stroke-width="5"/>`);
    [[10, 8, -3], [44, 12, 4], [80, 6, -2], [112, 14, 3]].forEach(([x, y, r], i) => {
      const flutter = Math.sin(tt * 6 + i) * 2;
      out.push(`<g transform="rotate(${r + flutter} ${x + 14} ${y})"><rect x="${x}" y="${y}" width="28" height="36" fill="${C.lite}"/><rect x="${x + 4}" y="${y + 8}" width="20" height="2" fill="${C.ink}"/><rect x="${x + 4}" y="${y + 14}" width="16" height="2" fill="${C.ink}"/><rect x="${x + 4}" y="${y + 20}" width="18" height="2" fill="${C.ink}"/><circle cx="${x + 14}" cy="${y + 3}" r="3" fill="${C.orange}"/></g>`);
    });
    out.push(text(75, -6, 'CONTRACTS', 13, C.ink, { ls: 2 }) + '</g>');
    // the pay window on the right wall
    out.push(I.yplane(230, 0, 92) + `<rect x="0" y="0" width="90" height="62" fill="${C.choc}"/><rect x="8" y="8" width="74" height="46" fill="#FFF4C8"/><rect x="0" y="-22" width="90" height="18" fill="${C.orange}"/>` + text(45, -8, 'PAY', 13, C.cream, { ls: 3 }) + '</g>');
    out.push(I.box(226, 0, 30, 98, 18, 6, '#E9DCC0', '#D2C4A4', '#BFB290'));
    // the worker reads the board, takes the cart by its handle and pushes it over to the pay window;
    // an arm comes out of the window with the envelope and he takes it
    const cartX = lerp(80, 150, k(tt, 4.0, 6.4, E.inOut)), cartY = 44;
    let wx, wy, po = {}, mirror = true;
    if (tt < 3.3) { wx = 40; wy = 190; mirror = false; po = { aL: tt > 0.8 ? 150 + Math.sin(tt * 8) * 6 : 0, headX: -3 }; }
    else if (tt < 4.0) { const p = k(tt, 3.3, 4.0, E.inOut); wx = lerp(40, 66, p); wy = lerp(190, 72, p); po = walkPose(tt * 9, 22); }
    else if (tt < 6.4) { wx = cartX - 14; wy = 72; po = { ...walkPose(tt * 9, 20), aL: 80, aR: 80 }; }
    else if (tt < 6.8) { const p = k(tt, 6.4, 6.8, E.inOut); wx = 136; wy = lerp(72, 24, p); po = walkPose(tt * 9, 20); }
    else if (tt < 7.4) { const p = k(tt, 6.8, 7.4, E.inOut); wx = lerp(136, 246, p); wy = 24; po = walkPose(tt * 9, 20); }
    else { wx = 246; wy = 24; po = tt < 7.8 ? { aR: -95 } : { aR: tt < 8.1 ? -95 : -150 + Math.sin(tt * 10) * 6, item: 'envelope', bob: tt > 8.1 && tt < 8.4 ? -10 : 0 }; }
    const [px, py] = I.p(wx, wy, 0);
    out.push(picto(px, py, 0.42 * I.s, { ...po, mirror }));
    out.push(cart(I, cartX, cartY, 0, 90, 56));
    [[cartX + 6, cartY + 8, 30], [cartX + 42, cartY + 8, 30], [cartX + 10, cartY + 10, 60]].forEach(([x, y, z]) => out.push(I.carton(x, y, z - 12, 30, 30, 28)));
    const reach = k(tt, 7.4, 7.8, E.out) * (1 - k(tt, 7.9, 8.2, E.in));
    if (reach > 0) {
      const a = I.p(276, 2, 50), b = I.p(258, 16, 56);
      const e = [lerp(a[0], b[0], reach), lerp(a[1], b[1], reach)];
      out.push(stroke([a, e], 6 * I.s));
      if (tt < 7.8) out.push(`<g transform="translate(${f(e[0])} ${f(e[1])}) rotate(-12)"><rect x="-9" y="-7" width="18" height="12" fill="${C.lite}"/><path d="M-9 -7 L0 0 L9 -7" fill="none" stroke="${C.cside}" stroke-width="1.5"/></g>`);
    }
    out.push(plant(I, 326, 296));
    return out.join('');
  }
  // kinetic type
  const out = [BG, stripes(0, 470, VW, 10, 4)];
  const words = [['READ.', 150, 11.2, C.teal], ['DO.', 265, 11.9, C.orange], ['COLLECT.', 380, 12.6, C.avo]];
  words.forEach(([w, y, at, c], i) => {
    const p = k(t, at, at + 0.3, E.back);
    if (p <= 0) return;
    let dy = 0, rot = 0;
    if (i === 2 && t > 17.6) { const fall = k(t, 17.6, 18.4, E.in); dy = fall * 300; rot = fall * 28; }
    const ul = k(t, at + 0.2, at + 0.45, E.out), ww = w.length * 54;
    out.push(`<g transform="translate(300 ${f(y + dy)}) rotate(${f(rot)})">` + (ul > 0 ? stripes(-ww / 2 - 10, 14, ww * ul, 4, 2) : '') + `<g transform="scale(${f(lerp(1.8, 1, p))})" opacity="${f(clamp(p * 1.5))}">${text(0, 0, w, 88, c, { ls: 2 })}</g></g>`);
  });
  // the intern, scratching their hard hat
  const ip = k(tt, 15.0, 15.5, E.back);
  if (ip > 0) out.push(picto(618, 470 + 160 * (1 - ip), 0.8, { aR: -150 + Math.sin(tt * 14) * 10, headX: 4 }));
  return out.join('');
});

// 7. the closing bell: the worker gets out through the hub door, the lights go out bank by bank, and then in three
// short flickers we see the boxes have moved in the dark, closer each time
scene('closing', 18, 'Office hours. The building keeps them, so you will too. When the closing bell rings, the lights go down and the building enters what Legal calls its “low-activity period.” Be back before then. (flat) Low activity is not the same as no activity.', t => {
  const tt = two(t);
  const W = 360, D = 340, H = 112;
  const I = roomCam(W, D);
  const out = [BG, officeRoom(I, W, D, H, { sz: 40 })];
  // three banks of ceiling light, as pools on the floor: far, middle, near. Off one by one.
  const offAt = [10.2, 11.0, 11.8];
  // three flickers, close together, each with a little dropout as the tubes catch; the dark between is short
  const FL = [[12.2, 12.85], [13.9, 14.55], [15.7, 16.5]];
  const flick = FL.some(([a, b]) => t > a && t < b && !(t > a + 0.1 && t < a + 0.17));
  const pools = [[280, 90], [190, 180], [90, 270]];
  pools.forEach(([x, y], i) => {
    if (t >= offAt[i] && !flick) return;
    const [cx, cy] = I.p(x, y, 0);
    out.push(`<ellipse cx="${f(cx)}" cy="${f(cy)}" rx="${f(120 * I.s)}" ry="${f(60 * I.s)}" fill="url(#pool)"/>`);
  });
  out.push(lockers(I, 290, 2));
  const doorOpen = k(t, 3.9, 4.2) * (1 - k(t, 5.2, 5.6));
  const door = hubDoor(I, 240, doorOpen);
  out.push(door.svg, plant(I, 196, 12));   // along the wall, clear of the door and its mat
  // the clock: the minute hand sweeps up to five o'clock, and the bell beside it rings
  const ring = t > 2.0 && t < 4.6;
  out.push(I.xplane(0, 230, 96));
  const minute = lerp(-30, 0, k(t, 0, 2.0, E.inOut));
  out.push(`<g transform="translate(66 40)">${clockFace(32, minute, 150 + minute / 12, two(t) * 360)}</g>`);
  // the electric bell: a brass dome on a dark plate, its little hammer a blur while it rings
  const hm = ring ? Math.sin(t * 90) * 28 : 0, bj = ring ? Math.sin(t * 70) * 1.2 : 0;
  out.push(`<g transform="translate(${f(128 + bj)} 34)"><rect x="-10" y="-22" width="20" height="16" rx="3" fill="${C.choc}"/><rect x="-2" y="-8" width="4" height="8" fill="${C.ink}"/>`
    + `<circle r="17" fill="#B8862E"/><circle r="17" fill="${C.gold}" transform="translate(-1.5 -1.5) scale(0.9)"/><circle r="6" fill="#B8862E"/><ellipse cx="-6" cy="-7" rx="5" ry="3" fill="#FFF1C4" opacity="0.8"/>`
    + `<g transform="rotate(${f(hm)} 0 30)"><rect x="-1.5" y="14" width="3" height="16" fill="${C.ink}"/><circle cx="0" cy="13" r="4" fill="${C.ink}"/></g><rect x="-8" y="28" width="16" height="6" rx="2" fill="${C.choc}"/></g>`);
  if (ring) [1, 2].forEach(i => out.push(`<path d="M${128 - 20 - i * 7} ${34 - 10 - i * 3} Q${128 - 26 - i * 9} 34 ${128 - 20 - i * 7} ${34 + 10 + i * 3} M${128 + 20 + i * 7} ${34 - 10 - i * 3} Q${128 + 26 + i * 9} 34 ${128 + 20 + i * 7} ${34 + 10 + i * 3}" fill="none" stroke="${C.ink}" stroke-width="2.5" stroke-linecap="round" opacity="${f(Math.floor(t * 12) % 2 ? 1 : 0.4)}"/>`));
  out.push('</g>');
  // the cartons: where they were left, then (unseen, in the dark) somewhere else, then closer. Each flicker shows
  // the clean patch where they stood and the drag marks across the floor to where they are now.
  const PS = [[[60, 60], [150, 250]], [[150, 120], [200, 200]], [[250, 200], [180, 262]], [[296, 270], [222, 282]]];
  const stage = t < 12.0 ? 0 : t < 13.6 ? 1 : t < 15.4 ? 2 : 3;
  const lines = (A, B) => A.map(([x0, y0], i) => { const [x1, y1] = B[i]; return [8, 28, 48].map(o => stroke([I.p(x0 + o, y0 + 28, 0.2), I.p(x1 + o, y1 + 28, 0.2)], 3.2 * I.s, '#A88F66')).join(''); }).join('');
  const patch = A => A.map(([x0, y0]) => I.poly([[x0, y0, 0.15], [x0 + 56, y0, 0.15], [x0 + 56, y0 + 56, 0.15], [x0, y0 + 56, 0.15]], '#E6D8B4', ` stroke="#FFF4C8" stroke-width="2" stroke-dasharray="5 4"`)).join('');
  for (let i = 1; i <= stage; i++) out.push(lines(PS[i - 1], PS[i]));
  if (stage > 0) out.push(patch(PS[stage - 1]));
  const at = PS[stage];
  // drawn back to front, so the nearer box always covers the further one
  [[at[0][0], at[0][1], 0], [at[0][0], at[0][1], 44], [at[1][0], at[1][1], 0]].sort((p, q) => (p[0] + p[1]) - (q[0] + q[1]) || p[2] - q[2])
    .forEach(([x, y, z]) => out.push(I.carton(x, y, z, 56, 56, 44)));
  // the worker: waits, jumps at the bell, looks at the clock, winds up on the spot, then zips across and out
  // through the hub door (clipped to the doorway, so he disappears into the light)
  if (t < 5.0) {
    const sc = 0.42 * I.s;
    const zip = k(tt, 3.7, 4.4, E.in), into = k(tt, 4.4, 4.8, E.lin);
    const wx = into > 0 ? 268 : lerp(230, 268, zip), wy = into > 0 ? lerp(24, -40, into) : lerp(190, 24, zip);
    const [px, py] = I.p(wx, wy, 0);
    let po, mirror = true, ex = '';
    if (tt < 2.1) { po = { lR: Math.floor(tt * 3) % 2 ? -14 : 0, headX: 2 }; mirror = false; }
    else if (tt < 2.6) { po = { aL: 160, aR: -160, bob: -34 * hop(tt - 2.1, 0.5) }; mirror = false; ex = mark(px, py - 92 * I.s, '!'); }
    else if (tt < 3.2) { po = { headX: -6 }; mirror = false; }
    else if (tt < 3.7) {
      po = { ...runPose(tt * 44), lean: 12, headX: 4 };
      for (let i = 0; i < 3; i++) { const q = ((tt * 3 + i / 3) % 1); ex += `<circle cx="${f(px - (16 + q * 30) * I.s)}" cy="${f(py - q * 10)}" r="${f((5 + q * 9) * I.s)}" fill="#E9DCC0" opacity="${f(0.9 * (1 - q))}"/>`; }
    }
    else po = runPose(tt * 30);
    const fig = picto(px, py, sc, { ...po, mirror, shadow: wy > 0 });
    // dust left where he stood, and speed lines while he runs
    const puff = k(tt, 3.7, 4.6, E.out);
    if (puff > 0 && puff < 1) {
      const [dx, dy] = I.p(230, 190, 0);
      out.push([0, 1, 2].map(i => `<circle cx="${f(dx + (i - 1) * 18 * I.s)}" cy="${f(dy - 6 - i % 2 * 8)}" r="${f((10 + puff * 16) * I.s)}" fill="#E9DCC0" opacity="${f(0.85 * (1 - puff))}"/>`).join(''));
    }
    { const [dx, dy] = I.p(230, 190, 0); out.push(dust(dx, dy, k(tt, 2.55, 2.95), I.s * 0.8)); }
    if (wy < 4) out.push(`<defs>${clipPoly('doorclip', door.clip)}</defs><g clip-path="url(#doorclip)">${fig}</g>`);
    else out.push(fig);
    if (zip > 0 && into <= 0) [0, 1, 2].forEach(i => out.push(stroke([[px - (40 + i * 6) * I.s, py - (30 + i * 16) * I.s + 14 * I.s], [px - (90 + i * 10) * I.s, py - (14 + i * 16) * I.s + 14 * I.s]], 3, 'rgba(46,31,23,0.5)')));
    out.push(ex);
  }
  // the dark: a step darker with each bank that goes off; the flickers bring the light back for a moment
  const dark = flick ? 0.1 : 0.32 * (t >= offAt[0]) + 0.28 * (t >= offAt[1]) + 0.28 * (t >= offAt[2]);
  if (dark > 0) out.push(`<rect width="${VW}" height="${VH}" fill="#140C08" opacity="${f(dark)}"/>`);
  return out.join('');
});

// 8. a natural phenomenon
scene('phenomenon', 12, 'You may notice things have moved. Lights off that you left on. Boxes on the floor. That is not a ghost. That is a natural phenomenon, and it is not covered by your contract.', t => {
  const tt = two(t);
  const W = 360, D = 340, H = 112;
  // in on the worker's double-take, out again for the stamp
  const zin = k(t, 4.5, 5.3, E.inOut) * (1 - k(t, 7.6, 8.3, E.inOut));
  const I = isoAt(lerp(W / 2, 250, zin), lerp(D / 2, 230, zin), lerp(0, 30, zin), VW / 2, lerp(300, 292, zin), lerp(0.8, 1.05, zin));
  const out = [BG, officeRoom(I, W, D, H, { sz: 40 })];
  out.push(lockers(I, 290, 2), wallClock(I, 150, 92));
  // a floor lamp, on, then off by itself
  const lampOn = t < 2.2;
  if (lampOn) out.push(I.poly([[30, 40, 0.2], [130, 40, 0.2], [130, 140, 0.2], [30, 140, 0.2]], '#FFF4C8', ' opacity="0.6"'));
  out.push(I.box(64, 74, 0, 32, 32, 4, '#4A3427', C.ink, '#3B281D'));
  out.push(I.box(77, 87, 4, 6, 6, 80, '#4A3427', C.ink, '#3B281D'));
  {
    const [sx, sy] = I.p(80, 90, 98), [bx2, by2] = I.p(80, 90, 82), s = I.s;
    const sh = lampOn ? '#FFF4C8' : '#B9A97F', band = lampOn ? '#F2DE9A' : '#9A8B62';
    out.push(`<path d="M${f(sx - 26 * s)} ${f(sy)} L${f(bx2 - 32 * s)} ${f(by2)} A${f(32 * s)} ${f(16 * s)} 0 0 0 ${f(bx2 + 32 * s)} ${f(by2)} L${f(sx + 26 * s)} ${f(sy)} Z" fill="${band}"/><ellipse cx="${f(sx)}" cy="${f(sy)}" rx="${f(26 * s)}" ry="${f(13 * s)}" fill="${sh}"/>`);
  }
  // a stack: the top carton slides off and drops
  out.push(I.carton(220, 60, 0, 56, 56, 44), I.carton(220, 60, 44, 56, 56, 44));
  const sl = k(t, 4.0, 4.6, E.in), fall = k(t, 4.6, 5.1, E.bounce);
  out.push(I.carton(220 + sl * 50 + fall * 20, 60 + sl * 10, 88 - fall * 88, 52, 52, 40));
  out.push(cooler(I, 318, 136));   // in front of the stack, so drawn after it
  // one of the crew in the foreground: jumps when the lamp goes, spins round when the box drops, then looks about
  {
    const [wx, wy] = I.p(256, 262, 0), sc = 0.62 * I.s;
    let po = {}, mirror = false, m = '';
    if (tt > 2.2 && tt < 3.4) { po = { aL: 150, aR: -150, bob: -16 * hop(tt - 2.2, 0.4), headX: -5 }; m = '!'; }
    else if (tt > 4.6 && tt < 6.4) { po = { aL: 60, aR: -60, headX: 5 }; mirror = true; m = tt < 5.4 ? '!' : '?'; }
    else if (tt > 6.4 && tt < 8) { mirror = Math.sin(tt * 5) > 0; po = { headX: 4 }; }
    out.push(dust(wx, wy, k(tt, 3.3, 3.7), I.s), picto(wx, wy, sc, { ...po, mirror }));
    if (m) out.push(mark(wx, wy - 140 * I.s, m, 34));
  }
  out.push(plant(I, 322, 300));
  if (t > 1.0) out.push(`<rect width="${VW}" height="${VH}" fill="${C.ink}" opacity="${f(0.18 * k(t, 2.2, 2.6))}"/>`);
  // the stamp
  const st = k(t, 8.4, 8.65, E.back);
  if (st > 0) out.push(`<g transform="translate(${VW / 2} 118) rotate(-6) scale(${f(lerp(2.4, 1, st))})" opacity="${f(clamp(st * 2))}"><rect x="-290" y="-44" width="580" height="80" fill="${C.cream}" stroke="${C.orange}" stroke-width="8"/>${text(0, 13, 'NATURAL PHENOMENON', 36, C.orange, { ls: 2 })}</g>`);
  const st2 = k(t, 10.2, 10.45, E.back);
  if (st2 > 0) out.push(`<g transform="translate(${VW / 2} 190) rotate(-6) scale(${f(lerp(2, 1, st2))})" opacity="${f(clamp(st2 * 2))}"><rect x="-230" y="-24" width="460" height="36" fill="${C.cream}"/>${text(0, 0, 'NOT COVERED BY YOUR CONTRACT', 20, C.ink, { weight: 700, ls: 3, italic: false })}</g>`);
  return out.join('');
});

// 9. returned: a worker is knocked out on the job; they come back down the RETURNS chute into the recovery bay,
// get stickered, the counter goes back to 0, and they are sent straight back out to fetch what they dropped
scene('returned', 14, 'Should you lose consciousness on the job, Overflow will return you to the hub, free of charge. (quick, low) If we can find your body. Whatever you were carrying stays where you dropped it. Go and get it. (beat) That’s company property.', t => {
  const tt = two(t);
  const W = 360, D = 340, H = 112;
  let I = roomCam(W, D);
  let sc = 0.46 * I.s;
  if (t < 5.0) {
    I = isoAt(170, 192, 20, VW / 2, 320, 1.55);   // close in, so the trip reads
    sc = 0.46 * I.s;
    const out = [BG, officeRoom(I, W, D, H, { sz: 40 })];
    out.push(lockers(I, 290, 2), plant(I, 326, 30), wallClock(I, 200, 92));
    out.push(`<rect width="${VW}" height="${VH}" fill="${C.ink}" opacity="0.15"/>`);
    // carrying a stack too tall to see over... a box left on the floor... the toe catches it, a held beat, a stagger
    // with the arms flung out, then down flat; one box lands ahead, the other comes down on the head, the hat rolls off
    const CATCH = 1.5, PITCH = 1.62, LAND = 2.2, BONK = 2.45;
    const walkX = lerp(30, 112, clamp(tt / CATCH));
    const stag = k(tt, PITCH, 1.95, E.out), slide = k(t, 1.95, LAND, E.in);
    const fx = walkX + 14 * stag + 6 * slide;                      // where the feet are
    const rot = tt < PITCH ? 0 : t < 1.95 ? 38 * stag : t < LAND ? lerp(38, 118, slide) : 116 - (t < LAND + 0.15 ? Math.sin((t - LAND) / 0.15 * Math.PI) * 6 : 0);
    const fbx = 138 + 9 * k(t, CATCH, CATCH + 0.15, E.out);       // the box on the floor, shoved by the foot
    const headW = [fx + 0.894 * 168 * sc / I.s, 190];               // the head, in the world, once they are down
    const [px, py] = I.p(fx, 190, 0);
    out.push(I.carton(fbx, 176, 0, 30, 30, 22));
    let fig;
    if (tt < CATCH) fig = picto(px, py, sc, { ...walkPose(tt * 9, 18), aL: 80, aR: 80, mirror: true });
    else if (tt < PITCH) fig = picto(px, py, sc, { lL: -30, lR: 18, aL: 80, aR: 80, bob: -3, lean: 6, mirror: true });   // the catch
    else {
      const flail = t < LAND ? Math.sin(t * 40) * 16 : 0;
      const arms = t < LAND ? { aL: lerp(80, 160, stag) + flail, aR: lerp(80, 175, stag) - flail } : { aL: 165, aR: 175 };
      const r = rot / 116;
      fig = `<ellipse cx="${f(px + 70 * sc * r)}" cy="${f(py + 18 * sc * r)}" rx="${f((40 + 60 * r) * sc)}" ry="${f(12 * sc)}" fill="rgba(46,31,23,0.18)"/>`
        + lying(px, py, sc, rot, { ...arms, lL: t < LAND ? -40 * (1 - slide) : -6, lR: t < LAND ? 30 * (1 - slide) : 4, mirror: true, noHat: t >= LAND });
    }
    // the carried boxes: in the arms, then thrown forward off them
    const fly = (from, to, a, b, arc) => { const q = clamp((t - a) / (b - a)); return [lerp(from[0], to[0], q), lerp(from[1], to[1], q), lerp(from[2], to[2], q) + Math.sin(q * Math.PI) * arc]; };
    const items = [];
    if (tt < PITCH) items.push([walkX + 8, 176, 34, 30, 30, 26], [walkX + 10, 177, 60, 28, 28, 24]);
    else {
      const from = walkX + 14 * stag;
      const A = fly([from + 8, 176, 34], [244, 198, 0], PITCH, 2.35, 46);
      const aBounce = t > 2.35 && t < 2.6 ? Math.abs(Math.sin(clamp((t - 2.35) / 0.25) * Math.PI)) * 8 : 0;
      items.push([A[0], A[1], A[2] + aBounce, 30, 30, 26]);
      const B = t < BONK ? fly([from + 10, 177, 60], [headW[0] - 12, 178, 6], PITCH, BONK, 110) : fly([headW[0] - 12, 178, 6], [headW[0] + 40, 132, 0], BONK, BONK + 0.4, 30);
      items.push([B[0], B[1], B[2], 28, 28, 24]);
    }
    out.push(fig);
    items.sort((p, q) => (p[0] + p[1]) - (q[0] + q[1])).forEach(bx => out.push(I.carton(...bx)));
    // landing: dust along the body, the hat off and rolling, BONK, stars
    const [hx, hy] = I.p(headW[0], headW[1], 8);
    const [mx_, my_] = I.p(fx + 40, 192, 0), [ax_, ay_] = I.p(259, 213, 0);
    out.push(dust(mx_, my_, k(t, LAND, LAND + 0.45), I.s * 1.2), dust(ax_, ay_, k(t, 2.35, 2.75), I.s));
    if (t >= LAND) {
      const hp = k(t, LAND, LAND + 0.5, E.out);
      const [h2x, h2y] = I.p(headW[0] + 16 * hp, 190 + 46 * hp, 0);   // rolls off in front, clear of the boxes
      out.push(hardhat(lerp(hx, h2x, hp), lerp(hy, h2y, hp) - Math.sin(hp * Math.PI) * 40 * I.s, 46 * I.s, { flip: hp > 0.5, rot: -200 * hp + (hp >= 1 ? Math.sin(t * 9) * 6 * Math.exp(-(t - LAND - 0.5) * 3) : 0) }));
    }
    if (t > BONK && t < BONK + 0.4) out.push(mark(hx + 30 * I.s, hy - 70 * I.s, 'BONK!', 26));
    if (t > BONK + 0.15) out.push(dizzy(hx, hy - 34 * I.s, 40 * I.s, t * 0.5));
    // iris out on them
    const iris = k(t, 3.5, 4.9, E.inOut);
    if (iris > 0) {
      const [cx, cy] = I.p(headW[0] - 10, 186, 10);
      const rr = lerp(560, 0, iris);
      out.push(`<path d="M0 0 H${VW} V${VH} H0 Z M${f(cx - rr)} ${f(cy)} A${f(rr)} ${f(rr)} 0 1 0 ${f(cx + rr)} ${f(cy)} A${f(rr)} ${f(rr)} 0 1 0 ${f(cx - rr)} ${f(cy)} Z" fill="#000" fill-rule="evenodd"/>`);
    }
    return out.join('');
  }
  const t2 = t - 5.0, tt2 = two(t2);
  // nudge in on the counter as it flips back to 0
  const zc = k(t2, 5.2, 5.7, E.inOut) * (1 - k(t2, 6.5, 7.0, E.inOut));
  I = isoAt(lerp(W / 2, 70, zc), lerp(D / 2, 80, zc), lerp(0, 60, zc), VW / 2, lerp(292, 280, zc), lerp(0.8, 1.2, zc));
  sc = 0.46 * I.s;
  const out = [BG, room(I, W, D, '#E9DDBE')];
  for (let i = 0; i < 9; i++) for (let j = 0; j < 9; j++) if ((i + j) % 2 === 0) out.push(I.poly([[i * 40, j * 40, 0.1], [Math.min(i * 40 + 40, W), j * 40, 0.1], [Math.min(i * 40 + 40, W), Math.min(j * 40 + 40, D), 0.1], [i * 40, Math.min(j * 40 + 40, D), 0.1]], '#BFD3C6'));
  out.push(wallX(I, D, H, C.cream), I.poly([[0, 0, 0], [0, D, 0], [0, D, 50], [0, 0, 50]], C.teal), I.poly([[0, 0, 50], [0, D, 50], [0, D, 54], [0, 0, 54]], C.gold));
  out.push(wallY(I, W, H, C.cream), I.poly([[0, 0, 0], [W, 0, 0], [W, 0, 50], [0, 0, 50]], C.teal), I.poly([[0, 0, 50], [W, 0, 50], [W, 0, 54], [0, 0, 54]], C.gold));
  out.push(I.yplane(40, 0, 106) + `<rect x="0" y="0" width="176" height="24" fill="${C.orange}"/>` + text(88, 17, 'RECOVERY BAY 2', 14, C.cream, { ls: 2 }) + `<rect x="0" y="27" width="176" height="4" fill="${C.gold}"/><rect x="0" y="33" width="176" height="4" fill="${C.avo}"/></g>`);
  // the counter, big, by the corner: it flips from 1 back to 0 when they're logged in
  const flipS = tt2 > 5.8 && tt2 < 6.2 ? Math.abs(Math.cos((tt2 - 5.8) / 0.4 * Math.PI)) : 1;
  const days = tt2 < 6.0 ? '1' : '0';
  out.push(I.xplane(0, 104, 112) + `<rect x="0" y="0" width="92" height="64" fill="${C.lite}" stroke="${C.ink}" stroke-width="3"/>`
    + text(46, 15, 'DAYS SINCE', 10, C.ink, { weight: 700, italic: false, ls: 0.5 }) + text(46, 27, 'LAST RETURN', 10, C.ink, { weight: 700, italic: false, ls: 0.5 })
    + `<rect x="30" y="31" width="32" height="29" fill="${C.ink}"/><g transform="translate(46 46) scale(1 ${f(flipS)}) translate(-46 -46)">${text(46, 56, days, 26, C.orange, { italic: false, ls: 0 })}</g></g>`);
  // the way back out to work
  const exit = leftDoor(I, 186, 56, 84, 'BACK TO WORK');
  out.push(exit.svg);
  // spare hats on pegs
  for (const yy of [270, 300, 330]) { const [px, py] = I.p(4, yy, 82); out.push(`<rect x="${f(px - 2)}" y="${f(py)}" width="4" height="8" fill="${C.ink}"/>` + hardhat(px + 2, py + 2, 34 * I.s)); }
  // the RETURNS hatch and its roller ramp
  const hx0 = 250;
  out.push(I.yplane(hx0 - 6, 0, 52) + `<rect x="0" y="0" width="68" height="52" fill="#5A4334"/><rect x="6" y="6" width="56" height="46" fill="${C.ink}"/></g>`);
  out.push(I.yplane(hx0 - 6, 0, 76) + `<rect x="0" y="0" width="68" height="18" fill="${C.cream}"/>` + text(34, 13, 'RETURNS', 10, C.ink, { ls: 2 }) + '</g>');
  for (let r = 0; r < 5; r++) out.push(I.box(hx0, 4 + r * 14, Math.max(18 - r * 3.6, 0), 56, 10, 3, '#C9C2B0', '#A39C8A', '#8A8473'));
  out.push(I.box(hx0 - 2, 0, 0, 3, 76, 18, '#8A8473', '#6E685A', '#6E685A'), I.box(hx0 + 57, 0, 0, 3, 76, 18, '#8A8473', '#6E685A', '#6E685A'));
  // the worker: slides out feet first down the rollers, lies there seeing stars, is stickered,
  // gets up on the count, and trudges off through BACK TO WORK
  const sl = k(t2, 0.6, 1.8, E.in);
  const up = k(tt2, 6.0, 6.4, E.back), walk = k(tt2, 7.2, 8.5, E.lin), inDoor = k(tt2, 8.5, 8.9, E.lin);
  let wx = hx0 + 40, wy = lerp(-40, 96, sl), wz = sl < 1 ? lerp(22, 0, sl) : 0;
  if (walk > 0) { wx = lerp(hx0 + 40, 26, walk); wy = lerp(96, 214, walk); }
  if (inDoor > 0) { wx = lerp(26, -40, inDoor); wy = 214; }
  const [px, py] = I.p(wx, wy, wz);
  let fig;
  if (up <= 0) {
    const st = k(tt2, 4.2, 4.35, E.back);
    fig = lying(px, py, sc, -90, { aL: 30, aR: -30, sticker: st > 0 ? st * 1.3 : 0 });
    const burst = k(tt2, 4.2, 4.6, E.out);
    if (burst > 0 && burst < 1) for (let i = 0; i < 8; i++) { const a = i / 8 * 2 * Math.PI, r0 = (20 + burst * 26) * sc * 1.3, r1 = r0 + 12 * sc; fig += stroke([[px - 104 * sc + Math.cos(a) * r0, py + Math.sin(a) * r0], [px - 104 * sc + Math.cos(a) * r1, py + Math.sin(a) * r1]], 2.5, C.orange, ` opacity="${f(1 - burst)}"`); }
    if (sl >= 1) fig += dizzy(px - 168 * sc, py - 34 * sc, 46 * sc, t2 * 0.5);
  } else {
    const sway = walk > 0 ? 0 : Math.sin(tt2 * 7) * 8 * (1 - k(tt2, 6.4, 7.2));
    fig = lying(px, py, sc, -90 * (1 - up) + sway, { ...(walk > 0 ? { ...walkPose(tt2 * 7, 16), lean: 6 } : { headX: Math.sin(tt2 * 7) * 4 }), sticker: 1.3 });
    if (tt2 < 7.2) fig += dizzy(px, py - 205 * sc, 40 * sc * (1 - k(tt2, 6.4, 7.2)) + 0.01, t2 * 0.5);
  }
  out.push(dust(px - 60 * sc, py, k(t2, 1.8, 2.3), I.s));
  if (wx < 4) out.push(`<defs>${clipPoly('exitclip', exit.clip)}</defs><g clip-path="url(#exitclip)">${fig}</g>`);
  // the attendant: walks over, slaps the sticker on, then points them at the door: a bubble with the box they dropped
  const wp = k(tt2, 2.0, 3.8, E.inOut);
  const [ax, ay] = I.p(lerp(120, 236, wp), lerp(300, 200, wp), 0);
  let po = { aL: 70, item: 'clip' };
  if (tt2 < 3.8 && tt2 >= 2.0) po = { ...walkPose(tt2 * 9, 18), aL: 70, item: 'clip' };
  if (tt2 > 3.9 && tt2 < 4.5) po = { aL: 70, aR: tt2 < 4.2 ? -150 : -60, item: 'clip' };
  if (tt2 > 6.6) po = { aR: -100, aL: 70, item: 'clip' };
  const att = picto(ax, ay, 0.5 * I.s, { ...po, mirror: false });
  const behind = wy + wx < 436;   // who is further back is drawn first
  if (wx >= 4) { if (behind) out.push(fig, att); else out.push(att, fig); } else out.push(att);
  const bub = k(tt2, 6.6, 6.9, E.back) * (1 - k(tt2, 8.8, 9.0));
  if (bub > 0) {
    const bx = ax - 8, by = ay - 128;
    out.push(`<g transform="translate(${f(bx)} ${f(by)}) scale(${f(bub)})"><path d="M-46 -30 H46 a8 8 0 0 1 8 8 V18 a8 8 0 0 1 -8 8 H8 L0 40 L-8 26 H-46 a8 8 0 0 1 -8 -8 V-22 a8 8 0 0 1 8 -8 Z" fill="${C.cream}" stroke="${C.ink}" stroke-width="3"/>`
      + `<g transform="translate(-24 -1)">${flatBox(26)}</g><path d="M34 -2 H8 M18 -10 L8 -2 L18 6" fill="none" stroke="${C.orange}" stroke-width="5" stroke-linecap="round" stroke-linejoin="round"/></g>`);
  }
  // the iris opens again on the RETURNS hatch: the reverse of how the last shot closed
  const iris2 = 1 - k(t2, 0, 0.9, E.inOut);
  if (iris2 > 0) {
    const [cx, cy] = I.p(hx0 + 28, 30, 30);
    const rr = lerp(620, 0, iris2);
    out.push(`<path d="M0 0 H${VW} V${VH} H0 Z M${f(cx - rr)} ${f(cy)} A${f(rr)} ${f(rr)} 0 1 0 ${f(cx + rr)} ${f(cy)} A${f(rr)} ${f(rr)} 0 1 0 ${f(cx - rr)} ${f(cy)} Z" fill="#000" fill-rule="evenodd"/>`);
  }
  return out.join('');
});

// 10. the work camera
function camcorder(t, recOn) {
  const o = [];
  const spin = t * 160;
  // the carry handle along the top, and the microphone at its front
  o.push(`<path d="M-168 -78 C-168 -146 46 -146 46 -78" fill="none" stroke="${C.ink}" stroke-width="18" stroke-linecap="round"/>`);
  // the microphone sits in a clamp on a short post, the post bolted into the top of the body; a cable loops back in
  o.push(`<path d="M40 -116 C18 -116 14 -100 22 -84" fill="none" stroke="#1C130E" stroke-width="5" stroke-linecap="round"/>`);
  o.push(`<rect x="54" y="-112" width="12" height="34" fill="#2B1C14"/><rect x="44" y="-86" width="32" height="8" rx="3" fill="#2B1C14"/>`);
  o.push(`<rect x="36" y="-130" width="96" height="24" rx="10" fill="#5A4334"/><rect x="36" y="-126" width="96" height="4" fill="#6E5443"/>`);
  o.push(`<rect x="118" y="-136" width="40" height="36" rx="16" fill="#3B281D"/>` + [0, 1, 2, 3].map(i => `<rect x="${126 + i * 7}" y="-131" width="3" height="26" rx="1.5" fill="#2B1C14"/>`).join(''));
  o.push(`<rect x="50" y="-134" width="20" height="32" rx="4" fill="${C.ink}"/><circle cx="60" cy="-118" r="3.5" fill="#8A8473"/>`);
  // the viewfinder at the back, with its rubber eyecup
  o.push(`<rect x="-262" y="-122" width="120" height="44" rx="10" fill="${C.ink}"/><rect x="-290" y="-130" width="36" height="60" rx="12" fill="#1C130E"/><rect x="-282" y="-118" width="20" height="36" rx="6" fill="#2B1C14"/>`);
  // the body
  o.push(`<rect x="-214" y="-82" width="348" height="156" rx="18" fill="${C.choc}"/><rect x="-200" y="-68" width="320" height="128" rx="12" fill="#5A4334"/>`);
  // the cassette door, its window showing the tape turning
  o.push(`<rect x="-184" y="-50" width="140" height="84" rx="6" fill="#3B281D"/><rect x="-170" y="-38" width="112" height="40" rx="5" fill="#1C130E"/>`);
  [-142, -86].forEach(cx => o.push(`<g transform="translate(${cx} -18) rotate(${f(recOn ? spin : 0)})"><circle r="15" fill="#E9DCC0"/><circle r="6" fill="#1C130E"/>${[0, 120, 240].map(a => `<rect x="-2" y="-14" width="4" height="8" fill="#1C130E" transform="rotate(${a})"/>`).join('')}</g>`));
  o.push(text(-114, 26, 'VHS', 14, '#E9DCC0', { italic: false, ls: 3 }));
  // stripes and the name on the side
  o.push(`<rect x="-28" y="-46" width="136" height="9" fill="${C.orange}"/><rect x="-28" y="-33" width="136" height="9" fill="${C.gold}"/><rect x="-28" y="-20" width="136" height="9" fill="${C.avo}"/>`);
  o.push(text(40, 32, 'OVERFLOW', 20, C.cream, { ls: 3 }));
  // the REC lamp and the buttons
  o.push(`<circle cx="104" cy="-60" r="9" fill="${recOn ? '#E8402A' : '#5A1E1A'}"/>` + (recOn ? `<circle cx="104" cy="-60" r="17" fill="#E8402A" opacity="0.3"/>` : ''));
  o.push(`<rect x="-28" y="44" width="22" height="12" rx="3" fill="#2B1C14"/><rect x="0" y="44" width="22" height="12" rx="3" fill="#2B1C14"/><rect x="28" y="44" width="22" height="12" rx="3" fill="#B5311F"/>`);
  // the lens: a ribbed barrel, a flared hood, the glass
  o.push(`<rect x="134" y="-64" width="100" height="118" rx="8" fill="${C.ink}"/>` + [0, 1, 2, 3].map(i => `<rect x="${150 + i * 20}" y="-64" width="8" height="118" fill="#3B281D"/>`).join(''));
  o.push(`<path d="M232 -70 L278 -88 L278 76 L232 58 Z" fill="#1C130E"/><ellipse cx="278" cy="-6" rx="18" ry="82" fill="#173B38"/><ellipse cx="280" cy="-6" rx="11" ry="56" fill="#1F4C48"/><ellipse cx="283" cy="-34" rx="4" ry="16" fill="#9FC2B6" opacity="0.8"/>`);
  return o.join('');
}
scene('camera', 10, 'This is your work camera. Keep it running. If it isn’t on tape, it didn’t happen. And if it didn’t happen, you don’t get paid.', t => {
  const tt = two(t);
  const out = [BG, stripes(0, 470, VW, 10, 4)];
  const up = k(t, 0.2, 0.9, E.back);
  const recOn = Math.floor(t * 2) % 2 === 0 && t > 1.2;
  const cy = 300 + 320 * (1 - up);
  out.push(`<g transform="translate(${VW / 2 - 6} ${f(cy)}) scale(0.68) rotate(${f(Math.sin(tt * 2) * 1.5)})">`);
  out.push(camcorder(t, recOn));
  // two hands holding it up from underneath, arms coming in from the bottom corners
  out.push(`<g fill="${C.ink}"><rect x="-235" y="20" width="48" height="460" rx="24" transform="rotate(64 -211 40)"/><circle cx="-211" cy="34" r="30"/><rect x="-206" y="-6" width="22" height="34" rx="11"/>`);
  out.push(`<rect x="166" y="50" width="48" height="460" rx="24" transform="rotate(-58 190 70)"/><circle cx="190" cy="64" r="30"/><rect x="180" y="26" width="22" height="34" rx="11"/></g>`);
  out.push(`</g>`);
  if (t >= 8.4) {
    // the view through the viewfinder: what the work camera shows you, with Boxley waving into it
    const inner = SCENES.find(s => s.name === 'hired').draw(7.0 + (t - 8.4) * 0.8);
    const sw = [Math.sin(t * 2.3) * 4, Math.cos(t * 1.7) * 3];
    const o = [`<rect width="${VW}" height="${VH}" fill="#0E0B09"/>`, `<g transform="translate(${f(VW / 2 + sw[0])} ${f(VH / 2 + sw[1])}) scale(0.9) translate(${-VW / 2} ${-VH / 2})">${inner}</g>`];
    o.push(`<path d="M0 0 H${VW} V${VH} H0 Z M60 70 Q60 40 90 40 H${VW - 90} Q${VW - 60} 40 ${VW - 60} 70 V${VH - 70} Q${VW - 60} ${VH - 40} ${VW - 90} ${VH - 40} H90 Q60 ${VH - 40} 60 ${VH - 70} Z" fill="#0E0B09" fill-rule="evenodd"/>`);
    const br = (x, y, dx, dy) => `<path d="M${x} ${y + dy * 26} V${y} H${x + dx * 26}" fill="none" stroke="#F7F2E4" stroke-width="4"/>`;
    o.push(br(96, 76, 1, 1), br(VW - 96, 76, -1, 1), br(96, VH - 76, 1, -1), br(VW - 96, VH - 76, -1, -1));
    o.push(`<path d="M${VW / 2 - 14} ${VH / 2} H${VW / 2 + 14} M${VW / 2} ${VH / 2 - 14} V${VH / 2 + 14}" stroke="#F7F2E4" stroke-width="2" opacity="0.8"/>`);
    const vt = (x, y, s, size, anchor, fill = '#F7F2E4') => `<text x="${x}" y="${y}" font-family="VT323, monospace" font-size="${size}" fill="${fill}" text-anchor="${anchor}">${s}</text>`;
    if (Math.floor(t * 2) % 2 === 0) o.push(vt(110, 112, '● REC', 30, 'start', '#E8402A'));
    o.push(`<rect x="${VW - 160}" y="92" width="46" height="22" fill="none" stroke="#F7F2E4" stroke-width="2.5"/><rect x="${VW - 114}" y="98" width="5" height="10" fill="#F7F2E4"/>` + [0, 1, 2].map(i => i < 2 || Math.floor(t * 3) % 2 ? `<rect x="${VW - 156 + i * 14}" y="96" width="11" height="14" fill="#F7F2E4"/>` : '').join(''));
    o.push(vt(VW - 110, VH - 92, 'TAPE 0:12:44', 26, 'end'), vt(110, VH - 92, 'OVERFLOW WC-1', 22, 'start'));
    return o.join('');
  }
  if (t > 1.2 && recOn) out.push(`<text x="${VW - 30}" y="60" font-family="VT323, monospace" font-size="40" fill="#E8402A" text-anchor="end" stroke="rgba(0,0,0,0.25)" stroke-width="2">● REC</text>`);
  // no tape, no pay
  const n1 = k(t, 5.0, 5.3, E.back), n2 = k(t, 7.4, 7.7, E.back);
  if (n1 > 0) out.push(`<g transform="translate(220 96) rotate(-5) scale(${f(lerp(1.8, 1, n1))})" opacity="${f(clamp(n1 * 2))}">${text(0, 0, 'NO TAPE,', 40, C.orange, { ls: 2 })}</g>`);
  if (n2 > 0) out.push(`<g transform="translate(470 96) rotate(-5) scale(${f(lerp(1.8, 1, n2))})" opacity="${f(clamp(n2 * 2))}">${text(0, 0, 'NO PAY.', 40, C.orange, { ls: 2 })}</g>`);
  return out.join('');
});

// 11. work hard, work fast
scene('slogan', 10, 'Work hard. Work fast. Be back by closing. (beat) Welcome to Overflow. There’s always more room.', t => {
  const tt = two(t);
  if (t < 5.4) {
    const out = [BG, stripes(0, 392, VW, 16, 5)];
    [['WORK HARD.', 0.3, 150], ['WORK FAST.', 1.5, 236], ['BE BACK BY CLOSING.', 2.8, 316]].forEach(([w, at, y], i) => {
      const p = k(t, at, at + 0.3, E.back);
      if (p <= 0) return;
      const ul = k(t, at + 0.2, at + 0.45, E.out), ww = w.length * (i === 2 ? 31 : 39);
      out.push(`<g transform="translate(${VW / 2} ${y})">` + (ul > 0 ? stripes(-ww / 2, 12, ww * ul, 3, 2) : '') + `<g transform="scale(${f(lerp(1.6, 1, p))})" opacity="${f(clamp(p * 2))}">${text(0, 0, w, i === 2 ? 46 : 58, [C.teal, C.orange, C.ink][i], { ls: 3 })}</g></g>`);
    });
    // the crew marching across along the stripes
    for (let i = 0; i < 5; i++) {
      const x = -100 + tt * 170 - i * 96;
      if (x < -60 || x > VW + 60) continue;
      out.push(picto(x, 486, 0.46, { ...walkPose(tt * 9 + i, 24), mirror: true }));
    }
    return out.join('');
  }
  return titleCard(t, { done: true, banner: 'WELCOME TO OVERFLOW' });
});

// 12. the tape clicks off
scene('off', 6, '', t => {
  if (t < 0.5) {
    const p = k(t, 0, 0.45, E.in);
    return `<rect width="${VW}" height="${VH}" fill="#000"/><g transform="translate(${VW / 2} ${VH / 2}) scale(${f(1 + p * 0.3)} ${f(Math.max(0.004, 1 - p))}) translate(${-VW / 2} ${-VH / 2})">${titleCard(10, { done: true, banner: 'WELCOME TO OVERFLOW' })}</g><rect x="0" y="${VH / 2 - 2}" width="${VW}" height="4" fill="#FFF" opacity="${f(p)}"/>`;
  }
  return `<rect width="${VW}" height="${VH}" fill="#0A1FA8"/>`;
});

// 13. the hidden take: blue screen, then the tape catches a few seconds of something recorded over: a worker's own
// footage down in the dark. They turn to the camera and wave it back; the light dies; then there's only the torch on
// the floor. No monster: whatever it is, it's never on tape.
const HID = { on: 3.8, end: 6.7 };
scene('hidden', 7, '(hidden) ...and whatever you do, don’t...', t => {
  if (t < HID.on) return `<rect width="${VW}" height="${VH}" fill="#0A1FA8"/>`;
  if (t >= HID.end) return `<rect width="${VW}" height="${VH}" fill="#000"/>`;
  const n = Math.floor(t * 24);
  if (t < 4.1) {   // the tape catching
    let snow = `<rect width="${VW}" height="${VH}" fill="#2A2A2A"/>`;
    for (let i = 0; i < 160; i++) snow += `<rect x="${f(rnd(n * 17 + i) * VW)}" y="${f(rnd(n * 19 + i * 3) * VH)}" width="${f(4 + rnd(i + n) * 40)}" height="2" fill="#FFF" opacity="${f(rnd(i * 7 + n) * 0.8)}"/>`;
    return snow;
  }
  if (t > 6.2 && t < 6.3) return `<rect width="${VW}" height="${VH}" fill="#000"/>`;
  const shake = [(rnd(n * 1.3) - 0.5) * (t > 5.3 ? 10 : 4), (rnd(n * 2.7) - 0.5) * (t > 5.3 ? 8 : 3)];
  const I = isoAt(120, -20, 0, 360 + shake[0], 380 + shake[1], 1.15);
  // an open stretch of hall: walls behind them, a row of walls in the foreground drawn over everything, nothing in between
  const out = [`<rect width="${VW}" height="${VH}" fill="#1A120C"/>`, backrooms(I, [[-1500, 1500, -1500, 1500]], [[-1500, 1500, -1500, -32], [-1500, -70, -32, 50]], 96)];
  const gone = t >= 6.3;
  // boxes left in the hall, on the open floor round them: the ones further back drawn before the worker
  const boxes = [[20, -14], [-30, 30], [206, 26], [64, 52]];
  boxes.filter(([x, y]) => x + y < 100).forEach(([x, y]) => out.push(I.carton(x, y, 0, 30, 30, 24)));
  const [px, py] = I.p(120, -20, 0), sc = 0.66;
  // the torch: in their hand pointing down the hall, or later rolling on the floor
  const beam = (x, y, ang, len, on) => on ? `<polygon points="${f(x)},${f(y)} ${f(x + Math.cos(ang - 0.3) * len)},${f(y + Math.sin(ang - 0.3) * len)} ${f(x + Math.cos(ang + 0.3) * len)},${f(y + Math.sin(ang + 0.3) * len)}" fill="#FFF0B8" opacity="0.38"/><circle cx="${f(x + Math.cos(ang) * len * 0.92)}" cy="${f(y + Math.sin(ang) * len * 0.92)}" r="${f(len * 0.24)}" fill="#FFF0B8" opacity="0.2"/>` : '';
  if (!gone) {
    const turned = t > 5.3, flickOn = !(t > 5.0 && t < 5.3 && Math.floor(t * 18) % 2), dying = t > 6.0;
    if (!turned) out.push(beam(px - 30, py - 90, -2.6 + Math.sin(t * 3) * 0.15, 260, flickOn));
    else out.push(beam(px + 26, py - 96, -0.5, 200, !dying));
    out.push(picto(px, py, sc, turned ? { aL: 150 + Math.sin(t * 22) * 22, aR: -150 - Math.sin(t * 22) * 22, hat: '#5A4334', bob: -Math.abs(Math.sin(t * 18)) * 4 } : { aL: 100, hat: '#5A4334', headX: -6 }));
    boxes.filter(([x, y]) => x + y >= 100).forEach(([x, y]) => out.push(I.carton(x, y, 0, 30, 30, 24)));
  } else {
    boxes.filter(([x, y]) => x + y >= 100).forEach(([x, y]) => out.push(I.carton(x, y, 0, 30, 30, 24)));
    // only the torch left, rolling to a stop
    const r = k(t, 6.3, 6.7, E.out);
    const [tx, ty] = I.p(120 + r * 20, -20 + r * 6, 0);
    out.push(beam(tx, ty, 2.3 + r * 0.6, 150, true));
    out.push(`<g transform="translate(${f(tx)} ${f(ty)}) rotate(${f(130 + r * 40)})"><rect x="-14" y="-5" width="28" height="10" rx="3" fill="#2B1C14"/><rect x="12" y="-7" width="6" height="14" rx="2" fill="#C9C2B0"/></g>`);
  }
  out.push(backrooms(I, [], [[-1500, 1500, 190, 1500], [320, 1500, -32, 190]], 96, 1, { pools: false }));
  if (!gone && t > 6.0) out.push(`<rect width="${VW}" height="${VH}" fill="#000" opacity="${f(k(t, 6.0, 6.2))}"/>`);   // the light dies
  // dark, but for a little light round the middle
  out.push(`<rect width="${VW}" height="${VH}" fill="url(#hidvig)"/><defs><radialGradient id="hidvig" cx="50%" cy="58%" r="60%"><stop offset="0" stop-color="#000" stop-opacity="0.25"/><stop offset="0.7" stop-color="#000" stop-opacity="0.75"/><stop offset="1" stop-color="#000" stop-opacity="0.92"/></radialGradient></defs>`);
  // the home camcorder's own date stamp, and a tear across the picture as it goes
  out.push(`<text x="30" y="${VH - 30}" font-family="VT323, monospace" font-size="30" fill="#F7F2E4" opacity="0.9">11:58 PM   OCT 14 1979</text>`);
  if (t > 5.9) for (let i = 0; i < 5; i++) { const y = rnd(n * 3 + i) * VH; out.push(`<rect x="0" y="${f(y)}" width="${VW}" height="${f(4 + rnd(n + i) * 14)}" fill="#FFF" opacity="${f(0.15 + rnd(i * n) * 0.3)}"/>`); }
  return out.join('');
});

// ---------- the timeline ----------
let acc = 0;
SCENES.forEach(s => { s.start = acc; acc += s.dur; });
const TOTAL = acc;
const TOTAL_FRAMES = Math.round(TOTAL * FPS);

// ---------- the wipes: the three stripes sweep across between sections, the logo riding on the gold one ----------
const WIPES = [8, 16, 27, 52, 68, 88, 118, 132, 142];
const LOGO = `<polygon points="20,66 0,44 50,40 60,66" fill="#DDB06A"/><polygon points="124,66 144,44 94,40 84,66" fill="#DDB06A"/><rect x="20" y="66" width="104" height="70" fill="${C.cfront}"/><rect x="64" y="66" width="16" height="22" fill="${C.tape}"/>`
  + `<rect x="44" y="18" width="30" height="30" fill="${C.orange}" transform="rotate(-14 59 33)"/><rect x="78" y="6" width="26" height="26" fill="${C.gold}" transform="rotate(12 91 19)"/><rect x="62" y="38" width="24" height="24" fill="${C.avo}"/>`;
function wipe(T) {
  const c = WIPES.find(c => Math.abs(T - c) < 0.32);
  if (c === undefined) return '';
  const p = (T - c + 0.32) / 0.64;              // the cut falls at p = 0.5, when the stripes cover everything
  const lead = lerp(-60, VW + 1060, E.inOut(p)), bw = 330, sl = 110;
  let o = '';
  [C.orange, C.gold, C.avo].forEach((col, i) => {
    const x1 = lead - i * bw, x0 = x1 - bw;
    o += `<polygon points="${f(x0 + sl)},0 ${f(x1 + sl)},0 ${f(x1 - sl)},${VH} ${f(x0 - sl)},${VH}" fill="${col}"/>`;
  });
  o += `<g transform="translate(${f(lead - 1.5 * bw - 58)} ${VH / 2 - 56}) scale(0.8)">${LOGO}</g>`;
  return o;
}

// ---------- the VHS layer drawn into the picture ----------
function vhs(T, n, sc) {
  const out = [];
  out.push(`<rect width="${VW}" height="${VH}" fill="url(#scan)"/>`);
  out.push(`<rect width="${VW}" height="${VH}" fill="url(#vig)"/>`);
  // head-switching noise along the bottom
  let hs = '';
  for (let i = 0; i < 9; i++) {
    const r = rnd(n * 31 + i);
    hs += `<rect x="${f(r * VW)}" y="${f(VH - 9 + rnd(n * 7 + i) * 8)}" width="${f(16 + rnd(i * 3 + n) * 70)}" height="${f(1 + rnd(n + i * 11) * 1.5)}" fill="#FFF" opacity="${f(0.1 + rnd(n * 3 + i) * 0.2)}"/>`;
  }
  out.push(hs);
  // a rolling tracking band now and then, and always at the start
  const bands = [[0.0, 1.4], [151.8, 152.4]];   // only as the tape starts and as it stops
  bands.forEach(([a, b]) => {
    if (T < a || T > b) return;
    const p = (T - a) / (b - a);
    const y = lerp(VH + 20, -40, p);
    let band = '';
    for (let i = 0; i < 34; i++) band += `<rect x="${f(rnd(n * 13 + i) * VW)}" y="${f(y + rnd(n + i * 5) * 26)}" width="${f(24 + rnd(i + n * 2) * 120)}" height="${f(1 + rnd(n * 5 + i) * 3)}" fill="#FFF" opacity="${f(0.25 + rnd(i * 9 + n) * 0.5)}"/>`;
    out.push(band);
  });
  if (T < 0.8) {
    let snow = `<rect width="${VW}" height="${VH}" fill="#3A3A3A" opacity="${f(1 - k(T, 0.5, 0.8))}"/>`;
    for (let i = 0; i < 140; i++) snow += `<rect x="${f(rnd(n * 17 + i) * VW)}" y="${f(rnd(n * 19 + i * 3) * VH)}" width="${f(4 + rnd(i + n) * 34)}" height="2" fill="#FFF" opacity="${f(rnd(i * 7 + n) * 0.8 * (1 - k(T, 0.5, 0.8)))}"/>`;
    out.push(snow);
  }
  // the VCR's own text
  const osd = (s, x, y, anchor = 'start', size = 34) => `<text x="${x}" y="${y}" font-family="VT323, monospace" font-size="${size}" fill="#F7F2E4" text-anchor="${anchor}" style="paint-order: stroke" stroke="rgba(0,0,0,0.35)" stroke-width="3">${s}</text>`;
  if (T > 0.3 && T < 4.5) out.push(osd('PLAY ▶', 30, 50));
  const off = SCENES.find(s => s.name === 'off').start;
  if (T < off + 0.5) {
    const sec = Math.floor(T), m = Math.floor(sec / 60), s = sec % 60;
    out.push(osd(`SP 0:0${m}:${String(s).padStart(2, '0')}`, VW - 30, VH - 20, 'end', 30));
  } else if (T < off + 3.5 || (sc.name === 'hidden' && T - sc.start > HID.on && T - sc.start < HID.end)) {
    out.push(osd(sc.name === 'hidden' ? 'PLAY ▶' : 'STOP ■', 30, 50));
  }
  return out.join('');
}

const DEFS = `<defs><pattern id="scan" width="4" height="3" patternUnits="userSpaceOnUse"><rect width="4" height="1" fill="rgba(0,0,0,0.09)"/></pattern>`
  + `<radialGradient id="pool"><stop offset="0" stop-color="#FFF6D0" stop-opacity="0.85"/><stop offset="0.6" stop-color="#FFF6D0" stop-opacity="0.45"/><stop offset="1" stop-color="#FFF6D0" stop-opacity="0"/></radialGradient>`
  + `<radialGradient id="vig" cx="50%" cy="50%" r="72%"><stop offset="62%" stop-color="#000" stop-opacity="0"/><stop offset="100%" stop-color="#1A0E06" stop-opacity="0.55"/></radialGradient></defs>`;

function captions(sc, tl) {
  if (!CAPTIONS || !sc.line) return '';
  const words = sc.line.split(' ');
  const lines = []; let cur = '';
  words.forEach(w => { if ((cur + ' ' + w).length > 54) { lines.push(cur); cur = w; } else cur = cur ? cur + ' ' + w : w; });
  if (cur) lines.push(cur);
  const y0 = 476 - lines.length * 22;
  return `<rect x="24" y="${y0 - 22}" width="${VW - 48}" height="${lines.length * 22 + 14}" fill="rgba(0,0,0,0.6)"/>` + lines.map((l, i) => `<text x="${VW / 2}" y="${y0 + i * 22}" font-family="Jost, sans-serif" font-size="18" font-weight="500" fill="#FFF" text-anchor="middle">${l}</text>`).join('');
}

function renderFrame(n) {
  const T = n / FPS;
  let sc = SCENES[SCENES.length - 1];
  for (const s of SCENES) if (T >= s.start && T < s.start + s.dur) { sc = s; break; }
  const tl = T - sc.start;
  NOW = T;
  const body = sc.draw(tl);
  const g = document.getElementById('grain');
  if (g) g.style.display = (sc.name === 'off' && tl > 0.5) || (sc.name === 'hidden' && (tl < HID.on || tl >= HID.end)) ? 'none' : '';
  // a gentle tape wobble: the picture sways a hair now and then
  const wob = (rnd(Math.floor(T * 3)) - 0.5) * 0.5;
  const blue = sc.name === 'off' && tl > 0.5 || (sc.name === 'hidden' && (tl < HID.on || tl >= HID.end));
  NOW = T;
  const svg = DEFS + `<g transform="translate(${blue ? 0 : f(wob)} 0)">${body}</g>` + wipe(T) + (blue ? '' : vhs(T, n, sc)) + (blue && sc.name === 'off' && tl < 3.5 ? `<text x="30" y="50" font-family="VT323, monospace" font-size="34" fill="#F7F2E4">STOP ■</text>` : '') + captions(sc, tl);
  document.getElementById('s').innerHTML = svg;
  return { T, scene: sc.name };
}

// the grain: drawn once into a canvas laid over the picture (multiplied in, so it only darkens a little)
(() => {
  const c = document.getElementById('grain');
  if (!c) return;
  const g = c.getContext('2d'), img = g.createImageData(c.width, c.height);
  for (let i = 0; i < img.data.length; i += 4) { const v = 255 - Math.floor(rnd(i * 0.37) * 70); img.data[i] = v; img.data[i + 1] = v - 3; img.data[i + 2] = v - 8; img.data[i + 3] = 255; }
  g.putImageData(img, 0, 0);
})();

window.renderFrame = renderFrame;
window.TOTAL_FRAMES = TOTAL_FRAMES;
window.SCENE_TIMES = SCENES.map(s => ({ name: s.name, start: s.start, dur: s.dur, line: s.line }));
renderFrame(Number(P.get('f') || 0));
