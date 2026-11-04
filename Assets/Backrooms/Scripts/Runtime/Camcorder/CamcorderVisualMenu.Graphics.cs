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

            BuildScreen();

            Section("DISPLAY", "Frame pacing and resolution.");
            Toggle("VSync", "Keeps frames in step with your monitor, removing tearing: at the fraction of its refresh rate nearest the cap below (a 240 Hz monitor and a 60 cap: every 4th refresh).",
                () => O().vSync, v => { O().vSync = v; O().Save(); });
            Slider("Frame rate cap (0 = unlimited)", "Upper limit on frames per second (60 by default). Frames beyond what you can see only heat the graphics card and spin its fans up. 30 feels like NTSC video; 0 removes the limit.",
                0f, 240f, "0", " fps", () => O().frameCap, v => { O().frameCap = Mathf.RoundToInt(v); O().Save(); }, true);
            Slider("Render scale", "Resolution the 3D view is drawn at, relative to the window. Below 1 is faster and softer (the camcorder look hides it well).",
                0.5f, 1.5f, "0.00", "x", () => O().renderScale, v => { O().renderScale = v; O().Save(); });
            Toggle("Match the recording", "With the camcorder picture on, draws the 3D view at about 1.35 times the recording's lines (720 for 536) instead of the full window: it looks the same through the camcorder, and the graphics card does about a third less work (quieter fans). Render scale above stays the most it draws.",
                () => O().matchRecording, v => { O().matchRecording = v; O().Save(); });
            Toggle("Ambient occlusion", "Soft contact shadows in corners and under furniture (screen-space).",
                () => O().ambientOcclusion, v => { O().ambientOcclusion = v; O().Save(); });
            Toggle("Anti-aliasing", "Smooths jagged edges.",
                () => O().antiAliasing, v => { O().antiAliasing = v; O().Save(); });
            Choice("Anti-aliasing type", "SMAA smooths edges in each frame. TAA blends each pixel with the last few frames (jittered): no crawling edges or sparkling tiles, a faint trail when moving fast. STP is Unity 6's spatial-temporal upscaler: TAA that also rebuilds the full picture from the smaller render (sharpest, with \"Match the recording\" on).",
                new[] { "SMAA", "TAA", "STP" }, () => O().aaKind, v => { O().aaKind = v; O().Save(); });
            Toggle("Tile specular anti-aliasing", "Glossy tiles far off: their highlights are widened where the surface turns faster than a pixel can show, so they shimmer less.",
                () => O().specularAA, v => { O().specularAA = v; O().Save(); });
            Toggle("Player shadow", "With no body shown, a rough stand-in casts your shadow (holding the camcorder up), in the lights and in the Poolrooms' sun.",
                () => O().playerShadow, v => { O().playerShadow = v; O().Save(); });
            Toggle("Water reflections", "The Poolrooms' water mirrors the room exactly: rails, ladders, curved walls and things on the deck (a second, half-size view of the room each frame). Off: a cheaper traced reflection that sees only walls, columns and openings.",
                () => O().planarWater, v => { O().planarWater = v; O().Save(); });

            Section("SHADOWS", "Lights hand over smoothly between real (shadowed) and grid light as you move.");
            Toggle("Light grid", "On: the nearest fixtures are real lights with live shadows and every other fixture lights a 3D light grid (light through doorways, bounce on ceilings). Off: the older system, a large pool of unshadowed lights kept inside their own rooms.",
                () => O().lightGrid, v => { O().lightGrid = v; O().Save(); });
            Slider("Shadow-casting lights", "How many of the nearest lights are real lights with shadows (light grid: capped to what the shadow atlas holds, about 38). Each one redraws nearby geometry, so this is the biggest performance setting. About 16 looks the same as more: walls already stop light exactly, shadows only add furniture detail.",
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

        /// <summary>The game window: how it is shown and its size (ScreenSettings). Applied straight away.</summary>
        void BuildScreen()
        {
            Section("SCREEN", "How the game window is shown and its size. On a wide monitor, WINDOWED or BORDERLESS WINDOW at 1920 × 1080 keeps the game to that size. Not in the editor.");
            Choice("Screen", "FULLSCREEN takes over the monitor at the size below (the monitor switches to it). BORDERLESS FULLSCREEN fills the screen as a window (quick to switch away from). WINDOWED is a normal window of the size below. BORDERLESS WINDOW is that window without its frame, centred.",
                ScreenSettings.ModeNames, () => (int)ScreenSettings.CurrentMode,
                v => ScreenSettings.Apply((ScreenSettings.Mode)v, ScreenSettings.CurrentSize));

            var row = new VisualElement(); row.AddToClassList("cc-row");
            var head = new VisualElement(); head.AddToClassList("cc-rowhead");
            head.Add(Text("SIZE", null));
            var value = Text("", "cc-value"); head.Add(value);
            row.Add(head);
            var buttons = BtnRow(row);
            void Step(int d)
            {
                var sizes = ScreenSettings.Sizes();
                int i = Mathf.Max(0, sizes.IndexOf(ScreenSettings.EffectiveSize));
                i = Mathf.Clamp(i + d, 0, sizes.Count - 1);
                ScreenSettings.Apply(ScreenSettings.CurrentMode, sizes[i]);
                value.text = ScreenSettings.SizeName(sizes[i]);
            }
            Button(buttons, "◄ LARGER", () => Step(-1));
            Button(buttons, "SMALLER ►", () => Step(1));
            AttachTip(row, "Size", "The game's size in pixels: the window's size, or the screen's in FULLSCREEN. Larger than the monitor is not offered.");
            _list.Add(row);
            _refreshers.Add(() => value.text = ScreenSettings.SizeName(ScreenSettings.EffectiveSize));
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
            if (lm) sb.Append("Lights on ").Append(lm.EnabledLights).Append(" (of 256 allowed)   with shadows ").Append(lm.ShadowedLights).Append("   mode ").Append(lm.mode).Append('\n');
            if (lm && lm.grid && lm.grid.Ready) sb.Append("Light grid: ").Append(lm.grid.DirtyTexelsLastFrame).Append(" texels recomputed last frame, ").Append(lm.grid.LastGpuSubmitMs.ToString("0.00")).Append(" ms CPU submit, bounce passes ").Append(lm.grid.BounceDispatchesLastFrame).Append(", uploads ").Append(lm.grid.BufferUploadsLastFrame).Append(", furniture boxes ").Append(lm.grid.OccluderCount).Append("\n");
            if (ws) sb.Append("Chunks loaded ").Append(ws.LoadedChunks).Append("   chunk build ").Append(ws.AverageBuildMs.ToString("0.0")).Append(" ms (worker), upload ").Append(ws.AverageUploadMs.ToString("0.0")).Append(" ms (main, per chunk)\n");
            sb.Append("VSync ").Append(QualitySettings.vSyncCount > 0 ? "on" : "off").Append("   target ").Append(Application.targetFrameRate > 0 ? Application.targetFrameRate + " fps" : "unlimited").Append('\n');
            _gfxReadout.text = sb.ToString();
        }
    }
}
