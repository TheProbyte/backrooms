using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A prop that can be grabbed, dragged, tipped and shoved. It stays kinematic (frozen, cheap) until
    /// something disturbs it, then simulates until it settles and freezes again, and its new pose is
    /// saved (<see cref="MovablePropManager"/>), so it is still there after its chunk unloads and reloads.
    /// </summary>
    [DisallowMultipleComponent]
    public class MovableProp : MonoBehaviour
    {
        public MovableSpec Spec { get; private set; }
        public MovableShape Shape { get; private set; }
        public Rigidbody Body { get; private set; }
        public float Mass => Body ? Body.mass : 0f;
        /// <summary>Held by the player's hand right now (never frozen while held).</summary>
        [System.NonSerialized] public bool held;

        float _still;
        Vector3 _lastGoodPos;
        Quaternion _lastGoodRot;

        static Mesh _cube;
        static PhysicsMaterial _rolling;
        static readonly System.Collections.Generic.Dictionary<float, PhysicsMaterial> _sliding = new System.Collections.Generic.Dictionary<float, PhysicsMaterial>();

        public static Mesh Cube
        {
            get
            {
                if (!_cube)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _cube = go.GetComponent<MeshFilter>().sharedMesh;
                    Destroy(go);
                }
                return _cube;
            }
        }

        /// <summary>Builds the prop: visible parts, collider boxes, shadow proxies, a frozen rigidbody.</summary>
        public static MovableProp Spawn(MovableSpec spec, Transform parent, Vector3 position, Quaternion rotation)
        {
            var root = new GameObject($"{spec.kind}_{spec.id & 0xFFFF:X4}");
            foreach (var p in spec.parts)
            {
                GameObject go;
                if (p.prefab)
                {
                    go = Object.Instantiate(p.prefab, root.transform, false);
                    go.transform.localPosition = p.position;
                    go.transform.localRotation = Quaternion.Euler(0f, p.yaw, 0f) * p.prefab.transform.localRotation;   // keep the model's own axis fix
                    go.transform.localScale = p.scale;
                    foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
                }
                else
                {
                    go = new GameObject("Box");
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = p.position;
                    go.transform.localRotation = Quaternion.Euler(0f, p.yaw, 0f);
                    go.transform.localScale = p.scale;
                    go.AddComponent<MeshFilter>().sharedMesh = Cube;
                    go.AddComponent<MeshRenderer>().sharedMaterial = p.material;
                }
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    r.renderingLayerMask = spec.renderMask;
                    r.shadowCastingMode = ShadowCastingMode.Off;    // the proxies cast its shadow
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
            }

            var builtMeshes = new System.Collections.Generic.List<Mesh>();
            if (spec.cardboard && spec.materials != null) BuildCardboard(root, spec, builtMeshes);

            var shape = MovableShapes.Build(spec, root);
            var mat = shape.rolls ? Rolling : Sliding(shape.friction);
            foreach (var (c, s) in shape.boxes)
            {
                var bc = root.AddComponent<BoxCollider>();
                bc.center = c; bc.size = s; bc.sharedMaterial = mat;
                if (spec.proxyMaterial)
                {
                    var px = new GameObject("ShadowProxy");
                    px.transform.SetParent(root.transform, false);
                    px.transform.localPosition = c;
                    px.transform.localScale = s - new Vector3(0.02f, 0.02f, 0.02f);
                    px.AddComponent<MeshFilter>().sharedMesh = Cube;
                    var pr = px.AddComponent<MeshRenderer>();
                    pr.sharedMaterial = spec.proxyMaterial;
                    pr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    pr.renderingLayerMask = 0xFFFFFFFFu;
                    pr.lightProbeUsage = LightProbeUsage.Off;
                }
            }

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = shape.mass;
            rb.centerOfMass = shape.centreOfMass;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = shape.mass < 10f ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;
            rb.maxDepenetrationVelocity = 1.2f;                  // things that start slightly overlapping ease apart
            rb.linearDamping = 0.05f;
            rb.angularDamping = shape.rolls ? 0.6f : 0.25f;

            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, rotation);
            var mp = root.AddComponent<MovableProp>();
            mp.Spec = spec; mp.Shape = shape; mp.Body = rb;
            mp._ownedMeshes = builtMeshes;
            mp._lastGoodPos = position; mp._lastGoodRot = rotation;
            return mp;
        }

        static PhysicsMaterial Rolling => _rolling ? _rolling : (_rolling = new PhysicsMaterial("Casters")
        {
            dynamicFriction = 0.04f, staticFriction = 0.06f, frictionCombine = PhysicsMaterialCombine.Minimum, bounciness = 0.05f,
        });

        static PhysicsMaterial Sliding(float f)
        {
            // Minimum combine: the prop decides how it slides on carpet (felt pads, cardboard).
            if (!_sliding.TryGetValue(f, out var m) || !m)
                _sliding[f] = m = new PhysicsMaterial("Furniture") { dynamicFriction = f, staticFriction = f * 1.2f, bounciness = 0.02f, frictionCombine = PhysicsMaterialCombine.Minimum };
            return m;
        }

        System.Collections.Generic.List<Mesh> _ownedMeshes;

        void OnDestroy()
        {
            if (_ownedMeshes != null) foreach (var m in _ownedMeshes) if (m) Destroy(m);
        }

        /// <summary>Builds the box visuals (board, tape, print) as three child meshes.</summary>
        static void BuildCardboard(GameObject root, MovableSpec spec, System.Collections.Generic.List<Mesh> meshes)
        {
            var board = new MeshWriter { worldOffset = spec.position };
            var tape = new MeshWriter { worldOffset = spec.position };
            var print = new MeshWriter { worldOffset = spec.position };
            CardboardBoxGeometry.Build(board, tape, print, Vector3.zero, spec.size, 0f, new System.Random(spec.variant));
            var writers = new[] { board, tape, print };
            for (int i = 0; i < 3; i++)
            {
                if (writers[i].VertexCount == 0) continue;
                var go = new GameObject("Box" + i);
                go.transform.SetParent(root.transform, false);
                var mesh = writers[i].ToMesh("Carton" + i);
                meshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = spec.materials[Mathf.Min(i, spec.materials.Length - 1)];
                r.renderingLayerMask = spec.renderMask;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        /// <summary>Starts simulating (and marks the prop as moved, so its pose is saved from now on).</summary>
        public void Wake()
        {
            _still = 0f;
            if (!Body.isKinematic) { Body.WakeUp(); return; }
            MovablePropManager.Instance?.Detach(this);
            Body.isKinematic = false;
            Body.WakeUp();
            // Frozen props touching this one (chairs tucked under a table) join in, or they would act as walls.
            foreach (var (c, s) in Shape.boxes)
            {
                int n = Physics.OverlapBoxNonAlloc(transform.TransformPoint(c), s * 0.5f + Vector3.one * 0.03f, _touching, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var o = _touching[i].attachedRigidbody ? _touching[i].attachedRigidbody.GetComponent<MovableProp>() : null;
                    if (o && o != this && o.Body.isKinematic) o.Wake();
                }
            }
        }

        static readonly Collider[] _touching = new Collider[16];

        void FixedUpdate()
        {
            if (Body.isKinematic) return;
            // Fell out of the world (unloaded floor, deep overlap): put it back where it last rested.
            if (transform.position.y < _lastGoodPos.y - 3f)
            {
                Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero;
                Body.position = _lastGoodPos; Body.rotation = _lastGoodRot;
            }
            if (held) { _still = 0f; return; }
            bool still = Body.linearVelocity.sqrMagnitude < 0.0025f && Body.angularVelocity.sqrMagnitude < 0.01f;
            _still = still ? _still + Time.fixedDeltaTime : 0f;
            if (_still > 0.8f || Body.IsSleeping()) Freeze();
        }

        /// <summary>Stops simulating where it is and saves the pose.</summary>
        public void Freeze()
        {
            if (Body.isKinematic) return;
            Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero;
            Body.isKinematic = true;
            _lastGoodPos = transform.position; _lastGoodRot = transform.rotation;
            MovablePropManager.Instance?.Store(this);
        }

        void OnCollisionEnter(Collision c)
        {
            // Something moving hits a frozen prop hard enough: it joins in.
            var other = c.rigidbody ? c.rigidbody.GetComponent<MovableProp>() : null;
            if (other && other.Body.isKinematic && !Body.isKinematic && c.relativeVelocity.sqrMagnitude > 0.16f)
            {
                other.Wake();
                other.Body.AddForceAtPosition(-c.impulse * 0.6f, c.GetContact(0).point, ForceMode.Impulse);
            }
        }

        /// <summary>Closest grab spot to a point the player is looking at: on one of the prop's edges, or the
        /// surface itself if no edge is near. A little randomness keeps grabs from looking identical.</summary>
        public bool FindGrip(Vector3 hitPoint, Vector3 hitNormal, Vector3 shoulder, System.Random rng, out Vector3 point, out Vector3 surfaceNormal, out Vector3 edgeDir)
        {
            float best = float.MaxValue;
            point = hitPoint; surfaceNormal = hitNormal; edgeDir = Vector3.zero;
            foreach (var e in Shape.edges)
            {
                Vector3 a = transform.TransformPoint(e.a), b = transform.TransformPoint(e.b);
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(hitPoint - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                Vector3 c = a + ab * t;
                float d = Vector3.Distance(c, hitPoint);
                if (d > 0.5f) continue;
                float score = d + 0.25f * Vector3.Distance(c, shoulder);
                if (score >= best) continue;
                best = score;
                // Not the exact same spot every time: a few centimetres either way along the edge.
                float len = ab.magnitude;
                float jitter = len > 0.1f ? (float)(rng.NextDouble() - 0.5) * Mathf.Min(0.1f, len * 0.3f) / len : 0f;
                point = a + ab * Mathf.Clamp01(t + jitter);
                surfaceNormal = transform.TransformDirection(e.normal);
                edgeDir = ab / Mathf.Max(len, 1e-4f);
            }
            return best < float.MaxValue;
        }
    }
}
