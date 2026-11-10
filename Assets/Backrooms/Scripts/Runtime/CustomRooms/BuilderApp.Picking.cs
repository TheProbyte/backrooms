using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Build mode, step B (round 17): everything can be clicked. In SELECT a click takes, nearest first: a prop or loot
    /// spot, a pillar, a ceiling light, a door or window, a run of wall, else the room whose floor it is (or, dragged on the
    /// floor, a box round props). Shift-click adds props to a group; a group moves, turns physics off, duplicates and
    /// deletes together. Walls, lights and pillars have their own inspector.
    /// </summary>
    public partial class BuilderApp
    {
        /// <summary>The selected run of wall: its line, its cells [a0, a1), and where along it was clicked (m).</summary>
        (bool vertical, int line, int a0, int a1, float at) _selWall;
        /// <summary>Props selected with the main one (indices into V.props).</summary>
        readonly List<int> _group = new List<int>();
        bool _boxing, _boxActive;
        Vector2 _boxStart, _boxNow;

        bool Grouped => _selected.kind == SelKind.Prop && _group.Count > 0;

        /// <summary>The main prop and the group, without repeats.</summary>
        List<int> GroupAll()
        {
            var all = new List<int>();
            if (_selected.kind == SelKind.Prop) all.Add(_selected.index);
            foreach (int i in _group) if (!all.Contains(i) && i < V.props.Count) all.Add(i);
            return all;
        }

        /// <summary>A SELECT click: picks what is under the pointer (see the class notes). Returns true if it started a drag.</summary>
        void SelectClick(Vector2 screen)
        {
            int before = _selected.kind == SelKind.Prop ? _selected.index : -1;
            var keep = new List<int>(_group);
            SelectAt();                                            // a prop or loot spot (starts its drag)
            if (_selected.kind == SelKind.Prop)
            {
                if (Shift && before >= 0 && before != _selected.index)
                {
                    // Shift: into the group (or out of it again).
                    if (keep.Contains(_selected.index) || before == _selected.index) keep.Remove(_selected.index);
                    if (!keep.Contains(before)) keep.Add(before);
                    _group.Clear(); _group.AddRange(keep);
                }
                else if (!keep.Contains(_selected.index) && before != _selected.index) _group.Clear();
                else { _group.Clear(); _group.AddRange(keep); if (before >= 0 && before != _selected.index && !_group.Contains(before)) _group.Add(before); _group.Remove(_selected.index); }
                return;
            }
            _group.Clear();
            if (_selected.kind != SelKind.None) return;
            if (PickPillar() || PickLight(screen)) { BuilderSounds.Play(BuilderSounds.Sfx.Pick); return; }
            int decal = DecalUnder(screen);
            if (decal >= 0) { _selected = new Selection { kind = SelKind.Decal, index = decal }; PushUndo(); _draggingDecal = true; BuilderSounds.Play(BuilderSounds.Sfx.Pick); return; }
            // A door or window on a wall nearer than the floor there: taken by its middle to slide it.
            if (SelectOpeningAt(screen)) { _hoverHandle = Handle.Move; BeginHandleDrag(screen); return; }
            if (PickWall(screen)) { BuilderSounds.Play(BuilderSounds.Sfx.Pick, 0.8f); return; }
            // The floor: a box round props if dragged, else (on letting go) the room it belongs to.
            _boxing = true; _boxActive = false; _boxStart = _boxNow = screen;
        }

        bool PickPillar()
        {
            if (!_hoverGround) return false;
            var v = V;
            for (int i = 0; i < v.pillars.Count; i++)
            {
                var p = v.pillars[i];
                if (Mathf.Abs(_hoverMetres.x - p.x * C) < p.size * 0.5f + 0.12f && Mathf.Abs(_hoverMetres.y - p.z * C) < p.size * 0.5f + 0.12f)
                {
                    PushUndo(); _dragging = true;
                    _selected = new Selection { kind = SelKind.Pillar, index = i };
                    return true;
                }
            }
            return false;
        }

        /// <summary>A light: on the ceiling where it is drawn (SOLID view), else its marker on the floor.</summary>
        bool PickLight(Vector2 screen)
        {
            var v = V;
            var ray = PointerRay(screen);
            float best = float.MaxValue; int pick = -1;
            for (int i = 0; i < v.fixtures.Count; i++)
            {
                var f = v.fixtures[i];
                Vector2 c = LightMarker(f);
                if (_view == BuilderView.Solid)
                {
                    float y = v.RoomAt(f.x, f.z).ceiling;
                    if (Mathf.Abs(ray.direction.y) < 1e-4f) continue;
                    float t = (y - ray.origin.y) / ray.direction.y;
                    if (t <= 0f || t >= best) continue;
                    var p = ray.origin + ray.direction * t;
                    if (Mathf.Abs(p.x - _origin.x - (f.x + 0.5f) * C) < 0.35f && Mathf.Abs(p.z - _origin.y - (f.z + 0.5f) * C) < 0.35f) { best = t; pick = i; }
                }
                else if (_hoverGround && Vector2.Distance(_hoverMetres, c) < 0.3f) { pick = i; break; }
            }
            if (pick < 0) return false;
            _selected = new Selection { kind = SelKind.Light, index = pick };
            return true;
        }

        static Vector2 LightMarker(FixtureDef f) => new Vector2((f.x + 0.5f) * C - 0.3f, (f.z + 0.5f) * C);

        /// <summary>A run of wall (solid, half or railing) the pointer is on, if it is nearer than the floor under it.</summary>
        bool PickWall(Vector2 screen)
        {
            var v = V;
            var ray = PointerRay(screen);
            bool hit = RayWall(ray, true, out bool vert, out int line, out float along, out float height);
            if (!hit) hit = RayHalf(ray, out vert, out line, out along);
            if (!hit) return false;
            var wp = vert ? W3(line * C, along, height) : W3(along, line * C, height);
            if (_hoverGround && Vector3.Distance(ray.origin, W3(_hoverMetres.x, _hoverMetres.y, 0f)) < Vector3.Distance(ray.origin, wp) - 0.05f) return false;
            int alongN = vert ? v.depth : v.width;
            int cell = Mathf.Clamp(Mathf.FloorToInt(along / C), 0, alongN - 1);
            int code = vert ? v.V(line, cell) : v.H(cell, line);
            int kind = EdgeCode.Kind(code);
            int a0 = cell, a1 = cell + 1;
            while (a0 > 0 && EdgeCode.Kind(vert ? v.V(line, a0 - 1) : v.H(a0 - 1, line)) == kind) a0--;
            while (a1 < alongN && EdgeCode.Kind(vert ? v.V(line, a1) : v.H(a1, line)) == kind) a1++;
            _selWall = (vert, line, a0, a1, along);
            _selected = new Selection { kind = SelKind.Wall, index = 0 };
            return true;
        }

        /// <summary>A half wall or railing (1.15 m) the ray meets.</summary>
        bool RayHalf(Ray ray, out bool vertical, out int line, out float along)
        {
            vertical = false; line = 0; along = 0f;
            var v = V; float best = float.MaxValue;
            for (int pass = 0; pass < 2; pass++)
            {
                bool vert = pass == 0;
                for (int ln = 1; ln < (vert ? v.width : v.depth); ln++)
                {
                    float plane = (vert ? _origin.x : _origin.y) + ln * C;
                    float dir = vert ? ray.direction.x : ray.direction.z, org = vert ? ray.origin.x : ray.origin.z;
                    if (Mathf.Abs(dir) < 1e-5f) continue;
                    float t = (plane - org) / dir;
                    if (t <= 0f || t >= best) continue;
                    var p = ray.origin + ray.direction * t;
                    float a = vert ? p.z - _origin.y : p.x - _origin.x;
                    if (a < 0f || a > v.LineLength(vert) || p.y < 0f || p.y > 1.2f) continue;
                    int cell = Mathf.Clamp(Mathf.FloorToInt(a / C), 0, (vert ? v.depth : v.width) - 1);
                    int k = EdgeCode.Kind(vert ? v.V(ln, cell) : v.H(cell, ln));
                    if (k != EdgeCode.Half && k != EdgeCode.Railing) continue;
                    best = t; vertical = vert; line = ln; along = a;
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>Every edge of the selected wall run set to a code (an outer wall stays a wall).</summary>
        void SetWallRun(int code)
        {
            var v = V; var w = _selWall;
            bool perimeter = v.PerimeterLine(w.vertical, w.line);
            if (perimeter && code != EdgeCode.Wall) { SetStatus("The outer wall stays a wall: put a door or window in it instead."); BuilderSounds.Play(BuilderSounds.Sfx.Error); return; }
            PushUndo();
            for (int a = w.a0; a < w.a1; a++) { if (w.vertical) v.SetV(w.line, a, code); else v.SetH(a, w.line, code); }
            if (code != EdgeCode.Wall) v.openings.RemoveAll(o => o.vertical == w.vertical && o.line == w.line && o.from < w.a1 * C && o.to > w.a0 * C);
            if (code == EdgeCode.Open) _selected = Selection.None;
            BuilderSounds.Play(code == EdgeCode.Open ? BuilderSounds.Sfx.Delete : BuilderSounds.Sfx.Place);
            Changed();
        }

        /// <summary>A door or window put on the selected wall where it was clicked.</summary>
        void AddOpeningOnWall(OpeningKind kind)
        {
            var v = V; var w = _selWall;
            var o = Preset(kind, v.CeilingAlong(w.vertical, w.line, w.at - 0.5f, w.at + 0.5f));
            float width = o.to - o.from;
            o.vertical = w.vertical; o.line = w.line;
            o.from = SnapLen(w.at - width * 0.5f); o.to = o.from + width;
            v.ClampOpening(o);
            if (!v.OpeningFits(o)) { SetStatus("It does not fit there: it needs solid wall, clear of other openings."); BuilderSounds.Play(BuilderSounds.Sfx.Error); return; }
            PushUndo();
            v.openings.Add(o);
            _selected = new Selection { kind = SelKind.Opening, index = v.openings.Count - 1 };
            _dispFor = null;
            BuilderSounds.Play(BuilderSounds.Sfx.Place);
            Changed();
        }

        /// <summary>The box drag on the floor: the props inside it on the screen become the selection.</summary>
        void EndBox()
        {
            _boxing = false;
            if (!_boxActive)
            {
                if (_hoverCellValid) { SelectRoom(V.RoomOf(_hoverCell.x, _hoverCell.y)); BuilderSounds.Play(BuilderSounds.Sfx.Pick, 0.9f); }
                return;
            }
            _boxActive = false;
            var v = V;
            var r = Rect.MinMaxRect(Mathf.Min(_boxStart.x, _boxNow.x), Mathf.Min(_boxStart.y, _boxNow.y), Mathf.Max(_boxStart.x, _boxNow.x), Mathf.Max(_boxStart.y, _boxNow.y));
            _group.Clear(); _selected = Selection.None;
            for (int i = 0; i < v.props.Count; i++)
            {
                var sp = _cam.WorldToScreenPoint(W3(v.props[i].x, v.props[i].z, v.props[i].elevation + 0.3f));
                if (sp.z <= 0f || !r.Contains(new Vector2(sp.x, sp.y))) continue;
                if (_selected.kind == SelKind.None) _selected = new Selection { kind = SelKind.Prop, index = i };
                else _group.Add(i);
            }
            if (_selected.kind != SelKind.None) BuilderSounds.Play(BuilderSounds.Sfx.Pick, 1.15f);
        }

        /// <summary>The box being dragged (OnGUI, virtual pixels).</summary>
        void DrawBox()
        {
            if (!_boxActive) return;
            float s = ImUi.Scale;
            var a = new Vector2(_boxStart.x / s, (Screen.height - _boxStart.y) / s);
            var b = new Vector2(_boxNow.x / s, (Screen.height - _boxNow.y) / s);
            var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            { var ac = ImUi.Accent; ac.a = 0.08f; ImUi.Fill(r, ac); }
            ImUi.Fill(new Rect(r.x, r.y, r.width, 1), ImUi.Accent); ImUi.Fill(new Rect(r.x, r.yMax - 1, r.width, 1), ImUi.Accent);
            ImUi.Fill(new Rect(r.x, r.y, 1, r.height), ImUi.Accent); ImUi.Fill(new Rect(r.xMax - 1, r.y, 1, r.height), ImUi.Accent);
        }

        /// <summary>Outlines of a selected wall run, light or pillar, and the group's markers (GL lines).</summary>
        void DrawPickGizmos()
        {
            var v = V;
            var hot = ImUi.Accent;
            if (_selected.kind == SelKind.Wall)
            {
                var w = _selWall;
                float top = EdgeCode.Kind(w.vertical ? v.V(w.line, Mathf.Min(w.a0, (w.vertical ? v.depth : v.width) - 1)) : v.H(Mathf.Min(w.a0, v.width - 1), w.line)) == EdgeCode.Wall
                    ? v.CeilingAlong(w.vertical, w.line, w.a0 * C, w.a1 * C) : 1.15f;
                foreach (float y in new[] { 0.04f, top })
                {
                    var a = w.vertical ? W3(w.line * C, w.a0 * C, y) : W3(w.a0 * C, w.line * C, y);
                    var b = w.vertical ? W3(w.line * C, w.a1 * C, y) : W3(w.a1 * C, w.line * C, y);
                    Thick(a, b, hot);
                }
                for (int a = w.a0; a <= w.a1; a++)
                {
                    var p = w.vertical ? W3(w.line * C, a * C, 0.04f) : W3(a * C, w.line * C, 0.04f);
                    Line(p, p + Vector3.up * top, new Color(hot.r, hot.g, hot.b, 0.5f));
                }
            }
            if (_selected.kind == SelKind.Light && _selected.index < v.fixtures.Count)
            {
                var f = v.fixtures[_selected.index];
                float y = _view == BuilderView.Solid ? v.RoomAt(f.x, f.z).ceiling - 0.02f : 0.06f;
                Vector2 c = _view == BuilderView.Solid ? new Vector2((f.x + 0.5f) * C, (f.z + 0.5f) * C) : LightMarker(f);
                float h = 0.36f;
                Thick(W3(c.x - h, c.y - h, y), W3(c.x + h, c.y - h, y), hot); Thick(W3(c.x + h, c.y - h, y), W3(c.x + h, c.y + h, y), hot);
                Thick(W3(c.x + h, c.y + h, y), W3(c.x - h, c.y + h, y), hot); Thick(W3(c.x - h, c.y + h, y), W3(c.x - h, c.y - h, y), hot);
            }
            if (_selected.kind == SelKind.Pillar && _selected.index < v.pillars.Count)
            {
                var p = v.pillars[_selected.index];
                float h = p.size * 0.5f + 0.08f;
                for (int k = 0; k < 2; k++)
                {
                    float y = k == 0 ? 0.04f : 2.6f;
                    var c = W3(p.x * C, p.z * C, y);
                    Thick(c + new Vector3(-h, 0, -h), c + new Vector3(h, 0, -h), hot); Thick(c + new Vector3(h, 0, -h), c + new Vector3(h, 0, h), hot);
                    Thick(c + new Vector3(h, 0, h), c + new Vector3(-h, 0, h), hot); Thick(c + new Vector3(-h, 0, h), c + new Vector3(-h, 0, -h), hot);
                }
            }
            foreach (int i in _group)
                if (i < v.props.Count) Marker(W3(v.props[i].x, v.props[i].z, v.props[i].elevation + 0.06f), 0.34f, v.props[i].yaw, hot, true);
        }

        /// <summary>The inspectors of a wall run, a light and a pillar (and the group's header).</summary>
        bool DrawPickInspector(ref ImUi.Column col)
        {
            var v = V;
            if (_selected.kind == SelKind.Wall)
            {
                var w = _selWall;
                int kind = EdgeCode.Kind(w.vertical ? v.V(w.line, w.a0) : v.H(w.a0, w.line));
                bool outer = v.PerimeterLine(w.vertical, w.line);
                ImUi.Header(col.Row(24), outer ? "Selected outer wall" : kind == EdgeCode.Half ? "Selected half wall" : kind == EdgeCode.Railing ? "Selected railing" : "Selected wall");
                ImUi.Dimmed(col.Row(22), $"{(w.a1 - w.a0) * C:0.0} m long ({w.a1 - w.a0} cells)");
                if (!outer)
                {
                    var r = col.Row();
                    float bw = r.width / 3f;
                    if (ImUi.Button(new Rect(r.x, r.y, bw - 3, r.height), "WALL", kind == EdgeCode.Wall, "Floor to ceiling.")) SetWallRun(EdgeCode.Wall);
                    if (ImUi.Button(new Rect(r.x + bw, r.y, bw - 3, r.height), "HALF", kind == EdgeCode.Half, "1.15 m: see and reach over it.")) SetWallRun(EdgeCode.Half);
                    if (ImUi.Button(new Rect(r.x + 2 * bw, r.y, bw - 3, r.height), "RAILING", kind == EdgeCode.Railing, "A slim rail.")) SetWallRun(EdgeCode.Railing);
                }
                if (kind == EdgeCode.Wall)
                {
                    var r2 = col.Row();
                    if (ImUi.Button(new Rect(r2.x, r2.y, r2.width * 0.5f - 3, r2.height), "+ DOOR HERE", false, "A door where you clicked the wall.")) AddOpeningOnWall(OpeningKind.Doorway);
                    if (ImUi.Button(new Rect(r2.center.x + 3, r2.y, r2.width * 0.5f - 3, r2.height), "+ WINDOW HERE", false, "A window where you clicked the wall.")) AddOpeningOnWall(OpeningKind.Window);
                }
                if (!outer && ImUi.Button(col.Row(), "REMOVE  [Del]", false, "Takes this run of wall away.")) SetWallRun(EdgeCode.Open);
                if (outer) ImUi.Dimmed(col.Row(40), "An outer wall stays. Doors in it are the ways in; drag the arrows outside to move it.");
                return true;
            }
            if (_selected.kind == SelKind.Light && _selected.index < v.fixtures.Count)
            {
                var f = v.fixtures[_selected.index];
                ImUi.Header(col.Row(24), "Selected light");
                var r = col.Row();
                float bw = r.width / 3f;
                if (ImUi.Button(new Rect(r.x, r.y, bw - 3, r.height), "WORKING", f.on && !f.faulty)) { PushUndo(); f.on = true; f.faulty = false; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick, 1.2f); }
                if (ImUi.Button(new Rect(r.x + bw, r.y, bw - 3, r.height), "FLICKERS", f.on && f.faulty, "On, but failing.")) { PushUndo(); f.on = true; f.faulty = true; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick, 1.0f); }
                if (ImUi.Button(new Rect(r.x + 2 * bw, r.y, bw - 3, r.height), "DEAD", !f.on, "A dead tube.")) { PushUndo(); f.on = false; f.faulty = false; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.8f); }
                if (f.on) DrawLightMood(ref col, f);
                if (ImUi.Button(col.Row(), "REMOVE  [Del]")) DeleteSelected();
                return true;
            }
            if (_selected.kind == SelKind.Pillar && _selected.index < v.pillars.Count)
            {
                var p = v.pillars[_selected.index];
                ImUi.Header(col.Row(24), "Selected pillar");
                ImUi.Dimmed(col.Row(22), "Drag it to another grid corner.");
                float size = Slide(col.Row(), "Size (m)", p.size, 0.3f, 1.2f, "0.00");
                size = Mathf.Round(size / 0.05f) * 0.05f;
                if (Mathf.Abs(size - p.size) > 0.001f) { p.size = size; _dirty = true; BuildSession.Dirty = true; BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.7f + size * 0.5f); RequestRebuild(); }
                if (ImUi.Button(col.Row(), "REMOVE  [Del]")) DeleteSelected();
                return true;
            }
            return false;
        }

        /// <summary>The header and actions of a group of props.</summary>
        void DrawGroupInspector(ref ImUi.Column col)
        {
            var v = V; var all = GroupAll();
            ImUi.Header(col.Row(24), $"{all.Count} props selected");
            ImUi.Dimmed(col.Row(34), "Drag any of them to move them all. Shift-click adds or takes one away.");
            bool anyFree = all.Exists(i => !v.props[i].pinned);
            if (ImUi.Button(col.Row(), anyFree ? "PHYSICS OFF FOR ALL (STUCK)" : "PHYSICS ON FOR ALL", false))
            { PushUndo(); foreach (int i in all) v.props[i].pinned = anyFree; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Place); }
            var r = col.Row();
            if (ImUi.Button(new Rect(r.x, r.y, r.width * 0.5f - 3, r.height), "DUPLICATE  [Ctrl+D]")) DuplicateSelected();
            if (ImUi.Button(new Rect(r.center.x + 3, r.y, r.width * 0.5f - 3, r.height), "DELETE  [Del]")) DeleteSelected();
            col.Space(6);
        }
    }
}
