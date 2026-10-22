using System;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Rendering-layer bookkeeping for unshadowed lights. Each light zone (rooms joined by open-plan
    /// edges) has a colour; a surface only receives lights whose layer bit it carries, so an unshadowed
    /// light cannot shine through a wall into a room of another colour.
    /// Bit 0 is Unity's "Default" layer: every renderer has it, no fixture light uses it.
    /// </summary>
    public static class LightLayers
    {
        /// <summary>Colours per chunk.</summary>
        public const int Colors = 7;

        /// <summary>Renderer mask for a room colour (always includes Default).</summary>
        public static uint LayerBit(int color) => color < 0 ? 1u : (1u | (1u << (color + 1)));

        /// <param name="neighbour">Layout of the chunk at (dx, dz) from this one, or null if unknown.</param>
        public static uint[,] CellMasks(OfficeLayout L, Func<int, int, OfficeLayout> neighbour)
        {
            int n = L.N;
            var mask = new uint[n, n];
            // An open-plan area that crosses into a neighbouring chunk is lit by that chunk's lights too:
            // its surfaces also take the neighbouring zone's layer.
            var extra = new uint[Colors + 1];
            if (neighbour != null)
            {
                void Link(int x, int z, EdgeKind e, int dx, int dz)
                {
                    if (e != EdgeKind.Open) return;
                    var nl = neighbour(dx, dz);
                    if (nl == null) return;
                    int nx = (x + dx + n) % n, nz = (z + dz + n) % n;
                    int c = L.RoomAt(x, z).color, nc = nl.RoomAt(nx, nz).color;
                    if (c >= 0 && nc >= 0 && nc != c) extra[c] |= 1u << (nc + 1);
                }
                for (int i = 0; i < n; i++)
                {
                    Link(0, i, L.vEdges[0, i], -1, 0);
                    Link(n - 1, i, L.vEdges[n, i], 1, 0);
                    Link(i, 0, L.hEdges[i, 0], 0, -1);
                    Link(i, n - 1, L.hEdges[i, n], 0, 1);
                }
            }
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                int c = L.RoomAt(x, z).color;
                mask[x, z] = LayerBit(c) | (c >= 0 ? extra[c] : 0u);
            }
            return mask;
        }

        // ---- Reach of each kind of unshadowed light (defaults of FixtureLightManager), metres ----

        public const float TubeRange = 6f, BounceRange = 4.4f, DoorwayRange = 4.5f, LampRange = 5f;

        /// <summary>Range of a ceiling fixture's main light (domes and strips throw farther in tall rooms).</summary>
        public static float FixtureRange(in Fixture f)
        {
            float extra = f.height - OfficeBiome.CeilingHeight;
            return f.dome || f.strip ? TubeRange + extra * 1.3f : TubeRange;
        }

        public static float BounceReach(in Fixture f) => f.dome || f.strip ? BounceRange * 1.6f : BounceRange;
    }
}
