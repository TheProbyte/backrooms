using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A flexible electrical cord between two items (a lamp and its plug; an extension cord's plug and its box): a chain of
    /// particles every ~12 cm with gravity, length constraints and collisions with the world (verlet), drawn as a round
    /// tube. It has a fixed length:
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

        Vector3[] _p, _prev;
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
            r.shadowCastingMode = ShadowCastingMode.On;
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
            _p = new Vector3[_n]; _prev = new Vector3[_n];
            Vector3 a = A, b = B;
            float slack = Mathf.Max(0f, length - Vector3.Distance(a, b));
            for (int i = 0; i < _n; i++)
            {
                float t = i / (float)(_n - 1);
                _p[i] = _prev[i] = Vector3.Lerp(a, b, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * slack * 0.35f);
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
                (plugA && plugA.Plugged ? plugA : plugB).Unplug(true);
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

        bool Ignored(Collider c)
        {
            if (c.isTrigger || c.GetComponent<CharacterController>()) return true;
            var t = c.transform;
            return (endA && t.IsChildOf(endA.transform)) || (endB && t.IsChildOf(endB.transform));
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
            for (int it = 0; it < 12; it++)
            {
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
            // Collisions: sweep each particle from where it was to where it went.
            for (int i = 1; i < _n - 1; i++)
            {
                Vector3 from = before[i], delta = _p[i] - from;
                float dist = delta.magnitude;
                if (dist < 1e-5f) continue;
                int hits = Physics.SphereCastNonAlloc(from, radius, delta / dist, _hits, dist, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue; int bi = -1;
                for (int h = 0; h < hits; h++)
                {
                    if (Ignored(_hits[h].collider)) continue;
                    if (_hits[h].distance == 0f && _hits[h].point == Vector3.zero) continue;   // started inside: let the constraints free it
                    if (_hits[h].distance < best) { best = _hits[h].distance; bi = h; }
                }
                if (bi >= 0)
                {
                    var hit = _hits[bi];
                    _p[i] = from + delta / dist * Mathf.Max(0f, hit.distance - 0.0005f);
                    _prev[i] = Vector3.Lerp(_prev[i], _p[i], 0.7f);              // friction against the surface
                }
                moved = Mathf.Max(moved, (_p[i] - before[i]).magnitude);
            }
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
