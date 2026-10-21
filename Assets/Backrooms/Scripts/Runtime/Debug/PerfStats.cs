using Unity.Profiling;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Frame timing and render counters (main thread, render thread, GPU, draw calls, shadow casters,
    /// triangles), averaged over the last second. Used by F5 > Graphics and by automated perf checks.
    /// Recorders only run while this component is enabled.
    /// </summary>
    public class PerfStats : MonoBehaviour
    {
        public static PerfStats Instance { get; private set; }

        ProfilerRecorder _main, _render, _gpu, _draws, _casters, _tris, _setPass;
        float _acc, _accMain, _accRender, _accGpu; int _n;
        public float Fps { get; private set; }
        public float FrameMs { get; private set; }
        public float MainMs { get; private set; }
        public float RenderMs { get; private set; }
        public float GpuMs { get; private set; }
        public long DrawCalls { get; private set; }
        public long SetPassCalls { get; private set; }
        public long ShadowCasters { get; private set; }
        public long Triangles { get; private set; }

        static readonly string[] Markers =
        {
            "FixtureLightManager.LateUpdate()", "BuildingAudio.LateUpdate()", "WorldStreamer.Update()", "CamcorderRig.LateUpdate()",
            "CamcorderAutoExposure.LateUpdate()", "CamcorderOSD.LateUpdate()", "CamcorderVisualMenu.Update()", "HandheldCameraShake.LateUpdate()",
        };
        readonly ProfilerRecorder[] _marks = new ProfilerRecorder[Markers.Length];
        readonly float[] _markAcc = new float[Markers.Length];
        public readonly float[] MarkerMs = new float[Markers.Length];

        public static PerfStats Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("PerfStats");
            return go.AddComponent<PerfStats>();
        }

        void OnEnable()
        {
            Instance = this;
            _main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            _render = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 15);
            _gpu = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 15);
            _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            for (int i = 0; i < Markers.Length; i++) _marks[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, Markers[i], 15);
        }

        void OnDisable()
        {
            foreach (var m in _marks) m.Dispose();
            _main.Dispose(); _render.Dispose(); _gpu.Dispose(); _draws.Dispose(); _setPass.Dispose(); _casters.Dispose(); _tris.Dispose();
            if (Instance == this) Instance = null;
        }

        static float Ms(ProfilerRecorder r) => r.Valid && r.Count > 0 ? r.LastValue * 1e-6f : 0f;

        void Update()
        {
            _acc += Time.unscaledDeltaTime; _n++;
            _accMain += Ms(_main); _accRender += Ms(_render); _accGpu += Ms(_gpu);
            for (int i = 0; i < _marks.Length; i++) _markAcc[i] += Ms(_marks[i]);
            if (_acc < 1f) return;
            for (int i = 0; i < _marks.Length; i++) { MarkerMs[i] = _markAcc[i] / _n; _markAcc[i] = 0f; }
            Fps = _n / _acc; FrameMs = _acc * 1000f / _n;
            MainMs = _accMain / _n; RenderMs = _accRender / _n; GpuMs = _accGpu / _n;
            DrawCalls = _draws.Valid ? _draws.LastValue : 0;
            SetPassCalls = _setPass.Valid ? _setPass.LastValue : 0;
            ShadowCasters = _casters.Valid ? _casters.LastValue : 0;
            Triangles = _tris.Valid ? _tris.LastValue : 0;
            _acc = _accMain = _accRender = _accGpu = 0f; _n = 0;
        }

        public string Summary() =>
            $"{Fps:0} fps ({FrameMs:0.0} ms)  main {MainMs:0.0} ms  render {RenderMs:0.0} ms  gpu {GpuMs:0.0} ms\n" +
            $"draw calls {DrawCalls}  set-pass {SetPassCalls}  shadow casters {ShadowCasters}  triangles {Triangles / 1000}k";

        public string ScriptSummary()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Markers.Length; i++) sb.Append(Markers[i].Replace("()", "")).Append(' ').Append(MarkerMs[i].ToString("0.00")).Append(" ms").AppendLine();
            return sb.ToString();
        }
    }
}
