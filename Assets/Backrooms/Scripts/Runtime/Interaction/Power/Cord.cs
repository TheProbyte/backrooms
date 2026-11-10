using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A flexible electrical cord between two items (a lamp and its plug; an extension cord's plug and its box): a chain of
    /// particles every ~7 cm with gravity, length constraints, a little stiffness (no joint bends sharply, so it lies in
    /// curves, not kinks) and collisions with the world (verlet), drawn as a smooth round tube. A particle only ever moves
    /// by sweeping from its last free position, so the cord cannot pass through floors or walls; a span that cuts through
    /// a corner pushes its particles back out. It has a length:
    ///  - with <see cref="maxLength"/> longer, the rest is wound on a coil at end B and pays out as the ends draw apart;
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
        /// <summary>Longer than <see cref="length"/>: the rest is wound on a coil at B and pays out as needed (an extension cord).</summary>
        public float maxLength;
        /// <summary>Space between the particles (m): with the curve drawn through them, short enough to look like one cord.</summary>
        public const float Spacing = 0.07f;
        /// <summary>How straight each joint stays: its neighbours at least this share of two spans apart (about 50 degrees at most).</summary>
        const float BendMin = 0.9f;
        /// <summary>Share of a too-sharp bend put right each round of the solver.</summary>
        const float BendK = 0.25f;
        /// <summary>Speed kept each step (air and the cord's own give).</summary>
        const float Damp = 0.985f;
        /// <summary>Share of its speed a particle loses touching something (1: all).</summary>
        const float Friction = 0.6f, Stick = 1f;
        /// <summary>Settled (<see cref="Settle"/>): no particle further than this from where it was for this long.</summary>
        const float SettleRange = 0.015f, SettleTime = 0.8f;
        /// <summary>Fastest it pays out of its coil (m/s): it slides off, it does not appear.</summary>
        const float FeedRate = 5f;
        /// <summary>Paying out, so much more than the way from end to end along the floor (m).</summary>
        const float FeedSlack = 0.25f;
        /// <summary>Some of it is still wound on its coil.</summary>
        public bool Feeding => maxLength > length + 1e-3f;
        public System.Action<Cord> onTaut;     // pulled past its length with neither end able to give (owner decides)
        /// <summary>Being reeled in: a plug that pops out does not jerk the hand.</summary>
        public bool reeling;
        public static readonly List<Cord> All = new List<Cord>();

        Vector3[] _p, _prev, _valid, _before;
        int _n;
        float _seg, _sleepT;
        bool _asleep;
        Vector3 _lastA, _lastB;
        Mesh _mesh;
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector3> _norms = new List<Vector3>();
        readonly List<int> _tris = new List<int>();
        readonly RaycastHit[] _hits = new RaycastHit[6];
        const int Sides = 8;
        readonly List<Vector3> _path = new List<Vector3>();

        public Vector3 A => endA ? endA.transform.TransformPoint(aLocal) : transform.position;
        public Vector3 B => endB ? endB.transform.TransformPoint(bLocal) : transform.position;
        /// <summary>Straight-line distance between the ends over the cord's length.</summary>
        public float Stretch => Vector3.Distance(A, B) / Mathf.Max(0.01f, length);

        public static Cord Create(string name, WorldItem a, Vector3 aLocal, WorldItem b, Vector3 bLocal, float length, float radius, Material material, float maxLength = 0f)
        {
            var go = new GameObject(name);
            var c = go.AddComponent<Cord>();
            c.endA = a; c.aLocal = aLocal; c.endB = b; c.bLocal = bLocal; c.length = length; c.radius = radius; c.maxLength = maxLength;
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
            _n = Mathf.Max(4, Mathf.CeilToInt(length / Spacing) + 1);
            _seg = length / (_n - 1);
            int room = Mathf.Max(_n, Mathf.CeilToInt(maxLength / Spacing) + 1);     // room for what is still on the coil
            _p = new Vector3[room]; _prev = new Vector3[room]; _valid = new Vector3[room]; _before = new Vector3[room]; _onFloor = null; _wrap = null;
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

        public void Wake() { _asleep = false; _sleepT = 0f; _settleT = -1f; _endsStill = 0f; }

        // (2026-11-08 profile: lamp cords whose ends jiggled a millimetre, a lamp resting on the floor, never slept: about
        // 0.4 ms each every frame. Woken now only when an end has moved 2 mm from where it was, and asleep once its ends have
        // been still 2.5 s whatever the particles do.)
        Vector3 _refA, _refB;
        float _endsStill;

        /// <summary>Wakes every cord lying in a box (a wall that changed under it: it may have to fall).</summary>
        public static void WakeAround(Bounds b)
        {
            foreach (var c in All)
            {
                if (!c) continue;
                var r = c.GetComponent<Renderer>();
                if (r && r.bounds.Intersects(b)) { c.Wake(); if (c._onFloor != null) System.Array.Clear(c._onFloor, 0, c._onFloor.Length); }
            }
        }

        Vector3[] _rest;
        float _settleT = -1f;

        /// <summary>
        /// Lying over an edge (a hole's sill, the edge of a box) a cord never comes quite to rest: the particle on the edge
        /// is put back on it, slips, is put back. With its ends still, once no particle has strayed more than
        /// <see cref="SettleRange"/> from where it was for <see cref="SettleTime"/>, it is left lying where it is (asleep).
        /// </summary>
        void Settle(float dt)
        {
            if (_rest == null || _rest.Length < _p.Length) _rest = new Vector3[_p.Length];
            float r2 = SettleRange * SettleRange;
            bool still = _settleT >= 0f;
            if (still)
                for (int i = 1; i < _n - 1; i++)
                    if ((_p[i] - _rest[i]).sqrMagnitude > r2) { still = false; break; }
            if (!still) { System.Array.Copy(_p, _rest, _n); _settleT = 0f; return; }
            _settleT += dt;
            if (_settleT >= SettleTime)
            {
                // Rest exactly as it is now, without speed (it wakes when an end moves).
                System.Array.Copy(_p, _prev, _n); System.Array.Copy(_p, _valid, _n);
                _asleep = true;
            }
        }

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
            if ((a - _refA).sqrMagnitude > 4e-6f || (b - _refB).sqrMagnitude > 4e-6f) { _refA = a; _refB = b; Wake(); }
            else _endsStill += Time.deltaTime;
            _lastA = a; _lastB = b;

            Feed(a, b);
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
                if (!_asleep) Settle(dt);
                if (_endsStill > 2.5f && !reeling) _asleep = true;
                Rebuild();
            }
        }

        /// <summary>
        /// Still partly on its coil: pays out as much as the way from end to end needs (down to the floor, along it and up
        /// again, with a little to spare), so it lies on the floor behind you as you walk off. New cord comes off the coil at
        /// B: a particle is added there whenever the spans grow longer than <see cref="Spacing"/>.
        /// </summary>
        void Feed(Vector3 a, Vector3 b)
        {
            if (!Feeding || reeling || _p == null) return;
            Vector3 mid = (a + b) * 0.5f;
            float floor = Mathf.Min(a.y, b.y);
            if (FirstHit(mid, Vector3.down, 3f, out var hit)) floor = Mathf.Min(floor, hit.point.y);
            float flat = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            float want = Mathf.Max(Vector3.Distance(a, b), flat + (a.y - floor) + (b.y - floor)) + FeedSlack;
            if (want <= length) return;
            length = Mathf.Min(maxLength, Mathf.Min(want, length + FeedRate * Mathf.Max(Time.deltaTime, 1e-3f)));
            while (_n < _p.Length && length / (_n - 1) > Spacing) AddAtB();
            _seg = length / (_n - 1);
            Wake();
        }

        /// <summary>A particle more, just before end B (cord coming off the coil), halfway along the last span.</summary>
        void AddAtB()
        {
            int e = _n - 1;
            _p[e + 1] = _p[e]; _prev[e + 1] = _prev[e]; _valid[e + 1] = _valid[e];
            Vector3 q = Sweep(_valid[e - 1], Vector3.Lerp(_p[e - 1], _p[e], 0.5f), out _);
            _p[e] = _prev[e] = _valid[e] = q;
            _n++;
            if (_onFloor != null) { _onFloor[e] = false; _onFloor[e + 1] = false; }
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
            if (Feeding && !reeling) { _pull = 0f; _tugged = false; return; }     // there is more on the coil
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
            Audio.WorldSound.PlayAt(Audio.SoundBank.CordWhip, held.transform.position, 0.45f * Audio.SoundCatalog.Volume(Audio.SoundIds.Cords));
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
            Audio.WorldSound.PlayAt(Audio.SoundBank.CordYank, held.transform.position, Audio.SoundCatalog.Volume(Audio.SoundIds.Cords));
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
            // (TryGetComponent: GetComponent finding nothing allocated an error message in the editor for every particle's
            // every hit, every frame: 2 ms and 161 KB a frame with three lamps out, 2026-11-08 profile.)
            if (c.isTrigger || c is CharacterController || c.TryGetComponent<CharacterController>(out _) || BrokenWall(c)) return true;
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
        bool[] _onFloor;

        void Collide()
        {
            if (_onFloor == null || _onFloor.Length < _p.Length) _onFloor = new bool[_p.Length];
            for (int i = 1; i < _n - 1; i++)
            {
                Vector3 d = _p[i] - _valid[i];
                if (d.sqrMagnitude < 1e-12f) continue;
                // Lying on the floor and only pressed down into it (gravity): it stays, no need to ask the physics.
                if (_onFloor[i] && d.y <= 0f && d.x * d.x + d.z * d.z < 2e-4f * 2e-4f)
                {
                    _p[i] = _valid[i];
                    _prev[i] = Vector3.Lerp(_prev[i], _valid[i], Friction);
                    continue;
                }
                OutOfWalls(i);
                Vector3 want = _p[i];
                Vector3 got = Sweep(_valid[i], want, out _, out bool hit, out Vector3 normal);
                _onFloor[i] = false;
                if (got != want && hit)
                {
                    // Slide along the surface with what is left of the move.
                    Vector3 rest = Vector3.ProjectOnPlane(want - got, normal);
                    if (rest.sqrMagnitude > 1e-8f) got = Sweep(got, got + rest, out _);
                    _prev[i] = Vector3.Lerp(_prev[i], got, Friction);            // friction
                    _onFloor[i] = normal.y > 0.7f;
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
        /// A span that cuts through the solid part of a broken wall is moved out of it by as little as will do: over the
        /// edge it is draped across (a hole's sill, the face of the wall), or (its ends either side of the wall) into the
        /// nearest opening, just clear of its edge. Both particles share the move, the one nearer the cut more (a pinned end
        /// none), so a cord lying through a hole comes to rest on the drywall and stays still. A wall with no opening near:
        /// the nearer particle goes back to its own side. True if it moved one.
        /// </summary>
        bool ThroughWall(int i)
        {
            Vector3 a = _p[i], b = _p[i + 1];
            if (!SlabMarch(a, b, out _, out _, out var bp)) return false;
            // The solid stretch of the span: from the first point inside, marching in from either end.
            float t0 = FirstInside(bp, a, b), t1 = 1f - FirstInside(bp, b, a);
            if (t0 < 0f) return false;
            if (t1 < t0) t1 = t0;
            float tm = (t0 + t1) * 0.5f;
            var k = bp.Key; float pitch = Destruction.WallDamage.Pitch;
            float h = Destruction.WallSpec.T * 0.5f + radius;
            Vector3 la = k.ToLocal(a, pitch), lb = k.ToLocal(b, pitch), lm = k.ToLocal(Vector3.Lerp(a, b, tm), pitch);
            bool across = Mathf.Abs(la.z) >= h && Mathf.Abs(lb.z) >= h && Mathf.Sign(la.z) != Mathf.Sign(lb.z);
            bool aFree = i > 0, bFree = i + 1 < _n - 1;
            if (!aFree && !bFree) return false;
            Vector3 da = Vector3.zero, db = Vector3.zero;
            // Over a sill (open just above the solid part, as along the bottom of a hole): lifted straight up, as a floor
            // holds it, so gravity and the edge meet head on and it comes to rest; pushed out sideways (or up at a slant)
            // it slid off, was put back and never stopped moving.
            if (SillLift(bp, a, b, t0, t1, out float tl, out float lift))
            {
                if (!Share(Vector3.up * lift, tl, aFree, bFree, out da, out db)) return false;
                bool m0 = da.sqrMagnitude > 1e-10f && Nudge(i, da);
                bool m1 = db.sqrMagnitude > 1e-10f && Nudge(i + 1, db);
                return m0 | m1;
            }
            bool hasFace = Destruction.WallSlabs.Escapes(bp, lm, radius, Skin * 2f, !across, out var face, out bool hasOpen, out var inPlane);
            if (hasFace || hasOpen)
            {
                // Of the ways out, the one that leaves the whole span clear (over a sill's corner that is up, over it, not
                // out along the face), the smaller if both do.
                Vector3 fa = Vector3.zero, fb = Vector3.zero, oa = Vector3.zero, ob = Vector3.zero;
                bool faceClear = hasFace && Share(k.DirToWorld(face), tm, aFree, bFree, out fa, out fb) && FirstInside(bp, a + fa, b + fb) < 0f;
                bool openClear = hasOpen && Share(k.DirToWorld(inPlane), tm, aFree, bFree, out oa, out ob) && FirstInside(bp, a + oa, b + ob) < 0f;
                bool useOpen;
                if (faceClear && openClear) useOpen = inPlane.sqrMagnitude <= face.sqrMagnitude;
                else if (openClear || faceClear) useOpen = openClear;
                else useOpen = hasOpen && (!hasFace || inPlane.sqrMagnitude <= face.sqrMagnitude * 4f);   // neither yet: rather over the edge
                if (useOpen) { da = oa; db = ob; } else { da = fa; db = fb; }
            }
            else
            {
                // Whole wall here: the nearer free particle back to its own side.
                int j = !aFree ? i + 1 : !bFree ? i : (Mathf.Abs(la.z) <= Mathf.Abs(lb.z) ? i : i + 1);
                Vector3 lj = j == i ? la : lb;
                Vector3 to = k.ToWorld(new Vector3(lj.x, lj.y, (Mathf.Abs(lj.z) > 1e-3f ? Mathf.Sign(lj.z) : 1f) * (h + Skin * 2f)), pitch);
                if (j == i) da = to - a; else db = to - b;
            }
            bool moved = false;
            if (da.sqrMagnitude > 1e-10f) moved |= Nudge(i, da);
            if (db.sqrMagnitude > 1e-10f) moved |= Nudge(i + 1, db);
            return moved;
        }

        /// <summary>
        /// The span's deepest point under a sill (the solid stretch t0..t1, 5 mm steps): where along it (tl) and how far it
        /// must rise to clear the sill's edge. False if the wall is not open within 15 cm above it anywhere (not a sill).
        /// </summary>
        bool SillLift(Destruction.BrokenPanel bp, Vector3 a, Vector3 b, float t0, float t1, out float tl, out float lift)
        {
            tl = (t0 + t1) * 0.5f; lift = 0f;
            var k = bp.Key; float pitch = Destruction.WallDamage.Pitch;
            int steps = Mathf.Max(1, Mathf.CeilToInt((t1 - t0) * Vector3.Distance(a, b) / 0.005f));
            bool any = false;
            for (int s = 0; s <= steps; s++)
            {
                float t = Mathf.Lerp(t0, t1, s / (float)steps);
                Vector3 q = Vector3.Lerp(a, b, t);
                if (!Destruction.WallSlabs.Blocked(bp, q, radius)) continue;
                var l = k.ToLocal(q, pitch);
                if (!Destruction.WallSlabs.OpenAbove(bp.Panel, l.x, l.y, radius, 0.15f, out float top)) return false;
                float need = top - l.y;
                if (!any || need > lift) { lift = need; tl = t; any = true; }
            }
            if (!any) return false;
            lift += Skin;
            return true;
        }

        /// <summary>
        /// A move m of the span's point at tm, shared between its particles so that point moves by m with the least motion
        /// ((1 - tm) da + tm db = m); a pinned end takes none. False if neither can move.
        /// </summary>
        static bool Share(Vector3 m, float tm, bool aFree, bool bFree, out Vector3 da, out Vector3 db)
        {
            da = db = Vector3.zero;
            if (!aFree && !bFree) return false;
            float wa = aFree ? 1f - tm : 0f, wb = bFree ? tm : 0f;
            float s = wa * wa + wb * wb;
            if (s < 0.04f) { if (aFree) da = m * 2f; else db = m * 2f; }
            else { da = m * (wa / s); db = m * (wb / s); }
            return true;
        }

        /// <summary>Where along from-to (0..1, 5 mm steps) the cord first enters the wall's solid part; -1 if it does not.</summary>
        float FirstInside(Destruction.BrokenPanel bp, Vector3 from, Vector3 to)
        {
            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(from, to) / 0.005f));
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                if (Destruction.WallSlabs.Blocked(bp, Vector3.Lerp(from, to, t), radius)) return t;
            }
            return -1f;
        }

        /// <summary>Moves a particle by d (swept: never into anything), and takes most of its speed (it rests where it is put).</summary>
        bool Nudge(int j, Vector3 d)
        {
            if (j <= 0 || j >= _n - 1) return false;
            if (d.sqrMagnitude > 0.15f * 0.15f) d = d.normalized * 0.15f;
            Vector3 got = Sweep(_valid[j], _p[j] + d, out _);
            if ((got - _p[j]).sqrMagnitude < 1e-12f) return false;
            _p[j] = _valid[j] = got;
            _prev[j] = Vector3.Lerp(_prev[j], got, Stick);
            return true;
        }

        /// <summary>
        /// A span between two particles that cuts through something (around a corner): the particle nearer the corner is put
        /// on the corner's outside edge, so the cord wraps it. The corner is where the two faces the span goes in and out
        /// of meet (or, for one face, just clear of it).
        /// </summary>
        void Spans()
        {
            if (_wrap == null || _wrap.Length < _p.Length) _wrap = new bool[_p.Length];
            const float still = 0.0003f * 0.0003f;
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < _n - 1; i++)
            {
                // Neither end moved this step and it was clear: it still is.
                if (!_wrap[i] && (_p[i] - _before[i]).sqrMagnitude < still && (_p[i + 1] - _before[i + 1]).sqrMagnitude < still) continue;
                bool moved = FixSpan(i);
                _wrap[i] = pass == 0 ? moved : _wrap[i] | moved;
            }
        }

        bool[] _wrap;

        /// <summary>
        /// Inside the solver, every few rounds: the spans that were lying over an edge last step are put right again, so
        /// the cord's length and its edges agree before the step ends (done only after, the two undid each other every
        /// step, and a cord over an edge never stopped moving).
        /// </summary>
        void Wraps()
        {
            if (_wrap == null) return;
            for (int i = 0; i < _n - 1; i++) if (_wrap[i]) FixSpan(i);
        }

        /// <summary>One span through something: put round it (a broken wall: through its opening). True if it moved one.</summary>
        bool FixSpan(int i)
        {
            {
                Vector3 a = _p[i], b = _p[i + 1], d = b - a;
                float len = d.magnitude;
                if (len < 1e-4f) return false;
                Vector3 dir = d / len;
                if (ThroughWall(i)) return true;
                if (!FirstHit(a, dir, len, out var inHit)) return false;
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
                if (k == 0 || k == _n - 1) return false;
                var got = Sweep(_valid[k], target, out _);
                _p[k] = _valid[k] = got;
                _prev[k] = Vector3.Lerp(_prev[k], got, Stick);                // it sticks on the edge rather than bouncing off
                return true;
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
            var before = _before;
            float moved = 0f;
            before[0] = _p[0]; before[_n - 1] = _p[_n - 1];
            for (int i = 1; i < _n - 1; i++)
            {
                before[i] = _p[i];
                Vector3 v = (_p[i] - _prev[i]) * Damp;
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
                Bend();
                if (it % 4 == 3) Wraps();
            }
            Collide();
            Spans();
            for (int i = 1; i < _n - 1; i++) moved = Mathf.Max(moved, (_p[i] - before[i]).magnitude);
            return moved / Mathf.Max(dt, 1e-4f) * 0.016f;
        }

        /// <summary>
        /// A little stiffness: a particle's two neighbours are kept at least <see cref="BendMin"/> of two spans apart, so no
        /// joint folds more than about 50 degrees. It lies in curves, drapes over edges in a bend, not a crease.
        /// </summary>
        void Bend()
        {
            float min = 2f * _seg * BendMin, min2 = min * min;
            for (int i = 1; i < _n - 1; i++)
            {
                Vector3 d = _p[i + 1] - _p[i - 1];
                float l2 = d.sqrMagnitude;
                if (l2 >= min2 || l2 < 1e-12f) continue;
                float l = Mathf.Sqrt(l2);
                Vector3 c = d * ((min - l) / l * BendK);
                bool pa = i - 1 == 0, pb = i + 1 == _n - 1;
                if (pa && pb) continue;
                if (pa) _p[i + 1] += c * 2f;
                else if (pb) _p[i - 1] -= c * 2f;
                else { _p[i - 1] -= c; _p[i + 1] += c; }
            }
        }

        void Rebuild()
        {
            if (!_mesh) { _mesh = new Mesh { name = "Cord" }; _mesh.MarkDynamic(); GetComponent<MeshFilter>().sharedMesh = _mesh; }
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _verts.Clear(); _norms.Clear(); _tris.Clear();
            // A smooth path through the particles (Catmull-Rom, three points per span).
            var path = _path; path.Clear();
            for (int i = 0; i < _n - 1; i++)
            {
                Vector3 p0 = _p[Mathf.Max(0, i - 1)], p1 = _p[i], p2 = _p[i + 1], p3 = _p[Mathf.Min(_n - 1, i + 2)];
                path.Add(p1);
                for (int k = 1; k < 3; k++)
                {
                    float t = k / 3f, t2 = t * t, t3 = t2 * t;
                    path.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
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
