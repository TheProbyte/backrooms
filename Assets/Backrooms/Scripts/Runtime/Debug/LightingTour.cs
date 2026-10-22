using System.Collections;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Visits a set of typical views (big lit office, dark room beside a lit one, doorway between lit
    /// rooms, corridor, tall hall, warehouse, arch, chunk borders) and saves a screenshot of each to
    /// <see cref="folder"/>. Used to review lighting changes. Add in play mode and call Run().
    ///
    /// Before/after comparisons: set <see cref="seed"/> (the world is rebuilt with it first) and
    /// <see cref="viewsFile"/>. The first run saves the chosen views there; later runs replay exactly
    /// the same camera positions, even if generation details changed.
    /// </summary>
    public class LightingTour : MonoBehaviour
    {
        public string folder = "";
        public string prefix = "tour";
        [Tooltip("World seed to tour (0 = keep the current world).")] public int seed;
        [Tooltip("JSON file with saved views: loaded if it exists, written otherwise.")] public string viewsFile = "";
        public bool Busy { get; private set; }
        public string Report = "";

        public void Run() { if (!Busy) StartCoroutine(Go()); }

        [System.Serializable] public struct View { public string name; public Vector3 pos; public float yaw, pitch; }
        [System.Serializable] class ViewList { public List<View> views = new List<View>(); }

        IEnumerator Go()
        {
            Busy = true; Report = "";
            var ws = FindAnyObjectByType<WorldStreamer>();
            var pl = FindAnyObjectByType<CamcorderPlayerController>();
            if (seed != 0 && ws && ws.Seed != seed)
            {
                ws.SetSeed(seed);
                for (int i = 0; i < 30; i++) yield return null;
            }
            List<View> views = null;
            if (!string.IsNullOrEmpty(viewsFile) && System.IO.File.Exists(viewsFile))
                views = JsonUtility.FromJson<ViewList>(System.IO.File.ReadAllText(viewsFile)).views;
            if (views == null)
            {
                views = FindViews();
                if (!string.IsNullOrEmpty(viewsFile))
                    System.IO.File.WriteAllText(viewsFile, JsonUtility.ToJson(new ViewList { views = views }, true));
            }
            foreach (var v in views)
            {
                pl.Teleport(v.pos, v.yaw, v.pitch);
                for (int i = 0; i < 40; i++) yield return null;            // settle exposure and streaming
                yield return new WaitForSeconds(1.2f);
                string path = System.IO.Path.Combine(folder, $"{prefix}_{v.name}.png");
                ScreenCapture.CaptureScreenshot(path);
                Report += v.name + " " + v.pos + "\n";
                yield return null; yield return null;
            }
            Busy = false;
        }

        List<View> FindViews()
        {
            var list = new List<View>();
            var ws = FindAnyObjectByType<WorldStreamer>();
            var center = WorldStreamer.ChunkOf(Camera.main.transform.position);
            const float C = OfficeBiome.CellSize, S = OfficeBiome.ChunkSize;
            const int N = OfficeBiome.ChunkCells;
            bool bigDone = false, darkDone = false, doorDone = false, hallDone = false, tallDone = false, wareDone = false, archDone = false;
            int borderWalls = 0, borderDoors = 0;
            for (int cx = center.x - 2; cx <= center.x + 2; cx++)
            for (int cz = center.y - 2; cz <= center.y + 2; cz++)
            {
                var L = ws.LayoutAt(new Vector2Int(cx, cz));
                if (L == null) continue;
                Vector3 o = new Vector3(cx * S, 0.05f, cz * S);
                foreach (var r in L.rooms)
                {
                    var rc = r.rect;
                    if (!bigDone && !r.Tall && r.lights == LightMode.AllOn && rc.width >= 6 && rc.height >= 5)
                    { bigDone = true; list.Add(new View { name = "1_big_office", pos = o + new Vector3((rc.xMin + 0.6f) * C, 0, (rc.yMin + 0.6f) * C), yaw = 45f, pitch = 4f }); }
                    if (!tallDone && r.Tall && r.lights == LightMode.AllOn && rc.width >= 5 && rc.height >= 5 && L.rules.stripLights < 0.5f)
                    { tallDone = true; list.Add(new View { name = "5_tall_hall", pos = o + new Vector3((rc.xMin + 0.6f) * C, 0, (rc.yMin + 0.6f) * C), yaw = 45f, pitch = -8f }); }
                    if (!wareDone && r.Tall && L.rules.stripLights > 0.5f && r.lights != LightMode.Dark)
                    { wareDone = true; list.Add(new View { name = "6_warehouse", pos = o + new Vector3((rc.xMin + 0.6f) * C, 0, (rc.yMin + 0.6f) * C), yaw = 45f, pitch = -5f }); }
                    if (!hallDone && r.kind == RoomKind.Hallway && r.lights == LightMode.AllOn && Mathf.Max(rc.width, rc.height) >= 6)
                    {
                        hallDone = true;
                        bool alongX = rc.width > rc.height;
                        list.Add(new View { name = "4_corridor", pos = o + new Vector3((rc.xMin + 0.5f) * C, 0, (rc.yMin + 0.5f) * C), yaw = alongX ? 90f : 0f, pitch = 3f });
                    }
                }
                for (int x = 2; x < L.N - 2; x++)
                for (int z = 1; z < L.N - 1; z++)
                {
                    var a = L.RoomAt(x, z); var b = L.RoomAt(x + 1, z); var e = L.vEdges[x + 1, z];
                    if (!archDone && e == EdgeKind.DoorHeader && OfficeChunkBuilder.IsArchedEdge(L, true, x + 1, z) && a.rect.width >= 3)
                    { archDone = true; list.Add(new View { name = "7_arch", pos = o + new Vector3((x - 1.5f) * C, 0, (z + 0.5f) * C), yaw = 90f, pitch = 4f }); }
                    if (a.id == b.id || !(e == EdgeKind.Door || e == EdgeKind.DoorHeader) || a.Tall || b.Tall) continue;
                    if (!darkDone && a.lights == LightMode.Dark && b.lights == LightMode.AllOn && a.rect.width >= 3)
                    { darkDone = true; list.Add(new View { name = "2_dark_room_by_lit", pos = o + new Vector3((a.rect.xMin + 0.5f) * C, 0, (z + 0.5f) * C), yaw = 90f, pitch = 8f }); }
                    if (!doorDone && a.lights == LightMode.AllOn && b.lights == LightMode.AllOn && a.rect.width >= 3)
                    { doorDone = true; list.Add(new View { name = "3_doorway_lit_lit", pos = o + new Vector3((x - 1.5f) * C, 0, (z + 0.5f) * C - 0.5f), yaw = 80f, pitch = 18f }); }
                }

                // Chunk borders (east edge of this chunk): a wall with a lit room on the far side, seen from a
                // dimmer room (where light through the wall shows), and a border doorway between lit rooms.
                var east = ws.LayoutAt(new Vector2Int(cx + 1, cz));
                if (east == null) continue;
                for (int z = 2; z < N - 2; z++)
                {
                    var mine = L.RoomAt(N - 1, z); var far = east.RoomAt(0, z);
                    bool farLit = false;
                    foreach (var f in east.fixtures) if (f.on && f.cell.x <= 2 && Mathf.Abs(f.cell.y - z) <= 1) farLit = true;
                    if (borderWalls < 3 && L.vEdges[N, z] == EdgeKind.Wall && farLit && mine.lights != LightMode.AllOn && mine.rect.width >= 3 && !mine.Tall)
                    {
                        borderWalls++;
                        list.Add(new View { name = "8_border_wall_" + borderWalls, pos = o + new Vector3((N - 2.6f) * C, 0, (z + 0.5f) * C), yaw = 90f, pitch = 6f });
                        z += 3;
                        continue;
                    }
                    if (borderDoors < 2 && OfficeLayout.IsPassable(L.vEdges[N, z]) && mine.lights == LightMode.AllOn && far.lights == LightMode.AllOn && mine.rect.width >= 3)
                    {
                        borderDoors++;
                        list.Add(new View { name = "9_border_doorway_" + borderDoors, pos = o + new Vector3((N - 2.8f) * C, 0, (z + 0.5f) * C - 0.4f), yaw = 82f, pitch = 14f });
                        z += 3;
                    }
                }
            }
            // Places where an unshadowed light would reach through a chunk-border wall (from the same
            // checks the tests run): seen from the receiving room, looking at the wall toward the light.
            if (ws.World != null)
            {
                int leaks = 0;
                var block = new RectInt(center.x - 2, center.y - 2, 5, 5);
                foreach (var issue in WorldChecks.LightLeaks(ws.World, block))
                {
                    if (leaks >= 4 || !issue.crossChunk || !issue.what.StartsWith("fixture")) continue;
                    var room = WorldChecks.RoomAt(ws.World, issue.cell);
                    if (room.lights == LightMode.AllOn || room.Tall || Mathf.Min(room.rect.width, room.rect.height) < 3) continue;
                    Vector2 dest = new Vector2((issue.cell.x + 0.5f) * C, (issue.cell.y + 0.5f) * C);
                    Vector2 src = new Vector2((issue.from.x + 0.5f) * C, (issue.from.y + 0.5f) * C);
                    Vector2 dir = (src - dest).normalized;
                    // Step back into the receiving room (stay inside its rectangle).
                    var ch = new Vector2Int(Mathf.FloorToInt(issue.cell.x / (float)N), Mathf.FloorToInt(issue.cell.y / (float)N));
                    Vector2 rmin = new Vector2((ch.x * N + room.rect.xMin + 0.4f) * C, (ch.y * N + room.rect.yMin + 0.4f) * C);
                    Vector2 rmax = new Vector2((ch.x * N + room.rect.xMax - 0.4f) * C, (ch.y * N + room.rect.yMax - 0.4f) * C);
                    Vector2 at = dest - dir * 2.2f;
                    at = new Vector2(Mathf.Clamp(at.x, rmin.x, rmax.x), Mathf.Clamp(at.y, rmin.y, rmax.y));
                    leaks++;
                    list.Add(new View { name = "10_leak_" + leaks, pos = new Vector3(at.x, 0.05f, at.y), yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, pitch = 5f });
                }
            }
            list.Sort((p, q) => string.CompareOrdinal(p.name, q.name));
            return list;
        }
    }
}
