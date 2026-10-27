using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Backrooms.Generation;

namespace Backrooms.Camcorder
{
    /// <summary>Marks a lighting look field: copied by the LOOK presets, "my look" and reset (not quality or speed settings).</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class LookAttribute : Attribute { }

    /// <summary>Performance and quality options (F5 > GRAPHICS) and the lighting look (F5 > LIGHT). Saved separately from the camcorder look.</summary>
    [Serializable]
    public class GraphicsOptions
    {
        public bool vSync = false;
        public int frameCap = 60;             // frames a second at most (0 = unlimited); with vsync, every Nth refresh nearest it
        public float renderScale = 1f;
        public bool matchRecording = true;    // camcorder look on: draw at about 1.35 x the recording's lines (no finer than renderScale)
        public int shadowLights = 16;         // nearest lights that cast shadows (the biggest cost: each redraws the geometry around it)
        public float shadowDistance = 18f;    // shadows fade out smoothly toward this distance
        public int shadowResolution = 1;      // 0 low, 1 medium, 2 high
        public float lightDistance = 36f;     // fixtures lit out to here (fades into the fog)
        public int bounceLights = 40;         // nearest fixtures with soft bounce fill
        public bool ambientOcclusion = true;
        public bool lightGrid = true;         // far fixtures light a 3D grid (off: older room-layer lights)
        public bool antiAliasing = true;

        // F5 > LIGHTING (multipliers are relative to the scene's own light settings)
        [Look] public float lightStrength = 1f;      // all fixtures
        [Look] public float lightReach = 1f;         // how far each fixture's light travels
        [Look] public float tallBoost = 0.15f;        // extra output for fixtures hung high above the floor
        [Look] public float fillLight = 1f;          // soft bounced light from lit floors
        [Look] public float softness = 0.15f;        // wrap: how far light bends round surfaces facing away
        [Look] public float poolSmoothing = 0.5f;   // rounds off boxy light pools of far fixtures
        [Look] public float darkSpots = 0f;          // makes weakly lit areas darker than the light says (0 = as the light falls)
        [Look] public float shadowDarkness = 1f;     // real-light shadows
        public int farLights = 0;             // extra real (unshadowed) lights beyond the shadowed ones; 0 when per-pixel distant light is on
        public bool perPixelFar = true;       // distant fixtures are lit exactly per pixel (no visible grid texels)
        public float exactLightRadius = 4.5f; // ...on surfaces within this distance of each fixture; its weak far light comes from the grid. 0 = everywhere
        public int version;                   // settings format (see Load)
        [Look] public float flicker = 0.35f;         // faulty tubes: how often and how deep they stutter (1 = the original amount)
        [Look] public float roomFill = 0.6f;         // bounced light that evens out walls (the older bounce field only)
        [Look] public float fixtureSpread = 0.8f;    // how broadly ceiling fixtures throw light (0 = lamp-like cone, 1 = diffuse troffer)
        [Look] public float contactOcclusion = 0.65f;
        public bool indoorReflections = true;
        [Look] public float panelSize = 1f;           // finite emitter size relative to the visible fitting

        // F5 > LIGHT, the look (2026-10-26)
        [Look] public float colourTemp = 4500f;      // kelvin: every fixture's colour (lamps and strips shift with the tubes); 4500 = as built
        [Look] public float colourTint = 0f;         // -1 magenta .. +1 green (tired fluorescent tubes run green)
        [Look] public float panelGlow = 1f;          // brightness of the fittings themselves as you see them (not the light they throw)
        [Look] public float colourBleed = 1f;        // how much surfaces tint the light they bounce (0 = grey bounce, 1 = their own colour)
        [Look] public float multiBounce = 1f;        // traced bounce: how much of the bounced light bounces again (1 = all of it, as in a real room)
        [Look] public float shadowSoftness = 1f;     // soft shadows that harden where things touch (0 = the plain soft filter)
        [Look] public float contactShadows = 0.6f;   // short screen-space shadows where things touch (their own depth pass)
        [Look] public float haze = 0.25f;            // thin haze in the air, lit by the lights (0 = clear)
        [Look] public float poolSun = 1f, poolSky = 1f, poolBounce = 1f, poolWarmth = 0f;   // the Poolrooms (their own light)
        public bool areaLights = true;               // ceiling panels lit as rectangles (a method, not part of a look)
        public bool tracedBounce = true;             // bounce light traced through the rooms (off: the older bounce field)
        public int lookPreset = 0;                   // the LOOK preset last applied (-1: changed since, -2: my look)

