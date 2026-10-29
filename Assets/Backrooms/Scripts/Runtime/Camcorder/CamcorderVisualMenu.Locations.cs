using System.Collections.Generic;
using System.Collections;
using Backrooms.CustomRooms;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backrooms.Camcorder
{
    /// <summary>F5 > PLACES: jump to specific kinds of places (atriums, stairs, elevators, colonnade halls) without searching for them.</summary>
    public partial class CamcorderVisualMenu
    {
        const float LocC = OfficeBiome.CellSize;
        Label _locReadout;
        readonly Dictionary<string, int> _locCycle = new Dictionary<string, int>();
        int _locSearch;
        void CancelLocationSearch()
        {
            _locSearch++;
            if (_locReadout != null && _locReadout.text == "Searching nearby districts…") _locReadout.text = "Search cancelled.";
        }

        /// <summary>Landmarks of a kind within about 10 districts of the player, nearest first.</summary>
        void NearbyLandmarks(LandmarkKind kind, int needConnectors, string blueprint, System.Action<List<(LandmarkPlacement p, float dist)>> found)
        {
            int request = ++_locSearch;
            if (_locReadout != null) _locReadout.text = "Searching nearby districtsâ€¦";
            StartCoroutine(SearchLandmarks(request, kind, needConnectors, blueprint, found));
        }

        IEnumerator SearchLandmarks(int request, LandmarkKind kind, int needConnectors, string blueprint, System.Action<List<(LandmarkPlacement p, float dist)>> found)
        {
            var result = new List<(LandmarkPlacement p, float dist)>();
            var ws = FindAnyObjectByType<WorldStreamer>();
            if (ws == null || player == null) { found(result); yield break; }
            var world = ws.World;
            var here = player.transform.position;
            var dh = WorldGenerator.DistrictOf(WorldStreamer.ChunkOf(here));
            int scanned = 0;
            for (int dx = -10; dx <= 10; dx++)
            for (int dz = -10; dz <= 10; dz++)
            {
                if (request != _locSearch || world != ws.World) yield break;
                foreach (var l in world.LandmarksIn(new Vector2Int(dh.x + dx, dh.y + dz)))
                {
                    if (l.kind != kind || (needConnectors != 0 && (l.connectors & needConnectors) == 0)) continue;
                    if (blueprint != null && l.blueprint != blueprint) continue;
                    var c = l.core.width > 0 ? l.core : l.rect;
                    var centre = new Vector3((l.district.x * WorldGenSettings.DistrictCells + c.center.x) * LocC, 0f, (l.district.y * WorldGenSettings.DistrictCells + c.center.y) * LocC);
                    result.Add((l, Vector2.Distance(new Vector2(centre.x, centre.z), new Vector2(here.x, here.z))));
                }
                if (++scanned % 8 == 0) yield return null;
            }
            result.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            world.Trim(WorldStreamer.ChunkOf(player.transform.position), ws.unloadRadius + 2);
            if (request == _locSearch) found(result);
        }

        static Vector2 DistrictOrigin(in LandmarkPlacement l) => new Vector2(l.district.x, l.district.y) * (WorldGenSettings.DistrictCells * LocC);

        void Go(Vector3 pos, Vector3 look, string what)
        {
            if (player == null) return;
            Vector3 to = look - pos; to.y = 0f;
            float yaw = to.sqrMagnitude > 0.01f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : 0f;
            float dist = Vector3.Distance(player.transform.position, pos);
            player.TeleportSafe(pos, yaw, 0f);
            if (_locReadout != null) _locReadout.text = $"Went to {what} ({dist:0} m away). You wait in place until the floor has loaded.";
        }

        /// <summary>Picks the next place of a kind each time it is pressed (nearest first, then the next nearest...).</summary>
        void GoLandmark(string key, LandmarkKind kind, int needConnectors, System.Func<LandmarkPlacement, (Vector3 pos, Vector3 look, string what)> where)
        {
            NearbyLandmarks(kind, needConnectors, null, list =>
            {
                if (list.Count == 0) { if (_locReadout != null) _locReadout.text = "None found within about 10 districts. Walk somewhere else and try again."; return; }
                _locCycle.TryGetValue(key, out int i);
                i = i % list.Count;
                _locCycle[key] = i + 1;
                var (pos, look, what) = where(list[i].p);
                Go(pos, look, $"{what} {i + 1} of {list.Count}");
            });
        }

        (Vector3, Vector3, string) AtriumSpot(LandmarkPlacement l, bool upper)
        {
            var ws = FindAnyObjectByType<WorldStreamer>();
            float pitch = ws.World.Settings.storeyPitch;
            var core = l.core.width > 0 ? l.core : l.rect;
            var o = DistrictOrigin(l);
            float y = (upper ? pitch : 0f) + 0.05f;
            var pos = new Vector3(o.x + (core.x + 1.5f) * LocC, y, o.y + (core.y + 1.0f) * LocC);        // in the ring, on the south side
            var look = new Vector3(o.x + core.center.x * LocC, y, o.y + core.center.y * LocC);
            return (pos, look, upper ? "an atrium's balcony" : "an atrium");
        }

        void BuildLocations()
        {
            Section("WHERE TO", "Jump to a kind of place. Press again for the next nearest of the same kind. Rooms away from the world's centre are as generated by the seed.");
            _locReadout = AddText("", "cc-readout");

            Section("MY ROOMS", "Jump to the nearest placed custom room, or the nearest placement of a saved design. Only rooms enabled when this world was generated can appear.");
            Button(BtnRow(_list), "NEAREST CUSTOM ROOM", () => GoCustomRoom(null));
            foreach (var bp in RoomLibrary.All)
            {
                if (!bp.enabled) continue;
                string id = bp.id;
                Button(BtnRow(_list), bp.name, () => GoCustomRoom(id));
            }

            Section("ATRIUMS", "The two-storey volume with the stairs and elevator wing. The balcony is on the upper storey.");
            var r1 = BtnRow(_list);
            Button(r1, "ATRIUM", () => GoLandmark("atrium", LandmarkKind.Atrium, 0, l => AtriumSpot(l, false)));
            Button(r1, "BALCONY", () => GoLandmark("balcony", LandmarkKind.Atrium, 0, l => AtriumSpot(l, true)));
            var r2 = BtnRow(_list);
            Button(r2, "STAIRS", () => GoLandmark("stairs", LandmarkKind.Atrium, AtriumAnnex.Stairs, l =>
            {
                AtriumAnnex.TryGet(l, FindAnyObjectByType<WorldStreamer>().World.Settings.storeyPitch, out var a);
                var o = DistrictOrigin(l);
                var p = a.f.Point(1.0f, 1.2f) + o; var q = a.f.Point(8f, 1.2f) + o;
                return (new Vector3(p.x, 0.05f, p.y), new Vector3(q.x, 0f, q.y), "the foot of a staircase");
            }));
            Button(r2, "ELEVATOR", () => GoLandmark("elevator", LandmarkKind.Atrium, AtriumAnnex.Elevator, l =>
            {
                AtriumAnnex.TryGet(l, FindAnyObjectByType<WorldStreamer>().World.Settings.storeyPitch, out var a);
                var o = DistrictOrigin(l);
                var p = a.f.Point((a.length - AtriumAnnex.ShaftCells) * LocC - 1.7f, 3.5f * LocC) + o; var q = a.f.Point((a.length - AtriumAnnex.ShaftCells) * LocC + 1f, 3.5f * LocC) + o;
                return (new Vector3(p.x, 0.05f, p.y), new Vector3(q.x, 1.4f, q.y), "an elevator lobby");
            }));

            BuildDepthLocations();

            Section("TESTING", "Stand next to the nearest crawl space (its vent: crawl in with Ctrl) or the nearest noclip door (the taped outline: walk into the wall). Searches about 10 districts around you; press again for the next nearest.");
            var rt = BtnRow(_list);
            Button(rt, "NEAREST VENT", () => GoNearest("vent"));
            Button(rt, "NEAREST NOCLIP DOOR", () => GoNearest("door"));

            Section("ZONES", "The Poolrooms are reached through noclip doors. The entrance puts you in front of the nearest one leading there; walk through the taped wall.");
            var rz = BtnRow(_list);
            Button(rz, "POOLROOMS ENTRANCE", () => GoZoneHole("Poolrooms", false));
            Button(rz, "POOLROOMS", () => GoZoneHole("Poolrooms", true));

            Section("OTHER", "");
            var r3 = BtnRow(_list);
            Button(r3, "COLONNADE HALL", () => GoLandmark("colonnade", LandmarkKind.ColonnadeHall, 0, l =>
            {
                var o = DistrictOrigin(l);
                var pos = new Vector3(o.x + (l.rect.x + 1.5f) * LocC, 0.05f, o.y + (l.rect.y + 1.5f) * LocC);
                var look = new Vector3(o.x + l.rect.center.x * LocC, 0f, o.y + l.rect.center.y * LocC);
                return (pos, look, "a colonnade hall");
            }));
            Button(r3, "SPAWN", () => Go(new Vector3(7.2f, 0.05f, 7.2f), new Vector3(7.2f, 0f, 14f), "the spawn hall"));
        }

        /// <summary>In front of either end of a zone's entrance hole (see ZoneRules.entrance), facing it or the room.</summary>
        /// <summary>The nearest crawl space vent or noclip door (Level 0 doors only for "door"), from the district plans around the player.</summary>
        void GoNearest(string what)
        {
            var ws = FindAnyObjectByType<WorldStreamer>();
            if (ws == null || player == null) return;
            int request = ++_locSearch;
            if (_locReadout != null) _locReadout.text = "Searching nearby districts\u2026";
            StartCoroutine(SearchNearest(request, what, ws));
        }

        IEnumerator SearchNearest(int request, string what, WorldStreamer ws)
        {
            var world = ws.World;
            var here = player.transform.position;
            var dh = WorldGenerator.DistrictOf(WorldStreamer.ChunkOf(here));
            var found = new List<(Vector3 pos, Vector3 normal, float dist)>();
            int scanned = 0, R = what == "door" ? 12 : 3;
            for (int dx = -R; dx <= R; dx++)
            for (int dz = -R; dz <= R; dz++)
            {
                if (request != _locSearch || world != ws.World) yield break;
                var d = new Vector2Int(dh.x + dx, dh.y + dz);
                if (what == "door" && !NoclipDoors.LinkHere(world.Settings, world.Seed, d, out _)) continue;
                var plan = world.District(new Vector3Int(d.x, 0, d.y));
                if (what == "vent")
                    foreach (var p in plan.portals) { if (p.space) found.Add((p.position, p.normal, Vector3.Distance(p.position, here))); }
                else
                    foreach (var nd in plan.noclipDoors) { if (!nd.arrival) found.Add((nd.position, nd.normal, Vector3.Distance(nd.position, here))); }
                if (++scanned % 6 == 0) yield return null;
            }
            world.Trim(WorldStreamer.ChunkOf(player.transform.position), ws.unloadRadius + 2);
            if (request != _locSearch) yield break;
            if (found.Count == 0) { if (_locReadout != null) _locReadout.text = what == "vent" ? "No crawl space nearby." : "No noclip door within about 12 districts."; yield break; }
            found.Sort((a, b) => a.dist.CompareTo(b.dist));
            _locCycle.TryGetValue(what, out int i);
            i %= found.Count;
            _locCycle[what] = i + 1;
            var f = found[i];
            var floor = new Vector3(f.pos.x, f.pos.y + 0.05f, f.pos.z);
            if (what == "vent") Go(floor + f.normal * 1.3f, floor, $"crawl space {i + 1} of {found.Count} (crawl in with Ctrl)");
            else Go(floor + f.normal * 1.6f, floor, $"noclip door {i + 1} of {found.Count} (walk into the taped wall)");
        }

        void GoZoneHole(string zoneName, bool inside)
        {
            if (zoneName == "Poolrooms")
            {
                if (!inside) { GoNearest("door"); return; }
                // Inside: in front of a Poolrooms arrival door.
                var w = FindAnyObjectByType<WorldStreamer>();
                if (w == null || player == null) return;
                int zp = w.World.Settings.ZoneIndex(zoneName);
                if (zp >= 0)
                {
                    var c = w.World.Settings.zones[zp].pinCentre;
                    foreach (var nd in w.World.District(new Vector3Int(c.x, 0, c.y)).noclipDoors)
                    {
                        if (!nd.arrival) continue;
                        var floor = new Vector3(nd.position.x, 0.05f, nd.position.z);
                        Go(floor + nd.normal * 1.2f, floor + nd.normal * 6f, "the Poolrooms (behind you: its taped door back)");
                        return;
                    }
                }
            }
            var ws = FindAnyObjectByType<WorldStreamer>();
            if (ws == null || player == null) return;
            int zi = ws.World.Settings.ZoneIndex(zoneName);
            if (zi < 0 || !ws.World.Settings.zones[zi].entrance) { if (_locReadout != null) _locReadout.text = zoneName + " has no entrance in this world's settings."; return; }
            var z = ws.World.Settings.zones[zi];
            var district = inside ? z.entranceTo : z.entranceFrom;
            foreach (var p in ws.World.District(new Vector3Int(district.x, 0, district.y)).portals)
            {
                if (!p.far) continue;
                var floor = new Vector3(p.position.x, 0.05f, p.position.z);
                if (inside) Go(floor + p.normal * 1.0f, floor + p.normal * 6f, "the " + zoneName);
                else Go(floor + p.normal * 1.0f, floor, "the way into the " + zoneName + " (the low hole in the wall)");
                return;
            }
            if (_locReadout != null) _locReadout.text = "The entrance to the " + zoneName + " could not be placed in this world.";
        }

        void GoCustomRoom(string blueprint)
        {
            var ws = FindAnyObjectByType<WorldStreamer>();
            if (ws == null || player == null) { if (_locReadout != null) _locReadout.text = "Custom rooms are available in the procedural world."; return; }
            NearbyLandmarks(LandmarkKind.Custom, 0, blueprint, list =>
            {
                foreach (var hit in list)
            {
                    var l = hit.p;
                    var plan = ws.World.District(new Vector3Int(l.district.x, 0, l.district.y));
                    if (!CustomRoomStamp.TryArrival(plan, l.rect, out var local)) continue;
                    var o = DistrictOrigin(l);
                    var pos = new Vector3(o.x + local.x, 0.05f, o.y + local.y);
                    var look = new Vector3(o.x + l.rect.center.x * LocC, 0f, o.y + l.rect.center.y * LocC);
                    Go(pos, look, l.name);
                    return;
                }
                if (_locReadout != null) _locReadout.text = "No reachable custom room found within about 10 districts. Enable and save a room, then start or regenerate the world. Spawn chance and biome filters still apply.";
            });
        }
    }
}
