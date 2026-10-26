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
        public const float PillarRadius = 0.4f;
        /// <summary>Stairs out of a basin: three risers within the cell next to the doorway.</summary>
        public const int Steps = 3;
        public const float StepRun = 0.3f;
        /// <summary>Tiles are 15 cm: eight to a cell, and every ceiling height (0.3 m steps) is a whole number of rows.</summary>
        public const float TileSize = 0.15f;

        /// <summary>Toward the sun. It is always the same mid-afternoon: high enough to reach the floor, low enough to throw
        /// the patch a few metres from its opening and up the walls.</summary>
        public static readonly Vector3 SunDirection = new Vector3(0.42f, 0.78f, 0.46f).normalized;

        public static float FloorY(bool basin) => basin ? -BasinDepth : 0f;

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
                case SkylightShape.SlotX: return Rect.MinMaxRect(x0, 0.45f, x1, 0.75f);
                case SkylightShape.SlotZ: return Rect.MinMaxRect(0.45f, z0, 0.75f, z1);
                case SkylightShape.Square: return Rect.MinMaxRect(0.3f, 0.3f, 0.9f, 0.9f);
                case SkylightShape.Round: return Rect.MinMaxRect(0.15f, 0.15f, 1.05f, 1.05f);   // bounds of the circle
                default: return Rect.zero;
            }
        }

        public const float RoundRadius = 0.45f;

        /// <summary>Is the point (metres inside the cell) in the opening?</summary>
        public static bool InHole(SkylightShape s, int joined, float u, float v)
        {
            if (s == SkylightShape.None) return false;
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
