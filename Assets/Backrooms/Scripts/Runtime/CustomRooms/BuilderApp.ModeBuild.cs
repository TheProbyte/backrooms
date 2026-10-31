using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    // Build mode, BUILD: walls, half walls and pillars on the 1.2 m grid (drag along grid lines; Ctrl erases).
    public partial class BuilderApp
    {
        sealed class BuildMode : Mode
        {
            public BuildMode(BuilderApp app) : base(app) { }
            public override string Name => "BUILD";
            public override int Icon => 1;
            public override string Tip => "Walls, half walls and pillars on the grid.";
            public override BuilderTool[] Tools => new[] { BuilderTool.Wall, BuilderTool.Half, BuilderTool.Pillar, BuilderTool.Erase };
            public override string ToolName(BuilderTool t) => t == BuilderTool.Wall ? "WALL" : t == BuilderTool.Half ? "HALF WALL" : t == BuilderTool.Pillar ? "PILLAR" : "ERASE";
            public override string ToolTip(BuilderTool t) =>
                t == BuilderTool.Wall ? "Drag along the grid lines to draw walls, floor to ceiling. Hold Ctrl to erase instead."
                : t == BuilderTool.Half ? "A half-height partition (1.15 m) you can see and reach over. Inside the room only. Ctrl erases."
                : t == BuilderTool.Pillar ? "Click a grid corner to put a pillar there, or take it away."
                : "Drag along walls to take them away. On the outer wall it closes a gap again.";
            public override void Options(ref ImUi.Column col)
            {
                if (app._tool != BuilderTool.Pillar) return;
                ImUi.Header(col.Row(24), "Pillar size");
                if (ImUi.Button(col.Row(30), "SLIM  0.45 m", app._pillarSize < 0.5f)) app._pillarSize = 0.45f;
                if (ImUi.Button(col.Row(30), "NORMAL  0.6 m", app._pillarSize >= 0.5f)) app._pillarSize = 0.6f;
            }
            public override string Hint() =>
                app._tool == BuilderTool.Pillar ? "Click a grid corner to put a pillar there, or take it away."
                : app._tool == BuilderTool.Erase ? "Drag along walls to take them away."
                : "Drag along the grid lines to draw. Hold Ctrl to erase. Doors and windows: DOORS (3).";
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
            bool perimeter = vertical ? (x == 0 || x == v.width) : (z == 0 || z == v.depth);
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
