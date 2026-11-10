using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Plans a whole district (4 x 4 chunks, 48 x 48 cells) of one storey at once: rooms, hallways,
    /// connections, open-plan areas, light zones, tall spaces and dressing. A district depends only on the
    /// seed, the settings, the zone and biome fields and the landmark grid, never on other districts or on
    /// generation order. Neighbouring districts agree only on their shared edges: the openings on a district
    /// edge (positions, widths, headers) come from a hash of that edge, per chunk-length segment.
    /// Storeys agree on multi-storey volumes the same way: through landmarks both storeys look up.
    ///
    /// Rules that keep the world sealed and connected:
    ///  - District edges are walls except for the shared-edge openings.
    ///  - Every walkable room is connected to every other walkable room inside the district (verified by
    ///    flood fill and repaired), so every edge opening reaches every other one.
    ///  - Cells next to an edge opening are always walkable.
    ///  - Crawl spaces are dead-end slots too narrow for the player and never carry connectivity.
    /// Streaming chunks are slices of the plan (see <see cref="WorldGenerator"/>).
    /// </summary>
    public static partial class DistrictGenerator
    {
        public const int MinRoom = 2;
        const int ChunkN = OfficeBiome.ChunkCells, DC = WorldGenSettings.DistrictChunks;

        /// <summary>Plans district (x, z) of a storey.</summary>
        public static OfficeLayout Generate(WorldGenSettings set, int seed, Vector2Int district, int storey)
        {
            int n = WorldGenSettings.DistrictCells;
            var L = new OfficeLayout(n, district, storey) { storeyPitch = set.storeyPitch };
            // Rules per chunk: biomes blend across the district.
            L.chunkRules = new BiomeRules[DC, DC];
            L.chunkBiomes = new string[DC, DC];
            for (int cx = 0; cx < DC; cx++)
            for (int cz = 0; cz < DC; cz++)
            {
                var sm = set.Evaluate(seed, new Vector2Int(district.x * DC + cx, district.y * DC + cz));
                L.chunkRules[cx, cz] = sm.rules; L.chunkBiomes[cx, cz] = sm.Dominant;
                if (cx == DC / 2 && cz == DC / 2) { L.rules = sm.rules; L.biomeName = sm.Dominant; L.zoneName = sm.zone; }
            }
            int sseed = StoreySeed(seed, storey);
            var rng = new System.Random(Hash(sseed, district.x, district.y, 0xD157));
            var zoneHit = set.ZoneAt(seed, district);
            L.style = set.zones[Mathf.Clamp(zoneHit.zone, 0, set.zones.Count - 1)].style;
            bool pool = L.style == ZoneStyle.Pool;
            if (pool) { L.headerHeight = PoolBiome.HeaderHeight; L.pool = new PoolCell[n, n]; }

            // 1. Edge contracts first: they constrain where crawl spaces may go. Each chunk-length segment
            //    of a district edge is keyed like the old chunk borders, so both districts agree.
            var openings = new List<(int x, int z, int side, bool header)>();
            int c0x = district.x * DC, c0z = district.y * DC;
            // Edges with a sealed zone (the Poolrooms) stay solid walls on both sides.
            bool openW = !set.SealedBetween(seed, district, district + Vector2Int.left), openE = !set.SealedBetween(seed, district, district + Vector2Int.right);
            bool openS = !set.SealedBetween(seed, district, district + Vector2Int.down), openN = !set.SealedBetween(seed, district, district + Vector2Int.up);
            for (int k = 0; k < DC; k++)
            {
                if (openW) AddBorder(openings, sseed, c0x, c0z + k, true, n, 0, k);          // west  (line X = 0)
                if (openE) AddBorder(openings, sseed, c0x + DC, c0z + k, true, n, 1, k);     // east  (line X = n)
                if (openS) AddBorder(openings, sseed, c0x + k, c0z, false, n, 2, k);         // south (line Z = 0)
                if (openN) AddBorder(openings, sseed, c0x + k, c0z + DC, false, n, 3, k);    // north (line Z = n)
            }
            var openingCells = new HashSet<Vector2Int>();
            foreach (var o in openings) openingCells.Add(new Vector2Int(o.x, o.z));

            // 2. Reserved areas: landmarks that reach this storey, and the spawn hall (the first chunk at the
            //    origin is always one big room).
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++) L.roomOf[x, z] = -1;
            var placements = new List<LandmarkPlacement>();
            Landmarks.InDistrict(set, seed, district, placements);
            var reserved = new List<(RectInt rect, int landmark)>();
            for (int i = 0; i < placements.Count; i++)
                if (storey < placements[i].storeys) { reserved.Add((placements[i].rect, i)); L.landmarks.Add(placements[i]); }
            // (Its north side, from 2026-11-07: the service lift's shaft and the rooms either side of it.)
            // (first, so the cuts go round it before the lift's shaft north of it: SplitAround needs reserved areas that do not straddle)
            if ((district == Vector2Int.zero || LeasePlan.IsCentre(district)) && storey == 0) reserved.Insert(0, (new RectInt(0, 0, ChunkN, CustomRooms.OverflowRooms.WorldLiftRect.yMin), -1));
            // Poolrooms: a spiral stair tower, the same square on every storey.
            RectInt? spiral = pool ? PoolPlan.SpiralSite(seed, district) : null;
            if (spiral.HasValue) reserved.Add((spiral.Value, -1));

            // 3. Partition around the reserved areas, then into rooms and hallways.
            var landmarkRooms = new List<Room>[placements.Count];
            SplitAround(L, rng, new RectInt(0, 0, n, n), reserved, openingCells, landmarkRooms, placements);

            // Room kinds (landmarks keep theirs).
            foreach (var r in L.rooms)
            {
                if (r.kind != RoomKind.Office || r.landmark != null) continue;
                var R = L.RulesAt(r.rect);
                int area = r.rect.width * r.rect.height, minDim = Mathf.Min(r.rect.width, r.rect.height);
                double p = rng.NextDouble();
                if (area >= 30 && minDim >= 5) r.kind = RoomKind.BigOffice;
                else if (minDim >= 3 && area >= 9 && p < R.loungeChance) r.kind = RoomKind.Lounge;
                else if (minDim >= 3 && Mathf.Max(r.rect.width, r.rect.height) >= 4 && p < R.loungeChance + R.conferenceChance) r.kind = RoomKind.Conference;
            }

            // Ceiling heights and wall finish. Big halls often rise to tall plaster ceilings; a few large
            // ordinary rooms and lounges do too. Heights step in 0.3 m so trims line up.
            if (pool) { PoolPlan.Ceilings(L, rng); if (spiral.HasValue) PoolPlan.MarkSpiral(L, spiral.Value); }
            else foreach (var r in L.rooms)
            {
                if (r.landmark != null) continue;
                var R = L.RulesAt(r.rect);
                int area = r.rect.width * r.rect.height;
                double tallChance = r.kind == RoomKind.BigOffice ? Mathf.Clamp01(R.tallChanceBig + (area >= 100 ? 0.2f : 0f))
                                  : (r.kind == RoomKind.Lounge || r.kind == RoomKind.Office) && area >= 16 ? R.tallChanceSmall : 0.0;
                if (rng.NextDouble() < tallChance)
                {
                    // Heights step in 0.3 m between the biome's min and max, never below 3.3 m.
                    int lo = Mathf.Max(2, Mathf.RoundToInt((R.tallMin - OfficeBiome.CeilingHeight) / 0.3f));
                    int hi = Mathf.Max(lo, Mathf.RoundToInt((Mathf.Min(R.tallMax, OfficeBiome.MaxCeilingHeight) - OfficeBiome.CeilingHeight) / 0.3f));
                    r.ceiling = OfficeBiome.CeilingHeight + 0.3f * rng.Next(lo, hi + 1);
                }
                if (r.kind != RoomKind.Crawl && r.kind != RoomKind.Hallway)
                    r.painted = r.Tall || rng.NextDouble() < R.paintedChance;   // wallpaper is designed for 2.7 m walls
                else r.painted = rng.NextDouble() < R.paintedChance * 0.5f;
                // Lounges and meeting rooms are living rooms: painted, so furnished (StyleOf).
                if (r.kind == RoomKind.Lounge || r.kind == RoomKind.Conference) r.painted = true;
                // The Living rooms biome: an emptied-out early-2000s living room (orange-peel drywall, now and then the clouds
                // wallpaper; carpet; baseboards; a flat plaster ceiling). Its dice only where the biome is (other rooms keep
                // theirs as they were).
                if (R.homeChance > 0f && r.kind != RoomKind.Crawl && rng.NextDouble() < R.homeChance)
                {
                    r.home = true; r.painted = true;
                    r.wallFinish = r.kind != RoomKind.Hallway && rng.NextDouble() < R.cloudWallpaper ? WallFinish.SkyWallpaper : WallFinish.OrangePeel;
                    r.floorFinish = FloorFinish.HomeCarpet;
                }
            }

            // 4. Walls everywhere rooms differ, plus the district edge.
            for (int x = 0; x <= n; x++)
            for (int z = 0; z < n; z++)
                L.vEdges[x, z] = (x == 0 || x == n || L.roomOf[x - 1, z] != L.roomOf[x, z]) ? EdgeKind.Wall : EdgeKind.Open;
            for (int x = 0; x < n; x++)
            for (int z = 0; z <= n; z++)
                L.hEdges[x, z] = (z == 0 || z == n || L.roomOf[x, z - 1] != L.roomOf[x, z]) ? EdgeKind.Wall : EdgeKind.Open;
            foreach (var o in openings) L.SetEdge(o.x, o.z, o.side, o.header ? EdgeKind.DoorHeader : EdgeKind.Door);
            // The way off a spiral stair's top: its only door, made before the rooms are joined up.
            if (spiral.HasValue && storey > 0) PoolPlan.LandingDoor(L, spiral.Value);

            // Crawl spaces open only onto the hallway they were cut from.
            foreach (var r in L.rooms)
                if (r.kind == RoomKind.Crawl) OpenCrawlMouth(L, r);

            // 5. Doors: random spanning tree over walkable rooms, plus loops and occasional open plan.
            //    Landmarks then open their insides and force their entrances.
            ConnectRooms(L, rng);
            for (int i = 0; i < placements.Count; i++)
                if (landmarkRooms[i] != null) Landmarks.Connect(L, placements[i], landmarkRooms[i]);
            EnsureConnected(L, rng);
            EncloseTallRooms(L);
            PlaceArches(L);
            if (pool) PoolArches(L, new System.Random(Hash(sseed, district.x, district.y, 0xA4C7)));
            // Height and depth (world version 14): a sunken wing, a pit room. Their own dice.
            DepthPlan.Apply(L, new System.Random(Hash(sseed, district.x, district.y, 0xDE97)));
            // Rooms worth making a base (one door, live outlets: SpecialRooms), then which rooms' outlets have power. Own dice.
            SpecialRooms.Choose(L, set, new System.Random(Hash(sseed, district.x, district.y, 0x600D)));
            SpecialRooms.SetLive(L, set, sseed);

            // 6. Lighting and dressing. Placements record the room id in their colour field; light colours
            // are assigned afterwards, once every light's position is known, and stamped in.
            foreach (var r in L.rooms) r.color = r.id;
            if (pool)
            {
                // The Poolrooms: no lights, furniture or outlets; water, columns and the roof openings instead.
                PoolPlan.Dress(L, rng);
                CrawlPortals.Place(L, set, new System.Random(Hash(seed, district.x, district.y, 0x9047A1 + storey)));
                NoclipDoors.Place(L, set, seed);
                AssignColors(L);
                return L;
            }
            foreach (var r in L.rooms)
            {
                if (r.landmark != null) continue;
                var R = L.RulesAt(r.rect);
                double p = rng.NextDouble();
                r.lights = p < R.lightsAllOn ? LightMode.AllOn : p < R.lightsAllOn + R.lightsMixed ? LightMode.Mixed : LightMode.Dark;
                if (r.kind == RoomKind.Lounge && rng.NextDouble() < 0.5) r.lights = LightMode.Dark; // lamps carry the room
                if (r.special != GoodRoom.None) r.lights = LightMode.Dark;                     // a good room's tubes are dead: you light it
            }
            PlaceFixtures(L, rng);
            PlacePillarsAndHalfWalls(L, rng);
            PlaceProps(L, rng);
            PlacePlates(L, rng);
            for (int i = 0; i < placements.Count; i++)
                if (landmarkRooms[i] != null) Landmarks.Decorate(L, placements[i], landmarkRooms[i]);
            // Loose cardboard boxes (movable). Their own dice, so the rest of the plan is unchanged.
            PlaceBoxes(L, new System.Random(Hash(seed, district.x, district.y, 0x0B0C5 + storey)));
            // Crawl spaces: their own dice too. Then the rare noclip door (after them, so it avoids their walls).
            CrawlPortals.Place(L, set, new System.Random(Hash(seed, district.x, district.y, 0x9047A1 + storey)));
            NoclipDoors.Place(L, set, seed);
            // Corners cut at 45 degrees where nothing is in the corner (last: it looks at everything placed).
            DepthPlan.Chamfers(L, new System.Random(Hash(sseed, district.x, district.y, 0xC4A3)));
            // Rugs in some living rooms: their own dice and last of the props, so nothing else in the plan moves or renumbers.
            PlaceRugs(L, new System.Random(Hash(sseed, district.x, district.y, 0x2A6E)), L.rooms, false);
            // What the offices left behind (partitions, a chair): their own dice, after the rugs, for the same reason.
            PlaceLeftovers(L, new System.Random(Hash(sseed, district.x, district.y, 0x1EF7)), L.rooms);
            // A contract floor's seams and storage rooms (LeaseSeams), then its edge (LeasePlan): fire doors chained shut,
            // sheeting, bricked doorways, signs. Own dice, last.
            LeaseSeams.Place(L, district, new System.Random(Hash(sseed, district.x, district.y, 0x5EA4)));
            LeaseEdges.Dress(L, district, new System.Random(Hash(sseed, district.x, district.y, 0x1EA5E)));
            AssignColors(L);
            return L;
        }

        /// <summary>Upper and lower storeys get their own dice (storey 0 keeps the plain seed).</summary>
        public static int StoreySeed(int seed, int storey) => storey == 0 ? seed : Hash(seed, storey, 0x5704, 0x77);

        // ---------------- Partition ----------------

        /// <summary>
        /// Guillotine cuts around each reserved rectangle (strips west and east of it, then south and north),
        /// so the reserved area becomes its own room(s) and everything else is split normally.
        /// Assumes reserved rectangles do not straddle each other's extents (one landmark per district,
        /// kept clear of the spawn hall).
        /// </summary>
        static void SplitAround(OfficeLayout L, System.Random rng, RectInt r, List<(RectInt rect, int landmark)> reserved,
                                HashSet<Vector2Int> openingCells, List<Room>[] landmarkRooms, List<LandmarkPlacement> placements)
        {
            if (r.width <= 0 || r.height <= 0) return;
            int pick = -1;
            for (int i = 0; i < reserved.Count; i++) if (r.Overlaps(reserved[i].rect)) { pick = i; break; }
            if (pick < 0) { Split(L, rng, r, 0, openingCells); return; }
            var q = reserved[pick].rect;
            SplitAround(L, rng, new RectInt(r.x, r.y, q.x - r.x, r.height), reserved, openingCells, landmarkRooms, placements);
            SplitAround(L, rng, new RectInt(q.xMax, r.y, r.xMax - q.xMax, r.height), reserved, openingCells, landmarkRooms, placements);
            SplitAround(L, rng, new RectInt(q.x, r.y, q.width, q.y - r.y), reserved, openingCells, landmarkRooms, placements);
            SplitAround(L, rng, new RectInt(q.x, q.yMax, q.width, r.yMax - q.yMax), reserved, openingCells, landmarkRooms, placements);

            int lm = reserved[pick].landmark;
            if (lm < 0) { MakeRoom(L, q, RoomKind.Office); return; }                 // plain reserved hall (spawn)
            var rooms = landmarkRooms[lm] = new List<Room>();
            foreach (var spec in Landmarks.Rooms(placements[lm], L.storey, L.storeyPitch))
            {
                var room = MakeRoom(L, spec.rect, spec.kind);
                room.ceiling = spec.ceiling; room.painted = spec.painted; room.lights = spec.lights;
                room.wallFinish = spec.wallFinish; room.floorFinish = spec.floorFinish;
                room.openAbove = spec.openAbove; room.noFloor = spec.noFloor; room.landmark = placements[lm].name;
                room.noDoors = spec.noDoors; room.role = spec.role;
                room.custom = placements[lm].kind == LandmarkKind.Custom && !spec.outside;
                if (spec.outside) { room.landmark = null; room.noDoors = false; }
                for (int x = spec.rect.xMin; x < spec.rect.xMax; x++)
                for (int z = spec.rect.yMin; z < spec.rect.yMax; z++)
                    L.flags[x, z] |= (spec.openAbove ? CellFlags.OpenAbove : 0) | (spec.noFloor ? CellFlags.NoFloor : 0);
                rooms.Add(room);
            }
        }

        /// <summary>
        /// Binary split. Regions longer than the biome's largest room always split (district scale, often
        /// with a hallway along the cut). A region that first fits is "top level": like a whole chunk used
        /// to be, it may stay one big hall. Smaller regions stop by size.
        /// </summary>
        static void Split(OfficeLayout L, System.Random rng, RectInt r, int depth, HashSet<Vector2Int> openingCells)
        {
            int w = r.width, h = r.height;
            if (w <= 0 || h <= 0) return;
            var R = L.RulesAt(r);
            int maxRoom = Mathf.Max(MinRoom * 2, Mathf.RoundToInt(R.maxRoomCells));
            bool canSplitX = w >= MinRoom * 2, canSplitZ = h >= MinRoom * 2;
            int maxDim = Mathf.Max(w, h);
            bool must = maxDim > maxRoom && (canSplitX || canSplitZ);

            bool stop = !canSplitX && !canSplitZ;
            if (!stop && !must)
            {
                if (depth == 0) stop = rng.NextDouble() < R.bigHallChance;
                else if (maxDim <= 3) stop = rng.NextDouble() < R.stopSmall;
                else if (maxDim <= 6) stop = rng.NextDouble() < R.stopMedium;
                else if (maxDim <= 9) stop = rng.NextDouble() < R.stopLarge;
                else stop = rng.NextDouble() < R.stopHuge;
            }
            if (stop) { MakeRoom(L, r, RoomKind.Office); return; }

            bool splitX = must
                ? (w >= h ? canSplitX : !canSplitZ)
                : canSplitX && (!canSplitZ || (w > h ? rng.NextDouble() < 0.8 : w < h ? rng.NextDouble() < 0.2 : rng.NextDouble() < 0.5));
            int len = splitX ? w : h;
            int childDepth = must ? 0 : depth + 1;

            // Hallway carved along the split line?
            double hallChance = depth <= 1 ? R.hallChanceShallow : R.hallChanceDeep;
            int hallW = rng.NextDouble() < 1f - R.hallWideChance ? 1 : 2;
            if (len >= MinRoom * 2 + hallW && rng.NextDouble() < hallChance)
            {
                int s = rng.Next(MinRoom, len - MinRoom - hallW + 1);
                RectInt a, hall, b;
                if (splitX)
                {
                    a = new RectInt(r.x, r.y, s, h);
                    hall = new RectInt(r.x + s, r.y, hallW, h);
                    b = new RectInt(r.x + s + hallW, r.y, w - s - hallW, h);
                }
                else
                {
                    a = new RectInt(r.x, r.y, w, s);
                    hall = new RectInt(r.x, r.y + s, w, hallW);
                    b = new RectInt(r.x, r.y + s + hallW, w, h - s - hallW);
                }
                MakeHallway(L, rng, hall, splitX, openingCells);
                Split(L, rng, a, childDepth, openingCells);
                Split(L, rng, b, childDepth, openingCells);
                return;
            }

            int cut = rng.Next(MinRoom, len - MinRoom + 1);
            if (splitX)
            {
                Split(L, rng, new RectInt(r.x, r.y, cut, h), childDepth, openingCells);
                Split(L, rng, new RectInt(r.x + cut, r.y, w - cut, h), childDepth, openingCells);
            }
            else
            {
                Split(L, rng, new RectInt(r.x, r.y, w, cut), childDepth, openingCells);
                Split(L, rng, new RectInt(r.x, r.y + cut, w, h - cut), childDepth, openingCells);
            }
        }

        /// <summary>Hallway running perpendicular to the split axis. 1-wide halls may end in a crawl slot.</summary>
        static void MakeHallway(OfficeLayout L, System.Random rng, RectInt hall, bool runsAlongZ, HashSet<Vector2Int> openingCells)
        {
            int length = runsAlongZ ? hall.height : hall.width;
            int width = runsAlongZ ? hall.width : hall.height;
            if (width == 1 && length >= 6 && rng.NextDouble() < L.RulesAt(hall).crawlChance)
            {
                int crawlLen = rng.Next(3, Mathf.Min(6, length - 3) + 1);
                bool atStart = rng.NextDouble() < 0.5;
                RectInt crawl = runsAlongZ
                    ? new RectInt(hall.x, atStart ? hall.y : hall.yMax - crawlLen, 1, crawlLen)
                    : new RectInt(atStart ? hall.x : hall.xMax - crawlLen, hall.y, crawlLen, 1);
                if (!Touches(crawl, openingCells))
                {
                    RectInt rest = runsAlongZ
                        ? new RectInt(hall.x, atStart ? hall.y + crawlLen : hall.y, 1, length - crawlLen)
                        : new RectInt(atStart ? hall.x + crawlLen : hall.x, hall.y, length - crawlLen, 1);
                    MakeRoom(L, rest, RoomKind.Hallway);
                    MakeRoom(L, crawl, RoomKind.Crawl);
                    return;
                }
            }
            MakeRoom(L, hall, RoomKind.Hallway);
        }

        static bool Touches(RectInt r, HashSet<Vector2Int> cells)
        {
            for (int x = r.xMin; x < r.xMax; x++)
            for (int z = r.yMin; z < r.yMax; z++)
                if (cells.Contains(new Vector2Int(x, z))) return true;
            return false;
        }

        internal static Room MakeRoom(OfficeLayout L, RectInt r, RoomKind kind)
        {
            var room = new Room { id = L.rooms.Count, globalId = L.rooms.Count, rect = r, fullRect = r, kind = kind };
            L.rooms.Add(room);
            for (int x = r.xMin; x < r.xMax; x++)
            for (int z = r.yMin; z < r.yMax; z++)
                L.roomOf[x, z] = room.id;
            return room;
        }

        // ---------------- Edge contracts ----------------

        /// <summary>
        /// Openings on one chunk-length segment of a district edge, keyed by that segment's line in world
        /// chunk space so both districts agree. vertical == true: the line X = a (in chunks) beside chunk row
        /// b; else the line Z = b beside chunk column a. k: which segment of this district's edge.
        /// </summary>
        static void AddBorder(List<(int, int, int, bool)> list, int seed, int a, int b, bool vertical, int n, int side, int k)
        {
            var rng = new System.Random(Hash(seed, a, b, vertical ? 0xB0D1 : 0xB0D2));
            int count = 1 + (rng.NextDouble() < 0.55 ? 1 : 0) + (rng.NextDouble() < 0.25 ? 1 : 0);
            var used = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                int pos = rng.Next(1, ChunkN - 1);
                bool wide = rng.NextDouble() < 0.3 && pos + 1 < ChunkN - 1;
                bool header = rng.NextDouble() < 0.4;
                for (int j = 0; j < (wide ? 2 : 1); j++)
                {
                    int p = pos + j;
                    if (!used.Add(p)) continue;
                    int along = k * ChunkN + p;
                    // Cell on this district's side of the edge.
                    int x = vertical ? (side == 0 ? 0 : n - 1) : along;
                    int z = vertical ? along : (side == 2 ? 0 : n - 1);
                    list.Add((x, z, side, header));
                }
            }
        }

        /// <summary>Doorways of a player-built room keep exactly the shape the blueprint gave them.</summary>
        static bool TouchesCustom(OfficeLayout L, int ax, int az, int bx, int bz) =>
            (L.InChunk(ax, az) && L.RoomAt(ax, az).custom) || (L.InChunk(bx, bz) && L.RoomAt(bx, bz).custom);

        /// <summary>Round tops on interior headed doorways, by the biome's chance, decided once per edge.</summary>
        static void PlaceArches(OfficeLayout L)
        {
            int n = L.N;
            int ox = L.coord.x * n, oz = L.coord.y * n;
            bool Roll(int wx, int wz, bool vertical, float chance)
            {
                if (chance <= 0f) return false;
                uint h = (uint)(wx * 73856093) ^ (uint)(wz * 19349663) ^ (vertical ? 0x9E3779B9u : 0x7F4A7C15u) ^ (uint)(L.storey * 0x632BE5AB);
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h & 0xFFFF) / 65535f < chance;
            }
            for (int x = 1; x < n; x++)
            for (int z = 0; z < n; z++)
                if (L.vEdges[x, z] == EdgeKind.DoorHeader && !L.archV[x, z] && !TouchesCustom(L, x - 1, z, x, z) && Roll(ox + x, oz + z, true, L.RulesAt(x, z).archedDoors)) L.archV[x, z] = true;
            for (int x = 0; x < n; x++)
            for (int z = 1; z < n; z++)
                if (L.hEdges[x, z] == EdgeKind.DoorHeader && !L.archH[x, z] && !TouchesCustom(L, x, z - 1, x, z) && Roll(ox + x, oz + z, false, L.RulesAt(x, z).archedDoors)) L.archH[x, z] = true;
        }

        // ---------------- Connectivity ----------------

        static void OpenCrawlMouth(OfficeLayout L, Room crawl)
        {
            var r = crawl.rect;
            // The crawl was cut from the end of a 1-wide hallway; open the edge facing that hallway.
            for (int side = 0; side < 4; side++)
            {
                int x = side == 1 ? r.xMax - 1 : r.xMin, z = side == 3 ? r.yMax - 1 : r.yMin;
                bool alongX = r.width > 1;
                if (alongX && (side == 2 || side == 3)) continue;
                if (!alongX && (side == 0 || side == 1)) continue;
                int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                if (!L.InChunk(nx, nz)) continue;
                if (L.RoomAt(nx, nz).kind == RoomKind.Hallway) { L.SetEdge(x, z, side, EdgeKind.Open); return; }
            }
        }

        /// <summary>
        /// Rule: where ceiling heights change, doors out of the taller room get a header (2.1 m), and so do
        /// chunk-border openings (the neighbour's ceiling is unknown). Open-plan spans inside a chunk stay
        /// open up to the lower ceiling, with a soffit wall above (the builder closes it off).
        /// Passability is unchanged (headers are above head height).
        /// </summary>
        static void EncloseTallRooms(OfficeLayout L)
        {
            int n = L.N;
            for (int x = 0; x <= n; x++)
            for (int z = 0; z < n; z++)
            {
                var e = L.vEdges[x, z];
                if (e == EdgeKind.Wall || e == EdgeKind.DoorHeader) continue;
                if (e == EdgeKind.Open && x > 0 && x < n) continue;      // open plan keeps a soffit at the lower ceiling
                float a = x > 0 ? L.CeilingAt(x - 1, z) : -1f, b = x < n ? L.CeilingAt(x, z) : -1f;
                if (NeedsHeader(a, b)) L.vEdges[x, z] = EdgeKind.DoorHeader;
            }
            for (int x = 0; x < n; x++)
            for (int z = 0; z <= n; z++)
            {
                var e = L.hEdges[x, z];
                if (e == EdgeKind.Wall || e == EdgeKind.DoorHeader) continue;
                if (e == EdgeKind.Open && z > 0 && z < n) continue;
                float a = z > 0 ? L.CeilingAt(x, z - 1) : -1f, b = z < n ? L.CeilingAt(x, z) : -1f;
                if (NeedsHeader(a, b)) L.hEdges[x, z] = EdgeKind.DoorHeader;
            }
        }

        /// <summary>a/b are the ceilings on each side (-1 = other chunk, unknown).</summary>
        static bool NeedsHeader(float a, float b)
        {
            const float std = OfficeBiome.CeilingHeight + 0.01f;
            if (a < 0f) return b > std;
            if (b < 0f) return a > std;
            return Mathf.Abs(a - b) > 0.01f;
        }

        struct Shared { public int a, b; public List<(int x, int z, int side)> edges; }

        static List<Shared> SharedBoundaries(OfficeLayout L)
        {
            var map = new Dictionary<long, Shared>();
            void Add(int x, int z, int side, int ra, int rb)
            {
                if (ra == rb) return;
                int a = Mathf.Min(ra, rb), b = Mathf.Max(ra, rb);
                long key = ((long)a << 32) | (uint)b;
                if (!map.TryGetValue(key, out var s)) { s = new Shared { a = a, b = b, edges = new List<(int, int, int)>() }; map[key] = s; }
                s.edges.Add((x, z, side));
            }
            for (int x = 1; x < L.N; x++) for (int z = 0; z < L.N; z++) Add(x, z, 0, L.roomOf[x - 1, z], L.roomOf[x, z]);
            for (int x = 0; x < L.N; x++) for (int z = 1; z < L.N; z++) Add(x, z, 2, L.roomOf[x, z - 1], L.roomOf[x, z]);
            return new List<Shared>(map.Values);
        }

        /// <summary>
        /// Poolrooms: wide round arches. Every open-plan run between two rooms becomes one arch, and some single doorways
        /// in long shared walls are widened into two- or three-cell arched openings. Marks <see cref="OfficeLayout.spanV"/>
        /// / spanH (the builder curves the opening's top).
        /// </summary>
        static void PoolArches(OfficeLayout L, System.Random rng)
        {
            int n = L.N;
            L.spanV = new ushort[n + 1, n]; L.spanH = new ushort[n, n + 1];
            foreach (var s in SharedBoundaries(L))
            {
                Room ra = L.rooms[s.a], rb = L.rooms[s.b];
                if (!ra.Walkable || !rb.Walkable || ra.spiral || rb.spiral || ra.noFloor || rb.noFloor) continue;
                var e = s.edges;            // in order along their line
                bool anyOpen = e.Exists(q => L.EdgeOf(q.x, q.z, q.side) == EdgeKind.Open);
                if (!anyOpen && e.Count >= 4 && rng.NextDouble() < 0.5)
                {
                    int door = e.FindIndex(q => OfficeLayout.IsPassable(L.EdgeOf(q.x, q.z, q.side)));
                    if (door >= 0)
                    {
                        int w = Mathf.Min(e.Count - 2, rng.NextDouble() < 0.5 ? 2 : 3);
                        int start = Mathf.Clamp(door - w / 2, 1, e.Count - 1 - w);
                        for (int i = 0; i < e.Count; i++)
                            if (i >= start && i < start + w) L.SetEdge(e[i].x, e[i].z, e[i].side, EdgeKind.Open);
                            else if (OfficeLayout.IsPassable(L.EdgeOf(e[i].x, e[i].z, e[i].side))) L.SetEdge(e[i].x, e[i].z, e[i].side, EdgeKind.Wall);
                    }
                }
                // Long open runs become arcades: three-cell arches on one-cell piers.
                for (int i = 0; i < e.Count;)
                {
                    if (L.EdgeOf(e[i].x, e[i].z, e[i].side) != EdgeKind.Open) { i++; continue; }
                    int j = i;
                    while (j < e.Count && L.EdgeOf(e[j].x, e[j].z, e[j].side) == EdgeKind.Open) j++;
                    if (j - i > 4)
                        for (int k = i + 3; k < j - 1; k += 4) L.SetEdge(e[k].x, e[k].z, e[k].side, EdgeKind.Wall);
                    i = j;
                }
                // Each run of open edges is one arch.
                for (int i = 0; i < e.Count;)
                {
                    if (L.EdgeOf(e[i].x, e[i].z, e[i].side) != EdgeKind.Open) { i++; continue; }
                    int j = i;
                    while (j < e.Count && L.EdgeOf(e[j].x, e[j].z, e[j].side) == EdgeKind.Open) j++;
                    int len = j - i;
                    if (len >= 2 && len < 255)
                        for (int k = i; k < j; k++)
                        {
                            var q = e[k];
                            ushort v = (ushort)((k - i + 1) | (len << 8));
                            if (q.side == 0) L.spanV[q.x, q.z] = v; else L.spanH[q.x, q.z] = v;
                        }
                    i = j;
                }
            }
        }

        static void ConnectRooms(OfficeLayout L, System.Random rng)
        {
            var shared = SharedBoundaries(L);
            Shuffle(shared, rng);
            var uf = new int[L.rooms.Count];
            for (int i = 0; i < uf.Length; i++) uf[i] = i;
            int Find(int i) { while (uf[i] != i) i = uf[i] = uf[uf[i]]; return i; }

            foreach (var s in shared)
            {
                Room ra = L.rooms[s.a], rb = L.rooms[s.b];
                if (!ra.Walkable || !rb.Walkable || ra.noDoors || rb.noDoors) continue;      // stair halls and shafts only get their landmark's doors
                bool joined = Find(s.a) != Find(s.b);
                var R = L.RulesAt(ra.rect);
                bool loop = !joined && rng.NextDouble() < R.loopChance;
                if (!joined && !loop) continue;
                if (joined) uf[Find(s.a)] = Find(s.b);

                bool openPlan = s.edges.Count >= 3 && rng.NextDouble() < R.openPlanChance &&
                                (ra.kind == RoomKind.BigOffice || rb.kind == RoomKind.BigOffice || ra.kind == RoomKind.Hallway || rb.kind == RoomKind.Hallway);
                if (openPlan)
                {
                    // Leave stub walls at both ends so the opening reads as architecture, not a missing wall.
                    for (int i = 1; i < s.edges.Count - 1; i++) { var e = s.edges[i]; L.SetEdge(e.x, e.z, e.side, EdgeKind.Open); }
                    if (s.edges.Count == 3) { var e = s.edges[1]; L.SetEdge(e.x, e.z, e.side, EdgeKind.DoorHeader); }
                }
                else
                {
                    var e = s.edges[rng.Next(s.edges.Count)];
                    L.SetEdge(e.x, e.z, e.side, rng.NextDouble() < 0.5 ? EdgeKind.DoorHeader : EdgeKind.Door);
                    if (s.edges.Count >= 4 && rng.NextDouble() < 0.2)
                    {
                        var e2 = s.edges[rng.Next(s.edges.Count)];
                        L.SetEdge(e2.x, e2.z, e2.side, EdgeKind.Door);
                    }
                }
            }
        }

        /// <summary>Flood-fills walkable cells and adds doors until every walkable room is reachable.</summary>
        static void EnsureConnected(OfficeLayout L, System.Random rng)
        {
            for (int guard = 0; guard < 4096; guard++)
            {
                var reached = Flood(L);
                int start = -1;
                for (int x = 0; x < L.N && start < 0; x++) for (int z = 0; z < L.N; z++)
                    if (L.RoomAt(x, z).Walkable && !reached[x, z]) { start = x * L.N + z; break; }
                if (start < 0) return;

                // Find a wall between a reached walkable cell and an unreached walkable cell.
                var candidates = new List<(int x, int z, int side)>();
                for (int x = 0; x < L.N; x++)
                for (int z = 0; z < L.N; z++)
                {
                    if (!reached[x, z] || !L.RoomAt(x, z).Walkable || L.RoomAt(x, z).noDoors) continue;
                    for (int side = 0; side < 4; side++)
                    {
                        int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                        if (!L.InChunk(nx, nz) || reached[nx, nz] || !L.RoomAt(nx, nz).Walkable || L.RoomAt(nx, nz).noDoors) continue;
                        if (L.EdgeOf(x, z, side) == EdgeKind.Wall) candidates.Add((x, z, side));
                    }
                }
                if (candidates.Count > 0)
                {
                    var c = candidates[rng.Next(candidates.Count)];
                    L.SetEdge(c.x, c.z, c.side, EdgeKind.Door);
                    continue;
                }
                // Isolated only by crawl spaces: turn crawls back into hallway and rewire.
                foreach (var r in L.rooms)
                {
                    if (r.kind != RoomKind.Crawl) continue;
                    r.kind = RoomKind.Hallway;
                }
            }
            Debug.LogWarning($"[Backrooms] District {L.coord} storey {L.storey} could not be fully connected.");
        }

        public static bool[,] Flood(OfficeLayout L)
        {
            var seen = new bool[L.N, L.N];
            var q = new Queue<Vector2Int>();
            for (int x = 0; x < L.N && q.Count == 0; x++) for (int z = 0; z < L.N; z++)
                if (L.RoomAt(x, z).Walkable) { q.Enqueue(new Vector2Int(x, z)); seen[x, z] = true; break; }
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                for (int side = 0; side < 4; side++)
                {
                    int nx = c.x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = c.y + (side == 2 ? -1 : side == 3 ? 1 : 0);
                    if (!L.InChunk(nx, nz) || seen[nx, nz]) continue;
                    if (!L.RoomAt(nx, nz).Walkable || !OfficeLayout.IsPassable(L.EdgeOf(c.x, c.y, side))) continue;
                    seen[nx, nz] = true; q.Enqueue(new Vector2Int(nx, nz));
                }
            }
            return seen;
        }

        // ---------------- Dressing ----------------

        /// <summary>
        /// Light colours (rendering layers for unshadowed lights). A light zone is a set of rooms joined by
        /// open-plan edges. Two zones conflict when a light in one (ceiling fixture, lamp, or the doorway
        /// light on its side of an opening) reaches a cell of the other: they must not share a layer, or
        /// the light would show through the wall. Zones are coloured with DSATUR over 28 layers. Zones within
        /// light reach of the district edge use only the district's colour set (7 layers, 4 sets by district
        /// x/z parity), so neighbouring districts, diagonals included, never share a layer where their light
        /// could meet; zones deeper inside may use any of the 28.
        /// </summary>
        internal static void AssignColors(OfficeLayout L)
        {
            int n = L.N, R = L.rooms.Count;
            const float C = OfficeBiome.CellSize;
            var parent = new int[R];
            for (int i = 0; i < R; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            var shared = SharedBoundaries(L);
            foreach (var s in shared)
                foreach (var e in s.edges)
                    if (L.EdgeOf(e.x, e.z, e.side) == EdgeKind.Open) { parent[Find(s.a)] = Find(s.b); break; }

            // Compact zone indices in room order (deterministic).
            var zoneIndex = new Dictionary<int, int>();
            var zi = new int[R];
            for (int i = 0; i < R; i++)
            {
                int root = Find(i);
                if (!zoneIndex.TryGetValue(root, out int z)) zoneIndex[root] = z = zoneIndex.Count;
                zi[i] = z;
            }
            int Z = zoneIndex.Count;
            var w = new float[Z, Z];
            var ownReach = new float[Z];                 // farthest any light of the zone reaches, metres

            // Every zone a light visibly reaches (brightest point of each cell's floor and walls).
            void Reach(in LightLayers.Spec spec, int src, Vector2 front)
            {
                ownReach[src] = Mathf.Max(ownReach[src], spec.range);
                int r = Mathf.CeilToInt(spec.range / C) + 1;
                int cx = Mathf.FloorToInt(spec.position.x / C), cz = Mathf.FloorToInt(spec.position.z / C);
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(n - 1, cx + r); x++)
                for (int z = Mathf.Max(0, cz - r); z <= Mathf.Min(n - 1, cz + r); z++)
                {
                    int other = zi[L.roomOf[x, z]];
                    if (other == src) continue;
                    if (front != Vector2.zero && ((x + 0.5f) * C - spec.position.x) * front.x + ((z + 0.5f) * C - spec.position.z) * front.y <= 0f) continue;
                    float e = LightLayers.MaxOnCell(spec, x * C, z * C, C, L.CeilingAt(x, z));
                    if (e < LightLayers.VisibleIrradiance) continue;
                    w[src, other] += e; w[other, src] += e;
                }
            }
            foreach (var f in L.fixtures)
            {
                if (!f.on) continue;
                var centre = new Vector2((f.cell.x + 0.5f) * C, (f.cell.y + 0.5f) * C);
                Reach(LightLayers.FixtureSpec(f, centre), zi[f.roomColor], Vector2.zero);
                Reach(LightLayers.BounceSpec(f, centre), zi[f.roomColor], Vector2.zero);
            }
            foreach (var p in L.props)
                if (p.kind == PropKind.FloorLamp && p.lit) Reach(LightLayers.LampSpec(p.position), zi[p.roomColor], Vector2.zero);
            // Doorway lights: every opening between two zones lights a little way into each side.
            float Top(EdgeKind e, int ax, int az, int bx, int bz) =>
                e == EdgeKind.Open ? Mathf.Min(L.CeilingAt(ax, az), L.CeilingAt(bx, bz)) : OfficeBiome.DoorHeaderHeight;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (x + 1 < n && OfficeLayout.IsPassable(L.vEdges[x + 1, z]) && zi[L.roomOf[x, z]] != zi[L.roomOf[x + 1, z]])
                {
                    var at = new Vector2((x + 1) * C, (z + 0.5f) * C);
                    float top = Top(L.vEdges[x + 1, z], x, z, x + 1, z);
                    Reach(LightLayers.DoorwaySpec(at, Vector2.left, top), zi[L.roomOf[x, z]], Vector2.left);
                    Reach(LightLayers.DoorwaySpec(at, Vector2.right, top), zi[L.roomOf[x + 1, z]], Vector2.right);
                }
                if (z + 1 < n && OfficeLayout.IsPassable(L.hEdges[x, z + 1]) && zi[L.roomOf[x, z]] != zi[L.roomOf[x, z + 1]])
                {
                    var at = new Vector2((x + 0.5f) * C, (z + 1) * C);
                    float top = Top(L.hEdges[x, z + 1], x, z, x, z + 1);
                    Reach(LightLayers.DoorwaySpec(at, Vector2.down, top), zi[L.roomOf[x, z]], Vector2.down);
                    Reach(LightLayers.DoorwaySpec(at, Vector2.up, top), zi[L.roomOf[x, z + 1]], Vector2.up);
                }
            }

            // Which layers may each zone use? Zones near the district edge (within any light's reach of a
            // neighbouring district) take one of the 7 colours of the district's parity set, so neighbouring
            // districts never share a layer there. Zones deeper inside are out of reach of every other
            // district, and their own lights stay inside, so they may use all 28 layers.
            const int K = LightLayers.Colors, KAll = LightLayers.LayerCount;
            const int EdgeBand = 12;                     // cells: farther than any light reaches (13 m, 14.75 cells for an atrium well from 5 cells in)
            int set = (L.coord.x & 1) + 2 * (L.coord.y & 1);
            var edgeDist = new int[Z];
            for (int i = 0; i < Z; i++) edgeDist[i] = int.MaxValue;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                int zz = zi[L.roomOf[x, z]];
                edgeDist[zz] = Mathf.Min(edgeDist[zz], Mathf.Min(Mathf.Min(x, z), Mathf.Min(n - 1 - x, n - 1 - z)));
            }
            var allowed = new uint[Z];
            for (int i = 0; i < Z; i++)
            {
                bool inner = edgeDist[i] >= EdgeBand && edgeDist[i] * C >= ownReach[i] + C;
                allowed[i] = inner ? (KAll >= 32 ? 0xFFFFFFFFu : (1u << KAll) - 1u) : (((1u << K) - 1u) << (set * K));
            }

            // DSATUR: colour the most constrained zone first.
            var col = new int[Z];
            for (int i = 0; i < Z; i++) col[i] = -1;
            var degree = new int[Z];
            for (int a = 0; a < Z; a++) for (int b = 0; b < Z; b++) if (w[a, b] > 0) degree[a]++;
            int offset = (Hash(L.coord.x, L.coord.y, 7, 0xC010) & 0x7FFFFFFF) % KAll;
            for (int step = 0; step < Z; step++)
            {
                int pick = -1, bestSat = int.MinValue, bestDeg = -1;
                for (int a = 0; a < Z; a++)
                {
                    if (col[a] >= 0) continue;
                    uint used = 0;
                    for (int b = 0; b < Z; b++) if (w[a, b] > 0 && col[b] >= 0) used |= 1u << col[b];
                    int sat = 0; for (int k = 0; k < KAll; k++) if ((used & allowed[a] & (1u << k)) != 0) sat++;
                    // Fewer free colours = more constrained (zones near the edge have only 7).
                    int free = 0; for (int k = 0; k < KAll; k++) if ((allowed[a] & ~used & (1u << k)) != 0) free++;
                    int score = sat * 64 - free;
                    if (score > bestSat || (score == bestSat && degree[a] > bestDeg)) { pick = a; bestSat = score; bestDeg = degree[a]; }
                }
                int best = -1; float bestCost = float.MaxValue;
                for (int k = 0; k < KAll; k++)
                {
                    int c = (offset + k) % KAll;
                    if ((allowed[pick] & (1u << c)) == 0) continue;
                    float cost = 0f;
                    for (int b = 0; b < Z; b++) if (col[b] == c) cost += w[pick, b];
                    if (cost < bestCost) { bestCost = cost; best = c; if (cost == 0) break; }
                }
                col[pick] = best;
            }

            var roomColor = new int[R];
            for (int i = 0; i < R; i++) roomColor[i] = col[zi[i]];
            foreach (var r in L.rooms) r.color = roomColor[r.id];
            // Stamp the colours into everything placed with a room id.
            for (int i = 0; i < L.fixtures.Count; i++) { var f = L.fixtures[i]; f.roomColor = roomColor[f.roomColor]; L.fixtures[i] = f; }
            for (int i = 0; i < L.pillars.Count; i++) { var p = L.pillars[i]; p.roomColor = roomColor[p.roomColor]; L.pillars[i] = p; }
            for (int i = 0; i < L.halfWalls.Count; i++) { var h = L.halfWalls[i]; h.roomColor = roomColor[h.roomColor]; L.halfWalls[i] = h; }
            for (int i = 0; i < L.plates.Count; i++) { var p = L.plates[i]; p.roomColor = roomColor[p.roomColor]; L.plates[i] = p; }
            for (int i = 0; i < L.props.Count; i++) { var p = L.props[i]; p.roomColor = roomColor[p.roomColor]; L.props[i] = p; }
        }

        internal static void PlaceFixtures(OfficeLayout L, System.Random rng)
        {
            foreach (var r in L.rooms)
            {
                if (r.kind == RoomKind.Crawl || r.noFloor || r.role != AnnexRole.None || r.custom) continue;      // custom rooms place their own lights      // a well's light belongs to the storey below; stair halls and shafts light themselves
                var R = L.RulesAt(r.rect);
                // Troffers every 2 cells (2.4 m); tall rooms use brighter fittings every 3 cells (3.6 m).
                // (Homes: a round flush-mount fitting every 3 cells, on the flat plaster ceiling.)
                int step = r.PlasterCeiling ? 3 : 2;
                bool strips = r.Tall && r.landmark == null && rng.NextDouble() < R.stripLights;
                int ox = ((r.rect.width - 1) % step + 1) / 2, oz = ((r.rect.height - 1) % step + 1) / 2;
                if (!r.PlasterCeiling && r.rect.width == 2) ox = rng.Next(2);
                if (!r.PlasterCeiling && r.rect.height == 2) oz = rng.Next(2);
                for (int x = r.rect.xMin; x < r.rect.xMax; x++)
                for (int z = r.rect.yMin; z < r.rect.yMax; z++)
                {
                    if ((x - r.rect.xMin - ox) % step != 0 || (z - r.rect.yMin - oz) % step != 0) continue;
                    if (x - r.rect.xMin < ox || z - r.rect.yMin < oz) continue;
                    bool on = r.lights == LightMode.AllOn ? rng.NextDouble() > 0.04
                            : r.lights == LightMode.Mixed ? rng.NextDouble() < R.mixedOnFraction
                            : rng.NextDouble() < 0.04;
                    // (None where a ceiling comes down: DepthPlan. After the dice, so the rest of the plan stays as it was.)
                    if (DepthPlan.NoLight(L, r, x, z)) { if (on) rng.NextDouble(); continue; }
                    L.fixtures.Add(new Fixture { cell = new Vector2Int(x, z), on = on, roomColor = r.color,
                        dome = r.PlasterCeiling && !strips, strip = strips, flush = r.home && !r.Tall,
                        height = strips ? Mathf.Min(r.ceiling - 0.5f, 4.6f) : r.ceiling,
                        faulty = on && rng.NextDouble() < R.faultyChance });
                }
            }
        }

        static void PlacePillarsAndHalfWalls(OfficeLayout L, System.Random rng)
        {
            foreach (var r in L.rooms)
            {
                if (r.landmark != null || r.special != GoodRoom.None) continue;   // landmarks and good rooms dress themselves
                if (r.feature == RoomFeature.Pit) continue;               // walkways over shafts
                var rc = r.rect;
                var R = L.RulesAt(rc);
                if (r.kind == RoomKind.BigOffice)
                {
                    int spacing = Mathf.Max(2, Mathf.RoundToInt(R.pillarSpacing + (float)(rng.NextDouble() - 0.5)));
                    float size = rng.NextDouble() < 0.5 ? 0.45f : 0.6f;
                    int sx = rc.xMin + Mathf.Max(2, (rc.width % spacing) / 2 + 1), sz = rc.yMin + Mathf.Max(2, (rc.height % spacing) / 2 + 1);
                    bool sparse = rng.NextDouble() < 0.35;
                    bool racks = rng.NextDouble() < R.rackChance;
                    // Racked halls skip the free-standing column grid so the rack rows stay unbroken.
                    if (racks || rng.NextDouble() >= R.pillarChance) sx = int.MaxValue;   // no pillar grid
                    for (int x = sx; x <= rc.xMax - 2; x += spacing)
                    for (int z = sz; z <= rc.yMax - 2; z += spacing)
                    {
                        if (sparse && rng.NextDouble() < 0.5) continue;
                        if (DepthPlan.Busy(L, x - 1, z - 1) || DepthPlan.Busy(L, x, z - 1) || DepthPlan.Busy(L, x - 1, z) || DepthPlan.Busy(L, x, z)) continue;
                        L.pillars.Add(new Pillar { vertex = new Vector2Int(x, z), size = size, roomColor = r.color });
                    }
                    int runs = r.feature == RoomFeature.Sunken ? 0 : rng.Next(0, Mathf.RoundToInt(R.halfWalls) + 1);
                    for (int i = 0; i < runs; i++) TryHalfWall(L, rng, r);
                    if (racks) PlaceRacks(L, rng, r);
                    if (r.Tall && rng.NextDouble() < 0.75) PlacePallets(L, rng, r);
                    if (racks) TryTruck(L, r);                                   // a platform trolley now and then (its own dice)
                }
                else if ((r.kind == RoomKind.Office) && rc.width >= 4 && rc.height >= 4)
                {
                    if (rng.NextDouble() < 0.3)
                        L.pillars.Add(new Pillar { vertex = new Vector2Int(rc.xMin + rc.width / 2, rc.yMin + rc.height / 2), size = 0.45f, roomColor = r.color });
                    else if (rng.NextDouble() < 0.3 && r.feature == RoomFeature.None) TryHalfWall(L, rng, r);
                }
            }
        }

        /// <summary>
        /// Warehouse storage: rows of racks along X, one cell deep, with two-cell aisles between rows and a
        /// cross aisle every few racks, kept one cell clear of the walls and away from pillars. Aisles keep
        /// the room fully walkable.

        /// <summary>
        /// Loaded wooden pallets standing against the walls of a tall hall (the ring of cells around the
        /// racks), long side along the wall. Kept clear of doors, pillars and each other.
        /// </summary>
        static void PlacePallets(OfficeLayout L, System.Random rng, Room r)
        {
            var rc = r.rect;
            float C = OfficeBiome.CellSize;
            int want = rng.Next(1, 5);
            var used = new List<Vector2>();
            for (int attempt = 0; attempt < 24 && used.Count < want; attempt++)
            {
                int x = rng.Next(rc.xMin, rc.xMax), z = rng.Next(rc.yMin, rc.yMax), side = rng.Next(4);
                if (L.EdgeOf(x, z, side) != EdgeKind.Wall || CellHasDoor(L, x, z) || NearPillarOrHalfWall(L, x, z)) continue;
                var centre = new Vector2((x + 0.5f) * C, (z + 0.5f) * C);
                bool clear = true;
                foreach (var u in used) if ((u - centre).sqrMagnitude < 2.6f * 2.6f) { clear = false; break; }
                foreach (var p in L.props) if (p.kind != PropKind.WallClock && (p.position - centre).sqrMagnitude < 2.0f * 2.0f) { clear = false; break; }
                if (!clear) continue;
                // Backed against the wall, long side (1.2 m) along it.
                var inward = side == 0 ? Vector2.right : side == 1 ? Vector2.left : side == 2 ? Vector2.up : Vector2.down;
                var wall = centre - inward * C * 0.5f;
                float gap = OfficeBiome.WallThickness * 0.5f + 0.5f + 0.03f + (float)rng.NextDouble() * 0.08f;
                var pos = wall + inward * gap;
                float yaw = (side >= 2 ? 0f : 90f) + (float)(rng.NextDouble() - 0.5) * 6f;
                L.props.Add(new Prop { kind = PropKind.Pallet, position = pos, yaw = yaw, size = new Vector3(1.2f, 1.6f, 1.0f), variant = rng.Next(1 << 16), roomColor = r.color, bare = true });
                used.Add(centre);
            }
        }
        /// </summary>
        static void PlaceRacks(OfficeLayout L, System.Random rng, Room r)
        {
            var rc = r.rect;
            float C = OfficeBiome.CellSize;
            // Tall halls get tall racks (up to ~5.4 m), leaving clear air under the high-bay lights.
            float h = Mathf.Min(r.ceiling - 1.2f, 2.6f + (float)rng.NextDouble() * Mathf.Max(0.6f, r.ceiling - 4.2f));
            if (h < 1.8f) return;
            int crossEvery = rng.Next(3, 5);
            for (int z = rc.yMin + 2; z <= rc.yMax - 3; z += 3)
            {
                int seg = 0;
                for (int x = rc.xMin + 2; x + 2 <= rc.xMax - 2; x += 2)
                {
                    if (++seg % (crossEvery + 1) == 0) { CrossingSign(L, r, x, z); continue; }   // cross aisle (a stop sign now and then)
                    bool blocked = false;
                    foreach (var p in L.pillars)
                        if (p.vertex.x >= x - 1 && p.vertex.x <= x + 3 && p.vertex.y >= z - 1 && p.vertex.y <= z + 2) blocked = true;
                    if (blocked) continue;
                    L.props.Add(new Prop { kind = PropKind.StorageRack, position = new Vector2((x + 1) * C, (z + 0.5f) * C),
                        yaw = 0f, size = new Vector3(2.3f, h, 0.95f), variant = rng.Next(1 << 16), roomColor = r.color, bare = true });
                }
            }
        }

        /// <summary>
        /// Now and then a stop sign where an aisle meets a cross aisle (2026-11-04), as warehouses mark forklift crossings: in
        /// the aisle north of rack row z, against the row's end, facing the traffic coming up the aisle. Its own dice, so the
        /// racks stay as they were.
        /// </summary>
        static void CrossingSign(OfficeLayout L, Room r, int x, int z)
        {
            if (z + 3 > r.rect.yMax - 3 || x - 1 < r.rect.xMin) return;           // an aisle beyond the row, a rack before the crossing
            var sr = new System.Random(Hash(r.rect.x * 31 + x, r.rect.y * 17 + z, 0x5709, 3));
            if (sr.NextDouble() > 0.3) return;
            float C = OfficeBiome.CellSize;
            L.props.Add(new Prop { kind = PropKind.StopSign, position = new Vector2(x * C - 0.32f, (z + 1) * C + 0.34f), yaw = -90f,
                size = new Vector3(0.61f, 2.06f, 0.5f), variant = sr.Next(1 << 16), roomColor = r.color });
        }

        static void TryHalfWall(OfficeLayout L, System.Random rng, Room r)
        {
            var rc = r.rect;
            bool alongX = rng.NextDouble() < 0.5;
            int spanLen = alongX ? rc.width : rc.height, spanCross = alongX ? rc.height : rc.width;
            if (spanLen < 4 || spanCross < 3) return;
            int length = rng.Next(1, Mathf.Min(3, spanLen - 2) + 1);
            int start = rng.Next(1, spanLen - length);              // vertices strictly inside the room
            int line = rng.Next(1, spanCross);
            int s = (alongX ? rc.xMin : rc.yMin) + start, ln = (alongX ? rc.yMin : rc.xMin) + line;
            // Keep clear of pillars (they sit on vertices).
            foreach (var p in L.pillars)
            {
                int pa = alongX ? p.vertex.x : p.vertex.y, pl = alongX ? p.vertex.y : p.vertex.x;
                if (Mathf.Abs(pl - ln) <= 1 && pa >= s - 1 && pa <= s + length + 1) return;
            }
            foreach (var h in L.halfWalls)
                if (h.alongX == alongX && Mathf.Abs(h.line - ln) <= 1 && h.start <= s + length + 1 && s <= h.start + h.length + 1) return;
            L.halfWalls.Add(new HalfWall { alongX = alongX, line = ln, start = s, length = length, roomColor = r.color });
        }

        /// <summary>
        /// Build mode's FURNISH FOR ME: these rooms dressed as the generator dresses its own (sofas against walls, armchairs,
        /// lamps in corners, boxes by the walls; racks and pallets on concrete). Adds to L.props.
        /// </summary>
        public static void DressLikeTheWorld(OfficeLayout L, System.Random rng, List<Room> rooms)
        {
            var occupied = new HashSet<Vector2Int>();
            float C = OfficeBiome.CellSize;
            foreach (var p in L.props) occupied.Add(new Vector2Int(Mathf.FloorToInt(p.position.x / C), Mathf.FloorToInt(p.position.y / C)));
            var keep = new List<(Room r, string landmark)>();
            foreach (var r in L.rooms) { keep.Add((r, r.landmark)); r.landmark = "-"; }
            foreach (var r in rooms)
            {
                r.landmark = null;
                if (StyleOf(r) == RoomStyle.Warehouse) { PlaceRacks(L, rng, r); PlacePallets(L, rng, r); TryTruck(L, r); continue; }
                int before = L.props.Count;
                for (int i = 0; i < 6 && L.props.Count == before; i++) Furnish(L, rng, r, occupied);
            }
            PlaceBoxes(L, rng);
            PlaceRugs(L, rng, rooms, true);
            foreach (var (r, lm) in keep) r.landmark = lm;
        }

        static void PlaceProps(OfficeLayout L, System.Random rng)
        {
            float C = OfficeBiome.CellSize;
            var occupied = new HashSet<Vector2Int>();
            foreach (var r in L.rooms)
            {
                if (r.landmark != null || r.noFloor) continue;
                var rc = r.rect;
                if (r.feature == RoomFeature.Pit) continue;                 // nothing stands on a pit room's walkways
                if (r.special != GoodRoom.None) { SpecialRooms.Dress(L, rng, r, occupied); continue; }
                var style = StyleOf(r);
                bool hasRacks = false;
                foreach (var pr in L.props)
                    if (pr.kind == PropKind.StorageRack && rc.Contains(new Vector2Int(Mathf.FloorToInt(pr.position.x / C), Mathf.FloorToInt(pr.position.y / C)))) { hasRacks = true; break; }
                // (Not in the backrooms: wallpapered rooms and halls stay bare.)
                if (r.kind == RoomKind.BigOffice && !hasRacks && style != RoomStyle.Backrooms && rng.NextDouble() < L.RulesAt(rc).scatteredSeating * (r.Tall ? 1.5f : 1f))
                {
                    // Abandoned seating left scattered across a big hall, at odd angles.
                    int count = rng.Next(2, 6);
                    for (int i = 0, tries = 0; i < count && tries < 30; tries++)
                    {
                        int cx = rng.Next(rc.xMin + 1, rc.xMax - 1), cz = rng.Next(rc.yMin + 1, rc.yMax - 1);
                        var cell = new Vector2Int(cx, cz);
                        if (occupied.Contains(cell) || NearPillarOrHalfWall(L, cx, cz) || DepthPlan.Busy(L, cx, cz)) continue;
                        bool clear = true;
                        for (int ox = -1; ox <= 1; ox++) for (int oz = -1; oz <= 1; oz++) if (occupied.Contains(new Vector2Int(cx + ox, cz + oz))) clear = false;
                        if (!clear) continue;
                        occupied.Add(cell);
                        float yaw = rng.Next(24) * 15f;
                        L.props.Add(new Prop { kind = PropKind.Sofa, position = new Vector2((cx + 0.5f) * C, (cz + 0.5f) * C), yaw = yaw,
                            size = new Vector3(2.0f, 0.82f, 0.88f), variant = rng.Next(4), roomColor = r.color });
                        i++;
                    }
                }
                if (style == RoomStyle.LivingRoom && !hasRacks) Furnish(L, rng, r, occupied);   // (racks: a warehouse room after all)
            }
        }

        /// <summary>
        /// Level 0's three kinds of room: the <b>backrooms</b> (wallpaper, carpet: the usual halls and rooms, bare), the
        /// <b>living rooms</b> (painted walls and carpet, the big ones with pillars: furnished, lamps), the <b>warehouse</b>
        /// (the concrete-floored halls: racks, pallets, boxes).
        /// </summary>
        public static RoomStyle StyleOf(Room r)
        {
            if (OfficeBiomeAssets.ConcreteFloor(r)) return RoomStyle.Warehouse;
            if (r.home) return r.kind != RoomKind.Hallway && r.kind != RoomKind.Crawl ? RoomStyle.LivingRoom : RoomStyle.Backrooms;
            bool painted = r.wallFinish == WallFinish.PaintedPlaster || (r.wallFinish == WallFinish.Default && r.painted);
            return painted && r.kind != RoomKind.Hallway && r.kind != RoomKind.Crawl ? RoomStyle.LivingRoom : RoomStyle.Backrooms;
        }

        /// <summary>
        /// A living room's furniture: sofas backed against its walls, an armchair or two, floor lamps in free corners. (No
        /// office chairs or tables for now.) Lounges and meeting rooms always; offices and big rooms often.
        /// </summary>
        static void Furnish(OfficeLayout L, System.Random rng, Room r, HashSet<Vector2Int> occupied)
        {
            if (r.home) { FurnishHome(L, rng, r, occupied); return; }
            float C = OfficeBiome.CellSize;
            var rc = r.rect;
            bool small = r.kind == RoomKind.Office;
            double chance = r.kind == RoomKind.Lounge || r.kind == RoomKind.Conference ? 1.0 : r.kind == RoomKind.BigOffice ? 0.6 : small ? 0.45 : 0.0;
            if (rc.width < 2 || rc.height < 2 || rng.NextDouble() >= chance) return;
            int sofas = small ? rng.Next(0, 2) : rng.Next(1, 4);
            var usedWalls = new HashSet<int>();
            for (int i = 0; i < sofas * 4 && usedWalls.Count < sofas; i++)
            {
                int side = rng.Next(4);
                if (usedWalls.Contains(side)) continue;
                if (TrySofa(L, rng, r, side, occupied)) usedWalls.Add(side);
            }
            int chairs = rng.Next(small ? 1 : 0, 3);
            for (int i = 0, tries = 0; i < chairs && tries < 12; tries++)
                if (TryArmchair(L, rng, r, occupied)) i++;
            int lamps = 1 + (rc.width * rc.height >= 30 && rng.NextDouble() < 0.6 ? 1 : 0);
            var usedCorners = new HashSet<int>();
            for (int i = 0, tries = 0; i < lamps && tries < 8; tries++)
            {
                int corner = rng.Next(4);
                if (!usedCorners.Add(corner)) continue;
                float lx = (corner & 1) == 0 ? rc.xMin * C + 0.45f : rc.xMax * C - 0.45f;
                float lz = (corner & 2) == 0 ? rc.yMin * C + 0.45f : rc.yMax * C - 0.45f;
                int cx = Mathf.Clamp(Mathf.FloorToInt(lx / C), 0, L.N - 1), cz = Mathf.Clamp(Mathf.FloorToInt(lz / C), 0, L.N - 1);
                if (CellHasDoor(L, cx, cz) || occupied.Contains(new Vector2Int(cx, cz)) || NearPillarOrHalfWall(L, cx, cz)) continue;
                occupied.Add(new Vector2Int(cx, cz));
                // Lit only where its outlet has power (most rooms' do not: SpecialRooms.SetLive).
                int lampVariant = rng.Next(2);
                bool lampLit = rng.NextDouble() < 0.8 && r.live;
                L.props.Add(new Prop { kind = PropKind.FloorLamp, position = new Vector2(lx, lz), variant = lampVariant, lit = lampLit, roomColor = r.color });
                i++;
            }
        }

        /// <summary>
        /// A home (the Living rooms biome) after the move-out: mostly empty. Now and then the one sofa left against a wall, or an
        /// armchair; rarely a floor lamp, unplugged. (Loose boxes come from PlaceBoxes, as anywhere.)
        /// </summary>
        static void FurnishHome(OfficeLayout L, System.Random rng, Room r, HashSet<Vector2Int> occupied)
        {
            var rc = r.rect;
            if (rc.width < 2 || rc.height < 2) return;
            double p = rng.NextDouble();
            if (p < 0.28) { for (int i = 0; i < 6; i++) if (TrySofa(L, rng, r, rng.Next(4), occupied)) break; }
            else if (p < 0.42) { for (int i = 0; i < 8; i++) if (TryArmchair(L, rng, r, occupied)) break; }
            if (rng.NextDouble() < 0.07)
            {
                float C = OfficeBiome.CellSize;
                int corner = rng.Next(4);
                float lx = (corner & 1) == 0 ? rc.xMin * C + 0.45f : rc.xMax * C - 0.45f;
                float lz = (corner & 2) == 0 ? rc.yMin * C + 0.45f : rc.yMax * C - 0.45f;
                int cx = Mathf.Clamp(Mathf.FloorToInt(lx / C), 0, L.N - 1), cz = Mathf.Clamp(Mathf.FloorToInt(lz / C), 0, L.N - 1);
                if (!CellHasDoor(L, cx, cz) && occupied.Add(new Vector2Int(cx, cz)))
                    L.props.Add(new Prop { kind = PropKind.FloorLamp, position = new Vector2(lx, lz), variant = rng.Next(2), lit = false, roomColor = r.color });
            }
        }

        /// <summary>An armchair against a solid wall in a free cell, turned a little toward the room.</summary>
        static bool TryArmchair(OfficeLayout L, System.Random rng, Room r, HashSet<Vector2Int> occupied)
        {
            float C = OfficeBiome.CellSize;
            var rc = r.rect;
            int x = rng.Next(rc.xMin, rc.xMax), z = rng.Next(rc.yMin, rc.yMax), side = rng.Next(4);
            var cell = new Vector2Int(x, z);
            if (L.EdgeOf(x, z, side) != EdgeKind.Wall || CellHasDoor(L, x, z) || occupied.Contains(cell) || NearPillarOrHalfWall(L, x, z)) return false;
            occupied.Add(cell);
            const float depth = 0.85f;
            float off = OfficeBiome.WallThickness * 0.5f + depth * 0.5f + 0.06f;
            Vector2 c = new Vector2((x + 0.5f) * C, (z + 0.5f) * C);
            Vector2 pos = side == 0 ? new Vector2(x * C + off, c.y) : side == 1 ? new Vector2((x + 1) * C - off, c.y)
                        : side == 2 ? new Vector2(c.x, z * C + off) : new Vector2(c.x, (z + 1) * C - off);
            float yaw = (side == 0 ? 90f : side == 1 ? -90f : side == 2 ? 0f : 180f) + (float)(rng.NextDouble() - 0.5) * 40f;
            L.props.Add(new Prop { kind = PropKind.Armchair, position = pos, yaw = yaw, size = new Vector3(0.9f, 0.9f, depth), variant = rng.Next(8), roomColor = r.color });
            return true;
        }

        /// <summary>
        /// A few loose cardboard boxes against walls in offices and big rooms: light things to pick up and
        /// move. Kept clear of doors, pillars and other props.
        /// </summary>
        static void PlaceBoxes(OfficeLayout L, System.Random rng)
        {
            float C = OfficeBiome.CellSize;
            foreach (var r in L.rooms)
            {
                if (r.landmark != null || r.noFloor || r.feature == RoomFeature.Pit || r.special != GoodRoom.None) continue;
                double chance = r.kind == RoomKind.Office ? 0.22 : r.kind == RoomKind.BigOffice ? 0.35 : r.kind == RoomKind.Hallway ? 0.04 : 0.0;
                if (rng.NextDouble() >= chance) continue;
                var rc = r.rect;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    int x = rng.Next(rc.xMin, rc.xMax), z = rng.Next(rc.yMin, rc.yMax), side = rng.Next(4);
                    if (L.EdgeOf(x, z, side) != EdgeKind.Wall || CellHasDoor(L, x, z) || NearPillarOrHalfWall(L, x, z)) continue;
                    var centre = new Vector2((x + 0.5f) * C, (z + 0.5f) * C);
                    bool clear = true;
                    foreach (var p in L.props)
                        if (p.kind != PropKind.WallClock && (p.position - centre).sqrMagnitude < 1.9f * 1.9f) { clear = false; break; }
                    if (!clear) continue;
                    // Inward normal of that wall and the direction along it.
                    var inward = side == 0 ? Vector2.right : side == 1 ? Vector2.left : side == 2 ? Vector2.up : Vector2.down;
                    var along = new Vector2(inward.y, -inward.x);
                    var wall = centre - inward * C * 0.5f;
                    int count = rng.Next(1, 4);
                    float s = (float)(rng.NextDouble() - 0.5) * 0.5f;
                    for (int i = 0; i < count; i++)
                    {
                        var size = new Vector3(0.32f + (float)rng.NextDouble() * 0.3f, 0.24f + (float)rng.NextDouble() * 0.3f, 0.3f + (float)rng.NextDouble() * 0.22f);
                        float gap = OfficeBiome.WallThickness * 0.5f + size.z * 0.5f + 0.02f + (float)rng.NextDouble() * 0.2f;
                        var pos = wall + inward * gap + along * s;
                        // Stay inside the room, off door cells, clear of side walls.
                        int bx = Mathf.FloorToInt(pos.x / C), bz = Mathf.FloorToInt(pos.y / C);
                        if (!rc.Contains(new Vector2Int(bx, bz)) || CellHasDoor(L, bx, bz)) break;
                        float fx = pos.x / C - bx, fz = pos.y / C - bz, m = (size.x * 0.75f + 0.1f) / C;
                        if ((fx < m && L.EdgeOf(bx, bz, 0) != EdgeKind.Open) || (fx > 1f - m && L.EdgeOf(bx, bz, 1) != EdgeKind.Open)
                            || (fz < m && L.EdgeOf(bx, bz, 2) != EdgeKind.Open) || (fz > 1f - m && L.EdgeOf(bx, bz, 3) != EdgeKind.Open))
                        {
                            // Only the wall it backs onto may be close.
                            bool ok = true;
                            if (fx < m && side != 0 && L.EdgeOf(bx, bz, 0) != EdgeKind.Open) ok = false;
                            if (fx > 1f - m && side != 1 && L.EdgeOf(bx, bz, 1) != EdgeKind.Open) ok = false;
                            if (fz < m && side != 2 && L.EdgeOf(bx, bz, 2) != EdgeKind.Open) ok = false;
                            if (fz > 1f - m && side != 3 && L.EdgeOf(bx, bz, 3) != EdgeKind.Open) ok = false;
                            if (!ok) break;
                        }
                        float yaw = Mathf.Atan2(inward.x, inward.y) * Mathf.Rad2Deg + (float)(rng.NextDouble() - 0.5) * 30f;
                        // (No loose boxes of the world's own any more, 2026-11-09, the owner: every box is a client's now.
                        // The dice are still rolled, so everything after comes out as before.)
                        rng.Next(1 << 16);
                        s += size.x * 0.5f + 0.06f + (float)rng.NextDouble() * 0.15f + 0.2f;
                    }
                    break;
                }
            }
        }

        /// <summary>Round wall clock on a solid wall, about 2.1 m up (higher in tall rooms). Not placed for now.</summary>
        static void TryClock(OfficeLayout L, System.Random rng, Room r)
        {
            double chance = r.kind == RoomKind.Lounge ? 0.45 : r.kind == RoomKind.Conference ? 0.5
                          : r.kind == RoomKind.BigOffice ? 0.2 : r.kind == RoomKind.Office ? 0.08 : 0.0;
            if (rng.NextDouble() >= chance) return;
            float C = OfficeBiome.CellSize;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int x = rng.Next(r.rect.xMin, r.rect.xMax), z = rng.Next(r.rect.yMin, r.rect.yMax), side = rng.Next(4);
                if (L.EdgeOf(x, z, side) != EdgeKind.Wall) continue;
                float off = OfficeBiome.WallThickness * 0.5f;
                Vector2 pos = side switch
                {
                    0 => new Vector2(x * C + off, (z + 0.5f) * C),
                    1 => new Vector2((x + 1) * C - off, (z + 0.5f) * C),
                    2 => new Vector2((x + 0.5f) * C, z * C + off),
                    _ => new Vector2((x + 0.5f) * C, (z + 1) * C - off),
                };
                float yaw = side == 0 ? 90f : side == 1 ? -90f : side == 2 ? 0f : 180f;
                float h = Mathf.Min(r.ceiling - 0.45f, r.Tall ? 2.6f : 2.1f);
                L.props.Add(new Prop { kind = PropKind.WallClock, position = pos, yaw = yaw, size = new Vector3(0, h, 0), roomColor = r.color });
                return;
            }
        }

        internal static bool NearPillarOrHalfWall(OfficeLayout L, int x, int z)
        {
            foreach (var p in L.pillars) if (p.vertex.x >= x - 1 && p.vertex.x <= x + 2 && p.vertex.y >= z - 1 && p.vertex.y <= z + 2) return true;
            foreach (var h in L.halfWalls)
            {
                int a = h.alongX ? x : z, l = h.alongX ? z : x;
                if (h.line >= l - 1 && h.line <= l + 2 && a >= h.start - 2 && a <= h.start + h.length + 1) return true;
            }
            return false;
        }

        internal static bool CellHasDoor(OfficeLayout L, int x, int z)
        {
            for (int s = 0; s < 4; s++) { var e = L.EdgeOf(x, z, s); if (e == EdgeKind.Door || e == EdgeKind.DoorHeader) return true; }
            // (Steps down into a sunken room, and a pit room's lattice, keep clear too.)
            return DepthPlan.Busy(L, x, z);
        }

        /// <summary>Sofa against one room wall, occupying two cells whose edges on that side are solid.</summary>
        static bool TrySofa(OfficeLayout L, System.Random rng, Room r, int side, HashSet<Vector2Int> occupied)
        {
            var rc = r.rect;
            float C = OfficeBiome.CellSize;
            bool wallAlongX = side >= 2;            // wall on -Z / +Z runs along X
            int runLen = wallAlongX ? rc.width : rc.height;
            if (runLen < 2) return false;
            int start = rng.Next(0, runLen - 1);
            for (int i = 0; i < 2; i++)
            {
                int x = wallAlongX ? rc.xMin + start + i : (side == 0 ? rc.xMin : rc.xMax - 1);
                int z = wallAlongX ? (side == 2 ? rc.yMin : rc.yMax - 1) : rc.yMin + start + i;
                if (L.EdgeOf(x, z, side) != EdgeKind.Wall || CellHasDoor(L, x, z) || occupied.Contains(new Vector2Int(x, z))) return false;
            }
            for (int i = 0; i < 2; i++)
                occupied.Add(wallAlongX ? new Vector2Int(rc.xMin + start + i, side == 2 ? rc.yMin : rc.yMax - 1)
                                        : new Vector2Int(side == 0 ? rc.xMin : rc.xMax - 1, rc.yMin + start + i));
            float depth = 0.88f, length = 2.0f + (float)rng.NextDouble() * 0.2f;
            float along = ((wallAlongX ? rc.xMin : rc.yMin) + start + 1) * C;
            float wallPos = side == 0 ? rc.xMin * C : side == 1 ? rc.xMax * C : side == 2 ? rc.yMin * C : rc.yMax * C;
            float inward = side == 0 || side == 2 ? 1f : -1f;
            float off = OfficeBiome.WallThickness * 0.5f + depth * 0.5f + 0.005f;   // backed right up to the wall
            Vector2 pos = wallAlongX ? new Vector2(along, wallPos + inward * off) : new Vector2(wallPos + inward * off, along);
            float yaw = side == 0 ? 90f : side == 1 ? -90f : side == 2 ? 0f : 180f;   // sofa front faces +Z locally
            L.props.Add(new Prop { kind = PropKind.Sofa, position = pos, yaw = yaw + (float)(rng.NextDouble() - 0.5) * 4f,
                size = new Vector3(length, 0.82f, depth), variant = rng.Next(4), roomColor = r.color });
            return true;
        }

        static void PlacePlates(OfficeLayout L, System.Random rng)
        {
            // An outlet on a wall beside each lit floor lamp (it is plugged into it: Interaction.FloorLamp).
            float C = OfficeBiome.CellSize;
            foreach (var p in L.props)
            {
                if (p.kind != PropKind.FloorLamp || !p.lit) continue;
                int lx = Mathf.Clamp(Mathf.FloorToInt(p.position.x / C), 0, L.N - 1), lz = Mathf.Clamp(Mathf.FloorToInt(p.position.y / C), 0, L.N - 1);
                float fx = p.position.x / C - lx, fz = p.position.y / C - lz;
                // The nearer solid wall of the lamp's cell, and the outlet along it beside the lamp (not behind it).
                int best = -1; float bd = 9f;
                for (int s = 0; s < 4; s++)
                {
                    if (L.EdgeOf(lx, lz, s) != EdgeKind.Wall) continue;
                    float d = s == 0 ? fx : s == 1 ? 1f - fx : s == 2 ? fz : 1f - fz;
                    if (d < bd) { bd = d; best = s; }
                }
                if (best < 0) continue;
                float along = best < 2 ? fz : fx;
                float t = along < 0.5f ? Mathf.Min(0.85f, along + 0.4f) : Mathf.Max(0.15f, along - 0.4f);
                L.plates.Add(new WallPlate { cell = new Vector2Int(lx, lz), side = best, t = t, height = 0.3f, roomColor = p.roomColor, live = L.RoomAt(lx, lz).live });
            }
            for (int x = 0; x < L.N; x++)
            for (int z = 0; z < L.N; z++)
            {
                var room = L.RoomAt(x, z);
                if (room.kind == RoomKind.Crawl || room.noFloor || room.role != AnnexRole.None) continue;
                for (int side = 0; side < 4; side++)
                {
                    var e = L.EdgeOf(x, z, side);
                    if (e != EdgeKind.Wall)
                    {
                        // No outlets or switches beside an elevator or stair door (the call button goes there).
                        int ax = x + (side == 0 ? -1 : side == 1 ? 1 : 0), az = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                        if (L.InChunk(ax, az) && L.RoomAt(ax, az).role != AnnexRole.None) continue;
                    }
                    if (e == EdgeKind.Wall && rng.NextDouble() < L.RulesAt(x, z).outletChance)
                        L.plates.Add(new WallPlate { cell = new Vector2Int(x, z), side = side, t = 0.2f + (float)rng.NextDouble() * 0.6f, height = 0.3f, roomColor = room.color, live = room.live });
                    else if ((e == EdgeKind.Door || e == EdgeKind.DoorHeader) && (rng.NextDouble() < 0.35 || room.special != GoodRoom.None))
                    {
                        // Light switch on the wall beside the door, if there is one.
                        int perp = side < 2 ? 2 : 0;
                        for (int k = 0; k < 2; k++)
                        {
                            int ps = perp + k;
                            int nx = x + (ps == 0 ? -1 : ps == 1 ? 1 : 0), nz = z + (ps == 2 ? -1 : ps == 3 ? 1 : 0);
                            if (!L.InChunk(nx, nz) || L.roomOf[nx, nz] != L.roomOf[x, z]) continue;
                            if (L.EdgeOf(nx, nz, side) != EdgeKind.Wall) continue;
                            float t = (ps == 0 || ps == 2) ? 0.85f : 0.15f;
                            L.plates.Add(new WallPlate { cell = new Vector2Int(nx, nz), side = side, t = t, height = 1.2f, isSwitch = true, roomColor = room.color });
                            break;
                        }
                    }
                }
            }
            // A good room has at least two outlets (they have power).
            foreach (var r in L.rooms)
            {
                if (r.special == GoodRoom.None) continue;
                int have = 0;
                foreach (var p in L.plates) if (!p.isSwitch && r.rect.Contains(p.cell)) have++;
                for (int tries = 0; have < 2 && tries < 30; tries++)
                {
                    int x = rng.Next(r.rect.xMin, r.rect.xMax), z = rng.Next(r.rect.yMin, r.rect.yMax), side = rng.Next(4);
                    if (L.EdgeOf(x, z, side) != EdgeKind.Wall || L.roomOf[x, z] != r.id) continue;
                    int ax = x + (side == 0 ? -1 : side == 1 ? 1 : 0), az = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                    if (L.InChunk(ax, az) && L.roomOf[ax, az] == r.id) continue;
                    bool taken = false;
                    foreach (var p in L.plates) if (p.cell.x == x && p.cell.y == z && p.side == side) taken = true;
                    if (taken) continue;
                    L.plates.Add(new WallPlate { cell = new Vector2Int(x, z), side = side, t = 0.25f + (float)rng.NextDouble() * 0.5f, height = 0.3f, roomColor = r.color, live = true });
                    have++;
                }
            }
        }

        // ---------------- Utils ----------------

        public static int Hash(int a, int b, int c, int d)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u; h ^= h >> 13;
                h = (h ^ (uint)b) * 16777619u; h ^= h >> 15;
                h = (h ^ (uint)c) * 16777619u; h ^= h >> 13;
                h = (h ^ (uint)d) * 16777619u; h ^= h >> 16;
                return (int)h;
            }
        }

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        }
    }
}
