using System.IO;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Checks <see cref="ChunkVisibility"/> never hides something that can be seen: from random eye positions and view
    /// directions near the player, renders the view with culling and without, and counts the pixels that differ. Run in
    /// Play Mode: <c>VisibilityProbe.Run(folder, samples)</c>; the worst pair is saved as PNGs in the folder.
    /// </summary>
    public static class VisibilityProbe
    {
        public static string Run(string folder, int samples = 40, int width = 480, int height = 270, int seed = 1)
        {
            var ws = WorldStreamer.Current;
            var vis = ws ? ws.GetComponent<ChunkVisibility>() : null;
            var main = Camera.main;
            if (!ws || !vis || !main) return "not ready";
            var go = new GameObject("VisibilityProbeCam");
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            var rt = new RenderTexture(width, height, 24);
            cam.targetTexture = rt;
            var a = new Texture2D(width, height, TextureFormat.RGB24, false);
            var b = new Texture2D(width, height, TextureFormat.RGB24, false);
            var rng = new System.Random(seed);
            int worst = -1, worstAt = -1, total = 0, tried = 0, hiddenSum = 0;
            bool was = ChunkVisibility.Enabled;
            Vector3 p0 = ws.player.position;
            float C = OfficeBiome.CellSize;
            for (int i = 0; i < samples * 4 && tried < samples; i++)
            {
                // A cell centre within 18 m, on the ground storey, not inside anything.
                var p = p0 + new Vector3((float)(rng.NextDouble() * 2 - 1) * 18f, 0f, (float)(rng.NextDouble() * 2 - 1) * 18f);
                p.x = (Mathf.Floor(p.x / C) + 0.5f) * C; p.z = (Mathf.Floor(p.z / C) + 0.5f) * C;
                if (ws.LayoutAt(WorldStreamer.ChunkOf(p)) == null) continue;
                if (!Physics.Raycast(p + Vector3.up * 1.7f, Vector3.down, out var floor, 3f) || floor.normal.y < 0.7f) continue;
                Vector3 eye = floor.point + Vector3.up * 1.58f;
                if (Physics.CheckSphere(eye, 0.2f)) continue;
                tried++;
                go.transform.SetPositionAndRotation(eye, Quaternion.Euler((float)(rng.NextDouble() * 60 - 30), (float)(rng.NextDouble() * 360), 0f));
                ChunkVisibility.Enabled = true; vis.ForceUpdate(cam);
                hiddenSum += vis.HiddenChunks;
                Grab(cam, rt, a);
                ChunkVisibility.Enabled = false; vis.ForceUpdate(cam);
                Grab(cam, rt, b);
                int diff = Diff(a, b);
                total += diff;
                if (diff > worst)
                {
                    worst = diff; worstAt = tried;
                    if (!string.IsNullOrEmpty(folder))
                    {
                        Directory.CreateDirectory(folder);
                        File.WriteAllBytes(Path.Combine(folder, "vis_culled.png"), a.EncodeToPNG());
                        File.WriteAllBytes(Path.Combine(folder, "vis_full.png"), b.EncodeToPNG());
                    }
                }
            }
            ChunkVisibility.Enabled = was; vis.ForceUpdate();
            cam.targetTexture = null;
            Object.Destroy(rt); Object.Destroy(a); Object.Destroy(b); Object.Destroy(go);
            return $"{tried} views, average {hiddenSum / Mathf.Max(1, tried)} chunks hidden; differing pixels: total {total}, worst {worst} (view {worstAt})";
        }

        static void Grab(Camera cam, RenderTexture rt, Texture2D into)
        {
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            into.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            into.Apply();
            RenderTexture.active = old;
        }

        static int Diff(Texture2D a, Texture2D b)
        {
            var pa = a.GetPixels32(); var pb = b.GetPixels32();
            int n = 0;
            for (int i = 0; i < pa.Length; i++)
                if (Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b) > 24) n++;
            return n;
        }
    }
}
