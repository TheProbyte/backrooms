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
        const float TopH = 52f, BottomH = 40f, LeftW = 232f, RightW = 372f;

        Vector2 _rightScroll, _libScroll;
        float _rightContentH = 900f;
        bool _clearFocus;
        bool _showHelp;
        readonly Dictionary<string, (string stamp, Texture2D tex)> _thumbCache = new Dictionary<string, (string, Texture2D)>();
        List<string> _biomeNames, _zoneNames;

        Rect LeftRect => new Rect(0, TopH, LeftW, ImUi.Height - TopH - BottomH);
        Rect RightRect => new Rect(ImUi.Width - RightW, TopH, RightW, ImUi.Height - TopH - BottomH);
        Rect TopRect => new Rect(0, 0, ImUi.Width, TopH);
        Rect BottomRect => new Rect(0, ImUi.Height - BottomH, ImUi.Width, BottomH);

        bool OverUi(Vector2 p)
        {
            if (_screen == ScreenKind.Library) return true;
            return _showHelp || LeftRect.Contains(p) || RightRect.Contains(p) || TopRect.Contains(p) || BottomRect.Contains(p);
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
            else if (_bp != null) { DrawEditor(); DrawHandleLabels(); }
            DrawModal();
            if (_showHelp) DrawHelp();
            else ImUi.DrawTooltip();
            _textFocus = GUI.GetNameOfFocusedControl().StartsWith("tf_");
            if (_clearFocus || (Event.current.type == EventType.MouseDown && !OverUi(ImUi.Pointer) && _modal == Modal.None)) { GUI.FocusControl(null); _clearFocus = false; }
            ImUi.End();
        }

        // ---------------- Library ----------------

        Texture2D Thumb(RoomBlueprint bp)
        {
            string stamp = bp.modified + bp.variants.Count;
            if (_thumbCache.TryGetValue(bp.id, out var c) && c.stamp == stamp && c.tex) return c.tex;
            if (c.tex) Destroy(c.tex);
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
        // (Round 17: the screen made simpler. Top: the room, undo, views, test, save. Left: five modes and the chosen one's
        // tools and options. Right: an inspector that shows only what is selected, or the room as a whole. Bottom: one line
        // saying what a click does now. Where the room spawns in the world is its own dialog, PUT IN THE WORLD.)

        void DrawEditor()
        {
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
            GUI.Label(new Rect(14, 8, 120, 36), "BUILD", ImUi.Title);
            GUI.Label(new Rect(136, 12, 300, 28), _bp.name + (_dirty ? "  •  unsaved" : ""), ImUi.Label);
            float x = 440;
            if (ImUi.Button(new Rect(x, 8, 70, 36), "UNDO", false, "Undo  [Ctrl+Z]")) Undo(); x += 74;
            if (ImUi.Button(new Rect(x, 8, 70, 36), "REDO", false, "Redo  [Ctrl+Y]")) Redo(); x += 90;
            string[] names = { "SOLID", "ROOM", "CUTOUT", "WALLS DOWN", "TOP" };
            string[] tips =
            {
                "Walls and ceiling as the world builds them.",
                "Ceiling off, so you can see in from above.",
                "Walls between you and the room are cut down as you turn the camera.",
                "Every wall lowered, to see the whole plan.",
                "Straight down on the plan.",
            };
            for (int i = 0; i < 5; i++)
            {
                float w = i == 3 ? 134 : i == 2 ? 96 : 80;
                if (ImUi.Button(new Rect(x, 8, w, 36), names[i], _view == ViewCycle[i], tips[i] + "  [Tab]")) SetView(ViewCycle[i]);
                x += w + 4;
            }
            if (ImUi.Button(new Rect(x + 6, 8, 60, 36), "GRID", _showGrid, "Show or hide the grid. [G]")) _showGrid = !_showGrid;
            float rx = ImUi.Width - 12;
            rx -= 80; if (ImUi.Button(new Rect(rx, 8, 80, 36), "MENU")) PauseMenu.Instance?.Open();
            rx -= 74; if (ImUi.Button(new Rect(rx, 8, 70, 36), "HELP", _showHelp, "Controls and a short guide. [F1]")) _showHelp = !_showHelp;
            rx -= 104; if (ImUi.Button(new Rect(rx, 8, 100, 36), "LIBRARY", false, "Back to all your rooms.")) { if (_dirty) _modal = Modal.Back; else ShowLibrary(); }
            rx -= 154; if (ImUi.Button(new Rect(rx, 8, 150, 36), "TEST IN WORLD", false, "Walks you into the room inside the real procedural world, with the camcorder filters on.")) TestInWorld();
            rx -= 94; if (ImUi.Button(new Rect(rx, 8, 90, 36), "SAVE", _dirty, "Saves the room to your library.  [Ctrl+S]")) SaveBlueprint(false);
        }

        void DrawLeftBar()
        {
            var r = LeftRect;
            var col = new ImUi.Column(r.x + 12, r.y + 12, r.width - 24, 34, 5);
            ImUi.Header(col.Row(24), "Mode");
            var cur = CurrentMode;
            for (int i = 0; i < Modes.Length; i++)
            {
                var m = Modes[i];
                var b = col.Row(40);
                if (ImUi.ToolButton(b, m.Name, m.Icon, m == cur, m.Tip + $"  [{i + 1}]")) SetMode(m);
                GUI.Label(new Rect(b.xMax - 26, b.y, 22, b.height), (i + 1).ToString(), new GUIStyle(ImUi.Small) { alignment = TextAnchor.MiddleRight, normal = { textColor = m == cur ? Color.black : ImUi.Dim } });
            }
            col.Space(12);
            if (cur.Tools.Length > 1)
            {
                ImUi.Header(col.Row(24), "Tool");
                var tr = col.Row(30);
                int n = cur.Tools.Length;
                if (n <= 2)
                    for (int i = 0; i < n; i++)
                    { var t = cur.Tools[i]; if (ImUi.Button(new Rect(tr.x + i * (tr.width / n), tr.y, tr.width / n - 3, tr.height), cur.ToolName(t), _tool == t, cur.ToolTip(t))) SetTool(t); }
                else
                {
                    for (int i = 0; i < n; i++)
                    {
                        var t = cur.Tools[i];
                        var br = i == 0 ? tr : col.Row(30);
                        if (ImUi.Button(br, cur.ToolName(t), _tool == t, cur.ToolTip(t))) SetTool(t);
                    }
                }
                col.Space(8);
            }
            cur.Options(ref col);
        }

        static readonly string[] _kindNames = { "OFFICE", "BIG OFFICE", "HALLWAY", "LOUNGE", "CONFERENCE" };
        static readonly string[] WallFinishNames = { "DEFAULT", "WALLPAPER", "PAINTED PLASTER", "REFERENCE CREAM", "REFERENCE OLIVE" };
        static readonly string[] FloorFinishNames = { "DEFAULT CARPET", "REFERENCE CARPET", "DARK WOOD", "LAMINATE" };
        static readonly RoomKind[] _kinds = { RoomKind.Office, RoomKind.BigOffice, RoomKind.Hallway, RoomKind.Lounge, RoomKind.Conference };

        /// <summary>The inspector: what is selected; in PAINT (or with a room's floor clicked) that room; else the whole room.</summary>
        void DrawRightBar()
        {
            var r = RightRect;
            var view = new Rect(r.x, r.y, r.width, r.height);
            var content = new Rect(0, 0, r.width - 18, _rightContentH);
            _rightScroll = GUI.BeginScrollView(view, _rightScroll, content, false, false, GUIStyle.none, GUIStyle.none);
            var col = new ImUi.Column(12, 12, r.width - 24 - 14, 30, 6);
            if (_selected.kind == SelKind.Prop || _selected.kind == SelKind.Loot || _selected.kind == SelKind.Opening)
            {
                if (ImUi.Button(col.Row(28), "‹  WHOLE ROOM", false, "Deselect. [Esc]")) _selected = Selection.None;
                col.Space(4);
                DrawSelected(ref col);
            }
            else if (_selected.kind == SelKind.Room || CurrentMode is PaintMode) DrawRoomInspector(ref col);
            else DrawOverview(ref col);
            col.Space(30);
            _rightContentH = col.y + 20;
            GUI.EndScrollView();
        }

        /// <summary>The room as a whole: its name and size, the rooms painted in it, its variants, its checks.</summary>
        void DrawOverview(ref ImUi.Column col)
        {
            var v = V;
            ImUi.Header(col.Row(24), "This room");
            string nm = ImUi.TextField(col.Row(32), _bp.name, "bpname", 40);
            if (nm != _bp.name) { _bp.name = nm; _dirty = true; BuildSession.Dirty = true; }
            SizeRow(col.Row(), "Width", v.width, true);
            SizeRow(col.Row(), "Depth", v.depth, false);
            ImUi.Dimmed(col.Row(34), $"{v.width * OfficeBiome.CellSize:0.0} × {v.depth * OfficeBiome.CellSize:0.0} m. Or drag the arrows outside its walls (SELECT).");
            col.Space(4);

            ImUi.Header(col.Row(24), "Rooms inside it");
            for (int i = 0; i < v.rooms.Count; i++)
            {
                var b = col.Row(30);
                ImUi.Fill(new Rect(b.x, b.y + 4, 10, b.height - 8), RoomColor(i));
                string info = $"{v.rooms[i].name}   ·   {v.rooms[i].ceiling:0.0} m";
                if (ImUi.Button(new Rect(b.x + 14, b.y, b.width - 14, b.height), info, false, "Its ceiling, walls, floor and lights.")) { SelectRoom(i); BuilderSounds.Play(BuilderSounds.Sfx.Pick); }
            }
            if (ImUi.Button(col.Row(30), "+ NEW ROOM", false, "A new room to paint cells into (PAINT)."))
            { PushUndo(); v.rooms.Add(new RoomSettings { name = "Room " + (v.rooms.Count + 1) }); SetMode(Modes[4]); SelectRoom(v.rooms.Count - 1); Changed(); }
            col.Space(4);

            ImUi.Header(col.Row(24), "Variants");
            ImUi.Dimmed(col.Row(34), "Each variant is its own layout of this room; the world picks one.");
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
            { PushUndo(); _bp.variants.RemoveAt(_vi); _vi = Mathf.Clamp(_vi, 0, _bp.variants.Count - 1); Changed(); }
            if (_bp.variants.Count > 1)
            {
                string vname = ImUi.TextField(col.Row(30), v.name, "vname", 30);
                if (vname != v.name) { v.name = vname; _dirty = true; }
                float vw = Slide(col.Row(), "Weight", v.weight, 0f, 10f, "0.0", "How often the world picks this variant against the others.");
                if (vw != v.weight) { v.weight = vw; _dirty = true; }
            }
            col.Space(4);

            DrawChecks(ref col);
            col.Space(8);
            if (ImUi.Button(col.Row(40), "PUT IN THE WORLD…", _bp.enabled, "Whether and where this room spawns in the procedural world.")) _modal = Modal.World;
            ImUi.Dimmed(col.Row(24), _bp.enabled ? "Spawns in the world." : "Not in the world yet: only in your library.");
            if (ImUi.Button(col.Row(32), "DISCARD CHANGES", false, "Back to the saved version of this room.")) _modal = Modal.Discard;
        }

        void DrawChecks(ref ImUi.Column col)
        {
            var v = V;
            ImUi.Header(col.Row(24), "Checks");
            int exits = v.CountExits();
            ImUi.Fill(new Rect(col.x, col.y + 6, 10, 10), exits > 0 ? ImUi.Good : ImUi.Bad);
            GUI.Label(new Rect(col.x + 14, col.y, col.w - 14, 22), exits > 0 ? $"{exits} way{(exits == 1 ? "" : "s")} in" : "No way in: put a door in an outer wall", ImUi.Label); col.Advance(22);
            bool lit = v.fixtures.Exists(f => f.on);
            ImUi.Fill(new Rect(col.x, col.y + 6, 10, 10), lit ? ImUi.Good : ImUi.Bad);
            GUI.Label(new Rect(col.x + 14, col.y, col.w - 14, 22), lit ? $"{v.fixtures.FindAll(f => f.on).Count} lights" : "No working lights", ImUi.Label); col.Advance(22);
            ImUi.Dimmed(col.Row(22), $"{v.openings.Count} doors and windows, {v.props.Count} props, {v.loot.Count} loot spots, {v.pillars.Count} pillars");
        }

        /// <summary>One room (an area of cells): its name, ceiling, walls, floor, lights; copy and paste; remove.</summary>
        void DrawRoomInspector(ref ImUi.Column col)
        {
            var v = V;
            _activeRoom = Mathf.Clamp(_activeRoom, 0, v.rooms.Count - 1);
            var rs = v.rooms[_activeRoom];
            if (ImUi.Button(col.Row(28), "‹  WHOLE ROOM", false, "Back to the room as a whole. [Esc]")) { _selected = Selection.None; if (CurrentMode is PaintMode) SetMode(Modes[0]); }
            col.Space(4);
            var hr = col.Row(24);
            ImUi.Fill(new Rect(hr.x, hr.y + 5, 12, 14), RoomColor(_activeRoom));
            ImUi.Header(new Rect(hr.x + 18, hr.y, hr.width - 18, hr.height), "Room");
            string rname = ImUi.TextField(col.Row(30), rs.name, "areaname", 24);
            if (rname != rs.name) { rs.name = rname; _dirty = true; }
            int cells = 0; foreach (int c in v.cellRoom) if (c == _activeRoom) cells++;
            ImUi.Dimmed(col.Row(22), $"{cells} cells ({cells * OfficeBiome.CellSize * OfficeBiome.CellSize:0.#} m²). PAINT adds cells to it.");
            float ceil = Slide(col.Row(), "Ceiling (m)", rs.ceiling, OfficeBiome.CeilingHeight, 5.4f, "0.0", "Normal offices are 2.7 m. Taller ceilings get plaster walls and round lights.");
            ceil = Mathf.Round((ceil - OfficeBiome.CeilingHeight) / 0.3f) * 0.3f + OfficeBiome.CeilingHeight;
            if (Mathf.Abs(ceil - rs.ceiling) > 0.001f) { PushUndo(); rs.ceiling = ceil; if (ceil > 2.8f) rs.painted = true; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.8f + ceil / 10f); }
            if (ImUi.Button(col.Row(), "WALLS: " + WallFinishNames[Mathf.Clamp((int)rs.wallFinish, 0, WallFinishNames.Length - 1)], false, "Cycle the wall finishes. DEFAULT follows 'Painted plaster' below; tall rooms are always plaster unless chosen here."))
            { PushUndo(); rs.wallFinish = (WallFinish)(((int)rs.wallFinish + 1) % System.Enum.GetValues(typeof(WallFinish)).Length); Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick); }
            if (rs.wallFinish == WallFinish.Default)
            {
                bool painted = rs.painted;
                Toggled(ref painted, col.Row(), "Painted plaster (not wallpaper)");
                if (painted != rs.painted) { PushUndo(); rs.painted = painted; Changed(); }
            }
            if (ImUi.Button(col.Row(), "FLOOR: " + FloorFinishNames[Mathf.Clamp((int)rs.floorFinish, 0, FloorFinishNames.Length - 1)], false, "Cycle the floors: carpet, Reference Match carpet, dark wood, laminate."))
            { PushUndo(); rs.floorFinish = (FloorFinish)(((int)rs.floorFinish + 1) % System.Enum.GetValues(typeof(FloorFinish)).Length); Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick); }
            ImUi.Text_(col.Row(22), "Lights");
            var lr = col.Row();
            string[] ln = { "ALL ON", "MIXED", "DARK" };
            for (int i = 0; i < 3; i++)
                if (ImUi.Button(new Rect(lr.x + i * (lr.width / 3f), lr.y, lr.width / 3f - 3, lr.height), ln[i], (int)rs.lights == i, "Which of its ceiling lights work. PAINT > LIGHTS sets each one."))
                { PushUndo(); rs.lights = (LightMode)i; foreach (var f in v.fixtures) if (v.RoomAt(f.x, f.z) == rs) f.on = rs.lights != LightMode.Dark && (rs.lights == LightMode.AllOn || (f.x + f.z) % 2 == 0); Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick); }
            int ki = System.Array.IndexOf(_kinds, rs.kind); if (ki < 0) ki = 0;
            if (ImUi.Button(col.Row(), "KIND: " + _kindNames[ki], false, "The kind of room: how the world dresses and lights around it."))
            { PushUndo(); rs.kind = _kinds[(ki + 1) % _kinds.Length]; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick); }
            col.Space(6);
            var cr = col.Row();
            if (ImUi.Button(new Rect(cr.x, cr.y, cr.width * 0.5f - 3, cr.height), "COPY ROOM", false, "Copy its cells, settings, walls, doors, lights and things. [Ctrl+C]")) CopyArea();
            if (ImUi.Button(new Rect(cr.center.x + 3, cr.y, cr.width * 0.5f - 3, cr.height), "PASTE", _pastingArea, "Then click where its lower-left cell goes. [Ctrl+V]")) BeginPasteArea();
            var pr = col.Row();
            if (ImUi.Button(new Rect(pr.x, pr.y, pr.width * 0.5f - 3, pr.height), "PAINT CELLS", CurrentMode is PaintMode && _tool == BuilderTool.Floor, "Paint more cells into this room.")) { SetTool(BuilderTool.Floor); SelectRoom(_activeRoom); }
            if (ImUi.Button(new Rect(pr.center.x + 3, pr.y, pr.width * 0.5f - 3, pr.height), "REMOVE", false, "Its cells go back to the first room.") && v.rooms.Count > 1)
            {
                PushUndo();
                int gone = _activeRoom;
                for (int i = 0; i < v.cellRoom.Length; i++) { if (v.cellRoom[i] == gone) v.cellRoom[i] = 0; else if (v.cellRoom[i] > gone) v.cellRoom[i]--; }
                v.rooms.RemoveAt(gone); _activeRoom = 0; _selected = Selection.None; Changed();
                BuilderSounds.Play(BuilderSounds.Sfx.Delete);
            }
        }

        /// <summary>PUT IN THE WORLD: whether the room spawns, how often, where.</summary>
        void DrawWorldDialog()
        {
            float w = Mathf.Min(760f, ImUi.Width - 40f), h = 720f;
            var box = new Rect((ImUi.Width - w) * 0.5f, (ImUi.Height - h) * 0.5f, w, h);
            ImUi.PanelBox(box);
            GUI.Label(new Rect(box.x + 24, box.y + 18, box.width - 48, 40), "PUT IN THE WORLD", ImUi.Title);
            ImUi.Dimmed(new Rect(box.x + 24, box.y + 60, box.width - 48, 40), "Rooms you put in the world turn up as rare set pieces in new worlds (about one spot per 3 × 3 districts is open to rooms like this).");
            var col = new ImUi.Column(box.x + 24, box.y + 108, box.width * 0.5f - 36, 30, 6);
            Toggled(ref _bp.enabled, col.Row(), "Spawns in the world");
            _bp.chance = Slide(col.Row(), "How often", _bp.chance, 0.02f, 1f, "0.00", "Chance that a spot that picks this room really holds it.");
            _bp.weight = Slide(col.Row(), "Weight", _bp.weight, 0.1f, 10f, "0.0", "Against the other set pieces that could take the same spot.");
            Toggled(ref _bp.allowRotate, col.Row(), "May be turned 90°");
            EnsureNames();
            var col2 = new ImUi.Column(box.x + box.width * 0.5f + 12, box.y + 108, box.width * 0.5f - 36, 26, 4);
            ImUi.Text_(col2.Row(24), "Biomes (none ticked = any):");
            foreach (var b in _biomeNames) ToggleList(_bp.biomes, b, col2.Row());
            if (_zoneNames.Count > 1)
            {
                col2.Space(6);
                ImUi.Text_(col2.Row(24), "Zones (none ticked = any):");
                foreach (var z in _zoneNames) ToggleList(_bp.zones, z, col2.Row());
            }
            float by = box.yMax - 62;
            if (ImUi.Button(new Rect(box.x + 24, by, 220, 40), "SAVE TO GAME", true, "Saves the room and turns spawning on."))
            { _modal = Modal.None; SaveBlueprint(true); }
            if (ImUi.Button(new Rect(box.xMax - 24 - 140, by, 140, 40), "DONE")) _modal = Modal.None;
        }

        void DrawSelected(ref ImUi.Column col)
        {
            var v = V;
            var so = SelectedOpening;
            if (so != null)
            {
                ImUi.Header(col.Row(24), so.Door ? (so.arch ? "Selected arch" : "Selected door") : "Selected window");
                ImUi.Dimmed(col.Row(34), "Drag its handles in the view, or set it here. Alt: no snapping.");
                float ceil = v.CeilingAlong(so.vertical, so.line, so.from, so.to);
                var before = so.Clone();
                float width = Slide(col.Row(), "Width (m)", so.Width, RoomVariant.MinOpening, Mathf.Min(v.LineLength(so.vertical), 9f), "0.00");
                if (Mathf.Abs(width - so.Width) > 0.004f) { float c = so.Centre; so.from = c - width * 0.5f; so.to = c + width * 0.5f; }
                float top = Slide(col.Row(), "Top (m)", so.top, 0.6f, ceil, "0.00", "Height of its top above the floor.");
                if (Mathf.Abs(top - so.top) > 0.004f) so.top = top;
                float sill = Slide(col.Row(), "Sill (m)", so.sill, 0f, Mathf.Max(0f, so.top - 0.3f), "0.00", "0 is a door you can walk through; above that it is a window.");
                if (Mathf.Abs(sill - so.sill) > 0.004f) so.sill = sill < 0.03f ? 0f : sill;
                bool arch = so.arch;
                Toggled(ref arch, col.Row(), "Round top (arch)");
                so.arch = arch;
                v.ClampOpening(so);
                bool changed = so.from != before.from || so.to != before.to || so.top != before.top || so.sill != before.sill || so.arch != before.arch;
                if (changed && !v.OpeningFits(so, so)) { so.from = before.from; so.to = before.to; so.top = before.top; so.sill = before.sill; so.arch = before.arch; changed = false; }
                if (changed) { _dirty = true; BuildSession.Dirty = true; BuilderSounds.Play(BuilderSounds.Sfx.Tick); RequestRebuild(); }
                var br = col.Row();
                if (ImUi.Button(new Rect(br.x, br.y, br.width * 0.5f - 3, br.height), so.Door ? "MAKE WINDOW" : "MAKE DOOR", false, "A door reaches the floor; a window has a sill."))
                { PushUndo(); so.sill = so.Door ? Mathf.Min(0.9f, so.top - 0.3f) : 0f; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Place); }
                if (ImUi.Button(new Rect(br.x + br.width * 0.5f + 3, br.y, br.width * 0.5f - 3, br.height), "TO CEILING", false, "Its top right up to the ceiling."))
                { PushUndo(); so.top = ceil; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Place); }
                if (ImUi.Button(col.Row(), "DELETE  [Del]")) DeleteSelected();
                col.Space(4);
                return;
            }
            if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count)
            {
                var p = v.props[_selected.index];
                ImUi.Header(col.Row(24), "Selected prop: " + p.kind);
                if (WallProp(p.kind)) ImUi.Dimmed(col.Row(34), "Wall-aligned: drag beside a wall to reposition.");
                float yaw = WallProp(p.kind) ? p.yaw : Slide(col.Row(), "Turn (°)", p.yaw, 0f, 359f, "0");
                if (Mathf.Abs(yaw - p.yaw) > 0.01f) { p.yaw = Mathf.Round(yaw / 5f) * 5f; _dirty = true; _needRebuild = true; }
                float ch = Slide(col.Row(), "Chance", p.chance, 0.05f, 1f, "0.00", "Chance this prop is there each time the room is placed.");
                if (Mathf.Abs(ch - p.chance) > 0.001f) { p.chance = ch; _dirty = true; }
                if (!WallProp(p.kind) && p.kind != PropKind.FloorLamp)
                {
                    bool pinned = p.pinned;
                    Toggled(ref pinned, col.Row(), "Physics off: stuck in place", "It never moves and cannot be taken: it can sit half inside a wall, float, or lie tipped over (the Backrooms way).");
                    if (pinned != p.pinned) { PushUndo(); p.pinned = pinned; Changed(); BuilderSounds.Play(pinned ? BuilderSounds.Sfx.Place : BuilderSounds.Sfx.Pick); }
                    float lift = Slide(col.Row(), "Lift (m)", p.elevation, -p.size.y, 3.5f, "0.00", "Up off the floor (or down into it). Its green handle does the same.");
                    float pitch = Slide(col.Row(), "Tip forward (°)", p.pitch, -180f, 180f, "0");
                    float roll = Slide(col.Row(), "Tip sideways (°)", p.roll, -180f, 180f, "0");
                    float scl = Slide(col.Row(), "Size (×)", p.scale > 0.01f ? p.scale : 1f, 0.25f, 4f, "0.00", "Its orange handle does the same.");
                    if (Mathf.Abs(lift - p.elevation) > 0.004f || Mathf.Abs(pitch - p.pitch) > 0.4f || Mathf.Abs(roll - p.roll) > 0.4f || Mathf.Abs(scl - p.scale) > 0.004f)
                    {
                        p.elevation = Mathf.Round(lift / 0.01f) * 0.01f; p.pitch = Mathf.Round(pitch); p.roll = Mathf.Round(roll); p.scale = Mathf.Round(scl / 0.01f) * 0.01f;
                        _dirty = true; BuildSession.Dirty = true;
                        BuilderSounds.Play(BuilderSounds.Sfx.Tick);
                        if (!HasView(_selected.index)) RequestRebuild(); else _rebuildPending = true;
                        if (!p.pinned && (Mathf.Abs(p.elevation) > 0.01f || Mathf.Abs(p.pitch) > 1f || Mathf.Abs(p.roll) > 1f))
                            ImUi.Dimmed(col.Row(34), "With physics on it drops as soon as anything touches it. Tick 'Physics off' to keep it like this.");
                    }
                }
                if (p.kind == PropKind.ConferenceTable) { float len = Slide(col.Row(), "Length (m)", p.size.x, 1.2f, 4.8f, "0.0"); if (Mathf.Abs(len - p.size.x) > 0.01f) { p.size.x = len; _dirty = true; _needRebuild = true; } }
                if (p.kind == PropKind.FloorLamp) { bool lit = p.lit; Toggled(ref lit, col.Row(), "Lit"); if (lit != p.lit) { p.lit = lit; _dirty = true; _needRebuild = true; } }
                if (p.kind == PropKind.WallClock)
                {
                    int cx = Mathf.Clamp(Mathf.FloorToInt(p.x / C), 0, v.width - 1), cz = Mathf.Clamp(Mathf.FloorToInt(p.z / C), 0, v.depth - 1);
                    float height = Slide(col.Row(), "Height (m)", p.size.y, 0.4f, v.RoomAt(cx, cz).ceiling - 0.25f, "0.00", "Clock centre above the floor.");
                    if (Mathf.Abs(height - p.size.y) > 0.001f) { PushUndo(); p.size.y = height; Changed(); }
                }
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

        /// <summary>The hint line: a message just shown, else the control under the pointer, else what a click does now.</summary>
        void DrawBottomBar()
        {
            var r = BottomRect;
            bool status = Time.unscaledTime < _statusUntil;
            string msg = status ? _status : !string.IsNullOrEmpty(ImUi.Tip) ? ImUi.Tip : CurrentMode.Hint();
            GUI.Label(new Rect(r.x + 16, r.y + 4, r.width - 460, r.height - 8), msg, new GUIStyle(ImUi.Small) { wordWrap = true, fontSize = 15, alignment = TextAnchor.MiddleLeft, normal = { textColor = status ? ImUi.Accent : ImUi.Text } });
            GUI.Label(new Rect(r.xMax - 440, r.y + 4, 428, r.height - 8), "RMB turn · MMB/WASD pan · wheel zoom · F frame · Tab view", new GUIStyle(ImUi.Small) { alignment = TextAnchor.MiddleRight, normal = { textColor = ImUi.Dim } });
        }

        // ---------------- Save / test ----------------

        void DrawHelp()
        {
            ImUi.Fill(new Rect(0, 0, ImUi.Width, ImUi.Height), new Color(0, 0, 0, 0.72f));
            float width = Mathf.Min(780f, ImUi.Width - 40f);
            var box = new Rect((ImUi.Width - width) * 0.5f, 140f, width, 760f);
            ImUi.PanelBox(box);
            var col = new ImUi.Column(box.x + 24, box.y + 18, box.width - 48, 36, 10);
            GUI.Label(col.Row(42), "BUILD MODE HELP", ImUi.Title);
            string[] lines =
            {
                "1 SELECT: click anything to select it; drag to move; drag its handles to size it. Arrows outside the walls grow the room.",
                "2 BUILD: drag along the grid for walls and half walls; click corners for pillars. Ctrl while drawing erases.",
                "3 DOORS & WINDOWS: point at a wall and click; drag the sides, top, bottom (sill) or middle.",
                "4 FURNISH: pick a prop and click the floor (racks and clocks against a wall). Loot spots too.",
                "5 PAINT: paint cells into rooms (each its own ceiling, walls, floor and lights); click cells for lights.",
                "Right panel: whatever is selected, or the room as a whole. PUT IN THE WORLD sets where it spawns.",
                "Alt drags without snapping. R turns a prop. Delete removes. Ctrl+D duplicates. Esc deselects.",
                "RMB orbit, MMB or Shift+RMB or WASD pan, wheel zoom, Q / E rotate, F frame, Tab views, G grid.",
                "Ctrl+Z / Ctrl+Y undo / redo. Ctrl+S save. Ctrl+C / Ctrl+V copy and paste a room.",
                "TEST IN WORLD: play your current edits in the real world (Esc there to come back).",
            };
            foreach (var line in lines) GUI.Label(col.Row(38), line, new GUIStyle(ImUi.Label) { wordWrap = true });
            if (ImUi.Button(new Rect(box.xMax - 170, box.yMax - 62, 146, 40), "CLOSE  [F1]", true)) _showHelp = false;
        }

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
            if (_modal == Modal.World) { DrawWorldDialog(); return; }
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
                else if (m == Modal.Delete) { RoomLibrary.Delete(_modalTarget); if (_thumbCache.TryGetValue(_modalTarget, out var thumb) && thumb.tex) Destroy(thumb.tex); _thumbCache.Remove(_modalTarget); }
                else if (m == Modal.NoExit) { _noExitAcknowledged = true; DoSave(_pendingToGame); _noExitAcknowledged = false; }
            }
            if (ImUi.Button(new Rect(box.xMax - 24 - 140, by, 140, 40), "CANCEL")) _modal = Modal.None;
        }
    }
}
