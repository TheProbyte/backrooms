# Realistic skinned first-person player: a right arm (sleeve, forearm, hand with nails; no watch or ring) and a body
# (shirt, trousers, belt, shoes), each one armature + skinned mesh. Same interface as make_player.py: the bones
# (= Unity Transforms) carry the same names and sit at the same joints, so FirstPersonBody.cs drives them as before.
#
#   blender -b --factory-startup --python make_player_rig.py -- <out_dir> [--left] [--no-arm] [--no-body]
#           [--res 0.00075] [--blend <file.blend>]
#
# Default: PlayerArmRight.fbx + PlayerBody.fbx; --left: PlayerArm.fbx (+ PlayerBody.fbx unless --no-body).
#
# The organic shapes are signed distance fields (numpy) polygonised with surface nets, projected onto the
# surface, decimated and skinned with analytic weights. Clothing pieces, nails, the ring and the watch are
# separate closed shells inside the same skinned mesh, so material borders are clean.
# Modelling frame: Blender, shoulder at the origin, the arm along -Y, back of the hand +Z. The hand is built as a
# LEFT hand (thumb on -X) and mirrored in x for the right one (S = -1), like make_player.py.
import bpy, bmesh, math, sys, time
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_dir = argv[0] if argv and not argv[0].startswith("--") else "."
LEFT = "--left" in argv
BUILD_ARM = "--no-arm" not in argv
BUILD_BODY = "--no-body" not in argv


def opt(name, default):
    return type(default)(argv[argv.index(name) + 1]) if name in argv else default


RES = opt("--res", 0.00075)        # skin grid step (m)
BLEND = opt("--blend", "")
S = 1.0 if LEFT else -1.0
T0 = time.time()


def log(*a):
    print(f"[{time.time() - T0:6.1f}s]", *a, flush=True)


bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def mat(name, rgb, rough=0.6, metal=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    m.diffuse_color = (*rgb, 1)
    return m


MATS = {
    "Skin": mat("Skin", (0.78, 0.52, 0.40), 0.55),
    "Nail": mat("Nail", (0.80, 0.60, 0.52), 0.3),
    "Shirt": mat("Shirt", (0.10, 0.10, 0.11), 0.85),
    "WatchStrap": mat("WatchStrap", (0.30, 0.32, 0.27), 0.9),
    "WatchCase": mat("WatchCase", (0.75, 0.74, 0.70), 0.3, 1.0),
    "WatchFace": mat("WatchFace", (0.02, 0.02, 0.025), 0.2),
    "Ring": mat("Ring", (0.12, 0.12, 0.12), 0.35, 0.8),
    "Pants": mat("Pants", (0.025, 0.025, 0.03), 0.8),
    "Shoes": mat("Shoes", (0.03, 0.03, 0.035), 0.6),
    "Sole": mat("Sole", (0.06, 0.06, 0.06), 0.7),
}

# ======================================================================================================
# SDF toolkit (P is an (N, 3) float array)
# ======================================================================================================


def nrm(v):
    return np.sqrt(np.einsum("ij,ij->i", v, v))


def smin(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b + (a - b) * h - k * h * (1.0 - h)


def smax(a, b, k):
    return -smin(-a, -b, k)


def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def gauss(x, c, s):
    return np.exp(-((x - c) / s) ** 2)


def ellipsoid(P, c, r, R=None):
    q = P - np.asarray(c, float)
    if R is not None:
        q = q @ np.asarray(R, float).T
    r = np.asarray(r, float)
    k0 = nrm(q / r)
    k1 = nrm(q / (r * r))
    return k0 * (k0 - 1.0) / np.maximum(k1, 1e-12)


def capsule(P, a, b, r):
    a = np.asarray(a, float)
    ba = np.asarray(b, float) - a
    pa = P - a
    h = np.clip(pa @ ba / (ba @ ba), 0.0, 1.0)
    return nrm(pa - h[:, None] * ba) - r


def round_cone(P, a, b, r1, r2):
    a = np.asarray(a, float)
    ba = np.asarray(b, float) - a
    l2 = ba @ ba
    rr = r1 - r2
    a2 = l2 - rr * rr
    il2 = 1.0 / l2
    pa = P - a
    y = pa @ ba
    z = y - l2
    xv = pa * l2 - y[:, None] * ba
    x2 = (xv * xv).sum(1)
    y2 = y * y * l2
    z2 = z * z * l2
    k = np.sign(rr) * rr * rr * x2
    d1 = np.sqrt(x2 + z2) * il2 - r2
    d2 = np.sqrt(x2 + y2) * il2 - r1
    d3 = (np.sqrt(np.maximum(x2 * a2 * il2, 0.0)) + y * rr) * il2 - r1
    return np.where(np.sign(z) * a2 * z2 > k, d1, np.where(np.sign(y) * a2 * y2 < k, d2, d3))


def spline(x, xs, ys):
    """Smooth (cubic Hermite) interpolation through control points, clamped at the ends."""
    xs = np.asarray(xs, float)
    ys = np.asarray(ys, float)
    x = np.clip(x, xs[0], xs[-1])
    i = np.clip(np.searchsorted(xs, x) - 1, 0, len(xs) - 2)
    m = np.empty_like(ys)
    m[1:-1] = (ys[2:] - ys[:-2]) / (xs[2:] - xs[:-2])
    m[0] = (ys[1] - ys[0]) / (xs[1] - xs[0])
    m[-1] = (ys[-1] - ys[-2]) / (xs[-1] - xs[-2])
    x0, x1 = xs[i], xs[i + 1]
    h = x1 - x0
    t = (x - x0) / h
    t2, t3 = t * t, t * t * t
    return (2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h * m[i] + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h * m[i + 1]


def loft(u, w, s, W, Td, Tp, cu, cw, nd, npp, s0, L0, s1, L1, arch=0.0):
    """A tube along s with a superellipse cross-section: half-width W, half-thickness Td above (w > cw) and Tp
    below, exponent nd / npp, and ellipsoidal end caps of length L0 / L1. Profiles are sampled at the clamped s."""
    X = (u - cu) / W
    dw = w - cw + arch * X * X
    up = dw > 0
    T = np.where(up, Td, Tp)
    n = np.where(up, nd, npp)
    Z = dw / T
    g = (np.abs(X) ** n + np.abs(Z) ** n) ** (1.0 / n)
    Sx = np.maximum(0.0, s - (s1 - L1)) / L1 + np.maximum(0.0, (s0 + L0) - s) / L0
    g3 = np.sqrt(g * g + Sx * Sx)
    return (g3 - 1.0) * np.minimum(W, T)


def unit(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v)


def eval_chunked(f, P, chunk=300000):
    out = np.empty(len(P), np.float64)
    for i in range(0, len(P), chunk):
        out[i:i + chunk] = f(P[i:i + chunk])
    return out


# ------------------------------------------------------------------------------------------------------
# Surface nets
# ------------------------------------------------------------------------------------------------------
CORNERS = np.array([[0, 0, 0], [1, 0, 0], [0, 1, 0], [1, 1, 0], [0, 0, 1], [1, 0, 1], [0, 1, 1], [1, 1, 1]])
EDGES = [(0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7)]


def upsample(Fc, n, c):
    out = Fc
    for ax in range(3):
        idx = np.arange(n[ax]) / c
        i0 = np.minimum(np.floor(idx).astype(int), Fc.shape[ax] - 2)
        f = (idx - i0).astype(np.float32)
        shp = [1, 1, 1]
        shp[ax] = -1
        f = f.reshape(shp)
        out = np.take(out, i0, axis=ax) * (1 - f) + np.take(out, i0 + 1, axis=ax) * f
    return out


def project(f, V, h, iters=3):
    E = np.eye(3) * (h * 0.25)
    for _ in range(iters):
        d = eval_chunked(f, V)
        g = np.stack([(eval_chunked(f, V + E[a]) - eval_chunked(f, V - E[a])) / (h * 0.5) for a in range(3)], 1)
        step = d[:, None] * g / np.maximum((g * g).sum(1), 1e-9)[:, None]
        ln = nrm(step)
        step *= np.minimum(1.0, h / np.maximum(ln, 1e-12))[:, None]
        V = V - step
    return V


def polygonize(f, lo, hi, h, coarse=4):
    lo = np.asarray(lo, float)
    hi = np.asarray(hi, float)
    n = np.ceil((hi - lo) / h).astype(int) + 1
    H = h * coarse
    nc = np.ceil((n - 1) / coarse).astype(int) + 2
    gc = [lo[a] + np.arange(nc[a]) * H for a in range(3)]
    Pc = np.stack(np.meshgrid(*gc, indexing="ij"), -1).reshape(-1, 3)
    Fc = eval_chunked(f, Pc).reshape(nc).astype(np.float32)
    F = upsample(Fc, n, coarse)
    idx = np.nonzero(np.abs(F.ravel()) < 2.0 * H)[0]
    ii = np.unravel_index(idx, n)
    P = np.stack([lo[a] + ii[a] * h for a in range(3)], -1)
    F = F.ravel()
    F[idx] = eval_chunked(f, P)
    F = F.reshape(n)
    s = F < 0
    any_ = s[:-1, :-1, :-1].copy()
    all_ = any_.copy()
    for dx, dy, dz in CORNERS[1:]:
        c = s[dx:n[0] - 1 + dx, dy:n[1] - 1 + dy, dz:n[2] - 1 + dz]
        any_ |= c
        all_ &= c
    cells = np.argwhere(any_ & ~all_)
    del any_, all_
    M = len(cells)
    vid = np.full(n - 1, -1, np.int32)
    vid[tuple(cells.T)] = np.arange(M, dtype=np.int32)
    cv = np.stack([F[cells[:, 0] + dx, cells[:, 1] + dy, cells[:, 2] + dz] for dx, dy, dz in CORNERS], 1)
    acc = np.zeros((M, 3))
    cnt = np.zeros(M)
    for a, b in EDGES:
        fa, fb = cv[:, a], cv[:, b]
        m = (fa < 0) != (fb < 0)
        t = np.where(m, fa / np.where(m, fa - fb, 1.0), 0.0)
        p = CORNERS[a][None, :] + t[:, None] * (CORNERS[b] - CORNERS[a])[None, :]
        acc += np.where(m[:, None], p, 0.0)
        cnt += m
    V = lo + (cells + acc / np.maximum(cnt, 1)[:, None]) * h
    quads = []
    for ax in range(3):
        sl0 = [slice(None)] * 3
        sl1 = [slice(None)] * 3
        sl0[ax] = slice(0, n[ax] - 1)
        sl1[ax] = slice(1, n[ax])
        g = np.argwhere(s[tuple(sl0)] != s[tuple(sl1)])
        o1, o2 = [a for a in range(3) if a != ax]
        ok = (g[:, o1] >= 1) & (g[:, o1] <= n[o1] - 2) & (g[:, o2] >= 1) & (g[:, o2] <= n[o2] - 2)
        g = g[ok]
        inside = s[tuple(g.T)]

        def cell(d1, d2):
            c = g.copy()
            c[:, o1] -= d1
            c[:, o2] -= d2
            return vid[tuple(c.T)]

        q = np.stack([cell(1, 1), cell(0, 1), cell(0, 0), cell(1, 0)], 1)
        nm = np.cross(V[q[:, 2]] - V[q[:, 0]], V[q[:, 3]] - V[q[:, 1]])
        want = np.where(inside, 1.0, -1.0)
        flip = nm[:, ax] * want < 0
        q[flip] = q[flip][:, ::-1]
        quads.append(q)
    Q = np.concatenate(quads)
    V = project(f, V, h)
    log(f"   polygonized {M} verts {len(Q)} quads (grid {n.tolist()})")
    return V, Q


def compact(V, Q, keep_face):
    Q = Q[keep_face]
    used = np.unique(Q)
    remap = np.full(len(V), -1, np.int64)
    remap[used] = np.arange(len(used))
    return V[used], remap[Q]


def new_obj(name, V, F, matname):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in np.asarray(V, float)], [], [tuple(int(i) for i in f) for f in F])
    me.validate()
    me.materials.append(MATS[matname])
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    me.shade_smooth()
    return ob


def sharpen(me, angle_deg):
    """Smooth shading everywhere, sharp edges where faces meet at more than angle_deg."""
    bm = bmesh.new()
    bm.from_mesh(me)
    lim = math.radians(angle_deg)
    for e in bm.edges:
        e.smooth = not (len(e.link_faces) == 2 and e.calc_face_angle(0.0) > lim)
    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(me)
    bm.free()


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def decimate(ob, target):
    tris = tri_count(ob)
    if tris <= target:
        return
    m = ob.modifiers.new("dec", "DECIMATE")
    m.decimate_type = "COLLAPSE"
    m.ratio = target / tris
    m.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    ob.modifiers.remove(m)
    old = ob.data
    ob.data = me
    bpy.data.meshes.remove(old)
    me.shade_smooth()


def get_co(ob):
    co = np.empty(len(ob.data.vertices) * 3)
    ob.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def set_co(ob, V):
    ob.data.vertices.foreach_set("co", np.asarray(V, float).ravel())
    ob.data.update()


def sdf_shell(name, f, lo, hi, h, target, matname, keep=None, reproject=True):
    V, Q = polygonize(f, lo, hi, h)
    if keep is not None:
        V, Q = compact(V, Q, keep(V[Q].mean(1)))
    ob = new_obj(name, V, Q, matname)
    decimate(ob, target)
    if reproject:
        set_co(ob, project(f, get_co(ob), h, 2))
    log(f"   {name}: {tri_count(ob)} tris")
    return ob


def ray_surface(f, origins, dirs, tmax, inside_first=True, iters=40):
    """Distance along each ray (from inside, at t=0, to outside, at t=tmax) where f crosses zero."""
    lo = np.zeros(len(origins))
    hi = np.full(len(origins), tmax)
    for _ in range(iters):
        mid = 0.5 * (lo + hi)
        d = f(origins + dirs * mid[:, None])
        inside = d < 0 if inside_first else d > 0
        lo = np.where(inside, mid, lo)
        hi = np.where(inside, hi, mid)
    return 0.5 * (lo + hi)


def grid_mesh(name, G, matname, closed_u=False, cap=False):
    """Quads over a (nv, nu, 3) grid of points (rows along v)."""
    nv, nu = G.shape[:2]
    V = G.reshape(-1, 3)
    F = []
    for i in range(nv - 1):
        for j in range(nu if closed_u else nu - 1):
            j1 = (j + 1) % nu
            F.append((i * nu + j, i * nu + j1, (i + 1) * nu + j1, (i + 1) * nu + j))
    return V, F


class MeshBuilder:
    """Collects shells (verts, faces, material, per-vertex weights) for one skinned mesh."""

    def __init__(self):
        self.V, self.F, self.M, self.W = [], [], [], []
        self.nv = 0

    def add(self, V, F, matname, weights):
        V = np.asarray(V, float)
        self.V.append(V)
        self.F.extend([tuple(int(i) + self.nv for i in f) for f in F])
        self.M.extend([matname] * len(F))
        self.W.append(weights)
        self.nv += len(V)

    def add_object(self, ob, weights_fn):
        V = get_co(ob)
        F = [tuple(p.vertices) for p in ob.data.polygons]
        mname = ob.data.materials[0].name
        self.add(V, F, mname, weights_fn(V))
        me = ob.data
        bpy.data.objects.remove(ob)
        bpy.data.meshes.remove(me)

    def build(self, name, mirror=False):
        V = np.concatenate(self.V)
        F = self.F
        if mirror:
            V = V * np.array([-1.0, 1.0, 1.0])
            F = [tuple(reversed(f)) for f in F]
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(v) for v in V], [], F)
        me.validate()
        names = []
        for m in self.M:
            if m not in names:
                names.append(m)
        order = [k for k in MATS if k in names]
        for k in order:
            me.materials.append(MATS[k])
        idx = np.array([order.index(m) for m in self.M], np.int32)
        me.polygons.foreach_set("material_index", idx)
        sharpen(me, 60)
        ob = bpy.data.objects.new(name, me)
        scene.collection.objects.link(ob)
        # weights: dict bone -> per-vertex array, concatenated; keep the 4 strongest, renormalise
        bones = []
        for w in self.W:
            for b in w:
                if b not in bones:
                    bones.append(b)
        Wm = np.zeros((len(V), len(bones)))
        off = 0
        for Vs, w in zip(self.V, self.W):
            n = len(Vs)
            for b, arr in w.items():
                Wm[off:off + n, bones.index(b)] += np.broadcast_to(np.asarray(arr, float), (n,))
            off += n
        Wm = np.maximum(Wm, 0.0)
        Wm[Wm < 0.004] = 0.0
        order4 = np.argsort(-Wm, 1)
        mask = np.zeros_like(Wm, bool)
        np.put_along_axis(mask, order4[:, :4], True, 1)
        Wm = np.where(mask, Wm, 0.0)
        Wm /= np.maximum(Wm.sum(1, keepdims=True), 1e-9)
        for bi, b in enumerate(bones):
            vg = ob.vertex_groups.new(name=b)
            nz = np.nonzero(Wm[:, bi] > 0)[0]
            for i in nz:
                vg.add([int(i)], float(Wm[i, bi]), "REPLACE")
        return ob


