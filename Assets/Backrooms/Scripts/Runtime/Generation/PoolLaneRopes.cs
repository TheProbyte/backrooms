using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The lap pools' lane ropes as real ones (2026-11-01, the owner: they "should have a connection to the wall and should
    /// move in the water, connected as a line but all together when waves hit it/ripples, and should stop you from passing
    /// so you have to go under or over"). Each rope is a line of floats on a cable tied to an eye on each end wall
    /// (OfficeChunkBuilder.RopeAnchor), joined up from the chunks' shares (PoolChunk.ropes), and simulated as a chain of
    /// points (Verlet, with distance constraints relaxed Jakobsen's way, as game ropes are since Hitman: Codename 47): held
    /// between its anchors with a little slack, each point floating on the moving surface (the ripples' heights, copied back
    /// from the GPU: PoolRipples.HeightAt) and pushed down the slopes of passing waves, so a wave lifts and sways the whole
    /// line in turn. It has a body: a run of thin boxes along it near the player, so wading into it stops you (it gives a
    /// little, bending away); duck under it (crouch, under the water) or climb over where it is shallow. The floats are as
    /// FINA's: red within 5 m of each wall, blue and white in runs between. Added by PoolWorld.
    /// </summary>
    public class PoolLaneRopes : MonoBehaviour
    {
        const float Spacing = 0.25f, FloatLen = 0.1f, FloatR = 0.052f, Slack = 0.0015f, Range = 34f, Eye = 0.072f;
        const int FloatSides = 10, FloatVerts = FloatSides * 4 + 2;

        class Rope
        {
            public string key;
            public Vector3 a, b;
            public Vector3[] p, prev;
            public float rest, length;
            public GameObject go;
            public Mesh mesh;
            public Vector3[] verts, norms;
            public float[] floatAt;
            public Transform[] boxes;
            public bool seen, active;
            public float acc;
        }

        readonly Dictionary<string, Rope> _ropes = new Dictionary<string, Rope>();
        float _gather;
        Transform _player;
        CharacterController _cc;

        /// <summary>Ropes being simulated (for tests).</summary>
        public static int Active { get; private set; }

        void Update()
        {
            var w = PoolWorld.Instance;
            if (!_player) { var ws = w ? w.streamer : null; _player = ws ? ws.player : null; if (_player) _cc = _player.GetComponent<CharacterController>(); }
            bool on = w && _player && w.IsPool(_player.position);
            Active = 0;
            if (!on) { foreach (var r in _ropes.Values) Show(r, false); return; }
            var pp = _player.position;
            _gather -= Time.deltaTime;
            if (_gather <= 0f) { _gather = 1f; Gather(pp, w); }
            foreach (var r in _ropes.Values)
            {
                var near = ClosestOnSegment(r.a, r.b, pp);
                bool want = (near - pp).sqrMagnitude < Range * Range && Mathf.Abs(r.a.y - pp.y) < 4f;
                Show(r, want);
                if (!want) continue;
                Active++;
                Simulate(r, pp);
                Draw(r);
                Body(r, pp);
            }
            if (Active > 0) PoolRipples.WantHeights();
        }

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            var d = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, d) / Mathf.Max(1e-6f, d.sqrMagnitude));
            return a + d * t;
        }

        void Show(Rope r, bool on)
        {
            if (r.active == on) return;
            r.active = on;
            if (r.go) r.go.SetActive(on);
        }

        // ---------------- Joining the shares ----------------

        void Gather(Vector3 near, PoolWorld w)
        {
            var groups = new Dictionary<(bool, int, int), List<(float s0, float s1, byte ends, Vector3 a, Vector3 b)>>();
            foreach (var c in PoolChunk.All)
            {
                if (!c || c.ropes == null || c.ropes.Length == 0) continue;
                if ((c.transform.position - near).sqrMagnitude > 48f * 48f) continue;
                for (int i = 0; i < c.ropes.Length; i++)
                {
                    var v = c.ropes[i];
                    var a = c.transform.TransformPoint(new Vector3(v.x, PoolBiome.WaterLevel + 0.05f, v.y));
                    var b = c.transform.TransformPoint(new Vector3(v.z, PoolBiome.WaterLevel + 0.05f, v.w));
                    bool alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.z - a.z);
                    var key = (alongX, Mathf.RoundToInt((alongX ? a.z : a.x) * 100f), Mathf.RoundToInt(a.y * 10f));
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<(float, float, byte, Vector3, Vector3)>();
                    float s0 = alongX ? a.x : a.z, s1 = alongX ? b.x : b.z;
                    list.Add((Mathf.Min(s0, s1), Mathf.Max(s0, s1), c.ropeEnds != null && i < c.ropeEnds.Length ? c.ropeEnds[i] : (byte)0, s0 <= s1 ? a : b, s0 <= s1 ? b : a));
                }
            }
            foreach (var r in _ropes.Values) r.seen = false;
            foreach (var kv in groups)
            {
                var list = kv.Value;
                list.Sort((x, y) => x.s0.CompareTo(y.s0));
                for (int i = 0; i < list.Count; i++)
                {
                    if ((list[i].ends & 1) == 0 || (list[i].ends & 4) != 0) continue;   // a rope starts at a tied end
                    int j = i;
                    bool whole = false;
                    while (true)
                    {
                        if ((list[j].ends & 4) != 0) break;                                // runs out into open water: none
                        if ((list[j].ends & 2) != 0) { whole = true; break; }
                        if (j + 1 >= list.Count || Mathf.Abs(list[j + 1].s0 - list[j].s1) > 0.05f) break;
                        j++;
                    }
                    if (!whole) continue;                                // its far end is not loaded yet
                    var dir = (list[j].b - list[i].a).normalized;
                    var A = list[i].a + dir * Eye; var B = list[j].b - dir * Eye;
                    string key = kv.Key.Item1 + ":" + kv.Key.Item2 + ":" + kv.Key.Item3 + ":" + Mathf.RoundToInt(list[i].s0 * 10f) + ":" + Mathf.RoundToInt(list[j].s1 * 10f);
                    if (_ropes.TryGetValue(key, out var have)) { have.seen = true; i = j; continue; }
                    var rope = Make(key, A, B);
                    if (rope != null) { rope.seen = true; _ropes[key] = rope; }
                    i = j;
                }
            }
            var gone = new List<string>();
            foreach (var kv in _ropes) if (!kv.Value.seen) gone.Add(kv.Key);
            foreach (var k in gone) { var r = _ropes[k]; if (r.go) Destroy(r.go); if (r.mesh) Destroy(r.mesh); _ropes.Remove(k); }
        }

        Rope Make(string key, Vector3 a, Vector3 b)
        {
            float len = (b - a).magnitude;
            if (len < 1.5f) return null;
            int n = Mathf.Max(3, Mathf.CeilToInt(len / Spacing) + 1);
            var r = new Rope { key = key, a = a, b = b, p = new Vector3[n], prev = new Vector3[n], length = len, rest = len * (1f + Slack) / (n - 1) };
            for (int i = 0; i < n; i++) r.p[i] = r.prev[i] = Vector3.Lerp(a, b, i / (float)(n - 1)) + Vector3.down * 0.05f;
            r.p[0] = r.prev[0] = a; r.p[n - 1] = r.prev[n - 1] = b;
            // The floats: one every 10 cm from the ends' cables in, red near the walls, blue and white in runs.
            var at = new List<float>(); var kind = new List<int>();
            for (float s = 0.18f; s < len - 0.18f; s += FloatLen + 0.004f)
            {
                at.Add(s);
                float redZone = Mathf.Min(5f, len * 0.2f);                // FINA's 5 m; less in a short pool
                bool red = s < redZone || s > len - redZone;
                kind.Add(red ? 0 : (Mathf.FloorToInt(s / 0.6f) & 1) == 0 ? 1 : 2);
            }
            r.floatAt = at.ToArray();
            int fc = at.Count;
            r.verts = new Vector3[fc * FloatVerts]; r.norms = new Vector3[fc * FloatVerts];
            var subs = new[] { new List<int>(), new List<int>(), new List<int>() };
            for (int f = 0; f < fc; f++)
            {
                var tris = subs[kind[f]];
                int o = f * FloatVerts;
                for (int ring = 0; ring < 3; ring++)
                for (int s = 0; s < FloatSides; s++)
                {
                    int s1 = (s + 1) % FloatSides;
                    int i0 = o + ring * FloatSides + s, i1 = o + ring * FloatSides + s1, j0 = i0 + FloatSides, j1 = i1 + FloatSides;
                    tris.Add(i0); tris.Add(j0); tris.Add(i1);
                    tris.Add(i1); tris.Add(j0); tris.Add(j1);
                }
                int c0 = o + FloatSides * 4, c1 = c0 + 1;
                for (int s = 0; s < FloatSides; s++)
                {
                    int s1 = (s + 1) % FloatSides;
                    tris.Add(c0); tris.Add(o + s); tris.Add(o + s1);
                    tris.Add(c1); tris.Add(o + 3 * FloatSides + s1); tris.Add(o + 3 * FloatSides + s);
                }
            }
            r.mesh = new Mesh { name = "Lane rope", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            r.mesh.MarkDynamic();
            r.mesh.vertices = r.verts;
            r.mesh.subMeshCount = 3;
            for (int k = 0; k < 3; k++) r.mesh.SetTriangles(subs[k], k, false);
            r.go = new GameObject("Lane rope");
            r.go.transform.SetParent(transform, false);
            r.go.AddComponent<MeshFilter>().sharedMesh = r.mesh;
            var mr = r.go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { PoolMaterials.PlasticRed, PoolMaterials.PlasticBlue, PoolMaterials.PlasticWhite };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Its body: thin boxes along it, kept only near the player.
            var rb = r.go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            int nb = Mathf.Max(1, (n - 1) / 2);
            r.boxes = new Transform[nb];
            for (int k = 0; k < nb; k++)
            {
                var bgo = new GameObject("Rope body");
                bgo.transform.SetParent(r.go.transform, false);
                var bc = bgo.AddComponent<BoxCollider>();
                bc.size = new Vector3(0.12f, 0.12f, 2f * r.rest + 0.03f);
                bgo.SetActive(false);
                r.boxes[k] = bgo.transform;
            }
            r.active = true;
            return r;
        }

        // ---------------- Floating ----------------

        void Simulate(Rope r, Vector3 player)
        {
            const float h = 1f / 60f;
            r.acc = Mathf.Min(r.acc + Time.deltaTime, 4f * h);
            float level = r.a.y - 0.05f;               // the still water's height on this storey
            int n = r.p.Length;
            float pr = _cc ? _cc.radius + 0.06f : 0.36f;
            float pBottom = player.y + (_cc ? _cc.center.y - _cc.height * 0.5f : 0f), pTop = player.y + (_cc ? _cc.center.y + _cc.height * 0.5f : 1.8f);
            int splashes = 0;
            while (r.acc >= h)
            {
                r.acc -= h;
                for (int i = 1; i < n - 1; i++)
                {
                    var x = r.p[i];
                    var v = x - r.prev[i];
                    float hh = PoolRipples.HeightAt(x.x, x.z) * 1.5f;
                    float surf = level + hh;
                    var acc = Vector3.down * 9.81f;
                    float dy = (surf + 0.004f) - x.y;
                    if (dy > -0.05f)
                    {
                        // Floating: the floats' buoyancy holds them half in the surface, the water damps their bobbing and
                        // drags them; a wave's slope pushes them down it.
                        acc.y += 9.81f + dy * 420f;
                        v.y *= 0.86f;
                        v.x *= 0.95f; v.z *= 0.95f;
                        const float e = 0.16f;
                        float gx = (PoolRipples.HeightAt(x.x + e, x.z) - PoolRipples.HeightAt(x.x - e, x.z)) * 1.5f / (2f * e);
                        float gz = (PoolRipples.HeightAt(x.x, x.z + e) - PoolRipples.HeightAt(x.x, x.z - e)) * 1.5f / (2f * e);
                        acc.x -= gx * 9.81f * 2.5f; acc.z -= gz * 9.81f * 2.5f;
                    }
                    r.prev[i] = x;
                    r.p[i] = x + v + acc * (h * h);
                }
                // The player: the rope is pushed aside where they wade into it (and stops them, by its body).
                for (int i = 1; i < n - 1; i++)
                {
                    var x = r.p[i];
                    if (x.y < pBottom - 0.05f || x.y > pTop) continue;
                    var d = new Vector2(x.x - player.x, x.z - player.z);
                    float m = d.magnitude;
                    if (m >= pr || m < 1e-4f) continue;
                    var push = d / m * (pr - m);
                    r.p[i] = new Vector3(x.x + push.x, x.y, x.z + push.y);
                    if (splashes < 2 && (r.p[i] - r.prev[i]).sqrMagnitude > 0.004f * 0.004f) { splashes++; PoolRipples.Splash(new Vector3(x.x, level, x.z), 0.1f, -0.0015f); }
                }
                // Held together: each link its length again, the ends on their eyes (Jakobsen's relaxation).
                for (int it = 0; it < 24; it++)
                {
                    r.p[0] = r.a; r.p[n - 1] = r.b;
                    for (int i = 0; i < n - 1; i++)
                    {
                        var d = r.p[i + 1] - r.p[i];
                        float len = d.magnitude;
                        if (len < 1e-6f || len <= r.rest) continue;          // a cable takes tension, not thrust
                        var corr = d * ((len - r.rest) / len * 0.5f);
                        if (i > 0) r.p[i] += corr; else r.p[i + 1] -= corr;
                        if (i + 1 < n - 1) r.p[i + 1] -= corr; else r.p[i] += corr;
                    }
                }
                r.p[0] = r.a; r.p[n - 1] = r.b;
            }
        }

        // ---------------- Drawing and body ----------------

        Vector3 Along(Rope r, float s, out Vector3 tangent)
        {
            int n = r.p.Length;
            float u = Mathf.Clamp(s / r.length * (n - 1), 0f, n - 1.0001f);
            int i = Mathf.FloorToInt(u);
            float t = u - i;
            var d = r.p[i + 1] - r.p[i];
            tangent = d.sqrMagnitude > 1e-8f ? d.normalized : (r.b - r.a).normalized;
            return Vector3.Lerp(r.p[i], r.p[i + 1], t);
        }

        void Draw(Rope r)
        {
            var origin = r.go.transform.position;
            for (int f = 0; f < r.floatAt.Length; f++)
            {
                var c = Along(r, r.floatAt[f], out var t) - origin + Vector3.up * 0.014f;      // riding a little proud of the water
                var side = Vector3.Cross(Vector3.up, t);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
                side.Normalize();
                var up = Vector3.Cross(t, side);
                int o = f * FloatVerts;
                for (int ring = 0; ring < 4; ring++)
                {
                    float along = (ring == 0 ? -0.5f : ring == 1 ? -0.3f : ring == 2 ? 0.3f : 0.5f) * FloatLen;
                    float rad = ring == 0 || ring == 3 ? FloatR * 0.86f : FloatR;
                    for (int s = 0; s < FloatSides; s++)
                    {
                        float a = s * Mathf.PI * 2f / FloatSides;
                        var nrm = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                        r.verts[o + ring * FloatSides + s] = c + t * along + nrm * rad;
                        r.norms[o + ring * FloatSides + s] = (ring == 0 ? (nrm - t * 0.8f).normalized : ring == 3 ? (nrm + t * 0.8f).normalized : nrm);
                    }
                }
                r.verts[o + FloatSides * 4] = c - t * (FloatLen * 0.5f); r.norms[o + FloatSides * 4] = -t;
                r.verts[o + FloatSides * 4 + 1] = c + t * (FloatLen * 0.5f); r.norms[o + FloatSides * 4 + 1] = t;
            }
            r.mesh.vertices = r.verts;
            r.mesh.normals = r.norms;
            r.mesh.RecalculateBounds();
        }

        void Body(Rope r, Vector3 player)
        {
            int n = r.p.Length;
            for (int k = 0; k < r.boxes.Length; k++)
            {
                int i = k * 2, j = Mathf.Min(n - 1, i + 2);
                var a = r.p[i]; var b = r.p[j];
                var mid = (a + b) * 0.5f;
                bool near = (new Vector2(mid.x - player.x, mid.z - player.z)).sqrMagnitude < 4f * 4f;
                var t = r.boxes[k];
                if (t.gameObject.activeSelf != near) t.gameObject.SetActive(near);
                if (!near) continue;
                var d = b - a;
                t.SetPositionAndRotation(mid, d.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(d) : Quaternion.identity);
            }
        }

        void OnDestroy()
        {
            foreach (var r in _ropes.Values) { if (r.go) Destroy(r.go); if (r.mesh) Destroy(r.mesh); }
            _ropes.Clear();
        }
    }
}
