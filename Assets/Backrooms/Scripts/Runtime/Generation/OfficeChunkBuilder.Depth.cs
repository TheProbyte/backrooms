using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Darker copies of the painted wall for the inside of a pit room's shafts (DepthPlan): the light grid lights a shaft's
    /// walls and floor as if they were open to the room, so the dark comes from the paint instead, deeper bands darker, as
    /// ambient occlusion would make them. Made on the main thread before any chunk is built (WorldStreamer); worker threads
    /// only read them.
    /// </summary>
    public static class DepthMaterials
    {
        /// <summary>Brightness of each band of a shaft's walls, from the top down, and of its floor.</summary>
        public static readonly float[] Bands = { 0.9f, 0.82f, 0.74f, 0.66f, 0.58f, 0.51f, 0.45f, 0.39f, 0.34f, 0.29f, 0.25f };
        public const float Bottom = 0.18f;
        static Material[] s_bands;
        static Material s_bottom, s_from;

        public static void Ensure(OfficeBiomeAssets a)
        {
            var src = a ? (a.paintedWall ? a.paintedWall : a.wallpaper) : null;
            if (!src || (s_from == src && s_bands != null && s_bands[0])) return;
            s_from = src;
            s_bands = new Material[Bands.Length];
            for (int i = 0; i < Bands.Length; i++) s_bands[i] = Darker(src, Bands[i], "Pit shaft " + i);
            s_bottom = Darker(src, Bottom, "Pit shaft floor");
        }

        static Material Darker(Material src, float k, string name)
        {
            var m = new Material(src) { name = name, hideFlags = HideFlags.DontSave };
            foreach (var p in new[] { "_BaseColor", "_Color" })
                if (m.HasProperty(p)) { var c = m.GetColor(p); m.SetColor(p, new Color(c.r * k, c.g * k, c.b * k, c.a)); }
            return m;
        }

        public static Material Band(int i, Material fallback) => s_bands != null && i < s_bands.Length && s_bands[i] ? s_bands[i] : fallback;
        public static Material Floor(Material fallback) => s_bottom ? s_bottom : fallback;
    }

    /// <summary>
    /// Height and depth (world version 14, DepthPlan): steps down into sunken rooms, a pit room's walkways over shafts, and
    /// corners cut at 45 degrees. (Floors, walls and posts carried down to a sunken floor are in the builder's own passes.)
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        void BuildDepth()
        {
            BuildRamps();
            BuildSteps();
            BuildPits();
            BuildChamfers();
        }

        /// <summary>The cell's side (0 -X, 1 +X, 2 -Z, 3 +Z) an edge seen from side s of it is on.</summary>
        static int SideOfEdge(bool vertical, int s) => vertical ? (s == 0 ? 1 : 0) : (s == 0 ? 3 : 2);

        // ---------------- Steps ----------------

        /// <summary>Steps down from a doorway into a sunken room: rising toward the doorway, across the cell's width.</summary>
        void BuildSteps()
        {
            if (_L.steps == null) return;
            int N = _L.N;
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                int side = _L.StepSide(x, z);
                if (side < 0) continue;
                var room = _L.RoomAt(x, z);
                float depth = -room.floor;
                if (depth < 0.01f) continue;
                int steps = DepthPlan.Steps(depth);
                float rise = depth / steps, tread = DepthPlan.Tread(depth);
                var w = WMask(_a.FloorMaterial(room), CellMask(x, z));
                Vector3 outDir = side == 0 ? Vector3.left : side == 1 ? Vector3.right : side == 2 ? Vector3.back : Vector3.forward;
                // (Right up to the doorway's line, where the last riser is: from half a wall in, a slot showed down to the floor.)
                Vector3 edgeMid = new Vector3((x + 0.5f) * C, 0f, (z + 0.5f) * C) + outDir * (C * 0.5f);
                bool alongZ = side < 2;
                // Faces: the top, the riser (facing into the room), and the two ends.
                int riser = side == 0 ? 2 : side == 1 ? 1 : side == 2 ? 32 : 16;
                int ends = alongZ ? 16 | 32 : 1 | 2;
                float width = C - T;
                for (int k = 1; k < steps; k++)
                {
                    float run = (steps - k) * tread, top = -depth + rise * k;
                    var c = edgeMid - outDir * (run * 0.5f) + Vector3.up * ((-depth + top) * 0.5f);
                    var s = alongZ ? new Vector3(run, top + depth, width) : new Vector3(width, top + depth, run);
                    w.Box(c, s, 8 | riser | ends, MeshWriter.MetreUV);
                    _boxes.Add((c, s));
                }
            }
        }

        // ---------------- Risers ----------------

        /// <summary>The floor on the far side of an edge at a point on its line (in this chunk or the next); 0 if unknown.</summary>
        float OtherFloorAt(bool vertical, int line, int along, int s, float at)
        {
            int ox = vertical ? (s == 0 ? line : line - 1) : along, oz = vertical ? along : (s == 0 ? line : line - 1);
            float px = vertical ? line : at / C, pz = vertical ? at / C : line;
            if (_L.InChunk(ox, oz)) return _L.FloorOf(_L.RoomAt(ox, oz), px, pz);
            int N = _L.N, dx = ox < 0 ? -1 : ox >= N ? 1 : 0, dz = oz < 0 ? -1 : oz >= N ? 1 : 0;
            var nl = Neighbour(dx, dz);
            if (nl == null) return 0f;
            return nl.FloorOf(nl.RoomAt(ox - dx * N, oz - dz * N), px - dx * N, pz - dz * N);
        }

        /// <summary>
        /// A doorway from a lower floor up to a higher one: the face on the edge's line between the two floors, seen from
        /// the lower side (the last riser of steps, a ramp's lip). Each end at its own heights.
        /// </summary>
        void DoorwayRiser(Room room, uint cm, bool vertical, int line, int along, int s, float l, Vector3 n)
        {
            float b0 = along * C, b1 = (along + 1) * C;
            float mine0 = FloorAlong(room, vertical, line, b0), mine1 = FloorAlong(room, vertical, line, b1);
            float other0 = OtherFloorAt(vertical, line, along, s, b0), other1 = OtherFloorAt(vertical, line, along, s, b1);
            if (other0 <= mine0 + 0.004f && other1 <= mine1 + 0.004f) return;
            Vector3 P(float at, float y) => vertical ? new Vector3(l, y, at) : new Vector3(at, y, l);
            WMask(_a.FloorMaterial(room), cm).Quad(P(b0, mine0), P(b1, mine1), P(b1, Mathf.Max(other1, mine1)), P(b0, Mathf.Max(other0, mine0)), n, MeshWriter.MetreUV);
        }

        // ---------------- Ramps ----------------

        /// <summary>A ramp room's floor at a point on an edge line (vertical: X = line at Z = at, metres along; else Z = line).</summary>
        float FloorAlong(Room r, bool vertical, int line, float at) => vertical ? _L.FloorOf(r, line, at / C) : _L.FloorOf(r, at / C, line);

        /// <summary>A wall face whose bottom follows a sloping floor (from ya at a to yb at b) up to y1.</summary>
        void SlopedWallFace(Room room, uint mask, Vector3 a, Vector3 b, float ya, float yb, float y1, Vector3 n)
        {
            WMask(WallMat(room), mask).Quad(a + Vector3.up * ya, b + Vector3.up * yb, b + Vector3.up * y1, a + Vector3.up * y1, n, Pool ? MeshWriter.MetreUV : MeshWriter.WallUV);
        }

        /// <summary>
        /// A ramp's colliders: each cell in slices 0.2 m long down its slope, each as high as its high end (3 cm steps at
        /// 1 in 6: the body walks them as a slope; the floor drawn is the smooth slope).
        /// </summary>
        void BuildRamps()
        {
            int N = _L.N;
            const int K = 6;
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                var r = _L.RoomAt(x, z);
                if (r.ramp == 0) continue;
                bool alongX = r.ramp <= 2;
                for (int k = 0; k < K; k++)
                {
                    float a = (float)k / K, b = (float)(k + 1) / K;
                    float h = alongX ? Mathf.Max(_L.FloorOf(r, x + a, z + 0.5f), _L.FloorOf(r, x + b, z + 0.5f))
                                     : Mathf.Max(_L.FloorOf(r, x + 0.5f, z + a), _L.FloorOf(r, x + 0.5f, z + b));
                    const float depth = 1.2f;
                    var c = alongX ? new Vector3((x + (a + b) * 0.5f) * C, h - depth * 0.5f, (z + 0.5f) * C) : new Vector3((x + 0.5f) * C, h - depth * 0.5f, (z + (a + b) * 0.5f) * C);
                    var s = alongX ? new Vector3(C / K, depth, C) : new Vector3(C, depth, C / K);
                    _boxes.Add((c, s));
                }
            }
        }

        // ---------------- Pit rooms ----------------

        /// <summary>Is a chunk-local point (x, z) inside one of this chunk's shafts?</summary>
        bool InShaft(float x, float z)
        {
            foreach (var p in _L.pits) if (x > p.rect.xMin && x < p.rect.xMax && z > p.rect.yMin && z < p.rect.yMax) return true;
            return false;
        }

        /// <summary>
        /// A pit room's floor in this chunk: the walkways (each cell less the shafts in it), the shafts' walls and floors, the
        /// block in a shaft's corner, and their colliders (the walkways solid 3 m down, so a shaft's walls are solid).
        /// </summary>
        void BuildPits()
        {
            int N = _L.N;
            bool any = false;
            foreach (var r in _L.rooms) if (r.feature == RoomFeature.Pit) { any = true; break; }
            if (!any) return;
            var xs = new List<float>(); var zs = new List<float>();
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                var room = _L.RoomAt(x, z);
                if (room.feature != RoomFeature.Pit) continue;
                float x0 = x * C, z0 = z * C, x1 = x0 + C, z1 = z0 + C;
                xs.Clear(); zs.Clear(); xs.Add(x0); xs.Add(x1); zs.Add(z0); zs.Add(z1);
                foreach (var p in _L.pits)
                {
                    if (p.rect.xMin > x0 && p.rect.xMin < x1) xs.Add(p.rect.xMin);
                    if (p.rect.xMax > x0 && p.rect.xMax < x1) xs.Add(p.rect.xMax);
                    if (p.rect.yMin > z0 && p.rect.yMin < z1) zs.Add(p.rect.yMin);
                    if (p.rect.yMax > z0 && p.rect.yMax < z1) zs.Add(p.rect.yMax);
                }
                xs.Sort(); zs.Sort();
                var w = WMask(WallMat(room), CellMask(x, z));
                for (int i = 0; i + 1 < xs.Count; i++)
                for (int j = 0; j + 1 < zs.Count; j++)
                {
                    float a0 = xs[i], a1 = xs[i + 1], b0 = zs[j], b1 = zs[j + 1];
                    if (a1 - a0 < 1e-4f || b1 - b0 < 1e-4f || InShaft((a0 + a1) * 0.5f, (b0 + b1) * 0.5f)) continue;
                    w.Quad(new Vector3(a0, 0, b0), new Vector3(a0, 0, b1), new Vector3(a1, 0, b1), new Vector3(a1, 0, b0), Vector3.up, MeshWriter.MetreUV);
                    _boxes.Add((new Vector3((a0 + a1) * 0.5f, -1.5f, (b0 + b1) * 0.5f), new Vector3(a1 - a0, 3f, b1 - b0)));
                }
            }

            float lo = 0f, hi = N * C;
            foreach (var p in _L.pits)
            {
                var r = p.rect;
                // This chunk's part of the shaft.
                float cx0 = Mathf.Max(r.xMin, lo), cx1 = Mathf.Min(r.xMax, hi), cz0 = Mathf.Max(r.yMin, lo), cz1 = Mathf.Min(r.yMax, hi);
                if (cx1 <= cx0 || cz1 <= cz0) continue;
                int mx = Mathf.Clamp(Mathf.FloorToInt((cx0 + cx1) * 0.5f / C), 0, N - 1), mz = Mathf.Clamp(Mathf.FloorToInt((cz0 + cz1) * 0.5f / C), 0, N - 1);
                uint m = CellMask(mx, mz);
                var room = _L.RoomAt(mx, mz);
                float d = p.depth;
                // Walls, in bands darker toward the bottom.
                void Wall(Vector3 a, Vector3 b, Vector3 n)
                {
                    int bands = DepthMaterials.Bands.Length;
                    for (int i = 0; i < bands; i++)
                    {
                        float y0 = -d * (i + 1) / bands, y1 = -d * i / bands;
                        WMask(DepthMaterials.Band(i, WallMat(room)), m).Quad(a + Vector3.up * y0, b + Vector3.up * y0, b + Vector3.up * y1, a + Vector3.up * y1, n, MeshWriter.MetreUV);
                    }
                }
                if (r.xMin >= lo && r.xMin < hi) Wall(new Vector3(r.xMin, 0, cz1), new Vector3(r.xMin, 0, cz0), Vector3.right);
                if (r.xMax > lo && r.xMax <= hi) Wall(new Vector3(r.xMax, 0, cz0), new Vector3(r.xMax, 0, cz1), Vector3.left);
                if (r.yMin >= lo && r.yMin < hi) Wall(new Vector3(cx0, 0, r.yMin), new Vector3(cx1, 0, r.yMin), Vector3.forward);
                if (r.yMax > lo && r.yMax <= hi) Wall(new Vector3(cx1, 0, r.yMax), new Vector3(cx0, 0, r.yMax), Vector3.back);
                // Its floor.
                var floor = WMask(DepthMaterials.Floor(WallMat(room)), m);
                floor.Quad(new Vector3(cx0, -d, cz0), new Vector3(cx0, -d, cz1), new Vector3(cx1, -d, cz1), new Vector3(cx1, -d, cz0), Vector3.up, MeshWriter.MetreUV);
                _boxes.Add((new Vector3((cx0 + cx1) * 0.5f, -d - 0.25f, (cz0 + cz1) * 0.5f), new Vector3(cx1 - cx0, 0.5f, cz1 - cz0)));
                // The block in its corner, a step up out of it (built by the chunk its middle is in).
                if (p.ledge > 0.01f)
                {
                    var b = DepthPlan.LedgeRect(p);
                    if (b.center.x >= lo && b.center.x < hi && b.center.y >= lo && b.center.y < hi)
                    {
                        var c = new Vector3(b.center.x, -d + p.ledge * 0.5f, b.center.y);
                        var s = new Vector3(b.width, p.ledge, b.height);
                        WMask(DepthMaterials.Band(DepthMaterials.Bands.Length - 2, WallMat(room)), m).Box(c, s, 8 | 1 | 2 | 16 | 32, MeshWriter.MetreUV);
                        _boxes.Add((c, s));
                    }
                }
            }
        }

        // ---------------- Corners cut at 45 degrees ----------------

        /// <summary>
        /// A diagonal wall across each cut corner cell of a room (Room.chamfers), from the face of one wall to the face of
        /// the other, floor to ceiling, with its baseboard and a collider. Built by the chunk the corner cell is in.
        /// </summary>
        void BuildChamfers()
        {
            int N = _L.N;
            var d = WorldGenerator.DistrictOf(_L.coord);
            int ox = (_L.coord.x - d.x * WorldGenSettings.DistrictChunks) * N, oz = (_L.coord.y - d.y * WorldGenSettings.DistrictChunks) * N;
            foreach (var r in _L.rooms)
            {
                if (r.chamfers == 0) continue;
                var f = r.fullRect;
                for (int c = 0; c < 4; c++)
                {
                    if ((r.chamfers & (1 << c)) == 0) continue;
                    int gx = (c & 1) == 0 ? f.xMin : f.xMax - 1, gz = (c & 2) == 0 ? f.yMin : f.yMax - 1;
                    int x = gx - ox, z = gz - oz;
                    if (!_L.InChunk(x, z)) continue;
                    // The room's corner and the ways into the room from it.
                    float cx = ((c & 1) == 0 ? x : x + 1) * C, cz = ((c & 2) == 0 ? z : z + 1) * C;
                    float ix = (c & 1) == 0 ? 1f : -1f, iz = (c & 2) == 0 ? 1f : -1f;
                    float e = T * 0.5f;
                    // From the face of the wall along X (at z = cz) one cell in, to the face of the wall along Z.
                    Vector3 a = new Vector3(cx + ix * C, 0f, cz + iz * e), b = new Vector3(cx + ix * e, 0f, cz + iz * C);
                    Vector3 n = new Vector3(ix, 0f, iz).normalized;
                    var room = _L.RoomAt(x, z);
                    uint m = CellMask(x, z);
                    float fl = room.floor, top = _L.WallTop(room);
                    WallFace(room, m, a, b, fl, top, n);
                    if (!NoBaseboard) Baseboard(m, a + Vector3.up * fl, b + Vector3.up * fl, n);
                    Vector3 dir = b - a;
                    // (A box's local z runs along the wall: PropGrabber-style rotated colliders, as the Poolrooms' curves.)
                    float len = dir.magnitude, yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    Vector3 mid = (a + b) * 0.5f - n * (T * 0.5f);
                    _rotBoxes.Add((new Vector3(mid.x, (fl + top) * 0.5f, mid.z), new Vector3(T, top - fl + 0.2f, len + T), yaw));
                }
            }
        }
    }
}
