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

        [Tooltip("How dark the shadows of real lights are (1 = black, lower lets some light through).")]
        [Range(0f, 1f)] public float shadowStrength = 1f;

        /// <summary>Most ceiling tubes that are real lights at once in grid mode (the F5 setting is clamped to this).</summary>
        public const int MaxRealTubes = 32;

        class Share
        {
            public LightSource src;
            public bool shadowed;           // its real light casts shadows (the nearest ones)
            public float w, target;         // real-light share now / wanted
            public float lastGrid = -1f;    // grid intensity last written (to detect changes)
            public int frame;
            public Slot slot;
        }

        readonly Dictionary<Vector3, Share> _shares = new Dictionary<Vector3, Share>();
        readonly List<LightGridMath.GridLight> _gridLights = new List<LightGridMath.GridLight>(512);
        readonly List<Vector3> _dropShares = new List<Vector3>();
        int _gridFrame;
        LightingMode _activeMode = (LightingMode)(-1);

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

        int _fixtureParams;
        bool _haveFixtureParams;

        void LateUpdateGrid(Vector3 eye, float now)
        {
            _gridFrame++;
            // Strength, reach and tall-room boost change every fixture's grid light: recompute the whole grid.
            int fp = System.HashCode.Combine(tubeIntensity, tubeRange, domeHeightBoost, lampIntensity, lampRange, spread);
            if (_haveFixtureParams && fp != _fixtureParams && grid) grid.MarkAllDirty();
            _fixtureParams = fp; _haveFixtureParams = true;
            float dt = Time.deltaTime;

            // Every registered light near the viewer, nearest first.
            _candidates.Clear();
            var center = WorldStreamer.ChunkOf(eye);
            int r = Mathf.CeilToInt(gridDistance / OfficeBiome.ChunkSize);
            float r2 = gridDistance * gridDistance;
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            for (int s = _minStorey; s <= _maxStorey; s++)
                if (_chunks.TryGetValue(new Vector3Int(center.x + dx, s, center.y + dz), out var cp))
                    foreach (var l in cp.lights)
                        if ((l.position - eye).sqrMagnitude < r2) _candidates.Add(l);
            if (_byDistance == null) _byDistance = (a, b) => (a.position - _eye).sqrMagnitude.CompareTo((b.position - _eye).sqrMagnitude);
            _candidates.Sort(_byDistance);

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
                if (counted == nShadow) shadowCutoff = Vector3.Distance(c.position, eye);
                if (counted == nReal) { cutoff = Vector3.Distance(c.position, eye); break; }
            }
            if (nShadow <= 0) shadowCutoff = -1f;
            if (nReal <= 0) cutoff = -1f;
            int tubes = 0, faulty = 0, lamps = 0;
            foreach (var c in _candidates)
            {
                bool created = false;
                if (!_shares.TryGetValue(c.position, out var sh)) { sh = new Share(); _shares[c.position] = sh; created = true; }
                sh.src = c; sh.frame = _gridFrame;
                float d = Vector3.Distance(c.position, eye);
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
                if (sh.lastGrid > 0f && grid) { Describe(sh.src, out var p, out _, out _, out var range, out _, out _, out _); grid.MarkDirty(p, range + 0.6f); }
                if (sh.slot != null) { Release(sh.slot); sh.slot = null; }
                _dropShares.Add(kv.Key);
            }
            foreach (var k in _dropShares) _shares.Remove(k);

            // Grid lights (with change tracking) and real lights.
            _gridLights.Clear();
            int enabled = 0, shadowed = 0;
            foreach (var c in _candidates)
            {
                var sh = _shares[c.position];
                Describe(c, out var pos, out var dir, out float intensity, out float range, out float outer, out float inner, out var col);
                // Power quantised so a steady light never re-dirties the grid.
                float power = Mathf.Round(Mathf.Clamp01(PowerLevel(c, now)) * 64f) / 64f;
                float gridShare = (1f - sh.w) * power;
                if (Mathf.Abs(gridShare - sh.lastGrid) > 0.003f)
                {
                    grid.MarkDirty(pos, range + 0.6f);
                    sh.lastGrid = gridShare;
                }
                var lin = (Vector3)(Vector4)col.linear * intensity;
                _gridLights.Add(new LightGridMath.GridLight
                {
                    pos = pos, range = range, dir = dir,
                    cosOuter = Mathf.Cos(outer * 0.5f * Mathf.Deg2Rad), cosInner = Mathf.Cos(inner * 0.5f * Mathf.Deg2Rad),
                    color = lin * gridShare, colorAll = lin * power, storey = c.chunk.y,
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
                float k = sh.w * Mathf.Clamp01(PowerLevel(c, now));
                L.enabled = k > 0.002f;
                L.intensity = intensity * k;
                L.range = range;
                L.color = col;
                if (!c.isLamp) { L.spotAngle = outer; L.innerSpotAngle = inner; }
                var sm = L.enabled && sh.shadowed ? LightShadows.Soft : LightShadows.None;
                if (L.shadows != sm) L.shadows = sm;
                L.shadowStrength = shadowStrength;
                L.shadowBias = c.dome || c.strip ? 0.08f : 0.03f;
                L.shadowNormalBias = 0.15f;
                if (L.enabled) { enabled++; if (sm != LightShadows.None) shadowed++; }
            }
            EnabledLights = enabled; ShadowedLights = shadowed;
            ActiveLights = enabled;
            if (grid) grid.Tick(eye, _gridLights);
        }
    }
}
