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
        // ---------------- Floor boxes ----------------

        void FloorBox(int x, int z, Room r, bool carpet, uint m)
        {
            if (!carpet || r.kind == RoomKind.Hallway || r.kind == RoomKind.Crawl || r.custom || r.home || !FittingMaterials.Brass) return;
            if (DistrictGenerator.StyleOf(r) != RoomStyle.Backrooms) return;
            // A room has them or not (the same in every chunk it runs through), then only away from its walls.
            if (Dice(r.globalId, _L.storey, 3200) >= 0.1f * DetailRates.FloorMarks) return;
            for (int s = 0; s < 4; s++) if (_L.EdgeOf(x, z, s) != EdgeKind.Open) return;
            int gx = GX(x), gz = GZ(z);
            if (Dice(gx, gz, 3201) >= 0.22f) return;
            var c = new Vector3((x + 0.5f + (Dice(gx, gz, 3202) - 0.5f) * 0.3f) * C, 0f, (z + 0.5f + (Dice(gx, gz, 3203) - 0.5f) * 0.3f) * C);
            if (!Clear(c, 0.15f)) return;
            var brass = WMask(FittingMaterials.Brass, m);
            bool round = Dice(gx, gz, 3204) < 0.6f, open = Dice(gx, gz, 3205) < 0.35f;
            float turn = Dice(gx, gz, 3206) < 0.5f ? 0f : 90f;
            var q = Quaternion.Euler(0f, turn, 0f);
            const float lip = 0.0045f;
            if (round)
            {
                brass.Frustum(c, 0.105f, 0.1f, lip, 24, true, MU);
                if (!open) brass.Disc(c + Vector3.up * lip, 0.1f, 24, Vector3.up, MU);
                else brass.Frustum(c + Vector3.up * lip, 0.1f, 0.085f, 0.0005f, 24, true, MU);
            }
            else
            {
                const float hs = 0.11f, ins = 0.085f;
                brass.Box(c + q * new Vector3(0f, lip * 0.5f, hs - (hs - ins) * 0.5f), new Vector3(hs * 2f, lip, hs - ins), q, 63 & ~4, MU);
                brass.Box(c + q * new Vector3(0f, lip * 0.5f, -hs + (hs - ins) * 0.5f), new Vector3(hs * 2f, lip, hs - ins), q, 63 & ~4, MU);
                brass.Box(c + q * new Vector3(hs - (hs - ins) * 0.5f, lip * 0.5f, 0f), new Vector3(hs - ins, lip, ins * 2f), q, 63 & ~4, MU);
                brass.Box(c + q * new Vector3(-hs + (hs - ins) * 0.5f, lip * 0.5f, 0f), new Vector3(hs - ins, lip, ins * 2f), q, 63 & ~4, MU);
                if (!open)
                {
                    var insert = WMask(_a.FloorMaterial(r), m);
                    var ic = c + Vector3.up * (lip - 0.0005f);
                    insert.Quad(ic + q * new Vector3(-ins, 0f, -ins), ic + q * new Vector3(ins, 0f, -ins), ic + q * new Vector3(ins, 0f, ins), ic + q * new Vector3(-ins, 0f, ins), Vector3.up, MeshWriter.FloorUV);
                }
            }
            if (open)
            {
                // The lid gone, the box dark inside, the cut ends of the cables that fed a desk.
                if (MaterialMaps.Plenum) WMask(MaterialMaps.Plenum, m).Disc(c + Vector3.up * 0.0006f, 0.084f, 18, Vector3.up, MU);
                var rng = new System.Random(gx * 4561 + gz * 1931);
                int n = 2 + rng.Next(3);
                for (int k = 0; k < n; k++)
                {
                    var cable = WMask(k % 2 == 0 ? FurnitureMaterials.Dark : FurnitureMaterials.Beige, m);
                    float ang = (float)rng.NextDouble() * 6.28f, off = 0.02f + 0.04f * (float)rng.NextDouble();
                    var b = c + new Vector3(Mathf.Cos(ang) * off, 0f, Mathf.Sin(ang) * off);
                    float tall = 0.03f + 0.07f * (float)rng.NextDouble(), lean = 20f + 50f * (float)rng.NextDouble();
                    var dir = Quaternion.AngleAxis(lean, new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang))) * Vector3.up;
                    var mid = b + Vector3.up * (tall * 0.5f);
                    cable.Box(mid, new Vector3(0.008f, tall, 0.008f), Quaternion.identity, 63 & ~4, MU);
                    var tip0 = b + Vector3.up * tall;
                    cable.Box(tip0 + dir * 0.025f, new Vector3(0.008f, 0.008f, 0.05f), Quaternion.LookRotation(dir), 63, MU);
                    brass.Box(tip0 + dir * 0.052f, new Vector3(0.004f, 0.004f, 0.006f), Quaternion.LookRotation(dir), 63, MU);   // copper
                }
                Saw(c, Vector3.up, "cable_stubs");
            }
            else Saw(c, Vector3.up, "floor_box");
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
                // The plug in the outlet, the cord down the wall to the floor and out to the run.
                var pf = plug.Value + plugN.Value * 0.012f;
                cord.Box(pf + plugN.Value * 0.014f, new Vector3(0.03f, 0.038f, 0.03f), Quaternion.LookRotation(plugN.Value), 63, MU);
                var foot = new Vector3(pf.x, y, pf.z) + plugN.Value * 0.03f;
                Tube(cord, pf + plugN.Value * 0.03f - Vector3.up * 0.02f, foot + Vector3.up * 0.02f, 0.0045f);
                Tube(cord, foot + Vector3.up * 0.02f, foot, 0.0045f);
                pts.Insert(0, foot);
            }
            for (int i = 0; i + 1 < pts.Count; i++) Tube(cord, pts[i], pts[i + 1], 0.0045f);
            // The loose end: a socket lying on the floor.
            var tip = pts[pts.Count - 1];
            var dirEnd = (tip - pts[pts.Count - 2]).normalized;
            cord.Box(tip + dirEnd * 0.03f + Vector3.up * 0.01f, new Vector3(0.045f, 0.03f, 0.06f), Quaternion.LookRotation(dirEnd), 63, MU);
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
                var tc = new Vector3(pts[i].x, 0.0005f, pts[i].z);
                // Flat either side, over the cord in the middle.
                tape.Box(tc + Vector3.up * 0.0005f, new Vector3(tw * 2f, 0.001f, tl * 2f), q, 63 & ~4, MU);
                tape.Box(tc + Vector3.up * 0.009f, new Vector3(tw * 2f, 0.001f, 0.024f), q, 63 & ~4, MU);
            }
            Saw(pts[pts.Count / 2], Vector3.up, "cord");
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
            if (!FittingMaterials.Fabric || !FurnitureMaterials.Steel) return;
            uint m = LayerBit(p.roomColor);
            var fabric = WMask(FittingMaterials.Fabric, m);
            var steel = WMask(FurnitureMaterials.Steel, m);
            float w = p.size.x, h = p.size.y, t = p.size.z;
            bool fallen = (p.variant & 1) != 0;
            if ((p.variant & 2) != 0) Saw(at + Vector3.up * 0.6f, Vector3.up, "partitions");
            // Built standing in its own frame (x across, y up, z through), then laid down if it fell: about its foot's
            // long edge, onto its face (its middle, where the plan put it, half its height out from its foot).
            var q = fallen ? rot * Quaternion.Euler(90f, 0f, 0f) : rot;
            var footAt = fallen ? at - rot * Vector3.forward * (h * 0.5f) : at;
            Vector3 L(float x, float y, float z) => footAt + q * new Vector3(x, y, z) + (fallen ? Vector3.up * (t * 0.5f) : Vector3.zero);
            const float frame = 0.03f, foot = 0.025f;
            float y0 = fallen ? 0f : foot;
            // The fabric faces and the frame round them.
            fabric.Box(L(0f, y0 + (h - foot) * 0.5f, 0f), new Vector3(w - 2f * frame, h - foot - 2f * frame, t), q, 16 | 32, MU);
            steel.Box(L(0f, y0 + h - foot - frame * 0.5f, 0f), new Vector3(w, frame, t + 0.004f), q, 63, MU);
            steel.Box(L(0f, y0 + frame * 0.5f, 0f), new Vector3(w, frame, t + 0.004f), q, 63, MU);
            steel.Box(L(-w * 0.5f + frame * 0.5f, y0 + (h - foot) * 0.5f, 0f), new Vector3(frame, h - foot - 2f * frame, t + 0.004f), q, 63, MU);
            steel.Box(L(w * 0.5f - frame * 0.5f, y0 + (h - foot) * 0.5f, 0f), new Vector3(frame, h - foot - 2f * frame, t + 0.004f), q, 63, MU);
            if (!fallen)
            {
                // Its feet: steel glides across under each end, a little wider than the panel.
                for (int s = -1; s <= 1; s += 2)
                    steel.Box(L(s * (w * 0.5f - 0.06f), foot * 0.5f, 0f), new Vector3(0.04f, foot, t + 0.22f), q, 63 & ~4, MU);
                _rotBoxes.Add((at + Vector3.up * (h * 0.5f), new Vector3(w, h, t + 0.02f), p.yaw));
            }
            else _rotBoxes.Add((at + Vector3.up * (t * 0.5f + 0.01f), new Vector3(w, t + 0.02f, h), p.yaw));
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
