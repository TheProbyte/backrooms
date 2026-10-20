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
            Battery();
        }

        RectTransform Bar(string name, bool left)
        {
            var img = new GameObject(name).AddComponent<Image>();
            img.transform.SetParent(_canvas.transform, false);
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
            var canvasRect = (RectTransform)_canvas.transform;
            float w = canvasRect.rect.width, h = canvasRect.rect.height;
            float bar = pillarbox ? Mathf.Max(0f, (w - h * 4f / 3f) * 0.5f) : 0f;
            _barL.sizeDelta = new Vector2(bar, 0); _barR.sizeDelta = new Vector2(bar, 0);
            _frame.GetComponent<AspectRatioFitter>().aspectRatio = pillarbox ? 4f / 3f : w / Mathf.Max(1f, h);
            _content.gameObject.SetActive(visible);

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
