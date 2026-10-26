using System.Collections.Generic;
using System.IO;
using Backrooms.Generation;
using Backrooms.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>Light switches' zones, tape laid as a path, and where doors go.</summary>
    public class RoundFourTests
    {
        [TearDown]
        public void TearDown() => LightZones.Clear();

        [Test]
        public void SwitchedOffRoomsAreDarkAndSurviveTheSave()
        {
            LightZones.Clear();
            var chunk = new Vector3Int(2, 0, -3);
            Assert.AreEqual(1f, LightZones.Level(chunk, 4, 0f, 0.5f), "never switched: on");
            LightZones.Set(chunk, 4, true, instant: true);
            Assert.IsTrue(LightZones.IsOff(chunk, 4));
            Assert.AreEqual(0f, LightZones.Level(chunk, 4, 10f, 0.5f), "off: dark");
            Assert.AreEqual(1f, LightZones.Level(chunk, 5, 10f, 0.5f), "the next room is not touched");
            var saved = LightZones.Save();
            LightZones.Clear();
            Assert.IsFalse(LightZones.IsOff(chunk, 4));
            LightZones.Load(saved);
            Assert.IsTrue(LightZones.IsOff(chunk, 4), "restored from the save");
            Assert.AreEqual(0f, LightZones.Level(chunk, 4, 0f, 0.5f), "restored dark at once, no flicker");
        }

        [Test]
        public void TapeBentRoundACornerIsOneStripAndSurvivesTheSave()
        {
            var go = new GameObject("MarksTest");
            try
            {
                var marks = go.AddComponent<Marks>();
                // Along a wall (normal -z), round its end (normal +x).
                var pts = new List<Vector3> { new Vector3(0, 1, 0), new Vector3(0.3f, 1.01f, 0), new Vector3(0.6f, 1.03f, 0), new Vector3(0.6f, 1.03f, 0.25f) };
                var nrm = new List<Vector3> { Vector3.back, Vector3.back, Vector3.right, Vector3.right };
                marks.AddTape(pts, nrm);
                Assert.AreEqual(1, marks.Count, "one strip");
                byte[] data;
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms)) { marks.Write(w); w.Flush(); data = ms.ToArray(); }
                var go2 = new GameObject("MarksTest2");
                try
                {
                    var again = go2.AddComponent<Marks>();
                    using (var r = new BinaryReader(new MemoryStream(data))) again.Read(r);
                    Assert.AreEqual(3, again.Count, "three pieces read back (each quad is kept)");
                    using (var ms = new MemoryStream())
                    using (var w = new BinaryWriter(ms)) { again.Write(w); w.Flush(); CollectionAssert.AreEqual(data, ms.ToArray(), "written back the same"); }
                }
                finally { Object.DestroyImmediate(go2); }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void DoorsOnlyInSquareHeadedDoorwaysBetweenTwoRooms()
        {
            var assets = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            DoorModels.Ensure(assets);
            Assume.That(DoorModels.HasModel, "the door model is in Resources/Items");
            var gen = new WorldGenerator(1234, WorldGenSettings.Defaults());
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            int doors = 0, headed = 0;
            for (int cx = -3; cx <= 3; cx++)
            for (int cz = -3; cz <= 3; cz++)
            {
                var L = gen.Chunk(new Vector2Int(cx, cz));
                var builder = new OfficeChunkBuilder(assets);
                builder.BuildData(L, gen.Neighbours(new Vector2Int(cx, cz)));
                var list = (List<OfficeChunkBuilder.DoorSpec>)typeof(OfficeChunkBuilder).GetField("_doors", flags).GetValue(builder);
                for (int x = 1; x < L.N; x++) for (int z = 0; z < L.N; z++) if (L.vEdges[x, z] == EdgeKind.DoorHeader && !L.archV[x, z]) headed++;
                for (int x = 0; x < L.N; x++) for (int z = 1; z < L.N; z++) if (L.hEdges[x, z] == EdgeKind.DoorHeader && !L.archH[x, z]) headed++;
                foreach (var d in list)
                {
                    doors++;
                    var parts = d.key.Split(',');
                    bool vertical = parts[3] == "1";
                    int line = int.Parse(parts[4]), along = int.Parse(parts[5]);
                    var e = vertical ? L.vEdges[line, along] : L.hEdges[along, line];
                    Assert.AreEqual(EdgeKind.DoorHeader, e, $"door {d.key} is in a headed doorway");
                    Assert.IsFalse(vertical ? L.archV[line, along] : L.archH[along, line], $"door {d.key} is not in an arch");
                    Assert.That(line, Is.InRange(1, L.N - 1), "inside its chunk (a border doorway would belong to two chunks)");
                    Assert.That(d.angle, Is.InRange(0f, Door.MaxOpen));
                }
                // The same every time.
                var again = new OfficeChunkBuilder(assets);
                again.BuildData(gen.Chunk(new Vector2Int(cx, cz)), gen.Neighbours(new Vector2Int(cx, cz)));
                var list2 = (List<OfficeChunkBuilder.DoorSpec>)typeof(OfficeChunkBuilder).GetField("_doors", flags).GetValue(again);
                Assert.AreEqual(list.Count, list2.Count);
                for (int i = 0; i < list.Count; i++) Assert.AreEqual(list[i].key, list2[i].key);
            }
            Assert.Greater(doors, 0, "some doorways have doors");
            Assert.Less(doors, headed, "not every doorway");
        }
    }
}
