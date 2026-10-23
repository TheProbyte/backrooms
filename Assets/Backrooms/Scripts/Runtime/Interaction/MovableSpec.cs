using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Interaction
{
    public enum MovableKind { Sofa, Armchair, OfficeChair, Table, Box }

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

        public void AddPrefab(GameObject prefab, Vector3 local, float yaw = 0f, Vector3? scale = null)
            => parts.Add(new Part { prefab = prefab, position = local, yaw = yaw, scale = scale ?? Vector3.one });

        public void AddBox(Material m, Vector3 centre, Vector3 size, float yaw = 0f)
            => parts.Add(new Part { material = m, position = centre, yaw = yaw, scale = size });

        /// <summary>Id for the n-th piece that belongs to one generated prop (the chairs around a table).</summary>
        public static ulong SubId(ulong id, int n) => id ^ (((ulong)(n + 1) * 0x9E3779B97F4A7C15ul) & ~0xFFFFul);
    }

    /// <summary>A place a hand can hold: a segment (prop-local) along an edge, and the way the surface under the palm faces.</summary>
    public struct GrabEdge
    {
        public Vector3 a, b;
        public Vector3 normal;
        public GrabEdge(Vector3 a, Vector3 b, Vector3 normal) { this.a = a; this.b = b; this.normal = normal; }
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
            Vector3 up = Vector3.up;
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
                    s.edges.Add(new GrabEdge(new Vector3(x0 + 0.12f, top, z0 + backD * 0.5f), new Vector3(x1 - 0.12f, top, z0 + backD * 0.5f), up));
                    s.edges.Add(new GrabEdge(new Vector3(x0 + arm * 0.5f, armTop, z0 + backD), new Vector3(x0 + arm * 0.5f, armTop, z1 - 0.04f), up));
                    s.edges.Add(new GrabEdge(new Vector3(x1 - arm * 0.5f, armTop, z0 + backD), new Vector3(x1 - arm * 0.5f, armTop, z1 - 0.04f), up));
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
                    s.edges.Add(new GrabEdge(new Vector3(cx - sw * 0.75f, top, z0 + 0.045f), new Vector3(cx + sw * 0.75f, top, z0 + 0.045f), up));
                    s.edges.Add(new GrabEdge(new Vector3(cx - sw, seat1, z0 + 0.15f), new Vector3(cx - sw, seat1, z1 - 0.05f), up));
                    s.edges.Add(new GrabEdge(new Vector3(cx + sw, seat1, z0 + 0.15f), new Vector3(cx + sw, seat1, z1 - 0.05f), up));
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
                    const float e = 0.03f;
                    s.edges.Add(new GrabEdge(new Vector3(-L * 0.5f + 0.1f, T, -D * 0.5f + e), new Vector3(L * 0.5f - 0.1f, T, -D * 0.5f + e), up));
                    s.edges.Add(new GrabEdge(new Vector3(-L * 0.5f + 0.1f, T, D * 0.5f - e), new Vector3(L * 0.5f - 0.1f, T, D * 0.5f - e), up));
                    s.edges.Add(new GrabEdge(new Vector3(-L * 0.5f + e, T, -D * 0.5f + 0.1f), new Vector3(-L * 0.5f + e, T, D * 0.5f - 0.1f), up));
                    s.edges.Add(new GrabEdge(new Vector3(L * 0.5f - e, T, -D * 0.5f + 0.1f), new Vector3(L * 0.5f - e, T, D * 0.5f - 0.1f), up));
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
                    const float e = 0.025f;
                    s.edges.Add(new GrabEdge(new Vector3(-z.x * 0.5f + 0.04f, z.y, -z.z * 0.5f + e), new Vector3(z.x * 0.5f - 0.04f, z.y, -z.z * 0.5f + e), up));
                    s.edges.Add(new GrabEdge(new Vector3(-z.x * 0.5f + 0.04f, z.y, z.z * 0.5f - e), new Vector3(z.x * 0.5f - 0.04f, z.y, z.z * 0.5f - e), up));
                    s.edges.Add(new GrabEdge(new Vector3(-z.x * 0.5f + e, z.y, -z.z * 0.5f + 0.04f), new Vector3(-z.x * 0.5f + e, z.y, z.z * 0.5f - 0.04f), up));
                    s.edges.Add(new GrabEdge(new Vector3(z.x * 0.5f - e, z.y, -z.z * 0.5f + 0.04f), new Vector3(z.x * 0.5f - e, z.y, z.z * 0.5f - 0.04f), up));
                    break;
                }
            }
            return s;
        }

        static void Box(MovableShape s, float x0, float x1, float y0, float y1, float z0, float z1)
            => s.boxes.Add((new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, y1 - y0, z1 - z0)));
    }
}
