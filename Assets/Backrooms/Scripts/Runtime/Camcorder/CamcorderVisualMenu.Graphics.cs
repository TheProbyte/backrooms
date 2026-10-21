using Backrooms.DebugTools;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.UI;

namespace Backrooms.Camcorder
{
    /// <summary>F5 > GRAPHICS: frame pacing, render scale, shadows and light distances, with live performance data.</summary>
    public partial class CamcorderVisualMenu
    {
        Text _gfxReadout;
        float _gfxNext;

        void BuildGraphics()
        {
            var gc = GraphicsController.Ensure();
            GraphicsOptions O() => gc.Options;

            Section("PERFORMANCE", "Live numbers. In the editor these include editor overhead; a build is faster.");
            var row = Rect("GfxReadout", _list);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 200;
            row.gameObject.AddComponent<Canvas>();
            _gfxReadout = Label(row, "", 14, TextCol, TextAnchor.UpperLeft);
            Stretch(_gfxReadout.rectTransform);
            _gfxReadout.supportRichText = true;

            Section("QUALITY PRESETS", "Sets the shadow and light options below together.");
            var presets = Rect("GfxPresets", _list);
            presets.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            var hl = presets.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6; hl.childForceExpandWidth = true; hl.childControlWidth = true; hl.childControlHeight = true;
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

            Section("SHADOWS", "Shadows ease in over half a second as lights come into range.");
            Slider("Shadow-casting lights", "How many of the nearest lights cast shadows. Shadowed lights also spill through doorways (unshadowed ones stay inside their own room, so light never passes through walls). Each one redraws nearby geometry, so this is the biggest performance setting.",
                0f, 48f, "0", "", () => O().shadowLights, v => { O().shadowLights = Mathf.RoundToInt(v); O().Save(); }, true);
            Slider("Shadow distance", "Lights farther than this give up their shadow (and their doorway spill).",
                8f, 30f, "0.0", " m", () => O().shadowDistance, v => { O().shadowDistance = v; O().Save(); });
            Slider("Shadow resolution", "Sharpness of each light's shadow map: 0 low, 1 medium, 2 high.",
                0f, 2f, "0", "", () => O().shadowResolution, v => { O().shadowResolution = Mathf.RoundToInt(v); O().Save(); }, true);

            Section("LIGHTS", "The building is already lit; these only decide how far real lights are drawn.");
            Slider("Light distance", "Every powered fixture closer than this is on, whether you can see it or not. Farther ones dim into the fog.",
                20f, 44f, "0", " m", () => O().lightDistance, v => { O().lightDistance = v; O().Save(); });
            Slider("Bounce fill lights", "Nearest fixtures that also add soft bounced light to walls and floor.",
                0f, 48f, "0", "", () => O().bounceLights, v => { O().bounceLights = Mathf.RoundToInt(v); O().Save(); }, true);
        }

        void UpdateGraphicsTab()
        {
            if (!IsOpen || _tab != 5 || !_gfxReadout || Time.unscaledTime < _gfxNext) return;
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
