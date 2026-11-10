using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Backrooms.Interaction;

namespace Backrooms.UI
{
    /// <summary>
    /// The side panels drawn as the camcorder's side display (Docs/UiPlan.md U4, 2026-11-10). They were plain wrapped
    /// text; now, in the black bars beside the 4:3 picture:
    /// <list type="bullet">
    /// <item><b>Left, your batches:</b> the date, IN HAND large, whether business time runs; then a card per batch: the
    /// client's colour and pictogram (the marks on their boxes), how many, what to do now in a word or two (green
    /// stored, amber take back, red late), the time left, and a bar that drains with it.</item>
    /// <item><b>Right, the work:</b> the next step; this stage's orders as rows with a tick and a filling bar instead of
    /// "12/20"; what finishing them unlocks; the J key to hide it all.</item>
    /// </list>
    /// Narrow bars (a 16:10 screen) get the left panel only, compact (pictogram, count, time, bar), and the work goes in
    /// the picture as before. Built once in uGUI on the bars' canvas and laid out again four times a second.
    /// </summary>
    public class SidePanels : MonoBehaviour
    {
        /// <summary>Bars at least this wide show both panels; from <see cref="MinCompact"/> the left one, compact.</summary>
        public const float MinFull = 200f, MinCompact = 110f;

        RectTransform _left, _right;
        Font _font, _bold;
        readonly Contracts.SideView _view = new Contracts.SideView();
        float _next;

        // Left
        Text _date, _inHandLabel, _money, _running, _batchesLabel;
        readonly List<Card> _cards = new List<Card>();
        readonly List<Text> _notes = new List<Text>();
        // Right
        Text _nextLabel, _nextText, _stage, _stageCount, _unlocks, _hideKey, _hideText;
        Image _hideCap;
        readonly List<Row> _rows = new List<Row>();

        sealed class Card
        {
            public RectTransform root;
            public Image bg, stripe, mark, barBg, barFill;
            public Text name, count, state, time;
        }

        sealed class Row
        {
            public RectTransform root;
            public Image box, tick, barBg, barFill;
            public Text title, count;
        }

        public void Build(Transform canvas, Font font, Font bold)
        {
            _font = font; _bold = bold;
            _left = Rect("SidePanelLeft", canvas);
            _right = Rect("SidePanelRight", canvas);
            _leftGroup = _left.gameObject.AddComponent<CanvasGroup>(); _rightGroup = _right.gameObject.AddComponent<CanvasGroup>();
            _leftGroup.blocksRaycasts = _rightGroup.blocksRaycasts = false;
            _date = Txt(_left, _bold, UiKit.Small, UiKit.Amber);
            _inHandLabel = Txt(_left, _font, UiKit.Small, UiKit.Tan);
            _inHandLabel.text = "IN HAND";
            _money = Txt(_left, _bold, 38, UiKit.Cream);
            _running = Txt(_left, _font, UiKit.Small, UiKit.Tan);
            _batchesLabel = Txt(_left, _bold, UiKit.Small, UiKit.Amber);
            _batchesLabel.text = "YOUR BATCHES";
            _nextLabel = Txt(_right, _bold, UiKit.Small, UiKit.Amber);
            _nextLabel.text = "NEXT";
            _nextText = Txt(_right, _bold, 20, UiKit.Cream);
            _stage = Txt(_right, _bold, UiKit.Small, UiKit.Amber);
            _stageCount = Txt(_right, _font, UiKit.Small, UiKit.Tan);
            _stageCount.alignment = TextAnchor.UpperRight;
            _unlocks = Txt(_right, _font, UiKit.Small, UiKit.Tan);
            _hideCap = Img(_right, UiKit.RoundRingSprite, UiKit.Tan);
            _hideKey = Txt(_hideCap.rectTransform, _bold, UiKit.Small, UiKit.Tan);
            _hideKey.alignment = TextAnchor.MiddleCenter;
            _hideKey.text = "J";
            _hideText = Txt(_right, _font, UiKit.Small, UiKit.Tan);
            _hideText.text = "HIDE";
            SetActive(_left, false); SetActive(_right, false);
        }

        static void SetActive(RectTransform r, bool on) { if (r && r.gameObject.activeSelf != on) r.gameObject.SetActive(on); }

