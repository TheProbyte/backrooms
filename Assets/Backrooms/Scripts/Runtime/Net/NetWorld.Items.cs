using System.Collections.Generic;
using Backrooms.Interaction;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Net
{
    /// <summary>An item's network id and what this game knows of it (who holds it, who is moving it, its motion).</summary>
    public sealed class NetItem : MonoBehaviour
    {
        public uint id;
        public WorldItem item;
        public ItemMode mode = ItemMode.World;
        public int holder = -1;
        public uint bag;
        /// <summary>The game simulating it while it moves (it was thrown or knocked there), or -1.</summary>
        public int owner = -1;
        public float amount = -1f, amountSentAt;
        public float still;
        /// <summary>When (its mover's clock) it came to rest, and who said so: older moves from them are late, not new.</summary>
        public double restT = double.MinValue;
        public int restFrom = -1;
        internal readonly List<NetWorld.Sample> buf = new List<NetWorld.Sample>();
    }

    public partial class NetWorld
    {
        /// <summary>A pose at a time (another game's clock), for following what it moves.</summary>
        public struct Sample { public double t; public Vector3 p; public Quaternion r; public Vector3 v; }

        readonly Dictionary<uint, NetItem> _items = new Dictionary<uint, NetItem>();
        readonly Dictionary<int, double> _clock = new Dictionary<int, double>();
        readonly Dictionary<int, uint> _mintHigh = new Dictionary<int, uint>();
        uint _mintNext = 1;
        float _claimAfter;
        readonly List<uint> _gone = new List<uint>();
        readonly List<KeyValuePair<uint, NetItem>> _tick = new List<KeyValuePair<uint, NetItem>>();

        // ---------------- Ids ----------------

        /// <summary>A new id from this player's own range (the top byte is the player: no two games give out the same one).</summary>
        uint Mint()
        {
            uint baseId = (uint)Me << 24;
            if (_mintNext <= baseId || _mintNext >= baseId + 0xFFFFFF) _mintNext = baseId + 1;
            while (_items.ContainsKey(_mintNext)) _mintNext++;
            return _mintNext++;
        }

        /// <summary>The host: where a joining player's ids start (past any they gave out before, this session).</summary>
        uint MintFrom(int peer)
        {
            uint baseId = (uint)peer << 24;
            uint high = _mintHigh.TryGetValue(peer, out var h) ? h : baseId;
            foreach (var k in _items.Keys) if ((k >> 24) == (uint)peer && k > high) high = k;
            return System.Math.Max(baseId + 1, high + 1);
        }

        void Seen(uint id) { int p = (int)(id >> 24); if (!_mintHigh.TryGetValue(p, out var h) || id > h) _mintHigh[p] = id; }

        NetItem Find(uint id) => _items.TryGetValue(id, out var n) && n && n.item ? n : null;

        NetItem Tag(WorldItem it, uint id)
        {
            var ni = it.GetComponent<NetItem>();
            if (!ni) ni = it.gameObject.AddComponent<NetItem>();
            if (ni.id != 0 && ni.id != id) _items.Remove(ni.id);
            ni.id = id; ni.item = it;
            _items[id] = ni;
            Seen(id);
            return ni;
        }

        static NetItem TagOf(WorldItem it) => it ? it.GetComponent<NetItem>() : null;

        static bool Shared(WorldItem it) => it && !string.IsNullOrEmpty(it.itemId) && !(it is Plug) && !it.IsLoot;

        /// <summary>Gives every item without an id one (the host at the start; new things as they appear).</summary>
        void TagUntagged(bool inactiveToo)
        {
            if (inactiveToo)
            {
                foreach (var it in FindObjectsByType<WorldItem>(FindObjectsInactive.Include))
                    if (Shared(it) && !TagOf(it) && it.lootTakenFrom == 0) Tag(it, Mint());
                return;
            }
            for (int i = 0; i < WorldItem.Live.Count; i++)
            {
                var it = WorldItem.Live[i];
                if (!Shared(it) || TagOf(it)) continue;
                if (it.lootTakenFrom != 0) { TookLoot(it); continue; }
                if (it.NetForeign) continue;
                var ni = Tag(it, Mint());
                // New here (made from the developer menu, out of a found bag): everyone gets it.
                var (mode, holder, bag) = LocalMode(it);
                ni.mode = mode; ni.holder = holder; ni.bag = bag;
                SendHeld(ni, mode == ItemMode.World ? it.Body.linearVelocity : Vector3.zero);
            }
        }

        // ---------------- What an item is to this game ----------------

        (ItemMode mode, int holder, uint bag) LocalMode(WorldItem it)
        {
            if (_hand)
            {
                if (_hand.HeldItem == it) return (ItemMode.Hand, Me, 0);
                for (int i = 0; i < PropGrabber.PocketCount; i++) if (_hand.Pocket(i) == it) return (ItemMode.Pocket, Me, 0);
                if (_hand.WornBag == it) return (ItemMode.Worn, Me, 0);
            }
            if (it.InBag) { var b = TagOf(it.InBag); return (ItemMode.Bag, -1, b ? b.id : 0); }
            if (it is Cassette cas && cas.Deck) { var b = TagOf(cas.Deck); return (ItemMode.Deck, -1, b ? b.id : 0); }
            return (ItemMode.World, -1, 0);
        }

        NetItemData Data(NetItem ni)
        {
            var it = ni.item;
            var d = new NetItemData { id = it.itemId, net = ni.id, holder = ni.holder, mode = (int)ni.mode, bagNet = ni.bag, amount = it.Amount };
            if (ni.mode == ItemMode.Bag || ni.mode == ItemMode.Deck) { d.position = it.transform.localPosition; d.rotation = it.transform.localRotation; }
            else { d.position = it.transform.position; d.rotation = it.transform.rotation; }
            if (it is SprayCan can) d.amount = can.paint;
            if (it is Backpack b && b.Worn) d.worn = true;
            PlugData(it, d);
            return d;
        }

        /// <summary>Every item in the world, in hands and on backs (not in pockets: those are their carrier's), for a joiner.</summary>
        List<NetItemData> WorldItems()
        {
            var list = new List<NetItemData>();
            foreach (var ni in _items.Values)
            {
                if (!ni || !ni.item) continue;
                var (mode, holder, bag) = ni.item.NetForeign ? (ni.mode, ni.holder, ni.bag) : LocalMode(ni.item);
                if (mode == ItemMode.Pocket) continue;
                ni.mode = mode; ni.holder = holder; ni.bag = bag;
                list.Add(Data(ni));
            }
            // Bags and boomboxes before what is in them.
            list.Sort((a, b) => Inside(a).CompareTo(Inside(b)));
            return list;
        }

        // ---------------- Each tick: what this game did with its things ----------------

        void TickItems()
        {
            TagUntagged(false);
            _gone.Clear();
            // (A copy: telling of a backpack gives ids to what is in it, which adds to the list.)
            _tick.Clear();
            _tick.AddRange(_items);
            foreach (var kv in _tick)
            {
                var ni = kv.Value;
                if (!ni || !ni.item) { _gone.Add(kv.Key); continue; }
                var it = ni.item;
                if (it.NetForeign) continue;                                // another player's: they say what happens to it
                var (mode, holder, bag) = LocalMode(it);
                if (mode != ni.mode || holder != ni.holder || bag != ni.bag)
                {
                    bool released = mode == ItemMode.World && ni.mode != ItemMode.World;
                    ni.mode = mode; ni.holder = holder; ni.bag = bag;
                    if (mode != ItemMode.World) { ni.owner = -1; it.NetRemote = false; ni.buf.Clear(); }
                    if (released) { ni.owner = Me; ni.still = 0f; it.NetRemote = false; }
                    SendHeld(ni, released ? it.Body.linearVelocity : Vector3.zero);
                    continue;
                }
                if (mode == ItemMode.World)
                {
                    bool moving = Moving(it);
                    if (ni.owner == Me)
                    {
                        if (moving || it.settling) { ni.still = 0f; SendMove(ni); }
                        else if ((ni.still += Tick) > 0.4f || it.Body.isKinematic) Rest(ni);
                    }
                    else if (ni.owner < 0 && !it.NetRemote && Time.time > _claimAfter && Knocked(it))
                    {
                        // Knocked here (a thrown thing, a shove): this game moves it now.
                        ni.owner = Me; ni.still = 0f;
                        var w = W(Msg.ItemOwner); w.UInt(ni.id); w.Short((short)Me);
                        NetSession.SendWorld(true);
                        SendMove(ni);
                    }
                }
                // Paint used, a lamp switched, a bag opened.
                float amount = it is SprayCan can ? can.paint : it.Amount;
                if (Mathf.Abs(amount - ni.amount) > 0.004f && Time.unscaledTime - ni.amountSentAt > 0.25f)
                {
                    bool real = ni.amount >= 0f;
                    ni.amount = amount; ni.amountSentAt = Time.unscaledTime;
                    if (real) { var w = W(Msg.ItemAmount); w.UInt(ni.id); w.Float(amount); NetSession.SendWorld(true); }
                }
            }
            foreach (var id in _gone)
            {
                _items.Remove(id);
                // Gone here (used up, destroyed): gone everywhere.
                W(Msg.ItemGone).UInt(id);
                NetSession.SendWorld(true);
            }
        }

        static int Inside(NetItemData d) => d.mode == (int)ItemMode.Bag || d.mode == (int)ItemMode.Deck ? 1 : 0;

        /// <summary>Really knocked (not just settling where it was put): this game takes it over.</summary>
        static bool Knocked(WorldItem it)
        {
            var b = it.Body;
            if (!b || b.isKinematic || b.IsSleeping()) return false;
            return b.linearVelocity.sqrMagnitude > 0.09f || b.angularVelocity.sqrMagnitude > 2f;
        }

        static bool Moving(WorldItem it)
        {
            var b = it.Body;
            if (!b || b.isKinematic || b.IsSleeping()) return false;
            return b.linearVelocity.sqrMagnitude > 0.004f || b.angularVelocity.sqrMagnitude > 0.04f;
        }

        void SendHeld(NetItem ni, Vector3 velocity)
        {
            var w = W(Msg.ItemHeld);
            WriteHeld(w, ni, velocity);
            NetSession.SendWorld(true);
        }

        void WriteHeld(NetWriter w, NetItem ni, Vector3 velocity)
        {
            w.UInt(ni.id);
            w.Short((short)ni.holder);
            w.Byte((byte)ni.mode);
            w.UInt(ni.bag);
            w.Str(JsonUtility.ToJson(Data(ni)));
            w.Vec(velocity);
            w.Short((short)ni.owner);
            // A backpack: what is in it, with their ids (so every game has the same things in it).
            if (ni.item is Backpack bag)
            {
                var contents = new List<NetItemData>();
                foreach (var c in bag.Contents)
                {
                    if (!Shared(c) && !(c && c.lootId != 0)) continue;
                    var cn = TagOf(c);
                    if (!cn) { if (c.lootId != 0) { Saves.LootRegistry.Take(c.lootId); c.lootId = 0; } cn = Tag(c, Mint()); }
                    cn.mode = ItemMode.Bag; cn.bag = ni.id; cn.holder = -1;
                    contents.Add(Data(cn));
                }
                w.Int(contents.Count);
                foreach (var d in contents) w.Str(JsonUtility.ToJson(d));
            }
            else w.Int(0);
        }

        void SendMove(NetItem ni)
        {
            var it = ni.item;
            var w = W(Msg.ItemMove);
            w.UInt(ni.id); w.Byte((byte)Me); w.Double(Now);
            w.Vec(it.transform.position); w.Rot(it.transform.rotation); w.Vec(it.Body.isKinematic ? Vector3.zero : it.Body.linearVelocity);
            NetSession.SendWorld(false);
        }

        void Rest(NetItem ni)
        {
            var it = ni.item;
            ni.owner = -1; ni.still = 0f;
            var w = W(Msg.ItemRest);
            w.UInt(ni.id); w.Vec(it.transform.position); w.Rot(it.transform.rotation);
            w.Float(it is SprayCan can ? can.paint : it.Amount);
            w.Byte((byte)Me); w.Double(Now);
            NetSession.SendWorld(true);
        }

        // ---------------- Loot taken from a room ----------------

        /// <summary>A thing laid out in a special room (or a found backpack) was just taken here: it gets an id and everyone's
        /// copy of the room gives up the same thing.</summary>
        void TookLoot(WorldItem it)
        {
            ulong loot = it.lootTakenFrom;
            it.lootTakenFrom = 0;
            var ni = Tag(it, Mint());
            var (mode, holder, bag) = LocalMode(it);
            ni.mode = mode; ni.holder = holder; ni.bag = bag;
            var w = W(Msg.LootTaken);
            w.ULong(loot);
            WriteHeld(w, ni, Vector3.zero);
            NetSession.SendWorld(true);
        }

        bool OnLootTaken(int from, NetReader r)
        {
            ulong loot = r.ULong();
            Saves.LootRegistry.Take(loot);
            WorldItem found = null;
            foreach (var it in FindObjectsByType<WorldItem>(FindObjectsInactive.Include))
                if (it && it.lootId == loot) { found = it; break; }
            if (found)
            {
                found.lootId = 0;
                found.transform.SetParent(null, true);
                Tag(found, r.PeekUInt());          // (the id is the first field of what follows)
            }
            return OnItemHeld(from, r);
        }

        // ---------------- Applying what another game did ----------------

        bool OnItemHeld(int from, NetReader r)
        {
            uint id = r.UInt();
            int holder = r.Short();
            var mode = (ItemMode)r.Byte();
            uint bagId = r.UInt();
            var d = JsonUtility.FromJson<NetItemData>(r.Str());
            Vector3 vel = r.Vec();
            int owner = r.Short();
            int nc = r.Int();
            var contents = new List<NetItemData>();
            for (int i = 0; i < nc; i++) contents.Add(JsonUtility.FromJson<NetItemData>(r.Str()));
            Seen(id);
            var ni = Find(id);

            // The host settles who has it: the first to take it.
            if (Host && from != 0 && ni && (mode == ItemMode.Hand || mode == ItemMode.Pocket || mode == ItemMode.Worn))
            {
                var (lm, lh, _) = ni.item.NetForeign ? (ni.mode, ni.holder, ni.bag) : LocalMode(ni.item);
                bool taken = (lm == ItemMode.Hand || lm == ItemMode.Pocket || lm == ItemMode.Worn) && lh >= 0 && lh != holder;
                if (taken)
                {
                    ni.mode = lm; ni.holder = lh;
                    var w = W(Msg.ItemHeld); WriteHeld(w, ni, Vector3.zero);
                    NetSession.SendTo(from, true);
                    Debug.Log($"[Net] {NetSession.NameOf(from)} reached for {ni.item.displayName}, but {NetSession.NameOf(lh)} has it.");
                    return false;
                }
            }

            // Not here (in a pocket, or never seen): made from what was sent, unless it is going into a pocket.
            if (!ni)
            {
                if (mode == ItemMode.Pocket) return true;
                var it = WorldSaver.SpawnItem(d);
                if (!it) return true;
                ni = Tag(it, id);
                ni.amount = d.amount;
            }
            var item = ni.item;
            ApplyAmount(item, d.amount);
            ni.amount = d.amount;
            if (holder == Me && mode != ItemMode.World && mode != ItemMode.Bag) { ni.mode = mode; ni.holder = holder; return true; }   // (ours: as it is)

            // Someone else has it now: out of this hand, the reach and the pockets.
            if (_hand && (mode == ItemMode.Hand || mode == ItemMode.Pocket || mode == ItemMode.Worn) && holder != Me) _hand.Forget(item);
            var avatarOld = HolderAvatar(item);
            if (avatarOld) { if (avatarOld.held == item) avatarOld.held = null; if (avatarOld.worn == item) avatarOld.worn = null; }

            if (mode != ItemMode.Deck && item is Cassette inDeck && inDeck.Deck) inDeck.LeaveDeck();
            ni.mode = mode; ni.holder = holder; ni.bag = bagId; ni.buf.Clear();
            switch (mode)
            {
                case ItemMode.Hand:
                case ItemMode.Worn:
                {
                    if (item.InBag) item.InBag.Removed(item);
                    Foreign(item, true);
                    var av = Avatar(holder);
                    if (av) { if (mode == ItemMode.Hand) av.held = item; else av.worn = item; }
                    else item.transform.SetPositionAndRotation(d.position, d.rotation);
                    ni.owner = -1;
                    break;
                }
                case ItemMode.Pocket:
                    // In their pocket: not in this world any more (the host keeps it in their record).
                    DestroyItem(item);
                    return true;
                case ItemMode.Deck:
                {
                    var box = Find(bagId)?.item as Boombox;
                    Foreign(item, false);
                    if (item.InBag) item.InBag.Removed(item);
                    if (box && item is Cassette cas && box.Inserted != cas) { if (box.Inserted) box.Inserted.LeaveDeck(); box.Adopt(cas); }
                    item.NetRemote = false; ni.owner = -1;
                    break;
                }
                case ItemMode.Bag:
                {
                    var bag = Find(bagId)?.item as Backpack;
                    Foreign(item, false);
                    if (bag) { if (item.InBag && item.InBag != bag) item.InBag.Removed(item); bag.AdoptSaved(item, d.position, d.rotation); }
                    else item.transform.SetPositionAndRotation(d.position, d.rotation);
                    item.NetRemote = false; ni.owner = -1;
                    break;
                }
                default:
                {
                    // Put down or thrown: in the world, moved by the game that let it go until it settles.
                    if (item.InBag) item.InBag.Removed(item);
                    Foreign(item, false);
                    item.transform.SetPositionAndRotation(d.position, d.rotation);
                    if (item.Body) { item.Body.position = d.position; item.Body.rotation = d.rotation; }
                    ni.owner = owner;
                    item.NetRemote = ni.owner != Me && ni.owner >= 0;
                    if (!item.NetRemote && item.Body && !item.Body.isKinematic) item.Body.linearVelocity = vel;
                    ni.buf.Clear();
                    ni.buf.Add(new Sample { t = -1, p = d.position, r = d.rotation, v = vel });
                    break;
                }
            }
            if (item is Backpack b2 && contents.Count > 0) SyncContents(b2, ni.id, contents);
            PlaceFromData(item, d);
            return true;
        }

        /// <summary>A backpack holds what its owner's game says, with the same ids.</summary>
        void SyncContents(Backpack bag, uint bagId, List<NetItemData> contents)
        {
            var keep = new HashSet<WorldItem>();
            foreach (var c in contents)
            {
                var cn = Find(c.net);
                WorldItem it = cn ? cn.item : null;
                if (!it)
                {
                    it = WorldSaver.SpawnItem(c);
                    if (!it) continue;
                    cn = Tag(it, c.net);
                }
                cn.mode = ItemMode.Bag; cn.bag = bagId; cn.holder = -1; cn.owner = -1;
                Foreign(it, false);
                if (it.InBag != bag) { if (it.InBag) it.InBag.Removed(it); bag.AdoptSaved(it, c.position, c.rotation); }
                keep.Add(it);
            }
            // What this game's copy of the bag had that the owner's has not (made by its room here: a duplicate).
            var extra = new List<WorldItem>();
            foreach (var it in bag.Contents) if (!keep.Contains(it)) extra.Add(it);
            foreach (var it in extra) { bag.Removed(it); DestroyItem(it); }
        }

        void OnItemMove(NetReader r)
        {
            uint id = r.UInt(); int from = r.Byte(); double t = r.Double();
            var s = new Sample { t = t, p = r.Vec(), r = r.Rot(), v = r.Vec() };
            Clock(from, t);
            var ni = Find(id);
            if (!ni || ni.item.NetForeign || ni.mode != ItemMode.World) return;
            if (ni.owner == Me) return;
            if (from == ni.restFrom && t <= ni.restT) return;                 // (sent before it came to rest: late by UDP)
            if (ni.owner < 0) ni.owner = from;
            ni.item.NetRemote = true;
            if (ni.buf.Count > 0 && ni.buf[ni.buf.Count - 1].t >= t) return;
            if (ni.buf.Count > 0 && ni.buf[0].t < 0) ni.buf.Clear();
            ni.buf.Add(s);
            while (ni.buf.Count > 20) ni.buf.RemoveAt(0);
        }

        void OnItemRest(int from, NetReader r)
        {
            uint id = r.UInt();
            Vector3 p = r.Vec(); Quaternion q = r.Rot(); float amount = r.Float();
            int sender = r.Byte(); double t = r.Double();
            var ni = Find(id);
            if (!ni || ni.item.NetForeign) return;
            var it = ni.item;
            ni.buf.Clear(); ni.owner = -1;
            ni.restFrom = sender; ni.restT = t;
            it.NetRemote = false;
            if (ni.mode == ItemMode.World && !it.Held)
            {
                if (it.Body) { if (!it.Body.isKinematic) { it.Body.linearVelocity = Vector3.zero; it.Body.angularVelocity = Vector3.zero; } it.Body.position = p; it.Body.rotation = q; }
                it.transform.SetPositionAndRotation(p, q);
                if (it.Body && !it.Body.isKinematic) it.Body.Sleep();
            }
            ApplyAmount(it, amount); ni.amount = amount;
        }

        void OnItemGone(NetReader r)
        {
            uint id = r.UInt();
            var ni = Find(id);
            if (ni) { if (_hand) _hand.Forget(ni.item); DestroyItem(ni.item); }
            _items.Remove(id);
        }

        void OnItemAmount(NetReader r)
        {
            uint id = r.UInt(); float a = r.Float();
            var ni = Find(id);
            if (!ni) return;
            ApplyAmount(ni.item, a); ni.amount = a;
        }

        bool OnItemOwner(int from, NetReader r)
        {
            uint id = r.UInt(); int owner = r.Short();
            var ni = Find(id);
            if (!ni) return true;
            // The host: someone else is already moving it: they keep it, and the claimant is told.
            if (Host && from != 0 && ni.owner >= 0 && ni.owner != owner)
            {
                var w = W(Msg.ItemOwner); w.UInt(id); w.Short((short)ni.owner);
                NetSession.SendTo(from, true);
                return false;
            }
            ni.owner = owner;
            ni.item.NetRemote = owner != Me && owner >= 0 && ni.mode == ItemMode.World;
            if (ni.item.NetRemote) ni.buf.Clear();
            return true;
        }

        static void ApplyAmount(WorldItem it, float a)
        {
            if (!it || a < 0f) return;
            if (it is SprayCan can) { can.paint = Mathf.Clamp01(a); return; }
            if (it is CordBox) return;
            if (it is Backpack bag)
            {
                bool open = a - Mathf.Floor(a + 0.01f) > 0.25f;
                if (open != bag.IsOpenOrOpening) { if (open) bag.OpenNow(); else bag.ShutNow(); }
                bag.Amount = Mathf.Floor(a + 0.01f) + (open ? 0.5f : 0f);
                return;
            }
            if (Mathf.Abs(it.Amount - a) > 0.001f) it.Amount = a;
        }

        /// <summary>Shown here for someone else (in their hand, on their back): no physics, nothing to bump into; or back to normal.</summary>
        static void Foreign(WorldItem it, bool foreign)
        {
            if (!it) return;
            if (it.NetForeign == foreign) { if (!foreign) it.gameObject.SetActive(true); return; }
            it.NetForeign = foreign;
            it.NetRemote = foreign;
            if (it.Body)
            {
                if (!it.Body.isKinematic) { it.Body.linearVelocity = Vector3.zero; it.Body.angularVelocity = Vector3.zero; }
                it.Body.isKinematic = true;
                it.Body.interpolation = foreign ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            }
            foreach (var c in it.GetComponentsInChildren<Collider>(true)) if (c) c.enabled = !foreign;
            if (!foreign) it.transform.SetParent(null, true);
            it.gameObject.SetActive(true);
        }

        RemotePlayer HolderAvatar(WorldItem it)
        {
            foreach (var a in _avatars.Values) if (a && (a.held == it || a.worn == it)) return a;
            return null;
        }

        void DestroyItem(WorldItem it)
        {
            if (!it) return;
            var ni = TagOf(it);
            if (ni) _items.Remove(ni.id);
            var av = HolderAvatar(it);
            if (av) { if (av.held == it) av.held = null; if (av.worn == it) av.worn = null; }
            if (it is Backpack bag) foreach (var c in new List<WorldItem>(bag.Contents)) { var cn = TagOf(c); if (cn) _items.Remove(cn.id); }
            Destroy(it.gameObject);
        }

        /// <summary>A player left: what they were moving settles here.</summary>
        void ForgetOwner(int peer)
        {
            foreach (var ni in _items.Values) if (ni && ni.owner == peer) { ni.owner = -1; if (ni.item) ni.item.NetRemote = false; ni.buf.Clear(); }
            foreach (var kv in _props) if (kv.Value.owner == peer) { kv.Value.owner = -1; ReleaseRemote(kv.Key); }
        }

        // ---------------- Following what another game moves ----------------

        void Clock(int peer, double t)
        {
            double est = Now - t;
            if (!_clock.TryGetValue(peer, out var off) || est < off) _clock[peer] = est;
            else _clock[peer] = off + (est - off) * 0.002;
        }

        /// <summary>Where something another game moves is now: a tenth of a second behind it, between its updates.</summary>
        bool SampleAt(List<Sample> buf, int peer, out Vector3 p, out Quaternion q)
        {
            p = default; q = Quaternion.identity;
            if (buf.Count == 0) return false;
            if (buf[0].t < 0) { p = buf[0].p; q = buf[0].r; return true; }
            double off = _clock.TryGetValue(peer, out var o) ? o : 0;
            double rt = Now - off - 0.1;
            var last = buf[buf.Count - 1];
            if (rt >= last.t) { p = last.p + last.v * Mathf.Min(0.2f, (float)(rt - last.t)); q = last.r; return true; }
            for (int i = buf.Count - 1; i > 0; i--)
            {
                var a = buf[i - 1]; var b = buf[i];
                if (rt < a.t) continue;
                float k = (float)((rt - a.t) / System.Math.Max(1e-6, b.t - a.t));
                p = Vector3.Lerp(a.p, b.p, k); q = Quaternion.Slerp(a.r, b.r, k);
                if (i > 2) buf.RemoveRange(0, i - 2);
                return true;
            }
            p = buf[0].p; q = buf[0].r;
            return true;
        }

        void UpdateRemoteItems()
        {
            foreach (var ni in _items.Values)
            {
                if (!ni || !ni.item || !ni.item.NetRemote || ni.item.NetForeign || ni.buf.Count == 0) continue;
                if (!SampleAt(ni.buf, ni.owner, out var p, out var q)) continue;
                var it = ni.item;
                if (it.Body && !it.Body.isKinematic) it.Body.isKinematic = true;
                it.transform.SetPositionAndRotation(p, q);
                if (it.Body) { it.Body.position = p; it.Body.rotation = q; }
            }
        }

        // ---------------- Lamps' plugs (where they are plugged in) ----------------

        static void PlugData(WorldItem it, NetItemData d)
        {
            if (!(it is PowerUser user) || !user.Plug) return;
            var plug = user.Plug;
            d.hasPlug = true; d.plugPosition = plug.transform.position; d.plugRotation = plug.transform.rotation;
            if (plug.Plugged && (plug.Socket == null || plug.Socket.IsWall)) { d.plugTarget = 0; d.plugWall = plug.WallPosition; }
        }

        /// <summary>A lamp arriving with its plug in the wall: put in there too.</summary>
        static void PlaceFromData(WorldItem it, NetItemData d)
        {
            if (!(it is PowerUser user) || !d.hasPlug) return;
            if (d.plugTarget != 0) return;
            var plug = user.Plug;
            if (plug && plug.Plugged) return;
            user.Unbundle();
            plug = user.Plug;
            if (!plug) return;
            plug.Body.position = d.plugPosition; plug.Body.rotation = d.plugRotation;
            plug.transform.SetPositionAndRotation(d.plugPosition, d.plugRotation);
            plug.WaitForWall(d.plugWall);
            user.WakeCord();
        }

        // ---------------- Joining: the host's things ----------------

        void SpawnSnapshotItems(List<NetItemData> list)
        {
            var bags = new List<NetItemData>();
            foreach (var d in list)
            {
                if (d.mode == (int)ItemMode.Bag || d.mode == (int)ItemMode.Deck) { bags.Add(d); continue; }
                var it = WorldSaver.SpawnItem(d);
                if (!it) continue;
                var ni = Tag(it, d.net);
                ni.mode = (ItemMode)d.mode; ni.holder = d.holder; ni.amount = d.amount;
                if (ni.mode == ItemMode.Hand || ni.mode == ItemMode.Worn)
                {
                    Foreign(it, true);
                    var av = Avatar(d.holder);
                    if (av) { if (ni.mode == ItemMode.Hand) av.held = it; else av.worn = it; }
                }
                PlaceFromData(it, d);
                if (it.Body && !it.Body.isKinematic) it.Body.Sleep();          // (as it lay in the host's world)
            }
            foreach (var d in bags)
            {
                var into = Find(d.bagNet)?.item;
                var it = WorldSaver.SpawnItem(d);
                if (!it) continue;
                var ni = Tag(it, d.net);
                ni.mode = (ItemMode)d.mode; ni.bag = d.bagNet; ni.amount = d.amount;
                if (into is Backpack bag) bag.AdoptSaved(it, d.position, d.rotation);
                else if (into is Boombox box && it is Cassette cas) box.Adopt(cas);
            }
            // Bags were saved open or shut: as they are.
            foreach (var d in list) { var ni = Find(d.net); if (ni && ni.item is Backpack) ApplyAmount(ni.item, d.amount); }
        }

        /// <summary>A player coming back: what they were carrying, back in their hand, pockets and on their back.</summary>
        void TakeMine(List<NetItemData> mine)
        {
            if (!_hand) return;
            Backpack worn = null;
            WorldItem held = null;
            int heldHome = -1;
            var spawned = new Dictionary<uint, WorldItem>();
            foreach (var d in mine)
            {
                if (d.mode == (int)ItemMode.Bag) continue;
                var it = WorldSaver.SpawnItem(d);
                if (!it) continue;
                var ni = Tag(it, d.net);
                spawned[d.net] = it;
                if (d.pocket >= 0) { _hand.PutInPocket(it, d.pocket); ni.mode = ItemMode.Pocket; ni.holder = Me; }
                else if (d.worn && it is Backpack b) { worn = b; ni.mode = ItemMode.Worn; ni.holder = Me; }
                else if (d.held) { held = it; heldHome = d.home; ni.mode = ItemMode.World; }
            }
            foreach (var d in mine)
            {
                if (d.mode != (int)ItemMode.Bag) continue;
                if (!spawned.TryGetValue(d.bagNet, out var bagIt) || !(bagIt is Backpack bag)) continue;
                var it = WorldSaver.SpawnItem(d);
                if (!it) continue;
                var ni = Tag(it, d.net);
                ni.mode = ItemMode.Bag; ni.bag = d.bagNet;
                bag.AdoptSaved(it, d.position, d.rotation);
            }
            if (worn) _hand.WearNow(worn);
            if (held) { _hand.PickUp(held); _hand.SetHeldHome(held, heldHome); }
        }
    }
}
