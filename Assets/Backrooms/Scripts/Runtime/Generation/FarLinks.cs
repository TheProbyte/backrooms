using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Far crawl-hole links: pairs of districts, far apart, whose crawl holes lead into each other. Each end is placed by
    /// its own district (CrawlPortals.Place, from that district's layout only); a chunk cut from the plan finds its
    /// partner's exact hole in the other district's plan (WorldGenerator.ResolveFarLinks). Neither end needs anything in
    /// between to be generated or loaded: at runtime only a small site around the partner is streamed in.
    ///
    /// Two sources, both symmetric and deterministic:
    /// <list type="bullet">
    /// <item>The lattice: every square of <see cref="WorldGenSettings.farLinkSpacing"/> districts links its (1, 1) district
    /// to its (spacing - 2, spacing - 2) district.</item>
    /// <item><see cref="Add"/>: links placed by hand, for example the entrance to a far zone (a future Poolrooms). Register
    /// them before the world is generated.</item>
    /// </list>
    /// Pure and thread-safe after setup (generation runs on worker threads).
    /// </summary>
    public static class FarLinks
    {
        static readonly Dictionary<Vector2Int, Vector2Int> s_explicit = new Dictionary<Vector2Int, Vector2Int>();

        /// <summary>Links two districts by hand (both directions). Call before the world generates them.</summary>
        public static void Add(Vector2Int a, Vector2Int b)
        {
            if (a == b) return;
            s_explicit[a] = b; s_explicit[b] = a;
        }

        public static void ClearExplicit() => s_explicit.Clear();

        /// <summary>Is this district one end of a far link, and where is the other end?</summary>
        public static bool PartnerOf(WorldGenSettings set, Vector2Int district, out Vector2Int partner)
        {
            if (s_explicit.TryGetValue(district, out partner)) return true;
            int n = set != null ? set.farLinkSpacing : 0;
            if (n < 4) { partner = default; return false; }
            var square = new Vector2Int(FloorDiv(district.x, n), FloorDiv(district.y, n));
            var local = district - square * n;
            Vector2Int a = new Vector2Int(1, 1), b = new Vector2Int(n - 2, n - 2);
            if (local == a) { partner = square * n + b; return !s_explicit.ContainsKey(partner); }
            if (local == b) { partner = square * n + a; return !s_explicit.ContainsKey(partner); }
            partner = default;
            return false;
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