def build_armature(name, bones):
    """bones: list of (name, head, tail, parent, connected, up_vector)."""
    arm = bpy.data.armatures.new(name)
    ob = bpy.data.objects.new(name, arm)
    scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for bn, head, tail, parent, conn, up in bones:
        eb = arm.edit_bones.new(bn)
        eb.head = Vector(head)
        eb.tail = Vector(tail)
        eb.use_deform = True
        if parent:
            eb.parent = arm.edit_bones[parent]
            eb.use_connect = conn
        eb.align_roll(Vector(up))
    bpy.ops.object.mode_set(mode="OBJECT")
    return ob


def skin_to(mesh_ob, arm_ob):
    mesh_ob.parent = arm_ob
    mod = mesh_ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_ob


def parent_to_bone(ob, arm_ob, bone, world):
    bpy.context.view_layer.update()
    ob.parent = arm_ob
    ob.parent_type = "BONE"
    ob.parent_bone = bone
    bpy.context.view_layer.update()
    ob.matrix_world = world
    bpy.context.view_layer.update()


def export(objs, path):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                             object_types={"ARMATURE", "MESH", "EMPTY"}, use_mesh_modifiers=True, mesh_smooth_type="FACE",
                             add_leaf_bones=False, use_armature_deform_only=True, primary_bone_axis="Y",
                             secondary_bone_axis="X", armature_nodetype="NULL", bake_anim=False, use_tspace=False)
    log("exported", path)


# ======================================================================================================
# ARM (canonical: left hand, thumb on -X)
# ======================================================================================================
EY, HY = -0.28, -0.525                   # elbow, wrist
FZ = 0.002                               # finger axis height above the wrist
FING = [("Index", -0.029, -0.098, 0.0088, [0.043, 0.025, 0.021]),
        ("Middle", -0.0095, -0.101, 0.0092, [0.047, 0.028, 0.022]),
        ("Ring", 0.0105, -0.099, 0.0087, [0.044, 0.027, 0.021]),
        ("Pinky", 0.029, -0.092, 0.0076, [0.034, 0.021, 0.018])]
TIP_EXT = 0.0035                         # fingertip beyond the distal segment length


def fdata(i):
    name, fx, fy, r, L = FING[i]
    J1, J2 = L[0], L[0] + L[1]
    st = J2 + L[2] + TIP_EXT
    sf = st - 0.0025                     # nail free edge
    sc = sf - {0: 0.0125, 1: 0.013, 2: 0.0122, 3: 0.0105}[i]     # cuticle
    return name, fx, fy, r, L, J1, J2, st, sc, sf


# Thumb: from the CMC joint (the old ThumbBase / Thumb1 origin), along the rest direction FirstPersonBody.cs
# asks for (out to the thumb side, forward, and down toward the palm), so its rest-pose correction is ~identity.
TC = np.array([-0.033, HY - 0.022, -0.006])
TA = unit([-0.515, -0.699, -0.496])
_nb = np.array([-0.62, 0.0, 0.78])
TB = unit(_nb - (_nb @ TA) * TA)         # thumbnail normal
TS = np.cross(TA, TB)
TL = [0.042, 0.032, 0.026]
TJ1, TJ2 = TL[0], TL[0] + TL[1]
TST = TJ2 + 0.030
TSF = TST - 0.0028
TSC = TSF - 0.0145


def fprof(s, r, J1, J2, st, sc):
    f = np.clip(s / st, 0.0, 1.0)
    W = r * (0.965 - 0.17 * f)
    W = W + r * (0.05 * gauss(s, J1, 0.0045) + 0.04 * gauss(s, J2, 0.0035)
                 - 0.025 * gauss(s, 0.5 * (J1 + J2), 0.005) - 0.02 * gauss(s, 0.55 * J1, 0.009))
    Td = 0.80 * W + r * (0.10 * gauss(s, J1, 0.0042) + 0.07 * gauss(s, J2, 0.0032) + 0.04 * gauss(s, 0.0, 0.006)
                         + 0.03 * gauss(s, sc - 0.0012, 0.0011))
    Tp = 0.86 * W + r * (0.07 * gauss(s, 0.45 * J1, 0.011) + 0.07 * gauss(s, 0.5 * (J1 + J2), 0.0055)
                         + 0.17 * gauss(s, J2 + 0.55 * (st - J2), 0.0065))
    Tp = Tp - r * (0.08 * gauss(s, J1 - 0.0013, 0.0010) + 0.08 * gauss(s, J1 + 0.0016, 0.0010)
                   + 0.08 * gauss(s, J2 + 0.0004, 0.0010) + 0.06 * gauss(s, 0.016, 0.0013))
    cw = -0.0005 * sstep(J2, st, s)
    nd = 2.05 + 0.45 * sstep(J2 - 0.002, J2 + 0.006, s)
    return W, Td, Tp, cw, nd, np.full_like(s, 2.0)


def finger_sdf(P, i):
    name, fx, fy, r, L, J1, J2, st, sc, sf = fdata(i)
    u = P[:, 0] - fx
    s = -(P[:, 1] - (HY + fy))
    w = P[:, 2] - FZ
    s0, L0, L1 = -0.014, 0.006, 0.0065
    ss = np.clip(s, s0 + L0, st - L1)
    W, Td, Tp, cw, nd, npp = fprof(ss, r, J1, J2, st, sc)
    return loft(u, w, s, W, Td, Tp, 0.0, cw, nd, npp, s0, L0, st, L1)


def thumb_local(P):
    q = P - TC
    return q @ TS, q @ TA, q @ TB      # u, s, w


def thumb_prof(s):
    W = spline(s, [-0.004, 0.012, 0.030, 0.042, 0.058, 0.074, 0.088, 0.104],
               [0.0105, 0.0118, 0.0118, 0.0114, 0.0100, 0.0105, 0.0105, 0.0098])
    Td = 0.78 * W + 0.0012 * gauss(s, TJ1, 0.005) + 0.0009 * gauss(s, TJ2, 0.004) + 0.0003 * gauss(s, TSC - 0.0013, 0.0012)
    Tp = 0.86 * W + 0.0018 * gauss(s, TJ2 + 0.016, 0.007) + 0.0008 * gauss(s, 0.058, 0.008)
    Tp = Tp - 0.0009 * gauss(s, TJ2 + 0.0005, 0.0012) - 0.0008 * gauss(s, TJ1 + 0.004, 0.0013)
    nd = 2.1 + 0.4 * sstep(TJ2 - 0.002, TJ2 + 0.008, s)
    return W, Td, Tp, nd


def thumb_sdf(P):
    u, s, w = thumb_local(P)
    s0, L0, L1 = -0.006, 0.008, 0.0085
    ss = np.clip(s, s0 + L0, TST - L1)
    W, Td, Tp, nd = thumb_prof(ss)
    return loft(u, w, s, W, Td, Tp, 0.0, 0.0, nd, np.full_like(s, 2.0), s0, L0, TST, L1)


_pc = np.array([0.004, HY - 0.045, -0.006]) - TC
TPW = unit(_pc - (_pc @ TA) * TA)                       # from the thumb axis toward the palm
THENAR_R = np.stack([TA, TPW, np.cross(TA, TPW)])


