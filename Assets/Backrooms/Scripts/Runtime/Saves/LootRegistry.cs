using System.Collections.Generic;

namespace Backrooms.Saves
{
    /// <summary>
    /// Items laid out in special rooms are part of their chunk: built with it each time it loads. Once one is picked up it
    /// is taken for good (a free item from then on, saved like any other) and its room does not make it again. The set of
    /// taken ones belongs to the world and is saved with it.
    /// </summary>
    public static class LootRegistry
    {
        static readonly HashSet<ulong> s_taken = new HashSet<ulong>();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear() => s_taken.Clear();

        public static void Take(ulong id) { if (id != 0) s_taken.Add(id); }
        public static bool IsTaken(ulong id) => s_taken.Contains(id);

        public static List<string> Export()
        {
            var l = new List<string>(s_taken.Count);
            foreach (var id in s_taken) l.Add(id.ToString("X16"));
            return l;
        }

        public static void Import(List<string> ids)
        {
            s_taken.Clear();
            if (ids == null) return;
            foreach (var s in ids) if (ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var id)) s_taken.Add(id);
        }
    }
}
