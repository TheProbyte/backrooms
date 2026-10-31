using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    // Build mode, SELECT: click anything to pick it (a prop, loot, a door or window, a room's floor), drag to move it; the
    // inspector on the right edits it, its handles size it (BuilderApp.Handles).
    public partial class BuilderApp
    {
        sealed class SelectMode : Mode
        {
            public SelectMode(BuilderApp app) : base(app) { }
            public override string Name => "SELECT";
            public override int Icon => 0;
            public override string Tip => "Click anything to select it and edit it on the right. Drag to move it.";
            public override BuilderTool[] Tools => new[] { BuilderTool.Select };
            public override void Options(ref ImUi.Column col)
            {
                ImUi.Dimmed(col.Row(132), "Click a prop, a door or window, or a room's floor: it shows on the right. Drag to move it, drag its handles to size it. The arrows outside the walls grow or shrink the room.\n\nAlt: no snapping.   Delete: remove.   R: turn.");
            }
            public override string Hint()
            {
                switch (app._selected.kind)
                {
                    case SelKind.Prop: return "Drag to move it. Green handle: lift.  Ring: turn.  Orange: size.  R turns 45°, Delete removes, Alt: no snapping.";
                    case SelKind.Opening: return "Drag its sides, top or bottom to size it; its middle slides it along the wall. Delete removes it.";
                    case SelKind.Loot: return "Drag to move it. Delete removes it.";
                    case SelKind.Room: return "This room's ceiling, walls, floor and lights are on the right. PAINT adds cells to it.";
                    case SelKind.Wall: return "Make it a wall, half wall or railing on the right, put a door or window in it, or remove it.";
                    case SelKind.Light: return "Working, flickering or dead (right). Delete removes it.";
                    case SelKind.Pillar: return "Drag it to another grid corner. Its size is on the right. Delete removes it.";
                }
                return "Click anything to select it; drag on the floor for a box round props (Shift-click adds). Arrows outside the walls grow the room.";
            }
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
            for (int i = 0; i < v.sounds.Count; i++)
            {
                float d = Vector2.Distance(new Vector2(v.sounds[i].x, v.sounds[i].z), _hoverMetres);
                if (d < Mathf.Min(best, 0.35f)) { best = d; _selected = new Selection { kind = SelKind.Sound, index = i }; }
            }
            if (_selected.kind != SelKind.None) { PushUndo(); _dragging = true; _grabDelta = SelectedPos() - _hoverMetres; BuilderSounds.Play(BuilderSounds.Sfx.Pick); }
        }

        Vector2 _grabDelta;
        Vector2 SelectedPos()
        {
            var v = V;
            if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count) return new Vector2(v.props[_selected.index].x, v.props[_selected.index].z);
            if (_selected.kind == SelKind.Loot && _selected.index < v.loot.Count) return new Vector2(v.loot[_selected.index].x, v.loot[_selected.index].z);
            if (_selected.kind == SelKind.Sound && _selected.index < v.sounds.Count) return new Vector2(v.sounds[_selected.index].x, v.sounds[_selected.index].z);
            return _hoverMetres;
        }

        void DragSelected()
        {
            if (!_dragging || !_hoverGround) return;
            var v = V;
            // A pillar: to the grid corner nearest the pointer.
            if (_selected.kind == SelKind.Pillar && _selected.index < v.pillars.Count)
            {
                var pl = v.pillars[_selected.index];
                int px = Mathf.Clamp(Mathf.RoundToInt(_hoverMetres.x / C), 0, v.width), pz = Mathf.Clamp(Mathf.RoundToInt(_hoverMetres.y / C), 0, v.depth);
                if ((px != pl.x || pz != pl.z) && !v.pillars.Exists(q => q != pl && q.x == px && q.z == pz))
                { pl.x = px; pl.z = pz; _strokeChanged = true; BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.9f); RequestRebuild(); }
                return;
            }
            // Smooth: where it was taken stays under the pointer; 5 cm steps (Alt: none). A prop with physics off may go
            // anywhere, into a wall or out of the room.
            var raw = _hoverMetres + _grabDelta;
            var m = Alt ? raw : new Vector2(Mathf.Round(raw.x / 0.05f) * 0.05f, Mathf.Round(raw.y / 0.05f) * 0.05f);
            bool free = _selected.kind == SelKind.Prop && _selected.index < v.props.Count && v.props[_selected.index].pinned;
            if (!free && !InsideRoom(m)) return;
            if (_selected.kind == SelKind.Sound && _selected.index < v.sounds.Count)
            {
                var s = v.sounds[_selected.index];
                if (s.x != m.x || s.z != m.y) { s.x = m.x; s.z = m.y; _strokeChanged = true; BuilderSounds.Play(BuilderSounds.Sfx.Tick, 1f, 0.5f); }
                return;
            }
            if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count && !WallProp(v.props[_selected.index].kind))
            {
                var pr = v.props[_selected.index];
                // Smart placing: flat against a wall, lined up with a neighbour (Alt: off; a group moves as it is).
                if (!Alt && !Grouped && !pr.pinned) { float yaw = pr.yaw; m = SmartPlace(m, pr.size, pr.scale > 0.01f ? pr.scale : 1f, ref yaw, _selected.index); if (!Mathf.Approximately(yaw, pr.yaw)) { pr.yaw = yaw; _strokeChanged = true; } }
                if (pr.x != m.x || pr.z != m.y)
                {
                    // The rest of a group goes the same way.
                    float gdx = m.x - pr.x, gdz = m.y - pr.z;
                    foreach (int gi in _group)
                        if (gi < v.props.Count && gi != _selected.index && !WallProp(v.props[gi].kind)) { v.props[gi].x += gdx; v.props[gi].z += gdz; if (!HasView(gi)) RequestRebuild(); }
                    pr.x = m.x; pr.z = m.y; _strokeChanged = true;
                    BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.9f + 0.15f * Random.value, 0.6f);
                    if (!HasView(_selected.index)) RequestRebuild();         // (a movable glides there: UpdatePropViews)
                }
                return;
            }
            if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count && WallProp(v.props[_selected.index].kind))
            {
                var p = v.props[_selected.index];
                if (!v.TryWallPlacement(_hoverMetres, p.size, out m, out float yaw)) return;
                if (p.x != m.x || p.z != m.y || p.yaw != yaw)
                { p.x = m.x; p.z = m.y; p.yaw = yaw; _strokeChanged = true; _needRebuild = true; }
                return;
            }
            if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count) { if (v.props[_selected.index].x != m.x || v.props[_selected.index].z != m.y) { v.props[_selected.index].x = m.x; v.props[_selected.index].z = m.y; _strokeChanged = true; _needRebuild = true; } }
            if (_selected.kind == SelKind.Loot && _selected.index < v.loot.Count) { if (v.loot[_selected.index].x != m.x || v.loot[_selected.index].z != m.y) { v.loot[_selected.index].x = m.x; v.loot[_selected.index].z = m.y; _strokeChanged = true; _needRebuild = true; } }
        }

        /// <summary>Ctrl+D: a copy of the selected prop, loot spot, door or window beside it, selected.</summary>
        void DuplicateSelected()
        {
            var v = V;
            if (Grouped)
            {
                PushUndo();
                var copies = new List<int>();
                foreach (int i in GroupAll())
                {
                    var q = JsonUtility.FromJson<PropDef>(JsonUtility.ToJson(v.props[i]));
                    q.x += 0.6f; q.z += 0.3f; q.variant = Random.Range(0, 1 << 16);
                    v.props.Add(q); copies.Add(v.props.Count - 1);
                }
                _selected = new Selection { kind = SelKind.Prop, index = copies[0] };
                _group.Clear(); for (int k = 1; k < copies.Count; k++) _group.Add(copies[k]);
            }
            else if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count)
            {
                PushUndo();
                var q = JsonUtility.FromJson<PropDef>(JsonUtility.ToJson(v.props[_selected.index]));
                q.x += 0.6f; q.z += 0.3f; q.variant = Random.Range(0, 1 << 16);
                v.props.Add(q); _selected = new Selection { kind = SelKind.Prop, index = v.props.Count - 1 };
            }
            else if (_selected.kind == SelKind.Loot && _selected.index < v.loot.Count)
            {
                PushUndo();
                var q = JsonUtility.FromJson<LootDef>(JsonUtility.ToJson(v.loot[_selected.index]));
                q.x += 0.5f; v.loot.Add(q); _selected = new Selection { kind = SelKind.Loot, index = v.loot.Count - 1 };
            }
            else if (_selected.kind == SelKind.Opening && _selected.index < v.openings.Count)
            {
                var o = v.openings[_selected.index];
                // Along the same wall, the first place it fits to either side.
                for (int k = 1; k <= 20; k++)
                    foreach (int sign in new[] { 1, -1 })
                    {
                        var q = o.Clone(); float shift = sign * (o.Width + 0.1f * k);
                        q.from += shift; q.to += shift;
                        if (!v.OpeningFits(q)) continue;
                        PushUndo(); v.openings.Add(q); _selected = new Selection { kind = SelKind.Opening, index = v.openings.Count - 1 };
                        BuilderSounds.Play(BuilderSounds.Sfx.Place, 1.1f); Changed(); return;
                    }
                SetStatus("No room on this wall for another one."); BuilderSounds.Play(BuilderSounds.Sfx.Error); return;
            }
            else return;
            BuilderSounds.Play(BuilderSounds.Sfx.Place, 1.1f);
            Changed();
        }

        void DeleteSelected()
        {
            var v = V;
            if (_selected.kind == SelKind.Wall) { SetWallRun(EdgeCode.Open); return; }
            if (Grouped)
            {
                PushUndo();
                var all = GroupAll(); all.Sort(); all.Reverse();
                foreach (int i in all) v.props.RemoveAt(i);
                _group.Clear();
            }
            else if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count) { PushUndo(); v.props.RemoveAt(_selected.index); }
            else if (_selected.kind == SelKind.Light && _selected.index < v.fixtures.Count) { PushUndo(); v.fixtures.RemoveAt(_selected.index); }
            else if (_selected.kind == SelKind.Pillar && _selected.index < v.pillars.Count) { PushUndo(); v.pillars.RemoveAt(_selected.index); }
            else if (_selected.kind == SelKind.Loot && _selected.index < v.loot.Count) { PushUndo(); v.loot.RemoveAt(_selected.index); }
            else if (_selected.kind == SelKind.Opening && _selected.index < v.openings.Count) { PushUndo(); v.openings.RemoveAt(_selected.index); v.Validate(); }
            else if (_selected.kind == SelKind.Decal && _selected.index < v.decals.Count) { PushUndo(); v.decals.RemoveAt(_selected.index); }
            else if (_selected.kind == SelKind.Sound && _selected.index < v.sounds.Count) { PushUndo(); v.sounds.RemoveAt(_selected.index); }
            else if (_selected.kind == SelKind.Trigger && _selected.index < v.triggers.Count) { PushUndo(); v.triggers.RemoveAt(_selected.index); }
            else return;
            BuilderSounds.Play(BuilderSounds.Sfx.Delete);
            _selected = Selection.None;
            Changed();
        }

        void RotateSelected()
        {
            var v = V;
            if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count && WallProp(v.props[_selected.index].kind))
            { SetStatus("Drag this prop to another wall to change its facing."); return; }
            if (_selected.kind == SelKind.Prop && _selected.index < v.props.Count) { PushUndo(); v.props[_selected.index].yaw = (v.props[_selected.index].yaw + 45f) % 360f; Changed(); }
        }
    }
}
