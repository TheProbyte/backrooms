using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    public enum RoomKind { Office, BigOffice, Hallway, Crawl, Lounge, Conference }
    public enum LightMode { AllOn, Mixed, Dark }
    public enum EdgeKind : byte { Open, Wall, Door, DoorHeader }

    public class Room
    {
        public int id;
        public RectInt rect;          // in chunk-local cells
        public RoomKind kind;
        public LightMode lights;
        public int color;             // 0..6, rendering-layer colour so bounce light stays in its room
        public float ceiling = OfficeBiome.CeilingHeight;   // tall rooms have plaster ceilings and dome lights
        public bool painted;          // painted plaster walls instead of wallpaper
        public bool Walkable => kind != RoomKind.Crawl;
        public bool Tall => ceiling > OfficeBiome.CeilingHeight + 0.01f;
    }

    public struct Fixture
    {
        public Vector2Int cell;       // chunk-local cell
        public bool on;
        public int roomColor;
        public bool dome;             // round surface-mounted fitting on a tall plaster ceiling
        public bool strip;            // warehouse: cool-white twin-tube batten hung on chains
        public float height;          // ceiling height at the fixture
        public bool faulty;           // tube that occasionally stutters
    }

    public struct Pillar { public Vector2Int vertex; public float size; public int roomColor; }

    /// <summary>A half-height partition along cell edges. Horizontal runs along X at line z.</summary>
    public struct HalfWall { public bool alongX; public int line; public int start; public int length; public int roomColor; }

    /// <summary>Electrical plate on a wall face. Face is the cell side the plate is visible from.</summary>
    public struct WallPlate { public Vector2Int cell; public int side; public float t; public float height; public bool isSwitch; public int roomColor; }

    public enum PropKind { Sofa, FloorLamp, ConferenceTable, WallClock, StorageRack }
    public struct Prop
    {
        public PropKind kind;
        public Vector2 position;      // chunk-local metres (XZ)
        public float yaw;             // degrees
        public Vector3 size;          // footprint / dimensions
        public int variant;
        public bool lit;
        public int roomColor;
    }

    /// <summary>
    /// Pure-data description of one chunk. Cell (x, z) spans [x, x+1) x [z, z+1) in cell units.
    /// vEdges[x, z] is the edge on the line X = x between cells (x-1, z) and (x, z): size (N+1) x N.
    /// hEdges[x, z] is the edge on the line Z = z between cells (x, z-1) and (x, z): size N x (N+1).
    /// Border edges (x == 0 / N or z == 0 / N) are shared with the neighbouring chunk; both chunks
    /// derive the same openings from the edge's world key, so connections always line up.
    /// </summary>
    public class OfficeLayout
    {
        public readonly int N;
        public readonly Vector2Int coord;
        public readonly int[,] roomOf;
        public readonly EdgeKind[,] vEdges, hEdges;
        public readonly List<Room> rooms = new List<Room>();
        public readonly List<Fixture> fixtures = new List<Fixture>();
        public readonly List<Pillar> pillars = new List<Pillar>();
        public readonly List<HalfWall> halfWalls = new List<HalfWall>();
        public readonly List<WallPlate> plates = new List<WallPlate>();
        public readonly List<Prop> props = new List<Prop>();
        /// <summary>Biome rules this chunk was generated with (blended across biome transitions).</summary>
        public BiomeRules rules = BiomeRules.Offices();
        public string biomeName = "Offices";

        public OfficeLayout(int n, Vector2Int coord)
        {
            N = n; this.coord = coord;
            roomOf = new int[n, n];
            vEdges = new EdgeKind[n + 1, n];
            hEdges = new EdgeKind[n, n + 1];
        }

        public Room RoomAt(int x, int z) => rooms[roomOf[x, z]];
        public float CeilingAt(int x, int z) => rooms[roomOf[x, z]].ceiling;
        public bool InChunk(int x, int z) => x >= 0 && z >= 0 && x < N && z < N;

        /// <summary>Edge on the given side of a cell. Side: 0 = -X, 1 = +X, 2 = -Z, 3 = +Z.</summary>
        public EdgeKind EdgeOf(int x, int z, int side)
        {
            switch (side)
            {
                case 0: return vEdges[x, z];
                case 1: return vEdges[x + 1, z];
                case 2: return hEdges[x, z];
                default: return hEdges[x, z + 1];
            }
        }

        public void SetEdge(int x, int z, int side, EdgeKind k)
        {
            switch (side)
            {
                case 0: vEdges[x, z] = k; break;
                case 1: vEdges[x + 1, z] = k; break;
                case 2: hEdges[x, z] = k; break;
                default: hEdges[x, z + 1] = k; break;
            }
        }

        public static bool IsSolid(EdgeKind k) => k == EdgeKind.Wall;
        public static bool IsPassable(EdgeKind k) => k != EdgeKind.Wall;
    }
}
