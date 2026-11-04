using System.Collections.Generic;
using System.Threading;
using Backrooms.Net;
using Backrooms.Saves;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>Co-op (2026-11-04): the messages, the copy of the world sent on joining, and the connection itself.</summary>
    public class NetTests
    {
        [Test]
        public void MessagesReadBackAsWritten()
        {
            var w = new NetWriter();
            w.Byte(7); w.Bool(true); w.Short(-1234); w.Int(-56789); w.UInt(4000000000u); w.Long(-1L << 40); w.ULong(0x9E3779B97F4A0003UL);
            w.Float(3.25f); w.Double(1e10 + 0.5); w.Vec(new Vector3(1, -2, 3.5f)); w.Rot(Quaternion.Euler(10, 20, 30));
            w.Str("HÉLLO — 25.1.2.3"); w.Str(""); w.Bytes(new byte[] { 1, 2, 3 }); w.Bytes(null);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(7, r.Byte());
            Assert.IsTrue(r.Bool());
            Assert.AreEqual(-1234, r.Short());
            Assert.AreEqual(-56789, r.Int());
            Assert.AreEqual(4000000000u, r.UInt());
            Assert.AreEqual(-1L << 40, r.Long());
            Assert.AreEqual(0x9E3779B97F4A0003UL, r.ULong());
            Assert.AreEqual(3.25f, r.Float());
            Assert.AreEqual(1e10 + 0.5, r.Double());
            Assert.AreEqual(new Vector3(1, -2, 3.5f), r.Vec());
            Assert.Less(Quaternion.Angle(Quaternion.Euler(10, 20, 30), r.Rot()), 0.01f);
            Assert.AreEqual("HÉLLO — 25.1.2.3", r.Str());
            Assert.AreEqual("", r.Str());
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, r.Bytes());
            Assert.IsNull(r.Bytes());
            Assert.AreEqual(0, r.Left);
            Assert.Throws<System.FormatException>(() => r.Int());               // past the end: refused, not garbage
        }

        [Test]
        public void PackedBlocksComeBack()
        {
            var data = new byte[70000];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 7 == 0 ? i : 0);
            CollectionAssert.AreEqual(data, NetPack.Unpack(NetPack.Pack(data)));
            Assert.AreEqual("a world", NetPack.UnpackString(NetPack.PackString("a world")));
        }

        [Test]
        public void NamesAreCleaned()
        {
            Assert.AreEqual("SAM", NetSession.Clean("sam"));
            Assert.AreEqual("PLAYER", NetSession.Clean("   "));
            Assert.AreEqual("A-B_C.D 2SCRIPT", NetSession.Clean("a-b_c.d 2<script>"));   // (the brackets go)
            Assert.AreEqual(16, NetSession.Clean("ABCDEFGHIJKLMNOPQRSTUVWXYZ").Length);
        }

        [Test]
        public void TheWorldCopyReadsBack()
        {
            var save = new WorldSaveData { seed = 288152760, playerPosition = new Vector3(9, 0, 9), hasPlayer = true };
            save.lightsOff.AddRange(new[] { 0, 0, 0, 3 });
            save.doorKeys.Add("-1,0,0,1,5,5"); save.doorAngles.Add(83.4f);
            var snap = new NetSnapshot
            {
                save = save, settings = Generation.WorldGenSettings.Defaults(), paint = new byte[] { 1, 2, 3, 4 }, marks = new byte[] { 9, 9 },
                spawn = new Vector3(1, 2, 3), spawnYaw = 45f, mintFrom = (1u << 24) + 5, hostTime = 12.5, returning = true,
            };
            snap.items.Add(new NetItemData { id = "spraycan", net = 3, mode = (int)ItemMode.World, amount = 0.6f, position = new Vector3(1, 0, 1) });
            snap.items.Add(new NetItemData { id = "chalk", net = 4, mode = (int)ItemMode.Hand, holder = 0 });
            snap.mine.Add(new NetItemData { id = "backpack", net = 9, mode = (int)ItemMode.Worn, worn = true });
            snap.mine.Add(new NetItemData { id = "tape", net = 10, mode = (int)ItemMode.Bag, bagNet = 9, bag = 1 });
            var bytes = snap.Write();
            var back = NetSnapshot.Read(bytes, 0);
            Assert.IsNotNull(back);
            Assert.AreEqual(288152760, back.save.seed);
            Assert.AreEqual(-1, back.save.slot, "a joined world is never a slot of this player's");
            CollectionAssert.AreEqual(save.lightsOff, back.save.lightsOff);
            Assert.AreEqual(83.4f, back.save.doorAngles[0]);
            Assert.IsNotNull(back.settings);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, back.paint);
            CollectionAssert.AreEqual(new byte[] { 9, 9 }, back.marks);
            Assert.AreEqual(2, back.items.Count);
            Assert.AreEqual(3u, back.items[0].net);
            Assert.AreEqual(0.6f, back.items[0].amount);
            Assert.AreEqual((int)ItemMode.Hand, back.items[1].mode);
            Assert.AreEqual(2, back.mine.Count);
            Assert.AreEqual(9u, back.mine[1].bagNet);
            Assert.IsTrue(back.returning);
            Assert.AreEqual((1u << 24) + 5, back.mintFrom);
            Assert.AreEqual(new Vector3(1, 2, 3), back.spawn);
        }

        [Test]
        public void TheWorldCopyRefusesGarbage()
        {
            Assert.IsNull(NetSnapshot.Read(new byte[] { 1, 2, 3 }, 0));
        }

        /// <summary>A host and a client on this machine: they connect, a reliable message goes each way, the frequent ones
        /// go by UDP once both ends hear each other, and the host's goodbye arrives before the line closes.</summary>
        [Test]
        public void HostAndClientTalk()
        {
            const int port = 47731;
            var host = NetTransport.Host(port, out var err);
            Assert.IsNotNull(host, err);
            var client = NetTransport.Connect("127.0.0.1", port, out err);
            Assert.IsNotNull(client, err);
            var he = new List<NetEvent>(); var ce = new List<NetEvent>();
            var hostGot = new List<byte[]>(); var clientGot = new List<byte[]>();
            int hostLink = -1; bool clientConnected = false, clientDropped = false; string why = null;
            void Pump(int ms)
            {
                var until = System.DateTime.Now.AddMilliseconds(ms);
                while (System.DateTime.Now < until)
                {
                    host?.Poll(he);
                    foreach (var e in he) { if (e.kind == NetEvent.Kind.Connected) hostLink = e.link; else if (e.kind == NetEvent.Kind.Data) hostGot.Add(e.data); }
                    client?.Poll(ce);
                    foreach (var e in ce)
                    {
                        if (e.kind == NetEvent.Kind.Connected) clientConnected = true;
                        else if (e.kind == NetEvent.Kind.Data) clientGot.Add(e.data);
                        else if (e.kind == NetEvent.Kind.Disconnected) { clientDropped = true; why = e.reason; }
                    }
                    Thread.Sleep(5);
                }
            }
            try
            {
                Pump(500);
                Assert.IsTrue(clientConnected, "the client connected");
                Assert.GreaterOrEqual(hostLink, 0, "the host accepted it");

                client.Send(0, new byte[] { 30, 1, 2 }, true);
                host.Send(hostLink, new byte[] { 31, 3, 4, 5 }, true);
                Pump(300);
                Assert.IsTrue(hostGot.Exists(d => d[0] == 30 && d.Length == 3), "reliable, client to host");
                Assert.IsTrue(clientGot.Exists(d => d[0] == 31 && d.Length == 4), "reliable, host to client");

                // UDP: both ends say they hear the other within a second or so.
                Pump(1500);
                Assert.IsTrue(client.Link(0).UdpOut, "the host hears the client's datagrams");
                Assert.IsTrue(host.Link(hostLink).UdpOut, "the client hears the host's datagrams");
                hostGot.Clear(); clientGot.Clear();
                for (int i = 0; i < 5; i++) { client.Send(0, new byte[] { 20, (byte)i }, false); host.Send(hostLink, new byte[] { 22, (byte)i }, false); }
                Pump(300);
                Assert.Greater(hostGot.FindAll(d => d[0] == 20).Count, 0, "unreliable, client to host");
                Assert.Greater(clientGot.FindAll(d => d[0] == 22).Count, 0, "unreliable, host to client");

                // The goodbye, then the end: read in that order.
                clientGot.Clear();
                host.Send(hostLink, new byte[] { 3, 9, 9 }, true);
                host.CloseGracefully(150);
                host = null;
                Pump(500);
                Assert.IsTrue(clientGot.Exists(d => d[0] == 3), "the goodbye arrived");
                Assert.IsTrue(clientDropped, "the client saw the line close");
                Assert.AreEqual("THE HOST CLOSED THE CONNECTION", why);
            }
            finally
            {
                host?.Dispose();
                client?.Dispose();
            }
        }

        [Test]
        public void ATakenPortSaysSo()
        {
            const int port = 47732;
            var a = NetTransport.Host(port, out var err);
            Assert.IsNotNull(a, err);
            try
            {
                var b = NetTransport.Host(port, out err);
                Assert.IsNull(b);
                StringAssert.Contains("47732", err);
                b?.Dispose();
            }
            finally { a.Dispose(); }
        }

        [Test]
        public void AJoinToNowhereFails()
        {
            Assert.IsNull(NetTransport.Connect("not an address at all", 7777, out var err));
            StringAssert.Contains("NOT AN ADDRESS", err);
        }
    }
}
