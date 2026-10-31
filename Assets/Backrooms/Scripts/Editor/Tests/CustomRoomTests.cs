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

        [Test]
        public void WallPropsSnapToAllFourSidesButNotOpenings()
        {
            var v = RoomVariant.CreateDefault();
            var size = new Vector3(0.36f, 2.1f, 0.06f);
            float edge = v.width * OfficeBiome.CellSize;
            var points = new[] { new Vector2(0.2f, 3f), new Vector2(edge - 0.2f, 3f), new Vector2(3f, 0.2f), new Vector2(3f, edge - 0.2f) };
            float[] angles = { 90f, -90f, 0f, 180f };
            for (int i = 0; i < points.Length; i++)
            {
                Assert.IsTrue(v.TryWallPlacement(points[i], size, out var p, out float yaw));
                Assert.AreEqual(angles[i], yaw);
                Assert.That(p.x, Is.InRange(0.05f, edge - 0.05f));
                Assert.That(p.y, Is.InRange(0.05f, edge - 0.05f));
            }
            Assert.IsFalse(v.TryWallPlacement(new Vector2(4.2f, 0.1f), size, out _, out _), "default doorway cannot support a clock");
            Assert.IsFalse(v.TryWallPlacement(new Vector2(3f, 3f), size, out _, out _), "no distant wall snapping");
            v.SetV(0, 1, EdgeCode.Header); v.SetV(0, 3, EdgeCode.Header);
            Assert.IsFalse(v.TryWallPlacement(new Vector2(0.2f, 3f), new Vector3(2.3f, 2.4f, 0.95f), out _, out _), "rack cannot fit between nearby doorways");
        }

        [Test]
        public void NewPropsBuildOnWorkerAndRackCollisionRotates()
        {
            var assets = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            var bp = RoomBlueprint.CreateNew();
            var v = bp.variants[0];
            v.props.Add(new PropDef { kind = PropKind.StorageRack, x = 1f, z = 3f, yaw = 90f, size = new Vector3(2.3f, 2.4f, 0.95f) });
            v.props.Add(new PropDef { kind = PropKind.WallClock, x = 3f, z = 7f, yaw = 180f, size = new Vector3(0.36f, 2.1f, 0.06f) });
            v.props.Add(new PropDef { kind = PropKind.Armchair, x = 4f, z = 4f, size = new Vector3(0.9f, 0.85f, 0.9f) });
            v.props.Add(new PropDef { kind = PropKind.OfficeChair, x = 5f, z = 4f });
            var layout = CustomRoomHarness.BuildLayout(bp, 0, 0, out _);
            var builder = new OfficeChunkBuilder(assets);
            System.Exception error = null;
            var worker = new System.Threading.Thread(() => { try { builder.BuildData(layout); } catch (System.Exception e) { error = e; } });
            worker.Start(); worker.Join();
            Assert.IsNull(error, error?.ToString());
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var boxes = (List<(Vector3 c, Vector3 s, float yaw)>)typeof(OfficeChunkBuilder).GetField("_rotBoxes", flags).GetValue(builder);
            // Its shelves (a deck slab per level, as long as the rack) and its uprights are solid, turned with it.
            Assert.IsTrue(boxes.Any(b => Mathf.Abs(b.s.x - 2.28f) < 0.01f && Mathf.Abs(b.yaw - 90f) < 0.01f), "rack shelf colliders follow its rotated geometry");
            var instances = (List<OfficeChunkBuilder.PropInstance>)typeof(OfficeChunkBuilder).GetField("_instances", flags).GetValue(builder);
            Assert.IsTrue(instances.Any(p => p.prefab == assets.wallClockPrefab && Mathf.Abs(p.position.y - 2.1f) < 0.01f), "clock mounts at its saved height");
            var movables = ((System.Collections.IList)typeof(OfficeChunkBuilder).GetField("_movables", flags).GetValue(builder)).Cast<Backrooms.Interaction.MovableSpec>().ToList();
            Assert.AreEqual(2, movables.Count(m => m.kind != Backrooms.Interaction.MovableKind.Box), "explicit armchair and office chair are movable");
            // The rack's boxes are movable too, turned with it.
            foreach (var m in movables.Where(m => m.kind == Backrooms.Interaction.MovableKind.Box))
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(m.yaw, 90f)), 10f, "a rack's box turns with the rack");
        }

        [Test]
        public void PalletsAndTheirBoxesAreMovableWithTheirOwnIds()
        {
            var assets = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            var bp = RoomBlueprint.CreateNew();
            var v = bp.variants[0];
            // Several, so some come out loaded (60 %).
            for (int i = 0; i < 6; i++) v.props.Add(new PropDef { kind = PropKind.Pallet, x = 1.5f + (i % 3) * 2.5f, z = 2f + (i / 3) * 3f, size = new Vector3(1.2f, 1.6f, 1.0f) });
            v.props.Add(new PropDef { kind = PropKind.StorageRack, x = 2f, z = 8f, size = new Vector3(2.3f, 2.4f, 0.95f) });
            var layout = CustomRoomHarness.BuildLayout(bp, 0, 0, out _);
            var builder = new OfficeChunkBuilder(assets);
            builder.BuildData(layout);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var movables = ((System.Collections.IList)typeof(OfficeChunkBuilder).GetField("_movables", flags).GetValue(builder)).Cast<Backrooms.Interaction.MovableSpec>().ToList();
            Assert.AreEqual(6, movables.Count(m => m.kind == Backrooms.Interaction.MovableKind.Pallet), "each pallet is a movable pallet");
            Assert.IsTrue(movables.Any(m => m.kind == Backrooms.Interaction.MovableKind.Box), "pallets and the rack carry movable boxes");
            Assert.AreEqual(movables.Count, movables.Select(m => m.id).Distinct().Count(), "every pallet and box has its own id (so each is saved on its own)");
            foreach (var m in movables.Where(m => m.kind == Backrooms.Interaction.MovableKind.Box))
                Assert.AreNotEqual(0, m.variant & 7, "warehouse boxes are never the heavy paper-filled kind");
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
        public void OpeningsCutTheirWallsAtEveryTurn([Values(0, 1, 2, 3)] int rot)
        {
            var bp = RoomBlueprint.CreateNew();
            var v = bp.variants[0];
            v.Resize(8, 6);
            v.ConvertGridOpenings();
            // A 2 m doorway in the south wall over two posts, a window across a post in the east wall.
            v.openings.Add(new OpeningDef { vertical = false, line = 0, from = 0.6f, to = 2.6f, top = 2.4f });
            v.openings.Add(new OpeningDef { vertical = true, line = 8, from = 1.8f, to = 4.2f, sill = 0.9f, top = 2.0f });
            bp.Validate();
            Assert.AreEqual(2, v.CountExits(), "the converted door and the wide doorway lead out; a window does not");
            var L = CustomRoomHarness.BuildLayout(bp, 0, rot, out var p);
            Assert.AreEqual(3, L.cutouts.Count, "every opening is a cutout in the plan");
            var wide = L.cutouts.First(c => Mathf.Abs(c.to - c.from - 2f) < 0.01f);
            var window = L.cutouts.First(c => c.sill > 0.5f);
            Assert.AreEqual(2.4f, wide.top, 1e-4f);
            // The edges under the doorway open; the window's stay wall.
            int Open(WallCutout c)
            {
                int n = 0;
                for (int a = Mathf.FloorToInt(c.from / OfficeBiome.CellSize); a < Mathf.CeilToInt(c.to / OfficeBiome.CellSize); a++)
                    if ((c.vertical ? L.vEdges[c.line, a] : L.hEdges[a, c.line]) == EdgeKind.Door) n++;
                return n;
            }
            Assert.AreEqual(3, Open(wide), $"rot {rot}: the doorway's three edges open");
            Assert.AreEqual(0, Open(window), $"rot {rot}: the window's edges stay wall");
            // It builds, on a worker thread as the world does.
            var assets = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            var builder = new OfficeChunkBuilder(assets);
            System.Exception error = null;
            var worker = new System.Threading.Thread(() => { try { builder.BuildData(L); } catch (System.Exception e) { error = e; } });
            worker.Start(); worker.Join();
            Assert.IsNull(error, error?.ToString());
            // There is wall over the doorway (a collider from its top up) and none across it below.
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var boxes = (List<(Vector3 c, Vector3 s)>)typeof(OfficeChunkBuilder).GetField("_boxes", flags).GetValue(builder);
            float mid = (wide.from + wide.to) * 0.5f, line = wide.line * OfficeBiome.CellSize;
            bool Covers((Vector3 c, Vector3 s) b, float y) =>
                Mathf.Abs((wide.vertical ? b.c.x : b.c.z) - line) < 0.1f && Mathf.Abs((wide.vertical ? b.c.z : b.c.x) - mid) < (wide.vertical ? b.s.z : b.s.x) * 0.5f
                && Mathf.Abs(b.c.y - y) < b.s.y * 0.5f;
            Assert.IsTrue(boxes.Any(b => Covers(b, 2.55f)), $"rot {rot}: wall above the doorway");
            Assert.IsFalse(boxes.Any(b => Covers(b, 1.0f)), $"rot {rot}: nothing across the doorway");
        }

        [Test]
        public void GridDoorwaysBecomeOpeningsAndGrowingWestKeepsThingsInPlace()
        {
            var v = RoomVariant.CreateDefault(6, 6);
            Assert.AreEqual(1, v.ConvertGridOpenings(), "the default doorway");
            var door = v.openings[0];
            Assert.IsFalse(door.vertical); Assert.AreEqual(0, door.line);
            Assert.AreEqual(EdgeCode.Wall, v.H(3, 0), "its edge is wall now; the opening cuts it");
            v.openings.Add(new OpeningDef { vertical = true, line = 0, from = 1f, to = 2f, sill = 1f, top = 2f });
            v.props.Add(new PropDef { kind = PropKind.Box, x = 2f, z = 2f });
            float doorFrom = door.from;
            v.Reframe(-2, 0, 8, 6);
            Assert.AreEqual(8, v.width);
            Assert.AreEqual(2f + 2 * OfficeBiome.CellSize, v.props[0].x, 1e-4f, "the prop stays where it was in the room");
            Assert.AreEqual(doorFrom + 2 * OfficeBiome.CellSize, v.openings.First(o => !o.vertical).from, 1e-4f, "the south door too");
            Assert.AreEqual(0, v.openings.First(o => o.vertical).line, "the west window goes out with the west wall");
            Assert.AreEqual(EdgeCode.Open, v.V(2, 3), "the old west wall line is inside now, open");
        }

        [Test]
        public void CatalogStylePlacesThatModel()
        {
            var assets = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            Assert.IsNotNull(assets.floralSofaPrefab, "the floral sofa is in the biome's assets");
            var bp = RoomBlueprint.CreateNew();
            bp.variants[0].props.Add(new PropDef { kind = PropKind.Sofa, style = Prop.FloralSofaStyle, x = 3f, z = 3f, size = new Vector3(2.25f, 0.94f, 0.93f) });
            bp.variants[0].props.Add(new PropDef { kind = PropKind.Armchair, style = 2, x = 5f, z = 5f, size = new Vector3(0.95f, 0.85f, 0.9f) });
            var L = CustomRoomHarness.BuildLayout(bp, 0, 0, out _);
            var builder = new OfficeChunkBuilder(assets);
            builder.BuildData(L);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var specs = ((System.Collections.IList)typeof(OfficeChunkBuilder).GetField("_movables", flags).GetValue(builder)).Cast<Backrooms.Interaction.MovableSpec>().ToList();
            Assert.IsTrue(specs.Any(m => m.kind == Backrooms.Interaction.MovableKind.Sofa && m.parts[0].prefab == assets.floralSofaPrefab), "the floral sofa, not a random one");
            Assert.IsTrue(specs.Any(m => m.kind == Backrooms.Interaction.MovableKind.Armchair && m.parts[0].prefab == assets.armchairPrefabs[1]), "the second armchair fabric");
        }

        [Test]
        public void StuckPropsKeepTheirPose()
        {
            var assets = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            var bp = RoomBlueprint.CreateNew();
            bp.variants[0].props.Add(new PropDef { kind = PropKind.Box, x = 2f, z = 2f, yaw = 30f, pitch = 20f, roll = -10f, elevation = 0.5f, scale = 1.5f, pinned = true, size = new Vector3(0.5f, 0.36f, 0.4f) });
            var L = CustomRoomHarness.BuildLayout(bp, 0, 0, out _);
            var builder = new OfficeChunkBuilder(assets);
            builder.BuildData(L);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var spec = ((System.Collections.IList)typeof(OfficeChunkBuilder).GetField("_movables", flags).GetValue(builder)).Cast<Backrooms.Interaction.MovableSpec>().Single();
            Assert.IsTrue(spec.pinned);
            Assert.AreEqual(20f, spec.pitch, 1e-4f); Assert.AreEqual(-10f, spec.roll, 1e-4f); Assert.AreEqual(1.5f, spec.scale, 1e-4f);
            Assert.AreEqual(0.5f, spec.position.y, 1e-4f, "lifted off the floor");
            // (Spawning outside play mode logs Unity's "Destroy may not be called from edit mode" for its temporary cube.)
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var prop = Backrooms.Interaction.MovableProp.Spawn(spec, null, spec.position, Quaternion.Euler(spec.pitch, spec.yaw, spec.roll));
            try
            {
                prop.Wake();
                Assert.IsTrue(prop.Body.isKinematic, "physics off: it never wakes");
                Assert.AreEqual(1.5f, prop.transform.localScale.x, 1e-4f);
            }
            finally { Object.DestroyImmediate(prop.gameObject); }
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
        public void CustomArrivalAvoidsFurnitureAndRejectsBlockedRooms()
        {
            var bp = Sample();
            var plan = CustomRoomHarness.BuildLayout(bp, 0, 0, out var placement);
            Assert.IsTrue(CustomRoomStamp.TryArrival(plan, placement.rect, out var first));
            plan.props.Add(new Prop { kind = PropKind.Box, position = first, size = Vector3.one });
            Assert.IsTrue(CustomRoomStamp.TryArrival(plan, placement.rect, out var next));
            Assert.Greater(Vector2.Distance(first, next), 0.6f);
            plan.props.Add(new Prop { kind = PropKind.StorageRack, position = placement.rect.center * OfficeBiome.CellSize, size = new Vector3(100f, 3f, 100f) });
            Assert.IsFalse(CustomRoomStamp.TryArrival(plan, placement.rect, out _), "do not teleport inside furniture when every cell is blocked");
        }

        [Test]
        public void AreaCopyPastesShapeEdgesSettingsAndDressingAsDetachedData()
        {
            var source = Sample().variants[0];
            source.cellRoom[4] = 0; // leave a hole in the copied east area's footprint
            source.rooms[1].floorFinish = FloorFinish.DarkWood;
            source.props.Add(new PropDef { kind = PropKind.WallClock, x = 6f, z = 4f, yaw = 90f, size = new Vector3(0.36f, 2.1f, 0.06f) });
            source.loot.Add(new LootDef { x = 7f, z = 4f, item = "Hammer", note = "copied", chance = 0.7f });
            source.pillars.Add(new PillarDef { x = 6, z = 3 });
            var copy = AreaClipboard.Capture(source, 1);
            Assert.IsNotNull(copy);
            source.rooms[1].floorFinish = FloorFinish.Laminate;
            var target = RoomVariant.CreateDefault(12, 12);
            var anchor = new Vector2Int(2, 3);
            target.props.Add(new PropDef { x = 4f, z = 6f }); // replaced inside footprint
            target.props.Add(new PropDef { x = 12f, z = 12f }); // outside remains
            Assert.IsTrue(copy.Paste(target, anchor, out int area));
            Assert.AreEqual(0, target.RoomOf(anchor.x, anchor.y), "hole in source shape does not overwrite target");
            Assert.AreEqual(area, target.RoomOf(anchor.x + 1, anchor.y));
            Assert.AreEqual(FloorFinish.DarkWood, target.rooms[area].floorFinish, "clipboard is a snapshot");
            Assert.AreEqual(EdgeCode.Header, target.V(2, 5), "middle doorway moves with area");
            Assert.AreEqual(2, target.props.Count, "replace destination prop and preserve outside prop");
            Assert.IsTrue(target.props.Exists(p => p.kind == PropKind.WallClock && p.yaw == 90f && p.size.y == 2.1f));
            Assert.AreEqual("Hammer", target.loot[0].item);
            Assert.AreEqual("copied", target.loot[0].note);
            Assert.IsTrue(target.pillars.Exists(p => p.x == 4 && p.z == 6));
            int fixtures = target.fixtures.Count;
            Assert.IsTrue(copy.Paste(target, anchor, out _));
            Assert.AreEqual(fixtures, target.fixtures.Count, "repeated paste does not stack fixtures");
            Assert.AreEqual(2, target.props.Count, "repeated paste does not stack props");
            string before = JsonUtility.ToJson(target);
            Assert.IsFalse(copy.Paste(target, new Vector2Int(11, 11), out _));
            Assert.AreEqual(before, JsonUtility.ToJson(target), "rejected paste makes no edits");
            Assert.IsNull(AreaClipboard.Capture(source, 9), "empty/nonexistent area is not copied");
        }

        [Test]
        public void UndoAfterSaveMarksTheRestoredBlueprintDirty()
        {
            var previous = BuildSession.Editing;
            bool previousDirty = BuildSession.Dirty;
            var root = new GameObject("Inactive builder undo test");
            root.SetActive(false); // exercise undo without starting the renderer or loading the library
            try
            {
                var app = root.AddComponent<BuilderApp>();
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var type = typeof(BuilderApp);
                var bp = Sample();
                type.GetField("_bp", flags).SetValue(app, bp);
                type.GetMethod("PushUndo", flags).Invoke(app, null);
                bp.variants[0].rooms.Add(new RoomSettings { name = "Pasted" });
                type.GetField("_dirty", flags).SetValue(app, false); BuildSession.Dirty = false;
                type.GetMethod("Undo", flags).Invoke(app, null);
                Assert.IsTrue((bool)type.GetField("_dirty", flags).GetValue(app));
                Assert.IsTrue(BuildSession.Dirty);
                Assert.AreEqual(2, BuildSession.Editing.variants[0].rooms.Count);
                type.GetMethod("Redo", flags).Invoke(app, null);
                Assert.AreEqual(3, BuildSession.Editing.variants[0].rooms.Count);
            }
            finally { Object.DestroyImmediate(root); BuildSession.Editing = previous; BuildSession.Dirty = previousDirty; }
        }

        [Test]
        public void AreaFinishesSurviveJsonRotationsAndChunkSlices([Values(0, 1, 2, 3)] int rot)
        {
            var bp = Sample();
            bp.variants[0].rooms[1].wallFinish = WallFinish.ReferenceOlive;
            bp.variants[0].rooms[1].floorFinish = FloorFinish.DarkWood;
            bp = RoomBlueprint.FromJson(bp.ToJson());
            var plan = CustomRoomHarness.BuildLayout(bp, 0, rot, out _);
            var area = plan.rooms.First(r => r.custom && r.ceiling > 3f);
            Assert.AreEqual(WallFinish.ReferenceOlive, area.wallFinish);
            Assert.AreEqual(FloorFinish.DarkWood, area.floorFinish);
            var slice = WorldGenerator.Slice(plan, new Vector3Int(0, 0, 0));
            Assert.IsTrue(slice.rooms.Any(r => r.custom && r.wallFinish == WallFinish.ReferenceOlive && r.floorFinish == FloorFinish.DarkWood));
            var assets = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            Assert.AreSame(assets.wood, assets.FloorMaterial(area));
            if (assets.referenceOlive) Assert.AreSame(assets.referenceOlive, assets.WallMaterial(area));
            var legacy = new Room { painted = false };
            Assert.AreSame(assets.wallpaper, assets.WallMaterial(legacy));
            legacy.painted = true;
            Assert.AreSame(assets.paintedWall, assets.WallMaterial(legacy));
            Assert.AreSame(assets.carpet, assets.FloorMaterial(legacy));
            System.Exception error = null;
            var worker = new System.Threading.Thread(() => { try { new OfficeChunkBuilder(assets).BuildData(plan); } catch (System.Exception e) { error = e; } });
            worker.Start(); worker.Join();
            Assert.IsNull(error, error?.ToString());
        }
    
        [Test]
        public void CrawlSpacesLeadToSealedRoomsAboveOrdinaryRooms()
        {
            var set = WorldGenSettings.Defaults();
            var gen = new WorldGenerator(1234, set);
            int found = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var plan = gen.District(new Vector3Int(dx, 0, dz));
                var taken = new System.Collections.Generic.HashSet<Vector2Int>();
                foreach (var p in plan.portals)
                {
                    Assert.IsTrue(p.space, "every vent is a crawl space now (no portal pairs, no far links)");
                    var behind = p.cell + (p.side == 0 ? Vector2Int.left : p.side == 1 ? Vector2Int.right : p.side == 2 ? Vector2Int.down : Vector2Int.up);
                    Assert.IsFalse(plan.plates.Exists(w => w.height < CrawlPortals.HoleSize &&
                        ((w.cell == p.cell && w.side == p.side) || (w.cell == behind && w.side == (p.side ^ 1)))), "no outlet floating in a hole");
                    Assert.AreEqual(EdgeKind.Wall, plan.EdgeOf(p.cell.x, p.cell.y, p.side), "a hole is cut into a solid wall");
                    Assert.IsTrue(p.room.Contains(p.cell), "the room lies over the vent's own cell");
                    float maxCeil = 0f;
                    foreach (var c in p.room.allPositionsWithin)
                    {
                        Assert.IsTrue(plan.InChunk(c.x, c.y));
                        Assert.AreEqual(p.cell.x / OfficeBiome.ChunkCells, c.x / OfficeBiome.ChunkCells, "built by the vent's own chunk");
                        Assert.AreEqual(p.cell.y / OfficeBiome.ChunkCells, c.y / OfficeBiome.ChunkCells);
                        var r = plan.RoomAt(c.x, c.y);
                        Assert.IsFalse(r.openAbove || r.noFloor || r.landmark != null, "only over ordinary rooms: nothing open up into it");
                        maxCeil = Mathf.Max(maxCeil, r.ceiling);
                        Assert.IsTrue(taken.Add(c), "special rooms never overlap");
                    }
                    // The room's duct runs back over the cells behind the hole: their ceilings stay under it too.
                    for (int back = 1; back <= 3; back++)
                    {
                        var d = p.cell + (behind - p.cell) * back;
                        if (!plan.InChunk(d.x, d.y)) continue;
                        var r = plan.RoomAt(d.x, d.y);
                        Assert.IsFalse(r.openAbove, "nothing open up into the duct");
                        maxCeil = Mathf.Max(maxCeil, r.ceiling);
                    }
                    Assert.AreEqual(maxCeil + CrawlPortals.PlenumGap, p.roomFloor, 1e-4f, "the floor sits just above the highest ceiling under it and under its duct");
                    Assert.Less(p.roomFloor + CrawlPortals.RoomHeight, set.storeyPitch - 0.5f, "and its top under the storey above");
                    var flat = new Vector2(p.partnerPosition.x - p.position.x, p.partnerPosition.z - p.position.z);
                    Assert.Less(flat.magnitude, 0.05f, "the room's vent is right above the hole");
                    Assert.AreEqual(p.position.y + p.roomFloor, p.partnerPosition.y, 1e-4f);
                    Assert.AreEqual(p.normal, p.partnerNormal);
                    found++;
                }
            }
            Assert.Greater(found, 3, "crawl spaces near the origin");
        }

        [Test]
        public void NoclipDoorsAreRareAndLeadIntoThePoolrooms()
        {
            var set = WorldGenSettings.Defaults();
            var pool = set.zones[NoclipDoors.TargetZone(set)];
            foreach (int seed in new[] { 1234, 7, 2032979943 })
            {
                var gen = new WorldGenerator(seed, set);
                int links = 0, doors = 0;
                for (int x = -8; x < 8; x++)
                for (int z = -8; z < 8; z++)
                {
                    var d = new Vector2Int(x, z);
                    if (!NoclipDoors.LinkHere(set, seed, d, out var partner)) continue;
                    Assert.IsTrue(NoclipDoors.LinkHere(set, seed, d, out var again) && again == partner, "deterministic");
                    Assert.IsTrue(pool.PinnedContains(partner), "it leads into the Poolrooms");
                    links++;
                    if (links > 3) continue;                       // building a few is enough
                    var plan = gen.District(new Vector3Int(x, 0, z));
                    foreach (var door in plan.noclipDoors)
                    {
                        Assert.IsFalse(door.arrival);
                        Assert.AreEqual(EdgeKind.Wall, plan.EdgeOf(door.cell.x, door.cell.y, door.side), "a door-sized part of a solid wall");
                        var chunk = gen.Chunk(new Vector3Int(Mathf.FloorToInt(door.position.x / OfficeBiome.ChunkSize), 0, Mathf.FloorToInt(door.position.z / OfficeBiome.ChunkSize)));
                        var cut = chunk.noclipDoors.Find(n => n.id == door.id);
                        Assert.IsTrue(cut.resolved, "the chunk knows where it leads");
                        var arrival = gen.District(new Vector3Int(partner.x, 0, partner.y)).noclipDoors.Find(n => n.arrival);
                        Assert.AreEqual(arrival.position, cut.partnerPosition, "to its Poolrooms district's taped door");
                        doors++;
                    }
                }
                // One per square of spacing x spacing districts: 16 in these 16 x 16, a few fewer where a square's pick falls on the Poolrooms.
                Assert.That(links, Is.InRange(8, 16), $"seed {seed}: rare");
                Assert.Greater(doors, 0, $"seed {seed}: doors placed");
            }
        }

        [Test]
        public void DuctWindowMapsOntoThePartnerOpening()
        {
            var root = new GameObject("Portal mapping test"); root.SetActive(false);
            var a = root.AddComponent<Backrooms.Interaction.CrawlPortalView>();
            var b = root.AddComponent<Backrooms.Interaction.CrawlPortalView>();
            try
            {
                for (int side = 0; side < 4; side++)
                for (int other = 0; other < 4; other++)
                {
                    var district = new Vector2Int(-2, 1); var cell = new Vector2Int(3, 4);
                    CrawlPortals.Frame(district, cell, side, 0, out a.position, out a.normal);
                    CrawlPortals.Frame(district, new Vector2Int(14, 17), other, 0, out b.position, out b.normal);
                    var centre = new Vector3((district.x * WorldGenSettings.DistrictCells + cell.x + 0.5f) * OfficeBiome.CellSize,
                        CrawlPortals.HoleHalf, (district.y * WorldGenSettings.DistrictCells + cell.y + 0.5f) * OfficeBiome.CellSize);
                    Assert.Less(Vector3.Distance(a.position, centre - a.normal * (OfficeBiome.CellSize - OfficeBiome.WallThickness) * 0.5f), 0.0001f, "opening on the wall face");
                    Assert.Less(Vector3.Distance(a.ExitPosition, a.position - a.normal * CrawlPortals.DuctLength), 0.0001f, "window at the duct's far end");
                    a.partnerPosition = b.position; a.partnerNormal = b.normal;
                    var m = a.ToPartner;
                    Assert.Less(Vector3.Distance(m.MultiplyPoint3x4(a.ExitPosition), b.position), 0.0001f, "the window opens onto the partner's opening");
                    Assert.Less(Vector3.Distance(m.MultiplyVector(-a.normal), b.normal), 0.0001f, "crawling on comes out of the partner, facing out");
                    // Just past the window = just outside the partner, at the same height; just before it = still behind the partner's wall.
                    var past = m.MultiplyPoint3x4(a.ExitPosition - a.normal * 0.1f + Vector3.up * 0.13f);
                    Assert.AreEqual(0.1f, b.Local(past).z, 0.0001f);
                    Assert.AreEqual(0.13f, b.Local(past).y, 0.0001f);
                    Assert.AreEqual(-0.1f, b.Local(m.MultiplyPoint3x4(a.ExitPosition + a.normal * 0.1f)).z, 0.0001f);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void CroppedPortalProjectionPreservesFullScreenPerspective()
        {
            var perspective = Matrix4x4.Perspective(75, 16f / 9, 0.05f, 45);
            foreach (var rect in new[] { new Rect(0, 0, 1, 1), new Rect(0.1f, 0.3f, 0.45f, 0.6f), new Rect(0.8f, 0, 0.2f, 0.15f) })
            {
                var crop = Backrooms.Interaction.CrawlPortalSystem.CropProjection(perspective, rect);
                for (int x = -4; x <= 4; x++) for (int y = -4; y <= 4; y++)
                {
                    var point = new Vector4(x * 0.2f, y * 0.2f, -2, 1);
                    var full = perspective * point; var clipped = crop * point;
                    var originalUV = new Vector2(full.x / full.w, full.y / full.w) * 0.5f + Vector2.one * 0.5f;
                    var cropUV = new Vector2(clipped.x / clipped.w, clipped.y / clipped.w) * 0.5f + Vector2.one * 0.5f;
                    var reconstructed = Vector2.Scale(cropUV, rect.size) + rect.position;
                    Assert.Less(Vector2.Distance(originalUV, reconstructed), 0.00001f);
                    Assert.AreEqual(full.z / full.w, clipped.z / clipped.w, 0.00001f, "crop must preserve perspective depth for clustered lighting");
                }
            }
        }

        [Test]
        public void PortalSealIsWallContainedAndCollisionIgnoresAreOwned()
        {
            var root = new GameObject("Portal seal test");
            var person = new GameObject("Test capsule");
            var capsule = person.AddComponent<CharacterController>();
            capsule.radius = 0.3f; capsule.height = 0.62f; capsule.center = Vector3.up * 0.31f;
            var v = Backrooms.Interaction.CrawlPortalView.Create(root.transform,
                new CrawlPortal { position = new Vector3(0, 0.4f, 0), normal = Vector3.back });
            try
            {
                person.transform.position = Vector3.back * 0.2f;
                Physics.SyncTransforms();
                Assert.IsTrue(v.Fits(capsule));
                Assert.AreEqual(OfficeBiome.WallThickness, v.backstop.size.z, 0.0001f);
                Assert.AreEqual(0, v.backstop.center.z - v.backstop.size.z * 0.5f, 0.0001f);
                Assert.AreEqual(OfficeBiome.WallThickness, v.backstop.center.z + v.backstop.size.z * 0.5f, 0.0001f);
                Assert.IsTrue(v.backstop.Raycast(new Ray(new Vector3(0, 0.4f, 1), Vector3.back), out _, 2));
                Assert.IsFalse(Physics.GetIgnoreCollision(capsule, v.backstop));
                v.SetPassage(capsule, true);
                Assert.IsTrue(Physics.GetIgnoreCollision(capsule, v.backstop));
                v.enabled = false;
                // Ordinary runtime MonoBehaviours do not receive lifecycle callbacks in EditMode.
                typeof(Backrooms.Interaction.CrawlPortalView).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(v, null);
                Assert.IsFalse(Physics.GetIgnoreCollision(capsule, v.backstop));
                capsule.height = 1.75f;
                Assert.IsFalse(v.Fits(capsule));
                capsule.height = 0.62f; person.transform.position += Vector3.right * 0.2f;
                Assert.IsFalse(v.Fits(capsule));
            }
            finally
            {
                typeof(Backrooms.Interaction.CrawlPortalView).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(v, null);
                Object.DestroyImmediate(root); Object.DestroyImmediate(person);
                var system = Object.FindAnyObjectByType<Backrooms.Interaction.CrawlPortalSystem>();
                if (system) Object.DestroyImmediate(system.gameObject);
            }
        }

        [Test]
        public void CrawlingThroughADuctComesOutOfThePartnerAndBack()
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var root = new GameObject("Duct traversal test"); root.SetActive(false);
            var backWall = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var source = Backrooms.Interaction.CrawlPortalView.Create(root.transform, new CrawlPortal {
                position = new Vector3(0, 0.4f, 0), normal = Vector3.forward,
                partnerPosition = new Vector3(10, 0.4f, 0), partnerNormal = Vector3.back }, null, null, backWall);
            var destination = Backrooms.Interaction.CrawlPortalView.Create(root.transform, new CrawlPortal {
                position = source.partnerPosition, normal = source.partnerNormal,
                partnerPosition = source.position, partnerNormal = source.normal }, null, null, backWall);
            var viewType = typeof(Backrooms.Interaction.CrawlPortalView);
            viewType.GetMethod("OnEnable", flags).Invoke(source, null); viewType.GetMethod("OnEnable", flags).Invoke(destination, null);
            var system = Object.FindAnyObjectByType<Backrooms.Interaction.CrawlPortalSystem>();
            var systemType = typeof(Backrooms.Interaction.CrawlPortalSystem);
            var person = new GameObject("Test player"); person.transform.SetParent(root.transform);
            var player = person.AddComponent<CamcorderPlayerController>();
            var capsule = person.GetComponent<CharacterController>(); capsule.radius = 0.3f;
            capsule.height = 0.62f; capsule.center = Vector3.up * 0.31f;
            typeof(CamcorderPlayerController).GetMethod("Awake", flags).Invoke(player, null);
            var eye = new GameObject("Test eye").AddComponent<Camera>(); eye.transform.SetParent(person.transform);
            eye.transform.localPosition = Vector3.up * 0.42f; eye.nearClipPlane = 0.05f;
            systemType.GetField("_player", flags).SetValue(system, player);
            systemType.GetField("_capsule", flags).SetValue(system, capsule);
            systemType.GetField("_main", flags).SetValue(system, eye);
            var warp = systemType.GetMethod("TryWarp", flags);
            System.Action<float> eyeAt = z => { person.transform.position = new Vector3(person.transform.position.x, 0.03f, z); warp.Invoke(system, new object[] { eye }); };
            try
            {
                var outside = source.duct.sharedMaterials[0];
                Assert.AreEqual((float)UnityEngine.Rendering.CompareFunction.Equal, outside.GetFloat("_StencilComp"), "seen from outside, the duct only draws through its hole");
                Assert.Greater(outside.renderQueue, 2500, "after the opaque rooms, out of the depth prepass");
                eyeAt(0.4f); eyeAt(0.1f);
                Assert.IsFalse(source.Inside);
                eyeAt(-0.1f);
                Assert.IsTrue(source.Inside, "crossing the opening puts the player in the duct");
                Assert.IsFalse(source.patch.enabled, "the way back stays open while inside");
                Assert.AreEqual((float)UnityEngine.Rendering.CompareFunction.Always, source.duct.sharedMaterials[0].GetFloat("_StencilComp"));
                Assert.AreEqual(0, person.transform.position.x, 0.0001f, "no trip before the far end");
                // The trip happens just before the eye reaches the window (the near plane would cut it off): at 8 cm.
                eyeAt(-1.0f); eyeAt(-1.7f); eyeAt(-1.8f);
                Assert.AreEqual(0, person.transform.position.x, 0.0001f);
                eyeAt(-1.85f);
                Assert.AreEqual(10, person.transform.position.x, 0.0001f, "crossing the window comes out of the partner");
                Assert.AreEqual(0.05f, person.transform.position.z, 0.0001f, "exactly as far into the partner's opening as the eye was from the window");
                Assert.IsFalse(source.Inside); Assert.IsTrue(source.patch.enabled);
                Assert.IsTrue(destination.Inside, "landing in the partner's mouth, crawling out of it");
                eyeAt(-0.1f);
                Assert.IsTrue(destination.Inside);
                eyeAt(-0.3f);
                Assert.IsFalse(destination.Inside, "out in the room");
                // Handheld jitter in front of the partner's opening never sends the player back.
                for (int i = 0; i < 6; i++) eyeAt(-0.3f + ((i & 1) == 0 ? 0.03f : -0.03f));
                Assert.AreEqual(10, person.transform.position.x, 0.0001f);
                // Turning round and crawling back in goes into the partner's own duct, which leads back.
                eyeAt(0.0f);
                Assert.IsTrue(destination.Inside);
                eyeAt(1.0f); eyeAt(1.75f); eyeAt(1.85f);
                Assert.AreEqual(0, person.transform.position.x, 0.0001f, "the partner's duct leads back to the first hole");
                Assert.AreEqual(-0.05f, person.transform.position.z, 0.0001f);
                Assert.IsTrue(source.Inside);
            }
            finally
            {
                foreach (var view in new[] { source, destination })
                { viewType.GetMethod("OnDisable", flags).Invoke(view, null); viewType.GetMethod("OnDestroy", flags).Invoke(view, null); }
                systemType.GetMethod("OnDisable", flags).Invoke(system, null); systemType.GetMethod("OnDestroy", flags).Invoke(system, null);
                Object.DestroyImmediate(system.gameObject); Object.DestroyImmediate(root); Object.DestroyImmediate(backWall);
            }
        }

        [Test]
        public void ChunksWithCrawlSpacesAndNoclipDoorsBuildOnAWorkerThread()
        {
            var assets = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            Assert.IsNotNull(assets);
            SpecialRoomMaterials.Ensure(assets);
            var set = WorldGenSettings.Defaults();
            var gen = new WorldGenerator(1234, set);
            void Build(OfficeLayout layout)
            {
                // BuildData runs on worker threads in the game: it must not touch Unity objects (e.g. create materials).
                System.Exception error = null;
                var t = new System.Threading.Thread(() => { try { new OfficeChunkBuilder(assets).BuildData(layout); } catch (System.Exception e) { error = e; } });
                t.Start(); t.Join();
                Assert.IsNull(error, error?.ToString());
            }
            OfficeLayout ChunkAt(Vector3 world) => gen.Chunk(new Vector3Int(Mathf.FloorToInt(world.x / OfficeBiome.ChunkSize), 0, Mathf.FloorToInt(world.z / OfficeBiome.ChunkSize)));
            var plan = gen.District(new Vector3Int(0, 0, 0));
            Assert.Greater(plan.portals.Count, 0, "a crawl space near spawn");
            var withSpace = ChunkAt(plan.portals[0].position);
            Assert.IsTrue(withSpace.portals.Exists(p => p.space), "the chunk holds the crawl space");
            Build(withSpace);
            for (int x = -8; x < 8; x++)
            for (int z = -8; z < 8; z++)
            {
                if (!NoclipDoors.LinkHere(set, gen.Seed, new Vector2Int(x, z), out _)) continue;
                var doors = gen.District(new Vector3Int(x, 0, z)).noclipDoors;
                if (doors.Count == 0) continue;
                var chunk = ChunkAt(doors[0].position);
                Assert.Greater(chunk.noclipDoors.Count, 0, "the chunk holds the door");
                Build(chunk);
                return;
            }
            Assert.Fail("no noclip door found");
        }

        /// <summary>
        /// Doors and windows of their own size leave no paper-thin wall ends: a short wall ending in a door gets its end post,
        /// two doors either side of a post keep it, and a wall meeting the line where a door is cut through has its end closed.
        /// </summary>
        [Test]
        public void CutoutsLeaveNoOpenWallEnds()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            const float C = OfficeBiome.CellSize, T = 0.15f;
            var bp = RoomBlueprint.CreateNew("Thin ends");
            var v = bp.variants[0];
            v.Resize(10, 8);
            v.ConvertGridOpenings();
            v.openings.Clear();
            v.SetV(3, 0, EdgeCode.Wall); v.SetV(3, 1, EdgeCode.Wall);                                                  // a stub off the south wall
            v.openings.Add(new OpeningDef { vertical = true, line = 3, from = 1.3f, to = 2.2f, top = 2.1f });             // a door near its free end
            v.openings.Add(new OpeningDef { vertical = false, line = 0, from = 4.0f, to = 4.72f, top = 2.1f });           // two doors either side
            v.openings.Add(new OpeningDef { vertical = false, line = 0, from = 4.88f, to = 5.9f, top = 2.1f });           // of the post at 4.8 m
            for (int x = 6; x < 10; x++) v.SetH(x, 4, EdgeCode.Wall);                                                    // a wall meeting the east wall
            v.openings.Add(new OpeningDef { vertical = true, line = 10, from = 4.2f, to = 5.4f, top = 2.1f });            // a door cut through there
            bp.Validate();
            Assert.IsTrue(v.openings.All(o => v.OpeningFits(o)), "the builder allows each of them");
            var L = CustomRoomHarness.BuildLayout(bp, 0, 0, out var placement);
            var assets = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            var builder = new OfficeChunkBuilder(assets);
            builder.BuildData(L);
            var root = new GameObject("ThinEndsTest");
            try
            {
                builder.Instantiate(root.transform);
                var faces = new List<(Vector3 p, Vector3 n)>();
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                {
                    var m = mf.sharedMesh; if (m == null) continue;
                    var vs = m.vertices; var ns = m.normals;
                    for (int i = 0; i < vs.Length && i < ns.Length; i++) faces.Add((mf.transform.localToWorldMatrix.MultiplyPoint3x4(vs[i]) - root.transform.position, ns[i]));
                }
                float ox = placement.rect.x * C, oz = placement.rect.y * C;
                bool Face(Vector3 normal, System.Func<Vector3, bool> where) => faces.Any(f => Vector3.Dot(f.n, normal) > 0.99f && where(f.p - new Vector3(ox, 0f, oz)));
                Assert.IsTrue(Face(Vector3.forward, p => Mathf.Abs(p.z - (2 * C + T * 0.5f)) < 0.003f && Mathf.Abs(p.x - 3 * C) < T * 0.5f + 0.003f && p.y > 1f),
                    "the stub's free end is a post, not two faces with nothing between");
                Assert.IsTrue(Face(Vector3.left, p => Mathf.Abs(p.x - (4 * C - T * 0.5f)) < 0.003f && Mathf.Abs(p.z) < T * 0.5f + 0.003f && p.y > 1f)
                              && Face(Vector3.right, p => Mathf.Abs(p.x - (4 * C + T * 0.5f)) < 0.003f && Mathf.Abs(p.z) < T * 0.5f + 0.003f && p.y > 1f),
                    "the post between the two doors stands");
                Assert.IsTrue(Face(Vector3.right, p => Mathf.Abs(p.x - (10 * C - T * 0.5f)) < 0.003f && Mathf.Abs(p.z - 4 * C) < T * 0.5f + 0.003f && p.y > 0.5f && p.y < 2.2f),
                    "the joining wall's end is closed where the door cuts through");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
