using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A flexible electrical cord between two items (a lamp and its plug; an extension cord's plug and its box): a chain of
    /// particles every ~12 cm with gravity, length constraints and collisions with the world (verlet), drawn as a round
    /// tube. A particle only ever moves by sweeping from its last free position, so the cord cannot pass through floors or
    /// walls; a span that cuts through a corner pushes its particles back out. It has a fixed length:
    ///  - pulled taut with a plugged end, the plug pops out of its socket;
    ///  - pulled taut by a held end, the other end is dragged along (it is a physics object lying in the world).
    /// Sleeps (no simulation) once it has settled and its ends stay still.
    /// </summary>
    public class Cord : MonoBehaviour
    {
        public WorldItem endA, endB;           // A: usually the plug
        public Vector3 aLocal, bLocal;         // attach points in each end's own frame
        public float length = 1.8f;
        public float radius = 0.003f;
        public System.Action<Cord> onTaut;     // pulled past its length with neither end able to give (owner decides)
        /// <summary>Being reeled in: a plug that pops out does not jerk the hand.</summary>
        public bool reeling;
        public static readonly List<Cord> All = new List<Cord>();

        Vector3[] _p, _prev, _valid;
        int _n;
        float _seg, _sleepT;
        bool _asleep;
        Vector3 _lastA, _lastB;
        Mesh _mesh;
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector3> _norms = new List<Vector3>();
        readonly List<int> _tris = new List<int>();
        readonly RaycastHit[] _hits = new RaycastHit[6];
        const int Sides = 6;

        public Vector3 A => endA ? endA.transform.TransformPoint(aLocal) : transform.position;
        public Vector3 B => endB ? endB.transform.TransformPoint(bLocal) : transform.position;
        /// <summary>Straight-line distance between the ends over the cord's length.</summary>
        public float Stretch => Vector3.Distance(A, B) / Mathf.Max(0.01f, length);

        public static Cord Create(string name, WorldItem a, Vector3 aLocal, WorldItem b, Vector3 bLocal, float length, float radius, Material material)
        {
            var go = new GameObject(name);
            var c = go.AddComponent<Cord>();
            c.endA = a; c.aLocal = aLocal; c.endB = b; c.bLocal = bLocal; c.length = length; c.radius = radius;
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;      // a few millimetres thick: too thin for the fixtures' shadow maps
            ItemModels.Prepare(r);
            c.Lay();
            Generation.PoolLitSwitch.Attach(go);
            return c;
        }

        /// <summary>Lays the cord out from A to B (slack sagging below the line).</summary>
        public void Lay()
        {
            _n = Mathf.Max(4, Mathf.CeilToInt(length / 0.12f) + 1);
            _seg = length / (_n - 1);
            _p = new Vector3[_n]; _prev = new Vector3[_n]; _valid = new Vector3[_n];
            Vector3 a = A, b = B;
            float slack = Mathf.Max(0f, length - Vector3.Distance(a, b));
            for (int i = 0; i < _n; i++)
            {
                float t = i / (float)(_n - 1);
                _p[i] = _prev[i] = Vector3.Lerp(a, b, t);
                _valid[i] = _p[i];
            }
            // The slack hangs down, but only as far as there is room.
            for (int i = 1; i < _n - 1; i++)
            {
                float t = i / (float)(_n - 1);
                _p[i] = _prev[i] = Sweep(_valid[i], _valid[i] + Vector3.down * (Mathf.Sin(t * Mathf.PI) * slack * 0.35f), out _);
                _valid[i] = _p[i];
            }
            Wake();
        }

        public void Wake() { _asleep = false; _sleepT = 0f; }

        /// <summary>A new length (reeling in): the same particles, shorter spans.</summary>
        public void SetLength(float metres)
        {
            length = Mathf.Max(0.05f, metres);
            if (_n > 1) _seg = length / (_n - 1);
            Wake();
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
        void OnDestroy() { if (_mesh) Destroy(_mesh); }

        /// <summary>
        /// The cord the view ray passes closest to (within a few centimetres, more further away), before maxDistance:
        /// the distance along the ray and the point on the cord.
        /// </summary>
        public static Cord Aimed(Vector3 eye, Vector3 dir, float maxDistance, out float along, out Vector3 point)
        {
            Cord best = null; along = float.MaxValue; point = default;
            foreach (var c in All)
            {
                if (!c || c._p == null || !c.isActiveAndEnabled) continue;
                for (int i = 0; i < c._n - 1; i++)
                {
                    ClosestRaySegment(eye, dir, c._p[i], c._p[i + 1], out float t, out Vector3 q);
                    if (t <= 0.05f || t > maxDistance || t >= along) continue;
                    float tol = c.radius + 0.02f + t * 0.012f;
                    if ((eye + dir * t - q).sqrMagnitude > tol * tol) continue;
                    best = c; along = t; point = q;
                }
            }
            return best;
        }

        /// <summary>Closest points of a ray (unit dir) and a segment: distance along the ray, and the point on the segment.</summary>
        static void ClosestRaySegment(Vector3 o, Vector3 d, Vector3 a, Vector3 b, out float t, out Vector3 q)
        {
            Vector3 u = b - a, w = o - a;
            float uu = Vector3.Dot(u, u), du = Vector3.Dot(d, u), dw = Vector3.Dot(d, w), uw = Vector3.Dot(u, w);
            float den = uu - du * du;                       // |d| = 1
            float s = den > 1e-8f ? Mathf.Clamp01((uw - du * dw) / den) : 0f;
            q = a + u * s;
            t = Vector3.Dot(q - o, d);
        }

        void LateUpdate()
        {
            if (!endA || !endB) { Destroy(gameObject); return; }
            Vector3 a = A, b = B;
            if ((a - _lastA).sqrMagnitude > 1e-6f || (b - _lastB).sqrMagnitude > 1e-6f) Wake();
            _lastA = a; _lastB = b;

            Taut(a, b);
            if (!_asleep)
            {
                float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
                int steps = dt > 1f / 70f ? 2 : 1;
                float move = 0f;
                for (int s = 0; s < steps; s++) move = Mathf.Max(move, Step(a, b, dt / steps));
                // Never left broken: anything not a number lays the cord out again.
                if (float.IsNaN(move) || float.IsNaN(_p[_n / 2].x)) { Lay(); move = 1f; }
                if (move < 0.0005f) { _sleepT += dt; if (_sleepT > 0.6f) _asleep = true; } else _sleepT = 0f;
                Rebuild();
            }
        }

        float _pull;            // seconds spent walking on against the taut cord
        bool _tugged;           // the tug of it going taut has been felt
        [Tooltip("Seconds of walking on against a taut cord (held at one end, plugged in at the other) before the plug comes out.")]
        public float pullOutTime = 0.55f;
        static CamcorderPlayerController s_player;

        /// <summary>
        /// Past its length: pop a plugged end, or drag a free end toward the held one. Held at one end and plugged in at the
        /// other, it first holds you back (a tug, and you stop); walk on against it and the plug comes out.
        /// </summary>
        void Taut(Vector3 a, Vector3 b)
        {
            float d = Vector3.Distance(a, b);
            bool aHeld = endA.Held, bHeld = endB.Held;
            var plugA = endA as Plug; var plugB = endB as Plug;
            Plug wall = plugA && plugA.Plugged && !aHeld && bHeld ? plugA : plugB && plugB.Plugged && !bHeld && aHeld ? plugB : null;
            if (wall && !reeling)
            {
                HoldBack(wall, wall == plugA ? endB : endA, d);
                return;
            }
            _pull = 0f; _tugged = false;
            if (d <= length) return;
            if ((plugA && plugA.Plugged && !aHeld && (bHeld || d > length * 1.15f)) || (plugB && plugB.Plugged && !bHeld && (aHeld || d > length * 1.15f)))
            {
                var plug = plugA && plugA.Plugged ? plugA : plugB;
                var other = plug == plugA ? endB : endA;
                plug.Unplug(true);
                if (other.Held && !reeling) Yank(plug, other);
                return;
            }
            WorldItem free = aHeld && !bHeld ? endB : bHeld && !aHeld ? endA : null;
            if (free && !free.Body.isKinematic)
            {
                // Along the cord (toward the first bit of it more than 15 cm away), so a dragged end follows it round a
                // corner instead of into the wall.
                Vector3 toward = (free == endA ? b - a : a - b) / Mathf.Max(d, 1e-4f);
                if (_p != null && _n > 2)
                {
                    Vector3 from = free == endA ? a : b;
                    for (int k = 1; k < _n - 1; k++)
                    {
                        Vector3 q = _p[free == endA ? k : _n - 1 - k] - from;
                        if (q.sqrMagnitude < 0.15f * 0.15f || float.IsNaN(q.x)) continue;
                        toward = q.normalized;
                        break;
                    }
                }
                float excess = d - length;
                Vector3 v = free.Body.linearVelocity;
                float along = Vector3.Dot(v, toward);
                float want = excess / Mathf.Max(Time.deltaTime, 1e-3f) * 0.5f;
                if (along < want) free.Body.linearVelocity = v + toward * Mathf.Min(want - along, 6f);
                free.Body.WakeUp();
            }
            else if (!free && d > length * 1.02f) onTaut?.Invoke(this);
        }

        /// <summary>
        /// Held at one end, plugged in at the other, near its length: the player's feet are kept within what the cord allows
        /// (a short tug when it first goes taut); walking on against it for <see cref="pullOutTime"/> pulls the plug out.
        /// </summary>
        void HoldBack(Plug wall, WorldItem held, float d)
        {
            if (!s_player) s_player = FindAnyObjectByType<CamcorderPlayerController>();
            var player = s_player;
            float slack = length - d;
            if (slack > 0.3f || !player)
            {
                if (slack > 0.15f) _tugged = false;
                _pull = Mathf.MoveTowards(_pull, 0f, Time.deltaTime);
                if (!player && d > length * 1.15f) { wall.Unplug(true); Yank(wall, held); }
                return;
            }
            Vector3 plugAt = wall.transform.position;
            Vector3 flat = player.transform.position - plugAt; flat.y = 0f;
            player.Leash(plugAt, flat.magnitude + slack);
            if (slack <= 0.01f && !_tugged) { _tugged = true; Tug(wall, held); }
            float strain = player.LeashStrain;
            if (slack <= 0.03f && strain > 0.25f) _pull += Time.deltaTime * (strain > 2f ? 1.7f : 1f);
            else _pull = Mathf.MoveTowards(_pull, 0f, Time.deltaTime * 0.6f);
            if (_pull >= pullOutTime || d > length + 0.35f)
            {
                wall.Unplug(true);
                Yank(wall, held);
                _pull = 0f; _tugged = false;
            }
        }

        /// <summary>The cord goes taut against the hand: a soft snap and a small jerk of the view toward the plug.</summary>
        static void Tug(Plug plug, WorldItem held)
        {
            AudioSource.PlayClipAtPoint(Audio.SoundBank.CordWhip, held.transform.position, 0.45f);
            var shake = FindAnyObjectByType<HandheldCameraShake>();
            var cam = Camera.main;
            if (!shake || !cam) return;
            Vector3 toward = plug.transform.position - held.transform.position;
            float side = Vector3.Dot(toward.normalized, cam.transform.right) >= 0f ? 1f : -1f;
            shake.Jolt(new Vector3(7f, 6f * side, -3f * side));
        }

        /// <summary>Walked off with the far end in hand: the plug tears out with a snap, flies after the cord, and the hand
        /// (and the camcorder with it) is jerked back.</summary>
        static void Yank(Plug plug, WorldItem held)
        {
            Vector3 toward = held.transform.position - plug.transform.position;
            plug.Body.linearVelocity = toward.normalized * 3.2f + Vector3.up * 1.4f;
            AudioSource.PlayClipAtPoint(Audio.SoundBank.CordYank, held.transform.position, 1f);
            var shake = FindAnyObjectByType<HandheldCameraShake>();
            var cam = Camera.main;
            if (shake && cam)
            {
                float side = Vector3.Dot(-toward.normalized, cam.transform.right) >= 0f ? 1f : -1f;
                shake.Jolt(new Vector3(28f, 18f * side, -14f * side));
            }
        }

        static readonly Dictionary<int, bool> s_brokenWall = new Dictionary<int, bool>();
        /// <summary>A collider of a broken wall: the cord meets those walls as slabs (Destruction.WallSlabs), not their thin boards.</summary>
        static bool BrokenWall(Collider c)
        {
            int id = c.GetInstanceID();
            if (s_brokenWall.TryGetValue(id, out bool v)) return v;
            if (s_brokenWall.Count > 4096) s_brokenWall.Clear();
            return s_brokenWall[id] = c.GetComponentInParent<Destruction.BrokenPanel>();
        }

        bool Ignored(Collider c)
        {
            if (c.isTrigger || c.GetComponent<CharacterController>() || BrokenWall(c)) return true;
            var t = c.transform;
            return (endA && t.IsChildOf(endA.transform)) || (endB && t.IsChildOf(endB.transform));
        }

        const float Skin = 0.0015f;

        /// <summary>Moves a particle from a free position toward a wanted one, stopping just short of anything solid.</summary>
        Vector3 Sweep(Vector3 from, Vector3 to, out RaycastHit hitInfo) => Sweep(from, to, out hitInfo, out _, out _);

        /// <summary>As above; hit and normal also for a broken wall (met as a slab: hitInfo has no collider then).</summary>
        Vector3 Sweep(Vector3 from, Vector3 to, out RaycastHit hitInfo, out bool hit, out Vector3 normal)
        {
            Vector3 got = SweepColliders(from, to, out hitInfo);
            hit = hitInfo.collider; normal = hit ? hitInfo.normal : Vector3.zero;
            if (SlabMarch(from, got, out var stop, out var n, out _)) { got = stop; hit = true; normal = n; }
            return got;
        }

        /// <summary>
        /// The first point along from-to (1 cm steps) where the cord would be inside a broken wall's solid part: stop is
        /// the last free point, normal the way out (Destruction.WallSlabs). False if it meets none.
        /// </summary>
        bool SlabMarch(Vector3 from, Vector3 to, out Vector3 stop, out Vector3 normal, out Destruction.BrokenPanel wall)
        {
            stop = to; normal = Vector3.zero; wall = null;
            var near = Destruction.WallSlabs.Near(from, to, radius);
            if (near.Count == 0) return false;
            float len = Vector3.Distance(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.01f));
            float bestT = float.MaxValue;
            foreach (var bp in near)
            {
                Vector3 prev = from;
                for (int s = 1; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    if (t >= bestT) break;
                    Vector3 q = Vector3.Lerp(from, to, t);
                    if (Destruction.WallSlabs.Blocked(bp, q, radius))
                    {
                        bestT = t; wall = bp;
                        normal = Destruction.WallSlabs.Normal(bp, prev, q, radius);
                        stop = prev;
                        break;
                    }
                    prev = q;
                }
            }
            return wall != null;
        }

        Vector3 SweepColliders(Vector3 from, Vector3 to, out RaycastHit hitInfo)
        {
            hitInfo = default;
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 1e-6f) return to;
            Vector3 dir = delta / dist;
            int hits = Physics.SphereCastNonAlloc(from, radius, dir, _hits, dist + Skin, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; int bi = -1;
            for (int h = 0; h < hits; h++)
            {
                if (Ignored(_hits[h].collider)) continue;
                if (_hits[h].distance <= 0f && _hits[h].point == Vector3.zero)
                {
                    // Already touching at the start: only motion away from that surface is allowed.
                    if (Physics.ComputePenetration(_ball(from), from, Quaternion.identity, _hits[h].collider, _hits[h].collider.transform.position, _hits[h].collider.transform.rotation, out var sepDir, out _)
                        && Vector3.Dot(sepDir, dir) < 0f) { best = 0f; bi = h; }
                    continue;
                }
                if (_hits[h].distance < best) { best = _hits[h].distance; bi = h; }
            }
            if (bi < 0) return to;
            hitInfo = _hits[bi];
            return from + dir * Mathf.Max(0f, best - Skin);
        }

        SphereCollider _probe;
        /// <summary>A disabled sphere the size of the cord, for overlap tests.</summary>
        SphereCollider _ball(Vector3 at)
        {
            if (!_probe)
            {
                var go = new GameObject("CordProbe") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(transform, false);
                _probe = go.AddComponent<SphereCollider>();
                _probe.enabled = false;
            }
            _probe.radius = radius;
            return _probe;
        }

        /// <summary>Every particle that moved: swept from its last free position (sliding on whatever stopped it).</summary>
        void Collide()
        {
            for (int i = 1; i < _n - 1; i++)
            {
                if ((_p[i] - _valid[i]).sqrMagnitude < 1e-12f) continue;
                OutOfWalls(i);
                Vector3 want = _p[i];
                Vector3 got = Sweep(_valid[i], want, out _, out bool hit, out Vector3 normal);
                if (got != want && hit)
                {
                    // Slide along the surface with what is left of the move.
                    Vector3 rest = Vector3.ProjectOnPlane(want - got, normal);
                    got = Sweep(got, got + rest, out _);
                    _prev[i] = Vector3.Lerp(_prev[i], got, 0.6f);            // friction
                }
                _p[i] = got; _valid[i] = got;
            }
        }

        /// <summary>
        /// A particle that finds itself inside a broken wall's solid part (the wall broke round a cord, or a cord saved so by
        /// an older version) is put back out, through the face nearer it.
        /// </summary>
        void OutOfWalls(int i)
        {
            var near = Destruction.WallSlabs.Near(_valid[i], _valid[i], radius);
            foreach (var bp in near)
            {
                if (!Destruction.WallSlabs.Blocked(bp, _valid[i], radius)) continue;
                var l = bp.Key.ToLocal(_valid[i], Destruction.WallDamage.Pitch);
                float side = l.z >= 0f ? 1f : -1f;
                l.z = side * (Destruction.WallSpec.T * 0.5f + radius + Skin * 2f);
                _valid[i] = _p[i] = _prev[i] = bp.Key.ToWorld(l, Destruction.WallDamage.Pitch);
            }
        }

        /// <summary>
        /// A span that cuts through the solid part of a broken wall goes through the wall's nearest opening instead: the
        /// particle nearer the wall moves into the hole (its neighbours on either side of the wall), or onto the hole's rim
        /// on its own side (a neighbour already in the hole), so the cord comes to lie over the edge of the drywall and
        /// through the hole. A wall with no opening near: the particle goes back to its own side. True if it moved one.
        /// </summary>
        bool ThroughWall(int i)
        {
            Vector3 a = _p[i], b = _p[i + 1];
            if (!SlabMarch(a, b, out _, out _, out var bp)) return false;
            var k = bp.Key; float pitch = Destruction.WallDamage.Pitch;
            float h = Destruction.WallSpec.T * 0.5f + radius;
            Vector3 la = k.ToLocal(a, pitch), lb = k.ToLocal(b, pitch);
            bool aIn = Mathf.Abs(la.z) < h, bIn = Mathf.Abs(lb.z) < h;
            bool Free(int j) => j > 0 && j < _n - 1;
            int mover; Vector3 target;
            if (aIn != bIn)
            {
                // One end in the hole, the other out, and solid wall between: the outer one onto the rim, at the open edge
                // nearest where the span meets the wall's face (or, pinned, the inner one to there inside the hole).
                int inner = aIn ? i : i + 1, outer = aIn ? i + 1 : i;
                Vector3 li = aIn ? la : lb, lo = aIn ? lb : la;
                float side = Mathf.Sign(lo.z);
                Vector3 f = Vector3.Lerp(li, lo, Mathf.Clamp01((side * h - li.z) / (lo.z - li.z)));
                if (!Destruction.WallSlabs.NearestOpen(bp.Panel, f.x, f.y, 0.5f, out var open)) return false;
                mover = Free(outer) ? outer : inner;
                if (!Free(mover)) return false;
                // Just inside the face: it settles onto the drywall's edge and rests there.
                target = k.ToWorld(new Vector3(open.x, open.y, mover == outer ? side * (h - 0.001f) : li.z), pitch);
            }
            else if (!aIn && Mathf.Sign(la.z) != Mathf.Sign(lb.z))
            {
                // Either side of the wall: the nearer free one into the wall's nearest opening (or back to its own side).
                mover = !Free(i) ? i + 1 : !Free(i + 1) ? i : (Mathf.Abs(la.z) <= Mathf.Abs(lb.z) ? i : i + 1);
                if (!Free(mover)) return false;
                Vector3 lm = mover == i ? la : lb;
                Vector3 cross = Vector3.Lerp(la, lb, Mathf.Clamp01(la.z / (la.z - lb.z)));
                target = Destruction.WallSlabs.NearestOpen(bp.Panel, cross.x, cross.y, 0.5f, out var open)
                    ? k.ToWorld(new Vector3(open.x, open.y, 0f), pitch)
                    : k.ToWorld(new Vector3(lm.x, lm.y, Mathf.Sign(lm.z) * (h + Skin * 2f)), pitch);
            }
            else return false;                    // both in the hole (across a stud): left to the colliders' corner wrap
            if ((target - _p[mover]).sqrMagnitude < 1e-8f) return false;
            _p[mover] = _valid[mover] = target;
            _prev[mover] = Vector3.Lerp(_prev[mover], target, 0.8f);
            return true;
        }

        /// <summary>
        /// A span between two particles that cuts through something (around a corner): the particle nearer the corner is put
        /// on the corner's outside edge, so the cord wraps it. The corner is where the two faces the span goes in and out
        /// of meet (or, for one face, just clear of it).
        /// </summary>
        void Spans()
        {
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < _n - 1; i++)
            {
                Vector3 a = _p[i], b = _p[i + 1], d = b - a;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                Vector3 dir = d / len;
                if (ThroughWall(i)) continue;
                if (!FirstHit(a, dir, len, out var inHit)) continue;
                bool outFound = FirstHit(b, -dir, len, out var outHit);
                Vector3 n1 = inHit.normal, n2 = outFound ? outHit.normal : n1;
                // Straight through something thin (its two faces facing away from each other): no corner to wrap; the
                // particle goes back out of the face it went in by.
                if (Vector3.Cross(n1, n2).sqrMagnitude < 1e-3f && Vector3.Dot(n1, n2) < 0f) n2 = n1;
                Vector3 corner;
                if (Vector3.Dot(n1, n2) > 0.95f)
                {
                    // One face (a bump, a floor edge): lift the span's middle clear of it.
                    corner = (inHit.point + (outFound ? outHit.point : inHit.point)) * 0.5f;
                }
                else
                {
                    // Two faces: the corner edge is where their planes meet; the point on it nearest the span.
                    Vector3 edgeDir = Vector3.Cross(n1, n2);
                    float e2 = edgeDir.sqrMagnitude;
                    Vector3 mid = (a + b) * 0.5f;
                    // A point on both planes (the planes' line of intersection), then the point on it nearest the span.
                    float d1 = Vector3.Dot(n1, inHit.point), d2 = Vector3.Dot(n2, outHit.point);
                    Vector3 onBoth = (Vector3.Cross(n2, edgeDir) * d1 + Vector3.Cross(edgeDir, n1) * d2) / e2;   // n1.p = d1, n2.p = d2
                    Vector3 ed = edgeDir / Mathf.Sqrt(e2);
                    corner = onBoth + ed * Vector3.Dot(mid - onBoth, ed);
                    if ((corner - mid).sqrMagnitude > len * len) corner = (inHit.point + outHit.point) * 0.5f;   // a poor fit: the hits' middle
                }
                Vector3 outward = (n1 + n2).sqrMagnitude > 1e-4f ? (n1 + n2).normalized : n1;
                Vector3 target = corner + outward * (radius * 1.6f + Skin * 2f);
                // Move the free particle nearer the corner (both if neither end is pinned and they are equally near).
                bool aFree = i > 0, bFree = i + 1 < _n - 1;
                int k = !aFree ? i + 1 : !bFree ? i : ((a - corner).sqrMagnitude <= (b - corner).sqrMagnitude ? i : i + 1);
                if (k == 0 || k == _n - 1) continue;
                var got = Sweep(_valid[k], target, out _);
                _p[k] = _valid[k] = got;
                _prev[k] = Vector3.Lerp(_prev[k], got, 0.8f);                 // it sticks on the edge rather than bouncing off
            }
        }

        /// <summary>The nearest solid thing along a short segment (not the cord's own ends).</summary>
        bool FirstHit(Vector3 from, Vector3 dir, float dist, out RaycastHit hit)
        {
            hit = default;
            int hits = Physics.RaycastNonAlloc(from, dir, _hits, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int h = 0; h < hits; h++)
            {
                if (Ignored(_hits[h].collider) || _hits[h].distance <= 0f || _hits[h].distance >= best) continue;
                best = _hits[h].distance; hit = _hits[h];
            }
            return best < float.MaxValue;
        }

        float Step(Vector3 a, Vector3 b, float dt)
        {
            Vector3 g = Physics.gravity * (dt * dt);
            var before = new Vector3[_n];
            float moved = 0f;
            for (int i = 1; i < _n - 1; i++)
            {
                before[i] = _p[i];
                Vector3 v = (_p[i] - _prev[i]) * 0.985f;
                _prev[i] = _p[i];
                _p[i] += v + g;
            }
            _p[0] = a; _p[_n - 1] = b;
            _valid[0] = a; _valid[_n - 1] = b;
            for (int it = 0; it < 12; it++)
            {
                if (it % 4 == 0) Collide();
                for (int i = 0; i < _n - 1; i++)
                {
                    Vector3 d = _p[i + 1] - _p[i];
                    float len = d.magnitude;
                    if (len < 1e-6f) continue;
                    float diff = (len - _seg) / len;
                    if (diff < 0f) diff *= 0.15f;                // a cord bends easily; it does not push back much
                    bool pinA = i == 0, pinB = i + 1 == _n - 1;
                    if (pinA && pinB) continue;
                    if (pinA) _p[i + 1] -= d * diff;
                    else if (pinB) _p[i] += d * diff;
                    else { _p[i] += d * (diff * 0.5f); _p[i + 1] -= d * (diff * 0.5f); }
                }
            }
            Collide();
            Spans();
            for (int i = 1; i < _n - 1; i++) moved = Mathf.Max(moved, (_p[i] - before[i]).magnitude);
            return moved / Mathf.Max(dt, 1e-4f) * 0.016f;
        }

        void Rebuild()
        {
            if (!_mesh) { _mesh = new Mesh { name = "Cord" }; _mesh.MarkDynamic(); GetComponent<MeshFilter>().sharedMesh = _mesh; }
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _verts.Clear(); _norms.Clear(); _tris.Clear();
            // A smooth path through the particles (Catmull-Rom, two points per span).
            var path = new List<Vector3>(_n * 2);
            for (int i = 0; i < _n - 1; i++)
            {
                Vector3 p0 = _p[Mathf.Max(0, i - 1)], p1 = _p[i], p2 = _p[i + 1], p3 = _p[Mathf.Min(_n - 1, i + 2)];
                path.Add(p1);
                path.Add(0.5f * (2f * p1 + (-p0 + p2) * 0.5f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * 0.25f + (-p0 + 3f * p1 - 3f * p2 + p3) * 0.125f));
            }
            path.Add(_p[_n - 1]);
            Vector3 prevSide = Vector3.zero;
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 t = (path[Mathf.Min(path.Count - 1, i + 1)] - path[Mathf.Max(0, i - 1)]);
                if (t.sqrMagnitude < 1e-10f) t = Vector3.forward;
                t.Normalize();
                Vector3 side = prevSide.sqrMagnitude > 0.5f ? Vector3.ProjectOnPlane(prevSide, t) : Vector3.Cross(t, Mathf.Abs(t.y) > 0.9f ? Vector3.right : Vector3.up);
                if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(t, Vector3.right);
                side.Normalize(); prevSide = side;
                Vector3 up = Vector3.Cross(t, side);
                for (int k = 0; k < Sides; k++)
                {
                    float ang = k * Mathf.PI * 2f / Sides;
                    Vector3 n = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                    _verts.Add(path[i] + n * radius); _norms.Add(n);
                }
                if (i == 0) continue;
                int r0 = (i - 1) * Sides, r1 = i * Sides;
                for (int k = 0; k < Sides; k++)
                {
                    int k1 = (k + 1) % Sides;
                    _tris.Add(r0 + k); _tris.Add(r1 + k1); _tris.Add(r1 + k);
                    _tris.Add(r0 + k); _tris.Add(r0 + k1); _tris.Add(r1 + k1);
                }
            }
            _mesh.Clear();
            _mesh.SetVertices(_verts); _mesh.SetNormals(_norms); _mesh.SetTriangles(_tris, 0, true);
        }
    }
}
