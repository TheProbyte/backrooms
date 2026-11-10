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
            Assert.AreEqual(WorkOrders.Goal.Tape, all[0].goal);
            Assert.AreEqual(WorkOrders.Stages, WorkOrders.StageUnlocks.Length);
            foreach (var o in all) { Assert.Greater(o.target, 0, o.title); Assert.Greater(o.pay, 0, o.title); }
        }

        [Test]
        public void EveryUnlockIsUsed()
        {
            // Each key a stage gives opens something at the counter or the improvements board.
            var keys = WorkOrders.StageUnlocks.SelectMany(k => k).ToList();
            var used = new[] { "counter", "handtruck", "truck", "worklamp", "chute", "tallshelf", "generator", "repairkit", "conveyor", "floodlight", "rack",
                               "rates1", "rates2", "wings", "poolrooms" };
            foreach (var k in keys) CollectionAssert.Contains(used, k);
            Assert.AreEqual(keys.Count, keys.Distinct().Count());
        }

        [Test]
        public void LockedThingsSayWhatOpensThem()
        {
            // Every unlock names the stage that opens it, on the boards and in the skip menu (2026-11-09).
            Assert.AreEqual("AFTER ONBOARDING", WorkOrders.LockedText("counter"));
            Assert.AreEqual("AFTER TIER 1", WorkOrders.LockedText("truck"));
            Assert.AreEqual("AFTER TIER 3", WorkOrders.LockedText("rates1"));
            Assert.AreEqual("AFTER THE QUARTER", WorkOrders.LockedText("poolrooms"));
            Assert.AreEqual("FINISH TIER 2'S WORK ORDERS", WorkOrders.FinishText("generator"));
            foreach (var k in WorkOrders.StageUnlocks.SelectMany(u => u)) Assert.GreaterOrEqual(WorkOrders.StageOf(k), 0, k);
            Assert.AreEqual(-1, WorkOrders.StageOf("nothing"));
            // A skip button per stage, each its own label.
            var labels = Enumerable.Range(1, WorkOrders.Stages).Select(WorkOrders.SkipLabel).ToList();
            Assert.AreEqual("SKIP ONBOARDING", labels[0]);
            Assert.AreEqual("SKIP TO TIER 2", labels[1]);
            Assert.AreEqual("SKIP TO THE FIRST QUARTER", labels[3]);
            Assert.AreEqual("UNLOCK EVERYTHING", labels[4]);
            Assert.AreEqual(labels.Count, labels.Distinct().Count());
        }

        [Test]
        public void BatchPaySplitsAndLateness()
        {
            // A third in when stored, the rest when back; rounded to $5.
            Contracts.Price(0, 6, 4f, 0f, 0, out int intake, out int completion);
            Assert.AreEqual(0, (intake + completion) % 5);
            Assert.That(intake, Is.InRange((intake + completion) / 3 - 5, (intake + completion) / 3 + 5));
            // Better rates pay more.
            Contracts.Price(0, 6, 4f, 0f, 2, out int i2, out int c2);
            Assert.Greater(i2 + c2, intake + completion);
            // The window: half the storage time, at least two minutes.
            Assert.AreEqual(120f, Contracts.WindowFor(60f));
            Assert.AreEqual(300f, Contracts.WindowFor(600f));
            // Inside the window, full pay; after it, sliding to half and no lower.
            Assert.AreEqual(1f, Contracts.LateFactor(100f, 120f));
            Assert.Less(Contracts.LateFactor(120f + Contracts.LateSlide * 0.5f, 120f), 1f);
            Assert.AreEqual(Contracts.LateFloor, Contracts.LateFactor(120f + Contracts.LateSlide * 3f, 120f), 1e-4f);
            // Scuffs take 5% each, never below 30% in all.
            Assert.AreEqual(90, Contracts.CompletionPay(100, 0f, 120f, 2));
            Assert.AreEqual(30, Contracts.CompletionPay(100, 9999f, 120f, 40));
            // Settled with boxes missing: paid for the share that came back; nothing back, nothing paid.
            Assert.AreEqual(Contracts.CompletionPay(100, 999f, 120f, 0), Contracts.SettlePay(100, 999f, 120f, 0, 4, 4));
            Assert.AreEqual(Mathf.RoundToInt(Contracts.CompletionPay(100, 999f, 120f, 0) * 0.75f), Contracts.SettlePay(100, 999f, 120f, 0, 3, 4));
            Assert.AreEqual(0, Contracts.SettlePay(100, 999f, 120f, 0, 0, 4));
        }

        [Test]
        public void MinutesAndSeconds()
        {
            Assert.AreEqual("3:05", Contracts.MinSec(185f));
            Assert.AreEqual("0:00", Contracts.MinSec(-4f));
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
