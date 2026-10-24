using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Stamps a custom room (<see cref="RoomBlueprint"/>) into the world's own plan. It is called from
    /// <see cref="Landmarks"/> for placements of kind <see cref="LandmarkKind.Custom"/>, so a custom room is generated, streamed
    /// and built exactly like the colonnade hall and the atrium: through <see cref="OfficeLayout"/> and the chunk builder.
    /// Runs on the generator's worker threads: only reads published blueprints, never touches Unity objects.
    /// </summary>
    public static class CustomRoomStamp
    {
        const float C = OfficeBiome.CellSize;

        /// <summary>Maps blueprint coordinates to the placed room's (a turn of rot x 90 degrees about the footprint).</summary>
        public readonly struct Xf
        {
            public readonly int rot, W, D;
            public Xf(int rot, int w, int d) { this.rot = ((rot % 4) + 4) % 4; W = w; D = d; }
            public int Width => (rot & 1) == 0 ? W : D;
            public int Depth => (rot & 1) == 0 ? D : W;

            public Vector2Int Cell(int x, int z)
            {
                switch (rot)
                {
                    case 1: return new Vector2Int(D - 1 - z, x);
                    case 2: return new Vector2Int(W - 1 - x, D - 1 - z);
                    case 3: return new Vector2Int(z, W - 1 - x);
                    default: return new Vector2Int(x, z);
                }
            }

            public Vector2Int Vertex(int u, int v)
            {
                switch (rot)
                {
                    case 1: return new Vector2Int(D - v, u);
                    case 2: return new Vector2Int(W - u, D - v);
                    case 3: return new Vector2Int(v, W - u);
                    default: return new Vector2Int(u, v);
                }
            }

            public Vector2 Metres(float px, float pz)
            {
                float wm = W * C, dm = D * C;
                switch (rot)
                {
                    case 1: return new Vector2(dm - pz, px);
                    case 2: return new Vector2(wm - px, dm - pz);
                    case 3: return new Vector2(pz, wm - px);
                    default: return new Vector2(px, pz);
                }
            }

            public float Yaw(float yaw) => yaw - 90f * rot;
        }

        /// <summary>A blueprint that is not (or not yet) saved, used by the builder's preview and test (main thread only).</summary>
        public static RoomBlueprint Override;

        public static bool TryResolve(in LandmarkPlacement p, out RoomBlueprint bp, out RoomVariant v, out Xf xf)
        {
            bp = Override != null && (Override.id == p.blueprint || p.blueprint == "unsaved") ? Override : RoomLibrary.Get(p.blueprint); v = null; xf = default;
            if (bp == null || bp.variants.Count == 0) return false;
            v = bp.variants[Mathf.Clamp(p.variant, 0, bp.variants.Count - 1)];
            xf = new Xf(p.rot, v.width, v.depth);
            return true;
        }

        /// <summary>Chooses variant and turn for a placement and the footprint it needs (the landmark dice, so deterministic).</summary>
        public static bool Choose(LandmarkRules rules, System.Random rng, out string id, out string name, out int variant, out int rot, out int w, out int d)
        {
            id = rules.name; name = id; variant = 0; rot = 0; w = d = 0;
            var bp = RoomLibrary.Get(id);
            if (bp == null || bp.variants.Count == 0) return false;
            name = bp.name;
            variant = bp.PickVariant(rng);
            var v = bp.variants[variant];
            rot = bp.allowRotate ? rng.Next(4) : 0;
            var xf = new Xf(rot, v.width, v.depth);
            w = xf.Width; d = xf.Depth;
            return true;
        }

        // ---------------- Rooms ----------------

        /// <summary>One RoomSpec per rectangle of cells of each blueprint room (a room of any shape is covered by rectangles).</summary>
        public static void Rooms(in LandmarkPlacement p, List<Landmarks.RoomSpec> list)
        {
            if (!TryResolve(p, out var bp, out var v, out var xf)) return;
            var done = new bool[v.width * v.depth];
            for (int z = 0; z < v.depth; z++)
            for (int x = 0; x < v.width; x++)
            {
                if (done[z * v.width + x]) continue;
                int room = v.RoomOf(x, z);
                int w = 1;
                while (x + w < v.width && !done[z * v.width + x + w] && v.RoomOf(x + w, z) == room) w++;
                int h = 1;
                for (; z + h < v.depth; h++)
                {
                    bool row = true;
                    for (int k = 0; k < w && row; k++) row = !done[(z + h) * v.width + x + k] && v.RoomOf(x + k, z + h) == room;
                    if (!row) break;
                }
                for (int j = 0; j < h; j++) for (int k = 0; k < w; k++) done[(z + j) * v.width + x + k] = true;

                var a = xf.Cell(x, z); var b = xf.Cell(x + w - 1, z + h - 1);
                int x0 = Mathf.Min(a.x, b.x), z0 = Mathf.Min(a.y, b.y), x1 = Mathf.Max(a.x, b.x), z1 = Mathf.Max(a.y, b.y);
                var rs = v.rooms[Mathf.Clamp(room, 0, v.rooms.Count - 1)];
                var kind = rs.kind == RoomKind.Crawl ? RoomKind.Office : rs.kind;
                list.Add(new Landmarks.RoomSpec
                {
                    rect = new RectInt(p.rect.x + x0, p.rect.y + z0, x1 - x0 + 1, z1 - z0 + 1),
                    kind = kind, ceiling = Mathf.Max(OfficeBiome.CeilingHeight, rs.ceiling), painted = rs.painted || rs.ceiling > OfficeBiome.CeilingHeight + 0.01f,
                    lights = rs.lights, noDoors = true,     // doors come from the blueprint, never at random
                });
            }
        }

        // ---------------- Edges ----------------

        /// <summary>Applies the blueprint's edges; opens the exits onto walkable neighbours (and at least one).</summary>
        public static void Connect(OfficeLayout L, in LandmarkPlacement p, List<Room> rooms)
        {
            if (!TryResolve(p, out var bp, out var v, out var xf)) return;
            var rect = p.rect;                       // (an in-parameter cannot be used inside the local function below)
            var origin = new Vector2Int(rect.x, rect.y);
            int exits = 0;
            var fallback = new List<(int x, int z, bool vertical)>();

            void Apply(Vector2Int a, Vector2Int b, int code, bool perimeter)
            {
                a += origin; b += origin;
                bool vertical = a.x == b.x;
                int line = vertical ? a.x : a.y, along = vertical ? Mathf.Min(a.y, b.y) : Mathf.Min(a.x, b.x);
                int kind = EdgeCode.Kind(code);
                if (perimeter)
                {
                    // Cells on both sides: this one, and the neighbour that must be walkable and inside the district.
                    int ix = vertical ? (line == rect.x ? line : line - 1) : along, iz = vertical ? along : (line == rect.y ? line : line - 1);
                    int ox = vertical ? (line == rect.x ? line - 1 : line) : along, oz = vertical ? along : (line == rect.y ? line - 1 : line);
                    bool wantsOut = kind == EdgeCode.Tall || kind == EdgeCode.Header;
                    bool canOut = L.InChunk(ox, oz) && L.RoomAt(ox, oz).Walkable && !L.RoomAt(ox, oz).custom && L.InChunk(ix, iz);
                    if (!wantsOut || !canOut)
                    {
                        Set(L, vertical, line, along, EdgeKind.Wall, false);
                        if (canOut) fallback.Add((line, along, vertical));
                        return;
                    }
                    exits++;
                }
                switch (kind)
                {
                    case EdgeCode.Wall: Set(L, vertical, line, along, EdgeKind.Wall, false); break;
                    case EdgeCode.Tall: Set(L, vertical, line, along, EdgeKind.Door, false); break;
                    case EdgeCode.Header: Set(L, vertical, line, along, EdgeKind.DoorHeader, EdgeCode.Arched(code)); break;
                    case EdgeCode.Half:
                    case EdgeCode.Railing:
                        Set(L, vertical, line, along, EdgeKind.Open, false);
                        L.halfWalls.Add(new HalfWall { alongX = !vertical, line = line, start = along, length = 1, roomColor = RoomIdAt(L, vertical, line, along), railing = kind == EdgeCode.Railing });
                        break;
                    default: Set(L, vertical, line, along, EdgeKind.Open, false); break;
                }
            }

            for (int x = 0; x <= v.width; x++)
            for (int z = 0; z < v.depth; z++)
                Apply(xf.Vertex(x, z), xf.Vertex(x, z + 1), v.V(x, z), x == 0 || x == v.width);
            for (int x = 0; x < v.width; x++)
            for (int z = 0; z <= v.depth; z++)
                Apply(xf.Vertex(x, z), xf.Vertex(x + 1, z), v.H(x, z), z == 0 || z == v.depth);

            if (exits == 0 && fallback.Count > 0)
            {
                // Nothing leads out (every exit faced something unwalkable): make one so the room is never sealed off.
                var f = fallback[fallback.Count / 2];
                Set(L, f.vertical, f.x, f.z, EdgeKind.DoorHeader, false);
            }
        }

        static void Set(OfficeLayout L, bool vertical, int line, int along, EdgeKind k, bool arch)
        {
            if (vertical) { if (line < 0 || line > L.N || along < 0 || along >= L.N) return; L.vEdges[line, along] = k; L.archV[line, along] = arch; }
            else { if (line < 0 || line > L.N || along < 0 || along >= L.N) return; L.hEdges[along, line] = k; L.archH[along, line] = arch; }
        }

        static int RoomIdAt(OfficeLayout L, bool vertical, int line, int along)
        {
            int cx = vertical ? Mathf.Min(line, L.N - 1) : along, cz = vertical ? along : Mathf.Min(line, L.N - 1);
            cx = Mathf.Clamp(cx, 0, L.N - 1); cz = Mathf.Clamp(cz, 0, L.N - 1);
            return L.roomOf[cx, cz];
        }

        // ---------------- Dressing ----------------

        public static void Decorate(OfficeLayout L, in LandmarkPlacement p, List<Room> rooms)
        {
            if (!TryResolve(p, out var bp, out var v, out var xf)) return;
            var origin = new Vector2Int(p.rect.x, p.rect.y);

            foreach (var f in v.fixtures)
            {
                var c = xf.Cell(f.x, f.z) + origin;
                if (!L.InChunk(c.x, c.y)) continue;
                var room = L.RoomAt(c.x, c.y);
                L.fixtures.Add(new Fixture { cell = c, on = f.on, roomColor = room.id, dome = room.Tall, height = room.ceiling, faulty = f.on && f.faulty });
            }
            foreach (var pl in v.pillars)
            {
                var vtx = xf.Vertex(pl.x, pl.z) + origin;
                var inside = new Vector2Int(Mathf.Clamp(vtx.x, p.rect.xMin, p.rect.xMax - 1), Mathf.Clamp(vtx.y, p.rect.yMin, p.rect.yMax - 1));
                L.pillars.Add(new Pillar { vertex = vtx, size = pl.size, roomColor = L.roomOf[inside.x, inside.y] });
            }
            var rng = new System.Random(DistrictGenerator.Hash(p.seed, 0x9E37, 0x79B9, 0x7F4A));
            for (int i = 0; i < v.props.Count; i++)
            {
                var d = v.props[i];
                if (d.chance < 1f && rng.NextDouble() >= d.chance) continue;
                var m = xf.Metres(d.x, d.z);
                var pos = m + new Vector2(origin.x * C, origin.y * C);
                int cx = Mathf.Clamp(Mathf.FloorToInt(pos.x / C), 0, L.N - 1), cz = Mathf.Clamp(Mathf.FloorToInt(pos.y / C), 0, L.N - 1);
                L.props.Add(new Prop { kind = d.kind, position = pos, yaw = xf.Yaw(d.yaw), size = d.size, variant = d.variant, lit = d.lit, roomColor = L.roomOf[cx, cz] });
            }
            for (int i = 0; i < v.loot.Count; i++)
            {
                var d = v.loot[i];
                if (d.chance < 1f && rng.NextDouble() >= d.chance) continue;
                var m = xf.Metres(d.x, d.z);
                var pos = m + new Vector2(origin.x * C, origin.y * C);
                int cx = Mathf.Clamp(Mathf.FloorToInt(pos.x / C), 0, L.N - 1), cz = Mathf.Clamp(Mathf.FloorToInt(pos.y / C), 0, L.N - 1);
                // For now loot is a loose cardboard box; the item name travels in the blueprint for the systems that will use it.
                L.props.Add(new Prop { kind = PropKind.Box, position = pos, yaw = (float)(rng.NextDouble() * 360.0), size = new Vector3(0.5f, 0.38f, 0.4f), variant = rng.Next(1 << 16), roomColor = L.roomOf[cx, cz] });
            }
        }
    }
}
