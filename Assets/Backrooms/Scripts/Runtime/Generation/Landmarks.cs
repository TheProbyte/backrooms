using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Landmarks: rare set pieces placed on a sparse grid (one landmark at most per
    /// <see cref="WorldGenSettings.landmarkSpacing"/> x spacing districts), decided from the seed and the
    /// zone/biome layers only, so every district (and every storey) agrees on where they are. A district
    /// reserves its landmarks' rectangles before rooms fill in, then lets the landmark shape its rooms.
    ///
    /// To add a landmark shape: add a <see cref="LandmarkKind"/>, a case in <see cref="Rooms"/> (its rooms per
    /// storey), and optionally in <see cref="Connect"/> (internal open edges, forced doors) and
    /// <see cref="Decorate"/> (pillars, railings). Then create a LandmarkDefinition asset for it.
    /// </summary>
    public static class Landmarks
    {
        const int Margin = 3;            // cells kept clear between a landmark and the district edge

        /// <summary>The landmark in one landmark grid cell, if any.</summary>
        public static bool TryPlace(WorldGenSettings set, int seed, Vector2Int gridCell, out LandmarkPlacement p)
        {
            p = default;
            if (set.landmarks == null || set.landmarks.Count == 0) return false;
            int sp = Mathf.Max(1, set.landmarkSpacing);
            int h = OfficeLayoutHash(seed, gridCell.x, gridCell.y, 0x1A4D);
            var rng = new System.Random(h);
            var district = new Vector2Int(gridCell.x * sp + rng.Next(sp), gridCell.y * sp + rng.Next(sp));
            int D = WorldGenSettings.DistrictCells;

            // Which landmarks may go here (zone and biome at the district centre)?
            var hit = set.ZoneAt(seed, district);
            string zone = set.zones[Mathf.Clamp(hit.zone, 0, set.zones.Count - 1)].name;
            var centreChunk = new Vector2Int(district.x * WorldGenSettings.DistrictChunks + 2, district.y * WorldGenSettings.DistrictChunks + 2);
            string biome = set.Evaluate(seed, centreChunk).Dominant;
            float total = 0f;
            foreach (var l in set.landmarks) if (Allowed(l, zone, biome)) total += Mathf.Max(0f, l.weight);
            if (total <= 0f) return false;
            float pick = (float)rng.NextDouble() * total;
            LandmarkRules chosen = null;
            foreach (var l in set.landmarks)
            {
                if (!Allowed(l, zone, biome)) continue;
                pick -= Mathf.Max(0f, l.weight);
                if (pick < 0f) { chosen = l; break; }
            }
            if (chosen == null || rng.NextDouble() >= chosen.chance) return false;

            bool turn = rng.NextDouble() < 0.5;
            int w = Mathf.Clamp(turn ? chosen.depth : chosen.width, 4, D - 2 * Margin);
            int d = Mathf.Clamp(turn ? chosen.width : chosen.depth, 4, D - 2 * Margin);
            // An atrium gets a wing (stairs and/or elevator) along one side, as long as the stairs need.
            int connectors = 0, side = 0, bw = w, bh = d;
            if (chosen.kind == LandmarkKind.Atrium)
            {
                double c = rng.NextDouble();
                connectors = c < 0.4 ? AtriumAnnex.Stairs : c < 0.8 ? AtriumAnnex.Elevator : AtriumAnnex.Stairs | AtriumAnnex.Elevator;
                side = rng.Next(4);
                if (side < 2) { w = Mathf.Max(w, AtriumAnnex.MinLength); bw = w; bh = d + AtriumAnnex.Depth; }
                else { d = Mathf.Max(d, AtriumAnnex.MinLength); bw = w + AtriumAnnex.Depth; bh = d; }
            }
            int lo = Margin;
            // The spawn hall fills the first chunk of district (0, 0): stay clear of it.
            if (district == Vector2Int.zero) lo = OfficeBiome.ChunkCells + Margin;
            int x = rng.Next(lo, Mathf.Max(lo + 1, D - Margin - bw + 1));
            int z = rng.Next(lo, Mathf.Max(lo + 1, D - Margin - bh + 1));
            var rect = new RectInt(x, z, bw, bh);
            RectInt core = rect, annex = default;
            if (connectors != 0)
            {
                switch (side)
                {
                    case 0: annex = new RectInt(x, z, w, AtriumAnnex.Depth); core = new RectInt(x, z + AtriumAnnex.Depth, w, d); break;
                    case 1: core = new RectInt(x, z, w, d); annex = new RectInt(x, z + d, w, AtriumAnnex.Depth); break;
                    case 2: annex = new RectInt(x, z, AtriumAnnex.Depth, d); core = new RectInt(x + AtriumAnnex.Depth, z, w, d); break;
                    default: core = new RectInt(x, z, w, d); annex = new RectInt(x + w, z, AtriumAnnex.Depth, d); break;
                }
            }
            p = new LandmarkPlacement
            {
                name = chosen.name, kind = chosen.kind, district = district, rect = rect, core = core, annex = annex, side = side,
                connectors = connectors, storeys = Mathf.Max(1, chosen.storeys), seed = rng.Next(),
            };
            return true;
        }

        static bool Allowed(LandmarkRules l, string zone, string biome) =>
            (l.zones.Count == 0 || l.zones.Contains(zone)) && (l.biomes.Count == 0 || l.biomes.Contains(biome));

        /// <summary>Landmarks whose rectangle lies in this district (any storey they span).</summary>
        public static void InDistrict(WorldGenSettings set, int seed, Vector2Int district, List<LandmarkPlacement> into)
        {
            int sp = Mathf.Max(1, set.landmarkSpacing);
            var cell = new Vector2Int(FloorDiv(district.x, sp), FloorDiv(district.y, sp));
            if (TryPlace(set, seed, cell, out var p) && p.district == district) into.Add(p);
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
        static int OfficeLayoutHash(int a, int b, int c, int d) => DistrictGenerator.Hash(a, b, c, d);

        // ---------------- Shapes ----------------

        /// <summary>A room a landmark needs: rectangle (district-local cells) and its fixed properties.</summary>
        public struct RoomSpec
        {
            public RectInt rect;
            public RoomKind kind;
            public float ceiling;
            public bool painted, openAbove, noFloor, noDoors;
            public AnnexRole role;
            public LightMode lights;
        }

        /// <summary>The rooms that make up a landmark on one storey (empty if it does not reach that storey).</summary>
        public static List<RoomSpec> Rooms(in LandmarkPlacement p, int storey, float storeyPitch)
        {
            var list = new List<RoomSpec>();
            if (storey < 0 || storey >= p.storeys) return list;
            var r = p.rect;
            switch (p.kind)
            {
                case LandmarkKind.ColonnadeHall:
                    list.Add(new RoomSpec { rect = r, kind = RoomKind.BigOffice, ceiling = 5.4f, painted = true, lights = LightMode.AllOn });
                    break;

                case LandmarkKind.Atrium:
                {
                    // A two-cell arcade ring around a lightwell. Ground storey: the ring has an ordinary
                    // ceiling, the well rises to the storey above (whose ceiling closes it). Upper storey: the
                    // ring is a balcony with a railing, the well has no floor. The annex (stairs / elevator)
                    // rooms come first in the list; the ring (four rooms) and the well are the last five.
                    if (AtriumAnnex.TryGet(p, storeyPitch, out var annex)) AnnexRooms(annex, storey, storeyPitch, list);
                    const int ring = 2;
                    var core = p.core.width > 0 ? p.core : r;
                    var inner = new RectInt(core.x + ring, core.y + ring, core.width - 2 * ring, core.height - 2 * ring);
                    bool ground = storey == 0;
                    float ringCeil = OfficeBiome.CeilingHeight;
                    void Ring(RectInt rr) => list.Add(new RoomSpec { rect = rr, kind = RoomKind.Hallway, ceiling = ringCeil, lights = LightMode.AllOn, painted = !ground });
                    Ring(new RectInt(core.x, core.y, core.width, ring));                                  // south
                    Ring(new RectInt(core.x, core.yMax - ring, core.width, ring));                        // north
                    Ring(new RectInt(core.x, core.y + ring, ring, core.height - 2 * ring));               // west
                    Ring(new RectInt(core.xMax - ring, core.y + ring, ring, core.height - 2 * ring));     // east
                    if (ground)
                        list.Add(new RoomSpec { rect = inner, kind = RoomKind.BigOffice, ceiling = storeyPitch + OfficeBiome.CeilingHeight, painted = true, openAbove = true, lights = LightMode.AllOn });
                    else
                        list.Add(new RoomSpec { rect = inner, kind = RoomKind.BigOffice, ceiling = OfficeBiome.CeilingHeight, painted = true, noFloor = true, lights = LightMode.Dark });
                    break;
                }
            }
            return list;
        }

        /// <summary>The stairs / elevator wing of an atrium on one storey (see <see cref="AtriumAnnex"/> for the plan).</summary>
        static void AnnexRooms(in AtriumAnnex.Plan a, int storey, float pitch, List<RoomSpec> list)
        {
            bool ground = storey == 0;
            float top = pitch + OfficeBiome.CeilingHeight;
            const float h = OfficeBiome.CeilingHeight;
            // Band S (v 0-1): stair hall and landing, or one filler room.
            if (a.stairs)
            {
                if (ground)
                {
                    list.Add(new RoomSpec { rect = a.Hall, kind = RoomKind.Hallway, ceiling = top, painted = true, openAbove = true, noDoors = true, role = AnnexRole.StairHall, lights = LightMode.AllOn });
                    list.Add(new RoomSpec { rect = a.Landing, kind = RoomKind.Office, ceiling = h, lights = LightMode.Dark });         // under the landing: a closet
                }
                else
                {
                    list.Add(new RoomSpec { rect = a.Hall, kind = RoomKind.Hallway, ceiling = h, painted = true, noFloor = true, noDoors = true, role = AnnexRole.StairVoid, lights = LightMode.Dark });
                    list.Add(new RoomSpec { rect = a.Landing, kind = RoomKind.Office, ceiling = h, painted = true, lights = LightMode.AllOn });
                }
            }
            else list.Add(new RoomSpec { rect = a.BandSFull, kind = RoomKind.Office, ceiling = h, lights = LightMode.Mixed });
            // Band N (v 2-4): lobby and shaft, or one filler room.
            if (a.elevator)
            {
                list.Add(new RoomSpec { rect = a.Lobby, kind = RoomKind.Hallway, ceiling = h, painted = true, lights = LightMode.AllOn });
                if (ground)
                    list.Add(new RoomSpec { rect = a.Shaft, kind = RoomKind.Office, ceiling = top, painted = true, openAbove = true, noDoors = true, role = AnnexRole.ShaftGround, lights = LightMode.Dark });
                else
                    list.Add(new RoomSpec { rect = a.Shaft, kind = RoomKind.Office, ceiling = h, painted = true, noFloor = true, noDoors = true, role = AnnexRole.ShaftUpper, lights = LightMode.Dark });
            }
            else list.Add(new RoomSpec { rect = a.BandNFull, kind = RoomKind.Office, ceiling = h, painted = !ground, lights = LightMode.AllOn });
        }

        static void SetLocalEdge(OfficeLayout L, in AtriumAnnex.Plan a, int u, int v, int du, int dv, EdgeKind k)
        {
            var c = a.f.Cell(u, v);
            L.SetEdge(c.x, c.y, AtriumAnnex.Frame.SideOf(a.f.Dir(du, dv)), k);
        }

        /// <summary>Doors of the wing: the stair hall's entrance, the way off the landing, doors to the atrium ring, the elevator door.</summary>
        static void ConnectAnnex(OfficeLayout L, in AtriumAnnex.Plan a)
        {
            bool ground = L.storey == 0;
            if (a.stairs)
            {
                if (ground) SetLocalEdge(L, a, 0, 1, 0, 1, EdgeKind.DoorHeader);                       // lobby -> foot of the stairs
                else
                {
                    for (int v = 0; v < AtriumAnnex.BandS; v++) SetLocalEdge(L, a, a.HallCells - 1, v, 1, 0, EdgeKind.Open);   // the top step runs onto the landing
                    SetLocalEdge(L, a, a.HallCells, 1, 0, 1, EdgeKind.Door);                          // landing -> lobby
                }
            }
            int top = AtriumAnnex.BandS + AtriumAnnex.BandN - 1;
            foreach (int u in new[] { 3, 9 }) SetLocalEdge(L, a, u, top, 0, 1, EdgeKind.Door);        // lobby / filler -> the atrium's ring
            if (a.elevator)
            {
                var d = a.DoorLobbyCell;
                SetLocalEdge(L, a, d.u, d.v, 1, 0, EdgeKind.DoorHeader);                              // lobby -> elevator
            }
        }

        /// <summary>
        /// After the plan's walls exist: open the landmark's internal edges and force its entrances.
        /// rooms: the plan rooms created from <see cref="Rooms"/>, in the same order.
        /// </summary>
        public static void Connect(OfficeLayout L, in LandmarkPlacement p, List<Room> rooms)
        {
            var r = p.rect;
            switch (p.kind)
            {
                case LandmarkKind.ColonnadeHall:
                {
                    // Arched doorways in the middle of each side, where the room beyond can be walked into.
                    void Door(int x, int z, int side)
                    {
                        int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                        if (!L.InChunk(nx, nz) || !L.RoomAt(nx, nz).Walkable) return;
                        L.SetEdge(x, z, side, EdgeKind.DoorHeader);
                        L.SetArch(x, z, side, true);
                    }
                    int mx = r.x + r.width / 2, mz = r.y + r.height / 2;
                    Door(mx, r.y, 2); Door(mx, r.yMax - 1, 3); Door(r.x, mz, 0); Door(r.xMax - 1, mz, 1);
                    break;
                }
                case LandmarkKind.Atrium:
                {
                    // Everything inside the core is one open space (ring strips and well).
                    var core = p.core.width > 0 ? p.core : r;
                    for (int x = core.xMin; x < core.xMax; x++)
                    for (int z = core.yMin; z < core.yMax; z++)
                    {
                        if (x + 1 < core.xMax) L.vEdges[x + 1, z] = EdgeKind.Open;
                        if (z + 1 < core.yMax) L.hEdges[x, z + 1] = EdgeKind.Open;
                    }
                    if (AtriumAnnex.TryGet(p, L.storeyPitch, out var annex)) ConnectAnnex(L, annex);
                    break;
                }
            }
        }

        /// <summary>Stair hall lights follow the sloping ceiling; the elevator door is never arched.</summary>
        static void DecorateAnnex(OfficeLayout L, in AtriumAnnex.Plan a, List<Room> rooms)
        {
            if (a.elevator)
            {
                var d = a.DoorLobbyCell;
                var c = a.f.Cell(d.u, d.v);
                L.SetArch(c.x, c.y, AtriumAnnex.Frame.SideOf(a.f.Dir(1, 0)), false);
            }
            if (!a.stairs || L.storey != 0) return;
            int colour = 0;
            foreach (var r in rooms) if (r.role == AnnexRole.StairHall) colour = r.color;
            // Twin-tube battens hung from the slope, one cell column in three, alternating sides.
            for (int u = 1, k = 0; u < a.HallCells - 1; u += 3, k++)
            {
                var cell = a.f.Cell(u, k & 1);
                float ceil = a.CeilingAt((u + 0.5f) * AtriumAnnex.C);
                L.fixtures.Add(new Fixture { cell = cell, on = true, roomColor = colour, strip = true, height = ceil - 0.45f });
            }
        }

        /// <summary>Landmark dressing, after the generic dressing (which skips landmark rooms).</summary>
        public static void Decorate(OfficeLayout L, in LandmarkPlacement p, List<Room> rooms)
        {
            var r = p.rect;
            switch (p.kind)
            {
                case LandmarkKind.ColonnadeHall:
                {
                    var room = rooms[0];
                    bool alongZ = r.height >= r.width;
                    int len = alongZ ? r.height : r.width;
                    int across0 = (alongZ ? r.x : r.y) + 3, across1 = (alongZ ? r.xMax : r.yMax) - 3;
                    for (int k = 2; k <= len - 2; k += 2)
                    {
                        int a = (alongZ ? r.y : r.x) + k;
                        foreach (int c in new[] { across0, across1 })
                            L.pillars.Add(new Pillar { vertex = alongZ ? new Vector2Int(c, a) : new Vector2Int(a, c), size = 0.6f, roomColor = room.id });
                    }
                    break;
                }
                case LandmarkKind.Atrium:
                {
                    if (AtriumAnnex.TryGet(p, L.storeyPitch, out var annex)) DecorateAnnex(L, annex, rooms);
                    if (L.storey == 0) break;
                    // Balcony railing around the well.
                    var well = rooms[rooms.Count - 1].rect;
                    int col = rooms[rooms.Count - 5].id;
                    L.halfWalls.Add(new HalfWall { alongX = true, line = well.yMin, start = well.xMin, length = well.width, roomColor = col, railing = true });
                    L.halfWalls.Add(new HalfWall { alongX = true, line = well.yMax, start = well.xMin, length = well.width, roomColor = col, railing = true });
                    L.halfWalls.Add(new HalfWall { alongX = false, line = well.xMin, start = well.yMin, length = well.height, roomColor = col, railing = true });
                    L.halfWalls.Add(new HalfWall { alongX = false, line = well.xMax, start = well.yMin, length = well.height, roomColor = col, railing = true });
                    break;
                }
            }
        }
    }
}
