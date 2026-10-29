using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Ceilings (Docs/RealismPlan.md, section 4), seeded like the wear:
    ///  - Tiles from another batch: whole tiles a shade whiter or greyer than the rest, mostly in patches where a run was
    ///    replaced, now and then one alone.
    ///  - A tile with a round hole where a fitting was taken out and never filled (the space above shows through it).
    ///  - A tile with one edge dropped where a cross tee is missing.
    ///  - Square lights hanging askew (one side come off the grid), lenses cracked, lenses yellowed with age (dimmer and
    ///    warmer light: <see cref="LightSource.tint"/>).
    ///  - Sprinklers missing their ring (the hole round them shows), the odd one painted over.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Is this tile from another batch? (Patches where the field is high, single ones anywhere.)</summary>
        bool BatchTile(int gx, int gz, int i)
        {
            float patch = Field(gx * 0.35f, gz * 0.35f, 1300);
            if (patch > 0.8f) return Dice(gx, gz, 1301 + i) < 0.55f * Mathf.Min(1f, DetailRates.CeilingWear);
            return Dice(gx, gz, 1303 + i) < 0.012f * DetailRates.CeilingWear;
        }

        /// <summary>
        /// A tile with a round hole (a fitting taken out): the tile round it, the hole's cut edge, and the space above (a
        /// gap, so the plenum is built there). tx0, tz0: the tile's corner; h: the ceiling.
        /// </summary>
        void HoledTile(MeshWriter tileW, MeshWriter edgeW, float tx0, float tz0, float h, float hx, float hz, float rad)
        {
            // Angles round the hole: even steps plus the four corners, so the tile's corners are kept.
            var angles = new List<float>();
            const int segs = 24;
            for (int k = 0; k < segs; k++) angles.Add(k * Mathf.PI * 2f / segs);
            foreach (var (cx, cz) in new[] { (0f, 0f), (TileSize, 0f), (TileSize, TileSize), (0f, TileSize) })
            {
                float a = Mathf.Atan2(cz - hz, cx - hx);
                if (a < 0f) a += Mathf.PI * 2f;
                angles.Add(a);
            }
            angles.Sort();
            Vector2 Edge(float a)
            {
                // Where a ray from the hole's centre leaves the tile.
                float dx = Mathf.Cos(a), dz = Mathf.Sin(a), t = float.MaxValue;
                if (dx > 1e-5f) t = Mathf.Min(t, (TileSize - hx) / dx); else if (dx < -1e-5f) t = Mathf.Min(t, -hx / dx);
                if (dz > 1e-5f) t = Mathf.Min(t, (TileSize - hz) / dz); else if (dz < -1e-5f) t = Mathf.Min(t, -hz / dz);
                return new Vector2(hx + dx * t, hz + dz * t);
            }
            Vector3 W(Vector2 l, float y) => new Vector3(tx0 + l.x, y, tz0 + l.y);
            for (int k = 0; k < angles.Count; k++)
            {
                float a0 = angles[k], a1 = k + 1 < angles.Count ? angles[k + 1] : angles[0] + Mathf.PI * 2f;
                if (a1 - a0 < 1e-5f) continue;
                Vector2 i0 = new Vector2(hx + Mathf.Cos(a0) * rad, hz + Mathf.Sin(a0) * rad), i1 = new Vector2(hx + Mathf.Cos(a1) * rad, hz + Mathf.Sin(a1) * rad);
                Vector2 o0 = Edge(a0), o1 = Edge(a1);
                tileW.Quad(W(i0, h), W(i1, h), W(o1, h), W(o0, h), Vector3.down, MeshWriter.CeilingUV);
                // The hole's cut edge, facing its centre.
                Vector3 inward = -new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
                edgeW.Quad(W(i0, h), W(i1, h), W(i1, h + TileThick), W(i0, h + TileThick), inward, MeshWriter.MetreUV);
            }
        }

        /// <summary>A tile with one edge dropped (its cross tee missing): hinged on the opposite edge, tilted down a little.</summary>
        static void DropTile(MeshWriter w, Vector3 centre, float h, float side, float drop)
        {
            int e = Mathf.Min(3, (int)(side * 4f));
            Vector3 toHinge = e == 0 ? Vector3.left : e == 1 ? Vector3.right : e == 2 ? Vector3.back : Vector3.forward;
            Vector3 hinge = new Vector3(centre.x, h, centre.z) + toHinge * (TileSize * 0.5f);
            Vector3 axis = Vector3.Cross(Vector3.up, toHinge);
            float deg = Mathf.Asin(Mathf.Clamp(drop / TileSize, 0f, 0.5f)) * Mathf.Rad2Deg;
            var q = Quaternion.AngleAxis(deg, axis);
            if ((q * (-toHinge)).y > 0f) q = Quaternion.AngleAxis(-deg, axis);          // the free edge goes down
            Vector3 c0 = new Vector3(centre.x, h + TileThick * 0.5f, centre.z);
            w.Box(hinge + q * (c0 - hinge), new Vector3(TileSize - 0.004f, TileThick, TileSize - 0.004f), q, 4 | 1 | 2 | 16 | 32, MeshWriter.CeilingUV);
        }

        /// <summary>
        /// A square troffer just built (its lens from lensStart, its frame from hwStart in their writers): now and then hanging
        /// askew, one side come off the grid (the dark above shows in the gap), or its lens cracked.
        /// </summary>
        void TrofferWear(Vector2Int cell, int roomColor, Vector3 sc, float h, MeshWriter lens, int lensStart, MeshWriter hw, int hwStart)
        {
            int gx = GX(cell.x), gz = GZ(cell.y);
            var room = _L.RoomAt(cell.x, cell.y);
            bool tiles = !room.Tall && !room.openAbove && room.ceilDrop == 0 && MaterialMaps.PlenumWood;
            if (tiles && Dice(gx, gz, 1320) < 0.025f * DetailRates.CeilingWear)
            {
                int e = (int)(Dice(gx, gz, 1321) * 4f) & 3;
                Vector3 toHinge = e == 0 ? Vector3.left : e == 1 ? Vector3.right : e == 2 ? Vector3.back : Vector3.forward;
                Vector3 pivot = new Vector3(sc.x, h, sc.z) + toHinge * 0.3f;
                Vector3 axis = Vector3.Cross(Vector3.up, toHinge);
                float deg = 6f + 4f * Dice(gx, gz, 1322);
                var q = Quaternion.AngleAxis(deg, axis);
                if ((q * (-toHinge)).y > 0f) q = Quaternion.AngleAxis(-deg, axis);       // the free side hangs down
                Saw(new Vector3(sc.x, h, sc.z), Vector3.down, "light_askew");
                // The fitting's sheet-metal body above its lens, seen now that it hangs out of the grid.
                hw.Box(new Vector3(sc.x, h + 0.05f, sc.z), new Vector3(0.56f, 0.1f, 0.56f), 1 | 2 | 8 | 16 | 32, MeshWriter.MetreUV);
                lens.RotateFrom(lensStart, pivot, q);
                hw.RotateFrom(hwStart, pivot, q);
                // Its hole in the grid opens on the space above the ceiling (joists, ducts, hanger wires: BuildPlenums).
                _gaps.Add((new Vector3(sc.x, h, sc.z), h, CellMask(cell.x, cell.y), room));
                return;
            }
            if (Dice(gx, gz, 1323) < 0.02f * DetailRates.CeilingWear)
            {
                LensCracks(new Vector3(sc.x, h - 0.007f, sc.z), gx * 31 + gz * 7);
                Saw(new Vector3(sc.x, h, sc.z), Vector3.down, "lens_cracked");
            }
        }

        /// <summary>Cracks across a square lens (dark lines out from where something hit it). c: the lens's face centre.</summary>
        void LensCracks(Vector3 c, int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            Vector3 hit = c + new Vector3((R() - 0.5f) * 0.35f, -0.0009f, (R() - 0.5f) * 0.35f);
            int arms = 3 + (int)(R() * 3f);
            for (int a = 0; a < arms; a++)
            {
                float ang = a * Mathf.PI * 2f / arms + (R() - 0.5f) * 0.9f;
                Vector3 p = hit;
                int pieces = 2 + (int)(R() * 3f);
                for (int k = 0; k < pieces; k++)
                {
                    ang += (R() - 0.5f) * 0.7f;
                    float len = 0.03f + 0.07f * R();
                    Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 q = p + dir * len;
                    if (Mathf.Abs(q.x - c.x) > 0.28f || Mathf.Abs(q.z - c.z) > 0.28f) break;
                    _wallWear.Rect((p + q) * 0.5f, dir, Vector3.Cross(Vector3.up, dir), len * 0.5f, 0.0011f, new Color32(70, 70, 70, 210), new Vector2(0.9f, 0f));
                    p = q;
                }
            }
        }
    }
}
