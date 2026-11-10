using System.Collections.Generic;
using Backrooms.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>Nails driven in with the hammer: kept, found, pulled out, saved and loaded.</summary>
    public class NailTests
    {
        [TearDown]
        public void Clean()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            NailWork.Clear();
            var root = GameObject.Find("Nails");
            if (root) Object.DestroyImmediate(root);
        }

        [Test]
        public void NailsInTheBuildingAreFoundPulledAndSaved()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            NailWork.Clear();
            NailWork.Add(new Vector3(1f, 1.2f, 2f), Vector3.back, null);
            NailWork.Add(new Vector3(1.3f, 1.2f, 2f), Vector3.back, null);
            Assert.AreEqual(2, NailWork.All.Count);
            int i = NailWork.Near(new Vector3(1.01f, 1.21f, 2f), 0.03f, out var head, out var dir, out var prop);
            Assert.AreEqual(0, i, "the nearer one");
            Assert.IsNull(prop, "in the building, not a prop");
            Assert.AreEqual(Vector3.back, dir);
            Assert.AreEqual(-1, NailWork.Near(new Vector3(2f, 1.2f, 2f), 0.03f, out _, out _, out _), "none there");

            var saved = NailWork.Export();
            Assert.AreEqual(2, saved.Count);
            Assert.AreEqual("", saved[0].prop);
            NailWork.Import(new List<Saves.NailData>(saved));
            Assert.AreEqual(2, NailWork.All.Count, "loaded back");
            NailWork.Remove(0);
            Assert.AreEqual(1, NailWork.All.Count, "pulled out");
            Assert.AreEqual(new Vector3(1.3f, 1.2f, 2f), NailWork.All[0].position);
            NailWork.Import(null);
            Assert.AreEqual(0, NailWork.All.Count, "a new world has none");
        }

        [Test]
        public void TheBoxCountsItsNailsAndKeepsTheCountAsItsAmount()
        {
            var go = new GameObject("NailsBox");
            try
            {
                var box = go.AddComponent<Nails>();
                Assert.AreEqual(Nails.Full, box.count);
                box.count = 20;
                float amount = box.Amount;
                box.count = 0;
                box.Amount = amount;
                Assert.AreEqual(20, box.count, "saved as the item's amount and read back");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
