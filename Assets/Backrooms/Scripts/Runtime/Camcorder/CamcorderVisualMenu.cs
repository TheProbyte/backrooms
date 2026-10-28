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
            if (!open) CancelLocationSearch();
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
            string[] names = { "CAMERA", "PLAYER", "WORLD", "AUDIO", "SOUND", "DISPLAY", "LIGHT", "PLACES" };
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
            _presetButtons = new Button[CamcorderSettings.PresetNames.Length];
            for (int i = 0; i < CamcorderSettings.PresetNames.Length; i++)
            {
                int idx = i;
                _presetButtons[i] = Button(_presets, CamcorderSettings.PresetNames[i], () => ApplyPreset(idx));
            }
            MarkPreset();

            _views = new ScrollView[names.Length];
            _list = _views[0] = MakeList(); BuildControls();
            _list = _views[1] = MakeList(); BuildGameplay();
            _list = _views[2] = MakeList(); BuildWorldGen();
            _list = _views[3] = MakeList(); BuildAudio();
            _list = _views[4] = MakeList(); BuildSound();
            _list = _views[5] = MakeList(); BuildGraphics();
            _list = _views[6] = MakeList(); BuildLighting();
            _list = _views[7] = MakeList(); BuildLocations();

            // Footer.
            var footer = BtnRow(_panel);
            footer.AddToClassList("cc-footer");
            _resetVisuals = Button(footer, "RESET", () => ApplyPreset(0));
            // Saves the current settings as the preset in use (the highlighted one), into the project in the editor.
            _setDefault = Button(footer, "SET AS DEFAULT", () =>
            {
                if (!rig) return;
                int p = CamcorderSettings.CurrentPreset;
                CamcorderSettings.SavePreset(p, rig.Settings);
                rig.Settings.Save();
                _setDefault.text = "SAVED AS " + CamcorderSettings.PresetNames[p].ToUpperInvariant();
                _setDefault.schedule.Execute(() => _setDefault.text = "SET AS DEFAULT").StartingIn(1600);
            });
            Button(footer, "END", () => SetOpen(false));

            BuildMinimap();
            ShowTab(0);
        }

        static readonly string[] PageNames = { "CAMERA SET", "PLAYER SET", "WORLD SET", "AUDIO SET", "SOUND LIST", "DISPLAY SET", "LIGHTING SET", "LOCATIONS" };
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
        Button _resetVisuals, _setDefault;

        ScrollView MakeList()
        {
            var sv = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, mouseWheelScrollSize = 40f };
            sv.AddToClassList("cc-scroll");
            _panel.Add(sv);
            return sv;
        }

        void ShowTab(int i)
        {
            if (i != 7) CancelLocationSearch();
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

            Toggle("Camcorder picture", "The whole camcorder signal chain: sensor, camera processing, recording format and playback. Off shows the plain rendered image (the Clean preset), for comparison.",
                () => S().camcorderLook, v => S().camcorderLook = v);

            Section("LENS", "Optics in front of the sensor.");
            Slider("Field of view (zoom)", "Vertical angle of view. A stock 1/4-inch camcorder sees about 36 degrees vertically (47 across) at its widest; higher values are like a wide-angle converter lens screwed on the front, which was very common.",
                30f, 90f, "0", "°", () => S().fieldOfView, v => S().fieldOfView = v);
            Slider("Lens distortion", "Bending of straight lines toward the frame edges. Positive bows them outward (barrel: wide lenses and converters, 1 to 5 % on a real camcorder); negative pinches them in (pincushion, which zoomed-in lenses show slightly). It turns toward pincushion as you zoom.",
                -0.4f, 0.4f, "0.00", "", () => S().lensDistortion, v => S().lensDistortion = v);
            Slider("Chromatic aberration", "Red and blue fringes on high-contrast edges toward the corners (lateral colour). On a consumer zoom about half to one recording pixel in the corners; a converter lens doubles it.",
                0f, 0.3f, "0.00", "", () => S().chromaticAberration, v => S().chromaticAberration = v);
            Slider("Vignette", "Light falling off toward the corners. Strongest with the iris wide open (in the dark), weaker once the iris closes down in bright light.",
                0f, 0.6f, "0.00", "", () => S().vignette, v => S().vignette = v);
            Slider("Lens glare", "Veiling glare: the zoom's many cheaply coated glass elements scatter a few percent of all the light into a wide haze, so bright tubes glow and lift the shadows around them. 1 is a typical camcorder. There is no threshold: it scales with exposure, like real glare.",
                0f, 3f, "0.00", "x", () => S().bloom, v => S().bloom = v);
            Slider("Glare spread", "How wide the glare spreads around bright lights.",
                0f, 1f, "0.00", "", () => S().bloomScatter, v => S().bloomScatter = v);
            Slider("Purple fringing", "Blue-violet edges hugging blown-out lights against dark surroundings (the lens and sensor focus violet slightly differently).",
                0f, 1f, "0.00", "", () => S().purpleFringe, v => S().purpleFringe = v);

            Section("FOCUS", "Contrast autofocus. On a small camcorder sensor almost everything is sharp; blur shows on close-ups, at full zoom and while the focus hunts in the dark.");
            Toggle("Autofocus + depth of field", "The camera drives the lens to whatever is in the centre (or your hand, close in front of it). In the dark it has no contrast to lock on to and hunts near and far. Off keeps everything sharp.",
                () => S().autofocus, v => S().autofocus = v);
            Slider("Widest aperture (f-number)", "The lens's maximum aperture at the wide end (f/1.6-2 on 1990s camcorders). The iris closes from here in brighter light, deepening the focus; zooming in also narrows it.",
                1.2f, 4f, "0.0", "", () => S().aperture, v => S().aperture = v);
            Slider("Focus motor speed", "How fast the focus motor moves.",
                0.2f, 3f, "0.00", "x", () => S().focusSpeed, v => S().focusSpeed = v);
            Slider("Focus hunting", "How readily the focus hunts in the dark and how far it overshoots when it refocuses.",
                0f, 1f, "0.00", "", () => S().focusHunting, v => S().focusHunting = v);
            Slider("Focus breathing", "A video zoom lens's angle of view changes slightly with focus: the frame creeps in as it focuses close (on your hand) and pulses while it hunts. 0 is a lens that does not breathe.",
                0f, 3f, "0.00", "x", () => S().focusBreathing, v => S().focusBreathing = v);

            Section("EXPOSURE", "The camera meters its own picture. It closes or opens the iris first; once the iris is wide open it turns the gain up (noisier), and drops the gain first when the light comes back.");
            Toggle("Auto exposure", "Automatic iris and gain. Off holds a fixed exposure (right for a lit corridor).",
                () => S().autoExposure, v => S().autoExposure = v);
            Slider("Exposure bias", "Shifts what the camera thinks is the right brightness, in stops (EV). This is not gain: it adds no noise unless it pushes the camera past its iris.",
                -2f, 2f, "0.00", " EV", () => S().exposureBias, v => S().exposureBias = v);
            Slider("Compensation", "How fully the exposure corrects for the light: 1 corrects completely (as real camcorders do until they run out of gain); lower lets dark rooms stay darker and bright ones brighter.",
                0.3f, 1f, "0.00", "", () => S().exposureCompensation, v => S().exposureCompensation = v);
            Slider("Iris speed", "How quickly the iris reacts.", 0.2f, 3f, "0.00", "x", () => S().exposureSpeed, v => S().exposureSpeed = v);
            Slider("Max gain", "How far the camera may turn its gain up in the dark, in stops (1 stop = 6 dB; 3 = +18 dB, typical). Every stop doubles the noise.",
                0f, 4f, "0.0", " EV", () => S().maxGain, v => S().maxGain = v);
            Choice("Shutter", "How long each picture is exposed. 1/60 s is the camcorder's normal (auto) shutter: smooth motion blur and no tube flicker. Faster shutters freeze motion (choppy, crisp) and show the tubes' 120 Hz flicker as a slow pulse.",
                CamcorderSettings.ShutterNames, () => S().shutter, v => S().shutter = v);
            Toggle("Low-lux slow shutter", "Once the gain is used up in the dark, the camera drops to 1/30 s: brighter, longer smears, and the picture only changes 30 times a second.",
                () => S().slowShutter, v => S().slowShutter = v);
            Slider("Tube flicker depth", "How much the fluorescent tubes flicker at 120 Hz. Invisible at a 1/60 shutter (it averages exactly two flickers); shows as a slow pulse at faster shutters.",
                0f, 1f, "0.00", "", () => S().fluorescentFlicker, v => S().fluorescentFlicker = v);

            Section("WHITE BALANCE", "Auto white balance pulls the picture slowly toward grey, within limits and mostly along warm/cool, so a room full of yellow keeps much of its yellow.");
            Toggle("Auto white balance", "Slowly corrects colour casts: walk from tube light into lamp light and the orange fades over a few seconds.",
                () => S().autoWhiteBalance, v => S().autoWhiteBalance = v);
            Slider("AWB strength", "How far the auto white balance pulls toward grey (1 = fully, which would neutralise the yellow rooms).",
                0f, 1f, "0.00", "", () => S().awbStrength, v => S().awbStrength = v);
            Slider("AWB speed", "How fast the white balance adapts.", 0.2f, 3f, "0.00", "x", () => S().awbSpeed, v => S().awbSpeed = v);
            Slider("Warmth shift", "A warm (positive) or cool (negative) cast on top of the auto white balance.",
                -50f, 50f, "0", "", () => S().whiteBalanceTemp, v => S().whiteBalanceTemp = v);
            Slider("Tint shift", "Green (negative) or magenta (positive) cast on top of the auto white balance. Fluorescent light reads slightly green on camera.",
                -50f, 50f, "0", "", () => S().whiteBalanceTint, v => S().whiteBalanceTint = v);

            Section("SENSOR", "A CCD's own noise and artefacts. 1 is what a late-1990s 1/4-inch CCD camcorder does.");
            Slider("Read noise", "The sensor's noise floor, strongest in the shadows. Grows with the gain.",
                0f, 3f, "0.00", "x", () => S().noise, v => S().noise = v);
            Slider("Noise growth with gain", "How much the noise grows as the gain goes up (1 = doubles every 6 dB, as on a real camera).",
                0f, 2f, "0.00", "x", () => S().gainNoise, v => S().gainNoise = v);
            Slider("Colour noise", "How much of the noise differs between red, green and blue: coloured speckle that the low colour resolution smears into blotches.",
                0f, 2f, "0.00", "x", () => S().chromaNoise, v => S().chromaNoise = v);
            Slider("Vertical smear", "A CCD's vertical stripe through a very bright light: charge leaking while the picture is read out. Shows in the dark with the gain up and a tube in frame.",
                0f, 3f, "0.00", "x", () => S().smear, v => S().smear = v);
            Slider("Fixed pattern / hot pixels", "The sensor's own faint fixed pattern and a few hot pixels, which show as steady dots at high gain.",
                0f, 3f, "0.00", "x", () => S().fixedPattern, v => S().fixedPattern = v);
            Slider("Noise reduction (3D)", "Digital camcorders average each frame with the last one in low light: less noise, but moving things leave faint ghost trails. 0 = none (analog camcorders).",
                0f, 1f, "0.00", "", () => S().noiseReduction, v => S().noiseReduction = v);

            Section("CAMERA PROCESSING", "The camera's picture processing, on a fixed recording picture (independent of your display).");
            Slider("Recording lines", "Height of the recorded picture: 480 lines for NTSC video (640 x 480 for a 4:3 frame). Everything after the lens happens at this size and is scaled to your screen at the end.",
                240f, 1080f, "0", " lines", () => S().videoLines, v => S().videoLines = Mathf.RoundToInt(v / 8f) * 8, true);
            Slider("Lens + sensor softness", "Blur from the optics and the sensor's anti-alias filter, in recording pixels.",
                0f, 2f, "0.00", " px", () => S().softness, v => S().softness = v);
            Slider("Edge enhancement", "The camera's (and VCR's) artificial sharpening: thin light and dark outlines along edges, reduced at high gain. 1 is typical for the format.",
                0f, 3f, "0.00", "x", () => S().sharpening, v => S().sharpening = v);
            Slider("Highlight knee", "Above this level the camera compresses bright light so tubes reach white gradually (lower = flatter, washed-out highlights). 1 = no knee, a hard clip.",
                0.6f, 1f, "0.00", "", () => S().highlightKnee, v => S().highlightKnee = v);
            Slider("White clip", "The brightest level the camera records (1 = 100 IRE). Everything above is flat white.",
                0.9f, 1.1f, "0.00", "", () => S().whiteClip, v => S().whiteClip = v);
            Slider("Highlight colour", "Where the camera starts draining colour from bright areas (highlight chroma suppression): from this level the colour fades to none at the clip. Lower = paler bright walls; the reference footage is about 0.6.",
                0.4f, 0.98f, "0.00", "", () => S().highlightColour, v => S().highlightColour = v);
            Slider("Black level (setup)", "Where black sits: 7.5 IRE (0.075) on analog NTSC tape, 0 on DV. Higher looks milky.",
                0f, 0.12f, "0.000", "", () => S().pedestal, v => S().pedestal = v);
            Slider("Saturation", "The camera's colour matrix. Colour also drops in the dark as the gain rises.",
                -60f, 60f, "0", "", () => S().saturation, v => S().saturation = v);
            Slider("Contrast (gamma)", "The camera's transfer curve: 0 is the video standard (gamma 0.45); higher gives darker mid-tones and more contrast.",
                -50f, 50f, "0", "", () => S().contrast, v => S().contrast = v);

            Section("HANDHELD", "How the operator holds the camera.");
            Slider("Shake intensity", "Overall strength of handheld motion while walking and running.",
                0f, 2f, "0.00", "x", () => S().shakeIntensity, v => S().shakeIntensity = v);
            Slider("Breathing sway", "The slow rise and fall of the operator's breathing while standing still.",
                0f, 3f, "0.00", "x", () => S().breathing, v => S().breathing = v);
            Slider("Image stabilisation", "Removes much of the fast shake (running jolts) but keeps slow drift.",
                0f, 1f, "0.00", "", () => S().stabilization, v => S().stabilization = v);
            Slider("Look overshoot", "When you stop turning, the camera carries on a fraction of a degree and settles back, like a hand-held camera. Adds no input delay. 0 = off.",
                0f, 2f, "0.00", "x", () => S().lookOvershoot, v => S().lookOvershoot = v);
            Slider("Look jitter", "A small irregular wobble while you turn the camera. 0 = off.",
                0f, 2f, "0.00", "x", () => S().lookJitter, v => S().lookJitter = v);
            Slider("Handheld roughness", "Fast, irregular hand shake on top of the smooth walking bob, with a small jolt on each step: someone walking with the camera in one hand. 0 = off.",
                0f, 3f, "0.00", "", () => S().handheldRoughness, v => S().handheldRoughness = v);
            Toggle("Recorded walk", "Walking moves the camera the way a real camera carried in the hand did: a phone recording (the sway, the bob of each step, the hand's tremor) kept in step with your feet. Off uses the generated walking bob. Shake intensity scales it.",
                () => S().recordedWalk, v => S().recordedWalk = v);
            Slider("Motion blur (shutter)", "Smear from movement while the shutter is open, in light (so lights streak bright), trailing behind the motion, the same at any frame rate. 1 = exactly what the shutter time gives.",
                0f, 2f, "0.00", "x", () => S().motionBlur, v => S().motionBlur = v);

            Section("FORMAT / TAPE", "What the footage was recorded on and played back from. Each format brings its own resolution, colour and artefacts; the sliders below scale them (1 = typical).");
            Choice("Format", "VHS-C SP: soft, smeared colour trailing to the right, tape noise, a slight wobble, head switching at the bottom. EP: the long-play speed, worse in every way. Hi8: sharper, the same colour-under. MiniDV: digital, crisp, no tape artefacts, 4:1:1 colour, faint compression in busy or noisy areas.",
                CamcorderFormat.Names, () => S().format, v => { S().SetFormat(v); RefreshAll(); });
            Slider("Colour resolution", "Colour detail compared with the format's own (lower = wider colour smear).",
                0.3f, 2f, "0.00", "x", () => S().chromaDetail, v => S().chromaDetail = v);
            Slider("Colour trailing", "How far analog colour smears to the right of edges.", 0f, 3f, "0.00", "x", () => S().chromaBleed, v => S().chromaBleed = v);
            Slider("Colour delay", "Colour shifted to the right of the picture by the tape's timing.", 0f, 3f, "0.00", "x", () => S().chromaShift, v => S().chromaShift = v);
            Slider("Timebase wobble", "Analog tape: each line starts a little early or late, so vertical edges wobble and the top of the picture bends.",
                0f, 3f, "0.00", "x", () => S().timebaseJitter, v => S().timebaseJitter = v);
            Slider("Tape noise", "Analog tape's own fine, streaky noise and colour noise.", 0f, 3f, "0.00", "x", () => S().tapeNoise, v => S().tapeNoise = v);
            Slider("Dropouts", "Analog tape: brief white dashes where the oxide is damaged.", 0f, 5f, "0.00", "x", () => S().dropouts, v => S().dropouts = v);
            Slider("Colour phase noise", "Analog colour-under: the hue wanders a little from line to line.", 0f, 3f, "0.00", "x", () => S().chromaPhaseNoise, v => S().chromaPhaseNoise = v);
            Slider("Cross-colour", "Rainbow shimmer on fine stripes when analog video is captured through a composite cable.", 0f, 3f, "0.00", "x", () => S().crossColour, v => S().crossColour = v);
            Slider("Head switching", "The bottom few lines of analog tape skew sideways with garbled colour where the video heads swap.",
                0f, 3f, "0.00", "x", () => S().headSwitching, v => S().headSwitching = v);
            Slider("Tracking noise bar", "A worn or mistracked tape: a band of noise and dropouts near the bottom of the picture. 0 = none.",
                0f, 1f, "0.00", "", () => S().trackingNoise, v => S().trackingNoise = v);
            Slider("DV compression", "MiniDV only: busy or noisy areas lose fine low-contrast detail in 8 x 8 blocks, with faint mosquito noise at sharp edges.",
                0f, 3f, "0.00", "x", () => S().blockiness, v => S().blockiness = v);
            Slider("Recording frame rate", "How often the recorded picture changes. The game still runs and responds at full speed; only the picture steps. 0 = every frame (a camcorder records 60 fields a second).",
                0f, 60f, "0", " fps", () => S().recordFps, v => S().recordFps = v < 12f ? 0f : Mathf.Round(v), true);
            Toggle("Interlaced fields", "Show the recording as fields, the way a TV or a simple capture shows 60i video: thin horizontal edges twitter up and down.",
                () => S().interlace, v => S().interlace = v);
            Slider("Line doubling", "Each recorded line is shown as a band instead of smoothly scaled, giving the stair-steps of standard-definition video on a big screen.",
                0f, 1f, "0.00", "", () => S().lineDoubling, v => S().lineDoubling = v);
            Slider("Bottom-edge line", "A thin, steady lilac line along the very bottom of the frame, as in the episode footage (a capture artefact).",
                0f, 1f, "0.00", "", () => S().bottomLine, v => S().bottomLine = v);

            Section("DISPLAY", "How the footage is shown (not part of the recording).");
            Toggle("4:3 frame", "The 4:3 shape of standard-definition video. Only the 4:3 part of the screen is rendered, so it is also faster.",
                () => S().aspect43, v => S().aspect43 = v);
            Toggle("Viewfinder OSD (REC, date, counter)", "The camcorder's viewfinder display: blinking REC, tape counter, battery and date. Shown on top, not recorded.",
                () => S().showOSD, v => S().showOSD = v);
            Slider("OSD eyepiece curvature", "Bends the viewfinder display slightly, as if read through the eyepiece. 0 keeps it flat.",
                0f, 2f, "0.00", "", () => S().osdCurvature, v => S().osdCurvature = v);
            Slider("CRT scanlines", "Dark lines between the recorded lines, as when the footage is shown on (or filmed off) a TV. A recording itself has none.",
                0f, 0.5f, "0.00", "", () => S().scanlines, v => S().scanlines = v);
        }

        /// <summary>A row of option buttons; the chosen one is highlighted.</summary>
        void Choice(string label, string tip, string[] options, Func<int> get, Action<int> set)
        {
            var row = new VisualElement(); row.AddToClassList("cc-row");
            var head = new VisualElement(); head.AddToClassList("cc-rowhead");
            head.Add(Text(label.ToUpperInvariant(), null));
            row.Add(head);
            var buttons = BtnRow(row);
            var list = new Button[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                list[i] = Button(buttons, options[i], () => { set(idx); for (int k = 0; k < list.Length; k++) list[k].EnableInClassList("cc-selected", k == idx); });
            }
            AttachTip(row, label, tip);
            _list.Add(row);
            _refreshers.Add(() => { int v = get(); for (int k = 0; k < list.Length; k++) list[k].EnableInClassList("cc-selected", k == v); });
        }

        void ApplyPreset(int i)
        {
            // Visual presets keep the current audio settings (audio has its own preset row).
            var p = CamcorderSettings.Preset(i);
            p.CopyAudioFrom(rig.Settings);
            rig.SetSettings(p);
            CamcorderSettings.CurrentPreset = i;
            MarkPreset();
            RefreshAll();
        }

        Button[] _presetButtons;

        /// <summary>Highlights the preset in use (what SET AS DEFAULT saves into).</summary>
        void MarkPreset()
        {
            if (_presetButtons == null) return;
            int cur = CamcorderSettings.CurrentPreset;
            for (int k = 0; k < _presetButtons.Length; k++) _presetButtons[k].EnableInClassList("cc-selected", k == cur);
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
