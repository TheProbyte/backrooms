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
            // Overflow's own rooms (the hub, the lift's shaft) first: made in code, never in the library.
            bp = OverflowRooms.Get(p.blueprint) ?? SeamRooms.Get(p.blueprint) ?? (Override != null && (Override.id == p.blueprint || p.blueprint == "unsaved") ? Override : RoomLibrary.Get(p.blueprint)); v = null; xf = default;
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

        /// <summary>One RoomSpec per rectangle of cells of each blueprint room (a room of any shape is covered by rectangles);
        /// cut-away cells (an L-shaped room's notch) become hallway the world has (<see cref="Landmarks.RoomSpec.outside"/>).</summary>
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
                var rect = new RectInt(p.rect.x + x0, p.rect.y + z0, x1 - x0 + 1, z1 - z0 + 1);
                if (room < 0)
                {
                    list.Add(new Landmarks.RoomSpec { rect = rect, kind = RoomKind.Hallway, ceiling = OfficeBiome.CeilingHeight, lights = LightMode.AllOn, outside = true });
                    continue;
                }
                var rs = v.rooms[Mathf.Clamp(room, 0, v.rooms.Count - 1)];
                var kind = rs.kind == RoomKind.Crawl ? RoomKind.Office : rs.kind;
                list.Add(new Landmarks.RoomSpec
                {
                    rect = rect,
                    kind = kind, ceiling = Mathf.Max(OfficeBiome.CeilingHeight, rs.ceiling), painted = rs.painted || rs.ceiling > OfficeBiome.CeilingHeight + 0.01f,
                    lights = rs.lights, noDoors = true,     // doors come from the blueprint, never at random
                    wallFinish = rs.wallFinish, floorFinish = rs.floorFinish,
                });
            }
        }

        /// <summary>The placed room's cells (layout coordinates): those of the room, and the cut-away ones.</summary>
        static void Cells(RoomVariant v, Xf xf, Vector2Int origin, HashSet<Vector2Int> inside, HashSet<Vector2Int> notch)
        {
            for (int z = 0; z < v.depth; z++)
            for (int x = 0; x < v.width; x++)
                (v.Outside(x, z) ? notch : inside).Add(xf.Cell(x, z) + origin);
        }

        /// <summary>An opening as a hole in the placed room's wall line (layout coordinates).</summary>
        public static WallCutout ToCutout(OpeningDef o, Xf xf, Vector2Int origin)
        {
            Vector2 pa = o.vertical ? new Vector2(o.line * C, o.from) : new Vector2(o.from, o.line * C);
            Vector2 pb = o.vertical ? new Vector2(o.line * C, o.to) : new Vector2(o.to, o.line * C);
            pa = xf.Metres(pa.x, pa.y) + (Vector2)origin * C; pb = xf.Metres(pb.x, pb.y) + (Vector2)origin * C;
            bool vertical = Mathf.Abs(pa.x - pb.x) < 0.001f;
            int line = Mathf.RoundToInt((vertical ? pa.x : pa.y) / C);
            float from = Mathf.Min(vertical ? pa.y : pa.x, vertical ? pb.y : pb.x), to = Mathf.Max(vertical ? pa.y : pa.x, vertical ? pb.y : pb.x);
            return new WallCutout { vertical = vertical, line = line, from = from, to = to, sill = o.sill, top = o.top, arch = o.arch };
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
            var inside = new HashSet<Vector2Int>(); var notch = new HashSet<Vector2Int>();
            Cells(v, xf, origin, inside, notch);
            bool Walkable(int x, int z) => L.InChunk(x, z) && L.RoomAt(x, z).Walkable && !L.RoomAt(x, z).custom;

            void Apply(Vector2Int a, Vector2Int b, int code)
            {
                a += origin; b += origin;
                bool vertical = a.x == b.x;
                int line = vertical ? a.x : a.y, along = vertical ? Mathf.Min(a.y, b.y) : Mathf.Min(a.x, b.x);
                var c0 = vertical ? new Vector2Int(line - 1, along) : new Vector2Int(along, line - 1);
                var c1 = vertical ? new Vector2Int(line, along) : new Vector2Int(along, line);
                bool in0 = inside.Contains(c0), in1 = inside.Contains(c1);
                int kind = EdgeCode.Kind(code);
                if (!in0 && !in1)
                {
                    // The notch: one open space with the world round it.
                    bool n0 = notch.Contains(c0), n1 = notch.Contains(c1);
                    if (n0 && n1) Set(L, vertical, line, along, EdgeKind.Open, false);
                    else if ((n0 && Walkable(c1.x, c1.y)) || (n1 && Walkable(c0.x, c0.y))) Set(L, vertical, line, along, EdgeKind.Open, false);
                    return;
                }
                if (in0 != in1)
                {
                    // The room's edge (its outer line, or onto the notch): a wall, or a way out if there is somewhere to go.
                    var ic = in0 ? c0 : c1; var oc = in0 ? c1 : c0;
                    bool wantsOut = kind == EdgeCode.Tall || kind == EdgeCode.Header;
                    bool canOut = Walkable(oc.x, oc.y) && L.InChunk(ic.x, ic.y);
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
                Apply(xf.Vertex(x, z), xf.Vertex(x, z + 1), v.V(x, z));
            for (int x = 0; x < v.width; x++)
            for (int z = 0; z <= v.depth; z++)
                Apply(xf.Vertex(x, z), xf.Vertex(x + 1, z), v.H(x, z));

            // Doors, arches and windows of their own size: holes in the wall lines; the edges under a doorway open.
            foreach (var o in v.openings)
            {
                var cut = ToCutout(o, xf, origin);
                bool vertical = cut.vertical; int line = cut.line; float from = cut.from, to = cut.to;
                int a0 = Mathf.FloorToInt(from / C), a1 = Mathf.CeilToInt(to / C) - 1;
                if (o.Door)
                {
                    if (v.LeadsOut(o))
                    {
                        // A way out: the edge it covers most must lead onto somewhere walkable, as for a doorway.
                        int best = a0; float most = -1f;
                        for (int a = a0; a <= a1; a++) { float cover = Mathf.Min(to, (a + 1) * C) - Mathf.Max(from, a * C); if (cover > most) { most = cover; best = a; } }
                        var c0 = vertical ? new Vector2Int(line - 1, best) : new Vector2Int(best, line - 1);
                        var c1 = vertical ? new Vector2Int(line, best) : new Vector2Int(best, line);
                        bool in0 = inside.Contains(c0);
                        var ic = in0 ? c0 : c1; var oc = in0 ? c1 : c0;
                        if (!(Walkable(oc.x, oc.y) && L.InChunk(ic.x, ic.y))) continue;              // the wall stays whole
                        exits++;
                    }
                    for (int a = a0; a <= a1; a++)
                        if (Mathf.Min(to, (a + 1) * C) - Mathf.Max(from, a * C) > 0.01f) Set(L, vertical, line, a, EdgeKind.Door, false);
                }
                L.cutouts.Add(cut);
            }

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

        /// <summary>Cell-centre arrival inside a placed room, clear of its props, pillars and half walls.</summary>
        public static bool TryArrival(OfficeLayout plan, RectInt rect, out Vector2 position)
        {
            position = default;
            float best = float.MaxValue;
            for (int z = rect.yMin; z < rect.yMax; z++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                if (!plan.InChunk(x, z) || !plan.RoomAt(x, z).custom || !plan.RoomAt(x, z).Walkable || plan.Has(x, z, CellFlags.NoFloor)) continue;
                var p = new Vector2((x + 0.5f) * C, (z + 0.5f) * C);
                float distance = (p / C - rect.center).sqrMagnitude;
                if (distance >= best) continue;
                bool blocked = false;
                foreach (var prop in plan.props)
                {
                    if (prop.kind == PropKind.WallClock) continue;
                    var d = Quaternion.Euler(0f, -prop.yaw, 0f) * new Vector3(p.x - prop.position.x, 0f, p.y - prop.position.y);
                    // Conference tables include chairs in the generated footprint.
                    float extra = prop.kind == PropKind.ConferenceTable ? 1.1f : 0.35f;
                    if (Mathf.Abs(d.x) < prop.size.x * 0.5f + extra && Mathf.Abs(d.z) < prop.size.z * 0.5f + extra) { blocked = true; break; }
                }
                if (blocked) continue;
                foreach (var pillar in plan.pillars)
                    if (Mathf.Abs(p.x - pillar.vertex.x * C) < pillar.size * 0.5f + 0.35f && Mathf.Abs(p.y - pillar.vertex.y * C) < pillar.size * 0.5f + 0.35f) { blocked = true; break; }
                if (blocked) continue;
                foreach (var wall in plan.halfWalls)
                {
                    float across = wall.alongX ? p.y : p.x, along = wall.alongX ? p.x : p.y;
                    if (Mathf.Abs(across - wall.line * C) < 0.45f && along > wall.start * C - 0.35f && along < (wall.start + wall.length) * C + 0.35f) { blocked = true; break; }
                }
                if (!blocked) { best = distance; position = p; }
            }
            return best < float.MaxValue;
        }

        public static void Decorate(OfficeLayout L, in LandmarkPlacement p, List<Room> rooms)
        {
            if (!TryResolve(p, out var bp, out var v, out var xf)) return;
            var origin = new Vector2Int(p.rect.x, p.rect.y);

            foreach (var f in v.fixtures)
            {
                var c = xf.Cell(f.x, f.z) + origin;
                if (!L.InChunk(c.x, c.y)) continue;
                var room = L.RoomAt(c.x, c.y);
                L.fixtures.Add(new Fixture { cell = c, on = f.on, roomColor = room.id, dome = room.Tall, height = room.ceiling, faulty = f.on && f.faulty, mood = (byte)Mathf.Clamp(f.mood, 0, 5), buzz = f.buzz });
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
                L.props.Add(new Prop { kind = d.kind, position = pos, yaw = xf.Yaw(d.yaw), size = d.size, variant = d.variant, lit = d.lit, roomColor = L.roomOf[cx, cz],
                                       elevation = d.elevation, pitch = d.pitch, roll = d.roll, scale = d.scale, pinned = d.pinned, style = d.style, model = d.model });
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
            PoolRoomStamp.Apply(L, p, v, xf, origin);      // its Poolrooms rooms' water, roof and fittings (2026-11-04)
            Extras(L, p, v, xf, origin);
        }

        /// <summary>Build mode's Backrooms touches: marks on the walls (outlets and switches as the world's own plates), sounds,
        /// triggers and linked doorways, turned with the room.</summary>
        static void Extras(OfficeLayout L, in LandmarkPlacement p, RoomVariant v, Xf xf, Vector2Int origin)
        {
            var o2 = (Vector2)origin * C;
            foreach (var d in v.decals)
            {
                // The point on its face and its normal, turned with the room.
                var at = d.vertical ? new Vector2(d.line * C, d.along) : new Vector2(d.along, d.line * C);
                var nrm = d.vertical ? new Vector2(d.side, 0f) : new Vector2(0f, d.side);
                var pm = xf.Metres(at.x, at.y) + o2;
                var dir = xf.Metres(at.x + nrm.x * 0.1f, at.y + nrm.y * 0.1f) + o2 - pm;
                bool vertical = Mathf.Abs(dir.x) > Mathf.Abs(dir.y);
                int line = Mathf.RoundToInt((vertical ? pm.x : pm.y) / C), side = (vertical ? dir.x : dir.y) > 0f ? 1 : -1;
                float along = vertical ? pm.y : pm.x;
                if (d.kind == DecalKind.Outlet || d.kind == DecalKind.Switch)
                {
                    int ac = Mathf.FloorToInt(along / C);
                    var cell = vertical ? new Vector2Int(side > 0 ? line : line - 1, ac) : new Vector2Int(ac, side > 0 ? line : line - 1);
                    if (!L.InChunk(cell.x, cell.y)) continue;
                    int wallSide = vertical ? (side > 0 ? 0 : 1) : (side > 0 ? 2 : 3);
                    L.plates.Add(new WallPlate { cell = cell, side = wallSide, t = Mathf.Clamp(along / C - ac, 0.1f, 0.9f), height = d.kind == DecalKind.Switch ? Mathf.Clamp(d.height, 0.9f, 1.5f) : Mathf.Clamp(d.height, 0.2f, 1.2f),
                                                 isSwitch = d.kind == DecalKind.Switch, roomColor = L.roomOf[cell.x, cell.y], live = true });
                    continue;
                }
                L.decals.Add(new WallDecal { vertical = vertical, line = line, side = side, along = along, height = d.height, size = d.size, kind = (int)d.kind, seed = d.seed + p.seed });
            }
            foreach (var s in v.sounds)
            {
                var m = xf.Metres(s.x, s.z) + o2;
                L.sounds.Add(new RoomSound { position = new Vector3(m.x, s.y, m.y), kind = (int)s.kind, volume = s.volume, range = s.range });
            }
            for (int i = 0; i < v.triggers.Count; i++)
            {
                var t = v.triggers[i];
                ulong id = (ulong)(uint)DistrictGenerator.Hash(p.seed, p.district.x, p.district.y, 0x7716 + i) | ((ulong)(uint)i << 32);
                var slam = t.action == TriggerAction.DoorSlam ? v.openings.Find(o => o.id == t.door && o.Door) : null;
                bool hasDoor = slam != null;
                var door = hasDoor ? ToCutout(slam, xf, origin) : default;
                bool first = true;
                // The area's cells as rectangles (an area of any shape), each its own zone with the trigger's id.
                var done = new bool[v.width * v.depth];
                for (int z = 0; z < v.depth; z++)
                for (int x = 0; x < v.width; x++)
                {
                    if (done[z * v.width + x] || v.RoomOf(x, z) != t.area) continue;
                    int w = 1;
                    while (x + w < v.width && !done[z * v.width + x + w] && v.RoomOf(x + w, z) == t.area) w++;
                    int h = 1;
                    for (; z + h < v.depth; h++)
                    {
                        bool row = true;
                        for (int k = 0; k < w && row; k++) row = !done[(z + h) * v.width + x + k] && v.RoomOf(x + k, z + h) == t.area;
                        if (!row) break;
                    }
                    for (int j = 0; j < h; j++) for (int k = 0; k < w; k++) done[(z + j) * v.width + x + k] = true;
                    var a = xf.Cell(x, z) + origin; var b = xf.Cell(x + w - 1, z + h - 1) + origin;
                    var cells = new RectInt(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(a.x - b.x) + 1, Mathf.Abs(a.y - b.y) + 1);
                    L.triggers.Add(new RoomTrigger { cells = cells, id = id, action = (int)t.action, sound = (int)t.sound, once = t.once, delay = t.delay, hasDoor = hasDoor && first, door = door });
                    first = false;
                }
            }
            foreach (var o in v.openings)
            {
                if (o.link == 0 || !o.Door) continue;
                var other = v.openings.Find(q => q.id == o.link);
                if (other == null || !other.Door || (other.link == o.id && other.id < o.id)) continue;      // each pair once
                L.links.Add(new DoorLink { a = ToCutout(o, xf, origin), b = ToCutout(other, xf, origin), flip = o.flip || other.flip,
                                           id = (ulong)(uint)DistrictGenerator.Hash(p.seed, o.id, other.id, 0x11D) });
            }
        }
    }
}
