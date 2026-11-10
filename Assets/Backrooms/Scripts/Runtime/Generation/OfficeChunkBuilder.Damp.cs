using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Water in Level 0 (2026-11-03), where the building leaks (the same slow field that stains the ceiling tiles), rare
    /// and seeded like the wear (world seed and cell):
    ///  - Damp carpet: dark, more saturated patches of lobes with uneven tide lines at their rims (wet carpet darkens and
    ///    takes on colour; its fibres hardly shine), under leaks, at the foot of water running down a wall, and now and
    ///    then on their own. Stepping on one squelches (DampFloor, Audio.PlayerAudio).
    ///  - Water down a wall: from under the ceiling a yellow-brown run, widest at the top, narrowing as it goes down, with
    ///    a darker tide line where it stopped; wallpaper bubbled at its foot; sometimes it reaches the floor.
    ///  - Leaks: under a stained tile, a drop gathers, falls and lands (CeilingLeak), every few seconds, into a puddle on a
    ///    hard floor, a dark patch on carpet, or a bucket someone put under it.
    ///  - Mould: black-green specks in an inside corner, thickest low down and in the corner, thinning out, over a grey
    ///    haze.
    /// Marks are the wear overlays (Backrooms/FloorWear); the puddle, bucket and bubbles are geometry.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Stained ceiling tiles (centre at the ceiling, chunk-local; cell; which of the cell's two), for leaks.</summary>
        readonly List<(Vector3 c, int x, int z, int i)> _stainedTiles = new List<(Vector3, int, int, int)>();

        public struct LeakSpec
        {
            public Vector3 top;        // chunk-local: where the drop gathers (just under the tile)
            public float land;         // chunk-local height it lands at (the floor, a puddle, the water in a bucket)
            public bool water;         // lands in water (a puddle, a bucket): a ripple and a plink; else a dull tap on carpet
            public float interval;     // seconds a drop takes to gather
            public int seed;
        }
        readonly List<LeakSpec> _leaks = new List<LeakSpec>();
        /// <summary>Damp patches on the floor (world x, z and radius in y), for footsteps (DampFloor).</summary>
        readonly List<Vector3> _damp = new List<Vector3>();

        /// <summary>A floor that soaks water up rather than pooling it: any carpet (the office's, a home's, the reference
        /// room's), not concrete, wood or laminate.</summary>
        bool SoftFloor(Room r) => !OfficeBiomeAssets.ConcreteFloor(r) && r.floorFinish != FloorFinish.DarkWood && r.floorFinish != FloorFinish.Laminate;

        /// <summary>How wet the building is here, 0..1: the slow field the stained tiles follow (DressCeiling).</summary>
        float Wetness(int gx, int gz) => Field(gx * 0.17f, gz * 0.17f, 901);

        void ClearDamp() { _stainedTiles.Clear(); _leaks.Clear(); _damp.Clear(); }

        // ---------------- Floor ----------------

        /// <summary>A cell's own damp patch, now and then where it is wet (carpet only).</summary>
        void DampCell(int x, int z, Room r, bool carpet)
        {
            if (!carpet) return;
            int gx = GX(x), gz = GZ(z);
            float wet = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.9f, Wetness(gx, gz)));
            if (Dice(gx, gz, 3000) >= 0.006f * wet * DetailRates.FloorMarks) return;
            var c = new Vector3((x + 0.25f + 0.5f * Dice(gx, gz, 3001)) * C, 0f, (z + 0.25f + 0.5f * Dice(gx, gz, 3002)) * C);
            if (!Clear(c, 0.15f)) return;
            float rad = Mathf.Min(0.22f + 0.3f * Dice(gx, gz, 3003), WallRoom(x, z, c) / 1.5f);
            if (rad < 0.12f) return;
            DampPatch(c, rad, gx * 7919 + gz * 104729 + 3);
            Saw(c, Vector3.up, "damp");
        }

        /// <summary>How far a point in cell (x, z) is from the cell's walls (its solid sides; a patch must not run under one:
        /// seen low across the floor, the overlay showed as a dark streak up the baseboard).</summary>
        float WallRoom(int x, int z, Vector3 c)
        {
            float best = 9f, yb = T * 0.5f + (NoBaseboard ? 0f : BD);
            for (int s = 0; s < 4; s++)
            {
                if (!OfficeLayout.IsSolid(_L.EdgeOf(x, z, s))) continue;
                float d = s == 0 ? c.x - x * C : s == 1 ? (x + 1) * C - c.x : s == 2 ? c.z - z * C : (z + 1) * C - c.z;
                best = Mathf.Min(best, d - yb);
            }
            return best;
        }

        /// <summary>
        /// A damp patch on the carpet: overlapping lobes (wetter where they overlap), each with a faint tide line at its rim,
        /// darker and more saturated than the dry carpet round it.
        /// </summary>
        void DampPatch(Vector3 c, float rad, int seed)
        {
            var rng = new System.Random(seed);
            int lobes = 4 + rng.Next(5);
            for (int k = 0; k < lobes; k++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f, d = rad * 0.55f * (float)rng.NextDouble();
                float lr = rad * (0.45f + 0.45f * (float)rng.NextDouble());
                var p = c + new Vector3(Mathf.Cos(ang) * d, 0.0016f + 0.0001f * k, Mathf.Sin(ang) * d);
                byte alpha = (byte)(120 + 70 * rng.NextDouble());
                _floorWear.Rect(p, Vector3.right, Vector3.forward, lr, lr * (0.75f + 0.4f * (float)rng.NextDouble()), new Color32(148, 132, 98, alpha), new Vector2(-0.3f, 0.35f));
            }
            // The wettest middle.
            _floorWear.Rect(c + Vector3.up * 0.0019f, Vector3.right, Vector3.forward, rad * 0.4f, rad * 0.4f, new Color32(150, 138, 108, 150), new Vector2(-0.1f, 0f));
            _damp.Add(new Vector3(_origin.x + c.x, rad, _origin.z + c.z));
        }

        // ---------------- Walls ----------------

        /// <summary>
        /// Water on one wall face (seen from cell x, z): a run from under the ceiling, and mould in its inside corners.
        /// face: a point on the face at the cell's middle, floor level; along: the face's horizontal axis; into: into the room.
        /// </summary>
        void DampWall(int x, int z, int side, Vector3 face, Vector3 along, Vector3 into, Room r, uint m, float top)
        {
            int gx = GX(x), gz = GZ(z);
            float wet = Wetness(gx, gz);
            float reach = C * 0.5f - T * 0.5f - 0.04f;
            Vector3 lift = into * 0.0013f;
            Vector3 P(float a, float y) => face + along * a + Vector3.up * y + lift;
            float yb = NoBaseboard ? 0f : BB;

            // Water run down from under the ceiling.
            if (top < 4f && !r.openAbove && Dice(gx, gz, 3010 + side) < 0.0025f * (0.2f + 1.8f * wet * wet) * DetailRates.WallWear)
            {
                var rng = new System.Random(gx * 486187739 + gz * 15485863 + side * 7 + 11);
                float a0 = Mathf.Lerp(-reach + 0.2f, reach - 0.2f, (float)rng.NextDouble());
                bool toFloor = rng.NextDouble() < 0.35;
                float len = toFloor ? top - yb : (0.6f + 1.4f * (float)rng.NextDouble());
                float yTop = top - 0.002f, yEnd = Mathf.Max(yb, yTop - len);
                int runs = 1 + rng.Next(3);
                var stain = new Color32(150, 112, 66, 205);
                var tide = new Color32(112, 80, 44, 215);
                for (int k = 0; k < runs; k++)
                {
                    float a = a0 + (k - (runs - 1) * 0.5f) * (0.07f + 0.06f * (float)rng.NextDouble());
                    float w0 = 0.06f + 0.1f * (float)rng.NextDouble();
                    float yk = Mathf.Lerp(yEnd, yTop, k == 0 ? 0f : 0.2f + 0.5f * (float)rng.NextDouble());
                    const int seg = 12;
                    float drift = 0f;
                    for (int s = 0; s < seg; s++)
                    {
                        float t0 = s / (float)seg, t1 = (s + 1) / (float)seg;
                        float y0 = Mathf.Lerp(yTop, yk, t0), y1 = Mathf.Lerp(yTop, yk, t1);
                        float d1 = drift + ((float)rng.NextDouble() - 0.5f) * 0.02f;
                        float h0 = w0 * (1f - 0.65f * t0), h1 = w0 * (1f - 0.65f * t1);
                        Color32 c0 = Tone(stain, 1f - 0.35f * t0), c1 = Tone(stain, 1f - 0.35f * t1);
                        _wallWear.Quad(P(a + drift - h0, y0), P(a + drift + h0, y0), P(a + d1 + h1, y1), P(a + d1 - h1, y1),
                                       new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-1f, 0f), c0, c0, c1, c1, new Vector2(0.45f, 0f));
                        // The edges darker: where the water dried, the dirt it carried was left.
                        foreach (float e in new[] { -1f, 1f })
                            _wallWear.Quad(P(a + drift + e * h0 - 0.006f, y0), P(a + drift + e * h0 + 0.006f, y0), P(a + d1 + e * h1 + 0.006f, y1), P(a + d1 + e * h1 - 0.006f, y1),
                                           new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-1f, 0f), Tone(tide, 0.55f), Tone(tide, 0.55f), Tone(tide, 0.45f), Tone(tide, 0.45f), new Vector2(0.2f, 0f));
                        drift = d1;
                    }
                    // Where it stopped: a rounded tide line.
                    float wEnd = w0 * 0.35f;
                    _wallWear.Rect(P(a + drift, yk + 0.004f), along, Vector3.up, wEnd * 1.2f, wEnd * 0.8f, tide, new Vector2(-0.4f, 0.6f));
                }
                // The band under the ceiling it came from.
                _wallWear.Rect(P(a0, yTop - 0.05f), along, Vector3.up, 0.22f + 0.1f * runs, 0.05f, new Color32(150, 118, 76, 180), new Vector2(0.5f, 0.3f));
                // Wallpaper let go at its foot.
                if (_a.wallpaper && _a.WallMaterial(r) == _a.wallpaper && rng.NextDouble() < 0.6)
                {
                    var paper = WMask(_a.wallpaper, m);
                    int n = 2 + rng.Next(3);
                    for (int j = 0; j < n; j++)
                    {
                        float rad = 0.02f + 0.03f * (float)rng.NextDouble();
                        float a = Mathf.Clamp(a0 + ((float)rng.NextDouble() - 0.5f) * 0.18f, -reach + rad, reach - rad);
                        float y = Mathf.Clamp(yEnd + 0.04f + 0.25f * (float)rng.NextDouble(), yb + rad + 0.02f, top - 0.1f);
                        Bubble(paper, face + along * a + Vector3.up * y, along, into, rad, 0.004f + 0.006f * (float)rng.NextDouble());
                    }
                }
                // At the floor, the carpet soaked.
                if (toFloor && Carpeted(r))
                {
                    float fr = 0.14f + 0.06f * (float)rng.NextDouble();
                    var foot = face + along * a0 + into * (BD + fr * 1.5f + 0.02f);
                    if (Clear(foot, 0.05f)) DampPatch(foot, fr, gx * 31 + gz * 17 + side);
                }
                Saw(P(a0, Mathf.Clamp((yEnd + yTop) * 0.5f, 1.1f, 1.7f)), into, "wall_water");
            }

            // Mould in an inside corner (decided by the corner, so both walls of it share one).
            for (int k = 0; k < 2; k++)
            {
                int endSide = side < 2 ? (k == 0 ? 2 : 3) : (k == 0 ? 0 : 1);
                if (!OfficeLayout.IsSolid(_L.EdgeOf(x, z, endSide))) continue;
                int vx = gx + ((side == 1) ? 1 : 0) + (side >= 2 && k == 1 ? 1 : 0);
                int vz = gz + ((side == 3) ? 1 : 0) + (side < 2 && k == 1 ? 1 : 0);
                float wetC = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.85f, wet));
                if (Dice(vx, vz, 3020) >= 0.02f * wetC * DetailRates.WallWear) continue;
                float sgn = k == 0 ? 1f : -1f, ae = k == 0 ? -(C * 0.5f - T * 0.5f) : C * 0.5f - T * 0.5f;
                var rng = new System.Random(vx * 92821 + vz * 68917 + side * 3 + 5);
                bool high = Dice(vx, vz, 3021) < 0.3f;                    // up by the ceiling instead (damp air rises)
                float yLo = high ? top - 0.7f : yb, yHi = high ? top - 0.01f : yb + 0.5f + 0.7f * Dice(vx, vz, 3022);
                // A grey-green haze under the specks.
                float hy = (yLo + yHi) * 0.5f, hh = (yHi - yLo) * 0.5f;
                _wallWear.Quad(P(ae, hy - hh), P(ae + sgn * 0.34f, hy - hh), P(ae + sgn * 0.34f, hy + hh), P(ae, hy + hh),
                               new Vector2(0f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Color32(96, 104, 78, 175), new Vector2(0.1f, 0.4f));
                // Clumps of specks, thickest in the corner.
                int clumps = 5 + rng.Next(6);
                for (int g = 0; g < clumps; g++)
                {
                    float cu = (float)rng.NextDouble(), cv = (float)rng.NextDouble();
                    float cd = 0.01f + 0.26f * cu * cu, ch = high ? 1f - cv * cv : cv * cv;
                    float cy = Mathf.Lerp(yLo, yHi, ch), cr = 0.03f + 0.06f * (float)rng.NextDouble();
                    _wallWear.Rect(P(ae + sgn * (cd + cr * 0.5f), cy), along, Vector3.up, cr, cr * 0.8f, new Color32(58, 66, 48, 165), new Vector2(-0.2f, 0f));
                    int specks = 10 + rng.Next(18);
                    for (int s = 0; s < specks; s++)
                    {
                        float ang = (float)rng.NextDouble() * 6.283f, dist = cr * 1.3f * Mathf.Sqrt((float)rng.NextDouble());
                        float rr = 0.004f + 0.012f * (float)rng.NextDouble();
                        float sa = Mathf.Max(0.004f, cd + cr * 0.5f + Mathf.Cos(ang) * dist);
                        float sy = Mathf.Clamp(cy + Mathf.Sin(ang) * dist, yLo, yHi);
                        _wallWear.Rect(P(ae + sgn * sa, sy), along, Vector3.up, rr, rr * (0.7f + 0.5f * (float)rng.NextDouble()), new Color32(26, 32, 22, (byte)(190 + 60 * rng.NextDouble())), new Vector2(-0.4f, 0f));
                    }
                }
                if (side < 2) Saw(P(ae + sgn * 0.12f, Mathf.Clamp(hy, 0.4f, top - 0.3f)), into, "mould");   // (once a corner: its x-facing wall)
            }
        }

        // ---------------- Leaks ----------------

        /// <summary>A few stained tiles leak (after the props: what they drip on must be clear).</summary>
        void Leaks()
        {
            foreach (var (c, x, z, i) in _stainedTiles)
            {
                if (_L.Has(x, z, CellFlags.NoFloor) || !_L.Plain(x, z)) continue;
                var r = _L.RoomAt(x, z);
                if (r.kind == RoomKind.Crawl || r.custom) continue;
                int gx = GX(x), gz = GZ(z);
                if (Dice(gx, gz, 3030 + i) >= 0.035f * DetailRates.CeilingWear) continue;
                var at = new Vector3(c.x + (Dice(gx, gz, 3032 + i) - 0.5f) * 0.36f, 0f, c.z + (Dice(gx, gz, 3034 + i) - 0.5f) * 0.36f);
                if (!Clear(at, 0.3f)) continue;
                bool carpet = SoftFloor(r);
                uint m = CellMask(x, z);
                int seed = gx * 7349 + gz * 2791 + i;
                bool bucket = FittingModels.Ready && Dice(gx, gz, 3036 + i) < (carpet ? 0.4f : 0.3f);
                float land = 0f;
                if (carpet) DampPatch(at, Mathf.Max(0.12f, Mathf.Min(0.35f + 0.25f * Dice(gx, gz, 3038 + i), WallRoom(x, z, at) / 1.5f)), seed);
                if (!carpet) Puddle(at, 0.2f + 0.25f * Dice(gx, gz, 3038 + i), m, seed, bucket);
                if (bucket) land = Bucket(at, m, seed);
                _leaks.Add(new LeakSpec
                {
                    top = new Vector3(at.x, c.y - 0.006f, at.z), land = land + 0.002f, water = bucket || !carpet,
                    interval = 1.6f + 3.4f * Dice(gx, gz, 3040 + i), seed = seed,
                });
                Saw(new Vector3(at.x, land + 0.05f, at.z), Vector3.up, "leak");     // (seen from beside it: the drop, what it lands in)
            }
        }

        /// <summary>Standing water on a hard floor (or the splash round a bucket): an uneven glossy film.</summary>
        void Puddle(Vector3 c, float rad, uint m, int seed, bool small)
        {
            if (!FittingMaterials.Water) return;
            var w = WMask(FittingMaterials.Water, m);
            var rng = new System.Random(seed * 3 + 1);
            const int n = 18;
            var pts = new Vector3[n];
            float k1 = (float)rng.NextDouble() * 6.28f, k2 = (float)rng.NextDouble() * 6.28f;
            for (int i = 0; i < n; i++)
            {
                float ang = i * Mathf.PI * 2f / n;
                float rr = rad * (0.78f + 0.14f * Mathf.Sin(ang * 2f + k1) + 0.1f * Mathf.Sin(ang * 3f + k2) + 0.05f * (float)rng.NextDouble());
                pts[i] = c + new Vector3(Mathf.Cos(ang) * rr * 1.2f, 0.0012f, Mathf.Sin(ang) * rr);
            }
            var mid = c + Vector3.up * 0.0012f;
            for (int i = 0; i < n; i++)
                w.Quad(mid, pts[(i + 1) % n], pts[i], pts[i], Vector3.up, MeshWriter.MetreUV);
            if (!small) _damp.Add(new Vector3(_origin.x + c.x, rad, _origin.z + c.z));
        }

        /// <summary>A plastic bucket put under a leak (the model: ribbed, rolled rim, its handle folded down), dirty water in it;
        /// returns the water's height.</summary>
        float Bucket(Vector3 at, uint m, int seed)
        {
            var rng = new System.Random(seed);
            const float rb = 0.12f, rt = 0.148f, h = 0.28f;
            Model("Bucket", m, at, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            float water = 0.06f + 0.1f * (float)rng.NextDouble();
            var wm = FittingMaterials.BucketWater ? FittingMaterials.BucketWater : FittingMaterials.Water;
            if (wm) WMask(wm, m).Disc(at + Vector3.up * water, Mathf.Lerp(rb, rt, water / h) - 0.009f, 32, Vector3.up, MeshWriter.MetreUV);
            _boxes.Add((at + Vector3.up * (h * 0.5f), new Vector3(rt * 2f, h, rt * 2f)));
            return water;
        }

        void InstantiateLeaks(Transform root)
        {
            foreach (var l in _leaks)
            {
                var go = new GameObject("Leak");
                go.transform.SetParent(root, false);
                go.transform.localPosition = l.top;
                var leak = go.AddComponent<CeilingLeak>();
                leak.fall = l.top.y - l.land;
                leak.water = l.water;
                leak.interval = l.interval;
                leak.seed = l.seed;
            }
            DampFloor.Set(new Vector3Int(_L.coord.x, _L.storey, _L.coord.y), _damp);
        }
    }

    /// <summary>Where the floor is damp (published by each chunk as it is made), for footsteps: a squelch on wet carpet.</summary>
    public static class DampFloor
    {
        static readonly Dictionary<Vector3Int, List<Vector3>> s_chunks = new Dictionary<Vector3Int, List<Vector3>>();

        public static void Set(Vector3Int chunk, List<Vector3> spots)
        {
            if (spots == null || spots.Count == 0) { s_chunks.Remove(chunk); return; }
            s_chunks[chunk] = new List<Vector3>(spots);
        }

        public static void Clear() => s_chunks.Clear();

        /// <summary>Is a point on the floor on a damp patch (x, z within most of its radius)?</summary>
        public static bool At(Vector3 p)
        {
            foreach (var kv in s_chunks)
                foreach (var s in kv.Value)
                {
                    float dx = p.x - s.x, dz = p.z - s.z, r = s.y * 0.85f;
                    if (dx * dx + dz * dz < r * r) return true;
                }
            return false;
        }
    }
}
