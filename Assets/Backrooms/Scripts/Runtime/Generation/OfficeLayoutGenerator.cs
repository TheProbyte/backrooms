using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Deterministic layout rules for the "offices" biome. Each chunk is generated from (seed, coord)
    /// alone, so chunks can be built in any order and rebuilt identically after unloading.
    ///
    /// Rules that keep the world sealed and connected:
    ///  - Chunk borders are walls except for openings derived from the shared edge key, which both
    ///    neighbours compute identically.
    ///  - Every walkable room is connected to every other walkable room inside the chunk (verified by
    ///    flood fill and repaired), so every border opening reaches every other one.
    ///  - Cells next to a border opening are always walkable.
    ///  - Crawl spaces are dead-end slots too narrow for the player and never carry connectivity.
    /// </summary>
    public static class OfficeLayoutGenerator
    {
        public const int MinRoom = 2;

        /// <summary>Default rules (offices) - used by tools and tests that don't care about biomes.</summary>
        public static OfficeLayout Generate(int seed, Vector2Int coord, int n) => Generate(seed, coord, n, BiomeRules.Offices(), "Offices");

        /// <summary>
        /// Builds one chunk from (seed, coord) using the given biome rules. Border openings never depend on
        /// the rules, so chunks generated with different biomes (or different live settings) always join up.
        /// </summary>
        public static OfficeLayout Generate(int seed, Vector2Int coord, int n, BiomeRules rules, string biomeName)
        {
            var L = new OfficeLayout(n, coord) { rules = rules ?? BiomeRules.Offices(), biomeName = biomeName };
            var R = L.rules;
            var rng = new System.Random(Hash(seed, coord.x, coord.y, 0x51ED));

            // 1. Border openings first: they constrain where crawl spaces may go.
            var openings = new List<(int x, int z, int side, bool header)>();
            AddBorder(openings, seed, coord.x, coord.y, true, n, 0);           // west  (line X = 0)
            AddBorder(openings, seed, coord.x + 1, coord.y, true, n, 1);       // east  (line X = N)
            AddBorder(openings, seed, coord.x, coord.y, false, n, 2);          // south (line Z = 0)
            AddBorder(openings, seed, coord.x, coord.y + 1, false, n, 3);      // north (line Z = N)
            var openingCells = new HashSet<Vector2Int>();
            foreach (var o in openings) openingCells.Add(new Vector2Int(o.x, o.z));

            // 2. Partition into rooms and hallways.
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++) L.roomOf[x, z] = -1;
            // The starting chunk is always one big hall; elsewhere big open halls are common.
            bool bigHall = rng.NextDouble() < R.bigHallChance || coord == Vector2Int.zero;
            Split(L, rng, new RectInt(0, 0, n, n), 0, bigHall, openingCells);

            // 3. Room kinds.
            foreach (var r in L.rooms)
            {
                if (r.kind != RoomKind.Office) continue;
                int area = r.rect.width * r.rect.height, minDim = Mathf.Min(r.rect.width, r.rect.height);
                double p = rng.NextDouble();
                if (area >= 30 && minDim >= 5) r.kind = RoomKind.BigOffice;
                else if (minDim >= 3 && area >= 9 && p < R.loungeChance) r.kind = RoomKind.Lounge;
                else if (minDim >= 3 && Mathf.Max(r.rect.width, r.rect.height) >= 4 && p < R.loungeChance + R.conferenceChance) r.kind = RoomKind.Conference;
            }

            // Ceiling heights and wall finish. Big halls often rise to tall plaster ceilings; a few large
            // ordinary rooms and lounges do too. Heights step in 0.3 m so trims line up.
            foreach (var r in L.rooms)
            {
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
            }

            // 4. Walls everywhere rooms differ, plus the chunk boundary.
            for (int x = 0; x <= n; x++)
            for (int z = 0; z < n; z++)
                L.vEdges[x, z] = (x == 0 || x == n || L.roomOf[x - 1, z] != L.roomOf[x, z]) ? EdgeKind.Wall : EdgeKind.Open;
            for (int x = 0; x < n; x++)
            for (int z = 0; z <= n; z++)
                L.hEdges[x, z] = (z == 0 || z == n || L.roomOf[x, z - 1] != L.roomOf[x, z]) ? EdgeKind.Wall : EdgeKind.Open;
            foreach (var o in openings) L.SetEdge(o.x, o.z, o.side, o.header ? EdgeKind.DoorHeader : EdgeKind.Door);

            // Crawl spaces open only onto the hallway they were cut from.
            foreach (var r in L.rooms)
                if (r.kind == RoomKind.Crawl) OpenCrawlMouth(L, r);

            // 5. Doors: random spanning tree over walkable rooms, plus loops and occasional open plan.
            ConnectRooms(L, rng);
            EnsureConnected(L, rng);
            EncloseTallRooms(L);

            // 6. Lighting, colours and dressing.
            AssignColors(L);
            foreach (var r in L.rooms)
            {
                double p = rng.NextDouble();
                r.lights = p < R.lightsAllOn ? LightMode.AllOn : p < R.lightsAllOn + R.lightsMixed ? LightMode.Mixed : LightMode.Dark;
                if (r.kind == RoomKind.Lounge && rng.NextDouble() < 0.5) r.lights = LightMode.Dark; // lamps carry the room
            }
            PlaceFixtures(L, rng);
            PlacePillarsAndHalfWalls(L, rng);
            PlaceProps(L, rng);
            PlacePlates(L, rng);
            return L;
        }

        // ---------------- Partition ----------------

        static void Split(OfficeLayout L, System.Random rng, RectInt r, int depth, bool bigHall, HashSet<Vector2Int> openingCells)
        {
            int w = r.width, h = r.height;
            bool canSplitX = w >= MinRoom * 2, canSplitZ = h >= MinRoom * 2;
            int maxDim = Mathf.Max(w, h);

            bool stop = !canSplitX && !canSplitZ;
            if (!stop && depth == 0 && bigHall) stop = true;
            if (!stop && depth > 0)
            {
                // Bigger rooms are rarer; small rooms always stop.
                var R = L.rules;
                if (maxDim <= 3) stop = rng.NextDouble() < R.stopSmall;
                else if (maxDim <= 6) stop = rng.NextDouble() < R.stopMedium;
                else if (maxDim <= 9) stop = rng.NextDouble() < R.stopLarge;
                else stop = rng.NextDouble() < R.stopHuge;
            }
            if (stop) { MakeRoom(L, r, RoomKind.Office); return; }

            bool splitX = canSplitX && (!canSplitZ || (w > h ? rng.NextDouble() < 0.8 : w < h ? rng.NextDouble() < 0.2 : rng.NextDouble() < 0.5));
            int len = splitX ? w : h;

            // Hallway carved along the split line?
            double hallChance = depth <= 1 ? L.rules.hallChanceShallow : L.rules.hallChanceDeep;
            int hallW = rng.NextDouble() < 1f - L.rules.hallWideChance ? 1 : 2;
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
                Split(L, rng, a, depth + 1, false, openingCells);
                Split(L, rng, b, depth + 1, false, openingCells);
                return;
            }

            int cut = rng.Next(MinRoom, len - MinRoom + 1);
            if (splitX)
            {
                Split(L, rng, new RectInt(r.x, r.y, cut, h), depth + 1, false, openingCells);
                Split(L, rng, new RectInt(r.x + cut, r.y, w - cut, h), depth + 1, false, openingCells);
            }
            else
            {
                Split(L, rng, new RectInt(r.x, r.y, w, cut), depth + 1, false, openingCells);
                Split(L, rng, new RectInt(r.x, r.y + cut, w, h - cut), depth + 1, false, openingCells);
            }
        }

        /// <summary>Hallway running perpendicular to the split axis. 1-wide halls may end in a crawl slot.</summary>
        static void MakeHallway(OfficeLayout L, System.Random rng, RectInt hall, bool runsAlongZ, HashSet<Vector2Int> openingCells)
        {
            int length = runsAlongZ ? hall.height : hall.width;
            int width = runsAlongZ ? hall.width : hall.height;
            if (width == 1 && length >= 6 && rng.NextDouble() < L.rules.crawlChance)
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

        static Room MakeRoom(OfficeLayout L, RectInt r, RoomKind kind)
        {
            var room = new Room { id = L.rooms.Count, rect = r, kind = kind };
            L.rooms.Add(room);
            for (int x = r.xMin; x < r.xMax; x++)
            for (int z = r.yMin; z < r.yMax; z++)
                L.roomOf[x, z] = room.id;
            return room;
        }

        // ---------------- Borders ----------------

        /// <summary>
        /// Openings on a chunk border, keyed by the border line in world chunk space so both sides agree.
        /// vertical == true: the line X = lineX * N between chunks (lineX-1, cz) and (lineX, cz).
        /// </summary>
        static void AddBorder(List<(int, int, int, bool)> list, int seed, int a, int b, bool vertical, int n, int side)
        {
            var rng = new System.Random(Hash(seed, a, b, vertical ? 0xB0D1 : 0xB0D2));
            int count = 1 + (rng.NextDouble() < 0.55 ? 1 : 0) + (rng.NextDouble() < 0.25 ? 1 : 0);
            var used = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                int pos = rng.Next(1, n - 1);
                bool wide = rng.NextDouble() < 0.3 && pos + 1 < n - 1;
                bool header = rng.NextDouble() < 0.4;
                for (int k = 0; k < (wide ? 2 : 1); k++)
                {
                    int p = pos + k;
                    if (!used.Add(p)) continue;
                    // Cell on this chunk's side of the border.
                    int x = vertical ? (side == 0 ? 0 : n - 1) : p;
                    int z = vertical ? p : (side == 2 ? 0 : n - 1);
                    list.Add((x, z, side, header));
                }
            }
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
        /// Rule: where ceiling heights change, the taller room is closed off by wall above a door header
        /// (2.1 m). Every opening out of a tall room - open plan, doors, chunk-border openings - becomes a
        /// headed doorway, so the space above a lower ceiling can never be seen from the tall side.
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
                float a = x > 0 ? L.CeilingAt(x - 1, z) : -1f, b = x < n ? L.CeilingAt(x, z) : -1f;
                if (NeedsHeader(a, b)) L.vEdges[x, z] = EdgeKind.DoorHeader;
            }
            for (int x = 0; x < n; x++)
            for (int z = 0; z <= n; z++)
            {
                var e = L.hEdges[x, z];
                if (e == EdgeKind.Wall || e == EdgeKind.DoorHeader) continue;
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
                if (!ra.Walkable || !rb.Walkable) continue;
                bool joined = Find(s.a) != Find(s.b);
                bool loop = !joined && rng.NextDouble() < L.rules.loopChance;
                if (!joined && !loop) continue;
                if (joined) uf[Find(s.a)] = Find(s.b);

                bool openPlan = s.edges.Count >= 3 && rng.NextDouble() < L.rules.openPlanChance &&
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
            for (int guard = 0; guard < 64; guard++)
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
                    if (!reached[x, z] || !L.RoomAt(x, z).Walkable) continue;
                    for (int side = 0; side < 4; side++)
                    {
                        int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                        if (!L.InChunk(nx, nz) || reached[nx, nz] || !L.RoomAt(nx, nz).Walkable) continue;
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
            Debug.LogWarning($"[Backrooms] Chunk {L.coord} could not be fully connected.");
        }

        static bool[,] Flood(OfficeLayout L)
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

        static void AssignColors(OfficeLayout L)
        {
            // Rooms joined by open-plan edges (no wall between them) form one light zone, so their bounce
            // light blends across the floor instead of stopping at an invisible seam.
            var zone = new int[L.rooms.Count];
            for (int i = 0; i < zone.Length; i++) zone[i] = i;
            int Find(int i) { while (zone[i] != i) i = zone[i] = zone[zone[i]]; return i; }
            var shared = SharedBoundaries(L);
            foreach (var s in shared)
            {
                bool open = false;
                foreach (var e in s.edges) if (L.EdgeOf(e.x, e.z, e.side) == EdgeKind.Open) { open = true; break; }
                if (open) zone[Find(s.a)] = Find(s.b);
            }

            // Greedy colouring of zones with 7 colours so neighbouring zones rarely share a bounce layer.
            var neighbours = new Dictionary<int, HashSet<int>>();
            foreach (var s in shared)
            {
                int a = Find(s.a), b = Find(s.b);
                if (a == b) continue;
                if (!neighbours.TryGetValue(a, out var na)) neighbours[a] = na = new HashSet<int>();
                if (!neighbours.TryGetValue(b, out var nb)) neighbours[b] = nb = new HashSet<int>();
                na.Add(b); nb.Add(a);
            }
            int offset = Hash(L.coord.x, L.coord.y, 7, 0xC010) & 0x7FFFFFFF;
            var zoneColor = new Dictionary<int, int>();
            foreach (var r in L.rooms)
            {
                int z = Find(r.id);
                if (zoneColor.TryGetValue(z, out int known)) { r.color = known; continue; }
                for (int k = 0; k < 7; k++)
                {
                    int c = (offset + z + k) % 7;
                    bool clash = false;
                    if (neighbours.TryGetValue(z, out var nbs))
                        foreach (var nb in nbs) if (zoneColor.TryGetValue(nb, out int nc) && nc == c) clash = true;
                    if (!clash || k == 6) { zoneColor[z] = c; r.color = c; break; }
                }
            }
        }

        static void PlaceFixtures(OfficeLayout L, System.Random rng)
        {
            foreach (var r in L.rooms)
            {
                if (r.kind == RoomKind.Crawl) continue;
                int ox = r.rect.width % 2 == 1 ? 0 : 1, oz = r.rect.height % 2 == 1 ? 0 : 1;
                if (r.rect.width == 2) ox = rng.Next(2);
                if (r.rect.height == 2) oz = rng.Next(2);
                for (int x = r.rect.xMin; x < r.rect.xMax; x++)
                for (int z = r.rect.yMin; z < r.rect.yMax; z++)
                {
                    if ((x - r.rect.xMin) % 2 != ox % 2 || (z - r.rect.yMin) % 2 != oz % 2) continue;
                    bool on = r.lights == LightMode.AllOn ? rng.NextDouble() > 0.04
                            : r.lights == LightMode.Mixed ? rng.NextDouble() < L.rules.mixedOnFraction
                            : rng.NextDouble() < 0.04;
                    L.fixtures.Add(new Fixture { cell = new Vector2Int(x, z), on = on, roomColor = r.color,
                        dome = r.Tall, height = r.ceiling, faulty = on && rng.NextDouble() < L.rules.faultyChance });
                }
            }
        }

        static void PlacePillarsAndHalfWalls(OfficeLayout L, System.Random rng)
        {
            foreach (var r in L.rooms)
            {
                var rc = r.rect;
                if (r.kind == RoomKind.BigOffice)
                {
                    int spacing = Mathf.Max(2, Mathf.RoundToInt(L.rules.pillarSpacing + (float)(rng.NextDouble() - 0.5)));
                    float size = rng.NextDouble() < 0.5 ? 0.45f : 0.6f;
                    int sx = rc.xMin + Mathf.Max(2, (rc.width % spacing) / 2 + 1), sz = rc.yMin + Mathf.Max(2, (rc.height % spacing) / 2 + 1);
                    bool sparse = rng.NextDouble() < 0.35;
                    bool racks = rng.NextDouble() < L.rules.rackChance;
                    // Racked halls skip the free-standing column grid so the rack rows stay unbroken.
                    if (racks || rng.NextDouble() >= L.rules.pillarChance) sx = int.MaxValue;   // no pillar grid
                    for (int x = sx; x <= rc.xMax - 2; x += spacing)
                    for (int z = sz; z <= rc.yMax - 2; z += spacing)
                    {
                        if (sparse && rng.NextDouble() < 0.5) continue;
                        L.pillars.Add(new Pillar { vertex = new Vector2Int(x, z), size = size, roomColor = r.color });
                    }
                    int runs = rng.Next(0, Mathf.RoundToInt(L.rules.halfWalls) + 1);
                    for (int i = 0; i < runs; i++) TryHalfWall(L, rng, r);
                    if (racks) PlaceRacks(L, rng, r);
                }
                else if ((r.kind == RoomKind.Office) && rc.width >= 4 && rc.height >= 4)
                {
                    if (rng.NextDouble() < 0.3)
                        L.pillars.Add(new Pillar { vertex = new Vector2Int(rc.xMin + rc.width / 2, rc.yMin + rc.height / 2), size = 0.45f, roomColor = r.color });
                    else if (rng.NextDouble() < 0.3) TryHalfWall(L, rng, r);
                }
            }
        }

        /// <summary>
        /// Warehouse storage: rows of racks along X, one cell deep, with two-cell aisles between rows and a
        /// cross aisle every few racks, kept one cell clear of the walls and away from pillars. Aisles keep
        /// the room fully walkable.
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
                    if (++seg % (crossEvery + 1) == 0) continue;               // cross aisle
                    bool blocked = false;
                    foreach (var p in L.pillars)
                        if (p.vertex.x >= x - 1 && p.vertex.x <= x + 3 && p.vertex.y >= z - 1 && p.vertex.y <= z + 2) blocked = true;
                    if (blocked) continue;
                    L.props.Add(new Prop { kind = PropKind.StorageRack, position = new Vector2((x + 1) * C, (z + 0.5f) * C),
                        yaw = 0f, size = new Vector3(2.3f, h, 0.95f), variant = rng.Next(1 << 16), roomColor = r.color });
                }
            }
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

        static void PlaceProps(OfficeLayout L, System.Random rng)
        {
            float C = OfficeBiome.CellSize;
            var occupied = new HashSet<Vector2Int>();
            foreach (var r in L.rooms)
            {
                var rc = r.rect;
                TryClock(L, rng, r);
                bool hasRacks = false;
                foreach (var pr in L.props)
                    if (pr.kind == PropKind.StorageRack && rc.Contains(new Vector2Int(Mathf.FloorToInt(pr.position.x / C), Mathf.FloorToInt(pr.position.y / C)))) { hasRacks = true; break; }
                if (r.kind == RoomKind.BigOffice && !hasRacks && rng.NextDouble() < L.rules.scatteredSeating * (r.Tall ? 1.5f : 1f))
                {
                    // Abandoned seating left scattered across a big hall, at odd angles.
                    int count = rng.Next(2, 6);
                    for (int i = 0, tries = 0; i < count && tries < 30; tries++)
                    {
                        int cx = rng.Next(rc.xMin + 1, rc.xMax - 1), cz = rng.Next(rc.yMin + 1, rc.yMax - 1);
                        var cell = new Vector2Int(cx, cz);
                        if (occupied.Contains(cell) || NearPillarOrHalfWall(L, cx, cz)) continue;
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
                if (r.kind == RoomKind.Lounge)
                {
                    int sofas = rng.Next(1, 4);
                    var usedWalls = new HashSet<int>();
                    for (int i = 0; i < sofas * 4 && usedWalls.Count < sofas; i++)
                    {
                        int side = rng.Next(4);
                        if (usedWalls.Contains(side)) continue;
                        if (TrySofa(L, rng, r, side, occupied)) usedWalls.Add(side);
                    }
                    // Floor lamp in a free corner.
                    int corner = rng.Next(4);
                    float lx = (corner & 1) == 0 ? rc.xMin * C + 0.45f : rc.xMax * C - 0.45f;
                    float lz = (corner & 2) == 0 ? rc.yMin * C + 0.45f : rc.yMax * C - 0.45f;
                    int cx = Mathf.Clamp(Mathf.FloorToInt(lx / C), 0, L.N - 1), cz = Mathf.Clamp(Mathf.FloorToInt(lz / C), 0, L.N - 1);
                    if (!CellHasDoor(L, cx, cz) && !occupied.Contains(new Vector2Int(cx, cz)))
                        L.props.Add(new Prop { kind = PropKind.FloorLamp, position = new Vector2(lx, lz), variant = rng.Next(2), lit = rng.NextDouble() < 0.8, roomColor = r.color });
                }
                else if (r.kind == RoomKind.Conference)
                {
                    bool alongX = rc.width >= rc.height;
                    float len = (alongX ? rc.width : rc.height) * C - 2.2f;
                    var center = new Vector2((rc.xMin + rc.width * 0.5f) * C, (rc.yMin + rc.height * 0.5f) * C);
                    L.props.Add(new Prop { kind = PropKind.ConferenceTable, position = center, yaw = alongX ? 0f : 90f,
                        size = new Vector3(Mathf.Clamp(len, 1.8f, 5.4f), 0.74f, 1.1f), variant = rng.Next(2), roomColor = r.color });
                }
                else if (r.kind == RoomKind.Office && rc.width >= 3 && rc.height >= 3 && rng.NextDouble() < 0.07)
                {
                    int corner = rng.Next(4);
                    float lx = (corner & 1) == 0 ? rc.xMin * C + 0.4f : rc.xMax * C - 0.4f;
                    float lz = (corner & 2) == 0 ? rc.yMin * C + 0.4f : rc.yMax * C - 0.4f;
                    int cx = Mathf.Clamp(Mathf.FloorToInt(lx / C), 0, L.N - 1), cz = Mathf.Clamp(Mathf.FloorToInt(lz / C), 0, L.N - 1);
                    if (!CellHasDoor(L, cx, cz) && !occupied.Contains(new Vector2Int(cx, cz)))
                        L.props.Add(new Prop { kind = PropKind.FloorLamp, position = new Vector2(lx, lz), variant = rng.Next(2), lit = rng.NextDouble() < 0.6, roomColor = r.color });
                }
            }
        }

        /// <summary>Round wall clock on a solid wall, about 2.1 m up (higher in tall rooms).</summary>
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

        static bool NearPillarOrHalfWall(OfficeLayout L, int x, int z)
        {
            foreach (var p in L.pillars) if (p.vertex.x >= x - 1 && p.vertex.x <= x + 2 && p.vertex.y >= z - 1 && p.vertex.y <= z + 2) return true;
            foreach (var h in L.halfWalls)
            {
                int a = h.alongX ? x : z, l = h.alongX ? z : x;
                if (h.line >= l - 1 && h.line <= l + 2 && a >= h.start - 2 && a <= h.start + h.length + 1) return true;
            }
            return false;
        }

        static bool CellHasDoor(OfficeLayout L, int x, int z)
        {
            for (int s = 0; s < 4; s++) { var e = L.EdgeOf(x, z, s); if (e == EdgeKind.Door || e == EdgeKind.DoorHeader) return true; }
            return false;
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
            for (int x = 0; x < L.N; x++)
            for (int z = 0; z < L.N; z++)
            {
                var room = L.RoomAt(x, z);
                if (room.kind == RoomKind.Crawl) continue;
                for (int side = 0; side < 4; side++)
                {
                    var e = L.EdgeOf(x, z, side);
                    if (e == EdgeKind.Wall && rng.NextDouble() < L.rules.outletChance)
                        L.plates.Add(new WallPlate { cell = new Vector2Int(x, z), side = side, t = 0.2f + (float)rng.NextDouble() * 0.6f, height = 0.3f, roomColor = room.color });
                    else if ((e == EdgeKind.Door || e == EdgeKind.DoorHeader) && rng.NextDouble() < 0.35)
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
