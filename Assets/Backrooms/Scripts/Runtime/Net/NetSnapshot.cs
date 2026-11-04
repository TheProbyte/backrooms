using System;
using System.Collections.Generic;
using Backrooms.CustomRooms;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Net
{
    /// <summary>Where a shared item is: lying in the world, in someone's hand, pocket or bag, or on their back.</summary>
    public enum ItemMode { World = 0, Hand = 1, Pocket = 2, Worn = 3, Bag = 4, Deck = 5 }

    /// <summary>An item as it goes over the network: the save's own record of it, its id, and where it is.</summary>
    [Serializable]
    public class NetItemData : ItemData
    {
        public uint net;
        public int holder = -1;          // the player carrying it (Hand, Pocket, Worn), or -1
        public int mode;                 // ItemMode
        public uint bagNet;              // Bag: the backpack's id
    }

    [Serializable] public class NetItemList { public List<NetItemData> list = new List<NetItemData>(); }

    /// <summary>
    /// The joining player's copy of the host's world (2026-11-04): its save as it is now (moved furniture, broken walls,
    /// lights, doors, what was taken), its paint and chalk, its generation settings and custom rooms (the building is
    /// generated from them), every item with its id and who holds it, and, for a player coming back, what they were
    /// carrying and where they were.
    /// </summary>
    public sealed class NetSnapshot
    {
        public WorldSaveData save;
        public WorldGenSettings settings;
        public List<RoomBlueprint> rooms = new List<RoomBlueprint>();
        public byte[] paint, marks;
        public List<NetItemData> items = new List<NetItemData>();
        /// <summary>This player's own things (a guest coming back: what they were carrying when they left).</summary>
        public List<NetItemData> mine = new List<NetItemData>();
        public bool returning;
        /// <summary>Where this player starts (next to the host, or where they left).</summary>
        public Vector3 spawn;
        public float spawnYaw;
        /// <summary>The first item id this player may give out (each player has its own range).</summary>
        public uint mintFrom;
        public double hostTime;

        const int Version = 1;

        public byte[] Write()
        {
            var w = new NetWriter();
            w.Int(Version);
            w.Bytes(NetPack.PackString(JsonUtility.ToJson(save)));
            w.Bytes(NetPack.PackString(settings != null ? JsonUtility.ToJson(settings) : ""));
            w.Int(rooms.Count);
            foreach (var r in rooms) w.Bytes(NetPack.PackString(r.ToJson()));
            w.Bytes(paint);
            w.Bytes(NetPack.Pack(marks));
            w.Bytes(NetPack.PackString(JsonUtility.ToJson(new NetItemList { list = items })));
            w.Bool(returning);
            w.Bytes(NetPack.PackString(JsonUtility.ToJson(new NetItemList { list = mine })));
            w.Vec(spawn); w.Float(spawnYaw);
            w.UInt(mintFrom);
            w.Double(hostTime);
            return w.ToArray();
        }

        public static NetSnapshot Read(byte[] data, int offset)
        {
            try
            {
                var r = new NetReader();
                r.Set(data, offset, data.Length - offset);
                if (r.Int() != Version) return null;
                var s = new NetSnapshot();
                s.save = JsonUtility.FromJson<WorldSaveData>(NetPack.UnpackString(r.Bytes()));
                string set = NetPack.UnpackString(r.Bytes());
                s.settings = string.IsNullOrEmpty(set) ? null : JsonUtility.FromJson<WorldGenSettings>(set);
                int n = r.Int();
                for (int i = 0; i < n; i++)
                {
                    var b = RoomBlueprint.FromJson(NetPack.UnpackString(r.Bytes()));
                    if (b != null) s.rooms.Add(b);
                }
                s.paint = r.Bytes();
                s.marks = NetPack.Unpack(r.Bytes());
                var list = JsonUtility.FromJson<NetItemList>(NetPack.UnpackString(r.Bytes()));
                if (list?.list != null) s.items = list.list;
                s.returning = r.Bool();
                var mine = JsonUtility.FromJson<NetItemList>(NetPack.UnpackString(r.Bytes()));
                if (mine?.list != null) s.mine = mine.list;
                s.spawn = r.Vec(); s.spawnYaw = r.Float();
                s.mintFrom = r.UInt();
                s.hostTime = r.Double();
                if (s.save == null) return null;
                s.save.slot = -1;
                return s;
            }
            catch (Exception e) { Debug.LogWarning("[Net] The world from the host could not be read: " + e); return null; }
        }
    }
}
