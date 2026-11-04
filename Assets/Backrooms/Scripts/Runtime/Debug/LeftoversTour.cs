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
            ctrl.TeleportSafe(home, yaw0);
            result = sb.ToString();
            done = true;
        }
    }
}
