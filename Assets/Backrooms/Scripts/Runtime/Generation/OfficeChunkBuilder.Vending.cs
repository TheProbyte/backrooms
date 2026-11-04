using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// A drinks vending machine (PropKind.VendingMachine, 2026-11-04; placed by SpecialRooms.TryVending): the modelled
    /// machine (Tools/Blender/build_vending.py) backed against its wall, solid, its power cord out of the back along the
    /// foot of the wall and up into the outlet put in beside it (or its plug lying on the floor, unplugged). Its glow,
    /// light, hum and buttons are Interaction.VendingMachine's, made on the main thread with the chunk.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Vending machines to make the working parts of: chunk-local foot, rotation, variant, room colour.</summary>
        readonly List<(Vector3 at, Quaternion rot, int variant, int color)> _vendings = new List<(Vector3, Quaternion, int, int)>();

        void Vending(Prop p, Vector3 at, Quaternion rot)
        {
            uint m = LayerBit(p.roomColor);
            if (!Model("VendingMachine", m, at, rot))
                W(FurnitureMaterials.Dark, p.roomColor).Box(at + Vector3.up * (p.size.y * 0.5f), p.size, rot, 63, MeshWriter.MetreUV);
            Solid(at, rot, p.yaw, new Vector3(0f, p.size.y * 0.5f, 0f), p.size);
            VendingCord(p, at, rot, m);
            _vendings.Add((at, rot, p.variant, p.roomColor));
            var front = rot * Vector3.forward;
            Saw(at + Vector3.up * 1.2f + front * (p.size.z * 0.5f), front, "vending");
        }

        /// <summary>A stop sign at an aisle crossing (PropKind.StopSign): the modelled sign on its post in a rubber base, solid.</summary>
        void StopSign(Prop p, Vector3 at, Quaternion rot)
        {
            uint m = LayerBit(p.roomColor);
            if (!Model("StopSign", m, at, rot)) return;
            Solid(at, rot, p.yaw, new Vector3(0f, 0.04f, 0f), new Vector3(0.5f, 0.08f, 0.5f));
            Solid(at, rot, p.yaw, new Vector3(0f, 1.0f, 0f), new Vector3(0.05f, 2.0f, 0.05f));
            Solid(at, rot, p.yaw, new Vector3(0f, 1.72f, 0.023f), new Vector3(0.61f, 0.61f, 0.01f));
            Saw(at + Vector3.up * 1.72f, rot * Vector3.forward, "stop_sign");
        }

        /// <summary>The face of a wall plate (as BuildPlates puts it): its middle on the plate's front, and out of the wall.</summary>
        bool PlateFace(WallPlate p, out Vector3 face, out Vector3 n)
        {
            float x0 = p.cell.x * C, z0 = p.cell.y * C;
            float py = p.height + _L.FloorAt(p.cell.x, p.cell.y);
            Vector3 pos;
            switch (p.side)
            {
                case 0: n = Vector3.right; pos = new Vector3(x0 + T * 0.5f, py, z0 + p.t * C); break;
                case 1: n = Vector3.left; pos = new Vector3(x0 + C - T * 0.5f, py, z0 + p.t * C); break;
                case 2: n = Vector3.forward; pos = new Vector3(x0 + p.t * C, py, z0 + T * 0.5f); break;
                default: n = Vector3.back; pos = new Vector3(x0 + p.t * C, py, z0 + C - T * 0.5f); break;
            }
            face = pos + n * 0.006f;
            return true;
        }

        /// <summary>
        /// The cord: out of the machine's back by the outlet's side, down to the floor, along the foot of the wall and up into
        /// the outlet's lower socket (its moulded plug, VendingPlug, pins in); unplugged (variant bit 1), the plug lies on the
        /// floor a little short of the outlet.
        /// </summary>
        void VendingCord(Prop p, Vector3 at, Quaternion rot, uint m)
        {
            // Its outlet: the plate at 0.3 m on the same wall nearest the machine.
            WallPlate best = default; float bd = float.MaxValue; bool found = false;
            foreach (var wp in _L.plates)
            {
                if (wp.isSwitch || Mathf.Abs(wp.height - 0.3f) > 0.01f) continue;
                PlateFace(wp, out var f, out _);
                float d = (new Vector2(f.x, f.z) - new Vector2(at.x, at.z)).sqrMagnitude;
                if (d < bd) { bd = d; best = wp; found = true; }
            }
            if (!found || bd > 1.6f * 1.6f) return;
            PlateFace(best, out var face, out var n);
            var mat = FittingMaterials.Rubber ? FittingMaterials.Rubber : FurnitureMaterials.Dark;
            if (!mat) return;
            var w = WMask(mat, m);
            var inv = Quaternion.Inverse(rot);
            float side = Mathf.Sign((inv * (face - at)).x);
            if (side == 0f) side = 1f;
            float floor = at.y + 0.006f;
            bool unplugged = (p.variant & 2) != 0;
            var socket = face + Vector3.down * 0.019f;
            var along = Vector3.Cross(Vector3.up, n).normalized;
            if (Vector3.Dot(along, socket - at) < 0f) along = -along;
            var pts = new List<Vector3>
            {
                at + rot * new Vector3(side * 0.4f, 0.12f, -0.42f),
                at + rot * new Vector3(side * 0.43f, 0.05f, -0.44f),
                at + rot * new Vector3(side * 0.47f, 0.0f, -0.445f) + Vector3.up * (floor - at.y),
            };
            Vector3 plugAt; Quaternion plugRot;
            if (!unplugged)
            {
                // Along the foot of the wall to below the socket, up to the plug's strain relief.
                var below = socket + n * 0.03f; below.y = floor;
                pts.Add(below - along * 0.06f);
                pts.Add(below + Vector3.up * 0.02f);
                pts.Add(socket + n * 0.075f + Vector3.down * 0.06f);
                pts.Add(socket + n * 0.084f);
                plugAt = socket + n * 0.05f;
                plugRot = Quaternion.FromToRotation(Vector3.up, -n);
            }
            else
            {
                // Pulled out and dropped: the plug on its side on the floor, a little short of the outlet.
                var rest = socket + n * 0.3f - along * 0.15f; rest.y = floor + 0.018f;
                pts.Add(rest + n * 0.05f - along * 0.1f + Vector3.down * 0.012f);
                pts.Add(rest + along * 0.02f + Vector3.down * 0.012f);
                plugAt = rest + along * 0.05f;
                plugRot = Quaternion.FromToRotation(Vector3.up, along);
            }
            // A smooth cord through the points (Catmull-Rom), 7 mm across.
            var path = new List<Vector3>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector3 a = pts[Mathf.Max(i - 1, 0)], b = pts[i], c = pts[i + 1], d = pts[Mathf.Min(i + 2, pts.Count - 1)];
                int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(b, c) / 0.06f), 2, 24);
                for (int k = 0; k < steps; k++)
                {
                    float t = k / (float)steps, t2 = t * t, t3 = t2 * t;
                    path.Add(0.5f * (2f * b + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (-a + 3f * b - 3f * c + d) * t3));
                }
            }
            path.Add(pts[pts.Count - 1]);
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var a = path[i]; var b = path[i + 1];
                var dir = (b - a).normalized;
                Tube(w, a - dir * 0.002f, b + dir * 0.002f, 0.0035f, 6);
            }
            Model("VendingPlug", m, plugAt, plugRot);
        }
    }
}
