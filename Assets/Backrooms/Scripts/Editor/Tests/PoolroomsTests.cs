using System.Collections.Generic;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The Poolrooms: a pinned, sealed zone reached through a far crawl hole from the spawn district.</summary>
    public class PoolroomsTests
    {
        static WorldGenSettings Set() => WorldGenSettings.Defaults();
        static ZoneRules Pool(WorldGenSettings s) => s.zones[s.ZoneIndex("Poolrooms")];

        [Test]
        public void PoolroomsArePinnedFarFromSpawn()
        {
            var s = Set();
            Assert.GreaterOrEqual(s.ZoneIndex("Poolrooms"), 0, "the world settings have the Poolrooms");
            var z = Pool(s);
            Assert.AreEqual(ZoneStyle.Pool, z.style);
            Assert.AreEqual("Poolrooms", s.zones[s.ZoneAt(1234, z.pinCentre).zone].name);
            Assert.AreEqual("Poolrooms", s.zones[s.ZoneAt(99, z.pinCentre + new Vector2Int(z.pinRadius, -z.pinRadius)).zone].name, "the whole square, any seed");
            Assert.AreNotEqual("Poolrooms", s.zones[s.ZoneAt(1234, z.pinCentre + new Vector2Int(z.pinRadius + 1, 0)).zone].name);
            Assert.AreEqual(0, s.ZoneAt(1234, Vector2Int.zero).zone, "spawn is Level 0");
            float km = (z.pinCentre.x - z.pinRadius) * WorldGenSettings.DistrictCells * OfficeBiome.CellSize;
            Assert.Greater(km, 1000f, "well over a kilometre from spawn");
        }

        [Test]
        public void PoolroomEdgesAreSolidOnBothSides()
        {
            var s = Set();
            var z = Pool(s);
            var gen = new WorldGenerator(1234, s);
            var inside = z.pinCentre + new Vector2Int(z.pinRadius, 0);       // east edge district
            var outside = inside + Vector2Int.right;
            var a = gen.District(new Vector3Int(inside.x, 0, inside.y));
            var b = gen.District(new Vector3Int(outside.x, 0, outside.y));
            Assert.AreEqual(ZoneStyle.Pool, a.style);
            Assert.AreEqual(ZoneStyle.Office, b.style);
            int n = a.N;
            for (int k = 0; k < n; k++)
            {
                Assert.AreEqual(EdgeKind.Wall, a.vEdges[n, k], "Poolrooms side of the edge");
                Assert.AreEqual(EdgeKind.Wall, b.vEdges[0, k], "Backrooms side of the edge");
            }
            // Between two Poolrooms districts the edge is open somewhere.
            var c = gen.District(new Vector3Int(inside.x - 1, 0, inside.y));
            bool open = false;
            for (int k = 0; k < n; k++) open |= c.vEdges[n, k] != EdgeKind.Wall;
            Assert.IsTrue(open, "Poolrooms districts connect to each other");
        }

        [Test]
        public void EveryPoolroomsDistrictHasADoorBack()
        {
            var s = Set();
            var z = Pool(s);
            foreach (int seed in new[] { 1234, 7, 2032979943 })
            {
                var gen = new WorldGenerator(seed, s);
                foreach (var d in new[] { z.pinCentre, z.pinCentre + new Vector2Int(z.pinRadius, 0), z.pinCentre - new Vector2Int(2, z.pinRadius) })
                {
                    var plan = gen.District(new Vector3Int(d.x, 0, d.y));
                    var doors = plan.noclipDoors.FindAll(n => n.arrival);
                    Assert.AreEqual(1, doors.Count, $"seed {seed}: one taped way back in district {d}");
                    Assert.IsFalse(plan.pool[doors[0].cell.x, doors[0].cell.y].basin, "you come out on a dry deck, not into water");
                    Assert.AreEqual(0, plan.portals.Count, "no crawl spaces in the Poolrooms");
                }
            }
        }

        [Test]
        public void PoolDistrictsAreWalkableAndDressed()
        {
            var s = Set();
            var gen = new WorldGenerator(1234, s);
            var L = gen.District(new Vector3Int(Pool(s).pinCentre.x, 0, Pool(s).pinCentre.y));
            Assert.AreEqual(0, L.fixtures.Count, "no ceiling lights");
            Assert.AreEqual(0, L.props.Count + L.plates.Count, "no furniture or outlets");
            // (2026-11-01: the zone has three biomes now. The Poolrooms' own rooms are mostly flooded; the water park has more
            // dry decks, the channels more water.)
            int basins = 0, sky = 0, poolCells = 0, poolBasins = 0;
            for (int x = 0; x < L.N; x++) for (int y = 0; y < L.N; y++)
            {
                if (L.pool[x, y].basin) basins++;
                if (L.pool[x, y].skylight != SkylightShape.None) sky++;
                var room = L.roomOf[x, y] >= 0 && L.roomOf[x, y] < L.rooms.Count ? L.rooms[L.roomOf[x, y]] : null;
                if (room != null && L.RulesAt(room.rect).name == "Poolrooms") { poolCells++; if (L.pool[x, y].basin) poolBasins++; }
            }
            Assert.Greater(basins, L.N * L.N / 4, "a lot of water");
            if (poolCells > 200) Assert.Greater(poolBasins, poolCells / 2, "the Poolrooms' own rooms mostly flooded");
            Assert.Greater(sky, 20, "openings to the sky");
            foreach (var r in L.rooms) Assert.GreaterOrEqual(r.ceiling, 3.29f, "tall rooms");

            // Every walkable cell is reachable from every other (doorways only; stairs connect the two floor levels).
            var seen = new bool[L.N, L.N];
            var q = new Queue<Vector2Int>();
            q.Enqueue(new Vector2Int(L.N / 2, L.N / 2)); seen[L.N / 2, L.N / 2] = true;
            int count = 1;
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                for (int side = 0; side < 4; side++)
                {
                    var nb = c + (side == 0 ? Vector2Int.left : side == 1 ? Vector2Int.right : side == 2 ? Vector2Int.down : Vector2Int.up);
                    if (!L.InChunk(nb.x, nb.y) || seen[nb.x, nb.y] || !OfficeLayout.IsPassable(L.EdgeOf(c.x, c.y, side))) continue;
                    seen[nb.x, nb.y] = true; count++; q.Enqueue(nb);
                }
            }
            Assert.AreEqual(L.N * L.N, count, "all cells connected");

            // Wherever a doorway steps down from a dry room into a basin, the basin side has stairs.
            for (int x = 0; x < L.N; x++)
            for (int y = 0; y < L.N; y++)
            {
                if (!L.pool[x, y].basin) continue;
                for (int side = 0; side < 4; side++)
                {
                    var nb = new Vector2Int(x, y) + (side == 0 ? Vector2Int.left : side == 1 ? Vector2Int.right : side == 2 ? Vector2Int.down : Vector2Int.up);
                    if (!L.InChunk(nb.x, nb.y) || L.pool[nb.x, nb.y].basin || L.roomOf[nb.x, nb.y] == L.roomOf[x, y]) continue;
                    if (!OfficeLayout.IsPassable(L.EdgeOf(x, y, side))) continue;
                    Assert.GreaterOrEqual(PoolPlan.StairSide(L, x, y, null), 0, $"stairs out of the basin at {x},{y}");
                }
            }
        }

        static int Flood(OfficeLayout L, Vector2Int from, out bool[,] seen)
        {
            seen = new bool[L.N, L.N];
            var q = new Queue<Vector2Int>();
            q.Enqueue(from); seen[from.x, from.y] = true;
            int count = 1;
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                for (int side = 0; side < 4; side++)
                {
                    var nb = c + (side == 0 ? Vector2Int.left : side == 1 ? Vector2Int.right : side == 2 ? Vector2Int.down : Vector2Int.up);
                    if (!L.InChunk(nb.x, nb.y) || seen[nb.x, nb.y] || !OfficeLayout.IsPassable(L.EdgeOf(c.x, c.y, side))) continue;
                    seen[nb.x, nb.y] = true; count++; q.Enqueue(nb);
                }
            }
            return count;
        }

        [Test]
        public void SpiralStairsLeadToASecondFloor()
        {
            var s = Set();
            var z = Pool(s);
            var gen = new WorldGenerator(1234, s);
            Assert.AreEqual(1, gen.TopStoreyAt(new Vector2Int(z.pinCentre.x * WorldGenSettings.DistrictChunks, 0)), "the second floor streams everywhere in the Poolrooms");
            Assert.AreEqual(0, gen.TopStoreyAt(Vector2Int.zero), "not at spawn");
            int towers = 0;
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                var d = z.pinCentre + new Vector2Int(dx, dz);
                var ground = gen.District(new Vector3Int(d.x, 0, d.y));
                var upper = gen.District(new Vector3Int(d.x, 1, d.y));
                Assert.AreEqual(ZoneStyle.Pool, upper.style);
                var site = PoolPlan.SpiralSite(1234, d);
                var tower = ground.rooms.Find(r => r.spiral);
                var well = upper.rooms.Find(r => r.spiral);
                Assert.AreEqual(site.HasValue, tower != null, "a tower where the site says");
                // The upper floor: every cell can be walked to from every other (the well through its one door).
                var start = new Vector2Int(0, 0);
                Assert.AreEqual(upper.N * upper.N, Flood(upper, start, out _), $"upper floor of {d} connected");
                if (tower == null) continue;
                towers++;
                Assert.IsTrue(tower.openAbove && !tower.noFloor && !ground.Basin(tower.rect.xMin, tower.rect.yMin), "the stair stands on a dry floor, open to the floor above");
                Assert.IsTrue(well != null && well.noFloor && well.rect.Equals(tower.rect), "the well above is the same square");
                Assert.IsTrue(OfficeLayout.IsPassable(upper.EdgeOf(tower.rect.xMax - 1, PoolPlan.LandingRow(tower.rect), 1)), "the top of the stair has a door out");
                int doors = 0;
                for (int x = tower.rect.xMin; x < tower.rect.xMax; x++)
                for (int y = tower.rect.yMin; y < tower.rect.yMax; y++)
                    for (int side = 0; side < 4; side++)
                    {
                        var nb = new Vector2Int(x, y) + (side == 0 ? Vector2Int.left : side == 1 ? Vector2Int.right : side == 2 ? Vector2Int.down : Vector2Int.up);
                        if (!tower.rect.Contains(nb) && OfficeLayout.IsPassable(ground.EdgeOf(x, y, side))) doors++;
                    }
                Assert.Greater(doors, 0, "the tower can be walked into on the ground floor");
            }
            Assert.Greater(towers, 3, "spiral stairs in most districts");
        }

        [Test]
        public void ArchesSpanOpenRunsOfTwoToFourCells()
        {
            var s = Set();
            var gen = new WorldGenerator(1234, s);
            int spans = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                var c = Pool(s).pinCentre;
                var L = gen.District(new Vector3Int(c.x + dx, 0, c.y));
                Assert.IsNotNull(L.spanV);
                for (int x = 0; x <= L.N; x++)
                for (int z = 0; z < L.N; z++)
                {
                    if (!L.ArchSpan(true, x, z, out int i, out int len)) continue;
                    spans++;
                    Assert.AreEqual(EdgeKind.Open, L.vEdges[x, z], "an arch is an opening");
                    Assert.That(len, Is.InRange(2, 4), "arches are two to four cells wide (longer runs are arcades)");
                    Assert.That(i, Is.InRange(0, len - 1));
                }
                foreach (var arc in L.arcs)
                {
                    var cell = new Vector2Int(Mathf.FloorToInt(arc.centre.x / OfficeBiome.CellSize), Mathf.FloorToInt(arc.centre.y / OfficeBiome.CellSize));
                    var room = L.RoomAt(cell.x, cell.y);
                    float margin = Mathf.Min(arc.centre.x - room.rect.xMin * OfficeBiome.CellSize, room.rect.xMax * OfficeBiome.CellSize - arc.centre.x,
                                             arc.centre.y - room.rect.yMin * OfficeBiome.CellSize, room.rect.yMax * OfficeBiome.CellSize - arc.centre.y);
                    Assert.Greater(margin - arc.radius, 1.0f, "a curved wall stands clear of the room's walls and doorways");
                }
            }
            Assert.Greater(spans, 10, "the Poolrooms have arches");
        }

        /// <summary>A hand-made grid for the sun test: one room, 4 m ceiling, one full roof opening.</summary>
        class Grid : IPoolCells
        {
            public HashSet<Vector2Int> sky = new HashSet<Vector2Int>();
            public HashSet<Vector2Int> wallWest = new HashSet<Vector2Int>();
            public PoolCellInfo Get(int x, int z)
            {
                if (x < 0 || z < 0 || x >= 20 || z >= 20) return default;
                var c = new PoolCellInfo { valid = true, ceiling = 4f, west = wallWest.Contains(new Vector2Int(x, z)) ? PoolSun.Wall : PoolSun.Open };
                if (sky.Contains(new Vector2Int(x, z)))
                {
                    c.sky = SkylightShape.Full;
                    c.joined = PoolBiome.Joined((a, b) => sky.Contains(new Vector2Int(a, b)) ? SkylightShape.Full : SkylightShape.None, x, z);
                }
                return c;
            }
        }

        [Test]
        public void SunComesThroughTheOpeningOnly()
        {
            var g = new Grid();
            for (int x = 10; x < 12; x++) for (int z = 10; z < 12; z++) g.sky.Add(new Vector2Int(x, z));
            float C = OfficeBiome.CellSize;
            var centre = new Vector3(11 * C, 0f, 11 * C);
            var L = PoolBiome.SunDirection;
            // The patch on the floor: straight below the opening's middle, moved away from the sun.
            var patch = centre - new Vector3(L.x, 0f, L.z) * ((4f + PoolBiome.ShaftHeight * 0.5f) / L.y);
            // (A glazed roof light lets 71 % through, 2026-11-01.)
            Assert.That(PoolSun.Visibility(g, patch + Vector3.up * 0.01f), Is.EqualTo(1f).Or.EqualTo(PoolBiome.GlazedTransmission).Within(1e-4f), "sunlit patch");
            Assert.AreEqual(0f, PoolSun.Visibility(g, centre + Vector3.up * 0.01f), "the floor right under the opening is in shade (the sun is slanted)");
            Assert.AreEqual(0f, PoolSun.Visibility(g, new Vector3(3f, 0.01f, 3f)), "far from the opening");
            // A wall between the patch and the opening blocks it.
            int wx = Mathf.FloorToInt(patch.x / C) + 1;
            for (int z = 0; z < 20; z++) g.wallWest.Add(new Vector2Int(wx, z));
            Assert.AreEqual(0f, PoolSun.Visibility(g, patch + Vector3.up * 0.01f), "walls stop the sun");
        }
    }
}
