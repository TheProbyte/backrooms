using Backrooms.Destruction;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>
    /// Breakable walls: how a board breaks (the fixed pattern), what a blow knocks out and what falls after it, studs,
    /// which walls break, saving, and the light grid's view of a hole.
    /// </summary>
    public class WallDamageTests
    {
        const float C = OfficeBiome.CellSize;

        /// <summary>One chunk (0, 0): two offices side by side, a wall between them on the line x = 6.</summary>
        static OfficeLayout TwoRooms(bool crawlHole = false)
        {
            var L = new OfficeLayout(12, Vector2Int.zero);
            L.rooms.Add(new Room { id = 0, rect = new RectInt(0, 0, 6, 12), fullRect = new RectInt(0, 0, 6, 12), kind = RoomKind.Office, color = 1 });
            L.rooms.Add(new Room { id = 1, rect = new RectInt(6, 0, 6, 12), fullRect = new RectInt(6, 0, 6, 12), kind = RoomKind.Office, color = 2 });
            for (int x = 0; x < 12; x++) for (int z = 0; z < 12; z++) L.roomOf[x, z] = x < 6 ? 0 : 1;
            for (int z = 0; z < 12; z++) { L.vEdges[0, z] = EdgeKind.Wall; L.vEdges[12, z] = EdgeKind.Wall; L.vEdges[6, z] = EdgeKind.Wall; }
            for (int x = 0; x < 12; x++) { L.hEdges[x, 0] = EdgeKind.Wall; L.hEdges[x, 12] = EdgeKind.Wall; }
            if (crawlHole) L.portals.Add(new CrawlPortal { cell = new Vector2Int(6, 4), side = 0 });
            return L;
        }

        static readonly WallKey Key = new WallKey(true, 6, 5, 0);

        [SetUp]
        public void SetUp()
        {
            WallDamage.Clear();
            var L = TwoRooms();
            WallDamage.Layouts = c => c == Vector3Int.zero ? L : null;
            WallDamage.Pitch = 9f; WallDamage.Assets = null;
            // A world whose test wall has no batts and no pipe (those are by the dice), so each blow meets the boards.
            for (int seed = 1; seed < 500; seed++)
            {
                WallDamage.WorldSeed = seed;
                var p = WallDamage.Get(Key, true);
                bool plain = !p.model.insulated && !p.model.HasPipe;
                WallDamage.Clear();
                if (plain) break;
            }
        }

        [TearDown]
        public void TearDown() { WallDamage.Clear(); WallDamage.Layouts = null; }

        /// <summary>A blow from side 0 along +x at a height, centred on the panel.</summary>
        static StrikeResult Hit(float u, float v, float energy, bool fromSide1 = false)
        {
            var target = Key.ToWorld(u, v, 0f, WallDamage.Pitch);
            var dir = fromSide1 ? Vector3.left : Vector3.right;
            return WallDamage.Strike(Key, target - dir * 0.9f, dir, 1.2f, energy);
        }

        [Test]
        public void BoardsBreakIntoAFixedPatternThatTilesThem()
        {
            var a = new FracturePattern(77, WallSpec.U0, WallSpec.U1, 0f, 2.7f);
            var b = new FracturePattern(77, WallSpec.U0, WallSpec.U1, 0f, 2.7f);
            Assert.AreEqual(a.pieces.Count, b.pieces.Count, "the same dice, the same pieces");
            Assert.Greater(a.pieces.Count, 120); Assert.Less(a.pieces.Count, 300);
            float area = 0f;
            for (int i = 0; i < a.pieces.Count; i++)
            {
                var f = a.pieces[i];
                Assert.AreEqual(f.site, b.pieces[i].site);
                Assert.AreEqual(i, a.PieceAt(f.site), "a piece's own site lies in it");
                foreach (int j in f.neighbours) CollectionAssert.Contains(a.pieces[j].neighbours, i, "neighbours both ways");
                Assert.Less(f.radius, 0.2f, "no piece larger than a plate");
                area += f.area;
            }
            Assert.AreEqual((WallSpec.U1 - WallSpec.U0) * 2.7f, area, 0.03f, "the pieces cover the board, once");
        }

        [Test]
        public void ABlowKnocksOutWhatItMeetsAndLeavesTheRest()
        {
            var r = Hit(0.675f, 1.275f, 1f);
            Assert.AreEqual(StrikeKind.Board, r.kind);
            Assert.Greater(r.removed, 0);
            var p = WallDamage.Find(Key);
            Assert.IsTrue(WallDamage.Taken(Key), "the chunks stop drawing it");
            Assert.IsFalse(p.state.BoardAt(p.model, 0, new Vector2(0.675f, 1.275f)), "a hole where it struck");
            Assert.IsTrue(p.state.BoardAt(p.model, 0, new Vector2(0.6f, 0.3f)), "the board near the floor is still there");
            Assert.IsTrue(p.state.BoardAt(p.model, 1, new Vector2(0.675f, 1.275f)), "the far board is untouched");
            Assert.IsTrue(p.holed); Assert.IsFalse(p.seeThrough, "one board broken: nothing to see through yet");
            // The next blow goes on into the hole and knocks the far board out from behind.
            var r2 = Hit(0.675f, 1.275f, 1f);
            Assert.AreEqual(StrikeKind.BoardBack, r2.kind);
            Assert.IsTrue(p.seeThrough && r2.through, "now it can be seen through");
            Assert.IsTrue(WallDamage.MaskOpen(p.mask, 0.675f, 1.275f), "and light passes where it is open");
            Assert.IsFalse(WallDamage.MaskOpen(p.mask, 0.6f, 0.2f));
            // A weak tap only cracks.
            int before = p.state.RemovedCount(0);
            var tap = Hit(0.25f, 2.2f, 0.2f);
            Assert.AreEqual(before, p.state.RemovedCount(0), "a tap does not break through");
            Assert.IsTrue(tap.broke);
        }

        [Test]
        public void PiecesHeldByNothingFall()
        {
            var p = WallDamage.Get(Key, true);
            var board = p.model.boards[0];
            // Knock out everything round one piece in the middle of a bay: it is left hanging and falls.
            int island = board.PieceAt(new Vector2(0.6f, 1.4f));
            foreach (int j in board.pieces[island].neighbours) p.state.removed[0][j] = true;
            var r = Hit(0.6f, 2.5f, 0.2f);              // any blow makes the wall look again
            Assert.IsTrue(p.state.removed[0][island], "a piece with no neighbour and no screw falls");
            Assert.IsTrue(r.fallen.Exists(f => f.part == Fallen.Part.Board && f.index == island));
        }

        [Test]
        public void StudsDentThenBreak()
        {
            var p = WallDamage.Get(Key, true);
            Assert.IsTrue(p.model.steel, "an office wall: steel studs");
            // Clear the board in front of the first stud, then hit it.
            float u = p.model.studs[0];
            for (int i = 0; i < p.model.boards[0].pieces.Count; i++)
                if (Mathf.Abs(p.model.boards[0].pieces[i].centroid.x - u) < 0.2f) p.state.removed[0][i] = true;
            var first = Hit(u, 1.2f, 1f);
            Assert.AreEqual(StrikeKind.Stud, first.kind);
            Assert.IsFalse(first.studBroke, "one blow dents it");
            var second = Hit(u, 1.2f, 1f);
            Assert.IsTrue(second.studBroke, "the second tears it");
            Assert.AreEqual(2, p.state.studSegments[0].Count, "two lengths left, one each side of the break");
            Assert.IsFalse(p.state.StudAt(0, 1.2f));
        }

        [Test]
        public void OnlyOrdinaryWallsBreak()
        {
            Assert.IsTrue(WallDamage.Breakable(Key));
            Assert.IsFalse(WallDamage.Breakable(new WallKey(true, 3, 5, 0)), "an open edge is no wall");
            var L = TwoRooms(crawlHole: true);
            WallDamage.Layouts = c => c == Vector3Int.zero ? L : null;
            Assert.IsFalse(WallDamage.Breakable(new WallKey(true, 6, 4, 0)), "a wall with a crawl hole");
            L.style = ZoneStyle.Pool;
            Assert.IsFalse(WallDamage.Breakable(Key), "the Poolrooms' tiled walls");
            L.style = ZoneStyle.Office;
            // The posts at a panel's ends do not break.
            var r = Hit(0.02f, 1.2f, 1f);
            Assert.AreEqual(StrikeKind.Post, r.kind);
            Assert.IsFalse(r.broke);
        }

        [Test]
        public void BrokenWallsAreSavedAndPutBack()
        {
            Hit(0.6f, 1.3f, 1f); Hit(0.6f, 1.3f, 1f); Hit(0.3f, 0.6f, 1f);
            var p = WallDamage.Find(Key);
            var removed0 = (bool[])p.state.removed[0].Clone();
            var removed1 = (bool[])p.state.removed[1].Clone();
            int segs = p.state.studSegments[0].Count;
            var saved = WallDamage.Export();
            var json = JsonUtility.ToJson(new Saves.WorldSaveData { walls = saved });
            WallDamage.Clear();
            Assert.IsNull(WallDamage.Find(Key));
            var back = JsonUtility.FromJson<Saves.WorldSaveData>(json);
            WallDamage.Import(back.walls);
            var q = WallDamage.Find(Key);
            Assert.IsNotNull(q);
            CollectionAssert.AreEqual(removed0, q.state.removed[0]);
            CollectionAssert.AreEqual(removed1, q.state.removed[1]);
            Assert.AreEqual(segs, q.state.studSegments[0].Count);
            Assert.IsTrue(WallDamage.Taken(Key), "its chunks will leave it out");
            CollectionAssert.AreEqual(p.mask, q.mask);
        }

        [Test]
        public void LightGoesThroughAHoleAndNowhereElse()
        {
            var L = WallDamage.Layouts(Vector3Int.zero);
            LightGridMath.OccupancyLookup occ = (x, z, s) =>
            {
                if (s != 0 || x < 0 || z < 0 || x >= 12 || z >= 12) return 0u;
                return LightGridMath.Encode(L, x, z, (dx, dz) => null);
            };
            Vector3 hole = Key.ToWorld(0.675f, 1.275f, 0f, 9f);
            Vector3 P = hole + new Vector3(-1.5f, 0.6f, 0f), Q = hole + new Vector3(1.5f, -0.6f, 0f);
            Assert.IsFalse(LightGridMath.Visible(P, Q, occ, 9f), "a whole wall stops it");
            Hit(0.675f, 1.275f, 1f); Hit(0.675f, 1.275f, 1f);
            uint o = occ(6, 5, 0);
            Assert.AreNotEqual(0u, o & LightGridMath.WestHoled, "the wall is flagged as holed");
            Assert.AreEqual(LightGridMath.Wall, LightGridMath.WestKind(o), "and is still a wall");
            Assert.IsTrue(LightGridMath.Visible(P, Q, occ, 9f), "through the hole");
            Vector3 low = Key.ToWorld(0.6f, 0.25f, 0f, 9f);
            Assert.IsFalse(LightGridMath.Visible(low + Vector3.left * 1.5f, low + Vector3.right * 1.5f, occ, 9f), "not through the wall below it");
            // The table the shaders read: the record, and its cell (the wall is the west edge of cell (6, 5)) points at it.
            var list = new System.Collections.Generic.List<(bool, int, int, int, uint[], uint[], float)>();
            var p = WallDamage.Find(Key);
            list.Add((true, 6, 5, 0, p.mask, p.maskAny, p.openFraction));
            const int n = LightGrid.OccN;
            var cells = new uint[n * n * 2];
            var table = LightGridMath.BuildHoleTable(list, null, cells, n, new Vector2Int(-8, -8));
            LightGridMath.HoleKey(true, 6, 5, 0, out uint k0, out uint k1);
            Assert.AreEqual(k0, table[0]); Assert.AreEqual(k1, table[1]);
            Assert.AreEqual(1u, cells[5 * n + 6] & 0xFFFFu, "cell (6, 5): its west wall is record 0 (+1)");
            int col = Mathf.FloorToInt(0.675f / WallSpec.MaskCell), row = Mathf.FloorToInt(1.275f / WallSpec.MaskCell), bit = row * 8 + col;
            Assert.AreNotEqual(0u, table[2 + (bit >> 5)] & (1u << (bit & 31)), "the block the shaders test is open");
            Assert.AreNotEqual(0u, table[7 + (bit >> 5)] & (1u << (bit & 31)), "and has an opening");
            int foot = 1 * 8 + col;
            Assert.AreEqual(0u, table[7 + (foot >> 5)] & (1u << (foot & 31)), "the whole wall near the floor stops even shadow-mapped lights");
        }

        [Test]
        public void ABrokenPanelIsLeftOutOfItsChunk()
        {
            var assets = UnityEditor.AssetDatabase.LoadAssetAtPath<OfficeBiomeAssets>("Assets/Backrooms/Settings/OfficeBiome_Assets.asset");
            Assume.That(assets != null, "biome assets");
            MaterialMaps.Ensure(assets);
            var L = WallDamage.Layouts(Vector3Int.zero);
            int Boxes()
            {
                var b = new OfficeChunkBuilder(assets);
                b.BuildData(L);
                var f = typeof(OfficeChunkBuilder).GetField("_boxes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var list = (System.Collections.IList)f.GetValue(b);
                // Wall boxes on the line x = 6 that cover the panel's middle.
                int n = 0;
                foreach (var o in list)
                {
                    var (c, s) = ((Vector3, Vector3))o;
                    if (Mathf.Abs(c.x - 6 * C) < 0.01f && s.y > 2f && Mathf.Abs(c.z - 5.5f * C) < s.z * 0.5f && c.y < 4.5f && s.z > 0.5f) n++;
                }
                return n;
            }
            Assert.AreEqual(1, Boxes(), "the whole wall's collider runs across the panel");
            Hit(0.6f, 1.3f, 1f);
            Assert.AreEqual(0, Boxes(), "broken, the panel has its own colliders");
        }
    }
}
