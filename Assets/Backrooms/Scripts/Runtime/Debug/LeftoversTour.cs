using System.Collections;
using System.IO;
using System.Text;
using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Pictures of Level 0's rarer things (2026-11-03): moves the player round a grid of places so their chunks get built,
    /// then for each kind goes where F5 > PLACES > WEAR AND DETAIL would (CamcorderVisualMenu.GoRealism) and takes a
    /// screenshot. Add in Play Mode in a world (World 3), set <see cref="folder"/>; <see cref="done"/> when finished, the
    /// report in <see cref="result"/>.
    /// </summary>
    public class LeftoversTour : MonoBehaviour
    {
        public string folder;
        public string[] kinds;
        /// <summary>Models to find by their mesh's name (furniture has no F5 > PLACES entry): the nearest of each.</summary>
        public string[] meshes;
        /// <summary>Props to find in the plan by their kind's name (PropKind: the good rooms' furniture, merged into the
        /// chunk's meshes), the nearest of each; "Kind:n" takes the n-th nearest.</summary>
        public string[] props;
        /// <summary>How far in front of a prop to stand (0: from its size).</summary>
        public float propDistance = 0f;
        public float spacing = 36f;
        public int ring = 2;
        /// <summary>Seconds to wait where each one is (its chunk may have to be built again).</summary>
        public float settle = 5f;
        /// <summary>Close up: stand this far from each (0: where F5 > PLACES puts you).</summary>
        public float closeUp = 0f;
        public bool done;
        public string result = "";

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            var ws = WorldStreamer.Current;
            var ctrl = ws.player.GetComponent<CamcorderPlayerController>();
            var menu = FindAnyObjectByType<CamcorderVisualMenu>();
            if (!ctrl || !menu) { result = "missing parts"; done = true; yield break; }
            Vector3 home = ws.player.position;
            float yaw0 = ws.player.eulerAngles.y;
            // Build the chunks round a grid of places (the sightings stay once a chunk has been made).
            for (int x = -ring; x <= ring; x++)
            for (int z = -ring; z <= ring; z++)
            {
                ctrl.TeleportSafe(home + new Vector3(x * spacing, 0f, z * spacing), yaw0);
                yield return new WaitForSeconds(1.2f);
            }
            var sb = new StringBuilder();
            foreach (var k in kinds)
            {
                var list = RealismSightings.Of(k, home);
                if (list.Count == 0) { sb.Append(k).Append(": none; "); continue; }
                if (closeUp > 0f)
                {
                    // Near the nearest one, looking straight at it.
                    var s = list[0];
                    var p = s.position;
                    float floor = Mathf.Floor((p.y + 0.01f) / 9f) * 9f;                   // (the storey's floor: a ray hit the thing itself)
                    Vector3 n = new Vector3(s.facing.x, 0f, s.facing.z);
                    if (n.sqrMagnitude < 0.01f) n = Vector3.forward;
                    n.Normalize();
                    if (Mathf.Abs(s.facing.y) > 0.5f)
                    {
                        // On the floor: from whichever side has room.
                        float best = -1f;
                        foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                        {
                            float room = Physics.Raycast(new Vector3(p.x, floor + 1f, p.z), d, out var h2, 2f) ? h2.distance : 2f;
                            if (room > best) { best = room; n = d; }
                        }
                    }
                    float dist = Mathf.Abs(s.facing.y) > 0.5f ? closeUp * 1.2f : closeUp;
                    var stand = new Vector3(p.x, floor, p.z) + n * dist;
                    var look = p - (stand + Vector3.up * 1.6f);
                    float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                    float pitch = Mathf.Atan2(-look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
                    ctrl.TeleportSafe(stand, yaw, pitch);                                   // (held until its chunk has a floor)
                }
                else menu.GoRealism(k);
                yield return new WaitForSeconds(settle);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, k + ".png"));
                yield return new WaitForSeconds(0.4f);
                sb.Append(k).Append(": ").Append(list.Count).Append("; ");
            }
            // Furniture by its model's mesh: the nearest of each, from the side with room, at a distance for its size.
            if (meshes != null)
                foreach (var mn in meshes)
                {
                    MeshFilter best = null; float bd = float.MaxValue;
                    foreach (var mf in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                    {
                        if (!mf.sharedMesh || !mf.sharedMesh.name.StartsWith(mn)) continue;
                        float d = (mf.transform.position - home).sqrMagnitude;
                        if (d < bd) { bd = d; best = mf; }
                    }
                    if (!best) { sb.Append(mn).Append(": none; "); continue; }
                    var rd = best.GetComponent<Renderer>();
                    var c = rd ? rd.bounds.center : best.transform.position;
                    float size = rd ? rd.bounds.extents.magnitude : 0.5f;
                    float floor = Mathf.Floor((c.y + 0.01f) / 9f) * 9f;
                    var dir = best.transform.forward; dir.y = 0f;
                    if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
                    dir.Normalize();
                    float dist = Mathf.Clamp(size * 1.6f, 0.8f, 3f);
                    // In front if there is room, else whichever side has most.
                    Vector3 pick = dir; float room = -1f;
                    foreach (var dd in new[] { dir, Quaternion.Euler(0f, 40f, 0f) * dir, Quaternion.Euler(0f, -40f, 0f) * dir, -dir })
                    {
                        float r = Physics.Raycast(new Vector3(c.x, floor + 1.2f, c.z) + dd * (size * 0.6f), dd, out var h3, dist + 0.5f) ? h3.distance : dist + 0.5f;
                        if (r > room + 0.3f) { room = r; pick = dd; }
                    }
                    var stand = new Vector3(c.x, floor, c.z) + pick * Mathf.Min(dist, room + size * 0.6f - 0.3f);
                    var look = c - (stand + Vector3.up * 1.6f);
                    ctrl.TeleportSafe(stand, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, Mathf.Atan2(-look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg);
                    yield return new WaitForSeconds(settle);
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "mesh_" + mn + ".png"));
                    yield return new WaitForSeconds(0.4f);
                    sb.Append(mn).Append(" at ").Append(c.ToString("F0")).Append("; ");
                }
            // The good rooms' furniture, from the plan: stand in front of it, looking at it.
            if (props != null && ws.World != null)
                foreach (var pk in props)
                {
                    string kname = pk; int nth = 0;
                    int colon = pk.IndexOf(':');
                    if (colon > 0) { kname = pk.Substring(0, colon); int.TryParse(pk.Substring(colon + 1), out nth); }
                    if (!System.Enum.TryParse<PropKind>(kname, out var kind)) { sb.Append(pk).Append(": no such kind; "); continue; }
                    float C = OfficeBiome.CellSize, D = WorldGenSettings.DistrictCells * C;
                    var found = new System.Collections.Generic.List<(Vector3 pos, float yaw, Vector3 size, float dist)>();
                    int hx = Mathf.FloorToInt(home.x / D), hz = Mathf.FloorToInt(home.z / D);
                    for (int dx = -2; dx <= 2; dx++)
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        var L = ws.World.District(new Vector3Int(hx + dx, 0, hz + dz));
                        if (L == null) continue;
                        var origin = new Vector3((hx + dx) * D, 0f, (hz + dz) * D);
                        foreach (var pr in L.props)
                            if (pr.kind == kind)
                            {
                                var wp = origin + new Vector3(pr.position.x, 0f, pr.position.y);
                                found.Add((wp, pr.yaw, pr.size, (wp - home).sqrMagnitude));
                            }
                    }
                    if (found.Count <= nth) { sb.Append(pk).Append(": none; "); continue; }
                    found.Sort((x, y) => x.dist.CompareTo(y.dist));
                    var f = found[nth];
                    var fwd = Quaternion.Euler(0f, f.yaw, 0f) * Vector3.forward;
                    float dist = propDistance > 0f ? propDistance : Mathf.Clamp(Mathf.Max(f.size.x, f.size.y) * 1.1f, 1.0f, 2.6f);
                    var stand = f.pos + fwd * dist + Quaternion.Euler(0f, f.yaw, 0f) * Vector3.right * (dist * 0.25f);
                    float ty = kind == PropKind.BreakerPanel ? f.size.y : f.size.y * 0.55f;
                    var look = f.pos + Vector3.up * ty - (stand + Vector3.up * 1.6f);
                    ctrl.TeleportSafe(stand, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, Mathf.Atan2(-look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg);
                    yield return new WaitForSeconds(settle);
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "prop_" + pk.Replace(':', '_') + ".png"));
                    yield return new WaitForSeconds(0.4f);
                    sb.Append(pk).Append(" at ").Append(f.pos.ToString("F0")).Append("; ");
                }
            ctrl.TeleportSafe(home, yaw0);
            result = sb.ToString();
            done = true;
        }
    }
}
