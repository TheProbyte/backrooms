using System.Collections;
using System.Collections.Generic;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Automated crawl-space test: add it in Play Mode. For every loaded crawl space it puts the player, crawling, in front
    /// of the vent, crawls in (bot input) until it comes out in the special room, then turns round and crawls back out.
    /// Any trip that does not get through within a few seconds is reported (where the player got stuck).
    /// Results in <see cref="Report"/> and the console.
    /// </summary>
    public class VentProbe : MonoBehaviour
    {
        public float timeout = 9f;
        public int maxVents = 12;
        public string Report { get; private set; } = "running";
        public bool Done { get; private set; }

        IEnumerator Start()
        {
            var ws = WorldStreamer.Current;
            var ctrl = ws && ws.player ? ws.player.GetComponent<CamcorderPlayerController>() : null;
            if (!ctrl) { Report = "no player"; Done = true; yield break; }
            var rooms = new List<SpecialRoom>(SpecialRoom.All);
            rooms.Sort((a, b) => Vector3.Distance(a.Centre, ctrl.transform.position).CompareTo(Vector3.Distance(b.Centre, ctrl.transform.position)));
            var lines = new List<string>();
            int ok = 0, tried = 0;
            foreach (var room in rooms)
            {
                if (tried >= maxVents) break;
                if (!room) continue;
                // The world vent is straight under the room's vent, on the floor of its storey.
                Vector3 n = room.back;
                Vector3 hole = new Vector3(room.vent.x, Mathf.Floor(room.vent.y / ws.Settings.storeyPitch) * ws.Settings.storeyPitch, room.vent.z);
                tried++;
                bool inOk = false, outOk = false;
                // In.
                ctrl.SetStance(CamcorderPlayerController.Stance.Crawl);
                ctrl.Teleport(hole + n * 1.1f + Vector3.up * 0.03f, Quaternion.LookRotation(-n).eulerAngles.y, 0f);
                yield return null;
                float t = 0f;
                ctrl.botInput = new Vector2(0f, 1f);
                while (t < timeout)
                {
                    t += Time.deltaTime;
                    if (ctrl.transform.position.y > room.min.y - 0.3f && Vector3.Dot(ctrl.transform.position - room.vent, room.back) > 0.45f) { inOk = true; break; }
                    yield return null;
                }
                var stuckIn = ctrl.transform.position;
                // And back out.
                if (inOk)
                {
                    ctrl.botInput = null;
                    yield return null;
                    ctrl.Teleport(room.vent + n * 0.9f + Vector3.down * (0.4f - 0.03f), Quaternion.LookRotation(-n).eulerAngles.y, 0f);
                    ctrl.SetStance(CamcorderPlayerController.Stance.Crawl);
                    yield return null;
                    t = 0f;
                    ctrl.botInput = new Vector2(0f, 1f);
                    while (t < timeout)
                    {
                        t += Time.deltaTime;
                        if (ctrl.transform.position.y < room.min.y - 1.5f && Vector3.Dot(ctrl.transform.position - hole, n) > 0.45f) { outOk = true; break; }
                        yield return null;
                    }
                }
                ctrl.botInput = null;
                var at = ctrl.transform.position;
                if (inOk && outOk) ok++;
                lines.Add($"{room.name} at {room.vent:F1}: in {(inOk ? "ok" : "STUCK at " + stuckIn.ToString("F2"))}, out {(outOk ? "ok" : inOk ? "STUCK at " + at.ToString("F2") : "-")}");
                yield return null;
            }
            ctrl.SetStance(CamcorderPlayerController.Stance.Stand);
            Report = $"{ok}/{tried} crawl spaces passed both ways\n" + string.Join("\n", lines);
            Debug.Log("[VentProbe] " + Report);
            Done = true;
        }
    }
}
