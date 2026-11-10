using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The contract floor (2026-11-07, Docs/ContractFloorsPlan.md): the rented space Overflow sends you down to. One floor
    /// for the whole world (the owner: "the world is the same", random per world but kept forever), in a block of 8 x 8
    /// districts on the ground storey about 2.5-9.5 km west of the world's start, which block picked by the world's seed.
    /// Its layout is a pure function of seed and position and of what has been leased (co-op games agree on both).
    /// <code>
    ///        . N N N N .          the core (4 x 4 districts, about 230 m across) is leased from the start, the service
    ///        . N N N N .          lift in its middle; each wing (N, E, S, W: 4 x 2 districts) is sealed off (LeaseEdges:
    ///        W C C C C E          NOT LEASED, chained fire doors) until it is leased as an improvement
    ///        W C C C C E          (Interaction.Improvements); past a wing, a noclip door into the Poolrooms once that is
    ///        W C C C C E          leased too. The corners are never part of it. One storey: no multi-storey landmark.
    ///        W C C C C E
    ///        . S S S S .
    ///        . S S S S .
    /// </code>
    /// The plan of the whole floor, wings and all, never depends on what is leased (<see cref="FloorOf"/>): leasing a wing
    /// moves nothing that is already there (stored boxes, fitted chutes). A wing not leased is not streamed, and every way
    /// into it from the core is boarded up (OfficeChunkBuilder.LeaseGates, Interaction.LeaseGate: plywood hoarding,
    /// NOT LEASED) until it is. Only the Poolrooms door is planned by what is leased (its district's plan is made again).
    /// </summary>
    public static class LeasePlan
    {
        public const int Block = 8;
        public const int Cols = 15, Rows = 15, Count = Cols * Rows;
        /// <summary>Block (0, 0)'s south-west district.</summary>
        public static readonly Vector2Int Origin = new Vector2Int(-164, -60);
        public static readonly string[] WingNames = { "NORTH", "EAST", "SOUTH", "WEST" };
        const float C = OfficeBiome.CellSize;

        /// <summary>The world's floor (its block), or -1 outside a world.</summary>
        public static int Current = -1;
        /// <summary>The wings leased: bit 0 north, 1 east, 2 south, 3 west.</summary>
        public static int Wings;
        /// <summary>The way into the Poolrooms is leased (a noclip door past the first wing).</summary>
        public static bool Poolrooms;

        static int Wrap(int slot) => ((slot % Count) + Count) % Count;

        /// <summary>The world's floor: a block picked by its seed.</summary>
        public static int ForSeed(int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ 0x1EA5Eu;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
                return (int)(h % Count);
            }
        }

        /// <summary>The block's south-west district.</summary>
        static Vector2Int BlockOrigin(int slot)
        {
            int b = Wrap(slot);
            return Origin + new Vector2Int((b % Cols) * Block, (b / Cols) * Block);
        }

        /// <summary>The district whose south-west corner is the floor's middle: the lift's spawn hall is there.</summary>
        public static Vector2Int Centre(int slot) => BlockOrigin(slot) + new Vector2Int(Block / 2, Block / 2);

        /// <summary>The whole block (core, wings and corners).</summary>
        public static RectInt Rect(int slot) { var o = BlockOrigin(slot); return new RectInt(o.x, o.y, Block, Block); }

        /// <summary>The leased core.</summary>
        public static RectInt Core(int slot) { var o = BlockOrigin(slot); return new RectInt(o.x + 2, o.y + 2, 4, 4); }

        /// <summary>Which part of the block a district is: -1 a corner, 0..3 a wing (N, E, S, W), 4 the core.</summary>
        public static int PartOf(int slot, Vector2Int district)
        {
            var l = district - BlockOrigin(slot);
            bool midX = l.x >= 2 && l.x <= 5, midZ = l.y >= 2 && l.y <= 5;
            if (midX && midZ) return 4;
            if (midX) return l.y >= 6 ? 0 : 2;
            if (midZ) return l.x >= 6 ? 1 : 3;
            return -1;
        }

        public static bool WingLeased(int wing) => (Wings & (1 << wing)) != 0;
        public static int WingCount { get { int n = 0; for (int w = 0; w < 4; w++) if (WingLeased(w)) n++; return n; } }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        static int BlockOf(Vector2Int district)
        {
            int bx = FloorDiv(district.x - Origin.x, Block), bz = FloorDiv(district.y - Origin.y, Block);
            if (bx < 0 || bz < 0 || bx >= Cols || bz >= Rows) return -1;
            return bz * Cols + bx;
        }

        /// <summary>The floor this district is a part of (core or wing, leased or not), or -1: what plans go by.</summary>
        public static int FloorOf(Vector2Int district)
        {
            int slot = BlockOf(district);
            if (slot < 0 || Current < 0 || slot != Wrap(Current)) return -1;
            return PartOf(slot, district) >= 0 ? slot : -1;
        }

        /// <summary>The floor this district is a leased part of (the world's), or -1 (outside, a corner, a wing not leased):
        /// what streaming and the shift go by.</summary>
        public static int SlotOf(Vector2Int district)
        {
            int slot = FloorOf(district);
            if (slot < 0) return -1;
            int part = PartOf(slot, district);
            return part == 4 || WingLeased(part) ? slot : -1;
        }

        /// <summary>The wing a district is in (0..3), or -1 (the core, a corner, outside).</summary>
        public static int WingOf(Vector2Int district)
        {
            int slot = FloorOf(district);
            if (slot < 0) return -1;
            int part = PartOf(slot, district);
            return part < 4 ? part : -1;
        }

        /// <summary>Is this district in the blocks' region at all (a floor, a wing or the sealed space between them)?</summary>
        public static bool InRegion(Vector2Int district) => BlockOf(district) >= 0;

        public static bool InFloor(Vector2Int district) => SlotOf(district) >= 0;
        public static bool IsCentre(Vector2Int district) { int s = FloorOf(district); return s >= 0 && Centre(s) == district; }
        public static int SlotOfChunk(Vector2Int chunk) => SlotOf(WorldGenSettings.DistrictOf(chunk));
        public static int SlotAt(Vector3 p) => SlotOfChunk(new Vector2Int(Mathf.FloorToInt(p.x / OfficeBiome.ChunkSize), Mathf.FloorToInt(p.z / OfficeBiome.ChunkSize)));
        /// <summary>Is the point on the floor (a leased part of it)?</summary>
        public static bool OnCurrent(Vector3 p) => Current >= 0 && SlotAt(p) == Wrap(Current);
        /// <summary>Is this chunk column part of the floor?</summary>
        public static bool ChunkInCurrent(Vector2Int chunk) => Current >= 0 && SlotOfChunk(chunk) == Wrap(Current);

        /// <summary>A district's south-west corner in the world (m).</summary>
        public static Vector3 DistrictOrigin(Vector2Int d) => new Vector3(d.x * WorldGenSettings.DistrictCells * C, 0f, d.y * WorldGenSettings.DistrictCells * C);

        /// <summary>The floor's leased districts.</summary>
        public static IEnumerable<Vector2Int> Districts(int slot)
        {
            var r = Rect(slot);
            for (int x = r.xMin; x < r.xMax; x++)
            for (int z = r.yMin; z < r.yMax; z++)
            {
                var d = new Vector2Int(x, z);
                if (SlotOf(d) == Wrap(slot)) yield return d;
            }
        }

        /// <summary>The floor's chunk columns (leased parts).</summary>
        public static IEnumerable<Vector2Int> Chunks(int slot)
        {
            int DC = WorldGenSettings.DistrictChunks;
            foreach (var d in Districts(slot))
                for (int x = d.x * DC; x < (d.x + 1) * DC; x++)
                for (int z = d.y * DC; z < (d.y + 1) * DC; z++)
                    yield return new Vector2Int(x, z);
        }

        /// <summary>Should the edge between two neighbouring districts be a solid wall (one side the floor, the other not)?
        /// (Core and wings are one plan: a wing not leased is boarded up, not walled.)</summary>
        public static bool SealedBetween(Vector2Int a, Vector2Int b)
        {
            if (!InRegion(a) && !InRegion(b)) return false;
            return FloorOf(a) != FloorOf(b);
        }

        /// <summary>
        /// The district holding the way into the Poolrooms, once leased: the far middle of the first wing leased (N, E, S, W
        /// in that order), or none.
        /// </summary>
        public static bool PoolDoorAt(Vector2Int district)
        {
            if (!Poolrooms || Current < 0) return false;
            var o = BlockOrigin(Current);
            for (int w = 0; w < 4; w++)
            {
                if (!WingLeased(w)) continue;
                var at = o + (w == 0 ? new Vector2Int(3, 7) : w == 1 ? new Vector2Int(7, 4) : w == 2 ? new Vector2Int(4, 0) : new Vector2Int(0, 3));
                return at == district;
            }
            return false;
        }

        /// <summary>Where the Poolrooms door would be (the district for the first leased wing), for remaking its plan.</summary>
        public static Vector2Int? PoolDoorDistrict()
        {
            if (Current < 0) return null;
            var o = BlockOrigin(Current);
            for (int w = 0; w < 4; w++)
                if (WingLeased(w)) return o + (w == 0 ? new Vector2Int(3, 7) : w == 1 ? new Vector2Int(7, 4) : w == 2 ? new Vector2Int(4, 0) : new Vector2Int(0, 3));
            return null;
        }
    }
}
