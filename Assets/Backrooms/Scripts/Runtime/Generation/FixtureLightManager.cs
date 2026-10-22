using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Generation
{
    /// <summary>
    /// The infinite world has thousands of working fixtures, so real lights come from a pool that follows
    /// the camera. Rules that keep lighting stable while you move:
    ///  - Every powered fixture within <see cref="maxDistance"/> is lit, nearest first, whether visible or
    ///    not: the building is already lit, nothing switches on as you approach. The farthest dim into fog.
    ///  - Every fixture light (shadowed or not) only reaches its own light zone's rendering layer, so light
    ///    never passes through a wall. Zones use 28 layers: 7 colours in 4 sets chosen by chunk parity, so
    ///    neighbouring chunks never share a layer (see LightLayers and OfficeLayoutGenerator.AssignColors).
    ///  - The nearest lights get shadows (with hysteresis, easing in). Shadows only add detail inside the
    ///    room (furniture, pillars); they do not let light out of it.
    ///  - Light through openings comes from doorway lights placed in the receiving room.
    ///  - The nearest troffers also get a soft upward "bounce" light restricted to their zone's layer.
    ///  - Each slot also owns a disabled "spill" light (an every-layer shadowed copy, kept for experiments;
    ///    it is never switched on).
    /// Newly streamed chunks can power up with a fluorescent start-up flicker, and chunks being unloaded
    /// stutter off; faulty tubes occasionally stutter. Fixture sound lives in Backrooms.Audio.BuildingAudio,
    /// which reads <see cref="PowerLevel"/> so hum, strikes and buzz stay in time with the light.
    /// </summary>
    public class FixtureLightManager : MonoBehaviour
    {
        public Transform viewer;

        [Header("Troffers")]
        public Color tubeColor = new Color(1.0f, 0.965f, 0.86f);
        [Range(0f, 30f)] public float tubeIntensity = 7f;
        [Range(2f, 10f)] public float tubeRange = 6f;
        [Range(0f, 3f)] public float bounceIntensity = 0.8f;
        [Range(1f, 8f)] public float bounceRange = 4.4f;
        [Tooltip("Dome fittings on tall ceilings: intensity multiplier per metre of extra height.")]
        public float domeHeightBoost = 0.3f;

        [Header("Floor lamps")]
        public Color lampColor = new Color(1f, 0.74f, 0.47f);
        [Range(0f, 10f)] public float lampIntensity = 1.8f;
        [Range(1f, 8f)] public float lampRange = 5f;

        [Header("Warehouse strips (cool white)")]
        public Color stripColor = new Color(0.9f, 0.95f, 1f);

        [Header("Pool")]
        [Tooltip("Ceiling light slots. Every powered fixture within the light distance is lit, nearest first; " +
                 "URP Forward+ allows 256 visible lights per camera, shared with the bounce lights.")]
        [Range(8, 220)] public int maxTroffers = 168;
        [Range(0, 16)] public int maxLamps = 10;
        [Tooltip("Fixtures are lit out to here, whether you can see them or not. Past fadeStart they dim into the fog.")]
        public float maxDistance = 36f;
        public float fadeStart = 28f;
        [Tooltip("Only the nearest fixtures add the soft bounce fill.")]
        [Range(0, 64)] public int bounceCount = 32;
        public float bounceDistance = 18f;
        [Header("Shadows")]
        [Range(0, 48)] public int shadowedTroffers = 24;
        [Range(0, 4)] public int shadowedLamps = 1;
        [Tooltip("Shadows fade out smoothly between 65% and 100% of this distance.")]
        public float shadowDistance = 20f;
        [Tooltip("A shadowed light keeps its shadow until a competitor is this much closer (fraction).")]
        [Range(0f, 0.5f)] public float shadowHysteresis = 0.22f;
        [Tooltip("Seconds for shadows to fade in or out when the nearest set changes.")]
        public float shadowFade = 0.5f;
        [Tooltip("0 low (256), 1 medium (512), 2 high (1024) shadow maps.")]
        [Range(0, 2)] public int shadowTier = 1;

        [Header("Power flicker")]
        [Tooltip("Seconds a newly streamed area takes to flicker on (and an unloading one to stutter off).")]
        public float powerFlickerTime = 1.1f;

        public int ActiveLights { get; private set; }

        class Slot
        {
            public Light main, bounce, spill;   // spill: an every-layer shadowed copy of main (unused, always off)
            public bool busy;
            public Vector3 sourcePos;
            public LightSource src;
            public float fade, target;
            public bool isLamp, wantShadow;
            public float shadowK;          // 0..1 shadow strength ramp
            public bool allLayers;         // (lamps) shadowed lamps light every room
            public float spillW;           // 0 = all light from the room-only main, 1 = from the shadowed spill
        }

        class ChunkPower
        {
            public List<LightSource> lights;
            public List<Renderer> lenses, faulty;
            public float start;            // time power began (start-up) or -1 when steady
            public float stop = -1f;       // time power-down began
            public float seed;
        }

        readonly Dictionary<Vector3Int, ChunkPower> _chunks = new Dictionary<Vector3Int, ChunkPower>();
        int _minStorey, _maxStorey;           // storeys with registered chunks (chunk keys are x, storey, z)
        readonly List<LightSource> _candidates = new List<LightSource>();
        readonly List<Slot> _troffers = new List<Slot>(), _lamps = new List<Slot>();
        MaterialPropertyBlock _mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly System.Reflection.FieldInfo s_tierField = typeof(UniversalAdditionalLightData).GetField(
            "m_AdditionalLightsShadowResolutionTier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        int _appliedTier = -1;

        // ---------------- Registration ----------------

        /// <summary>Called by the streamer. animatePowerOn: flicker the area on (false for the initial load).</summary>
        public void Register(Vector3Int chunk, List<LightSource> lights, List<Renderer> lenses, List<Renderer> faulty, bool animatePowerOn)
        {
            _minStorey = Mathf.Min(_minStorey, chunk.y); _maxStorey = Mathf.Max(_maxStorey, chunk.y);
            for (int i = 0; i < lights.Count; i++) { var l = lights[i]; l.chunk = chunk; lights[i] = l; }
            _chunks[chunk] = new ChunkPower
            {
                lights = lights, lenses = lenses ?? new List<Renderer>(), faulty = faulty ?? new List<Renderer>(),
                start = animatePowerOn ? Time.time : -1f, seed = (chunk.x * 0.731f + chunk.z * 1.37f + chunk.y * 2.11f) % 10f,
            };
        }

        static Color BaseEmission(Renderer r)
        {
            var m = r.sharedMaterial;
            return m && m.HasProperty(EmissionId) ? m.GetColor(EmissionId) : Color.black;
        }

        /// <summary>Starts the power-down stutter. Returns the seconds until the chunk may be destroyed.</summary>
        public float BeginPowerDown(Vector3Int chunk)
        {
            if (_chunks.TryGetValue(chunk, out var p) && p.stop < 0f) p.stop = Time.time;
            return powerFlickerTime;
        }

        public void Unregister(Vector3Int chunk)
        {
            if (!_chunks.TryGetValue(chunk, out var p)) return;
            _chunks.Remove(chunk);
            foreach (var s in _troffers) if (s.busy && s.src.chunk == chunk) Release(s);
            foreach (var s in _lamps) if (s.busy && s.src.chunk == chunk) Release(s);
        }

        /// <summary>Every powered ceiling tube (not lamps) in loaded chunks within radius of a point.</summary>
        public void GatherTubes(Vector3 center, float radius, List<LightSource> result)
        {
            result.Clear();
            var c = WorldStreamer.ChunkOf(center);
            int r = Mathf.CeilToInt(radius / OfficeBiome.ChunkSize);
            float r2 = radius * radius;
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            for (int s = _minStorey; s <= _maxStorey; s++)
                if (_chunks.TryGetValue(new Vector3Int(c.x + dx, s, c.y + dz), out var cp))
                    foreach (var l in cp.lights)
                        if (!l.isLamp && (l.position - center).sqrMagnitude < r2) result.Add(l);
        }

        /// <summary>True while the fixture's chunk is still striking on after streaming in.</summary>
        public bool IsStarting(in LightSource src) => _chunks.TryGetValue(src.chunk, out var cp) && cp.start >= 0f;

        // ---------------- Setup ----------------

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            for (int i = 0; i < maxTroffers; i++) _troffers.Add(MakeSlot(false, i));
            for (int i = 0; i < maxLamps; i++) _lamps.Add(MakeSlot(true, i));
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
                // An every-layer shadowed copy of main (its shadow would stop it at walls). Not used: it
                // stays disabled; doorway lights carry light through openings instead.
                var sp = new GameObject("Spill");
                sp.transform.SetParent(go.transform, false);
                s.spill = sp.AddComponent<Light>();
                s.spill.type = LightType.Spot;
                s.spill.color = tubeColor; s.spill.range = tubeRange; s.spill.spotAngle = 150f; s.spill.innerSpotAngle = 60f;
                s.spill.shadows = LightShadows.None; s.spill.shadowNearPlane = 0.1f;
                var sd = s.spill.GetUniversalAdditionalLightData();
                sd.softShadowQuality = SoftShadowQuality.High;
                sd.renderingLayers = 0xFFFFFFFEu;
                s_tierField?.SetValue(sd, UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium);
                s.spill.enabled = false;
                // Bounce: an upward fill for ceiling and upper walls, kept in the room. Pointing up keeps it
                // off the floor, where a room-limited light would draw a hard line at every doorway.
                var b = new GameObject("Bounce");
                b.transform.SetParent(go.transform, false);
                b.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                s.bounce = b.AddComponent<Light>();
                s.bounce.type = LightType.Spot;
                s.bounce.spotAngle = 172f; s.bounce.innerSpotAngle = 110f;
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
            _eye = eye;
            _candidates.Clear();
            var center = WorldStreamer.ChunkOf(eye);
            int r = Mathf.CeilToInt(maxDistance / OfficeBiome.ChunkSize);
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            for (int s = _minStorey; s <= _maxStorey; s++)
                if (_chunks.TryGetValue(new Vector3Int(center.x + dx, s, center.y + dz), out var cp))
                    foreach (var l in cp.lights)
                        if ((l.position - eye).sqrMagnitude < maxDistance * maxDistance) _candidates.Add(l);

            // The building is already lit: every powered fixture in range is on, nearest first, with no
            // dependence on what the camera can see. Light only reaches its own zone (rendering layers),
            // so it can never shine through a wall.
            if (_byDistance == null) _byDistance = (a, b) => (a.position - _eye).sqrMagnitude.CompareTo((b.position - _eye).sqrMagnitude);
            _candidates.Sort(_byDistance);
            _wantedTroffers.Clear(); _wantedLamps.Clear();
            for (int i = 0; i < _candidates.Count; i++)
            {
                if (_candidates[i].isLamp) { if (_wantedLamps.Count < _lamps.Count) _wantedLamps.Add(i); }
                else if (_wantedTroffers.Count < _troffers.Count) _wantedTroffers.Add(i);
            }
            // If the pool runs out before the light distance, dim the farthest few by rank instead.
            _rankEdge = _wantedTroffers.Count >= _troffers.Count && _wantedTroffers.Count > 0
                ? Vector3.Distance(_candidates[_wantedTroffers[_wantedTroffers.Count - 1]].position, eye) : float.MaxValue;
            // Distance at which the bounce budget runs out; bounce fades smoothly toward it.
            _bounceEdge = float.MaxValue;
            if (_wantedTroffers.Count > bounceCount && bounceCount > 0)
                _bounceEdge = Vector3.Distance(_candidates[_wantedTroffers[bounceCount - 1]].position, eye);
            ApplyShadowTier();
            UpdateDoorways(eye);

            ActiveLights = Assign(_troffers, _wantedTroffers, eye, shadowedTroffers) + Assign(_lamps, _wantedLamps, eye, shadowedLamps);
            UpdateLensEmission(now);
        }

        readonly List<int> _wantedTroffers = new List<int>(), _wantedLamps = new List<int>();
        System.Comparison<LightSource> _byDistance;
        Vector3 _eye;
        float _rankEdge = float.MaxValue, _bounceEdge = float.MaxValue;

        void ApplyShadowTier()
        {
            if (_appliedTier == shadowTier) return;
            _appliedTier = shadowTier;
            int tier = shadowTier == 0 ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow
                     : shadowTier == 2 ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh
                     : UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;
            foreach (var t in _troffers) s_tierField?.SetValue(t.main.GetUniversalAdditionalLightData(), tier);   // the shadowed ceiling lights
        }

        /// <summary>Lights currently enabled (ceiling, lamps and bounce) and shadowed, for the Graphics tab.</summary>
        public int EnabledLights { get; private set; }
        public int ShadowedLights { get; private set; }

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
                // A light is never switched on in view: it starts fully on (distance and rank dimming
                // handle the far edge, inside the fog).
                free.fade = 1f;
                free.wantShadow = false; free.shadowK = 0f;
                free.main.shadows = LightShadows.None;
                free.main.transform.position = src.isLamp ? src.position : src.position + Vector3.down * 0.06f;
                free.main.GetUniversalAdditionalLightData().renderingLayers = src.roomLayer & ~1u;
                free.allLayers = false; free.spillW = 0f;
                if (free.spill) { free.spill.enabled = false; free.spill.shadows = LightShadows.None; }
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
            if (shadowCount <= 0) cutoff = -1f;
            int granted = 0;
            foreach (var s in _active)
            {
                float d = Vector3.Distance(s.main.transform.position, eye);
                bool want = s.wantShadow ? d * (1f - shadowHysteresis) <= cutoff : d <= cutoff;
                if (d > shadowDistance) want = false;
                if (want && granted >= shadowCount + 3) want = false;     // hard cap (atlas budget)
                if (want) granted++;
                s.wantShadow = want;
            }

            int bounces = 0, enabled = 0, shadowed = 0;
            foreach (var s in _active)
            {
                float d = Vector3.Distance(s.main.transform.position, eye);
                float power = PowerLevel(s.src, Time.time);
                float distFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, maxDistance, d));
                float rankFade = s.isLamp ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_rankEdge - 4f, _rankEdge, d));
                float k = s.fade * power * distFade * rankFade;

                if (s.isLamp)
                {
                    s.main.enabled = k > 0.002f;
                    if (s.main.enabled) enabled++;
                    s.main.intensity = lampIntensity * k; s.main.range = lampRange; s.main.color = lampColor;
                    s.shadowK = Mathf.MoveTowards(s.shadowK, s.wantShadow ? 1f : 0f, dt / Mathf.Max(0.01f, shadowFade));
                    bool all = s.wantShadow && s.shadowK >= 0.999f && s.main.enabled;
                    if (all != s.allLayers)
                    {
                        s.allLayers = all;
                        s.main.GetUniversalAdditionalLightData().renderingLayers = all ? 0xFFFFFFFEu : s.src.roomLayer & ~1u;
                    }
                    float ls = Mathf.SmoothStep(0f, 1f, s.shadowK);
                    var lm = ls > 0.005f && s.main.enabled ? LightShadows.Soft : LightShadows.None;
                    if (s.main.shadows != lm) s.main.shadows = lm;
                    if (lm != LightShadows.None) shadowed++;
                    s.main.shadowStrength = ls;
                    continue;
                }

                float extra = s.src.ceiling - OfficeBiome.CeilingHeight;
                float boost = s.src.dome || s.src.strip ? 1f + extra * domeHeightBoost : 1f;
                if (s.src.dome) boost *= 1.6f;                // domes are spaced wider than troffers
                float intensity = tubeIntensity * k * boost;
                float range = s.src.dome || s.src.strip ? tubeRange + extra * 1.3f : tubeRange;
                // Domes light the whole hemisphere: a near-180 cone with a wide soft falloff, so no
                // cone edge is drawn across tall walls. Strips are a little narrower.
                float angle = s.src.dome ? 170f : s.src.strip ? 160f : 150f, inner = s.src.dome ? 40f : 60f;
                Color col = s.src.strip ? stripColor : tubeColor;

                // Ceiling lights only ever light their own room (rendering layer), so no light can pass
                // through a wall whatever the shadow resolution. Shadows ease in for the nearest lights and
                // only add detail (furniture, pillars). Doorway light comes from the doorway lights below.
                s.main.intensity = intensity;
                s.main.enabled = k > 0.002f;
                s.main.range = range; s.main.spotAngle = angle; s.main.innerSpotAngle = inner; s.main.color = col;
                s.shadowK = Mathf.MoveTowards(s.shadowK, s.wantShadow ? 1f : 0f, dt / Mathf.Max(0.01f, shadowFade));
                float st = Mathf.SmoothStep(0f, 1f, s.shadowK);
                var sm = st > 0.005f && s.main.enabled ? LightShadows.Soft : LightShadows.None;
                if (s.main.shadows != sm) s.main.shadows = sm;
                s.main.shadowStrength = st * 0.9f;
                s.main.shadowBias = s.src.dome || s.src.strip ? 0.08f : 0.03f;
                s.main.shadowNormalBias = 0.15f;
                if (sm != LightShadows.None) shadowed++;
                if (s.spill.enabled) { s.spill.enabled = false; s.spill.shadows = LightShadows.None; }
                if (s.main.enabled) enabled++;

                // Bounce fill only for the nearest fixtures, fading out before its distance limit.
                float bEdge = Mathf.Min(bounceDistance, _bounceEdge);
                float bk = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(bEdge - 3f, bEdge, d));
                if (bounces >= bounceCount + 8) bk = 0f;      // safety cap only
                s.bounce.enabled = k > 0.002f && bounceIntensity > 0f && bk > 0.01f;
                if (s.bounce.enabled) { bounces++; enabled++; }
                s.bounce.intensity = bounceIntensity * k * bk * (s.src.dome ? 1.6f : 1f);
                s.bounce.range = s.src.dome || s.src.strip ? bounceRange * 1.6f : bounceRange;
                s.bounce.color = s.src.strip ? Color.Lerp(stripColor, new Color(0.95f, 0.92f, 0.85f), 0.5f)
                                             : Color.Lerp(tubeColor, new Color(1f, 0.9f, 0.62f), 0.5f);
            }
            if (slots == _troffers) { EnabledLights = enabled; ShadowedLights = shadowed; }
            else { EnabledLights += enabled; ShadowedLights += shadowed; }
            return _active.Count;
        }

        void Release(Slot s)
        {
            s.busy = false; s.fade = 0f; s.target = 0f; s.shadowK = 0f; s.wantShadow = false; s.allLayers = false; s.spillW = 0f;
            s.main.enabled = false; s.main.shadows = LightShadows.None;
            if (s.spill) { s.spill.enabled = false; s.spill.shadows = LightShadows.None; }
            if (s.bounce) s.bounce.enabled = false;
        }

        // ---------------- Doorway lights ----------------

        [Header("Doorway light")]
        [Tooltip("Lights that carry a lit room's light a little way through each doorway into the next room.")]
        [Range(0, 48)] public int doorwayLights = 32;
        [Range(0f, 3f)] public float doorwayGain = 0.55f;
        public float doorwayDistance = 18f;

        readonly Dictionary<Vector3Int, List<Doorway>> _doors = new Dictionary<Vector3Int, List<Doorway>>();
        readonly List<Doorway> _doorCands = new List<Doorway>();
        readonly List<Light> _doorPool = new List<Light>();
        readonly List<float> _doorLevel = new List<float>();
        readonly Dictionary<Vector3, int> _doorSlot = new Dictionary<Vector3, int>();
        System.Comparison<Doorway> _doorByDist;

        /// <summary>Called by the streamer with the chunk's doorways (receiving side of each opening).</summary>
        public void RegisterDoorways(Vector3Int chunk, List<Doorway> doors) { _doors[chunk] = doors; }
        public void UnregisterDoorways(Vector3Int chunk) { _doors.Remove(chunk); }

        void EnsureDoorPool()
        {
            while (_doorPool.Count < doorwayLights)
            {
                var go = new GameObject("DoorLight_" + _doorPool.Count);
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = 150f; l.innerSpotAngle = 40f; l.range = 5f;
                l.shadows = LightShadows.None; l.color = Color.Lerp(tubeColor, new Color(1f, 0.92f, 0.75f), 0.3f);
                l.enabled = false;
                _doorPool.Add(l); _doorLevel.Add(0f);
            }
        }

        /// <summary>
        /// Light through openings without leaking through walls: each doorway near you gets a soft light
        /// placed in the receiving room, lighting only that room's layer, as bright as the lit fixtures
        /// just the other side of the opening (so it follows their power, flicker and faults). Rooms are
        /// rectangles, so a light inside one can never reach past its walls.
        /// </summary>
        void UpdateDoorways(Vector3 eye)
        {
            EnsureDoorPool();
            _doorCands.Clear();
            var center = WorldStreamer.ChunkOf(eye);
            int r = Mathf.CeilToInt(doorwayDistance / OfficeBiome.ChunkSize);
            float r2 = doorwayDistance * doorwayDistance;
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            for (int s = _minStorey; s <= _maxStorey; s++)
                if (_doors.TryGetValue(new Vector3Int(center.x + dx, s, center.y + dz), out var list))
                    foreach (var dw in list) if ((dw.position - eye).sqrMagnitude < r2) _doorCands.Add(dw);
            if (_doorByDist == null) _doorByDist = (a, b) => (a.position - _eye).sqrMagnitude.CompareTo((b.position - _eye).sqrMagnitude);
            _doorCands.Sort(_doorByDist);
            int n = Mathf.Min(_doorCands.Count, _doorPool.Count);
            float edge = _doorCands.Count > _doorPool.Count && n > 0 ? Vector3.Distance(_doorCands[n - 1].position, eye) : doorwayDistance;
            float now = Time.time, dt = Time.deltaTime;

            // Keep each doorway on the same pooled light while it stays in the set.
            _doorSlot.Clear();
            for (int i = 0; i < n; i++) _doorSlot[_doorCands[i].position] = i;
            for (int i = 0; i < _doorPool.Count; i++)
            {
                var l = _doorPool[i];
                if (i >= n) { _doorLevel[i] = 0f; l.enabled = false; continue; }
                var dw = _doorCands[i];
                // Brightness of the source side: lit fixtures within ~3.5 m behind the opening.
                float src = 0f;
                foreach (var c in _candidates)
                {
                    if (c.isLamp || (c.roomLayer & dw.layer & ~1u) != 0) continue;     // same room: not "the other side"
                    Vector3 rel = c.position - dw.position; rel.y = 0f;
                    float back = -Vector3.Dot(rel, dw.inward);
                    if (back < 0.05f) continue;
                    float dist = rel.magnitude;
                    if (dist > 3.6f) continue;
                    src += PowerLevel(c, now) * (1f - dist / 3.6f) * (c.dome || c.strip ? 1.4f : 1f);
                }
                float d = Vector3.Distance(dw.position, eye);
                float fade = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge - 3f, edge, d)))
                           * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(doorwayDistance - 4f, doorwayDistance, d)));
                float target = Mathf.Min(src, 2.5f) * doorwayGain * fade * dw.width;
                // Follow power changes instantly (flicker stays in sync); ease only small drift.
                _doorLevel[i] = target;
                l.enabled = target > 0.01f;
                if (!l.enabled) continue;
                l.transform.position = dw.position + dw.inward * 0.05f + Vector3.up * (dw.height - 0.3f);
                l.transform.rotation = Quaternion.LookRotation((dw.inward + Vector3.down * 0.9f).normalized);
                l.intensity = target;
                l.range = 4.5f;
                l.GetUniversalAdditionalLightData().renderingLayers = dw.layer & ~1u;
            }
        }

        // ---------------- Flicker ----------------

        /// <summary>0..1 multiplier from area power-up/down and faulty-tube stutter.</summary>
        public float PowerLevel(in LightSource src, float now)
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
    }
}
