using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>What the floor is at a point, for footsteps: the room's floor finish from the layout (carpet, wood or
    /// laminate), the Poolrooms' tile, the sheet metal of a crawl duct, the plywood of the rooms above the ceilings.</summary>
    public static class Surfaces
    {
        static readonly RaycastHit[] s_hits = new RaycastHit[6];

        public static ProceduralSounds.Floor At(Vector3 p, Transform ignore = null)
        {
            if (PoolRipples.PlayerInWater) return ProceduralSounds.Floor.Water;
            // A duct: its floor is one of the duct's own colliders.
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.3f, Vector3.down, s_hits, 0.8f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Collider under = null;
            for (int i = 0; i < n; i++)
            {
                var h = s_hits[i];
                if (ignore && h.collider.transform.IsChildOf(ignore)) continue;
                if (h.distance < best) { best = h.distance; under = h.collider; }
            }
            if (under && under.transform.parent && under.transform.parent.name == "DuctColliders") return ProceduralSounds.Floor.Metal;
            var ws = WorldStreamer.Current;
            if (!ws) return ProceduralSounds.Floor.Carpet;
            var L = ws.LayoutAt(WorldStreamer.ChunkOf(p));
            if (L == null) return ProceduralSounds.Floor.Concrete;
            if (L.style == ZoneStyle.Pool) return ProceduralSounds.Floor.Tile;
            int x = Mathf.FloorToInt(p.x / OfficeBiome.CellSize) - L.coord.x * L.N, z = Mathf.FloorToInt(p.z / OfficeBiome.CellSize) - L.coord.y * L.N;
            if (!L.InChunk(x, z)) return ProceduralSounds.Floor.Carpet;
            var room = L.RoomAt(x, z);
            if (p.y > L.BaseY + room.ceiling + 0.2f) return ProceduralSounds.Floor.Wood;          // above the ceiling: a special room
            switch (room.floorFinish)
            {
                case FloorFinish.DarkWood:
                case FloorFinish.Laminate: return ProceduralSounds.Floor.Wood;
            }
            return ProceduralSounds.Floor.Carpet;
        }
    }
}
