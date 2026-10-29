using System.Collections.Generic;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>Height and depth (world version 14, DepthPlan, Docs/WorldDepthPlan.md): sunken wings, pit rooms, cut corners.</summary>
    public class DepthPlanTests
    {
        static readonly int[] Seeds = { 1, 1234, 98765, 424242 };
        const float C = OfficeBiome.CellSize;
        /// <summary>The highest pull a shaft asks for: 1.3 m, under the mantle's 1.35 (CamcorderPlayerController.mantleMax).</summary>
        const float Mantle = 1.3f + 1e-3f;
        static readonly int[] DX = { -1, 1, 0, 0 }, DZ = { 0, 0, -1, 1 };

        static IEnumerable<OfficeLayout> Districts()
        {
            foreach (int seed in Seeds)
            {
                var gen = new WorldGenerator(seed, WorldGenSettings.Defaults());
                for (int x = -2; x <= 2; x++)
                for (int z = -2; z <= 2; z++)
                {
                    var L = gen.District(new Vector3Int(x, 0, z));
                    if (L.style == ZoneStyle.Pool) continue;
                    yield return L;
                }
            }
        }

        [Test]
        public void TheFeaturesTurnUp()
        {
            int wings = 0, pits = 0, corners = 0;
            foreach (var L in Districts())
            {
                bool wing = false, pit = false;
                foreach (var r in L.rooms)
                {
                    if (r.feature == RoomFeature.Sunken) wing = true;
                    if (r.feature == RoomFeature.Pit) pit = true;
                    for (int c = 0; c < 4; c++) if ((r.chamfers & (1 << c)) != 0) corners++;
                }
                if (wing) wings++;
                if (pit) pits++;
            }
            // 100 districts: a wing in about a third, a pit room in a few, cut corners all over.
            Assert.That(wings, Is.GreaterThan(10), "districts with a sunken wing");
            Assert.That(pits, Is.GreaterThan(0), "districts with a pit room");
            Assert.That(corners, Is.GreaterThan(20), "cut corners");
        }

        [Test]
        public void EveryWayIntoASunkenWingHasSteps()
        {
            foreach (var L in Districts())
            foreach (var r in L.rooms)
            {
                if (r.feature != RoomFeature.Sunken) continue;
                var rc = r.rect;
                Assert.That(rc.xMin > 0 && rc.yMin > 0 && rc.xMax < L.N && rc.yMax < L.N, Is.True, $"{L.coord}: a wing on the district's edge");
                Assert.That(-r.floor, Is.InRange(0.44f, 0.91f), "depth");
                for (int x = rc.xMin; x < rc.xMax; x++)
                for (int z = rc.yMin; z < rc.yMax; z++)
                {
                    int outs = 0;
                    for (int s = 0; s < 4; s++)
                    {
                        int nx = x + DX[s], nz = z + DZ[s];
                        if (!L.InChunk(nx, nz) || !OfficeLayout.IsPassable(L.EdgeOf(x, z, s))) continue;
                        if (Mathf.Abs(L.FloorAt(nx, nz) - r.floor) < 0.001f) continue;
                        outs++;
                        Assert.AreEqual(s, L.StepSide(x, z), $"{L.coord} cell ({x}, {z}): a way out to the higher floor with no steps up to it");
                        // The steps come down onto the wing's own floor.
                        int bx = x - DX[s], bz = z - DZ[s];
                        Assert.AreEqual(r.floor, L.FloorAt(bx, bz), 0.001f, "the cell below the steps");
                    }
                    Assert.That(outs, Is.LessThanOrEqualTo(1), $"{L.coord} cell ({x}, {z}): two flights in one cell");
                    if (outs == 0) Assert.AreEqual(-1, L.StepSide(x, z), "steps with nowhere to go");
                }
            }
        }

        [Test]
        public void PitShaftsLeaveWalkwaysAndCanBeClimbedOutOf()
        {
            foreach (var L in Districts())
                foreach (var s in L.pits)
                {
                    var r = L.RoomAt(Mathf.FloorToInt(s.rect.center.x / C), Mathf.FloorToInt(s.rect.center.y / C));
                    Assert.AreEqual(RoomFeature.Pit, r.feature, "a shaft outside a pit room");
                    var rc = r.rect;
                    // Off the walls by the rim (0.6 m), each other by a walkway (0.48 m).
                    Assert.That(s.rect.xMin - rc.xMin * C, Is.GreaterThanOrEqualTo(DepthPlan.PitWalk - 1e-3f));
                    Assert.That(rc.xMax * C - s.rect.xMax, Is.GreaterThanOrEqualTo(DepthPlan.PitWalk - 1e-3f));
                    if (Mathf.Abs(s.rect.xMin - rc.xMin * C - DepthPlan.PitWalk) < 1e-3f) Assert.Fail("a shaft only a walkway's half off the wall");
                    foreach (var o in L.pits)
                        if (!o.Equals(s) && o.rect.Overlaps(s.rect)) Assert.Fail("shafts overlap");
                    // Out of it with the mantle: in one pull, or two by the block in its corner.
                    if (s.ledge > 0f)
                    {
                        Assert.That(s.ledge, Is.LessThanOrEqualTo(Mantle), "the block");
                        Assert.That(s.depth - s.ledge, Is.LessThanOrEqualTo(Mantle), "from the block to the rim");
                    }
                    else Assert.That(s.depth, Is.LessThanOrEqualTo(Mantle), "a shaft with no block");
                    // No doorway opens over a shaft.
                    for (int x = rc.xMin; x < rc.xMax; x++)
                    for (int z = rc.yMin; z < rc.yMax; z++)
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + DX[k], nz = z + DZ[k];
                            if (L.InChunk(nx, nz) && L.roomOf[nx, nz] == r.id) continue;
                            if (!OfficeLayout.IsPassable(L.EdgeOf(x, z, k))) continue;
                            Assert.IsFalse(new Rect(x * C, z * C, C, C).Overlaps(s.rect), $"{L.coord}: doorway cell ({x}, {z}) over a shaft");
                        }
                }
        }

        [Test]
        public void CutCornersAreClear()
        {
            foreach (var L in Districts())
                foreach (var r in L.rooms)
                    for (int c = 0; c < 4; c++)
                    {
                        if ((r.chamfers & (1 << c)) == 0) continue;
                        var rc = r.rect;
                        int x = (c & 1) == 0 ? rc.xMin : rc.xMax - 1, z = (c & 2) == 0 ? rc.yMin : rc.yMax - 1;
                        Assert.AreEqual(EdgeKind.Wall, L.EdgeOf(x, z, (c & 1) == 0 ? 0 : 1), "outer wall along Z");
                        Assert.AreEqual(EdgeKind.Wall, L.EdgeOf(x, z, (c & 2) == 0 ? 2 : 3), "outer wall along X");
                        var cell = new Rect(x * C, z * C, C, C);
                        foreach (var p in L.props) Assert.IsFalse(cell.Contains(p.position), $"{p.kind} in a cut corner");
                        foreach (var f in L.fixtures) Assert.AreNotEqual(new Vector2Int(x, z), f.cell, "a light in a cut corner");
                        Assert.AreEqual(-1, L.StepSide(x, z), "steps in a cut corner");
                    }
        }

        [Test]
        public void ChunksCarryTheirStepsAndShafts()
        {
            // A chunk sliced from a plan has the plan's steps and every shaft that reaches into it, moved into its frame.
            foreach (int seed in Seeds)
            {
                var gen = new WorldGenerator(seed, WorldGenSettings.Defaults());
                for (int x = -2; x <= 2; x++)
                for (int z = -2; z <= 2; z++)
                {
                    var d = new Vector3Int(x, 0, z);
                    var plan = gen.District(d);
                    if (plan.pits.Count == 0 && plan.steps == null) continue;
                    int DC = WorldGenSettings.DistrictChunks, n = OfficeBiome.ChunkCells;
                    for (int cx = 0; cx < DC; cx++)
                    for (int cz = 0; cz < DC; cz++)
                    {
                        var S = gen.Chunk(new Vector3Int(x * DC + cx, 0, z * DC + cz));
                        for (int i = 0; i < n; i++)
                        for (int j = 0; j < n; j++)
                        {
                            Assert.AreEqual(plan.StepSide(cx * n + i, cz * n + j), S.StepSide(i, j), "steps");
                            Assert.AreEqual(plan.FloorAt(cx * n + i, cz * n + j), S.FloorAt(i, j), 1e-4f, "floor");
                        }
                        int want = 0;
                        var box = new Rect(cx * n * C, cz * n * C, n * C, n * C);
                        foreach (var p in plan.pits) if (p.rect.Overlaps(box)) want++;
                        Assert.AreEqual(want, S.pits.Count, "shafts in the chunk");
                    }
                }
            }
        }
    }
}
