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
        public void EntranceLeadsIntoThePoolrooms()
        {
            var s = Set();
            var z = Pool(s);
            Assert.IsTrue(FarLinks.PartnerOf(s, z.entranceFrom, out var to) && to == z.entranceTo);
            Assert.IsTrue(FarLinks.PartnerOf(s, z.entranceTo, out var back) && back == z.entranceFrom);
            foreach (int seed in new[] { 1234, 7, 2032979943 })
            {
                var gen = new WorldGenerator(seed, s);
                CrawlPortal FarHole(Vector2Int district)
                {
                    for (int x = 0; x < WorldGenSettings.DistrictChunks; x++)
                    for (int y = 0; y < WorldGenSettings.DistrictChunks; y++)
                        foreach (var p in gen.Chunk(new Vector2Int(district.x * WorldGenSettings.DistrictChunks + x, district.y * WorldGenSettings.DistrictChunks + y)).portals)
                            if (p.far) return p;
                    Assert.Fail($"seed {seed}: no far hole in district {district}");
                    return default;
                }
                var a = FarHole(z.entranceFrom);
                var b = FarHole(z.entranceTo);
                Assert.AreEqual(b.position, a.partnerPosition, "the spawn district's hole leads to the Poolrooms' hole");
                Assert.AreEqual(a.position, b.partnerPosition);
                var plan = gen.District(new Vector3Int(z.entranceTo.x, 0, z.entranceTo.y));
                var hole = plan.portals.Find(p => p.far);
                Assert.AreEqual(b.position, hole.position);
                Assert.IsFalse(plan.pool[hole.cell.x, hole.cell.y].basin, "you come out on a dry deck, not into water");
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
            int basins = 0, sky = 0;
            for (int x = 0; x < L.N; x++) for (int y = 0; y < L.N; y++) { if (L.pool[x, y].basin) basins++; if (L.pool[x, y].skylight != SkylightShape.None) sky++; }
            Assert.Greater(basins, L.N * L.N / 2, "mostly flooded");
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
            Assert.AreEqual(1f, PoolSun.Visibility(g, patch + Vector3.up * 0.01f), "sunlit patch");
            Assert.AreEqual(0f, PoolSun.Visibility(g, centre + Vector3.up * 0.01f), "the floor right under the opening is in shade (the sun is slanted)");
            Assert.AreEqual(0f, PoolSun.Visibility(g, new Vector3(3f, 0.01f, 3f)), "far from the opening");
            // A wall between the patch and the opening blocks it.
            int wx = Mathf.FloorToInt(patch.x / C) + 1;
            for (int z = 0; z < 20; z++) g.wallWest.Add(new Vector2Int(wx, z));
            Assert.AreEqual(0f, PoolSun.Visibility(g, patch + Vector3.up * 0.01f), "walls stop the sun");
        }
    }
}
