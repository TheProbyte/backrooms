using System.Collections.Generic;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    public class PerformanceLightingTests
    {
        [Test]
        public void PortalCacheSamplesFullPowerAndClearsOutOfWindowDestinations()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders unavailable");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var root = new GameObject("Portal cache test");
            var grid = root.AddComponent<LightGrid>(); grid.compute = Resources.Load<ComputeShader>("LightGrid");
            var output = new RenderTexture(6, 8, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
            var readback = new Texture2D(6, 8, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            try
            {
                typeof(LightGrid).GetMethod("OnEnable", flags).Invoke(grid, null);
                output.Create();
                var shader = Object.Instantiate(Resources.Load<ComputeShader>("PortalLighting"));
                try
                {
                    var positions = new Vector4[8]; var partners = new Vector4[8]; var normals = new Vector4[8];
                    positions[0] = positions[1] = new Vector4(0, 0, 0, 0.8f);
                    partners[0] = new Vector4(2, 0.4f, 2, 0); partners[1] = new Vector4(1000, 0.4f, 1000, 0);
                    normals[0] = normals[1] = Vector3.forward;
                    shader.SetVectorArray("_PortalSpillPosition", positions); shader.SetVectorArray("_PortalSpillPartner", partners);
                    shader.SetVectorArray("_PortalSpillPartnerNormal", normals); shader.SetInt("_PortalSpillCount", 2);
                    shader.SetTexture(0, "_PortalLightOutput", output);
                    var fixture = new LightGridMath.GridLight { pos = new Vector3(2, 2.6f, 2.12f), range = 8,
                        dir = Vector3.down, colorAll = new Vector3(3, 2, 1), color = new Vector3(3, 2, 1), cosOuter = 0.1f, cosInner = 0.5f };
                    var lights = new List<LightGridMath.GridLight> { fixture };
                    Color Sample()
                    {
                        grid.Tick(Vector3.zero, lights); Assert.IsTrue(grid.BindPortalLighting(shader, 0)); shader.Dispatch(0, 1, 2, 1);
                        RenderTexture.active = output; readback.ReadPixels(new Rect(0, 0, 6, 8), 0, 0); readback.Apply();
                        Assert.AreEqual(Color.black.r, readback.GetPixel(2, 1).r, 1e-6f, "outside window must not wrap into unrelated lighting");
                        return readback.GetPixel(2, 0);
                    }
                    var full = Sample(); Assert.Greater(full.r, 0.1f); Assert.Greater(full.r, full.g);
                    fixture.color = Vector3.zero; lights[0] = fixture;
                    var realOnly = Sample(); Assert.AreEqual(full.r, realOnly.r, 0.0001f, "real/grid handover must not dim the cache");
                    fixture.colorAll = Vector3.zero; lights[0] = fixture;
                    Assert.AreEqual(0, Sample().r, 1e-6f, "power off must clear the previous cached irradiance");
                }
                finally { Object.DestroyImmediate(shader); }
            }
            finally
            {
                RenderTexture.active = previous; output.Release(); Object.DestroyImmediate(output); Object.DestroyImmediate(readback);
                if (grid.Ready) typeof(LightGrid).GetMethod("OnDisable", flags).Invoke(grid, null);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EmitterRejectionKeepsEveryContributingGpuQuadratureSample()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders unavailable");
            var probe = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Backrooms/Scripts/Editor/Tests/FixtureLookupProbe.compute");
            var positions = new List<Vector3>();
            for (int x = -16; x <= 16; x++) for (int z = -16; z <= 16; z++)
                foreach (float y in new[] { 0f, 2.55f, 2.65f, 4f }) positions.Add(new Vector3(x * 0.4f, y, z * 0.4f));
            using (var lights = new ComputeBuffer(1, LightGridMath.GridLight.Stride))
            using (var queries = new ComputeBuffer(positions.Count, 12))
            using (var results = new ComputeBuffer(positions.Count, 16))
            {
                queries.SetData(positions); int kernel = probe.FindKernel("ConeProbe");
                probe.SetBuffer(kernel, "_LGridLights", lights); probe.SetBuffer(kernel, "_QueryPositions", queries);
                probe.SetBuffer(kernel, "_Results", results); probe.SetInt("_QueryCount", positions.Count);
                foreach (var size in new[] { Vector2.zero, new Vector2(0.285f, 0.285f), new Vector2(0.59f, 0.055f) })
                foreach (float cosine in new[] { 0.0087265f, 0.7f })
                {
                    lights.SetData(new[] { new LightGridMath.GridLight { pos = new Vector3(0, 2.6f, 0), range = 9f, dir = Vector3.down, halfSize = size, cosOuter = cosine, cosInner = 0.95f } });
                    probe.Dispatch(kernel, (positions.Count + 63) / 64, 1, 1);
                    var values = new Vector4[positions.Count]; results.GetData(values); int rejected = 0;
                    foreach (var value in values)
                        if (value.x == 0) { rejected++; Assert.LessOrEqual(value.y, 1e-8f, "culled a contributing finite-emitter sample"); }
                    Assert.Greater(rejected, 0, "test must exercise rejected receivers");
                }
            }
        }

        [Test]
        public void GpuFixtureLookupHandlesCollisionsNegativeCoordinatesAndRebuilds()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders unavailable");
            var probe = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Backrooms/Scripts/Editor/Tests/FixtureLookupProbe.compute");
            Assert.IsNotNull(probe);
            var lights = new List<LightGridMath.GridLight>();
            for (int i = 0; i < 768; i++)
                lights.Add(new LightGridMath.GridLight {
                    // Pairs deliberately share a hash cell; include exact cell boundaries and far negative positions.
                    pos = new Vector3((i / 32 - 12) * 1.2f, i % 2 == 0 ? 2.58f : 2.62f, (i / 2 % 16 - 8) * 1.2f),
                    colorAll = new Vector3(i + 1, 0, 0)
                });
            var queries = new Vector3[lights.Count + 1];
            for (int i = 0; i < lights.Count; i++) queries[i] = lights[i].pos;
            queries[lights.Count] = new Vector3(-10000.25f, 4, 10000.5f);
            var buffers = new List<ComputeBuffer>();
            ComputeBuffer Buffer(int count, int stride) { var b = new ComputeBuffer(count, stride); buffers.Add(b); return b; }
            try
            {
                var data = Buffer(lights.Count, LightGridMath.GridLight.Stride);
                var table = LightGridMath.BuildFixtureLookup(lights, null);
                var lookup = Buffer(table.Length, 4);
                var query = Buffer(queries.Length, 12); query.SetData(queries);
                var result = Buffer(queries.Length, 16);
                int kernel = probe.FindKernel("Probe");
                probe.SetBuffer(kernel, "_LGridLights", data); probe.SetBuffer(kernel, "_LGridFixtureLookup", lookup);
                probe.SetBuffer(kernel, "_QueryPositions", query); probe.SetBuffer(kernel, "_Results", result);
                probe.SetInt("_QueryCount", queries.Length); probe.SetInt("_LGridFixtureMask", table.Length - 1);
                probe.SetVector("_LGridParams", Vector4.one); probe.SetVector("_LGridWindow", new Vector4(-100, -100, 100, 100));
                void Check(int foundCount)
                {
                    data.SetData(lights); lookup.SetData(table);
                    probe.SetInt("_LGridFixtureMask", table.Length - 1);
                    probe.Dispatch(kernel, (queries.Length + 63) / 64, 1, 1);
                    var values = new Vector4[queries.Length]; result.GetData(values);
                    for (int i = 0; i < foundCount; i++)
                    { Assert.AreEqual(1f, values[i].w, "lost fixture " + i); Assert.AreEqual(i + 1, values[i].x, "wrong collision/reordered fixture"); }
                    for (int i = foundCount; i < queries.Length; i++)
                        Assert.AreEqual(0f, values[i].w, "removed/non-fixtures must not alias to a live light");
                }
                Check(768);
                lights.Reverse(); table = LightGridMath.BuildFixtureLookup(lights, table); Check(768);
                lights.RemoveRange(0, 384); table = LightGridMath.BuildFixtureLookup(lights, table);
                Check(384);
            }
            finally { foreach (var buffer in buffers) buffer.Release(); }
        }

        [Test]
        public void ChunkOwnershipReleasesOnlyRegisteredMeshes()
        {
            var root = new GameObject("Owned mesh test");
            var shared = new Mesh();
            var owned = new Mesh();
            root.AddComponent<MeshFilter>().sharedMesh = shared;
            root.AddComponent<GeneratedChunkResources>().Own(owned);
            Object.DestroyImmediate(root);
            Assert.IsTrue(owned == null);
            Assert.IsTrue(shared != null, "shared/imported meshes must survive");
            Object.DestroyImmediate(shared);
        }

        [Test]
        public void LandmarkEvictionDoesNotChangeRegeneration()
        {
            var world = new WorldGenerator(1234, WorldGenSettings.Defaults());
            var far = new Vector2Int(30, 30);
            var before = world.LandmarksIn(far);
            for (int x = -5; x <= 5; x++) for (int z = -5; z <= 5; z++) world.LandmarksIn(new Vector2Int(x, z));
            int oldCount = world.CachedLandmarks;
            world.Trim(Vector2Int.zero, 6);
            Assert.Less(world.CachedLandmarks, oldCount);
            var after = world.LandmarksIn(far);
            Assert.AreEqual(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++) { Assert.AreEqual(before[i].rect, after[i].rect); Assert.AreEqual(before[i].seed, after[i].seed); }
        }

        [Test]
        public void PartialChunkRemainsHiddenUntilComplete()
        {
            var assets = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            var layout = new OfficeLayout(2, Vector2Int.zero);
            layout.rooms.Add(new Room { id = 0, rect = new RectInt(0, 0, 2, 2) });
            var builder = new OfficeChunkBuilder(assets); builder.BuildData(layout);
            GameObject root = null;
            try
            {
                using (var steps = builder.InstantiateSteps(null))
                {
                    int count = 0;
                    while (steps.MoveNext()) { root = steps.Current; count++; Assert.IsFalse(root.activeSelf); }
                    Assert.Greater(count, 2);
                }
                Assert.IsTrue(root.activeSelf);
                Assert.Greater(root.GetComponentsInChildren<MeshFilter>().Length, 0);
                Assert.Greater(root.GetComponentsInChildren<Collider>().Length, 0);
            }
            finally { if (root) Object.DestroyImmediate(root); }
        }

        [Test]
        public void StableGridSkipsUploadsAndBounceButRespondsToChanges()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders unavailable");
            var root = new GameObject("Grid caching test"); root.SetActive(false);
            try
            {
                var grid = root.AddComponent<LightGrid>(); grid.compute = Resources.Load<ComputeShader>("LightGrid");
                grid.exactDistance = 0f;   // exact per-pixel light everywhere: the texture is not used
                root.SetActive(true); typeof(LightGrid).GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(grid, null);
                Assert.IsTrue(grid.Ready);
                var lights = new List<LightGridMath.GridLight> { new LightGridMath.GridLight { pos = new Vector3(2f, 2.6f, 2f), range = 6f, dir = Vector3.down, color = Vector3.one, colorAll = Vector3.one, cosOuter = 0.1f, cosInner = 0.5f } };
                grid.Tick(Vector3.zero, lights);
                Assert.AreEqual(0, grid.DirtyTexelsLastFrame, "per-pixel mode must not dispatch unused direct texture");
                grid.Tick(Vector3.zero, lights);
                Assert.AreEqual(0, grid.BufferUploadsLastFrame); Assert.AreEqual(0, grid.BounceDispatchesLastFrame);
                var l = lights[0]; l.color *= 0.5f; lights[0] = l;
                grid.Tick(Vector3.zero, lights);
                Assert.Greater(grid.BufferUploadsLastFrame, 0); Assert.AreEqual(0, grid.BounceDispatchesLastFrame, "handover alone does not change full-power bounce");
                l.colorAll *= 0.5f; lights[0] = l;
                grid.Tick(Vector3.zero, lights); Assert.Greater(grid.BounceDispatchesLastFrame, 0);
                grid.exactDistance = 4.5f; grid.Tick(Vector3.zero, lights);
                Assert.Greater(grid.DirtyTexelsLastFrame, 0, "split mode must refresh the stale texture that holds each light's far part");
                Assert.LessOrEqual(grid.DirtyTexelsLastFrame, grid.texelBudget, "a full refresh is spread over frames");
                for (int i = 0; i < 16 && grid.PendingTexels > 0; i++) grid.Tick(Vector3.zero, lights);
                Assert.AreEqual(0, grid.PendingTexels, "the queued refresh finishes");
                grid.Tick(Vector3.zero, lights);
                Assert.AreEqual(0, grid.DirtyTexelsLastFrame, "and then only recompute what changes");
                grid.exactDistance = 0f; grid.Tick(Vector3.zero, lights);
                grid.perPixelDirect = false; grid.Tick(Vector3.zero, lights);
                Assert.Greater(grid.DirtyTexelsLastFrame, 0, "fallback mode must refresh stale texture");
            }
            finally { var grid = root.GetComponent<LightGrid>(); if (grid && grid.Ready) typeof(LightGrid).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(grid, null); Object.DestroyImmediate(root); }
        }
        [Test]
        public void MovedFurnitureInvalidatesOcclusionAndStationaryFurnitureDoesNot()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders unavailable");
            var root = new GameObject("Occlusion cache test"); root.SetActive(false);
            var furniture = new GameObject("Furniture");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var box = furniture.AddComponent<BoxCollider>(); box.size = Vector3.one;
            var owner = furniture.AddComponent<LightingOccluder>();
            typeof(LightingOccluder).GetMethod("OnEnable", flags).Invoke(owner, null);
            try
            {
                var grid = root.AddComponent<LightGrid>(); grid.compute = Resources.Load<ComputeShader>("LightGrid");
                typeof(LightGrid).GetMethod("OnEnable", flags).Invoke(grid, null);
                var lights = new List<LightGridMath.GridLight>();
                Physics.SyncTransforms(); grid.Tick(Vector3.zero, lights);
                Assert.AreEqual(1, grid.OccluderCount); int revision = grid.OcclusionRevision;
                grid.Tick(Vector3.zero, lights); Assert.AreEqual(revision, grid.OcclusionRevision); Assert.AreEqual(0, grid.BufferUploadsLastFrame);
                furniture.transform.position = new Vector3(2, 0, 0); Physics.SyncTransforms(); grid.Tick(Vector3.zero, lights);
                Assert.Greater(grid.OcclusionRevision, revision); Assert.Greater(grid.BufferUploadsLastFrame, 0);
                grid.contactOcclusion = 0; grid.Tick(Vector3.zero, lights); Assert.AreEqual(0, grid.OccluderCount);
            }
            finally
            {
                typeof(LightingOccluder).GetMethod("OnDisable", flags).Invoke(owner, null);
                var grid = root.GetComponent<LightGrid>(); if (grid && grid.Ready) typeof(LightGrid).GetMethod("OnDisable", flags).Invoke(grid, null);
                Object.DestroyImmediate(furniture); Object.DestroyImmediate(root);
            }
        }
        [Test]
        public void FallbackBuffersReleaseBeforeReloadAndRebindForEditor()
        {
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var fields = typeof(LightGridGlobals).GetFields(flags);
            var owned = new List<ComputeBuffer>();
            Shader.EnableKeyword("BACKROOMS_PORTAL_CLIP");
            Shader.SetGlobalVector("_PortalClipPlane", Vector4.one);
            Shader.SetGlobalInt("_PortalSpillCount", 8);
            LightGridGlobals.ResetAndBindFallbacks();
            Assert.IsFalse(Shader.IsKeywordEnabled("BACKROOMS_PORTAL_CLIP"), "builder/editor draws must never inherit a portal clip");
            Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_PortalClipPlane"));
            Assert.AreEqual(0, Shader.GetGlobalInt("_PortalSpillCount"));
            Assert.IsNotNull(Shader.GetGlobalTexture("_PortalIrradiance"));
            foreach (var field in fields)
                if (field.FieldType == typeof(ComputeBuffer))
                {
                    var buffer = (ComputeBuffer)field.GetValue(null); Assert.IsTrue(buffer.IsValid()); owned.Add(buffer);
                }
            Assert.Greater(owned.Count, 0);
            try
            {
                LightGridGlobals.ReleaseFallbacks();
                foreach (var buffer in owned) Assert.IsFalse(buffer.IsValid(), "native storage must be released before domain unload");
                LightGridGlobals.BindFallbacks(); // a late component OnDisable must not recreate buffers during teardown
                foreach (var field in fields) if (field.FieldType == typeof(ComputeBuffer)) Assert.IsNull(field.GetValue(null));
            }
            finally { LightGridGlobals.ResetAndBindFallbacks(); }
            foreach (var field in fields) if (field.FieldType == typeof(ComputeBuffer)) Assert.IsTrue(((ComputeBuffer)field.GetValue(null)).IsValid());
            Assert.AreEqual(0, Shader.GetGlobalVector("_LGridParams").x);
        }
    }
}
