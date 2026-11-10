using System.IO;
using Backrooms.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The F2 item tuning: which settings an item shows, changing one for every item of its kind, saving it.</summary>
    public class ItemTuningTests
    {
        string _file;
        readonly System.Collections.Generic.List<GameObject> _made = new System.Collections.Generic.List<GameObject>();

        // In Edit Mode Unity does not call Awake on a new component, where an item takes its tuning in the game: do it here.
        T Made<T>(T item) where T : WorldItem { _made.Add(item.gameObject); if (!Application.isPlaying) ItemTuning.Apply(item); return item; }

        [SetUp]
        public void SetUp()
        {
            _file = Path.Combine(Path.GetTempPath(), "ItemTuningTest_" + System.Guid.NewGuid().ToString("N") + ".json");
            ItemTuning.PathOverride = _file;
            ItemTuning.Load();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _made) if (go) Object.DestroyImmediate(go);
            _made.Clear();
            ItemTuning.PathOverride = null;
            ItemTuning.Load();
            if (File.Exists(_file)) File.Delete(_file);
        }

        [Test]
        public void AnItemsSettingsAreItsOwnFieldsAndWeight()
        {
            var names = ItemTuning.SettingsOf(typeof(Sledgehammer)).ConvertAll(s => s.name);
            CollectionAssert.IsSubsetOf(new[] { "breakRadius", "tapRadius", "power", "fullWindUp", "armReach", "mass" }, names);
            CollectionAssert.DoesNotContain(names, "itemId", "the item base's own fields are not settings");
            var can = ItemTuning.SettingsOf(typeof(SprayCan)).ConvertAll(s => s.name);
            CollectionAssert.Contains(can, "spread");
            CollectionAssert.DoesNotContain(can, "paint", "the paint left is the can's state, not a setting");
            Assert.AreEqual("Break radius", ItemTuning.Nice("breakRadius"));
        }

        [Test]
        public void ATunedSettingAppliesToEveryItemOfItsKindAndIsSaved()
        {
            var a = Made(Sledgehammer.Create(Vector3.zero, Quaternion.identity));
            float built = a.breakRadius;
            var setting = ItemTuning.SettingsOf(typeof(Sledgehammer)).Find(s => s.name == "breakRadius");
            var weight = ItemTuning.SettingsOf(typeof(Sledgehammer)).Find(s => s.IsWeight);
            ItemTuning.Change(typeof(Sledgehammer), setting, 0.41f);
            ItemTuning.Change(typeof(Sledgehammer), weight, 3.5f);
            Assert.AreEqual(0.41f, a.breakRadius, 1e-6f, "the one already out");
            Assert.AreEqual(3.5f, a.GetComponent<Rigidbody>().mass, 1e-6f);
            var b = Made(Sledgehammer.Create(Vector3.one, Quaternion.identity));
            Assert.AreEqual(0.41f, b.breakRadius, 1e-6f, "a new one");
            Assert.AreEqual(built, ItemTuning.Default(typeof(Sledgehammer), "breakRadius"), 1e-6f, "the built-in value is remembered");

            ItemTuning.Save();
            Assert.IsTrue(File.Exists(_file));
            ItemTuning.Load();
            Assert.IsTrue(ItemTuning.TryTuned(typeof(Sledgehammer), "breakRadius", out float v));
            Assert.AreEqual(0.41f, v, 1e-6f, "saved and read back");
            var c = Made(Sledgehammer.Create(Vector3.one * 2f, Quaternion.identity));
            Assert.AreEqual(0.41f, c.breakRadius, 1e-6f, "after loading");

            ItemTuning.Reset(typeof(Sledgehammer));
            Assert.AreEqual(built, c.breakRadius, 1e-6f, "reset puts the built-in value back");
            Assert.IsFalse(ItemTuning.TryTuned(typeof(Sledgehammer), "breakRadius", out _));
        }
    }
}
