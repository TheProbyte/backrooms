using System.IO;
using Backrooms.Interaction;
using Backrooms.Saves;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>World saves: the three slots on disk (in a temporary folder here, never the player's own saves).</summary>
    public class WorldSaveTests
    {
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "BackroomsSaveTests_" + System.Guid.NewGuid().ToString("N"));
            SaveSlots.FolderOverride = _dir;
        }

        [TearDown]
        public void TearDown()
        {
            SaveSlots.FolderOverride = null;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void SlotsStartEmpty()
        {
            for (int i = 0; i < SaveSlots.Count; i++)
            {
                Assert.IsFalse(SaveSlots.Exists(i));
                Assert.IsNull(SaveSlots.Load(i));
            }
        }

        [Test]
        public void ANewSaveGetsItsOwnSeedAndIsWrittenStraightAway()
        {
            var a = SaveSlots.Create(0);
            var b = SaveSlots.Create(1);
            Assert.IsTrue(SaveSlots.Exists(0) && SaveSlots.Exists(1));
            Assert.AreNotEqual(0, a.seed);
            Assert.AreNotEqual(a.seed, b.seed, "two new worlds are different worlds");
            Assert.AreEqual(a.seed, SaveSlots.Load(0).seed);
        }

        [Test]
        public void EverythingSavedComesBack()
        {
            var d = SaveSlots.Create(2);
            d.hasPlayer = true; d.playerPosition = new Vector3(12.5f, 9.03f, -40f); d.playerYaw = 270f; d.playerPitch = -10f; d.stance = 2;
            d.playSeconds = 1234.5; d.zone = "Poolrooms";
            d.props.Add(new PropData
            {
                id = 0xDE00000000070000ul.ToString("X16"), kind = (int)MovableKind.Sofa, renderMask = 0xFFFFFFFFL, proxy = "baseboard",
                parts = { new PartData { prefab = "sofaPrefabs/3", scale = Vector3.one } },
                position = new Vector3(1, 0, 2), rotation = Quaternion.Euler(0, 45, 0),
            });
            d.items.Add(new ItemData { id = "spraycan", position = new Vector3(3, 1, 4), rotation = Quaternion.identity, amount = 0.25f, held = true });
            d.placed.Add(new PlacedData { name = "Wall clock", position = new Vector3(5, 2, 6), rotation = Quaternion.Euler(0, 90, 0) });
            SaveSlots.Write(d, new byte[] { 1, 2, 3 });

            var r = SaveSlots.Load(2);
            Assert.AreEqual(d.seed, r.seed);
            Assert.IsTrue(r.hasPlayer);
            Assert.AreEqual(d.playerPosition, r.playerPosition);
            Assert.AreEqual(270f, r.playerYaw); Assert.AreEqual(-10f, r.playerPitch); Assert.AreEqual(2, r.stance);
            Assert.AreEqual(1234.5, r.playSeconds, 1e-6); Assert.AreEqual("Poolrooms", r.zone);
            Assert.AreEqual(1, r.props.Count);
            Assert.AreEqual("DE00000000070000", r.props[0].id);
            Assert.AreEqual(0xFFFFFFFFL, r.props[0].renderMask, "a full render mask survives as a number");
            Assert.AreEqual("sofaPrefabs/3", r.props[0].parts[0].prefab);
            Assert.AreEqual(0.25f, r.items[0].amount); Assert.IsTrue(r.items[0].held);
            Assert.AreEqual("Wall clock", r.placed[0].name);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, SaveSlots.ReadPaint(2));
        }

        [Test]
        public void EraseEmptiesTheSlot()
        {
            var d = SaveSlots.Create(1);
            SaveSlots.Write(d, new byte[] { 9 });
            SaveSlots.Erase(1);
            Assert.IsFalse(SaveSlots.Exists(1));
            Assert.IsNull(SaveSlots.ReadPaint(1));
        }

        [Test]
        public void AnUnreadableSaveCountsAsEmpty()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(SaveSlots.DataPath(0), "not json {");
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            Assert.IsNull(SaveSlots.Load(0));
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }
    }
}
