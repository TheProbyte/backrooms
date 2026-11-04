using System.Collections.Generic;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    public class RuntimeBounceTests
    {
        struct Box { public Vector4 centre, halfSize, inverseRotation; }
        struct Surface { public Vector4 floor, wall, ceiling; }

        [Test]
        public void BounceUsesBothStoreysAndMaterialReflectanceWithoutCrossingSlabs()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shader support required");
            const int n = LightGrid.OccN;
            var shader = Object.Instantiate(Resources.Load<ComputeShader>("LightGrid"));
            var allocated = new List<ComputeBuffer>();
            var textures = new List<RenderTexture>();
            ComputeBuffer Buffer(int count, int stride) { var b = new ComputeBuffer(count, stride); allocated.Add(b); return b; }
            RenderTexture Texture() { var t = new RenderTexture(n, n * 2, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true }; t.Create(); textures.Add(t); return t; }
            Color Pixel(RenderTexture t, int x, int y)
            {
                var previous = RenderTexture.active; RenderTexture.active = t;
                var cpu = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
                try { cpu.ReadPixels(new Rect(x, y, 1, 1), 0, 0); cpu.Apply(); return cpu.GetPixel(0, 0); }
                finally { RenderTexture.active = previous; Object.DestroyImmediate(cpu); }
            }
            try
            {
                var occupancy = new uint[n * n * 2]; var surfaces = new Surface[occupancy.Length];
                for (int i = 0; i < occupancy.Length; i++)
                {
                    occupancy[i] = LightGridMath.Loaded | (27u << 16) | LightGridMath.PackTop(27, 4, 27) | LightGridMath.PackTop(27, 10, 28);
                    surfaces[i] = new Surface { floor = Vector4.one * 0.25f, wall = Vector4.one * 0.5f, ceiling = Vector4.one * 0.7f };
                }
                var occ = Buffer(occupancy.Length, 4); occ.SetData(occupancy);
                var material = Buffer(surfaces.Length, 48); material.SetData(surfaces);
                var light = new LightGridMath.GridLight { pos = new Vector3(0.6f, 11.6f, 0.6f), range = 15, colorAll = Vector3.one * 7, dir = Vector3.down, cosOuter = 0.1f, cosInner = 0.5f, halfSize = Vector2.one * 0.285f };
                var lights = Buffer(1, LightGridMath.GridLight.Stride); lights.SetData(new[] { light });
                var tiles = Buffer(LightGrid.Tiles * LightGrid.Tiles, 8); var tileValues = new uint[LightGrid.Tiles * LightGrid.Tiles * 2];
                for (int i = 1; i < tileValues.Length; i += 2) tileValues[i] = 1;
                tiles.SetData(tileValues); var indices = Buffer(1, 4); indices.SetData(new uint[] { 0 });
                var source = Texture(); var wall = Texture(); var direction = Texture();
                int kernel = shader.FindKernel("BounceSource");
                shader.SetInts("_GridSize", LightGrid.GX, LightGrid.GY, LightGrid.GZ, n);
                shader.SetInts("_TileInfo", LightGrid.TileTexels, LightGrid.Tiles, 0, 0);
                shader.SetInts("_CellMin", 0, 0, 0, 0); shader.SetVector("_GridMisc", new Vector4(9, 0.25f, 0.6f, 0));
                shader.SetBuffer(kernel, "_Occ", occ); shader.SetBuffer(kernel, "_Surfaces", material); shader.SetBuffer(kernel, "_Lights", lights);
                shader.SetBuffer(kernel, "_Tiles", tiles); shader.SetBuffer(kernel, "_TileLights", indices);
                var localBoxes = Buffer(1, 48);
                var localTiles = Buffer(LightGrid.Tiles * LightGrid.Tiles, 8); localTiles.SetData(new uint[LightGrid.Tiles * LightGrid.Tiles * 2]);
                var localIndices = Buffer(1, 4); localIndices.SetData(new uint[1]);
                shader.SetBuffer(kernel, "_LocalOccBoxes", localBoxes); shader.SetBuffer(kernel, "_LocalOccTiles", localTiles); shader.SetBuffer(kernel, "_LocalOccIndices", localIndices);
                shader.SetVector("_LocalOccInfo", new Vector4(4.8f, LightGrid.Tiles, 0, 0));
                shader.SetTexture(kernel, "_BounceSrc", source); shader.SetTexture(kernel, "_WallSrc", wall); shader.SetTexture(kernel, "_DirectionSrc", direction);
                shader.Dispatch(kernel, n / 8, n / 8, 2);
                float pale = Pixel(source, 0, n).r;
                Assert.Greater(pale, 0.01f, "upper rooms need their own floor bounce");
                Assert.Less(Pixel(source, 0, 0).r, 0.0001f, "upper light must not bounce through a closed floor slab");
                surfaces[n * n].floor = new Vector4(0.025f, 0.05f, 0.075f, 0); material.SetData(surfaces);
                shader.Dispatch(kernel, n / 8, n / 8, 2);
                var tinted = Pixel(source, 0, n);
                Assert.AreEqual(pale * 0.1f, tinted.r, pale * 0.002f);
                Assert.Greater(tinted.b, tinted.r * 2.8f, "reflected colour must follow the floor material");
                var contactTiles = new uint[LightGrid.Tiles * LightGrid.Tiles * 2];
                for (int i = 1; i < contactTiles.Length; i += 2) contactTiles[i] = 1;
                localTiles.SetData(contactTiles);
                localBoxes.SetData(new[] { new Box { centre = new Vector4(0.6f, 9.6f, 0.6f, 0), halfSize = new Vector4(0.4f, 0.3f, 0.4f, 0), inverseRotation = new Vector4(0, 0, 0, 1) } });
                shader.SetVector("_LocalOccInfo", new Vector4(4.8f, LightGrid.Tiles, 1, 1));
                shader.Dispatch(kernel, n / 8, n / 8, 2);
                Assert.Less(Pixel(source, 0, n).r, tinted.r * 0.1f, "a furniture box must occlude the floor bounce source");
                shader.SetVector("_LocalOccInfo", new Vector4(4.8f, LightGrid.Tiles, 0, 0));
                occupancy[n * n] = (occupancy[n * n] & ~3u) | LightGridMath.Wall; occ.SetData(occupancy);
                light.pos.x = 2.4f; lights.SetData(new[] { light });
                shader.Dispatch(kernel, n / 8, n / 8, 2);
                Assert.Greater(Pixel(wall, 0, n).r, 0.0001f);
                Assert.Less(Pixel(direction, 0, n).r, -0.0001f, "west wall must supply light from the west");
                // Uniform reflected source: diffusion must not repeatedly inject the ceiling return.
                void Clear(RenderTexture target, Color color)
                {
                    var old = RenderTexture.active; RenderTexture.active = target; GL.Clear(false, true, color); RenderTexture.active = old;
                }
                Clear(source, Color.white); Clear(wall, Color.clear); Clear(direction, Color.clear);
                var floor0 = Texture(); var floor1 = Texture(); var wall0 = Texture(); var wall1 = Texture(); var dir0 = Texture(); var dir1 = Texture();
                int diffuse = shader.FindKernel("BounceDiffuse");
                shader.SetBuffer(diffuse, "_Occ", occ); shader.SetBuffer(diffuse, "_Surfaces", material);
                shader.SetTexture(diffuse, "_BounceSrcIn", source); shader.SetTexture(diffuse, "_WallSrcIn", wall); shader.SetTexture(diffuse, "_DirectionSrcIn", direction);
                RenderTexture floorRead = source, wallRead = wall, dirRead = direction;
                for (int step = 0; step < 6; step++)
                {
                    var floorWrite = (step & 1) == 0 ? floor0 : floor1; var wallWrite = (step & 1) == 0 ? wall0 : wall1; var dirWrite = (step & 1) == 0 ? dir0 : dir1;
                    shader.SetTexture(diffuse, "_BounceIn", floorRead); shader.SetTexture(diffuse, "_WallIn", wallRead); shader.SetTexture(diffuse, "_DirectionIn", dirRead);
                    shader.SetTexture(diffuse, "_BounceOut", floorWrite); shader.SetTexture(diffuse, "_WallOut", wallWrite); shader.SetTexture(diffuse, "_DirectionOut", dirWrite);
                    shader.Dispatch(diffuse, n / 8, n / 8, 2);
                    floorRead = floorWrite; wallRead = wallWrite; dirRead = dirWrite;
                }
                var ceilingBounce = Pixel(wallRead, 5, 5); var isotropic = Pixel(dirRead, 5, 5);
                Assert.Greater(ceilingBounce.r, 0.01f);
                Assert.LessOrEqual(ceilingBounce.r, 0.08f * 0.7f + 0.0002f, "diffusion must not amplify its fixed source");
                Assert.AreEqual(ceilingBounce.r, isotropic.a, 0.0002f, "isotropic light must reduce directional anisotropy");
                Assert.AreEqual(0, isotropic.r, 0.0002f);
            }
            finally
            {
                foreach (var b in allocated) b.Release();
                foreach (var t in textures) { t.Release(); Object.DestroyImmediate(t); }
                Object.DestroyImmediate(shader);
            }
        }
    }
}
