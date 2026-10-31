using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    // Build mode, PAINT: which room each cell belongs to (each room its own ceiling, walls, floor and lights, set in the
    // inspector), and the ceiling lights; copy and paste of a room.
    public partial class BuilderApp
    {
        sealed class PaintMode : Mode
        {
            public PaintMode(BuilderApp app) : base(app) { }
            public override string Name => "PAINT";
            public override int Icon => 5;
            public override string Tip => "Rooms (ceiling, walls, floor, lights) and ceiling lights.";
            public override BuilderTool[] Tools => new[] { BuilderTool.Floor, BuilderTool.Light };
            public override string ToolName(BuilderTool t) => t == BuilderTool.Floor ? "ROOMS" : "LIGHTS";
            public override string ToolTip(BuilderTool t) => t == BuilderTool.Floor ? "Paint cells into a room." : "Click a ceiling cell: light on, dead, none.";
            public override void Options(ref ImUi.Column col)
            {
                if (app._tool == BuilderTool.Light)
                {
                    ImUi.Dimmed(col.Row(60), "Click a cell to cycle its ceiling light: working, dead, none. A room's light mode is on the right.");
                    return;
                }
                var v = app.V;
                ImUi.Header(col.Row(24), "Paint into");
                for (int i = 0; i < v.rooms.Count; i++)
                {
                    var b = col.Row(30);
                    ImUi.Fill(new Rect(b.x, b.y + 4, 10, b.height - 8), RoomColor(i));
                    if (ImUi.Button(new Rect(b.x + 14, b.y, b.width - 14, b.height), v.rooms[i].name, app._activeRoom == i)) app.SelectRoom(i);
                }
                if (ImUi.Button(col.Row(30), "+ NEW ROOM", false, "A new room to paint cells into, with its own ceiling, walls, floor and lights."))
                {
                    app.PushUndo(); v.rooms.Add(new RoomSettings { name = "Room " + (v.rooms.Count + 1) }); app.SelectRoom(v.rooms.Count - 1); app.Changed();
                    BuilderSounds.Play(BuilderSounds.Sfx.Place);
                }
                ImUi.Dimmed(col.Row(54), "Drag over the floor to paint. The room's ceiling, walls, floor and lights are on the right.");
            }
            public override string Hint() =>
                app._tool == BuilderTool.Light ? "Click a cell: light on, dead, none."
                : $"Drag over the floor to paint cells into '{app.V.rooms[Mathf.Clamp(app._activeRoom, 0, app.V.rooms.Count - 1)].name}'. Its settings are on the right.";
        }

        void PaintCell()
        {
            if (!_hoverCellValid) return;
            var v = V;
            int idx = _hoverCell.y * v.width + _hoverCell.x;
            _activeRoom = Mathf.Clamp(_activeRoom, 0, v.rooms.Count - 1);
            if (v.cellRoom[idx] == _activeRoom) return;
            v.cellRoom[idx] = _activeRoom;
            BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.9f + 0.2f * Random.value);
            _strokeChanged = true; _needRebuild = true;
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
            BuilderSounds.Play(BuilderSounds.Sfx.Tick, 1.2f);
            Changed();
        }

        void CopyArea()
        {
            _areaClipboard = AreaClipboard.Capture(V, _activeRoom);
            SetStatus(_areaClipboard == null ? "This area has no painted cells to copy." : $"Copied '{_areaClipboard.Name}'. Ctrl+V, then click its new lower-left cell. Paste replaces that footprint; Ctrl+Z undoes it.", 8f);
        }

        void BeginPasteArea()
        {
            if (_areaClipboard == null) { SetStatus("Select an area and copy it first. [Ctrl+C]"); return; }
            _pastingArea = true; _selected = Selection.None; _stroking = false;
            SetStatus("Click the copied area's lower-left destination cell. Red means it will not fit. Escape cancels. Existing contents in the footprint are replaced.", 12f);
        }

        void PasteArea()
        {
            if (!_hoverCellValid || !_areaClipboard.Fits(V, _hoverCell)) { SetStatus("The complete area must fit inside this room. Resize the room or choose another cell."); return; }
            PushUndo();
            if (_areaClipboard.Paste(V, _hoverCell, out int area))
            { _activeRoom = area; _pastingArea = false; Changed(); SetStatus("Area pasted. Ctrl+Z undoes the whole paste."); }
        }
    }
}
