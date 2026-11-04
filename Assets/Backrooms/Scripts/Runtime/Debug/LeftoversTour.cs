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
                menu.GoRealism(k);
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
