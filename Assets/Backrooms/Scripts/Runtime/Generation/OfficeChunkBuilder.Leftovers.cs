using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// What the office left behind (2026-11-03), rare, seeded like the wear:
    ///  - Floor boxes where desks stood: brass covers flush with the carpet (round, or square with carpet let into the lid)
    ///    in the open middle of some offices; in some the lid is up and cut cables stick out.
    ///  - An orange extension cord run down the length of a hallway, taped to the carpet with duct tape every metre or two,
    ///    from an outlet (or from nothing) to a loose socket end.
    ///  - Cubicle partitions (PropKind.Partition, placed by DistrictGenerator.PlaceLeftovers): fabric panels on steel feet,
    ///    a run, an L or a T of them left standing in an empty office, now and then one fallen flat.
    ///  - A wooden chair (PropKind.WoodenChair) on its own in the middle of a room: movable.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>The cords' plug and socket (Resources/Items models, made on the main thread): chunk-local place, turn, model, light layers.</summary>
        readonly List<(Vector3 at, Quaternion rot, string model, uint mask)> _cordEnds = new List<(Vector3, Quaternion, string, uint)>();

        void InstantiateCordEnds(Transform root)
        {
            foreach (var (at, rot, model, mask) in _cordEnds)
            {
                var go = new GameObject(model);
                go.transform.SetParent(root, false);
                go.transform.localPosition = at;
                go.transform.localRotation = rot;
                Interaction.ItemModels.Attach(go.transform, model, new Vector3(0.04f, 0.03f, 0.05f), "CordOrange");
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    r.renderingLayerMask = mask;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        // ---------------- Floor boxes ----------------

        void FloorBox(int x, int z, Room r, bool carpet, uint m)
        {
            if (!carpet || r.kind == RoomKind.Hallway || r.kind == RoomKind.Crawl || r.custom || r.home || !FittingModels.Ready) return;
            if (DistrictGenerator.StyleOf(r) != RoomStyle.Backrooms) return;
            // A room has them or not (the same in every chunk it runs through), then only away from its walls.
            if (Dice(r.globalId, _L.storey, 3200) >= 0.1f * DetailRates.FloorMarks) return;
            for (int s = 0; s < 4; s++) if (_L.EdgeOf(x, z, s) != EdgeKind.Open) return;
            int gx = GX(x), gz = GZ(z);
            if (Dice(gx, gz, 3201) >= 0.22f) return;
            var c = new Vector3((x + 0.5f + (Dice(gx, gz, 3202) - 0.5f) * 0.3f) * C, 0f, (z + 0.5f + (Dice(gx, gz, 3203) - 0.5f) * 0.3f) * C);
            if (!Clear(c, 0.15f)) return;
            bool round = Dice(gx, gz, 3204) < 0.6f, open = Dice(gx, gz, 3205) < 0.35f;
            float turn = Dice(gx, gz, 3206) * 360f;
            var q = Quaternion.Euler(0f, round ? turn : (Dice(gx, gz, 3206) < 0.5f ? 0f : 90f), 0f);
            var floor = _a.FloorMaterial(r);
            if (!Model(open ? "FloorBoxOpen" : round ? "FloorBoxRound" : "FloorBoxSquare", m, c, q, sl => sl == "CarpetInsert" ? floor : null)) return;
            // The carpet round it trodden darker, its pile worn back from the brass.
            _floorWear.Rect(c + Vector3.up * 0.0015f, Vector3.right, Vector3.forward, 0.16f, 0.16f, new Color32(170, 160, 142, 120), new Vector2(-0.55f, 0.4f));
            Saw(c, Vector3.up, open ? "cable_stubs" : "floor_box");
        }

        // ---------------- Extension cord ----------------

        /// <summary>Now and then a hallway (its part in this chunk) has an extension cord taped down along it.</summary>
        void Cords()
        {
            if (!FittingMaterials.Cord || !FittingMaterials.Tape) return;
            foreach (var r in _L.rooms)
            {
                if (r.kind != RoomKind.Hallway || r.custom || r.noFloor || r.landmark != null) continue;
                if (DistrictGenerator.StyleOf(r) != RoomStyle.Backrooms) continue;
                var rc = r.rect;
                bool alongX = rc.width >= rc.height;
                int len = alongX ? rc.width : rc.height;
                if (len < 4) continue;
                int gx = GX(rc.xMin), gz = GZ(rc.yMin);
                if (Dice(gx, gz, 3220 + r.id) >= 0.02f * DetailRates.FloorMarks) continue;
                Cord(r, alongX, gx * 977 + gz * 131 + r.id);
            }
        }

        void Cord(Room r, bool alongX, int seed)
        {
            var rng = new System.Random(seed);
            var rc = r.rect;
            Vector3 axis = alongX ? Vector3.right : Vector3.forward, across = alongX ? Vector3.forward : Vector3.right;
            float s0 = (alongX ? rc.xMin : rc.yMin) * C + 0.35f, s1 = (alongX ? rc.xMax : rc.yMax) * C - 0.35f;
            float w0 = (alongX ? rc.yMin : rc.xMin) * C + T * 0.5f + 0.2f, w1 = (alongX ? rc.yMax : rc.xMax) * C - T * 0.5f - 0.2f;
            if (w1 <= w0) return;
            float off = Mathf.Lerp(w0, w1, rng.NextDouble() < 0.5 ? 0.15f : 0.85f);
            bool reverse = rng.NextDouble() < 0.5;
            // The path: along the hallway near one wall, wandering a little, stopped by anything in the way.
            var pts = new List<Vector3>();
            // From an outlet in this hallway, if it has one: down the wall from the plug, out across to the run.
            Vector3? plug = null, plugN = null;
            foreach (var p in _L.plates)
            {
                if (p.isSwitch || _L.roomOf[p.cell.x, p.cell.y] != r.id) continue;
                float x0 = p.cell.x * C, z0 = p.cell.y * C;
                Vector3 n = p.side == 0 ? Vector3.right : p.side == 1 ? Vector3.left : p.side == 2 ? Vector3.forward : Vector3.back;
                Vector3 pos = p.side == 0 ? new Vector3(x0 + T * 0.5f, p.height, z0 + p.t * C) : p.side == 1 ? new Vector3(x0 + C - T * 0.5f, p.height, z0 + p.t * C)
                            : p.side == 2 ? new Vector3(x0 + p.t * C, p.height, z0 + T * 0.5f) : new Vector3(x0 + p.t * C, p.height, z0 + C - T * 0.5f);
                if (Mathf.Abs(Vector3.Dot(n, axis)) > 0.5f) continue;                  // an end wall: not along the run
                plug = pos; plugN = n;
                float sp = Vector3.Dot(pos, axis);
                reverse = sp > (s0 + s1) * 0.5f;
                off = Mathf.Clamp(Vector3.Dot(pos, across) + Vector3.Dot(n, across) * 0.25f, w0, w1);
                break;
            }
            float start = reverse ? s1 : s0, end = reverse ? s0 : s1;
            if (plug.HasValue) start = Mathf.Clamp(Vector3.Dot(plug.Value, axis), Mathf.Min(s0, s1), Mathf.Max(s0, s1));
            float total = Mathf.Abs(end - start);
            if (total < 2.5f) return;
            float stopAt = total * (0.55f + 0.45f * (float)rng.NextDouble());
            const float y = 0.0045f;
            float ph = (float)rng.NextDouble() * 6.28f;
            for (float d = 0f; d <= stopAt; d += 0.15f)
            {
                float s = start + (reverse ? -d : d);
                float wob = Mathf.Sin(d * 1.7f + ph) * 0.05f + Mathf.Sin(d * 4.1f + ph * 2f) * 0.015f;
                var p = axis * s + across * (off + wob) + Vector3.up * y;
                if (!Clear(p, 0.06f)) break;
                pts.Add(p);
            }
            if (pts.Count < 8) return;
            uint m = CellMask(Mathf.Clamp(Mathf.FloorToInt(pts[0].x / C), 0, _L.N - 1), Mathf.Clamp(Mathf.FloorToInt(pts[0].z / C), 0, _L.N - 1));
            var cord = WMask(FittingMaterials.Cord, m);
            if (plug.HasValue)
            {
                // The plug in the outlet (the plug-in cords' own model: prongs into the wall, its cord out of the back),
                // the cord curving down the wall to the floor and out to the run.
                var pf = plug.Value + plugN.Value * 0.011f + Vector3.down * 0.012f;
                _cordEnds.Add((pf, Quaternion.LookRotation(-plugN.Value, Vector3.up), "Plug", m));
                var back = pf + plugN.Value * 0.046f;
                var foot = new Vector3(back.x, y, back.z) + plugN.Value * 0.02f;
                var bend = new List<Vector3>();
                for (int k = 0; k <= 8; k++)
                {
                    float t = k / 8f;
                    var a = Vector3.Lerp(back, new Vector3(back.x, y + 0.03f, back.z) + plugN.Value * 0.02f, t);
                    bend.Add(Vector3.Lerp(a, foot, t * t) + plugN.Value * (0.012f * Mathf.Sin(t * Mathf.PI)));
                }
                for (int k = 0; k + 1 < bend.Count; k++) Tube(cord, bend[k], bend[k + 1], 0.0045f);
                pts.Insert(0, foot);
            }
            for (int i = 0; i + 1 < pts.Count; i++) Tube(cord, pts[i], pts[i + 1], 0.0045f);
            // The loose end: the orange socket (the cords' own model) lying on the floor, its cord out of its back.
            var tip = pts[pts.Count - 1];
            var dirEnd = (tip - pts[pts.Count - 2]).normalized;
            Tube(cord, tip, tip + dirEnd * 0.006f + Vector3.up * 0.009f, 0.0045f);
            _cordEnds.Add((new Vector3(tip.x, 0f, tip.z) + dirEnd * 0.066f, Quaternion.LookRotation(dirEnd, Vector3.up), "CordSocket", m));
            // Duct tape across it every metre or two.
            var tape = WMask(FittingMaterials.Tape, m);
            float next = 0.6f + 0.6f * (float)rng.NextDouble(), run = 0f;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                run += (pts[i] - pts[i - 1]).magnitude;
                if (run < next) continue;
                run = 0f; next = 1f + 0.8f * (float)rng.NextDouble();
                var dir = (pts[i + 1] - pts[i - 1]).normalized;
                var side = Vector3.Cross(Vector3.up, dir);
                float tw = 0.024f, tl = 0.09f + 0.05f * (float)rng.NextDouble(), skew = ((float)rng.NextDouble() - 0.5f) * 20f;
                var q = Quaternion.LookRotation(Quaternion.AngleAxis(skew, Vector3.up) * side);
                var tc = new Vector3(pts[i].x, 0f, pts[i].z);
                TapeStrip(tape, tc, q * Vector3.forward, q * Vector3.right, tl, tw, rng);
            }
            Saw(pts[pts.Count / 2], Vector3.up, "cord");
        }

        /// <summary>
        /// A strip of duct tape across a cord on the floor: flat to the carpet either side, humped over the cord in the middle,
        /// its torn ends ragged. c on the floor; along: across the cord (its length); side: along the cord (its width).
        /// </summary>
        static void TapeStrip(MeshWriter w, Vector3 c, Vector3 along, Vector3 side, float halfLen, float halfW, System.Random rng)
        {
            const int n = 10;
            float Hump(float s) { float a = Mathf.Abs(s); return a < 0.006f ? 0.0098f : a < 0.016f ? Mathf.Lerp(0.0098f, 0.0012f, (a - 0.006f) / 0.01f) : 0.0012f; }
            for (int i = 0; i < n; i++)
            {
                float s0 = -halfLen + 2f * halfLen * i / n, s1 = -halfLen + 2f * halfLen * (i + 1) / n;
                // The torn ends: the last piece each way jagged across.
                float j0 = i == 0 ? ((float)rng.NextDouble() - 0.5f) * 0.012f : 0f, j1 = i == n - 1 ? ((float)rng.NextDouble() - 0.5f) * 0.012f : 0f;
                Vector3 P(float s, float t, float jag) => c + along * (s + jag * t) + side * (t * halfW) + Vector3.up * Hump(s);
                Vector3 a = P(s0, -1f, j0), b = P(s1, -1f, j1), d = P(s0, 1f, -j0), e = P(s1, 1f, -j1);
                var nrm = Vector3.Cross(e - a, b - a).normalized;
                if (nrm.y < 0f) nrm = -nrm;
                w.Quad(a, b, e, d, nrm, MeshWriter.MetreUV);
            }
        }

        /// <summary>A cord's piece from a to b: an eight-sided tube of radius rad.</summary>
        static void Tube(MeshWriter w, Vector3 a, Vector3 b, float rad)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            var q = Quaternion.LookRotation(d / len, Mathf.Abs(d.y) / len > 0.9f ? Vector3.forward : Vector3.up);
            const int sides = 8;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var n0 = q * new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
                var n1 = q * new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                w.SmoothQuad(a + n0 * rad, a + n1 * rad, b + n1 * rad, b + n0 * rad, n0, n1, n1, n0,
                    new Vector2(i / (float)sides, 0f), new Vector2((i + 1) / (float)sides, 0f), new Vector2((i + 1) / (float)sides, len), new Vector2(i / (float)sides, len));
            }
        }

        // ---------------- Partitions ----------------

        /// <summary>
        /// A cubicle panel (PropKind.Partition): position the middle of its foot, yaw its face's normal (local Z), size width x
        /// height x thickness; variant bit 1: fallen flat on its face. Fabric faces in a steel frame on two feet.
        /// </summary>
        void Partition(Prop p, Vector3 at, Quaternion rot)
        {
            if (!FittingModels.Ready) return;
            uint m = LayerBit(p.roomColor);
            float w = p.size.x, h = p.size.y, t = p.size.z;
            bool fallen = (p.variant & 1) != 0;
            if ((p.variant & 2) != 0) Saw(at + Vector3.up * 0.6f, Vector3.up, "partitions");
            string name = "Partition_" + (w > 1f ? "120" : "90") + "_" + (h > 1.5f ? "165" : "135");
            if (!fallen)
            {
                Model(name, m, at, rot);
                _rotBoxes.Add((at + Vector3.up * (h * 0.5f), new Vector3(w, h, t + 0.02f), p.yaw));
            }
            else
            {
                // Fallen on its face about its foot's long edge: its middle (where the plan put it) half its height out.
                var foot = at - rot * Vector3.forward * (h * 0.5f) + Vector3.up * 0.032f;
                Model(name, m, foot, rot * Quaternion.Euler(90f, 0f, 0f));
                _rotBoxes.Add((at + Vector3.up * 0.04f, new Vector3(w, 0.08f, h), p.yaw));
            }
        }

        // ---------------- The wooden chair ----------------

        /// <summary>A wooden chair (PropKind.WoodenChair): the modelled one (movable), or a plain box chair if the model is missing.</summary>
        void WoodenChair(Prop p, Vector3 at)
        {
            Saw(at + Vector3.up * 0.45f, Vector3.up, "wooden_chair");
            if (_a.woodenChairPrefab)
            {
                Movable(p.id, Interaction.MovableKind.WoodenChair, at, p.yaw, p.roomColor).AddPrefab(_a.woodenChairPrefab, Vector3.zero);
                return;
            }
            var w = W(_a.wood ? _a.wood : FurnitureMaterials.Beige, p.roomColor);
            var q = Quaternion.Euler(0f, p.yaw, 0f);
            Vector3 L(float x, float y, float z) => at + q * new Vector3(x, y, z);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                w.Box(L(sx * 0.19f, 0.22f, sz * 0.19f), new Vector3(0.035f, 0.44f, 0.035f), q, 63, MU);
            w.Box(L(0f, 0.455f, 0f), new Vector3(0.44f, 0.03f, 0.44f), q, 63, MU);
            for (int sx = -1; sx <= 1; sx += 2) w.Box(L(sx * 0.19f, 0.7f, -0.2f), new Vector3(0.035f, 0.5f, 0.035f), q, 63, MU);
            w.Box(L(0f, 0.86f, -0.2f), new Vector3(0.42f, 0.08f, 0.025f), q, 63, MU);
            _rotBoxes.Add((at + Vector3.up * 0.45f, new Vector3(0.44f, 0.9f, 0.44f), p.yaw));
        }
    }
}
