using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Mains power by district: most rooms' outlets are dead until their district's breaker is switched on at the panel in
    /// a janitor's closet (<see cref="BreakerPanel"/>); good rooms and a few others have power of their own
    /// (SpecialRooms.SetLive). A circuit is a district of a storey: (district x, storey, district z). Saved with the world.
    /// </summary>
    public static class PowerGrid
    {
        static readonly HashSet<Vector3Int> s_on = new HashSet<Vector3Int>();

        /// <summary>Goes up whenever a breaker is switched.</summary>
        public static int Version { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { s_on.Clear(); AllLive = false; Version++; }

        /// <summary>The circuit of a chunk (x, storey, z).</summary>
        public static Vector3Int CircuitOf(Vector3Int chunk)
        {
            int d = WorldGenSettings.DistrictChunks;
            return new Vector3Int(Mathf.FloorToInt(chunk.x / (float)d), chunk.y, Mathf.FloorToInt(chunk.z / (float)d));
        }

        /// <summary>The contract floor (2026-11-08, Docs/GameDesign.md section 8): no breaker panels to hunt for; every wall
        /// outlet is always on (low power: lamps and the like; big machines need a generator). Set by Interaction.Contracts.</summary>
        public static bool AllLive;

        public static bool On(Vector3Int circuit) => AllLive || (s_on.Count > 0 && s_on.Contains(circuit));

        public static void Set(Vector3Int circuit, bool on)
        {
            if (on ? s_on.Add(circuit) : s_on.Remove(circuit))
            {
                Version++;
                if (Net.NetHooks.On) Net.NetHooks.Breaker?.Invoke(circuit, on);
            }
        }

        public static void Clear() { s_on.Clear(); Version++; }

        /// <summary>The circuits switched on, flattened as x, storey, z (for the save).</summary>
        public static List<int> Save()
        {
            var list = new List<int>();
            foreach (var c in s_on) { list.Add(c.x); list.Add(c.y); list.Add(c.z); }
            return list;
        }

        public static void Load(List<int> flat)
        {
            Clear();
            if (flat == null) return;
            for (int i = 0; i + 2 < flat.Count; i += 3) s_on.Add(new Vector3Int(flat[i], flat[i + 1], flat[i + 2]));
            Version++;
        }
    }
}
