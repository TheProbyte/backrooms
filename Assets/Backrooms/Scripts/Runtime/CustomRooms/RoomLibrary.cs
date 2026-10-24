using System;
using System.Collections.Generic;
using System.IO;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// The player's custom rooms, one JSON file each in <c>persistentDataPath/CustomRooms</c>. The world generator reads the
    /// published snapshot (<see cref="Rules"/>, <see cref="Get"/>) from its worker threads, so the snapshot is replaced as a
    /// whole and the blueprints in it are never modified after they are published. Call <see cref="Reload"/> on the main
    /// thread (the world does it when it starts; the builder after every save).
    /// </summary>
    public static class RoomLibrary
    {
        sealed class Snapshot
        {
            public RoomBlueprint[] all = new RoomBlueprint[0];
            public Dictionary<string, RoomBlueprint> byId = new Dictionary<string, RoomBlueprint>();
            public LandmarkRules[] rules = new LandmarkRules[0];
        }

        static volatile Snapshot _snap = new Snapshot();
        static string _dir;
        static bool _loaded;

        /// <summary>Where the files are. Set by <see cref="Reload"/> (main thread) so worker threads never ask Unity for it.</summary>
        public static string Dir
        {
            get { if (_dir == null) _dir = Path.Combine(Application.persistentDataPath, "CustomRooms"); return _dir; }
        }

        /// <summary>Test hook: use another folder.</summary>
        public static void SetDirectory(string dir) { _dir = dir; _loaded = false; Publish(new List<RoomBlueprint>()); }

        public static IReadOnlyList<RoomBlueprint> All { get { EnsureLoaded(); return _snap.all; } }

        /// <summary>Landmark rules of the enabled custom rooms (read by the generator).</summary>
        public static LandmarkRules[] Rules => _snap.rules;

        public static RoomBlueprint Get(string id)
        {
            if (id == null) return null;
            _snap.byId.TryGetValue(id, out var b);
            return b;
        }

        static void EnsureLoaded() { if (!_loaded) Reload(); }

        public static void Reload()
        {
            var list = new List<RoomBlueprint>();
            try
            {
                if (Directory.Exists(Dir))
                    foreach (var f in Directory.GetFiles(Dir, "*.json"))
                    {
                        try
                        {
                            var b = RoomBlueprint.FromJson(File.ReadAllText(f));
                            if (b == null) continue;
                            if (string.IsNullOrEmpty(b.id)) b.id = Path.GetFileNameWithoutExtension(f);
                            list.Add(b);
                        }
                        catch (Exception e) { Debug.LogWarning($"[Backrooms] Could not read custom room {Path.GetFileName(f)}: {e.Message}"); }
                    }
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not list custom rooms: " + e.Message); }
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            Publish(list);
            _loaded = true;
        }

        static void Publish(List<RoomBlueprint> list)
        {
            var s = new Snapshot { all = list.ToArray() };
            var rules = new List<LandmarkRules>();
            foreach (var b in list)
            {
                s.byId[b.id] = b;
                if (b.enabled && b.variants.Count > 0) rules.Add(b.ToRules());
            }
            s.rules = rules.ToArray();
            _snap = s;
        }

        public static string NewId() => "room_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>Writes the blueprint (a copy is published, so the caller may keep editing its own object).</summary>
        public static void Save(RoomBlueprint bp)
        {
            if (string.IsNullOrEmpty(bp.id)) bp.id = NewId();
            bp.Validate();
            bp.modified = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, bp.id + ".json");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, bp.ToJson());
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            var list = new List<RoomBlueprint>(All);
            int i = list.FindIndex(x => x.id == bp.id);
            var copy = bp.Clone();
            if (i >= 0) list[i] = copy; else list.Add(copy);
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            Publish(list);
        }

        public static void Delete(string id)
        {
            try { string path = Path.Combine(Dir, id + ".json"); if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not delete custom room: " + e.Message); }
            var list = new List<RoomBlueprint>(All);
            list.RemoveAll(x => x.id == id);
            Publish(list);
        }

        public static RoomBlueprint Duplicate(string id)
        {
            var src = Get(id);
            if (src == null) return null;
            var c = src.Clone();
            c.id = NewId(); c.name = src.name + " copy"; c.enabled = false;
            Save(c);
            return Get(c.id);
        }
    }
}
