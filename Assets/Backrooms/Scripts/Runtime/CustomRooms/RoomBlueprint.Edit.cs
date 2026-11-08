using System;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    public enum DecalKind { WaterStain, Damp, Mould, Peeling, Notice, Handprints, Scorch, Outlet, Switch }
    public enum SoundKind { Hum, Buzz, Drip, Static, Knocking, Air, Radio, Traffic }
    public enum TriggerAction { LightsOut, Flicker, DoorSlam, Sound, LightsOn }

    /// <summary>A mark on one face of a wall line (build mode, PAINT > WALLS). side +1: the face toward +X / +Z.</summary>
    [Serializable]
    public class DecalDef
    {
        public bool vertical;
        public int line, side = 1;
        public float along, height = 1.3f, size = 0.35f;
        public DecalKind kind;
        public int seed;
    }

    /// <summary>A sound that plays there all the time (build mode, FURNISH > SOUNDS). Blueprint metres; y above the floor.</summary>
    [Serializable]
    public class SoundDef
    {
        public float x, z, y = 2.2f;
        public SoundKind kind;
        [Range(0f, 1f)] public float volume = 0.6f;
        public float range = 9f;
    }

    /// <summary>When the player walks into a room (an area of cells, by index), something happens.</summary>
    [Serializable]
    public class TriggerDef
    {
        public int area;
        public TriggerAction action;
        [Tooltip("DoorSlam: which opening (its id) shuts.")] public int door;
        public SoundKind sound = SoundKind.Knocking;
        public bool once = true;
        public float delay;
    }

    /// <summary>Build mode's editing operations on a layout (round 18): walls drawn point to point, rooms drawn as boxes, a
    /// run of wall moved or stretched, cells cut away (L-shaped rooms), a mess made, furniture on the ceiling.</summary>
    public partial class RoomVariant
    {
        const float Cs = OfficeBiome.CellSize;

        public int Edge(bool vertical, int line, int along) => vertical ? V(line, along) : H(along, line);
        public void SetEdge(bool vertical, int line, int along, int code) { if (vertical) SetV(line, along, code); else SetH(along, line, code); }
        public int LineCount(bool vertical) => vertical ? width : depth;
        public int AlongCount(bool vertical) => vertical ? depth : width;

        /// <summary>Is the cell on this side of an edge part of the room? s = -1: the cell before the line, +1: after it.</summary>
        public bool InsideAt(bool vertical, int line, int along, int s)
        {
            int cx = vertical ? (s < 0 ? line - 1 : line) : along, cz = vertical ? along : (s < 0 ? line - 1 : line);
            return InBounds(cx, cz) && !Outside(cx, cz);
        }

        /// <summary>An edge between the room and what is not the room (the outer line, or a cut-away notch).</summary>
        public bool Boundary(bool vertical, int line, int along) => InsideAt(vertical, line, along, -1) != InsideAt(vertical, line, along, 1);

        /// <summary>A door leading out of the room (on its outer line, or into a cut-away notch).</summary>
        public bool LeadsOut(OpeningDef o)
        {
            int a0 = Mathf.Clamp(Mathf.FloorToInt(o.from / Cs), 0, AlongCount(o.vertical) - 1), a1 = Mathf.Clamp(Mathf.CeilToInt(o.to / Cs) - 1, 0, AlongCount(o.vertical) - 1);
            for (int a = a0; a <= a1; a++) if (Boundary(o.vertical, o.line, a)) return true;
            return false;
        }

        /// <summary>
        /// After cells were cut away or given back: a wall between the room and a cut-away cell (unless it has a way through),
        /// nothing between two cut-away cells or on the outer line beside one (the world joins them up), and nothing (lights,
        /// openings, marks, sounds) left standing in what is no longer the room.
        /// </summary>
        public void FixOutsideEdges()
        {
            if (cellRoom == null || vEdges == null || hEdges == null) return;
            bool any = false;
            foreach (int c in cellRoom) if (c < 0) { any = true; break; }
            for (int pass = 0; pass < 2; pass++)
            {
                bool vertical = pass == 0;
                for (int line = 0; line <= LineCount(vertical); line++)
                for (int a = 0; a < AlongCount(vertical); a++)
                {
                    bool before = InsideAt(vertical, line, a, -1), after = InsideAt(vertical, line, a, 1);
                    bool outer = line == 0 || line == LineCount(vertical);
                    int code = Edge(vertical, line, a), kind = EdgeCode.Kind(code);
                    if (!before && !after) { if (any) SetEdge(vertical, line, a, EdgeCode.Open); }
                    else if (before != after)
                    {
                        // The room's edge: a wall or a way through (a half wall or nothing would let the world in).
                        if (kind != EdgeCode.Wall && kind != EdgeCode.Tall && kind != EdgeCode.Header) SetEdge(vertical, line, a, EdgeCode.Wall);
                    }
                    else if (outer) SetEdge(vertical, line, a, kind == EdgeCode.Tall || kind == EdgeCode.Header ? code : EdgeCode.Wall);
                }
            }
            if (!any) return;
            fixtures.RemoveAll(f => InBounds(f.x, f.z) && Outside(f.x, f.z));
            openings.RemoveAll(o =>
            {
                int a0 = Mathf.Clamp(Mathf.FloorToInt(o.from / Cs), 0, AlongCount(o.vertical) - 1), a1 = Mathf.Clamp(Mathf.CeilToInt(o.to / Cs) - 1, 0, AlongCount(o.vertical) - 1);
                for (int a = a0; a <= a1; a++) if (InsideAt(o.vertical, o.line, a, -1) || InsideAt(o.vertical, o.line, a, 1)) return false;
                return true;
            });
            decals.RemoveAll(d => !InsideAt(d.vertical, d.line, Mathf.Clamp(Mathf.FloorToInt(d.along / Cs), 0, AlongCount(d.vertical) - 1), d.side));
        }

        /// <summary>Cells cut away from the room or given back to a room (area index; -1 cuts away).</summary>
        public void SetCell(int x, int z, int area)
        {
            if (!InBounds(x, z)) return;
            cellRoom[z * width + x] = Mathf.Clamp(area, -1, rooms.Count - 1);
        }

        // ---------------- Walls drawn and moved ----------------

        /// <summary>A straight run of edges [a0, a1) on a line set to a code (a wall drawn point to point, or erased). The
        /// room's own outer edge stays a wall; doors and windows on edges that are no longer wall go. Returns the edges changed.</summary>
        public int DrawLine(bool vertical, int line, int a0, int a1, int code)
        {
            if (line < 0 || line > LineCount(vertical)) return 0;
            if (a0 > a1) (a0, a1) = (a1, a0);
            a0 = Mathf.Max(0, a0); a1 = Mathf.Min(AlongCount(vertical), a1);
            int n = 0;
            for (int a = a0; a < a1; a++)
            {
                int c = code;
                bool boundary = Boundary(vertical, line, a);
                if (boundary && EdgeCode.Kind(c) != EdgeCode.Wall) c = EdgeCode.Wall;               // erasing the outside: it stays shut
                if (boundary && EdgeCode.Kind(c) == EdgeCode.Half) continue;
                if (!InsideAt(vertical, line, a, -1) && !InsideAt(vertical, line, a, 1)) continue;   // between cut-away cells: the world's
                if (Edge(vertical, line, a) == c) continue;
                SetEdge(vertical, line, a, c);
                n++;
            }
            if (EdgeCode.Kind(code) != EdgeCode.Wall)
                openings.RemoveAll(o => o.vertical == vertical && o.line == line && o.from < a1 * Cs && o.to > a0 * Cs && !Boundary(vertical, line, Mathf.Clamp(Mathf.FloorToInt(o.Centre / Cs), 0, AlongCount(vertical) - 1)));
            return n;
        }

        /// <summary>A room drawn as a box of cells: walls round it (the outer line keeps its own), its cells a new area.
        /// Returns the new area's index, or -1 if it is too small.</summary>
        public int DrawRoom(RectInt r, string name)
        {
            r = new RectInt(Mathf.Max(0, r.x), Mathf.Max(0, r.y), 0, 0) { xMax = Mathf.Min(width, r.xMax), yMax = Mathf.Min(depth, r.yMax) };
            if (r.width < 1 || r.height < 1) return -1;
            rooms.Add(new RoomSettings { name = name });
            int area = rooms.Count - 1;
            for (int x = r.xMin; x < r.xMax; x++) for (int z = r.yMin; z < r.yMax; z++) SetCell(x, z, area);
            DrawLine(false, r.yMin, r.xMin, r.xMax, EdgeCode.Wall); DrawLine(false, r.yMax, r.xMin, r.xMax, EdgeCode.Wall);
            DrawLine(true, r.xMin, r.yMin, r.yMax, EdgeCode.Wall); DrawLine(true, r.xMax, r.yMin, r.yMax, EdgeCode.Wall);
            // A way in: the middle of its longest inside side, if none is there yet.
            FixOutsideEdges();
            return area;
        }

        /// <summary>
        /// A run of wall [a0, a1) on an inside line moved to another line: its edges (and their kinds) go there, its doors and
        /// windows go with it, and the cells it swept over join the room on the side it moved away from. False if it cannot go
        /// there (onto the outer line, or out of the room).
        /// </summary>
        public bool MoveRun(bool vertical, int line, int a0, int a1, int newLine)
        {
            int lines = LineCount(vertical);
            if (newLine <= 0 || newLine >= lines || newLine == line || line <= 0 || line >= lines) return false;
            var codes = new int[a1 - a0];
            for (int a = a0; a < a1; a++) { codes[a - a0] = Edge(vertical, line, a); if (!InsideAt(vertical, newLine, a, -1) || !InsideAt(vertical, newLine, a, 1)) return false; }
            // The cells between the old line and the new one take the room of the side it moved away from.
            for (int a = a0; a < a1; a++)
            {
                int growFrom = newLine > line ? line - 1 : line;          // the cell just behind the old line, on the side that grows
                int area = vertical ? RoomOf(growFrom, a) : RoomOf(a, growFrom);
                int lo = Mathf.Min(line, newLine), hi = Mathf.Max(line, newLine);
                for (int k = lo; k < hi; k++) { if (vertical) SetCell(k, a, area); else SetCell(a, k, area); }
            }
            for (int a = a0; a < a1; a++) SetEdge(vertical, line, a, EdgeCode.Open);
            // Onto empty edges only: a wall already on the new line stays as it is (with its doors).
            for (int a = a0; a < a1; a++) if (EdgeCode.Kind(Edge(vertical, newLine, a)) == EdgeCode.Open) SetEdge(vertical, newLine, a, codes[a - a0]);
            foreach (var o in openings)
                if (o.vertical == vertical && o.line == line && o.Centre > a0 * Cs && o.Centre < a1 * Cs)
                {
                    o.line = newLine;
                    if (!OpeningFits(o, o)) o.line = -1;                  // the new line has no room for it there: it goes
                }
            openings.RemoveAll(o => o.line < 0);
            foreach (var d in decals)
                if (d.vertical == vertical && d.line == line && d.along > a0 * Cs && d.along < a1 * Cs) d.line = newLine;
            return true;
        }

        /// <summary>A run of wall stretched or shortened along its line to [n0, n1): new edges take its kind (only where there was
        /// nothing), edges it leaves are opened (with their doors and windows). False if nothing could change.</summary>
        public bool StretchRun(bool vertical, int line, int a0, int a1, int n0, int n1)
        {
            int max = AlongCount(vertical);
            n0 = Mathf.Clamp(n0, 0, max - 1); n1 = Mathf.Clamp(n1, n0 + 1, max);
            if (line <= 0 || line >= LineCount(vertical) || (n0 == a0 && n1 == a1)) return false;
            int code = Edge(vertical, line, a0);
            for (int a = n0; a < n1; a++)
                if ((a < a0 || a >= a1) && EdgeCode.Kind(Edge(vertical, line, a)) == EdgeCode.Open && (InsideAt(vertical, line, a, -1) || InsideAt(vertical, line, a, 1)))
                    SetEdge(vertical, line, a, code);
            for (int a = a0; a < a1; a++)
                if (a < n0 || a >= n1) SetEdge(vertical, line, a, EdgeCode.Open);
            openings.RemoveAll(o => o.vertical == vertical && o.line == line && (o.from < n0 * Cs - 0.01f || o.to > n1 * Cs + 0.01f) && o.from < a1 * Cs && o.to > a0 * Cs);
            return true;
        }

        // ---------------- Mess and wrongness ----------------

        /// <summary>
        /// Makes the props look abandoned instead of placed: each nudged and turned a little, some dragged toward a wall, the odd
        /// one tipped over, by an amount 0..1. Pinned (stuck) props stay as they are.
        /// </summary>
        public int Mess(float amount, int seed)
        {
            amount = Mathf.Clamp01(amount);
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            int n = 0;
            var size = SizeMetres;
            foreach (var p in props)
            {
                if (p.pinned || p.kind == PropKind.WallClock || p.kind == PropKind.StorageRack) continue;
                float half = Mathf.Max(p.size.x, p.size.z) * 0.5f * (p.scale > 0.01f ? p.scale : 1f);
                var pos = new Vector2(p.x, p.z) + new Vector2(R() - 0.5f, R() - 0.5f) * (0.9f * amount);
                p.yaw = Mathf.Repeat(p.yaw + (R() - 0.5f) * 70f * amount, 360f);
                if (R() < 0.3f * amount)
                {
                    // Dragged toward the nearest wall.
                    float dw = pos.x, de = size.x - pos.x, ds = pos.y, dn = size.y - pos.y;
                    float m = Mathf.Min(Mathf.Min(dw, de), Mathf.Min(ds, dn)), pull = (0.4f + 0.6f * R()) * amount;
                    if (m == dw) pos.x = Mathf.Lerp(pos.x, half + 0.12f, pull); else if (m == de) pos.x = Mathf.Lerp(pos.x, size.x - half - 0.12f, pull);
                    else if (m == ds) pos.y = Mathf.Lerp(pos.y, half + 0.12f, pull); else pos.y = Mathf.Lerp(pos.y, size.y - half - 0.12f, pull);
                }
                p.x = Mathf.Clamp(pos.x, half * 0.6f, size.x - half * 0.6f); p.z = Mathf.Clamp(pos.y, half * 0.6f, size.y - half * 0.6f);
                bool light = p.kind == PropKind.Box || p.kind == PropKind.OfficeChair || p.kind == PropKind.FloorLamp || p.kind == PropKind.Armchair;
                if (light && R() < 0.22f * amount)
                {
                    // Over on its side: stuck that way, so the physics does not stand it up again.
                    if (R() < 0.5f) p.roll = R() < 0.5f ? 90f : -90f; else p.pitch = R() < 0.5f ? 90f : -90f;
                    // (Turned about its foot: what was its width or depth now stands up, half of it below the foot.)
                    p.elevation = (p.roll != 0f ? p.size.x : p.size.z) * 0.5f * (p.scale > 0.01f ? p.scale : 1f);
                    p.pinned = true;
                }
                n++;
            }
            // Some lights die or start failing too.
            foreach (var f in fixtures)
            {
                float r = R();
                if (r < 0.12f * amount) { f.on = false; f.faulty = false; }
                else if (r < 0.3f * amount && f.on) f.faulty = true;
            }
            return n;
        }

        /// <summary>The prop upside down on the ceiling over where it stands (stuck there).</summary>
        public void ToCeiling(PropDef p)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt(p.x / Cs), 0, width - 1), cz = Mathf.Clamp(Mathf.FloorToInt(p.z / Cs), 0, depth - 1);
            p.roll = 180f; p.pitch = 0f;
            p.elevation = RoomAt(cx, cz).ceiling;                 // turned over about its foot: the foot is at the top
            p.pinned = true;
        }
    }
}
