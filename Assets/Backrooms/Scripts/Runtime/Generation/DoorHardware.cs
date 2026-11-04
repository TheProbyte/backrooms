using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>
    /// The hardware on some of Level 0's doors (2026-11-03; chosen in OfficeChunkBuilder.BuildDoors, seeded by the doorway):
    ///  - A door closer, regular-arm on the pull side: its body across the top of the leaf by the hinges, the arm out from
    ///    its spindle and the forearm back to a shoe on the frame head, folding and opening as the door swings
    ///    (DoorCloser: the elbow from the two arms' lengths, each frame the leaf has moved).
    ///  - A stainless kick plate, 25 cm high, across the bottom of the push side.
    ///  - A rubber wedge on the floor holding a wide-open door open.
    /// Built on the main thread on the door's model before it is opened (its rest pose is the shut one).
    /// </summary>
    public static class DoorHardware
    {
        static Mesh s_cube, s_wedge;

        static Mesh Cube()
        {
            if (s_cube) return s_cube;
            var w = new MeshWriter();
            w.Box(Vector3.zero, Vector3.one, 63, MeshWriter.MetreUV);
            s_cube = w.ToMesh("Door hardware box");
            return s_cube;
        }

        static Transform Part(Transform parent, string name, Material m, uint mask, Vector3 localPos, Quaternion localRot, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = Cube();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.renderingLayerMask = mask;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        /// <summary>The leaf's extent in its own frame (x across from the hinge, y through, z up), from its renderers.</summary>
        static Bounds LeafBounds(Transform leaf)
        {
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in leaf.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.StartsWith("Handle")) continue;
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = leaf.InverseTransformPoint(new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>
        /// Fits the closer and the kick plate on a shut door's model (root: the door object, whose +z is the side it swings
        /// to). The axis-aligned bounds of a turned leaf are wrong, so this runs before Door.Init opens it.
        /// </summary>
        public static void Fit(Transform root, Transform model, uint mask, bool closer, bool kick)
        {
            var leaf = model ? FindDeep(model, "Leaf") : null;
            if (!leaf || (!closer && !kick)) return;
            var metal = DoorModels.Metal;
            if (!metal) return;
            var lb = LeafBounds(leaf);
            // Which of the leaf's faces is on the swing (pull) side.
            float ny = Mathf.Sign(leaf.InverseTransformDirection(root.forward).y);
            float pullY = ny > 0f ? lb.max.y : lb.min.y, pushY = ny > 0f ? lb.min.y : lb.max.y;
            float hinge = Mathf.Abs(lb.max.x) < Mathf.Abs(lb.min.x) ? lb.max.x : lb.min.x;   // the hinge edge (x nearest 0)
            float free = hinge == lb.max.x ? lb.min.x : lb.max.x, toward = Mathf.Sign(free - hinge);
            float top = lb.max.z, width = Mathf.Abs(free - hinge);
            if (kick && FurnitureMaterials.Stainless)
                Part(leaf, "KickPlate", FurnitureMaterials.Stainless, mask, new Vector3(hinge + toward * width * 0.5f, pushY - ny * 0.001f, lb.min.z + 0.01f + 0.125f),
                     Quaternion.identity, new Vector3(width - 0.05f, 0.002f, 0.25f));
            if (!closer) return;
            // The body by the hinges, across the top rail.
            Part(leaf, "CloserBody", metal, mask, new Vector3(hinge + toward * 0.22f, pullY + ny * 0.03f, top - 0.05f), Quaternion.identity, new Vector3(0.27f, 0.06f, 0.065f));
            Part(leaf, "CloserCap", metal, mask, new Vector3(hinge + toward * 0.08f, pullY + ny * 0.035f, top - 0.05f), Quaternion.identity, new Vector3(0.012f, 0.05f, 0.055f));
            var spindle = new Vector3(hinge + toward * 0.12f, pullY + ny * 0.068f, top - 0.03f);
            // The shoe on the frame head above the leaf, toward the latch: fixed to the door object.
            var shoeW = leaf.TransformPoint(new Vector3(hinge + toward * 0.42f, pullY + ny * 0.02f, top + 0.035f));
            var shoe = root.InverseTransformPoint(shoeW);
            Part(root, "CloserShoe", metal, mask, shoe, Quaternion.LookRotation(root.InverseTransformDirection(leaf.TransformDirection(new Vector3(toward, 0f, 0f)))), new Vector3(0.025f, 0.03f, 0.06f));
            var dc = root.gameObject.AddComponent<DoorCloser>();
            dc.Set(leaf, spindle, shoe, metal, mask);
        }

        /// <summary>A rubber wedge under a wide-open door's free edge (after Door.Init has opened it), on the chunk's root.</summary>
        public static void Wedge(Transform chunkRoot, Transform model, uint mask)
        {
            var leaf = model ? FindDeep(model, "Leaf") : null;
            if (!leaf || !FittingMaterials.Rubber) return;
            var lb = LeafBounds(leaf);
            float hinge = Mathf.Abs(lb.max.x) < Mathf.Abs(lb.min.x) ? lb.max.x : lb.min.x;
            float free = hinge == lb.max.x ? lb.min.x : lb.max.x;
            var foot = leaf.TransformPoint(new Vector3(free - Mathf.Sign(free - hinge) * 0.06f, lb.center.y, lb.min.z));
            foot.y = chunkRoot.position.y + Mathf.Max(0f, foot.y - chunkRoot.position.y - 0.02f);
            var through = leaf.TransformDirection(Vector3.up);                   // across the leaf's thickness
            through.y = 0f;
            if (through.sqrMagnitude < 1e-4f) return;
            if (!s_wedge) s_wedge = WedgeMesh();
            var go = new GameObject("DoorWedge");
            go.transform.SetParent(chunkRoot, true);
            go.transform.position = new Vector3(foot.x, chunkRoot.position.y, foot.z);
            go.transform.rotation = Quaternion.LookRotation(through.normalized, Vector3.up);
            go.AddComponent<MeshFilter>().sharedMesh = s_wedge;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = FittingMaterials.Rubber;
            r.renderingLayerMask = mask;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>A wedge 13 cm long, 4.5 cm wide, rising to 3.5 cm at its back (+z), thin end at -z.</summary>
        static Mesh WedgeMesh()
        {
            var w = new MeshWriter();
            const float L = 0.13f, Wd = 0.0225f, Hh = 0.035f;
            Vector3 a0 = new Vector3(-Wd, 0f, -L * 0.5f), a1 = new Vector3(Wd, 0f, -L * 0.5f), b0 = new Vector3(-Wd, 0f, L * 0.5f), b1 = new Vector3(Wd, 0f, L * 0.5f);
            Vector3 t0 = new Vector3(-Wd, Hh, L * 0.5f), t1 = new Vector3(Wd, Hh, L * 0.5f), tip0 = new Vector3(-Wd, 0.003f, -L * 0.5f), tip1 = new Vector3(Wd, 0.003f, -L * 0.5f);
            var slope = Vector3.Cross(tip1 - tip0, t0 - tip0).normalized;
            if (slope.y < 0f) slope = -slope;
            w.Quad(tip0, tip1, t1, t0, slope, MeshWriter.MetreUV);
            w.Quad(b0, b1, t1, t0, Vector3.forward, MeshWriter.MetreUV);
            w.Quad(a0, b0, t0, tip0, Vector3.left, MeshWriter.MetreUV);
            w.Quad(a1, b1, t1, tip1, Vector3.right, MeshWriter.MetreUV);
            w.Quad(a0, a1, tip1, tip0, Vector3.back, MeshWriter.MetreUV);
            return w.ToMesh("Door wedge");
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
    }

    /// <summary>A closer's two arms, from its spindle on the leaf to its shoe on the frame, kept joined as the door swings.</summary>
    public class DoorCloser : MonoBehaviour
    {
        Transform _leaf, _arm, _fore;
        Vector3 _spindle, _shoe;              // leaf-local; door-local
        float _a, _b;
        Quaternion _last;

        public void Set(Transform leaf, Vector3 spindleLeaf, Vector3 shoeRoot, Material m, uint mask)
        {
            _leaf = leaf; _spindle = spindleLeaf; _shoe = shoeRoot;
            // The arm straight out from the leaf, shut; the forearm whatever reaches back to the shoe from its end.
            var s = transform.InverseTransformPoint(leaf.TransformPoint(spindleLeaf));
            _a = 0.21f;
            var elbow = s + transform.InverseTransformDirection(leaf.TransformDirection(Vector3.up)).normalized * Mathf.Sign(transform.InverseTransformDirection(leaf.TransformDirection(Vector3.up)).z) * _a;
            elbow.y = s.y;
            var shoeFlat = new Vector3(_shoe.x, s.y, _shoe.z);
            _b = (shoeFlat - elbow).magnitude;
            _arm = MakeArm("CloserArm", m, mask);
            _fore = MakeArm("CloserForearm", m, mask);
            _last = new Quaternion(0f, 0f, 0f, 0f);
            Pose();
        }

        Transform MakeArm(string name, Material m, uint mask)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.renderingLayerMask = mask;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        void LateUpdate()
        {
            if (!_leaf) { enabled = false; return; }
            if (_leaf.localRotation == _last) return;
            Pose();
        }

        void Pose()
        {
            _last = _leaf.localRotation;
            var s = transform.InverseTransformPoint(_leaf.TransformPoint(_spindle));
            var f = new Vector3(_shoe.x, s.y, _shoe.z);
            // The elbow: where circles of the two arms' lengths round the spindle and the shoe meet, out on the swing side.
            var d = f - s;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(_a - _b) + 1e-4f, _a + _b - 1e-4f);
            var dir = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.right;
            float along = (_a * _a - _b * _b + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, _a * _a - along * along));
            var perp = new Vector3(-dir.z, 0f, dir.x);
            var e1 = s + dir * along + perp * h;
            var e2 = s + dir * along - perp * h;
            var e = e1.z >= e2.z ? e1 : e2;
            Place(_arm, s, e, 0.024f, 0.012f);
            Place(_fore, e + Vector3.up * 0.006f, f + Vector3.up * 0.006f, 0.018f, 0.01f);
        }

        static void Place(Transform t, Vector3 a, Vector3 b, float w, float th)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            t.localPosition = (a + b) * 0.5f;
            t.localRotation = Quaternion.LookRotation(d / len, Vector3.up);
            t.localScale = new Vector3(w, th, len + w);
        }
    }
}
