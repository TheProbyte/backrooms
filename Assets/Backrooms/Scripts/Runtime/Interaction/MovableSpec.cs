using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Interaction
{
    public enum MovableKind { Sofa, Armchair, OfficeChair, Table, Box, Pallet, WoodenChair, LawnChair, SchoolChair, UtilityCart, JanitorCart, PlatformTruck }

    /// <summary>
    /// Everything needed to (re)create one movable prop: what it looks like and what kind of thing it is.
    /// Made by the chunk builder on a worker thread, so it holds only plain data and asset references.
    /// Shapes (colliders, grab edges, shadow proxies) are worked out on the main thread at spawn
    /// (<see cref="MovableShapes"/>). Moved props keep their spec in <see cref="MovablePropManager"/>,
    /// so they can be rebuilt wherever they were left.
    /// </summary>
    public class MovableSpec
    {
        public struct Part
        {
            public GameObject prefab;     // a modelled prop, or null for a plain box
            public Material material;     // for plain boxes
            public Vector3 position;      // local to the prop (floor level, front = +Z)
            public float yaw;
            public Vector3 scale;
        }

        public ulong id;
        public MovableKind kind;
        public uint renderMask;
        public Material proxyMaterial;    // shadow-only proxy boxes use this material's shadow pass
        public Vector3 size;              // kind-specific dimensions (table length/height/depth, box size)
        public int variant;
        public readonly List<Part> parts = new List<Part>();
        public bool cardboard;            // a cardboard box: the mesh is built at spawn (needs the main thread)
        public Material[] materials;      // board, tape, print

        public Vector3 position;          // world, where the generator put it
        public float yaw;
        /// <summary>Build mode: tipped over (degrees about its own X and Z), scaled, and stuck where it is (never moves).</summary>
        public float pitch, roll;
        public float scale = 1f;
        public bool pinned;
        /// <summary>Built from this named fitting model (Generation.FittingModels: the hub's sofa) instead of prefabs; "" none.</summary>
        public string fitting = "";

        public void AddPrefab(GameObject prefab, Vector3 local, float yaw = 0f, Vector3? scale = null)
            => parts.Add(new Part { prefab = prefab, position = local, yaw = yaw, scale = scale ?? Vector3.one });

        public void AddBox(Material m, Vector3 centre, Vector3 size, float yaw = 0f)
            => parts.Add(new Part { material = m, position = centre, yaw = yaw, scale = size });

        /// <summary>
        /// A conference table L long: its top laid in sections of about 1.2 m end to end (each module stretched only to its
        /// own length, so its edges and grain keep their size; a seam at each join, as a modular table has), its two
        /// T-legs at a third of the way in from each end.
        /// </summary>
        public void AddTable(GameObject module, GameObject leg, float L)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(L / 1.2f));
            float l = L / n;
            for (int i = 0; i < n; i++) AddPrefab(module, new Vector3(-L * 0.5f + l * (i + 0.5f), 0f, 0f), 0f, new Vector3(l, 1f, 1f));
            for (int i = -1; i <= 1; i += 2) AddPrefab(leg, new Vector3(i * L * 0.32f, 0, 0));
        }

        /// <summary>Id for the n-th piece that belongs to one generated prop (the chairs around a table).</summary>
        public static ulong SubId(ulong id, int n) => id ^ (((ulong)(n + 1) * 0x9E3779B97F4A7C15ul) & ~0xFFFFul);
    }

    /// <summary>
    /// A place a hand can hold: a segment (prop-local) along an edge of one of its boxes, and the two faces either side of
    /// it (the palm lies on one, the fingers go over onto the other), with how far in from the edge the palm lies on each.
    /// </summary>
    public struct GrabEdge
    {
        public Vector3 a, b;
        public Vector3 normal, normal2;
        public float inset, inset2;
        public GrabEdge(Vector3 a, Vector3 b, Vector3 normal) { this.a = a; this.b = b; this.normal = normal; normal2 = Vector3.zero; inset = inset2 = 0f; }
        public GrabEdge(Vector3 a, Vector3 b, Vector3 n1, Vector3 n2, float in1, float in2) { this.a = a; this.b = b; normal = n1; normal2 = n2; inset = in1; inset2 = in2; }
    }

    /// <summary>Physical shape of a movable prop: collider boxes (also its shadow proxies), grab edges, mass.</summary>
    public class MovableShape
    {
        public readonly List<(Vector3 centre, Vector3 size)> boxes = new List<(Vector3, Vector3)>();
        public readonly List<GrabEdge> edges = new List<GrabEdge>();
        public float mass = 10f;
        public Vector3 centreOfMass;
        public float friction = 0.6f;
        public bool rolls;                // casters: almost no friction against the floor
        public Bounds bounds;
    }

    public static class MovableShapes
    {
        /// <summary>Colliders, edges and mass for a prop whose parts are already built under <paramref name="root"/>
        /// (root at the origin and unrotated, so renderer bounds are prop-local).</summary>
        public static MovableShape Build(MovableSpec spec, GameObject root)
        {
            var s = new MovableShape();
            var B = new Bounds();
            bool first = true;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (first) { B = r.bounds; first = false; } else B.Encapsulate(r.bounds);
            }
            s.bounds = B;
            float x0 = B.min.x, x1 = B.max.x, z0 = B.min.z, z1 = B.max.z, top = B.max.y, h = B.size.y;
            switch (spec.kind)
            {
                case MovableKind.Sofa:
                case MovableKind.Armchair:
                {
                    // Backs face -Z. Seat block, back block, two arms.
                    float seatTop = B.min.y + h * 0.52f, armTop = B.min.y + h * 0.74f;
                    float backD = B.size.z * 0.27f, arm = Mathf.Min(0.2f, B.size.x * 0.14f);
                    Box(s, x0, x1, B.min.y, seatTop, z0, z1);
                    Box(s, x0, x1, seatTop, top, z0, z0 + backD);
                    Box(s, x0, x0 + arm, seatTop, armTop, z0 + backD, z1);
                    Box(s, x1 - arm, x1, seatTop, armTop, z0 + backD, z1);
                    s.mass = spec.kind == MovableKind.Sofa ? 30f + 12f * B.size.x : 24f;
                    s.friction = 0.38f;
                    s.centreOfMass = new Vector3(B.center.x, B.min.y + h * 0.35f, B.center.z - B.size.z * 0.08f);
                    break;
                }
                case MovableKind.OfficeChair:
                {
                    float seat0 = B.min.y + h * 0.42f, seat1 = B.min.y + h * 0.53f;
                    float sw = Mathf.Min(B.size.x, 0.5f) * 0.5f, cx = B.center.x, cz = B.center.z;
                    Box(s, x0, x1, B.min.y, B.min.y + 0.1f, z0, z1);                              // star base and casters
                    Box(s, cx - 0.04f, cx + 0.04f, B.min.y + 0.1f, seat0, cz - 0.04f, cz + 0.04f);  // gas column
                    Box(s, cx - sw, cx + sw, seat0, seat1, z0 + 0.04f, z1);                          // seat
                    Box(s, cx - sw * 0.92f, cx + sw * 0.92f, seat1, top, z0, z0 + 0.09f);            // back
                    s.mass = 12f;
                    s.centreOfMass = new Vector3(cx, B.min.y + 0.22f, cz);
                    s.rolls = true;
                    break;
                }
                case MovableKind.LawnChair:
                case MovableKind.SchoolChair:
                {
                    // As the wooden chair: legs to the seat, the seat, the back at -Z; light (2.5 and 4.5 kg).
                    float seat0 = B.min.y + h * 0.46f, seat1 = B.min.y + h * 0.53f;
                    Box(s, x0, x1, B.min.y, seat0, z0, z1);
                    Box(s, x0, x1, seat0, seat1, z0, z1);
                    Box(s, x0, x1, seat1, top, z0, z0 + Mathf.Min(0.08f, B.size.z * 0.18f));
                    s.mass = spec.kind == MovableKind.LawnChair ? 2.5f : 4.5f;
                    s.friction = spec.kind == MovableKind.LawnChair ? 0.4f : 0.5f;
                    s.centreOfMass = new Vector3(B.center.x, B.min.y + h * 0.4f, B.center.z - B.size.z * 0.05f);
                    break;
                }
                case MovableKind.WoodenChair:
                {
                    // Four legs to the seat, the seat, the back (it faces +Z: the back is at -Z).
                    float seat0 = B.min.y + h * 0.48f, seat1 = B.min.y + h * 0.53f;
                    Box(s, x0, x1, B.min.y, seat0, z0, z1);                                     // legs and rails, as a block
                    Box(s, x0, x1, seat0, seat1, z0, z1);                                       // the seat
                    Box(s, x0, x1, seat1, top, z0, z0 + Mathf.Min(0.07f, B.size.z * 0.18f));    // the back
                    s.mass = 6f;
                    s.friction = 0.55f;
                    s.centreOfMass = new Vector3(B.center.x, B.min.y + h * 0.38f, B.center.z - B.size.z * 0.06f);
                    break;
                }
                case MovableKind.Table:
                {
                    float L = spec.size.x, D = spec.size.z, T = spec.size.y;
                    Box(s, -L * 0.5f, L * 0.5f, T - 0.05f, T, -D * 0.5f, D * 0.5f);
                    for (int i = -1; i <= 1; i += 2)
                        Box(s, i * L * 0.32f - 0.05f, i * L * 0.32f + 0.05f, 0f, T - 0.05f, -D * 0.4f, D * 0.4f);
                    s.mass = 20f + 16f * L;
                    s.friction = 0.4f;
                    s.centreOfMass = new Vector3(0f, T * 0.6f, 0f);
                    break;
                }
                case MovableKind.UtilityCart:
                case MovableKind.JanitorCart:
                case MovableKind.PlatformTruck:
                {
                    // A cart (2026-11-04, CartWheels): its boxes from its layout, the casters' box first (it rolls).
                    var cart = CartLayouts.Get(spec.kind);
                    if (cart == null) { Box(s, x0, x1, B.min.y, top, z0, z1); s.mass = 15f; break; }
                    foreach (var b in cart.boxes) s.boxes.Add(b);
                    s.mass = cart.mass;
                    s.friction = 0.5f;
                    s.centreOfMass = new Vector3(0f, spec.kind == MovableKind.PlatformTruck ? 0.2f : 0.38f, 0f);
                    s.rolls = true;
                    break;
                }
                case MovableKind.Pallet:
                {
                    // One box round it (the slats are too thin to matter).
                    const float L = Generation.PalletGeometry.Length, W = Generation.PalletGeometry.Width, H = Generation.PalletGeometry.Height;
                    Box(s, -L * 0.5f, L * 0.5f, 0f, H, -W * 0.5f, W * 0.5f);
                    s.mass = 20f;
                    s.friction = 0.5f;
                    s.centreOfMass = new Vector3(0f, H * 0.5f, 0f);
                    break;
                }
                default:
                {
                    var z = spec.size;
                    Box(s, -z.x * 0.5f, z.x * 0.5f, 0f, z.y, -z.z * 0.5f, z.z * 0.5f);
                    bool full = (spec.variant & 7) == 0;          // one box in eight is full of paper
                    s.mass = (full ? 14f : 1.5f) + z.x * z.y * z.z * (full ? 60f : 25f);
                    s.centreOfMass = new Vector3(0f, z.y * 0.45f, 0f);
                    s.friction = 0.45f;
                    break;
                }
            }
            BoxEdges(s);
            return s;
        }

        /// <summary>
        /// Grab edges from the collider boxes themselves (round 14, owner: "I want to be able to grab any edge of something";
        /// a chair on its side could only be taken by a leg, the edges were a hand-written list for it standing up). Every
        /// edge of every box where both faces beside it are open (not against another of its boxes), in runs: a sofa's seat
        /// front between its arms, a table's legs and the underside of its top, every edge of a box or a pallet. Whichever
        /// way it lies, whatever is on top has edges to take.
        /// </summary>
        static void BoxEdges(MovableShape s)
        {
            s.edges.Clear();
            for (int bi = 0; bi < s.boxes.Count; bi++)
            {
                var (c, size) = s.boxes[bi];
                Vector3 h = size * 0.5f;
                for (int k = 0; k < 3; k++)
                {
                    int i = (k + 1) % 3, j = (k + 2) % 3;
                    float len = size[k];
                    if (len < 0.06f) continue;
                    Vector3 ek = Axis(k), ei = Axis(i), ej = Axis(j);
                    for (int si = -1; si <= 1; si += 2)
                    for (int sj = -1; sj <= 1; sj += 2)
                    {
                        Vector3 n1 = ei * si, n2 = ej * sj;
                        Vector3 mid = c + n1 * h[i] + n2 * h[j];
                        // Palm on face n1: in from the edge across that face (a thin board: less), and the same for n2.
                        float in1 = Mathf.Min(0.03f, size[j] * 0.4f), in2 = Mathf.Min(0.03f, size[i] * 0.4f);
                        float trim = Mathf.Min(0.04f, len * 0.15f);
                        Vector3 a = mid - ek * (h[k] - trim), b = mid + ek * (h[k] - trim);
                        int n = Mathf.Max(2, Mathf.CeilToInt((b - a).magnitude / 0.04f));
                        int runStart = -1;
                        for (int q = 0; q <= n + 1; q++)
                        {
                            bool open = q <= n && Open(s, Vector3.Lerp(a, b, q / (float)n), n1, n2, in1, in2);
                            if (open && runStart < 0) runStart = q;
                            if (!open && runStart >= 0)
                            {
                                Vector3 ra = Vector3.Lerp(a, b, runStart / (float)n), rb = Vector3.Lerp(a, b, (q - 1) / (float)n);
                                if ((rb - ra).magnitude >= 0.05f || len < 0.12f) s.edges.Add(new GrabEdge(ra, rb, n1, n2, in1, in2));
                                runStart = -1;
                            }
                        }
                    }
                }
            }
        }

        static Vector3 Axis(int k) => k == 0 ? Vector3.right : k == 1 ? Vector3.up : Vector3.forward;

        /// <summary>Both faces beside an edge are open there (just off each face, and just round the corner, no other box).</summary>
        static bool Open(MovableShape s, Vector3 p, Vector3 n1, Vector3 n2, float in1, float in2)
        {
            const float off = 0.012f;
            return !InAny(s, p + n1 * off - n2 * in1) && !InAny(s, p + n2 * off - n1 * in2) && !InAny(s, p + (n1 + n2) * off);
        }

        static bool InAny(MovableShape s, Vector3 p)
        {
            foreach (var (c, size) in s.boxes)
            {
                Vector3 d = p - c, h = size * 0.5f + Vector3.one * 0.002f;
                if (Mathf.Abs(d.x) < h.x && Mathf.Abs(d.y) < h.y && Mathf.Abs(d.z) < h.z) return true;
            }
            return false;
        }

        static void Box(MovableShape s, float x0, float x1, float y0, float y1, float z0, float z1)
            => s.boxes.Add((new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, y1 - y0, z1 - z0)));
    }
}
