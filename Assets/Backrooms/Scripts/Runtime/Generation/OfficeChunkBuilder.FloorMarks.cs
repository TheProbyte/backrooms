using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Floors (Docs/RealismPlan.md, section 5), seeded like the wear:
    ///  - Where a desk or a cabinet stood, now gone: four dents in the pile and a line of dirt round where it was.
    ///  - Some rooms laid in 60 cm carpet tiles: each a shade lighter or darker (laid quarter-turned, as they are), their
    ///    seams, the odd one replaced in another colour or missing (the concrete and the old glue showing).
    ///  - More stains: a rust ring where something metal stood, a dark patch by a door, very rarely a cigarette burn.
    ///  - Hard floors scratched where chairs and feet go.
    /// Marks are floor-wear overlay quads; the replaced and missing tiles are a few triangles in the floor's materials.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        const float CarpetTile = 0.6f;

        /// <summary>Is this room laid in carpet tiles? Decided for the room as a whole (its id in the district), so every chunk agrees.</summary>
        bool CarpetTiled(Room r)
        {
            var d = WorldGenerator.DistrictOf(_L.coord);
            return r.kind != RoomKind.Hallway && Dice(r.globalId, d.x * 7919 + d.y, 1430) < DetailRates.CarpetTileRooms;
        }

        /// <summary>The floor of one plain cell (from BuildWear, after the props, so Clear knows the furniture).</summary>
        void FloorMarks(int x, int z, Room r, bool carpet, uint m)
        {
            int gx = GX(x), gz = GZ(z);
            Vector3 P(float fx, float fz) => new Vector3((x + fx) * C, 0f, (z + fz) * C);
            if (carpet)
            {
                if (CarpetTiled(r)) { CarpetTiles(x, z, r, m); if (((GX(x) + GZ(z)) & 3) == 0) Saw(P(0.5f, 0.5f), Vector3.up, "carpet_tiles"); }
                float fm = DetailRates.FloorMarks;
                if (Dice(gx, gz, 1400) < 0.014f * fm) FurnitureGhost(x, z, r);
                if (Dice(gx, gz, 1410) < 0.006f * fm)
                {
                    var p = P(0.2f + 0.6f * Dice(gx, gz, 1411), 0.2f + 0.6f * Dice(gx, gz, 1412));
                    Spot(p, 0.035f + 0.035f * Dice(gx, gz, 1413), new Color32(150, 104, 66, 190), 0.85f);
                    Saw(p, Vector3.up, "rust");
                }
                if (Dice(gx, gz, 1420) < 0.003f * fm)
                {
                    var p = P(0.2f + 0.6f * Dice(gx, gz, 1421), 0.2f + 0.6f * Dice(gx, gz, 1422));
                    Spot(p, 0.022f, new Color32(126, 96, 66, 130), 0.3f);
                    Spot(p, 0.007f, new Color32(36, 30, 26, 235), 0f);
                    Saw(p, Vector3.up, "burn");
                }
                return;
            }
            // Hard floors: fine scratches, mostly along the room's long side (chairs pushed back, feet).
            if (_a.FloorMaterial(r) == MaterialMaps.Concrete || Dice(gx, gz, 1460) > 0.3f * DetailRates.FloorMarks) return;
            bool longX = r.rect.width >= r.rect.height;
            int n = 2 + (int)(Dice(gx, gz, 1461) * 5f);
            for (int k = 0; k < n; k++)
            {
                float ang = (longX ? 0f : 90f) + (Dice(gx, gz, 1462 + k) - 0.5f) * 50f;
                Vector3 a = Quaternion.Euler(0f, ang, 0f) * Vector3.right, b = Vector3.Cross(Vector3.up, a);
                var c = P(0.15f + 0.7f * Dice(gx, gz, 1470 + k), 0.15f + 0.7f * Dice(gx, gz, 1480 + k));
                c.y = 0.0015f;
                _floorWear.Rect(c, a, b, 0.04f + 0.14f * Dice(gx, gz, 1490 + k), 0.0008f + 0.0007f * Dice(gx, gz, 1500 + k),
                                new Color32(112, 106, 98, (byte)(70 + 60 * Dice(gx, gz, 1510 + k))), new Vector2(0.7f, 0f));
            }
        }

        /// <summary>A cell's four carpet tiles: tone and seams, the odd one replaced or missing.</summary>
        void CarpetTiles(int x, int z, Room r, uint m)
        {
            var floor = _a.FloorMaterial(r);
            var odd = floor == _a.referenceCarpet ? MaterialMaps.CarpetOddRef : MaterialMaps.CarpetOdd;
            for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
            {
                int tx = GX(x) * 2 + i, tz = GZ(z) * 2 + j;
                var c = new Vector3((x + 0.25f + 0.5f * i) * C, 0.0015f, (z + 0.25f + 0.5f * j) * C);
                const float h = CarpetTile * 0.5f;
                float u = Dice(tx, tz, 1440);
                if (u < 0.004f && MaterialMaps.Concrete) { MissingCarpetTile(c, floor, m, tx * 13 + tz); Saw(c, Vector3.up, "carpet_missing"); continue; }
                if (u < 0.012f && odd)
                {
                    // Replaced from another lot, its pile turned the other way.
                    MeshWriter.UVProjector turned = (w, nn) => new Vector2(w.z / 3f, w.x / 3f);
                    float y = 0.0009f;
                    Saw(c, Vector3.up, "carpet_odd");
                    WMask(odd, m).Quad(new Vector3(c.x - h, y, c.z - h), new Vector3(c.x + h, y, c.z - h), new Vector3(c.x + h, y, c.z + h), new Vector3(c.x - h, y, c.z + h), Vector3.up, turned);
                }
                // Laid quarter-turned, so neighbours catch the light differently: a checker of two shades, each a little off.
                float tone = (((tx + tz) & 1) == 0 ? 0.05f : 0.2f) + 0.09f * Dice(tx, tz, 1441);
                _floorWear.Rect(c, Vector3.right, Vector3.forward, h, h, new Color32(136, 128, 116, (byte)(tone * 255f)), new Vector2(2.985f, 0.985f));
                // Seams on its low sides (the next tile draws the others).
                var seam = new Color32(70, 64, 56, 165);
                _floorWear.Rect(c - Vector3.right * h, Vector3.right, Vector3.forward, 0.0022f, h, seam, new Vector2(0f, 0.98f));
                _floorWear.Rect(c - Vector3.forward * h, Vector3.right, Vector3.forward, h, 0.0022f, seam, new Vector2(0.98f, 0f));
            }
        }

        /// <summary>A carpet tile gone: the concrete under it with the old glue in trowel lines, the cut edges of its neighbours.</summary>
        void MissingCarpetTile(Vector3 c, Material floor, uint m, int seed)
        {
            const float h = CarpetTile * 0.5f, y = 0.003f, lip = 0.008f, rim = 0.012f;
            MeshWriter.UVProjector flat = (w, nn) => new Vector2(w.x / 3f, w.z / 3f);
            WMask(MaterialMaps.Concrete, m).Quad(new Vector3(c.x - h, y, c.z - h), new Vector3(c.x + h, y, c.z - h), new Vector3(c.x + h, y, c.z + h), new Vector3(c.x - h, y, c.z + h), Vector3.up, flat);
            var cw = WMask(floor, m);
            for (int s = 0; s < 4; s++)
            {
                var q = Quaternion.Euler(0f, 90f * s, 0f);
                Vector3 o = q * Vector3.forward, sd = q * Vector3.right;
                Vector3 e0 = c + o * h - sd * h, e1 = c + o * h + sd * h;
                e0.y = e1.y = 0f;
                // The neighbour's cut edge (facing into the hole) and its top along the edge.
                cw.Quad(e0 + Vector3.up * y, e1 + Vector3.up * y, e1 + Vector3.up * lip, e0 + Vector3.up * lip, -o, MeshWriter.FloorUV);
                cw.Quad(e0 + Vector3.up * lip, e1 + Vector3.up * lip, e1 + o * rim + Vector3.up * 0.0012f, e0 + o * rim + Vector3.up * 0.0012f, Vector3.up, MeshWriter.FloorUV);
            }
            var rng = new System.Random(seed);
            var glue = new Color32(176, 158, 112, 120);
            for (int k = 0; k < 5; k++)
            {
                float off = -h + 0.06f + k * (2f * h - 0.12f) / 4f + ((float)rng.NextDouble() - 0.5f) * 0.03f;
                _floorWear.Rect(new Vector3(c.x + off, 0.0015f + y, c.z), Vector3.right, Vector3.forward, 0.012f, h - 0.03f, glue, new Vector2(0.4f, 0.85f));
            }
        }

        /// <summary>Where a desk or a cabinet stood: four dents in the pile, a dirt line round its edge (the carpet under it kept clean).</summary>
        void FurnitureGhost(int x, int z, Room r)
        {
            int gx = GX(x), gz = GZ(z);
            bool alongX = Dice(gx, gz, 1401) < 0.5f;
            float hw = 0.3f + 0.32f * Dice(gx, gz, 1402), hd = 0.22f + 0.12f * Dice(gx, gz, 1403);
            var c = new Vector3((x + 0.35f + 0.3f * Dice(gx, gz, 1404)) * C, 0.0015f, (z + 0.35f + 0.3f * Dice(gx, gz, 1405)) * C);
            Vector3 a = alongX ? Vector3.right : Vector3.forward, b = alongX ? Vector3.forward : Vector3.right;
            if (!Clear(c, Mathf.Max(hw, hd) + 0.1f)) return;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 k = c + a * (sx * (hw + 0.08f)) + b * (sz * (hd + 0.08f));
                int cx = Mathf.FloorToInt(k.x / C), cz = Mathf.FloorToInt(k.z / C);
                if (!_L.InChunk(cx, cz) || _L.RoomAt(cx, cz) != r || !_L.Plain(cx, cz)) return;
            }
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Spot(c + a * (sx * (hw - 0.04f)) + b * (sz * (hd - 0.04f)), 0.013f + 0.008f * Dice(gx, gz, 1406 + sx + sz * 2), new Color32(108, 100, 90, 215), 0.45f);
            const float band = 0.06f;
            var col = new Color32(182, 174, 162, 125);
            Saw(c, Vector3.up, "furniture_ghost");
            _floorWear.Rect(c + b * (hd + band * 0.5f), a, b, hw + band, band * 0.5f, col, new Vector2(0.9f, 0f), -1f, 1f, 0f, 1f);
            _floorWear.Rect(c - b * (hd + band * 0.5f), a, b, hw + band, band * 0.5f, col, new Vector2(0.9f, 0f), -1f, 1f, 1f, 0f);
            _floorWear.Rect(c - a * (hw + band * 0.5f), a, b, band * 0.5f, hd, col, new Vector2(0f, 0.9f), 1f, 0f, -1f, 1f);
            _floorWear.Rect(c + a * (hw + band * 0.5f), a, b, band * 0.5f, hd, col, new Vector2(0f, 0.9f), 0f, 1f, -1f, 1f);
        }
    }
}
