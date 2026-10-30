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
        /// <summary>Props whose models cast their own shadows (no box proxies): sofas and armchairs.</summary>
        static bool OwnShadow(MovableKind k) => k == MovableKind.Sofa || k == MovableKind.Armchair;
        /// <summary>Its own model casts its shadow (no box proxies): upholstery, and the meshes built at spawn (a carton, a
        /// pallet: closed shapes, and fewer objects for the many on a warehouse's racks).</summary>
        static bool OwnShadow(MovableSpec s) => OwnShadow(s.kind) || s.cardboard || s.kind == MovableKind.Pallet;

        public static MovableProp Spawn(MovableSpec spec, Transform parent, Vector3 position, Quaternion rotation)
        {
            var root = new GameObject($"{spec.kind}_{spec.id & 0xFFFF:X4}");
            foreach (var p in spec.parts)
            {
                GameObject go;
                if (p.prefab)
                {
                    go = Object.Instantiate(p.prefab, root.transform, false);
                    MaterialMaps.Apply(go);
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
                    // Upholstery casts its own soft-edged shadow (its box proxies left square shadows on the wall behind);
                    // everything else: the proxies cast its shadow.
                    r.shadowCastingMode = OwnShadow(spec) ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
            }

            var builtMeshes = new System.Collections.Generic.List<Mesh>();
            if (spec.cardboard && spec.materials != null) BuildCardboard(root, spec, builtMeshes);
            if (spec.kind == MovableKind.Pallet && spec.materials != null && spec.materials.Length > 0) BuildPallet(root, spec, builtMeshes);

            var shape = MovableShapes.Build(spec, root);
            var mat = shape.rolls ? Rolling : Sliding(shape.friction);
            foreach (var (c, s) in shape.boxes)
            {
                var bc = root.AddComponent<BoxCollider>();
                bc.center = c; bc.size = s; bc.sharedMaterial = mat;
                if (spec.proxyMaterial && !OwnShadow(spec))
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
            // Smoothed only while it moves (Wake/Freeze): interpolating hundreds of frozen boxes on a warehouse's racks cost
            // about 5 ms a frame.
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = shape.mass < 10f ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;
            rb.maxDepenetrationVelocity = 1.2f;                  // things that start slightly overlapping ease apart
            rb.linearDamping = 0.05f;
            rb.angularDamping = shape.rolls ? 0.6f : 0.25f;

            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, rotation);
            // (Not upholstery: its collision boxes darkened the wall behind it in a box above its back; it casts its own shadow.)
            if (!OwnShadow(spec.kind)) root.AddComponent<LightingOccluder>();
            root.AddComponent<Audio.ImpactSounder>();             // knocks, thumps and scraping (Audio/ImpactAudio)
            var mp = root.AddComponent<MovableProp>();
            mp.Spec = spec; mp.Shape = shape; mp.Body = rb;
            mp._ownedMeshes = builtMeshes;
            mp._lastGoodPos = position; mp._lastGoodRot = rotation;
            RuntimeIndoorReflections.Instance?.Prepare(root);
            PoolLitSwitch.Attach(root);                           // lit by the Poolrooms' light while it is in them
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

        /// <summary>Builds the box visuals (board, tape, print) as one mesh with a part for each, casting its own shadow.</summary>
        static void BuildCardboard(GameObject root, MovableSpec spec, System.Collections.Generic.List<Mesh> meshes)
        {
            var board = new MeshWriter { worldOffset = spec.position };
            var tape = new MeshWriter { worldOffset = spec.position };
            var print = new MeshWriter { worldOffset = spec.position };
            CardboardBoxGeometry.Build(board, tape, print, Vector3.zero, spec.size, 0f, new System.Random(spec.variant));
            OneRenderer(root, spec, meshes, "Carton", new[] { board, tape, print });
        }

        /// <summary>Builds a pallet's planks.</summary>
        static void BuildPallet(GameObject root, MovableSpec spec, System.Collections.Generic.List<Mesh> meshes)
        {
            var wood = new MeshWriter { worldOffset = spec.position };
            PalletGeometry.Build(wood, Vector3.zero);
            OneRenderer(root, spec, meshes, "Pallet", new[] { wood });
        }

        static readonly System.Collections.Generic.List<CombineInstance> s_combine = new System.Collections.Generic.List<CombineInstance>();
        static readonly System.Collections.Generic.List<Material> s_mats = new System.Collections.Generic.List<Material>();

        /// <summary>One child renderer for writers that each have their own material (spec.materials in order), casting its own shadow.</summary>
        static void OneRenderer(GameObject root, MovableSpec spec, System.Collections.Generic.List<Mesh> meshes, string name, MeshWriter[] writers)
        {
            s_combine.Clear(); s_mats.Clear();
            for (int i = 0; i < writers.Length; i++)
            {
                if (writers[i].VertexCount == 0) continue;
                s_combine.Add(new CombineInstance { mesh = writers[i].ToMesh(name + i, true), transform = Matrix4x4.identity });
                s_mats.Add(spec.materials[Mathf.Min(i, spec.materials.Length - 1)]);
            }
            if (s_combine.Count == 0) return;
            Mesh mesh;
            if (s_combine.Count == 1) mesh = s_combine[0].mesh;
            else
            {
                mesh = new Mesh { name = name };
                mesh.CombineMeshes(s_combine.ToArray(), false, false);
                foreach (var c in s_combine) Destroy(c.mesh);
            }
            mesh.UploadMeshData(true);                  // (no CPU copy kept, as the chunk's meshes)
            meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = s_mats.ToArray();
            r.renderingLayerMask = spec.renderMask;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Starts simulating (and marks the prop as moved, so its pose is saved from now on).</summary>
        public void Wake()
        {
            _still = 0f;
            if (!Body.isKinematic) { Body.WakeUp(); return; }
            MovablePropManager.Instance?.Detach(this);
            Body.isKinematic = false;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
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
            // Fell out of the world (unloaded floor, deep overlap): put it back where it last rested. Falling to a lower storey is fine.
            if (transform.position.y < -4f)                       // below the ground storey: through an unloaded floor
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
            Body.interpolation = RigidbodyInterpolation.None;
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

        static readonly System.Collections.Generic.Dictionary<Mesh, Vector3[]> s_vertices = new System.Collections.Generic.Dictionary<Mesh, Vector3[]>();
        static readonly System.Collections.Generic.Dictionary<Mesh, int[]> s_triangles = new System.Collections.Generic.Dictionary<Mesh, int[]>();
        static readonly System.Collections.Generic.List<Vector3> s_near = new System.Collections.Generic.List<Vector3>();
        const int Grid = 9;
        const float Cell = 0.035f;
        static readonly float[,] s_height = new float[Grid, Grid];

        /// <summary>
        /// Where the hand goes for a grab near <paramref name="grip"/> on a face with normal <paramref name="normal"/> (found
        /// on the collider boxes), worked out on the model itself. The model's surface is sampled in a small grid around the
        /// grip (rays straight down onto its triangles), then every way the fingers could point is tried on it:
        ///  - the palm lies on the highest point under it, so no part of it is inside padding or a rounded arm;
        ///  - fingers hook over an edge where the surface drops away under them, and lie flat where it does not;
        ///  - a rise under the fingers (a sofa's back beyond its seat) counts against that way, and so does pointing the
        ///    fingers back toward the shoulder (the wrist cannot bend like that).
        /// Returns false if the model has no readable mesh there (the caller keeps the collider's grip).
        /// </summary>
        public bool PlaceHand(Vector3 grip, Vector3 normal, Vector3 shoulder, out Vector3 palm, out Vector3 fingers, out bool hook)
        {
            palm = grip; fingers = Vector3.zero; hook = false;
            Vector3 n = normal.normalized;
            // Triangles near the grip, in world space.
            s_near.Clear();
            float reach = Cell * (Grid - 1) * 0.5f * 1.5f + 0.05f;
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (!mesh || !mr || !mr.enabled || mr.shadowCastingMode == ShadowCastingMode.ShadowsOnly || !mesh.isReadable) continue;
                if (!s_vertices.TryGetValue(mesh, out var verts) || verts == null) { verts = mesh.vertices; s_vertices[mesh] = verts; }
                if (!s_triangles.TryGetValue(mesh, out var tris)) { tris = mesh.triangles; s_triangles[mesh] = tris; }
                var m = mf.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(verts[tris[i]]), b = m.MultiplyPoint3x4(verts[tris[i + 1]]), c = m.MultiplyPoint3x4(verts[tris[i + 2]]);
                    Vector3 lo = Vector3.Min(a, Vector3.Min(b, c)), hi = Vector3.Max(a, Vector3.Max(b, c));
                    Vector3 q = Vector3.Max(lo, Vector3.Min(grip, hi));
                    if ((q - grip).sqrMagnitude > reach * reach) continue;
                    s_near.Add(a); s_near.Add(b); s_near.Add(c);
                }
            }
            if (s_near.Count == 0) return false;

            // Heights over the grip's plane (NaN: nothing there, off the model).
            Vector3 T = Vector3.ProjectOnPlane(Mathf.Abs(n.y) < 0.9f ? Vector3.up : transform.forward, n).normalized;
            Vector3 S = Vector3.Cross(n, T);
            const float above = 0.3f;
            int hits = 0;
            for (int i = 0; i < Grid; i++)
            for (int j = 0; j < Grid; j++)
            {
                Vector3 o = grip + S * ((i - (Grid - 1) * 0.5f) * Cell) + T * ((j - (Grid - 1) * 0.5f) * Cell) + n * above;
                float best = float.MaxValue;
                for (int k = 0; k < s_near.Count; k += 3)
                    if (RayTriangle(o, -n, s_near[k], s_near[k + 1], s_near[k + 2], out float t) && t < best) best = t;
                s_height[i, j] = best < above * 2f ? above - best : float.NaN;
                if (best < above * 2f) hits++;
            }
            if (hits == 0) return false;

            // Every way the fingers could point.
            Vector3 toward = Vector3.ProjectOnPlane(grip - shoulder, n);
            if (Mathf.Abs(n.y) < 0.6f) toward += Vector3.ProjectOnPlane(Vector3.up, n) * 0.35f;     // on a side: fingers up a little, as when pushing
            Vector3 prefer = toward.sqrMagnitude > 1e-6f ? toward.normalized : T;
            float bestScore = float.MaxValue; float bestPalm = 0f;
            for (int c = 0; c < 16; c++)
            {
                Vector3 F = Quaternion.AngleAxis(c * 22.5f, n) * T;
                Vector3 side = Vector3.Cross(n, F);
                float palmH = float.MinValue, fingerHi = float.MinValue;
                int fingerHits = 0;
                for (int s = -1; s <= 1; s++)
                {
                    foreach (float along in PalmAlong) palmH = Mathf.Max(palmH, Sample(grip, F * along + side * (s * 0.038f), S, T, n));
                    foreach (float along in FingerAlong)
                    {
                        float h = Sample(grip, F * along + side * (s * 0.034f), S, T, n);
                        if (float.IsNaN(h)) continue;
                        fingerHi = Mathf.Max(fingerHi, h); fingerHits++;
                    }
                }
                if (palmH == float.MinValue || float.IsNaN(palmH)) continue;
                float rise = fingerHits > 0 ? Mathf.Max(0f, fingerHi - palmH) : 0f;
                bool edge = fingerHits == 0 || fingerHi < palmH - 0.03f;
                float score = rise * 3f + Vector3.Angle(F, prefer) / 180f * 0.14f + (edge ? -0.015f : 0f);
                if (score >= bestScore) continue;
                bestScore = score; fingers = F; hook = edge; bestPalm = palmH;
            }
            if (bestScore == float.MaxValue) return false;
            palm = grip + n * (bestPalm + 0.004f);
            return true;
        }

        static readonly float[] PalmAlong = { -0.05f, -0.02f, 0.01f };
        static readonly float[] FingerAlong = { 0.045f, 0.075f, 0.1f };

        /// <summary>The sampled height at a point of the grip's plane (bilinear; NaN off the model).</summary>
        static float Sample(Vector3 grip, Vector3 offset, Vector3 S, Vector3 T, Vector3 n)
        {
            float x = Vector3.Dot(offset, S) / Cell + (Grid - 1) * 0.5f, y = Vector3.Dot(offset, T) / Cell + (Grid - 1) * 0.5f;
            if (x < 0f || y < 0f || x > Grid - 1 || y > Grid - 1) return float.NaN;
            int i = Mathf.Min((int)x, Grid - 2), j = Mathf.Min((int)y, Grid - 2);
            float fx = x - i, fy = y - j;
            float a = s_height[i, j], b = s_height[i + 1, j], c = s_height[i, j + 1], d = s_height[i + 1, j + 1];
            // Off the model at any corner: the nearest sample as it is (an edge is not smoothed into the air).
            if (float.IsNaN(a) || float.IsNaN(b) || float.IsNaN(c) || float.IsNaN(d))
                return fx < 0.5f ? (fy < 0.5f ? a : c) : (fy < 0.5f ? b : d);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Moller-Trumbore, both faces.</summary>
        static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -1e-9f && det < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }

        /// <summary>
        /// Puts a grip found on the collider boxes onto the surface you see: the colliders are boxes round padded, rounded
        /// models, so a box edge can be inside a cushion or hang in the air beside a rolled arm. The model's highest point
        /// (along the surface normal) within a few centimetres of the grip's line is where the palm goes.
        /// </summary>
        public Vector3 OnSurface(Vector3 grip, Vector3 normal)
        {
            float best = float.MinValue;
            for (int pass = 0; pass < 2 && best == float.MinValue; pass++)
            {
                float r = pass == 0 ? 0.045f : 0.09f, r2 = r * r;
                foreach (var mf in GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = mf.sharedMesh;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (!mesh || !mr || !mr.enabled || mr.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                    if (!s_vertices.TryGetValue(mesh, out var verts))
                    {
                        verts = mesh.isReadable ? mesh.vertices : null;
                        s_vertices[mesh] = verts;
                    }
                    if (verts == null) continue;
                    var m = mf.transform.localToWorldMatrix;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 d = m.MultiplyPoint3x4(verts[i]) - grip;
                        float h = Vector3.Dot(d, normal);
                        if (h < -0.15f || h > 0.15f) continue;
                        if ((d - normal * h).sqrMagnitude > r2) continue;
                        if (h > best) best = h;
                    }
                }
            }
            return best == float.MinValue ? grip : grip + normal * best;
        }

        /// <summary>Closest grab spot to a point the player is looking at: on one of the prop's edges, or the
        /// surface itself if no edge is near. A little randomness keeps grabs from looking identical.</summary>
        /// <summary>Its colliders' bounds in the world.</summary>
        public Bounds WorldBounds
        {
            get
            {
                var cs = GetComponentsInChildren<Collider>();
                if (cs.Length == 0) return new Bounds(transform.position, Vector3.zero);
                var b = cs[0].bounds;
                for (int i = 1; i < cs.Length; i++) b.Encapsulate(cs[i].bounds);
                return b;
            }
        }

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
                // Edges facing down, or away from you, only when clearly nearest (the hand goes under or round only then:
                // the undersides are there for a chair lying on its side or upside down).
                Vector3 wn = transform.TransformDirection(e.normal);
                score += 0.3f * Mathf.Max(0f, -wn.y) + 0.2f * Mathf.Max(0f, Vector3.Dot(wn, (c - shoulder).normalized));
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
