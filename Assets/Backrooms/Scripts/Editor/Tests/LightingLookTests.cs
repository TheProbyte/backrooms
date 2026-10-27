using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The F5 > LIGHT look (presets, colour), the rectangular panels' form factor, and the traced bounce kernels.</summary>
    public class LightingLookTests
    {
        [Test]
        public void LooksOnlyTouchLookSettings()
        {
            var o = new GraphicsOptions { shadowLights = 9, renderScale = 0.7f, perPixelFar = false };
            o.ApplyLook(2);
            Assert.AreEqual(9, o.shadowLights, "quality settings are not part of a look");
            Assert.AreEqual(0.7f, o.renderScale);
            Assert.IsFalse(o.perPixelFar);
            Assert.AreEqual(2, o.lookPreset);
            Assert.Less(o.lightStrength, 1f, "dim & moody is dimmer");
            o.ApplyLook(0);
            var d = new GraphicsOptions();
            Assert.AreEqual(d.lightStrength, o.lightStrength); Assert.AreEqual(d.haze, o.haze); Assert.AreEqual(d.colourTemp, o.colourTemp);
            for (int i = 0; i < GraphicsOptions.LookNames.Length; i++)
            {
                o.ApplyLook(i);
                Assert.That(o.colourTemp, Is.InRange(2700f, 6500f), GraphicsOptions.LookNames[i]);
                Assert.That(o.haze, Is.InRange(0f, 1f));
            }
            Assert.AreEqual(GraphicsOptions.LookNames.Length, GraphicsOptions.LookTips.Length);
        }

        [Test]
        public void ColourShiftKeepsBrightness()
        {
            var w = GraphicsOptions.ColourShift(4500f, 0f);
            Assert.AreEqual(1f, w.r, 1e-4f); Assert.AreEqual(1f, w.g, 1e-4f); Assert.AreEqual(1f, w.b, 1e-4f);
            foreach (var k in new[] { 2700f, 3500f, 6500f })
            foreach (var t in new[] { -1f, 0f, 1f })
            {
                var s = GraphicsOptions.ColourShift(k, t);
                Assert.AreEqual(1f, 0.2126f * s.r + 0.7152f * s.g + 0.0722f * s.b, 1e-3f, $"{k} K, tint {t}");
            }
            Assert.Greater(GraphicsOptions.ColourShift(3000f, 0f).r, GraphicsOptions.ColourShift(3000f, 0f).b, "warm is red over blue");
            Assert.Greater(GraphicsOptions.ColourShift(6500f, 0f).b, GraphicsOptions.ColourShift(6500f, 0f).r, "cool is blue over red");
            Assert.Greater(GraphicsOptions.ColourShift(4500f, 1f).g, 1f, "green tint");
        }

        [Test]
        public void RectangularPanelMatchesNumericIntegral()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shader support required");
            var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Backrooms/Scripts/Editor/Tests/AreaLightProbe.compute");
            Assert.IsNotNull(shader);
            var centre = new Vector3(0f, 2.6f, 0f);
            var half = new Vector2(0.285f, 0.285f);
            var queries = new List<(Vector3 p, Vector3 n)>
            {
                (new Vector3(0, 0, 0), Vector3.up),                  // floor under it
                (new Vector3(1.5f, 0, 0.4f), Vector3.up),            // floor off to the side
                (new Vector3(0, 2.2f, 0), Vector3.up),               // just under the panel
                (new Vector3(0.6f, 2.0f, 0), Vector3.left),          // a wall right beside it, facing it
                (new Vector3(0.1f, 2.4f, 0), Vector3.left),          // a wall cutting through it: half the panel is behind
                (new Vector3(2f, 1.2f, -1f), new Vector3(-1, 0.3f, 0.5f).normalized),
                (new Vector3(0, 3f, 0), Vector3.up),                 // above it: sees only its back (two-sided integral, but no light reaches)
            };
            var positions = new ComputeBuffer(queries.Count, 12); var normals = new ComputeBuffer(queries.Count, 12); var results = new ComputeBuffer(queries.Count, 4);
            try
            {
                var pa = new Vector3[queries.Count]; var na = new Vector3[queries.Count];
                for (int i = 0; i < queries.Count; i++) { pa[i] = queries[i].p; na[i] = queries[i].n; }
                positions.SetData(pa); normals.SetData(na);
                int k = shader.FindKernel("FormFactor");
                shader.SetBuffer(k, "_Positions", positions); shader.SetBuffer(k, "_Normals", normals); shader.SetBuffer(k, "_Results", results);
                shader.SetVector("_Rect", centre); shader.SetVector("_Half", half); shader.SetInt("_Count", queries.Count);
                shader.Dispatch(k, 1, 1, 1);
                var got = new float[queries.Count]; results.GetData(got);
                for (int i = 0; i < queries.Count; i++)
                {
                    float want = NumericFormFactor(queries[i].p, queries[i].n, centre, half);
                    // Also where a surface cuts through the panel (query 4): its far corners are moved onto the surface's plane.
                    Assert.AreEqual(want, got[i], Mathf.Max(0.02f * want, 2e-4f), $"query {i}: {queries[i].p} {queries[i].n}");
                }
                // Far away it is the point formula: A cos cos / (pi d^2).
                Assert.AreEqual(0.57f * 0.57f / (Mathf.PI * 2.6f * 2.6f), got[0], 0.03f * got[0]);
            }
            finally { positions.Release(); normals.Release(); results.Release(); }
        }

        /// <summary>Brute force: (1/pi) integral over the panel of cos(receiver) cos(panel) / d^2 (receiver cosine clamped).</summary>
        static float NumericFormFactor(Vector3 p, Vector3 n, Vector3 c, Vector2 h)
        {
            const int steps = 200;
            double sum = 0, dA = (2.0 * h.x / steps) * (2.0 * h.y / steps);
            for (int i = 0; i < steps; i++)
            for (int j = 0; j < steps; j++)
            {
                var q = c + new Vector3(-h.x + (i + 0.5f) * 2f * h.x / steps, 0f, -h.y + (j + 0.5f) * 2f * h.y / steps);
                var d = q - p;
                double d2 = d.sqrMagnitude;
                var w = d / Mathf.Sqrt((float)d2);
                double cr = Mathf.Max(0f, Vector3.Dot(n, w));
                double ce = Mathf.Abs(w.y);                       // the panel is horizontal; the integral is two-sided
                sum += cr * ce / d2 * dA;
            }
            return (float)(sum / Mathf.PI);
        }

        struct Box { public Vector4 centre, halfSize, inverseRotation; }
        struct Surface { public Vector4 floor, wall, ceiling; }

        /// <summary>
        /// Two rooms side by side, a light in the west one. The traced bounce reaches the east room only through an opening:
        /// with a solid wall (or a shut door, which is the same edge) the east room's probes stay dark. In the lit room light
        /// comes up from the floor more than down from the unlit ceiling.
        /// </summary>
        [Test]
        public void TracedBounceGoesThroughOpeningsNotWalls()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shader support required");
            const int n = LightGrid.OccN;
            var shader = Object.Instantiate(Resources.Load<ComputeShader>("LightGrid"));
            var buffers = new List<ComputeBuffer>(); var textures = new List<RenderTexture>();
            ComputeBuffer Buffer(int count, int stride) { var b = new ComputeBuffer(count, stride); buffers.Add(b); return b; }
            RenderTexture Probe() { var t = new RenderTexture(n, n * 4, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true }; t.Create(); textures.Add(t); return t; }
            try
            {
                // Cells 0..3 in x: west room 0-1, east room 2-3, the edge between them (west edge of cell 2) a wall or open.
                // Every other edge of the two rooms is a wall; outside them nothing is loaded.
                var occupancy = new uint[n * n * 2]; var surfaces = new Surface[occupancy.Length];
                uint ceiling = 27u << 16, tops = LightGridMath.PackTop(27, 4, 27) | LightGridMath.PackTop(27, 10, 28);
                for (int x = 0; x < 5; x++)
                for (int z = 0; z < 3; z++)
                {
                    int i = z * n + x;
                    bool inside = x < 4 && z < 2;
                    uint west = x == 0 || x == 4 ? LightGridMath.Wall : 0u;       // outer walls (x = 4 is the east wall's owner)
                    uint south = z == 0 || z == 2 ? LightGridMath.Wall : 0u;
                    occupancy[i] = (inside ? LightGridMath.Loaded : 0u) | ceiling | tops | west | (south << 2);
                    if (!inside && x == 4 && z < 2) occupancy[i] = LightGridMath.Loaded | ceiling | tops | LightGridMath.Wall;   // owner of the east wall, outside
                    if (!inside && z == 2 && x < 4) occupancy[i] = LightGridMath.Loaded | ceiling | tops | (LightGridMath.Wall << 2);
                    surfaces[i] = new Surface { floor = Vector4.one * 0.3f, wall = Vector4.one * 0.6f, ceiling = Vector4.one * 0.7f };
                }
                int middle = 2;                       // cells (2, 0) and (2, 1): their west edge divides the rooms
                void SetMiddle(uint kind) { for (int z = 0; z < 2; z++) { int i = z * n + middle; occupancy[i] = (occupancy[i] & ~3u) | kind; } }
                var occ = Buffer(occupancy.Length, 4);
                var material = Buffer(surfaces.Length, 48); material.SetData(surfaces);
                var light = new LightGridMath.GridLight { pos = new Vector3(1.2f, 2.64f, 1.2f), range = 8, colorAll = Vector3.one * 7, color = Vector3.zero, dir = Vector3.down, cosOuter = 0.06f, cosInner = 0.9f, halfSize = Vector2.one * 0.285f };
                var lights = Buffer(1, LightGridMath.GridLight.Stride); lights.SetData(new[] { light });
                var tileValues = new uint[LightGrid.Tiles * LightGrid.Tiles * 2];
                for (int i = 1; i < tileValues.Length; i += 2) tileValues[i] = 1;
                var tiles = Buffer(LightGrid.Tiles * LightGrid.Tiles, 8); tiles.SetData(tileValues);
                var indices = Buffer(1, 4); indices.SetData(new uint[] { 0 });
                var localBoxes = Buffer(1, 48);
                var localTiles = Buffer(LightGrid.Tiles * LightGrid.Tiles, 8); localTiles.SetData(new uint[LightGrid.Tiles * LightGrid.Tiles * 2]);
                var localIndices = Buffer(1, 4); localIndices.SetData(new uint[1]);
                var face = Buffer(n * n * 2 * 12, 16);
                var key = Buffer(n * n * 4, 4);
                var a = new[] { Probe(), Probe(), Probe() }; var b = new[] { Probe(), Probe(), Probe() };

                shader.SetInts("_GridSize", LightGrid.GX, LightGrid.GY, LightGrid.GZ, n);
                shader.SetInts("_TileInfo", LightGrid.TileTexels, LightGrid.Tiles, 0, 0);
                shader.SetInts("_CellMin", 0, 0, 0, 0); shader.SetVector("_GridMisc", new Vector4(9, 0.25f, 0.6f, 0));
                shader.SetVector("_LocalOccInfo", new Vector4(4.8f, LightGrid.Tiles, 0, 0));
                int kFace = shader.FindKernel("FaceLight"), kCopy = shader.FindKernel("ProbeCopy"), kTrace = shader.FindKernel("ProbeTrace");
                foreach (int k in new[] { kFace, kCopy, kTrace })
                {
                    shader.SetBuffer(k, "_Occ", occ);
                    shader.SetBuffer(k, "_ProbeKey", key);
                }
                foreach (int k in new[] { kFace, kTrace })
                {
                    shader.SetBuffer(k, "_Surfaces", material);
                    shader.SetBuffer(k, "_LocalOccBoxes", localBoxes); shader.SetBuffer(k, "_LocalOccTiles", localTiles); shader.SetBuffer(k, "_LocalOccIndices", localIndices);
                }
                shader.SetBuffer(kFace, "_Lights", lights); shader.SetBuffer(kFace, "_Tiles", tiles); shader.SetBuffer(kFace, "_TileLights", indices);
                shader.SetBuffer(kFace, "_FaceLightOut", face); shader.SetBuffer(kTrace, "_FaceLightIn", face);
                shader.SetInt("_HoleCount", 0); shader.SetTexture(kTrace, "_DuctMouthLight", Probe());   // no crawl holes here
                shader.SetVector("_ProbeParams2", new Vector4(1, 0, 0, 0));

                // Irradiance of a cell's low (slab 0) or high (slab 1) probe toward a normal, read back from the SH textures.
                float Irradiance(RenderTexture[] sh, int x, int z, Vector3 normal, int slab = 0)
                {
                    var c = new float[3];
                    for (int ch = 0; ch < 3; ch++)
                    {
                        var prev = RenderTexture.active; RenderTexture.active = sh[ch];
                        var cpu = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                        cpu.ReadPixels(new Rect(x, z + slab * n, 1, 1), 0, 0); cpu.Apply();
                        var v = cpu.GetPixel(0, 0);
                        RenderTexture.active = prev; Object.DestroyImmediate(cpu);
                        c[ch] = Mathf.Max(0f, v.r * 0.282095f + 0.325735f * (v.g * normal.x + v.b * normal.y + v.a * normal.z));
                    }
                    return (c[0] + c[1] + c[2]) / 3f;
                }

                RenderTexture[] Run(uint middleKind)
                {
                    SetMiddle(middleKind); occ.SetData(occupancy);
                    key.SetData(new uint[n * n * 4]);
                    shader.SetVector("_ProbeParams", new Vector4(0, 0.6f, 0.7f, 1f));
                    shader.SetInts("_ProbeInfo", LightGrid.ProbeSlices, 2, 0, -1);
                    shader.Dispatch(kFace, n / 8, n / 8, 2);
                    RenderTexture[] cur = a, prev = b;
                    for (int frame = 0; frame < 24; frame++)
                    {
                        cur = (frame & 1) == 0 ? a : b; prev = (frame & 1) == 0 ? b : a;
                        shader.SetVector("_ProbeParams", new Vector4(frame, 0.6f, 0.7f, 1f));
                        shader.SetInts("_ProbeInfo", LightGrid.ProbeSlices, 2, frame & 3, -1);
                        for (int ch = 0; ch < 3; ch++)
                        {
                            string[] pn = { "_ProbePrevR", "_ProbePrevG", "_ProbePrevB" }, on = { "_ProbeOutR", "_ProbeOutG", "_ProbeOutB" };
                            shader.SetTexture(kCopy, pn[ch], prev[ch]); shader.SetTexture(kCopy, on[ch], cur[ch]);
                            shader.SetTexture(kTrace, pn[ch], prev[ch]); shader.SetTexture(kTrace, on[ch], cur[ch]);
                        }
                        shader.Dispatch(kCopy, n / 8, n / 8, 4);
                        shader.Dispatch(kTrace, n / LightGrid.ProbeSlices * n * 4, 1, 1);
                    }
                    return cur;
                }

                var walled = Run(LightGridMath.Wall);
                // The high probe (2.2 m): below it the lit floor and most of the walls, above it the unlit ceiling.
                float litUp = Irradiance(walled, 1, 0, Vector3.down, 1);     // light arriving from below
                float litDown = Irradiance(walled, 1, 0, Vector3.up, 1);     // from above
                float eastWalled = Irradiance(walled, 3, 0, Vector3.left);
                Assert.Greater(litUp, 0.01f, "the lit room has bounce light");
                Assert.Greater(litUp, litDown * 1.5f, "under the ceiling, more bounce comes up from the lit room than down from the unlit ceiling");
                Assert.Greater(litDown, 0f, "the ceiling has its own (second) bounce");
                Assert.Less(eastWalled, litUp * 0.01f, "nothing passes a solid wall (or a shut door)");
                var open = Run(0u);
                float eastOpen = Irradiance(open, 3, 0, Vector3.left);
                Assert.Greater(eastOpen, eastWalled + litUp * 0.05f, "bounce light goes through an opening into the next room");

                // A crawl hole in the dark east room's far (east) wall, whose other end looks into a lit room: light comes out.
                var mouth = new Texture2D(6, 1, TextureFormat.RGBAFloat, false, true);
                try
                {
                    var bright = new Color[6]; bright[0] = new Color(2, 2, 2, 1);          // facing +x at the far mouth: lit
                    mouth.SetPixels(bright); mouth.Apply();
                    shader.SetTexture(kTrace, "_DuctMouthLight", mouth);
                    shader.SetInt("_HoleCount", 1); shader.SetFloat("_HoleTransmission", 0.15f);
                    shader.SetVectorArray("_HolePos", new[] { new Vector4(4.8f, 0.4f, 1.2f, 0.4f) });
                    shader.SetVectorArray("_HoleNormal", new[] { new Vector4(-1, 0, 0, 0) });
                    shader.SetVectorArray("_HolePartner", new[] { new Vector4(1, 0, 0, 0) });
                    var holed = Run(LightGridMath.Wall);
                    float eastHole = Irradiance(holed, 3, 0, Vector3.right);
                    Assert.Greater(eastHole, eastWalled + 0.001f, "light from the room at a crawl hole's far end comes out of it");
                }
                finally { Object.DestroyImmediate(mouth); }
            }
            finally
            {
                foreach (var x in buffers) x.Release();
                foreach (var t in textures) { t.Release(); Object.DestroyImmediate(t); }
                Object.DestroyImmediate(shader);
            }
        }
    }
}
