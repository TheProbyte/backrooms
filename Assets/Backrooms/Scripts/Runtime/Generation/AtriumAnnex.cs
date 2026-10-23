using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>What a room is for in an atrium's annex (stairs and elevator wing).</summary>
    public enum AnnexRole : byte { None, StairHall, StairVoid, ShaftGround, ShaftUpper }

    /// <summary>
    /// The wing attached to one side of an atrium that lets people reach the upper storey: a straight stair hall
    /// and/or an elevator. Pure geometry of the layout, derived from the <see cref="LandmarkPlacement"/>, so
    /// generation (rooms, doors), the chunk builder (steps, sloped ceiling, shaft) and the runtime (elevator)
    /// all agree.
    ///
    /// Local frame (u along the wing, v from its outer wall toward the atrium core), in cells:
    ///
    ///   v 5.. core (the atrium's ring and well)
    ///   v 2-4 band N, 3 deep:  lobby (u 0..L-3) + elevator shaft (u L-2..L-1)      or one filler room
    ///   v 0-1 band S, 2 deep:  stair hall (u 0..L-4), landing/closet (u L-3..L-1)  or one filler room
    ///
    /// The stair rises along +u from the hall's flat entry to the end of the hall and arrives, one storey up, on
    /// the landing. The hall's ceiling slopes with the stairs and meets the upper storey's ceiling at the top.
    /// The shaft is a hole through both storeys; its door faces the lobby (toward -u) in the middle cell.
    /// </summary>
    public static class AtriumAnnex
    {
        public const int Stairs = 1, Elevator = 2;
        public const int Depth = 5, BandS = 2, BandN = 3, MinLength = 16, LandingCells = 3, ShaftCells = 2;
        public const float C = OfficeBiome.CellSize;
        /// <summary>Flat floor at the foot of the stairs, metres (the doorway into the hall lies in it).</summary>
        public const float EntryFlat = 1.8f;

        /// <summary>Maps local (u, v) cells to district-local cells. u always runs along +x or +z.</summary>
        public struct Frame
        {
            public Vector2Int ux, vz;       // world direction of +u and +v
            public Vector2 corner;          // world position (cells) of local (0, 0)'s corner

            public static Frame Make(int side, RectInt annex)
            {
                switch (side)
                {
                    case 0:  return new Frame { ux = new Vector2Int(1, 0), vz = new Vector2Int(0, 1), corner = new Vector2(annex.xMin, annex.yMin) };   // annex south of the core
                    case 1:  return new Frame { ux = new Vector2Int(1, 0), vz = new Vector2Int(0, -1), corner = new Vector2(annex.xMin, annex.yMax) };  // north
                    case 2:  return new Frame { ux = new Vector2Int(0, 1), vz = new Vector2Int(1, 0), corner = new Vector2(annex.xMin, annex.yMin) };   // west
                    default: return new Frame { ux = new Vector2Int(0, 1), vz = new Vector2Int(-1, 0), corner = new Vector2(annex.xMax, annex.yMin) };  // east
                }
            }

            /// <summary>Cell containing the centre of local cell (u, v).</summary>
            public Vector2Int Cell(int u, int v)
            {
                var p = corner + new Vector2(ux.x, ux.y) * (u + 0.5f) + new Vector2(vz.x, vz.y) * (v + 0.5f);
                return new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
            }

            public RectInt Rect(int u, int v, int w, int h)
            {
                var a = Cell(u, v); var b = Cell(u + w - 1, v + h - 1);
                return new RectInt(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(a.x - b.x) + 1, Mathf.Abs(a.y - b.y) + 1);
            }

            /// <summary>Position (metres, district-local) of local point (u, v) metres.</summary>
            public Vector2 Point(float u, float v) => (corner + new Vector2(ux.x, ux.y) * (u / C) + new Vector2(vz.x, vz.y) * (v / C)) * C;

            /// <summary>World direction (unit) of local direction (du, dv).</summary>
            public Vector2Int Dir(int du, int dv) => ux * du + vz * dv;

            /// <summary>Side index (0 = -X, 1 = +X, 2 = -Z, 3 = +Z) of a unit world direction.</summary>
            public static int SideOf(Vector2Int d) => d.x < 0 ? 0 : d.x > 0 ? 1 : d.y < 0 ? 2 : 3;
        }

        public struct Plan
        {
            public Frame f;
            public int length;              // cells along u
            public bool stairs, elevator;
            public float pitch;

            public int HallCells => length - LandingCells;
            public float StairStart => EntryFlat;
            public float StairEnd => HallCells * C;
            public int Steps => Mathf.Max(8, Mathf.RoundToInt((StairEnd - StairStart) / 0.28f));
            public float Riser => pitch / Steps;
            public float Tread => (StairEnd - StairStart) / Steps;

            /// <summary>Floor height of the stairs at local u metres (stepped); 0 before the first step.</summary>
            public float StepTop(float u)
            {
                if (u <= StairStart) return 0f;
                int i = Mathf.Min(Steps - 1, Mathf.FloorToInt((u - StairStart) / Tread));
                return (i + 1) * Riser;
            }

            /// <summary>Slope of the stair hall's ceiling at local u metres: the normal ceiling at the foot, rising to the upper ceiling.</summary>
            public float CeilingAt(float u)
            {
                float t = Mathf.Clamp01((u - StairStart) / (StairEnd - StairStart));
                return OfficeBiome.CeilingHeight + pitch * t;
            }

            // Rooms (local cells: u, v, width, height)
            public RectInt Hall => f.Rect(0, 0, HallCells, BandS);
            public RectInt Landing => f.Rect(HallCells, 0, LandingCells, BandS);
            public RectInt BandSFull => f.Rect(0, 0, length, BandS);
            public RectInt Lobby => f.Rect(0, BandS, length - ShaftCells, BandN);
            public RectInt Shaft => f.Rect(length - ShaftCells, BandS, ShaftCells, BandN);
            public RectInt BandNFull => f.Rect(0, BandS, length, BandN);

            /// <summary>The elevator's door: local cell on the lobby side, and the cell on the shaft side (the door edge lies between).</summary>
            public (int u, int v) DoorLobbyCell => (length - ShaftCells - 1, BandS + 1);
            public (int u, int v) DoorShaftCell => (length - ShaftCells, BandS + 1);
        }

        /// <summary>The wing of a placement, if it has one.</summary>
        public static bool TryGet(in LandmarkPlacement p, float pitch, out Plan plan)
        {
            plan = default;
            if (p.kind != LandmarkKind.Atrium || p.connectors == 0 || p.annex.width <= 0) return false;
            var f = Frame.Make(p.side, p.annex);
            plan = new Plan
            {
                f = f, length = p.side < 2 ? p.annex.width : p.annex.height, pitch = pitch,
                stairs = (p.connectors & Stairs) != 0, elevator = (p.connectors & Elevator) != 0,
            };
            return true;
        }

        /// <summary>Stable id of the elevator of a placement (key for its saved state).</summary>
        public static ulong ElevatorId(in LandmarkPlacement p)
        {
            unchecked
            {
                ulong h = (ulong)(uint)p.district.x * 0x9E3779B97F4A7C15ul ^ (ulong)(uint)p.district.y * 0xC2B2AE3D27D4EB4Ful ^ (ulong)(uint)p.seed * 0x165667B19E3779F9ul;
                return h | 1ul;
            }
        }
    }
}
