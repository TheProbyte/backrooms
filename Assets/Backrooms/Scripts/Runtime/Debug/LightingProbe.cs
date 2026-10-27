using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Look and cost check for the lighting look (F5 > LIGHT): the same views with each new feature off and on
    /// (rectangular panels, traced bounce, soft shadows, contact shadows, haze) and with every built-in look, exposure held
    /// still so pictures compare. Views: a lit room, a room switched off beside lit ones, a piece of furniture. Frame times
    /// (CPU and, where the platform reports it, GPU) per variant go in <see cref="Report"/>. Add in Play Mode with
    /// <c>LightingProbe.Run(folder)</c>; the player's options are restored afterwards.
    /// </summary>
    public class LightingProbe : MonoBehaviour
    {
        public string folder;
        public string what = "all";
        public bool Done { get; private set; }
        public string Report => _log.ToString();
        readonly StringBuilder _log = new StringBuilder();

        CamcorderPlayerController _pl;
        CamcorderRig _rig;
        GraphicsOptions _o;
        RenderTexture _rt;
        Texture2D _tex;
        readonly FrameTiming[] _timing = new FrameTiming[1];

        public static LightingProbe Run(string folder, string what = "all")
        {
            var go = new GameObject("LightingProbe");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            var p = go.AddComponent<LightingProbe>();
            p.folder = folder; p.what = what;
            go.SetActive(true);
            return p;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            _pl = FindAnyObjectByType<CamcorderPlayerController>();
            _rig = FindAnyObjectByType<CamcorderRig>();
            var gc = GraphicsController.Ensure();
            _o = gc.Options;
            if (!_pl || !_rig) { _log.AppendLine("no player or rig"); Done = true; yield break; }
            string saved = JsonUtility.ToJson(_o);
            bool autoExposure = _rig.Settings.autoExposure;
            _rt = new RenderTexture(960, 720, 24);
            _tex = new Texture2D(960, 720, TextureFormat.RGB24, false);
            if (what == "hole") { yield return Hole(); goto restore; }
            yield return Lit();
            if (what == "all" || what == "dark") yield return Dark();
            if (what == "all" || what == "furniture") yield return Furniture();
            restore:
            // Restore.
            JsonUtility.FromJsonOverwrite(saved, _o);
            _rig.Settings.autoExposure = autoExposure;
            Destroy(_rt); Destroy(_tex);
            Done = true;
        }

        void Set(bool area, bool traced, float soft, float contact, float haze)
        {
            _o.ApplyLook(0);
            _o.areaLights = area; _o.tracedBounce = traced; _o.shadowSoftness = soft; _o.contactShadows = contact; _o.haze = haze;
        }

        IEnumerator Variants(string view, bool looks)
        {
            // Exposure from the full look, then held for every variant.
            Set(true, true, 1f, 0.6f, 0.25f);
            _rig.Settings.autoExposure = true;
            yield return Wait(2.5f);
            _rig.Settings.autoExposure = false;
            var list = new List<(string name, System.Action apply)>
            {
                ("old", () => Set(false, false, 0f, 0f, 0f)),
                ("area", () => Set(true, false, 0f, 0f, 0f)),
                ("traced", () => Set(false, true, 0f, 0f, 0f)),
                ("soft", () => Set(false, false, 1f, 0f, 0f)),
                ("contact", () => Set(false, false, 0f, 1f, 0f)),
                ("haze", () => Set(false, false, 0f, 0f, 0.6f)),
                ("all", () => Set(true, true, 1f, 0.6f, 0.25f)),
            };
            if (looks)
                for (int i = 1; i < GraphicsOptions.LookNames.Length; i++) { int k = i; list.Add(("look" + k, () => { _o.ApplyLook(k); _o.areaLights = true; _o.tracedBounce = true; })); }
            foreach (var (name, apply) in list)
            {
                apply();
                yield return Wait(1.5f);
                float cpu = 0f, gpu = 0f; int n = 0, g = 0;
                for (int f = 0; f < 60; f++)
                {
                    yield return null;
                    cpu += Time.unscaledDeltaTime * 1000f; n++;
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, _timing) > 0 && _timing[0].gpuFrameTime > 0) { gpu += (float)_timing[0].gpuFrameTime; g++; }
                }
                var grid = FindAnyObjectByType<FixtureLightManager>()?.grid;
                _log.AppendLine($"{view}/{name}: frame {cpu / n:0.00} ms, gpu {(g > 0 ? (gpu / g).ToString("0.00") : "n/a")} ms, probes {(grid ? grid.ProbeFrames : -1)}, gridSubmit {(grid ? grid.LastGpuSubmitMs : 0):0.00} ms");
                Save($"{view}_{name}.png");
            }
        }

        IEnumerator Lit()
        {
            Light best = null; float bd = float.MaxValue;
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (!l.enabled || l.type != LightType.Spot || l.intensity < 1f || l.transform.position.y < 2f) continue;
                float d = (l.transform.position - _pl.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = l; }
            }
            if (!best) { _log.AppendLine("lit: no light"); yield break; }
            Vector3 p = best.transform.position;
            if (Physics.Raycast(p + Vector3.down * 0.3f, Vector3.down, out var floor, 4f)) p = floor.point + Vector3.up * 0.05f;
            float yaw = OpenestYaw(p + Vector3.up * 1.5f);
            // Step back from the light so it and its pool are in view.
            p -= Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 1.2f;
            _pl.Teleport(p, yaw, 12f);
            yield return Wait(2f);
            yield return Variants("lit", true);
        }

        IEnumerator Dark()
        {
            LightSwitch sw = null; float bd = float.MaxValue;
            foreach (var s in FindObjectsByType<LightSwitch>(FindObjectsInactive.Exclude))
            {
                float d = (s.transform.position - _pl.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; sw = s; }
            }
            if (!sw) { _log.AppendLine("dark: no switch"); yield break; }
            Vector3 p = sw.transform.position + sw.transform.forward * 0.9f;
            if (Physics.Raycast(p, Vector3.down, out var floor, 3f)) p = floor.point + Vector3.up * 0.05f;
            _pl.Teleport(p, OpenestYaw(p + Vector3.up * 1.5f), 5f);
            yield return Wait(1.5f);
            sw.Press();
            yield return Wait(2.5f);
            yield return Variants("dark", false);
            sw.Press();
            yield return Wait(1f);
        }

        IEnumerator Furniture()
        {
            Collider best = null; float bd = float.MaxValue;
            foreach (var c in FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude))
            {
                if (!c.GetComponentInParent<LightingOccluder>() || c.bounds.size.y < 0.4f) continue;
                float d = (c.bounds.center - _pl.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            if (!best) { _log.AppendLine("furniture: none"); yield break; }
            var b = best.bounds;
            Vector3 from = b.center + new Vector3(1.4f, 0f, 0.9f);
            for (int k = 0; k < 8; k++)
            {
                var dir = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                var at = b.center + dir * (1.2f + b.extents.magnitude);
                at.y = b.min.y + 0.05f;
                if (Physics.CheckSphere(at + Vector3.up * 1f, 0.35f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.Linecast(at + Vector3.up * 1.2f, b.center, out var hit) && hit.collider != best) continue;
                from = at; break;
            }
            var look = b.center - (from + Vector3.up * 1.5f);
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            _pl.Teleport(from, yaw, pitch);
            yield return Wait(2f);
            yield return Variants("furniture", false);
        }

        /// <summary>In front of the nearest crawl hole: the light out of it with the duct passing none, the default and a lot.</summary>
        IEnumerator Hole()
        {
            CrawlPortalView best = null; float bd = float.MaxValue;
            foreach (var v in CrawlPortalView.All)
            {
                if (!v) continue;
                float d = (v.position - _pl.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = v; }
            }
            var grid = FindAnyObjectByType<FixtureLightManager>()?.grid;
            if (!best || !grid) { _log.AppendLine("hole: none"); yield break; }
            Vector3 stand = best.position + best.normal * 2.2f; stand.y = best.position.y - CrawlPortals.HoleHalf + 0.05f;
            Vector3 look = best.position - (stand + Vector3.up * _pl.standEye);
            _pl.Teleport(stand, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg);
            _o.ApplyLook(0);
            _rig.Settings.autoExposure = true;
            yield return Wait(3f);
            _rig.Settings.autoExposure = false;
            float keep = grid.holeTransmission;
            foreach (var t in new[] { 0f, 0.15f, 0.5f })
            {
                grid.holeTransmission = t;
                yield return Wait(2.5f);
                _log.AppendLine($"hole transmission {t}: lit duct {best.name}");
                Save($"hole_{t:0.00}.png");
            }
            grid.holeTransmission = keep;
        }

        static float OpenestYaw(Vector3 eye)
        {
            float best = 0f, bestD = -1f;
            for (int k = 0; k < 16; k++)
            {
                float yaw = k * 22.5f;
                var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                float d = Physics.Raycast(eye, dir, out var h, 12f, ~0, QueryTriggerInteraction.Ignore) ? h.distance : 12f;
                if (d > 7f) d = 7f - (d - 7f) * 0.3f;   // a wall in view, not an endless corridor
                if (d > bestD) { bestD = d; best = yaw; }
            }
            return best;
        }

        void Save(string name)
        {
            var cam = Camera.main;
            var old = cam.targetTexture;
            cam.targetTexture = _rt;
            cam.Render();
            cam.targetTexture = old;
            var active = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0); _tex.Apply();
            RenderTexture.active = active;
            File.WriteAllBytes(Path.Combine(folder, name), _tex.EncodeToPNG());
        }

        static IEnumerator Wait(float s) { for (float t = 0; t < s; t += Time.unscaledDeltaTime) yield return null; }
    }
}
