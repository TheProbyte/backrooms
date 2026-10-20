using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Generation
{
    /// <summary>
    /// The infinite world has thousands of working fixtures, so real lights come from a pool that follows
    /// the camera. Rules that keep lighting stable while you move:
    ///  - Every fixture inside <see cref="alwaysOnDistance"/> is lit whether visible or not, so turning a
    ///    corner never reveals a light switching on. Farther fixtures are lit when seen (and stay lit for a
    ///    few seconds), and they fade with distance into the fog.
    ///  - Shadows go to the nearest lights with hysteresis (a light keeps its shadow until it is clearly
    ///    farther than a competitor), and shadow strength fades in and out instead of snapping.
    ///  - Each troffer is a downward spot plus a soft "bounce" point light restricted to its own room's
    ///    rendering layer, standing in for baked bounce without leaking through walls.
    /// Newly streamed chunks power up with a fluorescent start-up flicker, and chunks being unloaded
    /// stutter off; faulty tubes occasionally stutter. The hum is pooled to a few voices.
    /// </summary>
    public class FixtureLightManager : MonoBehaviour
    {
        public Transform viewer;

        [Header("Troffers")]
        public Color tubeColor = new Color(1.0f, 0.965f, 0.86f);
        [Range(0f, 30f)] public float tubeIntensity = 7f;
        [Range(2f, 10f)] public float tubeRange = 6f;
        [Range(0f, 3f)] public float bounceIntensity = 0.9f;
        [Range(1f, 8f)] public float bounceRange = 4.4f;
        [Tooltip("Dome fittings on tall ceilings: intensity multiplier per metre of extra height.")]
        public float domeHeightBoost = 0.3f;

        [Header("Floor lamps")]
        public Color lampColor = new Color(1f, 0.74f, 0.47f);
        [Range(0f, 10f)] public float lampIntensity = 1.8f;
        [Range(1f, 8f)] public float lampRange = 5f;

        [Header("Pool")]
        [Range(8, 160)] public int maxTroffers = 110;
        [Range(0, 16)] public int maxLamps = 10;
        [Tooltip("Lights within this distance are always active, visible or not.")]
        public float alwaysOnDistance = 14f;
        public float maxDistance = 30f;
        public float fadeStart = 20f;
        [Range(0, 48)] public int shadowedTroffers = 26;
        [Range(0, 4)] public int shadowedLamps = 1;
        [Tooltip("A shadowed light keeps its shadow until a competitor is this much closer (fraction).")]
        [Range(0f, 0.5f)] public float shadowHysteresis = 0.22f;
        [Tooltip("Seconds for shadows to fade in or out.")]
        public float shadowFade = 0.35f;
        [Tooltip("Visibility rays tested per frame.")]
        public int raysPerFrame = 200;

        [Header("Power flicker")]
        [Tooltip("Seconds a newly streamed area takes to flicker on (and an unloading one to stutter off).")]
        public float powerFlickerTime = 1.1f;

        [Header("Hum")]
        [Range(0, 12)] public int humVoices = 5;
        [Range(0f, 0.2f)] public float humVolume = 0.018f;

        public int ActiveLights { get; private set; }

        class Slot
        {
            public Light main, bounce;
            public bool busy;
            public Vector3 sourcePos;
            public LightSource src;
            public float fade, target;
            public bool isLamp, wantShadow;
            public float shadowK;          // 0..1 shadow strength ramp
        }

        class ChunkPower
        {
            public List<LightSource> lights;
            public List<Renderer> lenses, faulty;
            public float start;            // time power began (start-up) or -1 when steady
            public float stop = -1f;       // time power-down began
            public float seed;
        }

        readonly Dictionary<Vector2Int, ChunkPower> _chunks = new Dictionary<Vector2Int, ChunkPower>();
        readonly List<LightSource> _candidates = new List<LightSource>();
        readonly Dictionary<Vector3, float> _lastSeen = new Dictionary<Vector3, float>();
        readonly List<Slot> _troffers = new List<Slot>(), _lamps = new List<Slot>();
        readonly List<AudioSource> _hum = new List<AudioSource>();
        MaterialPropertyBlock _mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly System.Reflection.FieldInfo s_tierField = typeof(UniversalAdditionalLightData).GetField(
            "m_AdditionalLightsShadowResolutionTier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        int _rayCursor, _mask;

        // ---------------- Registration ----------------

        /// <summary>Called by the streamer. animatePowerOn: flicker the area on (false for the initial load).</summary>
        public void Register(Vector2Int chunk, List<LightSource> lights, List<Renderer> lenses, List<Renderer> faulty, bool animatePowerOn)
        {
            for (int i = 0; i < lights.Count; i++) { var l = lights[i]; l.chunk = chunk; lights[i] = l; }
            _chunks[chunk] = new ChunkPower
            {
                lights = lights, lenses = lenses ?? new List<Renderer>(), faulty = faulty ?? new List<Renderer>(),
                start = animatePowerOn ? Time.time : -1f, seed = (chunk.x * 0.731f + chunk.y * 1.37f) % 10f,
            };
        }

        static Color BaseEmission(Renderer r)
        {
            var m = r.sharedMaterial;
            return m && m.HasProperty(EmissionId) ? m.GetColor(EmissionId) : Color.black;
        }

        /// <summary>Starts the power-down stutter. Returns the seconds until the chunk may be destroyed.</summary>
        public float BeginPowerDown(Vector2Int chunk)
        {
            if (_chunks.TryGetValue(chunk, out var p) && p.stop < 0f) p.stop = Time.time;
            return powerFlickerTime;
        }

        public void Unregister(Vector2Int chunk)
        {
            if (!_chunks.TryGetValue(chunk, out var p)) return;
            foreach (var l in p.lights) _lastSeen.Remove(l.position);
            _chunks.Remove(chunk);
            foreach (var s in _troffers) if (s.busy && s.src.chunk == chunk) Release(s);
            foreach (var s in _lamps) if (s.busy && s.src.chunk == chunk) Release(s);
        }

        // ---------------- Setup ----------------

        void Awake()
        {
            _mask = Physics.DefaultRaycastLayers;
            _mpb = new MaterialPropertyBlock();
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
            s.main.shadowBias = 0.04f;
            s.main.shadowNormalBias = 0.5f;
            s.main.shadowNearPlane = 0.1f;
            data.softShadowQuality = SoftShadowQuality.High;
            // Medium (512) spot maps and low lamp cubes keep all pooled shadows inside the atlas.
            // (The tier has no public setter; it is a serialised field.)
            s_tierField?.SetValue(data, lamp
                ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow
                : UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium);
            s.main.enabled = false;
            return s;
        }

        // ---------------- Per frame ----------------

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
                if (_chunks.TryGetValue(new Vector2Int(center.x + dx, center.y + dz), out var cp))
                    foreach (var l in cp.lights)
                        if ((l.position - eye).sqrMagnitude < maxDistance * maxDistance) _candidates.Add(l);

            // Round-robin visibility rays: to the lens and to the floor below it.
            int rays = Mathf.Min(raysPerFrame, _candidates.Count);
            for (int i = 0; i < rays; i++)
            {
                _rayCursor = (_rayCursor + 1) % Mathf.Max(1, _candidates.Count);
                var c = _candidates[_rayCursor];
                if ((c.position - eye).sqrMagnitude < alwaysOnDistance * alwaysOnDistance) continue;
                Vector3 lens = c.position + Vector3.down * 0.08f;
                Vector3 floor = new Vector3(c.position.x, 0.4f, c.position.z);
                if (!Physics.Linecast(eye, lens, _mask, QueryTriggerInteraction.Ignore) ||
                    !Physics.Linecast(eye, floor, _mask, QueryTriggerInteraction.Ignore))
                    _lastSeen[c.position] = now;
            }

            _candidates.Sort((a, b) => (a.position - eye).sqrMagnitude.CompareTo((b.position - eye).sqrMagnitude));
            var wantedTroffers = new List<int>(); var wantedLamps = new List<int>();
            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                float d = Vector3.Distance(c.position, eye);
                bool want = d < alwaysOnDistance || (_lastSeen.TryGetValue(c.position, out var t) && now - t < 4f);
                if (!want) continue;
                if (c.isLamp) { if (wantedLamps.Count < _lamps.Count) wantedLamps.Add(i); }
                else if (wantedTroffers.Count < _troffers.Count) wantedTroffers.Add(i);
            }

            ActiveLights = Assign(_troffers, wantedTroffers, eye, shadowedTroffers) + Assign(_lamps, wantedLamps, eye, shadowedLamps);
            UpdateLensEmission(now);
            UpdateHum(eye);
        }

        readonly Dictionary<Vector3, int> _wantedIndex = new Dictionary<Vector3, int>();
        readonly HashSet<int> _taken = new HashSet<int>();
        readonly List<Slot> _active = new List<Slot>();

        int Assign(List<Slot> slots, List<int> wanted, Vector3 eye, int shadowCount)
        {
            _wantedIndex.Clear();
            for (int i = 0; i < wanted.Count; i++) _wantedIndex[_candidates[wanted[i]].position] = i;

            // Keep slots whose source is still wanted; the rest fade out before being reused.
            _taken.Clear();
            foreach (var s in slots)
            {
                if (!s.busy) continue;
                if (_wantedIndex.TryGetValue(s.sourcePos, out int wi)) { _taken.Add(wi); s.target = 1f; }
                else s.target = 0f;
            }
            for (int wi = 0; wi < wanted.Count; wi++)
            {
                if (_taken.Contains(wi)) continue;
                Slot free = null;
                foreach (var s in slots) if (!s.busy) { free = s; break; }
                if (free == null) break;
                var src = _candidates[wanted[wi]];
                free.busy = true; free.src = src; free.sourcePos = src.position;
                free.target = 1f;
                // Lights entering within the always-on radius start fully on: they were already lit
                // before reaching it, so a fade-in here would read as a light switching on.
                float d = Vector3.Distance(src.position, eye);
                free.fade = d < alwaysOnDistance * 0.9f ? 1f : 0f;
                free.wantShadow = false; free.shadowK = 0f;
                free.main.shadows = LightShadows.None;
                free.main.transform.position = src.isLamp ? src.position : src.position + Vector3.down * 0.06f;
                if (free.bounce)
                {
                    float drop = src.dome ? Mathf.Min(src.ceiling * 0.45f, 2.2f) : 1.1f;
                    free.bounce.transform.position = src.position + Vector3.down * drop;
                    free.bounce.GetUniversalAdditionalLightData().renderingLayers = src.roomLayer & ~1u;
                }
            }

            // Fade, distance attenuation, power flicker.
            _active.Clear();
            float dt = Time.deltaTime;
            foreach (var s in slots)
            {
                if (!s.busy) continue;
                s.fade = Mathf.MoveTowards(s.fade, s.target, dt * (s.target > s.fade ? 2.5f : 2f));
                if (s.fade <= 0.001f && s.target == 0f) { Release(s); continue; }
                _active.Add(s);
            }
            _active.Sort((a, b) => (a.main.transform.position - eye).sqrMagnitude.CompareTo((b.main.transform.position - eye).sqrMagnitude));

            // Shadows with hysteresis: the nearest N get shadows, but a light already shadowed keeps it
            // unless it falls clearly behind (its distance is discounted by the hysteresis fraction).
            float cutoff = _active.Count > shadowCount && shadowCount > 0
                ? Vector3.Distance(_active[shadowCount - 1].main.transform.position, eye) : float.MaxValue;
            int granted = 0;
            foreach (var s in _active)
            {
                float d = Vector3.Distance(s.main.transform.position, eye);
                bool want = s.wantShadow ? d * (1f - shadowHysteresis) <= cutoff : d <= cutoff;
                if (want && granted >= shadowCount + 4) want = false;     // hard cap (atlas budget)
                if (want) granted++;
                s.wantShadow = want;
            }

            foreach (var s in _active)
            {
                float d = Vector3.Distance(s.main.transform.position, eye);
                float power = PowerLevel(s.src, Time.time);
                float k = s.fade * power * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, maxDistance, d)));
                s.main.enabled = k > 0.002f;
                if (s.isLamp) { s.main.intensity = lampIntensity * k; s.main.range = lampRange; s.main.color = lampColor; }
                else
                {
                    float boost = s.src.dome ? 1f + (s.src.ceiling - OfficeBiome.CeilingHeight) * domeHeightBoost : 1f;
                    s.main.intensity = tubeIntensity * k * boost;
                    s.main.range = s.src.dome ? tubeRange + (s.src.ceiling - OfficeBiome.CeilingHeight) * 1.2f : tubeRange;
                    // Domes light the whole hemisphere: a near-180 cone with a wide soft falloff, so no
                    // cone edge is drawn across tall walls.
                    s.main.spotAngle = s.src.dome ? 176f : 150f;
                    s.main.innerSpotAngle = s.src.dome ? 30f : 60f;
                    s.main.color = tubeColor;
                    s.bounce.enabled = s.main.enabled && bounceIntensity > 0f;
                    s.bounce.intensity = bounceIntensity * k * (s.src.dome ? 1.4f : 1f);
                    s.bounce.range = s.src.dome ? bounceRange * 1.5f : bounceRange;
                }

                // Shadow strength ramps; the shadow map is only dropped once fully faded.
                s.shadowK = Mathf.MoveTowards(s.shadowK, s.wantShadow ? 1f : 0f, dt / Mathf.Max(0.01f, shadowFade));
                var mode = s.shadowK > 0.001f ? LightShadows.Soft : LightShadows.None;
                if (s.main.shadows != mode) s.main.shadows = mode;
                s.main.shadowStrength = Mathf.SmoothStep(0f, 0.82f, s.shadowK);
            }
            return _active.Count;
        }

        void Release(Slot s)
        {
            s.busy = false; s.fade = 0f; s.target = 0f; s.shadowK = 0f; s.wantShadow = false;
            s.main.enabled = false; s.main.shadows = LightShadows.None;
            if (s.bounce) s.bounce.enabled = false;
        }

        // ---------------- Flicker ----------------

        /// <summary>0..1 multiplier from area power-up/down and faulty-tube stutter.</summary>
        float PowerLevel(in LightSource src, float now)
        {
            if (!_chunks.TryGetValue(src.chunk, out var cp)) return 0f;
            float seed = Hash01(src.position);
            float level = 1f;
            if (cp.start >= 0f)
            {
                float t = now - cp.start - seed * powerFlickerTime * 0.6f;   // fixtures strike at different moments
                level = StartupCurve(t, seed);
                if (t > 2f) cp.start = -1f;
            }
            if (cp.stop >= 0f) level *= ShutdownCurve(now - cp.stop - seed * powerFlickerTime * 0.4f, seed);
            if (src.faultyId >= 0) level *= FaultyCurve(now, seed);
            return level;
        }

        /// <summary>Fluorescent strike: dark, a couple of blinks, a brief dim glow, then full.</summary>
        static float StartupCurve(float t, float seed)
        {
            if (t < 0f) return 0f;
            if (t > 0.9f) return 1f;
            float blink1 = t > 0.05f && t < 0.11f ? 0.9f : 0f;
            float blink2 = t > 0.28f + seed * 0.1f && t < 0.33f + seed * 0.1f ? 0.7f : 0f;
            float glow = t > 0.5f ? Mathf.SmoothStep(0.25f, 1f, (t - 0.5f) / 0.4f) : 0f;
            return Mathf.Max(blink1, Mathf.Max(blink2, glow));
        }

        static float ShutdownCurve(float t, float seed)
        {
            if (t < 0f) return 1f;
            if (t > 0.7f) return 0f;
            float stutter = Mathf.PerlinNoise(t * 22f, seed * 10f) > 0.45f ? 1f : 0.15f;
            return stutter * (1f - t / 0.7f);
        }

        /// <summary>Mostly steady, with rare bursts of rapid stutter lasting under a second.</summary>
        static float FaultyCurve(float now, float seed)
        {
            float burst = Mathf.PerlinNoise(now * 0.18f, seed * 31f);
            if (burst < 0.72f) return 1f;
            float s = Mathf.PerlinNoise(now * 28f, seed * 7f);
            return s > 0.5f ? 1f : 0.08f + s * 0.3f;
        }

        static float Hash01(Vector3 p)
        {
            unchecked
            {
                uint h = (uint)(Mathf.RoundToInt(p.x * 10f) * 73856093 ^ Mathf.RoundToInt(p.z * 10f) * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFF) / 65535f;
            }
        }

        /// <summary>Drives lens/shade emission for chunks that are powering up or down, and faulty tubes.</summary>
        void UpdateLensEmission(float now)
        {
            foreach (var kv in _chunks)
            {
                var cp = kv.Value;
                bool animating = cp.start >= 0f || cp.stop >= 0f;
                float level = 1f;
                if (cp.start >= 0f) level = StartupCurve(now - cp.start - 0.25f, cp.seed % 1f);
                if (cp.stop >= 0f) level *= ShutdownCurve(now - cp.stop, cp.seed % 1f);
                foreach (var r in cp.lenses)
                {
                    if (!r) continue;
                    if (!animating) { if (r.HasPropertyBlock()) r.SetPropertyBlock(null); continue; }
                    _mpb.Clear(); _mpb.SetColor(EmissionId, BaseEmission(r) * level); r.SetPropertyBlock(_mpb);
                }
                for (int i = 0; i < cp.faulty.Count; i++)
                {
                    var r = cp.faulty[i];
                    if (!r) continue;
                    float f = level;
                    foreach (var l in cp.lights) if (l.faultyId == i) { f *= FaultyCurve(now, Hash01(l.position)); break; }
                    _mpb.Clear(); _mpb.SetColor(EmissionId, BaseEmission(r) * f); r.SetPropertyBlock(_mpb);
                }
            }
        }

        void UpdateHum(Vector3 eye)
        {
            int v = 0;
            foreach (var c in _candidates)
            {
                if (v >= _hum.Count) break;
                if (c.isLamp) continue;
                _hum[v].transform.position = c.position;
                _hum[v].volume = Mathf.MoveTowards(_hum[v].volume, humVolume * PowerLevel(c, Time.time), Time.deltaTime * 0.05f);
                v++;
            }
            for (; v < _hum.Count; v++) _hum[v].volume = Mathf.MoveTowards(_hum[v].volume, 0f, Time.deltaTime * 0.05f);
        }
    }
}
