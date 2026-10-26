using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Repeatable frame-time benchmark, for builds: run the game with <c>-benchmark [results file]</c>. It switches to world
    /// seed 1234, visits fixed spots (a long corridor, a crawl hole, a short walk that streams new chunks), waits for each to
    /// finish loading, records frame times and writes median / 95th percentile / worst per spot, then quits. Settings are
    /// whatever the build has saved (F5), so the numbers match what you play with.
    /// </summary>
    public class Benchmark : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-benchmark");
            if (i < 0) return;
            var b = new GameObject("Benchmark").AddComponent<Benchmark>();
            b._output = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1]
                : Path.Combine(Application.persistentDataPath, "benchmark.txt");
            DontDestroyOnLoad(b.gameObject);
        }

        string _output;
        readonly StringBuilder _report = new StringBuilder();

        IEnumerator Start()
        {
            WorldStreamer world = null; CamcorderPlayerController player = null;
            while (!world || !player)
            {
                world = FindAnyObjectByType<WorldStreamer>(); player = FindAnyObjectByType<CamcorderPlayerController>();
                yield return null;
            }
            _report.AppendLine($"Backrooms benchmark  {System.DateTime.Now:yyyy-MM-dd HH:mm}  {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})  {SystemInfo.processorType}");
            _report.AppendLine($"{Screen.width}x{Screen.height}  editor={Application.isEditor}  vsync={QualitySettings.vSyncCount}  target={Application.targetFrameRate}");
            var gc = Camcorder.GraphicsController.Instance;
            ApplyOverrides(gc);
            if (gc) _report.AppendLine("settings " + JsonUtility.ToJson(gc.Options));
            world.SetSeed(1234);
            yield return null;

            yield return Spot("corridor", world, player, new Vector3(-17.76f, 0f, 36.41f), 82f, 14f, 600, Vector2.zero);
            yield return Spot("crawl hole", world, player, new Vector3(37.8f, 0f, 20.1f), 180f, 22f, 600, Vector2.zero);
            yield return Spot("walk (streaming)", world, player, new Vector3(-17.76f, 0f, 36.41f), 82f, 0f, 900, new Vector2(0f, 1f));
            if (FindHole(world, false, out var nearHole, out var nearNormal)) yield return Crawl("crawl through", world, player, nearHole, nearNormal);
            if (FindHole(world, true, out var farHole, out var farNormal)) yield return Crawl("crawl through (far)", world, player, farHole, farNormal);

            File.WriteAllText(_output, _report.ToString());
            Debug.Log("[Backrooms] Benchmark written to " + _output + "\n" + _report);
            Application.Quit();
        }

        /// <summary>
        /// <c>-benchmark-set "a=1,b=false"</c>: graphics options for this run only (field names of GraphicsOptions; nothing is
        /// saved), plus <c>depthPriming=Disabled|Auto|Forced</c> for the player's renderer. For A/B runs of one build.
        /// </summary>
        void ApplyOverrides(Camcorder.GraphicsController gc)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-benchmark-set");
            if (i < 0 || i + 1 >= args.Length) return;
            var json = new StringBuilder("{");
            foreach (var pair in args[i + 1].Split(','))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2) continue;
                string key = kv[0].Trim(), value = kv[1].Trim();
                if (key == "depthPriming")
                {
                    var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                    var f = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (urp && f?.GetValue(urp) is UnityEngine.Rendering.Universal.ScriptableRendererData[] list && list.Length > 0 && list[0] is UnityEngine.Rendering.Universal.UniversalRendererData data
                        && System.Enum.TryParse(value, out UnityEngine.Rendering.Universal.DepthPrimingMode mode))
                        data.depthPrimingMode = mode;
                    continue;
                }
                if (key.StartsWith("feature."))   // feature.<renderer feature name>=false: switch a PC renderer feature for cost A/B
                {
                    var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                    var f = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (urp && f?.GetValue(urp) is UnityEngine.Rendering.Universal.ScriptableRendererData[] list && list.Length > 0 && list[0])
                        foreach (var feature in list[0].rendererFeatures)
                            if (feature && feature.name == key.Substring(8)) feature.SetActive(value == "true");
                    continue;
                }
                if (json.Length > 1) json.Append(',');
                bool literal = value == "true" || value == "false" || float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
                json.Append('"').Append(key).Append("\":").Append(literal ? value : "\"" + value + "\"");
            }
            json.Append('}');
            if (gc && json.Length > 2) JsonUtility.FromJsonOverwrite(json.ToString(), gc.Options);
            _report.AppendLine("overrides " + args[i + 1]);
        }

        /// <summary>A crawl hole near the origin (an ordinary pair), or the far-link hole of district (1, 1).</summary>
        static bool FindHole(WorldStreamer world, bool far, out Vector3 position, out Vector3 normal)
        {
            position = normal = default;
            var district = far ? new Vector2Int(1, 1) : Vector2Int.zero;
            int n = WorldGenSettings.DistrictChunks;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                foreach (var p in world.World.Chunk(new Vector2Int(district.x * n + x, district.y * n + z)).portals)
                    if (p.far == far) { position = p.position; normal = p.normal; return true; }
            return false;
        }

        /// <summary>
        /// Crawls into a hole from 1 m in front of it, through the duct and out of the partner, and reports the frame times
        /// plus the slowest frames with where they happened (approach, in the duct, after the trip).
        /// </summary>
        IEnumerator Crawl(string name, WorldStreamer world, CamcorderPlayerController player, Vector3 hole, Vector3 normal)
        {
            player.botInput = Vector2.zero;
            player.SetStance(CamcorderPlayerController.Stance.Crawl);
            float yaw = Mathf.Atan2(-normal.x, -normal.z) * Mathf.Rad2Deg;
            var at = new Vector3(hole.x, 0f, hole.z) + normal * 1.0f;
            player.TeleportSafe(at, yaw, 0f);
            var grid = FindAnyObjectByType<LightGrid>();
            float t0 = Time.realtimeSinceStartup;
            yield return null;
            while ((world.QueuedChunks > 0 || (grid && grid.PendingTexels > 0)) && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            // Placed again now that the floor there exists (the first placement may have been over unloaded ground).
            player.TeleportSafe(at, yaw, 0f);
            float warm = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < warm) yield return null;
            int warp0 = Interaction.CrawlPortalSystem.LastWarpFrame, warpAt = -1;
            var ms = new List<(float ms, string phase)>(1500);
            player.botInput = new Vector2(0f, 1f);
            for (int i = 0; i < 1500; i++)
            {
                yield return null;
                if (warpAt < 0 && Interaction.CrawlPortalSystem.LastWarpFrame != warp0) warpAt = i;
                var eye = Camera.main ? Camera.main.transform.position : player.transform.position;
                float d = Vector3.Dot(eye - hole, normal);
                string phase = warpAt >= 0 ? $"+{i - warpAt} frames after the trip" : d > 0.1f ? $"{d:0.00} m before the hole" : $"{-d:0.00} m into the duct";
                ms.Add((Time.unscaledDeltaTime * 1000f, phase));
                if (warpAt >= 0 && i - warpAt > 240) break;
            }
            player.botInput = null;
            player.SetStance(CamcorderPlayerController.Stance.Stand);
            var sorted = new List<float>(ms.Count); foreach (var m in ms) sorted.Add(m.ms); sorted.Sort();
            float P(float q) => sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
            int slow = 0; foreach (var m in ms) if (m.ms > 25f) slow++;
            _report.AppendLine($"{name,-18} median {P(0.5f):0.0} ms ({1000f / P(0.5f):0} fps)   p95 {P(0.95f):0.0} ms ({1000f / P(0.95f):0} fps)   worst {sorted[sorted.Count - 1]:0.0} ms   n={ms.Count}   over 25 ms: {slow}   trip: {(warpAt >= 0 ? "yes" : "NO")}");
            ms.Sort((a, b) => b.ms.CompareTo(a.ms));
            for (int i = 0; i < Mathf.Min(6, ms.Count); i++) _report.AppendLine($"    {ms[i].ms,5:0.0} ms  {ms[i].phase}");
        }

        IEnumerator Spot(string name, WorldStreamer world, CamcorderPlayerController player, Vector3 at, float yaw, float pitch, int frames, Vector2 walk)
        {
            player.botInput = Vector2.zero;
            player.TeleportSafe(at, yaw, pitch);
            var grid = FindAnyObjectByType<LightGrid>();
            float t0 = Time.realtimeSinceStartup;
            // Loaded and settled: no chunks queued, no light-grid work pending, then 3 s of warm-up.
            while ((world.QueuedChunks > 0 || (grid && grid.PendingTexels > 0)) && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            float warm = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < warm) yield return null;
            if (walk != Vector2.zero) player.botInput = walk;
            var ms = new List<float>(frames);
            for (int i = 0; i < frames; i++) { yield return null; ms.Add(Time.unscaledDeltaTime * 1000f); }
            player.botInput = null;
            ms.Sort();
            float P(float q) => ms[Mathf.Clamp(Mathf.RoundToInt(q * (ms.Count - 1)), 0, ms.Count - 1)];
            _report.AppendLine($"{name,-18} median {P(0.5f):0.0} ms ({1000f / P(0.5f):0} fps)   p95 {P(0.95f):0.0} ms ({1000f / P(0.95f):0} fps)   worst {ms[ms.Count - 1]:0.0} ms   n={ms.Count}");
        }
    }
}
