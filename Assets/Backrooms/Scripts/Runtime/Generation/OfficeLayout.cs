using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    public enum RoomKind { Office, BigOffice, Hallway, Crawl, Lounge, Conference }
    public enum LightMode { AllOn, Mixed, Dark }
    public enum EdgeKind : byte { Open, Wall, Door, DoorHeader }
    public enum WallFinish { Default, Wallpaper, PaintedPlaster, ReferenceCream, ReferenceOlive }
    public enum FloorFinish { Default, ReferenceCarpet, DarkWood, Laminate }

    /// <summary>
    /// What a room does with its floor (world version 14, Docs/WorldDepthPlan.md; DepthPlan): sunk below the storey's floor
    /// with steps down at its doorways, or a pit room (narrow walkways over square shafts).
    /// </summary>
    public enum RoomFeature : byte { None, Sunken, Pit }

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
        public WallFinish wallFinish; // Default preserves the legacy painted flag
        public FloorFinish floorFinish;
        public string landmark;       // name of the landmark this room belongs to, if any
        public bool openAbove;        // continues into the storey above (no ceiling here; walls stop at the storey top)
        public bool noFloor;          // upper part of a volume from the storey below (no floor, no fixtures)
        public bool noDoors;          // no random doors (stair halls and elevator shafts get their doors from their landmark)
        public AnnexRole role;        // stair hall, shaft ... (see AtriumAnnex)
        public bool custom;           // part of a player-built room: its own lights, doors and dressing (see CustomRooms)
        public bool spiral;           // Poolrooms: the tower of a spiral stair (storey 0: the stair; storey 1: its well and landing)
        /// <summary>Floor height above (below: negative) the storey's floor, m (DepthPlan). The ceiling stays where it was, so a
        /// sunken room is that much taller.</summary>
        public float floor;
        public RoomFeature feature;
        /// <summary>Corners cut off at 45 degrees across their corner cell (DepthPlan.Chamfers), bits in district-local terms
        /// of fullRect: 1 (xMin, zMin), 2 (xMax, zMin), 4 (xMin, zMax), 8 (xMax, zMax).</summary>
        public byte chamfers;
        public bool Walkable => kind != RoomKind.Crawl;
        public bool Tall => ceiling > OfficeBiome.CeilingHeight + 0.01f;

        public Room CloneFor(int newId, RectInt newRect) => new Room
        {
            id = newId, globalId = globalId, rect = newRect, fullRect = fullRect, kind = kind, lights = lights, color = color,
            ceiling = ceiling, painted = painted, wallFinish = wallFinish, floorFinish = floorFinish, landmark = landmark, openAbove = openAbove, noFloor = noFloor, noDoors = noDoors, role = role, custom = custom, spiral = spiral,
            floor = floor, feature = feature, chamfers = chamfers,
        };
    }

    public struct Fixture
    {
        public Vector2Int cell;       // layout-local cell
        public bool on;
        public int roomColor;
        /// <summary>Stable id (district, storey, index in the plan): the key for saved edits such as moved props.</summary>
        public ulong id;
        public bool dome;             // round surface-mounted fitting on a tall plaster ceiling
        public bool strip;            // warehouse: cool-white twin-tube batten hung on chains
        public float height;          // ceiling height at the fixture (above this storey's floor)
        public bool faulty;           // tube that occasionally stutters
    }

    public struct Pillar { public Vector2Int vertex; public float size; public int roomColor; }

    /// <summary>Shape of a roof opening over one cell (Poolrooms). Neighbouring Full cells join into one big opening.</summary>
    public enum SkylightShape : byte { None, Full, SlotX, SlotZ, Square, Round }

    /// <summary>
    /// A free-standing curved wall in a Poolrooms hall: an arc of a circle, from one angle to another (degrees, 0 = +X,
    /// counter-clockwise seen from above), floor to ceiling, with rounded ends.
    /// </summary>
    public struct PoolArc
    {
        public Vector2 centre;        // layout-local metres (x, z)
        public float radius, from, to;
        public float top;             // height of the wall above the deck
    }

    /// <summary>Per-cell data of pool-style layouts (see PoolPlan).</summary>
    public struct PoolCell
    {
        /// <summary>Flooded: the floor is <see cref="PoolBiome.BasinDepth"/> below the deck, under still water.</summary>
        public bool basin;
        public SkylightShape skylight;
    }

    /// <summary>
    /// A square shaft in a pit room's floor (DepthPlan): layout-local metres (x, z), its depth below the storey's floor, and
    /// the block in one corner that is a step up out of it (half as high as it is deep; 0: none).
    /// </summary>
    public struct PitShaft
    {
        public Rect rect;
        public float depth;
        public float ledge;
        /// <summary>Which corner the block stands in: 0 (xMin, zMin), 1 (xMax, zMin), 2 (xMin, zMax), 3 (xMax, zMax).</summary>
        public int ledgeCorner;
        public int roomColor;
    }

    /// <summary>A half-height partition along cell edges. Horizontal runs along X at line z. Railings are slimmer.</summary>
    public struct HalfWall { public bool alongX; public int line; public int start; public int length; public int roomColor; public bool railing; }

    /// <summary>Electrical plate on a wall face. Face is the cell side the plate is visible from.</summary>
    public struct WallPlate { public Vector2Int cell; public int side; public float t; public float height; public bool isSwitch; public int roomColor; }

    public enum PropKind { Sofa, FloorLamp, ConferenceTable, WallClock, StorageRack, Box, Pallet, Armchair, OfficeChair }

    /// <summary>
    /// A crawl hole: a low square opening at the foot of a wall. The wall has a short boxed duct behind it on the other side,
    /// yet crawling in brings you out of a paired hole somewhere else (see CrawlPortals). Frames are in world space so a
    /// chunk slice knows where its partner is without looking it up.
    /// </summary>
    public struct CrawlPortal
    {
        public Vector2Int cell;        // layout-local cell the hole is entered from
        public int side;               // which side of that cell the wall is on (0 -X, 1 +X, 2 -Z, 3 +Z)
        public Vector3 position;       // world: centre of the opening, on the wall face
        public Vector3 normal;         // world: out of the wall, into the cell
        public Vector3 partnerPosition, partnerNormal;
        public ulong id;
        /// <summary>A far link (see FarLinks): the partner is this hole's counterpart in <see cref="partnerDistrict"/>, found
        /// when a chunk is cut from the plan (partnerPosition/Normal are only valid in chunk layouts).</summary>
        public bool far;
        public Vector2Int partnerDistrict;

        /// <summary>A crawl space (see CrawlPortals.PlaceSpaces): the hole is a portal to the vent of a small sealed
        /// special room built above the ceiling over it (partnerPosition/Normal are that vent).</summary>
        public bool space;
        public RectInt room;           // layout-local cells the special room lies above (the hole's cell is one of them)
        public float roomFloor;        // height of its floor above the storey's floor
        public int template;           // what kind of room (CrawlSpaces.Template)
        public int seed;               // its own dice (dressing, loot)
    }
    /// <summary>A door-sized section of a wall that can be walked through into another zone (see NoclipDoors).</summary>
    public struct NoclipDoor
    {
        public Vector2Int cell;        // layout-local cell on one side of the wall
        public int side;               // which side of that cell the wall is on (0 -X, 1 +X, 2 -Z, 3 +Z)
        public Vector3 position;       // world: middle of the wall line, at floor level
        public Vector3 normal;         // world: out of the wall into the cell
        public ulong id;
        public bool arrival;           // a Poolrooms door: leads back to the door you came in by
        public Vector2Int partnerDistrict;
        public bool resolved;          // Level 0 doors: the partner was found (chunk layouts only)
        public Vector3 partnerPosition, partnerNormal;
    }
    public struct Prop
    {
        public PropKind kind;
        public Vector2 position;      // layout-local metres (XZ)
        public float yaw;             // degrees
        public Vector3 size;          // footprint / dimensions
        public int variant;
        public bool lit;
        public int roomColor;
        /// <summary>Stable id (district, storey, index in the plan): the key for saved edits such as moved props.</summary>
        public ulong id;
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
        public readonly List<CrawlPortal> portals = new List<CrawlPortal>();
        public readonly List<NoclipDoor> noclipDoors = new List<NoclipDoor>();
        /// <summary>Biome rules of this chunk (blended across biome transitions). District plans use <see cref="RulesAt"/>.</summary>
        public BiomeRules rules = BiomeRules.Offices();
        public string biomeName = "Offices";
        public string zoneName = "Level 0";
        /// <summary>How the builder turns this layout into geometry (see <see cref="ZoneStyle"/>).</summary>
        public ZoneStyle style;
        /// <summary>Top of a headed doorway (2.1 m in offices, taller in the Poolrooms).</summary>
        public float headerHeight = OfficeBiome.DoorHeaderHeight;
        /// <summary>Pool-style layouts only: flooded cells and roof openings, [x, z].</summary>
        public PoolCell[,] pool;
        public bool Basin(int x, int z) => pool != null && pool[x, z].basin;
        /// <summary>
        /// Steps down into a sunken room (DepthPlan): per cell, the side (0 -X, 1 +X, 2 -Z, 3 +Z) whose doorway the steps rise
        /// toward, or -1; null when there are none in this block.
        /// </summary>
        public sbyte[,] steps;
        public int StepSide(int x, int z) => steps == null ? -1 : steps[x, z];
        /// <summary>Pit rooms' shafts (DepthPlan), layout-local.</summary>
        public readonly List<PitShaft> pits = new List<PitShaft>();
        /// <summary>The floor of a cell's room above (below) the storey's floor.</summary>
        public float FloorAt(int x, int z) => rooms[roomOf[x, z]].floor;
        /// <summary>A cell with the plain flat floor (no sunken room, no pit room): crawl holes, noclip doors and doors only here.</summary>
        public bool Plain(int x, int z) { var r = rooms[roomOf[x, z]]; return r.feature == RoomFeature.None && Mathf.Abs(r.floor) < 0.001f; }
        /// <summary>Poolrooms: wide round-arched openings. Per edge (same layout as vEdges / hEdges): 0 = none, else
        /// (index in the run + 1) | (run length in cells &lt;&lt; 8). The run is a row of open edges between two rooms.</summary>
        public ushort[,] spanV, spanH;
        public readonly List<PoolArc> arcs = new List<PoolArc>();

        /// <summary>The arched span an edge belongs to, if any (vertical: line X = line at z = along; else line Z = line at x = along).</summary>
        public bool ArchSpan(bool vertical, int line, int along, out int index, out int length)
        {
            index = length = 0;
            var a = vertical ? spanV : spanH;
            if (a == null) return false;
            ushort v = vertical ? a[line, along] : a[along, line];
            if (v == 0) return false;
            index = (v & 0xFF) - 1; length = v >> 8;
            return true;
        }
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
