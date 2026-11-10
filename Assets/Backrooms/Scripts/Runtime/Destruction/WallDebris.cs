using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Destruction
{
    /// <summary>
    /// A part knocked out of a wall (a plate of board, a baseboard, a length of stud, a batt) falling and tumbling as a
    /// light physics body. Once it comes to rest it becomes rubble: its pose is kept with its wall (and saved), it joins
    /// the wall's rubble mesh, and this body goes. Small things do not trip the player up.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WallDebris : MonoBehaviour
    {
        public WallKey key;
        public Fallen.Part part;
        public int side, index;
        public Vector2 span;
        public bool steel;
        Rigidbody _rb;
        float _born, _still, _quietUntil;
        Mesh _mesh;

        public static int Active { get; private set; }
        static PanelMesh s_mb;
        static PhysicsMaterial s_board;

        public static WallDebris Spawn(WallDamage.Panel p, Fallen f, Transform parent, Collider player)
        {
            var m = p.model;
            float pitch = WallDamage.Pitch;
            s_mb ??= new PanelMesh(PanelGeometry.Slots);
            s_mb.Clear();
            Vector3 centre = BrokenPanel.Part(s_mb, m, f.part, f.side, f.index, f.span, pitch);
            var go = new GameObject("WallDebris");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, Quaternion.identity);
            var d = go.AddComponent<WallDebris>();
            d.key = m.key; d.part = f.part; d.side = f.side; d.index = f.index; d.span = f.span; d.steel = m.steel;
            d._mesh = s_mb.Apply(null, PanelGeometry.Materials(m), out var used, "WallDebris");
            go.AddComponent<MeshFilter>().sharedMesh = d._mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = used;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.renderingLayerMask = 0xFFFFFFFFu;
            var box = go.AddComponent<BoxCollider>();
            float mass;
            switch (f.part)
            {
                case Fallen.Part.Board:
                    box.size = PanelGeometry.PieceSize(m, f.side, f.index);
                    mass = Mathf.Max(0.02f, m.boards[f.side].pieces[f.index].area * 8.5f);           // 12.7 mm board: 8.5 kg/m^2
                    break;
                case Fallen.Part.Baseboard: box.size = PanelGeometry.BaseboardSize(m, f.index); mass = 0.25f; break;
                case Fallen.Part.Stud: box.size = PanelGeometry.StudSize(m, f.span); mass = (f.span.y - f.span.x) * (m.steel ? 0.6f : 2.3f); break;
                default: box.size = PanelGeometry.BattSize(m, f.index / m.rows); box.size *= 0.9f; mass = 0.25f; break;
            }
            if (!s_board) s_board = new PhysicsMaterial("Wall debris") { dynamicFriction = 0.75f, staticFriction = 0.85f, bounciness = 0.04f, frictionCombine = PhysicsMaterialCombine.Maximum };
            box.sharedMaterial = s_board;
            if (player) Physics.IgnoreCollision(player, box, true);
            var rb = go.GetComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.02f, mass);
            rb.linearDamping = f.part == Fallen.Part.Insulation ? 1.5f : 0.05f;
            rb.angularDamping = f.part == Fallen.Part.Insulation ? 2f : 0.3f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearVelocity = f.velocity;
            rb.angularVelocity = f.spin;
            rb.sleepThreshold = 0.03f;
            // It starts where it was in the wall, touching what is left and its neighbours: eased apart, not shot out.
            rb.maxDepenetrationVelocity = 0.6f;
            return d;
        }

        void Awake() { _rb = GetComponent<Rigidbody>(); _born = Time.time; Active++; }
        void OnDestroy() { Active--; if (_mesh) Destroy(_mesh); }

        void FixedUpdate()
        {
            float age = Time.time - _born;
            if (transform.position.y < -40f) { Destroy(gameObject); return; }
            _still = _rb.linearVelocity.sqrMagnitude < 0.0025f && _rb.angularVelocity.sqrMagnitude < 0.05f ? _still + Time.fixedDeltaTime : 0f;
            if (age > 0.6f && (_rb.IsSleeping() || _still > 0.6f) || age > 12f)
            {
                // Come to rest wedged in the wall (in the cavity, on the edge of a hole, against a stud): it would hang in
                // the hole for good. Knocked out of the wall's plane, twice at most, then laid on the floor below.
                if (WallDamage.Lodged(key, transform.position))
                {
                    if (_nudges < 2 && age < 12f) { Nudge(); return; }
                    WallDamage.SettleOnFloor(key, part, side, index, span, transform.position);
                    Destroy(gameObject);
                    return;
                }
                Settle();
            }
        }

        int _nudges;

        void Nudge()
        {
            _nudges++;
            var l = key.ToLocal(transform.position, WallDamage.Pitch);
            float sgn = Mathf.Abs(l.z) > 0.005f ? Mathf.Sign(l.z) : (side == 0 ? -1f : 1f);
            _rb.WakeUp();
            _rb.linearVelocity = key.DirToWorld(new Vector3(0f, 0f, sgn)).normalized * 0.9f + Vector3.up * 0.3f;
            _rb.angularVelocity = Random.insideUnitSphere * 5f;
            _still = 0f;
            _born = Time.time - 0.2f;
        }

        /// <summary>Comes to rest here: rubble from now on.</summary>
        public void Settle()
        {
            WallDamage.Settle(key, new WallDamage.Rubble { part = part, side = side, index = index, span = span, position = transform.position, rotation = transform.rotation });
            Destroy(gameObject);
        }

        void OnCollisionEnter(Collision c)
        {
            if (Time.time < _quietUntil) return;
            float v = c.relativeVelocity.magnitude;
            if (v < 0.9f) return;
            _quietUntil = Time.time + 0.25f;
            WallBreaker.DebrisLanded(transform.position, part, steel, Mathf.Clamp01((v - 0.8f) / 4f) * Mathf.Clamp01(_rb.mass * 6f));
        }
    }
}
