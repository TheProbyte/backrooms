using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The channels biome of the Poolrooms zone (2026-11-01, the review's item 15; reference image 5 of the known gaps):
    /// narrow tiled walkways winding beside water. Its corridors are all vaulted channels, flooded, with the walkway on
    /// one side (the side changing from one corridor to the next, so the way winds from bank to bank); its halls are
    /// flooded but for a walkway meandering through them from one end to the other, a cell wide, stepping sideways every
    /// few metres. The water is the basins' usual depth, so it can be waded where the walkway does not go.
    /// </summary>
    public static partial class PoolPlan
    {
        public static bool IsChannels(OfficeLayout L, RectInt rc) => L.RulesAt(rc).name == "Channels";

        /// <summary>Plans a room of the channels biome; false leaves it to the usual rules.</summary>
        static bool ChannelRoom(OfficeLayout L, Room r, System.Random rng)
        {
            var rc = r.rect;
            int w = rc.width, h = rc.height, mn = Mathf.Min(w, h), mx = Mathf.Max(w, h);
            if (r.kind == RoomKind.Hallway && mn <= 3 && mx >= 4) { Vault(L, r, rng, true); return true; }
            if (mn < 4 || mx < 5) return false;
            // A hall of water with a walkway winding through it along its length.
            FillCells(L, rc, c => c.basin = true);
            bool alongX = w >= h;
            int across = alongX ? h : w, length = alongX ? w : h;
            int lane = 1 + rng.Next(Mathf.Max(1, across - 2));
            int next = 2 + rng.Next(3);
            for (int i = 0; i < length; i++)
            {
                Deck(L, rc, alongX, i, lane);
                if (i == next && i < length - 1)
                {
                    int to = Mathf.Clamp(lane + (rng.NextDouble() < 0.5 ? -1 : 1) * (1 + rng.Next(2)), 0, across - 1);
                    if (to == lane) to = lane > 0 ? lane - 1 : lane + 1;
                    for (int k = Mathf.Min(lane, to); k <= Mathf.Max(lane, to); k++) Deck(L, rc, alongX, i, k);
                    lane = to;
                    next = i + 2 + rng.Next(3);
                }
            }
            return true;
        }

        static void Deck(OfficeLayout L, RectInt rc, bool alongX, int i, int k)
        {
            int x = alongX ? rc.xMin + i : rc.xMin + k, z = alongX ? rc.yMin + k : rc.yMin + i;
            var c = L.pool[x, z]; c.basin = false; L.pool[x, z] = c;
        }
    }
}