_wa = TC + TA * 0.034
_wb = np.array([-0.027, HY - 0.074, 0.001])
_we1 = unit(_wb - _wa)
_we3 = unit(np.cross(TA, [0.0, -1.0, 0.0]))
_we2 = np.cross(_we3, _we1)
WEB_R = np.stack([_we1, _we2, _we3])
WEB_C = 0.5 * (_wa + _wb) + _we2 * 0.004
WEB_L = 0.5 * np.linalg.norm(_wb - _wa) + 0.004


def thenar_sdf(P):
    c = TC + TA * 0.018 + TPW * 0.0105 - np.array([0, 0, 1.0]) * 0.0045
    d = ellipsoid(P, c, (0.029, 0.020, 0.0135), THENAR_R)
    return smin(d, ellipsoid(P, WEB_C, (WEB_L, 0.011, 0.0052), WEB_R), 0.008)                # first web


PS = [-0.02, 0.0, 0.025, 0.05, 0.075, 0.095]
PXMIN = [-0.028, -0.028, -0.032, -0.0355, -0.0375, -0.0375]
PXMAX = [0.028, 0.029, 0.0335, 0.036, 0.0365, 0.0345]
PZD = [0.0175, 0.0165, 0.0145, 0.0128, 0.0118, 0.0112]
PZP = [-0.019, -0.018, -0.0165, -0.0148, -0.0136, -0.0128]


def palm_sdf(P):
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    s = -(y - HY)
    s0, L0, s1, L1 = -0.03, 0.01, 0.096, 0.012
    ss = np.clip(s, s0 + L0, s1 - L1)
    xmin, xmax = spline(ss, PS, PXMIN), spline(ss, PS, PXMAX)
    zd, zp = spline(ss, PS, PZD), spline(ss, PS, PZP)
    W = 0.5 * (xmax - xmin)
    cu = 0.5 * (xmax + xmin)
    cw = 0.5 * (zd + zp)
    T = 0.5 * (zd - zp)
    d = loft(x, z, s, W, T, T, cu, cw, 2.5, 2.5, s0, L0, s1, L1, arch=0.0035)
    for i in range(4):
        name, fx, fy, r, L, *_ = fdata(i)
        d = smin(d, ellipsoid(P, (fx, HY + fy + 0.002, 0.0040), (r * 0.98, 0.0105, r * 0.95)), 0.006)      # knuckle
        d = smin(d, ellipsoid(P, (fx, HY + fy + 0.012, -0.0050), (0.0105, 0.015, 0.0068)), 0.012)          # pad
        d = smin(d, capsule(P, (fx * 0.45, HY - 0.02, 0.0132), (fx, HY + fy + 0.014, 0.0090), 0.0026), 0.009)  # tendon
    for i in range(3):
        a, b = fdata(i), fdata(i + 1)
        xm, ym = 0.5 * (a[1] + b[1]), 0.5 * (a[2] + b[2])
        d = smin(d, ellipsoid(P, (xm, HY + ym - 0.004, -0.001), (0.0085, 0.010, 0.0055)), 0.004)            # web
    d = smin(d, ellipsoid(P, (0.0255, HY - 0.036, -0.0095), (0.0115, 0.032, 0.0105)), 0.009)               # hypothenar
    d = smax(d, -ellipsoid(P, (0.002, HY - 0.056, -0.0255), (0.019, 0.026, 0.0135)), 0.007)                # hollow
    return d


FT = [-0.08, 0.0, 0.05, 0.10, 0.15, 0.20, 0.235, 0.26]
FA = [0.043, 0.044, 0.043, 0.039, 0.0345, 0.031, 0.0295, 0.029]
FCU = [0.0, -0.001, -0.002, -0.0015, -0.001, 0.0, 0.0, 0.0]
FBT = [0.039, 0.039, 0.036, 0.030, 0.0245, 0.0205, 0.019, 0.018]
FBB = [0.039, 0.039, 0.037, 0.032, 0.026, 0.021, 0.0195, 0.019]
FCW = [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, -0.0005, -0.0008]
FN = [2.1, 2.1, 2.15, 2.2, 2.3, 2.45, 2.5, 2.5]


def forearm_sdf(P):
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    t = EY - y
    s0, L0, s1, L1 = -0.09, 0.01, 0.275, 0.015
    tt = np.clip(t, s0 + L0, s1 - L1)
    n = spline(tt, FT, FN)
    d = loft(x, z, t, spline(tt, FT, FA), spline(tt, FT, FBT), spline(tt, FT, FBB), spline(tt, FT, FCU),
             spline(tt, FT, FCW), n, n, s0, L0, s1, L1)
    d = smin(d, ellipsoid(P, (0.021, HY + 0.012, 0.0105), (0.0065, 0.008, 0.006)), 0.006)                 # ulnar head
    return d


def arm_sdf(P, parts=False):
    fore = forearm_sdf(P)
    palm = palm_sdf(P)
    hand = smin(fore, palm, 0.014)
    s = -(P[:, 1] - HY)
    hand = hand + 0.0006 * gauss(s, -0.002, 0.0022) * sstep(-0.006, -0.014, P[:, 2])                        # wrist crease
    thumb = smin(thumb_sdf(P), thenar_sdf(P), 0.008)
    d = smin(hand, thumb, 0.011)
    fs = [finger_sdf(P, i) for i in range(4)]
    for f in fs:
        d = np.minimum(d, smin(f, palm, 0.0035))
    if parts:
        return d, {"palm": palm, "thumb": thumb, "f0": fs[0], "f1": fs[1], "f2": fs[2], "f3": fs[3], "hand": hand}
    return d


Y_CUT = -0.315


def skin_weights(V):
    """Analytic skin weights (canonical coordinates)."""
    _, parts = arm_sdf(V, parts=True)
    x, y, z = V[:, 0], V[:, 1], V[:, 2]
    sh = -(y - HY)
    w_hand = sstep(-0.010, 0.014, sh)
    keys = ["palm", "thumb", "f0", "f1", "f2", "f3"]
    D = np.stack([np.maximum(parts[k], 0.0) for k in keys], 1)
    m = np.exp(-(D - D.min(1, keepdims=True)) / 0.0008)
    m /= m.sum(1, keepdims=True)
    W = {"Forearm": 1.0 - w_hand}
    acc = {}

    def add(b, v):
        acc[b] = acc.get(b, 0.0) + v

    add("Hand", m[:, 0])
    # thumb chain
    q = V - TC
    t = q @ TA
    sC = sstep(-0.010, 0.014, t)
    s1 = sstep(TJ1 - 0.008, TJ1 + 0.006, t)
    s2 = sstep(TJ2 - 0.005, TJ2 + 0.005, t)
    add("Hand", m[:, 1] * (1 - sC))
    add("ThumbBase", m[:, 1] * 0.35 * (sC - s1))
    add("Thumb1", m[:, 1] * 0.65 * (sC - s1))
    add("Thumb2", m[:, 1] * (s1 - s2))
    add("Thumb3", m[:, 1] * s2)
    for i in range(4):
        name, fx, fy, r, L, J1, J2, st, sc, sf = fdata(i)
        t = -(y - (HY + fy))
        c1 = 0.005 * sstep(0.002, -0.006, z - FZ)
        a1 = sstep(-0.010 + c1, 0.009 + c1, t)
        a2 = sstep(J1 - 0.0055, J1 + 0.0055, t)
        a3 = sstep(J2 - 0.0045, J2 + 0.0045, t)
        mm = m[:, 2 + i]
        add("Hand", mm * (1 - a1))
        add(name + "1", mm * (a1 - a2))
        add(name + "2", mm * (a2 - a3))
        add(name + "3", mm * a3)
    for b, v in acc.items():
        W[b] = W.get(b, 0.0) + w_hand * v
    return W


def arm_profile(y):
    """Skin ellipse (cu, cw, A, B) of the arm at height y (upper arm above the elbow, forearm below)."""
    y = np.asarray(y, float)
    t = EY - y
    fa, fcu = spline(t, FT, FA), spline(t, FT, FCU)
    fb = 0.5 * (spline(t, FT, FBT) + spline(t, FT, FBB))
    v = -y
    UY = [-0.05, 0.0, 0.06, 0.14, 0.22, 0.28]
    ua = spline(v, UY, [0.040, 0.050, 0.049, 0.046, 0.043, 0.044])
    ub = spline(v, UY, [0.040, 0.048, 0.047, 0.045, 0.041, 0.039])
    lower = y < EY
    return np.where(lower, fcu, 0.0), np.zeros_like(y), np.where(lower, fa, ua), np.where(lower, fb, ub)