        static System.Reflection.FieldInfo[] s_lookFields;
        static System.Reflection.FieldInfo[] LookFields => s_lookFields ??= Array.FindAll(typeof(GraphicsOptions).GetFields(),
            f => Attribute.IsDefined(f, typeof(LookAttribute)));

        /// <summary>Copies every look field (not quality settings) from another set of options.</summary>
        public void CopyLook(GraphicsOptions from) { foreach (var f in LookFields) f.SetValue(this, f.GetValue(from)); }

        public void ResetLighting()
        {
            var d = new GraphicsOptions();
            CopyLook(d);
            indoorReflections = d.indoorReflections; farLights = d.farLights; exactLightRadius = d.exactLightRadius;
            areaLights = d.areaLights; tracedBounce = d.tracedBounce; lookPreset = 0;
        }
        /// <summary>Evenly lit and pale, like the found-footage references: little contrast, a lot of fill, soft shadows.</summary>
        public void ApplyEvenLighting() => ApplyLook(1);

        public static readonly string[] LookNames = { "STILL RECORDING", "EVEN & PALE", "DIM & MOODY", "HARSH TUBES", "WARM & OLD" };
        public static readonly string[] LookTips =
        {
            "The game's own look: fluorescent panels, soft bounced light, deep corners, a touch of haze.",
            "Evenly lit and pale, like the found-footage references: little contrast, a lot of fill, soft shadows.",
            "Dimmer pools with dark gaps between them, heavier haze, warm-green old tubes.",
            "Cold, hard, slightly green office light: tight bright pools, crisp shadows, little haze.",
            "Warm and dusty: yellowed tubes, a lot of warm bounce off the carpet and walls, thick air.",
        };

        /// <summary>A built-in look: the defaults with a few changes.</summary>
        public void ApplyLook(int i)
        {
            CopyLook(new GraphicsOptions());
            switch (i)
            {
                case 1:
                    lightStrength = 1.35f; lightReach = 1.3f; tallBoost = 0.3f; fillLight = 2f; softness = 0.6f;
                    poolSmoothing = 0.8f; darkSpots = 0f; shadowDarkness = 0.6f; fixtureSpread = 1f; roomFill = 1f;
                    multiBounce = 0.9f; shadowSoftness = 1.4f; contactShadows = 0.35f; haze = 0.15f; colourTemp = 4800f;
                    break;
                case 2:
                    lightStrength = 0.75f; lightReach = 0.85f; darkSpots = 0.7f; fillLight = 0.75f; roomFill = 0.35f;
                    shadowSoftness = 1.2f; contactShadows = 0.8f; haze = 0.55f; colourTemp = 3700f; colourTint = 0.2f; panelGlow = 0.85f;
                    multiBounce = 0.55f;
                    break;
                case 3:
                    lightStrength = 1.15f; fixtureSpread = 0.45f; darkSpots = 0.5f; fillLight = 0.8f; shadowSoftness = 0.45f;
                    contactShadows = 0.85f; haze = 0.1f; colourTemp = 5600f; colourTint = 0.35f; panelGlow = 1.35f; softness = 0.05f;
                    break;
                case 4:
                    lightStrength = 0.95f; fillLight = 1.3f; colourBleed = 1.25f; multiBounce = 0.85f; haze = 0.45f;
                    colourTemp = 3500f; colourTint = 0.08f; darkSpots = 0.3f; roomFill = 0.75f; shadowSoftness = 1.3f;
                    break;
            }
            lookPreset = i;
        }

