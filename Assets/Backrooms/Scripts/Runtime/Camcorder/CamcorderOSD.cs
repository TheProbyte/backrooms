using System;
using UnityEngine;
using UnityEngine.UI;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// On-screen display burned into the viewfinder: blinking REC, tape counter, date/time stamp and battery,
    /// laid out inside the 4:3 frame when pillarboxing is on. Built entirely in code.
    /// </summary>
    public class CamcorderOSD : MonoBehaviour
    {
        public bool visible = true;
        public bool pillarbox = false;
        [Tooltip("Small debug readout (position, chunk, seed, fps) for reporting bugs.")]
        public bool showDebug = false;
        Text _debug;
        [Tooltip("Date the tape thinks it is. Time advances from here in real time.")]
        public string startDate = "1998-10-19 22:41:00";
        [Tooltip("STBY instead of a blinking REC (the main menu).")]
        public bool standby;
        /// <summary>Seconds already on the tape when this scene started (the world save's play time).</summary>
        public static float TapeSeconds;

        Canvas _canvas;
        RectTransform _frame, _barL, _barR, _content;
        Text _rec, _counter, _date, _time, _sp, _dev;
        Text _notice;
        static string s_notice;
        static float s_noticeAt = -100f;
        const float NoticeFor = 4f;

        /// <summary>A word stamped in the middle of the picture for a few seconds (HOME), shown with or without the viewfinder display.</summary>
        public static void Notice(string text) { s_notice = text; s_noticeAt = Time.unscaledTime; }

        /// <summary>The LOW LIGHT warning (0..1, faded in and out by Interaction.ShelfWatch while you look at a shelf that is
        /// not lit enough to be safe overnight), as real camcorders warned when the picture was too dark.</summary>
        public static float LowLight;
        Text _lowLight;

        /// <summary>The work orders pinned in the top right (Interaction.OrderTracker: rich text, "" for none), how much of
        /// it shows (0..1: J hides it, and hidden it pops up a moment when an order moves on), and its key's hint.</summary>
        public static string Tracker = "";
        public static float TrackerAlpha;
        public static string TrackerHint = "";
        Text _tracker;

        /// <summary>
        /// The side panels (2026-11-09, Docs/ImprovementPlan.md V1). The picture is 4:3: on a 16:9 screen a quarter of the
        /// screen is black bar, on a 32:9 one nearly two thirds, and the work list used to sit over the picture instead.
        /// With bars at least <see cref="MinSide"/> wide the list is drawn in them, as a camcorder's side panel: the work
        /// (next step, this stage's orders) in the right bar, your batches and what is in hand in the left. Inside the
        /// picture only the camcorder's own marks stay. <see cref="SideWidth"/> is a bar's width in the display's units
        /// (1080 high), 0 with no bars: Interaction.OrderTracker sizes its text by it.
        /// </summary>
        public static string SideRight = "", SideLeft = "";
        public static float SideAlpha = 1f, SideWidth;
        public const float MinSide = 200f;
        Text _sideR, _sideL;

        /// <summary>Money in (Interaction.Contracts.PayMoment): IN HAND counts up from one figure to the other over this
        /// long, bottom right of the picture, and stays a few seconds.</summary>
        public static void Money(int from, int to, float time) { s_moneyFrom = from; s_moneyTo = to; s_moneyTime = Mathf.Max(0.1f, time); s_moneyAt = Time.unscaledTime; }
        static int s_moneyFrom, s_moneyTo;
        static float s_moneyAt = -100f, s_moneyTime = 1f;
        const float MoneyFor = 5f;
        Text _money;

        /// <summary>
        /// The pay slip (2026-11-08, Interaction.Contracts.Punch): printed at punch-out and held up in front of you for a
        /// while, a paper slip with the day's lines (each "left\tright"; "--" a rule), as a day ends on its report in
        /// Supermarket Simulator. Shown flat over the picture (not through the eyepiece's curve), with or without the
        /// viewfinder display.
        /// </summary>
        public static void ShowSlip(string lines, float seconds = 14f) { s_slip = lines ?? ""; s_slipAt = Time.unscaledTime; s_slipFor = seconds; s_slipNew = true; }
        static string s_slip = "";
        static float s_slipAt = -100f, s_slipFor = 14f;
        static bool s_slipNew;
        RectTransform _slip;
        Text _slipLeft, _slipRight, _slipHead;
        Image _recDot;
        DateTime _start;
        float _t0;
        Font _font, _paper, _paperBold;

        void Awake()
        {
            if (!DateTime.TryParse(startDate, out _start)) _start = new DateTime(1998, 10, 19, 22, 41, 0);
            _t0 = Time.time;
            // The kit's two faces (UiKit, 2026-11-10): the camcorder's marks in its own, the side panels and the pay slip on
            // the company's paper.
            _font = Backrooms.UI.UiKit.Font(Backrooms.UI.UiKit.Face.Cam);
            _paper = Backrooms.UI.UiKit.Font(Backrooms.UI.UiKit.Face.Paper);
            _paperBold = Backrooms.UI.UiKit.Font(Backrooms.UI.UiKit.Face.Paper, true);
            Build();
        }

        // ---------------- Lens-curved rendering ----------------
        // The OSD is drawn by its own camera into a texture; the camcorder video pass composites it with a
        // slight barrel warp, so the text and icons bend like a viewfinder seen through an eyepiece.

        [Tooltip("Renderer (index in the URP asset) with no full-screen passes, used by the OSD camera. -1 = draw as a flat overlay.")]
        public int uiRendererIndex = -1;
        public const int OsdLayer = 5;   // Unity's built-in "UI" layer
        static readonly int OsdTexId = Shader.PropertyToID("_CamOSDTex");

        Camera _uiCam;
        RenderTexture _rt;
        Canvas _barCanvas;
        public bool Curved => _uiCam != null;

        void SetupCurvedCanvas()
        {
            if (uiRendererIndex < 0) return;
            var camGo = new GameObject("OSD_Camera");
            camGo.transform.SetParent(transform, false);
            _uiCam = camGo.AddComponent<Camera>();
            _uiCam.clearFlags = CameraClearFlags.SolidColor;
            _uiCam.backgroundColor = new Color(0, 0, 0, 0);
            _uiCam.cullingMask = 1 << OsdLayer;
            _uiCam.orthographic = true;
            _uiCam.depth = -50f;                      // before the main camera samples it
            _uiCam.nearClipPlane = 0.1f; _uiCam.farClipPlane = 10f;
            _uiCam.allowHDR = false; _uiCam.allowMSAA = false;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(_uiCam);
            data.SetRenderer(uiRendererIndex);
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresDepthTexture = false; data.requiresColorTexture = false;
            EnsureTarget();
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += ClearOsdTarget;
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = _uiCam;
            _canvas.planeDistance = 1f;
            SetLayer(_canvas.transform, OsdLayer);
        }

        void EnsureTarget()
        {
            int w = Mathf.Max(64, Screen.width), h = Mathf.Max(64, Screen.height);
            if (_rt && _rt.width == w && _rt.height == h) return;
            if (_rt) { _uiCam.targetTexture = null; _rt.Release(); Destroy(_rt); }
            // The render graph requires a depth buffer on a camera's output texture.
            _rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "CamcorderOSD" };
            _uiCam.targetTexture = _rt;
            Shader.SetGlobalTexture(OsdTexId, _rt);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        /// <summary>The bare OSD renderer doesn't clear its target, so wipe it to transparent each frame.</summary>
        void ClearOsdTarget(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _uiCam || !_rt) return;
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            RenderTexture.active = prev;
        }

        void OnDestroy()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= ClearOsdTarget;
            Shader.SetGlobalTexture(OsdTexId, Texture2D.blackTexture);
            if (_rt) { _rt.Release(); Destroy(_rt); }
        }

        void Build()
        {
            var go = new GameObject("CamcorderOSD_Canvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            // Pillarbox bars and the debug readout stay flat and crisp on their own overlay canvas.
            var bgo = new GameObject("CamcorderOSD_Bars");
            bgo.transform.SetParent(transform, false);
            _barCanvas = bgo.AddComponent<Canvas>();
            _barCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _barCanvas.sortingOrder = 49;
            var bs = bgo.AddComponent<CanvasScaler>();
            bs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            bs.referenceResolution = new Vector2(1920, 1080);
            bs.matchWidthOrHeight = 1f;

            _barL = Bar("PillarL", true);
            _barR = Bar("PillarR", false);

            _frame = NewRect("Frame", go.transform);
            Stretch(_frame);
            var fit = _frame.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 16f / 9f;

            _content = NewRect("Safe", _frame);
            _content.anchorMin = new Vector2(0.06f, 0.07f);
            _content.anchorMax = new Vector2(0.94f, 0.93f);
            _content.offsetMin = _content.offsetMax = Vector2.zero;

            _recDot = new GameObject("RecDot").AddComponent<Image>();
            _recDot.transform.SetParent(_content, false);
            var dr = _recDot.rectTransform;
            dr.anchorMin = dr.anchorMax = new Vector2(0, 1); dr.pivot = new Vector2(0, 1);
            dr.sizeDelta = new Vector2(22, 22); dr.anchoredPosition = new Vector2(0, -8);
            _recDot.color = new Color(0.95f, 0.12f, 0.1f, 0.95f);

            _rec = Label("REC", new Vector2(0, 1), new Vector2(32, 0), TextAnchor.UpperLeft, 34);
            _sp = Label("SP", new Vector2(0, 1), new Vector2(0, -46), TextAnchor.UpperLeft, 28);
            // The developer's tools are on (DevTools): a small DEV under SP, so a picture shows it was not the player's game.
            _dev = Label("DEV", new Vector2(0, 1), new Vector2(0, -84), TextAnchor.UpperLeft, 22);
            _counter = Label("0:00:00", new Vector2(1, 1), Vector2.zero, TextAnchor.UpperRight, 34);
            _time = Label("", new Vector2(0, 0), new Vector2(0, 44), TextAnchor.LowerLeft, 34);
            _date = Label("", new Vector2(0, 0), Vector2.zero, TextAnchor.LowerLeft, 34);
            _debug = Label("", new Vector2(1, 0), Vector2.zero, TextAnchor.LowerRight, 20);
            _lowLight = Label("LOW LIGHT", new Vector2(0, 0.5f), Vector2.zero, TextAnchor.MiddleLeft, 40);
            _tracker = Label("", new Vector2(1, 1), new Vector2(0, -96), TextAnchor.UpperRight, 22);
            _tracker.supportRichText = true;
            _tracker.font = _paper;   // the work list: company paper, as in the side panels
            _tracker.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tracker.lineSpacing = 1.05f;
            _tracker.rectTransform.sizeDelta = new Vector2(700, 640);
            // Smaller text over bright walls: a dark rim all round as well as the drop shadow.
            var rim = _tracker.gameObject.AddComponent<Outline>();
            rim.effectColor = new Color(0, 0, 0, 0.8f); rim.effectDistance = new Vector2(1.5f, -1.5f);
            _money = Label("", new Vector2(1, 0), Vector2.zero, TextAnchor.LowerRight, 40);
            _money.enabled = false;
            _sideR = SidePanel("SideRight");
            _sideL = SidePanel("SideLeft");
            _notice = Label("", new Vector2(0.5f, 0.5f), new Vector2(0, -60), TextAnchor.MiddleCenter, 72);
            _notice.transform.SetParent(_barCanvas.transform, false);
            var nr = _notice.rectTransform; nr.anchorMin = nr.anchorMax = nr.pivot = new Vector2(0.5f, 0.32f); nr.anchoredPosition = Vector2.zero;
            // (2026-11-09, the owner: "the huge text on screen is always cut off". It was one line of 72 that never wrapped,
            // so a long notice ran off both sides. Now it wraps inside the picture and shrinks to fit: two lines at most.)
            _notice.horizontalOverflow = HorizontalWrapMode.Wrap;
            _notice.verticalOverflow = VerticalWrapMode.Truncate;
            _notice.resizeTextForBestFit = true;
            _notice.resizeTextMinSize = 26; _notice.resizeTextMaxSize = 56;
            _notice.lineSpacing = 1f;
            _debug.transform.SetParent(_barCanvas.transform, false);
            var dr2 = _debug.rectTransform; dr2.anchorMin = dr2.anchorMax = dr2.pivot = new Vector2(1, 0); dr2.anchoredPosition = new Vector2(-16, 12);
            Battery();
            BuildSlip();
            SetupCurvedCanvas();
        }

        void BuildSlip()
        {
            var paper = new GameObject("PaySlip").AddComponent<Image>();
            paper.transform.SetParent(_barCanvas.transform, false);
            paper.color = Backrooms.UI.UiKit.Cream;
            _slip = paper.rectTransform;
            _slip.anchorMin = _slip.anchorMax = new Vector2(0.5f, 0f);
            _slip.pivot = new Vector2(0.5f, 0f);
            _slip.sizeDelta = new Vector2(560, 400);
            var sh = paper.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.45f); sh.effectDistance = new Vector2(6, -6);
            // The company's amber band along the top, its name on it.
            var band = new GameObject("Band").AddComponent<Image>();
            band.transform.SetParent(_slip, false);
            band.color = Backrooms.UI.UiKit.Amber;
            var br = band.rectTransform;
            br.anchorMin = new Vector2(0, 1); br.anchorMax = new Vector2(1, 1); br.pivot = new Vector2(0.5f, 1);
            br.sizeDelta = new Vector2(0, 44); br.anchoredPosition = Vector2.zero;
            var ink = Backrooms.UI.UiKit.Choc;
            Text T(string name, Transform parent, TextAnchor align, int size)
            {
                var t = new GameObject(name).AddComponent<Text>();
                t.transform.SetParent(parent, false);
                t.font = _paper; t.fontSize = size; t.alignment = align; t.color = ink; t.lineSpacing = 1.1f;
                t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
                var r = t.rectTransform;
                r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
                return t;
            }
            _slipHead = T("Head", band.transform, TextAnchor.MiddleCenter, 24);
            _slipHead.font = _paperBold;
            _slipHead.text = "OVERFLOW  -  PAY SLIP";
            var hr = _slipHead.rectTransform; hr.anchorMin = Vector2.zero; hr.anchorMax = Vector2.one; hr.offsetMin = hr.offsetMax = Vector2.zero;
            _slipLeft = T("Left", _slip, TextAnchor.UpperLeft, 21);
            _slipRight = T("Right", _slip, TextAnchor.UpperRight, 21);
            foreach (var t in new[] { _slipLeft, _slipRight })
            {
                var r = t.rectTransform;
                r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(26, 14); r.offsetMax = new Vector2(-26, -58);
            }
            _slip.gameObject.SetActive(false);
        }

        /// <summary>The slip: slides up from the bottom, stays, slides away.</summary>
        void UpdateSlip()
        {
            if (!_slip) return;
            float t = Time.unscaledTime - s_slipAt;
            bool on = !string.IsNullOrEmpty(s_slip) && t < s_slipFor;
            _slip.gameObject.SetActive(on);
            if (!on) return;
            if (s_slipNew)
            {
                s_slipNew = false;
                var left = new System.Text.StringBuilder();
                var right = new System.Text.StringBuilder();
                int n = 0;
                foreach (var line in s_slip.Split('\n'))
                {
                    int tab = line.IndexOf('\t');
                    string l = tab >= 0 ? line.Substring(0, tab) : line, r = tab >= 0 ? line.Substring(tab + 1) : "";
                    bool rule = l == "--";
                    bool total = l == "TODAY" || l == "IN HAND";
                    if (n > 0) { left.Append('\n'); right.Append('\n'); }
                    if (rule) { left.Append("<color=#3b2a1e66>- - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -</color>"); }
                    else if (total) { left.Append("<b>").Append(l).Append("</b>"); right.Append("<b>").Append(r).Append("</b>"); }
                    else
                    {
                        left.Append(l);
                        // LOST in red, money taken off in red.
                        right.Append(r == "LOST" || r.StartsWith("-") ? "<color=#b8402a>" + r + "</color>" : r);
                    }
                    n++;
                }
                _slipLeft.supportRichText = _slipRight.supportRichText = true;
                _slipLeft.text = left.ToString();
                _slipRight.text = right.ToString();
                _slip.sizeDelta = new Vector2(620, 58 + 30 + n * 25.5f);
            }
            // In over 0.35 s, out over the last 0.5 s.
            float k = Mathf.Min(Mathf.SmoothStep(0f, 1f, t / 0.35f), Mathf.SmoothStep(0f, 1f, (s_slipFor - t) / 0.5f));
            _slip.anchoredPosition = new Vector2(-380f, Mathf.Lerp(-_slip.sizeDelta.y - 20f, 215f, k));
            _slip.localRotation = Quaternion.Euler(0f, 0f, 1.2f);
        }

        /// <summary>A side panel's text, flat on the bars' canvas (over the black bar it sits in).</summary>
        Text SidePanel(string name)
        {
            var t = new GameObject(name).AddComponent<Text>();
            t.transform.SetParent(_barCanvas.transform, false);
            t.font = _paper; t.fontSize = 22; t.alignment = TextAnchor.UpperLeft; t.supportRichText = true;
            t.color = new Color(Backrooms.UI.UiKit.Cream.r, Backrooms.UI.UiKit.Cream.g, Backrooms.UI.UiKit.Cream.b, 0.92f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.lineSpacing = 1.08f;
            t.raycastTarget = false;
            t.enabled = false;
            return t;
        }

        RectTransform Bar(string name, bool left)
        {
            var img = new GameObject(name).AddComponent<Image>();
            img.transform.SetParent(_barCanvas.transform, false);
            img.color = Color.black;
            var r = img.rectTransform;
            r.anchorMin = new Vector2(left ? 0 : 1, 0); r.anchorMax = new Vector2(left ? 0 : 1, 1);
            r.pivot = new Vector2(left ? 0 : 1, 0.5f);
            r.sizeDelta = new Vector2(0, 0);
            return r;
        }

        void Battery()
        {
            var root = NewRect("Battery", _content);
            root.anchorMin = root.anchorMax = new Vector2(1, 1); root.pivot = new Vector2(1, 1);
            root.anchoredPosition = new Vector2(0, -50); root.sizeDelta = new Vector2(56, 24);
            // Outline from four thin bars.
            var white = new Color(1, 1, 1, 0.9f);
            Piece(root, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 2), white);
            Piece(root, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -2), new Vector2(0, 0), white);
            Piece(root, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(2, 0), white);
            Piece(root, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-2, 0), new Vector2(0, 0), white);
            for (int i = 0; i < 4; i++)
            {
                var cell = new GameObject("Cell").AddComponent<Image>();
                cell.transform.SetParent(root, false);
                var r = cell.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0, 0.5f); r.pivot = new Vector2(0, 0.5f);
                r.sizeDelta = new Vector2(10, 16); r.anchoredPosition = new Vector2(4 + i * 12, 0);
                cell.color = i < 3 ? new Color(1, 1, 1, 0.92f) : new Color(1, 1, 1, 0.15f);
                cell.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.6f);
            }
            var tip = new GameObject("Tip").AddComponent<Image>();
            tip.transform.SetParent(root, false);
            var tr = tip.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(1, 0.5f); tr.pivot = new Vector2(0, 0.5f);
            tr.sizeDelta = new Vector2(4, 10); tr.anchoredPosition = new Vector2(2, 0);
            tip.color = new Color(1, 1, 1, 0.9f);
        }

        static void Piece(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color c)
        {
            var img = new GameObject("Edge").AddComponent<Image>();
            img.transform.SetParent(parent, false);
            var r = img.rectTransform;
            r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = oMin; r.offsetMax = oMax;
            img.color = c;
        }

        Text Label(string text, Vector2 anchor, Vector2 pos, TextAnchor align, int size)
        {
            var t = new GameObject("OSD_" + text).AddComponent<Text>();
            t.transform.SetParent(_content, false);
            t.font = _font; t.fontSize = size; t.alignment = align; t.text = text;
            t.color = new Color(1f, 1f, 1f, 0.92f);
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var r = t.rectTransform;
            r.anchorMin = r.anchorMax = anchor; r.pivot = anchor;
            r.anchoredPosition = pos; r.sizeDelta = new Vector2(500, 44);
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.75f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        // The display's ink: white, or in the Overflow art style the company's warm cream with a chocolate shadow
        // (2026-11-07, the colours of the menus; the REC dot stays red).
        Color _ink = Color.white;
        int _inkShown = -1;

        void ApplyInk()
        {
            int ov = Backrooms.Generation.ArtStyle.Current == Backrooms.Generation.ArtStyle.Look.Overflow ? 1 : 0;
            if (ov == _inkShown) return;
            _inkShown = ov;
            _ink = ov == 1 ? Backrooms.UI.UiKit.Cream : Color.white;
            var shadow = ov == 1 ? new Color(30 / 255f, 18 / 255f, 8 / 255f, 0.8f) : new Color(0, 0, 0, 0.75f);
            foreach (var root in new Transform[] { _content, _notice.transform, _debug.transform })
            {
                foreach (var g in root.GetComponentsInChildren<Graphic>(true))
                    if (g != _recDot) g.color = new Color(_ink.r, _ink.g, _ink.b, g.color.a);
                foreach (var sh in root.GetComponentsInChildren<Shadow>(true))
                    sh.effectColor = new Color(shadow.r, shadow.g, shadow.b, sh.effectColor.a > 0.65f ? shadow.a : sh.effectColor.a);
            }
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        void LateUpdate()
        {
            if (!_canvas) return;
            if (_uiCam) { EnsureTarget(); _uiCam.enabled = visible; }   // nothing to draw while hidden
            var canvasRect = (RectTransform)_barCanvas.transform;
            float w = canvasRect.rect.width, h = canvasRect.rect.height;
            float bar = pillarbox ? Mathf.Max(0f, (w - h * 4f / 3f) * 0.5f) : 0f;
            _barL.sizeDelta = new Vector2(bar, 0); _barR.sizeDelta = new Vector2(bar, 0);
            _frame.GetComponent<AspectRatioFitter>().aspectRatio = pillarbox ? 4f / 3f : w / Mathf.Max(1f, h);
            _content.gameObject.SetActive(visible);
            ApplyInk();
            _debug.enabled = showDebug;
            _dev.enabled = DevTools.On;
            UpdateSlip();
            // A notice given while the slip is up (the new morning's NEW WORK) waits until it has gone.
            if (_slip && _slip.gameObject.activeSelf && s_noticeAt >= s_slipAt) s_noticeAt = Time.unscaledTime;
            float na = Time.unscaledTime - s_noticeAt;
            if (_slip && _slip.gameObject.activeSelf && s_noticeAt >= s_slipAt) na = NoticeFor;    // (held back: hidden)
            _notice.enabled = na < NoticeFor && !string.IsNullOrEmpty(s_notice);
            if (_notice.enabled)
            {
                _notice.text = s_notice;
                // As wide as the picture less a margin (the picture is 16:9 or 4:3 inside whatever window).
                var cr = ((RectTransform)_barCanvas.transform).rect;
                float pw = Mathf.Min(cr.width, cr.height * (_frame ? _frame.GetComponent<AspectRatioFitter>().aspectRatio : 16f / 9f));
                _notice.rectTransform.sizeDelta = new Vector2(pw * 0.8f, 150f);
                // Steady, then a few blinks out, like a camcorder's title.
                bool show = na < NoticeFor - 1f || ((na * 4f) % 1f) < 0.6f;
                _notice.color = new Color(_ink.r, _ink.g, _ink.b, show ? 0.95f : 0f);
            }
            // LOW LIGHT: faded in, then a slow blink while it stays.
            if (_lowLight)
            {
                bool on = LowLight > 0.01f;
                _lowLight.enabled = on;
                if (on) _lowLight.color = new Color(_ink.r, _ink.g, _ink.b, 0.95f * LowLight * (LowLight < 0.99f || ((Time.unscaledTime * 1.25f) % 1f) < 0.7f ? 1f : 0.35f));
            }
            // Money in: the figure counts up, holds, and goes.
            if (_money)
            {
                float mt = Time.unscaledTime - s_moneyAt;
                _money.enabled = mt < MoneyFor;
                if (_money.enabled)
                {
                    float k = Mathf.Clamp01(mt / s_moneyTime);
                    int shown = Mathf.RoundToInt(Mathf.Lerp(s_moneyFrom, s_moneyTo, k));
                    _money.text = $"IN HAND  ${shown}";
                    float fade = Mathf.Clamp01((MoneyFor - mt) / 0.6f);
                    // (A little larger while it counts, settling as it lands.)
                    _money.fontSize = k < 1f ? 44 : 40;
                    _money.color = new Color(_ink.r, _ink.g, _ink.b, 0.95f * fade);
                }
            }
            // The side panels: the work in the right bar, your batches in the left, hard against the picture.
            SideWidth = bar;
            bool side = bar >= MinSide && visible;
            if (_sideR && _sideL)
            {
                bool on = side && SideAlpha > 0.01f;
                _sideR.enabled = on && !string.IsNullOrEmpty(SideRight);
                _sideL.enabled = on && !string.IsNullOrEmpty(SideLeft);
                if (on)
                {
                    float pw = Mathf.Min(bar - 36f, 560f), ph = h - 150f;
                    var rr = _sideR.rectTransform;
                    rr.anchorMin = rr.anchorMax = new Vector2(1, 1); rr.pivot = new Vector2(0, 1);
                    rr.anchoredPosition = new Vector2(-bar + 18f, -72f); rr.sizeDelta = new Vector2(pw, ph);
                    var lr = _sideL.rectTransform;
                    lr.anchorMin = lr.anchorMax = new Vector2(0, 1); lr.pivot = new Vector2(1, 1);
                    lr.anchoredPosition = new Vector2(bar - 18f, -72f); lr.sizeDelta = new Vector2(pw, ph);
                    _sideR.text = SideRight; _sideL.text = SideLeft;
                    var c = new Color(Backrooms.UI.UiKit.Cream.r, Backrooms.UI.UiKit.Cream.g, Backrooms.UI.UiKit.Cream.b, 0.92f * SideAlpha);
                    _sideR.color = c; _sideL.color = c;
                }
            }
            // The work orders in the picture, top right: only with no bars to put them in (a narrow window, 4:3 off).
            if (_tracker)
            {
                bool any = !string.IsNullOrEmpty(Tracker) && !side;
                bool full = any && TrackerAlpha > 0.01f;
                _tracker.enabled = any;
                if (any)
                {
                    _tracker.text = full ? Tracker + "\n<size=19>" + TrackerHint + "</size>" : "<size=19>" + TrackerHint + "</size>";
                    _tracker.color = new Color(_ink.r, _ink.g, _ink.b, full ? 0.92f * TrackerAlpha : 0.5f);
                }
            }
            if (showDebug && Camera.main)
            {
                var p = Camera.main.transform.position;
                var ws = FindAnyObjectByType<Backrooms.Generation.WorldStreamer>();
                var ch = Backrooms.Generation.WorldStreamer.ChunkOf(p);
                _debug.text = $"pos {p.x:F1} {p.y:F1} {p.z:F1}   chunk {ch.x},{ch.y}   seed {(ws ? ws.Seed : 0)}   {1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):F0} fps";
            }

            float elapsed = standby ? 0f : Time.time - _t0 + TapeSeconds;
            var now = _start.AddSeconds(standby ? Time.time - _t0 : elapsed);
            // A contract's office hours (Interaction.Contracts): the clock shows the shift's time of day.
            var shift = Backrooms.Interaction.Contracts.Instance ? Backrooms.Interaction.Contracts.Instance.Clock : null;
            if (shift.HasValue) now = shift.Value;
            bool blink = ((Time.time - _t0) % 1.2f) < 0.8f;
            _recDot.enabled = blink && !standby;
            _rec.text = standby ? "STBY" : "REC";
            _rec.rectTransform.anchoredPosition = new Vector2(standby ? 0 : 32, 0);
            var span = TimeSpan.FromSeconds(elapsed);
            _counter.text = $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
            _date.text = now.ToString("MMM. dd yyyy").ToUpperInvariant();
            _time.text = (now.Hour >= 12 ? "PM " : "AM ") + now.ToString("h:mm:ss");
        }
    }
}