def sleeve_mesh():
    NA = 30
    th = np.arange(NA) / NA * 2 * math.pi
    rings = []
    for k in range(1, 6):                                # dome over the shoulder (pole added separately)
        phi = math.pi / 2 * (1 - k / 6)
        rings.append((0.05 * math.sin(phi), 0.008, math.cos(phi), True))
    for y in [0.0, -0.035, -0.07, -0.11, -0.15, -0.19, -0.225, -0.25]:
        rings.append((y, 0.008, 1.0, True))
    for y in np.linspace(-0.262, -0.333, 13):
        rings.append((float(y), 0.0072, 1.0, True))
    for y, off in [(-0.337, 0.0092), (-0.341, 0.0108), (-0.346, 0.0116), (-0.351, 0.0108), (-0.3545, 0.0104),
                   (-0.358, 0.0112), (-0.363, 0.0106), (-0.3665, 0.0086), (-0.3688, 0.0058), (-0.3685, 0.0030),
                   (-0.366, 0.0017), (-0.358, 0.0014), (-0.345, 0.0013), (-0.33, 0.0013), (-0.305, 0.0013)]:
        rings.append((y, off, 1.0, off > 0.004))
    G = []
    for idx, (y, off, sc, outer) in enumerate(rings):
        cu, cw, A, B = [float(v) for v in arm_profile(np.array([y]))]
        o = np.full(NA, off)
        if outer and idx < 26:
            if y > -0.25:
                o += 0.0011 * np.sin(3 * th + 14 * y + 0.6) + 0.0007 * np.sin(5 * th - 20 * y + 1.0)
            else:
                env = sstep(-0.245, -0.27, y) * sstep(-0.336, -0.325, y)
                o += 0.0026 * env * np.sin(2 * math.pi * (y + 0.25) / 0.023 + 1.3 * np.sin(2 * th + 0.5) + 0.7 * np.sin(3 * th + 1.9))
                o += 0.0008 * np.sin(4 * th + 30 * y)
        elif outer:
            o += 0.0006 * np.sin(4 * th + 2.0) + 0.0004 * np.sin(7 * th + 1.0)
        X = cu + (A + o) * np.cos(th) * sc
        Z = cw + (B + o) * np.sin(th) * sc
        G.append(np.stack([X, np.full(NA, y), Z], 1))
    G = np.stack(G)
    V, F = grid_mesh("sleeve", G, "Shirt", closed_u=True)
    V = np.concatenate([V, [[0.0, 0.05, 0.0]]])
    pole = len(V) - 1
    F = F + [(pole, (j + 1) % NA, j) for j in range(NA)]
    # outward check on an upper-arm face
    i0 = 8 * NA
    f = F[i0]
    nm = np.cross(V[f[1]] - V[f[0]], V[f[2]] - V[f[0]])
    c = V[list(f)].mean(0)
    if nm @ np.array([c[0], 0, c[2]]) < 0:
        F = [tuple(reversed(f)) for f in F]
    y = V[:, 1]
    w_root = 0.5 * sstep(0.0, 0.05, y)
    w_fore = sstep(-0.235, -0.30, y)
    return V, F, {"LeftArm" if LEFT else "RightArm": w_root, "UpperArm": (1 - w_root) * (1 - w_fore), "Forearm": w_fore}


def nail_mesh(origin, side, axis, back, Wn, s_c, s_f, s_lim, f):
    """Nail plate: a thin closed slab following the fingertip's dorsal surface, sunk into the folds at its edges."""
    nu, nt = 7, 8
    a = np.linspace(-1, 1, nu)
    t = np.linspace(0, 1, nt)
    A, Tt = np.meshgrid(a, t)
    U = A * Wn * (0.80 + 0.20 * sstep(0.0, 0.35, Tt))
    Sg = s_c + Tt * (s_f - s_c) + 0.0019 * A * A * (1 - Tt) - 0.0012 * A * A * Tt
    Sq = np.minimum(Sg, s_lim)
    org = origin + U.ravel()[:, None] * side + Sq.ravel()[:, None] * axis - 0.004 * back
    h = ray_surface(f, org, np.broadcast_to(back, org.shape), 0.03) - 0.004
    h = h.reshape(U.shape)
    ex = np.maximum(Sg - s_lim, 0.0)
    h = h - 0.05 * ex - 12.0 * ex * ex
    lift = 0.00032 - 0.00052 * np.abs(A) ** 4 - 0.00055 * gauss(Tt, 0.0, 0.13)
    top = origin + U[..., None] * side + Sg[..., None] * axis + (h + lift)[..., None] * back
    bot = top - 0.00055 * back
    nv = nu * nt
    V = np.concatenate([top.reshape(-1, 3), bot.reshape(-1, 3)])
    F = []
    for i in range(nt - 1):
        for j in range(nu - 1):
            q = (i * nu + j, i * nu + j + 1, (i + 1) * nu + j + 1, (i + 1) * nu + j)
            F.append(q)
            F.append(tuple(reversed([k + nv for k in q])))
    ring = [j for j in range(nu)] + [i * nu + nu - 1 for i in range(1, nt)] + \
           [(nt - 1) * nu + j for j in range(nu - 2, -1, -1)] + [i * nu for i in range(nt - 2, 0, -1)]
    for k in range(len(ring)):
        p, q = ring[k], ring[(k + 1) % len(ring)]
        F.append((q, p, p + nv, q + nv))
    # orient: top faces along +back
    f0 = F[0]
    nm = np.cross(V[f0[1]] - V[f0[0]], V[f0[2]] - V[f0[0]])
    if nm @ back < 0:
        F = [tuple(reversed(x)) for x in F]
    return V, F


def band_mesh(center, axis, e1, e2, f, s_half, prof, NA=28, rmax=0.03, gap=0.0004):
    """A band around an axis (ring, strap): profile = list of (along, out) points around the cross-section,
    'out' measured from the skin surface found by ray marching in each direction."""
    th = np.arange(NA) / NA * 2 * math.pi
    G = []
    for along, out in prof:
        c = center + axis * along
        dirs = np.cos(th)[:, None] * e1 + np.sin(th)[:, None] * e2
        r = ray_surface(f, np.repeat(c[None], NA, 0), dirs, rmax)
        G.append(c + dirs * (r + gap + out)[:, None])
    G = np.stack(G)
    V, F = grid_mesh("band", G, "Ring", closed_u=True)
    # closed profile: connect last ring to first
    np_ = len(prof)
    F = F + [((np_ - 1) * NA + j, (np_ - 1) * NA + (j + 1) % NA, (j + 1) % NA, j) for j in range(NA)]
    # orient outward
    fq = F[NA // 2]
    nm = np.cross(V[fq[1]] - V[fq[0]], V[fq[2]] - V[fq[0]])
    cc = V[list(fq)].mean(0) - center
    cc -= (cc @ axis) * axis
    return V, F if nm @ cc > 0 else [tuple(reversed(x)) for x in F]


def revolve(prof, seg, z0=0.0):
    """Closed solid of revolution about +Z: prof = [(r, z), ...] from the bottom centre to the top centre."""
    bm = bmesh.new()
    rings = []
    for r, z in prof:
        if r < 1e-7:
            rings.append([bm.verts.new((0, 0, z + z0))])
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * k / seg), r * math.sin(2 * math.pi * k / seg), z + z0)) for k in range(seg)])
    for a, b in zip(rings[:-1], rings[1:]):
        if len(a) == 1:
            for k in range(seg):
                bm.faces.new((a[0], b[(k + 1) % seg], b[k]))
        elif len(b) == 1:
            for k in range(seg):
                bm.faces.new((a[k], a[(k + 1) % seg], b[0]))
        else:
            for k in range(seg):
                bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def bm_box(bm, centre, half, R=None, bevel=0.0):
    tmp = bmesh.new()
    bmesh.ops.create_cube(tmp, size=2.0)
    for v in tmp.verts:
        v.co = Vector((v.co.x * half[0], v.co.y * half[1], v.co.z * half[2]))
    if bevel > 0:
        bmesh.ops.bevel(tmp, geom=tmp.edges[:] + tmp.verts[:], offset=bevel, segments=2, affect="EDGES", profile=0.5)
    M = Matrix.Translation(Vector(centre)) @ (R.to_4x4() if R is not None else Matrix.Identity(4))
    for v in tmp.verts:
        v.co = M @ v.co
    me = bpy.data.meshes.new("tmp")
    tmp.to_mesh(me)
    tmp.free()
    bm.from_mesh(me)
    bpy.data.meshes.remove(me)


