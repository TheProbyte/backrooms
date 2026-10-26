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
            if (gc) _report.AppendLine("settings " + JsonUtility.ToJson(gc.Options));
            world.SetSeed(1234);
            yield return null;

            yield return Spot("corridor", world, player, new Vector3(-17.76f, 0f, 36.41f), 82f, 14f, 600, Vector2.zero);
            yield return Spot("crawl hole", world, player, new Vector3(37.8f, 0f, 20.1f), 180f, 22f, 600, Vector2.zero);
            yield return Spot("walk (streaming)", world, player, new Vector3(-17.76f, 0f, 36.41f), 82f, 0f, 900, new Vector2(0f, 1f));

            File.WriteAllText(_output, _report.ToString());
            Debug.Log("[Backrooms] Benchmark written to " + _output + "\n" + _report);
            Application.Quit();
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
