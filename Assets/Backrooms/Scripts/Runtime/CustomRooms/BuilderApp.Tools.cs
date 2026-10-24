using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    /// <summary>Build mode: input, picking, the editing tools, selection and undo.</summary>
    public partial class BuilderApp
    {
        enum Modal { None, Discard, Delete, Back, NoExit }
        Modal _modal = Modal.None;
        string _modalTarget;
        bool _textFocus;

        // Selection and per-tool options
        enum SelKind { None, Prop, Loot }
        struct Selection { public SelKind kind; public int index; public static Selection None => new Selection { kind = SelKind.None, index = -1 }; }
        Selection _selected = Selection.None;
        PropKind _propKind = PropKind.Box;
        string _lootItem = "Loot";
        int _activeRoom;
        float _pillarSize = 0.6f;
        bool _dragging;

        // Undo
        readonly Stack<string> _undo = new Stack<string>(), _redo = new Stack<string>();

        // Picking results (blueprint-local)
        bool _hoverGround;
        Vector2 _hoverMetres;
        bool _hoverEdgeValid, _hoverEdgeVertical;
        int _hoverEdgeX, _hoverEdgeZ;
        bool _hoverVertexValid; Vector2Int _hoverVertex;
        bool _hoverCellValid; Vector2Int _hoverCell;

        // Stroke
        bool _stroking, _strokeLocked, _strokeVertical;
        int _strokeLine, _lastAlong = int.MinValue;
        Vector2 _lastMouse;
        bool _strokeChanged;

        static readonly BuilderView[] ViewCycle = { BuilderView.Solid, BuilderView.Room, BuilderView.Cutout, BuilderView.WallsDown, BuilderView.Top };

        void Update()
        {
            // The 3D view fills the space between the panels (so nothing is drawn under them).
            float sc = ImUi.Scale;
            _cam.pixelRect = _screen == ScreenKind.Editor && _bp != null
                ? new Rect(LeftW * sc, BottomH * sc, Screen.width - (LeftW + RightW) * sc, Screen.height - (TopH + BottomH) * sc)
                : new Rect(0, 0, Screen.width, Screen.height);
            if (_screen != ScreenKind.Editor || _bp == null)
            {
                HandleLibraryKeys();
                return;
            }
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (mouse == null || kb == null) return;

            bool overUi = OverUi(ImUi.Pointer) || _modal != Modal.None || PauseMenu.IsOpen;
            HandleKeys(kb);
            if (PauseMenu.IsOpen) return;
            UpdateCamera(overUi);

            if (!overUi && !_textFocus)
            {
                UpdateHover(mouse.position.ReadValue());
                if (mouse.leftButton.wasPressedThisFrame) BeginStroke(mouse.position.ReadValue());
                else if (_stroking && mouse.leftButton.isPressed) ContinueStroke(mouse.position.ReadValue());
            }
            else if (!_stroking) { _hoverEdgeValid = _hoverVertexValid = _hoverCellValid = _hoverGround = false; }
            if (_stroking && (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)) EndStroke();

            // Views that depend on where the camera is need a new build when the cut-down walls change.
            if (_view == BuilderView.Cutout && !_needRebuild)
            {
                int hash = ComputeCutHash(out _);
                if (hash != _cutHash) _needRebuild = true;
            }
            if (_needRebuild) Rebuild();
        }

        void HandleKeys(Keyboard kb)
        {
            if (kb.escapeKey.wasPressedThisFrame && PauseMenu.ToggledFrame != Time.frameCount)
            {
                if (PauseMenu.IsOpen) PauseMenu.Instance.Close();
                else if (_modal != Modal.None) _modal = Modal.None;
                else if (_stroking) EndStroke();
                else if (_selected.kind != SelKind.None) _selected = Selection.None;
                else PauseMenu.Instance?.Open();
            }
            if (_textFocus || _modal != Modal.None || PauseMenu.IsOpen) return;
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (ctrl && kb.zKey.wasPressedThisFrame) Undo();
            if (ctrl && kb.yKey.wasPressedThisFrame) Redo();
            if (ctrl && kb.sKey.wasPressedThisFrame) SaveBlueprint(false);
            if (kb.tabKey.wasPressedThisFrame) CycleView();
            if (kb.gKey.wasPressedThisFrame) _showGrid = !_showGrid;
            if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) DeleteSelected();
            if (kb.rKey.wasPressedThisFrame) RotateSelected();
            Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0 };
            for (int i = 0; i < keys.Length; i++) if (kb[keys[i]].wasPressedThisFrame && !ctrl) SetTool((BuilderTool)i);
        }

        void SetTool(BuilderTool t) { _tool = t; if (t != BuilderTool.Select) _selected = Selection.None; }

        void SetView(BuilderView v)
        {
            if (_view == v) return;
            _view = v;
            if (v == BuilderView.Top) _pitch = 90f; else if (_pitch > 85f) _pitch = 52f;
            _needRebuild = true;
        }

        void CycleView()
        {
            int i = System.Array.IndexOf(ViewCycle, _view);
            SetView(ViewCycle[(i + 1) % ViewCycle.Length]);
        }

        // ---------------- Undo ----------------

        void PushUndo()
        {
            _undo.Push(_bp.ToJson());
            if (_undo.Count > 80) { var keep = new List<string>(_undo); keep.RemoveRange(80, keep.Count - 80); _undo.Clear(); for (int i = keep.Count - 1; i >= 0; i--) _undo.Push(keep[i]); }
            _redo.Clear();
        }

        void Undo()
        {
            if (_undo.Count == 0) { SetStatus("Nothing to undo."); return; }
            _redo.Push(_bp.ToJson());
            Restore(_undo.Pop());
        }

        void Redo()
        {
            if (_redo.Count == 0) { SetStatus("Nothing to redo."); return; }
            _undo.Push(_bp.ToJson());
            Restore(_redo.Pop());
        }

        void Restore(string json)
        {
            var b = RoomBlueprint.FromJson(json);
            if (b == null) return;
            _bp = b; BuildSession.Editing = b;
            _vi = Mathf.Clamp(_vi, 0, b.variants.Count - 1);
            _activeRoom = Mathf.Clamp(_activeRoom, 0, V.rooms.Count - 1);
            _selected = Selection.None;
            Changed(false);
        }

        void Changed(bool dirty = true)
        {
            if (dirty) { _dirty = true; BuildSession.Dirty = true; }
            _needRebuild = true;
        }

        // ---------------- Picking ----------------

        bool GroundAt(Vector2 screen, out Vector2 localMetres)
        {
            localMetres = default;
            var ray = _cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f || t > 400f) return false;
            var p = ray.origin + ray.direction * t;
            localMetres = new Vector2(p.x - _origin.x, p.z - _origin.y);
            return true;
        }

        void UpdateHover(Vector2 screen)
        {
            _hoverEdgeValid = _hoverVertexValid = _hoverCellValid = false;
            _hoverGround = GroundAt(screen, out _hoverMetres);
            if (!_hoverGround) return;
            var v = V;
            float gx = _hoverMetres.x / C, gz = _hoverMetres.y / C;
            int cx = Mathf.FloorToInt(gx), cz = Mathf.FloorToInt(gz);
            if (v.InBounds(cx, cz)) { _hoverCellValid = true; _hoverCell = new Vector2Int(cx, cz); }
            int vx = Mathf.RoundToInt(gx), vz = Mathf.RoundToInt(gz);
            if (vx >= 0 && vz >= 0 && vx <= v.width && vz <= v.depth && Mathf.Max(Mathf.Abs(gx - vx), Mathf.Abs(gz - vz)) < 0.4f) { _hoverVertexValid = true; _hoverVertex = new Vector2Int(vx, vz); }
            PickEdge(gx, gz, out _hoverEdgeValid, out _hoverEdgeVertical, out _hoverEdgeX, out _hoverEdgeZ);
        }

        void PickEdge(float gx, float gz, out bool valid, out bool vertical, out int x, out int z)
        {
            var v = V;
            valid = false; vertical = false; x = z = 0;
            int xi = Mathf.RoundToInt(gx), zj = Mathf.RoundToInt(gz);
            float dv = Mathf.Abs(gx - xi), dh = Mathf.Abs(gz - zj);
            int zi = Mathf.FloorToInt(gz), xj = Mathf.FloorToInt(gx);
            bool vOk = xi >= 0 && xi <= v.width && zi >= 0 && zi < v.depth && dv < 0.4f;
            bool hOk = zj >= 0 && zj <= v.depth && xj >= 0 && xj < v.width && dh < 0.4f;
            if (vOk && (!hOk || dv <= dh)) { valid = true; vertical = true; x = xi; z = zi; }
            else if (hOk) { valid = true; vertical = false; x = xj; z = zj; }
        }

        // ---------------- Strokes ----------------

        bool EdgeTool => _tool == BuilderTool.Wall || _tool == BuilderTool.Opening || _tool == BuilderTool.Half || _tool == BuilderTool.Erase;

        void BeginStroke(Vector2 screen)
        {
            _stroking = true; _strokeLocked = false; _lastAlong = int.MinValue; _lastMouse = screen; _strokeChanged = false; _dragging = false;
            switch (_tool)
            {
                case BuilderTool.Select: SelectAt(); break;
                case BuilderTool.Prop: PlaceProp(); _stroking = false; break;
                case BuilderTool.Loot: PlaceLoot(); _stroking = false; break;
                case BuilderTool.Pillar: TogglePillar(); _stroking = false; break;
                case BuilderTool.Light: CycleLight(); _stroking = false; break;
                case BuilderTool.Floor: PushUndo(); PaintCell(); break;
                default:
                    if (EdgeTool) { PushUndo(); StrokeEdge(screen); }
                    else _stroking = false;
                    break;
            }
        }

        void ContinueStroke(Vector2 screen)
        {
            if (_tool == BuilderTool.Select) { DragSelected(); _lastMouse = screen; return; }
            // Sample along the pointer's path so a fast drag does not skip edges or cells.
            int steps = Mathf.Clamp(Mathf.CeilToInt((screen - _lastMouse).magnitude / 8f), 1, 24);
            for (int i = 1; i <= steps; i++)
            {
                var p = Vector2.Lerp(_lastMouse, screen, i / (float)steps);
                if (EdgeTool) StrokeEdge(p);
                else if (_tool == BuilderTool.Floor) { if (GroundAt(p, out var lm)) { int cx = Mathf.FloorToInt(lm.x / C), cz = Mathf.FloorToInt(lm.y / C); if (V.InBounds(cx, cz)) { _hoverCell = new Vector2Int(cx, cz); _hoverCellValid = true; PaintCell(); } } }
            }
            _lastMouse = screen;
        }

        void EndStroke()
        {
            _stroking = false; _strokeLocked = false; _dragging = false;
            if (_strokeChanged) { Changed(); }
        }

        void StrokeEdge(Vector2 screen)
        {
            if (!GroundAt(screen, out var lm)) return;
            var v = V;
            float gx = lm.x / C, gz = lm.y / C;
            if (!_strokeLocked)
            {
                PickEdge(gx, gz, out bool ok, out bool vertical, out int x, out int z);
                if (!ok) return;
                _strokeLocked = true; _strokeVertical = vertical; _strokeLine = vertical ? x : z;
                _lastAlong = int.MinValue;
            }
            int along = _strokeVertical ? Mathf.FloorToInt(gz) : Mathf.FloorToInt(gx);
            int max = _strokeVertical ? v.depth : v.width;
            if (along < 0 || along >= max || along == _lastAlong) return;
            // Close to the line being drawn, not just anywhere on screen.
            float off = _strokeVertical ? Mathf.Abs(gx - _strokeLine) : Mathf.Abs(gz - _strokeLine);
            if (off > 0.9f) return;
            _lastAlong = along;
            if (_strokeVertical) ApplyEdge(true, _strokeLine, along); else ApplyEdge(false, along, _strokeLine);
        }

        // ---------------- Edge tools ----------------

        int CodeForTool(bool perimeter)
        {
            switch (_tool)
            {
                case BuilderTool.Wall: return EdgeCode.Wall;
                case BuilderTool.Half: return perimeter ? -1 : EdgeCode.Half;
                case BuilderTool.Erase: return perimeter ? EdgeCode.Wall : EdgeCode.Open;
                case BuilderTool.Opening:
                    switch (_opening)
                    {
                        case OpeningKind.Arch: return EdgeCode.Header | EdgeCode.Arch;
                        case OpeningKind.Tall: return EdgeCode.Tall;
                        case OpeningKind.Gap: return perimeter ? EdgeCode.Tall : EdgeCode.Open;
                        default: return EdgeCode.Header;
                    }
            }
            return -1;
        }

        void ApplyEdge(bool vertical, int x, int z)
        {
            var v = V;
            bool perimeter = vertical ? (x == 0 || x == v.width) : (z == 0 || z == v.depth);
            int code = CodeForTool(perimeter);
            if (code < 0) { SetStatus("Half walls go inside the room; the outer edge is a wall or an exit."); return; }
            int old = vertical ? v.V(x, z) : v.H(x, z);
            if (old == code) return;
            if (vertical) v.SetV(x, z, code); else v.SetH(x, z, code);
            _strokeChanged = true;
            _needRebuild = true;
        }

        // ---------------- Cells, pillars, lights ----------------

        void PaintCell()
        {
            if (!_hoverCellValid) return;
            var v = V;
            int idx = _hoverCell.y * v.width + _hoverCell.x;
            _activeRoom = Mathf.Clamp(_activeRoom, 0, v.rooms.Count - 1);
            if (v.cellRoom[idx] == _activeRoom) return;
            v.cellRoom[idx] = _activeRoom;
            _strokeChanged = true; _needRebuild = true;
        }

        void TogglePillar()
        {
            if (!_hoverVertexValid) return;
            PushUndo();
            var v = V;
            int i = v.pillars.FindIndex(p => p.x == _hoverVertex.x && p.z == _hoverVertex.y);
            if (i >= 0) v.pillars.RemoveAt(i); else v.pillars.Add(new PillarDef { x = _hoverVertex.x, z = _hoverVertex.y, size = _pillarSize });
            Changed();
        }

        void CycleLight()
        {
            if (!_hoverCellValid) return;
            PushUndo();
            var v = V;
            int i = v.fixtures.FindIndex(f => f.x == _hoverCell.x && f.z == _hoverCell.y);
            if (i < 0) v.fixtures.Add(new FixtureDef { x = _hoverCell.x, z = _hoverCell.y, on = true });
            else if (v.fixtures[i].on) v.fixtures[i].on = false;
            else v.fixtures.RemoveAt(i);
            Changed();
        }

        // ---------------- Props and loot ----------------

        static Vector3 DefaultSize(PropKind k)
        {
            switch (k)
            {
                case PropKind.Sofa: return new Vector3(1.9f, 0.8f, 0.9f);
                case PropKind.ConferenceTable: return new Vector3(2.4f, 0.74f, 1.1f);
                case PropKind.Pallet: return new Vector3(1.2f, 1.6f, 1.0f);
                case PropKind.FloorLamp: return new Vector3(0.4f, 1.6f, 0.4f);
                default: return new Vector3(0.6f, 0.5f, 0.5f);
            }
        }

        Vector2 Snap(Vector2 m) => new Vector2(Mathf.Round(m.x / 0.15f) * 0.15f, Mathf.Round(m.y / 0.15f) * 0.15f);

        bool InsideRoom(Vector2 m) { var s = V.SizeMetres; return m.x > 0.05f && m.y > 0.05f && m.x < s.x - 0.05f && m.y < s.y - 0.05f; }

        void PlaceProp()
        {
            if (!_hoverGround) return;
            var m = Snap(_hoverMetres);
            if (!InsideRoom(m)) { SetStatus("Place props inside the room."); return; }
            PushUndo();
            var v = V;
            v.props.Add(new PropDef { kind = _propKind, x = m.x, z = m.y, size = DefaultSize(_propKind), lit = true, variant = Random.Range(0, 1 << 16) });
            _selected = new Selection { kind = SelKind.Prop, index = v.props.Count - 1 };
            _tool = BuilderTool.Select;
            Changed();
        }

        void PlaceLoot()
        {
            if (!_hoverGround) return;
            var m = Snap(_hoverMetres);
            if (!InsideRoom(m)) { SetStatus("Place loot inside the room."); return; }
            PushUndo();
            var v = V;
            v.loot.Add(new LootDef { item = string.IsNullOrWhiteSpace(_lootItem) ? "Loot" : _lootItem, x = m.x, z = m.y });
            _selected = new Selection { kind = SelKind.Loot, index = v.loot.Count - 1 };
            _tool = BuilderTool.Select;
            Changed();
        }

        void SelectAt()
        {
            _selected = Selection.None;
            if (!_hoverGround) return;
            var v = V;
            float best = 0.6f;
            for (int i = 0; i < v.props.Count; i++)
            {
                float d = Vector2.Distance(new Vector2(v.props[i].x, v.props[i].z), _hoverMetres);
                if (d < best) { best = d; _selected = new Selection { kind = SelKind.Prop, index = i }; }
            }
            for (int i = 0; i < v.loot.Count; i++)
            {
                float d = Vector2.Distance(new Vector2(v.loot[i].x, v.loot[i].z), _hoverMetres);
                if (d < best) { best = d; _selected = new Selection { kind = SelKind.Loot, index = i }; }
            }
            if (_selected.kind != SelKind.None) { PushUndo(); _dragging = true; }
        }

        void DragSelected()
        {
            if (!_dragging || !_hoverGround) return;
            var v = V;
            var m = Snap(_hoverMetres);
            if (!InsideRoom(m)) return;
            if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count) { if (v.props[_selected.index].x != m.x || v.props[_selected.index].z != m.y) { v.props[_selected.index].x = m.x; v.props[_selected.index].z = m.y; _strokeChanged = true; _needRebuild = true; } }
            if (_selected.kind == SelKind.Loot && _selected.index < v.loot.Count) { if (v.loot[_selected.index].x != m.x || v.loot[_selected.index].z != m.y) { v.loot[_selected.index].x = m.x; v.loot[_selected.index].z = m.y; _strokeChanged = true; _needRebuild = true; } }
        }

        void DeleteSelected()
        {
            var v = V;
            if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count) { PushUndo(); v.props.RemoveAt(_selected.index); }
            else if (_selected.kind == SelKind.Loot && _selected.index < v.loot.Count) { PushUndo(); v.loot.RemoveAt(_selected.index); }
            else return;
            _selected = Selection.None;
            Changed();
        }

        void RotateSelected()
        {
            var v = V;
            if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count) { PushUndo(); v.props[_selected.index].yaw = (v.props[_selected.index].yaw + 45f) % 360f; Changed(); }
        }

        // ---------------- Gizmos ----------------

        void DrawGizmos()
        {
            var v = V;
            // Cell highlight (Floor tool, Light tool).
            GL.Begin(GL.QUADS);
            if (_hoverCellValid && (_tool == BuilderTool.Floor || _tool == BuilderTool.Light))
            {
                float x0 = _hoverCell.x * C, z0 = _hoverCell.y * C;
                Quad(W3(x0, z0), W3(x0 + C, z0), W3(x0 + C, z0 + C), W3(x0, z0 + C), new Color(0.93f, 0.78f, 0.28f, 0.28f));
            }
            // Room areas as a faint tint.
            for (int z = 0; z < v.depth; z++)
            for (int x = 0; x < v.width; x++)
            {
                int r = v.RoomOf(x, z);
                if (v.rooms.Count > 1 || _tool == BuilderTool.Floor)
                {
                    var col = RoomColor(r); col.a = _tool == BuilderTool.Floor ? 0.22f : 0.09f;
                    Quad(W3(x * C, z * C), W3((x + 1) * C, z * C), W3((x + 1) * C, (z + 1) * C), W3(x * C, (z + 1) * C), col);
                }
            }
            // Lights
            foreach (var f in v.fixtures)
            {
                float cx = (f.x + 0.5f) * C - 0.3f, cz = (f.z + 0.5f) * C, h = 0.27f;
                var col = f.on ? new Color(1f, 0.95f, 0.5f, 0.55f) : new Color(0.5f, 0.5f, 0.5f, 0.45f);
                if (_tool == BuilderTool.Light || f.on == false) Quad(W3(cx - h, cz - h), W3(cx + h, cz - h), W3(cx + h, cz + h), W3(cx - h, cz + h), col);
            }
            GL.End();

            GL.Begin(GL.LINES);
            // Hover on edges
            if (_hoverEdgeValid && EdgeTool)
            {
                Vector3 a, b;
                if (_hoverEdgeVertical) { a = W3(_hoverEdgeX * C, _hoverEdgeZ * C, 0.05f); b = W3(_hoverEdgeX * C, (_hoverEdgeZ + 1) * C, 0.05f); }
                else { a = W3(_hoverEdgeX * C, _hoverEdgeZ * C, 0.05f); b = W3((_hoverEdgeX + 1) * C, _hoverEdgeZ * C, 0.05f); }
                Thick(a, b, new Color(1f, 0.9f, 0.3f, 1f));
            }
            if (_hoverVertexValid && _tool == BuilderTool.Pillar)
            {
                float s = 0.3f; var c = new Color(1f, 0.9f, 0.3f, 1f);
                var p = W3(_hoverVertex.x * C, _hoverVertex.y * C, 0.05f);
                Line(p + new Vector3(-s, 0, -s), p + new Vector3(s, 0, -s), c); Line(p + new Vector3(s, 0, -s), p + new Vector3(s, 0, s), c);
                Line(p + new Vector3(s, 0, s), p + new Vector3(-s, 0, s), c); Line(p + new Vector3(-s, 0, s), p + new Vector3(-s, 0, -s), c);
            }
            // Props and loot markers
            for (int i = 0; i < v.props.Count; i++)
            {
                var pr = v.props[i];
                bool sel = _selected.kind == SelKind.Prop && _selected.index == i;
                Marker(W3(pr.x, pr.z, 0.06f), 0.28f, pr.yaw, sel ? new Color(1f, 0.9f, 0.3f) : new Color(0.55f, 0.8f, 1f, 0.8f), true);
            }
            for (int i = 0; i < v.loot.Count; i++)
            {
                var l = v.loot[i];
                bool sel = _selected.kind == SelKind.Loot && _selected.index == i;
                Marker(W3(l.x, l.z, 0.35f), 0.2f, 45f, sel ? new Color(1f, 0.9f, 0.3f) : new Color(0.5f, 1f, 0.55f), false);
            }
            if (_hoverGround && (_tool == BuilderTool.Prop || _tool == BuilderTool.Loot))
            {
                var m = Snap(_hoverMetres);
                Marker(W3(m.x, m.y, 0.06f), 0.22f, 0f, new Color(1f, 1f, 1f, 0.7f), _tool == BuilderTool.Prop);
            }
            // Exits: arrows on the outside of perimeter openings
            DrawExits(v);
            GL.End();
        }

        void Thick(Vector3 a, Vector3 b, Color c)
        {
            var d = (b - a).normalized; var side = Vector3.Cross(d, Vector3.up) * 0.025f;
            Line(a, b, c); Line(a + side, b + side, c); Line(a - side, b - side, c);
        }

        void Marker(Vector3 p, float r, float yaw, Color c, bool square)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            if (square)
            {
                Vector3 A = p + q * new Vector3(-r, 0, -r), B = p + q * new Vector3(r, 0, -r), Cc = p + q * new Vector3(r, 0, r), D = p + q * new Vector3(-r, 0, r);
                Line(A, B, c); Line(B, Cc, c); Line(Cc, D, c); Line(D, A, c); Line(p, p + q * new Vector3(0, 0, r * 1.5f), c);
            }
            else
            {
                Vector3 A = p + new Vector3(-r, 0, 0), B = p + new Vector3(0, 0, -r), Cc = p + new Vector3(r, 0, 0), D = p + new Vector3(0, 0, r);
                Line(A, B, c); Line(B, Cc, c); Line(Cc, D, c); Line(D, A, c);
                Line(p + Vector3.up * 0.15f, p - Vector3.up * 0.15f, c);
            }
        }

        void DrawExits(RoomVariant v)
        {
            var c = new Color(0.45f, 0.95f, 0.5f, 1f);
            void Arrow(float mx, float mz, float dx, float dz)
            {
                var p = W3(mx, mz, 0.05f); var d = new Vector3(dx, 0, dz); var s = new Vector3(-dz, 0, dx);
                Line(p, p + d * 0.9f, c); Line(p + d * 0.9f, p + d * 0.55f + s * 0.3f, c); Line(p + d * 0.9f, p + d * 0.55f - s * 0.3f, c);
            }
            for (int z = 0; z < v.depth; z++)
            {
                if (EdgeCode.Walkthrough(v.V(0, z))) Arrow(0, (z + 0.5f) * C, -1, 0);
                if (EdgeCode.Walkthrough(v.V(v.width, z))) Arrow(v.width * C, (z + 0.5f) * C, 1, 0);
            }
            for (int x = 0; x < v.width; x++)
            {
                if (EdgeCode.Walkthrough(v.H(x, 0))) Arrow((x + 0.5f) * C, 0, 0, -1);
                if (EdgeCode.Walkthrough(v.H(x, v.depth))) Arrow((x + 0.5f) * C, v.depth * C, 0, 1);
            }
        }

        static Color RoomColor(int i)
        {
            Color[] palette = { new Color(0.4f, 0.7f, 1f), new Color(1f, 0.55f, 0.4f), new Color(0.6f, 0.9f, 0.45f), new Color(0.85f, 0.5f, 0.95f), new Color(1f, 0.85f, 0.35f), new Color(0.4f, 0.95f, 0.85f) };
            return palette[((i % palette.Length) + palette.Length) % palette.Length];
        }
    }
}
