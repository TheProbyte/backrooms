using Backrooms.DebugTools;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backrooms.Camcorder
{
    /// <summary>F5 > GRAPHICS: frame pacing, render scale, shadows and light distances, with live performance data.</summary>
    public partial class CamcorderVisualMenu
    {
        Label _gfxReadout;
        float _gfxNext;

        void BuildGraphics()
        {
            var gc = GraphicsController.Ensure();
            GraphicsOptions O() => gc.Options;

            Section("PERFORMANCE", "Live numbers. In the editor these include editor overhead; a build is faster.");
            _gfxReadout = AddText("", "cc-readout");

            Section("QUALITY PRESETS", "Sets the shadow and light options below together.");
            var presets = BtnRow(_list);
            for (int i = 0; i < GraphicsOptions.PresetNames.Length; i++)
            {
                int idx = i;
                Button(presets, GraphicsOptions.PresetNames[i], () => { O().ApplyPreset(idx); O().Save(); RefreshAll(); });
            }

            Section("DISPLAY", "Frame pacing and resolution.");
            Toggle("VSync", "Locks the frame rate to your monitor's refresh rate, removing tearing. Off lets it run as fast as it can (or up to the cap below).",
                () => O().vSync, v => { O().vSync = v; O().Save(); });
            Slider("Frame rate cap (0 = unlimited)", "Upper limit on frames per second when VSync is off. 30 feels like NTSC video; 0 removes the limit.",
                0f, 240f, "0", " fps", () => O().frameCap, v => { O().frameCap = Mathf.RoundToInt(v); O().Save(); }, true);
            Slider("Render scale", "Resolution the 3D view is drawn at, relative to the window. Below 1 is faster and softer (the camcorder look hides it well).",
                0.5f, 1.5f, "0.00", "x", () => O().renderScale, v => { O().renderScale = v; O().Save(); });
            Toggle("Ambient occlusion", "Soft contact shadows in corners and under furniture (screen-space).",
                () => O().ambientOcclusion, v => { O().ambientOcclusion = v; O().Save(); });
            Toggle("Anti-aliasing (SMAA)", "Smooths jagged edges.",
                () => O().antiAliasing, v => { O().antiAliasing = v; O().Save(); });

            Section("SHADOWS", "The building is lit by a baked lightmap; these real-time shadows are for furniture.");
            Slider("Shadow-casting lights", "How many of the nearest lights cast furniture shadows. Each one redraws nearby furniture, so fewer is faster.",
                0f, 32f, "0", "", () => O().shadowLights, v => { O().shadowLights = Mathf.RoundToInt(v); O().Save(); }, true);
            Slider("Shadow distance", "Lights farther than this give up their furniture shadow.",
                8f, 30f, "0.0", " m", () => O().shadowDistance, v => { O().shadowDistance = v; O().Save(); });
            Slider("Shadow resolution", "Sharpness of each light's shadow map: 0 low, 1 medium, 2 high.",
                0f, 2f, "0", "", () => O().shadowResolution, v => { O().shadowResolution = Mathf.RoundToInt(v); O().Save(); }, true);

            Section("LIGHTS", "Walls, floors and ceilings use baked light (always the same at any distance).");
            Slider("Furniture light distance", "How far real-time lights are drawn on furniture and props. Farther ones dim into the fog.",
                20f, 44f, "0", " m", () => O().lightDistance, v => { O().lightDistance = v; O().Save(); });
        }

        void UpdateGraphicsTab()
        {
            if (!IsOpen || _tab != 5 || _gfxReadout == null || Time.unscaledTime < _gfxNext) return;
            _gfxNext = Time.unscaledTime + 0.5f;
            var p = PerfStats.Ensure();
            var lm = FindAnyObjectByType<FixtureLightManager>();
            var ws = FindAnyObjectByType<WorldStreamer>();
            var sb = new System.Text.StringBuilder();
            sb.Append("<color=#e8c45c>").Append(p.Fps.ToString("0")).Append(" fps</color>  (").Append(p.FrameMs.ToString("0.0")).Append(" ms)   CPU main ")
              .Append(p.MainMs.ToString("0.0")).Append(" ms   GPU ").Append(p.GpuMs.ToString("0.0")).Append(" ms\n");
            sb.Append("Set-pass calls ").Append(p.SetPassCalls).Append("   shadow casters ").Append(p.ShadowCasters)
              .Append("   triangles ").Append(p.Triangles / 1000).Append("k\n");
            if (lm) sb.Append("Lights on ").Append(lm.EnabledLights).Append(" (of 256 allowed)   with shadows ").Append(lm.ShadowedLights).Append('\n');
            if (ws) sb.Append("Chunks loaded ").Append(ws.LoadedChunks).Append("   chunk build ").Append(ws.AverageBuildMs.ToString("0.0")).Append(" ms (worker thread)\n");
            sb.Append("VSync ").Append(QualitySettings.vSyncCount > 0 ? "on" : "off").Append("   target ").Append(Application.targetFrameRate > 0 ? Application.targetFrameRate + " fps" : "unlimited").Append('\n');
            _gfxReadout.text = sb.ToString();
        }
    }
}
