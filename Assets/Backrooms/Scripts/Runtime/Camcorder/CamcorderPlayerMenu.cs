using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// F5 for the player (Docs/UiPlan.md U13, 2026-11-10): what a camcorder's own menu has, as a 90s Handycam's did
    /// (PROGRAM AE, DISPLAY, 16:9 WIDE): the picture (four looks with names that fit), the date and REC marks on or off,
    /// the picture's shape, the side panels and the map. Nothing in it can break the picture. The tuning bench that F5 was
    /// (CamcorderVisualMenu: dozens of sliders over eight tabs) is the developer's now, one line at the foot while the
    /// developer tools are on.
    /// </summary>
    public class CamcorderPlayerMenu : MonoBehaviour
    {
        static CamcorderPlayerMenu s_instance;
        public static bool IsOpen => s_instance && s_instance._open;
        /// <summary>Open as the frame began (Escape that closed it is not also the pause menu's).</summary>
        public static bool WasOpen { get; private set; }

        /// <summary>The four looks a player picks from: CamcorderSettings presets, by index, and their names here.</summary>
        static readonly int[] Looks = { 0, 5, 1, 2 };
        static readonly string[] LookNames = { "STANDARD", "VHS", "HI8", "MINIDV" };

        CamcorderVisualMenu _bench;
        UIDocument _doc;
        VisualElement _root, _panel, _list;
        Label _desc;
        readonly List<(VisualElement el, Action<int> step, string desc, Action refresh)> _rows = new List<(VisualElement, Action<int>, string, Action)>();
        bool _open;
        int _sel, _openedFrame;
        CursorLockMode _prevLock;

        public static void Toggle(CamcorderVisualMenu bench)
        {
            if (!s_instance)
            {
                s_instance = bench.gameObject.AddComponent<CamcorderPlayerMenu>();
                s_instance._bench = bench;
            }
            s_instance.SetOpen(!s_instance._open);
        }

        /// <summary>For the picture run.</summary>
        public static void SetOpenForShots(CamcorderVisualMenu bench, bool open) { if (open != IsOpen) Toggle(bench); }

        void SetOpen(bool open)
        {
            if (_doc == null) Build();
            _open = open;
            _openedFrame = Time.frameCount;
            _root.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            CamcorderPlayerController.InputBlocked = open;
            if (open)
            {
                _prevLock = Cursor.lockState;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                Fill();
            }
            else
            {
                var rig = _bench ? _bench.rig : null;
                if (rig) rig.Settings.Save();
                if (_prevLock == CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            }
        }

        void Build()
        {
            var go = new GameObject("CamcorderPlayerMenu_UI");
            go.transform.SetParent(transform, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = Backrooms.UI.UiKit.Panel;
            _doc.sortingOrder = 12;
            _root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/CamcorderMenu");
            if (sheet) _root.styleSheets.Add(sheet);
            _root.AddToClassList("cc-root");
            _root.pickingMode = PickingMode.Ignore;
            Backrooms.UI.UiKit.Apply(_root, Backrooms.UI.UiKit.Face.Cam);
            _panel = new VisualElement(); _panel.AddToClassList("pm-panel"); _root.Add(_panel);
            var head = new VisualElement(); head.AddToClassList("pm-head");
            head.Add(L("MENU", "pm-title"));
            head.Add(L("CAMERA SET", "pm-page"));
            _panel.Add(head);
            var rule = new VisualElement(); rule.AddToClassList("cc-rule"); _panel.Add(rule);
            _list = new VisualElement(); _panel.Add(_list);
            _desc = L("", "pm-desc"); _panel.Add(_desc);
            _panel.Add(L("▲▼ SELECT   ◄ ► SET   F5 OR ESC CLOSE", "pm-hint"));
        }

        static Label L(string text, string cls) { var l = new Label(text); l.AddToClassList(cls); l.pickingMode = PickingMode.Ignore; return l; }

        void Fill()
        {
            _list.Clear(); _rows.Clear();
            var rig = _bench ? _bench.rig : null;
            Row("PROGRAM AE", () =>
            {
                int k = Array.IndexOf(Looks, CamcorderSettings.CurrentPreset);
                return k >= 0 ? LookNames[k] : "CUSTOM";
            }, d =>
            {
                int k = Array.IndexOf(Looks, CamcorderSettings.CurrentPreset);
                k = k < 0 ? 0 : (k + d + Looks.Length) % Looks.Length;
                SetLook(Looks[k]);
            }, "THE TAPE'S LOOK: STANDARD, OLD VHS, HI8 OR A CLEANER MINIDV.");
            Row("DISPLAY", () => rig && rig.Settings.showOSD ? "ON" : "OFF", d => { if (rig) rig.Settings.showOSD = !rig.Settings.showOSD; },
                "REC, THE COUNTER, THE DATE AND TIME IN THE PICTURE.");
            Row("PICTURE", () => rig && rig.Settings.aspect43 ? "4:3" : "16:9 WIDE", d => { if (rig) rig.Settings.aspect43 = !rig.Settings.aspect43; },
                "4:3: THE CAMCORDER'S OWN PICTURE, YOUR WORK IN THE BARS BESIDE IT. 16:9 WIDE: THE WHOLE SCREEN.");
            Row("WORK PANELS", () => Interaction.OrderTracker.Shown ? "SHOWN" : "HIDDEN", d =>
            {
                var t = FindAnyObjectByType<Interaction.OrderTracker>();
                if (t) t.SetMode(Interaction.OrderTracker.Shown ? 0 : 1);
            }, "YOUR BATCHES AND THE WORK (J DOES THE SAME).");
            Row("MAP", () => _bench && _bench.MinimapOn ? "ON" : "OFF", d => { if (_bench) _bench.SetMinimap(!_bench.MinimapOn); },
                "A SMALL MAP OF WHERE YOU ARE (F6 DOES THE SAME).");
            if (DevTools.On)
                Row("DEVELOPER BENCH", () => "►", d => { SetOpen(false); if (_bench) _bench.SetOpen(true); },
                    "EVERY SETTING OF THE PICTURE, TAPE, SOUND AND LIGHT (THE DEVELOPER'S).");
            Select(0);
        }

        /// <summary>A look, keeping what the player set apart from it (the marks, the shape, the sound).</summary>
        void SetLook(int preset)
        {
            var rig = _bench ? _bench.rig : null;
            if (!rig) return;
            var cur = rig.Settings;
            var p = CamcorderSettings.Preset(preset);
            p.CopyAudioFrom(cur);
            p.showOSD = cur.showOSD; p.aspect43 = cur.aspect43;
            rig.SetSettings(p);
            CamcorderSettings.CurrentPreset = preset;
        }

        void Row(string label, Func<string> value, Action<int> step, string desc)
        {
            var row = new VisualElement(); row.AddToClassList("pm-row");
            row.Add(L("►", "pm-cursor"));
            row.Add(L(label, "pm-label"));
            var val = L("", "pm-val"); row.Add(val);
            void Refresh() => val.text = value();
            Refresh();
            int index = _rows.Count;
            _rows.Add((row, d => { step(d); RefreshAll(); }, desc, Refresh));
            row.RegisterCallback<PointerEnterEvent>(_ => Select(index));
            row.RegisterCallback<ClickEvent>(_ => { Select(index); _rows[index].step(1); });
            _list.Add(row);
        }

        void RefreshAll() { foreach (var r in _rows) r.refresh(); }

        void Select(int i)
        {
            if (_rows.Count == 0) return;
            _sel = Mathf.Clamp(i, 0, _rows.Count - 1);
            for (int k = 0; k < _rows.Count; k++) _rows[k].el.EnableInClassList("pm-sel", k == _sel);
            _desc.text = _rows[_sel].desc;
        }

        void Update()
        {
            WasOpen = _open;
            if (!_open) return;
            CamcorderPlayerController.InputBlocked = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _panel.style.left = new Length((Backrooms.UI.UiKit.BarShare + 0.03f) * 100f, LengthUnit.Percent);
            var kb = Keyboard.current;
            if (kb == null || Time.frameCount == _openedFrame) return;
            if (kb.escapeKey.wasPressedThisFrame) { SetOpen(false); return; }
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Select((_sel + _rows.Count - 1) % _rows.Count);
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Select((_sel + 1) % _rows.Count);
            else if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) _rows[_sel].step(-1);
            else if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) _rows[_sel].step(1);
        }

        /// <summary>F5 closes it (CamcorderVisualMenu passes F5 here while it is open).</summary>
        public static void Close() { if (s_instance && s_instance._open) s_instance.SetOpen(false); }
    }
}
