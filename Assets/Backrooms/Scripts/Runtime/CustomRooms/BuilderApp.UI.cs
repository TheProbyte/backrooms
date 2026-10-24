using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Backrooms.CustomRooms
{
    /// <summary>Build mode: the library screen and the editor's panels.</summary>
    public partial class BuilderApp
    {
        const float TopH = 52f, BottomH = 64f, LeftW = 232f, RightW = 372f;

        Vector2 _rightScroll, _libScroll;
        float _rightContentH = 900f;
        bool _clearFocus;
        readonly Dictionary<string, (string stamp, Texture2D tex)> _thumbCache = new Dictionary<string, (string, Texture2D)>();
        List<string> _biomeNames, _zoneNames;

        Rect LeftRect => new Rect(0, TopH, LeftW, ImUi.Height - TopH - BottomH);
        Rect RightRect => new Rect(ImUi.Width - RightW, TopH, RightW, ImUi.Height - TopH - BottomH);
        Rect TopRect => new Rect(0, 0, ImUi.Width, TopH);
        Rect BottomRect => new Rect(0, ImUi.Height - BottomH, ImUi.Width, BottomH);

        bool OverUi(Vector2 p)
        {
            if (_screen == ScreenKind.Library) return true;
            return LeftRect.Contains(p) || RightRect.Contains(p) || TopRect.Contains(p) || BottomRect.Contains(p);
        }

        void HandleLibraryKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame && PauseMenu.ToggledFrame != Time.frameCount)
            {
                if (PauseMenu.IsOpen) PauseMenu.Instance.Close();
                else if (_modal != Modal.None) _modal = Modal.None;
                else PauseMenu.Instance?.Open();
            }
        }

        void OnGUI()
        {
            if (PauseMenu.IsOpen) return;       // the pause menu draws itself
            ImUi.Begin();
            ImUi.Tip = "";
            if (_screen == ScreenKind.Library) DrawLibrary();
            else if (_bp != null) DrawEditor();
            DrawModal();
            _textFocus = GUI.GetNameOfFocusedControl().StartsWith("tf_");
            if (_clearFocus || (Event.current.type == EventType.MouseDown && !OverUi(ImUi.Pointer) && _modal == Modal.None)) { GUI.FocusControl(null); _clearFocus = false; }
            ImUi.End();
        }

        // ---------------- Library ----------------

        Texture2D Thumb(RoomBlueprint bp)
        {
            string stamp = bp.modified + bp.variants.Count;
            if (_thumbCache.TryGetValue(bp.id, out var c) && c.stamp == stamp && c.tex) return c.tex;
            var t = RoomThumbnails.Make(bp);
            _thumbCache[bp.id] = (stamp, t);
            return t;
        }

        void DrawLibrary()
        {
            ImUi.Fill(new Rect(0, 0, ImUi.Width, ImUi.Height), new Color(0.045f, 0.047f, 0.05f, 1f));
            GUI.Label(new Rect(40, 24, 700, 44), "ROOM LIBRARY", ImUi.Title);
            ImUi.Dimmed(new Rect(42, 66, 1200, 24), "Rooms you build here join the procedural world as set pieces. Pick one to edit, or start a new room.");
            if (ImUi.Button(new Rect(ImUi.Width - 190, 28, 150, 40), "MENU  (Esc)")) PauseMenu.Instance?.Open();

            var list = RoomLibrary.All;
            float cardW = 300, cardH = 276, gap = 24, x0 = 40, y0 = 112;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((ImUi.Width - 2 * x0 + gap) / (cardW + gap)));
            int count = list.Count + 1;
            int rows = Mathf.CeilToInt(count / (float)perRow);
            var view = new Rect(0, y0, ImUi.Width, ImUi.Height - y0 - 10);
            var content = new Rect(0, 0, ImUi.Width - 20, rows * (cardH + gap) + 20);
            _libScroll = GUI.BeginScrollView(view, _libScroll, content);
            for (int i = 0; i < count; i++)
            {
                int row = i / perRow, col = i % perRow;
                var r = new Rect(x0 + col * (cardW + gap), 6 + row * (cardH + gap), cardW, cardH);
                if (i == 0) DrawNewCard(r);
                else DrawRoomCard(r, list[i - 1]);
            }
            GUI.EndScrollView();
            if (list.Count == 0) ImUi.Dimmed(new Rect(x0 + cardW + gap + 10, y0 + 100, 600, 60), "No rooms yet. Click NEW ROOM to build your first one.");
        }

        void DrawNewCard(Rect r)
        {
            ImUi.Fill(r, new Color(0.1f, 0.1f, 0.11f, 1f));
            ImUi.Fill(new Rect(r.x, r.y, r.width, 3), ImUi.Accent);
            GUI.Label(new Rect(r.x, r.y + 70, r.width, 60), "+", new GUIStyle(ImUi.Title) { fontSize = 64, alignment = TextAnchor.MiddleCenter });
            if (ImUi.Button(new Rect(r.x + 40, r.y + r.height - 90, r.width - 80, 44), "NEW ROOM", true)) StartNew();
            ImUi.Dimmed(new Rect(r.x + 10, r.y + r.height - 40, r.width - 20, 26), "Four walls, a floor, a ceiling and a door.");
        }

        void DrawRoomCard(Rect r, RoomBlueprint bp)
        {
            ImUi.Fill(r, ImUi.Raised);
            var v = bp.variants[0];
            ImUi.Fill(new Rect(r.x + 10, r.y + 10, r.width - 20, 150), new Color(0.07f, 0.07f, 0.08f, 1f));
            GUI.DrawTexture(new Rect(r.x + 14, r.y + 14, r.width - 28, 142), Thumb(bp), ScaleMode.ScaleToFit);
            GUI.Label(new Rect(r.x + 6, r.y + 164, r.width - 12 - 86, 26), bp.name, ImUi.Label);
            GUI.Label(new Rect(r.xMax - 94, r.y + 164, 88, 26), bp.enabled ? "SPAWNS" : "OFF", new GUIStyle(ImUi.Small) { alignment = TextAnchor.MiddleRight, normal = { textColor = bp.enabled ? ImUi.Good : ImUi.Dim } });
            string info = $"{v.width}×{v.depth} cells ({v.width * OfficeBiome.CellSize:0.#}×{v.depth * OfficeBiome.CellSize:0.#} m)   {bp.variants.Count} variant{(bp.variants.Count == 1 ? "" : "s")}";
            ImUi.Dimmed(new Rect(r.x + 6, r.y + 190, r.width - 12, 22), info);
            float bw = (r.width - 20 - 12) / 4f;
            if (ImUi.Button(new Rect(r.x + 10, r.y + r.height - 50, bw, 38), "EDIT", true)) StartEdit(bp.id);
            if (ImUi.Button(new Rect(r.x + 10 + (bw + 4), r.y + r.height - 50, bw, 38), bp.enabled ? "OFF" : "ON", false, "Turn spawning in the procedural world on or off for this room."))
            { var c = bp.Clone(); c.enabled = !c.enabled; RoomLibrary.Save(c); }
            if (ImUi.Button(new Rect(r.x + 10 + 2 * (bw + 4), r.y + r.height - 50, bw, 38), "COPY")) { RoomLibrary.Duplicate(bp.id); }
            if (ImUi.Button(new Rect(r.x + 10 + 3 * (bw + 4), r.y + r.height - 50, bw, 38), "DEL")) { _modal = Modal.Delete; _modalTarget = bp.id; }
        }

        // ---------------- Editor ----------------

        static readonly string[] ToolNames = { "SELECT", "WALL", "OPENING", "HALF WALL", "ERASE", "FLOOR AREAS", "PILLAR", "LIGHT", "PROP", "LOOT" };
        static readonly string[] ToolTips =
        {
            "Click a prop or loot marker to select it, drag to move it. R turns it, Delete removes it.",
            "Click or drag along the grid lines to build walls. A wall runs from floor to ceiling. Drag to build long walls.",
            "Click or drag along walls to cut doorways, arches and openings. Drag along several edges for a wider opening. Openings in the outer wall are the room's entrances and exits.",
            "A half-height partition (1.15 m): you can see and reach over it. Drag along edges.",
            "Click or drag to remove walls and openings. On the outer wall this closes an exit again.",
            "Paint which area each cell belongs to. Areas have their own ceiling height, wall finish and lights (right panel).",
            "Click a grid corner to put a pillar there, or remove it.",
            "Click a cell to cycle its ceiling light: on, off (dead tube), none.",
            "Pick a kind of prop, then click inside the room.",
            "Place a spot where an item can be found. The item name and chance are set when selected.",
        };

        void DrawEditor()
        {
            var v = V;
            // Bars
            ImUi.Fill(TopRect, ImUi.Back);
            ImUi.Fill(LeftRect, ImUi.Panel);
            ImUi.Fill(RightRect, ImUi.Panel);
            ImUi.Fill(BottomRect, ImUi.Back);

            DrawTopBar();
            DrawLeftBar();
            DrawRightBar();
            DrawBottomBar();
        }

        void DrawTopBar()
        {
            GUI.Label(new Rect(14, 8, 420, 36), "BUILD MODE", ImUi.Title);
            float x = 230;
            GUI.Label(new Rect(x, 12, 360, 28), _bp.name + (_dirty ? "  •  unsaved" : ""), ImUi.Label);
            x = ImUi.Width * 0.5f - 280;
            string[] names = { "SOLID", "ROOM", "CUTOUT", "WALLS DOWN", "TOP" };
            string[] tips =
            {
                "Walls and ceiling as the world builds them.",
                "Ceiling off, so you can see in from above.",
                "Walls between you and the room are cut down as you turn the camera (like Tiny Glade).",
                "Every wall lowered, to see the whole plan.",
                "Straight down on the plan. (Camera turning is off here.)",
            };
            for (int i = 0; i < 5; i++)
            {
                float w = i == 3 ? 124 : 84;
                if (ImUi.Button(new Rect(x, 8, w, 36), names[i], _view == ViewCycle[i], tips[i] + "  [Tab]")) SetView(ViewCycle[i]);
                x += w + 6;
            }
            if (ImUi.Button(new Rect(x + 10, 8, 64, 36), "GRID", _showGrid, "Show or hide the grid. [G]")) _showGrid = !_showGrid;
            if (ImUi.Button(new Rect(ImUi.Width - 110, 8, 98, 36), "MENU")) PauseMenu.Instance?.Open();
        }

        void DrawLeftBar()
        {
            var r = LeftRect;
            var col = new ImUi.Column(r.x + 12, r.y + 12, r.width - 24, 34, 5);
            ImUi.Header(col.Row(24), "Tools");
            for (int i = 0; i < ToolNames.Length; i++)
            {
                var b = col.Row();
                if (ImUi.Button(b, $"{ToolNames[i]}", _tool == (BuilderTool)i, ToolTips[i] + $"  [{(i + 1) % 10}]")) SetTool((BuilderTool)i);
                GUI.Label(new Rect(b.xMax - 30, b.y, 26, b.height), ((i + 1) % 10).ToString(), new GUIStyle(ImUi.Small) { alignment = TextAnchor.MiddleRight, normal = { textColor = _tool == (BuilderTool)i ? Color.black : ImUi.Dim } });
            }
            col.Space(10);
            switch (_tool)
            {
                case BuilderTool.Opening:
                {
                    ImUi.Header(col.Row(24), "Opening type");
                    string[] n = { "DOORWAY", "ARCH", "TALL OPENING", "OPEN GAP" };
                    string[] t = { "A one-cell doorway with a 2.1 m header.", "A round-topped doorway.", "An opening one cell wide that goes up to the ceiling.", "Removes the wall between cells (inside the room). On the outer wall it makes a tall exit." };
                    for (int i = 0; i < 4; i++) if (ImUi.Button(col.Row(30), n[i], _opening == (OpeningKind)i, t[i])) _opening = (OpeningKind)i;
                    break;
                }
                case BuilderTool.Prop:
                {
                    ImUi.Header(col.Row(24), "Prop");
                    PropKind[] kinds = { PropKind.Box, PropKind.Pallet, PropKind.Sofa, PropKind.ConferenceTable, PropKind.FloorLamp };
                    string[] n = { "BOX", "PALLET", "SOFA", "TABLE", "FLOOR LAMP" };
                    for (int i = 0; i < kinds.Length; i++) if (ImUi.Button(col.Row(30), n[i], _propKind == kinds[i])) _propKind = kinds[i];
                    break;
                }
                case BuilderTool.Loot:
                {
                    ImUi.Header(col.Row(24), "Item name");
                    _lootItem = ImUi.TextField(col.Row(30), _lootItem, "lootitem", 40);
                    ImUi.Dimmed(col.Row(40), "Spawns as a loose box until the game has loot of its own.");
                    break;
                }
                case BuilderTool.Pillar:
                {
                    ImUi.Header(col.Row(24), "Pillar size");
                    if (ImUi.Button(col.Row(30), "SLIM  0.45 m", _pillarSize < 0.5f)) _pillarSize = 0.45f;
                    if (ImUi.Button(col.Row(30), "NORMAL  0.6 m", _pillarSize >= 0.5f)) _pillarSize = 0.6f;
                    break;
                }
                case BuilderTool.Floor:
                {
                    ImUi.Header(col.Row(24), "Paint area");
                    var v = V;
                    for (int i = 0; i < v.rooms.Count; i++)
                    {
                        var b = col.Row(30);
                        ImUi.Fill(new Rect(b.x, b.y + 4, 10, b.height - 8), RoomColor(i));
                        if (ImUi.Button(new Rect(b.x + 14, b.y, b.width - 14, b.height), v.rooms[i].name, _activeRoom == i)) _activeRoom = i;
                    }
                    break;
                }
            }
        }

        string[] _kindNames = { "OFFICE", "BIG OFFICE", "HALLWAY", "LOUNGE", "CONFERENCE" };
        RoomKind[] _kinds = { RoomKind.Office, RoomKind.BigOffice, RoomKind.Hallway, RoomKind.Lounge, RoomKind.Conference };

        void DrawRightBar()
        {
            var r = RightRect;
            var view = new Rect(r.x, r.y, r.width, r.height);
            var content = new Rect(0, 0, r.width - 18, _rightContentH);
            _rightScroll = GUI.BeginScrollView(view, _rightScroll, content, false, false, GUIStyle.none, GUIStyle.none);
            var col = new ImUi.Column(12, 12, r.width - 24 - 14, 30, 6);
            var v = V;

            // ---- Room ----
            ImUi.Header(col.Row(24), "Room");
            string nm = ImUi.TextField(col.Row(32), _bp.name, "bpname", 40);
            if (nm != _bp.name) { _bp.name = nm; _dirty = true; BuildSession.Dirty = true; }
            col.Space(2);

            // ---- Spawn ----
            ImUi.Header(col.Row(24), "Spawning in the world");
            Toggled(ref _bp.enabled, col.Row(), "Spawns in the world", "Off: the room is saved but the world ignores it. Save to game turns this on.");
            _bp.chance = Slide(col.Row(), "How often", _bp.chance, 0.02f, 1f, "0.00", "Chance that a landmark spot that picks this room really holds it. Landmark spots are rare (about one per 3 × 3 districts), so even 1.00 is a rare find.");
            _bp.weight = Slide(col.Row(), "Weight", _bp.weight, 0.1f, 10f, "0.0", "Against the colonnade halls, atriums and other rooms of yours that could take the same spot.");
            Toggled(ref _bp.allowRotate, col.Row(), "May be turned 90°", "The world can place the room turned in quarter steps.");
            EnsureNames();
            ImUi.Text_(col.Row(24), "Biomes (none ticked = any):");
            foreach (var b in _biomeNames) ToggleList(_bp.biomes, b, col.Row(26));
            if (_zoneNames.Count > 1)
            {
                ImUi.Text_(col.Row(24), "Zones (none ticked = any):");
                foreach (var z in _zoneNames) ToggleList(_bp.zones, z, col.Row(26));
            }
            col.Space(4);

            // ---- Variants ----
            ImUi.Header(col.Row(24), "Variants");
            ImUi.Dimmed(col.Row(34), "Each variant is its own layout. The world picks one when it places the room.");
            for (int i = 0; i < _bp.variants.Count; i++)
            {
                var b = col.Row(30);
                if (ImUi.Button(new Rect(b.x, b.y, b.width - 62, b.height), _bp.variants[i].name, i == _vi)) { _vi = i; _activeRoom = 0; _selected = Selection.None; _needRebuild = true; FrameRoom(); }
                GUI.Label(new Rect(b.xMax - 58, b.y, 58, b.height), $"w {_bp.variants[i].weight:0.#}", ImUi.Small);
            }
            var vr = col.Row();
            if (ImUi.Button(new Rect(vr.x, vr.y, vr.width * 0.5f - 3, vr.height), "+ COPY AS NEW", false, "Starts a new variant from this one."))
            {
                PushUndo();
                var c = V.Clone(); c.name = "Variant " + (_bp.variants.Count + 1);
                _bp.variants.Add(c); _vi = _bp.variants.Count - 1; Changed(); FrameRoom();
            }
            if (ImUi.Button(new Rect(vr.x + vr.width * 0.5f + 3, vr.y, vr.width * 0.5f - 3, vr.height), "DELETE", false) && _bp.variants.Count > 1)
            {
                PushUndo(); _bp.variants.RemoveAt(_vi); _vi = Mathf.Clamp(_vi, 0, _bp.variants.Count - 1); Changed();
            }
            string vname = ImUi.TextField(col.Row(30), v.name, "vname", 30);
            if (vname != v.name) { v.name = vname; _dirty = true; }
            float vw = Slide(col.Row(), "Weight", v.weight, 0f, 10f, "0.0", "How often the world picks this variant against the others.");
            if (vw != v.weight) { v.weight = vw; _dirty = true; }
            col.Space(2);

            // ---- Size ----
            ImUi.Header(col.Row(24), "Size");
            SizeRow(col.Row(), "Width", v.width, true);
            SizeRow(col.Row(), "Depth", v.depth, false);
            ImUi.Dimmed(col.Row(22), $"{v.width * OfficeBiome.CellSize:0.0} × {v.depth * OfficeBiome.CellSize:0.0} m  (cells are 1.2 m)");
            col.Space(2);

            // ---- Areas ----
            ImUi.Header(col.Row(24), "Areas");
            ImUi.Dimmed(col.Row(34), "Paint areas with the FLOOR AREAS tool. Each has its own ceiling, walls and lighting.");
            for (int i = 0; i < v.rooms.Count; i++)
            {
                var b = col.Row(30);
                ImUi.Fill(new Rect(b.x, b.y + 4, 10, b.height - 8), RoomColor(i));
                if (ImUi.Button(new Rect(b.x + 14, b.y, b.width - 14, b.height), v.rooms[i].name, _activeRoom == i)) _activeRoom = i;
            }
            var ar = col.Row();
            if (ImUi.Button(new Rect(ar.x, ar.y, ar.width * 0.5f - 3, ar.height), "+ NEW AREA"))
            {
                PushUndo(); v.rooms.Add(new RoomSettings { name = "Area " + (v.rooms.Count + 1) }); _activeRoom = v.rooms.Count - 1; _tool = BuilderTool.Floor; Changed();
            }
            if (ImUi.Button(new Rect(ar.x + ar.width * 0.5f + 3, ar.y, ar.width * 0.5f - 3, ar.height), "REMOVE") && v.rooms.Count > 1)
            {
                PushUndo();
                int gone = _activeRoom;
                for (int i = 0; i < v.cellRoom.Length; i++) { if (v.cellRoom[i] == gone) v.cellRoom[i] = 0; else if (v.cellRoom[i] > gone) v.cellRoom[i]--; }
                v.rooms.RemoveAt(gone); _activeRoom = Mathf.Clamp(_activeRoom, 0, v.rooms.Count - 1); Changed();
            }
            _activeRoom = Mathf.Clamp(_activeRoom, 0, v.rooms.Count - 1);
            var rs = v.rooms[_activeRoom];
            string rname = ImUi.TextField(col.Row(30), rs.name, "areaname", 24);
            if (rname != rs.name) { rs.name = rname; _dirty = true; }
            float ceil = Slide(col.Row(), "Ceiling (m)", rs.ceiling, OfficeBiome.CeilingHeight, 5.4f, "0.0", "Normal offices are 2.7 m. Taller ceilings get plaster walls and round lights.");
            ceil = Mathf.Round((ceil - OfficeBiome.CeilingHeight) / 0.3f) * 0.3f + OfficeBiome.CeilingHeight;
            if (Mathf.Abs(ceil - rs.ceiling) > 0.001f) { PushUndo(); rs.ceiling = ceil; if (ceil > 2.8f) rs.painted = true; Changed(); }
            bool painted = rs.painted;
            Toggled(ref painted, col.Row(), "Painted walls (not wallpaper)", "Plaster instead of the wallpaper. Tall ceilings always use plaster.");
            if (painted != rs.painted) { PushUndo(); rs.painted = painted; Changed(); }
            var lr = col.Row();
            string[] ln = { "ALL ON", "MIXED", "DARK" };
            for (int i = 0; i < 3; i++)
                if (ImUi.Button(new Rect(lr.x + i * (lr.width / 3f), lr.y, lr.width / 3f - 3, lr.height), ln[i], (int)rs.lights == i, "Which ceiling lights work in this area."))
                { PushUndo(); rs.lights = (LightMode)i; foreach (var f in v.fixtures) if (v.RoomAt(f.x, f.z) == rs) f.on = rs.lights != LightMode.Dark && (rs.lights == LightMode.AllOn || (f.x + f.z) % 2 == 0); Changed(); }
            var kr = col.Row();
            int ki = System.Array.IndexOf(_kinds, rs.kind); if (ki < 0) ki = 0;
            if (ImUi.Button(kr, "KIND: " + _kindNames[ki], false, "The kind of room (affects how the world dresses and lights around it)."))
            { PushUndo(); rs.kind = _kinds[(ki + 1) % _kinds.Length]; Changed(); }
            col.Space(2);

            // ---- Selected ----
            DrawSelected(ref col);

            // ---- Checks ----
            ImUi.Header(col.Row(24), "Checks");
            int exits = v.CountExits();
            ImUi.Fill(new Rect(col.x, col.y + 6, 10, 10), exits > 0 ? ImUi.Good : ImUi.Bad);
            GUI.Label(new Rect(col.x + 14, col.y, col.w - 14, 22), exits > 0 ? $"{exits} entrance{(exits == 1 ? "" : "s")} / exit{(exits == 1 ? "" : "s")}" : "No exit: the world will cut one", ImUi.Label); col.Advance(22);
            ImUi.Fill(new Rect(col.x, col.y + 6, 10, 10), v.fixtures.Exists(f => f.on) ? ImUi.Good : ImUi.Bad);
            GUI.Label(new Rect(col.x + 14, col.y, col.w - 14, 22), v.fixtures.Exists(f => f.on) ? $"{v.fixtures.FindAll(f => f.on).Count} lights" : "No working lights", ImUi.Label); col.Advance(22);
            ImUi.Dimmed(col.Row(22), $"{v.props.Count} props, {v.loot.Count} loot spots, {v.pillars.Count} pillars");
            col.Space(30);
            _rightContentH = col.y + 20;
            GUI.EndScrollView();
        }

        void DrawSelected(ref ImUi.Column col)
        {
            var v = V;
            if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count)
            {
                var p = v.props[_selected.index];
                ImUi.Header(col.Row(24), "Selected prop: " + p.kind);
                float yaw = Slide(col.Row(), "Turn (°)", p.yaw, 0f, 359f, "0");
                if (Mathf.Abs(yaw - p.yaw) > 0.01f) { p.yaw = Mathf.Round(yaw / 5f) * 5f; _dirty = true; _needRebuild = true; }
                float ch = Slide(col.Row(), "Chance", p.chance, 0.05f, 1f, "0.00", "Chance this prop is there each time the room is placed.");
                if (Mathf.Abs(ch - p.chance) > 0.001f) { p.chance = ch; _dirty = true; }
                if (p.kind == PropKind.ConferenceTable) { float len = Slide(col.Row(), "Length (m)", p.size.x, 1.2f, 4.8f, "0.0"); if (Mathf.Abs(len - p.size.x) > 0.01f) { p.size.x = len; _dirty = true; _needRebuild = true; } }
                if (p.kind == PropKind.FloorLamp) { bool lit = p.lit; Toggled(ref lit, col.Row(), "Lit"); if (lit != p.lit) { p.lit = lit; _dirty = true; _needRebuild = true; } }
                if (ImUi.Button(col.Row(), "DELETE PROP  [Del]")) DeleteSelected();
                col.Space(4);
            }
            else if (_selected.kind == SelKind.Loot && _selected.index < v.loot.Count)
            {
                var l = v.loot[_selected.index];
                ImUi.Header(col.Row(24), "Selected loot spot");
                string item = ImUi.TextField(col.Row(30), l.item, "lootname", 40);
                if (item != l.item) { l.item = item; _dirty = true; }
                float ch = Slide(col.Row(), "Chance", l.chance, 0.05f, 1f, "0.00", "Chance something is here each time the room is placed.");
                if (Mathf.Abs(ch - l.chance) > 0.001f) { l.chance = ch; _dirty = true; }
                string note = ImUi.TextField(col.Row(30), l.note, "lootnote", 80);
                if (note != l.note) { l.note = note; _dirty = true; }
                if (ImUi.Button(col.Row(), "DELETE SPOT  [Del]")) DeleteSelected();
                col.Space(4);
            }
        }

        float Slide(Rect r, string label, float value, float min, float max, string fmt, string tip = null) => ImUi.Slider(r, label, value, min, max, fmt, tip);

        void Toggled(ref bool value, Rect r, string text, string tip = null)
        {
            bool nv = ImUi.Toggle(r, text, value, tip);
            if (nv != value) { value = nv; _dirty = true; BuildSession.Dirty = true; }
        }

        void ToggleList(List<string> list, string name, Rect r)
        {
            bool on = list.Contains(name);
            bool nv = ImUi.Toggle(new Rect(r.x + 10, r.y, r.width - 10, r.height), name, on);
            if (nv != on) { if (nv) list.Add(name); else list.Remove(name); _dirty = true; BuildSession.Dirty = true; }
        }

        void SizeRow(Rect r, string label, int value, bool width)
        {
            GUI.Label(new Rect(r.x, r.y, 100, r.height), label, ImUi.Label);
            if (ImUi.Button(new Rect(r.x + 120, r.y, 40, r.height), "−")) ResizeTo(width ? value - 1 : V.width, width ? V.depth : value - 1);
            GUI.Label(new Rect(r.x + 164, r.y, 50, r.height), value.ToString(), new GUIStyle(ImUi.Label) { alignment = TextAnchor.MiddleCenter });
            if (ImUi.Button(new Rect(r.x + 218, r.y, 40, r.height), "+")) ResizeTo(width ? value + 1 : V.width, width ? V.depth : value + 1);
        }

        void ResizeTo(int w, int d)
        {
            w = Mathf.Clamp(w, RoomVariant.MinSize, RoomVariant.MaxSize); d = Mathf.Clamp(d, RoomVariant.MinSize, RoomVariant.MaxSize);
            if (w == V.width && d == V.depth) return;
            PushUndo();
            V.Resize(w, d);
            _selected = Selection.None;
            Changed();
            FrameRoom();
        }

        void EnsureNames()
        {
            if (_biomeNames != null) return;
            _biomeNames = new List<string>(); _zoneNames = new List<string>();
            var set = WorldGenSettings.Load();
            foreach (var z in set.zones)
            {
                if (!_zoneNames.Contains(z.name)) _zoneNames.Add(z.name);
                foreach (var b in z.biomes) if (b.rules != null && !_biomeNames.Contains(b.rules.name)) _biomeNames.Add(b.rules.name);
            }
            if (_biomeNames.Count == 0) _biomeNames.AddRange(new[] { "Offices", "Hallways", "Warehouse" });
        }

        void DrawBottomBar()
        {
            var r = BottomRect;
            float x = 14, y = r.y + 12, h = 40;
            if (ImUi.Button(new Rect(x, y, 120, h), "SAVE", _dirty, "Saves the room to your library. It spawns only if 'Spawns in the world' is on.  [Ctrl+S]")) SaveBlueprint(false); x += 126;
            if (ImUi.Button(new Rect(x, y, 170, h), "SAVE TO GAME", false, "Saves the room and turns spawning on: the next procedural world will contain it.")) SaveBlueprint(true); x += 176;
            if (ImUi.Button(new Rect(x, y, 170, h), "TEST IN WORLD", false, "Walks you into the room inside the real procedural world, with the camcorder filters on.")) TestInWorld(); x += 176;
            if (ImUi.Button(new Rect(x, y, 170, h), "DISCARD CHANGES", false, "Goes back to the saved version of this room.")) { _modal = Modal.Discard; } x += 176;
            if (ImUi.Button(new Rect(x, y, 140, h), "LIBRARY", false, "Back to all your rooms.")) { if (_dirty) _modal = Modal.Back; else ShowLibrary(); }
            string msg = Time.unscaledTime < _statusUntil ? _status : ImUi.Tip;
            if (string.IsNullOrEmpty(msg)) msg = "RMB turn   MMB / Shift+RMB or WASD pan   wheel zoom   Q E rotate   F frame   Tab view   Ctrl+Z undo";
            var col = Time.unscaledTime < _statusUntil ? ImUi.Accent : ImUi.Dim;
            GUI.Label(new Rect(x + 160, r.y + 8, ImUi.Width - x - 170 - 30, 48), msg, new GUIStyle(ImUi.Small) { wordWrap = true, normal = { textColor = col }, fontSize = 14 });
        }

        // ---------------- Save / test ----------------

        void SaveBlueprint(bool toGame)
        {
            if (_bp == null) return;
            if (toGame) _bp.enabled = true;
            if (V.CountExits() == 0 && _modal == Modal.None && !_noExitAcknowledged) { _modal = Modal.NoExit; _pendingToGame = toGame; return; }
            DoSave(toGame);
        }

        bool _noExitAcknowledged, _pendingToGame;

        void DoSave(bool toGame)
        {
            try
            {
                RoomLibrary.Save(_bp);
                _dirty = false; BuildSession.Dirty = false;
                SetStatus(toGame || _bp.enabled ? $"Saved '{_bp.name}'. It will spawn in the next procedural world." : $"Saved '{_bp.name}'. Turn on 'Spawns in the world' (or Save to game) to make it appear in the world.");
            }
            catch (System.Exception e) { SetStatus("Could not save: " + e.Message, 8f); }
        }

        void TestInWorld()
        {
            if (V.CountExits() == 0) { SetStatus("This room has no exit. Open a doorway in the outer wall first."); return; }
            BuildSession.Editing = _bp; BuildSession.Variant = _vi; BuildSession.Dirty = _dirty;
            CustomRoomTest.Begin(_bp, _vi);
            if (!Application.CanStreamedLevelBeLoaded(SceneIds.World)) { SetStatus("The world scene is not in the build settings."); CustomRoomTest.End(); return; }
            SceneManager.LoadScene(SceneIds.World);
        }

        // ---------------- Modals ----------------

        void DrawModal()
        {
            if (_modal == Modal.None) return;
            ImUi.Fill(new Rect(0, 0, ImUi.Width, ImUi.Height), new Color(0, 0, 0, 0.55f));
            var box = new Rect(ImUi.Width * 0.5f - 250, ImUi.Height * 0.5f - 110, 500, 220);
            ImUi.PanelBox(box);
            string title = "", body = "";
            switch (_modal)
            {
                case Modal.Discard: title = "DISCARD CHANGES?"; body = "Everything since the last save is lost."; break;
                case Modal.Delete: title = "DELETE THIS ROOM?"; body = "It is removed from your library and from the world. This cannot be undone."; break;
                case Modal.Back: title = "UNSAVED CHANGES"; body = "Save the room before going back to the library?"; break;
                case Modal.NoExit: title = "NO WAY OUT"; body = "This room has no entrance. The world will cut a door into one of its walls so it can be reached. Save anyway?"; break;
            }
            GUI.Label(new Rect(box.x + 24, box.y + 18, box.width - 48, 36), title, new GUIStyle(ImUi.Title) { fontSize = 24 });
            GUI.Label(new Rect(box.x + 24, box.y + 62, box.width - 48, 70), body, new GUIStyle(ImUi.Label) { wordWrap = true });
            float by = box.yMax - 62, bw = 140;
            if (_modal == Modal.Back)
            {
                if (ImUi.Button(new Rect(box.x + 24, by, bw, 40), "SAVE", true)) { _modal = Modal.None; DoSave(false); ShowLibrary(); }
                if (ImUi.Button(new Rect(box.x + 24 + bw + 8, by, bw, 40), "DON'T SAVE")) { _modal = Modal.None; _dirty = false; BuildSession.Dirty = false; ShowLibrary(); }
                if (ImUi.Button(new Rect(box.xMax - 24 - bw, by, bw, 40), "CANCEL")) _modal = Modal.None;
                return;
            }
            if (ImUi.Button(new Rect(box.x + 24, by, 200, 40), _modal == Modal.Delete ? "DELETE" : _modal == Modal.NoExit ? "SAVE ANYWAY" : "DISCARD", true))
            {
                var m = _modal; _modal = Modal.None;
                if (m == Modal.Discard)
                {
                    var saved = RoomLibrary.Get(_bp.id);
                    if (saved != null) { _bp = saved.Clone(); BuildSession.Editing = _bp; _vi = Mathf.Clamp(_vi, 0, _bp.variants.Count - 1); _undo.Clear(); _redo.Clear(); _selected = Selection.None; _dirty = false; BuildSession.Dirty = false; _needRebuild = true; }
                    else { _bp = RoomBlueprint.CreateNew("New room"); _bp.id = RoomLibrary.NewId(); _vi = 0; BuildSession.Editing = _bp; _dirty = true; _needRebuild = true; }
                }
                else if (m == Modal.Delete) { RoomLibrary.Delete(_modalTarget); _thumbCache.Remove(_modalTarget); }
                else if (m == Modal.NoExit) { _noExitAcknowledged = true; DoSave(_pendingToGame); _noExitAcknowledged = false; }
            }
            if (ImUi.Button(new Rect(box.xMax - 24 - 140, by, 140, 40), "CANCEL")) _modal = Modal.None;
        }
    }
}
