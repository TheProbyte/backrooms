using Unity.Profiling;
using UnityEngine;

namespace Backrooms.StyleLab
{
    /// <summary>
    /// Measures frame times in Play mode for the style lab's comparisons (2026-11-06: what the suit and the stylise pass
    /// cost): the main thread, the render thread and the GPU (where the platform reports it), averaged over a number of
    /// frames after a short settle. Add it, read <see cref="Result"/> once <see cref="Done"/>. Editor numbers run higher
    /// than a build; the differences between runs are what to compare.
    /// </summary>
    public class StyleLabFrameProbe : MonoBehaviour
    {
        public int settleFrames = 30, frames = 240;
        public bool Done { get; private set; }
        public string Result { get; private set; } = "running";

        ProfilerRecorder _main, _render, _gpu;
        int _n;
        double _sumMain, _sumRender, _sumGpu, _sumDt;

        void OnEnable()
        {
            _main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            _render = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Render Thread", 1);
            _gpu = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 1);
        }

        void OnDisable() { _main.Dispose(); _render.Dispose(); _gpu.Dispose(); }

        void Update()
        {
            if (Done) return;
            _n++;
            if (_n <= settleFrames) return;
            _sumMain += _main.LastValue * 1e-6;
            _sumRender += _render.LastValue * 1e-6;
            _sumGpu += _gpu.LastValue * 1e-6;
            _sumDt += Time.unscaledDeltaTime * 1000.0;
            if (_n >= settleFrames + frames)
            {
                Done = true;
                Result = $"frame {_sumDt / frames:F2} ms, main {_sumMain / frames:F2} ms, render {_sumRender / frames:F2} ms, gpu {(_sumGpu > 0 ? (_sumGpu / frames).ToString("F2") + " ms" : "n/a")}";
            }
        }
    }
}
