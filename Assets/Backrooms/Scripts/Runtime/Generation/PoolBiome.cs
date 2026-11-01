using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Dimensions and fixed light of the Poolrooms (pool-style zones). The deck (dry floor) is at the storey's floor
    /// level like every office floor, so crawl holes, spawning and the player work unchanged; flooded basins are sunk
    /// below it, with still water just under deck level.
    /// </summary>
    public static class PoolBiome
    {
        /// <summary>Basin floor below the deck, metres.</summary>
        public const float BasinDepth = 0.5f;
        /// <summary>Water surface, metres above the deck (negative: a little below it, like a pool edge).</summary>
        public const float WaterLevel = -0.12f;
        /// <summary>Top of headed doorways: tall, plain openings.</summary>
        public const float HeaderHeight = 2.7f;
        /// <summary>Light well above a roof opening, from the ceiling to the sky.</summary>
        public const float ShaftHeight = 1.2f;
        /// <summary>
        /// A light well's height (2026-11-01: in tall halls the 1.2 m well reached up past the deep pools of the storey
        /// above, whose steps and floors then hung in it, the owner's "ceiling tile glitches"): under another storey it stops
        /// 1.15 m below that storey's floor. As PoolCommon.hlsl's PoolShaft.
        /// </summary>
        public static float Shaft(float ceiling, int storey, int storeys, float pitch) =>
            storey < storeys - 1 ? Mathf.Clamp(pitch - 1.15f - ceiling, 0.15f, ShaftHeight) : ShaftHeight;
        public const float PillarRadius = 0.4f;
        /// <summary>Ceiling of a spiral stair's well on the storey above.</summary>
        public const float SpiralUpperCeiling = 4.5f;
        /// <summary>Spiral stair towers are this many cells square.</summary>
        public const int SpiralCells = 5;
        public const float SpiralColumn = 0.35f, SpiralOuter = 2.3f, SpiralRise = 0.18f, SpiralStepDegrees = 22.5f;
        /// <summary>Stairs out of a basin: three risers within the cell next to the doorway.</summary>
        public const int Steps = 3;
        public const float StepRun = 0.3f;
        /// <summary>
        /// High windows are clerestories (2026-11-01; the owner: a window in a single wall, with another window on its other
        /// side, both out into the sun, "ain't possible"): only in a tall room's wall above the roof of a lower room next
        /// door, as basilicas and natatoriums light their halls. The window runs from the lower room's roof (its ceiling
        /// plus WindowRoof) to WindowHead under the taller ceiling, inset from each end of the cell (so 0.6 m wide), and is
        /// built only if that leaves WindowMinHeight. From the lower room it is above the ceiling: a plain wall. Must match
        /// PoolCommon.hlsl.
        /// </summary>
        public const float WindowRoof = 0.35f, WindowHead = 0.45f, WindowInset = 0.3f, WindowMinHeight = 0.75f;
        public static float WindowSill(float ceilA, float ceilB) => Mathf.Min(ceilA, ceilB) + WindowRoof;
        public static float WindowTop(float ceilA, float ceilB) => Mathf.Max(ceilA, ceilB) - WindowHead;
        public static bool WindowFits(float ceilA, float ceilB) => WindowTop(ceilA, ceilB) - WindowSill(ceilA, ceilB) >= WindowMinHeight - 0.001f;
        /// <summary>Signed distance (m) from a point on a window's edge line (along: 0..1.2 in the cell, y above the deck) to
        /// the window's border; > 0 inside.</summary>
        public static float WindowSD(float along, float y, float ceilA, float ceilB) =>
            Mathf.Min(Mathf.Min(along - WindowInset, C - WindowInset - along), Mathf.Min(y - WindowSill(ceilA, ceilB), WindowTop(ceilA, ceilB) - y));

        /// <summary>Tiles are 15 cm: eight to a cell, and every ceiling height (0.3 m steps) is a whole number of rows.</summary>
        public const float TileSize = 0.15f;

        /// <summary>Toward the sun. It is always the same mid-afternoon: high enough to reach the floor, low enough to throw
        /// the patch a few metres from its opening and up the walls.</summary>
        public static readonly Vector3 SunDirection = new Vector3(0.42f, 0.78f, 0.46f).normalized;

        public static float FloorY(bool basin) => basin ? -BasinDepth : 0f;

        // ---------------- Shapes (2026-10-31, the architecture plan) ----------------

        static Vector2 ToLocal(PoolFeature f, Vector2 p)
        {
            var d = p - f.centre;
            float a = -f.yaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
        }

        /// <summary>Signed distance (m) from a point (layout-local metres) to a free pool's edge, &lt; 0 inside.</summary>
        public static float ShapeSD(PoolFeature f, Vector2 p)
        {
            var q = ToLocal(f, p);
            switch (f.shape)
            {
                case 1: return q.magnitude - f.radius;
                case 2:
                {
                    // Kidney: two circles melted together, the smaller one off to one side.
                    float ra = f.size.y, rb = f.size.y * 0.72f;
                    float a = (q - new Vector2(-f.size.x + ra, 0f)).magnitude - ra;
                    float b = (q - new Vector2(f.size.x - rb, f.size.y * 0.28f)).magnitude - rb;
                    float k = 1.4f, h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
                    return Mathf.Lerp(b, a, h) - k * h * (1f - h);
                }
                default:
                {
                    var e = new Vector2(Mathf.Abs(q.x), Mathf.Abs(q.y)) - (f.size - Vector2.one * f.radius);
                    return new Vector2(Mathf.Max(e.x, 0f), Mathf.Max(e.y, 0f)).magnitude + Mathf.Min(Mathf.Max(e.x, e.y), 0f) - f.radius;
                }
            }
        }

        /// <summary>Depth of a sloping pool floor at a point: shallow at local -x, deep at +x (FreePool and Slope).</summary>
        public static float SlopedDepth(PoolFeature f, Vector2 p)
        {
            if (f.depth2 <= 0f) return f.depth > 0f ? f.depth : BasinDepth;
            float t;
            if (f.kind == PoolFeatureKind.Slope)
            {
                var b = f.bounds;
                float yaw = Mathf.Repeat(f.yaw, 360f);
                t = yaw < 45f || yaw >= 315f ? (p.x - b.xMin) / b.width : yaw < 135f ? (p.y - b.yMin) / b.height
                  : yaw < 225f ? (b.xMax - p.x) / b.width : (b.yMax - p.y) / b.height;
            }
            else t = (ToLocal(f, p).x + f.size.x) / (2f * f.size.x);
            // Flat at each end, a slope between (as pools are built).
            t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.3f) / 0.45f));
            return Mathf.Lerp(f.depth, f.depth2, t);
        }

        const float C = OfficeBiome.CellSize;

        /// <summary>
        /// Opening of a roof cell, in metres inside the cell (0..1.2 on each axis). joined: sides whose neighbour continues
        /// the same opening (bits: 1 -X, 2 +X, 4 -Z, 8 +Z); a joined side runs to the cell edge, a closed side keeps a
        /// tile-wide rim. Must match the shader (PoolCommon.hlsl, PoolInHole).
        /// </summary>
        public static Rect HoleRect(SkylightShape s, int joined)
        {
            const float rim = 0.15f;
            float x0 = (joined & 1) != 0 ? 0f : rim, x1 = (joined & 2) != 0 ? C : C - rim;
            float z0 = (joined & 4) != 0 ? 0f : rim, z1 = (joined & 8) != 0 ? C : C - rim;
            switch (s)
            {
                case SkylightShape.Full: return Rect.MinMaxRect(x0, z0, x1, z1);
                case SkylightShape.SlotX: return Rect.MinMaxRect(x0, 0.3f, x1, 0.9f);
                case SkylightShape.SlotZ: return Rect.MinMaxRect(0.3f, z0, 0.9f, z1);
                case SkylightShape.Square: return Rect.MinMaxRect(0.2f, 0.2f, 1.0f, 1.0f);
                case SkylightShape.Round: return Rect.MinMaxRect(0.1f, 0.1f, 1.1f, 1.1f);   // bounds of the circle
                default: return Rect.zero;
            }
        }

        public const float RoundRadius = 0.5f;

        /// <summary>Is the point (metres inside the cell) in the opening? (A Disc needs the cell's own disc: see the overload.)</summary>
        public static bool InHole(SkylightShape s, int joined, float u, float v)
        {
            if (s == SkylightShape.None || s == SkylightShape.Disc) return false;
            if (s == SkylightShape.Round) { float du = u - C * 0.5f, dv = v - C * 0.5f; return du * du + dv * dv < RoundRadius * RoundRadius; }
            return HoleRect(s, joined).Contains(new Vector2(u, v));
        }

        /// <summary>Which sides of a roof cell continue into a neighbour with the same kind of opening (Full and slots only).</summary>
        public static int Joined(System.Func<int, int, SkylightShape> at, int x, int z)
        {
            var s = at(x, z);
            if (s != SkylightShape.Full && s != SkylightShape.SlotX && s != SkylightShape.SlotZ) return 0;
            int j = 0;
            bool alongX = s != SkylightShape.SlotZ, alongZ = s != SkylightShape.SlotX;
            if (alongX && at(x - 1, z) == s) j |= 1;
            if (alongX && at(x + 1, z) == s) j |= 2;
            if (alongZ && at(x, z - 1) == s) j |= 4;
            if (alongZ && at(x, z + 1) == s) j |= 8;
            return j;
        }
    }
}