        /// <summary>Each frame from the camcorder's overlay: the bars' width (canvas units), whether each panel shows, how
        /// much (J hides them, and hidden they pop up a moment when something moves on).</summary>
        public void Layout(float bar, bool leftOn, bool rightOn, float alpha)
        {
            if (!_left) return;
            bool compact = bar < MinFull;
            leftOn &= bar >= MinCompact && alpha > 0.01f;
            rightOn &= !compact && alpha > 0.01f;
            SetActive(_left, leftOn); SetActive(_right, rightOn);
            if (!leftOn && !rightOn) return;
            if (Time.unscaledTime >= _next || _view.date.Length == 0)
            {
                _next = Time.unscaledTime + 0.25f;
                if (Contracts.Instance) Contracts.Instance.FillSide(_view);
                else { _view.batches.Clear(); _view.orders.Clear(); _view.notes.Clear(); _view.date = ""; }
            }
            float pad = compact ? 12f : 18f;
            float w = Mathf.Min(bar - pad * 2f, 420f);
            if (leftOn)
            {
                _left.anchorMin = _left.anchorMax = new Vector2(0, 1); _left.pivot = new Vector2(0, 1);
                _left.anchoredPosition = new Vector2(bar - pad - w, -64f);
                _left.sizeDelta = new Vector2(w, 900f);
                LayoutLeft(w, compact);
            }
            if (rightOn)
            {
                _right.anchorMin = _right.anchorMax = new Vector2(1, 1); _right.pivot = new Vector2(0, 1);
                _right.anchoredPosition = new Vector2(-bar + pad, -64f);
                _right.sizeDelta = new Vector2(w, 900f);
                LayoutRight(w);
            }
            _leftGroup.alpha = _rightGroup.alpha = alpha;
        }

        CanvasGroup _leftGroup, _rightGroup;

        void LayoutLeft(float w, bool compact)
        {
            var v = _view;
            float y = 0f;
            y = Put(_date, 0f, y, w, v.date) - 6f;
            y = Put(_inHandLabel, 0f, y, w, compact ? "" : "IN HAND") - 0f;
            _money.fontSize = compact ? 28 : 38;
            y = Put(_money, 0f, y, w, "$" + v.pay) - 4f;
            _running.color = v.running ? UiKit.Good : UiKit.Tan;
            y = Put(_running, 0f, y, w, compact ? "" : v.running ? "BUSINESS TIME RUNNING" : "TIME STOPPED - OFF THE FLOOR") - 22f;
            y = Put(_batchesLabel, 0f, y, w, v.batches.Count > 0 && !compact ? "YOUR BATCHES" : "") - 8f;
            int i = 0;
            float maxY = -820f;
            for (; i < v.batches.Count && y > maxY; i++)
            {
                var c = CardAt(i);
                y = LayoutCard(c, v.batches[i], y, w, compact) - 10f;
            }
            for (int k = i; k < _cards.Count; k++) _cards[k].root.gameObject.SetActive(false);
            int n = 0;
            if (!compact)
                foreach (var note in v.notes)
                {
                    if (y < maxY) break;
                    var t = NoteAt(n++);
                    y = Put(t, 0f, y, w, note) - 6f;
                }
            for (int k = n; k < _notes.Count; k++) _notes[k].gameObject.SetActive(false);
        }

