using System;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Destruction
{
    /// <summary>
    /// One 1.2 m wall panel: the wall on one cell edge, named in world cells (as <see cref="DoorEdges"/> names them).
    /// Vertical: the wall on the line x = line (cells) at row z = along, between the cells (line - 1, along) (side 0) and
    /// (line, along) (side 1). Otherwise the wall on the line z = line at column x = along, between (along, line - 1)
    /// (side 0) and (along, line) (side 1).
    ///
    /// The panel's own frame: u along the wall from the edge's start (0 at one post's centre, 1.2 at the other's), v up from
    /// the storey's floor, w across from the wall's centre plane toward side 1.
    /// </summary>
    [Serializable]
    public struct WallKey : IEquatable<WallKey>
    {
        public bool vertical;
        public int line, along, storey;

        public WallKey(bool vertical, int line, int along, int storey) { this.vertical = vertical; this.line = line; this.along = along; this.storey = storey; }

        const float C = OfficeBiome.CellSize;

        /// <summary>The world cell on a side of the wall (0 or 1).</summary>
        public Vector2Int Cell(int side) => vertical ? new Vector2Int(side == 0 ? line - 1 : line, along) : new Vector2Int(along, side == 0 ? line - 1 : line);

        /// <summary>The chunk (x, storey, z) the cell on a side belongs to.</summary>
        public Vector3Int Chunk(int side)
        {
            var c = Cell(side);
            int n = OfficeBiome.ChunkCells;
            return new Vector3Int(FloorDiv(c.x, n), storey, FloorDiv(c.y, n));
        }

        /// <summary>The cell on a side in its chunk's own coordinates.</summary>
        public Vector2Int LocalCell(int side)
        {
            var c = Cell(side); var k = Chunk(side);
            int n = OfficeBiome.ChunkCells;
            return new Vector2Int(c.x - k.x * n, c.y - k.z * n);
        }

        /// <summary>Which side of its cell the wall is on, seen from the cell on a side (0 -X, 1 +X, 2 -Z, 3 +Z).</summary>
        public int CellSide(int side) => vertical ? (side == 0 ? 1 : 0) : (side == 0 ? 3 : 2);

        public Vector3 Along => vertical ? Vector3.forward : Vector3.right;
        /// <summary>Out of side 0 into side 1.</summary>
        public Vector3 Across => vertical ? Vector3.right : Vector3.forward;

        public Vector3 Origin(float pitch) => vertical ? new Vector3(line * C, storey * pitch, along * C) : new Vector3(along * C, storey * pitch, line * C);

        public Vector3 ToWorld(float u, float v, float w, float pitch) => Origin(pitch) + Along * u + Vector3.up * v + Across * w;
        public Vector3 ToWorld(Vector3 uvw, float pitch) => ToWorld(uvw.x, uvw.y, uvw.z, pitch);
        public Vector3 DirToWorld(Vector3 d) => Along * d.x + Vector3.up * d.y + Across * d.z;

        public Vector3 ToLocal(Vector3 world, float pitch)
        {
            var d = world - Origin(pitch);
            return new Vector3(Vector3.Dot(d, Along), d.y, Vector3.Dot(d, Across));
        }

        public Vector3 DirToLocal(Vector3 d) => new Vector3(Vector3.Dot(d, Along), d.y, Vector3.Dot(d, Across));

        /// <summary>The panel whose wall a world point lies in (or next to), if the point is within the wall's thickness.</summary>
        public static bool At(Vector3 world, Vector3 normal, int storey, out WallKey key)
        {
            key = default;
            bool vertical = Mathf.Abs(normal.x) > Mathf.Abs(normal.z);
            float pos = vertical ? world.x : world.z, other = vertical ? world.z : world.x;
            int line = Mathf.RoundToInt(pos / C);
            if (Mathf.Abs(pos - line * C) > OfficeBiome.WallThickness * 0.5f + 0.06f) return false;
            key = new WallKey(vertical, line, Mathf.FloorToInt(other / C), storey);
            return true;
        }

        /// <summary>A stable number for the panel's dice (with the world seed).</summary>
        public int Seed(int worldSeed)
        {
            unchecked
            {
                uint h = (uint)worldSeed * 0x9E3779B1u;
                h ^= (uint)line * 0x85EBCA77u; h = (h << 13) | (h >> 19);
                h ^= (uint)along * 0xC2B2AE3Du; h = (h << 11) | (h >> 21);
                h ^= (uint)storey * 0x27D4EB2Fu ^ (vertical ? 0x165667B1u : 0u);
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (int)(h & 0x7FFFFFFF);
            }
        }

        public bool Equals(WallKey o) => vertical == o.vertical && line == o.line && along == o.along && storey == o.storey;
        public override bool Equals(object obj) => obj is WallKey k && Equals(k);
        public override int GetHashCode() { unchecked { return ((line * 73856093) ^ (along * 19349663) ^ (storey * 83492791)) * 2 + (vertical ? 1 : 0); } }
        public static bool operator ==(WallKey a, WallKey b) => a.Equals(b);
        public static bool operator !=(WallKey a, WallKey b) => !a.Equals(b);
        public override string ToString() => $"{(vertical ? "x" : "z")}={line} at {along} (storey {storey})";

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }

    /// <summary>How a breakable wall is built: sizes of the drywall, studs and tracks (metres).</summary>
    public static class WallSpec
    {
        public const float C = OfficeBiome.CellSize, T = OfficeBiome.WallThickness;
        /// <summary>One sheet of gypsum board (half an inch).</summary>
        public const float Board = 0.0127f;
        /// <summary>The panel runs between the two posts (corner studs) at the ends of its edge.</summary>
        public const float U0 = T * 0.5f, U1 = C - T * 0.5f;
        /// <summary>The cavity between the boards.</summary>
        public const float CavityHalf = T * 0.5f - Board;
        /// <summary>Studs at 400 mm centres (the posts are the ones at 0 and 1.2 m).</summary>
        public static readonly float[] StudU = { 0.4f, 0.8f };
        public const float SteelFlange = 0.032f, SteelGauge = 0.0012f, SteelLip = 0.008f;
        public const float WoodWidth = 0.038f;
        public const float WoodPlate = 0.038f;
        public const float SteelTrackLeg = 0.03f;
        /// <summary>Breakable walls stop at a normal room's ceiling; above it (tall rooms) the wall stays whole.</summary>
        public const float MaxHeight = OfficeBiome.CeilingHeight;
        /// <summary>Light blocks for the light grid: 8 across (0.15 m), 18 up (0.15 m).</summary>
        public const int MaskCols = 8, MaskRows = 18;
        public const float MaskCell = 0.15f;
        public const int MaskWords = 5;

        public static float StudWidth(bool steel) => steel ? SteelFlange : WoodWidth;
        /// <summary>Bottom and top of a stud (the tracks or plates below and above it).</summary>
        public static float StudBottom(bool steel) => steel ? SteelGauge : WoodPlate;
        public static float StudTop(bool steel, float height) => steel ? height - SteelGauge : height - WoodPlate * 2f;
    }
}
