using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// F5 opens a left-hand panel of camcorder visual settings. Every slider writes straight into the live
    /// <see cref="CamcorderSettings"/>, so changes are visible immediately while the world keeps running.
    /// Settings are saved when the panel closes. Built with uGUI in code (no prefabs to maintain).
    /// </summary>
    public class CamcorderVisualMenu : MonoBehaviour
    {
        public CamcorderRig rig;
        public CamcorderPlayerController player;

        public bool IsOpen { get; private set; }

        Canvas _canvas;
        RectTransform _panel, _list;
        Font _font;
        readonly List<Action> _refreshers = new List<Action>();
        CursorLockMode _prevLock;

        static readonly Color PanelBg = new Color(0.05f, 0.05f, 0.045f, 0.95f);
        static readonly Color Accent = new Color(0.93f, 0.78f, 0.36f, 1f);
        static readonly Color TextCol = new Color(0.92f, 0.9f, 0.84f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.6f, 0.55f, 1f);

        void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            Build();
            SetOpen(false);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f5Key.wasPressedThisFrame) SetOpen(!IsOpen);
            else if (IsOpen && kb.escapeKey.wasPressedThisFrame) SetOpen(false);
        }

        public void SetOpen(bool open)
        {
            if (open == IsOpen && _panel && _panel.gameObject.activeSelf == open) return;
            IsOpen = open;
            if (_panel) _panel.gameObject.SetActive(open);
            CamcorderPlayerController.InputBlocked = open;
            if (open)
            {
                _prevLock = Cursor.lockState;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                RefreshAll();
            }
            else
            {
                if (rig) rig.Settings.Save();
                if (_prevLock == CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            }
        }

        // ---------------- Layout ----------------

        void Build()
        {
            var cgo = new GameObject("CamcorderMenu_Canvas");
            cgo.transform.SetParent(transform, false);
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            cgo.AddComponent<GraphicRaycaster>();

            _panel = Rect("Panel", cgo.transform);
            _panel.anchorMin = new Vector2(0, 0); _panel.anchorMax = new Vector2(0, 1); _panel.pivot = new Vector2(0, 0.5f);
            _panel.sizeDelta = new Vector2(460, 0); _panel.anchoredPosition = Vector2.zero;
            _panel.gameObject.AddComponent<Image>().color = PanelBg;

            var title = Label(_panel, "CAMCORDER  ·  VISUAL SETTINGS", 24, Accent, TextAnchor.MiddleLeft);
            Place(title.rectTransform, 20, -18, -20, 34);
            var hint = Label(_panel, "F5 / Esc to close.  Changes apply live and are saved.", 15, Dim, TextAnchor.MiddleLeft);
            Place(hint.rectTransform, 20, -50, -20, 22);

            // Presets row.
            var presets = Rect("Presets", _panel);
            Place(presets, 16, -80, -16, 34);
            var hl = presets.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6; hl.childForceExpandWidth = true; hl.childControlWidth = true; hl.childControlHeight = true;
            for (int i = 0; i < CamcorderSettings.PresetNames.Length; i++)
            {
                int idx = i;
                Button(presets, CamcorderSettings.PresetNames[i], () => ApplyPreset(idx));
            }

            // Scrollable list.
            var view = Rect("Scroll", _panel);
            view.anchorMin = new Vector2(0, 0); view.anchorMax = new Vector2(1, 1);
            view.offsetMin = new Vector2(10, 56); view.offsetMax = new Vector2(-10, -124);
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f); // raycast target for wheel
            scroll.horizontal = false; scroll.scrollSensitivity = 40f; scroll.movementType = ScrollRect.MovementType.Clamped;

            _list = Rect("List", view);
            _list.anchorMin = new Vector2(0, 1); _list.anchorMax = new Vector2(1, 1); _list.pivot = new Vector2(0.5f, 1);
            _list.offsetMin = _list.offsetMax = Vector2.zero;
            var vl = _list.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(10, 18, 6, 16); vl.spacing = 4;
            vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            _list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _list; scroll.viewport = view;

            // Footer.
            var footer = Rect("Footer", _panel);
            footer.anchorMin = new Vector2(0, 0); footer.anchorMax = new Vector2(1, 0); footer.pivot = new Vector2(0.5f, 0);
            footer.offsetMin = new Vector2(16, 12); footer.offsetMax = new Vector2(-16, 46);
            var fl = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            fl.spacing = 8; fl.childForceExpandWidth = true; fl.childControlWidth = true; fl.childControlHeight = true;
            Button(footer, "Reset to default", () => ApplyPreset(0));
            Button(footer, "Close", () => SetOpen(false));

            BuildControls();
        }

        void BuildControls()
        {
            CamcorderSettings S() => rig.Settings;

            Section("LENS", "Optics in front of the sensor.");
            Slider("Field of view (zoom)", 40f, 90f, "0", "°", () => S().fieldOfView, v => S().fieldOfView = v);
            Slider("Barrel distortion", -0.4f, 0.2f, "0.00", "", () => S().lensDistortion, v => S().lensDistortion = v);
            Slider("Chromatic aberration", 0f, 0.6f, "0.00", "", () => S().chromaticAberration, v => S().chromaticAberration = v);
            Slider("Vignette", 0f, 0.6f, "0.00", "", () => S().vignette, v => S().vignette = v);
            Slider("Bloom / halation", 0f, 1.5f, "0.00", "", () => S().bloom, v => S().bloom = v);
            Slider("Bloom spread", 0.2f, 0.95f, "0.00", "", () => S().bloomScatter, v => S().bloomScatter = v);

            Section("FOCUS", "Contrast autofocus drives depth of field from the centre of frame.");
            Toggle("Autofocus + depth of field", () => S().autofocus, v => S().autofocus = v);
            Slider("Aperture (f-number)", 1.4f, 16f, "0.0", "", () => S().aperture, v => S().aperture = v);
            Slider("Focus motor speed", 0.2f, 3f, "0.00", "x", () => S().focusSpeed, v => S().focusSpeed = v);
            Slider("Focus hunting", 0f, 1f, "0.00", "", () => S().focusHunting, v => S().focusHunting = v);

            Section("EXPOSURE / IRIS", "The auto-iris opens slowly in the dark and clamps down fast in bright light.");
            Toggle("Auto exposure", () => S().autoExposure, v => S().autoExposure = v);
            Slider("Exposure bias", -2f, 2f, "0.00", " EV", () => S().exposureBias, v => S().exposureBias = v);
            Slider("Iris speed", 0.1f, 3f, "0.00", "x", () => S().exposureSpeed, v => S().exposureSpeed = v);
            Slider("Max gain-up", 0f, 2.5f, "0.00", " EV", () => S().maxGain, v => S().maxGain = v);
            Slider("Fluorescent flicker beat", 0f, 1f, "0.00", "", () => S().fluorescentFlicker, v => S().fluorescentFlicker = v);

            Section("SENSOR NOISE", "Gain noise rises as the camera brightens dark scenes.");
            Slider("Base noise", 0f, 0.08f, "0.000", "", () => S().noise, v => S().noise = v);
            Slider("Noise per EV of gain", 0f, 0.15f, "0.000", "", () => S().gainNoise, v => S().gainNoise = v);
            Slider("Chroma noise (colour blotches)", 0f, 0.15f, "0.000", "", () => S().chromaNoise, v => S().chromaNoise = v);

            Section("IMAGE PROCESSING", "Camcorder DSP: soft optics, edge enhancement, subsampled colour.");
            Slider("Softness / resolution", 0f, 3f, "0.00", " px", () => S().softness, v => S().softness = v);
            Slider("Edge enhancement", 0f, 3f, "0.00", "", () => S().sharpening, v => S().sharpening = v);
            Slider("Chroma bleed", 0f, 10f, "0.0", " px", () => S().chromaBleed, v => S().chromaBleed = v);
            Slider("Chroma delay", -3f, 3f, "0.0", " px", () => S().chromaShift, v => S().chromaShift = v);
            Slider("Highlight knee", 0.6f, 1f, "0.00", "", () => S().highlightKnee, v => S().highlightKnee = v);
            Slider("Black pedestal", 0f, 0.1f, "0.000", "", () => S().pedestal, v => S().pedestal = v);

            Section("COLOUR", "White balance and grading.");
            Slider("White balance (temp)", -50f, 50f, "0", "", () => S().whiteBalanceTemp, v => S().whiteBalanceTemp = v);
            Slider("White balance (tint)", -50f, 50f, "0", "", () => S().whiteBalanceTint, v => S().whiteBalanceTint = v);
            Slider("Saturation", -60f, 40f, "0", "", () => S().saturation, v => S().saturation = v);
            Slider("Contrast", -30f, 50f, "0", "", () => S().contrast, v => S().contrast = v);

            Section("HANDHELD", "How the operator holds the camera.");
            Slider("Shake intensity", 0f, 2f, "0.00", "x", () => S().shakeIntensity, v => S().shakeIntensity = v);
            Slider("Breathing sway", 0f, 3f, "0.00", "x", () => S().breathing, v => S().breathing = v);
            Slider("Image stabilisation", 0f, 1f, "0.00", "", () => S().stabilization, v => S().stabilization = v);
            Slider("Motion blur (shutter)", 0f, 0.6f, "0.00", "", () => S().motionBlur, v => S().motionBlur = v);

            Section("FORMAT / TAPE", "Recording format and playback artefacts.");
            Toggle("4:3 frame (pillarbox)", () => S().aspect43, v => S().aspect43 = v);
            Toggle("Viewfinder OSD (REC, date, counter)", () => S().showOSD, v => S().showOSD = v);
            Slider("Scanlines", 0f, 0.3f, "0.00", "", () => S().scanlines, v => S().scanlines = v);
            Slider("Tape tracking noise", 0f, 1f, "0.00", "", () => S().trackingNoise, v => S().trackingNoise = v);
            Slider("Frame rate cap (0 = off)", 0f, 60f, "0", " fps", () => S().frameRateCap, v => S().frameRateCap = Mathf.RoundToInt(v), true);
        }

        void ApplyPreset(int i)
        {
            rig.SetSettings(CamcorderSettings.Preset(i));
            RefreshAll();
        }

        void RefreshAll() { foreach (var r in _refreshers) r(); }

        // ---------------- Widgets ----------------

        void Section(string title, string desc)
        {
            var box = Rect("Section_" + title, _list);
            var le = box.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 52;
            var t = Label(box, title, 17, Accent, TextAnchor.LowerLeft);
            Place(t.rectTransform, 0, -10, 0, 22);
            var d = Label(box, desc, 13, Dim, TextAnchor.UpperLeft);
            Place(d.rectTransform, 0, -32, 0, 18);
        }

        void Slider(string label, float min, float max, string fmt, string unit, Func<float> get, Action<float> set, bool whole = false)
        {
            var row = Rect("Row_" + label, _list);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
            var name = Label(row, label, 15, TextCol, TextAnchor.UpperLeft);
            Place(name.rectTransform, 0, 0, -90, 20);
            var value = Label(row, "", 15, Accent, TextAnchor.UpperRight);
            value.rectTransform.anchorMin = new Vector2(1, 1); value.rectTransform.anchorMax = new Vector2(1, 1);
            value.rectTransform.pivot = new Vector2(1, 1); value.rectTransform.sizeDelta = new Vector2(90, 20); value.rectTransform.anchoredPosition = Vector2.zero;

            var sgo = Rect("Slider", row);
            sgo.anchorMin = new Vector2(0, 0); sgo.anchorMax = new Vector2(1, 0); sgo.pivot = new Vector2(0.5f, 0);
            sgo.offsetMin = new Vector2(0, 4); sgo.offsetMax = new Vector2(0, 18);
            var bg = Rect("Track", sgo); Stretch(bg); bg.offsetMin = new Vector2(0, 5); bg.offsetMax = new Vector2(0, -5);
            bg.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.12f);
            var fillArea = Rect("FillArea", sgo); Stretch(fillArea); fillArea.offsetMin = new Vector2(0, 5); fillArea.offsetMax = new Vector2(-8, -5);
            var fill = Rect("Fill", fillArea); Stretch(fill);
            fill.gameObject.AddComponent<Image>().color = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
            var handleArea = Rect("HandleArea", sgo); Stretch(handleArea); handleArea.offsetMin = new Vector2(6, 0); handleArea.offsetMax = new Vector2(-6, 0);
            var handle = Rect("Handle", handleArea); handle.sizeDelta = new Vector2(12, 0);
            var himg = handle.gameObject.AddComponent<Image>(); himg.color = Accent;

            var s = sgo.gameObject.AddComponent<UnityEngine.UI.Slider>();
            s.fillRect = fill; s.handleRect = handle; s.targetGraphic = himg;
            s.minValue = min; s.maxValue = max; s.wholeNumbers = whole;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;

            void Show(float v) => value.text = v.ToString(fmt) + unit;
            bool syncing = false;
            s.onValueChanged.AddListener(v => { if (syncing) return; set(v); Show(v); });
            _refreshers.Add(() => { syncing = true; float v = get(); s.value = v; Show(v); syncing = false; });
        }

        void Toggle(string label, Func<bool> get, Action<bool> set)
        {
            var row = Rect("Toggle_" + label, _list);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
            var box = Rect("Box", row);
            box.anchorMin = box.anchorMax = new Vector2(0, 0.5f); box.pivot = new Vector2(0, 0.5f);
            box.sizeDelta = new Vector2(20, 20); box.anchoredPosition = Vector2.zero;
            var bimg = box.gameObject.AddComponent<Image>(); bimg.color = new Color(1, 1, 1, 0.14f);
            var check = Rect("Check", box); Stretch(check); check.offsetMin = new Vector2(4, 4); check.offsetMax = new Vector2(-4, -4);
            var cimg = check.gameObject.AddComponent<Image>(); cimg.color = Accent;
            var name = Label(row, label, 15, TextCol, TextAnchor.MiddleLeft);
            name.rectTransform.anchorMin = new Vector2(0, 0); name.rectTransform.anchorMax = new Vector2(1, 1);
            name.rectTransform.offsetMin = new Vector2(30, 0); name.rectTransform.offsetMax = Vector2.zero;

            var t = row.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            t.targetGraphic = bimg; t.graphic = cimg;
            bool syncing = false;
            t.onValueChanged.AddListener(v => { if (!syncing) set(v); });
            _refreshers.Add(() => { syncing = true; t.isOn = get(); syncing = false; });
        }

        void Button(RectTransform parent, string text, Action onClick)
        {
            var b = Rect("Button_" + text, parent);
            var img = b.gameObject.AddComponent<Image>(); img.color = new Color(1, 1, 1, 0.1f);
            var btn = b.gameObject.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            var colors = btn.colors; colors.highlightedColor = new Color(1, 0.9f, 0.6f, 1f); colors.pressedColor = Accent; btn.colors = colors;
            btn.onClick.AddListener(() => onClick());
            var l = Label(b, text, 14, TextCol, TextAnchor.MiddleCenter);
            Stretch(l.rectTransform);
        }

        Text Label(RectTransform parent, string text, int size, Color color, TextAnchor align)
        {
            var t = new GameObject("Text").AddComponent<Text>();
            t.transform.SetParent(parent, false);
            t.font = _font; t.fontSize = size; t.color = color; t.alignment = align; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        /// <summary>Top-anchored full-width placement: left inset, y from top, right inset, height.</summary>
        static void Place(RectTransform r, float left, float top, float right, float height)
        {
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
            r.offsetMin = new Vector2(left, top - height); r.offsetMax = new Vector2(right, top);
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }
    }
}
