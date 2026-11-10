using System.Collections;
using System.Collections.Generic;
using System.IO;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Look check for the Poolrooms: picks viewpoints in big rooms of a Poolrooms district (from a corner, looking across
    /// the room toward its roof openings), visits each and saves a screenshot. Run from the editor while playing.
    /// </summary>
    public class PoolTour : MonoBehaviour
    {
        public static void Run(string folder, Vector2Int district, int count = 6, int width = 1280, int height = 720)
        {
            var go = new GameObject("PoolTour");
            go.AddComponent<PoolTour>().StartCoroutine(go.GetComponent<PoolTour>().Tour(folder, district, count, width, height));
        }

        /// <summary>Viewpoints: (position on the floor, yaw, pitch).</summary>
        public static List<(Vector3 pos, float yaw, float pitch)> Views(WorldGenerator world, Vector2Int district, int count)
        {
            var L = world.District(new Vector3Int(district.x, 0, district.y));
            float C = OfficeBiome.CellSize, D = WorldGenSettings.DistrictCells * C;
            var origin = new Vector3(district.x * D, 0f, district.y * D);
            var rooms = new List<Room>(L.rooms);
            rooms.Sort((a, b) => (b.rect.width * b.rect.height).CompareTo(a.rect.width * a.rect.height));
            var list = new List<(Vector3, float, float)>();
            foreach (var r in rooms)
            {
                if (list.Count >= count) break;
                if (!r.Walkable || r.rect.width < 4 || r.rect.height < 4) continue;
                int sky = 0;
                for (int x = r.rect.xMin; x < r.rect.xMax; x++) for (int z = r.rect.yMin; z < r.rect.yMax; z++) if (L.pool[x, z].skylight != SkylightShape.None) sky++;
                if (sky == 0) continue;
                // From the corner the sun comes from (patches fall away from it), looking at the far corner.
                var a = new Vector3((r.rect.xMin + 0.7f) * C, 0f, (r.rect.yMin + 0.7f) * C);
                var b = new Vector3((r.rect.xMax - 0.5f) * C, 0f, (r.rect.yMax - 0.5f) * C);
                if (list.Count % 2 == 1) { var t = a; a = b; b = t; }
                var dir = b - a;
                list.Add((origin + a, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 8f));
            }
            return list;
        }

        IEnumerator Tour(string folder, Vector2Int district, int count, int w, int h)
        {
            Directory.CreateDirectory(folder);
            var ws = FindAnyObjectByType<WorldStreamer>();
            var player = FindAnyObjectByType<CamcorderPlayerController>();
            var views = Views(ws.World, district, count);
            var full = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            var small = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int i = 0; i < views.Count; i++)
            {
                var (pos, yaw, pitch) = views[i];
                player.TeleportSafe(pos, yaw, pitch);
                float t0 = Time.realtimeSinceStartup;
                yield return null;
                while (ws.QueuedChunks > 0 && Time.realtimeSinceStartup - t0 < 20f) yield return null;
                player.TeleportSafe(pos, yaw, pitch);
                float until = Time.realtimeSinceStartup + 2.5f;       // exposure settles
                while (Time.realtimeSinceStartup < until) yield return null;
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
                if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(full, small, new Vector2(1f, -1f), new Vector2(0f, 1f));
                else Graphics.Blit(full, small);
                var prev = RenderTexture.active; RenderTexture.active = small;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(folder, $"v{i}.png"), tex.EncodeToPNG());
            }
            full.Release(); small.Release();
            Destroy(full); Destroy(small); Destroy(tex);
            File.WriteAllText(Path.Combine(folder, "done.txt"), views.Count.ToString());
            Destroy(gameObject);
        }
    }
}
