using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    public enum RoomKind { Office, BigOffice, Hallway, Crawl, Lounge, Conference }
    public enum LightMode { AllOn, Mixed, Dark }
    public enum EdgeKind : byte { Open, Wall, Door, DoorHeader }

    /// <summary>Per-cell flags for multi-storey volumes.</summary>
    [System.Flags]
    public enum CellFlags : byte
    {
        None = 0,
        /// <summary>No floor here: the cell is the upper part of a volume that starts on a lower storey.</summary>
        NoFloor = 1,
        /// <summary>No ceiling here: the room continues into the storey above, whose ceiling closes it.</summary>
        OpenAbove = 2,
    }

    public class Room
    {
        public int id;                // index in the layout's room list
        public int globalId = -1;     // id in the district plan (same for every chunk slice of the room)
        public RectInt rect;          // in layout-local cells (clipped to the chunk for chunk slices)
        public RectInt fullRect;      // whole room in district-local cells (equal to rect in a district plan)
        public RoomKind kind;
        public LightMode lights;
        public int color;             // light-layer colour (0..27), see LightLayers
        public float ceiling = OfficeBiome.CeilingHeight;   // tall rooms have plaster ceilings and dome lights
        public bool painted;          // painted plaster walls instead of wallpaper
        public string landmark;       // name of the landmark this room belongs to, if any
        public bool openAbove;        // continues into the storey above (no ceiling here; walls stop at the storey top)
        public bool noFloor;          // upper part of a volume from the storey below (no floor, no fixtures)
        public bool Walkable => kind != RoomKind.Crawl;
        public bool Tall => ceiling > OfficeBiome.CeilingHeight + 0.01f;

        public Room CloneFor(int newId, RectInt newRect) => new Room
        {
            id = newId, globalId = globalId, rect = newRect, fullRect = fullRect, kind = kind, lights = lights, color = color,
            ceiling = ceiling, painted = painted, landmark = landmark, openAbove = openAbove, noFloor = noFloor,
        };
    }

    public struct Fixture
    {
        public Vector2Int cell;       // layout-local cell
        public bool on;
        public int roomColor;
        public bool dome;             // round surface-mounted fitting on a tall plaster ceiling
        public bool strip;            // warehouse: cool-white twin-tube batten hung on chains
        public float height;          // ceiling height at the fixture (above this storey's floor)
        public bool faulty;           // tube that occasionally stutters
    }

    public struct Pillar { public Vector2Int vertex; public float size; public int roomColor; }

    /// <summary>A half-height partition along cell edges. Horizontal runs along X at line z. Railings are slimmer.</summary>
    public struct HalfWall { public bool alongX; public int line; public int start; public int length; public int roomColor; public bool railing; }

    /// <summary>Electrical plate on a wall face. Face is the cell side the plate is visible from.</summary>
    public struct WallPlate { public Vector2Int cell; public int side; public float t; public float height; public bool isSwitch; public int roomColor; }

    public enum PropKind { Sofa, FloorLamp, ConferenceTable, WallClock, StorageRack }
    public struct Prop
    {
        public PropKind kind;
        public Vector2 position;      // layout-local metres (XZ)
        public float yaw;             // degrees
        public Vector3 size;          // footprint / dimensions
        public int variant;
        public bool lit;
        public int roomColor;
    }

    /// <summary>
    /// Pure-data description of a block of cells: a whole district plan (N = 48) or one chunk slice of it
    /// (N = 12). Cell (x, z) spans [x, x+1) x [z, z+1) in cell units.
    /// vEdges[x, z] is the edge on the line X = x between cells (x-1, z) and (x, z): size (N+1) x N.
    /// hEdges[x, z] is the edge on the line Z = z between cells (x, z-1) and (x, z): size N x (N+1).
    /// Edges on the outer lines are shared with the neighbouring block, and both blocks always hold the
    /// same kind there (district borders come from a shared contract; chunk borders inside a district
    /// are sliced from one plan).
    /// </summary>
    public class OfficeLayout
    {
        public readonly int N;
        /// <summary>Chunk coordinate (x, z) for chunk slices, district coordinate for district plans.</summary>
        public readonly Vector2Int coord;
        /// <summary>Storey (0 = ground). Storeys stack <see cref="WorldGenSettings.storeyPitch"/> metres apart.</summary>
        public readonly int storey;
        /// <summary>Height between storeys (metres); the floor of this layout is at storey * pitch.</summary>
        public float storeyPitch = 9f;
        public float BaseY => storey * storeyPitch;
        public readonly int[,] roomOf;
        public readonly EdgeKind[,] vEdges, hEdges;
        /// <summary>Round-topped doorway on this edge (only ever on headed openings).</summary>
        public readonly bool[,] archV, archH;
        public readonly CellFlags[,] flags;
        public readonly List<Room> rooms = new List<Room>();
        public readonly List<Fixture> fixtures = new List<Fixture>();
        public readonly List<Pillar> pillars = new List<Pillar>();
        public readonly List<HalfWall> halfWalls = new List<HalfWall>();
        public readonly List<WallPlate> plates = new List<WallPlate>();
        public readonly List<Prop> props = new List<Prop>();
        /// <summary>Biome rules of this chunk (blended across biome transitions). District plans use <see cref="RulesAt"/>.</summary>
        public BiomeRules rules = BiomeRules.Offices();
        public string biomeName = "Offices";
        public string zoneName = "Level 0";
        /// <summary>District plans: rules per chunk of the district, [cx, cz].</summary>
        public BiomeRules[,] chunkRules;
        public string[,] chunkBiomes;
        /// <summary>Landmarks placed in this block (district plans and their slices).</summary>
        public readonly List<LandmarkPlacement> landmarks = new List<LandmarkPlacement>();

        public OfficeLayout(int n, Vector2Int coord, int storey = 0)
        {
            N = n; this.coord = coord; this.storey = storey;
            roomOf = new int[n, n];
            vEdges = new EdgeKind[n + 1, n];
            hEdges = new EdgeKind[n, n + 1];
            archV = new bool[n + 1, n];
            archH = new bool[n, n + 1];
            flags = new CellFlags[n, n];
        }

        public Room RoomAt(int x, int z) => rooms[roomOf[x, z]];
        public float CeilingAt(int x, int z) => rooms[roomOf[x, z]].ceiling;
        public bool InChunk(int x, int z) => x >= 0 && z >= 0 && x < N && z < N;

        /// <summary>Rules that apply at a cell (per chunk in a district plan).</summary>
        public BiomeRules RulesAt(int x, int z)
        {
            if (chunkRules == null) return rules;
            int c = OfficeBiome.ChunkCells;
            return chunkRules[Mathf.Clamp(x / c, 0, chunkRules.GetLength(0) - 1), Mathf.Clamp(z / c, 0, chunkRules.GetLength(1) - 1)];
        }

        /// <summary>Rules at the centre of a rectangle of cells.</summary>
        public BiomeRules RulesAt(RectInt r) => RulesAt(r.xMin + r.width / 2, r.yMin + r.height / 2);

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

        public void SetArch(int x, int z, int side, bool arched)
        {
            switch (side)
            {
                case 0: archV[x, z] = arched; break;
                case 1: archV[x + 1, z] = arched; break;
                case 2: archH[x, z] = arched; break;
                default: archH[x, z + 1] = arched; break;
            }
        }

        public bool Has(int x, int z, CellFlags f) => (flags[x, z] & f) != 0;

        /// <summary>Top of a room's walls: its ceiling, or the storey top for a room that continues upward.</summary>
        public float WallTop(Room r) => r.openAbove ? Mathf.Min(r.ceiling, storeyPitch) : r.ceiling;

        public static bool IsSolid(EdgeKind k) => k == EdgeKind.Wall;
        public static bool IsPassable(EdgeKind k) => k != EdgeKind.Wall;
    }
}