        const string MyLookKey = "Backrooms.MyLook.v1";
        public static bool HasMyLook => PlayerPrefs.HasKey(MyLookKey);
        /// <summary>Keeps the current look (every look field) as "my look".</summary>
        public void SaveMyLook() { PlayerPrefs.SetString(MyLookKey, JsonUtility.ToJson(this)); PlayerPrefs.Save(); }
        /// <summary>Back to the look kept with <see cref="SaveMyLook"/> (quality settings are not touched).</summary>
        public bool LoadMyLook()
        {
            var j = PlayerPrefs.GetString(MyLookKey, "");
            if (string.IsNullOrEmpty(j)) return false;
            try { CopyLook(JsonUtility.FromJson<GraphicsOptions>(j)); lookPreset = -2; return true; }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not load my look: " + e.Message); return false; }
        }

        /// <summary>Colour of a fixture after the look's colour temperature and tint, with its brightness kept.</summary>
        public static Color ShiftColour(Color c, float kelvin, float tint)
        {
            var s = ColourShift(kelvin, tint);
            return new Color(c.r * s.r, c.g * s.g, c.b * s.b, c.a);
        }

        /// <summary>The look's colour shift as a multiplier (luminance 1; white at 4500 K and no tint).</summary>
        public static Color ColourShift(float kelvin, float tint)
        {
            var k = Mathf.CorrelatedColorTemperatureToRGB(Mathf.Clamp(kelvin, 1500f, 12000f));
            var b = Mathf.CorrelatedColorTemperatureToRGB(4500f);
            var s = new Color(k.r / b.r, k.g / b.g, k.b / b.b);
            tint = Mathf.Clamp(tint, -1f, 1f);
            s = new Color(s.r * (1f - 0.08f * tint), s.g * (1f + 0.12f * tint), s.b * (1f - 0.1f * tint));
            float lum = 0.2126f * s.r + 0.7152f * s.g + 0.0722f * s.b;
            return s * (1f / Mathf.Max(lum, 1e-3f));
        }

        const string Key = "Backrooms.Graphics.v1";
        const int CurrentVersion = 4;
        public static GraphicsOptions Load()
        {
            try
            {
                var j = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(j))
                {
                    var o = JsonUtility.FromJson<GraphicsOptions>(j);
                    if (o.version < 2)
                    {
                        // Version 2 (60 fps pass): more than ~16 shadowed lights and 20 m of shadows cost a lot of frame time
                        // for detail you cannot see; per-pixel distant light became exact near, grid texture far.
                        o.shadowLights = Mathf.Min(o.shadowLights, 16);
                        o.shadowDistance = Mathf.Min(o.shadowDistance, 20f);
                        o.exactLightRadius = 4.5f;
                    }
                    if (o.version < 3 && Mathf.Approximately(o.darkSpots, 0.35f) && Mathf.Approximately(o.multiBounce, 0.7f))
                    {
                        // Version 3: light as it falls. Every surface passes on its share of the light (multiple bounces in
                        // full: a room with light walls is evenly lit, as in the reference footage), and dim areas are not
                        // pushed darker. Only for players who had not changed these two.
                        o.darkSpots = 0f; o.multiBounce = 1f;
                    }
                    if (o.version < 4 && o.frameCap == 0)
                    {
                        // Version 4: a 60 fps limit. Unlimited (vsync at a 240 Hz display is no limit) ran the graphics card
                        // flat out for frames nobody sees, and the fans loud.
                        o.frameCap = 60;
                    }
                    o.version = CurrentVersion;
                    return o;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not load graphics options: " + e.Message); }
            return new GraphicsOptions { version = CurrentVersion };
        }
        public void Save() { PlayerPrefs.SetString(Key, JsonUtility.ToJson(this)); PlayerPrefs.Save(); }

        public static readonly string[] PresetNames = { "Low", "Medium", "High", "Ultra" };
        public void ApplyPreset(int i)
        {
            shadowLights = new[] { 8, 12, 16, 24 }[i];
            shadowDistance = new[] { 12f, 15f, 18f, 24f }[i];
            exactLightRadius = new[] { 3f, 4f, 4.5f, 6f }[i];
            shadowResolution = new[] { 0, 1, 1, 2 }[i];
            lightDistance = new[] { 26f, 32f, 36f, 40f }[i];
            bounceLights = new[] { 16, 28, 40, 48 }[i];
            ambientOcclusion = i >= 1;
            antiAliasing = true;
            renderScale = i == 0 ? 0.8f : 1f;
        }
    }

