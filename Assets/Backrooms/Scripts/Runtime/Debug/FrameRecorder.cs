using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Records every displayed frame for a while (scaled, 960 x 540 by default, read back asynchronously so the game keeps its
    /// pace) plus one line of caller-supplied state per frame (log.txt), for checking transitions frame by frame.
    /// </summary>
    public class FrameRecorder : MonoBehaviour
    {
        public static FrameRecorder Instance { get; private set; }
        public bool Recording { get; private set; }
        public string LastFolder { get; private set; }
        /// <summary>Optional: a line of state written next to each frame.</summary>
        public Func<string> state;

        public static FrameRecorder Ensure()
        {
            if (Instance) return Instance;
            return new GameObject("FrameRecorder").AddComponent<FrameRecorder>();
        }

        void OnEnable() => Instance = this;
        void OnDisable() { if (Instance == this) Instance = null; }

        public void Record(string folder, int frames, int width = 960, int height = 540) { if (!Recording) StartCoroutine(Run(folder, frames, width, height)); }

        IEnumerator Run(string folder, int frames, int w, int h)
        {
            Recording = true; LastFolder = folder;
            Directory.CreateDirectory(folder);
            var full = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            var small = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            var shots = new byte[frames][];
            var lines = new List<string>(frames);
            int pending = 0;
            for (int i = 0; i < frames; i++)
            {
                yield return new WaitForEndOfFrame();
                lines.Add($"{i}\t{Time.frameCount}\t{Time.unscaledDeltaTime * 1000f:0.0}ms\t{(state != null ? state() : "")}");
                ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
                if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(full, small, new Vector2(1f, -1f), new Vector2(0f, 1f));
                else Graphics.Blit(full, small);
                int index = i; pending++;
                AsyncGPUReadback.Request(small, 0, TextureFormat.RGBA32, r => { if (!r.hasError) shots[index] = r.GetData<byte>().ToArray(); pending--; });
            }
            while (pending > 0) yield return null;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int i = 0; i < frames; i++)
            {
                if (shots[i] == null) continue;
                tex.LoadRawTextureData(shots[i]); tex.Apply();
                File.WriteAllBytes(Path.Combine(folder, $"f{i:0000}.png"), tex.EncodeToPNG());
            }
            File.WriteAllLines(Path.Combine(folder, "log.txt"), lines);
            Destroy(tex); full.Release(); small.Release(); Destroy(full); Destroy(small);
            Recording = false;
        }
    }
}
