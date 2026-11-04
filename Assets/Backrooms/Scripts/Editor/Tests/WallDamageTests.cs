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
        public void SteelStudsHoldAndLeaveRoomForAPerson()
        {
            var p = WallDamage.Get(Key, true);
            Assert.IsTrue(p.model.steel, "an office wall: steel studs");
            Assert.AreEqual(1, p.model.studs.Length, "one steel stud a panel");
            // The player is 0.6 m across: the gap on one side of the stud is wide enough, clear of the post's flanges.
            float u = p.model.studs[0], half = WallSpec.StudWidth(true) * 0.5f;
            float gap = Mathf.Max(u - half - (WallSpec.U0 + WallSpec.SteelFlange), WallSpec.U1 - WallSpec.SteelFlange - (u + half));
            Assert.GreaterOrEqual(gap, 0.7f, "a person fits beside the stud");
            // Clear the board in front of the stud, then hit it, again and again: it rings and holds.
            for (int i = 0; i < p.model.boards[0].pieces.Count; i++)
                if (Mathf.Abs(p.model.boards[0].pieces[i].centroid.x - u) < 0.2f) p.state.removed[0][i] = true;
            for (int k = 0; k < 4; k++)
            {
                var r = Hit(u, 1.2f, 1.5f);
                Assert.AreEqual(StrikeKind.Stud, r.kind);
                Assert.IsFalse(r.studBroke, "steel does not give way");
            }
            Assert.AreEqual(1, p.state.studSegments[0].Count, "still one whole length");
        }

        [Test]
        public void WoodStudsSplitAtTheThirdBlow()
        {
            var L = TwoRooms();
            L.biomeName = "Hallways";
            WallDamage.Layouts = c => c == Vector3Int.zero ? L : null;
            var p = WallDamage.Get(Key, true);
            Assert.IsFalse(p.model.steel, "the older hallways: wood studs");
            Assert.AreEqual(2, p.model.studs.Length, "wood at 400 mm");
            float u = p.model.studs[0];
            for (int i = 0; i < p.model.boards[0].pieces.Count; i++)
                if (Mathf.Abs(p.model.boards[0].pieces[i].centroid.x - u) < 0.2f) p.state.removed[0][i] = true;
            Assert.IsFalse(Hit(u, 1.2f, 1f).studBroke, "one blow");
            Assert.IsFalse(Hit(u, 1.2f, 1f).studBroke, "two");
            Assert.IsTrue(Hit(u, 1.2f, 1f).studBroke, "the third splits it");
            Assert.AreEqual(2, p.state.studSegments[0].Count, "two lengths left, one each side of the break");
            Assert.IsFalse(p.state.StudAt(0, 1.2f));
        }

        [Test]
        public void APieceOverAStudWithoutAScrewFalls()
        {
            var p = WallDamage.Get(Key, true);
            var m = p.model; var board = m.boards[0];
            float u = m.studs[0];
            // The pieces with a screw in them (every 0.3 m up the stud), and one over the stud's line without one.
            var screwed = new System.Collections.Generic.HashSet<int>();
            for (float v = WallDamage.ScrewFirst; v < m.height - 0.05f; v += WallDamage.ScrewSpacing) screwed.Add(board.PieceAt(new Vector2(u, v)));
            int chip = -1, held = -1;
            float half = WallSpec.StudWidth(m.steel) * 0.5f;
            for (int i = 0; i < board.pieces.Count && (chip < 0 || held < 0); i++)
            {
                var f = board.pieces[i];
                if (f.bounds.xMax < u - half || f.bounds.xMin > u + half || f.bounds.yMin < 0.3f || f.bounds.yMax > m.height - 0.3f) continue;
                if (screwed.Contains(i)) { if (held < 0) held = i; }
                else if (chip < 0) chip = i;
            }
            Assume.That(chip >= 0 && held >= 0, "this pattern has both");
            // Cut both free of their neighbours.
            foreach (int j in board.pieces[chip].neighbours) p.state.removed[0][j] = true;
            foreach (int j in board.pieces[held].neighbours) p.state.removed[0][j] = true;
            p.state.removed[0][chip] = p.state.removed[0][held] = false;
            Hit(0.6f, 2.5f, 0.2f);                       // any blow makes the wall look again
            Assert.IsTrue(p.state.removed[0][chip], "a chip over the stud with no screw in it falls");
            Assert.IsFalse(p.state.removed[0][held], "a piece with a screw in it stays");
        }

        [Test]
        public void ASavedWallsLoosePiecesAreFoundOnTheFloor()
        {
            var p = WallDamage.Get(Key, true);
            var board = p.model.boards[0];
            int island = board.PieceAt(new Vector2(0.6f, 1.4f));
            Hit(0.6f, 2.5f, 1f);                         // taken over, so it is saved
            foreach (int j in board.pieces[island].neighbours) p.state.removed[0][j] = true;
            p.state.removed[0][island] = false;          // left floating, as an older version could leave it
            var saved = WallDamage.Export();
            WallDamage.Clear();
            WallDamage.Import(saved);
            var q = WallDamage.Find(Key);
            Assert.IsTrue(q.state.removed[0][island], "nothing holds it: it is not in the wall");
            if (board.pieces[island].area >= 0.0035f)
            {
                var r = q.rubble.Find(x => x.part == Fallen.Part.Board && x.side == 0 && x.index == island);
                Assert.AreEqual(island, r.index, "it lies at the foot of the wall");
                Assert.Less(r.position.y, 0.05f, "on the floor");
            }
        }

        [Test]
        public void AnOlderSaveNamingPartsTheWallNoLongerHasStillLoads()
        {
            Hit(0.6f, 1.3f, 1f);
            var saved = WallDamage.Export();
            // Saved when steel walls had two studs and batts: a length of the second stud and a batt lie as rubble.
            saved[0].rubble.Add(new WallDamage.Rubble { part = Fallen.Part.Stud, index = 1, span = new Vector2(0.5f, 1.2f), position = Vector3.one, rotation = Quaternion.identity });
            saved[0].rubble.Add(new WallDamage.Rubble { part = Fallen.Part.Insulation, index = 3, position = Vector3.one, rotation = Quaternion.identity });
            int before = saved[0].rubble.Count;
            WallDamage.Clear();
            WallDamage.Import(saved);
            var q = WallDamage.Find(Key);
            Assert.AreEqual(1, q.model.studs.Length);
            Assert.AreEqual(before - 2, q.rubble.Count, "what the wall no longer has is left out");
            Assert.IsTrue(q.rubble.TrueForAll(r => WallDamage.Fits(q.model, r)));
        }

        [Test]
        public void RubbleWedgedInAWallFallsToTheFloor()
        {
            Hit(0.6f, 1.3f, 1f);
            var p = WallDamage.Find(Key);
            p.rubble.Clear();
            // Settled in the cavity at chest height (as an older version could leave it), and one on the floor beside the wall.
            var wedged = new WallDamage.Rubble { part = Fallen.Part.Board, side = 0, index = 5, position = Key.ToWorld(0.6f, 1.3f, 0f, WallDamage.Pitch), rotation = Quaternion.identity };
            var floor = new WallDamage.Rubble { part = Fallen.Part.Board, side = 0, index = 6, position = Key.ToWorld(0.4f, 0.006f, -0.4f, WallDamage.Pitch), rotation = Quaternion.identity };
            Assert.IsTrue(WallDamage.Lodged(Key, wedged.position)); Assert.IsFalse(WallDamage.Lodged(Key, floor.position));
            p.rubble.Add(wedged); p.rubble.Add(floor);
            Assert.IsTrue(WallDamage.DropLodged(p));
            Assert.Less(p.rubble[0].position.y, 0.05f, "on the floor now");
            Assert.Greater(Mathf.Abs(Key.ToLocal(p.rubble[0].position, WallDamage.Pitch).z), WallSpec.T * 0.5f, "out of the wall");
            Assert.AreEqual(floor.position, p.rubble[1].position, "what lay on the floor stays");
        }

        [Test]
        public void TidyingSweepsCrumbsAndKeepsTheBiggest()
        {
            Hit(0.6f, 1.3f, 1f);
            var p = WallDamage.Find(Key); var board = p.model.boards[0];
            p.rubble.Clear();
            for (int i = 0; i < board.pieces.Count; i++)
                p.rubble.Add(new WallDamage.Rubble { part = Fallen.Part.Board, side = 0, index = i, position = Vector3.zero, rotation = Quaternion.identity });
            p.rubble.Add(new WallDamage.Rubble { part = Fallen.Part.Stud, index = 0, span = new Vector2(0.2f, 0.6f), position = Vector3.zero, rotation = Quaternion.identity });
            float biggest = 0f; int big = -1;
            for (int i = 0; i < board.pieces.Count; i++) if (board.pieces[i].area > biggest) { biggest = board.pieces[i].area; big = i; }
            int bigger = 0;
            for (int i = 0; i < board.pieces.Count; i++) if (board.pieces[i].area >= WallDamage.CrumbArea) bigger++;
            WallDamage.Tidy(p);
            Assert.AreEqual(Mathf.Min(WallDamage.KeepRubble, bigger + 1), p.rubble.Count, "only the few biggest stay");
            Assert.IsTrue(p.rubble.TrueForAll(r => r.part != Fallen.Part.Board || board.pieces[r.index].area >= WallDamage.CrumbArea), "no crumbs left");
            Assert.IsTrue(p.rubble.Exists(r => r.part == Fallen.Part.Board && r.index == big), "the biggest piece stays");
            Assert.IsFalse(WallDamage.Tidy(p), "nothing more to tidy");
        }

        [Test]
        public void TheBoardsColliderFollowsThePiecesLeft()
        {
            var p = WallDamage.Get(Key, true);
            var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
            var origin = Key.Origin(WallDamage.Pitch);
            PanelGeometry.BoardCollider(v, t, p.model, p.state, WallDamage.Pitch, origin);
            int whole = t.Count;
            Assert.Greater(whole, 0);
            foreach (var x in v)
            {
                var l = Key.ToLocal(x + origin, WallDamage.Pitch);
                Assert.LessOrEqual(Mathf.Abs(l.z), WallSpec.T * 0.5f + 1e-4f, "inside the wall's thickness");
                Assert.GreaterOrEqual(Mathf.Abs(l.z), WallSpec.T * 0.5f - PanelGeometry.BoardColliderDepth - 1e-4f, "no deeper than the collider's depth");
            }
            Hit(0.6f, 1.3f, 1f); Hit(0.6f, 1.3f, 1f);
            PanelGeometry.BoardCollider(v, t, p.model, p.state, WallDamage.Pitch, origin);
            Assert.Less(t.Count, whole, "fewer pieces, less collider");
        }

        [Test]
        public void RubbleLandingOnRubbleLiesOnTopOfIt()
        {
            Hit(0.6f, 1.3f, 1f);
            var p = WallDamage.Find(Key);
            p.rubble.Clear();
            var at = Key.ToWorld(0.6f, 0.007f, -0.3f, WallDamage.Pitch);
            for (int i = 0; i < 4; i++)
                WallDamage.Settle(Key, new WallDamage.Rubble { part = Fallen.Part.Board, side = 0, index = 40 + i, position = at, rotation = Quaternion.identity });
            for (int i = 0; i < p.rubble.Count; i++)
                for (int j = 0; j < i; j++)
                    Assert.Greater(Mathf.Abs(p.rubble[i].position.y - p.rubble[j].position.y), WallSpec.Board * 0.9f, "no two plates at one height (they flickered through each other)");
        }

        [Test]
        public void ACordMeetsABrokenWallAsASlabWithItsHoles()
        {
            var p = WallDamage.Get(Key, true);
            Assert.IsTrue(WallSlabs.Solid(p, 0.6f, 1.3f), "a whole wall is solid");
            Hit(0.6f, 1.3f, 1f); Hit(0.6f, 1.3f, 1f);
            Assert.IsFalse(WallSlabs.Solid(p, 0.6f, 1.3f), "open where both boards are gone");
            Assert.IsTrue(WallSlabs.Solid(p, 0.6f, 0.3f), "solid below the hole");
            Assert.IsTrue(WallSlabs.Solid(p, 0.03f, 1.3f), "the posts");
            Assert.IsTrue(WallSlabs.NearestOpen(p, 0.6f, 0.3f, 1.5f, out var open), "an opening above");
            Assert.IsFalse(WallSlabs.Solid(p, open.x, open.y));
            Assert.Greater(open.y, 0.3f);
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
