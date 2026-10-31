using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    /// <summary>Build mode: input, picking, the editing tools, selection and undo.</summary>
    public partial class BuilderApp
    {
        enum Modal { None, Discard, Delete, Back, NoExit, World, Import, Templates }
        Modal _modal = Modal.None;
        string _modalTarget;
        bool _textFocus;

        // Selection and per-tool options
        enum SelKind { None, Prop, Loot, Opening, Room, Wall, Light, Pillar, Decal, Sound, Trigger }
        struct Selection { public SelKind kind; public int index; public static Selection None => new Selection { kind = SelKind.None, index = -1 }; }
        Selection _selected = Selection.None;
        PropKind _propKind = PropKind.Box;
        string _lootItem = "Loot";
        int _activeRoom;
        float _pillarSize = 0.6f;
        bool _dragging;
        static AreaClipboard _areaClipboard;
        bool _pastingArea;

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
            if (_walking)
            {
                _cam.pixelRect = new Rect(0, 0, Screen.width, Screen.height);
                UpdateWalk();
                if (_needRebuild) Rebuild();
                return;
            }
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
            UpdateAutosave();
            UpdateContextClick(mouse, overUi);
            if (PauseMenu.IsOpen || _showHelp) return;
            UpdateCamera(overUi);

            UpdateHandles(mouse.position.ReadValue(), overUi);
            if (!overUi && !_textFocus)
            {
                UpdateHover(mouse.position.ReadValue());
                UpdateHoverPick(mouse.position.ReadValue());
                if (mouse.leftButton.wasPressedThisFrame) BeginStroke(mouse.position.ReadValue());
                else if (_stroking && mouse.leftButton.isPressed) ContinueStroke(mouse.position.ReadValue());
            }
            else if (!_stroking) { _hoverEdgeValid = _hoverVertexValid = _hoverCellValid = _hoverGround = false; _hoverPick = Selection.None; }
            if (_stroking && (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)) EndStroke();
            UpdatePropViews();
            UpdateRebuildThrottle();
            if (_tool == BuilderTool.Prop && assets) BuilderCatalog.Picture(assets);

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
                else if (_showHelp) _showHelp = false;
                else if (_pastingArea) _pastingArea = false;
                else if (_contextOpen) _contextOpen = false;
                else if (_linkFirst != null) { _linkFirst = null; SetStatus("Cancelled."); }
                else if (_lining) _lining = false;
                else if (_modal != Modal.None) _modal = Modal.None;
                else if (_stroking) EndStroke();
                else if (_selected.kind != SelKind.None) { _selected = Selection.None; _group.Clear(); }
                else PauseMenu.Instance?.Open();
            }
            if (_textFocus || _modal != Modal.None || PauseMenu.IsOpen) return;
            if (kb.f1Key.wasPressedThisFrame) _showHelp = !_showHelp;
            if (_showHelp) return;
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (ctrl && kb.cKey.wasPressedThisFrame) CopyArea();
            if (ctrl && kb.vKey.wasPressedThisFrame) BeginPasteArea();
            if (ctrl && kb.zKey.wasPressedThisFrame) Undo();
            if (ctrl && kb.yKey.wasPressedThisFrame) Redo();
            if (ctrl && kb.sKey.wasPressedThisFrame) SaveBlueprint(false);
            if (ctrl && kb.dKey.wasPressedThisFrame) DuplicateSelected();
            if (kb.tabKey.wasPressedThisFrame) CycleView();
            if (kb.gKey.wasPressedThisFrame) _showGrid = !_showGrid;
            if (kb.hKey.wasPressedThisFrame && !ctrl) ToggleCeiling();
            if (kb.vKey.wasPressedThisFrame && !ctrl) BeginWalk();
            if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) DeleteSelected();
            if (kb.rKey.wasPressedThisFrame) RotateSelected();
            if (kb.mKey.wasPressedThisFrame && !ctrl) _showMap = !_showMap;
            Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6 };
            for (int i = 0; i < keys.Length; i++) if (kb[keys[i]].wasPressedThisFrame && !ctrl) SetMode(Modes[i]);
        }

        void SetTool(BuilderTool t)
        {
            _tool = t; _pastingArea = false;
            ModeOf(t).last = t;
            if (t != BuilderTool.Select && !(t == BuilderTool.Floor && _selected.kind == SelKind.Room)) _selected = Selection.None;
            if (t == BuilderTool.Floor) SelectRoom(_activeRoom);
        }

        void SetView(BuilderView v)
        {
            if (_view == v) return;
            _view = v;
            ApplyCeiling();
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
            BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.7f);
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
            _pastingArea = false;
            Changed();
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

        bool EdgeTool => _tool == BuilderTool.Opening;

        void BeginStroke(Vector2 screen)
        {
            if (_pastingArea) { PasteArea(); return; }
            _stroking = true; _strokeLocked = false; _lastAlong = int.MinValue; _lastMouse = screen; _strokeChanged = false; _dragging = false;
            // A handle under the pointer (of what is selected, or the room's sides) is dragged, whatever the tool.
            if (_hoverHandle != Handle.None) { BeginHandleDrag(screen); return; }
            switch (_tool)
            {
                case BuilderTool.Select: SelectClick(screen); break;
                case BuilderTool.Opening when _opening != OpeningKind.Gap: PlaceOpening(); _stroking = false; break;
                case BuilderTool.Prop: PlaceProp(); _stroking = false; break;
                case BuilderTool.Loot: PlaceLoot(); _stroking = false; break;
                case BuilderTool.Sound: PlaceSound(); _stroking = false; break;
                case BuilderTool.Pillar: TogglePillar(); _stroking = false; break;
                case BuilderTool.Light: CycleLight(); _stroking = false; break;
                case BuilderTool.Floor: case BuilderTool.Outside: PushUndo(); PaintCell(); break;
                case BuilderTool.Wall: case BuilderTool.Half: case BuilderTool.Erase: case BuilderTool.RoomBox: BeginLine(screen); break;
                case BuilderTool.Decal: PlaceDecal(screen); break;
                case BuilderTool.Link: LinkClick(screen); _stroking = false; break;
                case BuilderTool.Trigger: TriggerFloorClick(); _stroking = false; break;
                default:
                    if (EdgeTool) { PushUndo(); StrokeEdge(screen); }
                    else _stroking = false;
                    break;
            }
        }

        void ContinueStroke(Vector2 screen)
        {
            if (_dragHandle != Handle.None) { DragHandle(screen); _lastMouse = screen; return; }
            if (_lining) { ContinueLine(screen); _lastMouse = screen; return; }
            if (_draggingDecal) { DragDecal(screen); _lastMouse = screen; return; }
            if (_tool == BuilderTool.Select)
            {
                if (_boxing) { _boxNow = screen; if ((screen - _boxStart).magnitude > 8f) _boxActive = true; }
                else DragSelected();
                _lastMouse = screen; return;
            }
            // Sample along the pointer's path so a fast drag does not skip edges or cells.
            int steps = Mathf.Clamp(Mathf.CeilToInt((screen - _lastMouse).magnitude / 8f), 1, 24);
            for (int i = 1; i <= steps; i++)
            {
                var p = Vector2.Lerp(_lastMouse, screen, i / (float)steps);
                if (EdgeTool) StrokeEdge(p);
                else if (_tool == BuilderTool.Floor || _tool == BuilderTool.Outside) { if (GroundAt(p, out var lm)) { int cx = Mathf.FloorToInt(lm.x / C), cz = Mathf.FloorToInt(lm.y / C); if (V.InBounds(cx, cz)) { _hoverCell = new Vector2Int(cx, cz); _hoverCellValid = true; PaintCell(); } } }
            }
            _lastMouse = screen;
        }

        void EndStroke()
        {
            _stroking = false; _strokeLocked = false;
            if (_dragHandle != Handle.None) { EndHandleDrag(); _dragging = false; return; }
            if (_lining) { EndLine(); return; }
            if (_draggingDecal) { _draggingDecal = false; if (_strokeChanged) Changed(); else if (_undo.Count > 0) _undo.Pop(); _strokeChanged = false; return; }
            if (_boxing) { EndBox(); return; }
            if (_dragging && _selected.kind != SelKind.None) BuilderSounds.Play(BuilderSounds.Sfx.Drop, 0.95f);
            _dragging = false;
            if (_strokeChanged) { Changed(); }
        }

        // ---------------- Gizmos ----------------

        void DrawGizmos()
        {
            var v = V;
            // Cell highlight (Floor tool, Light tool).
            GL.Begin(GL.QUADS);
            if (_pastingArea && _areaClipboard != null && _hoverCellValid)
            {
                bool fits = _areaClipboard.Fits(v, _hoverCell);
                var colour = fits ? new Color(0.45f, 0.95f, 0.5f, 0.45f) : new Color(1f, 0.2f, 0.15f, 0.45f);
                for (int z = 0; z < _areaClipboard.bounds.height; z++)
                for (int x = 0; x < _areaClipboard.bounds.width; x++)
                {
                    if (!_areaClipboard.Cell(x, z)) continue;
                    float x0 = (_hoverCell.x + x) * C, z0 = (_hoverCell.y + z) * C;
                    Quad(W3(x0, z0, 0.08f), W3(x0 + C, z0, 0.08f), W3(x0 + C, z0 + C, 0.08f), W3(x0, z0 + C, 0.08f), colour);
                }
            }
            DrawFlash();
            if (_hoverCellValid && (_tool == BuilderTool.Floor || _tool == BuilderTool.Light || _tool == BuilderTool.Outside))
            {
                float x0 = _hoverCell.x * C, z0 = _hoverCell.y * C;
                Quad(W3(x0, z0), W3(x0 + C, z0), W3(x0 + C, z0 + C), W3(x0, z0 + C), new Color(0.93f, 0.78f, 0.28f, 0.28f));
            }
            // Room areas as a faint tint.
            for (int z = 0; z < v.depth; z++)
            for (int x = 0; x < v.width; x++)
            {
                int r = v.RoomOf(x, z);
                bool chosen = _selected.kind == SelKind.Room && _selected.index == r;
                if (v.rooms.Count > 1 || _tool == BuilderTool.Floor || chosen)
                {
                    var col = RoomColor(r); col.a = chosen ? 0.3f : _tool == BuilderTool.Floor ? 0.2f : 0.08f;
                    Quad(W3(x * C, z * C), W3((x + 1) * C, z * C), W3((x + 1) * C, (z + 1) * C), W3(x * C, (z + 1) * C), col);
                }
            }
            // Lights
            foreach (var f in v.fixtures)
            {
                float cx = (f.x + 0.5f) * C - 0.3f, cz = (f.z + 0.5f) * C, h = 0.27f;
                float fade = _tool == BuilderTool.Light ? 1f : 0.45f;
                var col = f.on ? new Color(1f, 0.95f, 0.5f, 0.55f * fade) : new Color(0.5f, 0.5f, 0.5f, 0.45f * fade);
                if (_tool == BuilderTool.Light || f.on == false || (_tool == BuilderTool.Select && _view != BuilderView.Solid)) Quad(W3(cx - h, cz - h), W3(cx + h, cz - h), W3(cx + h, cz + h), W3(cx - h, cz + h), col);
            }
            GL.End();

            GL.Begin(GL.LINES);
            DrawLineGizmo();
            DrawExtraGizmos();
            DrawHoverGizmos();
            DrawMeasureGizmos();
            // Hover on edges
            if (_hoverEdgeValid && (EdgeTool || (LineTool && !_lining)) && !(LineTool && _hoverVertexValid))
            {
                Vector3 a, b;
                if (_hoverEdgeVertical) { a = W3(_hoverEdgeX * C, _hoverEdgeZ * C, 0.05f); b = W3(_hoverEdgeX * C, (_hoverEdgeZ + 1) * C, 0.05f); }
                else { a = W3(_hoverEdgeX * C, _hoverEdgeZ * C, 0.05f); b = W3((_hoverEdgeX + 1) * C, _hoverEdgeZ * C, 0.05f); }
                Thick(a, b, new Color(1f, 0.9f, 0.3f, 1f));
            }
            if (_hoverVertexValid && (_tool == BuilderTool.Pillar || (LineTool && !_lining)))
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
                float yaw = 0f;
                var size = _catalogPick != null ? _catalogPick.size : DefaultSize(_propKind);
                bool valid = _tool != BuilderTool.Prop || (_catalogPick != null && (!WallProp(_catalogPick.kind) || v.TryWallPlacement(_hoverMetres, size, out m, out yaw)));
                var c = valid ? new Color(1f, 1f, 1f, 0.75f) : ImUi.Bad;
                if (_tool == BuilderTool.Prop)
                {
                    // Its footprint where it would go, and which way it faces.
                    var q = Quaternion.Euler(0f, yaw, 0f); var p0 = W3(m.x, m.y, 0.05f);
                    Vector3 hx = q * new Vector3(size.x * 0.5f, 0f, 0f), hz = q * new Vector3(0f, 0f, size.z * 0.5f);
                    Line(p0 - hx - hz, p0 + hx - hz, c); Line(p0 + hx - hz, p0 + hx + hz, c); Line(p0 + hx + hz, p0 - hx + hz, c); Line(p0 - hx + hz, p0 - hx - hz, c);
                    Line(p0, p0 + hz * 1.4f, c);
                }
                else Marker(W3(m.x, m.y, 0.06f), 0.22f, yaw, c, false);
            }
            DrawHandleGizmos();
            DrawPickGizmos();
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
                // (Only out of the room: not along a cut-away notch's own edge, which is the world's.)
                int cx = Mathf.Clamp(Mathf.FloorToInt((mx - dx * 0.1f) / C), 0, v.width - 1), cz = Mathf.Clamp(Mathf.FloorToInt((mz - dz * 0.1f) / C), 0, v.depth - 1);
                if (v.Outside(cx, cz)) return;
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
            foreach (var o in v.openings)
            {
                if (!o.Door || !v.PerimeterLine(o.vertical, o.line)) continue;
                bool far = o.line != 0;
                if (o.vertical) Arrow(o.line * C, o.Centre, far ? 1 : -1, 0); else Arrow(o.Centre, o.line * C, 0, far ? 1 : -1);
            }
        }

        static Color RoomColor(int i)
        {
            Color[] palette = { new Color(0.4f, 0.7f, 1f), new Color(1f, 0.55f, 0.4f), new Color(0.6f, 0.9f, 0.45f), new Color(0.85f, 0.5f, 0.95f), new Color(1f, 0.85f, 0.35f), new Color(0.4f, 0.95f, 0.85f) };
            return palette[((i % palette.Length) + palette.Length) % palette.Length];
        }
    }
}
