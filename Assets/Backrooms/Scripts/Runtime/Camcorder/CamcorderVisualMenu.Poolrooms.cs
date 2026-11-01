using System.Collections;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// F5 > PLACES > THE POOLROOMS (2026-11-01, the owner: "add zones as dropdowns where each zone has that zones
    /// teleportation places. and add many poolrooms sections now"): the zone's kinds of places, found in the district plans
    /// round the player (the Poolrooms' own districts only), nearest first; each press goes to the next nearest. You are
    /// put on dry floor a few metres off with the place in view (or on it, for a walkway or a roof opening).
    /// </summary>
    public partial class CamcorderVisualMenu
    {
        struct PoolSpot { public Vector3 pos, look; public float pitch; }

        static readonly (string label, string kind, string what)[] PoolPlaceKinds =
        {
            ("SLIDE TOWER", "tower", "a slide tower"), ("MUSHROOM", "mushroom", "a mushroom fountain"), ("KIDDIE POOL", "kiddie", "a kiddie pool"),
            ("WATER ARCH", "arch", "a water arch"), ("LAZY RIVER", "river", "a lazy river"), ("WAVE POOL", "wave", "a wave pool"),
            ("CHANNEL HALL", "walkway", "a channel hall's walkway"), ("FLOODED VAULT", "vault", "a vaulted channel"),
            ("LAP POOL", "lap", "a lap pool with lane ropes"), ("FREE POOL", "free", "a free-form pool"), ("ROUND HALL", "round", "a round hall"),
            ("SLOPING POOL", "slope", "a pool with a sloping floor"), ("GALLERY", "gallery", "a gallery round a tall hall"), ("SPIRAL STAIR", "spiral", "a spiral stair tower"),
            ("CURVED WALL", "arc", "a curved wall"), ("CHANGING ROOM", "changing", "changing rooms"), ("SHOWERS", "showers", "showers"),
            ("BLEACHERS", "bleachers", "bleachers"), ("LIFEGUARD CHAIR", "lifeguard", "a lifeguard's chair"), ("POOL LADDER", "ladder", "a pool ladder"),
            ("ROOF OPENING", "sky", "a roof opening"), ("FLOOR DRAIN", "drain", "a floor drain"),
        };

        void BuildPoolroomsLocations()
        {
            Section("THE POOLROOMS", "The tiled, flooded zone behind the noclip doors. Each button finds the nearest of its kind in the Poolrooms' districts round you; press again for the next nearest. Searches about 8 districts.", false);
            var outer = _list;
            _list = Group("GETTING THERE", "In front of the nearest noclip door that leads there (walk into the taped wall), or straight inside.");
            var rz = BtnRow(_list);
            Button(rz, "ENTRANCE", () => GoZoneHole("Poolrooms", false));
            Button(rz, "INSIDE", () => GoZoneHole("Poolrooms", true));
            void GroupOf(string title, string desc, int from, int to)
            {
                _list = outer;
                _list = Group(title, desc);
                UnityEngine.UIElements.VisualElement row = null;
                for (int i = from; i < to; i++)
                {
                    if ((i - from) % 3 == 0) row = BtnRow(_list);
                    string kind = PoolPlaceKinds[i].kind;
                    Button(row, PoolPlaceKinds[i].label, () => GoPool(kind));
                }
            }
            GroupOf("THE WATER PARK", "Slide towers over plunge pools, mushroom fountains in kiddie pools and flooded halls, water arches, the lazy river, the wave pool (a wave every few seconds for forty seconds, then calm).", 0, 6);
            GroupOf("THE CHANNELS", "Flooded halls with a walkway curving through them, and vaulted channels with a walkway along one side.", 6, 8);
            GroupOf("POOLS AND HALLS", "Lap pools (lane ropes), free-form pools, round halls with a round roof opening, sloping pool floors, galleries, the spiral stair towers to the floor above, curved walls.", 8, 15);
            GroupOf("ROOMS WITH A PURPOSE", "Left empty: changing stalls, showers, bleachers, a lifeguard's chair, a chrome ladder.", 15, 20);
            GroupOf("LIGHT AND WATER", "Under a roof opening (the sky, the puddles the rain leaves), or by a floor drain with water standing round it.", 20, 22);
            _list = outer;
        }

        public void GoPool(string kind)
        {
            var ws = FindAnyObjectByType<WorldStreamer>();
            if (ws == null || player == null) { if (_locReadout != null) _locReadout.text = "Only in the procedural world."; return; }
            int request = ++_locSearch;
            if (_locReadout != null) _locReadout.text = "Searching nearby districts…";
            StartCoroutine(SearchPool(request, kind, ws));
        }

        IEnumerator SearchPool(int request, string kind, WorldStreamer ws)
        {
            var world = ws.World;
            var here = player.transform.position;
            var dh = WorldGenerator.DistrictOf(WorldStreamer.ChunkOf(here));
            _locCycle.TryGetValue("pool:" + kind, out int want);
            var found = new List<(PoolSpot s, float dist)>();
            const int MaxRing = 8;
            int scanned = 0;
            for (int ring = 0; ring <= MaxRing; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                for (int dz = -ring; dz <= ring; dz++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring) continue;
                    if (request != _locSearch || world != ws.World) yield break;
                    var d = new Vector2Int(dh.x + dx, dh.y + dz);
                    var plan = world.District(new Vector3Int(d.x, 0, d.y));
                    if (plan.style != ZoneStyle.Pool || plan.pool == null) continue;
                    var origin = new Vector2(d.x, d.y) * (WorldGenSettings.DistrictCells * LocC);
                    foreach (var s in PoolSpots(plan, origin, kind)) found.Add((s, Vector3.Distance(s.pos, here)));
                    if (++scanned % 3 == 0) yield return null;
                }
                if (found.Count > want + 2 && ring >= 1) break;
            }
            world.Trim(WorldStreamer.ChunkOf(player.transform.position), ws.unloadRadius + 2);
            if (request != _locSearch) yield break;
            string what = kind;
            foreach (var k in PoolPlaceKinds) if (k.kind == kind) what = k.what;
            if (found.Count == 0) { if (_locReadout != null) _locReadout.text = $"No {what} within about {MaxRing} districts of the Poolrooms round you. Go into the Poolrooms (or further in) and try again."; yield break; }
            found.Sort((x, y) => x.dist.CompareTo(y.dist));
            int i = want % found.Count;
            _locCycle["pool:" + kind] = i + 1;
            var f = found[i].s;
            Go(f.pos, f.look, f.pitch, $"{what} {i + 1} of {found.Count}");
        }

        /// <summary>The places of a kind in one district plan (world positions; storey 0).</summary>
        static IEnumerable<PoolSpot> PoolSpots(OfficeLayout plan, Vector2 origin, string kind)
        {
            Vector3 W(Vector2 p, float y = 0f) => new Vector3(origin.x + p.x, y, origin.y + p.y);
            int n = plan.N;
            foreach (var f in plan.poolFeatures)
            {
                Vector2? target = null;
                float height = 1.2f;
                switch (kind)
                {
                    case "tower": if (f.kind == PoolFeatureKind.SlideTower) { target = f.centre + Dir2(f.yaw) * 1.6f; height = 2f; } break;
                    case "mushroom": if (f.kind == PoolFeatureKind.Mushroom) { target = f.centre; height = 1.5f; } break;
                    case "kiddie": if (f.kind == PoolFeatureKind.FreePool && f.shape == 1 && f.depth > 0f && f.depth < 0.35f && f.depth2 <= 0f) target = f.centre; break;
                    case "arch": if (f.kind == PoolFeatureKind.Arch) { target = f.centre + Dir2(f.yaw) * (f.size.x * 0.5f); height = 0.6f; } break;
                    case "river": if (f.kind == PoolFeatureKind.FreePool && f.shape == 3) target = f.centre; break;
                    case "wave": if (f.kind == PoolFeatureKind.WaveMaker) target = f.centre + Dir2(f.yaw) * 3f; break;
                    case "free": if (f.kind == PoolFeatureKind.FreePool && f.shape != 3 && f.depth >= 0.35f && !RoomHas(plan, f.room, PoolFeatureKind.RoundHall)) target = f.centre; break;
                    case "round": if (f.kind == PoolFeatureKind.RoundHall) { target = f.centre; height = 3f; } break;
                    case "slope": if (f.kind == PoolFeatureKind.Slope) target = f.bounds.center; break;
                    case "gallery": if (f.kind == PoolFeatureKind.Gallery) { target = f.bounds.center; height = 2f; } break;
                    case "vault": if (f.kind == PoolFeatureKind.Vault) { target = f.bounds.center; height = 2.5f; } break;
                    case "changing": if (f.kind == PoolFeatureKind.Changing) target = f.centre; break;
                    case "showers": if (f.kind == PoolFeatureKind.Showers) target = f.centre; break;
                    case "bleachers": if (f.kind == PoolFeatureKind.Bleachers) target = f.centre; break;
                    case "lifeguard": if (f.kind == PoolFeatureKind.Lifeguard) target = f.centre; break;
                    case "ladder": if (f.kind == PoolFeatureKind.Ladder) target = f.centre; break;
                    case "walkway":
                        if (f.kind == PoolFeatureKind.Walkway && f.shape == 0)
                        {
                            var a = PoolPlan.WalkwayPoint(f, 0.08f, out _);
                            var b = PoolPlan.WalkwayPoint(f, 0.5f, out _);
                            yield return new PoolSpot { pos = W(a, 0.05f), look = W(b), pitch = 8f };
                        }
                        break;
                }
                if (!target.HasValue) continue;
                if (StandNear(plan, target.Value, out var stand)) yield return Facing(W(stand, 0.05f), W(target.Value, height));
            }
            if (kind == "lap")
            {
                var seen = new HashSet<int>();
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (plan.pool[x, z].lanes == 0) continue;
                    var room = plan.RoomAt(x, z);
                    if (!seen.Add(room.id)) continue;
                    var c = new Vector2((room.rect.xMin + room.rect.width * 0.5f) * LocC, (room.rect.yMin + room.rect.height * 0.5f) * LocC);
                    if (StandNear(plan, c, out var stand)) yield return Facing(W(stand, 0.05f), W(c));
                }
            }
            else if (kind == "spiral")
            {
                foreach (var r in plan.rooms)
                {
                    if (!r.spiral) continue;
                    var c = new Vector2((r.rect.xMin + r.rect.width * 0.5f) * LocC, (r.rect.yMin + r.rect.height * 0.5f) * LocC);
                    var stand = c + new Vector2(PoolBiome.SpiralOuter + 0.5f, 0f);
                    yield return Facing(W(stand, 0.05f), W(c, 2.5f));
                }
            }
            else if (kind == "arc")
            {
                foreach (var a in plan.arcs)
                {
                    float mid = (a.from + a.to) * 0.5f * Mathf.Deg2Rad;
                    var onWall = a.centre + new Vector2(Mathf.Cos(mid), Mathf.Sin(mid)) * a.radius;
                    var stand = a.centre + new Vector2(Mathf.Cos(mid), Mathf.Sin(mid)) * Mathf.Max(0.8f, a.radius - 2.2f);
                    yield return Facing(W(stand, 0.05f), W(onWall, 1.6f));
                }
            }
            else if (kind == "sky" || kind == "drain")
            {
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    var c = plan.pool[x, z];
                    if (c.basin || c.solid) continue;
                    var room = plan.RoomAt(x, z);
                    if (!room.Walkable || room.spiral || plan.FreePoolRoom(x, z)) continue;
                    var at = new Vector2((x + 0.5f) * LocC, (z + 0.5f) * LocC);
                    if (kind == "sky" && c.skylight != SkylightShape.None && ((x * 7 + z * 13) & 3) == 0)
                        yield return new PoolSpot { pos = W(at + new Vector2(0.9f, 0.4f), 0.05f), look = W(at, 6f), pitch = -55f };
                    if (kind == "drain" && c.drain)
                        yield return Facing(W(at + new Vector2(1.3f, 0.4f), 0.05f), W(at, 0f));
                }
            }
        }

        /// <summary>No wall between two points of a plan (its cell edges along the line, every 10 cm).</summary>
        static bool InSight(OfficeLayout plan, Vector2 a, Vector2 b)
        {
            int n = plan.N;
            int cx = Mathf.FloorToInt(a.x / LocC), cz = Mathf.FloorToInt(a.y / LocC);
            int steps = Mathf.CeilToInt((b - a).magnitude / 0.1f);
            for (int i = 1; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                int x = Mathf.FloorToInt(p.x / LocC), z = Mathf.FloorToInt(p.y / LocC);
                if (x == cx && z == cz) continue;
                if (x < 0 || z < 0 || x >= n || z >= n || cx < 0 || cz < 0 || cx >= n || cz >= n) return false;
                // Moved one cell over (or diagonally: both ways round must be open).
                if (x != cx && !OfficeLayout.IsPassable(plan.EdgeOf(cx, cz, x > cx ? 1 : 0))) return false;
                if (z != cz && !OfficeLayout.IsPassable(plan.EdgeOf(x, cz, z > cz ? 3 : 2))) return false;
                cx = x; cz = z;
            }
            return true;
        }

        static Vector2 Dir2(float yaw) { float a = yaw * Mathf.Deg2Rad; return new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }

        static bool RoomHas(OfficeLayout plan, int room, PoolFeatureKind kind)
        {
            foreach (var f in plan.poolFeatures) if (f.room == room && f.kind == kind) return true;
            return false;
        }

        /// <summary>Standing at pos, looking at look: the pitch down (or up) to it from eye height.</summary>
        static PoolSpot Facing(Vector3 pos, Vector3 look)
        {
            var d = look - (pos + Vector3.up * 1.55f);
            float flat = new Vector2(d.x, d.z).magnitude;
            return new PoolSpot { pos = pos, look = look, pitch = Mathf.Clamp(-Mathf.Atan2(d.y, Mathf.Max(0.3f, flat)) * Mathf.Rad2Deg, -60f, 60f) };
        }

        /// <summary>A dry, open spot about 3-5 m from a point (not in water, not behind a round wall, not in a free pool),
        /// in the same room or the next, nearest that distance first.</summary>
        static bool StandNear(OfficeLayout plan, Vector2 target, out Vector2 stand)
        {
            int n = plan.N;
            int tx = Mathf.FloorToInt(target.x / LocC), tz = Mathf.FloorToInt(target.y / LocC);
            float best = float.MaxValue; stand = target;
            for (int x = tx - 5; x <= tx + 5; x++)
            for (int z = tz - 5; z <= tz + 5; z++)
            {
                if (x < 0 || z < 0 || x >= n || z >= n) continue;
                var c = plan.pool[x, z];
                if (c.basin || c.solid || c.gallery > 0f) continue;
                var room = plan.RoomAt(x, z);
                if (!room.Walkable || room.spiral || room.noFloor) continue;
                var p = new Vector2((x + 0.5f) * LocC, (z + 0.5f) * LocC);
                if (plan.FreePoolRoom(x, z))
                {
                    bool wet = false;
                    foreach (var f in plan.poolFeatures)
                        if (f.kind == PoolFeatureKind.FreePool && f.room == room.globalId && PoolBiome.ShapeSD(f, p) < 0.5f) wet = true;
                    if (wet) continue;
                }
                bool blocked = false;
                foreach (var f in plan.poolFeatures)
                {
                    if (f.kind == PoolFeatureKind.SlideTower)
                    {
                        // The tower, and its stair behind it.
                        var back = f.centre - Dir2(f.yaw) * (0.85f + f.height * 0.41f);
                        if ((p - f.centre).magnitude < 2.1f || (p - back).magnitude < 1.7f) blocked = true;
                    }
                    if (f.kind == PoolFeatureKind.Mushroom && (p - f.centre).magnitude < f.radius + 0.8f) blocked = true;
                }
                if (blocked || !InSight(plan, p, target)) continue;
                float d = (p - target).magnitude;
                float score = Mathf.Abs(d - 3.8f) + (plan.roomOf[x, z] == plan.roomOf[Mathf.Clamp(tx, 0, n - 1), Mathf.Clamp(tz, 0, n - 1)] ? 0f : 4f);
                if (score < best) { best = score; stand = p; }
            }
            return best < float.MaxValue;
        }
    }
}
