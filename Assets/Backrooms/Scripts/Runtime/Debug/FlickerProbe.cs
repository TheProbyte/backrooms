using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Measures light that flickers or pops when nothing in view changes: switches the nearest room off, stands in it, and
    /// for each lighting variant renders the view (no camcorder post, so its noise does not count) for 120 frames. Reports
    /// the mean frame-to-frame change and the worst 16x16-pixel block's, both relative to the image's mean brightness, and
    /// saves the first frame and a map of where it changed. Add in Play Mode with <c>FlickerProbe.Run(folder)</c>.
    /// </summary>
    public class FlickerProbe : MonoBehaviour
    {
        public string folder;
        public bool Done { get; private set; }
        public string Report => _log.ToString();
        readonly StringBuilder _log = new StringBuilder();
        const int W = 160, H = 120, Frames = 120;

        CamcorderPlayerController _pl;
        GraphicsOptions _o;
        Camera _cam;
        RenderTexture _rt;
        Texture2D _tex;

        public static FlickerProbe Run(string folder)
        {
            var go = new GameObject("FlickerProbe");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            var p = go.AddComponent<FlickerProbe>();
            p.folder = folder;
            go.SetActive(true);
            return p;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            _pl = FindAnyObjectByType<CamcorderPlayerController>();
            _o = GraphicsController.Ensure().Options;
            if (!_pl) { _log.AppendLine("no player"); Done = true; yield break; }
            string saved = JsonUtility.ToJson(_o);
            var go = new GameObject("FlickerCam");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<Camera>();
            _cam.enabled = false;
            _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf);
            _tex = new Texture2D(W, H, TextureFormat.RGBAHalf, false, true);

            // A dark place: at least 9 m from every powered fixture, with lit areas 9-20 m away (their bounce reaches it).
            LightSwitch sw = null;
            var lights = new List<Vector3>();
            var lm = FindAnyObjectByType<FixtureLightManager>();
            var field = typeof(FixtureLightManager).GetField("_gridLights", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (lm && field?.GetValue(lm) is List<LightGridMath.GridLight> list)
                foreach (var l in list) if (l.colorAll.sqrMagnitude > 1e-6f) lights.Add(l.pos);
            Vector3 home = _pl.transform.position, best = home; float bestScore = -1f;
            for (float x = -14f; x <= 14f; x += 0.6f)
            for (float z = -14f; z <= 14f; z += 0.6f)
            {
                var top = home + new Vector3(x, 1.5f, z);
                if (!Physics.Raycast(top, Vector3.down, out var hit, 2.5f, ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.9f) continue;
                if (Physics.CheckSphere(hit.point + Vector3.up * 1f, 0.4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                float nearest = float.MaxValue; int around = 0;
                foreach (var l in lights) { float d = Vector3.Distance(l, hit.point); nearest = Mathf.Min(nearest, d); if (d < 20f) around++; }
                if (nearest < 7f || around == 0) continue;
                float score = around - nearest * 0.2f;
                if (score > bestScore) { bestScore = score; best = hit.point; }
            }
            if (bestScore < 0f)
            {
                _log.AppendLine("no dark place found: switching the nearest room off instead");
                float bd = float.MaxValue;
                foreach (var s in FindObjectsByType<LightSwitch>(FindObjectsInactive.Exclude))
                {
                    float d = (s.transform.position - _pl.transform.position).sqrMagnitude;
                    if (d < bd) { bd = d; sw = s; }
                }
                if (sw) { best = sw.transform.position + sw.transform.forward * 1.2f; if (Physics.Raycast(best, Vector3.down, out var f2, 3f)) best = f2.point; }
            }
            _pl.Teleport(best + Vector3.up * 0.05f, OpenestYaw(best + Vector3.up * 1.5f), 4f);
            yield return Wait(1f);
            if (sw) sw.Press();
            yield return Wait(3f);
            _log.AppendLine($"at {best} ({lights.Count} powered fixtures in the grid)");

            var grid = lm ? lm.grid : null;
            // Old: blending on any big relative change, no smoothing pass, faulty tubes' flicker in bounce and haze.
            void Old(bool old)
            {
                if (grid) { grid.noiseAwareBlend = !old; grid.probeFilter = !old; grid.hysteresis = old ? 0.8f : 0.95f; }
                if (lm) lm.smoothBounce = !old;
            }
            var variants = new List<(string name, System.Action set)>
            {
                ("all old", () => { _o.ApplyLook(0); Old(true); }),
                ("all new", () => { _o.ApplyLook(0); Old(false); }),
                ("traced only old", () => { _o.ApplyLook(0); _o.haze = 0f; _o.contactShadows = 0f; _o.shadowSoftness = 0f; _o.areaLights = false; Old(true); }),
                ("traced only new", () => { _o.ApplyLook(0); _o.haze = 0f; _o.contactShadows = 0f; _o.shadowSoftness = 0f; _o.areaLights = false; Old(false); }),
                ("haze only new", () => { _o.ApplyLook(0); _o.tracedBounce = false; _o.contactShadows = 0f; _o.shadowSoftness = 0f; _o.areaLights = false; Old(false); }),
                ("nothing new", () => { _o.ApplyLook(0); _o.haze = 0f; _o.contactShadows = 0f; _o.shadowSoftness = 0f; _o.areaLights = false; _o.tracedBounce = false; Old(false); }),
            };
            foreach (var (name, set) in variants)
            {
                set();
                yield return Wait(2.5f);
                yield return Measure(name);
            }
            Old(false);
            JsonUtility.FromJsonOverwrite(saved, _o);
            if (sw) sw.Press();
            Destroy(_rt); Destroy(_tex);
            Done = true;
        }

        IEnumerator Measure(string name)
        {
            var prev = new float[W * H];
            var change = new float[W * H];
            double sumChange = 0, sumLum = 0; int pairs = 0;
            // One still view (the player's camera sways with breathing and the hand): only the light may change.
            _cam.CopyFrom(Camera.main);
            _cam.targetTexture = _rt;
            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None;
            // The camcorder pass (a renderer feature, so it runs on any camera) adds fresh sensor noise every frame: it is
            // switched off for these renders only.
            var video = VideoFeature();
            for (int f = 0; f < Frames; f++)
            {
                yield return null;
                bool was = video && video.isActive;
                if (video) video.SetActive(false);
                _cam.Render();
                if (video) video.SetActive(was);
                var active = RenderTexture.active; RenderTexture.active = _rt;
                _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); _tex.Apply(false);
                RenderTexture.active = active;
                var px = _tex.GetPixels();
                double frameLum = 0, frameChange = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    float l = 0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b;
                    frameLum += l;
                    if (f > 0) { float d = Mathf.Abs(l - prev[i]); frameChange += d; change[i] += d; }
                    prev[i] = l;
                }
                sumLum += frameLum / px.Length;
                if (f > 0) { sumChange += frameChange / px.Length; pairs++; }
                if (f == 0) Save($"{Safe(name)}_frame.png", px, false);
            }
            double meanLum = sumLum / Frames, meanChange = sumChange / pairs;
            // Worst 16x16 block: where it pops.
            double worst = 0;
            for (int by = 0; by < H / 16; by++)
            for (int bx = 0; bx < W / 16; bx++)
            {
                double s = 0;
                for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) s += change[(by * 16 + y) * W + bx * 16 + x];
                worst = System.Math.Max(worst, s / (256.0 * pairs));
            }
            var map = new Color[W * H];
            for (int i = 0; i < map.Length; i++) { float v = Mathf.Clamp01(change[i] / pairs / (float)(meanLum + 1e-4) * 10f); map[i] = new Color(v, v, v, 1); }
            Save($"{Safe(name)}_change.png", map, true);
            _log.AppendLine($"{name}: mean brightness {meanLum:0.0000}, frame-to-frame change {100 * meanChange / (meanLum + 1e-6):0.00}% (worst block {100 * worst / (meanLum + 1e-6):0.00}%)");
        }

        void Save(string file, Color[] px, bool raw)
        {
            var t = new Texture2D(W, H, TextureFormat.RGB24, false);
            if (!raw)
            {
                // Exposed for viewing: the raw render is dark.
                float sum = 0; foreach (var c in px) sum += 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                float k = 0.25f / Mathf.Max(sum / px.Length, 1e-4f);
                var o = new Color[px.Length];
                for (int i = 0; i < px.Length; i++) o[i] = new Color(Mathf.Pow(Mathf.Clamp01(px[i].r * k), 0.45f), Mathf.Pow(Mathf.Clamp01(px[i].g * k), 0.45f), Mathf.Pow(Mathf.Clamp01(px[i].b * k), 0.45f));
                t.SetPixels(o);
            }
            else t.SetPixels(px);
            t.Apply();
            File.WriteAllBytes(Path.Combine(folder, file), t.EncodeToPNG());
            Destroy(t);
        }

        static string Safe(string s) => s.Replace(' ', '_');

        static UnityEngine.Rendering.Universal.ScriptableRendererFeature VideoFeature()
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (!urp || !(f?.GetValue(urp) is ScriptableRendererData[] list)) return null;
            foreach (var data in list)
                if (data) foreach (var feat in data.rendererFeatures) if (feat && feat.name == "CamcorderVideo") return feat;
            return null;
        }

        static float OpenestYaw(Vector3 eye)
        {
            float best = 0f, bestD = -1f;
            for (int k = 0; k < 16; k++)
            {
                float yaw = k * 22.5f;
                var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                float d = Physics.Raycast(eye, dir, out var h, 12f, ~0, QueryTriggerInteraction.Ignore) ? h.distance : 12f;
                if (d > 7f) d = 7f - (d - 7f) * 0.3f;
                if (d > bestD) { bestD = d; best = yaw; }
            }
            return best;
        }

        static IEnumerator Wait(float s) { for (float t = 0; t < s; t += Time.unscaledDeltaTime) yield return null; }
    }
}
