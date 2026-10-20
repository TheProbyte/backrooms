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

        Canvas _canvas;
        RectTransform _frame, _barL, _barR, _content;
        Text _rec, _counter, _date, _time, _sp;
        Image _recDot;
        DateTime _start;
        float _t0;
        Font _font;

        void Awake()
        {
            if (!DateTime.TryParse(startDate, out _start)) _start = new DateTime(1998, 10, 19, 22, 41, 0);
            _t0 = Time.time;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "CamcorderOSD" };
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
            _counter = Label("0:00:00", new Vector2(1, 1), Vector2.zero, TextAnchor.UpperRight, 34);
            _time = Label("", new Vector2(0, 0), new Vector2(0, 44), TextAnchor.LowerLeft, 34);
            _date = Label("", new Vector2(0, 0), Vector2.zero, TextAnchor.LowerLeft, 34);
            _debug = Label("", new Vector2(1, 0), Vector2.zero, TextAnchor.LowerRight, 20);
            _debug.transform.SetParent(_barCanvas.transform, false);
            var dr2 = _debug.rectTransform; dr2.anchorMin = dr2.anchorMax = dr2.pivot = new Vector2(1, 0); dr2.anchoredPosition = new Vector2(-16, 12);
            Battery();
            SetupCurvedCanvas();
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
            if (_uiCam) EnsureTarget();
            var canvasRect = (RectTransform)_barCanvas.transform;
            float w = canvasRect.rect.width, h = canvasRect.rect.height;
            float bar = pillarbox ? Mathf.Max(0f, (w - h * 4f / 3f) * 0.5f) : 0f;
            _barL.sizeDelta = new Vector2(bar, 0); _barR.sizeDelta = new Vector2(bar, 0);
            _frame.GetComponent<AspectRatioFitter>().aspectRatio = pillarbox ? 4f / 3f : w / Mathf.Max(1f, h);
            _content.gameObject.SetActive(visible);
            _debug.enabled = showDebug;
            if (showDebug && Camera.main)
            {
                var p = Camera.main.transform.position;
                var ws = FindFirstObjectByType<Backrooms.Generation.WorldStreamer>();
                var ch = Backrooms.Generation.WorldStreamer.ChunkOf(p);
                _debug.text = $"pos {p.x:F1} {p.y:F1} {p.z:F1}   chunk {ch.x},{ch.y}   seed {(ws ? ws.Seed : 0)}   {1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):F0} fps";
            }

            float elapsed = Time.time - _t0;
            var now = _start.AddSeconds(elapsed);
            bool blink = (elapsed % 1.2f) < 0.8f;
            _recDot.enabled = blink;
            var span = TimeSpan.FromSeconds(elapsed);
            _counter.text = $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
            _date.text = now.ToString("MMM. dd yyyy").ToUpperInvariant();
            _time.text = (now.Hour >= 12 ? "PM " : "AM ") + now.ToString("h:mm:ss");
        }
    }
}
