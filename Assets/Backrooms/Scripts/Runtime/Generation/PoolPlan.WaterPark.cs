using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The water park biome's rooms (2026-11-01, the owner: "indoor water park, slides, mushroom water things with water
    /// going down them"). Laid out as indoor water parks are (WhiteWater's indoor guides: slides, a lazy river or a wave pool
    /// as the big crowd features, splash zones for the young, splash features along the river), each in a room of its own:
    ///  - a slide tower over a plunge pool: stairs up to a railed platform, a flume curling down into the pool;
    ///  - a lazy river: a ring of 0.85 m deep water round a tiled island, its return jets all pushing one way;
    ///  - a wave pool: a wide beach entry (14 cm at the shallow end) down to 1.7 m at the deep end, where the wave chambers
    ///    behind their grates push a wave out every few seconds while a session runs;
    ///  - kiddie pools: shallow round pools, each with a mushroom fountain, water pouring off its cap all round;
    ///  - in the flooded halls: mushrooms standing in the water, and water arches from nozzles in the walls.
    /// Rooms in it get Tag.Park (plain dressing otherwise: windows, no columns in them).
    /// </summary>
    public static partial class PoolPlan
    {
        public static bool IsPark(OfficeLayout L, RectInt rc) => L.RulesAt(rc).name == "WaterPark";

        /// <summary>Plans a water park room; false if it stays a plain room.</summary>
        static bool WaterParkRoom(OfficeLayout L, Room r, System.Random rng)
        {
            var rc = r.rect;
            int w = rc.width, h = rc.height, mn = Mathf.Min(w, h), mx = Mathf.Max(w, h);
            if (r.kind == RoomKind.Hallway || mn < 3) return false;
            bool basin = L.Basin(rc.xMin + w / 2, rc.yMin + h / 2);
            var m = Metres(rc);
            bool longX = w >= h;
            float yaw = longX ? 0f : 90f;
            int colour = rng.Next(4);
            double roll = rng.NextDouble();
            if (!basin && mn >= 9 && roll < 0.3)
            {
                // The lazy river: a ring round an island, a walkway of deck outside it.
                var size = new Vector2(m.width, m.height) * 0.5f - Vector2.one * 1.3f;
                if (!longX) size = new Vector2(size.y, size.x);
                var f = AddFreePool(L, r, m.center, size, Mathf.Min(2.2f, Mathf.Min(size.x, size.y) * 0.6f), 3, yaw, 0.85f, 0f, rng, width: 2.2f);
                return true;
            }
            if (!basin && mn >= 8 && mx >= 11 && roll < 0.6)
            {
                // The wave pool: the beach at local -x, the wave chambers at +x.
                var size = new Vector2(Mathf.Max(m.width, m.height), Mathf.Min(m.width, m.height)) * 0.5f - Vector2.one * 1.5f;
                var f = AddFreePool(L, r, m.center, size, Mathf.Min(1.4f, size.y * 0.5f), 0, yaw, 0.14f, 1.7f, rng);
                var dir = Rot(Vector2.right, yaw);
                var wm = Fitting(PoolFeatureKind.WaveMaker, m.center + dir * size.x, yaw + 180f, new Vector2(size.y * 0.8f, 0.5f), r);
                L.poolFeatures.Add(wm);
                return true;
            }
            if (!basin && mn >= 6 && roll < 0.82)
            {
                // A slide tower over a plunge pool: the pool toward one end, the tower on the deck at the other.
                var dir = Rot(Vector2.right, yaw);
                float halfLong = Mathf.Max(m.width, m.height) * 0.5f, halfShort = Mathf.Min(m.width, m.height) * 0.5f;
                var size = new Vector2(Mathf.Min(3.2f, halfLong - 3.2f), Mathf.Min(2.2f, halfShort - 1.4f));
                if (size.x < 1.6f || size.y < 1.2f) return false;
                var pc = m.center - dir * (halfLong - 1.2f - size.x);
                AddFreePool(L, r, pc, size, Mathf.Min(1.2f, size.y * 0.6f), rng.NextDouble() < 0.5 ? 2 : 0, yaw, 1.1f, 1.4f, rng, ladders: false);
                var towerAt = pc + dir * (size.x + 1.6f);
                var t = Fitting(PoolFeatureKind.SlideTower, towerAt, yaw + 180f, new Vector2(size.x + 1.6f, size.y), r);
                t.height = Mathf.Min(r.ceiling - 1.4f, 3.8f);
                t.shape = colour + (rng.NextDouble() < 0.5 ? 4 : 0);           // the low bits the colour, 4: which way it curls
                if (t.height < 2.4f) return true;
                L.poolFeatures.Add(t);
                return true;
            }
            if (!basin)
            {
                // Kiddie pools, each round a mushroom fountain.
                int n = mn >= 7 ? 2 : 1;
                for (int i = 0; i < n; i++)
                {
                    var dir = Rot(Vector2.right, yaw);
                    float off = n == 1 ? 0f : (i == 0 ? -1f : 1f) * Mathf.Max(m.width, m.height) * 0.22f;
                    var c = m.center + dir * off;
                    float R = Mathf.Min(1.9f, Mathf.Min(m.width, m.height) * 0.5f - 1.0f);
                    if (R < 1.4f) return i > 0;
                    AddFreePool(L, r, c, new Vector2(R, R), R, 1, 0f, 0.28f, 0f, rng, ladders: false);
                    var mush = Fitting(PoolFeatureKind.Mushroom, c, 0f, new Vector2(R, R), r);
                    mush.radius = Mathf.Min(R - 0.7f, 0.75f + (float)rng.NextDouble() * 0.5f);     // its curtain lands well inside
                    mush.height = 1.7f + (float)rng.NextDouble() * 0.6f;
                    mush.shape = rng.Next(4);
                    L.poolFeatures.Add(mush);
                }
                return true;
            }
            // A flooded hall: mushrooms standing in the water, arches from the walls.
            bool any = false;
            if (mn >= 5 && rng.NextDouble() < 0.6)
            {
                int n = mx >= 10 ? 2 : 1;
                for (int i = 0; i < n; i++)
                {
                    var dir = Rot(Vector2.right, yaw);
                    var c = m.center + dir * (n == 1 ? 0f : (i == 0 ? -1f : 1f) * Mathf.Max(m.width, m.height) * 0.25f);
                    var mush = Fitting(PoolFeatureKind.Mushroom, c, 0f, new Vector2(1.6f, 1.6f), r);
                    mush.radius = 0.9f + (float)rng.NextDouble() * 0.5f;
                    mush.height = 2.1f + (float)rng.NextDouble() * 0.6f;
                    mush.shape = rng.Next(4);
                    L.poolFeatures.Add(mush);
                    any = true;
                }
            }
            var walls = WetWalls(L, r);
            if (walls.Count > 0 && rng.NextDouble() < 0.6)
            {
                int n = Mathf.Min(walls.Count, 2 + rng.Next(3));
                for (int i = 0; i < n; i++)
                {
                    var (x, z, side) = walls[rng.Next(walls.Count)];
                    var (at, wy) = WallPoint(x, z, side);
                    var a = Fitting(PoolFeatureKind.Arch, at, wy, new Vector2(1.8f + (float)rng.NextDouble() * 1.2f, 0.5f), r);
                    a.height = 0.7f + (float)rng.NextDouble() * 0.5f;
                    L.poolFeatures.Add(a);
                    any = true;
                }
            }
            return any;
        }
    }
}
