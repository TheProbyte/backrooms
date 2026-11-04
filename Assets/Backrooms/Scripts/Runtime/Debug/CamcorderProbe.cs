using System.Collections;
using System.IO;
using System.Text;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Screenshots of the finished camcorder picture (what is on screen, after every camcorder effect) in a few fixed
    /// situations: a lit corridor two ways, looking up at the tubes, a fast pan, the lights going out (just after, and once
    /// the gain has come up). Also the GPU frame time in the lit corridor. <c>CamcorderProbe.Run(folder)</c>; read
    /// <see cref="Report"/> when <see cref="Done"/>.
    /// </summary>
    public class CamcorderProbe : MonoBehaviour
    {
        public string folder;
        public bool Done { get; private set; }
        public string Report => _log.ToString();
        readonly StringBuilder _log = new StringBuilder();
        CamcorderPlayerController _pl;
        CamcorderAutoExposure _exp;
        RenderTexture _rt;
        Texture2D _tex;

        /// <summary>Preset to shoot the main series with (-1: the settings in use), and presets for one extra shot each.</summary>
        public int preset = -1;
        public int[] extraPresets;
        /// <summary>Also time the camcorder picture (on / off) and the 4:3 viewport (on / full width), alternating three times.</summary>
        public bool measureCost;

        public static CamcorderProbe Run(string folder, int preset = -1, int[] extraPresets = null)
        {
            var go = new GameObject("CamcorderProbe");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            var p = go.AddComponent<CamcorderProbe>();
            p.folder = folder; p.preset = preset; p.extraPresets = extraPresets;
            go.SetActive(true);
            return p;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            _pl = FindAnyObjectByType<CamcorderPlayerController>();
            _exp = FindAnyObjectByType<CamcorderAutoExposure>();
            Vector3 home = _pl.transform.position;
            float yaw = _pl.transform.eulerAngles.y;
            var perf = PerfStats.Ensure();
            var rig = FindAnyObjectByType<Camcorder.CamcorderRig>();
            var original = rig ? rig.Settings : null;
            if (rig && preset >= 0) Use(rig, preset);

            _pl.Teleport(home, yaw, 0f);
            yield return Wait(4f);
            yield return Shot("lit_a");
            yield return Wait(2.2f);
            _log.AppendLine($"lit corridor: GPU {perf.GpuMs:0.00} ms, CPU main {perf.MainMs:0.00} ms, {perf.Fps:0} fps, screen {Screen.width}x{Screen.height}");
            if (measureCost && rig) yield return Cost(rig, perf);

            _pl.Teleport(home, yaw + 90f, 0f);
            yield return Wait(3f);
            yield return Shot("lit_b");

            _pl.Teleport(home, yaw, -38f);
            yield return Wait(3f);
            yield return Shot("tubes");

            // A fast pan (240 degrees a second), caught in the middle.
            _pl.Teleport(home, yaw, 0f);
            yield return Wait(1.5f);
            float t0 = Time.time, a = yaw;
            while (Time.time - t0 < 0.45f) { a += 240f * Time.deltaTime; _pl.Teleport(home, a, 0f); yield return null; }
            yield return Shot("pan", () => { a += 240f * Time.deltaTime; _pl.Teleport(home, a, 0f); });

            // Lights out in this room.
            _pl.Teleport(home, yaw, 0f);
            yield return Wait(3f);
            var ws = WorldStreamer.Current;
            var L = ws ? ws.LayoutAt(WorldStreamer.ChunkOf(home)) : null;
            if (L != null)
            {
                int x = Mathf.FloorToInt(home.x / OfficeBiome.CellSize) - L.coord.x * L.N, z = Mathf.FloorToInt(home.z / OfficeBiome.CellSize) - L.coord.y * L.N;
                var chunk = new Vector3Int(L.coord.x, L.storey, L.coord.y);
                int room = L.roomOf[x, z];
                LightZones.Set(chunk, room, true);
                yield return Wait(0.6f);
                yield return Shot("dark_0.6s");
                yield return Wait(7f);
                yield return Shot("dark_7s");
                _pl.Teleport(home, yaw + 180f, 0f);
                yield return Wait(2f);
                yield return Shot("dark_other_way");
                LightZones.Set(chunk, room, false);
                yield return Wait(2f);
            }
            else _log.AppendLine("lights out: no layout here");
            _pl.Teleport(home, yaw, 0f);
            if (rig && extraPresets != null)
            {
                foreach (int i in extraPresets)
                {
                    Use(rig, i);
                    yield return Wait(3f);
                    yield return Shot("preset_" + Camcorder.CamcorderSettings.PresetNames[i].Replace(' ', '_').Replace("'", ""));
                }
            }
            if (rig && original != null) rig.SetSettings(original);
            Done = true;
        }

        IEnumerator Cost(Camcorder.CamcorderRig rig, PerfStats perf)
        {
            var s = rig.Settings;
            bool aspect = s.aspect43;
            float[] sum = new float[3];
            for (int round = 0; round < 3; round++)
                for (int mode = 0; mode < 3; mode++)
                {
                    Camcorder.CamcorderPipeline.Suspended = mode == 1;
                    s.aspect43 = mode == 2 ? false : true;
                    yield return Wait(2.3f);
                    sum[mode] += perf.GpuMs;
                }
            Camcorder.CamcorderPipeline.Suspended = false;
            s.aspect43 = aspect;
            _log.AppendLine($"cost (GPU frame, 3 rounds): 4:3 + camcorder picture {sum[0] / 3f:0.00} ms, 4:3 without it {sum[1] / 3f:0.00} ms, full width + picture {sum[2] / 3f:0.00} ms");
            yield return Wait(1f);
        }

        static void Use(Camcorder.CamcorderRig rig, int index)
        {
            var p = Camcorder.CamcorderSettings.Preset(index);
            p.CopyAudioFrom(rig.Settings);
            rig.SetSettings(p);
        }

        static IEnumerator Wait(float s) { float t = Time.time + s; while (Time.time < t) yield return null; }

        IEnumerator Shot(string name, System.Action everyFrame = null)
        {
            yield return new WaitForEndOfFrame();
            everyFrame?.Invoke();
            if (!_rt || _rt.width != Screen.width || _rt.height != Screen.height)
            {
                if (_rt) { _rt.Release(); Destroy(_rt); }
                _rt = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                if (_tex) Destroy(_tex);
                _tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_rt);
            var tmp = RenderTexture.GetTemporary(_rt.width, _rt.height, 0, RenderTextureFormat.ARGB32);
            if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(_rt, tmp, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(_rt, tmp);
            var prev = RenderTexture.active;
            RenderTexture.active = tmp;
            _tex.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0); _tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), _tex.EncodeToPNG());
            var rig = FindAnyObjectByType<Camcorder.CamcorderRig>();
            _log.AppendLine($"{name}: iris {(_exp ? _exp.IrisEV : 0f):0.00} EV, gain {(_exp ? _exp.GainEV : 0f):0.00} EV, shutter +{(_exp ? _exp.ShutterEV : 0f):0} EV, " +
                $"exposure x{(_exp ? _exp.Exposure : 0f):0.000}, metered {(_exp ? _exp.MeteredLevel : 0f):0.000} (scene {(_exp ? _exp.MeasuredLuminance : 0f):0.0000}), " +
                $"white balance {(_exp ? _exp.WhiteBalance : Vector3.one)}, f/{(rig ? rig.FNumber : 0f):0.0}, focus {(rig ? rig.FocusDistance : 0f):0.00} m, DoF {(rig && rig.DepthOfFieldOn ? "on" : "off")}");
        }

        void OnDestroy()
        {
            if (_rt) { _rt.Release(); Destroy(_rt); }
            if (_tex) Destroy(_tex);
        }
    }
}
