using Backrooms.CustomRooms;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>
    /// The reference room as a room of the procedural world, and the light it is matched with: Lambertian ceiling panels, and a
    /// camcorder white balance that keeps yellow rooms yellow.
    /// </summary>
    public class ReferenceRoomTests
    {
        [Test]
        public void ReferenceRoomIsAWorldRoom()
        {
            var bp = ReferenceBlueprint.Create();
            var v = bp.variants[0];
            Assert.LessOrEqual(Mathf.Max(v.width, v.depth), RoomVariant.MaxSize);
            // The opening: three headed cells under one header, walls either side; the exit behind the camera.
            for (int x = 0; x < v.width; x++)
                Assert.AreEqual(x >= 3 && x <= 5 ? EdgeCode.Header : EdgeCode.Wall, v.H(x, 7), "opening wall at x " + x);
            Assert.AreEqual(EdgeCode.Header, v.H(5, 0), "exit behind the camera");
            // Built like any placed room: the stamped layout has the panels and the wide headed opening.
            var L = CustomRoomHarness.BuildLayout(bp, 0, 0, out var p);
            int inside = 0;
            foreach (var f in L.fixtures) if (p.rect.Contains(f.cell)) inside++;
            Assert.AreEqual(v.fixtures.Count, inside, "every panel is a world fixture");
            int headers = 0;
            for (int x = 0; x < L.N; x++) for (int z = 0; z <= L.N; z++) if (L.hEdges[x, z] == EdgeKind.DoorHeader) headers++;
            Assert.GreaterOrEqual(headers, 4, "the opening (3) and the exit");
            // The camera stands in the front room, 5.2 m from the opening wall, like the hand-built room.
            var cam = ReferenceBlueprint.ToBlueprint(ReferenceBlueprint.Camera);
            Assert.AreEqual(0, v.RoomOf(Mathf.FloorToInt(cam.x / OfficeBiome.CellSize), Mathf.FloorToInt(cam.z / OfficeBiome.CellSize)));
            Assert.AreEqual(5.2f, 7 * OfficeBiome.CellSize - cam.z, 1e-4f);
        }

        [Test]
        public void PanelsAreLambertianAtFullSpread()
        {
            FixtureLightManager.TrofferCone(1f, false, false, out float outer, out float inner);
            float co = Mathf.Cos(outer * 0.5f * Mathf.Deg2Rad), ci = Mathf.Cos(inner * 0.5f * Mathf.Deg2Rad);
            for (int deg = 0; deg <= 85; deg += 5)
            {
                float c = Mathf.Cos(deg * Mathf.Deg2Rad);
                Assert.AreEqual(c, LightGridMath.PanelProfile(c, co, ci), 0.01f, "intensity ~ cos(angle) at " + deg);
            }
            // The default spread is close to it; a low spread is narrower (a downlight).
            FixtureLightManager.TrofferCone(0.8f, false, false, out outer, out inner);
            float c60 = Mathf.Cos(60f * Mathf.Deg2Rad);
            Assert.AreEqual(0.5f, LightGridMath.PanelProfile(c60, Mathf.Cos(outer * 0.5f * Mathf.Deg2Rad), Mathf.Cos(inner * 0.5f * Mathf.Deg2Rad)), 0.05f);
            FixtureLightManager.TrofferCone(0f, false, false, out outer, out inner);
            Assert.Less(LightGridMath.PanelProfile(c60, Mathf.Cos(outer * 0.5f * Mathf.Deg2Rad), Mathf.Cos(inner * 0.5f * Mathf.Deg2Rad)), 0.45f);
        }

        [Test]
        public void WhiteBalanceKeepsYellowYellow()
        {
            // A yellow room: red near green, little blue. A camcorder AWB lifts blue and leaves red near green's level; a
            // warm/cool axis would cut red as far as it lifts blue and turn the yellow green.
            var g = CamcorderAutoExposure.AwbGains(new Vector3(1f, 0.95f, 0.4f), 0.3f);
            Assert.Greater(g.z, 1.15f, "blue is lifted");
            Assert.AreEqual(g.y, g.x, 0.03f, "red stays with green");
            // An orange cast is pulled toward yellow (red down a little), a blue one warmed.
            var o = CamcorderAutoExposure.AwbGains(new Vector3(1f, 0.7f, 0.4f), 0.3f);
            Assert.Less(o.x, o.y);
            var b = CamcorderAutoExposure.AwbGains(new Vector3(0.6f, 0.8f, 1f), 0.3f);
            Assert.Greater(b.x, b.y); Assert.Less(b.z, b.y);
        }
    }
}
