using System.Collections.Generic;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The stairs / elevator wing of atriums: it must be reachable, consistent between storeys, and gentle to climb.</summary>
    public class AtriumAnnexTests
    {
        static List<LandmarkPlacement> Atriums(WorldGenerator gen, int want)
        {
            var list = new List<LandmarkPlacement>();
            for (int r = 0; r < 60 && list.Count < want; r++)
            for (int x = -r; x <= r && list.Count < want; x++)
            for (int z = -r; z <= r && list.Count < want; z++)
            {
                if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != r) continue;
                foreach (var l in gen.LandmarksIn(new Vector2Int(x, z))) if (l.kind == LandmarkKind.Atrium) list.Add(l);
            }
            return list;
        }

        /// <summary>Cells reachable from a start cell through passable edges, staying inside a rectangle.</summary>
        static HashSet<Vector2Int> Flood(OfficeLayout L, Vector2Int start, RectInt within)
        {
            var seen = new HashSet<Vector2Int> { start };
            var q = new Queue<Vector2Int>(); q.Enqueue(start);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                for (int side = 0; side < 4; side++)
                {
                    var n = new Vector2Int(c.x + (side == 0 ? -1 : side == 1 ? 1 : 0), c.y + (side == 2 ? -1 : side == 3 ? 1 : 0));
                    if (!within.Contains(n) || seen.Contains(n) || !OfficeLayout.IsPassable(L.EdgeOf(c.x, c.y, side))) continue;
                    seen.Add(n); q.Enqueue(n);
                }
            }
            return seen;
        }

        static bool Arched(OfficeLayout L, Vector2Int c, int side) =>
            side == 0 ? L.archV[c.x, c.y] : side == 1 ? L.archV[c.x + 1, c.y] : side == 2 ? L.archH[c.x, c.y] : L.archH[c.x, c.y + 1];

        [Test]
        public void EveryAtriumHasAWayUp()
        {
            var gen = new WorldGenerator(1234, WorldGenSettings.Defaults());
            float pitch = gen.Settings.storeyPitch;
            var variants = new HashSet<int>();
            foreach (var p in Atriums(gen, 24))
            {
                Assert.IsTrue(AtriumAnnex.TryGet(p, pitch, out var a), $"atrium at {p.district} has no connector");
                variants.Add(p.connectors);
                Assert.IsTrue(p.rect.Contains(p.core.min) && p.rect.Contains(p.core.max - Vector2Int.one) && p.rect.Contains(p.annex.min) && p.rect.Contains(p.annex.max - Vector2Int.one), "core and annex lie inside the reserved rectangle");
                Assert.GreaterOrEqual(a.length, AtriumAnnex.MinLength);

                var ground = gen.District(new Vector3Int(p.district.x, 0, p.district.y));
                var upper = gen.District(new Vector3Int(p.district.x, 1, p.district.y));
                // From the atrium's ring, inside the landmark only, one can reach every piece of the wing.
                var ringG = a.f.Cell(3, AtriumAnnex.Depth);        // first ring cell next to the wing
                var reachG = Flood(ground, ringG, p.rect);
                var reachU = Flood(upper, ringG, p.rect);
                var lobbyCell = a.f.Cell(2, 3);
                Assert.IsTrue(reachG.Contains(lobbyCell), $"{p.district}: lobby not reachable from the atrium's ring");
                if (a.stairs)
                {
                    Assert.IsTrue(reachG.Contains(a.f.Cell(0, 0)), $"{p.district}: foot of the stairs not reachable");
                    Assert.IsTrue(reachU.Contains(a.f.Cell(a.length - 1, 0)), $"{p.district}: the landing at the top of the stairs is not reachable from the upper ring");
                    Assert.AreEqual(AnnexRole.StairHall, ground.RoomAt(a.f.Cell(5, 0).x, a.f.Cell(5, 0).y).role);
                    Assert.IsTrue(upper.Has(a.f.Cell(5, 0).x, a.f.Cell(5, 0).y, CellFlags.NoFloor) && ground.Has(a.f.Cell(5, 0).x, a.f.Cell(5, 0).y, CellFlags.OpenAbove), "the stair void matches on both storeys");
                    Assert.IsFalse(upper.Has(a.f.Cell(a.length - 1, 0).x, a.f.Cell(a.length - 1, 0).y, CellFlags.NoFloor), "the landing has a floor");
                }
                if (a.elevator)
                {
                    var d = a.DoorLobbyCell;
                    var cell = a.f.Cell(d.u, d.v);
                    int side = AtriumAnnex.Frame.SideOf(a.f.Dir(1, 0));
                    Assert.AreEqual(EdgeKind.DoorHeader, ground.EdgeOf(cell.x, cell.y, side), "elevator door, ground storey");
                    Assert.AreEqual(EdgeKind.DoorHeader, upper.EdgeOf(cell.x, cell.y, side), "elevator door, upper storey");
                    Assert.IsFalse(Arched(ground, cell, side) || Arched(upper, cell, side), "the elevator door is never arched");
                    var shaft = a.f.Cell(a.length - 1, 3);
                    Assert.IsTrue(ground.Has(shaft.x, shaft.y, CellFlags.OpenAbove) && upper.Has(shaft.x, shaft.y, CellFlags.NoFloor), "the shaft runs through both storeys");
                    Assert.IsTrue(reachG.Contains(shaft) && reachU.Contains(shaft), "the shaft is reachable through its door on both storeys");
                }
            }
            Assert.GreaterOrEqual(variants.Count, 2, "different atriums roll different connectors");
        }

        [Test]
        public void StairsAreGentleAndMeetTheCeilingAbove()
        {
            const float pitch = 9f;
            var f = AtriumAnnex.Frame.Make(0, new RectInt(0, 0, 16, 5));
            var a = new AtriumAnnex.Plan { f = f, length = 16, stairs = true, pitch = pitch };
            Assert.LessOrEqual(a.Riser, 0.2f, "riser");
            Assert.GreaterOrEqual(a.Tread, 0.26f, "tread");
            Assert.AreEqual(pitch, a.Steps * a.Riser, 0.001f);
            Assert.AreEqual(pitch, a.StepTop(a.StairEnd - 0.01f), 0.001f, "the last step is at the upper floor's height");
            Assert.AreEqual(0f, a.StepTop(AtriumAnnex.EntryFlat - 0.01f), "flat entry");
            Assert.AreEqual(OfficeBiome.CeilingHeight + pitch, a.CeilingAt(a.StairEnd), 0.001f, "the slope meets the upper storey's ceiling");
            // Headroom stays above 2.4 m anywhere on the stairs.
            for (float u = AtriumAnnex.EntryFlat; u < a.StairEnd; u += 0.05f) Assert.GreaterOrEqual(a.CeilingAt(u) - a.StepTop(u), 2.4f, $"headroom at {u}");
        }
    }
}
