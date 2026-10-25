using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Crawl holes (see <see cref="CrawlPortals"/>): the wall around the hole, its reveals, the ordinary solid reverse wall, its colliders, and a <see cref="Interaction.CrawlPortalView"/> per hole that shows (and leads to) the other end.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        const float HS = CrawlPortals.HoleSize;

        /// <summary>Hole edges: (vertical, line, along) -> (portal index, side that is entered from: 0 = cell at line-1, 1 = cell at line).</summary>
        readonly Dictionary<(bool, int, int), (int portal, int entry)> _holes = new Dictionary<(bool, int, int), (int, int)>();
        readonly List<CrawlPortal> _portalViews = new List<CrawlPortal>();

        void IndexHoles()
        {
            _holes.Clear(); _portalViews.Clear();
            for (int i = 0; i < _L.portals.Count; i++)
            {
                var p = _L.portals[i];
                int x = p.cell.x, z = p.cell.y;
                switch (p.side)
                {
                    case 0: _holes[(true, x, z)] = (i, 1); break;
                    case 1: _holes[(true, x + 1, z)] = (i, 0); break;
                    case 2: _holes[(false, z, x)] = (i, 1); break;
                    default: _holes[(false, z + 1, x)] = (i, 0); break;
                }
                _portalViews.Add(p);
            }
        }

        bool HoleAt(bool vertical, int line, int along, out int entry)
        {
            entry = 0;
            if (_holes.Count == 0 || !_holes.TryGetValue((vertical, line, along), out var h)) return false;
            entry = h.entry;
            return true;
        }

        /// <summary>One side of a wall with a hole at its foot. The entered side also builds the reveals through the wall.</summary>
        void HoleFaces(Room room, uint cm, bool vertical, int along, float l, float f, float a0, float a1, Vector3 n, float myCeil, bool entered)
        {
            Vector3 A(float pos, float at) => vertical ? new Vector3(pos, 0, at) : new Vector3(at, 0, pos);
            // The reverse face is an ordinary, continuous wall. Nothing occupies the neighbouring room.
            if (!entered)
            {
                WallFace(room, cm, A(f, a0), A(f, a1), 0f, myCeil, n);
                Baseboard(cm, A(f, a0), A(f, a1), n);
                return;
            }
            float mid = (along + 0.5f) * C, h0 = mid - HS * 0.5f, h1 = mid + HS * 0.5f;
            WallFace(room, cm, A(f, a0), A(f, h0), 0f, myCeil, n);
            WallFace(room, cm, A(f, h1), A(f, a1), 0f, myCeil, n);
            WallFace(room, cm, A(f, h0), A(f, h1), HS, myCeil, n);
            var w = WMask(WallMat(room), cm);
            float fo = l - (f - l);                                    // the other face of the wall
            Vector3 alongDir = vertical ? Vector3.forward : Vector3.right;
            Vector3 up = Vector3.up * HS;
            w.Quad(A(f, h0), A(fo, h0), A(fo, h0) + up, A(f, h0) + up, alongDir, MeshWriter.WallUV);        // jambs face into the hole
            w.Quad(A(f, h1), A(fo, h1), A(fo, h1) + up, A(f, h1) + up, -alongDir, MeshWriter.WallUV);
            w.Quad(A(f, h0) + up, A(f, h1) + up, A(fo, h1) + up, A(fo, h0) + up, Vector3.down, MeshWriter.WallUV);
        }

        /// <summary>The wall colliders around each aperture.</summary>
        void BuildPortals()
        {
            foreach (var kv in _holes)
            {
                var (vertical, line, along) = kv.Key;
                int entry = kv.Value.entry;
                // Both wall faces belong to this chunk.
                int bx = vertical ? (entry == 1 ? line - 1 : line) : along, bz = vertical ? along : (entry == 1 ? line - 1 : line);
                if (!_L.InChunk(bx, bz)) continue;
                float l = line * C;
                float mid = (along + 0.5f) * C, h0 = mid - HS * 0.5f, h1 = mid + HS * 0.5f;
                // No duct or geometry beyond the wall. A separately owned aperture collider in
                // CrawlPortalView closes the reverse side and can ignore only the traversing capsule.

                // Wall colliders around the hole (the edge is left out of the merged wall runs).
                const float HC = OfficeBiome.MaxCeilingHeight;
                Vector3 WallBox(float a0, float a1, float y0, float y1, out Vector3 s)
                {
                    s = vertical ? new Vector3(T, y1 - y0, a1 - a0) : new Vector3(a1 - a0, y1 - y0, T);
                    return vertical ? new Vector3(l, (y0 + y1) * 0.5f, (a0 + a1) * 0.5f) : new Vector3((a0 + a1) * 0.5f, (y0 + y1) * 0.5f, l);
                }
                float e0 = along * C - T * 0.5f, e1 = (along + 1) * C + T * 0.5f;
                var b1 = WallBox(e0, h0, 0f, HC + 0.2f, out var s1); _boxes.Add((b1, s1));
                var b2 = WallBox(h1, e1, 0f, HC + 0.2f, out var s2); _boxes.Add((b2, s2));
                var b3 = WallBox(h0, h1, HS, HC + 0.2f, out var s3); _boxes.Add((b3, s3));
            }
        }

        void InstantiatePortals(Transform root)
        {
            foreach (var p in _portalViews)
                Interaction.CrawlPortalView.Create(root, p);
        }
    }
}
