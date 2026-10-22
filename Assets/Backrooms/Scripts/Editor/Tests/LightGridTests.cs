using System.Collections.Generic;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>
    /// The light grid's occlusion and falloff (CPU mirror of Resources/LightGrid.compute) on small
    /// hand-made layouts, plus occupancy packing against generated chunks.
    /// </summary>
    public class LightGridTests
    {
        const float C = OfficeBiome.CellSize, Pitch = 9f;

        /// <summary>A row of cells along X with given edges between them; every cell 2.7 m high, storey 0.</summary>
        static LightGridMath.OccupancyLookup Row(params (uint kind, float top)[] edgesBetween)
        {
            var cells = new Dictionary<(int, int, int), uint>();
            for (int x = 0; x <= edgesBetween.Length; x++)
            {
                uint west = x == 0 ? LightGridMath.Wall : edgesBetween[x - 1].kind;
                float top = x == 0 ? 0f : edgesBetween[x - 1].top;
                uint o = LightGridMath.Loaded | west | (LightGridMath.Wall << 2) | ((uint)Mathf.RoundToInt(top * 10f) << 4) | (27u << 16);
                cells[(x, 0, 0)] = o;
            }
            return (x, z, s) => cells.TryGetValue((x, z, s), out var v) ? v : 0u;
        }

        static LightGridMath.GridLight Troffer(float x, float z)
        {
            var f = new Fixture { height = OfficeBiome.CeilingHeight };
            var spec = LightLayers.FixtureSpec(f, new Vector2(x, z));
            return LightGridMath.FromSpec(spec, Color.white, 0, 0f);
        }

        [Test]
        public void WallsBlockAndDoorsPass()
        {
            var light = Troffer(0.5f * C, 0.5f * C);
            var target = new Vector3(1.5f * C, 0.3f, 0.5f * C);           // floor of the next cell
            Assert.AreEqual(0f, LightGridMath.Irradiance(light, target, Row((LightGridMath.Wall, 0f)), Pitch), "light went through a wall");
            Assert.Greater(LightGridMath.Irradiance(light, target, Row((LightGridMath.Door, 2.1f)), Pitch), 0.05f, "light should pass a doorway");
            Assert.Greater(LightGridMath.Irradiance(light, target, Row((LightGridMath.Open, 2.7f)), Pitch), 0.05f, "light should cross an open-plan edge");
        }

        [Test]
        public void HeadersBlockLightAboveTheOpening()
        {
            var light = Troffer(0.5f * C, 0.5f * C);
            var headed = Row((LightGridMath.Header, 2.1f), (LightGridMath.Open, 2.7f));
            var open = Row((LightGridMath.Open, 2.7f), (LightGridMath.Open, 2.7f));
            // A wall point two cells away at 1.8 m: the ray crosses the doorway line at about 2.4 m.
            var high = new Vector3(2.5f * C, 1.8f, 0.5f * C);
            Assert.Greater(LightGridMath.Irradiance(light, high, open, Pitch), 0f, "control: lit without the header");
            Assert.AreEqual(0f, LightGridMath.Irradiance(light, high, headed, Pitch), "the header should block it");
            // The floor two cells away: the ray dips under the header.
            Assert.Greater(LightGridMath.Irradiance(light, new Vector3(2.5f * C, 0.3f, 0.5f * C), headed, Pitch), 0f);
        }

        [Test]
        public void FloorsBlockLightExceptThroughAWell()
        {
            // Light on storey 1 above cell 0; target on storey 0 below cell 0.
            var light = Troffer(0.5f * C, 0.5f * C); light.pos.y += Pitch; light.storey = 1;
            var target = new Vector3(0.5f * C, 0.3f, 0.5f * C);
            uint ground = LightGridMath.Loaded | LightGridMath.Wall | (LightGridMath.Wall << 2) | (27u << 16);
            LightGridMath.OccupancyLookup Occ(bool well) => (x, z, s) =>
                x == 0 && z == 0 ? (s == 0 ? ground | (well ? LightGridMath.OpenAbove : 0u) : ground | (well ? LightGridMath.NoFloor : 0u)) : 0u;
            light.range = 15f;
            Assert.AreEqual(0f, LightGridMath.Irradiance(light, target, Occ(false), Pitch), "light went through a floor slab");
            Assert.Greater(LightGridMath.Irradiance(light, target, Occ(true), Pitch), 0f, "light should fall down a well");
        }

        [Test]
        public void UnobstructedGridLightMatchesTheRealLight()
        {
            // Same falloff as URP (and as the real light the fixture becomes when near).
            var f = new Fixture { height = OfficeBiome.CeilingHeight };
            var spec = LightLayers.FixtureSpec(f, new Vector2(0.5f * C, 0.5f * C));
            var g = LightGridMath.FromSpec(spec, Color.white, 0, 0f);
            var occ = Row((LightGridMath.Open, 2.7f), (LightGridMath.Open, 2.7f));
            foreach (var p in new[] { new Vector3(0.6f, 0.3f, 0.6f), new Vector3(1.8f, 0.3f, 0.6f), new Vector3(3.0f, 1.2f, 0.6f) })
            {
                float expected = LightLayers.Irradiance(spec, p);
                Assert.AreEqual(expected, LightGridMath.Irradiance(g, p, occ, Pitch), expected * 0.02f + 1e-5f, $"at {p}");
            }
        }

        [Test]
        public void OccupancyMatchesTheLayout()
        {
            var gen = new WorldGenerator(1234, WorldGenSettings.Defaults());
            var occ = LightGridMath.FromWorld(gen);
            int N = OfficeBiome.ChunkCells;
            for (int cx = -1; cx <= 1; cx++)
            for (int cz = -1; cz <= 1; cz++)
            {
                var L = gen.Chunk(new Vector2Int(cx, cz));
                for (int x = 0; x < N; x++)
                for (int z = 0; z < N; z++)
                {
                    uint o = occ(cx * N + x, cz * N + z, 0);
                    Assert.IsTrue(LightGridMath.IsLoaded(o));
                    Assert.AreEqual(L.vEdges[x, z] == EdgeKind.Wall, LightGridMath.WestKind(o) == LightGridMath.Wall);
                    Assert.AreEqual(L.hEdges[x, z] == EdgeKind.Wall, LightGridMath.SouthKind(o) == LightGridMath.Wall);
                    Assert.AreEqual(L.CeilingAt(x, z), LightGridMath.Ceiling(o), 0.051f);
                }
            }
        }
    }
}