        float LayoutCard(Card c, Contracts.BatchView b, float y, float w, bool compact)
        {
            c.root.gameObject.SetActive(true);
            float h = compact ? 62f : 74f;
            Place(c.root, 0f, y, w, h);
            var col = UiKit.Client(b.client);
            c.bg.color = new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, b.dim ? 0.04f : 0.08f);
            c.stripe.color = col;
            c.mark.color = Color.Lerp(col, Color.white, 0.25f);
            float ms = compact ? 24f : 28f;
            Place(c.mark.rectTransform, 14f, -9f, ms, ms);
            if (compact)
            {
                c.name.text = "";
                Put(c.count, 14f + ms + 6f, -9f, w - ms - 26f, "×" + b.count);
                Put(c.state, 14f, -38f, w - 22f, "");
                c.time.alignment = TextAnchor.UpperLeft;
                Put(c.time, 14f, -38f, w - 22f, b.time.Length > 0 ? b.time : b.state);
            }
            else
            {
                float nameW = w - 14f - ms - 8f - 50f;
                Put(c.name, 14f + ms + 8f, -11f, nameW, b.name + (b.rush ? " RUSH" : ""));
                c.count.alignment = TextAnchor.UpperRight;
                Put(c.count, w - 58f, -11f, 48f, "×" + b.count);
                Put(c.state, 14f, -44f, w - 80f, b.state);
                c.time.alignment = TextAnchor.UpperRight;
                Put(c.time, w - 80f, -44f, 70f, b.time);
            }
            c.state.color = b.ink; c.time.color = b.ink;
            c.name.color = c.count.color = b.dim ? UiKit.Tan : UiKit.Cream;
            bool bar = b.fill >= 0f;
            c.barBg.gameObject.SetActive(bar);
            if (bar)
            {
                Place(c.barBg.rectTransform, 14f, -(h - 10f), w - 24f, 5f);
                c.barFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(b.fill), 1f);
                c.barFill.color = b.ink;
            }
            return y - h;
        }

        void LayoutRight(float w)
        {
            var v = _view;
            float y = 0f;
            bool hasNext = v.next.Length > 0;
            y = Put(_nextLabel, 0f, y, w, hasNext ? "NEXT" : "") - 2f;
            y = Put(_nextText, 0f, y, w, v.next) - (hasNext ? 22f : 0f);
            bool orders = v.orders.Count > 0;
            Put(_stageCount, w - 50f, y, 50f, orders ? $"{v.stageDone}/{v.stageTotal}" : "");
            y = Put(_stage, 0f, y, w - 56f, orders ? v.stage : "") - 10f;
            int i = 0;
            for (; i < v.orders.Count; i++) y = LayoutRow(RowAt(i), v.orders[i], y, w) - 8f;
            for (int k = i; k < _rows.Count; k++) _rows[k].root.gameObject.SetActive(false);
            y = Put(_unlocks, 0f, y - 4f, w, orders && v.unlocks.Length > 0 ? "UNLOCKS " + v.unlocks : "") - 26f;
            Place(_hideCap.rectTransform, 0f, y, 28f, 28f);
            Place(_hideKey.rectTransform, 0f, 0f, 28f, 28f);
            _hideKey.rectTransform.anchorMin = Vector2.zero; _hideKey.rectTransform.anchorMax = Vector2.one;
            _hideKey.rectTransform.offsetMin = _hideKey.rectTransform.offsetMax = Vector2.zero;
            Put(_hideText, 36f, y - 4f, w - 36f, "HIDE");
        }

        float LayoutRow(Row r, Contracts.OrderView o, float y, float w)
        {
            r.root.gameObject.SetActive(true);
            float countW = o.need > 0 && !o.done ? 64f : 0f;
            r.title.color = o.done ? UiKit.Good : UiKit.Cream;
            float th = Measure(r.title, w - 26f - countW, o.title);
            float h = Mathf.Max(18f, th) + (o.need > 0 && !o.done ? 12f : 0f);
            Place(r.root, 0f, y, w, h);
            Place(r.box.rectTransform, 0f, -2f, 16f, 16f);
            r.box.color = o.done ? UiKit.Good : UiKit.Cream;
            r.tick.gameObject.SetActive(o.done);
            r.tick.color = UiKit.Good;
            Put(r.title, 26f, 0f, w - 26f - countW, o.title);
            r.count.alignment = TextAnchor.UpperRight;
            Put(r.count, w - countW, 0f, countW, countW > 0f ? $"{o.have}/{o.need}" : "");
            bool bar = o.need > 0 && !o.done;
            r.barBg.gameObject.SetActive(bar);
            if (bar)
            {
                Place(r.barBg.rectTransform, 26f, -(th + 5f), w - 26f, 4f);
                r.barFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(o.have / (float)o.need), 1f);
            }
            return y - h;
        }

        // ---- Pools ----

        Card CardAt(int i)
        {
            while (_cards.Count <= i)
            {
                var c = new Card { root = Rect("Card", _left) };
                c.bg = Img(c.root, UiKit.RoundFillSprite, Color.white); Stretch(c.bg.rectTransform);
                c.stripe = Img(c.root, null, Color.white);
                c.stripe.rectTransform.anchorMin = new Vector2(0, 0); c.stripe.rectTransform.anchorMax = new Vector2(0, 1);
                c.stripe.rectTransform.pivot = new Vector2(0, 0.5f); c.stripe.rectTransform.sizeDelta = new Vector2(5, -6); c.stripe.rectTransform.anchoredPosition = new Vector2(0, 0);
                c.mark = Img(c.root, null, Color.white); c.mark.sprite = null;
                c.name = Txt(c.root, _bold, 20, UiKit.Cream); c.name.horizontalOverflow = HorizontalWrapMode.Overflow;
                c.count = Txt(c.root, _bold, 20, UiKit.Cream);
                c.state = Txt(c.root, _bold, UiKit.Small, UiKit.Amber); c.state.horizontalOverflow = HorizontalWrapMode.Overflow;
                c.time = Txt(c.root, _bold, UiKit.Small, UiKit.Amber);
                c.barBg = Img(c.root, null, new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, 0.15f));
                c.barFill = Img(c.barBg.rectTransform, null, Color.white);
                c.barFill.rectTransform.anchorMin = Vector2.zero; c.barFill.rectTransform.anchorMax = Vector2.one;
                c.barFill.rectTransform.offsetMin = c.barFill.rectTransform.offsetMax = Vector2.zero; c.barFill.rectTransform.pivot = new Vector2(0, 0.5f);
                _cards.Add(c);
            }
            var card = _cards[i];
            int client = _view.batches.Count > i ? _view.batches[i].client : 0;
            card.mark.sprite = UiKit.MarkSprite(client);
            return card;
        }

        Row RowAt(int i)
        {
            while (_rows.Count <= i)
            {
                var r = new Row { root = Rect("Order", _right) };
                r.box = Img(r.root, UiKit.RoundRingSprite, UiKit.Cream);
                r.tick = Img(r.box.rectTransform, UiKit.RoundFillSprite, UiKit.Good);
                var tr = r.tick.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(4, 4); tr.offsetMax = new Vector2(-4, -4);
                r.title = Txt(r.root, _font, UiKit.Small, UiKit.Cream);
                r.count = Txt(r.root, _font, UiKit.Small, UiKit.Tan);
                r.barBg = Img(r.root, null, new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, 0.15f));
                r.barFill = Img(r.barBg.rectTransform, null, UiKit.Amber);
                r.barFill.rectTransform.anchorMin = Vector2.zero; r.barFill.rectTransform.anchorMax = Vector2.one;
                r.barFill.rectTransform.offsetMin = r.barFill.rectTransform.offsetMax = Vector2.zero;
                _rows.Add(r);
            }
            return _rows[i];
        }

        Text NoteAt(int i)
        {
            while (_notes.Count <= i) _notes.Add(Txt(_left, _font, UiKit.Small, UiKit.Tan));
            _notes[i].gameObject.SetActive(true);
            return _notes[i];
        }

        // ---- Building blocks ----

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            return r;
        }

        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
        }

        static Image Img(RectTransform parent, Sprite sprite, Color c)
        {
            var r = Rect("Image", parent);
            var i = r.gameObject.AddComponent<Image>();
            i.sprite = sprite; i.type = sprite ? Image.Type.Sliced : Image.Type.Simple;
            i.color = c; i.raycastTarget = false;
            return i;
        }

        Text Txt(RectTransform parent, Font f, int size, Color c)
        {
            var r = Rect("Text", parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = f; t.fontSize = size; t.color = c; t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.05f; t.raycastTarget = false; t.supportRichText = false;
            return t;
        }

        /// <summary>Sets a text at (x, y) in its width and returns the y under it (empty text: nothing, y unchanged).</summary>
        static float Put(Text t, float x, float y, float w, string text)
        {
            bool on = !string.IsNullOrEmpty(text);
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            if (!on) return y;
            float h = Measure(t, w, text);
            Place(t.rectTransform, x, y, w, h);
            return y - h;
        }

        static float Measure(Text t, float w, string text)
        {
            if (t.text != text) t.text = text;
            var r = t.rectTransform;
            if (Mathf.Abs(r.sizeDelta.x - w) > 0.01f) r.sizeDelta = new Vector2(w, r.sizeDelta.y);
            var settings = t.GetGenerationSettings(new Vector2(w, 0f));
            return Mathf.Ceil(t.cachedTextGeneratorForLayout.GetPreferredHeight(text, settings) / t.pixelsPerUnit);
        }
    }
}
