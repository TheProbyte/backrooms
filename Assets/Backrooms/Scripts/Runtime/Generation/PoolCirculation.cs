using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The pools' circulation (2026-11-01, the realism plan 12): looked-after water is never quite still. The return jets
    /// in the pool walls push slow plumes of water out into the pool and the skimmers draw the surface in, so foam,
    /// floating things and the faint film on the water drift slowly round and gather at the skimmers and edges. The jets
    /// and skimmers come from the chunks (PoolChunk.fittings). The ripple simulation takes them as forcing on its flow
    /// (PoolRipples.compute: _Jets, _Sinks), and floating bodies drift with CurrentAt (PoolBodies).
    /// </summary>
    public static class PoolCirculation
    {
        public const int MaxJets = 16, MaxSinks = 8;
        /// <summary>A jet's speed at its nozzle (m/s) and how far its plume carries.</summary>
        public const float JetSpeed = 0.22f, PlumeLength = 2.4f, SinkPull = 0.07f, SinkReach = 2f;

        static readonly List<(Vector3 p, Vector3 d)> s_jets = new List<(Vector3, Vector3)>();
        static readonly List<Vector3> s_sinks = new List<Vector3>();
        static int s_frame = -1;

        /// <summary>The jets and skimmers within reach of a point (once a frame).</summary>
        static void Gather(Vector3 near)
        {
            if (s_frame == Time.frameCount) return;
            s_frame = Time.frameCount;
            s_jets.Clear(); s_sinks.Clear();
            foreach (var c in PoolChunk.All)
            {
                if (!c || c.fittings == null) continue;
                for (int i = 0; i < c.fittings.Length; i++)
                {
                    int kind = Mathf.RoundToInt(c.fittings[i].w);
                    if (kind > 1) continue;
                    var at = c.FittingAt(i);
                    if ((at - near).sqrMagnitude > 18f * 18f || Mathf.Abs(at.y - near.y) > 3f) continue;
                    if (kind == 0) { var d = c.fittingDirs[i]; d.y = 0f; s_jets.Add((at, d.normalized)); }
                    else s_sinks.Add(at);
                }
            }
            // The nearest first, so the simulation's few slots go to those that matter.
            s_jets.Sort((a, b) => (a.p - near).sqrMagnitude.CompareTo((b.p - near).sqrMagnitude));
            s_sinks.Sort((a, b) => (a - near).sqrMagnitude.CompareTo((b - near).sqrMagnitude));
        }

        /// <summary>The water's slow current at a point (horizontal, m/s), for floating things.</summary>
        public static Vector3 CurrentAt(Vector3 p, Vector3 near)
        {
            Gather(near);
            var v = Vector3.zero;
            foreach (var (j, d) in s_jets)
            {
                var rel = p - j; rel.y = 0f;
                float along = Vector3.Dot(rel, d);
                if (along < -0.1f || along > PlumeLength * 2f) continue;
                float lat = (rel - d * along).magnitude, w = 0.2f + 0.18f * Mathf.Max(0f, along);
                v += d * (JetSpeed * Mathf.Exp(-Mathf.Max(0f, along) / PlumeLength) * Mathf.Exp(-lat * lat / (w * w)));
            }
            foreach (var s in s_sinks)
            {
                var to = s - p; to.y = 0f;
                float r = to.magnitude;
                if (r < 0.05f || r > SinkReach) continue;
                v += to / r * (SinkPull * (1f - r / SinkReach));
            }
            return v;
        }

        /// <summary>The nearest jets and skimmers for the simulation: jets (x, z, push x, push z), sinks (x, z, pull, reach).</summary>
        public static void Fill(Vector3 near, Vector4[] jets, out int jetCount, Vector4[] sinks, out int sinkCount)
        {
            Gather(near);
            jetCount = Mathf.Min(s_jets.Count, Mathf.Min(MaxJets, jets.Length));
            for (int i = 0; i < jetCount; i++) { var (p, d) = s_jets[i]; jets[i] = new Vector4(p.x, p.z, d.x * JetSpeed, d.z * JetSpeed); }
            sinkCount = Mathf.Min(s_sinks.Count, Mathf.Min(MaxSinks, sinks.Length));
            for (int i = 0; i < sinkCount; i++) { var p = s_sinks[i]; sinks[i] = new Vector4(p.x, p.z, SinkPull, SinkReach); }
        }
    }
}
