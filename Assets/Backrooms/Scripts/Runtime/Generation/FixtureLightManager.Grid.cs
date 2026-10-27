using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Generation
{
    /// <summary>
    /// Light grid mode (the default): the nearest fixtures are real URP lights with live shadows; every
    /// other powered fixture lights the <see cref="LightGrid"/>, which the Backrooms/Lit shader samples.
    ///  - Light adds up, so a fixture is split between the two: its real light carries share w, the grid
    ///    carries 1 - w. When it joins or leaves the nearest set, w eases over <see cref="handoverTime"/>,
    ///    so nothing counts twice and nothing pops. Only the grid around a changing fixture is recomputed.
    ///  - Real lights reach every rendering layer: their shadows (closed proxy boxes, see OfficeChunkBuilder)
    ///    stop them at walls, so light falls through doorways naturally.
    ///  - Faulty tubes within the light distance are always real lights, so their flicker is exact.
    ///  - Doorway lights and bounce lights are not used: the grid carries light through openings and
    ///    its bounce field lights ceilings and upper walls.
    /// </summary>
    public partial class FixtureLightManager
    {
        public enum LightingMode { LightGrid, RoomLayers }

        [Header("Lighting mode")]
        [Tooltip("LightGrid: nearest fixtures are real shadowed lights, the rest light a 3D grid (default).\n" +
                 "RoomLayers: the older pool of unshadowed lights kept inside their room by rendering layers.")]
        public LightingMode mode = LightingMode.LightGrid;
        public LightGrid grid;

        [Header("Light grid mode")]
        [Tooltip("Faulty tubes within the light distance are real lights (so their flicker is exact), up to this many.")]
        [Range(0, 16)] public int faultyRealLights = 6;
        [Tooltip("Nearest floor lamps that are real (shadowed) lights; the rest light the grid.")]
        [Range(0, 6)] public int realLamps = 1;
        [Tooltip("Seconds for a fixture's light to hand over between the grid and its real light.")]
        public float handoverTime = 0.6f;
        [Tooltip("Further fixtures (after the shadowed ones) that are also real lights, but without shadows: crisp pools on distant walls.\n" +
                 "Walls still stop them (the shader tests line of sight), so only furniture shadows are missing, far away.")]
        [Range(0, 120)] public int farRealLights = 56;
        [Tooltip("Fixtures within this distance feed the grid (the grid covers about 38 m around you).")]
        public float gridDistance = 40f;
        [Tooltip("Bounce light and haze follow faulty tubes' power smoothed over ~0.35 s (their direct light still flickers exactly).")]
        public bool smoothBounce = true;

        [Tooltip("How dark the shadows of real lights are (1 = black, lower lets some light through).")]
        [Range(0f, 1f)] public float shadowStrength = 1f;

        /// <summary>Most ceiling tubes that are real lights at once in grid mode (the F5 setting is clamped to this).</summary>
        public const int MaxRealTubes = 32;

        [Range(0f, 1f)] public float emitterSize = 1f;

        [Tooltip("The cone (degrees) a real ceiling light's shadow map covers. URP spreads a spot light's map over its whole cone, " +
                 "and the fixtures' soft falloff runs to 150-179 degrees, where a map's texels are 8-60 cm on the floor. Everything " +
                 "a fixture reaches on the floor lies within 120 degrees, where they are about 1-2 cm. The light grid draws each real " +
                 "light's weak outer ring past it (the per-pixel distant light must be on; otherwise the light's own cone is used). " +
                 "0 = the light's own cone.")]
        [Range(0f, 150f)] public float shadowCone = 120f;
        static readonly int ShadowConeId = Shader.PropertyToID("_BRShadowCone");

        class Share
        {
            public LightSource src;
            public bool shadowed;           // its real light casts shadows (the nearest ones)
            public float w, target;         // real-light share now / wanted
            public float lastGrid = -1f;    // grid intensity last written (to detect changes)
            public float smoothPower = -1f; // power smoothed over a fraction of a second (bounce and haze, see GridLight.padding)
            public int frame;
            public Slot slot;
        }

        readonly Dictionary<Vector3, Share> _shares = new Dictionary<Vector3, Share>();
        readonly List<LightGridMath.GridLight> _gridLights = new List<LightGridMath.GridLight>(512);
        readonly List<Vector3> _dropShares = new List<Vector3>();
        static readonly System.Comparison<LightGridMath.GridLight> GridOrder = (a, b) =>
        {
            int x = a.pos.x.CompareTo(b.pos.x); if (x != 0) return x;
            int z = a.pos.z.CompareTo(b.pos.z); return z != 0 ? z : a.pos.y.CompareTo(b.pos.y);
        };
        int _gridFrame;
        LightingMode _activeMode = (LightingMode)(-1);
        System.Comparison<LightSource> _byPoolDistance;

        bool UseGrid => mode == LightingMode.LightGrid && LightGrid.Supported;

        void EnsureGrid()
        {
            if (grid) return;
            grid = GetComponentInChildren<LightGrid>(true);
            if (grid) return;
            // Built inactive so its fields are set before OnEnable runs.
            var go = new GameObject("LightGrid");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            grid = go.AddComponent<LightGrid>();
            grid.compute = Resources.Load<ComputeShader>("LightGrid");
            grid.streamer = FindAnyObjectByType<WorldStreamer>();
            go.SetActive(true);
        }

        /// <summary>Switches between modes: releases every pooled light and turns the grid on or off.</summary>
        void ApplyMode()
        {
            if (_activeMode == mode) return;
            _activeMode = mode;
            foreach (var s in _troffers) Release(s);
            foreach (var s in _lamps) Release(s);
            foreach (var l in _doorPool) if (l) l.enabled = false;
            foreach (var sh in _shares.Values) sh.slot = null;
            _shares.Clear();
            if (UseGrid) { EnsureGrid(); if (grid && !grid.enabled) grid.enabled = true; }
            else if (grid && grid.enabled) grid.enabled = false;
            if (!UseGrid) Shader.SetGlobalVector(ShadowConeId, Vector4.zero);
            // In grid mode walls stop real lights in the shader; their shadow casters would only add artifacts.
            OfficeChunkBuilder.WallShadows = !UseGrid;
        }

        /// <summary>How a fixture's real light is set up (and what the grid draws for it at full share).</summary>
        void Describe(in LightSource src, out Vector3 pos, out Vector3 dir, out float intensity, out float range, out float outer, out float inner, out Color col)
        {
            if (src.isLamp)
            {
                pos = src.position; dir = Vector3.zero; intensity = lampIntensity; range = lampRange; outer = inner = 0f; col = lampColor;
                return;
            }
            float extra = src.ceiling - OfficeBiome.CeilingHeight;
            float boost = src.dome || src.strip ? 1f + extra * domeHeightBoost : 1f;
            if (src.dome) boost *= 1.6f;
            pos = src.position + Vector3.down * 0.06f;
            dir = Vector3.down;
            intensity = tubeIntensity * boost;
            range = src.dome || src.strip ? tubeRange + extra * 1.3f : tubeRange;
            TrofferCone(spread, src.dome, src.strip, out outer, out inner);
            col = src.strip ? stripColor : tubeColor;
        }

        // ---------------- Far crawl-hole sites ----------------
        // A crawl hole whose partner is far away (beyond the main grid's window) gets a second light grid centred on the
        // partner (see CrawlPortalSystem.TryGetRemoteSite): the portal view of that site is lit by it, and when the player
        // comes out there the two grids swap roles, so the light is already complete on arrival.
        [Tooltip("The grid around a far crawl hole's partner (created on demand, switched off when no far hole is near).")]
        public LightGrid siteGrid;
        [Tooltip("Fixtures within this distance of a far site feed the site grid.")]
        public float siteGridDistance = 30f;
        [Tooltip("Seconds without a far site before the site grid is switched off (its GPU memory released).")]
        public float siteGridLinger = 20f;
        bool _haveSite;
        Vector3 _site;
        float _siteLastUsed = -999f;
        readonly HashSet<Vector3Int> _gathered = new HashSet<Vector3Int>();

        void EnsureSiteGrid()
        {
            if (siteGrid) { if (!siteGrid.enabled) siteGrid.enabled = true; return; }
            var go = new GameObject("LightGrid (far site)");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            siteGrid = go.AddComponent<LightGrid>();
            siteGrid.compute = Instantiate(Resources.Load<ComputeShader>("LightGrid"));   // its own parameter block
            siteGrid.streamer = grid ? grid.streamer : FindAnyObjectByType<WorldStreamer>();
            siteGrid.bindGlobals = false;
            go.SetActive(true);
        }

        static void CopyGridSettings(LightGrid from, LightGrid to)
        {
            to.directGain = from.directGain; to.bounceGain = from.bounceGain; to.floorAlbedo = from.floorAlbedo;
            to.bounceSpread = from.bounceSpread; to.bounceSteps = from.bounceSteps; to.wrap = from.wrap; to.smoothing = from.smoothing;
            to.contrast = from.contrast; to.roomFill = from.roomFill; to.perPixelDirect = from.perPixelDirect;
            to.exactDistance = from.exactDistance; to.normalOffset = from.normalOffset; to.contactOcclusion = from.contactOcclusion;
            to.texelBudget = from.texelBudget;
            to.tracedBounce = from.tracedBounce; to.colourBleed = from.colourBleed; to.multiBounce = from.multiBounce;
            to.raysPerThread = from.raysPerThread; to.hysteresis = from.hysteresis; to.haze = from.haze;
        }

        void MarkGridDirty(Vector3 p, float radius)
        {
            if (grid) grid.MarkDirty(p, radius);
            if (siteGrid && siteGrid.enabled) siteGrid.MarkDirty(p, radius);
        }

        /// <summary>
        /// After a trip to a far site: if the site grid already covers where the player now is, it becomes the main grid
        /// (already filled, so nothing relights) and the old main grid stays behind as the site grid for the way back.
        /// </summary>
        public bool SwapGridsIfSiteCovers(Vector3 player, Vector3 left)
        {
            if (!grid || !siteGrid || !siteGrid.enabled || !siteGrid.Ready || !siteGrid.Covers(player, 6f)) return false;
            (grid, siteGrid) = (siteGrid, grid);
            grid.bindGlobals = true; grid.siteCentre = null;
            siteGrid.bindGlobals = false; siteGrid.siteCentre = left;
            grid.BindGlobals();
            _site = left; _haveSite = true; _siteLastUsed = Time.time;
            return true;
        }

        int _fixtureParams;
        bool _haveFixtureParams;
        bool _haveAhead;
        Vector3 _ahead;

        /// <summary>
        /// Distance used to choose real lights: to the player, or to where they are about to come out of a crawl hole
        /// (whichever is nearer), so the far side of a hole is already lit by real, shadowed lights before they arrive.
        /// </summary>
        float PoolDistance(Vector3 p)
        {
            float d = Vector3.Distance(p, _eye);
            return _haveAhead ? Mathf.Min(d, Vector3.Distance(p, _ahead)) : d;
        }

        void Gather(Vector3 centre, float distance)
        {
            var cc = WorldStreamer.ChunkOf(centre);
            int r = Mathf.CeilToInt(distance / OfficeBiome.ChunkSize);
            float r2 = distance * distance;
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            for (int s = _minStorey; s <= _maxStorey; s++)
            {
                var key = new Vector3Int(cc.x + dx, s, cc.y + dz);
                if (!_gathered.Add(key) || !_chunks.TryGetValue(key, out var cp)) continue;
                foreach (var l in cp.lights)
                    if ((l.position - centre).sqrMagnitude < r2) _candidates.Add(l);
            }
        }

        void LateUpdateGrid(Vector3 eye, float now)
        {
            _gridFrame++;
            // Strength, reach and tall-room boost change every fixture's grid light: recompute the whole grid.
            int fp = System.HashCode.Combine(tubeIntensity, tubeRange, domeHeightBoost, lampIntensity, lampRange, spread);
            if (_haveFixtureParams && fp != _fixtureParams && grid) { grid.MarkAllDirty(); if (siteGrid && siteGrid.enabled) siteGrid.MarkAllDirty(); }
            _fixtureParams = fp; _haveFixtureParams = true;
            float dt = Time.deltaTime;
            // A far crawl-hole site: its own grid, and its fixtures join the candidates (so its real lights can be chosen too).
            _haveSite = Interaction.CrawlPortalSystem.TryGetRemoteSite(out _site) && grid && !grid.Covers(_site, 12f);
            if (_haveSite) { EnsureSiteGrid(); _siteLastUsed = Time.time; }
            else if (siteGrid && siteGrid.enabled && Time.time - _siteLastUsed > siteGridLinger) siteGrid.enabled = false;
            _haveAhead = Interaction.CrawlPortalSystem.TryGetLookahead(out _ahead) &&
                         ((_ahead - eye).sqrMagnitude < gridDistance * gridDistance || (_haveSite && (_ahead - _site).sqrMagnitude < siteGridDistance * siteGridDistance));

            // Every registered light near the viewer (and near a far site), nearest first. Each chunk once.
            _candidates.Clear();
            _gathered.Clear();
            Gather(eye, gridDistance);
            if (_haveSite) Gather(_site, siteGridDistance);
            if (_byPoolDistance == null) _byPoolDistance = (a, b) => PoolDistance(a.position).CompareTo(PoolDistance(b.position));
            _candidates.Sort(_byPoolDistance);

            // Which fixtures are real lights: the nearest tubes (with hysteresis), faulty tubes, nearest lamps.
            // Every real light casts shadows, so the count stays within the 4096 shadow atlas: 64 spot maps at
            // the medium tier, less 6 per lamp cube, the faulty tubes and the hysteresis margin. (At the high
            // tier URP lowers the resolution itself to fit.)
            const int atlasMaps = 64;
            int capacity = atlasMaps - faultyRealLights - realLamps * 6 - 4;
            int nShadow = Mathf.Clamp(Mathf.Min(Mathf.Min(shadowedTroffers, MaxRealTubes), capacity), 0, _troffers.Count - faultyRealLights - 4);
            int nReal = Mathf.Clamp(nShadow + farRealLights, 0, _troffers.Count - faultyRealLights - 4);       // shadowed + far (unshadowed) real tubes
            float cutoff = float.MaxValue, shadowCutoff = float.MaxValue;
            int counted = 0;
            foreach (var c in _candidates)
            {
                if (c.isLamp || c.faultyId >= 0) continue;
                counted++;
                if (counted == nShadow) shadowCutoff = PoolDistance(c.position);
                if (counted == nReal) { cutoff = PoolDistance(c.position); break; }
            }
            if (nShadow <= 0) shadowCutoff = -1f;
            if (nReal <= 0) cutoff = -1f;
            int tubes = 0, faulty = 0, lamps = 0;
            foreach (var c in _candidates)
            {
                bool created = false;
                if (!_shares.TryGetValue(c.position, out var sh)) { sh = new Share(); _shares[c.position] = sh; created = true; }
                sh.src = c; sh.frame = _gridFrame;
                float d = PoolDistance(c.position);
                bool real;
                if (c.isLamp) real = lamps < realLamps && d <= maxDistance;
                else if (c.faultyId >= 0) real = faulty < faultyRealLights && d <= maxDistance;
                else
                {
                    bool was = sh.target > 0.5f;
                    real = was ? d * (1f - shadowHysteresis) <= cutoff : d <= cutoff;
                    if (real && tubes >= nReal + 4) real = false;          // hard cap (slots)
                    sh.shadowed = sh.shadowed ? d * (1f - shadowHysteresis) <= shadowCutoff : d <= shadowCutoff;
                }
                if (c.isLamp || c.faultyId >= 0) sh.shadowed = true;
                if (real) { if (c.isLamp) lamps++; else if (c.faultyId >= 0) faulty++; else tubes++; }
                sh.target = real ? 1f : 0f;
                // Something that only just loaded starts where it should be: nothing to hand over yet.
                if (created) sh.w = sh.target;
                sh.w = Mathf.MoveTowards(sh.w, sh.target, dt / Mathf.Max(0.05f, handoverTime));
            }

            // Lights that left the area: clear their grid share and free their slot.
            _dropShares.Clear();
            foreach (var kv in _shares)
            {
                if (kv.Value.frame == _gridFrame) continue;
                var sh = kv.Value;
                if (sh.lastGrid > 0f && grid) { Describe(sh.src, out var p, out _, out _, out var range, out _, out _, out _); MarkGridDirty(p, range + 0.6f); }
                if (sh.slot != null) { Release(sh.slot); sh.slot = null; }
                _dropShares.Add(kv.Key);
            }
            foreach (var k in _dropShares) _shares.Remove(k);

            // Real ceiling lights' shadow maps cover a narrower cone than their light; the shader hands the ring outside it
            // (with a 2 degree margin inside the map) to the grid's per-pixel lights.
            bool narrow = shadowCone > 1f && grid && grid.perPixelDirect;
            Shader.SetGlobalVector(ShadowConeId, narrow
                ? new Vector4(Mathf.Cos((shadowCone * 0.5f - 2f) * Mathf.Deg2Rad), Mathf.Tan(shadowCone * 0.5f * Mathf.Deg2Rad), 1f, 0f)
                : Vector4.zero);

            // Grid lights (with change tracking) and real lights.
            _gridLights.Clear();
            int enabled = 0, shadowed = 0;
            int shadowMaps = 0;
            foreach (var active in _shares.Values)
                if (active.slot != null && active.slot.shadowK > 0.001f) shadowMaps += active.src.isLamp ? 6 : 1;
            foreach (var c in _candidates)
            {
                var sh = _shares[c.position];
                Describe(c, out var pos, out var dir, out float intensity, out float range, out float outer, out float inner, out var col);
                // Quantise distant power fades, but retain the exact waveform of a faulty tube.
                float rawPower = Mathf.Clamp01(PowerLevel(c, now));
                float power = c.faultyId >= 0 ? rawPower : Mathf.Round(rawPower * 64f) / 64f;
                float gridShare = (1f - sh.w) * power;
                // With the per-pixel split the texture only holds each light's weak far part: while a light is handing
                // over or powering up, redraw it in coarse steps (plus once when it settles), not every frame.
                bool changing = sh.w != sh.target || (power > 0.001f && power < 0.999f);
                float step = grid && grid.CoarseUpdates && changing ? 0.08f : 0.003f;
                if (Mathf.Abs(gridShare - sh.lastGrid) > step)
                {
                    MarkGridDirty(pos, range + 0.6f);
                    sh.lastGrid = gridShare;
                }
                var lin = (Vector3)(Vector4)col.linear * intensity;
                // Bounce and haze follow a stuttering tube's power smoothed (they update in slices and boxes, so following
                // every stutter made soft light pop in patches in the dark); its direct light keeps the exact waveform.
                sh.smoothPower = sh.smoothPower < 0f || !smoothBounce ? power : Mathf.Lerp(sh.smoothPower, power, 1f - Mathf.Exp(-dt / 0.35f));
                float bounceRatio = power > 0.001f ? Mathf.Round((Mathf.Clamp(sh.smoothPower / power, 0f, 8f) - 1f) * 64f) / 64f : 0f;
                _gridLights.Add(new LightGridMath.GridLight
                {
                    pos = pos, range = range, dir = dir,
                    cosOuter = Mathf.Cos(outer * 0.5f * Mathf.Deg2Rad), cosInner = Mathf.Cos(inner * 0.5f * Mathf.Deg2Rad),
                    color = lin * gridShare, colorAll = lin * power, storey = c.chunk.y,
                    halfSize = c.isLamp ? Vector2.zero : (c.strip ? new Vector2(0.59f, 0.055f) : c.dome ? new Vector2(0.16f, 0.16f) : new Vector2(0.285f, 0.285f)) * emitterSize,
                    padding = new Vector2(bounceRatio, 0f),
                });

                // Real light for the real share.
                if (sh.w <= 0.001f)
                {
                    if (sh.slot != null) { Release(sh.slot); sh.slot = null; }
                    continue;
                }
                var pool = c.isLamp ? _lamps : _troffers;
                if (sh.slot == null)
                {
                    foreach (var s in pool) if (!s.busy) { sh.slot = s; break; }
                    if (sh.slot == null) continue;                          // pool exhausted (should not happen)
                    var slot = sh.slot;
                    slot.busy = true; slot.src = c; slot.sourcePos = c.position;
                    slot.main.transform.position = pos;
                    slot.main.GetUniversalAdditionalLightData().renderingLayers = 0xFFFFFFFFu;
                    if (slot.bounce) slot.bounce.enabled = false;
                    if (slot.spill) slot.spill.enabled = false;
                }
                var L = sh.slot.main;
                float k = sh.w * power;
                L.enabled = k > 0.002f;
                L.intensity = intensity * k * Mains;                   // the building's supply sags now and then
                L.range = range;
                L.color = col;
                if (!c.isLamp)
                {
                    L.spotAngle = narrow ? Mathf.Min(outer, shadowCone) : outer;   // the shadow map's cone (see shadowCone)
                    L.innerSpotAngle = Mathf.Min(inner, L.spotAngle);
                }
                // Keep outgoing shadow maps until their strength reaches zero; only then give that
                // atlas capacity to an incoming map. Fast movement cannot oversubscribe the atlas.
                int maps = c.isLamp ? 6 : 1;
                float distanceFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shadowDistance * 0.65f, shadowDistance, PoolDistance(pos)));
                float wantedShadow = L.enabled && sh.shadowed && sh.target > 0f ? distanceFade : 0f;
                bool hadShadow = sh.slot.shadowK > 0.001f;
                if (!hadShadow && shadowMaps + maps > atlasMaps) wantedShadow = 0f;
                sh.slot.shadowK = Mathf.MoveTowards(sh.slot.shadowK, wantedShadow, dt / Mathf.Max(0.01f, shadowFade));
                bool hasShadow = sh.slot.shadowK > 0.001f;
                if (hasShadow != hadShadow) shadowMaps += hasShadow ? maps : -maps;
                var sm = L.enabled && hasShadow ? LightShadows.Soft : LightShadows.None;
                if (L.shadows != sm) L.shadows = sm;
                L.shadowStrength = shadowStrength * Mathf.SmoothStep(0f, 1f, sh.slot.shadowK);
                L.shadowBias = c.dome || c.strip ? 0.08f : 0.03f;
                L.shadowNormalBias = 0.15f;
                if (L.enabled) { enabled++; if (sm != LightShadows.None) shadowed++; }
            }
            EnabledLights = enabled; ShadowedLights = shadowed;
            ActiveLights = enabled;
            _gridLights.Sort(GridOrder); // distance rank changes must not invalidate unchanged full-power bounce
            if (grid) grid.Tick(eye, _gridLights);
            if (_haveSite && siteGrid && siteGrid.enabled && grid)
            {
                CopyGridSettings(grid, siteGrid);
                siteGrid.siteCentre = _site;
                siteGrid.Tick(_site, _gridLights);
            }
        }
    }
}
