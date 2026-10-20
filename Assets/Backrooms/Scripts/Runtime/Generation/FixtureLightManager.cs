using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Generation
{
    /// <summary>
    /// The infinite world has thousands of working fixtures, so real lights come from a small pool that
    /// follows the camera. A fixture gets a light when it is close, or when the camera can see it or the
    /// floor beneath it. The nearest lights cast soft shadows (stopping spill through walls); farther
    /// pooled lights fade out with distance into the fog. Each troffer light is a downward spot plus a
    /// soft "bounce" point light that only affects its own room's rendering layer, standing in for the
    /// baked bounce of the hand-built room. The fluorescent hum is pooled the same way.
    /// </summary>
    public class FixtureLightManager : MonoBehaviour
    {
        public Transform viewer;

        [Header("Troffers")]
        public Color tubeColor = new Color(1.0f, 0.965f, 0.86f);
        [Range(0f, 30f)] public float tubeIntensity = 7f;
        [Range(2f, 10f)] public float tubeRange = 6f;
        [Range(0f, 3f)] public float bounceIntensity = 0.55f;
        [Range(1f, 8f)] public float bounceRange = 4.2f;

        [Header("Floor lamps")]
        public Color lampColor = new Color(1f, 0.74f, 0.47f);
        [Range(0f, 10f)] public float lampIntensity = 1.6f;
        [Range(1f, 8f)] public float lampRange = 4.5f;

        [Header("Pool")]
        [Range(8, 128)] public int maxTroffers = 56;
        [Range(0, 16)] public int maxLamps = 8;
        [Tooltip("Lights within this distance are always active, visible or not (covers spill through doors).")]
        public float alwaysOnDistance = 9f;
        public float maxDistance = 28f;
        public float fadeStart = 18f;
        [Range(0, 32)] public int shadowedTroffers = 14;
        [Range(0, 4)] public int shadowedLamps = 2;
        [Tooltip("Visibility rays tested per frame.")]
        public int raysPerFrame = 160;

        [Header("Hum")]
        [Range(0, 12)] public int humVoices = 5;
        [Range(0f, 0.2f)] public float humVolume = 0.018f;

        public int ActiveLights { get; private set; }

        class Slot
        {
            public Light main, bounce;
            public int source = -1;
            public Vector3? sourcePos;
            public uint roomLayer;
            public float fade, target;
            public bool isLamp;
        }

        readonly Dictionary<Vector2Int, List<LightSource>> _byChunk = new Dictionary<Vector2Int, List<LightSource>>();
        readonly List<LightSource> _candidates = new List<LightSource>();
        readonly Dictionary<Vector3, float> _lastSeen = new Dictionary<Vector3, float>();
        readonly List<Slot> _troffers = new List<Slot>(), _lamps = new List<Slot>();
        readonly List<AudioSource> _hum = new List<AudioSource>();
        int _rayCursor;
        int _mask;

        public void Register(Vector2Int chunk, List<LightSource> lights) => _byChunk[chunk] = lights;

        public void Unregister(Vector2Int chunk)
        {
            if (!_byChunk.TryGetValue(chunk, out var list)) return;
            foreach (var l in list) _lastSeen.Remove(l.position);
            _byChunk.Remove(chunk);
        }

        void Awake()
        {
            _mask = Physics.DefaultRaycastLayers;
            for (int i = 0; i < maxTroffers; i++) _troffers.Add(MakeSlot(false, i));
            for (int i = 0; i < maxLamps; i++) _lamps.Add(MakeSlot(true, i));
            var clip = ProceduralAmbience.BuildHumClip(60f, 11);
            for (int i = 0; i < humVoices; i++)
            {
                var go = new GameObject("Hum_" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.clip = clip; src.loop = true; src.spatialBlend = 1f; src.dopplerLevel = 0f;
                src.rolloffMode = AudioRolloffMode.Logarithmic; src.minDistance = 0.8f; src.maxDistance = 10f;
                src.volume = 0f; src.timeSamples = (clip.samples / Mathf.Max(1, humVoices)) * i;
                src.pitch = 1f + (i - humVoices * 0.5f) * 0.002f;
                src.Play();
                _hum.Add(src);
            }
        }

        Slot MakeSlot(bool lamp, int i)
        {
            var s = new Slot { isLamp = lamp };
            var go = new GameObject((lamp ? "LampLight_" : "TubeLight_") + i);
            go.transform.SetParent(transform, false);
            s.main = go.AddComponent<Light>();
            var data = s.main.GetUniversalAdditionalLightData();
            if (lamp)
            {
                s.main.type = LightType.Point;
                s.main.color = lampColor;
                s.main.range = lampRange;
            }
            else
            {
                s.main.type = LightType.Spot;
                s.main.color = tubeColor;
                s.main.range = tubeRange;
                s.main.spotAngle = 150f;
                s.main.innerSpotAngle = 60f;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var b = new GameObject("Bounce");
                b.transform.SetParent(go.transform, false);
                s.bounce = b.AddComponent<Light>();
                s.bounce.type = LightType.Point;
                s.bounce.color = Color.Lerp(tubeColor, new Color(1f, 0.9f, 0.62f), 0.5f);
                s.bounce.range = bounceRange;
                s.bounce.shadows = LightShadows.None;
                s.bounce.enabled = false;
            }
            s.main.shadows = LightShadows.None;
            s.main.shadowBias = 0.03f;
            s.main.shadowNormalBias = 0.4f;
            s.main.shadowNearPlane = 0.1f;
            data.softShadowQuality = SoftShadowQuality.Medium;
            s.main.enabled = false;
            return s;
        }

        void LateUpdate()
        {
            if (!viewer) return;
            Vector3 eye = viewer.position;
            float now = Time.time;

            // Gather candidates from chunks near the viewer.
            _candidates.Clear();
            var center = WorldStreamer.ChunkOf(eye);
            int r = Mathf.CeilToInt(maxDistance / OfficeBiome.ChunkSize);
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
                if (_byChunk.TryGetValue(new Vector2Int(center.x + dx, center.y + dz), out var list))
                    foreach (var l in list)
                        if ((l.position - eye).sqrMagnitude < maxDistance * maxDistance) _candidates.Add(l);

            // Round-robin visibility rays: to the lens and to the floor below it.
            int rays = Mathf.Min(raysPerFrame, _candidates.Count);
            for (int i = 0; i < rays; i++)
            {
                _rayCursor = (_rayCursor + 1) % Mathf.Max(1, _candidates.Count);
                var c = _candidates[_rayCursor];
                Vector3 lens = c.position + Vector3.down * 0.08f;
                Vector3 floor = new Vector3(c.position.x, 0.4f, c.position.z);
                if (!Physics.Linecast(eye, lens, _mask, QueryTriggerInteraction.Ignore) ||
                    !Physics.Linecast(eye, floor, _mask, QueryTriggerInteraction.Ignore))
                    _lastSeen[c.position] = now;
            }

            // Wanted = close, or seen within the last 2 s. Sort by distance.
            _candidates.Sort((a, b) => (a.position - eye).sqrMagnitude.CompareTo((b.position - eye).sqrMagnitude));
            var wantedTroffers = new List<int>(); var wantedLamps = new List<int>();
            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                float d = Vector3.Distance(c.position, eye);
                bool want = d < alwaysOnDistance || (_lastSeen.TryGetValue(c.position, out var t) && now - t < 2f);
                if (!want) continue;
                if (c.isLamp) { if (wantedLamps.Count < _lamps.Count) wantedLamps.Add(i); }
                else if (wantedTroffers.Count < _troffers.Count) wantedTroffers.Add(i);
            }

            ActiveLights = Assign(_troffers, wantedTroffers, eye, shadowedTroffers) + Assign(_lamps, wantedLamps, eye, shadowedLamps);
            UpdateHum(eye);
        }

        readonly Dictionary<Vector3, int> _wantedIndex = new Dictionary<Vector3, int>();

        int Assign(List<Slot> slots, List<int> wanted, Vector3 eye, int shadowCount)
        {
            _wantedIndex.Clear();
            for (int i = 0; i < wanted.Count; i++) _wantedIndex[_candidates[wanted[i]].position] = i;

            // Keep slots whose source is still wanted; free the rest (they fade out first).
            var taken = new HashSet<int>();
            foreach (var s in slots)
            {
                if (s.source < 0) continue;
                if (s.sourcePos.HasValue && _wantedIndex.TryGetValue(s.sourcePos.Value, out int wi)) { taken.Add(wi); s.target = 1f; }
                else s.target = 0f;
            }
            foreach (int wi in EnumerateMissing(wanted.Count, taken))
            {
                Slot free = null;
                foreach (var s in slots) if (s.source < 0 || (s.target == 0f && s.fade <= 0.001f)) { free = s; break; }
                if (free == null) break;
                var src = _candidates[wanted[wi]];
                free.source = wi; free.sourcePos = src.position; free.target = 1f; free.fade = 0f;
                free.roomLayer = src.roomLayer;
                free.main.transform.position = src.isLamp ? src.position : src.position + Vector3.down * 0.06f;
                if (free.bounce)
                {
                    free.bounce.transform.position = src.position + Vector3.down * 1.1f;
                    free.bounce.GetUniversalAdditionalLightData().renderingLayers = src.roomLayer & ~1u;
                }
            }

            // Fade, distance attenuation, shadows for the nearest.
            var active = new List<Slot>();
            foreach (var s in slots)
            {
                if (s.source < 0) continue;
                s.fade = Mathf.MoveTowards(s.fade, s.target, Time.deltaTime * (s.target > s.fade ? 6f : 3f));
                if (s.fade <= 0.001f && s.target == 0f) { s.source = -1; s.sourcePos = null; s.main.enabled = false; if (s.bounce) s.bounce.enabled = false; continue; }
                active.Add(s);
            }
            active.Sort((a, b) => (a.main.transform.position - eye).sqrMagnitude.CompareTo((b.main.transform.position - eye).sqrMagnitude));
            for (int i = 0; i < active.Count; i++)
            {
                var s = active[i];
                float d = Vector3.Distance(s.main.transform.position, eye);
                float k = s.fade * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, maxDistance, d)));
                s.main.enabled = k > 0.002f;
                if (s.isLamp) { s.main.intensity = lampIntensity * k; s.main.range = lampRange; s.main.color = lampColor; }
                else
                {
                    s.main.intensity = tubeIntensity * k; s.main.range = tubeRange; s.main.color = tubeColor;
                    s.bounce.enabled = s.main.enabled && bounceIntensity > 0f;
                    s.bounce.intensity = bounceIntensity * k; s.bounce.range = bounceRange;
                }
                var want = i < shadowCount ? LightShadows.Soft : LightShadows.None;
                if (s.main.shadows != want) s.main.shadows = want;
            }
            return active.Count;
        }

        static IEnumerable<int> EnumerateMissing(int count, HashSet<int> taken)
        {
            for (int i = 0; i < count; i++) if (!taken.Contains(i)) yield return i;
        }

        void UpdateHum(Vector3 eye)
        {
            int v = 0;
            foreach (var c in _candidates)
            {
                if (v >= _hum.Count) break;
                if (c.isLamp) continue;
                _hum[v].transform.position = c.position;
                _hum[v].volume = Mathf.MoveTowards(_hum[v].volume, humVolume, Time.deltaTime * 0.05f);
                v++;
            }
            for (; v < _hum.Count; v++) _hum[v].volume = Mathf.MoveTowards(_hum[v].volume, 0f, Time.deltaTime * 0.05f);
        }
    }
}
