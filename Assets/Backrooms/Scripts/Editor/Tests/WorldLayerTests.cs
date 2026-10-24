using System.Collections.Generic;
using System.Linq;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>
    /// The upper generation layers: zones, landmarks, districts and storeys.
    /// </summary>
    public class WorldLayerTests
    {
        const int Seed = 1234;

        static WorldGenSettings TwoZones()
        {
            var s = WorldGenSettings.Defaults();
            var test = ZoneRules.Level0();
            test.name = "Test zone"; test.mapColor = Color.cyan; test.weight = 1f;
            test.biomes = new List<BiomeBand> { new BiomeBand { rules = BiomeRules.Warehouse(), upTo = 1f } };
            test.edgeBiome = "Warehouse";
            s.zones.Add(test);
            s.zoneSize = 400f;           // small zones so a test area sees several
            return s;
        }

        [Test]
        public void ZonesAreDeterministicAndDataDriven()
        {
            var set = TwoZones();
            var seen = new HashSet<string>();
            int edges = 0;
            for (int x = -20; x <= 20; x += 2)
            for (int z = -20; z <= 20; z += 2)
            {
                var d = new Vector2Int(x, z);
                var a = set.ZoneAt(Seed, d);
                var b = set.Clone().ZoneAt(Seed, d);
                Assert.AreEqual(a.zone, b.zone, $"zone of district {d} differs between equal settings");
                Assert.AreEqual(a.edge, b.edge);
                seen.Add(set.zones[a.zone].name);
                if (a.edge) edges++;
            }
            Assert.IsTrue(seen.Contains("Level 0") && seen.Contains("Test zone"), "a zone added as data should appear: saw " + string.Join(", ", seen));
            Assert.Greater(edges, 0, "zone edges should produce transition districts");
            Assert.AreEqual(0, set.ZoneAt(Seed, Vector2Int.zero).zone, "spawn is always in the first zone");

            // The new zone's biome table is used in its chunks.
            var gen = new WorldGenerator(Seed, set);
            bool found = false;
            for (int x = -200; x <= 200 && !found; x += 8)
            for (int z = -200; z <= 200 && !found; z += 8)
            {
                var s = gen.Sample(new Vector2Int(x, z));
                if (s.zone == "Test zone" && !s.zoneEdge) { found = true; Assert.AreEqual("Warehouse", s.Dominant); }
            }
            Assert.IsTrue(found, "expected a chunk inside the test zone");
        }

        [Test]
        public void ZonesThatMayNotTouchGetATransitionZone()
        {
            var set = TwoZones();
            var buffer = ZoneRules.Level0(); buffer.name = "Buffer"; buffer.weight = 0f;
            set.zones.Add(buffer);
            set.zones[1].neighbours = new List<string> { "Buffer" };      // the test zone may only touch the buffer
            set.zones[1].transitionZone = "Buffer";
            set.zoneEdge = 120f;
            int buffers = 0, direct = 0;
            for (int x = -30; x <= 30; x++)
            for (int z = -30; z <= 30; z++)
            {
                var hit = set.ZoneAt(Seed, new Vector2Int(x, z));
                if (set.zones[hit.zone].name == "Buffer") buffers++;
                // A Level 0 district right on the edge with the test zone would be a forbidden contact.
                if (hit.edge && hit.edgeDistance < 30f && set.zones[hit.zone].name != "Buffer" && hit.neighbour >= 0 &&
                    set.zones[hit.zone].name != set.zones[hit.neighbour].name &&
                    (set.zones[hit.zone].name == "Test zone" || set.zones[hit.neighbour].name == "Test zone")) direct++;
            }
            Assert.Greater(buffers, 0, "the transition zone should appear along forbidden edges");
            Assert.AreEqual(0, direct, "zones that may not touch met directly");
        }

        static bool FindLandmark(WorldGenerator gen, LandmarkKind kind, out LandmarkPlacement found)
        {
            for (int r = 0; r < 30; r++)
            for (int x = -r; x <= r; x++)
            for (int z = -r; z <= r; z++)
            {
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != r) continue;
                foreach (var l in gen.LandmarksIn(new Vector2Int(x, z)))
                    if (l.kind == kind) { found = l; return true; }
            }
            found = default;
            return false;
        }

        [Test]
        public void LandmarksAreReservedBeforeRooms()
        {
            var gen = new WorldGenerator(Seed, WorldGenSettings.Defaults());
            Assert.IsTrue(FindLandmark(gen, LandmarkKind.ColonnadeHall, out var p), "no colonnade hall found near the origin");
            var plan = gen.District(new Vector3Int(p.district.x, 0, p.district.y));
            var ids = new HashSet<int>();
            for (int x = p.rect.xMin; x < p.rect.xMax; x++)
            for (int z = p.rect.yMin; z < p.rect.yMax; z++)
            {
                var room = plan.RoomAt(x, z);
                Assert.AreEqual(p.name, room.landmark, $"cell {x},{z} of the landmark belongs to an ordinary room");
                ids.Add(room.id);
            }
            Assert.AreEqual(1, ids.Count, "a colonnade hall is one room");
            Assert.IsTrue(plan.rooms[ids.First()].Tall);
            Assert.Greater(plan.pillars.Count(pl => p.rect.Contains(pl.vertex)), 4, "colonnade pillars");
            // Independent of order: a fresh world asked only for this district agrees.
            var fresh = new WorldGenerator(Seed, WorldGenSettings.Defaults());
            Assert.AreEqual(LayoutDigest.Of(plan), LayoutDigest.Of(fresh.District(new Vector3Int(p.district.x, 0, p.district.y))));
        }

        [Test]
        public void MultiStoreyVolumeAgreesBetweenStoreys()
        {
            var gen = new WorldGenerator(Seed, WorldGenSettings.Defaults());
            Assert.IsTrue(FindLandmark(gen, LandmarkKind.Atrium, out var p), "no atrium found near the origin");
            Assert.AreEqual(2, p.storeys);
            var ground = gen.District(new Vector3Int(p.district.x, 0, p.district.y));
            var upper = gen.District(new Vector3Int(p.district.x, 1, p.district.y));
            int well = 0;
            for (int x = p.rect.xMin; x < p.rect.xMax; x++)
            for (int z = p.rect.yMin; z < p.rect.yMax; z++)
            {
                bool openAbove = ground.Has(x, z, CellFlags.OpenAbove), noFloor = upper.Has(x, z, CellFlags.NoFloor);
                Assert.AreEqual(openAbove, noFloor, $"cell {x},{z}: the ground storey's well and the upper storey's void disagree");
                if (openAbove) well++;
                Assert.AreEqual(p.name, upper.RoomAt(x, z).landmark, "the upper storey reserves the same footprint");
            }
            Assert.Greater(well, 0);
            // The well's lights hang at the upper storey's ceiling height and belong to the ground storey.
            float pitch = gen.Settings.storeyPitch;
            Assert.IsTrue(ground.fixtures.Any(f => p.rect.Contains(f.cell) && ground.Has(f.cell.x, f.cell.y, CellFlags.OpenAbove) && f.height > pitch));
            Assert.IsFalse(upper.fixtures.Any(f => upper.Has(f.cell.x, f.cell.y, CellFlags.NoFloor)));
            // Railings around the void on the upper storey; the column streams the upper storey.
            Assert.AreEqual(4, upper.halfWalls.Count(h => h.railing));
            var chunk = new Vector2Int(Mathf.FloorToInt(p.WorldCells.center.x / OfficeBiome.ChunkCells), Mathf.FloorToInt(p.WorldCells.center.y / OfficeBiome.ChunkCells));
            Assert.GreaterOrEqual(gen.TopStoreyAt(chunk), 1, "the volume's column must stream the upper storey");
        }

        [Test]
        public void ChunksAreSlicesOfTheirDistrict()
        {
            var gen = new WorldGenerator(Seed, WorldGenSettings.Defaults());
            var plan = gen.District(new Vector3Int(0, 0, 0));
            int n = OfficeBiome.ChunkCells;
            for (int cx = 0; cx < WorldGenSettings.DistrictChunks; cx++)
            for (int cz = 0; cz < WorldGenSettings.DistrictChunks; cz++)
            {
                var c = gen.Chunk(new Vector2Int(cx, cz));
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    Assert.AreEqual(plan.roomOf[cx * n + x, cz * n + z], c.RoomAt(x, z).globalId);
                    for (int side = 0; side < 4; side++) Assert.AreEqual(plan.EdgeOf(cx * n + x, cz * n + z, side), c.EdgeOf(x, z, side));
                }
            }
            // Every fixture lands in exactly one chunk.
            int sliced = 0;
            for (int cx = 0; cx < WorldGenSettings.DistrictChunks; cx++)
            for (int cz = 0; cz < WorldGenSettings.DistrictChunks; cz++)
                sliced += gen.Chunk(new Vector2Int(cx, cz)).fixtures.Count;
            Assert.AreEqual(plan.fixtures.Count, sliced);
        }
    }
}
