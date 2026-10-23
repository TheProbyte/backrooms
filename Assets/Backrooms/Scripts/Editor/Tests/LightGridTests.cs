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

        [Test]
        public void OpeningTopsAboveSixMetresSurvivePacking()
        {
            // 6 bits of tenths stopped at 6.3 m, which blocked light across every edge above that in tall rooms and atrium wells.
            foreach (float top in new[] { 2.1f, 2.7f, 6.3f, 6.4f, 8.1f, 11.7f, 12.7f })
            {
                uint tenths = (uint)Mathf.RoundToInt(top * 10f);
                Assert.AreEqual(top, LightGridMath.WestTop(LightGridMath.PackTop(tenths, 4, 27)), 0.001f, $"west {top}");
                Assert.AreEqual(top, LightGridMath.SouthTop(LightGridMath.PackTop(tenths, 10, 28)), 0.001f, $"south {top}");
            }
        }

        [Test]
        public void LightsHungHighInAnAtriumWellReachItsFloor()
        {
            var gen = new WorldGenerator(1234, WorldGenSettings.Defaults());
            LandmarkPlacement p = default; bool found = false;
            for (int r = 0; r < 40 && !found; r++)
            for (int x = -r; x <= r && !found; x++)
            for (int z = -r; z <= r && !found; z++)
            {
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != r) continue;
                foreach (var l in gen.LandmarksIn(new Vector2Int(x, z))) if (l.kind == LandmarkKind.Atrium) { p = l; found = true; break; }
            }
            Assert.IsTrue(found, "no atrium near the origin");
            var occ = LightGridMath.FromWorld(gen);
            var core = p.core.width > 0 ? p.core : p.rect;
            float cx = p.district.x * WorldGenSettings.DistrictCells + core.x + core.width * 0.5f, cz = p.district.y * WorldGenSettings.DistrictCells + core.y + core.height * 0.5f;
            var light = new Vector3(cx * C, Pitch + 2.6f, cz * C);
            // Floor and the wall face of the well, every point on the well's side of the ring: all in view of a lamp hung from the top.
            for (float dx = -3.6f; dx <= 3.6f; dx += 1.2f)
            for (float dz = -3.6f; dz <= 3.6f; dz += 1.2f)
                foreach (float y in new[] { 0.1f, 4f, 7f })
                    Assert.IsTrue(LightGridMath.Visible(light, new Vector3(light.x + dx, y, light.z + dz), occ, Pitch), $"well point {dx},{y},{dz} not visible from the top lamp");
        }
    }
}
