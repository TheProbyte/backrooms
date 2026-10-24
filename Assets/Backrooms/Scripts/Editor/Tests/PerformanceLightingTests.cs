using System.Collections.Generic;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    public class PerformanceLightingTests
    {
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
    }
}
