using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// First-use stutters (2026-11-09, Docs/ImprovementPlan.md E1). On Direct3D 12 the graphics card's pipeline state for a
    /// shader variant is made the first time something is drawn with it, on the frame that needs it: a first ride down to
    /// the floor and the first seconds in a corridor had frames of 240 to 320 ms, second visits none. Unity 6's answer is
    /// to record the states a play-through uses (GraphicsStateCollection) and make them all ahead of time.
    /// <list type="bullet">
    /// <item><b>Recording</b> (a development build): run with <c>-trace-pso [file]</c> and play, or run the benchmark, through
    ///   everything; on quitting the states are written to the file (default: next to the player log). Copy it to
    ///   <c>Assets/StreamingAssets/GraphicsStates/</c> under the name it was written with.</item>
    /// <item><b>Warming</b> (every build): at start-up the file for this graphics API, if the build has one, is loaded and
    ///   its states are made on worker threads while the title menu is up. Without a file nothing happens.</item>
    /// </list>
    /// A recording is only as good as the play-through behind it: re-record after adding places or materials.
    /// </summary>
    public static class PsoWarmup
    {
        static GraphicsStateCollection s_trace, s_warm;
        static string s_traceFile;

        /// <summary>The file's name for this graphics API (one recording per API: states differ between them).</summary>
        public static string FileName => "Overflow_" + SystemInfo.graphicsDeviceType + ".graphicsstate";

        public static string ShippedPath => Path.Combine(Application.streamingAssetsPath, "GraphicsStates", FileName);

        /// <summary>How many states the shipped file held and how many are made so far (0, 0 with no file).</summary>
        public static int Total => s_warm ? s_warm.totalGraphicsStateCount : 0;
        public static int Done => s_warm ? s_warm.completedWarmupCount : 0;
        public static bool Ready => !s_warm || s_warm.isWarmedUp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (Application.isEditor) return;
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-trace-pso");
            if (i >= 0)
            {
                if (!Debug.isDebugBuild) { Debug.LogWarning("[PsoWarmup] -trace-pso needs a development build: nothing is recorded."); return; }
                s_traceFile = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.Combine(Application.persistentDataPath, FileName);
                s_trace = new GraphicsStateCollection();
                s_trace.BeginTrace();
                Application.quitting += EndTrace;
                Debug.Log("[PsoWarmup] Recording the pipeline states this run uses, to " + s_traceFile);
                return;                                        // (a recording run warms nothing: it must see every first use)
            }
            if (System.Array.IndexOf(args, "-no-pso-warmup") >= 0) return;
            string path = ShippedPath;
            if (!File.Exists(path)) return;
            try
            {
                s_warm = new GraphicsStateCollection();
                if (!s_warm.LoadFromFile(path)) { Debug.LogWarning("[PsoWarmup] Could not read " + path); s_warm = null; return; }
                s_warm.WarmUp();
                Debug.Log($"[PsoWarmup] Making {s_warm.totalGraphicsStateCount} pipeline states of {s_warm.variantCount} shader variants ahead of time.");
            }
            catch (System.Exception e) { Debug.LogWarning("[PsoWarmup] " + e.Message); s_warm = null; }
        }

        static void EndTrace()
        {
            if (!s_trace) return;
            try
            {
                s_trace.EndTrace();
                Directory.CreateDirectory(Path.GetDirectoryName(s_traceFile) ?? ".");
                bool ok = s_trace.SaveToFile(s_traceFile);
                Debug.Log($"[PsoWarmup] {(ok ? "Wrote" : "Could not write")} {s_trace.totalGraphicsStateCount} states of {s_trace.variantCount} variants to {s_traceFile}");
            }
            catch (System.Exception e) { Debug.LogWarning("[PsoWarmup] " + e.Message); }
            s_trace = null;
        }
    }
}
