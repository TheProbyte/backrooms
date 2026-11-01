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
        /// <summary>A lazy river's jets (kind 3, the water park) push harder: its current about half a metre a second, as
        /// real lazy rivers run (slow enough to stand in), the plumes longer so they join into one stream.</summary>
        public const float RiverJetGain = 2.5f, RiverPlumeGain = 1.6f;

        static readonly List<(Vector3 p, Vector3 d, float s)> s_jets = new List<(Vector3, Vector3, float)>();
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
                    if (kind > 3 || kind == 2) continue;
                    var at = c.FittingAt(i);
                    if ((at - near).sqrMagnitude > 18f * 18f || Mathf.Abs(at.y - near.y) > 3f) continue;
                    if (kind == 0 || kind == 3) { var d = c.fittingDirs[i]; d.y = 0f; s_jets.Add((at, d.normalized, kind == 3 ? RiverJetGain : 1f)); }
                    else s_sinks.Add(at);
                }
            }
            // The nearest first, so the simulation's few slots go to those that matter.
            s_jets.Sort((a, b) => (a.p - near).sqrMagnitude.CompareTo((b.p - near).sqrMagnitude));
            s_sinks.Sort((a, b) => (a - near).sqrMagnitude.CompareTo((b - near).sqrMagnitude));
        }

        static float s_bubbleT;
        /// <summary>
        /// Where a return jet pushes water the surface boils a little over it, with now and then a few bubbles (2026-11-01,
        /// the review): small disturbances of the ripple simulation and bubbles for the nearest four within 10 m.
        /// </summary>
        public static void Stir(Vector3 near, float dt)
        {
            Gather(near);
            s_bubbleT += dt;
            bool bubbles = s_bubbleT > 0.35f;
            if (bubbles) s_bubbleT = 0f;
            int n = 0;
            foreach (var (p, d, _) in s_jets)
            {
                if (n >= 4) break;
                if ((p - near).sqrMagnitude > 100f) continue;
                n++;
                var surf = new Vector3(p.x, PoolBiome.WaterLevel + Mathf.Floor((p.y + 0.6f) / 9f) * 9f, p.z) + d * 0.35f;
                PoolRipples.Splash(surf + new Vector3(Random.Range(-0.1f, 0.1f), 0f, Random.Range(-0.1f, 0.1f)), 0.1f, Random.Range(-0.0012f, 0.0012f));
                if (bubbles) PoolSplashes.Bubbles(surf, 2, 0.08f, Mathf.Max(0.1f, surf.y - p.y));
            }
        }

        /// <summary>The water's slow current at a point (horizontal, m/s), for floating things.</summary>
        public static Vector3 CurrentAt(Vector3 p, Vector3 near)
        {
            Gather(near);
            var v = Vector3.zero;
            foreach (var (j, d, s) in s_jets)
            {
                var rel = p - j; rel.y = 0f;
                float along = Vector3.Dot(rel, d), plume = PlumeLength * (s > 1f ? RiverPlumeGain : 1f);
                if (along < -0.1f || along > plume * 2f) continue;
                float lat = (rel - d * along).magnitude, w = 0.2f + 0.18f * Mathf.Max(0f, along);
                v += d * (JetSpeed * s * Mathf.Exp(-Mathf.Max(0f, along) / plume) * Mathf.Exp(-lat * lat / (w * w)));
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
            for (int i = 0; i < jetCount; i++) { var (p, d, s) = s_jets[i]; jets[i] = new Vector4(p.x, p.z, d.x * JetSpeed * s, d.z * JetSpeed * s); }
            sinkCount = Mathf.Min(s_sinks.Count, Mathf.Min(MaxSinks, sinks.Length));
            for (int i = 0; i < sinkCount; i++) { var p = s_sinks[i]; sinks[i] = new Vector4(p.x, p.z, SinkPull, SinkReach); }
        }
    }
}
