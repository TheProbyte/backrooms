using System.Collections;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Light-leak diagnosis: flies the camera to given positions (camera position as the OSD shows it) and
    /// captures each view in up to three lighting setups:
    ///   "full"  light grid mode as played (nearest fixtures are real shadowed lights, the rest the grid)
    ///   "grid"  light grid only (no real lights at all)
    ///   "rooms" room-layer mode (grid off)
    /// so a leak can be pinned on the real lights' shadows, the grid, or neither.
    /// </summary>
    public class LeakCapture : MonoBehaviour
    {
        [System.Serializable] public struct Shot { public string name; public Vector3 camera; public float yaw, pitch; }
        public string folder = "";
        public string prefix = "leak";
        public int seed;
        public List<Shot> shots = new List<Shot>();
        public bool full = true, gridOnly = true, rooms = true;
        public bool Busy { get; private set; }

        static LeakCapture _running;

        public void Run()
        {
            if (Busy || (_running && _running.Busy)) return;
            _running = this;
            StartCoroutine(Go());
        }

        IEnumerator Go()
        {
            Busy = true;
            var ws = FindAnyObjectByType<WorldStreamer>();
            var pl = FindAnyObjectByType<CamcorderPlayerController>();
            var lm = FindAnyObjectByType<FixtureLightManager>();
            if (seed != 0 && ws && ws.Seed != seed) ws.SetSeed(seed);
            pl.SetNoclip(true);
            int real = lm.shadowedTroffers, faulty = lm.faultyRealLights, lamps = lm.realLamps;
            var mode0 = lm.mode;
            foreach (var s in shots)
            {
                // Noclip: the camera pivot sits standEye above the body.
                pl.Teleport(s.camera - Vector3.up * pl.standEye, s.yaw, s.pitch);
                for (float t = 0; t < 2.5f || (ws && ws.QueuedChunks > 0 && t < 12f); t += Time.unscaledDeltaTime) yield return null;
                if (full) yield return Capture(s, "full", lm, FixtureLightManager.LightingMode.LightGrid, real, faulty, lamps);
                if (gridOnly) yield return Capture(s, "grid", lm, FixtureLightManager.LightingMode.LightGrid, 0, 0, 0);
                if (rooms) yield return Capture(s, "rooms", lm, FixtureLightManager.LightingMode.RoomLayers, real, faulty, lamps);
            }
            lm.shadowedTroffers = real; lm.faultyRealLights = faulty; lm.realLamps = lamps; lm.mode = mode0;
            Busy = false;
        }

        public string IsolateReport = "";
        public bool Isolating;
        public void RunIsolate(Rect region, float radius)
        {
            if (Isolating) return;
            Isolating = true; IsolateReport = "";
            StartCoroutine(Isolate(region, radius, r => { IsolateReport = r; Isolating = false; }));
        }

        /// <summary>Lights each real light near the camera on its own (grid off) and prints the mean brightness of a screen region (0..1 of the screen), to find which light brightens a surface.</summary>
        public IEnumerator Isolate(Rect region, float radius, System.Action<string> done)
        {
            var lm = FindAnyObjectByType<FixtureLightManager>();
            var grid = FindAnyObjectByType<LightGrid>();
            float dg = grid.directGain, bg = grid.bounceGain;
            var all = new List<Light>();
            var cam = Camera.main.transform.position;
            foreach (var l in FindObjectsByType<Light>()) if (l.enabled && l.gameObject.activeInHierarchy && l.type != LightType.Directional && (l.transform.position - cam).magnitude < radius) all.Add(l);
            lm.enabled = false;                               // freeze the manager so it does not re-assign lights
            var sb = new System.Text.StringBuilder();
            yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "iso_all.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            foreach (var l in all) l.enabled = false;
            yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "iso_none.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            foreach (var l in all) l.enabled = true;
            for (int i = 0; i < all.Count; i++)
            {
                var l = all[i];
                l.enabled = false;                              // everything on except this one
                yield return new WaitForSecondsRealtime(0.6f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, "iso_" + i.ToString("D2") + ".png"));
                yield return new WaitForSecondsRealtime(0.4f);
                sb.AppendLine(i.ToString("D2") + " " + l.name + " at " + l.transform.position.ToString("F1") + " I=" + l.intensity.ToString("F1"));
                l.enabled = true;
            }
            foreach (var l in all) l.enabled = true;
            lm.enabled = true; grid.directGain = dg; grid.bounceGain = bg;
            done(sb.ToString());
        }

        float Measure(Rect r)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            int x0 = (int)(r.xMin * tex.width), x1 = (int)(r.xMax * tex.width), y0 = (int)(r.yMin * tex.height), y1 = (int)(r.yMax * tex.height);
            var px = tex.GetPixels(x0, y0, x1 - x0, y1 - y0);
            float sum = 0; foreach (var p in px) sum += p.grayscale;
            Destroy(tex);
            return sum / px.Length;
        }

        IEnumerator Capture(Shot s, string tag, FixtureLightManager lm, FixtureLightManager.LightingMode mode, int real, int faulty, int lamps)
        {
            lm.mode = mode; lm.shadowedTroffers = real; lm.faultyRealLights = faulty; lm.realLamps = lamps;
            // Handover (0.6 s), grid rebuild and the auto-iris need a moment.
            for (float t = 0; t < 2.2f; t += Time.unscaledDeltaTime) yield return null;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, $"{prefix}_{s.name}_{tag}.png"));
            yield return null; yield return null;
        }
    }
}
