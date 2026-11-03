using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    public enum RoomKind { Office, BigOffice, Hallway, Crawl, Lounge, Conference }
    public enum LightMode { AllOn, Mixed, Dark }
    public enum EdgeKind : byte { Open, Wall, Door, DoorHeader }
    public enum WallFinish { Default, Wallpaper, PaintedPlaster, ReferenceCream, ReferenceOlive, OrangePeel, SkyWallpaper }
    public enum FloorFinish { Default, ReferenceCarpet, DarkWood, Laminate, HomeCarpet }

    /// <summary>
    /// What a room does with its floor (world version 14, Docs/WorldDepthPlan.md; DepthPlan): sunk below the storey's floor
    /// with steps down at its doorways, or a pit room (narrow walkways over square shafts).
    /// </summary>
    public enum RoomFeature : byte { None, Sunken, Pit }

    /// <summary>
    /// A room worth making a base (SpecialRooms, Docs/BaseRoomsPlan.md): one way in through a wooden door, live outlets,
    /// its ceiling lights dead, its own furniture. Rare: a few per district.
    /// </summary>
    public enum GoodRoom : byte { None, StockRoom, SecurityOffice, BreakRoom, JanitorCloset }

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
        /// <summary>An emptied-out early-2000s living room (the Living rooms biome, BiomeRules.homeChance): orange-peel or
        /// clouds walls, carpet, baseboards, a flat plaster ceiling with flush-mount lights, sparse furniture.</summary>
        public bool home;
        /// <summary>Floor height above (below: negative) the storey's floor, m (DepthPlan). The ceiling stays where it was, so a
        /// sunken room is that much taller.</summary>
        public float floor;
        public RoomFeature feature;
        /// <summary>Corners cut off at 45 degrees across their corner cell (DepthPlan.Chamfers), bits in district-local terms
        /// of fullRect: 1 (xMin, zMin), 2 (xMax, zMin), 4 (xMin, zMax), 8 (xMax, zMax).</summary>
        public byte chamfers;
        /// <summary>A room worth making a base (SpecialRooms), or None.</summary>
        public GoodRoom special;
        /// <summary>Its wall outlets have power without a breaker being switched on (SpecialRooms.Live; most rooms' do not).</summary>
        public bool live;
        /// <summary>
        /// A ramp (DepthPlan): the floor runs from <see cref="floor"/> at one end of the room (fullRect) up to the storey's
        /// floor at the other: 0 none, the way it rises: 1 toward +X, 2 -X, 3 +Z, 4 -Z.
        /// </summary>
        public byte ramp;
        /// <summary>How many cells from its low end a ramp takes to rise to the storey's floor (0: the whole room); flat after.</summary>
        public int rampCells;
        /// <summary>How many cells from its low end a ramp stays level with the wing before it starts to rise.</summary>
        public int rampStart;
        /// <summary>
        /// A ceiling that comes down (DepthPlan): over the last <see cref="ceilCells"/> cells toward one end of the room it
        /// slopes from <see cref="ceiling"/> to <see cref="ceilLow"/> at that end: 0 none, the end it comes down toward: 1 +X,
        /// 2 -X, 3 +Z, 4 -Z.
        /// </summary>
        public byte ceilDrop;
        public float ceilLow;
        public int ceilCells;

        /// <summary>The floor at a point (district-local cell units): the ramp's slope if it has one.</summary>
        public float FloorAtD(float dx, float dz)
        {
            if (ramp == 0) return floor;
            var f = fullRect;
            // Cells from the low end, over the ramp's length.
            float u = (ramp == 1 ? dx - f.xMin : ramp == 2 ? f.xMax - dx : ramp == 3 ? dz - f.yMin : f.yMax - dz) - rampStart;
            float len = rampCells > 0 ? rampCells : (ramp <= 2 ? f.width : f.height);
            return Mathf.Lerp(floor, 0f, Mathf.Clamp01(u / len));
        }

        /// <summary>The ceiling at a point (district-local cell units): lower toward the end it comes down to, if it does.</summary>
        public float CeilingAtD(float dx, float dz)
        {
            if (ceilDrop == 0 || ceilCells <= 0) return ceiling;
            var f = fullRect;
            float u = ceilDrop == 1 ? f.xMax - dx : ceilDrop == 2 ? dx - f.xMin : ceilDrop == 3 ? f.yMax - dz : dz - f.yMin;
            return Mathf.Lerp(ceilLow, ceiling, Mathf.Clamp01(u / ceilCells));
        }
        public bool Walkable => kind != RoomKind.Crawl;
        public bool Tall => ceiling > OfficeBiome.CeilingHeight + 0.01f;
        /// <summary>A flat plaster ceiling with round fittings (tall rooms, and homes) instead of drop tiles and troffers.</summary>
        public bool PlasterCeiling => Tall || home;

        public Room CloneFor(int newId, RectInt newRect) => new Room
        {
            id = newId, globalId = globalId, rect = newRect, fullRect = fullRect, kind = kind, lights = lights, color = color,
            ceiling = ceiling, painted = painted, wallFinish = wallFinish, floorFinish = floorFinish, landmark = landmark, openAbove = openAbove, noFloor = noFloor, noDoors = noDoors, role = role, custom = custom, spiral = spiral,
            floor = floor, feature = feature, chamfers = chamfers, ramp = ramp, rampCells = rampCells, rampStart = rampStart, ceilDrop = ceilDrop, ceilLow = ceilLow, ceilCells = ceilCells,
            special = special, live = live, home = home,
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
        public bool flush;            // a home's round flush-mount fitting on a normal-height plaster ceiling (dome: its model)
        public float height;          // ceiling height at the fixture (above this storey's floor)
        public bool faulty;           // tube that occasionally stutters
        /// <summary>Build mode's light moods: 0 as made, 1 dim, 2 sickly green, 3 yellowed, 4 cold blue, 5 pink.</summary>
        public byte mood;
        /// <summary>Build mode: the fitting buzzes (a sound at the light).</summary>
        public bool buzz;
    }

    public struct Pillar { public Vector2Int vertex; public float size; public int roomColor; }

    /// <summary>Shape of a roof opening over one cell (Poolrooms). Neighbouring Full cells join into one big opening.</summary>
    public enum SkylightShape : byte { None, Full, SlotX, SlotZ, Square, Round, Disc }

    /// <summary>The Poolrooms' larger pieces (2026-10-31, the architecture plan): what they are.</summary>
    public enum PoolFeatureKind : byte { FreePool, Slope, Vault, RoundHall, Gallery, Spout, Cascade, Ladder, Changing, Showers, Bleachers, Lifeguard, Slide,
        Mushroom, Arch, SlideTower, WaveMaker, Walkway }

    /// <summary>
    /// One of the Poolrooms' larger pieces, in layout-local metres (x, z). Planned per district (PoolPlan), sliced to every
    /// chunk it reaches (WorldGenerator.Slice). Meaning of the fields by kind:
    ///  - FreePool: a pool in a deck room: centre, size (half extents), radius (corners, or the circle), shape (0 rounded
    ///    rectangle, 1 circle, 2 kidney), yaw; depth at the shallow end, depth2 at the deep end (along local +x).
    ///  - Slope: a whole-room basin's floor sloping from depth (shallow) to depth2 (deep) along yaw's direction over bounds.
    ///  - Vault: a barrel vault over a corridor: bounds, yaw 0 (along x) or 90 (along z), radius, height (its spring).
    ///  - RoundHall: centre, radius (the round wall), size.x (the round roof opening's radius).
    ///  - Gallery: a walkway round a tall hall: bounds (the room), height (its floor), shape (the side its stair is on),
    ///    centre (the stair's foot), size.x (its run).
    ///  - Spout / Cascade: centre (the point on the wall face), yaw (facing into the room), height (of the slot), size.x
    ///    (half its width); a cascade's steps run size.x along the wall.
    ///  - Ladder, Lifeguard, Slide, Bleachers, Changing, Showers: centre, yaw, size (extent), shape (variant).
    ///  - The water park (2026-11-01, PoolPlan.WaterPark): FreePool shape 3 is a ring (the lazy river), height its width.
    ///    Mushroom: centre, radius (its cap), height (the cap's rim), shape (colour). Arch: centre (the nozzle on the wall
    ///    face), yaw (into the room), size.x (how far it throws), height (its peak). SlideTower: centre (the tower), yaw
    ///    (toward its pool), size.x (to the pool's middle), height (the platform), shape (colour 0-3, 4: curls the other
    ///    way). WaveMaker: centre (the deep end's wall), yaw (toward the beach), size.x (half the chambers' width).
    ///  - Walkway (2026-11-01, the channels): a tiled walkway curving through a flooded hall, from centre (on a doorway's line;
    ///    size the way in) to to (toNormal the way in there; shape 1: it ends on another walkway), meandering radius either side
    ///    of the straight way in height half waves; bounds the hall.
    /// </summary>
    public struct PoolFeature
    {
        public PoolFeatureKind kind;
        public Rect bounds;
        public Vector2 centre, size;
        public float radius, height, yaw, depth, depth2;
        public int shape, room;
        /// <summary>A walkway's far end and the way into the room there (centre and size are its near end's).</summary>
        public Vector2 to, toNormal;

        public void Shift(Vector2 d) { bounds.position += d; centre += d; to += d; }
    }

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
        /// <summary>A high window (sky beyond) in this cell's edge: 1 the -X (west) edge, 2 the -Z (south) edge.</summary>
        public byte windows;
        /// <summary>Basin floor depth below the deck at the cell's middle (m); 0: the usual (PoolBiome.BasinDepth).</summary>
        public float depth;
        /// <summary>A gallery's floor over this cell, metres above the deck (0: none).</summary>
        public float gallery;
        /// <summary>Lane lines on the pool floor: 0 none, 1 running along X, 2 along Z; a drain in the middle of the cell.</summary>
        public byte lanes;
        public bool drain;
        /// <summary>Walled off behind a round hall's wall: not part of the Poolrooms the light and the water know.</summary>
        public bool solid;
        /// <summary>A round hall's round roof opening (SkylightShape.Disc): its centre in half cells from this cell's (0, 0)
        /// corner, and its radius (m).</summary>
        public sbyte discX, discZ;
        public float discR;
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

    /// <summary>
    /// A flight of steps up into a wall, going nowhere (DepthPlan): it rises from the room toward the wall on the cell's
    /// side, its top step against the wall. Layout-local.
    /// </summary>
    public struct NowhereStairs
    {
        public Vector2Int cell;
        public int side;
        public int steps;
    }

    /// <summary>
    /// A steel walkway along one wall of a tall hall (DepthPlan): the strip of cells against the wall on <see cref="side"/>
    /// from <see cref="start"/> for <see cref="length"/> cells (along Z for sides 0 and 1, along X for 2 and 3), its deck
    /// <see cref="height"/> up, reached by a stair up the first <see cref="stairCells"/> cells of the strip from the min end
    /// (or the max end). Layout-local.
    /// </summary>
    public struct Mezzanine
    {
        public int side;
        public Vector2Int start;
        public int length, stairCells;
        public bool stairAtMax;
        public float height;
        public int roomColor;

        public bool AlongZ => side < 2;
        /// <summary>The strip's cell number i (0 at start).</summary>
        public Vector2Int Cell(int i) => AlongZ ? new Vector2Int(start.x, start.y + i) : new Vector2Int(start.x + i, start.y);
        /// <summary>Is cell i of the strip part of the stair (else the deck)?</summary>
        public bool IsStair(int i) => stairAtMax ? i >= length - stairCells : i < stairCells;
    }

    /// <summary>A half-height partition along cell edges. Horizontal runs along X at line z. Railings are slimmer.</summary>
    public struct HalfWall { public bool alongX; public int line; public int start; public int length; public int roomColor; public bool railing; }

    /// <summary>Electrical plate on a wall face. Face is the cell side the plate is visible from.</summary>
    public struct WallPlate { public Vector2Int cell; public int side; public float t; public float height; public bool isSwitch; public int roomColor; public bool live; }

    public enum PropKind
    {
        Sofa, FloorLamp, ConferenceTable, WallClock, StorageRack, Box, Pallet, Armchair, OfficeChair,
        // The good rooms' furniture (SpecialRooms; built in OfficeChunkBuilder.Special): position is the middle of the foot,
        // yaw turns the front (local +Z) toward the room, size is width x height x depth.
        Counter, Fridge, Shelving, Desk, Monitor, BreakerPanel, MopSink, Microwave, DiningTable, DeskLamp,
        /// <summary>Build mode: a model the player brought in (.glb / .obj, <see cref="Prop.model"/>), static scenery.</summary>
        Custom,
        /// <summary>A children's road rug lying on a living room's floor (DistrictGenerator.PlaceRugs, OfficeChunkBuilder.Rug):
        /// position is its middle, yaw turns its long side (local X), size is length x thickness x width, variant its own dice
        /// (where the print starts, the binding's colour, a corner come up). Fixed in place.</summary>
        Rug,
    }

    /// <summary>Level 0's kinds of room (DistrictGenerator.StyleOf): the backrooms (wallpaper), living rooms (painted,
    /// furnished), the warehouse (concrete floors, racks).</summary>
    public enum RoomStyle { Backrooms, LivingRoom, Warehouse }

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
        // Custom rooms (build mode): lifted off the floor, tipped over, scaled, and stuck where it is (never moves; may be
        // half inside a wall). All zero / one / false for the generator's own props.
        public float elevation, pitch, roll;
        public float scale;           // 0 means 1
        public bool pinned;
        /// <summary>Build mode's catalog pick: which model (a sofa's or armchair's prefab index + 1, <see cref="FloralSofaStyle"/>
        /// for the floral sofa); 0 lets the dice choose as for the generator's own.</summary>
        public int style;
        public const int FloralSofaStyle = 100;
        /// <summary>A model the player brought in (<see cref="PropKind.Custom"/>): its file name in CustomModels.</summary>
        public string model;
    }

    /// <summary>
    /// A rectangular hole through a wall line (custom rooms, build mode): a doorway of any width and height, an arch, or
    /// a window (a sill above the floor). It may span several edges and the posts between them; the chunk builder builds the
    /// wall round it (OfficeChunkBuilder.Cutouts). The edges under a doorway are Door (open to light, sound and walking);
    /// under a window they stay Wall.
    /// </summary>
    public struct WallCutout
    {
        public bool vertical;         // on the line X = line (else the line Z = line)
        public int line;
        public float from, to;        // along the line, layout-local metres
        public float sill, top;       // metres above the floor (sill 0: a doorway)
        public bool arch;             // round-topped: a half circle across its width up to top
    }

    /// <summary>Build mode: a mark painted on one face of a wall line (a water stain, a damp patch, mould, peeling paper, a
    /// faded notice, an outlet or a switch). side: +1 the face toward +X / +Z, -1 the other.</summary>
    public struct WallDecal
    {
        public bool vertical;
        public int line, side, kind, seed;
        public float along, height, size;      // layout-local metres along the line; centre height; half size
    }

    /// <summary>Build mode: a sound that plays there all the time (a hum, a buzz, a drip...). Layout-local metres.</summary>
    public struct RoomSound
    {
        public Vector3 position;
        public int kind;
        public float volume, range;
    }

    /// <summary>Build mode: when the player walks into these cells, something happens (lights cut out, flicker, a door slams,
    /// a sound). cells: layout-local. A door to slam is a cutout (its hole gets a door leaf that swings shut).</summary>
    public struct RoomTrigger
    {
        public RectInt cells;
        public ulong id;
        public int action, sound;
        public bool once;
        public float delay;
        public bool hasDoor;
        public WallCutout door;
    }

    /// <summary>Build mode: two doorways linked (walk through one and you come out of the other). Layout-local; built by the
    /// chunk door a is in.</summary>
    public struct DoorLink
    {
        public WallCutout a, b;
        public bool flip;
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
        public readonly List<WallCutout> cutouts = new List<WallCutout>();
        public readonly List<WallDecal> decals = new List<WallDecal>();
        public readonly List<RoomSound> sounds = new List<RoomSound>();
        public readonly List<RoomTrigger> triggers = new List<RoomTrigger>();
        public readonly List<DoorLink> links = new List<DoorLink>();
        /// <summary>Cells of a custom room's plan that are not part of it (an L-shaped room's notch): given back to the world.</summary>
        public readonly List<RectInt> outsideCells = new List<RectInt>();
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
        /// <summary>Stairs going nowhere and mezzanines (DepthPlan), layout-local.</summary>
        public readonly List<NowhereStairs> nowhere = new List<NowhereStairs>();
        public readonly List<Mezzanine> mezzanines = new List<Mezzanine>();
        /// <summary>District-local cell of this block's cell (0, 0): 0 for a district plan, the chunk's place in it for a slice.</summary>
        public Vector2Int origin;
        /// <summary>The floor above (below) the storey's floor at the middle of a cell.</summary>
        public float FloorAt(int x, int z) => FloorAtPoint(x + 0.5f, z + 0.5f);
        /// <summary>The floor at a point (layout-local cell units), in the room of the cell it lies in (a ramp slopes).</summary>
        public float FloorAtPoint(float x, float z) => RoomAtPoint(x, z).FloorAtD(origin.x + x, origin.y + z);
        /// <summary>The floor of a given room at a point (layout-local cell units): for points on its edges.</summary>
        public float FloorOf(Room r, float x, float z) => r.FloorAtD(origin.x + x, origin.y + z);
        /// <summary>The ceiling of a given room at a point (layout-local cell units).</summary>
        public float CeilingOf(Room r, float x, float z) => r.CeilingAtD(origin.x + x, origin.y + z);
        /// <summary>The lowest ceiling over a cell (where a ceiling comes down, its low side).</summary>
        public float CeilingLowAt(int x, int z)
        {
            var r = RoomAt(x, z);
            if (r.ceilDrop == 0) return r.ceiling;
            return Mathf.Min(Mathf.Min(CeilingOf(r, x, z), CeilingOf(r, x + 1, z)), Mathf.Min(CeilingOf(r, x, z + 1), CeilingOf(r, x + 1, z + 1)));
        }
        Room RoomAtPoint(float x, float z) => rooms[roomOf[Mathf.Clamp(Mathf.FloorToInt(x), 0, N - 1), Mathf.Clamp(Mathf.FloorToInt(z), 0, N - 1)]];
        /// <summary>A cell with the plain flat floor (no sunken room, no pit room): crawl holes, noclip doors and doors only here.</summary>
        public bool Plain(int x, int z) { var r = rooms[roomOf[x, z]]; return r.feature == RoomFeature.None && Mathf.Abs(r.floor) < 0.001f; }
        /// <summary>Poolrooms: wide round-arched openings. Per edge (same layout as vEdges / hEdges): 0 = none, else
        /// (index in the run + 1) | (run length in cells &lt;&lt; 8). The run is a row of open edges between two rooms.</summary>
        public ushort[,] spanV, spanH;
        public readonly List<PoolArc> arcs = new List<PoolArc>();
        /// <summary>The Poolrooms' larger pieces (free pools, vaults, round halls, galleries, spouts, fittings), layout-local.</summary>
        public readonly List<PoolFeature> poolFeatures = new List<PoolFeature>();

        /// <summary>The pool floor at a point (layout-local metres): below the deck (negative), from the pieces that shape it.</summary>
        public float BasinFloorAt(float mx, float mz)
        {
            var p = new Vector2(mx, mz);
            foreach (var f in poolFeatures)
            {
                if (f.kind != PoolFeatureKind.Slope && f.kind != PoolFeatureKind.FreePool) continue;
                if (!f.bounds.Contains(p)) continue;
                return -PoolBiome.SlopedDepth(f, p);
            }
            return -PoolBiome.BasinDepth;
        }

        /// <summary>Is a point (layout-local metres) over water: a free pool's shape where there is one, else its cell's basin.</summary>
        public bool WaterAtPoint(float mx, float mz)
        {
            var p = new Vector2(mx, mz);
            foreach (var f in poolFeatures)
                if (f.kind == PoolFeatureKind.FreePool && f.bounds.Contains(p)) return PoolBiome.ShapeSD(f, p) < 0f;
            int x = Mathf.FloorToInt(mx / OfficeBiome.CellSize), z = Mathf.FloorToInt(mz / OfficeBiome.CellSize);
            if (pool == null || !InChunk(x, z)) return false;
            if (FreePoolRoom(x, z)) return false;
            return pool[x, z].basin;
        }

        /// <summary>Is this cell in a room with a free pool (its floor is built from the pool's shape)?</summary>
        public bool FreePoolRoom(int x, int z)
        {
            int gid = RoomAt(x, z).globalId;
            foreach (var f in poolFeatures) if (f.kind == PoolFeatureKind.FreePool && f.room == gid) return true;
            return false;
        }

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
