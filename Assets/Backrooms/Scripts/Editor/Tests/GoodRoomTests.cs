using System.Collections.Generic;
using Backrooms.Generation;
using Backrooms.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>Rooms worth making a base (SpecialRooms, Docs/BaseRoomsPlan.md), power (PowerGrid) and homes.</summary>
    public class GoodRoomTests
    {
        static readonly int[] Seeds = { 1, 1234, 553893173 };
        const float C = OfficeBiome.CellSize;

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

        static bool In(Room r, Vector2 p) => r.rect.Contains(new Vector2Int(Mathf.FloorToInt(p.x / C), Mathf.FloorToInt(p.y / C)));

        [Test]
        public void EachKindTurnsUpWithOneWayIn()
        {
            var kinds = new HashSet<GoodRoom>();
            int rooms = 0;
            foreach (var L in Districts())
                foreach (var r in L.rooms)
                {
                    if (r.special == GoodRoom.None) continue;
                    rooms++;
                    kinds.Add(r.special);
                    int ways = 0;
                    for (int x = r.rect.xMin; x < r.rect.xMax; x++)
                    for (int z = r.rect.yMin; z < r.rect.yMax; z++)
                    for (int s = 0; s < 4; s++)
                    {
                        int nx = x + (s == 0 ? -1 : s == 1 ? 1 : 0), nz = z + (s == 2 ? -1 : s == 3 ? 1 : 0);
                        if (r.rect.Contains(new Vector2Int(nx, nz))) continue;
                        var e = L.EdgeOf(x, z, s);
                        if (!OfficeLayout.IsPassable(e)) continue;
                        ways++;
                        Assert.AreEqual(EdgeKind.DoorHeader, e, $"{r.special} in {L.coord}: its doorway has a header (for its door)");
                        Assert.IsTrue(L.InChunk(nx, nz), $"{r.special} in {L.coord}: never on the district edge");
                    }
                    Assert.AreEqual(1, ways, $"{r.special} in {L.coord} at {r.rect}: one way in");
                    Assert.IsTrue(r.live, $"{r.special}: its outlets have power");
                    Assert.AreEqual(LightMode.Dark, r.lights, $"{r.special}: its tubes are dead");
                }
            Assert.Greater(rooms, 20, "good rooms turn up");
            foreach (GoodRoom k in System.Enum.GetValues(typeof(GoodRoom)))
                if (k != GoodRoom.None) Assert.IsTrue(kinds.Contains(k), k + " turns up");
        }

        [Test]
        public void TheDistrictStaysConnected()
        {
            foreach (var L in Districts())
            {
                var seen = DistrictGenerator.Flood(L);
                for (int x = 0; x < L.N; x++)
                for (int z = 0; z < L.N; z++)
                    if (L.RoomAt(x, z).Walkable) Assert.IsTrue(seen[x, z], $"district {L.coord}: cell {x},{z} reachable");
            }
        }

        [Test]
        public void TheyAreFurnishedAndPowered()
        {
            foreach (var L in Districts())
                foreach (var r in L.rooms)
                {
                    if (r.special == GoodRoom.None) continue;
                    int outlets = 0, props = 0, panels = 0, desks = 0, counters = 0, shelves = 0;
                    foreach (var p in L.plates) if (!p.isSwitch && r.rect.Contains(p.cell)) { outlets++; Assert.IsTrue(p.live, "a good room's outlet is live"); }
                    foreach (var p in L.props)
                    {
                        if (!In(r, p.position)) continue;
                        props++;
                        if (p.kind == PropKind.BreakerPanel) panels++;
                        if (p.kind == PropKind.Desk) desks++;
                        if (p.kind == PropKind.Counter) counters++;
                        if (p.kind == PropKind.Shelving) shelves++;
                    }
                    Assert.GreaterOrEqual(outlets, 2, $"{r.special} in {L.coord}: two outlets at least");
                    Assert.Greater(props, 0, $"{r.special} in {L.coord}: furnished");
                    if (r.special == GoodRoom.JanitorCloset) Assert.AreEqual(1, panels, "a janitor's closet has the breaker panel");
                    if (r.special == GoodRoom.SecurityOffice) Assert.AreEqual(1, desks, "a security office has its desk");
                    if (r.special == GoodRoom.BreakRoom) Assert.AreEqual(1, counters, "a break room has its counter");
                    if (r.special == GoodRoom.StockRoom) Assert.GreaterOrEqual(shelves, 3, "a stock room is shelved");
                }
        }

        [Test]
        public void MostRoomsHaveNoPowerOfTheirOwn()
        {
            int rooms = 0, live = 0, lamps = 0, lit = 0;
            foreach (var L in Districts())
            {
                foreach (var r in L.rooms) { if (!r.Walkable) continue; rooms++; if (r.live) live++; }
                foreach (var p in L.props)
                {
                    if (p.kind != PropKind.FloorLamp) continue;
                    lamps++;
                    int cx = Mathf.Clamp(Mathf.FloorToInt(p.position.x / C), 0, L.N - 1), cz = Mathf.Clamp(Mathf.FloorToInt(p.position.y / C), 0, L.N - 1);
                    if (p.lit) { lit++; Assert.IsTrue(L.RoomAt(cx, cz).live, "a lamp is lit only where its outlet has power"); }
                }
            }
            float share = live / (float)rooms;
            Assert.Less(share, 0.3f, "most rooms are dead");
            Assert.Greater(share, 0.05f, "a few have power");
            Assert.Less(lit, lamps, "not every lamp is lit");
        }

        [Test]
        public void BreakersAndHomesAreSavedAndLoaded()
        {
            PowerGrid.Clear();
            var c = PowerGrid.CircuitOf(new Vector3Int(-5, 0, 9));
            Assert.AreEqual(new Vector3Int(-2, 0, 2), c, "a chunk's district (4 chunks a side)");
            PowerGrid.Set(c, true);
            var flat = PowerGrid.Save();
            PowerGrid.Clear();
            Assert.IsFalse(PowerGrid.On(c));
            PowerGrid.Load(flat);
            Assert.IsTrue(PowerGrid.On(c));
            PowerGrid.Clear();

            Homes.Clear();
            var k = new Homes.Key { district = new Vector2Int(-1, 3), storey = 0, room = 42 };
            Assert.IsTrue(Homes.Add(k));
            var hf = Homes.Save();
            Homes.Clear();
            Homes.Load(hf);
            Assert.IsTrue(Homes.IsHome(k));
            Assert.AreEqual(1, Homes.Count);
            Homes.Clear();
        }
    }
}
