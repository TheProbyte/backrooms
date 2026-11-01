using System.Collections;
using System.Collections.Generic;
using System.IO;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Look check for the Poolrooms' water and tiles in the sun: finds sunlit water near the player (from the shaders' own
    /// map, <see cref="PoolWorld.SunAt"/>), stands a few metres off with a clear view of it and saves a screenshot of each.
    /// Run from the editor while playing in the Poolrooms.
    /// </summary>
    public class PoolShots : MonoBehaviour
    {
        public static void Run(string folder, int count = 4, float pitch = 22f)
        {
            var go = new GameObject("PoolShots");
            go.AddComponent<PoolShots>().StartCoroutine(go.GetComponent<PoolShots>().Shots(folder, count, pitch));
        }

        /// <summary>Sunlit water spots near the player, at least 6 m apart, nearest first.</summary>
        public static List<Vector3> SunlitWater(int count)
        {
            var w = PoolWorld.Instance;
            var list = new List<Vector3>();
            if (!w || !w.streamer || !w.streamer.player) return list;
            var pp = w.streamer.player.position;
            float C = OfficeBiome.CellSize;
            int s = w.StoreyOf(pp.y);
            float waterY = s * w.streamer.Settings.storeyPitch + PoolBiome.WaterLevel;
            int cx = Mathf.FloorToInt(pp.x / C), cz = Mathf.FloorToInt(pp.z / C);
            for (int r = 0; r < 30 && list.Count < count; r++)
            for (int dz = -r; dz <= r && list.Count < count; dz++)
            for (int dx = -r; dx <= r && list.Count < count; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                var c = w.MapCell(cx + dx, cz + dz, s);
                if (!c.valid || !c.basin) continue;
                var p = new Vector3((cx + dx + 0.5f) * C, waterY, (cz + dz + 0.5f) * C);
                if (w.SunAt(p + Vector3.up * 0.01f) < 0.9f) continue;
                bool far = true;
                foreach (var q in list) if ((q - p).sqrMagnitude < 36f) far = false;
                if (far) list.Add(p);
            }
            return list;
        }

        IEnumerator Shots(string folder, int count, float pitch)
        {
            Directory.CreateDirectory(folder);
            var player = FindAnyObjectByType<CamcorderPlayerController>();
            var spots = SunlitWater(count);
            int n = 0;
            foreach (var spot in spots)
            {
                // Somewhere 4-6 m off with nothing between (prefer facing the sun: the glint and the openings in view).
                Vector3 from = Vector3.zero; bool ok = false;
                for (int k = 0; k < 16 && !ok; k++)
                {
                    float ang = (225f + k * 22.5f * ((k & 1) == 0 ? 1 : -1) * ((k + 1) / 2)) * Mathf.Deg2Rad;
                    for (float dist = 5.5f; dist >= 3.5f && !ok; dist -= 1f)
                    {
                        var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                        var eye = spot + Vector3.up * 1.6f;
                        if (Physics.SphereCast(eye, 0.3f, dir, out _, dist + 0.4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        from = spot + dir * dist; ok = true;
                    }
                }
                if (!ok) continue;
                var d = spot - from;
                player.TeleportSafe(new Vector3(from.x, spot.y + 0.12f, from.z), Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pitch);
                float until = Time.realtimeSinceStartup + 2.5f;       // exposure settles
                while (Time.realtimeSinceStartup < until) yield return null;
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"s{n++}.png"));
                yield return null;
            }
            yield return null;
            File.WriteAllText(Path.Combine(folder, "done.txt"), n.ToString());
            Destroy(gameObject);
        }
    }
}
