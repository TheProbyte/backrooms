using System.Collections.Generic;
using Backrooms.Destruction;
using Backrooms.Generation;
using Backrooms.Interaction;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Net
{
    public partial class NetWorld
    {
        // ---------------- Hooks ----------------

        void HookBuilding()
        {
            NetHooks.Light = (c, room, off, instant) => { var w = W(Msg.Light); w.Int(c.x); w.Int(c.y); w.Int(c.z); w.Int(room); w.Bool(off); w.Bool(instant); NetSession.SendWorld(true); };
            NetHooks.Drain = (c, room, on) => { var w = W(Msg.Drain); w.Int(c.x); w.Int(c.y); w.Int(c.z); w.Int(room); w.Bool(on); NetSession.SendWorld(true); };
            NetHooks.Breaker = (c, on) => { var w = W(Msg.Breaker); w.Int(c.x); w.Int(c.y); w.Int(c.z); w.Bool(on); NetSession.SendWorld(true); };
            NetHooks.Home = k => { var w = W(Msg.Home); w.Int(k.district.x); w.Int(k.district.y); w.Int(k.storey); w.Int(k.room); NetSession.SendWorld(true); };
            NetHooks.DoorRest = (key, angle) => { var w = W(Msg.DoorRest); w.Str(key); w.Float(angle); w.Byte((byte)Me); w.Double(Now); NetSession.SendWorld(true); };
            NetHooks.DoorBolt = (key, on) => { var w = W(Msg.DoorBolt); w.Str(key); w.Bool(on); NetSession.SendWorld(true); };
            NetHooks.Poke = SendPoke;
            NetHooks.Strip = (kind, a, b, n) => { var w = W(Msg.Mark); w.Byte((byte)kind); w.Vec(a); w.Vec(b); w.Vec(n); NetSession.SendWorld(true); };
            NetHooks.Tape = (pts, nrm) =>
            {
                var w = W(Msg.Mark); w.Byte(2); w.Int(pts.Count);
                for (int i = 0; i < pts.Count; i++) { w.Vec(pts[i]); w.Vec(i < nrm.Count ? nrm[i] : Vector3.up); }
                NetSession.SendWorld(true);
            };
            NetHooks.Scrape = (pts, width, col) =>
            {
                var w = W(Msg.Mark); w.Byte(3); w.Float(width); w.Byte(col.r); w.Byte(col.g); w.Byte(col.b); w.Byte(col.a); w.Int(pts.Count);
                for (int i = 0; i < pts.Count; i++) w.Vec(pts[i]);
                NetSession.SendWorld(true);
            };
            NetHooks.Strike = (k, origin, dir, reach, blow, seed) =>
            {
                var w = W(Msg.Strike);
                w.Bool(k.vertical); w.Int(k.line); w.Int(k.along); w.Int(k.storey);
                w.Vec(origin); w.Vec(dir); w.Float(reach);
                w.Float(blow.energy); w.Float(blow.radius); w.Float(blow.crackSpread);
                w.Int(seed);
                NetSession.SendWorld(true);
            };
            NetHooks.NailAdd = (at, dir, prop) => { var w = W(Msg.Nail); w.Vec(at); w.Vec(dir); w.ULong(prop); NetSession.SendWorld(true); };
            NetHooks.NailRemove = at => { var w = W(Msg.Unnail); w.Vec(at); NetSession.SendWorld(true); };
            NetHooks.PropWoke = LocalWoke;
            NetHooks.PropRested = LocalRested;
        }

        void UnhookBuilding()
        {
            NetHooks.Light = null; NetHooks.Drain = null; NetHooks.Breaker = null; NetHooks.Home = null;
            NetHooks.DoorRest = null; NetHooks.DoorBolt = null; NetHooks.Poke = null;
            NetHooks.Strip = null; NetHooks.Tape = null; NetHooks.Scrape = null; NetHooks.Strike = null;
            NetHooks.NailAdd = null; NetHooks.NailRemove = null; NetHooks.PropWoke = null; NetHooks.PropRested = null;
        }

        static Vector3Int V3i(NetReader r) => new Vector3Int(r.Int(), r.Int(), r.Int());

        // ---------------- Lights, breakers, homes ----------------

        void OnLight(NetReader r)
        {
            var c = V3i(r); int room = r.Int(); bool off = r.Bool(), instant = r.Bool();
            LightZones.Set(c, room, off, instant);
            LightSwitch.Show(c, room);
        }

        void OnDrain(NetReader r) { var c = V3i(r); int room = r.Int(); bool on = r.Bool(); LightZones.Drain(c, room, on); }
        void OnBreaker(NetReader r) { var c = V3i(r); bool on = r.Bool(); PowerGrid.Set(c, on); }
        void OnHome(NetReader r) { var k = new Homes.Key { district = new Vector2Int(r.Int(), r.Int()), storey = r.Int(), room = r.Int() }; Homes.Add(k); }

        // ---------------- Buttons (the elevator, a drinks machine, a boombox's keys) ----------------

        void SendPoke(IPokeTarget t)
        {
            // Switches and breakers go as what they set (Light, Breaker): pressing them twice would undo it.
            if (t == null || t is LightSwitch || t is BreakerPanel) return;
            var w = W(Msg.Poke); w.Vec(t.FocusPoint);
            NetSession.SendWorld(true);
        }

        static readonly Collider[] s_near = new Collider[32];

        void OnPoke(NetReader r)
        {
            Vector3 at = r.Vec();
            int n = Physics.OverlapSphereNonAlloc(at, 0.12f, s_near, ~0, QueryTriggerInteraction.Collide);
            IPokeTarget best = null; float bd = 0.03f * 0.03f;
            for (int i = 0; i < n; i++)
                foreach (var t in s_near[i].GetComponentsInParent<IPokeTarget>(true))
                {
                    if (t is LightSwitch || t is BreakerPanel) continue;
                    float d = (t.FocusPoint - at).sqrMagnitude;
                    if (d < bd) { bd = d; best = t; }
                }
            best?.Press();
        }

        // ---------------- Doors ----------------

        void TickDoors()
        {
            foreach (var d in Door.Live)
            {
                if (!d || d.NetDriven || string.IsNullOrEmpty(d.Key)) continue;
                if (!d.Held && Mathf.Abs(d.Speed) < 0.01f) continue;
                var w = W(Msg.DoorMove); w.Str(d.Key); w.Byte((byte)Me); w.Double(Now); w.Float(d.Angle);
                NetSession.SendWorld(false);
            }
        }

        void UpdateRemoteDoors() { }

        readonly Dictionary<string, (int from, double t)> _doorRest = new Dictionary<string, (int, double)>();

        void OnDoorMove(NetReader r)
        {
            string key = r.Str(); int from = r.Byte(); double t = r.Double(); float angle = r.Float();
            Clock(from, t);
            if (_doorRest.TryGetValue(key, out var rest) && rest.from == from && t <= rest.t) return;   // (late)
            Door.Find(key)?.NetSet(angle, false);
        }

        void OnDoorRest(NetReader r)
        {
            string key = r.Str(); float angle = r.Float();
            int sender = r.Byte(); double t = r.Double();
            _doorRest[key] = (sender, t);
            DoorStates.Set(key, angle);
            Door.Find(key)?.NetSet(angle, true);
        }

        void OnDoorBolt(NetReader r)
        {
            string key = r.Str(); bool on = r.Bool();
            DoorStates.SetBolted(key, on);
            var d = Door.Find(key);
            if (d && d.Bolted != on) d.ToggleBolt();
        }

        // ---------------- Chalk, tape, paint ----------------

        void OnMark(NetReader r)
        {
            var m = Marks.Instance;
            int kind = r.Byte();
            switch (kind)
            {
                case 0: { var a = r.Vec(); var b = r.Vec(); var n = r.Vec(); m.AddChalk(a, b, n); break; }
                case 1: { var a = r.Vec(); var b = r.Vec(); var n = r.Vec(); m.AddTape(a, b, n); break; }
                case 2:
                {
                    int c = r.Int(); var pts = new List<Vector3>(c); var nrm = new List<Vector3>(c);
                    for (int i = 0; i < c; i++) { pts.Add(r.Vec()); nrm.Add(r.Vec()); }
                    m.AddTape(pts, nrm);
                    break;
                }
                case 3:
                {
                    float width = r.Float(); var col = new Color32(r.Byte(), r.Byte(), r.Byte(), r.Byte());
                    int c = r.Int(); var pts = new List<Vector3>(c);
                    for (int i = 0; i < c; i++) pts.Add(r.Vec());
                    m.AddScrape(pts, width, col);
                    break;
                }
            }
        }

        readonly List<SprayPaint.NetPatch> _patches = new List<SprayPaint.NetPatch>();
        float _nextPaint;

        void TickPaint()
        {
            var sp = SprayPaint.Existing;
            if (!sp || Time.unscaledTime < _nextPaint) return;
            _nextPaint = Time.unscaledTime + 0.2f;
            _patches.Clear();
            sp.TakeNetPatches(_patches);
            foreach (var p in _patches)
            {
                var w = W(Msg.Paint);
                w.Byte((byte)(sbyte)p.nx); w.Byte((byte)(sbyte)p.ny); w.Byte((byte)(sbyte)p.nz);
                w.Int(p.d); w.Int(p.u); w.Int(p.v);
                w.Short((short)p.x0); w.Short((short)p.y0); w.Short((short)p.w); w.Short((short)p.h);
                w.Bytes(p.packed);
                NetSession.SendWorld(true);
            }
        }

        void OnPaint(NetReader r)
        {
            var p = new SprayPaint.NetPatch
            {
                nx = (sbyte)r.Byte(), ny = (sbyte)r.Byte(), nz = (sbyte)r.Byte(), d = r.Int(), u = r.Int(), v = r.Int(),
                x0 = r.Short(), y0 = r.Short(), w = r.Short(), h = r.Short(), packed = r.Bytes(),
            };
            SprayPaint.Instance.MergeNetPatch(p);
        }

        // ---------------- Walls broken, nails ----------------

        void OnStrike(NetReader r)
        {
            var k = new WallKey { vertical = r.Bool(), line = r.Int(), along = r.Int(), storey = r.Int() };
            Vector3 origin = r.Vec(), dir = r.Vec(); float reach = r.Float();
            var blow = new Blow { energy = r.Float(), radius = r.Float(), crackSpread = r.Float() };
            int seed = r.Int();
            if (WallBreaker.Instance) WallBreaker.Instance.NetStrike(k, origin, dir, reach, blow, seed);
            else WallDamage.StrikeSeeded(seed, k, origin, dir, reach, blow);
        }

        void OnNail(NetReader r)
        {
            Vector3 at = r.Vec(), dir = r.Vec(); ulong prop = r.ULong();
            var mp = prop != 0 ? MovableProp.Find(prop) : null;
            if (prop != 0 && !mp) return;                            // (its prop is far off here: it comes with its record)
            NailWork.Add(at, dir, mp);
            if (mp && !mp.Pinned) mp.NailDown();
        }

        void OnUnnail(NetReader r)
        {
            Vector3 at = r.Vec();
            int i = NailWork.Near(at, 0.01f, out _, out _, out _);
            if (i >= 0) NailWork.Remove(i);
        }

        // ---------------- Lamps plugged in ----------------

        readonly Dictionary<uint, (bool plugged, Vector3 wall)> _plugs = new Dictionary<uint, (bool, Vector3)>();

        void TickPlugs()
        {
            foreach (var ni in _items.Values)
            {
                if (!ni || !ni.item || ni.item.NetForeign || !(ni.item is PowerUser user) || !user.Plug) continue;
                var plug = user.Plug;
                bool wall = plug.Plugged && (plug.Socket == null || plug.Socket.IsWall);
                Vector3 at = wall ? plug.WallPosition : Vector3.zero;
                if (_plugs.TryGetValue(ni.id, out var was) && was.plugged == wall && (was.wall - at).sqrMagnitude < 1e-4f) continue;
                bool first = !_plugs.ContainsKey(ni.id);
                _plugs[ni.id] = (wall, at);
                if (first && !wall) continue;
                var w = W(Msg.Plug); w.UInt(ni.id); w.Bool(wall); w.Vec(at); w.Vec(plug.transform.position); w.Rot(plug.transform.rotation);
                NetSession.SendWorld(true);
            }
        }

        void OnPlug(NetReader r)
        {
            uint id = r.UInt(); bool wall = r.Bool(); Vector3 at = r.Vec(), pos = r.Vec(); Quaternion rot = r.Rot();
            var ni = Find(id);
            if (!ni || !(ni.item is PowerUser user)) return;
            _plugs[id] = (wall, at);
            if (wall)
            {
                user.Unbundle();
                var plug = user.Plug;
                if (!plug) return;
                if (plug.Plugged) plug.Unplug(false);
                plug.Body.position = pos; plug.Body.rotation = rot;
                plug.transform.SetPositionAndRotation(pos, rot);
                plug.WaitForWall(at);
                user.WakeCord();
            }
            else if (user.Plug && user.Plug.Plugged) user.Plug.Unplug(false);
        }

        // ---------------- What each player carries (the host keeps it, for their coming back) ----------------

        List<GuestData> _guests = new List<GuestData>();
        string _lastInventory = "";
        float _nextInventory;

        List<GuestData> Guests() => _guests;

        void LoadGuests()
        {
            var d = GameSession.Data;
            if (d == null) return;
            if (d.guests == null) d.guests = new List<GuestData>();
            _guests = d.guests;
        }

        void TickInventory()
        {
            TickPlugs();
            if (Host || Time.unscaledTime < _nextInventory) return;
            _nextInventory = Time.unscaledTime + 0.5f;
            var json = InventoryJson();
            if (json == _lastInventory) return;
            _lastInventory = json;
            var w = W(Msg.Inventory);
            w.Str(json);
            w.Vec(_player ? _player.position : Vector3.zero); w.Float(_player ? _player.eulerAngles.y : 0f);
            NetSession.SendWorld(true);
        }

        /// <summary>Said once more before leaving on purpose (so the host has it as it is now).</summary>
        void SendInventoryNow()
        {
            if (Host || !_ready) return;
            _lastInventory = "";
            _nextInventory = 0f;
            TickInventory();
        }

        string InventoryJson()
        {
            var list = new List<ItemData>();
            if (_hand)
            {
                var h = _hand.HeldItem;
                if (h && Shared(h)) list.Add(new ItemData { id = h.itemId, position = h.transform.position, rotation = h.transform.rotation, amount = AmountOf(h), held = true, home = _hand.HeldHome });
                for (int i = 0; i < PropGrabber.PocketCount; i++)
                {
                    var it = _hand.Pocket(i);
                    if (it && Shared(it)) list.Add(new ItemData { id = it.itemId, position = it.transform.position, rotation = it.transform.rotation, amount = AmountOf(it), pocket = i });
                }
                var bag = _hand.WornBag;
                if (bag)
                {
                    list.Add(new ItemData { id = bag.itemId, position = bag.transform.position, rotation = bag.transform.rotation, amount = bag.Amount, worn = true });
                    int bi = list.Count;
                    foreach (var c in bag.Contents)
                        if (c && !string.IsNullOrEmpty(c.itemId)) list.Add(new ItemData { id = c.itemId, position = c.transform.localPosition, rotation = c.transform.localRotation, amount = AmountOf(c), bag = bi });
                }
            }
            return JsonUtility.ToJson(new Inventory { items = list });
        }

        [System.Serializable] class Inventory { public List<ItemData> items = new List<ItemData>(); }

        static float AmountOf(WorldItem it) => it is SprayCan can ? can.paint : it.Amount;

        void OnInventory(int from, NetReader r)
        {
            if (!Host) return;
            var inv = JsonUtility.FromJson<Inventory>(r.Str());
            Vector3 pos = r.Vec(); float yaw = r.Float();
            string name = NetSession.NameOf(from);
            var g = _guests.Find(x => x.name == name);
            if (g == null) { g = new GuestData { name = name }; _guests.Add(g); }
            g.items = inv?.items ?? new List<ItemData>();
            g.position = pos; g.yaw = yaw;
        }

        void GuestLeft(Peer p)
        {
            var g = _guests.Find(x => x.name == p.name);
            if (g == null) { g = new GuestData { name = p.name }; _guests.Add(g); }
            if (_avatars.TryGetValue(p.id, out var a) && a && a.HasState) { g.position = a.Current.pos; g.yaw = a.Current.yaw; }
            WorldSaver.SaveNow();
        }

        /// <summary>A guest's record as items with ids (a coming-back player's things).</summary>
        List<NetItemData> GuestItems(GuestData g, int peer)
        {
            var list = new List<NetItemData>();
            var ids = new List<uint>();
            foreach (var d in g.items)
            {
                uint id = Mint();
                _mintNext = id + 1;
                ids.Add(id);
                var n = JsonUtility.FromJson<NetItemData>(JsonUtility.ToJson(d));
                n.net = id; n.holder = peer;
                n.mode = d.bag > 0 ? (int)ItemMode.Bag : d.pocket >= 0 ? (int)ItemMode.Pocket : d.worn ? (int)ItemMode.Worn : (int)ItemMode.Hand;
                list.Add(n);
            }
            for (int i = 0; i < list.Count; i++)
                if (list[i].mode == (int)ItemMode.Bag && g.items[i].bag - 1 >= 0 && g.items[i].bag - 1 < ids.Count) list[i].bagNet = ids[g.items[i].bag - 1];
            return list;
        }
    }
}
