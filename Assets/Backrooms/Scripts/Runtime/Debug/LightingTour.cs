using System.Collections;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Visits a set of typical views (big lit office, dark room beside a lit one, doorway between lit
    /// rooms, corridor, tall hall, warehouse) and saves a screenshot of each to <see cref="folder"/>.
    /// Used to review lighting changes quickly. Add in play mode and call Run().
    /// </summary>
    public class LightingTour : MonoBehaviour
    {
        public string folder = "";
        public string prefix = "tour";
        public bool Busy { get; private set; }
        public string Report = "";

        public void Run() { if (!Busy) StartCoroutine(Go()); }

        struct View { public string name; public Vector3 pos; public float yaw, pitch; }

        IEnumerator Go()
        {
            Busy = true; Report = "";
            var views = FindViews();
            var pl = FindAnyObjectByType<CamcorderPlayerController>();
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
            bool bigDone = false, darkDone = false, doorDone = false, hallDone = false, tallDone = false, wareDone = false;
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
                    if (a.id == b.id || !(e == EdgeKind.Door || e == EdgeKind.DoorHeader) || a.Tall || b.Tall) continue;
                    if (!darkDone && a.lights == LightMode.Dark && b.lights == LightMode.AllOn && a.rect.width >= 3)
                    { darkDone = true; list.Add(new View { name = "2_dark_room_by_lit", pos = o + new Vector3((a.rect.xMin + 0.5f) * C, 0, (z + 0.5f) * C), yaw = 90f, pitch = 8f }); }
                    if (!doorDone && a.lights == LightMode.AllOn && b.lights == LightMode.AllOn && a.rect.width >= 3)
                    { doorDone = true; list.Add(new View { name = "3_doorway_lit_lit", pos = o + new Vector3((x - 1.5f) * C, 0, (z + 0.5f) * C - 0.5f), yaw = 80f, pitch = 18f }); }
                }
            }
            list.Sort((p, q) => string.CompareOrdinal(p.name, q.name));
            return list;
        }
    }
}
