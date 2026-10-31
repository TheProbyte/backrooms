using System;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Edge codes of a blueprint. Edges lie on the 1.2 m cell grid exactly like the procedural world's
    /// (<see cref="OfficeLayout"/>), so a blueprint is stamped into the world's own layout and built by the world's own
    /// chunk builder. The low three bits are the kind, bit 3 makes a headed doorway round-topped.
    /// </summary>
    public static class EdgeCode
    {
        /// <summary>Nothing: the two cells are one open space.</summary>
        public const int Open = 0;
        /// <summary>A solid wall, floor to ceiling.</summary>
        public const int Wall = 1;
        /// <summary>A tall opening, one cell wide, up to the ceiling (no header).</summary>
        public const int Tall = 2;
        /// <summary>A doorway one cell wide with a 2.1 m header.</summary>
        public const int Header = 3;
        /// <summary>A half-height partition (1.15 m): the two cells stay open to each other above it.</summary>
        public const int Half = 4;
        /// <summary>A slim railing.</summary>
        public const int Railing = 5;
        /// <summary>Added to <see cref="Header"/>: a round-topped arch.</summary>
        public const int Arch = 8;

        public static int Kind(int c) => c & 7;
        public static bool Arched(int c) => (c & Arch) != 0;
        public static bool Passable(int c) => Kind(c) != Wall;
        /// <summary>Edges the player can walk through (openings, doorways and the open space between cells).</summary>
        public static bool Walkthrough(int c) { int k = Kind(c); return k == Open || k == Tall || k == Header; }
        public static string Name(int c)
        {
            switch (Kind(c))
            {
                case Open: return "open";
                case Wall: return "wall";
                case Tall: return "tall opening";
                case Header: return Arched(c) ? "arch" : "doorway";
                case Half: return "half wall";
                case Railing: return "railing";
            }
            return "?";
        }
    }

    [Serializable]
    public class RoomSettings
    {
        public string name = "Room";
        public RoomKind kind = RoomKind.Office;
        [Tooltip("Ceiling height in metres (2.7 is the normal office; taller ceilings get plaster and round lights).")]
        public float ceiling = OfficeBiome.CeilingHeight;
        [Tooltip("Painted plaster walls instead of wallpaper.")] public bool painted;
        public WallFinish wallFinish;
        public FloorFinish floorFinish;
        public LightMode lights = LightMode.AllOn;

        public RoomSettings Clone() => (RoomSettings)MemberwiseClone();
    }

    [Serializable] public class PillarDef { public int x, z; public float size = 0.6f; }
    [Serializable] public class FixtureDef { public int x, z; public bool on = true; public bool faulty; }
    [Serializable]
    public class PropDef
    {
        public PropKind kind = PropKind.Box;
        public float x, z;            // metres from the blueprint's south-west corner
        public float yaw;
        public int variant;
        public bool lit = true;       // floor lamps
        public Vector3 size = new Vector3(0.6f, 0.5f, 0.5f);
        [Range(0f, 1f)] public float chance = 1f;
        [Tooltip("Lifted off the floor (m).")] public float elevation;
        [Tooltip("Tipped over: degrees about its own sideways and front axes.")] public float pitch, roll;
        [Tooltip("Size (1 = as made).")] public float scale = 1f;
        [Tooltip("Physics off: stuck exactly where it is put, never moves, cannot be taken. May sit half inside a wall.")]
        public bool pinned;
        [Tooltip("Which model the catalog chose (0: the dice choose).")] public int style;
    }

    /// <summary>
    /// A door, arch or window of its own size on a wall line (build mode). It cuts the walls along its line; a doorway (sill
    /// 0) on the outer wall is an entrance. Positions are metres from the blueprint's south-west corner.
    /// </summary>
    [Serializable]
    public class OpeningDef
    {
        public bool vertical;         // on the line X = line (else Z = line)
        public int line;
        public float from = 0.15f, to = 1.05f;   // along the line (m)
        public float sill;            // 0: a doorway; above: a window
        public float top = 2.1f;
        public bool arch;

        public float Width => to - from;
        public float Centre => (from + to) * 0.5f;
        public bool Door => sill < 0.01f;
        public OpeningDef Clone() => (OpeningDef)MemberwiseClone();
    }
    [Serializable]
    public class LootDef
    {
        public string item = "Loot";
        public float x, z;
        [Range(0f, 1f)] public float chance = 1f;
        public string note = "";
    }

    /// <summary>
    /// One layout of a custom room: a grid of cells (1.2 m), per-cell room settings, and the walls, doorways, lights and
    /// dressing on it. A room can have several variants; the world picks one when it places the room.
    /// </summary>
    [Serializable]
    public class RoomVariant
    {
        public string name = "Variant";
        public float weight = 1f;
        public int width = 6, depth = 6;
        public List<RoomSettings> rooms = new List<RoomSettings> { new RoomSettings() };
        /// <summary>Which of <see cref="rooms"/> each cell belongs to; index z * width + x.</summary>
        public int[] cellRoom;
        /// <summary>Edges on the lines X = 0..width between cells (x-1, z) and (x, z); index x * depth + z.</summary>
        public int[] vEdges;
        /// <summary>Edges on the lines Z = 0..depth between cells (x, z-1) and (x, z); index x * (depth + 1) + z.</summary>
        public int[] hEdges;
        public List<PillarDef> pillars = new List<PillarDef>();
        public List<FixtureDef> fixtures = new List<FixtureDef>();
        public List<PropDef> props = new List<PropDef>();
        public List<LootDef> loot = new List<LootDef>();
        public List<OpeningDef> openings = new List<OpeningDef>();

        public const int MinSize = 2, MaxSize = 24;
        /// <summary>Smallest door or window (m), and the room left above one (m).</summary>
        public const float MinOpening = 0.4f, HeadRoom = 0.05f;

        /// <summary>Length of a wall line (m).</summary>
        public float LineLength(bool vertical) => (vertical ? depth : width) * OfficeBiome.CellSize;
        public bool PerimeterLine(bool vertical, int line) => line == 0 || line == (vertical ? width : depth);

        /// <summary>The lowest ceiling of the areas either side of a wall line between two points (m).</summary>
        public float CeilingAlong(bool vertical, int line, float from, float to)
        {
            const float c = OfficeBiome.CellSize;
            float ceil = OfficeBiome.MaxCeilingHeight;
            int a0 = Mathf.Max(0, Mathf.FloorToInt(from / c)), a1 = Mathf.Min((vertical ? depth : width) - 1, Mathf.CeilToInt(to / c) - 1);
            for (int a = a0; a <= a1; a++)
                for (int s = -1; s <= 0; s++)
                {
                    int cx = vertical ? line + s : a, cz = vertical ? a : line + s;
                    if (InBounds(cx, cz)) ceil = Mathf.Min(ceil, RoomAt(cx, cz).ceiling);
                }
            return ceil == OfficeBiome.MaxCeilingHeight ? OfficeBiome.CeilingHeight : ceil;
        }

        /// <summary>Keeps an opening on its line, at least the smallest size, under the ceiling, a sill below its top.</summary>
        public void ClampOpening(OpeningDef o)
        {
            float len = LineLength(o.vertical);
            o.line = Mathf.Clamp(o.line, 0, o.vertical ? width : depth);
            if (o.to - o.from < MinOpening) o.to = o.from + MinOpening;
            if (o.from < 0f) { o.to -= o.from; o.from = 0f; }
            if (o.to > len) { o.from -= o.to - len; o.to = len; }
            o.from = Mathf.Max(0f, o.from);
            float ceil = CeilingAlong(o.vertical, o.line, o.from, o.to);
            o.top = Mathf.Clamp(o.top, 0.5f, ceil);
            o.sill = Mathf.Clamp(o.sill, 0f, o.top - 0.3f);
        }

        public int V(int x, int z) => vEdges[x * depth + z];
        public int H(int x, int z) => hEdges[x * (depth + 1) + z];
        public void SetV(int x, int z, int code) => vEdges[x * depth + z] = code;
        public void SetH(int x, int z, int code) => hEdges[x * (depth + 1) + z] = code;
        public int RoomOf(int x, int z) => cellRoom[z * width + x];
        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < width && z < depth;
        public RoomSettings RoomAt(int x, int z) => rooms[Mathf.Clamp(RoomOf(x, z), 0, rooms.Count - 1)];

        /// <summary>Snap to a solid wall on the pointer's side. The prop's local +Z faces into that area.</summary>
        public bool TryWallPlacement(Vector2 pointer, Vector3 size, out Vector2 position, out float yaw)
        {
            position = default; yaw = 0f;
            const float c = OfficeBiome.CellSize;
            int cx = Mathf.FloorToInt(pointer.x / c), cz = Mathf.FloorToInt(pointer.y / c);
            if (!InBounds(cx, cz)) return false;
            float best = 0.95f;
            for (int side = 0; side < 4; side++)
            {
                bool vertical = side < 2;
                int line = vertical ? cx + side : cz + side - 2;
                int along = vertical ? cz : cx;
                if (EdgeCode.Kind(vertical ? V(line, along) : H(along, line)) != EdgeCode.Wall) continue;
                float edge = line * c;
                float distance = Mathf.Abs((vertical ? pointer.x : pointer.y) - edge);
                if (distance >= best) continue;
                float centre = vertical ? pointer.y : pointer.x;
                // Find the continuous solid run so wide racks cannot cover a doorway or project past a corner.
                int lo = along, hi = along + 1, limit = vertical ? depth : width;
                while (lo > 0 && EdgeCode.Kind(vertical ? V(line, lo - 1) : H(lo - 1, line)) == EdgeCode.Wall) lo--;
                while (hi < limit && EdgeCode.Kind(vertical ? V(line, hi) : H(hi, line)) == EdgeCode.Wall) hi++;
                float half = size.x * 0.5f + 0.03f;
                if ((hi - lo) * c < half * 2f) continue;
                centre = Mathf.Clamp(Mathf.Round(centre / 0.15f) * 0.15f, lo * c + half, hi * c - half);
                float offset = OfficeBiome.WallThickness * 0.5f + size.z * 0.5f + 0.02f;
                bool positive = side == 0 || side == 2;
                position = vertical ? new Vector2(edge + (positive ? offset : -offset), centre)
                    : new Vector2(centre, edge + (positive ? offset : -offset));
                yaw = side == 0 ? 90f : side == 1 ? -90f : side == 2 ? 0f : 180f;
                best = distance;
            }
            return best < 0.95f;
        }

        /// <summary>A simple room: four walls, one doorway in the south wall, a light every two cells.</summary>
        public static RoomVariant CreateDefault(int w = 6, int d = 6, string name = "Variant 1")
        {
            var v = new RoomVariant { name = name, width = Mathf.Clamp(w, MinSize, MaxSize), depth = Mathf.Clamp(d, MinSize, MaxSize) };
            v.Allocate();
            v.WallPerimeter();
            v.SetH(v.width / 2, 0, EdgeCode.Header);          // doorway in the middle of the south wall
            v.AutoFixtures();
            return v;
        }

        void Allocate()
        {
            cellRoom = new int[width * depth];
            vEdges = new int[(width + 1) * depth];
            hEdges = new int[width * (depth + 1)];
        }

        /// <summary>Walls on all four sides (replaces whatever is on the perimeter).</summary>
        public void WallPerimeter()
        {
            for (int z = 0; z < depth; z++) { SetV(0, z, EdgeCode.Wall); SetV(width, z, EdgeCode.Wall); }
            for (int x = 0; x < width; x++) { SetH(x, 0, EdgeCode.Wall); SetH(x, depth, EdgeCode.Wall); }
        }

        /// <summary>Makes the arrays consistent with width and depth (after loading or hand edits) and clamps values.</summary>
        public void Validate()
        {
            width = Mathf.Clamp(width, MinSize, MaxSize); depth = Mathf.Clamp(depth, MinSize, MaxSize);
            if (rooms == null || rooms.Count == 0) rooms = new List<RoomSettings> { new RoomSettings() };
            if (cellRoom == null || cellRoom.Length != width * depth || vEdges == null || vEdges.Length != (width + 1) * depth || hEdges == null || hEdges.Length != width * (depth + 1))
            {
                var old = (cellRoom, vEdges, hEdges);
                Allocate();
                if (old.cellRoom == null) WallPerimeter();
            }
            for (int i = 0; i < cellRoom.Length; i++) cellRoom[i] = Mathf.Clamp(cellRoom[i], 0, rooms.Count - 1);
            for (int i = 0; i < vEdges.Length; i++) vEdges[i] = Mathf.Clamp(vEdges[i], 0, 15);
            for (int i = 0; i < hEdges.Length; i++) hEdges[i] = Mathf.Clamp(hEdges[i], 0, 15);
            foreach (var r in rooms) r.ceiling = Mathf.Clamp(r.ceiling, OfficeBiome.CeilingHeight, OfficeBiome.MaxCeilingHeight);
            fixtures.RemoveAll(f => !InBounds(f.x, f.z));
            pillars.RemoveAll(p => p.x < 0 || p.z < 0 || p.x > width || p.z > depth);
            weight = Mathf.Max(0f, weight);
            if (openings == null) openings = new List<OpeningDef>();
            openings.RemoveAll(o => o == null || o.line < 0 || o.line > (o.vertical ? width : depth) || o.from >= LineLength(o.vertical) - 0.05f);
            foreach (var o in openings) ClampOpening(o);
            foreach (var p in props) { if (p.scale <= 0.01f) p.scale = 1f; p.scale = Mathf.Clamp(p.scale, 0.25f, 4f); }
        }

        /// <summary>
        /// Resizes the grid. Cells outside the old grid copy the nearest old cell; interior walls are kept (and extended
        /// when the room grows); a perimeter wall that moves takes its doorways with it.
        /// </summary>
        public void Resize(int w, int d)
        {
            w = Mathf.Clamp(w, MinSize, MaxSize); d = Mathf.Clamp(d, MinSize, MaxSize);
            if (w == width && d == depth) return;
            var old = Clone();
            width = w; depth = d;
            Allocate();
            for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                cellRoom[z * width + x] = old.cellRoom[Mathf.Min(z, old.depth - 1) * old.width + Mathf.Min(x, old.width - 1)];

            // A perimeter edge can only be a wall or a way out.
            int Perimeter(int c) { int k = EdgeCode.Kind(c); return k == EdgeCode.Tall || k == EdgeCode.Header ? c : EdgeCode.Wall; }
            for (int x = 0; x <= width; x++)
            for (int z = 0; z < depth; z++)
            {
                int oz = Mathf.Min(z, old.depth - 1);
                int code;
                if (x == 0) code = Perimeter(old.V(0, oz));
                else if (x == width) code = Perimeter(old.V(Mathf.Min(width, old.width), oz));
                else code = x < old.width ? old.V(x, oz) : EdgeCode.Open;
                SetV(x, z, code);
            }
            for (int x = 0; x < width; x++)
            for (int z = 0; z <= depth; z++)
            {
                int ox = Mathf.Min(x, old.width - 1);
                int code;
                if (z == 0) code = Perimeter(old.H(ox, 0));
                else if (z == depth) code = Perimeter(old.H(ox, Mathf.Min(depth, old.depth)));
                else code = z < old.depth ? old.H(ox, z) : EdgeCode.Open;
                SetH(x, z, code);
            }
            rooms = old.rooms.ConvertAll(r => r.Clone());
            pillars = old.pillars.FindAll(p => p.x <= width && p.z <= depth);
            props = old.props.FindAll(p => p.x < width * OfficeBiome.CellSize && p.z < depth * OfficeBiome.CellSize);
            loot = old.loot.FindAll(l => l.x < width * OfficeBiome.CellSize && l.z < depth * OfficeBiome.CellSize);
            // Openings stay on their line; one on an outer line that moved goes with it.
            openings = new List<OpeningDef>();
            foreach (var o in old.openings)
            {
                var c = o.Clone();
                int oldMax = c.vertical ? old.width : old.depth;
                if (c.line == oldMax) c.line = c.vertical ? width : depth;
                if (c.line > (c.vertical ? width : depth) || c.from >= LineLength(c.vertical) - MinOpening) continue;
                ClampOpening(c);
                openings.Add(c);
            }
            fixtures = old.fixtures.FindAll(f => f.x < width && f.z < depth);
            // New area gets the usual light spacing.
            var grid = AutoFixtureCells();
            foreach (var c in grid)
                if ((c.x >= old.width || c.y >= old.depth) && !fixtures.Exists(f => f.x == c.x && f.z == c.y))
                    fixtures.Add(new FixtureDef { x = c.x, z = c.y, on = RoomAt(c.x, c.y).lights != LightMode.Dark });
        }

        /// <summary>
        /// Re-frames the grid on the old cells [x0, x0 + w) x [z0, z0 + d) (it may reach past them: growing west or south is
        /// a negative x0 or z0). As <see cref="Resize"/> does for the east and north: each outer wall keeps its own doorways
        /// and moves out or in with that side, a wall line that ends up inside is opened, new cells copy the nearest old one,
        /// and everything else keeps its place in the room.
        /// </summary>
        public void Reframe(int x0, int z0, int w, int d)
        {
            w = Mathf.Clamp(w, MinSize, MaxSize); d = Mathf.Clamp(d, MinSize, MaxSize);
            var old = Clone();
            width = w; depth = d;
            Allocate();
            const float c = OfficeBiome.CellSize;
            for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                cellRoom[z * width + x] = old.cellRoom[Mathf.Clamp(z + z0, 0, old.depth - 1) * old.width + Mathf.Clamp(x + x0, 0, old.width - 1)];
            int Perimeter(int code) { int k = EdgeCode.Kind(code); return k == EdgeCode.Tall || k == EdgeCode.Header ? code : EdgeCode.Wall; }
            for (int x = 0; x <= width; x++)
            for (int z = 0; z < depth; z++)
            {
                int ox = x + x0, oz = Mathf.Clamp(z + z0, 0, old.depth - 1);
                int code;
                if (x == 0) code = Perimeter(old.V(0, oz));
                else if (x == width) code = Perimeter(old.V(old.width, oz));
                else code = ox > 0 && ox < old.width && z + z0 >= 0 && z + z0 < old.depth ? old.V(ox, oz) : EdgeCode.Open;
                SetV(x, z, code);
            }
            for (int x = 0; x < width; x++)
            for (int z = 0; z <= depth; z++)
            {
                int ox = Mathf.Clamp(x + x0, 0, old.width - 1), oz = z + z0;
                int code;
                if (z == 0) code = Perimeter(old.H(ox, 0));
                else if (z == depth) code = Perimeter(old.H(ox, old.depth));
                else code = oz > 0 && oz < old.depth && x + x0 >= 0 && x + x0 < old.width ? old.H(ox, oz) : EdgeCode.Open;
                SetH(x, z, code);
            }
            rooms = old.rooms.ConvertAll(r => r.Clone());
            float dx = -x0 * c, dz = -z0 * c, wm = width * c, dm = depth * c;
            bool Inside(float x, float z) => x > 0f && z > 0f && x < wm && z < dm;
            pillars = new List<PillarDef>();
            foreach (var p in old.pillars) { int px = p.x - x0, pz = p.z - z0; if (px >= 0 && pz >= 0 && px <= width && pz <= depth) pillars.Add(new PillarDef { x = px, z = pz, size = p.size }); }
            fixtures = new List<FixtureDef>();
            foreach (var f in old.fixtures) { int fx = f.x - x0, fz = f.z - z0; if (InBounds(fx, fz)) fixtures.Add(new FixtureDef { x = fx, z = fz, on = f.on, faulty = f.faulty }); }
            props = new List<PropDef>();
            foreach (var p in old.props)
            {
                var q = JsonUtility.FromJson<PropDef>(JsonUtility.ToJson(p)); q.x += dx; q.z += dz;
                if (Inside(q.x, q.z) || q.pinned) props.Add(q);
            }
            loot = new List<LootDef>();
            foreach (var l in old.loot) { var q = JsonUtility.FromJson<LootDef>(JsonUtility.ToJson(l)); q.x += dx; q.z += dz; if (Inside(q.x, q.z)) loot.Add(q); }
            openings = new List<OpeningDef>();
            foreach (var o in old.openings)
            {
                var q = o.Clone();
                int max = q.vertical ? old.width : old.depth, shift = q.vertical ? x0 : z0;
                float slide = q.vertical ? dz : dx;
                // An opening in an outer wall goes with that wall; the others keep their place.
                q.line = q.line == 0 ? 0 : q.line == max ? (q.vertical ? width : depth) : q.line - shift;
                q.from += slide; q.to += slide;
                if ((q.line <= 0 && o.line != 0) || (q.line >= (q.vertical ? width : depth) && o.line != max)) continue;
                if (q.to <= MinOpening * 0.5f || q.from >= LineLength(q.vertical) - MinOpening * 0.5f) continue;
                ClampOpening(q);
                openings.Add(q);
            }
            // New cells get the usual light spacing.
            foreach (var cell in AutoFixtureCells())
            {
                int ox = cell.x + x0, oz = cell.y + z0;
                if ((ox < 0 || oz < 0 || ox >= old.width || oz >= old.depth) && !fixtures.Exists(f => f.x == cell.x && f.z == cell.y))
                    fixtures.Add(new FixtureDef { x = cell.x, z = cell.y, on = RoomAt(cell.x, cell.y).lights != LightMode.Dark });
            }
        }

        /// <summary>
        /// The grid's own doorways, arches and tall openings (edge codes) as openings of their own size, so they can be
        /// dragged wider and taller: each run of one kind along a line becomes one opening between its end posts, and its
        /// edges become wall. Returns how many it made.
        /// </summary>
        public int ConvertGridOpenings()
        {
            const float c = OfficeBiome.CellSize, t = OfficeBiome.WallThickness;
            int made = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                bool vertical = pass == 0;
                int lines = vertical ? width : depth, alongN = vertical ? depth : width;
                for (int line = 0; line <= lines; line++)
                {
                    int a = 0;
                    while (a < alongN)
                    {
                        int code = vertical ? V(line, a) : H(a, line);
                        int kind = EdgeCode.Kind(code);
                        if (kind != EdgeCode.Header && kind != EdgeCode.Tall) { a++; continue; }
                        int b = a + 1;
                        while (b < alongN && (vertical ? V(line, b) : H(b, line)) == code) b++;
                        float top = kind == EdgeCode.Tall ? CeilingAlong(vertical, line, a * c, b * c) : EdgeCode.Arched(code) ? 2.3f : 2.1f;
                        var o = new OpeningDef { vertical = vertical, line = line, from = a * c + t * 0.5f, to = b * c - t * 0.5f, top = top, arch = EdgeCode.Arched(code) };
                        ClampOpening(o);
                        openings.Add(o);
                        for (int k = a; k < b; k++) { if (vertical) SetV(line, k, EdgeCode.Wall); else SetH(k, line, EdgeCode.Wall); }
                        made++;
                        a = b;
                    }
                }
            }
            return made;
        }

        /// <summary>
        /// Does an opening fit: on solid wall all along (not open floor or a half wall), clear of the other openings on its
        /// line (by 5 cm), inside the line, under the ceiling.
        /// </summary>
        public bool OpeningFits(OpeningDef o, OpeningDef ignore = null)
        {
            const float c = OfficeBiome.CellSize;
            if (o.from < -0.001f || o.to > LineLength(o.vertical) + 0.001f || o.to - o.from < MinOpening - 0.001f) return false;
            if (o.line < 0 || o.line > (o.vertical ? width : depth)) return false;
            int a0 = Mathf.FloorToInt(o.from / c), a1 = Mathf.CeilToInt(o.to / c) - 1;
            for (int a = Mathf.Max(0, a0); a <= Mathf.Min((o.vertical ? depth : width) - 1, a1); a++)
                if (EdgeCode.Kind(o.vertical ? V(o.line, a) : H(a, o.line)) != EdgeCode.Wall) return false;
            foreach (var q in openings)
                if (q != ignore && q != o && q.vertical == o.vertical && q.line == o.line && q.from < o.to + 0.05f && q.to > o.from - 0.05f) return false;
            return o.top <= CeilingAlong(o.vertical, o.line, o.from, o.to) + 0.001f;
        }

        List<Vector2Int> AutoFixtureCells()
        {
            // The generator's spacing: a troffer every 2 cells, centred in the room.
            var list = new List<Vector2Int>();
            int ox = ((width - 1) % 2 + 1) / 2, oz = ((depth - 1) % 2 + 1) / 2;
            for (int x = ox; x < width; x += 2)
            for (int z = oz; z < depth; z += 2) list.Add(new Vector2Int(x, z));
            return list;
        }

        /// <summary>One ceiling light every two cells, like the generator's own.</summary>
        public void AutoFixtures()
        {
            fixtures.Clear();
            foreach (var c in AutoFixtureCells())
                fixtures.Add(new FixtureDef { x = c.x, z = c.y, on = RoomAt(c.x, c.y).lights != LightMode.Dark });
        }

        public RoomVariant Clone() => JsonUtility.FromJson<RoomVariant>(JsonUtility.ToJson(this));

        /// <summary>Metres: footprint.</summary>
        public Vector2 SizeMetres => new Vector2(width, depth) * OfficeBiome.CellSize;

        /// <summary>Does at least one perimeter edge lead outside (an entrance / exit)?</summary>
        public int CountExits()
        {
            int n = 0;
            for (int z = 0; z < depth; z++) { if (EdgeCode.Walkthrough(V(0, z))) n++; if (EdgeCode.Walkthrough(V(width, z))) n++; }
            for (int x = 0; x < width; x++) { if (EdgeCode.Walkthrough(H(x, 0))) n++; if (EdgeCode.Walkthrough(H(x, depth))) n++; }
            foreach (var o in openings) if (o.Door && PerimeterLine(o.vertical, o.line)) n++;
            return n;
        }
    }

    /// <summary>A custom room as stored on disk: spawn rules plus one or more layouts (variants).</summary>
    [Serializable]
    public class RoomBlueprint
    {
        /// <summary>2: doors and arches are openings of their own size (<see cref="OpeningDef"/>); a format 1 room's grid
        /// doorways are turned into them when it is loaded.</summary>
        public int format = 2;
        public string id = "";
        public string name = "New room";
        [Tooltip("Spawns in the procedural world (Save to game turns this on).")] public bool enabled;
        [Tooltip("Chance that a landmark spot that picks this room really holds it (the world's landmark grid sets how many spots there are).")]
        [Range(0f, 1f)] public float chance = 0.5f;
        [Tooltip("Relative weight against other landmarks and rooms allowed at the same spot.")] public float weight = 1f;
        public List<string> biomes = new List<string>();
        public List<string> zones = new List<string>();
        [Tooltip("The world may turn the room by 90 degree steps.")] public bool allowRotate = true;
        public string notes = "";
        public string modified = "";
        public List<RoomVariant> variants = new List<RoomVariant>();

        public static RoomBlueprint CreateNew(string name = "New room")
        {
            var b = new RoomBlueprint { name = name };
            b.variants.Add(RoomVariant.CreateDefault());
            return b;
        }

        public void Validate()
        {
            if (variants == null) variants = new List<RoomVariant>();
            if (variants.Count == 0) variants.Add(RoomVariant.CreateDefault());
            foreach (var v in variants) v.Validate();
            chance = Mathf.Clamp01(chance); weight = Mathf.Max(0f, weight);
            if (biomes == null) biomes = new List<string>();
            if (zones == null) zones = new List<string>();
            if (string.IsNullOrWhiteSpace(name)) name = "Room";
        }

        public RoomBlueprint Clone() => JsonUtility.FromJson<RoomBlueprint>(JsonUtility.ToJson(this));

        public string ToJson() => JsonUtility.ToJson(this, true);

        public static RoomBlueprint FromJson(string json)
        {
            var b = JsonUtility.FromJson<RoomBlueprint>(json);
            if (b == null) return null;
            b.Validate();
            if (b.format < 2)
            {
                foreach (var v in b.variants) v.ConvertGridOpenings();
                b.format = 2;
            }
            return b;
        }

        /// <summary>The landmark rule the world's generator uses for this room (footprint of the largest variant).</summary>
        public LandmarkRules ToRules()
        {
            int w = 0, d = 0;
            foreach (var v in variants) { w = Mathf.Max(w, Mathf.Max(v.width, v.depth)); d = w; }
            var r = new LandmarkRules { name = id, kind = LandmarkKind.Custom, width = w, depth = d, chance = chance, weight = weight, storeys = 1 };
            r.zones.Clear(); r.biomes.Clear();
            foreach (var z in zones) r.zones.Add(z);
            foreach (var b in biomes) r.biomes.Add(b);
            return r;
        }

        /// <summary>Picks a variant by weight with the caller's dice.</summary>
        public int PickVariant(System.Random rng)
        {
            float total = 0f;
            foreach (var v in variants) total += Mathf.Max(0f, v.weight);
            if (total <= 0f) return rng.Next(variants.Count);
            float pick = (float)rng.NextDouble() * total;
            for (int i = 0; i < variants.Count; i++) { pick -= Mathf.Max(0f, variants[i].weight); if (pick < 0f) return i; }
            return variants.Count - 1;
        }
    }
}