WATCH_O = np.array([0.0, -0.495, 0.027])


def watch_case_object():
    """The case (origin = the old WatchCase position, back of the wrist) with the dial as a second material."""
    bm = revolve([(0.0, -0.0055), (0.0172, -0.0055), (0.0192, -0.0042), (0.0199, -0.0015), (0.0199, 0.0012),
                  (0.0194, 0.0030), (0.0184, 0.0041), (0.0168, 0.0043), (0.0161, 0.0026), (0.0, 0.0026)], 32)
    case_faces = len(bm.faces)
    # dial: a thin disc just above the recess
    dial = revolve([(0.0, 0.0020), (0.0161, 0.0020), (0.0161, 0.0029), (0.0, 0.0029)], 32)
    me_d = bpy.data.meshes.new("d")
    dial.to_mesh(me_d)
    dial.free()
    bm.from_mesh(me_d)
    bpy.data.meshes.remove(me_d)
    n_dial = len(bm.faces)
    # lugs (12 and 6 o'clock along the strap, +-x), crown at 3 (toward the hand, -y)
    for sx in (-1, 1):
        for sy in (-1, 1):
            bm_box(bm, (sx * 0.0205, sy * 0.0074, -0.0022), (0.0042, 0.0016, 0.0022))
    crown = revolve([(0.0, 0.0), (0.0022, 0.0), (0.0023, 0.0012), (0.0023, 0.0030), (0.0, 0.0031)], 12)
    me_c = bpy.data.meshes.new("c")
    crown.to_mesh(me_c)
    crown.free()
    me_c.transform(Matrix.Translation((0, -0.0195, -0.0008)) @ Matrix.Rotation(math.radians(90), 4, "X"))
    bm.from_mesh(me_c)
    bpy.data.meshes.remove(me_c)
    # hour markers and hands on the dial (case metal); 12 o'clock toward +x
    for k in range(12):
        a = 2 * math.pi * k / 12
        R = Matrix.Rotation(-a, 3, "Z")
        L = 0.0016 if k % 3 else 0.0026
        c = R @ Vector((0.0132 - L / 2, 0, 0.0031))
        bm_box(bm, c, (L / 2, 0.00045 if k % 3 else 0.0007, 0.00025), R=R)
    for ang, L, wdt in [(math.radians(-62), 0.0085, 0.0007), (math.radians(-200), 0.0124, 0.0005)]:
        R = Matrix.Rotation(ang, 3, "Z")
        bm_box(bm, R @ Vector((L / 2 - 0.0015, 0, 0.0035)), (L / 2, wdt, 0.0002), R=R)
    bm.faces.ensure_lookup_table()
    me = bpy.data.meshes.new("WatchCase")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(MATS["WatchCase"])
    me.materials.append(MATS["WatchFace"])
    mi = np.zeros(len(me.polygons), np.int32)
    mi[case_faces:n_dial] = 1
    me.polygons.foreach_set("material_index", mi)
    sharpen(me, 40)
    ob = bpy.data.objects.new("WatchCase", me)
    scene.collection.objects.link(ob)
    return ob


def build_arm():
    log("ARM", "left" if LEFT else "right")
    mb = MeshBuilder()
    # ---- skin
    lo = (-0.112, -0.745, -0.082)
    hi = (0.062, Y_CUT - 0.004, 0.052)
    V, Q = polygonize(arm_sdf, lo, hi, RES)
    V, Q = compact(V, Q, V[Q][:, :, 1].max(1) < Y_CUT)
    skin = new_obj("Skin", V, Q, "Skin")
    decimate(skin, 7600)
    set_co(skin, project(arm_sdf, get_co(skin), RES, 2))
    log("   skin tris", tri_count(skin))
    mb.add_object(skin, skin_weights)
    # ---- sleeve
    V, F, W = sleeve_mesh()
    mb.add(V, F, "Shirt", W)
    # ---- nails
    up = np.array([0.0, 0.0, 1.0])
    for i in range(4):
        name, fx, fy, r, L, J1, J2, st, sc, sf = fdata(i)
        origin = np.array([fx, HY + fy, FZ])
        Wf = fprof(np.array([0.5 * (sc + sf)]), r, J1, J2, st, sc)[0][0]
        V, F = nail_mesh(origin, np.array([1.0, 0, 0]), np.array([0, -1.0, 0]), up, 0.70 * Wf, sc, sf, st - 0.0068,
                         lambda P, i=i: finger_sdf(P, i))
        mb.add(V, F, "Nail", {name + "3": 1.0})
    Wt = thumb_prof(np.array([0.5 * (TSC + TSF)]))[0][0]
    V, F = nail_mesh(TC, TS, TA, TB, 0.72 * Wt, TSC, TSF, TST - 0.0088, thumb_sdf)
    mb.add(V, F, "Nail", {"Thumb3": 1.0})
    # (No ring and no watch: the player wanted a bare hand. An empty named WatchCase stays on the back of the wrist,
    # where the code measures the back of the forearm from.)
    mesh = mb.build("ArmMesh", mirror=not LEFT)
    # ---- a marker on the back of the wrist (FirstPersonBody finds the back of the forearm from it)
    case = bpy.data.objects.new("WatchCase", None)
    case.empty_display_size = 0.01
    scene.collection.objects.link(case)
    # ---- armature (heads at the joints of make_player.py)
    root = "LeftArm" if LEFT else "RightArm"
    X = lambda p: (p[0] * S, p[1], p[2])
    up = (0, 0, 1)
    bones = [(root, (0, 0, 0), (0, 0, 0.04), None, False, (0, -1, 0)),
             ("UpperArm", (0, 0, 0), (0, EY, 0), root, False, up),
             ("Forearm", (0, EY, 0), (0, HY, 0), "UpperArm", True, up),
             ("Hand", (0, HY, 0), X((-0.0095, HY - 0.101, FZ)), "Forearm", True, up)]
    for i in range(4):
        name, fx, fy, r, L, J1, J2, st, sc, sf = fdata(i)
        heads = [0.0, J1, J2, J2 + L[2]]
        for k in range(3):
            bones.append((f"{name}{k + 1}", X((fx, HY + fy - heads[k], FZ)), X((fx, HY + fy - heads[k + 1], FZ)),
                          "Hand" if k == 0 else f"{name}{k}", k > 0, up))
    tb = tuple(TB)
    bones.append(("ThumbBase", X(TC), X(TC + TA * 0.015), "Hand", False, X(tb)))
    th = [0.0, TJ1, TJ2, TJ2 + TL[2]]
    for k in range(3):
        bones.append((f"Thumb{k + 1}", X(TC + TA * th[k]), X(TC + TA * th[k + 1]), "ThumbBase" if k == 0 else f"Thumb{k}",
                      k > 0, X(tb)))
    rig = build_armature("ArmRig", bones)
    skin_to(mesh, rig)
    parent_to_bone(case, rig, "Forearm", Matrix.Translation(Vector(WATCH_O)))
    log("ARM tris", tri_count(mesh))
    return rig, [rig, mesh, case]


# ======================================================================================================
# BODY (Blender: character faces -Y, ThighL at x = -0.1 like make_player.py)
# ======================================================================================================
ZS = [0.90, 0.97, 1.02, 1.10, 1.20, 1.30, 1.38, 1.44]
TX = [0.148, 0.150, 0.160, 0.163, 0.166, 0.172, 0.176, 0.165]
TYF = [0.098, 0.100, 0.110, 0.116, 0.112, 0.110, 0.104, 0.090]
TYB = [0.092, 0.093, 0.097, 0.098, 0.100, 0.104, 0.104, 0.092]
NECK = np.array([0.0, 0.012])


