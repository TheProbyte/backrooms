using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Wall cutouts (<see cref="WallCutout"/>, build mode's doors and windows of any size): the wall of each edge a hole
    /// reaches is built as the pieces round it (full height beside it, above it, below a window's sill), its reveals (the
    /// sides, the head or arch, the sill, half the wall's thickness from each face), its colliders, and the posts it spans
    /// cut to above and below it.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Edge (vertical, line, along) -> the cutouts that reach it.</summary>
        readonly Dictionary<(bool, int, int), List<WallCutout>> _cuts = new Dictionary<(bool, int, int), List<WallCutout>>();

        void IndexCutouts()
        {
            _cuts.Clear();
            foreach (var c in _L.cutouts)
            {
                if (c.line < 0 || c.line > _L.N || c.to - c.from < 0.05f) continue;
                int a0 = Mathf.Max(0, Mathf.FloorToInt(c.from / C)), a1 = Mathf.Min(_L.N - 1, Mathf.CeilToInt(c.to / C) - 1);
                for (int a = a0; a <= a1; a++)
                {
                    if (c.to <= a * C + 0.005f || c.from >= (a + 1) * C - 0.005f) continue;
                    var key = (c.vertical, c.line, a);
                    if (!_cuts.TryGetValue(key, out var list)) _cuts[key] = list = new List<WallCutout>();
                    list.Add(c);
                }
            }
            foreach (var list in _cuts.Values) list.Sort((p, q) => p.from.CompareTo(q.from));
        }

        bool CutAt(bool vertical, int line, int along) => _cuts.Count > 0 && _cuts.ContainsKey((vertical, line, along));

        /// <summary>Height of a cutout's top at a point along its line (an arch: the half circle across its width).</summary>
        static float TopAt(in WallCutout c, float at)
        {
            if (!c.arch) return c.top;
            float r = (c.to - c.from) * 0.5f, mid = (c.from + c.to) * 0.5f;
            float spring = Mathf.Max(c.sill + 0.05f, c.top - r);
            float d = Mathf.Clamp(at - mid, -r, r);
            return Mathf.Min(c.top, spring + Mathf.Sqrt(Mathf.Max(0f, r * r - d * d)) * ((c.top - spring) / Mathf.Max(r, 1e-4f)));
        }

        /// <summary>One face of a wall edge with cutouts: the pieces round each hole and this side's half of its reveals.</summary>
        void CutoutFaces(Room room, uint cm, bool vertical, int line, int along, float l, float f, float a0, float a1, Vector3 n, float myCeil)
        {
            var holes = _cuts[(vertical, line, along)];
            Vector3 P(float plane, float at, float y) => (vertical ? new Vector3(plane, 0, at) : new Vector3(at, 0, plane)) + Vector3.up * y;
            Vector3 alongDir = vertical ? Vector3.forward : Vector3.right;
            float floor = room.floor;
            var w = WMask(WallMat(room), cm);
            var uv = Pool ? MeshWriter.MetreUV : (MeshWriter.UVProjector)MeshWriter.WallUV;
            void Solid(float x0, float x1, float y0, float y1, bool baseboard)
            {
                if (x1 - x0 < 0.001f || y1 - y0 < 0.001f) return;
                w.Quad(P(f, x0, y0), P(f, x1, y0), P(f, x1, y1), P(f, x0, y1), n, uv);
                if (baseboard && !room.noFloor) Baseboard(cm, P(f, x0, floor), P(f, x1, floor), n);
            }
            float cursor = a0;
            foreach (var h in holes)
            {
                float h0 = Mathf.Max(h.from, a0), h1 = Mathf.Min(h.to, a1);
                if (h1 <= h0 + 0.001f) continue;
                if (h0 > cursor) Solid(cursor, h0, floor, myCeil, true);
                float sill = floor + Mathf.Max(0f, h.sill);
                // Below a window's sill, with its top.
                if (h.sill > 0.001f)
                {
                    Solid(h0, h1, floor, Mathf.Min(sill, myCeil), true);
                    if (sill < myCeil) w.Quad(P(l, h0, sill), P(f, h0, sill), P(f, h1, sill), P(l, h1, sill), Vector3.up, uv);
                }
                // Above it, and its head (flat) or arch (in strips).
                int K = h.arch ? Mathf.Max(2, Mathf.CeilToInt((h1 - h0) / 0.04f)) : 1;
                for (int i = 0; i < K; i++)
                {
                    float x0 = Mathf.Lerp(h0, h1, i / (float)K), x1 = Mathf.Lerp(h0, h1, (i + 1) / (float)K);
                    float y0 = Mathf.Min(floor + TopAt(h, x0), myCeil), y1 = Mathf.Min(floor + TopAt(h, x1), myCeil);
                    if (myCeil > Mathf.Min(y0, y1) + 0.001f)
                        w.Quad(P(f, x0, y0), P(f, x1, y1), P(f, x1, myCeil), P(f, x0, myCeil), n, uv);
                    if (y0 < myCeil - 0.001f || y1 < myCeil - 0.001f)
                    {
                        // The underside: faces down (into the arch's centre for an arch).
                        Vector3 d = P(0f, x1, y1) - P(0f, x0, y0);
                        Vector3 down = Vector3.Cross(d, vertical ? Vector3.right : Vector3.forward).normalized;
                        if (down.y > 0f) down = -down;
                        w.Quad(P(l, x0, y0), P(f, x0, y0), P(f, x1, y1), P(l, x1, y1), down, uv);
                    }
                }
                // Its sides, where they are in this edge (at a post the post's face is the side).
                float top0 = Mathf.Min(floor + TopAt(h, h.from), myCeil), top1 = Mathf.Min(floor + TopAt(h, h.to), myCeil);
                if (h.from > a0 + 0.001f && h.from < a1 - 0.001f && top0 > sill)
                    w.Quad(P(l, h.from, sill), P(f, h.from, sill), P(f, h.from, top0), P(l, h.from, top0), alongDir, uv);
                if (h.to > a0 + 0.001f && h.to < a1 - 0.001f && top1 > sill)
                    w.Quad(P(l, h.to, sill), P(f, h.to, sill), P(f, h.to, top1), P(l, h.to, top1), -alongDir, uv);
                cursor = Mathf.Max(cursor, h1);
            }
            if (cursor < a1) Solid(cursor, a1, floor, myCeil, true);
        }

        /// <summary>Colliders of the wall edges with cutouts (left out of the merged wall runs).</summary>
        void CutoutColliders()
        {
            const float HC = OfficeBiome.MaxCeilingHeight;
            int N = _L.N;
            foreach (var kv in _cuts)
            {
                var (vertical, line, along) = kv.Key;
                var e = vertical ? _L.vEdges[line, along] : _L.hEdges[along, line];
                if (e == EdgeKind.Open) continue;
                float pos = line * C, th = T;
                if (line == 0) { pos += T * 0.25f; th = T * 0.5f; } else if (line == N) { pos -= T * 0.25f; th = T * 0.5f; }
                void Box(float x0, float x1, float y0, float y1)
                {
                    if (x1 - x0 < 0.005f || y1 - y0 < 0.005f) return;
                    var c = vertical ? new Vector3(pos, (y0 + y1) * 0.5f, (x0 + x1) * 0.5f) : new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, pos);
                    var s = vertical ? new Vector3(th, y1 - y0, x1 - x0) : new Vector3(x1 - x0, y1 - y0, th);
                    _boxes.Add((c, s));
                }
                float e0 = along * C - T * 0.5f, e1 = (along + 1) * C + T * 0.5f;
                float cursor = e0;
                foreach (var h in kv.Value)
                {
                    float h0 = Mathf.Max(h.from, e0), h1 = Mathf.Min(h.to, e1);
                    if (h1 <= h0) continue;
                    if (h0 > cursor) Box(cursor, h0, 0f, HC + 0.2f);
                    // Over it (an arch: from its springing line, the corners are a little open); under a window's sill.
                    float top = h.arch ? Mathf.Max(h.sill + 0.05f, h.top - (h.to - h.from) * 0.5f) : h.top;
                    Box(h0, h1, top, HC + 0.2f);
                    if (h.sill > 0.01f) Box(h0, h1, 0f, h.sill);
                    cursor = Mathf.Max(cursor, h1);
                }
                if (cursor < e1) Box(cursor, e1, 0f, HC + 0.2f);
            }
        }

        /// <summary>
        /// A post at this vertex standing inside a cutout (the hole runs on past it): only the parts above and below the hole
        /// are built. Returns false if no cutout spans the vertex.
        /// </summary>
        bool PostInCutout(int x, int z, out WallCutout cut)
        {
            cut = default;
            if (_cuts.Count == 0) return false;
            foreach (var c in _L.cutouts)
            {
                float at;
                if (c.vertical && c.line == x) at = z * C;
                else if (!c.vertical && c.line == z) at = x * C;
                else continue;
                if (c.from < at - T * 0.5f + 0.001f && c.to > at + T * 0.5f - 0.001f) { cut = c; return true; }
            }
            return false;
        }

        /// <summary>
        /// The post pieces above and below a cutout running through this vertex: the wall's two faces across the post (their
        /// underside following an arch's curve, so the crown has no step), the underside, a window's sill, and colliders.
        /// </summary>
        void CutPost(int x, int z, WallCutout c)
        {
            const float HC = OfficeBiome.MaxCeilingHeight;
            bool vertical = c.vertical;
            float at = vertical ? z * C : x * C, l = vertical ? x * C : z * C;
            Vector3 P(float plane, float a, float y) => (vertical ? new Vector3(plane, 0, a) : new Vector3(a, 0, plane)) + Vector3.up * y;
            var uv = Pool ? MeshWriter.MetreUV : (MeshWriter.UVProjector)MeshWriter.WallUV;
            float a0 = at - T * 0.5f, a1 = at + T * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                // A cell on this side of the line (either of the two the post stands between).
                int cx0 = vertical ? (side < 0 ? x - 1 : x) : x, cz0 = vertical ? z : (side < 0 ? z - 1 : z);
                int cx1 = vertical ? cx0 : x - 1, cz1 = vertical ? z - 1 : cz0;
                int cx = _L.InChunk(cx0, cz0) ? cx0 : cx1, cz = _L.InChunk(cx0, cz0) ? cz0 : cz1;
                if (!_L.InChunk(cx, cz)) continue;
                var room = _L.RoomAt(cx, cz);
                var w = WMask(WallMat(room), CellMask(cx, cz));
                float FH = _L.WallTop(room), FB = room.floor, f = l + side * T * 0.5f;
                Vector3 n = vertical ? new Vector3(side, 0, 0) : new Vector3(0, 0, side);
                int K = c.arch ? 4 : 1;
                for (int i = 0; i < K; i++)
                {
                    float x0 = Mathf.Lerp(a0, a1, i / (float)K), x1 = Mathf.Lerp(a0, a1, (i + 1) / (float)K);
                    float y0 = Mathf.Min(FB + TopAt(c, x0), FH), y1 = Mathf.Min(FB + TopAt(c, x1), FH);
                    if (FH > Mathf.Min(y0, y1) + 0.001f) w.Quad(P(f, x0, y0), P(f, x1, y1), P(f, x1, FH), P(f, x0, FH), n, uv);
                    if (y0 < FH - 0.001f || y1 < FH - 0.001f)
                    {
                        Vector3 d = P(0f, x1, y1) - P(0f, x0, y0);
                        Vector3 down = Vector3.Cross(d, vertical ? Vector3.right : Vector3.forward).normalized;
                        if (down.y > 0f) down = -down;
                        w.Quad(P(l, x0, y0), P(f, x0, y0), P(f, x1, y1), P(l, x1, y1), down, uv);
                    }
                }
                if (c.sill > 0.001f)
                {
                    float sy = Mathf.Min(FB + c.sill, FH);
                    w.Quad(P(f, a0, FB), P(f, a1, FB), P(f, a1, sy), P(f, a0, sy), n, uv);
                    w.Quad(P(l, a0, sy), P(f, a0, sy), P(f, a1, sy), P(l, a1, sy), Vector3.up, uv);
                }
            }
            // Colliders over (from the arch's springing line) and under it.
            float top = c.arch ? Mathf.Max(c.sill + 0.05f, c.top - (c.to - c.from) * 0.5f) : c.top;
            _boxes.Add((new Vector3(x * C, (top + HC + 0.2f) * 0.5f, z * C), new Vector3(T, HC + 0.2f - top, T)));
            if (c.sill > 0.01f) _boxes.Add((new Vector3(x * C, c.sill * 0.5f, z * C), new Vector3(T, c.sill, T)));
        }

        /// <summary>A wall plate (a switch, an outlet) that would sit in a cutout.</summary>
        bool PlateInCutout(WallPlate p)
        {
            if (_cuts.Count == 0) return false;
            bool vertical = p.side < 2;
            int line = p.side == 0 ? p.cell.x : p.side == 1 ? p.cell.x + 1 : p.side == 2 ? p.cell.y : p.cell.y + 1;
            int along = vertical ? p.cell.y : p.cell.x;
            if (!_cuts.TryGetValue((vertical, line, along), out var list)) return false;
            float at = (along + p.t) * C;
            foreach (var c in list)
                if (at > c.from - 0.08f && at < c.to + 0.08f && p.height > c.sill - 0.08f && p.height < TopAt(c, at) + 0.08f) return true;
            return false;
        }
    }
}
