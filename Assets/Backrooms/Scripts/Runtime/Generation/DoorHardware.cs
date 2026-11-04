using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The hardware on some of Level 0's doors (2026-11-03; chosen in OfficeChunkBuilder.BuildDoors, seeded by the doorway;
    /// the models are FittingModels'):
    ///  - A door closer, regular-arm on the pull side: its body across the top rail by the hinges, its spindle up, the arm
    ///    out from the spindle and the forearm back to a shoe on the frame head, folding and opening as the door swings
    ///    (DoorCloser: the elbow from the two arms' lengths, each frame the leaf has moved).
    ///  - A brushed stainless kick plate, 25 cm high, across the bottom of the push side, screwed on.
    ///  - A ribbed rubber wedge on the floor holding a wide-open door open.
    /// Built on the main thread on the door's model before it is opened (its rest pose is the shut one).
    /// </summary>
    public static class DoorHardware
    {
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
            if (!leaf || (!closer && !kick) || !FittingModels.Ready) return;
            var lb = LeafBounds(leaf);
            // Which of the leaf's faces is on the swing (pull) side; the leaf's frame has z up.
            float ny = Mathf.Sign(leaf.InverseTransformDirection(root.forward).y);
            float pullY = ny > 0f ? lb.max.y : lb.min.y, pushY = ny > 0f ? lb.min.y : lb.max.y;
            float hinge = Mathf.Abs(lb.max.x) < Mathf.Abs(lb.min.x) ? lb.max.x : lb.min.x;   // the hinge edge (x nearest 0)
            float free = hinge == lb.max.x ? lb.min.x : lb.max.x, toward = Mathf.Sign(free - hinge);
            float top = lb.max.z, width = Mathf.Abs(free - hinge);
            var leafUp = Vector3.forward;                                                     // (leaf-local z)
            if (kick)
            {
                var q = Quaternion.LookRotation(new Vector3(0f, -ny, 0f), leafUp);
                FittingModels.Spawn("KickPlate", leaf, mask, new Vector3(hinge + toward * width * 0.5f, pushY, lb.min.z + 0.01f + 0.125f), q,
                                    new Vector3((width - 0.05f) / 0.907f, 1f, 1f));
            }
            if (!closer) return;
            // The body by the hinges, upside down on the top rail (its spindle up), its +X toward the latch.
            var bq = Quaternion.LookRotation(new Vector3(0f, ny, 0f), -leafUp);
            float sx = Mathf.Sign(Vector3.Dot(bq * Vector3.right, new Vector3(toward, 0f, 0f)));
            FittingModels.Spawn("CloserBody", leaf, mask, new Vector3(hinge + toward * 0.22f, pullY, top - 0.045f), bq, new Vector3(sx, 1f, 1f));
            var spindle = new Vector3(hinge + toward * 0.12f, pullY + ny * 0.03f, top + 0.004f);
            // The shoe on the frame head, toward the latch: fixed to the door object, facing the swing side.
            var shoe = root.InverseTransformPoint(leaf.TransformPoint(new Vector3(hinge + toward * 0.42f, pullY + ny * 0.012f, top + 0.004f)));
            FittingModels.Spawn("CloserShoe", root, mask, shoe, Quaternion.identity);
            root.gameObject.AddComponent<DoorCloser>().Set(leaf, spindle, shoe, mask);
        }

        /// <summary>A rubber wedge under a wide-open door's free edge (after Door.Init has opened it), on the chunk's root.</summary>
        public static void Wedge(Transform chunkRoot, Transform model, uint mask)
        {
            var leaf = model ? FindDeep(model, "Leaf") : null;
            if (!leaf || !FittingModels.Ready) return;
            var lb = LeafBounds(leaf);
            float hinge = Mathf.Abs(lb.max.x) < Mathf.Abs(lb.min.x) ? lb.max.x : lb.min.x;
            float free = hinge == lb.max.x ? lb.min.x : lb.max.x;
            var foot = leaf.TransformPoint(new Vector3(free - Mathf.Sign(free - hinge) * 0.06f, lb.center.y, lb.min.z));
            var through = leaf.TransformDirection(Vector3.up);                   // across the leaf's thickness
            through.y = 0f;
            if (through.sqrMagnitude < 1e-4f) return;
            var at = chunkRoot.InverseTransformPoint(new Vector3(foot.x, chunkRoot.position.y, foot.z));
            at.y = 0f;
            var rot = Quaternion.Inverse(chunkRoot.rotation) * Quaternion.LookRotation(through.normalized, Vector3.up);
            var w = FittingModels.Spawn("DoorWedge", chunkRoot, mask, at, rot);
            if (w) w.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
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

        public void Set(Transform leaf, Vector3 spindleLeaf, Vector3 shoeRoot, uint mask)
        {
            _leaf = leaf; _spindle = spindleLeaf; _shoe = shoeRoot;
            // The arm straight out from the leaf, shut; the forearm whatever reaches back to the shoe from its end.
            var s = transform.InverseTransformPoint(leaf.TransformPoint(spindleLeaf));
            _a = 0.21f;
            var elbow = s + Vector3.forward * _a;                     // (the door object's +z: the swing side)
            var shoeFlat = new Vector3(_shoe.x, s.y, _shoe.z);
            _b = (shoeFlat - elbow).magnitude;
            _arm = FittingModels.Spawn("CloserArm", transform, mask, s, Quaternion.identity);
            _fore = FittingModels.Spawn("CloserForearm", transform, mask, elbow, Quaternion.identity);
            if (!_arm || !_fore) { enabled = false; return; }
            _last = new Quaternion(0f, 0f, 0f, 0f);
            Pose();
        }

        void LateUpdate()
        {
            if (!_leaf || !_arm) { enabled = false; return; }
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
            _arm.localPosition = s;
            _arm.localRotation = Quaternion.LookRotation((e - s).normalized, Vector3.up);
            var fe = f - e;
            _fore.localPosition = e + Vector3.up * 0.007f;
            _fore.localRotation = Quaternion.LookRotation(fe.normalized, Vector3.up);
            _fore.localScale = new Vector3(1f, 1f, fe.magnitude);
        }
    }
}
