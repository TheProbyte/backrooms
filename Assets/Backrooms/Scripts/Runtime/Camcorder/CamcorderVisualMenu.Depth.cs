using System.Collections;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// F5 > PLACES > HEIGHT AND DEPTH: jump to the nearest sunken room, pit room, ramp, ceiling coming down, sloping room,
    /// stairs to nowhere, mezzanine or cut corner (DepthPlan), placed where it is seen best. Press again for the next nearest.
    /// Searches outward ring by ring from the player's district until it has enough, up to about 8 districts away.
    /// </summary>
    public partial class CamcorderVisualMenu
    {
        public const string DepthSunken = "sunken", DepthPit = "pit", DepthRamp = "ramp", DepthLowCeiling = "lowceiling",
            DepthSloping = "sloping", DepthNowhere = "nowhere", DepthMezzanine = "mezzanine", DepthCorner = "corner";

        static readonly int[] LDX = { -1, 1, 0, 0 }, LDZ = { 0, 0, -1, 1 };

        void BuildDepthLocations()
        {
            Section("HEIGHT AND DEPTH", "The world's floors and ceilings that are not flat (Docs/WorldDepthPlan.md). Each puts you where the thing is seen best; press again for the next nearest of the same kind.");
            var a = BtnRow(_list);
            Button(a, "SUNKEN ROOM", () => GoDepth(DepthSunken));
            Button(a, "PIT ROOM", () => GoDepth(DepthPit));
            Button(a, "RAMP", () => GoDepth(DepthRamp));
            Button(a, "LOW CEILING", () => GoDepth(DepthLowCeiling));
            var b = BtnRow(_list);
            Button(b, "SLOPING ROOM", () => GoDepth(DepthSloping));
            Button(b, "STAIRS TO NOWHERE", () => GoDepth(DepthNowhere));
            Button(b, "MEZZANINE", () => GoDepth(DepthMezzanine));
            Button(b, "CUT CORNER", () => GoDepth(DepthCorner));
        }

        /// <summary>The nearest place of a kind (see the Depth* names), the next nearest on each press.</summary>
        public void GoDepth(string kind)
        {
            var ws = FindAnyObjectByType<WorldStreamer>();
            if (ws == null || player == null) { if (_locReadout != null) _locReadout.text = "Only in the procedural world."; return; }
            int request = ++_locSearch;
            if (_locReadout != null) _locReadout.text = "Searching nearby districts…";
            StartCoroutine(SearchDepth(request, kind, ws));
        }

        IEnumerator SearchDepth(int request, string kind, WorldStreamer ws)
        {
            var world = ws.World;
            var here = player.transform.position;
            var dh = WorldGenerator.DistrictOf(WorldStreamer.ChunkOf(here));
            _locCycle.TryGetValue("depth:" + kind, out int want);
            var found = new List<(Vector3 pos, Vector3 look, float pitch, string what, float dist)>();
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
                    if (plan.style == ZoneStyle.Pool) continue;
                    var origin = new Vector2(d.x, d.y) * (WorldGenSettings.DistrictCells * LocC);
                    foreach (var s in DepthSpots(plan, origin, kind))
                        found.Add((s.pos, s.look, s.pitch, s.what, Vector3.Distance(s.pos, here)));
                    if (++scanned % 4 == 0) yield return null;
                }
                // Enough once this ring is done: the next one to show, and a couple beyond it.
                if (found.Count > want + 2 && ring >= 1) break;
            }
            world.Trim(WorldStreamer.ChunkOf(player.transform.position), ws.unloadRadius + 2);
            if (request != _locSearch) yield break;
            if (found.Count == 0) { if (_locReadout != null) _locReadout.text = "None within about 8 districts. Walk somewhere else and try again."; yield break; }
            found.Sort((x, y) => x.dist.CompareTo(y.dist));
            int i = want % found.Count;
            _locCycle["depth:" + kind] = i + 1;
            var f = found[i];
            Go(f.pos, f.look, f.pitch, $"{f.what} {i + 1} of {found.Count}");
        }

        /// <summary>Where to stand for each place of a kind in a district plan, what to look at, how far down, and its name.</summary>
        static IEnumerable<(Vector3 pos, Vector3 look, float pitch, string what)> DepthSpots(OfficeLayout L, Vector2 origin, string kind)
        {
            float C = LocC;
            Vector3 P(float x, float z, float y = 0.05f) => new Vector3(origin.x + x * C, y, origin.y + z * C);
            switch (kind)
            {
                case DepthSunken:
                {
                    // At a doorway into a sunken room, looking down its steps. One per room.
                    var seen = new HashSet<int>();
                    if (L.steps == null) yield break;
                    for (int x = 0; x < L.N; x++)
                    for (int z = 0; z < L.N; z++)
                    {
                        int s = L.StepSide(x, z);
                        if (s < 0 || !seen.Add(L.roomOf[x, z])) continue;
                        float ox = x + 0.5f + LDX[s] * 1.1f, oz = z + 0.5f + LDZ[s] * 1.1f;
                        yield return (P(ox, oz), P(x + 0.5f - LDX[s] * 2f, z + 0.5f - LDZ[s] * 2f), 22f, $"a sunken room ({-L.RoomAt(x, z).floor:0.00} m down)");
                    }
                    break;
                }
                case DepthPit:
                    foreach (var r in L.rooms)
                    {
                        if (r.feature != RoomFeature.Pit) continue;
                        // Inside its first doorway (a landing), looking across the shafts.
                        var rc = r.rect;
                        bool done = false;
                        for (int x = rc.xMin; x < rc.xMax && !done; x++)
                        for (int z = rc.yMin; z < rc.yMax && !done; z++)
                            for (int s = 0; s < 4; s++)
                            {
                                int nx = x + LDX[s], nz = z + LDZ[s];
                                if (L.InChunk(nx, nz) && L.roomOf[nx, nz] == r.id) continue;
                                if (!OfficeLayout.IsPassable(L.EdgeOf(x, z, s))) continue;
                                yield return (P(x + 0.5f, z + 0.5f), P(rc.center.x, rc.center.y), 28f, "a pit room");
                                done = true; break;
                            }
                    }
                    break;
                case DepthRamp:
                case DepthSloping:
                    foreach (var r in L.rooms)
                    {
                        if (r.ramp == 0 || (kind == DepthRamp) != (r.rampCells > 0)) continue;
                        // At the ramp's top (or the sloping room's high end), looking down it.
                        var f = r.rect;
                        int len = r.rampCells > 0 ? r.rampCells : (r.ramp <= 2 ? f.width : f.height);
                        float top = r.rampCells > 0 ? len + 0.6f : len - 0.6f;
                        float cx = f.center.x, cz = f.center.y;
                        Vector2 at, low;
                        switch (r.ramp)
                        {
                            case 1: at = new Vector2(f.xMin + top, cz); low = new Vector2(f.xMin, cz); break;
                            case 2: at = new Vector2(f.xMax - top, cz); low = new Vector2(f.xMax, cz); break;
                            case 3: at = new Vector2(cx, f.yMin + top); low = new Vector2(cx, f.yMin); break;
                            default: at = new Vector2(cx, f.yMax - top); low = new Vector2(cx, f.yMax); break;
                        }
                        float y = L.FloorOf(r, at.x, at.y) + 0.05f;
                        yield return (P(at.x, at.y, y), P(low.x, low.y), 14f, kind == DepthRamp ? $"a ramp ({-r.floor:0.00} m down)" : $"a sloping room ({-r.floor:0.00} m down at its far end)");
                    }
                    break;
                case DepthLowCeiling:
                    foreach (var r in L.rooms)
                    {
                        if (r.ceilDrop == 0) continue;
                        // Where the ceiling starts coming down, a cell back, looking toward the slot.
                        var f = r.rect;
                        float back = r.ceilCells + 1.5f, cx = f.center.x, cz = f.center.y;
                        Vector2 at, end;
                        switch (r.ceilDrop)
                        {
                            case 1: at = new Vector2(f.xMax - back, cz); end = new Vector2(f.xMax, cz); break;
                            case 2: at = new Vector2(f.xMin + back, cz); end = new Vector2(f.xMin, cz); break;
                            case 3: at = new Vector2(cx, f.yMax - back); end = new Vector2(cx, f.yMax); break;
                            default: at = new Vector2(cx, f.yMin + back); end = new Vector2(cx, f.yMin); break;
                        }
                        yield return (P(at.x, at.y), P(end.x, end.y), -4f, "a ceiling coming down to a crawl slot");
                    }
                    break;
                case DepthNowhere:
                    foreach (var st in L.nowhere)
                    {
                        // Three cells in front of the flight, facing the wall it climbs into.
                        float x = st.cell.x + 0.5f, z = st.cell.y + 0.5f;
                        yield return (P(x - LDX[st.side] * 3.2f, z - LDZ[st.side] * 3.2f), P(x, z), 6f, "stairs to nowhere");
                    }
                    break;
                case DepthMezzanine:
                    foreach (var m in L.mezzanines)
                    {
                        // Out in the hall, four cells from the wall at the strip's middle, looking at the deck.
                        var c = m.Cell(m.length / 2);
                        float x = c.x + 0.5f, z = c.y + 0.5f;
                        yield return (P(x - LDX[m.side] * 4.5f, z - LDZ[m.side] * 4.5f), P(x, z), -22f, "a mezzanine (its stair at one end)");
                    }
                    break;
                case DepthCorner:
                    foreach (var r in L.rooms)
                    {
                        if (r.chamfers == 0) continue;
                        var f = r.rect;
                        for (int c = 0; c < 4; c++)
                        {
                            if ((r.chamfers & (1 << c)) == 0) continue;
                            float x = (c & 1) == 0 ? f.xMin : f.xMax, z = (c & 2) == 0 ? f.yMin : f.yMax;
                            float ix = (c & 1) == 0 ? 1f : -1f, iz = (c & 2) == 0 ? 1f : -1f;
                            // From the room's middle side of the corner, looking at it.
                            float back = Mathf.Min(2.6f, Mathf.Min(f.width, f.height) - 0.5f);
                            yield return (P(x + ix * back, z + iz * back), P(x, z), 6f, "a cut corner");
                            break;
                        }
                    }
                    break;
            }
        }

        void Go(Vector3 pos, Vector3 look, float pitch, string what)
        {
            if (player == null) return;
            Vector3 to = look - pos; to.y = 0f;
            float yaw = to.sqrMagnitude > 0.01f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : 0f;
            float dist = Vector3.Distance(player.transform.position, pos);
            player.TeleportSafe(pos, yaw, pitch);
            if (_locReadout != null) _locReadout.text = $"Went to {what} ({dist:0} m away). You wait in place until the floor has loaded.";
        }
    }
}
