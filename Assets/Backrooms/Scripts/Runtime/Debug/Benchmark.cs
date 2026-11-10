using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Backrooms.Generation;
using Unity.Profiling;
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
        bool _testCubes, _spikeLog;

        /// <summary>
        /// -benchmark-set testcubes=1: two cubes 2.5 m in front of the camera for each spot's picture, a plain URP Lit one
        /// (left) and one with the world's wall material (Backrooms/Lit, right): a rendering path that draws one and not the
        /// other (the GPU Resident Drawer drew the world black) shows which shader it is.
        /// </summary>
        void TestCubes(bool on)
        {
            foreach (var old in GameObject.FindObjectsByType<Transform>(FindObjectsInactive.Exclude)) if (old && old.name == "BenchTestCube") Destroy(old.gameObject);
            var cam = Camera.main;
            if (!on || !cam) return;
            var assets = FindAnyObjectByType<WorldStreamer>()?.assets;
            for (int i = 0; i < 2; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "BenchTestCube";
                Destroy(c.GetComponent<Collider>());
                c.transform.position = cam.transform.position + cam.transform.forward * 2.5f + cam.transform.right * (i == 0 ? -0.6f : 0.6f);
                c.transform.localScale = Vector3.one * 0.6f;
                var r = c.GetComponent<Renderer>();
                if (i == 0) { var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = Color.white; r.sharedMaterial = m; }
                else if (assets && assets.paintedWall) r.sharedMaterial = assets.paintedWall;
            }
        }
        readonly StringBuilder _report = new StringBuilder();

        IEnumerator Start()
        {
            // Played, not behind the title menu: the menu switches off the player (walking, the body, the hands, the
            // footsteps, the handheld shake), so the walk did not walk and the frame missed what playing costs. A world of
            // its own in a folder of its own: the player's saves are never touched.
            if (Saves.GameSession.InMenu)
            {
                Saves.SaveSlots.FolderOverride = Path.Combine(Application.temporaryCachePath, "BenchmarkSaves");
                Saves.SaveSlots.Erase(0);
                yield return null;
                Saves.GameSession.Play(0);
                float until = Time.realtimeSinceStartup + 30f;
                while ((Saves.GameSession.InMenu || FindAnyObjectByType<Menu.MainMenu>()) && Time.realtimeSinceStartup < until) yield return null;
                for (int i = 0; i < 10; i++) yield return null;
            }
            WorldStreamer world = null; CamcorderPlayerController player = null;
            while (!world || !player || !player.isActiveAndEnabled)
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

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark-lift") >= 0)
            {
                // Just the lift, down and up, twice (quick runs while working on the ride).
                var wake1 = FindAnyObjectByType<Interaction.HubWake>();
                if (wake1) wake1.GetUp();
                for (int f = 0; f < 5; f++) yield return null;
                for (int round = 0; round < 2; round++)
                {
                    yield return Ride("lift down", world, player, true);
                    yield return Ride("lift up", world, player, false);
                }
                File.WriteAllText(_output, _report.ToString());
                Application.Quit();
                yield break;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark-spikes-lift") >= 0)
            {
                // (A development build.) Both lift rides with the profiler recording every frame to <results>_spikes.raw.
                var wake0 = FindAnyObjectByType<Interaction.HubWake>();
                if (wake0) wake0.GetUp();
                for (int f = 0; f < 5; f++) yield return null;
                UnityEngine.Profiling.Profiler.logFile = Path.Combine(Path.GetDirectoryName(_output) ?? ".", Path.GetFileNameWithoutExtension(_output) + "_spikes");
                UnityEngine.Profiling.Profiler.maxUsedMemory = 512 * 1024 * 1024;
                UnityEngine.Profiling.Profiler.SetAreaEnabled(UnityEngine.Profiling.ProfilerArea.GPU, true);   // what the card does too
                _spikeLog = true;
                yield return Ride("lift down", world, player, true);
                yield return Ride("lift up", world, player, false);
                File.WriteAllText(_output, _report.ToString());
                Application.Quit();
                yield break;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark-spikes") >= 0)
            {
                // (A development build.) The streaming walk with the profiler recording every frame to <results>_spikes.raw:
                // the slow frames, step by step, to open in the Profiler or read with ProfilerDriver.LoadProfile.
                UnityEngine.Profiling.Profiler.logFile = Path.Combine(Path.GetDirectoryName(_output) ?? ".", Path.GetFileNameWithoutExtension(_output) + "_spikes");
                _spikeLog = true;
                yield return Spot("walk (streaming)", world, player, new Vector3(-17.76f, 0f, 36.41f), 82f, 0f, 700, new Vector2(0f, 1f));
                File.WriteAllText(_output, _report.ToString());
                Application.Quit();
                yield break;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark-work") >= 0)
            {
                // Only the work floor's spots (the storage room, the seam trips, a client's room).
                var wakeW = FindAnyObjectByType<Interaction.HubWake>();
                if (wakeW) wakeW.GetUp();
                for (int f = 0; f < 5; f++) yield return null;
                yield return WorkFloor(world, player);
                File.WriteAllText(_output, _report.ToString());
                Application.Quit();
                yield break;
            }

            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark-markers") >= 0)
            {
                yield return Markers(world, player);
                File.WriteAllText(_output, _report.ToString());
                Application.Quit();
                yield break;
            }

            int ab = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark-ab");
            if (ab >= 0 && ab + 1 < System.Environment.GetCommandLineArgs().Length)
            {
                yield return AB(System.Environment.GetCommandLineArgs()[ab + 1], world, player, gc);
                File.WriteAllText(_output, _report.ToString());
                Application.Quit();
                yield break;
            }

            // The hub (Docs/HubPlan.md): a world starts there, on a cot; up off it, then its lift hall, a walk, and the
            // lift down to the world and back up (the ride swaps the player between two far-apart places mid-ride).
            var wake = FindAnyObjectByType<Interaction.HubWake>();
            if (wake) wake.GetUp();
            for (int f = 0; f < 5; f++) yield return null;
            var hall = HubPlan.HubCab.position + Vector3.forward * 1.6f;
            yield return Spot("hub lift hall", world, player, hall, 0f, 4f, 600, Vector2.zero);
            yield return Spot("hub walk", world, player, hall, 0f, 0f, 600, new Vector2(0f, 1f));
            if (Interaction.HubLift.Instance)
            {
                yield return Ride("lift down", world, player, true);
                yield return Ride("lift up", world, player, false);
            }

            yield return Spot("corridor", world, player, new Vector3(-17.76f, 0f, 36.41f), 82f, 14f, 600, Vector2.zero);
            yield return Spot("crawl hole", world, player, new Vector3(37.8f, 0f, 20.1f), 180f, 22f, 600, Vector2.zero);
            yield return Spot("walk (streaming)", world, player, new Vector3(-17.76f, 0f, 36.41f), 82f, 0f, 900, new Vector2(0f, 1f));
            if (FindHole(world, false, out var nearHole, out var nearNormal)) yield return Crawl("crawl through", world, player, nearHole, nearNormal);
            if (FindHole(world, true, out var farHole, out var farNormal)) yield return Crawl("crawl through (far)", world, player, farHole, farNormal);
            yield return WorkFloor(world, player);

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
                if (key == "testcubes") { _testCubes = value == "true" || value == "1"; continue; }
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

        /// <summary>
        /// <c>-benchmark-ab "label:key=value|key=value;label2:..."</c>: at the corridor spot, the settings in turn (each from the
        /// saved ones), round after round (so drift from heat or other programs falls on all of them alike), and per setting
        /// the median frame, main-thread and GPU time. One run measures what each feature costs. Keys as -benchmark-set.
        /// </summary>
        IEnumerator AB(string spec, WorldStreamer world, CamcorderPlayerController player, Camcorder.GraphicsController gc)
        {
            var configs = new List<(string label, string set)> { ("base", "") };
            foreach (var part in spec.Split(';'))
            {
                int c = part.IndexOf(':');
                if (c > 0) configs.Add((part.Substring(0, c).Trim(), part.Substring(c + 1).Replace('|', ',')));
            }
            string baseJson = gc ? JsonUtility.ToJson(gc.Options) : null;
            var rig = FindAnyObjectByType<Camcorder.CamcorderRig>();
            bool baseLook = rig && rig.Settings != null && rig.Settings.camcorderLook;
            float baseFar = Camera.main ? Camera.main.farClipPlane : 1000f;
            player.botInput = Vector2.zero;
            player.TeleportSafe(new Vector3(-17.76f, 0f, 36.41f), 82f, 14f);
            var grid = FindAnyObjectByType<LightGrid>();
            float t0 = Time.realtimeSinceStartup;
            while ((world.QueuedChunks > 0 || (grid && grid.PendingTexels > 0)) && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            float warm = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < warm) yield return null;
            var frame = new List<float>[configs.Count]; var main = new List<float>[configs.Count]; var gpu = new List<float>[configs.Count];
            var render = new List<float>[configs.Count]; var mainWork = new List<float>[configs.Count];
            for (int i = 0; i < configs.Count; i++) { frame[i] = new List<float>(); main[i] = new List<float>(); gpu[i] = new List<float>(); render[i] = new List<float>(); mainWork[i] = new List<float>(); }
            var ft = new FrameTiming[1];
            var mainRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            var gpuRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 1);
            const int rounds = 3, settle = 60, samples = 150;
            for (int r = 0; r < rounds; r++)
                for (int i = 0; i < configs.Count; i++)
                {
                    if (gc && baseJson != null) JsonUtility.FromJsonOverwrite(baseJson, gc.Options);
                    if (rig && rig.Settings != null) rig.Settings.camcorderLook = baseLook;
                    if (!ChunkVisibility.HiddenCastShadows) { ChunkVisibility.HiddenCastShadows = true; var cv = FindAnyObjectByType<ChunkVisibility>(); if (cv) { cv.ShowAll(); cv.ForceUpdate(); } }
                    if (Camera.main && Camera.main.farClipPlane != baseFar) Camera.main.farClipPlane = baseFar;
                    ApplySet(configs[i].set, gc);
                    for (int f = 0; f < settle; f++) yield return null;
                    for (int f = 0; f < samples; f++)
                    {
                        yield return null;
                        frame[i].Add(Time.unscaledDeltaTime * 1000f);
                        if (mainRec.Valid) main[i].Add(mainRec.LastValue * 1e-6f);
                        if (gpuRec.Valid) gpu[i].Add(gpuRec.LastValue * 1e-6f);
                        FrameTimingManager.CaptureFrameTimings();
                        if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
                        {
                            render[i].Add((float)ft[0].cpuRenderThreadFrameTime);
                            mainWork[i].Add((float)(ft[0].cpuMainThreadFrameTime - ft[0].cpuMainThreadPresentWaitTime));
                        }
                    }
                }
            mainRec.Dispose(); gpuRec.Dispose();
            if (gc && baseJson != null) JsonUtility.FromJsonOverwrite(baseJson, gc.Options);
            ApplySet("", gc);
            float Med(List<float> l) { if (l.Count == 0) return 0f; l.Sort(); return l[l.Count / 2]; }
            float baseGpu = Med(gpu[0]), baseFrame = Med(frame[0]);
            _report.AppendLine($"A/B at the corridor, {rounds} rounds x {samples} frames each");
            for (int i = 0; i < configs.Count; i++)
            {
                float g = Med(gpu[i]), fr = Med(frame[i]);
                _report.AppendLine($"  {configs[i].label,-16} frame {fr:0.00} ms ({fr - baseFrame:+0.00;-0.00})   main work {Med(mainWork[i]):0.00}   render thread {Med(render[i]):0.00}   gpu {g:0.00} ms ({g - baseGpu:+0.00;-0.00})   {configs[i].set}");
            }
        }

        /// <summary>
        /// <c>-benchmark-markers</c> (a development build: profiler markers are only recorded there): at the corridor spot,
        /// every profiler marker's time (all threads), averaged over 400 frames, the most expensive first; and the frame
        /// split from FrameTimingManager (main thread, render thread, GPU). Where the frame goes, script by script and engine
        /// step by engine step.
        /// </summary>
        IEnumerator Markers(WorldStreamer world, CamcorderPlayerController player)
        {
            player.botInput = Vector2.zero;
            player.TeleportSafe(new Vector3(-17.76f, 0f, 36.41f), 82f, 14f);
            var grid = FindAnyObjectByType<LightGrid>();
            float t0 = Time.realtimeSinceStartup;
            while ((world.QueuedChunks > 0 || (grid && grid.PendingTexels > 0)) && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            float warm = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < warm) yield return null;
            var names = new List<string>();
            UnityEngine.Profiling.Sampler.GetNames(names);
            var recs = new List<(string name, UnityEngine.Profiling.Recorder rec)>();
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                UnityEngine.Profiling.Recorder r;
                try { r = UnityEngine.Profiling.Recorder.Get(n); } catch (System.ArgumentException) { continue; }
                if (r == null || !r.isValid) continue;
                r.enabled = true;
                recs.Add((n, r));
            }
            var sum = new double[recs.Count];
            var timings = new FrameTiming[1];
            double fMain = 0, fRender = 0, fGpu = 0, fWait = 0, frameMs = 0; int nt = 0;
            const int frames = 400;
            yield return null;
            for (int f = 0; f < frames; f++)
            {
                yield return null;
                for (int i = 0; i < recs.Count; i++) sum[i] += recs[i].rec.elapsedNanoseconds;
                frameMs += Time.unscaledDeltaTime * 1000.0;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    fMain += timings[0].cpuMainThreadFrameTime; fRender += timings[0].cpuRenderThreadFrameTime;
                    fGpu += timings[0].gpuFrameTime; fWait += timings[0].cpuMainThreadPresentWaitTime; nt++;
                }
            }
            foreach (var r in recs) r.rec.enabled = false;
            // A profiler capture of 90 frames next to the results (<results>_profile.raw): every thread's timeline, to open
            // in the Profiler window (or read with ProfilerDriver.LoadProfile) when the totals above do not say who waits.
            UnityEngine.Profiling.Profiler.logFile = Path.Combine(Path.GetDirectoryName(_output) ?? ".", Path.GetFileNameWithoutExtension(_output) + "_profile");
            UnityEngine.Profiling.Profiler.enableBinaryLog = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            for (int f = 0; f < 90; f++) yield return null;
            UnityEngine.Profiling.Profiler.enabled = false;
            UnityEngine.Profiling.Profiler.enableBinaryLog = false;
            UnityEngine.Profiling.Profiler.logFile = "";
            _report.AppendLine($"markers at the corridor, {frames} frames, {recs.Count} markers; frame {frameMs / frames:0.00} ms");
            if (nt > 0) _report.AppendLine($"frame timing: main {fMain / nt:0.00} ms (of which waiting to present {fWait / nt:0.00})  render thread {fRender / nt:0.00} ms  gpu {fGpu / nt:0.00} ms");
            var order = new List<int>(); for (int i = 0; i < recs.Count; i++) order.Add(i);
            order.Sort((a, b) => sum[b].CompareTo(sum[a]));
            for (int k = 0; k < Mathf.Min(160, order.Count); k++)
            {
                int i = order[k];
                double ms = sum[i] / frames / 1e6;
                if (ms < 0.03) break;
                _report.AppendLine($"  {ms,7:0.000} ms  {recs[i].name}");
            }
        }

        /// <summary>Settings for this run only (as -benchmark-set): graphics options, depthPriming, feature.name.</summary>
        void ApplySet(string set, Camcorder.GraphicsController gc)
        {
            var json = new StringBuilder("{");
            foreach (var pair in set.Split(','))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2) continue;
                string key = kv[0].Trim(), value = kv[1].Trim();
                if (key.StartsWith("feature."))
                {
                    var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                    var f = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (urp && f?.GetValue(urp) is UnityEngine.Rendering.Universal.ScriptableRendererData[] list && list.Length > 0 && list[0])
                        foreach (var feature in list[0].rendererFeatures)
                            if (feature && feature.name == key.Substring(8)) feature.SetActive(value == "true");
                    continue;
                }
                if (key == "hiddenShadows") { ChunkVisibility.HiddenCastShadows = value == "true"; var cv = FindAnyObjectByType<ChunkVisibility>(); if (cv) { cv.ShowAll(); cv.ForceUpdate(); } continue; }
                if (key == "farClip") { var c = Camera.main; if (c) c.farClipPlane = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture); continue; }
                if (key == "camcorderLook")
                {
                    var rig = FindAnyObjectByType<Camcorder.CamcorderRig>();
                    if (rig && rig.Settings != null) rig.Settings.camcorderLook = value == "true";
                    continue;
                }
                if (json.Length > 1) json.Append(',');
                bool literal = value == "true" || value == "false" || float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
                json.Append('"').Append(key).Append("\":").Append(literal ? value : "\"" + value + "\"");
            }
            json.Append('}');
            if (gc && json.Length > 2) JsonUtility.FromJsonOverwrite(json.ToString(), gc.Options);
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

        /// <summary>
        /// The work, measured (2026-11-09, Docs/ImprovementPlan.md E3: the benchmark had no boxes, no seam and no client's
        /// room in it, so nobody would have seen the work slow the game down). On the leased floor by the lift: twelve
        /// shelves in two rows with 96 boxes on them as four stored batches (the tickets, the lamps and the half-second
        /// checks all running), stood in front of; then the nearest seam walked through and back twice (the first trip
        /// shows what a first use costs, the second what every trip does), and the client's room stood in.
        /// </summary>
        IEnumerator WorkFloor(WorldStreamer world, CamcorderPlayerController player)
        {
            var cab = HubPlan.WorldCab;
            Vector3 stand = cab.position + cab.rotation * new Vector3(0f, 0.05f, 1.6f);
            float yaw = cab.rotation.eulerAngles.y;
            player.botInput = Vector2.zero;
            player.TeleportSafe(stand, yaw, 6f);
            float t0 = Time.realtimeSinceStartup;
            while (world.QueuedChunks > 0 && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            for (int i = 0; i < 120; i++) yield return null;
            var d = Saves.GameSession.Data;
            var mgr = Interaction.MovablePropManager.Instance;
            if (d == null || !mgr || !Interaction.Contracts.Instance) { _report.AppendLine("work floor          (no contract floor in this world: skipped)"); yield break; }
            // The same view before anything is stored there: what the storage costs is the difference.
            yield return Spot("by the lift, empty", world, player, stand, yaw, 6f, 400, Vector2.zero);
            var batches = new Saves.BatchData[4];
            for (int k = 0; k < 4; k++)
            {
                batches[k] = new Saves.BatchData { id = ++d.nextBatch, client = k, state = 2, storeFor = 99999f, left = 99999f, window = 600f, intake = 10, completion = 20 };
                d.batches.Add(batches[k]);
            }
            int n = 0;
            var type = Interaction.ShelfTypes.Get(Interaction.ShelfTypes.Standard);
            for (int row = 0; row < 2; row++)
            for (int col = 0; col < 6; col++)
            {
                Vector3 at = stand + cab.rotation * new Vector3((col - 2.5f) * 1.2f, -0.05f, 2.6f + row * 1.8f);
                var rot = cab.rotation * Quaternion.Euler(0f, 180f, 0f);            // facing where the camera stands
                ulong shelf = Interaction.Shelves.Add(Interaction.ShelfTypes.Standard, at, rot);
                if (shelf == 0) continue;
                Interaction.Shelves.SetLevel(shelf, 1f);
                for (int level = 0; level < type.levels; level++)
                for (int side = 0; side < 2; side++)
                {
                    int client = n % 4;
                    ulong id = Interaction.Contracts.BoxId(client, d.nextBox++, 0);
                    Vector3 p = at + rot * new Vector3(side == 0 ? -0.27f : 0.27f, Interaction.ShelfTypes.DeckTop(type, level) + 0.01f, 0f);
                    var spec = Interaction.Contracts.BoxSpec(id, p, rot.eulerAngles.y);
                    if (spec == null) continue;
                    mgr.NetPlace(spec, p, rot);
                    batches[client].boxes.Add(id); batches[client].unsafeFor.Add(0f);
                    n++;
                }
            }
            for (int k = 0; k < 4; k++) batches[k].count = batches[k].boxes.Count;
            _report.AppendLine($"    (the work floor: 12 shelves, {n} boxes in 4 stored batches, by the lift)");
            yield return Spot("storage, " + n + " boxes", world, player, stand, yaw, 6f, 600, Vector2.zero);

            // The nearest seam: through and back, twice.
            Interaction.Seam seam = null; float best = float.MaxValue;
            foreach (var s in Interaction.Seam.All)
            {
                if (!s || s.kind > 3) continue;
                float dd = (s.transform.position - stand).sqrMagnitude;
                if (dd < best) { best = dd; seam = s; }
            }
            if (!seam) { _report.AppendLine("seam trip           (no seam loaded near the lift: skipped)"); yield break; }
            Vector3 front = seam.transform.position + seam.transform.forward * 1.4f + Vector3.up * 0.05f;
            float toward = Quaternion.LookRotation(-seam.transform.forward).eulerAngles.y;
            for (int round = 0; round < 2; round++)
            {
                yield return Trip("seam, there" + (round == 0 ? " (1st)" : ""), world, player, front, toward, true);
                if (!SeamPlan.InZone(player.transform.position)) { _report.AppendLine("    (did not get through: the rest of the trip skipped)"); yield break; }
                if (round == 0) yield return Spot("client's room", world, player, player.transform.position, player.transform.eulerAngles.y, 0f, 600, Vector2.zero);
                int kind = SeamPlan.KindAt(player.transform.position);
                SeamPlan.Tear(kind, out var tear, out var normal);
                yield return Trip("seam, back" + (round == 0 ? " (1st)" : ""), world, player, tear + normal * 1.4f + Vector3.up * 0.05f, Quaternion.LookRotation(-normal).eulerAngles.y, false);
            }
        }

        /// <summary>A walk through a seam: stood in front of it, walked at it, every frame until 1.5 s after arriving on the
        /// other side (or six seconds, if the way is shut).</summary>
        IEnumerator Trip(string name, WorldStreamer world, CamcorderPlayerController player, Vector3 from, float yaw, bool intoRoom)
        {
            player.botInput = Vector2.zero;
            player.Teleport(from, yaw, 0f);
            float warm = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < warm) yield return null;
            var ms = new List<float>(900);
            var split = new Split();
            player.botInput = new Vector2(0f, 1f);
            float until = Time.realtimeSinceStartup + 6f, arrivedAt = -1f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                ms.Add(Time.unscaledDeltaTime * 1000f); split.Sample();
                bool there = SeamPlan.InZone(player.transform.position) == intoRoom;
                if (there && arrivedAt < 0f) { arrivedAt = Time.realtimeSinceStartup; until = arrivedAt + 1.5f; player.botInput = Vector2.zero; }
            }
            player.botInput = null;
            var sorted = new List<float>(ms); sorted.Sort();
            float P(float q) => sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
            int over33 = 0, over50 = 0; foreach (var v in ms) { if (v > 33.4f) over33++; if (v > 50f) over50++; }
            _report.AppendLine($"{name,-18} median {P(0.5f):0.0} ms ({1000f / P(0.5f):0} fps)   p95 {P(0.95f):0.0} ms   worst {sorted[sorted.Count - 1]:0.0} ms   n={ms.Count}   >33ms {over33}   >50ms {over50}   arrived={arrivedAt >= 0f}");
            _report.AppendLine("    " + split.Report());
            split.Dispose();
        }

        /// <summary>
        /// A lift ride (fromHub: down from the hub, else up from the world's lift): stood in the cab, the button pressed, every
        /// frame from the press until the far doors are open, and the slowest frames' times in order.
        /// </summary>
        IEnumerator Ride(string name, WorldStreamer world, CamcorderPlayerController player, bool fromHub)
        {
            var lift = Interaction.HubLift.Instance;
            var frame = fromHub ? HubPlan.HubCab : HubPlan.WorldCab;
            player.botInput = Vector2.zero;
            player.TeleportSafe(frame.position + frame.rotation * new Vector3(0f, 0.05f, -1.1f), frame.rotation.eulerAngles.y, 0f);
            var grid = FindAnyObjectByType<LightGrid>();
            float t0 = Time.realtimeSinceStartup;
            while ((world.QueuedChunks > 0 || (grid && grid.PendingTexels > 0)) && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            float warm = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < warm) yield return null;
            if (_spikeLog) { UnityEngine.Profiling.Profiler.enableBinaryLog = true; UnityEngine.Profiling.Profiler.enabled = true; }
            lift.PressCab(fromHub);
            var ms = new List<float>(2000);
            var split = new Split();
            float until = Time.realtimeSinceStartup + 45f, pressed = Time.realtimeSinceStartup;
            var when = new StringBuilder("    slow at (s after the press, ms, where):");
            bool wasInHub = HubPlan.InHub(player.transform.position);
            float swappedAt = -1f;
            void Frame()
            {
                float v = Time.unscaledDeltaTime * 1000f;
                ms.Add(v); split.Sample();
                bool inHub = HubPlan.InHub(player.transform.position);
                if (inHub != wasInHub && swappedAt < 0f) swappedAt = Time.realtimeSinceStartup - pressed;
                if (v > 50f) when.Append($" {Time.realtimeSinceStartup - pressed:0.00}/{v:0}{(swappedAt >= 0f ? "*" : "")}");
            }
            // Until it has left and come to rest again (the far doors opening).
            while (!lift.Moving && Time.realtimeSinceStartup < until) { yield return null; Frame(); }
            while (lift.Moving && Time.realtimeSinceStartup < until) { yield return null; Frame(); }
            float end = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < end) { yield return null; Frame(); }
            when.Append($"   (* after the swap at {swappedAt:0.00} s)");
            player.botInput = null;
            // (The binary log stays on across both rides: one capture, cut by the frame times written below.)
            if (_spikeLog && !fromHub) { UnityEngine.Profiling.Profiler.enabled = false; UnityEngine.Profiling.Profiler.enableBinaryLog = false; }
            bool arrived = (fromHub ? !HubPlan.InHub(player.transform.position) : HubPlan.InHub(player.transform.position));
            var sorted = new List<float>(ms); sorted.Sort();
            float P(float q) => sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
            int over33 = 0, over50 = 0; foreach (var v in ms) { if (v > 33.4f) over33++; if (v > 50f) over50++; }
            _report.AppendLine($"{name,-18} median {P(0.5f):0.0} ms ({1000f / P(0.5f):0} fps)   p95 {P(0.95f):0.0} ms   worst {sorted[sorted.Count - 1]:0.0} ms   n={ms.Count}   >33ms {over33}   >50ms {over50}   arrived={arrived}");
            var worst = new StringBuilder("    slowest:");
            for (int i = sorted.Count - 1; i >= Mathf.Max(0, sorted.Count - 8); i--) worst.Append($" {sorted[i]:0}");
            _report.AppendLine(worst.ToString());
            _report.AppendLine(when.ToString());
            _report.AppendLine("    " + split.Report());
            split.Dispose();
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
            // -benchmark-shots: a picture of each spot next to the results (to compare two builds: nothing missing or changed).
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark-shots") >= 0)
            {
                if (_testCubes) { TestCubes(true); yield return null; yield return null; }
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(_output) ?? ".", Path.GetFileNameWithoutExtension(_output) + "_" + name.Split(' ')[0] + ".png"));
                yield return null; yield return null;
                if (_testCubes) TestCubes(false);
            }
            if (walk != Vector2.zero) player.botInput = walk;
            if (_spikeLog) { UnityEngine.Profiling.Profiler.enableBinaryLog = true; UnityEngine.Profiling.Profiler.enabled = true; }
            var ms = new List<float>(frames);
            var split = new Split();
            for (int i = 0; i < frames; i++) { yield return null; ms.Add(Time.unscaledDeltaTime * 1000f); split.Sample(); }
            player.botInput = null;
            if (_spikeLog) { UnityEngine.Profiling.Profiler.enabled = false; UnityEngine.Profiling.Profiler.enableBinaryLog = false; UnityEngine.Profiling.Profiler.logFile = ""; _spikeLog = false; }
            ms.Sort();
            float P(float q) => ms[Mathf.Clamp(Mathf.RoundToInt(q * (ms.Count - 1)), 0, ms.Count - 1)];
            int hitches = 0; foreach (var v in ms) if (v > 50f) hitches++;
            _report.AppendLine($"{name,-18} median {P(0.5f):0.0} ms ({1000f / P(0.5f):0} fps)   p95 {P(0.95f):0.0} ms ({1000f / P(0.95f):0} fps)   worst {ms[ms.Count - 1]:0.0} ms   >50ms {hitches}   n={ms.Count}");
            _report.AppendLine("    " + split.Report());
            split.Dispose();
        }

        /// <summary>
        /// Where a spot's frame goes (medians over its frames): the main thread, the render thread and the GPU (frame timing
        /// stats), draw and SetPass calls, triangles, and memory allocated by scripts per frame (garbage the collector must
        /// later sweep up: the usual cause of hitches).
        /// </summary>
        sealed class Split : System.IDisposable
        {
            readonly ProfilerRecorder _main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            readonly ProfilerRecorder _render = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 1);
            readonly ProfilerRecorder _gpu = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 1);
            readonly ProfilerRecorder _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            readonly ProfilerRecorder _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            readonly ProfilerRecorder _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            readonly ProfilerRecorder _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            readonly ProfilerRecorder _gcCount = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            readonly List<float> m = new List<float>(), r = new List<float>(), g = new List<float>(), d = new List<float>(), s = new List<float>(),
                t = new List<float>(), a = new List<float>(), n = new List<float>();

            public void Sample()
            {
                if (_main.Valid) m.Add(_main.LastValue * 1e-6f);
                if (_render.Valid) r.Add(_render.LastValue * 1e-6f);
                if (_gpu.Valid) g.Add(_gpu.LastValue * 1e-6f);
                if (_draws.Valid) d.Add(_draws.LastValue);
                if (_setPass.Valid) s.Add(_setPass.LastValue);
                if (_tris.Valid) t.Add(_tris.LastValue / 1000f);
                if (_gcAlloc.Valid) a.Add(_gcAlloc.LastValue / 1024f);
                if (_gcCount.Valid) n.Add(_gcCount.LastValue);
            }

            static string Med(List<float> v, string format)
            {
                if (v.Count == 0) return "-";
                v.Sort();
                return v[v.Count / 2].ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            }

            public string Report() => $"main {Med(m, "0.0")} ms  render {Med(r, "0.0")} ms  gpu {Med(g, "0.0")} ms  draws {Med(d, "0")}  setpass {Med(s, "0")}  " +
                                      $"tris {Med(t, "0")}k  gc {Med(a, "0.0")} KB in {Med(n, "0")} allocs/frame";

            public void Dispose()
            {
                _main.Dispose(); _render.Dispose(); _gpu.Dispose(); _draws.Dispose(); _setPass.Dispose(); _tris.Dispose(); _gcAlloc.Dispose(); _gcCount.Dispose();
            }
        }
    }
}
