using System.Reflection;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    public class IndoorReflectionTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [Test]
        public void WorldResetCancelsOldCaptureAndAllowsANewProbe()
        {
            var root = new GameObject("Reflection reset test");
            var reflections = root.AddComponent<RuntimeIndoorReflections>();
            try
            {
                var world = new WorldGenerator(1234, WorldGenSettings.Defaults());
                typeof(RuntimeIndoorReflections).GetMethod("FollowWorld", Private).Invoke(reflections, new object[] { world });
                var make = typeof(RuntimeIndoorReflections).GetMethod("MakeSlot", Private);
                var old = make.Invoke(reflections, new object[] { 0 });
                var probe = (ReflectionProbe)old.GetType().GetField("probe").GetValue(old);
                typeof(RuntimeIndoorReflections).GetField("_capturing", Private).SetValue(reflections, old);
                Assert.IsTrue(reflections.Capturing);
                typeof(RuntimeIndoorReflections).GetMethod("FollowWorld", Private).Invoke(reflections, new object[] { new WorldGenerator(5678, WorldGenSettings.Defaults()) });
                Assert.IsFalse(reflections.Capturing, "a stale time-sliced capture must not block the next world");
                Assert.IsTrue(probe == null, "old world's probe must be released");
                Assert.IsNotNull(make.Invoke(reflections, new object[] { 0 }));
            }
            finally
            {
                typeof(RuntimeIndoorReflections).GetMethod("OnDestroy", Private).Invoke(reflections, null);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ProbeBoundsStopAtInternalWallsEvenInsideOneRoomId()
        {
            var root = new GameObject("Reflection bounds test");
            try
            {
                var streamer = root.AddComponent<WorldStreamer>();
                var layout = new OfficeLayout(OfficeBiome.ChunkCells, Vector2Int.zero);
                layout.rooms.Add(new Room { id = 0, globalId = 0, rect = new RectInt(0, 0, OfficeBiome.ChunkCells, OfficeBiome.ChunkCells) });
                for (int z = 0; z < OfficeBiome.ChunkCells; z++) layout.vEdges[2, z] = EdgeKind.Wall;
                var chunkType = typeof(WorldStreamer).GetNestedType("Chunk", BindingFlags.NonPublic);
                var chunk = System.Activator.CreateInstance(chunkType, true);
                chunkType.GetField("layout").SetValue(chunk, layout);
                var chunks = (System.Collections.IDictionary)typeof(WorldStreamer).GetField("_chunks", Private).GetValue(streamer);
                chunks.Add(Vector3Int.zero, chunk);
                var reflections = root.AddComponent<RuntimeIndoorReflections>(); reflections.streamer = streamer;
                var args = new object[] { new Vector3Int(1, 0, 1), default(Bounds) };
                Assert.IsTrue((bool)typeof(RuntimeIndoorReflections).GetMethod("TryBounds", Private).Invoke(reflections, args));
                var bounds = (Bounds)args[1];
                Assert.AreEqual(2 * OfficeBiome.CellSize - 0.03f, bounds.max.x, 0.001f);
                Assert.AreEqual(0.03f, bounds.min.x, 0.001f);
                Assert.AreEqual(OfficeBiome.CeilingHeight + 0.03f, bounds.max.y, 0.001f);
                Assert.Less(bounds.min.y, 0, "floor pixels must be inside the reflection volume");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ReflectionVariantsAreSharedAndLeaveSourceMaterialUnchanged()
        {
            var root = new GameObject("Reflection material test");
            var source = new Material(Shader.Find("Backrooms/Lit"));
            try
            {
                source.SetFloat("_EnvironmentReflections", 0); source.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                source.SetFloat("_Smoothness", 0.5f);
                var streamer = root.AddComponent<WorldStreamer>();
                var reflections = root.AddComponent<RuntimeIndoorReflections>(); reflections.streamer = streamer;
                var a = new GameObject("a"); a.transform.SetParent(root.transform); var ra = a.AddComponent<MeshRenderer>(); ra.sharedMaterial = source;
                var b = new GameObject("b"); b.transform.SetParent(root.transform); var rb = b.AddComponent<MeshRenderer>(); rb.sharedMaterial = source;
                reflections.Prepare(root);
                Assert.AreSame(ra.sharedMaterial, rb.sharedMaterial); Assert.AreNotSame(source, ra.sharedMaterial);
                Assert.AreEqual(0, source.GetFloat("_EnvironmentReflections")); Assert.IsTrue(source.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"));
                Assert.AreEqual(1, ra.sharedMaterial.GetFloat("_EnvironmentReflections"));
                Assert.IsFalse(ra.sharedMaterial.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"));
                reflections.Prepare(root); Assert.AreSame(ra.sharedMaterial, rb.sharedMaterial);
                // EditMode does not invoke ordinary MonoBehaviour destruction callbacks.
                typeof(RuntimeIndoorReflections).GetMethod("OnDestroy", Private).Invoke(reflections, null);
                Assert.AreSame(source, ra.sharedMaterial);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(source); }
        }
    }
}
