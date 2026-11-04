using System.Collections.Generic;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>
    /// Level 0's rarer things (2026-11-03): water (damp carpet, runs down walls, leaks, mould), old tubes, fire gear,
    /// thermostats, vents, fountains, filled windows, door hardware, floor boxes, cords, partitions and the lone chair.
    /// They must all turn up somewhere, stay rare, and stand where they belong.
    /// </summary>
    public class LeftoversTests
    {
        static readonly string[] Kinds =
        {
            "damp", "wall_water", "leak", "mould", "tube_ends", "tube_starter", "extinguisher", "pull_station", "strobe",
            "door_closer", "kick_plate", "door_wedge", "corner_guard", "thermostat", "wall_vent", "fountain", "floor_box",
            "cable_stubs", "partitions", "window_filled", "wooden_chair", "cord",
        };

        static OfficeBiomeAssets Assets()
        {
            var a = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            Assert.IsNotNull(a);
            MaterialMaps.Ensure(a);
            FurnitureMaterials.Ensure(a);
            FittingMaterials.Ensure(a);
            DoorModels.Ensure(a);
            Assert.IsTrue(FittingMaterials.Ready, "the fittings' materials");
            return a;
        }

        [Test]
        public void TheRareThingsTurnUpAndStayRare()
        {
            var a = Assets();
            var counts = new Dictionary<string, int>();
            int chunks = 0;
            foreach (int seed in new[] { 288152760, 1234 })
            {
                Destruction.WallDamage.WorldSeed = seed;
                var gen = new WorldGenerator(seed, WorldGenSettings.Defaults());
                for (int x = -6; x <= 6; x++)
                for (int z = -6; z <= 6; z++)
                {
                    var c = new Vector2Int(x, z);
                    var L = gen.Chunk(c);
                    if (L.style == ZoneStyle.Pool) continue;
                    var b = new OfficeChunkBuilder(a);
                    b.BuildData(L, gen.Neighbours(c));
                    chunks++;
                    foreach (var s in b.Sightings)
                    {
                        counts.TryGetValue(s.kind, out int n);
                        counts[s.kind] = n + 1;
                    }
                }
            }
            Destruction.WallDamage.WorldSeed = 0;
            var report = new System.Text.StringBuilder($"{chunks} chunks:");
            foreach (var k in Kinds) { counts.TryGetValue(k, out int n); report.Append($" {k} {n}"); }
            Debug.Log("[LeftoversTests] " + report);
            foreach (var k in Kinds)
            {
                counts.TryGetValue(k, out int n);
                Assert.Greater(n, 0, $"{k}: turns up ({report})");
                Assert.Less(n / (float)chunks, 0.35f, $"{k}: rare, not on every other chunk ({report})");
            }
        }

        [Test]
        public void PartitionsAndChairsStandInEmptyOffices()
        {
            int partitions = 0, chairs = 0;
            float C = OfficeBiome.CellSize;
            foreach (int seed in new[] { 1, 1234, 288152760 })
            {
                var gen = new WorldGenerator(seed, WorldGenSettings.Defaults());
                for (int x = -3; x <= 3; x++)
                for (int z = -3; z <= 3; z++)
                {
                    var L = gen.District(new Vector3Int(x, 0, z));
                    if (L.style == ZoneStyle.Pool) continue;
                    for (int i = 0; i < L.props.Count; i++)
                    {
                        var p = L.props[i];
                        if (p.kind != PropKind.Partition && p.kind != PropKind.WoodenChair) continue;
                        if (p.kind == PropKind.Partition) partitions++; else chairs++;
                        var cell = new Vector2Int(Mathf.FloorToInt(p.position.x / C), Mathf.FloorToInt(p.position.y / C));
                        var r = L.RoomAt(cell.x, cell.y);
                        Assert.AreEqual(RoomStyle.Backrooms, DistrictGenerator.StyleOf(r), $"{p.kind} in {L.coord}: in Level 0's own rooms");
                        Assert.AreNotEqual(RoomKind.Hallway, r.kind, $"{p.kind} in {L.coord}: not in a hallway");
                        // Clear of everything that is not one of them.
                        var foot = p.kind == PropKind.WoodenChair ? new Vector2(p.size.x, p.size.z)
                                 : (p.variant & 1) != 0 ? new Vector2(p.size.x, p.size.y) : new Vector2(p.size.x, p.size.z);
                        for (int j = 0; j < L.props.Count; j++)
                        {
                            var q = L.props[j];
                            if (j == i || q.kind == PropKind.WallClock || q.kind == PropKind.Partition || q.kind == PropKind.WoodenChair) continue;
                            Assert.IsFalse(DistrictGenerator.Overlaps(p.position, foot, p.yaw, q.position, new Vector2(Mathf.Max(q.size.x, 0.5f), Mathf.Max(q.size.z, 0.5f)), q.yaw),
                                $"{p.kind} in {L.coord} at {p.position}: under a {q.kind}");
                        }
                    }
                }
            }
            Assert.Greater(partitions, 5, "partitions turn up");
            Assert.Greater(chairs, 3, "chairs turn up");
        }
    }
}
