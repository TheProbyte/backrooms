using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Backrooms.Interaction
{
    /// <summary>
    /// F2: a small panel of the settings of the item in your hand, or the one you are looking at (within 4 m). Every
    /// change applies at once to every item of that kind and is saved as its default in every world (<see cref="ItemTuning"/>:
    /// into the project in the editor). Sliders for numbers (or type an exact value), boxes for on/off; RESET puts the
    /// item back to its built-in values. Escape or F2 closes it. The F5 menu's look (UI Toolkit, Resources/UI).
    /// </summary>
    public class ItemTuningMenu : MonoBehaviour
    {
        public static ItemTuningMenu Instance { get; private set; }
        public bool IsOpen { get; private set; }
        /// <summary>Open when the frame began (Escape closes it; the pause menu must not open on the same key).</summary>
        public static bool WasOpen { get; private set; }

        UIDocument _doc;
        VisualElement _root, _panel;
        ScrollView _list;
        Label _page, _note, _saved, _hint;
        Type _type;
        string _name;
        readonly List<Action> _refreshers = new List<Action>();
        CursorLockMode _prevLock;
        bool _prevVisible;
        float _changedAt = -1f, _hintUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance) return;
            var go = new GameObject("ItemTuningMenu");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ItemTuningMenu>();
        }

        void Start() => Build();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (IsOpen)
            {
                if (kb.f2Key.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) SetOpen(false);
                else if (F5Open()) SetOpen(false);
            }
            else if (kb.f2Key.wasPressedThisFrame && !F5Open() && !CustomRooms.PauseMenu.IsOpen && FindAnyObjectByType<CamcorderPlayerController>())
            {
                var item = Target();
                if (item) Show(item);
                else Hint("F2: HOLD OR LOOK AT AN ITEM TO TUNE IT");
            }
            // Saved a moment after the last change (not on every step of a slider).
            if (ItemTuning.Dirty && _changedAt >= 0f && Time.unscaledTime - _changedAt > 0.6f) Save();
            if (_hint != null && _hint.style.display == DisplayStyle.Flex && Time.unscaledTime > _hintUntil) _hint.style.display = DisplayStyle.None;
        }

        void LateUpdate() { WasOpen = IsOpen; }

        void OnApplicationQuit() { if (ItemTuning.Dirty) ItemTuning.Save(); }

        static bool F5Open()
        {
            var m = FindAnyObjectByType<Camcorder.CamcorderVisualMenu>();
            return m && m.IsOpen;
        }

        /// <summary>The item in the hand, else the one the view rests on (4 m).</summary>
        static WorldItem Target()
        {
            var player = FindAnyObjectByType<CamcorderPlayerController>();
            var hand = player ? player.GetComponent<PropGrabber>() : null;
            if (hand && hand.HeldItem) return hand.HeldItem;
            var cam = Camera.main;
            if (!cam) return null;
            var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, 4f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (player && h.collider.transform.IsChildOf(player.transform)) continue;
                var item = h.collider.GetComponentInParent<WorldItem>();
                if (item) return item;
                if (!h.collider.isTrigger) break;                 // something solid in front
            }
            return null;
        }

        // ---------------- Open and close ----------------

        void Show(WorldItem item)
        {
            if (_root == null) Build();
            _type = item.GetType();
            _name = string.IsNullOrEmpty(item.displayName) ? ItemTuning.Nice(_type.Name) : item.displayName;
            Fill(item);
            SetOpen(true);
        }

        void SetOpen(bool open)
        {
            if (open == IsOpen) return;
            IsOpen = open;
            _panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            CamcorderPlayerController.InputBlocked = open;
            if (open)
            {
                _prevLock = Cursor.lockState; _prevVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            }
            else
            {
                if (ItemTuning.Dirty) Save();
                _root?.focusController?.focusedElement?.Blur();
                Cursor.lockState = _prevLock; Cursor.visible = _prevVisible;
            }
        }

        void Save()
        {
            ItemTuning.Save();
            _changedAt = -1f;
            if (_saved != null) _saved.text = Application.isEditor ? "SAVED INTO THE PROJECT" : "SAVED";
        }

        void Hint(string text)
        {
            if (_root == null) Build();
            _hint.text = text;
            _hint.style.display = DisplayStyle.Flex;
            _hintUntil = Time.unscaledTime + 2f;
        }

        // ---------------- Layout ----------------

        void Build()
        {
            if (_root != null) return;
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = Resources.Load<PanelSettings>("UI/CamcorderPanel");
            _doc.sortingOrder = 50;
            _root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/CamcorderMenu");
            if (sheet) _root.styleSheets.Add(sheet);
            _root.AddToClassList("cc-root");
            _root.pickingMode = PickingMode.Ignore;

            _panel = new VisualElement { name = "ItemTuning" };
            _panel.AddToClassList("cc-panel");
            // On the right, the height of what it holds (the F5 panel is on the left).
            _panel.style.left = StyleKeyword.Auto; _panel.style.right = 18; _panel.style.bottom = StyleKeyword.Auto;
            _panel.style.width = 430; _panel.style.maxHeight = new StyleLength(new Length(92f, LengthUnit.Percent));
            _root.Add(_panel);

            var header = new VisualElement(); header.AddToClassList("cc-header");
            header.Add(Text("ITEM", "cc-title"));
            _page = Text("", "cc-page"); header.Add(_page);
            header.Add(Text("F2", "cc-stby"));
            _panel.Add(header);
            var rule = new VisualElement(); rule.AddToClassList("cc-rule"); _panel.Add(rule);
            _note = Text("", "cc-desc"); _panel.Add(_note);

            _list = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, mouseWheelScrollSize = 40f };
            _list.AddToClassList("cc-scroll");
            _list.style.flexShrink = 1;
            _panel.Add(_list);

            var footer = new VisualElement(); footer.AddToClassList("cc-btnrow"); footer.AddToClassList("cc-footer");
            Button(footer, "RESET", () => { if (_type == null) return; ItemTuning.Reset(_type); Changed(); Refresh(); });
            Button(footer, "CLOSE", () => SetOpen(false));
            _panel.Add(footer);
            _saved = Text("", "cc-hint"); _panel.Add(_saved);
            _panel.style.display = DisplayStyle.None;

            // A line at the top of the screen when there is nothing to tune.
            _hint = Text("", "cc-hint");
            _hint.style.position = Position.Absolute; _hint.style.top = 24; _hint.style.left = 0; _hint.style.right = 0;
            _hint.style.unityTextAlign = TextAnchor.UpperCenter; _hint.style.fontSize = 16;
            _hint.style.display = DisplayStyle.None;
            _root.Add(_hint);
        }

        /// <summary>The rows for an item type's settings.</summary>
        void Fill(WorldItem sample)
        {
            _list.Clear();
            _refreshers.Clear();
            _page.text = (_name + " SETTINGS").ToUpperInvariant();
            _note.text = $"For every {_name.ToLowerInvariant()}, in every world, from now on. Saved as you change them" +
                         (Application.isEditor ? " (into the project)." : ".");
            _saved.text = "";
            foreach (var s in ItemTuning.SettingsOf(_type))
            {
                float now = ItemTuning.Get(sample, s);
                if (s.kind == ItemTuning.Kind.Bool) AddToggle(s, sample);
                else AddSlider(s, sample, now);
            }
            Refresh();
        }

        void AddSlider(ItemTuning.Setting s, WorldItem sample, float now)
        {
            float def = ItemTuning.Default(_type, s.name);
            if (float.IsNaN(def)) def = now;
            // A range: the field's own, or one round its built-in value.
            float min = s.ranged ? s.min : 0f, max = s.ranged ? s.max : Mathf.Max(Mathf.Abs(def) * 3f, s.kind == ItemTuning.Kind.Int ? 10f : 1f);
            if (!s.ranged && def < 0f) min = def * 3f;
            min = Mathf.Min(min, now); max = Mathf.Max(max, now);
            bool whole = s.kind == ItemTuning.Kind.Int;
            string fmt = whole ? "0" : max - min <= 2f ? "0.000" : max - min <= 20f ? "0.00" : "0.0";

            var row = new VisualElement(); row.AddToClassList("cc-row");
            var head = new VisualElement(); head.AddToClassList("cc-rowhead");
            var name = Text(s.label.ToUpperInvariant(), null);
            var field = new TextField { isDelayed = true };
            field.AddToClassList("cc-field");
            field.style.maxWidth = 110; field.style.flexGrow = 0; field.style.flexBasis = 110;
            head.Add(name); head.Add(field);
            row.Add(head);
            var slider = new UnityEngine.UIElements.Slider(min, max) { fill = true };
            slider.AddToClassList("cc-slider");
            row.Add(slider);
            var info = Text(Describe(s, def, fmt), "cc-desc");
            row.Add(info);
            _list.Add(row);

            void Set(float v)
            {
                if (whole) v = Mathf.Round(v);
                ItemTuning.Change(_type, s, v);
                // Typed past the slider's ends: widen it.
                if (v < slider.lowValue) slider.lowValue = v;
                if (v > slider.highValue) slider.highValue = v;
                slider.SetValueWithoutNotify(v);
                field.SetValueWithoutNotify(v.ToString(fmt, CultureInfo.InvariantCulture));
                Changed();
            }
            slider.RegisterValueChangedCallback(e => Set(e.newValue));
            field.RegisterValueChangedCallback(e =>
            {
                if (float.TryParse(e.newValue.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) Set(v);
                else field.SetValueWithoutNotify(slider.value.ToString(fmt, CultureInfo.InvariantCulture));
            });
            _refreshers.Add(() =>
            {
                float v = Current(s, sample);
                if (v < slider.lowValue) slider.lowValue = v;
                if (v > slider.highValue) slider.highValue = v;
                slider.SetValueWithoutNotify(v);
                field.SetValueWithoutNotify(v.ToString(fmt, CultureInfo.InvariantCulture));
            });
        }

        void AddToggle(ItemTuning.Setting s, WorldItem sample)
        {
            var row = new VisualElement(); row.AddToClassList("cc-row");
            var t = new UnityEngine.UIElements.Toggle { text = s.label.ToUpperInvariant() };
            t.AddToClassList("cc-toggle");
            row.Add(t);
            float def = ItemTuning.Default(_type, s.name);
            row.Add(Text(Describe(s, def, "0"), "cc-desc"));
            _list.Add(row);
            t.RegisterValueChangedCallback(e => { ItemTuning.Change(_type, s, e.newValue ? 1f : 0f); Changed(); });
            _refreshers.Add(() => t.SetValueWithoutNotify(Current(s, sample) > 0.5f));
        }

        static string Describe(ItemTuning.Setting s, float def, string fmt)
        {
            string built = float.IsNaN(def) ? "" : s.kind == ItemTuning.Kind.Bool ? (def > 0.5f ? "Built in: on." : "Built in: off.") : "Built in: " + def.ToString(fmt, CultureInfo.InvariantCulture) + ".";
            return string.IsNullOrEmpty(s.tip) ? built : s.tip + " " + built;
        }

        /// <summary>What the setting is now: tuned, or the item's own value.</summary>
        float Current(ItemTuning.Setting s, WorldItem sample)
        {
            if (ItemTuning.TryTuned(_type, s.name, out float v)) return v;
            float def = ItemTuning.Default(_type, s.name);
            return !float.IsNaN(def) ? def : sample ? ItemTuning.Get(sample, s) : 0f;
        }

        void Changed()
        {
            _changedAt = Time.unscaledTime;
            if (_saved != null) _saved.text = "";
        }

        void Refresh() { foreach (var r in _refreshers) r(); }

        static Label Text(string text, string cls)
        {
            var l = new Label(text) { enableRichText = false };
            if (cls != null) l.AddToClassList(cls);
            return l;
        }

        static Button Button(VisualElement parent, string text, Action onClick)
        {
            var b = new Button(onClick) { text = text, focusable = false };
            b.AddToClassList("cc-btn");
            parent.Add(b);
            return b;
        }
    }
}
