using System.Collections.Generic;
using System.IO;
using System.Linq;
using Backrooms.CustomRooms;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>Player-built rooms: blueprints, their JSON, and how they are stamped into the world's plan.</summary>
    public class CustomRoomTests
    {
        /// <summary>8 x 5 room in two halves: a wall across the middle with a doorway, a west door, a south opening, a half wall.</summary>
        static RoomBlueprint Sample()
        {
            var bp = RoomBlueprint.CreateNew("Test room");
            bp.id = "test_sample";
            var v = bp.variants[0];
            v.Resize(8, 5);
            v.WallPerimeter();
            for (int x = 0; x < v.width; x++) v.SetH(x, 0, EdgeCode.Wall);
            v.SetH(6, 0, EdgeCode.Tall);                               // opening south at x = 6
            v.SetV(0, 1, EdgeCode.Header | EdgeCode.Arch);              // arched door west at z = 1
            for (int z = 0; z < v.depth; z++) v.SetV(4, z, EdgeCode.Wall);   // wall across the middle
            v.SetV(4, 2, EdgeCode.Header);                              // doorway through it
            v.SetH(1, 3, EdgeCode.Half);                                // half wall inside the west half
            v.rooms.Add(new RoomSettings { name = "East", ceiling = 3.3f, painted = true });
            for (int z = 0; z < v.depth; z++) for (int x = 4; x < v.width; x++) v.cellRoom[z * v.width + x] = 1;
            v.pillars.Add(new PillarDef { x = 2, z = 2, size = 0.45f });
            v.AutoFixtures();
            v.props.Add(new PropDef { kind = PropKind.Box, x = 2f, z = 2f, size = new Vector3(0.5f, 0.4f, 0.4f) });
            return bp;
        }

        [Test]
        public void BlueprintSurvivesJson()
        {
            var bp = Sample();
            var back = RoomBlueprint.FromJson(bp.ToJson());
            Assert.AreEqual(bp.name, back.name);
            var a = bp.variants[0]; var b = back.variants[0];
            Assert.AreEqual(a.width, b.width); Assert.AreEqual(a.depth, b.depth);
            CollectionAssert.AreEqual(a.vEdges, b.vEdges); CollectionAssert.AreEqual(a.hEdges, b.hEdges); CollectionAssert.AreEqual(a.cellRoom, b.cellRoom);
            Assert.AreEqual(a.fixtures.Count, b.fixtures.Count);
            Assert.AreEqual(a.rooms[1].ceiling, b.rooms[1].ceiling, 1e-4f);
        }

        [Test]
        public void ResizeKeepsWallsAndDoorways()
        {
            var v = RoomVariant.CreateDefault(6, 6);
            Assert.AreEqual(EdgeCode.Header, v.H(3, 0), "default doorway in the south wall");
            v.Resize(8, 6);
            Assert.AreEqual(EdgeCode.Header, v.H(3, 0), "doorway kept when the room grows east");
            for (int z = 0; z < v.depth; z++) { Assert.AreEqual(EdgeCode.Wall, v.V(0, z)); Assert.AreEqual(EdgeCode.Wall, v.V(8, z)); }
            v.Resize(6, 4);
            for (int x = 0; x < v.width; x++) Assert.AreEqual(EdgeCode.Wall, v.H(x, 4), "north wall closed after shrinking");
            Assert.AreEqual(1, v.CountExits());
            v.Validate();
        }

        static bool Reaches(OfficeLayout L, Vector2Int from, System.Func<Vector2Int, bool> goal)
        {
            var seen = new HashSet<Vector2Int> { from };
            var q = new Queue<Vector2Int>(); q.Enqueue(from);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                if (goal(c)) return true;
                for (int side = 0; side < 4; side++)
                {
                    var n = new Vector2Int(c.x + (side == 0 ? -1 : side == 1 ? 1 : 0), c.y + (side == 2 ? -1 : side == 3 ? 1 : 0));
                    if (!L.InChunk(n.x, n.y) || seen.Contains(n) || !OfficeLayout.IsPassable(L.EdgeOf(c.x, c.y, side))) continue;
                    seen.Add(n); q.Enqueue(n);
                }
            }
            return false;
        }

        [Test]
        public void EveryRotationStampsTheSameRoom([Values(0, 1, 2, 3)] int rot)
        {
            var bp = Sample();
            var v = bp.variants[0];
            var L = CustomRoomHarness.BuildLayout(bp, 0, rot, out var p);
            var xf = new CustomRoomStamp.Xf(rot, v.width, v.depth);
            var origin = new Vector2Int(p.rect.x, p.rect.y);

            EdgeKind At(Vector2Int a, Vector2Int b)
            {
                a += origin; b += origin;
                return a.x == b.x ? L.vEdges[a.x, Mathf.Min(a.y, b.y)] : L.hEdges[Mathf.Min(a.x, b.x), a.y];
            }
            // The wall across the middle and the doorway in it.
            for (int z = 0; z < v.depth; z++)
            {
                var k = At(xf.Vertex(4, z), xf.Vertex(4, z + 1));
                Assert.AreEqual(z == 2 ? EdgeKind.DoorHeader : EdgeKind.Wall, k, $"rot {rot}: middle wall at z={z}");
            }
            Assert.AreEqual(EdgeKind.Door, At(xf.Vertex(6, 0), xf.Vertex(7, 0)), $"rot {rot}: the tall south opening");
            var door = xf.Vertex(0, 1) + origin; var door2 = xf.Vertex(0, 2) + origin;
            bool arched = door.x == door2.x ? L.archV[door.x, Mathf.Min(door.y, door2.y)] : L.archH[Mathf.Min(door.x, door2.x), door.y];
            Assert.IsTrue(arched, $"rot {rot}: the west door stays arched");
            Assert.AreEqual(1, L.halfWalls.Count, "one half wall");
            Assert.AreEqual(1, L.pillars.Count);
            Assert.AreEqual(v.fixtures.Count, L.fixtures.Count(f => L.RoomAt(f.cell.x, f.cell.y).custom), "its own lights, not generated ones");
            Assert.AreEqual(1, L.props.Count);
            // Two rooms, the east one taller.
            Assert.AreEqual(2, L.rooms.Where(r => r.custom).Select(r => r.ceiling).Distinct().Count());

            // From the ring corridor you can walk to every cell of the room: both exits lead out.
            var inside = new List<Vector2Int>();
            for (int x = 0; x < v.width; x++) for (int z = 0; z < v.depth; z++) inside.Add(xf.Cell(x, z) + origin);
            var from = new Vector2Int(0, 0);
            foreach (var cell in inside)
                Assert.IsTrue(Reaches(L, from, c => c == cell), $"rot {rot}: cell {cell} cannot be reached from the corridor");
        }

        [Test]
        public void WorldPlacesAndBuildsCustomRoom()
        {
            string dir = Path.Combine(Path.GetTempPath(), "backrooms_customrooms_test_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                RoomLibrary.SetDirectory(dir);
                var bp = Sample();
                bp.enabled = true; bp.chance = 1f; bp.weight = 1000f;
                RoomLibrary.Save(bp);
                Assert.AreEqual(1, RoomLibrary.Rules.Length, "an enabled room is a landmark rule");
                Assert.AreEqual(LandmarkKind.Custom, RoomLibrary.Rules[0].kind);

                var set = WorldGenSettings.Defaults();
                set.landmarks.Clear();
                set.landmarkSpacing = 1;
                var gen = new WorldGenerator(4321, set);
                LandmarkPlacement found = default; bool ok = false;
                for (int x = -4; x <= 4 && !ok; x++)
                for (int z = -4; z <= 4 && !ok; z++)
                    foreach (var l in gen.LandmarksIn(new Vector2Int(x, z)))
                        if (l.kind == LandmarkKind.Custom) { found = l; ok = true; break; }
                Assert.IsTrue(ok, "no custom room placed near the origin");
                Assert.AreEqual(bp.id, found.blueprint);

                var plan = gen.District(new Vector3Int(found.district.x, 0, found.district.y));
                var custom = plan.rooms.Where(r => r.custom).ToList();
                Assert.AreEqual(2, custom.Count(r => r.ceiling > 3f) + custom.Count(r => r.ceiling <= 3f), "both rooms of the blueprint exist");
                Assert.IsTrue(custom.All(r => r.landmark == bp.name && r.noDoors));
                var any = new Vector2Int(found.rect.x, found.rect.y);
                // The room is connected to the rest of the world through its exits.
                Assert.IsTrue(Reaches(plan, any, c => !plan.RoomAt(c.x, c.y).custom), "the room has no way out");
                // Generation is deterministic.
                var fresh = new WorldGenerator(4321, set);
                Assert.AreEqual(LayoutDigest.Of(plan), LayoutDigest.Of(fresh.District(new Vector3Int(found.district.x, 0, found.district.y))));

                // A chunk of it builds (the real chunk builder, on the worker-thread path).
                var chunk = gen.Chunk(new Vector3Int(found.WorldCells.x / OfficeBiome.ChunkCells, 0, found.WorldCells.y / OfficeBiome.ChunkCells));
                Assert.IsNotNull(chunk);
            }
            finally
            {
                RoomLibrary.SetDirectory(null);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    
        [Test]
        public void CrawlHolesComeInFarApartPairsOnRealWalls()
        {
            var gen = new WorldGenerator(1234, WorldGenSettings.Defaults());
            int found = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var plan = gen.District(new Vector3Int(dx, 0, dz));
                Assert.AreEqual(0, plan.portals.Count % 2, "holes come in pairs");
                foreach (var p in plan.portals)
                {
                    found++;
                    Assert.AreEqual(EdgeKind.Wall, plan.EdgeOf(p.cell.x, p.cell.y, p.side), "a hole is cut into a solid wall");
                    float apart = Vector3.Distance(p.position, p.partnerPosition);
                    Assert.That(apart, Is.InRange(CrawlPortals.MinApart - 0.5f, CrawlPortals.MaxApart + 2f), "ends within streaming range but not close");
                    Assert.IsTrue(plan.portals.Exists(q => q.position == p.partnerPosition && q.partnerPosition == p.position), "partner exists");
                }
            }
            Assert.Greater(found, 0, "no crawl holes near the origin");
        }
    }
}
