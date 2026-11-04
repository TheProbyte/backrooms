using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Interleaved A/B frame timing: cycles through named configurations several times (settle, then sample each),
    /// so slow drift from other programs or the editor affects every configuration equally. Used by automated perf checks.
    /// </summary>
    public class PerfAB : MonoBehaviour
    {
        public static PerfAB Instance { get; private set; }
        readonly List<(string name, Action apply)> _configs = new List<(string, Action)>();
        readonly List<List<float>> _frame = new List<List<float>>(), _gpu = new List<List<float>>();
        int _round, _rounds, _index, _wait, _left, _settle, _samples;
        ProfilerRecorder _gpuRec;
        public bool Running { get; private set; }
        public string Result { get; private set; } = "";

        public static PerfAB Ensure()
        {
            if (Instance) return Instance;
            return new GameObject("PerfAB").AddComponent<PerfAB>();
        }

        void OnEnable() { Instance = this; _gpuRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 4); }
        void OnDisable() { _gpuRec.Dispose(); if (Instance == this) Instance = null; }

        public void Clear() { _configs.Clear(); Running = false; Result = ""; }
        public void Add(string name, Action apply) => _configs.Add((name, apply));

        public void Run(int rounds = 3, int settleFrames = 40, int sampleFrames = 120)
        {
            if (_configs.Count == 0) return;
            _frame.Clear(); _gpu.Clear();
            foreach (var _ in _configs) { _frame.Add(new List<float>()); _gpu.Add(new List<float>()); }
            _rounds = rounds; _settle = settleFrames; _samples = sampleFrames;
            _round = 0; _index = 0; Begin();
            Running = true; Result = "";
        }

        void Begin() { _configs[_index].apply(); _wait = _settle; _left = _samples; }

        void LateUpdate()
        {
            if (!Running) return;
            if (_wait > 0) { _wait--; return; }
            _frame[_index].Add(Time.unscaledDeltaTime * 1000f);
            _gpu[_index].Add(_gpuRec.Valid && _gpuRec.Count > 0 ? _gpuRec.LastValue * 1e-6f : 0f);
            if (--_left > 0) return;
            _index++;
            if (_index >= _configs.Count) { _index = 0; _round++; }
            if (_round >= _rounds) { Finish(); return; }
            Begin();
        }

        void Finish()
        {
            Running = false;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _configs.Count; i++)
            {
                float f = Median(_frame[i]), g = Median(_gpu[i]);
                sb.Append(_configs[i].name).Append(": frame ").Append(f.ToString("0.0")).Append(" ms (").Append((1000f / f).ToString("0"))
                  .Append(" fps) gpu ").Append(g.ToString("0.0")).Append(" ms; ");
            }
            _configs[0].apply();
            Result = sb.ToString();
        }

        static float Median(List<float> l) { if (l.Count == 0) return 0; l.Sort(); return l[l.Count / 2]; }
    }
}
