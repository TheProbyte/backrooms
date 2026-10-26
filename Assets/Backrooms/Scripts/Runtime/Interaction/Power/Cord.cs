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

        void OnDestroy() { if (_mesh) Destroy(_mesh); }

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
                if (move < 0.0005f) { _sleepT += dt; if (_sleepT > 0.6f) _asleep = true; } else _sleepT = 0f;
                Rebuild();
            }
        }

        /// <summary>Past its length: pop a plugged end, or drag a free end toward the held one.</summary>
        void Taut(Vector3 a, Vector3 b)
        {
            float d = Vector3.Distance(a, b);
            if (d <= length) return;
            bool aHeld = endA.Held, bHeld = endB.Held;
            var plugA = endA as Plug; var plugB = endB as Plug;
            if ((plugA && plugA.Plugged && !aHeld && (bHeld || d > length * 1.15f)) || (plugB && plugB.Plugged && !bHeld && (aHeld || d > length * 1.15f)))
            {
                var plug = plugA && plugA.Plugged ? plugA : plugB;
                var other = plug == plugA ? endB : endA;
                plug.Unplug(true);
                if (other.Held) Yank(plug, other);
                return;
            }
            WorldItem free = aHeld && !bHeld ? endB : bHeld && !aHeld ? endA : null;
            if (free && !free.Body.isKinematic)
            {
                Vector3 toward = (free == endA ? b - a : a - b) / Mathf.Max(d, 1e-4f);
                float excess = d - length;
                Vector3 v = free.Body.linearVelocity;
                float along = Vector3.Dot(v, toward);
                float want = excess / Mathf.Max(Time.deltaTime, 1e-3f) * 0.5f;
                if (along < want) free.Body.linearVelocity = v + toward * Mathf.Min(want - along, 6f);
                free.Body.WakeUp();
            }
            else if (!free && d > length * 1.02f) onTaut?.Invoke(this);
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

        bool Ignored(Collider c)
        {
            if (c.isTrigger || c.GetComponent<CharacterController>()) return true;
            var t = c.transform;
            return (endA && t.IsChildOf(endA.transform)) || (endB && t.IsChildOf(endB.transform));
        }

        const float Skin = 0.0015f;

        /// <summary>Moves a particle from a free position toward a wanted one, stopping just short of anything solid.</summary>
        Vector3 Sweep(Vector3 from, Vector3 to, out RaycastHit hitInfo)
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
                Vector3 want = _p[i];
                Vector3 got = Sweep(_valid[i], want, out var hit);
                if (got != want && hit.collider)
                {
                    // Slide along the surface with what is left of the move.
                    Vector3 rest = Vector3.ProjectOnPlane(want - got, hit.normal);
                    got = Sweep(got, got + rest, out _);
                    _prev[i] = Vector3.Lerp(_prev[i], got, 0.6f);            // friction
                }
                _p[i] = got; _valid[i] = got;
            }
        }

        /// <summary>A span between two particles that cuts through something (around a corner): push its free ends back out.</summary>
        void Spans()
        {
            for (int i = 0; i < _n - 1; i++)
            {
                Vector3 a = _p[i], d = _p[i + 1] - a;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                int hits = Physics.RaycastNonAlloc(a, d / len, _hits, len, ~0, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < hits; h++)
                {
                    if (Ignored(_hits[h].collider) || _hits[h].distance <= 0f) continue;
                    Vector3 push = _hits[h].normal * (radius * 2f + 0.004f);
                    if (i > 0) { var p = Sweep(_valid[i], _valid[i] + push, out _); _p[i] = _valid[i] = p; }
                    if (i + 1 < _n - 1) { var p = Sweep(_valid[i + 1], _valid[i + 1] + push, out _); _p[i + 1] = _valid[i + 1] = p; }
                    break;
                }
            }
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