    /// <summary>Applies <see cref="GraphicsOptions"/> every frame; restores project assets it touched on exit.</summary>
    public class GraphicsController : MonoBehaviour
    {
        public static GraphicsController Instance { get; private set; }
        public GraphicsOptions Options { get; private set; }

        FixtureLightManager _lights;
        int _appliedGrid = -1;
        Camera _cam;
        CamcorderRig _rig;
        float _origScale = -1f;
        float _baseTube, _baseLamp, _baseTubeRange, _baseLampRange;
        Color _baseTubeColour, _baseLampColour, _baseStripColour;
        bool _haveBase;
        PoolWorld _pool;
        float _poolSun, _poolBounce, _poolSky; Color _poolSunColour, _poolAmbient;
        Vector4 _poolApplied = new Vector4(-1, -1, -1, -1);
        static readonly int LookId = Shader.PropertyToID("_BRLook"), EmissionTintId = Shader.PropertyToID("_BREmissionTint");
        // SSAO on every renderer: the player's and the crawl holes' views (so the view through a hole has the same AO
        // as the room has once you are through it).
        readonly System.Collections.Generic.List<(ScriptableRendererFeature f, bool orig)> _ssao = new System.Collections.Generic.List<(ScriptableRendererFeature, bool)>();

        public static GraphicsController Ensure()
        {
            if (Instance) return Instance;
            return new GameObject("GraphicsController").AddComponent<GraphicsController>();
        }

        void Awake()
        {
            Instance = this;
            Options = GraphicsOptions.Load();
        }

        void Start()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp)
            {
                _origScale = urp.renderScale;
                var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f?.GetValue(urp) is ScriptableRendererData[] list)
                    foreach (var data in list)
                        if (data)
                            foreach (var feat in data.rendererFeatures)
                                if (feat && feat.GetType().Name.Contains("AmbientOcclusion")) { _ssao.Add((feat, feat.isActive)); PhysicalAO(feat, true); }
            }
        }

        // Screen-space AO stands in for bounced light that a nearby surface blocks. Direct light is the shadows' job, so it
        // must not darken it (the asset has 0.6: a dark, grainy band where a lit pillar met the ceiling). It also assumes the
        // blocker is black, while here it is usually a lit wall or ceiling reflecting its own light, and the traced bounce
        // already covers occlusion at room scale; so it is kept to a tight 0.2 m at a little over half strength: the soft
        // darkening where floors meet walls. Set at runtime (the renderer asset is not edited); restored on disable.
        // At half resolution (2026-10-27): at full 1080p it took about a fifth of the graphics card (12 samples and a
        // bilateral blur over every pixel) for a soft 0.2 m darkening the camcorder records at 480 lines anyway.
        const float AOIntensity = 0.6f, AODirect = 0f, AORadius = 0.2f;
        const bool AOHalfRes = true;
        readonly System.Collections.Generic.List<(object settings, float intensity, float direct, float radius, bool half)> _aoOrig =
            new System.Collections.Generic.List<(object, float, float, float, bool)>();
        void PhysicalAO(ScriptableRendererFeature feat, bool apply)
        {
            const System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            var st = feat.GetType().GetField("m_Settings", bf)?.GetValue(feat);
            if (st == null) return;
            var t = st.GetType();
            System.Reflection.FieldInfo fi = t.GetField("Intensity", bf), fd = t.GetField("DirectLightingStrength", bf), fr = t.GetField("Radius", bf), fh = t.GetField("Downsample", bf);
            if (fi == null || fd == null || fr == null) return;
            _aoOrig.Add((st, (float)fi.GetValue(st), (float)fd.GetValue(st), (float)fr.GetValue(st), fh != null && (bool)fh.GetValue(st)));
            fi.SetValue(st, AOIntensity); fd.SetValue(st, AODirect); fr.SetValue(st, AORadius);
            fh?.SetValue(st, AOHalfRes);
        }

        void OnDisable()
        {
            // Settings changed at runtime must not end up saved in the project's assets.
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp && _origScale > 0f) urp.renderScale = _origScale;
            foreach (var s in _ssao) if (s.f) s.f.SetActive(s.orig);
            foreach (var (st, i, d, r, h) in _aoOrig)
            {
                const System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                var t = st.GetType();
                t.GetField("Intensity", bf).SetValue(st, i); t.GetField("DirectLightingStrength", bf).SetValue(st, d); t.GetField("Radius", bf).SetValue(st, r);
                t.GetField("Downsample", bf)?.SetValue(st, h);
            }
            _aoOrig.Clear();
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            var o = Options;
            int cap = o.frameCap > 0 ? o.frameCap : -1, vs = 0, fps = cap;
            if (o.vSync)
            {
                // In step with the display, at the fraction of its rate nearest the limit (240 Hz and 60: every 4th refresh).
                double hz = Screen.currentResolution.refreshRateRatio.value;
                vs = cap > 0 && hz > 1.0 ? Mathf.Clamp(Mathf.RoundToInt((float)(hz / cap)), 1, 4) : 1;
                fps = -1;
            }