def shirt_sdf(P):
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    s0, L0, s1, L1 = 0.88, 0.02, 1.50, 0.06
    zz = np.clip(z, s0 + L0, s1 - L1)
    d = loft(x, -y, z, spline(zz, ZS, TX), spline(zz, ZS, TYF), spline(zz, ZS, TYB), 0.0, 0.0, 2.3, 2.3, s0, L0, s1, L1)
    for sx in (-1, 1):
        d = smin(d, round_cone(P, (0, 0.010, 1.425), (sx * 0.172, 0.010, 1.402), 0.055, 0.066), 0.035)
        d = smin(d, round_cone(P, (sx * 0.183, 0.006, 1.402), (sx * 0.212, 0.0, 1.335), 0.066, 0.06), 0.02)
    th = np.arctan2(-y, x)
    # neck opening and collar band
    rn = np.sqrt(x * x + (y - NECK[1]) ** 2)
    hole = smax(rn - 0.058, 1.40 - z, 0.01)
    d = smax(d, -hole, 0.004)
    d = smin(d, np.sqrt((rn - 0.064) ** 2 + (z - 1.458) ** 2) - 0.0062, 0.004)
    front = sstep(0.0, -0.05, y)
    # placket, chest pocket (wearer's left = +x), blousing over the belt, drag folds
    d -= 0.0016 * sstep(0.0175, 0.0140, np.abs(x)) * sstep(0.985, 1.0, z) * sstep(1.45, 1.43, z) * front
    d += 0.0005 * gauss(np.abs(x), 0.0172, 0.0011) * sstep(0.985, 1.0, z) * sstep(1.45, 1.43, z) * front
    pk = sstep(0.043, 0.047, x) * sstep(0.157, 0.153, x) * sstep(1.205, 1.21, z) * sstep(1.355, 1.35, z) * front
    d -= 0.0012 * pk
    d += 0.0008 * gauss(z, 1.322, 0.0016) * sstep(0.043, 0.047, x) * sstep(0.157, 0.153, x) * front
    d += 0.0022 * gauss(z, 1.003, 0.022) * np.sin(9 * th + 2.2 * np.sin(2 * th))
    d += 0.0010 * gauss(z, 1.12, 0.06) * np.sin(5 * th + 25 * z)
    d += 0.0009 * gauss(z, 1.30, 0.08) * np.sin(3 * th + 40 * z + 1.0) * (1 - front)
    return d


PZ = [0.76, 0.80, 0.86, 0.92, 0.96, 0.995]
PX = [0.150, 0.166, 0.176, 0.174, 0.166, 0.162]
PYF = [0.085, 0.100, 0.108, 0.110, 0.107, 0.105]
PYB = [0.100, 0.115, 0.122, 0.112, 0.102, 0.098]


def pelvis_sdf(P, grow=0.0):
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    s0, L0, s1, L1 = 0.74, 0.05, 0.998, 0.006
    zz = np.clip(z, s0 + L0, s1 - L1)
    return loft(x, -y, z, spline(zz, PZ, PX) + grow, spline(zz, PZ, PYF) + grow, spline(zz, PZ, PYB) + grow,
                0.0, 0.0, 2.4, 2.4, s0, L0, s1, L1)


def pants_sdf(P):
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    pel = pelvis_sdf(P)
    legs = []
    for sx in (-1, 1):
        lx = sx * 0.097
        thigh = round_cone(P, (lx, 0.0, 0.89), (sx * 0.10, -0.004, 0.50), 0.089, 0.068)
        shin = round_cone(P, (sx * 0.10, -0.004, 0.50), (sx * 0.10, 0.004, 0.03), 0.066, 0.058)
        leg = smin(thigh, shin, 0.02)
        th = np.arctan2(-y, x - sx * 0.10)
        leg += 0.0022 * gauss(z, 0.12, 0.035) * np.sin(2 * math.pi * z / 0.034 + 1.5 * np.sin(2 * th) + sx)
        leg += 0.0016 * gauss(z, 0.53, 0.035) * sstep(0.0, 0.04, y) * np.sin(2 * math.pi * z / 0.028 + np.sin(3 * th))
        leg += 0.0012 * gauss(z, 0.30, 0.10) * np.sin(3 * th + 9 * z + sx)
        leg = smin(leg, pel, 0.03)
        hem = 0.064 - 0.25 * y
        legs.append(smax(leg, hem - z, 0.004))
    d = np.minimum(legs[0], legs[1])
    front = sstep(0.0, -0.04, y)
    d -= 0.0012 * sstep(0.952, 0.957, z)                                                       # waistband
    d -= 0.0013 * sstep(-0.006, -0.002, x) * sstep(0.034, 0.030, x) * sstep(0.80, 0.82, z) * sstep(0.955, 0.95, z) * front  # fly
    # front pocket openings
    ax_, az_, bx_, bz_ = 0.105, 0.955, 0.168, 0.855
    ex, ez = bx_ - ax_, bz_ - az_
    hh = np.clip(((np.abs(x) - ax_) * ex + (z - az_) * ez) / (ex * ex + ez * ez), 0, 1)
    dl = np.sqrt((np.abs(x) - ax_ - hh * ex) ** 2 + (z - az_ - hh * ez) ** 2)
    d += 0.0012 * gauss(dl, 0.0, 0.0018) * front
    return d


BELT_Z = (0.9575, 0.9895)


def belt_sdf(P):
    z = P[:, 2]
    outer = pelvis_sdf(P, 0.0052)
    inner = pelvis_sdf(P, 0.0002)
    slab = np.abs(z - 0.5 * (BELT_Z[0] + BELT_Z[1])) - 0.5 * (BELT_Z[1] - BELT_Z[0])
    return smax(smax(outer, -inner, 0.0005), slab, 0.0015)


SL = [-0.085, -0.06, -0.02, 0.03, 0.08, 0.12, 0.16, 0.20]
SHW = [0.030, 0.035, 0.036, 0.039, 0.045, 0.045, 0.038, 0.026]
SCU = [0.0, 0.0, 0.0, 0.002, 0.004, 0.004, 0.002, -0.002]
SHT_L = [-0.09, -0.06, -0.02, 0.02, 0.06, 0.10, 0.14, 0.18, 0.215]
SHT = [0.068, 0.090, 0.086, 0.098, 0.084, 0.068, 0.054, 0.045, 0.036]


def toe_spring(l):
    return 0.009 * sstep(0.13, 0.225, l) ** 1.3


def shoe_frame(P, fx):
    m = -np.sign(fx)
    return (P[:, 0] - fx) * m, -P[:, 1], P[:, 2]


def shoe_sdf(P, fx):
    u, l, h = shoe_frame(P, fx)
    s0, L0, s1, L1 = -0.092, 0.022, 0.214, 0.050
    ll = np.clip(l, s0 + L0, s1 - L1)
    hw = spline(ll, SL, SHW)
    cu = spline(ll, SL, SCU)
    ht = spline(np.clip(l, SHT_L[0], SHT_L[-1]), SHT_L, SHT) + toe_spring(l) * 0.5
    hb = 0.016 + toe_spring(l)
    T = 0.5 * (ht - hb)
    d = loft(u, h, l, hw, T, T, cu, 0.5 * (ht + hb), 2.0, 3.0, s0, L0, s1, L1)
    X = np.abs(u - cu) / hw
    topz = 0.5 * (ht + hb) + T * np.clip(1 - X ** 2.0, 0, 1) ** 0.5
    lace = sstep(0.025, 0.035, l) * sstep(0.115, 0.105, l)
    # eyelet stays (two ridges), the lace opening between them, laces across
    d -= 0.0011 * gauss(np.abs(u - cu), 0.0125, 0.003) * lace
    d += 0.0010 * sstep(0.010, 0.006, np.abs(u - cu)) * lace
    for k in range(5):
        lk = 0.040 + 0.016 * k
        d = smin(d, np.sqrt(((np.abs(u - cu) - 0.013).clip(0)) ** 2 + (l - lk) ** 2 + (h - topz - 0.0004) ** 2) - 0.0018, 0.002)
    # toe cap seam, flex creases, heel counter seam
    d += 0.0008 * gauss(l - 0.135 - 0.25 * (u - cu) ** 2 / 0.04, 0.0, 0.0014)
    d += 0.0006 * gauss(l, 0.115, 0.012) * np.sin(2 * math.pi * l / 0.009 + 40 * u) * sstep(0.03, 0.05, h)
    d += 0.0006 * gauss(h, 0.05, 0.002) * sstep(0.0, -0.03, l)
    return d


