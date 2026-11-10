using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>The water of a Poolrooms room in build mode: a pool inset with a deck round it, the whole room flooded, or
    /// only a tiled deck.</summary>
    public enum PoolShape { Inset, Flooded, Deck }

    /// <summary>The roof of a Poolrooms room in build mode: closed, square or round openings to the sky, one long strip, or
    /// open over the whole room.</summary>
    public enum PoolRoof { Closed, Squares, Rounds, Strip, Open }

    /// <summary>
    /// A build-mode room made in the Poolrooms' style (2026-11-04): its tiles, its water (shape, how deep, sloping, lanes),
    /// its roof openings and its fittings. A blueprint with one spawns in the Poolrooms' districts (its zones get
    /// "Poolrooms"); the builder's preview builds it as the Poolrooms would.
    /// </summary>
    [System.Serializable]
    public class PoolRoomSettings
    {
        [Tooltip("A Poolrooms room: tiled, with water and the Poolrooms' light.")] public bool enabled;
        [Tooltip("Its tiles: -1 the world's choice, 0 sage, 1 cream, 2 mint, 3 aqua.")] public int tiles = -1;
        public PoolShape shape = PoolShape.Inset;
        [Tooltip("Cells of deck round an inset pool.")] public int deck = 1;
        [Tooltip("Depth at the shallow end (m).")] public float shallow = 0.9f;
        [Tooltip("Depth at the deep end (m).")] public float deep = 1.8f;
        [Tooltip("The floor slopes from the shallow end down to the deep end (else all deep).")] public bool slope = true;
        [Tooltip("Lanes marked on the pool's floor and roped (0: none).")] public int lanes;
        public PoolRoof roof = PoolRoof.Squares;
        [Tooltip("Chrome ladders into the water.")] public bool ladders = true;
        [Tooltip("A lifeguard's chair on the deck.")] public bool lifeguard;
        [Tooltip("Showers on a wall.")] public bool showers;
        [Tooltip("Tiled bleachers with aluminium planks.")] public bool bleachers;
        [Tooltip("Plastic lawn chairs on the deck.")] public bool lawnChairs = true;
        [Tooltip("A drain in the deep end's floor.")] public bool drain = true;

        public PoolRoomSettings Clone() => (PoolRoomSettings)MemberwiseClone();
    }

    /// <summary>
    /// Stamps build mode's Poolrooms rooms into a layout (after the district's own pool plan, which it overrides there):
    /// each cell of the room deck or basin (its depth, its lanes, a drain), its roof openings, its tile colour, and its
    /// fittings as the Poolrooms' own pieces (ladders at the pool's edge, a lifeguard's chair, showers, bleachers) and lawn
    /// chairs on the deck.
    /// </summary>
    public static class PoolRoomStamp
    {
        const float C = OfficeBiome.CellSize;

        public static bool Any(RoomVariant v)
        {
            foreach (var r in v.rooms) if (r.pool != null && r.pool.enabled) return true;
            return false;
        }

        public static void Apply(OfficeLayout L, in LandmarkPlacement p, RoomVariant v, CustomRoomStamp.Xf xf, Vector2Int origin)
        {
            if (!Any(v)) return;
            int n = L.N;
            if (L.pool == null) L.pool = new PoolCell[n, n];
            for (int ri = 0; ri < v.rooms.Count; ri++)
            {
                var ps = v.rooms[ri].pool;
                if (ps == null || !ps.enabled) continue;
                // The room's cells, in the layout, and which of them are its pool.
                var cells = new List<Vector2Int>();
                var set = new HashSet<Vector2Int>();
                for (int z = 0; z < v.depth; z++)
                for (int x = 0; x < v.width; x++)
                {
                    if (v.RoomOf(x, z) != ri) continue;
                    var c = xf.Cell(x, z) + origin;
                    if (!L.InChunk(c.x, c.y)) continue;
                    cells.Add(c); set.Add(c);
                }
                if (cells.Count == 0) continue;
                int deck = Mathf.Clamp(ps.deck, 1, 4);
                bool Basin(Vector2Int c)
                {
                    if (ps.shape == PoolShape.Deck) return false;
                    if (ps.shape == PoolShape.Flooded) return true;
                    for (int dx = -deck; dx <= deck; dx++)
                    for (int dz = -deck; dz <= deck; dz++)
                        if (!set.Contains(new Vector2Int(c.x + dx, c.y + dz))) return false;
                    return true;
                }
                var basin = new HashSet<Vector2Int>();
                foreach (var c in cells) if (Basin(c)) basin.Add(c);
                // The pool's extent and its long axis: the depth runs along it, shallow to deep.
                RectInt bb = Bounds(basin.Count > 0 ? (IEnumerable<Vector2Int>)basin : cells);
                bool alongX = bb.width >= bb.height;
                float shallow = Mathf.Clamp(ps.shallow, 0.3f, 4f), deep = Mathf.Clamp(Mathf.Max(ps.deep, shallow), 0.3f, 4.5f);
                Vector2Int deepest = default; float dmax = -1f;
                foreach (var c in cells)
                {
                    var pc = new PoolCell();             // (what the district's plan put there goes: no galleries or windows)
                    bool b = basin.Contains(c);
                    pc.basin = b;
                    float t = alongX ? Mathf.InverseLerp(bb.xMin, bb.xMax - 1, c.x) : Mathf.InverseLerp(bb.yMin, bb.yMax - 1, c.y);
                    pc.depth = b ? (ps.slope ? Mathf.Lerp(shallow, deep, Mathf.SmoothStep(0f, 1f, t)) : deep) : 0f;
                    pc.lanes = (byte)(b ? Mathf.Clamp(ps.lanes, 0, 8) : 0);
                    pc.drain = false;
                    pc.skylight = Roof(ps.roof, c, cells, set, bb, alongX);
                    L.pool[c.x, c.y] = pc;
                    if (b && pc.depth > dmax) { dmax = pc.depth; deepest = c; }
                }
                if (ps.drain && dmax > 0f) { var d = L.pool[deepest.x, deepest.y]; d.drain = true; L.pool[deepest.x, deepest.y] = d; }
                // Its rooms in the layout: their tiles; the plan's own pieces in them go (the room has its own).
                foreach (var c in cells) { var r = L.RoomAt(c.x, c.y); if (r != null) r.poolTile = ps.tiles; }
                L.poolFeatures.RemoveAll(f => set.Contains(new Vector2Int(Mathf.FloorToInt(f.centre.x / C), Mathf.FloorToInt(f.centre.y / C))));
                // The plan's round columns inside it go too (the blueprint's own pillars stay).
                var own = new HashSet<Vector2Int>();
                foreach (var pl in v.pillars) own.Add(xf.Vertex(pl.x, pl.z) + origin);
                L.pillars.RemoveAll(q => !own.Contains(q.vertex) && set.Contains(q.vertex) && set.Contains(q.vertex - Vector2Int.one)
                                         && set.Contains(q.vertex - Vector2Int.right) && set.Contains(q.vertex - Vector2Int.up));
                var room = L.RoomAt(cells[0].x, cells[0].y);
                Fittings(L, ps, room, cells, set, basin, bb, alongX, p.seed ^ (ri * 7919));
            }
        }

        static RectInt Bounds(IEnumerable<Vector2Int> cells)
        {
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
            foreach (var c in cells) { x0 = Mathf.Min(x0, c.x); z0 = Mathf.Min(z0, c.y); x1 = Mathf.Max(x1, c.x); z1 = Mathf.Max(z1, c.y); }
            return new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
        }

        /// <summary>A cell's roof opening: a grid of squares or rounds over the room's inside (not its edge cells), a strip down
        /// its middle along its length, or open overhead.</summary>
        static SkylightShape Roof(PoolRoof roof, Vector2Int c, List<Vector2Int> cells, HashSet<Vector2Int> set, RectInt bb, bool alongX)
        {
            bool inner = set.Contains(c + Vector2Int.left) && set.Contains(c + Vector2Int.right) && set.Contains(c + Vector2Int.up) && set.Contains(c + Vector2Int.down);
            switch (roof)
            {
                case PoolRoof.Squares: return inner && ((c.x - bb.xMin) % 2 == 1) && ((c.y - bb.yMin) % 2 == 1) ? SkylightShape.Square : SkylightShape.None;
                case PoolRoof.Rounds: return inner && ((c.x - bb.xMin) % 2 == 1) && ((c.y - bb.yMin) % 2 == 1) ? SkylightShape.Round : SkylightShape.None;
                case PoolRoof.Strip:
                    if (!inner) return SkylightShape.None;
                    return alongX ? (c.y == bb.yMin + bb.height / 2 ? SkylightShape.SlotX : SkylightShape.None)
                                  : (c.x == bb.xMin + bb.width / 2 ? SkylightShape.SlotZ : SkylightShape.None);
                case PoolRoof.Open: return inner ? SkylightShape.Full : SkylightShape.None;
                default: return SkylightShape.None;
            }
        }

        static PoolFeature Fitting(PoolFeatureKind k, Vector2 at, float yaw, Vector2 size, Room r)
        {
            float e = Mathf.Max(size.x, size.y) + 0.5f;
            return new PoolFeature { kind = k, centre = at, yaw = yaw, size = size, room = r != null ? r.globalId : -1, bounds = new Rect(at.x - e, at.y - e, 2f * e, 2f * e) };
        }

        /// <summary>A pool fitting's yaw (PoolFeatures: Dir(yaw) = (cos, sin)) pointing along a direction.</summary>
        static float YawOf(Vector2 d) => Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

        static void Fittings(OfficeLayout L, PoolRoomSettings ps, Room room, List<Vector2Int> cells, HashSet<Vector2Int> set, HashSet<Vector2Int> basin,
                             RectInt bb, bool alongX, int seed)
        {
            var rng = new System.Random(seed);
            // The pool's edges: a deck cell beside a basin cell, and the way into the water.
            var edges = new List<(Vector2Int deck, Vector2 into)>();
            foreach (var c in cells)
            {
                if (basin.Contains(c)) continue;
                foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                    if (basin.Contains(c + d)) edges.Add((c, d));
            }
            // The room's walls: a cell on its edge and the way into the room from the wall.
            var walls = new List<(Vector2Int cell, Vector2 into)>();
            foreach (var c in cells)
                foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                    if (!set.Contains(c - d) && !basin.Contains(c)) walls.Add((c, d));
            Vector2 Mid(Vector2Int c) => new Vector2((c.x + 0.5f) * C, (c.y + 0.5f) * C);
            var used = new List<Vector2>();
            bool Free(Vector2 at, float r)
            {
                foreach (var u in used) if ((u - at).sqrMagnitude < r * r) return false;
                return true;
            }
            // Ladders: at the shallow end's sides of the pool (along its long sides, near the shallow end), at its edge.
            if (ps.ladders && edges.Count > 0)
            {
                var sides = edges.FindAll(e => alongX ? Mathf.Abs(e.into.y) > 0.5f : Mathf.Abs(e.into.x) > 0.5f);
                sides.Sort((a, b) => (alongX ? a.deck.x : a.deck.y).CompareTo(alongX ? b.deck.x : b.deck.y));
                foreach (var side in new[] { -1, 1 })
                    foreach (var e in sides)
                    {
                        float s = alongX ? e.into.y : e.into.x;
                        if (Mathf.Sign(s) != side) continue;
                        var at = Mid(e.deck) + e.into * (C * 0.5f);
                        if (!Free(at, 1.2f)) continue;
                        L.poolFeatures.Add(Fitting(PoolFeatureKind.Ladder, at, YawOf(e.into), new Vector2(0.6f, 0.6f), room));
                        used.Add(at);
                        break;
                    }
            }
            // The lifeguard's chair: on the deck at the middle of a long side, facing the water.
            if (ps.lifeguard && edges.Count > 0)
            {
                Vector2 centre = new Vector2((bb.xMin + bb.width * 0.5f) * C, (bb.yMin + bb.height * 0.5f) * C);
                (Vector2Int deck, Vector2 into) best = edges[0]; float bd = float.MaxValue;
                foreach (var e in edges)
                {
                    bool longSide = alongX ? Mathf.Abs(e.into.y) > 0.5f : Mathf.Abs(e.into.x) > 0.5f;
                    float d = (Mid(e.deck) - centre).sqrMagnitude * (longSide ? 1f : 4f);
                    if (d < bd && Free(Mid(e.deck), 1.4f)) { bd = d; best = e; }
                }
                var at = Mid(best.deck) - best.into * 0.15f;
                // (Its front faces the pool: the stand's yaw is the water's direction less 90, see PoolFeatures.Lifeguard.)
                L.poolFeatures.Add(Fitting(PoolFeatureKind.Lifeguard, at, YawOf(best.into) - 90f, new Vector2(0.9f, 0.9f), room));
                used.Add(at);
            }
            // Showers along a wall away from the water; bleachers against a wall, facing it.
            if (ps.showers && walls.Count > 0)
            {
                foreach (var w in Shuffle(walls, rng))
                {
                    var at = Mid(w.cell) - w.into * (C * 0.5f - OfficeBiome.WallThickness * 0.5f);
                    if (!Free(at, 1.6f)) continue;
                    L.poolFeatures.Add(Fitting(PoolFeatureKind.Showers, at, YawOf(w.into), new Vector2(1.0f, 0.4f), room));
                    used.Add(at);
                    break;
                }
            }
            if (ps.bleachers && walls.Count > 0)
            {
                foreach (var w in Shuffle(walls, rng))
                {
                    // Room for its three tiers (1.3 m) between the wall and the water.
                    var wallAt = Mid(w.cell) - w.into * (C * 0.5f - OfficeBiome.WallThickness * 0.5f);
                    var at = wallAt + w.into * 1.26f;
                    bool fits = true;
                    for (float s = 0.2f; s < 1.5f && fits; s += 0.3f)
                    {
                        var q = wallAt + w.into * s;
                        var qc = new Vector2Int(Mathf.FloorToInt(q.x / C), Mathf.FloorToInt(q.y / C));
                        if (!set.Contains(qc) || basin.Contains(qc)) fits = false;
                    }
                    if (!fits || !Free(at, 2f)) continue;
                    L.poolFeatures.Add(Fitting(PoolFeatureKind.Bleachers, at, YawOf(w.into), new Vector2(1.2f, 0.6f), room));
                    used.Add(at); used.Add(wallAt);
                    break;
                }
            }
            // Lawn chairs: two or three on the deck a step back from the edge, facing the water.
            if (ps.lawnChairs && edges.Count > 0)
            {
                int want = 2 + rng.Next(2);
                foreach (var e in Shuffle(edges, rng))
                {
                    if (want == 0) break;
                    var at = Mid(e.deck) - e.into * 0.2f + new Vector2(e.into.y, -e.into.x) * (((float)rng.NextDouble() - 0.5f) * 0.5f);
                    if (!Free(at, 0.9f)) continue;
                    float yaw = Mathf.Atan2(e.into.x, e.into.y) * Mathf.Rad2Deg + ((float)rng.NextDouble() - 0.5f) * 24f;
                    L.props.Add(new Prop { kind = PropKind.LawnChair, position = at, yaw = yaw, size = new Vector3(0.62f, 0.85f, 0.58f),
                                           variant = (rng.Next(3) == 0 ? 1 : 0) | (rng.Next(1 << 20) << 1), roomColor = room != null ? room.color : 0 });
                    used.Add(at); want--;
                }
            }
        }

        static List<T> Shuffle<T>(List<T> list, System.Random rng)
        {
            var o = new List<T>(list);
            for (int i = o.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = o[i]; o[i] = o[j]; o[j] = t; }
            return o;
        }
    }
}