#if UNITY_EDITOR
            // The editor's Game view keeps its own vsync: there the limit alone holds the rate.
            if (vs > 0) { vs = 0; fps = cap; }
#endif
            if (QualitySettings.vSyncCount != vs) QualitySettings.vSyncCount = vs;
            if (Application.targetFrameRate != fps) Application.targetFrameRate = fps;

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float scale = Mathf.Clamp(o.renderScale, 0.5f, 1.5f);
            // The camcorder records at its own lines (480-540) and processes the picture at that size: drawing the 3D
            // view at the full window (1080 lines) shaded 4-5 times the pixels it keeps. At 1.35 x the recording it looks
            // the same through the camcorder and the graphics card does about a third less (2026-10-27: 67 % -> 46 %).
            if (o.matchRecording)
            {
                if (!_rig) _rig = FindAnyObjectByType<CamcorderRig>();
                if (_rig && _rig.Settings != null && _rig.Settings.camcorderLook && Screen.height > 0)
                    scale = Mathf.Clamp(_rig.Settings.videoLines * 1.35f / Screen.height, 0.5f, scale);
            }
            if (urp && Mathf.Abs(urp.renderScale - scale) > 0.001f) urp.renderScale = scale;
            foreach (var s in _ssao) if (s.f && s.f.isActive != o.ambientOcclusion) s.f.SetActive(o.ambientOcclusion);

            if (!_cam) _cam = Camera.main;
            if (_cam)
            {
                var data = _cam.GetUniversalAdditionalCameraData();
                var aa = o.antiAliasing ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                if (data.antialiasing != aa) data.antialiasing = aa;
            }

            if (!_lights) _lights = FindAnyObjectByType<FixtureLightManager>();
            if (_lights)
            {
                _lights.shadowedTroffers = Mathf.Clamp(o.shadowLights, 0, 48);
                _lights.shadowDistance = Mathf.Clamp(o.shadowDistance, 6f, 30f);
                _lights.shadowTier = Mathf.Clamp(o.shadowResolution, 0, 2);
                _lights.maxDistance = Mathf.Clamp(o.lightDistance, 16f, 44f);
                _lights.fadeStart = _lights.maxDistance - 8f;
                if (!_haveBase)
                {
                    _baseTube = _lights.tubeIntensity; _baseLamp = _lights.lampIntensity;
                    _baseTubeRange = _lights.tubeRange; _baseLampRange = _lights.lampRange;
                    _baseTubeColour = _lights.tubeColor; _baseLampColour = _lights.lampColor; _baseStripColour = _lights.stripColor;
                    _haveBase = true;
                }
                // The look's colour temperature and tint shift every fixture together (brightness kept).
                _lights.tubeColor = GraphicsOptions.ShiftColour(_baseTubeColour, o.colourTemp, o.colourTint);
                _lights.lampColor = GraphicsOptions.ShiftColour(_baseLampColour, o.colourTemp, o.colourTint);
                _lights.stripColor = GraphicsOptions.ShiftColour(_baseStripColour, o.colourTemp, o.colourTint);
                _lights.tubeIntensity = _baseTube * o.lightStrength;
                _lights.lampIntensity = _baseLamp * o.lightStrength;
                _lights.tubeRange = _baseTubeRange * o.lightReach;
                _lights.lampRange = _baseLampRange * o.lightReach;
                _lights.domeHeightBoost = o.tallBoost;
                _lights.shadowStrength = o.shadowDarkness;
                _lights.farRealLights = Mathf.Clamp(o.perPixelFar ? 0 : o.farLights, 0, 120);   // per-pixel light makes extra real lights unnecessary
                _lights.flickerAmount = Mathf.Clamp(o.flicker, 0f, 1.5f);
                _lights.spread = Mathf.Clamp01(o.fixtureSpread);
                _lights.emitterSize = Mathf.Clamp01(o.panelSize);
                if (_lights.grid)
                {
                    _lights.grid.bounceGain = o.fillLight;
                    _lights.grid.contactOcclusion = Mathf.Clamp01(o.contactOcclusion);
                    _lights.grid.roomFill = Mathf.Clamp01(o.roomFill);
                    _lights.grid.perPixelDirect = o.perPixelFar;
                    _lights.grid.exactDistance = Mathf.Clamp(o.exactLightRadius, 0f, 12f);
                    _lights.grid.wrap = o.softness;
                    _lights.grid.smoothing = o.poolSmoothing;
                    _lights.grid.contrast = o.darkSpots;
                    _lights.grid.tracedBounce = o.tracedBounce;
                    _lights.grid.colourBleed = Mathf.Clamp(o.colourBleed, 0f, 2f);
                    _lights.grid.multiBounce = Mathf.Clamp01(o.multiBounce);
                    _lights.grid.haze = Mathf.Clamp01(o.haze);
                }
                _lights.bounceCount = Mathf.Clamp(o.bounceLights, 0, 48);   // ceiling + lamps + bounce stay under URP's 256 visible lights
                // Applied when the option changes, so debug tools can switch modes for comparisons.
                if (_appliedGrid != (o.lightGrid ? 1 : 0))
                {
                    _appliedGrid = o.lightGrid ? 1 : 0;
                    _lights.mode = o.lightGrid ? FixtureLightManager.LightingMode.LightGrid : FixtureLightManager.LightingMode.RoomLayers;
                }
            }

            // Backrooms/Lit's look switches (uniforms, no shader variants). Zero, as bound when no controller runs, is "off".
            // x: ceiling panels as rectangles, y: contact-hardening shadow softness, z: contact shadow strength, w: set.
            Shader.SetGlobalVector(LookId, new Vector4(o.areaLights ? 1f : 0f, Mathf.Clamp(o.shadowSoftness, 0f, 2f), Mathf.Clamp01(o.contactShadows), 1f));
            // Contact shadows have a render pass of their own on the player's camera (it reads the camera depth properly).
            ContactShadows.Ensure().strength = Mathf.Clamp01(o.contactShadows);
            var tint = GraphicsOptions.ColourShift(o.colourTemp, o.colourTint) * Mathf.Clamp(o.panelGlow, 0f, 3f);
            Shader.SetGlobalVector(EmissionTintId, new Vector4(tint.r, tint.g, tint.b, 1f));

            // The Poolrooms keep their own light (sun, sky, baked bounce): the look scales it.
            if (!_pool) { _pool = PoolWorld.Instance; if (_pool) { _poolSun = _pool.sunIntensity; _poolBounce = _pool.bounceGain; _poolSky = _pool.skyIntensity; _poolSunColour = _pool.sunColor; _poolAmbient = _pool.ambient; _poolApplied = new Vector4(-1, -1, -1, -1); } }
            if (_pool)
            {
                var want = new Vector4(o.poolSun, o.poolBounce, o.poolSky, o.poolWarmth);
                if (want != _poolApplied)
                {
                    _poolApplied = want;
                    _pool.sunIntensity = _poolSun * Mathf.Clamp(o.poolSun, 0f, 3f);
                    _pool.bounceGain = _poolBounce * Mathf.Clamp(o.poolBounce, 0f, 3f);
                    _pool.skyIntensity = _poolSky * Mathf.Clamp(o.poolSky, 0f, 3f);
                    _pool.ambient = _poolAmbient * Mathf.Clamp(o.poolSky, 0f, 3f);
                    _pool.sunColor = GraphicsOptions.ShiftColour(_poolSunColour, 4500f - 1500f * Mathf.Clamp(o.poolWarmth, -1f, 1f), 0f);
                    _pool.ApplyLight();
                }
            }
        }
    }
}
