using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Owns the scene's lightmap array at runtime: each streamed chunk adds its baked lightmap and frees
    /// the slot when it unloads. Renderers point at their slot with lightmapIndex; URP's Lit shader then
    /// uses the lightmap as the surface's light.
    /// </summary>
    public static class RuntimeLightmaps
    {
        static readonly List<LightmapData> s_maps = new List<LightmapData>();
        static readonly Stack<int> s_free = new Stack<int>();
        static Texture2D s_black, s_flatDir;
        static bool s_dirty;

        public static int Count => s_maps.Count - s_free.Count;

        public static int Add(ushort[] rgbaHalf, int width, int height, byte[] dirRgba = null)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
            {
                name = "ChunkLightmap", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 0,
            };
            tex.SetPixelData(rgbaHalf, 0);
            tex.Apply(false, true);
            var data = new LightmapData { lightmapColor = tex };
            if (dirRgba != null)
            {
                var dir = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                {
                    name = "ChunkLightmapDir", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 0,
                };
                dir.SetPixelData(dirRgba, 0);
                dir.Apply(false, true);
                data.lightmapDir = dir;
            }
            int index;
            if (s_free.Count > 0) { index = s_free.Pop(); s_maps[index] = data; }
            else { index = s_maps.Count; s_maps.Add(data); }
            s_dirty = true;
            Apply();
            return index;
        }

        public static void Release(int index)
        {
            if (index < 0 || index >= s_maps.Count) return;
            var old = s_maps[index];
            if (old != null && old.lightmapColor) Object.Destroy(old.lightmapColor);
            if (old != null && old.lightmapDir) Object.Destroy(old.lightmapDir);
            if (!s_flatDir)
            {
                s_flatDir = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "LightmapDirEmpty" };
                s_flatDir.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 0.5f)); s_flatDir.Apply(false, true);
            }
            if (!s_black)
            {
                s_black = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true) { name = "LightmapEmpty" };
                s_black.SetPixel(0, 0, Color.black); s_black.Apply(false, true);
            }
            s_maps[index] = new LightmapData { lightmapColor = s_black, lightmapDir = s_flatDir };
            s_free.Push(index);
            s_dirty = true;
            Apply();
        }

        static void Apply()
        {
            if (!s_dirty) return;
            s_dirty = false;
            LightmapSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
            LightmapSettings.lightmaps = s_maps.ToArray();
        }

        /// <summary>Clears everything (scene start).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { s_maps.Clear(); s_free.Clear(); s_dirty = false; }
    }

    /// <summary>Frees a chunk's lightmap slot when the chunk is destroyed.</summary>
    public class ChunkLightmapHandle : MonoBehaviour
    {
        public int index = -1;
        void OnDestroy() { if (index >= 0) RuntimeLightmaps.Release(index); index = -1; }
    }
}
