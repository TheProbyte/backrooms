using System.Collections.Generic;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>
    /// Shadows of the ceiling panels: walls and door headers hide part of a panel (a penumbra, not a cut), checked on the GPU
    /// against counting rays from the receiver to points over the whole panel.
    /// </summary>
    public class ShadowLightTests
    {
        const int N = 16;                 // occupancy cells per axis (toroidal)
        const float C = LightGridMath.Cell;
        const float Ceiling = 2.7f, Header = 2.1f;

        // A layout: a wall along x = 7.2 for z in [0, 6] (its end at z = 6), and a wall along z = 14.4 with one headed
        // doorway (x in [3.6, 4.8], top 2.1 m). Everything else open to the ceiling.
        static uint[] Layout()
        {
            uint Top(float m) => (uint)Mathf.RoundToInt(m * 10f);
            var occ = new uint[2 * N * N];
            for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                uint west = LightGridMath.Open, south = LightGridMath.Open;
                float westTop = Ceiling, southTop = Ceiling;
                if (x == 6 && z <= 4) west = LightGridMath.Wall;
                if (z == 12) { if (x == 3) { south = LightGridMath.Header; southTop = Header; } else south = LightGridMath.Wall; }
                uint o = LightGridMath.Loaded | (Top(Ceiling) << 16) | west | (south << 2);
                if (west != LightGridMath.Wall) o |= (Top(westTop) & 63u) << 4;
                if (south != LightGridMath.Wall) o |= (Top(southTop) & 63u) << 10;
                occ[z * N + x] = o;
            }
            return occ;
        }

        static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t)
        {
            Vector2 r = b - a, s = d - c;
            float den = r.x * s.y - r.y * s.x;
            t = 0f;
            if (Mathf.Abs(den) < 1e-9f) return false;
            Vector2 q = c - a;
            t = (q.x * s.y - q.y * s.x) / den;
            float u = (q.x * r.y - q.y * r.x) / den;
            return t > 0f && t < 1f && u >= 0f && u <= 1f;
        }

        // The share of the panel seen from P: rays to 40 x 40 points over it, stopped by the walls (full height) and by the
        // header above its doorway.
        static float Reference(Vector3 P, Vector3 centre, Vector2 half)
        {
            P += Vector3.up * 0.06f;              // the shader starts just off the surface
            var walls = new[] { (new Vector2(7.2f, 0f), new Vector2(7.2f, 6f)), (new Vector2(0f, 14.4f), new Vector2(3.6f, 14.4f)), (new Vector2(4.8f, 14.4f), new Vector2(19.2f, 14.4f)) };
            var header = (new Vector2(3.6f, 14.4f), new Vector2(4.8f, 14.4f));
            int seen = 0, total = 0;
            for (int i = 0; i < 40; i++)
            for (int j = 0; j < 40; j++)
            {
                var e = centre + new Vector3(((i + 0.5f) / 40f * 2f - 1f) * half.x, 0f, ((j + 0.5f) / 40f * 2f - 1f) * half.y);
                Vector2 a = new Vector2(P.x, P.z), b = new Vector2(e.x, e.z);
                bool blocked = false;
                foreach (var (w0, w1) in walls) if (Crosses(a, b, w0, w1, out _)) { blocked = true; break; }
                if (!blocked && Crosses(a, b, header.Item1, header.Item2, out float t) && Mathf.Lerp(P.y, e.y, t) > Header + 0.02f) blocked = true;
                total++; if (!blocked) seen++;
            }
            return seen / (float)total;
        }

        static float[] Gpu(List<Vector3> receivers, Vector3 centre, Vector2 half)
        {
            var probe = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Backrooms/Scripts/Editor/Tests/EmitterVisibilityProbe.compute");
            Assert.IsNotNull(probe);
            using (var occ = new ComputeBuffer(2 * N * N, 4))
            using (var queries = new ComputeBuffer(receivers.Count, 16))
            using (var output = new ComputeBuffer(receivers.Count, 4))
            {
                occ.SetData(Layout());
                var q = new Vector4[receivers.Count];
                for (int i = 0; i < q.Length; i++) q[i] = receivers[i];
                queries.SetData(q);
                int k = probe.FindKernel("Visible");
                probe.SetBuffer(k, "_LGridOcc", occ); probe.SetBuffer(k, "_Queries", queries); probe.SetBuffer(k, "_Out", output);
                probe.SetInt("_QueryCount", receivers.Count);
                probe.SetVector("_LGridSize", new Vector4(0, 0, 0, N));
                probe.SetVector("_LGridParams", new Vector4(1, 1, 1, 100f));       // one storey
                probe.SetVector("_LGridWindow", new Vector4(-1000, -1000, 1000, 1000));
                probe.SetVector("_Panel", centre); probe.SetVector("_PanelSize", half);
                probe.Dispatch(k, (receivers.Count + 63) / 64, 1, 1);
                var r = new float[receivers.Count]; output.GetData(r);
                return r;
            }
        }

        static void Compare(string what, List<Vector3> receivers, Vector3 centre, Vector2 half, float tolerance, out float[] gpu)
        {
            gpu = Gpu(receivers, centre, half);
            float worst = 0f; int at = 0;
            for (int i = 0; i < receivers.Count; i++)
            {
                float d = Mathf.Abs(gpu[i] - Reference(receivers[i], centre, half));
                if (d > worst) { worst = d; at = i; }
            }
            Assert.LessOrEqual(worst, tolerance, $"{what}: worst at {receivers[at]} (shader {gpu[at]:0.000}, rays {Reference(receivers[at], centre, half):0.000})");
        }

        // The width (metres along the line of receivers) over which the panel goes from 90 % to 10 % seen.
        static float Penumbra(List<Vector3> receivers, float[] seen)
        {
            float first = float.NaN, last = float.NaN;
            for (int i = 0; i < seen.Length; i++)
                if (seen[i] < 0.9f && seen[i] > 0.1f) { if (float.IsNaN(first)) first = receivers[i].z; last = receivers[i].z; }
            return float.IsNaN(first) ? 0f : Mathf.Abs(last - first);
        }

        [Test]
        public void WallEndCastsThePenumbraOfThePanel()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders unavailable");
            // A panel west of the wall's end; receivers on the floor east of the wall, across the shadow's edge.
            var centre = new Vector3(4.5f * C, 2.64f, 7.5f * C);
            var near = new List<Vector3>();
            var far = new List<Vector3>();
            for (int i = 0; i <= 120; i++) { near.Add(new Vector3(7.6f, 0f, 3f + i * 0.04f)); far.Add(new Vector3(9.2f, 0f, 0.2f + i * 0.06f)); }
            var square = new Vector2(0.285f, 0.285f);
            Compare("square, near the wall", near, centre, square, 0.05f, out var nearSeen);
            Compare("square, far from the wall", far, centre, square, 0.05f, out var farSeen);
            float pn = Penumbra(near, nearSeen), pf = Penumbra(far, farSeen);
            Assert.Greater(pn, 0.05f, "a penumbra, not a line");
            Assert.Greater(pf, pn * 2f, "the penumbra widens away from the wall end");
            // A tube along x (the warehouse strips) is long across this line of sight: wide penumbra; turned along z, narrow.
            Compare("tube along x", far, centre, new Vector2(0.59f, 0.055f), 0.05f, out var tubeX);
            Compare("tube along z", far, centre, new Vector2(0.055f, 0.59f), 0.05f, out var tubeZ);
            Assert.Greater(Penumbra(far, tubeX), Penumbra(far, tubeZ) * 1.2f, "a tube lying across the line of sight casts the wider penumbra");
        }

        [Test]
        public void DoorHeaderHidesTheNearPartOfThePanel()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shaders unavailable");
            // A panel 0.6 m in front of a headed doorway; receivers on the floor through it and beside it.
            var centre = new Vector3(3.5f * C, 2.64f, 11.5f * C);
            var through = new List<Vector3>();
            for (int i = 0; i <= 80; i++) through.Add(new Vector3(4.2f, 0f, 14.6f + i * 0.05f));
            Compare("through the doorway", through, centre, new Vector2(0.285f, 0.285f), 0.05f, out var seen);
            Assert.Less(seen[seen.Length - 1], 0.9f, "far beyond the doorway the header hides part of the panel");
            Assert.Greater(seen[0], seen[seen.Length - 1], "right behind the doorway more of the panel is seen");
            var beside = new List<Vector3>();
            for (int i = 0; i <= 80; i++) beside.Add(new Vector3(2.0f + i * 0.04f, 0f, 16f));
            Compare("across the jambs", beside, centre, new Vector2(0.285f, 0.285f), 0.05f, out _);
        }
    }
}
