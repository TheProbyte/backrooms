using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Rooms the player has made home (Docs/BaseRoomsPlan.md). Nothing to press: a room becomes home when a lamp of yours (one
    /// you have carried, not one still where the world put it) is left lit in it (plugged in, switched on, not in the hand)
    /// with every way in shut by a door, for a few seconds. The camcorder
    /// stamps HOME once. Any number of homes. A room is named by its district, storey and its id in the district plan.
    /// Inside a home the building's hum and room tone drop (BuildingAudio.HomeQuiet). Saved with the world.
    /// What you leave there stays as you left it (items, lights, doors and paint are already saved).
    /// </summary>
    public static class Homes
    {
        public struct Key
        {
            public Vector2Int district;
            public int storey, room;
            public (int, int, int, int) Tuple => (district.x, storey, district.y, room);
        }

        static readonly HashSet<(int, int, int, int)> s_homes = new HashSet<(int, int, int, int)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { s_homes.Clear(); InHome = false; }

        public static int Count => s_homes.Count;
        /// <summary>The player is in one of their homes right now.</summary>
        public static bool InHome { get; internal set; }

        public static bool IsHome(Key k) => s_homes.Contains(k.Tuple);
        public static bool Add(Key k)
        {
            if (!s_homes.Add(k.Tuple)) return false;
            if (Net.NetHooks.On) Net.NetHooks.Home?.Invoke(k);
            return true;
        }
        public static void Clear() { s_homes.Clear(); InHome = false; }

        public static List<int> Save()
        {
            var list = new List<int>();
            foreach (var (x, s, z, r) in s_homes) { list.Add(x); list.Add(s); list.Add(z); list.Add(r); }
            return list;
        }

        public static void Load(List<int> flat)
        {
            Clear();
            if (flat == null) return;
            for (int i = 0; i + 3 < flat.Count; i += 4) s_homes.Add((flat[i], flat[i + 1], flat[i + 2], flat[i + 3]));
        }

        // ---------------- Rooms in the world ----------------

        /// <summary>The room at a world point (in a loaded chunk), its key, and the chunk layout it was found in.</summary>
        public static bool RoomAt(WorldStreamer ws, Vector3 p, out Key key, out Room room)
        {
            key = default; room = null;
            if (!ws) return false;
            float pitch = ws.Settings != null ? ws.Settings.storeyPitch : 9f;
            int storey = Mathf.FloorToInt((p.y + 1.5f) / pitch);
            var ch = WorldStreamer.ChunkOf(p);
            var L = ws.LayoutAt(new Vector3Int(ch.x, storey, ch.y));
            if (L == null) return false;
            float C = OfficeBiome.CellSize;
            int cx = Mathf.FloorToInt(p.x / C) - ch.x * L.N, cz = Mathf.FloorToInt(p.z / C) - ch.y * L.N;
            if (!L.InChunk(cx, cz)) return false;
            room = L.RoomAt(cx, cz);
            int dc = WorldGenSettings.DistrictChunks;
            key = new Key { district = new Vector2Int(Mathf.FloorToInt(ch.x / (float)dc), Mathf.FloorToInt(ch.y / (float)dc)), storey = storey, room = room.globalId };
            return room.Walkable && room.kind != RoomKind.Crawl;
        }

        /// <summary>
        /// Is every way into the room shut by a door? Walks the room's whole outline (in the district plan's cells, across
        /// chunks): each opening must hold a shut door. False if part of it is not loaded, or it opens onto open plan.
        /// </summary>
        public static bool Enclosed(WorldStreamer ws, Key key, Room room)
        {
            var fr = room.fullRect;
            int dcells = WorldGenSettings.DistrictCells, n = OfficeBiome.ChunkCells;
            int ox = key.district.x * dcells, oz = key.district.y * dcells;
            int doors = 0;
            for (int x = fr.xMin; x < fr.xMax; x++)
                for (int z = fr.yMin; z < fr.yMax; z++)
                    for (int s = 0; s < 4; s++)
                    {
                        if (s == 0 && x != fr.xMin || s == 1 && x != fr.xMax - 1 || s == 2 && z != fr.yMin || s == 3 && z != fr.yMax - 1) continue;
                        int wx = ox + x, wz = oz + z;
                        int chx = Mathf.FloorToInt(wx / (float)n), chz = Mathf.FloorToInt(wz / (float)n);
                        var L = ws.LayoutAt(new Vector3Int(chx, key.storey, chz));
                        if (L == null) return false;
                        var e = L.EdgeOf(wx - chx * n, wz - chz * n, s);
                        if (e == EdgeKind.Wall) continue;
                        if (e == EdgeKind.Open) return false;
                        bool vertical = s < 2;
                        int line = s == 0 ? wx : s == 1 ? wx + 1 : s == 2 ? wz : wz + 1;
                        int along = vertical ? wz : wx;
                        if (!DoorEdges.IsShut(vertical, line, along, key.storey)) return false;
                        doors++;
                    }
            return doors > 0;
        }
    }

    /// <summary>Watches for a home being made and for the player being in one (added by the WorldStreamer while a save is played).</summary>
    public class HomeTracker : MonoBehaviour
    {
        [Tooltip("Seconds a lamp must stay lit in a shut room before it becomes home.")]
        public float settle = 3f;

        WorldStreamer _ws;
        float _next;
        readonly Dictionary<(int, int, int, int), float> _since = new Dictionary<(int, int, int, int), float>();

        void Awake() => _ws = GetComponent<WorldStreamer>();

        void OnDisable()
        {
            Homes.InHome = false;
            Audio.BuildingAudio.HomeQuiet = 1f;
        }

        void Update()
        {
            // The hum eases down inside a home, back up outside.
            float want = Homes.InHome ? 0.5f : 1f;
            Audio.BuildingAudio.HomeQuiet = Mathf.MoveTowards(Audio.BuildingAudio.HomeQuiet, want, Time.deltaTime * 0.35f);

            if (Time.time < _next || !_ws) return;
            _next = Time.time + 0.5f;
            var cam = Camera.main;
            Homes.InHome = cam && Homes.RoomAt(_ws, cam.transform.position - Vector3.up * 1.2f, out var here, out _) && Homes.IsHome(here);

            var seen = new HashSet<(int, int, int, int)>();
            foreach (var u in FindObjectsByType<PowerUser>(FindObjectsSortMode.None))
            {
                if (u.Held || u.IsLoot || !(u is DeskLamp d && d.Lit || u is FloorLamp f && f.Lit)) continue;   // (a lamp you have carried)
                if (!Homes.RoomAt(_ws, u.transform.position + Vector3.up * 0.1f, out var key, out var room) || Homes.IsHome(key)) continue;
                var t = key.Tuple;
                if (!seen.Add(t)) continue;
                if (!Homes.Enclosed(_ws, key, room)) { _since.Remove(t); continue; }
                if (!_since.TryGetValue(t, out float at)) { _since[t] = Time.time; continue; }
                if (Time.time - at < settle) continue;
                _since.Remove(t);
                Homes.Add(key);
                Camcorder.CamcorderOSD.Notice(Homes.Count == 1 ? "HOME" : "HOME  " + Homes.Count);
                Saves.WorldSaver.SaveNow();
            }
            // Forget rooms whose lamp went out or left.
            var stale = new List<(int, int, int, int)>();
            foreach (var k in _since.Keys) if (!seen.Contains(k)) stale.Add(k);
            foreach (var k in stale) _since.Remove(k);
        }
    }
}
