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