def sole_sdf(P, fx):
    u, l, h = shoe_frame(P, fx)
    s0, L0, s1, L1 = -0.096, 0.012, 0.219, 0.032
    ll = np.clip(l, s0 + L0, s1 - L1)
    hw = spline(ll, SL, SHW) + 0.0045
    cu = spline(ll, SL, SCU)
    zb = toe_spring(ll)
    thick = 0.019 + 0.008 * sstep(0.0, -0.01, ll)
    T = 0.5 * thick
    d = loft(u, h, l, hw, T, T, cu, zb + T, 3.6, 3.6, s0, L0, s1, L1)
    d += 0.0006 * gauss(h - zb, 0.008, 0.0012)                                                  # sole edge groove
    return d


def body_weights(kind):
    def fn(V):
        x, y, z = V[:, 0], V[:, 1], V[:, 2]
        if kind == "shirt":
            t = sstep(0.95, 1.10, z)
            return {"Torso": t, "Hips": 1 - t}
        if kind == "torso":
            return {"Torso": 1.0}
        if kind == "hips":
            return {"Hips": 1.0}
        if kind in ("footL", "footR"):
            return {"Foot" + kind[-1]: 1.0}
        # pants
        wL = sstep(0.02, -0.02, x)
        t = 0.93 - z
        leg = sstep(0.0, 0.15, t)
        knee = sstep(0.38, 0.48, t)
        return {"Hips": 1 - leg, "ThighL": leg * (1 - knee) * wL, "ShinL": leg * knee * wL,
                "ThighR": leg * (1 - knee) * (1 - wL), "ShinR": leg * knee * (1 - wL)}
    return fn


def build_body():
    log("BODY")
    mb = MeshBuilder()
    shirt = sdf_shell("Shirt", shirt_sdf, (-0.26, -0.16, 0.86), (0.26, 0.15, 1.54), 0.0028, 4600, "Shirt")
    mb.add_object(shirt, body_weights("shirt"))
    pants = sdf_shell("Pants", pants_sdf, (-0.235, -0.17, 0.02), (0.235, 0.16, 1.01), 0.0028, 6000, "Pants")
    mb.add_object(pants, body_weights("pants"))
    belt = sdf_shell("Belt", belt_sdf, (-0.2, -0.14, 0.95), (0.2, 0.14, 0.997), 0.0011, 900, "Shoes")
    mb.add_object(belt, body_weights("hips"))
    for fx, side in ((-0.1, "L"), (0.1, "R")):
        sh = sdf_shell("Shoe" + side, lambda P, fx=fx: shoe_sdf(P, fx), (fx - 0.065, -0.235, 0.008), (fx + 0.065, 0.105, 0.115),
                       0.0018, 1700, "Shoes")
        mb.add_object(sh, body_weights("foot" + side))
        so = sdf_shell("Sole" + side, lambda P, fx=fx: sole_sdf(P, fx), (fx - 0.065, -0.235, -0.006), (fx + 0.065, 0.11, 0.04),
                       0.0018, 700, "Sole")
        mb.add_object(so, body_weights("foot" + side))
    # neck stub (hidden by the camera, closes the collar)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=8, radius=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * 0.056, NECK[1] + v.co.y * 0.052, 1.45 + v.co.z * 0.06))
    V = np.array([v.co[:] for v in bm.verts])
    F = [tuple(v.index for v in f.verts) for f in bm.faces]
    bm.free()
    mb.add(V, F, "Skin", {"Torso": 1.0})
    # buttons down the placket
    for zb in (1.07, 1.16, 1.25, 1.34, 1.43):
        o = np.array([[0.0, -0.2, zb]])
        tt = ray_surface(lambda P: -shirt_sdf(P), o, np.array([[0, 1.0, 0]]), 0.2)[0]
        c = o[0] + np.array([0, tt + 0.0003, 0])
        bmb = revolve([(0.0, 0.0), (0.0058, 0.0), (0.0060, 0.0012), (0.0050, 0.0021), (0.0, 0.0019)], 12)
        me = bpy.data.meshes.new("b")
        bmb.to_mesh(me)
        bmb.free()
        V = np.array([(Matrix.Translation(Vector(c)) @ Matrix.Rotation(math.radians(90), 4, "X") @ v.co)[:] for v in me.vertices])
        F = [tuple(p.vertices) for p in me.polygons]
        bpy.data.meshes.remove(me)
        mb.add(V, F, "Sole", {"Torso": 1.0})
    # belt loops (trouser fabric) and buckle
    zc = 0.5 * (BELT_Z[0] + BELT_Z[1])
    for ang in (-0.62, 0.62, -1.45, 1.45, math.pi):
        dvec = np.array([[math.sin(ang), -math.cos(ang), 0.0]])
        o = np.array([[0, 0, zc]])
        r = ray_surface(lambda P: pelvis_sdf(P), o, dvec, 0.3)[0]
        p = o[0] + dvec[0] * (r + 0.0072)
        bm = bmesh.new()
        R = Matrix.Rotation(ang, 3, "Z")
        bm_box(bm, p, (0.0055, 0.0016, 0.0215), R=R, bevel=0.0008)
        me = bpy.data.meshes.new("l")
        bm.to_mesh(me)
        bm.free()
        mb.add(np.array([v.co[:] for v in me.vertices]), [tuple(q.vertices) for q in me.polygons], "Pants", {"Hips": 1.0})
        bpy.data.meshes.remove(me)
    o = np.array([[0, 0, zc]])
    r = ray_surface(lambda P: pelvis_sdf(P), o, np.array([[0, -1.0, 0]]), 0.3)[0]
    yb = -(r + 0.0052 + 0.0022)
    bm = bmesh.new()
    for cx, cz, hx, hz in [(0, 0.0175, 0.026, 0.0035), (0, -0.0175, 0.026, 0.0035), (-0.0225, 0, 0.0035, 0.021), (0.0225, 0, 0.0035, 0.021)]:
        bm_box(bm, (cx, yb, zc + cz), (hx, 0.0022, hz), bevel=0.0012)
    bm_box(bm, (-0.006, yb + 0.0006, zc), (0.0165, 0.0012, 0.0011), bevel=0.0004)                # prong
    me = bpy.data.meshes.new("k")
    bm.to_mesh(me)
    bm.free()
    mb.add(np.array([v.co[:] for v in me.vertices]), [tuple(q.vertices) for q in me.polygons], "Ring", {"Hips": 1.0})
    bpy.data.meshes.remove(me)
    mesh = mb.build("BodyMesh")
    bones = [("Hips", (0, 0, 0.93), (0, 0, 1.05), None, False, (0, -1, 0)),
             ("Torso", (0, 0, 0.93), (0, 0, 1.45), "Hips", False, (0, -1, 0))]
    for side, x in (("L", -0.1), ("R", 0.1)):
        bones += [(f"Thigh{side}", (x, 0, 0.93), (x, 0, 0.50), "Hips", False, (0, -1, 0)),
                  (f"Shin{side}", (x, 0, 0.50), (x, 0, 0.08), f"Thigh{side}", True, (0, -1, 0)),
                  (f"Foot{side}", (x, 0, 0.08), (x, -0.13, 0.03), f"Shin{side}", True, (0, 0, 1))]
    rig = build_armature("PlayerBody", bones)
    skin_to(mesh, rig)
    log("BODY tris", tri_count(mesh))
    return rig, [rig, mesh]


if BUILD_ARM:
    rig, objs = build_arm()
    bpy.context.view_layer.update()
    export(objs, out_dir + ("/PlayerArm.fbx" if LEFT else "/PlayerArmRight.fbx"))
if BUILD_BODY:
    rig, objs = build_body()
    bpy.context.view_layer.update()
    export(objs, out_dir + "/PlayerBody.fbx")
if BLEND:
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
log("EXPORTED")
