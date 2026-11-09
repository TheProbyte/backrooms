using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.Saves
{
    /// <summary>
    /// Keeps the world being played (<see cref="GameSession.Data"/>) saved: it puts back what was saved when the world
    /// starts (moved furniture, things spawned from the developer menu, items, paint, where the player was), and saves
    /// on quitting (in game, or stopping Play Mode in the editor), on leaving for the menu or another scene, and every
    /// few minutes in case the game crashes.
    /// Added by the WorldStreamer while a save slot is being played.
    /// </summary>
    public class WorldSaver : MonoBehaviour
    {
        public static WorldSaver Instance { get; private set; }
        [Tooltip("Seconds between automatic saves (0: only on quit and leaving).")]
        public float autosaveInterval = 180f;

        WorldStreamer _streamer;
        WorldSaveData _data;
        double _playAtStart;
        float _timeAtStart, _nextAutosave;
        bool _restored;

        public static double PlaySeconds => Instance ? Instance._playAtStart + (Time.time - Instance._timeAtStart) : 0;
        /// <summary>What was saved is back in the world (items, paint, the hand's things).</summary>
        public bool Restored => _restored;
        /// <summary>Co-op (2026-11-04): the joined players' records, for the host's save (NetWorld keeps them).</summary>
        public static Func<List<GuestData>> NetGuests;

        void Awake()
        {
            Instance = this;
            _streamer = GetComponent<WorldStreamer>();
            _data = GameSession.Data;
            _playAtStart = _data != null ? _data.playSeconds : 0;
            LootRegistry.Import(_data?.takenLoot);
            NoclipTravel.HasOrigin = _data != null && _data.hasNoclipOrigin;
            if (_data != null) { NoclipTravel.OriginPosition = _data.noclipOrigin; NoclipTravel.OriginNormal = _data.noclipOriginNormal; }
            _timeAtStart = Time.time;
            _nextAutosave = Time.time + autosaveInterval;
            Camcorder.CamcorderOSD.TapeSeconds = (float)_playAtStart;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Camcorder.CamcorderOSD.TapeSeconds = 0f;
        }

        void Update()
        {
            if (autosaveInterval > 0f && Time.time >= _nextAutosave && _restored)
            {
                _nextAutosave = Time.time + autosaveInterval;
                Save();
            }
        }

        void OnApplicationQuit() => Save();

        /// <summary>Saves the world being played, if there is one.</summary>
        public static void SaveNow() { if (Instance) Instance.Save(); }

        // ---------------- Restore ----------------

        /// <summary>Moved props go back on the list before any chunk is built (so their chunks leave them out); broken walls too.</summary>
        public void RestoreProps(WorldGenerator world)
        {
            if (_data != null) Destruction.WallDamage.Import(_data.walls);
            NailWork.Import(_data?.nails);                  // (a new world: none)
            var mgr = MovablePropManager.Instance;
            if (_data == null || !mgr) return;
            var list = new List<MovablePropManager.Saved>();
            foreach (var p in _data.props)
            {
                var spec = ToSpec(p);
                if (spec == null) continue;
                list.Add(new MovablePropManager.Saved { spec = spec, position = p.position, rotation = p.rotation });
                ItemCatalog.ReserveId(spec.id);
            }
            mgr.Import(world, list);
        }

        /// <summary>Puts the player where they were (instead of the spawn). False if the save has no player yet.</summary>
        public bool RestorePlayer(Transform player)
        {
            if (_data == null || !_data.hasPlayer || !player) return false;
            var ctrl = player.GetComponent<CamcorderPlayerController>();
            if (!ctrl) { player.position = _data.playerPosition; return true; }
            if (_data.noclip) ctrl.SetNoclip(true);
            ctrl.SetStance((CamcorderPlayerController.Stance)Mathf.Clamp(_data.stance, 0, 2));
            ctrl.TeleportSafe(_data.playerPosition, _data.playerYaw, _data.playerPitch);
            return true;
        }

        /// <summary>After the first chunks are built: paint, items and placed pieces.</summary>
        public void RestoreWorld()
        {
            if (_data == null) { _restored = true; return; }
            // (A joined world's paint and chalk came with it from the host.)
            var paint = GameSession.NetClient ? GameSession.NetPaint : SaveSlots.ReadPaint(_data.slot);
            if (paint != null)
            {
                try { using (var r = new BinaryReader(new MemoryStream(paint))) SprayPaint.Instance.Read(r); }
                catch (Exception e) { Debug.LogWarning($"[Backrooms] Paint of save {_data.slot + 1} could not be read: {e.Message}"); }
            }
            var marks = GameSession.NetClient ? GameSession.NetMarks : SaveSlots.ReadMarks(_data.slot);
            if (marks != null)
            {
                try { using (var r = new BinaryReader(new MemoryStream(marks))) Marks.Instance.Read(r); }
                catch (Exception e) { Debug.LogWarning($"[Backrooms] Chalk and tape of save {_data.slot + 1} could not be read: {e.Message}"); }
            }
            StartCoroutine(RestoreThings());
        }

        IEnumerator RestoreThings()
        {
            yield return null;            // the player's hand is set up
            WorldItem held = null;
            int heldHome = -1;
            var spawned = new List<WorldItem>();
            var pocketed = new List<(WorldItem item, int slot)>();
            Backpack worn = null;
            foreach (var it in _data.items)
            {
                var item = SpawnItem(it);
                spawned.Add(item);
                // In a backpack: back where it lay in it (the bag comes before what is in it).
                if (item && it.bag > 0 && it.bag - 1 < spawned.Count && spawned[it.bag - 1] is Backpack bag) bag.AdoptSaved(item, it.position, it.rotation);
                else if (item && it.pocket >= 0) pocketed.Add((item, it.pocket));
                else if (item && it.held) { held = item; heldHome = it.home; }
                if (item is Backpack b && it.worn) worn = b;
            }
            yield return null;            // lamps make their plugs and cords
            RestorePlugs(spawned);
            if (_data.placed.Count > 0)
            {
                var catalog = ItemCatalog.Build(_streamer ? _streamer.assets : null, _streamer);
                foreach (var p in _data.placed)
                {
                    var e = catalog.Find(x => x.name == p.name);
                    if (e == null) continue;
                    var go = e.spawn(p.position, p.rotation, false);
                    if (go && !go.GetComponent<PlacedThing>()) go.AddComponent<PlacedThing>().catalogName = p.name;
                }
            }
            var grabber = _streamer && _streamer.player ? _streamer.player.GetComponent<PropGrabber>() : null;
            if (grabber) foreach (var (item, slot) in pocketed) grabber.PutInPocket(item, slot);
            if (grabber && worn) grabber.WearNow(worn);
            if (held && grabber) { grabber.PickUp(held); grabber.SetHeldHome(held, heldHome); }
            _restored = true;
        }

        internal static WorldItem SpawnItem(ItemData d)
        {
            var item = ItemFactory.Spawn(d.id, d.position, d.rotation);
            if (!item) { Debug.LogWarning($"[Backrooms] Saved item '{d.id}' is not known; left out."); return null; }
            if (item is SprayCan can) can.paint = Mathf.Clamp01(d.amount);
            else if (!(item is CordBox)) item.Amount = d.amount;
            return item;
        }

        /// <summary>Extension cords laid out again, then every plug back in its socket (a wall socket is found when its chunk loads).</summary>
        void RestorePlugs(List<WorldItem> spawned)
        {
            var data = _data.items;
            for (int i = 0; i < data.Count && i < spawned.Count; i++)
                if (spawned[i] is CordBox box && data[i].amount > 0.01f) box.Deploy(null, data[i].plugPosition, data[i].amount * CordBox.Length);
            for (int i = 0; i < data.Count && i < spawned.Count; i++)
            {
                if (!(spawned[i] is PowerUser user) || !data[i].hasPlug) continue;
                user.Unbundle();                       // a lamp's cord was out
                if (!user.Plug) continue;
                var plug = user.Plug;
                var rb = plug.Body;
                rb.position = data[i].plugPosition; rb.rotation = data[i].plugRotation;
                plug.transform.SetPositionAndRotation(data[i].plugPosition, data[i].plugRotation);
                int target = data[i].plugTarget;
                if (target == 0) plug.WaitForWall(data[i].plugWall);
                else if (target > 0 && target - 1 < spawned.Count && spawned[target - 1] is CordBox into && !into.coiled)
                {
                    var sockets = into.BoxSockets;
                    if (data[i].plugSocket >= 0 && data[i].plugSocket < sockets.Count) plug.PlugInto(sockets[data[i].plugSocket]);
                }
                user.WakeCord();
            }
        }

        // ---------------- Save ----------------

        public void Save()
        {
            if (_data == null || GameSession.NetClient) return;          // a joined world is the host's to save
            if (Collect(out var paint, out var marks)) SaveSlots.Write(_data, paint, marks);
        }

        /// <summary>
        /// Brings the save data up to date with the world as it is now (and its paint and chalk), without writing it: what
        /// Save writes, and what a co-op host sends a joining player (2026-11-04). False before what was saved is back.
        /// </summary>
        public bool Collect(out byte[] paint, out byte[] marks)
        {
            paint = null; marks = null;
            if (_data == null) return false;
            if (!_restored)
            {
                // Quitting before the saved things were put back: keep the file as it is.
                return false;
            }
            var d = _data;
            d.version = WorldSaveData.CurrentVersion;
            d.slot = GameSession.Slot >= 0 ? GameSession.Slot : d.slot;
            if (_streamer) d.seed = _streamer.Seed;
            d.playSeconds = PlaySeconds;
            d.saved = DateTime.Now.ToString("o");

            var player = _streamer ? _streamer.player : null;
            if (player)
            {
                var ctrl = player.GetComponent<CamcorderPlayerController>();
                d.hasPlayer = true;
                d.playerPosition = player.position;
                d.playerYaw = player.eulerAngles.y;
                d.playerPitch = ctrl ? ctrl.Pitch : 0f;
                d.stance = ctrl ? (int)ctrl.CurrentStance : 0;
                d.noclip = ctrl && ctrl.noclip;
                d.zone = ZoneName(player.position);
            }

            d.lightsOff = Generation.LightZones.Save();
            d.breakersOn = Interaction.PowerGrid.Save();
            d.homes = Interaction.Homes.Save();
            Interaction.DoorStates.Save(d.doorKeys, d.doorAngles, d.doorBolts);
            d.props.Clear();
            var mgr = MovablePropManager.Instance;
            if (mgr) foreach (var s in mgr.Export()) d.props.Add(FromSpec(s.spec, s.position, s.rotation));

            d.items.Clear();
            var users = new List<WorldItem>();
            foreach (var it in FindObjectsByType<WorldItem>())
            {
                // Plugs go with their lamp or cord; things still lying in a special room are its own (made again there);
                // what is in a backpack goes with it (below).
                if (string.IsNullOrEmpty(it.itemId) || it is Plug || it.IsLoot || it.InBag || it.NetForeign) continue;   // (another player's: saved with them)
                users.Add(it);
            }
            foreach (var it in users)
            {
                // A plug held in the hand is saved where the hand holds it, as an item would be.
                var data = new ItemData { id = it.itemId, position = it.transform.position, rotation = it.transform.rotation, held = it.Held, amount = it.Amount };
                if (it.Held && _streamer && _streamer.player && _streamer.player.GetComponent<PropGrabber>() is PropGrabber g && g.HeldItem == it) data.home = g.HeldHome;
                if (it is Backpack worn && worn.Worn) data.worn = true;
                if (it is SprayCan can) data.amount = can.paint;
                if (it is PowerUser user && user.Plug)
                {
                    var plug = user.Plug;
                    data.hasPlug = true; data.plugPosition = plug.transform.position; data.plugRotation = plug.transform.rotation;
                    if (plug.Plugged && (plug.Socket == null || plug.Socket.IsWall)) { data.plugTarget = 0; data.plugWall = plug.WallPosition; }
                    else if (plug.Socket != null && plug.Socket.box)
                    {
                        int bi = users.IndexOf(plug.Socket.box);
                        if (bi >= 0) { data.plugTarget = bi + 1; data.plugSocket = IndexOf(plug.Socket.box.BoxSockets, plug.Socket); }
                    }
                }
                d.items.Add(data);
            }
            // What is in each backpack (switched off while it is shut, so not found above), where it lies in it.
            for (int bi = 0; bi < users.Count; bi++)
            {
                if (!(users[bi] is Backpack bag)) continue;
                foreach (var it in bag.Contents)
                {
                    if (string.IsNullOrEmpty(it.itemId)) continue;
                    var data = new ItemData { id = it.itemId, position = it.transform.localPosition, rotation = it.transform.localRotation, amount = it.Amount, bag = bi + 1 };
                    if (it is SprayCan can) data.amount = can.paint;
                    d.items.Add(data);
                }
            }
            // Things in the pockets (switched off, so not found above).
            var hand = _streamer && _streamer.player ? _streamer.player.GetComponent<PropGrabber>() : null;
            if (hand)
                for (int i = 0; i < PropGrabber.PocketCount; i++)
                {
                    var it = hand.Pocket(i);
                    if (!it || string.IsNullOrEmpty(it.itemId)) continue;
                    var data = new ItemData { id = it.itemId, position = it.transform.position, rotation = it.transform.rotation, pocket = i, amount = it.Amount };
                    if (it is SprayCan can) data.amount = can.paint;
                    d.items.Add(data);
                }
            d.takenLoot = LootRegistry.Export();
            // Broken walls, with their rubble (and the bits still falling, where they are now).
            d.walls = Destruction.WallDamage.Export();
            d.nails = NailWork.Export();
            foreach (var deb in FindObjectsByType<Destruction.WallDebris>())
            {
                var w = d.walls.Find(x => x.vertical == deb.key.vertical && x.line == deb.key.line && x.along == deb.key.along && x.storey == deb.key.storey);
                w?.rubble.Add(new Destruction.WallDamage.Rubble { part = deb.part, side = deb.side, index = deb.index, span = deb.span, position = deb.transform.position, rotation = deb.transform.rotation });
            }
            if (NetGuests != null) d.guests = NetGuests() ?? d.guests;
            d.hasNoclipOrigin = NoclipTravel.HasOrigin; d.noclipOrigin = NoclipTravel.OriginPosition; d.noclipOriginNormal = NoclipTravel.OriginNormal;
            d.placed.Clear();
            foreach (var p in FindObjectsByType<PlacedThing>())
                d.placed.Add(new PlacedData { name = p.catalogName, position = p.transform.position, rotation = p.transform.rotation });

            var sp = SprayPaint.Existing;
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                if (sp) sp.Write(w); else SprayPaint.WriteEmpty(w);
                w.Flush();
                paint = ms.ToArray();
            }
            var mk = Marks.Existing;
            var ms2 = new MemoryStream();
            using (var w = new BinaryWriter(ms2))
            {
                if (mk) mk.Write(w); else Marks.WriteEmpty(w);
                w.Flush();
                marks = ms2.ToArray();
            }
            return true;
        }

        static int IndexOf(IReadOnlyList<Socket> list, Socket s)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == s) return i;
            return 0;
        }

        string ZoneName(Vector3 p)
        {
            var settings = _streamer ? _streamer.Settings : null;
            if (settings == null || settings.zones == null || settings.zones.Count == 0) return "";
            var hit = settings.ZoneAt(_streamer.Seed, WorldGenerator.DistrictOf(WorldStreamer.ChunkOf(p)));
            return settings.zones[Mathf.Clamp(hit.zone, 0, settings.zones.Count - 1)].name;
        }

        // ---------------- Props <-> data ----------------

        internal static PropData FromSpec(MovableSpec s, Vector3 position, Quaternion rotation)
        {
            var d = new PropData
            {
                id = s.id.ToString("X16"), kind = (int)s.kind, renderMask = s.renderMask, proxy = AssetKeys.Key(s.proxyMaterial),
                size = s.size, variant = s.variant, cardboard = s.cardboard, origin = s.position, originYaw = s.yaw,
                position = position, rotation = rotation, pinned = s.pinned, scale = s.scale, fitting = s.fitting ?? "",
            };
            if (s.materials != null) { d.materials = new string[s.materials.Length]; for (int i = 0; i < s.materials.Length; i++) d.materials[i] = AssetKeys.Key(s.materials[i]); }
            foreach (var p in s.parts)
                d.parts.Add(new PartData { prefab = AssetKeys.Key(p.prefab), material = AssetKeys.Key(p.material), position = p.position, scale = p.scale, yaw = p.yaw });
            return d;
        }

        internal static MovableSpec ToSpec(PropData d)
        {
            if (!ulong.TryParse(d.id, System.Globalization.NumberStyles.HexNumber, null, out ulong id)) return null;
            var s = new MovableSpec
            {
                id = id, kind = (MovableKind)d.kind, renderMask = (uint)d.renderMask, proxyMaterial = AssetKeys.Find<Material>(d.proxy),
                size = d.size, variant = d.variant, cardboard = d.cardboard, position = d.origin, yaw = d.originYaw,
                pinned = d.pinned, scale = d.scale > 0.01f ? d.scale : 1f, fitting = d.fitting ?? "",
            };
            // A machine saved while it had no model (2026-11-08): its model now (MachineUnit falls back to blocks without it).
            if (string.IsNullOrEmpty(s.fitting) && Interaction.Machines.Is(id)) s.fitting = Interaction.Machines.Model(Interaction.Machines.KindOf(id));
            if (d.materials != null) { s.materials = new Material[d.materials.Length]; for (int i = 0; i < d.materials.Length; i++) s.materials[i] = AssetKeys.Find<Material>(d.materials[i]); }
            foreach (var p in d.parts)
            {
                var prefab = AssetKeys.Find<GameObject>(p.prefab);
                var mat = AssetKeys.Find<Material>(p.material);
                if (!prefab && !mat) { Debug.LogWarning($"[Backrooms] A saved {s.kind} lost a part ('{p.prefab}{p.material}')."); continue; }
                s.parts.Add(new MovableSpec.Part { prefab = prefab, material = mat, position = p.position, scale = p.scale, yaw = p.yaw });
            }
            // (Props built from a model rather than parts come back too: a named fitting, the shelves and machines, the
            // chairs and carts built by their kind. Only those left with nothing to build them from are dropped.)
            if (s.parts.Count == 0 && !s.cardboard && s.kind != MovableKind.Pallet && string.IsNullOrEmpty(s.fitting) && !BuiltByKind(s.kind)) return null;
            return s;
        }

        static bool BuiltByKind(MovableKind k) =>
            k == MovableKind.Shelf || k == MovableKind.Generator || k == MovableKind.Conveyor || k == MovableKind.Floodlight
            || k == MovableKind.LawnChair || k == MovableKind.SchoolChair || CartLayouts.IsCart(k);
    }

    /// <summary>Names for the biome's prefabs and materials in a save: "field" or "field/index" of OfficeBiomeAssets.</summary>
    public static class AssetKeys
    {
        static OfficeBiomeAssets s_for;
        static readonly Dictionary<UnityEngine.Object, string> s_key = new Dictionary<UnityEngine.Object, string>();
        static readonly Dictionary<string, UnityEngine.Object> s_obj = new Dictionary<string, UnityEngine.Object>();

        static void Ensure()
        {
            var a = FindAssets();
            if (a == s_for && s_obj.Count > 0) return;
            s_for = a; s_key.Clear(); s_obj.Clear();
            if (!a) return;
            foreach (var f in typeof(OfficeBiomeAssets).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)) Add(f.Name, f.GetValue(a) as UnityEngine.Object);
                else if (f.FieldType.IsArray && typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType.GetElementType()) && f.GetValue(a) is Array arr)
                    for (int i = 0; i < arr.Length; i++) Add(f.Name + "/" + i, arr.GetValue(i) as UnityEngine.Object);
            }
        }

        static void Add(string key, UnityEngine.Object o)
        {
            if (!o) return;
            s_obj[key] = o;
            if (!s_key.ContainsKey(o)) s_key[o] = key;
        }

        static OfficeBiomeAssets FindAssets()
        {
            var ws = UnityEngine.Object.FindAnyObjectByType<WorldStreamer>();
            return ws ? ws.assets : null;
        }

        public static string Key(UnityEngine.Object o)
        {
            if (!o) return "";
            Ensure();
            return s_key.TryGetValue(o, out var k) ? k : "name:" + o.name;
        }

        public static T Find<T>(string key) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(key)) return null;
            Ensure();
            if (key.StartsWith("name:"))
            {
                string n = key.Substring(5);
                foreach (var o in s_obj.Values) if (o is T t && o.name == n) return t;
                // Furniture kept in Resources rather than the biome's assets (the floral sofa).
                return Resources.Load<T>("Furniture/" + n);
            }
            return s_obj.TryGetValue(key, out var v) ? v as T : null;
        }
    }

    /// <summary>Marks a fixed piece spawned from the developer menu, so it is saved with the world.</summary>
    public class PlacedThing : MonoBehaviour
    {
        public string catalogName;
    }
}
