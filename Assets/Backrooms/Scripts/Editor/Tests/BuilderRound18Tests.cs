using System.Collections.Generic;
using System.IO;
using System.Linq;
using Backrooms.CustomRooms;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>Build mode round 18: walls drawn and moved, any-shaped rooms, the Backrooms touches, sharing, models.</summary>
    public class BuilderRound18Tests
    {
        const float C = OfficeBiome.CellSize;

        static RoomBlueprint Room(int w, int d)
        {
            var bp = RoomBlueprint.CreateNew("t");
            var v = bp.variants[0];
            v.Resize(w, d);
            v.ConvertGridOpenings();
            return bp;
        }

        static void BuildOnWorker(OfficeLayout L)
        {
            var assets = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            System.Exception error = null;
            var t = new System.Threading.Thread(() => { try { new OfficeChunkBuilder(assets).BuildData(L); } catch (System.Exception e) { error = e; } });
            t.Start(); t.Join();
            Assert.IsNull(error, error?.ToString());
        }

        [Test]
        public void WallsDrawnPointToPointAndRoomsAsBoxes()
        {
            var v = Room(8, 6).variants[0];
            Assert.AreEqual(3, v.DrawLine(true, 4, 0, 3, EdgeCode.Wall), "three edges of wall on the line X = 4");
            for (int z = 0; z < 3; z++) Assert.AreEqual(EdgeCode.Wall, v.V(4, z));
            Assert.AreEqual(EdgeCode.Open, v.V(4, 3));
            v.DrawLine(true, 4, 1, 2, EdgeCode.Open);
            Assert.AreEqual(EdgeCode.Open, v.V(4, 1), "erased");
            v.DrawLine(false, 0, 0, 8, EdgeCode.Open);
            Assert.AreEqual(EdgeCode.Wall, v.H(2, 0), "the outer wall stays shut when erased");
            int area = v.DrawRoom(new RectInt(5, 3, 2, 2), "Closet");
            Assert.AreEqual(1, area);
            Assert.AreEqual(area, v.RoomOf(6, 4));
            Assert.AreEqual(EdgeCode.Wall, v.H(5, 3)); Assert.AreEqual(EdgeCode.Wall, v.V(5, 4)); Assert.AreEqual(EdgeCode.Wall, v.V(7, 3));
        }

        [Test]
        public void WallRunsMoveWithTheirDoorsAndStretch()
        {
            var bp = Room(8, 6); var v = bp.variants[0];
            v.DrawLine(true, 3, 0, 6, EdgeCode.Wall);
            v.rooms.Add(new RoomSettings { name = "East" });
            for (int x = 3; x < 8; x++) for (int z = 0; z < 6; z++) v.SetCell(x, z, 1);
            v.openings.Add(new OpeningDef { vertical = true, line = 3, from = 2.5f, to = 3.4f, top = 2.1f });
            Assert.IsTrue(v.MoveRun(true, 3, 0, 6, 5));
            Assert.AreEqual(EdgeCode.Open, v.V(3, 2)); Assert.AreEqual(EdgeCode.Wall, v.V(5, 2));
            Assert.AreEqual(5, v.openings.Last().line, "the door went with the wall");
            Assert.AreEqual(0, v.RoomOf(3, 2)); Assert.AreEqual(0, v.RoomOf(4, 2)); Assert.AreEqual(1, v.RoomOf(5, 2));
            Assert.IsFalse(v.MoveRun(true, 5, 0, 6, 8), "not onto the outer line");
            Assert.IsTrue(v.StretchRun(true, 5, 0, 6, 2, 6));
            Assert.AreEqual(EdgeCode.Open, v.V(5, 0)); Assert.AreEqual(EdgeCode.Wall, v.V(5, 3));
            bp.Validate();
        }

        [Test]
        public void CutAwayCellsMakeAnLShapeTheWorldWrapsRound([Values(0, 1)] int rot)
        {
            var bp = Room(6, 5); var v = bp.variants[0];
            for (int x = 4; x < 6; x++) for (int z = 3; z < 5; z++) v.SetCell(x, z, -1);
            v.FixOutsideEdges();
            Assert.AreEqual(EdgeCode.Wall, v.V(4, 3), "a wall closes the room against the notch");
            Assert.AreEqual(EdgeCode.Wall, v.H(4, 3));
            Assert.AreEqual(EdgeCode.Open, v.V(5, 3), "between notch cells: open");
            Assert.AreEqual(EdgeCode.Open, v.V(6, 3), "the notch's outer edge: the world's");
            v.openings.Add(new OpeningDef { vertical = true, line = 4, from = 3.8f, to = 4.7f, top = 2.1f });
            bp.Validate();
            Assert.IsTrue(v.LeadsOut(v.openings.Last()), "a door into the notch is a way out");
            Assert.Greater(v.CountExits(), 1);
            var L = CustomRoomHarness.BuildLayout(bp, 0, rot, out var p);
            var notch = new CustomRoomStamp.Xf(rot, v.width, v.depth).Cell(5, 4) + new Vector2Int(p.rect.x, p.rect.y);
            var nr = L.RoomAt(notch.x, notch.y);
            Assert.IsFalse(nr.custom, "the notch is the world's hallway");
            var inside = new CustomRoomStamp.Xf(rot, v.width, v.depth).Cell(1, 1) + new Vector2Int(p.rect.x, p.rect.y);
            Assert.IsTrue(L.RoomAt(inside.x, inside.y).custom);
            BuildOnWorker(L);
        }

        [Test]
        public void MarksSoundsTriggersAndLinksReachTheWorld([Values(0, 1, 2, 3)] int rot)
        {
            var bp = Room(4, 10); var v = bp.variants[0];
            var a = new OpeningDef { vertical = false, line = 0, from = 1.9f, to = 2.9f, top = 2.1f };
            var b = new OpeningDef { vertical = false, line = 10, from = 1.9f, to = 2.9f, top = 2.1f };
            v.openings.Clear(); v.openings.Add(a); v.openings.Add(b);
            bp.Validate();
            a.link = b.id; b.link = a.id;
            v.decals.Add(new DecalDef { vertical = true, line = 0, side = 1, along = 3f, height = 1.4f, size = 0.4f, kind = DecalKind.WaterStain });
            v.decals.Add(new DecalDef { vertical = true, line = 0, side = 1, along = 5f, height = 1.2f, size = 0.06f, kind = DecalKind.Switch });
            v.sounds.Add(new SoundDef { x = 2f, z = 6f, kind = SoundKind.Drip });
            v.triggers.Add(new TriggerDef { area = 0, action = TriggerAction.DoorSlam, door = a.id });
            v.fixtures[0].mood = 2; v.fixtures[0].buzz = true;
            bp.Validate();
            var L = CustomRoomHarness.BuildLayout(bp, 0, rot, out var p);
            Assert.AreEqual(1, L.decals.Count, "the stain");
            Assert.IsTrue(L.plates.Exists(q => q.isSwitch), "the switch is the world's own plate");
            Assert.AreEqual(1, L.sounds.Count);
            Assert.AreEqual(1, L.links.Count, "one link for the pair");
            Assert.AreNotEqual(L.links[0].a.line, L.links[0].b.line, "between the two ends");
            Assert.IsTrue(L.triggers.Count >= 1 && L.triggers.Exists(t => t.hasDoor), "the slam has its door");
            Assert.IsTrue(L.fixtures.Exists(f => f.mood == 2 && f.buzz));
            // The stain faces into the room: the cell on its side is the room's.
            var d = L.decals[0];
            int along = Mathf.FloorToInt(d.along / C);
            var cell = d.vertical ? new Vector2Int(d.side > 0 ? d.line : d.line - 1, along) : new Vector2Int(along, d.side > 0 ? d.line : d.line - 1);
            Assert.IsTrue(L.RoomAt(cell.x, cell.y).custom, $"rot {rot}: the mark is on the room's side");
            BuildOnWorker(L);
        }

        [Test]
        public void MessAndCeilingAndFurnishing()
        {
            var bp = Room(6, 6); var v = bp.variants[0];
            for (int i = 0; i < 6; i++) v.props.Add(new PropDef { kind = PropKind.Box, x = 1f + i, z = 3f, size = new Vector3(0.5f, 0.36f, 0.4f) });
            var before = v.props.Select(q => new Vector2(q.x, q.z)).ToList();
            Assert.AreEqual(6, v.Mess(1f, 7));
            Assert.IsTrue(v.props.Where((q, i) => new Vector2(q.x, q.z) != before[i]).Any(), "things moved");
            v.ToCeiling(v.props[0]);
            Assert.AreEqual(180f, v.props[0].roll); Assert.IsTrue(v.props[0].pinned);
            Assert.AreEqual(v.rooms[0].ceiling, v.props[0].elevation, 1e-4f);
            var L = CustomRoomHarness.BuildLayout(bp, 0, 0, out _);
            int n = L.props.Count;
            DistrictGenerator.DressLikeTheWorld(L, new System.Random(3), L.rooms.FindAll(r => r.custom));
            Assert.Greater(L.props.Count, n - 0, "furnished (or at least nothing lost)");
        }

        [Test]
        public void ShareCodesAndTemplatesRoundTrip()
        {
            foreach (var t in RoomTemplates.All)
            {
                var bp = t.make();
                bp.Validate();
                var back = BuilderApp.FromShareCode(BuilderApp.ShareCode(bp));
                Assert.IsNotNull(back, t.name);
                Assert.AreEqual(bp.variants[0].width, back.variants[0].width, t.name);
                Assert.AreEqual(bp.variants[0].openings.Count, back.variants[0].openings.Count, t.name);
                Assert.Greater(bp.variants[0].CountExits(), 0, t.name + " has a way in");
                var L = CustomRoomHarness.BuildLayout(bp, 0, 0, out _);
                BuildOnWorker(L);
            }
            var endless = RoomTemplates.All.First(t => t.name.StartsWith("Endless")).make();
            Assert.AreEqual(1, CustomRoomHarness.BuildLayout(endless, 0, 0, out _).links.Count, "the endless hallway's doors are linked");
        }

        [Test]
        public void ModelsAreReadFromGlbAndObj()
        {
            string dir = Path.Combine(Path.GetTempPath(), "br_models_" + System.Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            try
            {
                // A one-triangle .obj and the same as a .glb.
                File.WriteAllText(Path.Combine(dir, "tri.obj"), "v 0 0 0\nv 1 0 0\nv 0 2 0\nf 1 2 3\n");
                File.WriteAllBytes(Path.Combine(dir, "tri.glb"), TinyGlb());
                var f = typeof(CustomModels).GetField("s_dir", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var old = f.GetValue(null);
                f.SetValue(null, dir);
                try
                {
                    UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
                    CustomModels.Reload(null);
                    Assert.IsTrue(CustomModels.Get("tri.obj"), "obj: " + string.Join(",", CustomModels.Errors.Select(kv => kv.Key + " " + kv.Value)));
                    Assert.IsTrue(CustomModels.Get("tri.glb"), "glb: " + string.Join(",", CustomModels.Errors.Select(kv => kv.Key + " " + kv.Value)));
                    var box = CustomModels.Get("tri.glb").GetComponent<BoxCollider>();
                    Assert.AreEqual(2f, box.size.y, 1e-3f, "its height");
                    Assert.AreEqual(1f, box.center.y, 1e-3f, "its foot at the pivot");
                }
                finally { f.SetValue(null, old); CustomModels.Reload(null); }
            }
            finally { Directory.Delete(dir, true); }
        }

        /// <summary>A minimal binary glTF: one triangle (0,0,0) (1,0,0) (0,2,0), no indices.</summary>
        static byte[] TinyGlb()
        {
            var bin = new List<byte>();
            foreach (var f in new float[] { 0, 0, 0, 1, 0, 0, 0, 2, 0 }) bin.AddRange(System.BitConverter.GetBytes(f));
            string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"mesh\":0}],"
                + "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0}}]}],"
                + "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,2,0]}],"
                + "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36}],\"buffers\":[{\"byteLength\":36}]}";
            var jb = new List<byte>(System.Text.Encoding.UTF8.GetBytes(json));
            while (jb.Count % 4 != 0) jb.Add(0x20);
            var o = new List<byte>();
            o.AddRange(System.BitConverter.GetBytes(0x46546C67u)); o.AddRange(System.BitConverter.GetBytes(2u));
            o.AddRange(System.BitConverter.GetBytes((uint)(12 + 8 + jb.Count + 8 + bin.Count)));
            o.AddRange(System.BitConverter.GetBytes((uint)jb.Count)); o.AddRange(System.BitConverter.GetBytes(0x4E4F534Au)); o.AddRange(jb);
            o.AddRange(System.BitConverter.GetBytes((uint)bin.Count)); o.AddRange(System.BitConverter.GetBytes(0x004E4942u)); o.AddRange(bin);
            return o.ToArray();
        }
    }
}
