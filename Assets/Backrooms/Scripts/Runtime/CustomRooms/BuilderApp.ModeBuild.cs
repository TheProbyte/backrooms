using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    // Build mode, BUILD: walls and half walls drawn point to point (press on a grid corner, drag, let go: a straight run with
    // its length shown as you go), rooms drawn as a box (walls round it, its cells a room of their own), pillars; Ctrl erases.
    public partial class BuilderApp
    {
        sealed class BuildMode : Mode
        {
            public BuildMode(BuilderApp app) : base(app) { }
            public override string Name => "BUILD";
            public override int Icon => 1;
            public override string Tip => "Walls, half walls, whole rooms and pillars.";
            public override BuilderTool[] Tools => new[] { BuilderTool.Wall, BuilderTool.Half, BuilderTool.RoomBox, BuilderTool.Pillar, BuilderTool.Erase };
            public override string ToolName(BuilderTool t) => t == BuilderTool.Wall ? "WALL" : t == BuilderTool.Half ? "HALF WALL" : t == BuilderTool.RoomBox ? "ROOM" : t == BuilderTool.Pillar ? "PILLAR" : "ERASE";
            public override string ToolTip(BuilderTool t) =>
                t == BuilderTool.Wall ? "Press on a grid corner and drag: a straight wall to where you let go, floor to ceiling. Hold Ctrl to erase instead."
                : t == BuilderTool.Half ? "A half-height partition (1.15 m) you can see and reach over, drawn the same way. Ctrl erases."
                : t == BuilderTool.RoomBox ? "Drag a box over the floor: walls round it and its cells a new room (its own ceiling, floor, lights)."
                : t == BuilderTool.Pillar ? "Click a grid corner to put a pillar there, or take it away."
                : "Drag along walls to take them away. On the outer wall it closes a gap again.";
            public override void Options(ref ImUi.Column col)
            {
                if (app._tool == BuilderTool.Pillar)
                {
                    ImUi.Header(col.Row(24), "Pillar size");
                    if (ImUi.Button(col.Row(30), "SLIM  0.45 m", app._pillarSize < 0.5f)) app._pillarSize = 0.45f;
                    if (ImUi.Button(col.Row(30), "NORMAL  0.6 m", app._pillarSize >= 0.5f)) app._pillarSize = 0.6f;
                    return;
                }
                ImUi.Dimmed(col.Row(96), app._tool == BuilderTool.RoomBox
                    ? "Drag over the floor from one corner cell to the other. The new room gets walls on every side that has none; give it a door in DOORS (3)."
                    : "Press on a grid corner and drag. The wall follows the pointer along the grid; its length shows as you go. Let go to build it. A click on one edge does just that edge.");
            }
            public override string Hint() =>
                app._tool == BuilderTool.Pillar ? "Click a grid corner to put a pillar there, or take it away."
                : app._tool == BuilderTool.RoomBox ? "Drag a box over the floor to make a room with walls round it."
                : app._tool == BuilderTool.Erase ? "Drag along walls to take them away."
                : "Press on a corner and drag to draw. Hold Ctrl to erase. Doors and windows: DOORS (3).";
        }

        // ---------------- Drawn lines and boxes ----------------

        bool _lining;                    // a wall line or room box being drawn
        Vector2Int _lineFrom, _lineTo;   // grid corners (line tools) or cells (room box)
        int _lineCells = -1;

        bool LineTool => _tool == BuilderTool.Wall || _tool == BuilderTool.Half || _tool == BuilderTool.Erase;

        Vector2Int NearestCorner(Vector2 m)
        {
            var v = V;
            return new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(m.x / C), 0, v.width), Mathf.Clamp(Mathf.RoundToInt(m.y / C), 0, v.depth));
        }

        Vector2Int CellAt(Vector2 m) => new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(m.x / C), 0, V.width - 1), Mathf.Clamp(Mathf.FloorToInt(m.y / C), 0, V.depth - 1));

        void BeginLine(Vector2 screen)
        {
            if (!GroundAt(screen, out var lm)) { _stroking = false; return; }
            _lining = true; _lineCells = -1;
            _lineFrom = _lineTo = _tool == BuilderTool.RoomBox ? CellAt(lm) : NearestCorner(lm);
            _lineStartMetres = lm;
        }

        Vector2 _lineStartMetres;

        void ContinueLine(Vector2 screen)
        {
            if (!_lining || !GroundAt(screen, out var lm)) return;
            if (_tool == BuilderTool.RoomBox)
            {
                var c = CellAt(lm);
                if (c != _lineTo) { _lineTo = c; BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.8f + 0.03f * (Mathf.Abs(c.x - _lineFrom.x) + Mathf.Abs(c.y - _lineFrom.y))); }
                return;
            }
            var end = NearestCorner(lm);
            // Straight: along whichever axis the pointer has gone further.
            if (Mathf.Abs(end.x - _lineFrom.x) >= Mathf.Abs(end.y - _lineFrom.y)) end.y = _lineFrom.y; else end.x = _lineFrom.x;
            if (end != _lineTo)
            {
                _lineTo = end;
                int cells = Mathf.Abs(end.x - _lineFrom.x) + Mathf.Abs(end.y - _lineFrom.y);
                if (cells != _lineCells) { BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.75f + 0.04f * cells, 0.7f); _lineCells = cells; }
            }
        }

        void EndLine()
        {
            if (!_lining) return;
            _lining = false;
            var v = V;
            if (_tool == BuilderTool.RoomBox)
            {
                var r = new RectInt(Mathf.Min(_lineFrom.x, _lineTo.x), Mathf.Min(_lineFrom.y, _lineTo.y), Mathf.Abs(_lineTo.x - _lineFrom.x) + 1, Mathf.Abs(_lineTo.y - _lineFrom.y) + 1);
                if (r.width >= v.width && r.height >= v.depth) { SetStatus("That is the whole room. Draw a smaller box inside it."); return; }
                PushUndo();
                int area = v.DrawRoom(r, "Room " + (v.rooms.Count + 1));
                if (area < 0) { _undo.Pop(); return; }
                SelectRoom(area);
                BuilderSounds.Play(BuilderSounds.Sfx.Grow);
                SetStatus($"A new room, {r.width} × {r.height} cells. Give it a way in: DOORS (3).");
                Changed();
                return;
            }
            int code = CodeForTool(false);
            if (code < 0) return;
            if (_lineFrom == _lineTo)
            {
                // A click: the one edge under the pointer, as before.
                float gx = _lineStartMetres.x / C, gz = _lineStartMetres.y / C;
                PickEdge(gx, gz, out bool ok, out bool vertical, out int x, out int z);
                if (!ok) return;
                PushUndo();
                ApplyEdge(vertical, x, z);
                if (_strokeChanged) Changed(); else _undo.Pop();
                _strokeChanged = false;
                return;
            }
            bool vert = _lineFrom.x == _lineTo.x;
            int line = vert ? _lineFrom.x : _lineFrom.y;
            int a0 = vert ? Mathf.Min(_lineFrom.y, _lineTo.y) : Mathf.Min(_lineFrom.x, _lineTo.x);
            int a1 = vert ? Mathf.Max(_lineFrom.y, _lineTo.y) : Mathf.Max(_lineFrom.x, _lineTo.x);
            if (EdgeCode.Kind(code) == EdgeCode.Half && v.PerimeterLine(vert, line)) { SetStatus("Half walls go inside the room; the outer edge is a wall or an exit."); return; }
            PushUndo();
            int n = v.DrawLine(vert, line, a0, a1, code);
            if (n == 0) { _undo.Pop(); return; }
            if (_selected.kind == SelKind.Opening) _selected = Selection.None;
            BuilderSounds.Play(EdgeCode.Kind(code) == EdgeCode.Open || _tool == BuilderTool.Erase ? BuilderSounds.Sfx.Shrink : BuilderSounds.Sfx.Wall);
            Changed();
        }

        /// <summary>The line or box being drawn, and its length (GL; labels in DrawLineLabel).</summary>
        void DrawLineGizmo()
        {
            if (!_lining) return;
            if (_tool == BuilderTool.RoomBox)
            {
                float x0 = Mathf.Min(_lineFrom.x, _lineTo.x) * C, z0 = Mathf.Min(_lineFrom.y, _lineTo.y) * C;
                float x1 = (Mathf.Max(_lineFrom.x, _lineTo.x) + 1) * C, z1 = (Mathf.Max(_lineFrom.y, _lineTo.y) + 1) * C;
                var c = ImUi.Accent;
                Thick(W3(x0, z0, 0.06f), W3(x1, z0, 0.06f), c); Thick(W3(x1, z0, 0.06f), W3(x1, z1, 0.06f), c);
                Thick(W3(x1, z1, 0.06f), W3(x0, z1, 0.06f), c); Thick(W3(x0, z1, 0.06f), W3(x0, z0, 0.06f), c);
                return;
            }
            bool erase = CodeForTool(false) == EdgeCode.Open;
            var col = erase ? ImUi.Bad : _tool == BuilderTool.Half ? new Color(0.5f, 0.9f, 1f) : ImUi.Accent;
            var a = W3(_lineFrom.x * C, _lineFrom.y * C, 0.06f); var b = W3(_lineTo.x * C, _lineTo.y * C, 0.06f);
            Thick(a, b, col);
            float h = _tool == BuilderTool.Half ? 1.15f : 2.7f;
            if (!erase) { Line(a, a + Vector3.up * h, col); Line(b, b + Vector3.up * h, col); Line(a + Vector3.up * h, b + Vector3.up * h, new Color(col.r, col.g, col.b, 0.5f)); }
        }

        void DrawLineLabel()
        {
            if (!_lining) return;
            if (_tool == BuilderTool.RoomBox)
            {
                int w = Mathf.Abs(_lineTo.x - _lineFrom.x) + 1, d = Mathf.Abs(_lineTo.y - _lineFrom.y) + 1;
                var mid = W3((Mathf.Min(_lineFrom.x, _lineTo.x) + w * 0.5f) * C, (Mathf.Min(_lineFrom.y, _lineTo.y) + d * 0.5f) * C, 0.1f);
                Label(mid, $"{w} × {d} cells   {w * C:0.0} × {d * C:0.0} m");
                return;
            }
            int cells = Mathf.Abs(_lineTo.x - _lineFrom.x) + Mathf.Abs(_lineTo.y - _lineFrom.y);
            if (cells == 0) return;
            var m = W3((_lineFrom.x + _lineTo.x) * 0.5f * C, (_lineFrom.y + _lineTo.y) * 0.5f * C, _tool == BuilderTool.Half ? 1.4f : 2.9f);
            Label(m, $"{cells * C:0.0} m   ({cells} cell{(cells == 1 ? "" : "s")})");
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
            float off = _strokeVertical ? Mathf.Abs(gx - _strokeLine) : Mathf.Abs(gz - _strokeLine);
            if (off > 0.9f) return;
            _lastAlong = along;
            if (_strokeVertical) ApplyEdge(true, _strokeLine, along); else ApplyEdge(false, along, _strokeLine);
        }

        // ---------------- Edge tools ----------------

        int CodeForTool(bool perimeter)
        {
            // (Ctrl while drawing walls erases.)
            var tool = _tool;
            var kb = Keyboard.current;
            if ((tool == BuilderTool.Wall || tool == BuilderTool.Half) && kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)) tool = BuilderTool.Erase;
            switch (tool)
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
            bool perimeter = vertical ? v.Boundary(true, x, z) : v.Boundary(false, z, x);
            int code = CodeForTool(perimeter);
            if (code < 0) { SetStatus("Half walls go inside the room; the outer edge is a wall or an exit."); return; }
            int old = vertical ? v.V(x, z) : v.H(x, z);
            if (old == code) return;
            if (vertical) v.SetV(x, z, code); else v.SetH(x, z, code);
            // A door or window on a wall that is no longer there goes with it.
            if (EdgeCode.Kind(code) != EdgeCode.Wall)
            {
                float a0 = (vertical ? z : x) * C, a1 = a0 + C; int line = vertical ? x : z;
                v.openings.RemoveAll(o => o.vertical == vertical && o.line == line && o.from < a1 && o.to > a0);
                if (_selected.kind == SelKind.Opening) _selected = Selection.None;
            }
            BuilderSounds.Play(_tool == BuilderTool.Erase ? BuilderSounds.Sfx.Shrink : BuilderSounds.Sfx.Wall, 0.9f + 0.2f * Random.value);
            _strokeChanged = true;
            _needRebuild = true;
        }

        void TogglePillar()
        {
            if (!_hoverVertexValid) return;
            PushUndo();
            var v = V;
            int i = v.pillars.FindIndex(p => p.x == _hoverVertex.x && p.z == _hoverVertex.y);
            if (i >= 0) { v.pillars.RemoveAt(i); BuilderSounds.Play(BuilderSounds.Sfx.Delete); }
            else { v.pillars.Add(new PillarDef { x = _hoverVertex.x, z = _hoverVertex.y, size = _pillarSize }); BuilderSounds.Play(BuilderSounds.Sfx.Place); }
            Changed();
        }
    }
}
