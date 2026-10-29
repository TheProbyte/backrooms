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
    ///  - Carpet: darker along the bottoms of the walls, a trodden patch at each doorway, worn paths between a room's
    ///    doorways (Backrooms/FloorWear, a multiplied overlay, one mesh a chunk), the edge come off its tack strip along
    ///    some walls (a frayed lip), and ripples near walls where it has come loose.
    /// The smaller things (ceiling devices, damaged tiles, wall and door marks, spills, thresholds) are in .Details.cs.
    /// Costs almost nothing: some hundreds of triangles a chunk and one cheap overlay pass on the floor.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        enum TileKind { Plain, Stained, Yellowed, Sag, Tilted, Shifted, Missing, Cracked, Crumbled, Supply, Return }

        /// <summary>Kind of fixture in each cell that has one (0 square, 1 dome, 2 strip): tiles there are left alone.</summary>
        readonly Dictionary<Vector2Int, int> _fixtureKinds = new Dictionary<Vector2Int, int>();
        /// <summary>Missing tiles (centre, chunk-local), to lay one on the floor below if there is room.</summary>
        readonly List<(Vector3 c, int x, int z)> _missingTiles = new List<(Vector3, int, int)>();
        /// <summary>Gaps in the ceiling (a tile missing, slid or pushed up): centre (chunk-local, at the ceiling), height, mask, room.</summary>
        readonly List<(Vector3 c, float h, uint m, Room r)> _gaps = new List<(Vector3, float, uint, Room)>();
        static Material s_wearMat;

        const float TileSize = 0.6f, TileThick = 0.014f;

        void ClearWear()
        {
            _floorWear.Clear(); _wallWear.Clear(); _crumbs.Clear();
            _missingTiles.Clear(); _gaps.Clear();
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
                else if (u < (t += 0.006f)) k = TileKind.Crumbled;
                else if (u < (t += 0.01f)) k = TileKind.Cracked;
                else if (u < (t += 0.012f * wet)) k = TileKind.Sag;
                else if (u < (t += 0.035f * wet)) k = TileKind.Stained;
                else if (u < (t += 0.03f)) k = TileKind.Yellowed;
                else if (u < (t += 0.03f)) k = TileKind.Supply;
                else if (u < (t += 0.018f)) k = TileKind.Return;
                kinds[i] = k;
                any |= k != TileKind.Plain;
            }
            {
                float dx0 = x * C, dz0 = z * C + OfficeBiome.TileGridOffset;
                for (int i = 0; i < 2; i++)
                    if (!(lit && i == 0) && (kinds[i] == TileKind.Plain || kinds[i] == TileKind.Stained || kinds[i] == TileKind.Yellowed || kinds[i] == TileKind.Cracked))
                        CeilingDevices(x, z, kinds[i] == TileKind.Cracked ? h - 0.006f : h, m, dx0 + i * TileSize, dz0, i);
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
                        _gaps.Add((centre, h, m, r));
                        TiltTile(WMask(Dice(gx, gz, 130 + i) < 0.4f ? stained : ceil, m), centre, h, Dice(gx, gz, 150 + i), 4f + 6f * Dice(gx, gz, 160 + i));
                        break;
                    case TileKind.Shifted:
                        _gaps.Add((centre, h, m, r));
                        ShiftTile(WMask(ceil, m), centre, h, Dice(gx, gz, 150 + i), Dice(gx, gz, 160 + i), Dice(gx, gz, 170 + i));
                        break;
                    case TileKind.Missing:
                        _gaps.Add((centre, h, m, r));
                        if (Dice(gx, gz, 180 + i) < 0.6f) _missingTiles.Add((new Vector3(centre.x, 0f, centre.z), x, z));
                        break;
                    case TileKind.Cracked:
                        SagTile(WMask(MaterialMaps.CeilingCracked ? MaterialMaps.CeilingCracked : ceil, m), tx0, zm0, h, 0.003f + 0.006f * Dice(gx, gz, 140 + i));
                        break;
                    case TileKind.Crumbled:
                    {
                        _gaps.Add((centre, h, m, r));
                        int corner = (int)(Dice(gx, gz, 190 + i) * 4f) & 3;
                        float a = 0.08f + 0.14f * Dice(gx, gz, 191 + i), b = 0.08f + 0.14f * Dice(gx, gz, 192 + i);
                        CrumbledTile(WMask(ceil, m), WMask(MaterialMaps.TileBack ? MaterialMaps.TileBack : ceil, m), tx0, zm0, h, corner, a, b, gx * 977 + gz * 131 + i);
                        var k = new Vector3(corner == 1 || corner == 2 ? tx1 : tx0, 0f, corner >= 2 ? zm1 : zm0);
                        _crumbs.Add((Vector3.Lerp(k, new Vector3(centre.x, 0f, centre.z), 0.25f), x, z));
                        break;
                    }
                    case TileKind.Supply: SupplyDiffuser(m, tx0, zm0, h); break;
                    case TileKind.Return: ReturnGrille(m, tx0, zm0, h, Dice(gx, gz, 195 + i)); break;
                }
            }
            return true;
        }

        static void CeilQuad(MeshWriter w, float ax0, float az0, float ax1, float az1, float h) =>
            w.Quad(new Vector3(ax0, h, az0), new Vector3(ax1, h, az0), new Vector3(ax1, h, az1), new Vector3(ax0, h, az1), Vector3.down, MeshWriter.CeilingUV);

        // ---------------- Above the ceiling ----------------

        const float PlenumDepth = 0.6f, JoistDepth = 0.235f, JoistWidth = 0.045f, JoistSpacing = 0.4f;

        /// <summary>
        /// The space above the ceiling, built only round its gaps (gaps of one room near each other share one): the backs of
        /// the tiles round the gap, hanger wires down to the grid, old joists every 40 cm under the boards of the floor above,
        /// dusty fibreglass in some of the bays, often a pipe or conduit across the joists, sometimes a sagging foil duct, a
        /// cable or two; closed by dark walls past where it can be seen. Everything is placed in world coordinates, so two
        /// gaps a few tiles apart look into the same structure. Lit by the world's light as the ceiling is: dark and dim,
        /// not black.
        /// </summary>
        void BuildPlenums()
        {
            if (_gaps.Count == 0 || !MaterialMaps.PlenumWood) return;
            var done = new bool[_gaps.Count];
            var holes = new List<Vector3>();
            for (int i = 0; i < _gaps.Count; i++)
            {
                if (done[i]) continue;
                var (c0, h, m, r) = _gaps[i];
                holes.Clear(); holes.Add(c0); done[i] = true;
                for (int j = i + 1; j < _gaps.Count; j++)
                    if (!done[j] && _gaps[j].r == r && Mathf.Abs(_gaps[j].h - h) < 0.01f && (_gaps[j].c - c0).sqrMagnitude < 2.4f * 2.4f)
                    { holes.Add(_gaps[j].c); done[j] = true; }
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                foreach (var c in holes) { x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x); z0 = Mathf.Min(z0, c.z); z1 = Mathf.Max(z1, c.z); }
                const float reach = 0.3f + 0.9f;
                x0 = Mathf.Max(x0 - reach, r.rect.xMin * C + T * 0.5f); x1 = Mathf.Min(x1 + reach, r.rect.xMax * C - T * 0.5f);
                z0 = Mathf.Max(z0 - reach, r.rect.yMin * C + T * 0.5f); z1 = Mathf.Min(z1 + reach, r.rect.yMax * C - T * 0.5f);
                if (x1 - x0 < 0.6f || z1 - z0 < 0.6f) continue;
                PlenumSpace(holes, x0, x1, z0, z1, Mathf.Min(h + PlenumDepth, _L.storeyPitch - 0.3f), h, m);
            }
        }

        void PlenumSpace(List<Vector3> holes, float x0, float x1, float z0, float z1, float top, float h, uint m)
        {
            var w0 = _origin + holes[0];
            int sx = Mathf.FloorToInt(w0.x / 2.4f), sz = Mathf.FloorToInt(w0.z / 2.4f);
            float D(int salt) => Dice(sx, sz, salt);
            float jb = top - JoistDepth;                                   // the joists' undersides
            var dark = WMask(MaterialMaps.Plenum, m);
            var wood = WMask(MaterialMaps.PlenumWood, m);
            var metal = WMask(MaterialMaps.PlenumMetal, m);
            var MU = (MeshWriter.UVProjector)MeshWriter.MetreUV;

            // Closed past where it can be seen, and the boards of the floor above.
            dark.Quad(new Vector3(x0, h, z0), new Vector3(x0, top, z0), new Vector3(x0, top, z1), new Vector3(x0, h, z1), Vector3.right, MU);
            dark.Quad(new Vector3(x1, h, z0), new Vector3(x1, top, z0), new Vector3(x1, top, z1), new Vector3(x1, h, z1), Vector3.left, MU);
            dark.Quad(new Vector3(x0, h, z0), new Vector3(x1, h, z0), new Vector3(x1, top, z0), new Vector3(x0, top, z0), Vector3.forward, MU);
            dark.Quad(new Vector3(x0, h, z1), new Vector3(x1, h, z1), new Vector3(x1, top, z1), new Vector3(x0, top, z1), Vector3.back, MU);
            WMask(MaterialMaps.PlenumDeck, m).Quad(new Vector3(x0, top, z0), new Vector3(x1, top, z0), new Vector3(x1, top, z1), new Vector3(x0, top, z1), Vector3.down, MU);

            // Joists, world-aligned; along x or z.
            bool alongX = D(600) < 0.5f;
            float a0 = alongX ? z0 : x0, a1 = alongX ? z1 : x1;           // across the joists
            float l0 = alongX ? x0 : z0, l1 = alongX ? x1 : z1;           // along them
            float off = alongX ? _origin.z : _origin.x;
            Vector3 P(float across, float y, float along) => alongX ? new Vector3(along, y, across) : new Vector3(across, y, along);
            Vector3 S(float across, float y, float along) => alongX ? new Vector3(along, y, across) : new Vector3(across, y, along);
            float first = Mathf.Ceil((a0 + off) / JoistSpacing) * JoistSpacing - off;
            for (float j = first; j <= a1; j += JoistSpacing)
                wood.Box(P(j, jb + JoistDepth * 0.5f, (l0 + l1) * 0.5f), S(JoistWidth, JoistDepth, l1 - l0), 1 | 2 | 4 | 16 | 32, MU);

            // Fibreglass in some bays, lumpy, here and there sagging below the joists.
            var batt = WMask(MaterialMaps.PlenumBatt, m);
            for (float j = first - JoistSpacing; j <= a1; j += JoistSpacing)
            {
                float b0 = Mathf.Max(a0, j + JoistWidth * 0.5f + 0.004f), b1 = Mathf.Min(a1, j + JoistSpacing - JoistWidth * 0.5f - 0.004f);
                if (b1 - b0 < 0.05f) continue;
                int bay = Mathf.RoundToInt((j + off) / JoistSpacing);
                if (Dice(bay, alongX ? 1 : 2, 610 + sx * 7 + sz) > 0.6f) continue;
                int k = 0;
                for (float t = l0; t < l1 - 0.05f; k++)
                {
                    float len = 0.4f + 0.4f * Dice(bay, k, 620), thick = 0.1f + 0.06f * Dice(bay, k, 621);
                    float sag = Dice(bay, k, 622) < 0.3f ? 0.02f + 0.05f * Dice(bay, k, 623) : 0.01f * Dice(bay, k, 623);
                    float t1 = Mathf.Min(l1, t + len);
                    float bottom = jb + 0.02f - sag;
                    batt.Box(P((b0 + b1) * 0.5f, bottom + thick * 0.5f, (t + t1) * 0.5f), S(b1 - b0, thick, t1 - t - 0.01f), 1 | 2 | 4 | 16 | 32, MU);
                    t = t1;
                }
            }

            // A pipe or conduit hung across the joists.
            if (D(630) < 0.75f)
            {
                float rad = D(631) < 0.5f ? 0.012f : 0.022f + 0.012f * D(632);
                float at = Mathf.Lerp(l0, l1, 0.25f + 0.5f * D(633));
                float y = jb - 0.04f - rad;
                Tube(metal, P(a0, y, at), P(a1, y, at), rad, 8, 0f);
            }
            // A foil flex duct along the joists, sagging between hangers.
            if (D(640) < 0.35f)
            {
                float at = Mathf.Lerp(a0, a1, 0.2f + 0.6f * D(641));
                Tube(WMask(MaterialMaps.PlenumDuct, m), P(at, jb - 0.12f, l0), P(at, jb - 0.12f, l1), 0.1f, 12, 0.04f);
            }
            // A cable or two, loose.
            int cables = D(650) < 0.5f ? 1 : 2;
            for (int c = 0; c < cables; c++)
            {
                float u0 = Mathf.Lerp(l0, l1, D(651 + c)), u1 = Mathf.Lerp(l0, l1, D(653 + c));
                Tube(metal, P(a0, jb - 0.015f, u0), P(a1, jb - 0.015f, u1), 0.005f, 5, 0.05f);
            }
            // Hanger wires from the joists to the grid's main runners (every 1.2 m along them).
            float runner0 = Mathf.Ceil((z0 + _origin.z - OfficeBiome.TileGridOffset) / 1.2f) * 1.2f + OfficeBiome.TileGridOffset - _origin.z;
            for (float wz = runner0; wz <= z1; wz += 1.2f)
            for (float wx = Mathf.Ceil((x0 + _origin.x) / 1.2f) * 1.2f - _origin.x + 0.6f; wx <= x1; wx += 1.2f)
                metal.Box(new Vector3(wx, (h + jb) * 0.5f, wz), new Vector3(0.003f, jb - h, 0.003f), 1 | 2 | 16 | 32, MU);

            // The backs of the tiles round the gaps.
            var back = WMask(MaterialMaps.TileBack, m);
            float ty = h + TileThick;
            for (float tx = Mathf.Floor((x0 + _origin.x) / TileSize) * TileSize - _origin.x; tx < x1; tx += TileSize)
            for (float tz = Mathf.Floor((z0 + _origin.z - OfficeBiome.TileGridOffset) / TileSize) * TileSize + OfficeBiome.TileGridOffset - _origin.z; tz < z1; tz += TileSize)
            {
                float cx = tx + TileSize * 0.5f, cz = tz + TileSize * 0.5f;
                bool gap = false;
                foreach (var hc in holes) if (Mathf.Abs(hc.x - cx) < 0.29f && Mathf.Abs(hc.z - cz) < 0.29f) { gap = true; break; }
                if (gap) continue;
                float qx0 = Mathf.Max(tx, x0), qx1 = Mathf.Min(tx + TileSize, x1), qz0 = Mathf.Max(tz, z0), qz1 = Mathf.Min(tz + TileSize, z1);
                if (qx1 - qx0 < 0.01f || qz1 - qz0 < 0.01f) continue;
                back.Quad(new Vector3(qx0, ty, qz0), new Vector3(qx1, ty, qz0), new Vector3(qx1, ty, qz1), new Vector3(qx0, ty, qz1), Vector3.up, MU);
            }
        }

        /// <summary>A round tube from a to b (sagging by sag in the middle: five pieces), its sides only.</summary>
        static void Tube(MeshWriter w, Vector3 a, Vector3 b, float r, int sides, float sag)
        {
            int pieces = sag > 0f ? 5 : 1;
            for (int p = 0; p < pieces; p++)
            {
                float t0 = p / (float)pieces, t1 = (p + 1) / (float)pieces;
                Vector3 p0 = Vector3.Lerp(a, b, t0) + Vector3.down * sag * 4f * t0 * (1f - t0);
                Vector3 p1 = Vector3.Lerp(a, b, t1) + Vector3.down * sag * 4f * t1 * (1f - t1);
                Vector3 dir = (p1 - p0).normalized;
                Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up).normalized, v = Vector3.Cross(dir, u);
                for (int k = 0; k < sides; k++)
                {
                    float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides, am = (a0 + a1) * 0.5f;
                    Vector3 o0 = (u * Mathf.Cos(a0) + v * Mathf.Sin(a0)) * r, o1 = (u * Mathf.Cos(a1) + v * Mathf.Sin(a1)) * r;
                    w.Quad(p0 + o0, p1 + o0, p1 + o1, p0 + o1, u * Mathf.Cos(am) + v * Mathf.Sin(am), MeshWriter.MetreUV);
                }
            }
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
            BuildPlenums();
            FallenTiles();
            Crumbs();
            var exits = new Dictionary<int, List<Vector3>>();
            int N = _L.N;
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                if (_L.Has(x, z, CellFlags.NoFloor)) continue;
                if (!_L.Plain(x, z)) continue;            // (wear is laid at the storey's floor: not in sunken or pit rooms)
                var r = _L.RoomAt(x, z);
                if (r.kind == RoomKind.Crawl) continue;
                bool carpet = Carpeted(r);
                uint m = CellMask(x, z);
                if (carpet) CoffeeSpots(x, z);
                var centre = new Vector3((x + 0.5f) * C, 0f, (z + 0.5f) * C);
                for (int side = 0; side < 4; side++)
                {
                    var e = _L.EdgeOf(x, z, side);
                    Vector3 outDir = side == 0 ? Vector3.left : side == 1 ? Vector3.right : side == 2 ? Vector3.back : Vector3.forward;
                    Vector3 along = side < 2 ? Vector3.forward : Vector3.right;
                    Vector3 edge = centre + outDir * (C * 0.5f);
                    int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                    bool inChunk = _L.InChunk(nx, nz);
                    EdgeOfSide(x, z, side, out bool vert, out int line, out int alongI);
                    if (OfficeLayout.IsSolid(e))
                    {
                        if (!Broken(vert, line, alongI, out _) && !HoleAt(vert, line, alongI, out _))
                            WallDetails(x, z, side, edge - outDir * (T * 0.5f), along, -outDir, r, m, _L.WallTop(r));
                        if (!carpet) continue;
                        WallGrime(x, z, side, edge, outDir, along, r);
                        float loose = Dice(GX(x), GZ(z), 300 + side);
                        if (loose < 0.06f) WallLift(x, z, side, edge, outDir, along, r, m);
                        else if (loose < 0.095f) Ripple(x, z, side, edge, outDir, along, r, m);
                        continue;
                    }
                    Threshold(x, z, side, edge, along, r);
                    // Only over a flat header with wall above it: a plain Door edge is open to the ceiling and an arch's
                    // curve reaches the sign's height, so a sign there would hang in the air.
                    bool headed = false;
                    if (e == EdgeKind.DoorHeader && _L.WallTop(r) > OfficeBiome.DoorHeaderHeight + 0.35f)
                    {
                        var view = ViewOpening(_L, Neighbour, vert, line, alongI, side == 1 || side == 3 ? 0 : 1);
                        headed = !view.arched && view.top <= OfficeBiome.DoorHeaderHeight + 0.01f && !(Pool && _L.ArchSpan(vert, line, alongI, out _, out _));
                    }
                    if (headed)
                    {
                        // The odd exit sign over a doorway, on one side (the same dice from both rooms).
                        int ex = GX(side == 1 ? x + 1 : x), ez = GZ(side == 3 ? z + 1 : z);
                        if (Dice(ex, ez, vert ? 720 : 721) < 0.06f && (Dice(ex, ez, vert ? 722 : 723) < 0.5f) == (side == 0 || side == 2))
                            ExitSign(edge, outDir, m);
                    }
                    if (!carpet) continue;
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

        /// <summary>A doorway: a trodden patch just inside (feet turn and stop here). No line across it: one at every doorway
        /// read as the edge of something (a chunk), not of carpet.</summary>
        void Doorway(int x, int z, int side, EdgeKind e, Vector3 edge, Vector3 outDir, Vector3 along, Room r, uint m)
        {
            Vector3 c = edge - outDir * 0.3f;
            WearQuad(c - along * 0.55f, c + along * 0.55f, -outDir, 0.42f, new Color32(214, 206, 194, 190), new Vector2(0.35f, 0.3f), -1f, 1f, -1f, 1f);
        }

        /// <summary>
        /// Carpet come away from the wall (off its tack strip): along part of a wall its edge has lifted a few millimetres a
        /// centimetre or two short of the baseboard, threads hanging into the dark gap it left.
        /// </summary>
        void WallLift(int x, int z, int side, Vector3 edge, Vector3 outDir, Vector3 along, Room r, uint m)
        {
            int gx = GX(x), gz = GZ(z);
            float len = 0.3f + 0.6f * Dice(gx, gz, 360 + side);
            float s0 = -C * 0.5f + 0.08f + (C - 0.16f - len) * Dice(gx, gz, 361 + side), s1 = s0 + len;
            float face = T * 0.5f + (NoBaseboard ? 0f : BD), gap = 0.008f + 0.017f * Dice(gx, gz, 362 + side);
            float lift = 0.005f + 0.007f * Dice(gx, gz, 363 + side);
            Vector3 lip = edge - outDir * (face + gap);
            CarpetLip(WMask(_a.FloorMaterial(r), m), lip, outDir, along, s0, s1, lift, gx * 131 + gz * 7 + side);
            // The gap: the bare floor and the tack strip, dark.
            Vector3 g = edge - outDir * face;
            WearQuad(g + along * s0, g + along * s1, -outDir, gap + 0.004f, new Color32(92, 84, 76, 235), new Vector2(0.8f, 0.7f), -1f, 1f, 0f, 1f);
        }

        /// <summary>
        /// The carpet's edge come up: a wedge rising to the lip, the cut edge, and a fringe of loose threads hanging off it
        /// (outDir: the way the edge faces).
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
            _floorWear.Quad(p0, p1, p2, p3, new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0), col, inner);
        }

        /// <summary>A missing tile lying on the floor under its gap, if nothing stands there.</summary>
        void FallenTiles()
        {
            foreach (var (c, x, z) in _missingTiles)
            {
                if (_L.Has(x, z, CellFlags.NoFloor) || !_L.Plain(x, z) || !Clear(c, 0.48f)) continue;
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

        /// <summary>The overlays as meshes (main thread): the floor's, the walls'.</summary>
        void MakeWear(Transform root, GeneratedChunkResources resources)
        {
            MakeOverlay(root, resources, _floorWear, "FloorWear");
            MakeOverlay(root, resources, _wallWear, "WallWear");
        }
    }
}
