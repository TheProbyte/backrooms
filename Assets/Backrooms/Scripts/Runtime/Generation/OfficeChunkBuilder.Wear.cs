using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>
    /// Wear and neglect (2026-10-27), all decided by the world seed and the cell, so a place looks the same each visit:
    ///  - Ceiling tiles: here and there one is stained (water rings, mould: MaterialMaps' stained copies), yellowed,
    ///    sagging, pushed up at one edge, slid off its grid, or missing (the dark space above shows; a missing one may lie
    ///    on the floor below). Stains cluster where the building leaks. Only whole tiles in plain cells, never by a light.
    ///  - Carpet: darker along the bottoms of the walls, a seam and a trodden patch at each doorway, worn paths between a
    ///    room's doorways (Backrooms/FloorWear, a multiplied overlay, one mesh a chunk), a few doorways where the carpet
    ///    edge has come up with a frayed lip, and ripples near walls where it has come loose.
    /// Costs almost nothing: some hundreds of triangles a chunk and one cheap overlay pass on the floor.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        enum TileKind { Plain, Stained, Yellowed, Sag, Tilted, Shifted, Missing }

        readonly List<Vector3> _wearV = new List<Vector3>();
        readonly List<Color32> _wearC = new List<Color32>();
        readonly List<Vector2> _wearUV = new List<Vector2>(), _wearIn = new List<Vector2>();
        readonly List<int> _wearI = new List<int>();
        /// <summary>Kind of fixture in each cell that has one (0 square, 1 dome, 2 strip): tiles there are left alone.</summary>
        readonly Dictionary<Vector2Int, int> _fixtureKinds = new Dictionary<Vector2Int, int>();
        /// <summary>Missing tiles (centre, chunk-local), to lay one on the floor below if there is room.</summary>
        readonly List<(Vector3 c, int x, int z)> _missingTiles = new List<(Vector3, int, int)>();
        static Material s_wearMat;

        const float TileSize = 0.6f, TileThick = 0.014f, PlenumDepth = 0.22f;

        void ClearWear()
        {
            _wearV.Clear(); _wearC.Clear(); _wearUV.Clear(); _wearIn.Clear(); _wearI.Clear();
            _missingTiles.Clear();
            _fixtureKinds.Clear();
            foreach (var f in _L.fixtures) _fixtureKinds[f.cell] = f.strip ? 2 : f.dome ? 1 : 0;
        }

        // ---------------- Dice ----------------

        int GX(int x) => _L.coord.x * _L.N + x;
        int GZ(int z) => _L.coord.y * _L.N + z;

        /// <summary>0..1 from the world seed, a world cell and a salt (the same in every run and from either chunk).</summary>
        float Dice(int gx, int gz, int salt)
        {
            unchecked
            {
                uint h = (uint)gx * 73856093u ^ (uint)gz * 19349663u ^ (uint)_L.storey * 83492791u ^ (uint)salt * 2654435761u
                         ^ (uint)Destruction.WallDamage.WorldSeed * 2246822519u;
                h = (h ^ (h >> 15)) * 2246822519u;
                h = (h ^ (h >> 13)) * 3266489917u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Smooth 0..1 noise over world cells (where it leaks, where people walk).</summary>
        float Field(float gx, float gz, int salt)
        {
            int ix = Mathf.FloorToInt(gx), iz = Mathf.FloorToInt(gz);
            float fx = gx - ix, fz = gz - iz;
            fx = fx * fx * (3f - 2f * fx); fz = fz * fz * (3f - 2f * fz);
            return Mathf.Lerp(Mathf.Lerp(Dice(ix, iz, salt), Dice(ix + 1, iz, salt), fx),
                              Mathf.Lerp(Dice(ix, iz + 1, salt), Dice(ix + 1, iz + 1, salt), fx), fz);
        }

        bool Carpeted(Room r)
        {
            var m = _a.FloorMaterial(r);
            return m && (m == _a.carpet || m == _a.referenceCarpet);
        }

        // ---------------- Ceiling tiles ----------------

        /// <summary>
        /// A cell's acoustic ceiling, if any of its two whole tiles (the middle row: tiles run 0.3 m off the cell's edges in
        /// z) is not plain. False: nothing drawn, the caller draws the plain quad.
        /// </summary>
        bool DressCeiling(int x, int z, Room r, float h, uint m, Material ceil)
        {
            if (ceil != _a.ceilingTile || r.Tall || r.openAbove || MaterialMaps.Plenum == null) return false;
            var cell = new Vector2Int(x, z);
            _fixtureKinds.TryGetValue(cell, out int fixture);
            bool lit = _fixtureKinds.ContainsKey(cell);
            if (lit && fixture != 0) return false;                       // a dome or a batten: leave the cell alone
            foreach (var p in _L.portals)
                if (p.space && (p.cell == cell || (x >= p.room.xMin - 1 && x <= p.room.xMax && z >= p.room.yMin - 1 && z <= p.room.yMax))) return false;

            int gx = GX(x), gz = GZ(z);
            float wet = 0.3f + 1.6f * Field(gx * 0.17f, gz * 0.17f, 901);
            var kinds = new TileKind[2];
            bool any = false;
            for (int i = 0; i < 2; i++)
            {
                if (lit && i == 0) continue;                              // the square panel sits in tile 0
                float u = Dice(gx, gz, 100 + i);
                float t = 0f;
                TileKind k = TileKind.Plain;
                if (u < (t += 0.005f)) k = TileKind.Missing;
                else if (u < (t += 0.012f)) k = TileKind.Tilted;
                else if (u < (t += 0.01f)) k = TileKind.Shifted;
                else if (u < (t += 0.012f * wet)) k = TileKind.Sag;
                else if (u < (t += 0.035f * wet)) k = TileKind.Stained;
                else if (u < (t += 0.03f)) k = TileKind.Yellowed;
                kinds[i] = k;
                any |= k != TileKind.Plain;
            }
            if (!any) return false;

            float x0 = x * C, z0 = z * C, x1 = x0 + C, z1 = z0 + C;
            float zm0 = z0 + OfficeBiome.TileGridOffset, zm1 = zm0 + TileSize;
            var plain = WMask(ceil, m);
            CeilQuad(plain, x0, z0, x1, zm0, h);                          // the half tiles either side in z
            CeilQuad(plain, x0, zm1, x1, z1, h);
            for (int i = 0; i < 2; i++)
            {
                float tx0 = x0 + i * TileSize, tx1 = tx0 + TileSize;
                var centre = new Vector3((tx0 + tx1) * 0.5f, h, (zm0 + zm1) * 0.5f);
                Material stained = Dice(gx, gz, 120 + i) < 0.5f ? MaterialMaps.CeilingStainedA : MaterialMaps.CeilingStainedB;
                if (!stained) stained = ceil;
                switch (kinds[i])
                {
                    case TileKind.Plain: CeilQuad(plain, tx0, zm0, tx1, zm1, h); break;
                    case TileKind.Stained: CeilQuad(WMask(stained, m), tx0, zm0, tx1, zm1, h); break;
                    case TileKind.Yellowed: CeilQuad(WMask(MaterialMaps.CeilingYellowed, m), tx0, zm0, tx1, zm1, h); break;
                    case TileKind.Sag:
                        SagTile(WMask(Dice(gx, gz, 130 + i) < 0.7f ? stained : ceil, m), tx0, zm0, h, 0.012f + 0.02f * Dice(gx, gz, 140 + i));
                        break;
                    case TileKind.Tilted:
                        PlenumBox(m, tx0, zm0, tx1, zm1, h);
                        TiltTile(WMask(Dice(gx, gz, 130 + i) < 0.4f ? stained : ceil, m), centre, h, Dice(gx, gz, 150 + i), 4f + 6f * Dice(gx, gz, 160 + i));
                        break;
                    case TileKind.Shifted:
                        PlenumBox(m, tx0, zm0, tx1, zm1, h);
                        ShiftTile(WMask(ceil, m), centre, h, Dice(gx, gz, 150 + i), Dice(gx, gz, 160 + i), Dice(gx, gz, 170 + i));
                        break;
                    case TileKind.Missing:
                        PlenumBox(m, tx0, zm0, tx1, zm1, h);
                        if (Dice(gx, gz, 180 + i) < 0.6f) _missingTiles.Add((new Vector3(centre.x, 0f, centre.z), x, z));
                        break;
                }
            }
            return true;
        }

        static void CeilQuad(MeshWriter w, float ax0, float az0, float ax1, float az1, float h) =>
            w.Quad(new Vector3(ax0, h, az0), new Vector3(ax1, h, az0), new Vector3(ax1, h, az1), new Vector3(ax0, h, az1), Vector3.down, MeshWriter.CeilingUV);

        /// <summary>The dark space above the ceiling over a tile (a little wider: seen past a lifted or shifted tile's edge).</summary>
        void PlenumBox(uint m, float tx0, float tz0, float tx1, float tz1, float h)
        {
            var w = WMask(MaterialMaps.Plenum, m);
            const float pad = 0.05f;
            float a0 = tx0 - pad, a1 = tx1 + pad, b0 = tz0 - pad, b1 = tz1 + pad, top = h + PlenumDepth;
            w.Quad(new Vector3(a0, top, b0), new Vector3(a1, top, b0), new Vector3(a1, top, b1), new Vector3(a0, top, b1), Vector3.down, MeshWriter.MetreUV);
            w.Quad(new Vector3(a0, h, b0), new Vector3(a0, top, b0), new Vector3(a0, top, b1), new Vector3(a0, h, b1), Vector3.right, MeshWriter.MetreUV);
            w.Quad(new Vector3(a1, h, b0), new Vector3(a1, top, b0), new Vector3(a1, top, b1), new Vector3(a1, h, b1), Vector3.left, MeshWriter.MetreUV);
            w.Quad(new Vector3(a0, h, b0), new Vector3(a1, h, b0), new Vector3(a1, top, b0), new Vector3(a0, top, b0), Vector3.forward, MeshWriter.MetreUV);
            w.Quad(new Vector3(a0, h, b1), new Vector3(a1, h, b1), new Vector3(a1, top, b1), new Vector3(a0, top, b1), Vector3.back, MeshWriter.MetreUV);
        }

        /// <summary>A tile sagging in the middle (wet, or old): its edges on the grid, its centre a couple of centimetres down.</summary>
        static void SagTile(MeshWriter w, float tx0, float tz0, float h, float sag)
        {
            const int n = 4;
            float step = TileSize / n;
            Vector3 P(int i, int j)
            {
                float fu = 2f * i / n - 1f, fv = 2f * j / n - 1f;
                return new Vector3(tx0 + i * step, h - sag * (1f - fu * fu) * (1f - fv * fv), tz0 + j * step);
            }
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                Vector3 a = P(i, j), b = P(i + 1, j), c = P(i + 1, j + 1), d = P(i, j + 1);
                Vector3 nrm = Vector3.Cross(c - a, b - d).normalized;
                if (nrm.y > 0f) nrm = -nrm;
                w.Quad(a, b, c, d, nrm, MeshWriter.CeilingUV);
            }
        }

        /// <summary>A tile pushed up at one edge (someone looked into the ceiling): hinged on the opposite edge, tilted in.</summary>
        static void TiltTile(MeshWriter w, Vector3 centre, float h, float side, float degrees)
        {
            int e = Mathf.Min(3, (int)(side * 4f));
            Vector3 toHinge = e == 0 ? Vector3.left : e == 1 ? Vector3.right : e == 2 ? Vector3.back : Vector3.forward;
            Vector3 hinge = new Vector3(centre.x, h, centre.z) + toHinge * (TileSize * 0.5f);
            Vector3 axis = Vector3.Cross(Vector3.up, toHinge);
            var q = Quaternion.AngleAxis(degrees, axis);
            if ((q * (-toHinge)).y < 0f) q = Quaternion.AngleAxis(-degrees, axis);       // the free edge goes up
            Vector3 c0 = new Vector3(centre.x, h + TileThick * 0.5f, centre.z);
            Vector3 c = hinge + q * (c0 - hinge);
            w.Box(c, new Vector3(TileSize - 0.004f, TileThick, TileSize - 0.004f), q, 4 | 1 | 2 | 16 | 32, MeshWriter.CeilingUV);
        }

        /// <summary>A tile slid a few centimetres off its grid and turned a little: a dark slot shows on one side.</summary>
        static void ShiftTile(MeshWriter w, Vector3 centre, float h, float a, float b, float c)
        {
            float d = 0.022f + 0.02f * b;
            Vector3 off = a < 0.25f ? Vector3.left : a < 0.5f ? Vector3.right : a < 0.75f ? Vector3.back : Vector3.forward;
            var q = Quaternion.Euler(0f, (c - 0.5f) * 4f, 0f);
            w.Box(new Vector3(centre.x, h + 0.004f + TileThick * 0.5f, centre.z) + off * d, new Vector3(TileSize - 0.004f, TileThick, TileSize - 0.004f), q,
                  4 | 1 | 2 | 16 | 32, MeshWriter.CeilingUV);
        }

        // ---------------- Floor ----------------

        /// <summary>After the props (a fallen tile needs a clear floor): the floor's wear, the carpet's edges, fallen tiles.</summary>
        void BuildWear()
        {
            if (Pool) return;
            FallenTiles();
            var exits = new Dictionary<int, List<Vector3>>();
            int N = _L.N;
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                if (_L.Has(x, z, CellFlags.NoFloor)) continue;
                var r = _L.RoomAt(x, z);
                if (r.kind == RoomKind.Crawl || !Carpeted(r)) continue;
                uint m = CellMask(x, z);
                var centre = new Vector3((x + 0.5f) * C, 0f, (z + 0.5f) * C);
                for (int side = 0; side < 4; side++)
                {
                    var e = _L.EdgeOf(x, z, side);
                    Vector3 outDir = side == 0 ? Vector3.left : side == 1 ? Vector3.right : side == 2 ? Vector3.back : Vector3.forward;
                    Vector3 along = side < 2 ? Vector3.forward : Vector3.right;
                    Vector3 edge = centre + outDir * (C * 0.5f);
                    int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                    bool inChunk = _L.InChunk(nx, nz);
                    if (OfficeLayout.IsSolid(e))
                    {
                        WallGrime(x, z, side, edge, outDir, along, r);
                        if (Dice(GX(x), GZ(z), 300 + side) < 0.035f) Ripple(x, z, side, edge, outDir, along, r, m);
                        continue;
                    }
                    bool otherRoom = inChunk ? _L.RoomAt(nx, nz) != r : e != EdgeKind.Open;
                    if (!otherRoom) continue;
                    // A doorway (not an open run between parts of one open plan) gets its seam and its worn patch.
                    if (e == EdgeKind.Door || e == EdgeKind.DoorHeader) Doorway(x, z, side, e, edge, outDir, along, r, m);
                    if (!exits.TryGetValue(r.id, out var list)) exits[r.id] = list = new List<Vector3>();
                    Vector3 p = edge - outDir * 0.35f;
                    bool near = false;
                    foreach (var q in list) if ((q - p).sqrMagnitude < 1.1f * 1.1f) { near = true; break; }
                    if (!near) list.Add(p);
                }
            }
            foreach (var kv in exits) Paths(kv.Value);
        }

        /// <summary>Grime along the bottom of a wall, darkest at the baseboard, fading where the wall run ends.</summary>
        void WallGrime(int x, int z, int side, Vector3 edge, Vector3 outDir, Vector3 along, Room r)
        {
            float face = T * 0.5f + (NoBaseboard ? 0f : BD);
            Vector3 w0 = edge - outDir * face;
            bool Continues(int dx, int dz)
            {
                int ax = x + dx, az = z + dz;
                return _L.InChunk(ax, az) && _L.RoomAt(ax, az) == r && OfficeLayout.IsSolid(_L.EdgeOf(ax, az, side));
            }
            int sx = side < 2 ? 0 : 1, sz = side < 2 ? 1 : 0;
            float u0 = Continues(-sx, -sz) ? 0f : -1f, u1 = Continues(sx, sz) ? 0f : 1f;
            float k = 0.6f + 0.4f * Dice(GX(x), GZ(z), 200 + side);
            var col = new Color32(176, 164, 148, (byte)(215 * k));
            WearQuad(w0 - along * (C * 0.5f), w0 + along * (C * 0.5f), -outDir, 0.24f, col, new Vector2(0.6f, 0f), u0, u1, 0f, 1f);
        }

        /// <summary>A doorway: a dark seam on the line, a trodden patch either side, and now and then a lifted, frayed edge.</summary>
        void Doorway(int x, int z, int side, EdgeKind e, Vector3 edge, Vector3 outDir, Vector3 along, Room r, uint m)
        {
            // Seam: a thin dark line (each side draws its half).
            WearQuad(edge - along * (C * 0.5f - T * 0.5f), edge + along * (C * 0.5f - T * 0.5f), -outDir, 0.012f,
                     new Color32(120, 108, 96, 200), new Vector2(0.92f, 0f), -1f, 1f, 0f, 1f);
            // Worn just inside: feet turn and stop here.
            Vector3 c = edge - outDir * 0.3f;
            WearQuad(c - along * 0.55f, c + along * 0.55f, -outDir, 0.42f, new Color32(214, 206, 194, 190), new Vector2(0.35f, 0.3f), -1f, 1f, -1f, 1f);

            // The same dice from both rooms: the edge's own cell coordinates (its lower-left cell) and orientation.
            bool vertical = side < 2;
            int ex = GX(side == 1 ? x + 1 : x), ez = GZ(side == 3 ? z + 1 : z);
            if (Dice(ex, ez, vertical ? 400 : 401) > 0.35f) return;
            bool liftHere = (Dice(ex, ez, vertical ? 402 : 403) < 0.5f) == (side == 0 || side == 2);
            if (!liftHere) return;
            float half = e == EdgeKind.Door ? 0.4f : C * 0.5f - T * 0.5f - 0.05f;
            float a = Dice(ex, ez, 404), b = Dice(ex, ez, 405);
            float s0 = -half + a * half * 0.6f, s1 = half - b * half * 0.6f;
            float lift = e == EdgeKind.Door ? 0.003f + 0.003f * Dice(ex, ez, 406) : 0.005f + 0.005f * Dice(ex, ez, 406);
            CarpetLip(WMask(_a.FloorMaterial(r), m), edge, outDir, along, s0, s1, lift, ex * 31 + ez);
        }

        /// <summary>
        /// The carpet's edge come up at a seam: a wedge rising to the lip, the cut edge, and a fringe of loose threads
        /// lying over the line.
        /// </summary>
        static void CarpetLip(MeshWriter w, Vector3 edge, Vector3 outDir, Vector3 along, float s0, float s1, float lift, int seed)
        {
            const float back = 0.08f, floorY = 0.0006f;
            Vector3 inward = -outDir;
            Vector3 A0 = edge + along * s0, A1 = edge + along * s1;
            Vector3 lip0 = A0 + Vector3.up * lift, lip1 = A1 + Vector3.up * lift;
            Vector3 back0 = A0 + inward * back + Vector3.up * floorY, back1 = A1 + inward * back + Vector3.up * floorY;
            Vector3 up = Vector3.Cross(lip1 - lip0, back0 - lip0).normalized;
            if (up.y < 0f) up = -up;
            w.Quad(lip0, lip1, back1, back0, up, MeshWriter.FloorUV);                                       // the lifted carpet
            w.Quad(A0 + Vector3.up * floorY, A1 + Vector3.up * floorY, lip1, lip0, outDir, MeshWriter.FloorUV); // its cut edge
            w.Quad(A0 + Vector3.up * floorY, lip0, back0, back0, along * -1f, MeshWriter.FloorUV);           // the ends
            w.Quad(A1 + Vector3.up * floorY, lip1, back1, back1, along, MeshWriter.FloorUV);
            // Fringe: loose threads over the line, every 6-9 mm, 8-20 mm long, lying down.
            var rng = new System.Random(seed);
            for (float s = s0 + 0.004f; s < s1 - 0.004f; s += 0.006f + (float)rng.NextDouble() * 0.003f)
            {
                float len = 0.008f + (float)rng.NextDouble() * 0.012f, wid = 0.002f + (float)rng.NextDouble() * 0.0018f;
                float skew = ((float)rng.NextDouble() - 0.5f) * 0.006f;
                Vector3 r0 = edge + along * s + Vector3.up * lift * 0.9f, r1 = r0 + along * wid;
                Vector3 tip = edge + along * (s + wid * 0.5f + skew) + outDir * len + Vector3.up * (floorY + 0.0008f);
                w.Quad(r0, r1, tip, tip, Vector3.up, MeshWriter.FloorUV);
            }
        }

        /// <summary>Carpet come loose near a wall: a low ripple along it, rising and falling over half a metre or so.</summary>
        void Ripple(int x, int z, int side, Vector3 edge, Vector3 outDir, Vector3 along, Room r, uint m)
        {
            int gx = GX(x), gz = GZ(z);
            float len = 0.35f + 0.45f * Dice(gx, gz, 310 + side);
            float start = -C * 0.5f + 0.1f + (C - 0.2f - len) * Dice(gx, gz, 320 + side);
            float off = T * 0.5f + BD + 0.02f + 0.05f * Dice(gx, gz, 330 + side);
            float width = 0.09f + 0.06f * Dice(gx, gz, 340 + side), height = 0.008f + 0.008f * Dice(gx, gz, 350 + side);
            var w = WMask(_a.FloorMaterial(r), m);
            Vector3 inward = -outDir;
            const int seg = 6; const float floorY = 0.0006f;
            for (int k = 0; k < seg; k++)
            {
                float t0 = k / (float)seg, t1 = (k + 1) / (float)seg;
                float h0 = floorY + height * Mathf.Sin(Mathf.PI * t0), h1 = floorY + height * Mathf.Sin(Mathf.PI * t1);
                Vector3 b0 = edge + along * (start + len * t0), b1 = edge + along * (start + len * t1);
                Vector3 n0 = b0 + inward * off + Vector3.up * floorY, n1 = b1 + inward * off + Vector3.up * floorY;
                Vector3 r0 = b0 + inward * (off + width * 0.5f) + Vector3.up * h0, r1 = b1 + inward * (off + width * 0.5f) + Vector3.up * h1;
                Vector3 f0 = b0 + inward * (off + width) + Vector3.up * floorY, f1 = b1 + inward * (off + width) + Vector3.up * floorY;
                Slope(w, n0, n1, r1, r0);
                Slope(w, r0, r1, f1, f0);
            }
        }

        static void Slope(MeshWriter w, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            if (n.y < 0f) n = -n;
            w.Quad(a, b, c, d, n, MeshWriter.FloorUV);
        }

        /// <summary>Trodden paths between a room's doorways (two: one path; more: each to the middle of them), faint and patchy.</summary>
        void Paths(List<Vector3> exits)
        {
            if (exits.Count == 0) return;
            var col = new Color32(222, 214, 202, 175);
            var inner = new Vector2(0.72f, 0.15f);
            if (exits.Count == 1) return;
            if (exits.Count == 2) { WearQuad(exits[0], exits[1], Vector3.zero, 0.36f, col, inner, -1f, 1f, -1f, 1f); return; }
            Vector3 hub = Vector3.zero;
            foreach (var p in exits) hub += p;
            hub /= exits.Count;
            foreach (var p in exits) if ((p - hub).sqrMagnitude > 0.5f * 0.5f) WearQuad(p, hub, Vector3.zero, 0.34f, col, inner, -1f, 1f, -1f, 1f);
        }

        /// <summary>
        /// One overlay quad from a to b (chunk-local, on the floor). across zero: centred on the line, half width w either
        /// side (v -1..1); else from the line toward across, w wide (v v0..v1). u runs u0..u1 from a to b.
        /// </summary>
        void WearQuad(Vector3 a, Vector3 b, Vector3 across, float w, Color32 col, Vector2 inner, float u0, float u1, float v0, float v1)
        {
            const float y = 0.0015f;
            a.y = b.y = y;
            Vector3 dir = b - a; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            Vector3 s = across == Vector3.zero ? Vector3.Cross(Vector3.up, dir.normalized) : across;
            Vector3 p0, p1, p2, p3;
            if (across == Vector3.zero) { p0 = a - s * w; p1 = a + s * w; p2 = b + s * w; p3 = b - s * w; }
            else { p0 = a; p1 = a + s * w; p2 = b + s * w; p3 = b; }
            int i = _wearV.Count;
            _wearV.Add(p0); _wearV.Add(p1); _wearV.Add(p2); _wearV.Add(p3);
            _wearUV.Add(new Vector2(u0, v0)); _wearUV.Add(new Vector2(u0, v1)); _wearUV.Add(new Vector2(u1, v1)); _wearUV.Add(new Vector2(u1, v0));
            for (int k = 0; k < 4; k++) { _wearC.Add(col); _wearIn.Add(inner); }
            _wearI.Add(i); _wearI.Add(i + 1); _wearI.Add(i + 2); _wearI.Add(i); _wearI.Add(i + 2); _wearI.Add(i + 3);
        }

        /// <summary>A missing tile lying on the floor under its gap, if nothing stands there.</summary>
        void FallenTiles()
        {
            foreach (var (c, x, z) in _missingTiles)
            {
                if (_L.Has(x, z, CellFlags.NoFloor) || !Clear(c, 0.48f)) continue;
                int gx = GX(x), gz = GZ(z);
                float yaw = Dice(gx, gz, 500) * 360f, tilt = Dice(gx, gz, 501) * 3f;
                var q = Quaternion.Euler(tilt, yaw, 0f);
                var at = c + new Vector3((Dice(gx, gz, 502) - 0.5f) * 0.3f, TileThick * 0.5f + Mathf.Sin(tilt * Mathf.Deg2Rad) * TileSize * 0.5f, (Dice(gx, gz, 503) - 0.5f) * 0.3f);
                // Its own face up: one tile of the ceiling texture (4 x 4 tiles in 2.4 m), turned with it.
                var inv = Quaternion.Inverse(q);
                var world = _origin + at;
                MeshWriter.UVProjector uv = (wp, n) => { var l = inv * (wp - world); return new Vector2(l.x / 2.4f + 0.125f, l.z / 2.4f + 0.125f); };
                WMask(_a.ceilingTile, CellMask(x, z)).Box(at, new Vector3(TileSize, TileThick, TileSize), q, 8 | 1 | 2 | 16 | 32, uv);
            }
        }

        /// <summary>No collider box (furniture, walls) within r of a point on the floor.</summary>
        bool Clear(Vector3 p, float r)
        {
            foreach (var (c, s) in _boxes)
            {
                if (c.y - s.y * 0.5f > 0.3f) continue;
                if (Mathf.Abs(p.x - c.x) < s.x * 0.5f + r && Mathf.Abs(p.z - c.z) < s.z * 0.5f + r) return false;
            }
            foreach (var b in _rotBoxes)
            {
                if (b.c.y - b.s.y * 0.5f > 0.3f) continue;
                float reach = Mathf.Max(b.s.x, b.s.z) * 0.5f + r;
                if (new Vector2(p.x - b.c.x, p.z - b.c.z).sqrMagnitude < reach * reach) return false;
            }
            return true;
        }

        /// <summary>The overlay as one mesh (main thread).</summary>
        void MakeWear(Transform root, GeneratedChunkResources resources)
        {
            if (_wearI.Count == 0) return;
            if (!s_wearMat)
            {
                var sh = Shader.Find("Backrooms/FloorWear");
                if (!sh) return;
                s_wearMat = new Material(sh) { name = "Floor wear", hideFlags = HideFlags.DontSave };
            }
            var mesh = new Mesh { name = "FloorWear" };
            mesh.SetVertices(_wearV); mesh.SetColors(_wearC); mesh.SetUVs(0, _wearUV); mesh.SetUVs(1, _wearIn);
            mesh.SetTriangles(_wearI, 0, true);
            resources.Own(mesh);
            var go = new GameObject("FloorWear");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = s_wearMat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.renderingLayerMask = 0xFFFFFFFFu;
        }
    }
}
