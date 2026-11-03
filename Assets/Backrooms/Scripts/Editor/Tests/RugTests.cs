using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The living rooms' road rugs (DistrictGenerator.PlaceRugs): where they lie.</summary>
    public class RugTests
    {
        static readonly int[] Seeds = { 1, 1234, 288152760 };
        const float C = OfficeBiome.CellSize;

        [Test]
        public void RugsLieInTheOpenFloorOfLivingRooms()
        {
            int rugs = 0;
            foreach (int seed in Seeds)
            {
                var gen = new WorldGenerator(seed, WorldGenSettings.Defaults());
                for (int x = -2; x <= 2; x++)
                for (int z = -2; z <= 2; z++)
                {
                    var L = gen.District(new Vector3Int(x, 0, z));
                    if (L.style == ZoneStyle.Pool) continue;
                    for (int i = 0; i < L.props.Count; i++)
                    {
                        var p = L.props[i];
                        if (p.kind != PropKind.Rug) continue;
                        rugs++;
                        var cell = new Vector2Int(Mathf.FloorToInt(p.position.x / C), Mathf.FloorToInt(p.position.y / C));
                        var r = L.RoomAt(cell.x, cell.y);
                        Assert.AreEqual(RoomStyle.LivingRoom, DistrictGenerator.StyleOf(r), $"rug in {L.coord}: in a living room");
                        // Its corners: inside its room, 0.4 m or more from the walls.
                        var rot = Quaternion.Euler(0f, p.yaw, 0f);
                        for (int k = 0; k < 4; k++)
                        {
                            var c = rot * new Vector3(((k & 1) == 0 ? -0.5f : 0.5f) * p.size.x, 0f, ((k & 2) == 0 ? -0.5f : 0.5f) * p.size.z);
                            var w = new Vector2(p.position.x + c.x, p.position.y + c.z);
                            Assert.IsTrue(w.x > r.rect.xMin * C + 0.4f && w.x < r.rect.xMax * C - 0.4f && w.y > r.rect.yMin * C + 0.4f && w.y < r.rect.yMax * C - 0.4f,
                                $"rug in {L.coord} at {p.position}: clear of the walls of {r.rect}");
                        }
                        // Nothing else stands on it.
                        for (int j = 0; j < L.props.Count; j++)
                        {
                            if (j == i || L.props[j].kind == PropKind.WallClock) continue;
                            var q = L.props[j];
                            Assert.IsFalse(DistrictGenerator.Overlaps(p.position, new Vector2(p.size.x, p.size.z), p.yaw, q.position,
                                new Vector2(Mathf.Max(q.size.x, 0.5f), Mathf.Max(q.size.z, 0.5f)), q.yaw), $"rug in {L.coord} at {p.position}: under a {q.kind}");
                        }
                    }
                }
            }
            Assert.Greater(rugs, 40, "rugs turn up");
        }
    }
}
