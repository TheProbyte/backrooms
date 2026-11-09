using System.Linq;
using Backrooms.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The storage game's rules (2026-11-08, Docs/GameDesign.md sections 7, 8 and 10).</summary>
    public class StorageTests
    {
        [Test]
        public void ShelfDecksMatchTheGeneratedShelving()
        {
            // The standard kit shelf is the generated 1.1 x 2.0 shelving: four decks, 0.1 m up, the top one at the top.
            var t = ShelfTypes.All[ShelfTypes.Standard];
            Assert.AreEqual(4, t.levels);
            Assert.AreEqual(0.1f, ShelfTypes.DeckTop(t, 0), 1e-4f);
            Assert.AreEqual(t.size.y - 0.02f, ShelfTypes.DeckTop(t, t.levels - 1), 1e-4f);
            // Two of the contract boxes (0.5 x 0.36 x 0.4) side by side on a deck, standing between decks.
            Assert.GreaterOrEqual(t.size.x, 1.0f);
            Assert.Greater(ShelfTypes.Pitch(t) - 0.04f, 0.36f);
        }

        [Test]
        public void BrokenIsItsOwnBit()
        {
            int v = ShelfTypes.Tall | ShelfTypes.Broken;
            Assert.IsTrue(ShelfTypes.IsBroken(v));
            Assert.AreEqual("TALL SHELVING", ShelfTypes.Get(v).name);
            Assert.IsFalse(ShelfTypes.IsBroken(ShelfTypes.Rack));
        }

        [Test]
        public void LightBands()
        {
            Assert.AreEqual(0, Shelves.Band(1f));
            Assert.AreEqual(1, Shelves.Band(0.7f));
            Assert.AreEqual(2, Shelves.Band(0.3f));
            Assert.AreEqual(3, Shelves.Band(0.05f));
        }

        [Test]
        public void KitIdsKeepTheirKind()
        {
            foreach (Kits.Kind k in System.Enum.GetValues(typeof(Kits.Kind)))
            {
                ulong id = Kits.Id(k, 12345);
                Assert.IsTrue(Improvements.IsKit(id), k.ToString());
                Assert.AreEqual(k, Kits.KindOf(id));
            }
            // The chute's kits (Improvements.KitId) are kind 0.
            Assert.AreEqual(Kits.Kind.Chute, Kits.KindOf(Improvements.KitId(3)));
            Assert.IsTrue(Machines.Is(Machines.Id(Kits.Kind.Conveyor, 7)));
            Assert.AreEqual(Kits.Kind.Conveyor, Machines.KindOf(Machines.Id(Kits.Kind.Conveyor, 7)));
            Assert.IsFalse(Shelves.IsShelfId(Kits.Id(Kits.Kind.Shelf, 1)));
        }

        [Test]
        public void WorkOrdersRunInStages()
        {
            var all = WorkOrders.All;
            // In stage order, every stage has an order, onboarding first, the gate last.
            for (int i = 1; i < all.Length; i++) Assert.GreaterOrEqual(all[i].stage, all[i - 1].stage);
            for (int s = 0; s < WorkOrders.Stages; s++) Assert.IsTrue(all.Any(o => o.stage == s), "stage " + s);
            Assert.AreEqual(WorkOrders.Goal.ClockIn, all[0].goal);
            Assert.AreEqual(WorkOrders.Stages, WorkOrders.StageUnlocks.Length);
            foreach (var o in all) { Assert.Greater(o.target, 0, o.title); Assert.Greater(o.pay, 0, o.title); }
        }

        [Test]
        public void EveryUnlockIsUsed()
        {
            // Each key a stage gives opens something at the counter or the improvements board.
            var keys = WorkOrders.StageUnlocks.SelectMany(k => k).ToList();
            var used = new[] { "counter", "truck", "worklamp", "chute", "tallshelf", "generator", "repairkit", "conveyor", "floodlight", "rack",
                               "overtime1", "overtime2", "overtime3", "backup1", "backup2", "rentreview1", "rentreview2", "wings", "poolrooms" };
            foreach (var k in keys) CollectionAssert.Contains(used, k);
            Assert.AreEqual(keys.Count, keys.Distinct().Count());
        }

        [Test]
        public void GeneratorLoad()
        {
            Assert.AreEqual(1, Machines.LoadOf(Kits.Kind.Conveyor));
            Assert.AreEqual(2, Machines.LoadOf(Kits.Kind.Floodlight));
            Assert.GreaterOrEqual(Machines.Capacity, 4);
        }
    }
}
