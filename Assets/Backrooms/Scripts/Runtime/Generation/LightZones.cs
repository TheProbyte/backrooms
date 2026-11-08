using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Ceiling lights switched off at a wall switch (Interaction.LightSwitch). A zone is one room of one chunk: the chunk
    /// (x, storey, z) and the room's index in that chunk's layout, so a room that runs on into the next chunk is switched
    /// in this chunk's part only. Switched off, its tubes go dark at once; switched on again, they strike (a flicker each,
    /// at slightly different moments) and light. The light manager multiplies a fixture's power by <see cref="Level"/>;
    /// the room's lens mesh (<see cref="RoomLens"/>) shows the lit or unlit lens. Saved with the world.
    /// </summary>
    public static class LightZones
    {
        struct Zone { public bool off; public float at; }

        static readonly Dictionary<(Vector3Int, int), Zone> s_zones = new Dictionary<(Vector3Int, int), Zone>();
        /// <summary>Rooms the shadow (Atmosphere.ShadowStalker) is in: their tubes die while it is there and strike again a few
        /// seconds after it has gone. Not a switch: not saved, and a switch does not bring them back.</summary>
        static readonly Dictionary<(Vector3Int, int), Zone> s_drained = new Dictionary<(Vector3Int, int), Zone>();
        static readonly HashSet<Vector3Int> s_chunks = new HashSet<Vector3Int>();

        /// <summary>Goes up with every switch (for anything that caches what it shows).</summary>
        public static int Version { get; private set; }

        public static bool IsOff(Vector3Int chunk, int room) => s_zones.TryGetValue((chunk, room), out var z) && z.off;

        /// <summary>Closing time on the contract floors (Interaction.Contracts): when it came (Time.time), or -1.</summary>
        public static float ClosingAt = -1f;

        /// <summary>After closing every light on a contract floor dies, each at its own moment over a few seconds.</summary>
        public static float ClosingLevel(Vector3Int chunk, float now, float seed)
        {
            if (ClosingAt < 0f || !LeasePlan.InRegion(WorldGenSettings.DistrictOf(new Vector2Int(chunk.x, chunk.z)))) return 1f;
            float t = now - ClosingAt - seed * 6f;
            if (t < 0f) return 1f;
            if (t > 0.6f) return 0f;
            return t < 0.15f || (t > 0.3f && t < 0.38f) ? 0.15f : 0.7f;      // a stutter, then out
        }

        /// <summary>Has any room of this chunk been switched (a quick test before the per-room one).</summary>
        public static bool Touched(Vector3Int chunk) => s_chunks.Contains(chunk);

        /// <summary>Switches a zone. instant: no flicker (restoring a save).</summary>
        public static void Set(Vector3Int chunk, int room, bool off, bool instant = false)
        {
            if (room < 0) return;
            s_zones[(chunk, room)] = new Zone { off = off, at = instant ? -100f : Time.time };
            s_chunks.Add(chunk);
            Version++;
            if (Net.NetHooks.On) Net.NetHooks.Light?.Invoke(chunk, room, off, instant);      // (co-op: the others' lights too)
        }

        /// <summary>
        /// The shadow comes into a room (on) or leaves it (off). Coming in, the room's tubes stutter out one after another
        /// over about a second; after it has gone they strike again, 2.5 to 4 seconds later, each at its own moment.
        /// </summary>
        public static void Drain(Vector3Int chunk, int room, bool on)
        {
            if (room < 0) return;
            if (s_drained.TryGetValue((chunk, room), out var d) && d.off == on) return;
            s_drained[(chunk, room)] = new Zone { off = on, at = Time.time };
            s_chunks.Add(chunk);
            Version++;
            if (Net.NetHooks.On) Net.NetHooks.Drain?.Invoke(chunk, room, on);
        }

        /// <summary>Is the shadow in this room, and how long since it came (s)?</summary>
        public static bool Drained(Vector3Int chunk, int room, out float since)
        {
            since = 0f;
            if (!s_drained.TryGetValue((chunk, room), out var d) || !d.off) return false;
            since = Time.time - d.at;
            return true;
        }

        /// <summary>0..1 for a fixture of that zone at time now (1 if the zone was never switched). seed 0..1 per tube.</summary>
        public static float Level(Vector3Int chunk, int room, float now, float seed)
        {
            if (room < 0 || !s_chunks.Contains(chunk)) return 1f;
            float level = 1f;
            if (s_zones.TryGetValue((chunk, room), out var z))
            {
                float t = now - z.at;
                level = z.off ? (t < 0.04f ? 1f - t / 0.04f : 0f) : Strike(t - seed * 0.3f, seed);
            }
            if (level > 0f && s_drained.TryGetValue((chunk, room), out var d))
            {
                float t = now - d.at;
                if (d.off)
                {
                    // Out one after another (seed), each with a stutter as it goes.
                    t -= seed * 0.7f;
                    if (t > 0f) level *= t > 0.35f ? 0f : (Mathf.PerlinNoise(t * 30f, seed * 13f) > 0.5f ? 1f - t / 0.35f : 0.08f);
                }
                else level *= Strike(t - 2.5f - seed * 1.5f, seed);
            }
            return level;
        }

        /// <summary>A fluorescent tube switched on: dark a moment, a blink or two, a dim glow, then full.</summary>
        static float Strike(float t, float seed)
        {
            if (t < 0f) return 0f;
            if (t > 0.75f) return 1f;
            float blink1 = t > 0.06f && t < 0.1f ? 0.85f : 0f;
            float blink2 = t > 0.22f + seed * 0.1f && t < 0.27f + seed * 0.1f ? 0.6f : 0f;
            float glow = t > 0.4f ? Mathf.SmoothStep(0.3f, 1f, (t - 0.4f) / 0.35f) : 0f;
            return Mathf.Max(blink1, Mathf.Max(blink2, glow));
        }

        /// <summary>A new world: every light on.</summary>
        public static void Clear() { s_zones.Clear(); s_drained.Clear(); s_chunks.Clear(); Version++; }

        /// <summary>The zones switched off, flattened as x, storey, z, room (for the save).</summary>
        public static List<int> Save()
        {
            var list = new List<int>();
            foreach (var kv in s_zones)
            {
                if (!kv.Value.off) continue;
                var (c, room) = kv.Key;
                list.Add(c.x); list.Add(c.y); list.Add(c.z); list.Add(room);
            }
            return list;
        }

        public static void Load(List<int> flat)
        {
            Clear();
            if (flat == null) return;
            for (int i = 0; i + 3 < flat.Count; i += 4) Set(new Vector3Int(flat[i], flat[i + 1], flat[i + 2]), flat[i + 3], true, true);
        }
    }
}
