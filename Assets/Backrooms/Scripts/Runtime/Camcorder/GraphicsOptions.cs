using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Backrooms.Generation;

namespace Backrooms.Camcorder
{
    /// <summary>Performance and quality options (F5 > GRAPHICS). Saved separately from the camcorder look.</summary>
    [Serializable]
    public class GraphicsOptions
    {
        public bool vSync = false;
        public int frameCap = 0;              // 0 = unlimited (when vsync is off)
        public float renderScale = 1f;
        public int shadowLights = 24;         // nearest lights that cast shadows
        public float shadowDistance = 20f;    // shadows fade out smoothly toward this distance
        public int shadowResolution = 1;      // 0 low, 1 medium, 2 high
        public float lightDistance = 36f;     // fixtures lit out to here (fades into the fog)
        public int bounceLights = 40;         // nearest fixtures with soft bounce fill
        public bool ambientOcclusion = true;
        public bool lightGrid = true;         // far fixtures light a 3D grid (off: older room-layer lights)
        public bool antiAliasing = true;

        // F5 > LIGHTING (multipliers are relative to the scene's own light settings)
        public float lightStrength = 1f;      // all fixtures
        public float lightReach = 1f;         // how far each fixture's light travels
        public float tallBoost = 0.15f;        // extra output for fixtures hung high above the floor
        public float fillLight = 1f;          // soft bounced light from lit floors
        public float softness = 0.15f;        // wrap: how far light bends round surfaces facing away
        public float poolSmoothing = 0.5f;   // rounds off boxy light pools of far fixtures
        public float darkSpots = 0.35f;       // makes weakly lit areas darker
        public float shadowDarkness = 1f;     // real-light shadows
        public int farLights = 0;             // extra real (unshadowed) lights beyond the shadowed ones; 0 when per-pixel distant light is on
        public bool perPixelFar = true;       // distant fixtures are lit exactly per pixel (no visible grid texels)
        public float flicker = 0.35f;         // faulty tubes: how often and how deep they stutter (1 = the original amount)
        public float roomFill = 0.6f;         // bounced light that evens out walls (a stand-in for global illumination)
        public float fixtureSpread = 0.8f;    // how broadly ceiling fixtures throw light (0 = lamp-like cone, 1 = diffuse troffer)
        public void ResetLighting()
        {
            var d = new GraphicsOptions();
            lightStrength = d.lightStrength; lightReach = d.lightReach; tallBoost = d.tallBoost; fillLight = d.fillLight;
            softness = d.softness; poolSmoothing = d.poolSmoothing; darkSpots = d.darkSpots; shadowDarkness = d.shadowDarkness;
            farLights = d.farLights; flicker = d.flicker; fixtureSpread = d.fixtureSpread; roomFill = d.roomFill;
        }
        /// <summary>Evenly lit and pale, like the found-footage references: little contrast, a lot of fill, soft shadows.</summary>
        public void ApplyEvenLighting()
        {
            lightStrength = 1.35f; lightReach = 1.3f; tallBoost = 0.3f; fillLight = 2f; softness = 0.6f;
            poolSmoothing = 0.8f; darkSpots = 0f; shadowDarkness = 0.6f; fixtureSpread = 1f; roomFill = 1f;
        }

        const string Key = "Backrooms.Graphics.v1";
        public static GraphicsOptions Load()
        {
            try { var j = PlayerPrefs.GetString(Key, ""); if (!string.IsNullOrEmpty(j)) return JsonUtility.FromJson<GraphicsOptions>(j); }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not load graphics options: " + e.Message); }
            return new GraphicsOptions();
        }
        public void Save() { PlayerPrefs.SetString(Key, JsonUtility.ToJson(this)); PlayerPrefs.Save(); }

        public static readonly string[] PresetNames = { "Low", "Medium", "High", "Ultra" };
        public void ApplyPreset(int i)
        {
            shadowLights = new[] { 8, 16, 24, 40 }[i];
            shadowDistance = new[] { 12f, 16f, 20f, 26f }[i];
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
        float _origScale = -1f;
        float _baseTube, _baseLamp, _baseTubeRange, _baseLampRange;
        bool _haveBase;
        ScriptableRendererFeature _ssao;
        bool _origSsao;

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
                if (f?.GetValue(urp) is ScriptableRendererData[] list && list.Length > 0 && list[0])
                    foreach (var feat in list[0].rendererFeatures)
                        if (feat && feat.GetType().Name.Contains("AmbientOcclusion")) { _ssao = feat; _origSsao = feat.isActive; }
            }
        }

        void OnDisable()
        {
            // Settings changed at runtime must not end up saved in the project's assets.
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp && _origScale > 0f) urp.renderScale = _origScale;
            if (_ssao) _ssao.SetActive(_origSsao);
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            var o = Options;
            int vs = o.vSync ? 1 : 0;
            if (QualitySettings.vSyncCount != vs) QualitySettings.vSyncCount = vs;
            int fps = o.vSync ? -1 : o.frameCap > 0 ? o.frameCap : -1;
            if (Application.targetFrameRate != fps) Application.targetFrameRate = fps;

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float scale = Mathf.Clamp(o.renderScale, 0.5f, 1.5f);
            if (urp && Mathf.Abs(urp.renderScale - scale) > 0.001f) urp.renderScale = scale;
            if (_ssao && _ssao.isActive != o.ambientOcclusion) _ssao.SetActive(o.ambientOcclusion);

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
                    _baseTubeRange = _lights.tubeRange; _baseLampRange = _lights.lampRange; _haveBase = true;
                }
                _lights.tubeIntensity = _baseTube * o.lightStrength;
                _lights.lampIntensity = _baseLamp * o.lightStrength;
                _lights.tubeRange = _baseTubeRange * o.lightReach;
                _lights.lampRange = _baseLampRange * o.lightReach;
                _lights.domeHeightBoost = o.tallBoost;
                _lights.shadowStrength = o.shadowDarkness;
                _lights.farRealLights = Mathf.Clamp(o.perPixelFar ? 0 : o.farLights, 0, 120);   // per-pixel light makes extra real lights unnecessary
                _lights.flickerAmount = Mathf.Clamp(o.flicker, 0f, 1.5f);
                _lights.spread = Mathf.Clamp01(o.fixtureSpread);
                if (_lights.grid)
                {
                    _lights.grid.bounceGain = o.fillLight;
                    _lights.grid.roomFill = Mathf.Clamp01(o.roomFill);
                    _lights.grid.perPixelDirect = o.perPixelFar;
                    _lights.grid.wrap = o.softness;
                    _lights.grid.smoothing = o.poolSmoothing;
                    _lights.grid.contrast = o.darkSpots;
                }
                _lights.bounceCount = Mathf.Clamp(o.bounceLights, 0, 48);   // ceiling + lamps + bounce stay under URP's 256 visible lights
                // Applied when the option changes, so debug tools can switch modes for comparisons.
                if (_appliedGrid != (o.lightGrid ? 1 : 0))
                {
                    _appliedGrid = o.lightGrid ? 1 : 0;
                    _lights.mode = o.lightGrid ? FixtureLightManager.LightingMode.LightGrid : FixtureLightManager.LightingMode.RoomLayers;
                }
            }
        }
    }
}
