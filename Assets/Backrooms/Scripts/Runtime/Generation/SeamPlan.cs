using System.Collections.Generic;
using Backrooms.CustomRooms;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Where seams lead (2026-11-07, Docs/ContractFloorsPlan.md): a sealed patch of the map like the hub's (3 x 3 districts,
    /// nothing but void round what is built), 350 m north of the hub, holding the four real-world places (SeamRooms), one
    /// to a chunk of its middle district. Each has a tear in its south wall: an arrival noclip door (NoclipTravel), so it
    /// leads back to the seam you came in by. Every contract floor's seams of a kind lead to the same room: what is in it
    /// (the job's boxes) is the contracts' business (Interaction.Contracts).
    /// </summary>
    public static class SeamPlan
    {
        public static readonly Vector2Int District = new Vector2Int(88, 6);
        public const string ZoneName = "Seams";
        const float C = OfficeBiome.CellSize;
        const int DC = WorldGenSettings.DistrictChunks;

        public static bool InZone(Vector2Int district) => Mathf.Abs(district.x - District.x) <= 1 && Mathf.Abs(district.y - District.y) <= 1;
        public static bool ChunkInZone(Vector2Int chunk) => InZone(WorldGenSettings.DistrictOf(chunk));
        public static bool InZone(Vector3 p) => ChunkInZone(new Vector2Int(Mathf.FloorToInt(p.x / OfficeBiome.ChunkSize), Mathf.FloorToInt(p.z / OfficeBiome.ChunkSize)));

        /// <summary>A kind's room (cells in the middle district): one to a chunk.</summary>
        public static RectInt Room(int kind) => new RectInt(14 + (kind % 2) * 12, 14 + (kind / 2) * 12, SeamRooms.W, SeamRooms.D);

        /// <summary>Is this chunk one a seam room stands in?</summary>
        public static bool IsRoomChunk(Vector2Int chunk)
        {
            if (WorldGenSettings.DistrictOf(chunk) != District) return false;
            int cx = chunk.x - District.x * DC, cz = chunk.y - District.y * DC;
            return (cx == 1 || cx == 2) && (cz == 1 || cz == 2);
        }

        /// <summary>A point in a kind's room, given in blueprint metres from its south-west corner (y up).</summary>
        public static Vector3 World(int kind, float x, float z, float y = 0f)
        {
            var r = Room(kind);
            return new Vector3((District.x * WorldGenSettings.DistrictCells + r.xMin) * C + x, y, (District.y * WorldGenSettings.DistrictCells + r.yMin) * C + z);
        }

        /// <summary>The kind whose room this point is in, or -1.</summary>
        public static int KindAt(Vector3 p)
        {
            for (int k = 0; k < 4; k++)
            {
                var a = World(k, 0f, 0f);
                if (p.x >= a.x && p.z >= a.z && p.x < a.x + SeamRooms.W * C && p.z < a.z + SeamRooms.D * C) return k;
            }
            return -1;
        }

        /// <summary>The tear in a kind's room: the middle of its south wall line, and out of it into the room.</summary>
        public static void Tear(int kind, out Vector3 position, out Vector3 normal)
        {
            position = World(kind, SeamRooms.TearX, 0f);
            normal = Vector3.forward;
        }

        /// <summary>Where the job's box i stands (the old 0.5 x 0.36 x 0.4 m box): on the pickup pallet, two across and two
        /// deep, three high; past twelve, a second stack on the floor beside it.</summary>
        public static Vector3 Stack(int kind, int i) => Stack(kind, i, new Vector3(0.5f, 0.36f, 0.4f));

        /// <summary>
        /// As above for a box this size (2026-11-08: each client's boxes have their own shape, Contracts.BoxShapes): as
        /// many across and deep as fit the pallet (1.2 x 1.0 m), as many layers as stay under about 1.2 m; past a pile's
        /// worth, the next pile beside it on the floor.
        /// </summary>
        public static Vector3 Stack(int kind, int i, Vector3 size)
        {
            Grid(size, out int across, out int deep, out int layers);
            int perLayer = across * deep, cap = perLayer * layers;
            int pile = i / cap, j = i % cap, k = j % perLayer, layer = j / perLayer;
            var p = SeamRooms.Pickup;
            float baseY = pile == 0 ? SeamRooms.PalletTop : 0.005f;
            float x = (k % across - (across - 1) * 0.5f) * (size.x + 0.06f);
            float z = (k / across - (deep - 1) * 0.5f) * (size.z + 0.06f);
            return World(kind, p.x + pile * 1.3f + x, p.y + z, baseY + layer * (size.y + 0.01f));
        }

        /// <summary>How many boxes this size make one pile (Stack).</summary>
        public static int PileOf(Vector3 size) { Grid(size, out int a, out int d, out int l); return a * d * l; }

        static void Grid(Vector3 size, out int across, out int deep, out int layers)
        {
            across = Mathf.Max(1, Mathf.FloorToInt(1.18f / (size.x + 0.06f)));
            deep = Mathf.Max(1, Mathf.FloorToInt(0.98f / (size.z + 0.06f)));
            layers = Mathf.Clamp(Mathf.FloorToInt(1.2f / (size.y + 0.01f)), 1, 8);
        }

        /// <summary>The DELIVERIES bay in a kind's room (world x, z).</summary>
        public static Rect Bay(int kind)
        {
            var b = SeamRooms.Bay; var o = World(kind, b.xMin, b.yMin);
            return new Rect(o.x, o.z, b.width, b.height);
        }

        // ---------------- The plan ----------------

        public static OfficeLayout Generate(WorldGenSettings set, int seed, Vector2Int district, int storey)
        {
            int n = WorldGenSettings.DistrictCells;
            var L = new OfficeLayout(n, district, storey) { storeyPitch = set.storeyPitch, zoneName = ZoneName, biomeName = "Offices" };
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++) L.roomOf[x, z] = -1;
            var voids = new List<Room>();
            bool here = district == District && storey == 0;
            // The rooms first (their cells claimed), then the void filling the rest, one room a free cell run.
            var placements = new List<(LandmarkPlacement p, List<Room> rooms)>();
            if (here)
                for (int k = 0; k < 4; k++)
                {
                    var rect = Room(k);
                    var placement = new LandmarkPlacement
                    {
                        name = LeaseSeams.Kinds[k], kind = LandmarkKind.Custom, district = district, rect = rect, core = rect, storeys = 1,
                        seed = 0x5EA0 + k, blueprint = SeamRooms.Id(k), variant = 0, rot = 0,
                    };
                    var specs = new List<Landmarks.RoomSpec>();
                    CustomRoomStamp.Rooms(placement, specs);
                    var rooms = new List<Room>();
                    foreach (var spec in specs)
                    {
                        var room = DistrictGenerator.MakeRoom(L, spec.rect, spec.kind);
                        room.ceiling = spec.ceiling; room.painted = spec.painted; room.lights = spec.lights;
                        room.wallFinish = spec.wallFinish; room.floorFinish = spec.floorFinish;
                        room.noDoors = true; room.landmark = placement.name; room.custom = true;
                        rooms.Add(room);
                    }
                    L.landmarks.Add(placement);
                    placements.Add((placement, rooms));
                }
            // Void: every unclaimed run of cells along x, row by row (merged into rectangles where rows match).
            for (int z = 0; z < n; z++)
            {
                int x = 0;
                while (x < n)
                {
                    if (L.roomOf[x, z] >= 0) { x++; continue; }
                    int x1 = x; while (x1 < n && L.roomOf[x1, z] < 0) x1++;
                    int z1 = z + 1;
                    bool fits = true;
                    while (z1 < n && fits)
                    {
                        for (int xx = x; xx < x1 && fits; xx++) if (L.roomOf[xx, z1] >= 0) fits = false;
                        if (fits) z1++;
                    }
                    var room = DistrictGenerator.MakeRoom(L, new RectInt(x, z, x1 - x, z1 - z), RoomKind.Hallway);
                    room.lights = LightMode.Dark; room.custom = true; room.noDoors = true; room.painted = true; room.wallFinish = WallFinish.PaintedPlaster;
                    voids.Add(room);
                    x = x1;
                }
            }
            bool IsVoid(int id) => voids.Exists(v => v.id == id);
            for (int x = 0; x <= n; x++)
            for (int z = 0; z < n; z++)
                L.vEdges[x, z] = x == 0 || x == n || (L.roomOf[x - 1, z] != L.roomOf[x, z] && !(IsVoid(L.roomOf[x - 1, z]) && IsVoid(L.roomOf[x, z]))) ? EdgeKind.Wall : EdgeKind.Open;
            for (int x = 0; x < n; x++)
            for (int z = 0; z <= n; z++)
                L.hEdges[x, z] = z == 0 || z == n || (L.roomOf[x, z - 1] != L.roomOf[x, z] && !(IsVoid(L.roomOf[x, z - 1]) && IsVoid(L.roomOf[x, z]))) ? EdgeKind.Wall : EdgeKind.Open;
            foreach (var r in L.rooms) r.color = r.id;
            foreach (var (p, rooms) in placements)
            {
                CustomRoomStamp.Connect(L, p, rooms);
                CustomRoomStamp.Decorate(L, p, rooms);
            }
            // The tears: arrival doors (they lead back to the seam you came in by).
            if (here)
                for (int k = 0; k < 4; k++)
                {
                    var r = Room(k);
                    var cell = new Vector2Int(r.xMin + 3, r.yMin);
                    NoclipDoors.Frame(L, cell, 2, out var pos, out var nrm);
                    L.noclipDoors.Add(new NoclipDoor
                    {
                        cell = cell, side = 2, position = pos, normal = nrm, arrival = true, seam = true,
                        id = WorldGenerator.PropId(L.coord, L.storey, 0xE500 + k),
                    });
                }
            DistrictGenerator.AssignColors(L);
            for (int i = 0; i < L.props.Count; i++) { var p = L.props[i]; p.id = WorldGenerator.PropId(L.coord, storey, i); L.props[i] = p; }
            return L;
        }
    }
}
