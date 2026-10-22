using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// F5 opens a left-hand panel of camcorder visual settings. Every slider writes straight into the live
    /// <see cref="CamcorderSettings"/>, so changes are visible immediately while the world keeps running.
    /// Settings are saved when the panel closes. Built with UI Toolkit in code; styling lives in
    /// Resources/UI/CamcorderMenu.uss and the panel settings in Resources/UI/CamcorderPanel.asset.
    /// </summary>
    public partial class CamcorderVisualMenu : MonoBehaviour
    {
        public CamcorderRig rig;
        public CamcorderPlayerController player;

        public bool IsOpen { get; private set; }
        int _tab;

        UIDocument _doc;
        VisualElement _root, _panel, _list;
        readonly List<Action> _refreshers = new List<Action>();
        CursorLockMode _prevLock;

        void Start()
        {
            Build();
            SetOpen(false);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f5Key.wasPressedThisFrame) SetOpen(!IsOpen);
            else if (IsOpen && kb.escapeKey.wasPressedThisFrame) SetOpen(false);
            UpdateWorldGen();
            UpdateAudioTab();
            UpdateGraphicsTab();
            UpdateMinimap(kb);
            if (IsOpen && _stby != null) _stby.style.opacity = (Time.unscaledTime % 1.2f) < 0.8f ? 1f : 0f;
        }

        public void SetOpen(bool open)
        {
            IsOpen = open;
            if (_panel != null) Show(_panel, open);
            CamcorderPlayerController.InputBlocked = open;
            if (open)
            {
                _prevLock = Cursor.lockState;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                _auOpened = true;
                RefreshAll();
            }
            else
            {
                if (rig) rig.Settings.Save();
                CloseAudio();
                HideTip();
                _root?.focusController?.focusedElement?.Blur();
                if (_prevLock == CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            }
        }

        // ---------------- Layout ----------------

        void Build()
        {
            var go = new GameObject("CamcorderMenu_UI");
            go.transform.SetParent(transform, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = Resources.Load<PanelSettings>("UI/CamcorderPanel");
            _root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/CamcorderMenu");
            if (sheet) _root.styleSheets.Add(sheet);
            _root.AddToClassList("cc-root");
            _root.pickingMode = PickingMode.Ignore;           // only the panel itself catches the pointer
            ApplyMenuFont();

            _panel = new VisualElement { name = "Panel" };
            _panel.AddToClassList("cc-panel");
            _root.Add(_panel);

            // Header like a camcorder's on-screen menu: MENU, the page name, and a blinking STBY.
            var header = new VisualElement(); header.AddToClassList("cc-header");
            header.Add(Text("MENU", "cc-title"));
            _pageLabel = Text("", "cc-page"); header.Add(_pageLabel);
            _stby = Text("STBY", "cc-stby"); header.Add(_stby);
            _panel.Add(header);
            var rule = new VisualElement(); rule.AddToClassList("cc-rule"); _panel.Add(rule);

            var tabs = BtnRow(_panel);
            string[] names = { "CAMERA", "PLAYER", "WORLD", "AUDIO", "SOUND", "DISPLAY" };
            _tabButtons = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                _tabButtons[i] = Button(tabs, names[i], () => ShowTab(idx));
                _tabButtons[i].AddToClassList("cc-tab");
            }

            _panel.Add(Text("[F5] END   [F6] MAP   HOVER AN ITEM FOR HELP", "cc-hint"));

            // Presets row (visual tab only).
            _presets = BtnRow(_panel);
            _presets.AddToClassList("cc-presets");
            for (int i = 0; i < CamcorderSettings.PresetNames.Length; i++)
            {
                int idx = i;
                Button(_presets, CamcorderSettings.PresetNames[i], () => ApplyPreset(idx));
            }

            _views = new ScrollView[names.Length];
            _list = _views[0] = MakeList(); BuildControls();
            _list = _views[1] = MakeList(); BuildGameplay();
            _list = _views[2] = MakeList(); BuildWorldGen();
            _list = _views[3] = MakeList(); BuildAudio();
            _list = _views[4] = MakeList(); BuildSound();
            _list = _views[5] = MakeList(); BuildGraphics();

            // Footer.
            var footer = BtnRow(_panel);
            footer.AddToClassList("cc-footer");
            _resetVisuals = Button(footer, "RESET", () => ApplyPreset(0));
            Button(footer, "END", () => SetOpen(false));

            BuildMinimap();
            ShowTab(0);
        }

        static readonly string[] PageNames = { "CAMERA SET", "PLAYER SET", "WORLD SET", "AUDIO SET", "SOUND LIST", "DISPLAY SET" };
        Label _pageLabel, _stby;

        /// <summary>A monospaced system font reads closer to a camcorder's character generator; falls back
        /// to the default font when none is installed.</summary>
        void ApplyMenuFont()
        {
            foreach (var family in new[] { "Consolas", "Lucida Console", "Courier New" })
            {
                try
                {
                    var fa = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(family, "Bold");
                    if (fa == null) fa = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(family, "Regular");
                    if (fa == null) continue;
                    _root.style.unityFontDefinition = FontDefinition.FromSDFFont(fa);
                    return;
                }
                catch (Exception) { }
            }
        }

        ScrollView[] _views;
        Button[] _tabButtons;
        VisualElement _presets;
        Button _resetVisuals;

        ScrollView MakeList()
        {
            var sv = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, mouseWheelScrollSize = 40f };
            sv.AddToClassList("cc-scroll");
            _panel.Add(sv);
            return sv;
        }

        void ShowTab(int i)
        {
            for (int k = 0; k < _views.Length; k++)
            {
                Show(_views[k], k == i);
                _tabButtons[k].EnableInClassList("cc-selected", k == i);
            }
            Show(_presets, i == 0);
            _tab = i;
            if (_pageLabel != null) _pageLabel.text = PageNames[i];
            _panel.style.width = i == 2 ? 560 : i >= 3 ? 520 : 460;
            if (i == 4) RefreshAll();
            if (_resetVisuals != null) Show(_resetVisuals, i == 0);
            HideTip();
        }

        void BuildGameplay()
        {
            Section("MOVEMENT", "Non-visual options. More will be added here.");
            Toggle("Noclip (walk through anything)", "Turns off collision and gravity so you can fly through walls, floors and ceilings. Look where you want to go and use WASD; Space rises, Ctrl or C sinks, Shift is faster. Turn off to drop back to the floor.",
                () => player && player.noclip, v => { if (player) player.SetNoclip(v); });
            Toggle("Show debug info", "Shows your position, chunk, world seed and frame rate in the corner, handy for reporting generation bugs.",
                () => rig && rig.osd && rig.osd.showDebug, v => { if (rig && rig.osd) rig.osd.showDebug = v; });
            Slider("Noclip speed","Flying speed while noclip is on (Shift doubles it).",
                1f, 12f, "0.0", " m/s", () => player ? player.noclipSpeed : 4f, v => { if (player) player.noclipSpeed = v; });
            Slider("Mouse sensitivity", "How far the camera turns per mouse movement.",
                0.01f, 0.3f, "0.000", "", () => player ? player.mouseSensitivity : 0.08f, v => { if (player) player.mouseSensitivity = v; });
            Toggle("Invert mouse Y", "Pushing the mouse forward looks down instead of up.",
                () => player && player.invertY, v => { if (player) player.invertY = v; });
            Slider("Walk speed", "Normal walking pace.", 0.5f, 3f, "0.00", " m/s",
                () => player ? player.walkSpeed : 1.35f, v => { if (player) player.walkSpeed = v; });
            Slider("Run speed", "Speed while holding Shift.", 1.5f, 6f, "0.00", " m/s",
                () => player ? player.runSpeed : 3.6f, v => { if (player) player.runSpeed = v; });
        }

        void BuildControls()
        {
            CamcorderSettings S() => rig.Settings;

            Section("LENS", "Optics in front of the sensor.");
            Slider("Field of view (zoom)", "How wide the lens sees. Consumer camcorders were around 45-55 degrees at their widest; lower values zoom in and flatten perspective.",
                40f, 90f, "0", "°", () => S().fieldOfView, v => S().fieldOfView = v);
            Slider("Lens distortion", "Bending of straight lines near the frame edges. Negative bows lines outward (barrel, wide lenses); positive pinches them inward (pincushion, zoomed lenses).",
                -0.4f, 0.4f, "0.00", "", () => S().lensDistortion, v => S().lensDistortion = v);
            Slider("Chromatic aberration", "Coloured fringes at high-contrast edges toward the frame corners, from cheap glass focusing red and blue slightly differently.",
                0f, 0.6f, "0.00", "", () => S().chromaticAberration, v => S().chromaticAberration = v);
            Slider("Vignette", "Darkening toward the corners from light falling off across the lens.",
                0f, 0.6f, "0.00", "", () => S().vignette, v => S().vignette = v);
            Slider("Bloom / halation", "Glow that spills around bright lights. Old camcorder optics and CCDs bloom heavily, which gives the hazy glow around fluorescent tubes.",
                0f, 1.5f, "0.00", "", () => S().bloom, v => S().bloom = v);
            Slider("Bloom spread", "How far the glow reaches. Low keeps it tight around the light; high washes it across walls and ceiling.",
                0.2f, 0.95f, "0.00", "", () => S().bloomScatter, v => S().bloomScatter = v);

            Section("FOCUS", "Contrast autofocus drives depth of field from the centre of frame.");
            Toggle("Autofocus + depth of field", "The camera measures what is in the centre of frame and drives the lens to it, blurring things much nearer or farther. Off keeps everything sharp.",
                () => S().autofocus, v => S().autofocus = v);
            Slider("Aperture (f-number)", "Size of the lens opening. Low numbers (f/1.4-2.8) give shallow, blurry backgrounds; high numbers (f/8-16) keep everything in focus.",
                1.4f, 16f, "0.0", "", () => S().aperture, v => S().aperture = v);
            Slider("Focus motor speed", "How fast the autofocus motor moves when the subject distance changes.",
                0.2f, 3f, "0.00", "x", () => S().focusSpeed, v => S().focusSpeed = v);
            Slider("Focus hunting", "How much the focus overshoots and settles back: the searching pulse you see when a camcorder refocuses.",
                0f, 1f, "0.00", "", () => S().focusHunting, v => S().focusHunting = v);

            Section("EXPOSURE / IRIS", "The auto-iris opens slowly in the dark and clamps down fast in bright light.");
            Toggle("Auto exposure", "Automatic iris: the image brightens in dark areas and darkens under bright lights, with a lag. Off holds a fixed exposure.",
                () => S().autoExposure, v => S().autoExposure = v);
            Slider("Exposure bias", "Overall brightness offset in stops (EV). Negative is darker and moodier, positive brighter.",
                -2f, 2f, "0.00", " EV", () => S().exposureBias, v => S().exposureBias = v);
            Slider("Iris speed", "How quickly auto exposure reacts. Low values give the slow, lazy adaptation of cheap camcorders.",
                0.1f, 3f, "0.00", "x", () => S().exposureSpeed, v => S().exposureSpeed = v);
            Slider("Max gain-up", "How many stops the camera may electronically brighten dark scenes. More gain sees further into the dark but adds noise.",
                0f, 2.5f, "0.00", " EV", () => S().maxGain, v => S().maxGain = v);
            Slider("Fluorescent flicker beat", "Slow brightness pulsing caused by the camera shutter beating against 60 Hz fluorescent tubes.",
                0f, 1f, "0.00", "", () => S().fluorescentFlicker, v => S().fluorescentFlicker = v);

            Section("SENSOR NOISE", "Gain noise rises as the camera brightens dark scenes.");
            Slider("Base noise", "Constant sensor grain, strongest in shadows like a real CCD. Generated fresh every frame with no repeating pattern.",
                0f, 0.15f, "0.000", "", () => S().noise, v => S().noise = v);
            Slider("Noise per EV of gain", "Extra grain added for every stop of gain the auto exposure applies, so dark rooms get noisier as the camera brightens them.",
                0f, 0.25f, "0.000", "", () => S().gainNoise, v => S().gainNoise = v);
            Slider("Chroma noise (colour blotches)", "Coarse, soft colour speckle in the shadows from the camera's low colour resolution.",
                0f, 0.15f, "0.000", "", () => S().chromaNoise, v => S().chromaNoise = v);
            Slider("Grain size", "Size of the grain relative to a 720-line sensor. Higher values give coarser, clumpier grain like older tape formats.",
                0.5f, 3f, "0.00", "x", () => S().grainSize, v => S().grainSize = v);

            Section("IMAGE PROCESSING", "Camcorder DSP: soft optics, edge enhancement, subsampled colour.");
            Slider("Softness / resolution", "Optical and sensor blur. Consumer video resolves far less detail than a modern screen; raise this for an authentic soft image.",
                0f, 3f, "0.00", " px", () => S().softness, v => S().softness = v);
            Slider("Edge enhancement", "The camera's artificial sharpening, which draws light and dark halos along edges. High values give the crisp but fake video outline.",
                0f, 4f, "0.00", "", () => S().sharpening, v => S().sharpening = v);
            Slider("Chroma bleed", "Colour smears sideways past object edges, because video stores colour at much lower horizontal resolution than brightness.",
                0f, 10f, "0.0", " px", () => S().chromaBleed, v => S().chromaBleed = v);
            Slider("Chroma delay", "Colour shifted slightly left or right of the brightness image, from timing errors in the tape signal.",
                -3f, 3f, "0.0", " px", () => S().chromaShift, v => S().chromaShift = v);
            Slider("Highlight knee", "Where bright areas start to compress before clipping to white. Lower values flatten lights into soft, washed-out blobs.",
                0.6f, 1f, "0.00", "", () => S().highlightKnee, v => S().highlightKnee = v);
            Slider("Black pedestal", "Lifts the darkest blacks to a milky grey, like video that never quite reaches true black.",
                0f, 0.1f, "0.000", "", () => S().pedestal, v => S().pedestal = v);

            Section("COLOUR", "White balance and grading.");
            Slider("White balance (temp)", "Warm (positive) or cool (negative) colour cast from a camera balanced for the wrong light.",
                -50f, 50f, "0", "", () => S().whiteBalanceTemp, v => S().whiteBalanceTemp = v);
            Slider("White balance (tint)", "Green (negative) or magenta (positive) cast. Fluorescent light reads green on camera; a little magenta corrects it.",
                -50f, 50f, "0", "", () => S().whiteBalanceTint, v => S().whiteBalanceTint = v);
            Slider("Saturation", "Colour intensity.", -60f, 40f, "0", "", () => S().saturation, v => S().saturation = v);
            Slider("Contrast", "Separation between darks and brights.", -30f, 50f, "0", "", () => S().contrast, v => S().contrast = v);

            Section("HANDHELD", "How the operator holds the camera.");
            Slider("Shake intensity", "Overall strength of handheld motion while walking and running.",
                0f, 2f, "0.00", "x", () => S().shakeIntensity, v => S().shakeIntensity = v);
            Slider("Breathing sway", "The slow rise and fall of the operator's breathing while standing still.",
                0f, 3f, "0.00", "x", () => S().breathing, v => S().breathing = v);
            Slider("Image stabilisation", "Electronic stabilisation that removes much of the fast shake (running jolts) but keeps slow drift.",
                0f, 1f, "0.00", "", () => S().stabilization, v => S().stabilization = v);
            Slider("Look overshoot", "When you stop turning, the camera carries on a fraction of a degree and settles back, like a hand-held camera. Adds no input delay. 0 = off.",
                0f, 2f, "0.00", "x", () => S().lookOvershoot, v => S().lookOvershoot = v);
            Slider("Look jitter", "A small irregular wobble while you turn the camera. 0 = off.",
                0f, 2f, "0.00", "x", () => S().lookJitter, v => S().lookJitter = v);
            Slider("Motion blur (shutter)", "Smear when turning quickly, from the camera's shutter staying open for part of each frame.",
                0f, 0.6f, "0.00", "", () => S().motionBlur, v => S().motionBlur = v);

            Section("FORMAT / TAPE", "Recording format and playback artefacts.");
            Toggle("4:3 frame (pillarbox)", "Show the image in the 4:3 shape of standard-definition video, with black bars at the sides.",
                () => S().aspect43, v => S().aspect43 = v);
            Toggle("Viewfinder OSD (REC, date, counter)", "The camcorder's on-screen display: blinking REC, tape counter, battery and date stamp.",
                () => S().showOSD, v => S().showOSD = v);
            Slider("OSD lens curvature", "Bends the REC, time and counter overlay slightly, as if you are reading it through the viewfinder eyepiece. 0 keeps it flat.",
                0f, 2f, "0.00", "", () => S().osdCurvature, v => S().osdCurvature = v);
            Slider("Scanlines","The faint horizontal line structure of 480-line interlaced video. Filtered so it never shimmers or forms moire.",
                0f, 0.3f, "0.00", "", () => S().scanlines, v => S().scanlines = v);
            Slider("Tape tracking noise", "A soft band of wobble and brightening that drifts slowly up the frame, like a worn tape.",
                0f, 1f, "0.00", "", () => S().trackingNoise, v => S().trackingNoise = v);
        }

        void ApplyPreset(int i)
        {
            // Visual presets keep the current audio settings (audio has its own preset row).
            var p = CamcorderSettings.Preset(i);
            p.CopyAudioFrom(rig.Settings);
            rig.SetSettings(p);
            RefreshAll();
        }

        bool _refreshing;
        void RefreshAll() { _refreshing = true; foreach (var r in _refreshers) r(); _refreshing = false; }

        // ---------------- Widgets ----------------

        /// <summary>A labelled slider row; tab code keeps these to relabel or re-range them.</summary>
        class SliderRow
        {
            public VisualElement row;
            public Label name, value;
            public UnityEngine.UIElements.Slider slider;
        }

        static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        static Label Text(string text, string cls)
        {
            var l = new Label(text) { enableRichText = true };
            if (cls != null) l.AddToClassList(cls);
            return l;
        }

        /// <summary>A label added to the current list.</summary>
        Label AddText(string text, string cls) { var l = Text(text, cls); _list.Add(l); return l; }

        VisualElement BtnRow(VisualElement parent)
        {
            var r = new VisualElement();
            r.AddToClassList("cc-btnrow");
            parent.Add(r);
            return r;
        }

        void Section(string title, string desc)
        {
            AddText(title.ToUpperInvariant(), "cc-section");
            if (!string.IsNullOrEmpty(desc)) AddText(desc, "cc-desc");
        }

        SliderRow Slider(string label, string tip, float min, float max, string fmt, string unit, Func<float> get, Action<float> set, bool whole = false)
        {
            var r = new SliderRow { row = new VisualElement() };
            r.row.AddToClassList("cc-row");
            var head = new VisualElement(); head.AddToClassList("cc-rowhead");
            r.name = Text(label.ToUpperInvariant(), null); r.value = Text("", "cc-value");
            head.Add(r.name); head.Add(r.value);
            r.row.Add(head);
            r.slider = new UnityEngine.UIElements.Slider(min, max) { fill = true };
            r.slider.AddToClassList("cc-slider");
            r.row.Add(r.slider);
            AttachTip(r.row, label, tip);
            _list.Add(r.row);

            void ShowValue(float v) => r.value.text = v.ToString(fmt) + unit;
            r.slider.RegisterValueChangedCallback(e =>
            {
                float v = whole ? Mathf.Round(e.newValue) : e.newValue;
                if (whole && !Mathf.Approximately(v, e.newValue)) r.slider.SetValueWithoutNotify(v);
                set(v); ShowValue(v);
            });
            _refreshers.Add(() => { float v = get(); r.slider.SetValueWithoutNotify(v); ShowValue(v); });
            return r;
        }

        VisualElement Toggle(string label, string tip, Func<bool> get, Action<bool> set)
        {
            var t = new UnityEngine.UIElements.Toggle { text = label.ToUpperInvariant() };
            t.AddToClassList("cc-toggle");
            var row = new VisualElement(); row.AddToClassList("cc-row");
            row.Add(t);
            AttachTip(row, label, tip);
            _list.Add(row);
            t.RegisterValueChangedCallback(e => set(e.newValue));
            _refreshers.Add(() => t.SetValueWithoutNotify(get()));
            return row;
        }

        Button Button(VisualElement parent, string text, Action onClick)
        {
            var b = new Button(onClick) { text = text.ToUpperInvariant(), focusable = false };
            b.AddToClassList("cc-btn");
            parent.Add(b);
            return b;
        }

        TextField InputBox(VisualElement parent, string value)
        {
            var f = new TextField { value = value };
            f.AddToClassList("cc-field");
            parent.Add(f);
            return f;
        }

        // ---------------- Hover explanations ----------------

        VisualElement _tip;
        Label _tipTitle, _tipBody;

        /// <summary>Hovering a row shows its explanation in a box beside the panel, level with the row.</summary>
        void AttachTip(VisualElement row, string title, string body)
        {
            row.RegisterCallback<PointerEnterEvent>(_ => ShowTip(row, title, body));
            row.RegisterCallback<PointerLeaveEvent>(_ => HideTip());
        }

        void EnsureTip()
        {
            if (_tip != null) return;
            _tip = new VisualElement { pickingMode = PickingMode.Ignore };
            _tip.AddToClassList("cc-tip");
            _tipTitle = Text("", "cc-tip-title"); _tipBody = Text("", "cc-tip-body");
            _tip.Add(_tipTitle); _tip.Add(_tipBody);
            _root.Add(_tip);
        }

        void ShowTip(VisualElement row, string title, string body)
        {
            if (string.IsNullOrEmpty(body)) return;
            EnsureTip();
            _tipTitle.text = title.ToUpperInvariant(); _tipBody.text = body;
            Show(_tip, true);
            // Level with the row, just right of the panel, kept on screen.
            float h = _root.layout.height;
            _tip.style.left = _panel.layout.xMax + 12f;
            _tip.style.top = Mathf.Clamp(row.worldBound.yMin, 10f, Mathf.Max(10f, h - 160f));
        }

        void HideTip() { if (_tip != null) Show(_tip, false); }
    }
}
