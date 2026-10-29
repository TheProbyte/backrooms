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
                if (r.feature != RoomFeature.Sunken || r.ramp != 0) continue;       // (a ramp: RampsRiseToTheFloorAtTheirFarEnd)
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
                        // The floors on the doorway's line (a ramp's end meets the wing at the wing's floor).
                        float ex = x + 0.5f + DX[s] * 0.5f, ez = z + 0.5f + DZ[s] * 0.5f;
                        // (A ramp's lip, at most DepthPlan.MaxLip, needs no steps.)
                        if (Mathf.Abs(L.FloorOf(L.RoomAt(nx, nz), ex, ez) - r.floor) <= DepthPlan.MaxLip + 1e-3f) continue;
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
        public void RampsRiseToTheFloorAtTheirFarEnd()
        {
            int ramps = 0;
            foreach (var L in Districts())
            foreach (var r in L.rooms)
            {
                if (r.ramp == 0) continue;
                ramps++;
                var rc = r.rect;
                // A hallway into a wing, or a sloping room (the whole room, rampCells 0).
                Assert.IsTrue(r.kind == RoomKind.Hallway || r.rampCells == 0, "a ramp that is neither");
                bool alongX = r.ramp <= 2;
                float length = (r.rampCells > 0 ? r.rampCells : (alongX ? rc.width : rc.height)) * C;
                Assert.That(-r.floor / length, Is.LessThanOrEqualTo(DepthPlan.MaxSlope + 1e-4f), "slope");
                Assert.That(r.rampStart + r.rampCells, Is.LessThanOrEqualTo(alongX ? rc.width : rc.height), "the ramp fits its hallway");
                for (int x = rc.xMin; x < rc.xMax; x++)
                for (int z = rc.yMin; z < rc.yMax; z++)
                {
                    Assert.AreEqual(-1, L.StepSide(x, z), "steps on a ramp");
                    for (int s = 0; s < 4; s++)
                    {
                        int nx = x + DX[s], nz = z + DZ[s];
                        if (!L.InChunk(nx, nz) || L.roomOf[nx, nz] == r.id || !OfficeLayout.IsPassable(L.EdgeOf(x, z, s))) continue;
                        if (L.StepSide(nx, nz) == (s ^ 1)) continue;          // the wing's steps come up to the ramp's flat part
                        // Every doorway meets a floor at the ramp's own height there.
                        float ex = x + 0.5f + DX[s] * 0.5f, ez = z + 0.5f + DZ[s] * 0.5f;
                        Assert.AreEqual(L.FloorOf(r, ex, ez), L.FloorOf(L.RoomAt(nx, nz), ex, ez), DepthPlan.MaxLip + 1e-3f, $"{L.coord}: a ramp's doorway at ({x}, {z}) side {s}");
                    }
                }
            }
            Assert.That(ramps, Is.GreaterThan(0), "ramps");
        }

        [Test]
        public void CeilingsComeDownToCrawlSlotsWithNoLightsUnder()
        {
            int drops = 0;
            foreach (var L in Districts())
            foreach (var r in L.rooms)
            {
                if (r.ceilDrop == 0) continue;
                drops++;
                Assert.AreEqual(RoomKind.Hallway, r.kind);
                var rc = r.rect;
                // Along the hallway, not across it.
                Assert.AreEqual(rc.width > rc.height, r.ceilDrop <= 2, "comes down along the hallway");
                float lowest = float.MaxValue;
                for (int x = rc.xMin; x < rc.xMax; x++)
                for (int z = rc.yMin; z < rc.yMax; z++)
                {
                    float low = L.CeilingLowAt(x, z);
                    lowest = Mathf.Min(lowest, low);
                    if (low < r.ceiling - 0.05f)
                    {
                        foreach (var f in L.fixtures) Assert.AreNotEqual(new Vector2Int(x, z), f.cell, "a light under a ceiling coming down");
                        // No doorway in the hallway's sides there (it is taller than the ceiling: you saw above it).
                        for (int s = 0; s < 4; s++)
                        {
                            if ((s < 2) == (rc.width > rc.height)) continue;
                            int nx = x + DX[s], nz = z + DZ[s];
                            if (L.InChunk(nx, nz) && L.roomOf[nx, nz] == r.id) continue;
                            Assert.IsFalse(OfficeLayout.IsPassable(L.EdgeOf(x, z, s)), $"{L.coord}: a doorway under a ceiling coming down at ({x}, {z})");
                        }
                    }
                }
                Assert.AreEqual(1.35f, lowest, 0.01f, "the low end");
            }
            Assert.That(drops, Is.GreaterThan(0), "ceilings coming down");
        }

        [Test]
        public void StairsToNowhereAndMezzaninesKeepClear()
        {
            int stairs = 0, decks = 0;
            foreach (var L in Districts())
            {
                foreach (var st in L.nowhere)
                {
                    stairs++;
                    Assert.AreEqual(EdgeKind.Wall, L.EdgeOf(st.cell.x, st.cell.y, st.side), "they climb into a wall");
                    Assert.That(st.steps * DepthPlan.StairTread, Is.LessThanOrEqualTo(2f * C), "two cells deep at most");
                    var cells = new[] { st.cell, st.cell - new Vector2Int(DX[st.side], DZ[st.side]) };
                    foreach (var c in cells)
                    {
                        Assert.AreEqual(L.roomOf[st.cell.x, st.cell.y], L.roomOf[c.x, c.y], "in one room");
                        var cell = new Rect(c.x * C, c.y * C, C, C);
                        foreach (var p in L.props) Assert.IsFalse(cell.Contains(p.position), $"{p.kind} on stairs to nowhere");
                    }
                }
                foreach (var m in L.mezzanines)
                {
                    decks++;
                    var r = L.RoomAt(m.start.x, m.start.y);
                    Assert.That(r.ceiling - m.height, Is.GreaterThanOrEqualTo(2.2f - 1e-3f), "headroom on the deck");
                    for (int i = 0; i < m.length; i++)
                    {
                        var c = m.Cell(i);
                        Assert.AreEqual(r.id, L.roomOf[c.x, c.y], "the strip is in its room");
                        foreach (var f in L.fixtures) Assert.AreNotEqual(c, f.cell, "a light in a mezzanine's strip");
                        if (!m.IsStair(i)) continue;
                        for (int s = 0; s < 4; s++)
                        {
                            int nx = c.x + DX[s], nz = c.y + DZ[s];
                            if (L.InChunk(nx, nz) && L.roomOf[nx, nz] == r.id) continue;
                            Assert.IsFalse(OfficeLayout.IsPassable(L.EdgeOf(c.x, c.y, s)), "a doorway behind the stair");
                        }
                    }
                }
            }
            Assert.That(stairs, Is.GreaterThan(0), "stairs to nowhere");
            Assert.That(decks, Is.GreaterThan(0), "mezzanines");
        }

        [Test]
        public void PitShaftsLeaveWalkwaysAndDropIntoTheDark()
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
                    // Bottomless to look at: all the same depth, too deep to climb, no block (PitRescue brings you up).
                    Assert.AreEqual(DepthPlan.PitDepth, s.depth, 1e-4f, "depth");
                    Assert.That(s.ledge, Is.EqualTo(0f), "a block in a shaft");
                    Assert.That(s.depth, Is.GreaterThan(Mantle), "a shaft shallow enough to climb out of");
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
