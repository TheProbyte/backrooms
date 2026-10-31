using System.Collections.Generic;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Build mode, the hands-on part (round 16, owner: "more like a Sims builder or Tiny Glade"): doors, arches and windows
    /// of their own size placed on a wall where you point and dragged by their handles (sides: wider; top: taller; bottom:
    /// a sill, a window; middle: along the wall), the room's outer walls dragged out or in, a selected prop moved, lifted,
    /// turned and scaled by its handles. Everything follows the pointer smoothly, snaps in small steps (5 cm, 15 degrees;
    /// Alt: free; to the posts and the usual heights), ticks as it snaps, and shows its size as you drag.
    /// </summary>
    public partial class BuilderApp
    {
        enum Handle { None, Left, Right, Top, Bottom, Move, RoomW, RoomE, RoomS, RoomN, PropLift, PropTurn, PropScale, WallMove, WallEnd0, WallEnd1 }

        /// <summary>A selected run of wall inside the room (not its outer line) can be moved and stretched.</summary>
        bool WallRunEditable(out int kind)
        {
            kind = 0;
            if (_selected.kind != SelKind.Wall) return false;
            var v = V; var w = _selWall;
            if (w.line <= 0 || w.line >= v.LineCount(w.vertical) || w.a0 < 0 || w.a1 > v.AlongCount(w.vertical) || w.a1 <= w.a0) return false;
            kind = EdgeCode.Kind(v.Edge(w.vertical, w.line, w.a0));
            return kind == EdgeCode.Wall || kind == EdgeCode.Half || kind == EdgeCode.Railing;
        }

        void WallHandlePoints(out Vector3 move, out Vector3 end0, out Vector3 end1)
        {
            var w = _selWall;
            float mid = (w.a0 + w.a1) * 0.5f * C;
            move = w.vertical ? W3(w.line * C, mid, 0.08f) : W3(mid, w.line * C, 0.08f);
            end0 = w.vertical ? W3(w.line * C, w.a0 * C, 0.08f) : W3(w.a0 * C, w.line * C, 0.08f);
            end1 = w.vertical ? W3(w.line * C, w.a1 * C, 0.08f) : W3(w.a1 * C, w.line * C, 0.08f);
        }

        (bool vertical, int line, int a0, int a1, float at) _wallDragStart;

        Handle _hoverHandle = Handle.None, _dragHandle = Handle.None;
        OpeningDef _ghost;               // the Opening tool's preview where you point
        bool _ghostFits;
        // Drag state
        float _grabOffset;               // pointer to the dragged value at the start (m or degrees)
        OpeningDef _dragStart;           // the opening as it was (to go back to when a step does not fit)
        RoomVariant _frameSnapshot;      // room drags: the room as it was; re-framed from it each step
        RectInt _frame;                  // room drags: the new frame in the snapshot's cells
        Vector2 _frameOrigin;            // the snapshot's south-west corner in the preview (layout metres)
        float _lastRebuild, _lastValue;
        bool _rebuildPending, _dragInvalid;
        // Eased display of the selected opening (from, to, sill, top), so its outline glides
        Vector4 _dispOpening;
        OpeningDef _dispFor;
        // The preview objects of props (index as V.props), moved live while dragged
        Transform[] _propViews = new Transform[0];
        float _scaleStart, _scaleDist0;

        OpeningDef SelectedOpening => _selected.kind == SelKind.Opening && _selected.index >= 0 && _selected.index < V.openings.Count ? V.openings[_selected.index] : null;
        PropDef SelectedProp => _selected.kind == SelKind.Prop && _selected.index >= 0 && _selected.index < V.props.Count ? V.props[_selected.index] : null;
        bool Alt => Keyboard.current != null && (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);
        bool Shift => Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

        // ---------------- Picking on walls ----------------

        Ray PointerRay(Vector2 screen) => _cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));

        /// <summary>
        /// The nearest wall face the ray meets: its line, how far along it (m) and how high (m). solidOnly: only where the
        /// edge is a wall; else also open edges with an opening on them (to pick an opening through its hole).
        /// </summary>
        bool RayWall(Ray ray, bool solidOnly, out bool vertical, out int line, out float along, out float height)
        {
            vertical = false; line = 0; along = 0f; height = 0f;
            var v = V;
            float best = float.MaxValue;
            for (int pass = 0; pass < 2; pass++)
            {
                bool vert = pass == 0;
                int lines = vert ? v.width : v.depth;
                for (int ln = 0; ln <= lines; ln++)
                {
                    // The plane of the line in the preview (x = const or z = const).
                    float plane = (vert ? _origin.x : _origin.y) + ln * C;
                    float dir = vert ? ray.direction.x : ray.direction.z, org = vert ? ray.origin.x : ray.origin.z;
                    if (Mathf.Abs(dir) < 1e-5f) continue;
                    float t = (plane - org) / dir;
                    if (t <= 0f || t >= best) continue;
                    var p = ray.origin + ray.direction * t;
                    float a = (vert ? p.z - _origin.y : p.x - _origin.x);
                    if (a < 0f || a > v.LineLength(vert) || p.y < 0f) continue;
                    int cell = Mathf.Clamp(Mathf.FloorToInt(a / C), 0, (vert ? v.depth : v.width) - 1);
                    int code = vert ? v.V(ln, cell) : v.H(cell, ln);
                    bool wall = EdgeCode.Kind(code) == EdgeCode.Wall;
                    bool inOpening = v.openings.Exists(o => o.vertical == vert && o.line == ln && a >= o.from && a <= o.to);
                    if (!(wall || (!solidOnly && inOpening))) continue;
                    if (p.y > v.CeilingAlong(vert, ln, a - 0.01f, a + 0.01f) + 0.02f) continue;
                    // The cut-down views lower walls to half height: only that much can be pointed at there.
                    if ((_view == BuilderView.WallsDown || _view == BuilderView.Top) && p.y > 1.2f && !inOpening) continue;
                    best = t; vertical = vert; line = ln; along = a; height = p.y;
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>A point on an opening's wall plane where the ray crosses it: along (m) and height (m).</summary>
        bool OnOpeningPlane(Ray ray, OpeningDef o, out float along, out float height)
        {
            along = height = 0f;
            float plane = (o.vertical ? _origin.x : _origin.y) + o.line * C;
            float dir = o.vertical ? ray.direction.x : ray.direction.z, org = o.vertical ? ray.origin.x : ray.origin.z;
            if (Mathf.Abs(dir) < 1e-4f) return false;
            float t = (plane - org) / dir;
            if (t <= 0f) return false;
            var p = ray.origin + ray.direction * t;
            along = o.vertical ? p.z - _origin.y : p.x - _origin.x;
            height = p.y;
            return true;
        }

        Vector3 OpeningPoint(OpeningDef o, float along, float y) => o.vertical ? W3(o.line * C, along, y) : W3(along, o.line * C, y);

        // ---------------- Hover ----------------

        /// <summary>Each frame: the opening tool's preview, and which handle the pointer is over.</summary>
        void UpdateHandles(Vector2 screen, bool overUi)
        {
            _ghost = null;
            if (overUi || _textFocus) { if (_dragHandle == Handle.None) _hoverHandle = Handle.None; return; }
            if (_dragHandle != Handle.None) return;
            if (_tool == BuilderTool.Opening && _opening != OpeningKind.Gap && !_pastingArea)
            {
                if (RayWall(PointerRay(screen), true, out bool vert, out int line, out float along, out _))
                {
                    var g = Preset(_opening, V.CeilingAlong(vert, line, along - 0.5f, along + 0.5f));
                    float w = g.to - g.from;
                    g.vertical = vert; g.line = line;
                    g.from = SnapLen(along - w * 0.5f); g.to = g.from + w;
                    V.ClampOpening(g);
                    _ghost = g; _ghostFits = V.OpeningFits(g);
                }
            }
            _hoverHandle = PickHandle(screen);
        }

        /// <summary>The handle nearest the pointer within 16 px (the selected opening's, the selected prop's, the room's sides).</summary>
        Handle PickHandle(Vector2 screen)
        {
            float best = 16f * Mathf.Max(1f, ImUi.Scale);
            var hit = Handle.None;
            void Try(Handle h, Vector3 world)
            {
                var sp = _cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) return;
                float d = Vector2.Distance(new Vector2(sp.x, sp.y), screen);
                if (d < best) { best = d; hit = h; }
            }
            var o = SelectedOpening;
            if (o != null)
            {
                float mid = (o.sill + o.top) * 0.5f;
                Try(Handle.Left, OpeningPoint(o, o.from, mid));
                Try(Handle.Right, OpeningPoint(o, o.to, mid));
                Try(Handle.Top, OpeningPoint(o, o.Centre, o.top));
                Try(Handle.Bottom, OpeningPoint(o, o.Centre, Mathf.Max(o.sill, 0.06f)));
                Try(Handle.Move, OpeningPoint(o, o.Centre, mid));
            }
            var p = SelectedProp;
            if (p != null)
            {
                PropHandlePoints(p, out var lift, out var turn, out var scale);
                Try(Handle.PropLift, lift); Try(Handle.PropTurn, turn); Try(Handle.PropScale, scale);
            }
            if (WallRunEditable(out _))
            {
                WallHandlePoints(out var mv, out var e0, out var e1);
                Try(Handle.WallMove, mv); Try(Handle.WallEnd0, e0); Try(Handle.WallEnd1, e1);
            }
            if (_tool == BuilderTool.Select && o == null && p == null)
            {
                var v = V; float w = v.width * C, d = v.depth * C;
                Try(Handle.RoomW, W3(-0.45f, d * 0.5f, 0.05f)); Try(Handle.RoomE, W3(w + 0.45f, d * 0.5f, 0.05f));
                Try(Handle.RoomS, W3(w * 0.5f, -0.45f, 0.05f)); Try(Handle.RoomN, W3(w * 0.5f, d + 0.45f, 0.05f));
            }
            return hit;
        }

        void PropHandlePoints(PropDef p, out Vector3 lift, out Vector3 turn, out Vector3 scale)
        {
            float s = p.scale > 0.01f ? p.scale : 1f;
            float r = Mathf.Max(p.size.x, p.size.z) * 0.5f * s + 0.35f;
            var q = Quaternion.Euler(0f, p.yaw, 0f);
            lift = W3(p.x, p.z, p.elevation + p.size.y * s + 0.35f);
            turn = W3(p.x, p.z, p.elevation + 0.04f) + q * new Vector3(0f, 0f, r);
            scale = W3(p.x, p.z, p.elevation + 0.04f) + q * new Vector3(p.size.x * 0.5f * s + 0.12f, 0f, -p.size.z * 0.5f * s - 0.12f);
        }

        // ---------------- Dragging handles ----------------

        float SnapLen(float m) => Alt ? m : Mathf.Round(m / 0.05f) * 0.05f;

        /// <summary>An end of an opening: snapped, and onto a post's face when within 6 cm of one.</summary>
        float SnapEnd(float a, bool isFrom)
        {
            float s = SnapLen(a);
            if (Alt) return s;
            float vtx = Mathf.Round(a / C) * C, face = vtx + (isFrom ? 1f : -1f) * OfficeBiome.WallThickness * 0.5f;
            return Mathf.Abs(a - face) < 0.06f ? face : s;
        }

        float SnapHeight(float h, float ceiling)
        {
            if (Alt) return h;
            foreach (float mag in new[] { 0f, 0.9f, 2.1f, ceiling }) if (Mathf.Abs(h - mag) < 0.05f) return mag;
            return Mathf.Round(h / 0.05f) * 0.05f;
        }

        void BeginHandleDrag(Vector2 screen)
        {
            _dragHandle = _hoverHandle;
            _dragInvalid = false;
            PushUndo();
            var ray = PointerRay(screen);
            var o = SelectedOpening;
            if (o != null && _dragHandle <= Handle.Move)
            {
                _dragStart = o.Clone();
                if (_dragHandle == Handle.Move && OnOpeningPlane(ray, o, out float a, out _)) _grabOffset = a - o.Centre;
            }
            else if (_dragHandle >= Handle.WallMove)
            {
                _frameSnapshot = V.Clone();
                _wallDragStart = _selWall;
            }
            else if (_dragHandle >= Handle.RoomW && _dragHandle <= Handle.RoomN)
            {
                _frameSnapshot = V.Clone();
                _frame = new RectInt(0, 0, V.width, V.depth);
                _frameOrigin = _origin;
            }
            else if (_dragHandle == Handle.PropLift && SelectedProp != null)
            {
                var p = SelectedProp;
                if (LiftPlane(ray, p, out float h)) _grabOffset = h - p.elevation;
            }
            else if (_dragHandle == Handle.PropScale && SelectedProp != null)
            {
                var p = SelectedProp;
                _scaleStart = p.scale > 0.01f ? p.scale : 1f;
                _scaleDist0 = GroundAtHeight(ray, p.elevation, out var g) ? Mathf.Max(0.05f, Vector2.Distance(g, new Vector2(p.x, p.z))) : 1f;
            }
            _lastValue = float.NaN;
            BuilderSounds.Play(BuilderSounds.Sfx.Pick, 1.05f);
        }

        void DragHandle(Vector2 screen)
        {
            var ray = PointerRay(screen);
            var v = V;
            var o = SelectedOpening;
            if (o != null && _dragHandle <= Handle.Move)
            {
                if (!OnOpeningPlane(ray, o, out float along, out float height)) return;
                float ceil = v.CeilingAlong(o.vertical, o.line, o.from, o.to);
                var before = o.Clone();
                switch (_dragHandle)
                {
                    case Handle.Left: o.from = Mathf.Clamp(SnapEnd(along, true), 0f, o.to - RoomVariant.MinOpening); break;
                    case Handle.Right: o.to = Mathf.Clamp(SnapEnd(along, false), o.from + RoomVariant.MinOpening, v.LineLength(o.vertical)); break;
                    case Handle.Top: o.top = Mathf.Clamp(SnapHeight(height, ceil), o.sill + 0.3f, ceil); break;
                    case Handle.Bottom: o.sill = Mathf.Clamp(SnapHeight(height, ceil), 0f, o.top - 0.3f); break;
                    case Handle.Move:
                    {
                        float w = o.to - o.from, c = SnapLen(along - _grabOffset);
                        float f = Mathf.Clamp(c - w * 0.5f, 0f, v.LineLength(o.vertical) - w);
                        o.from = f; o.to = f + w;
                        break;
                    }
                }
                if (!v.OpeningFits(o, o))
                {
                    // Not there (over open floor, into another opening): it stays at the last place it fitted.
                    o.from = before.from; o.to = before.to; o.sill = before.sill; o.top = before.top;
                    if (!_dragInvalid) { BuilderSounds.Play(BuilderSounds.Sfx.Error); _dragInvalid = true; }
                    return;
                }
                _dragInvalid = false;
                float value = _dragHandle == Handle.Top ? o.top : _dragHandle == Handle.Bottom ? o.sill : _dragHandle == Handle.Move ? o.from : o.to - o.from;
                if (!Mathf.Approximately(value, _lastValue))
                {
                    if (!float.IsNaN(_lastValue)) BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.75f + 0.35f * Mathf.Clamp01((_dragHandle == Handle.Top || _dragHandle == Handle.Bottom ? value / 3f : (o.to - o.from) / 3f)));
                    _lastValue = value;
                    _strokeChanged = true;
                    RequestRebuild();
                }
                return;
            }
            if (_dragHandle >= Handle.RoomW && _dragHandle <= Handle.RoomN) { DragFrame(ray); return; }
            if (_dragHandle >= Handle.WallMove) { DragWallRun(ray); return; }
            var p = SelectedProp;
            if (p == null) return;
            switch (_dragHandle)
            {
                case Handle.PropLift:
                    if (LiftPlane(ray, p, out float h))
                    {
                        float e = Alt ? h - _grabOffset : Mathf.Round((h - _grabOffset) / 0.05f) * 0.05f;
                        e = Mathf.Clamp(e, -p.size.y * p.scale, 3.5f);
                        if (!Mathf.Approximately(e, p.elevation)) { p.elevation = e; PropTick(e / 3f); }
                    }
                    break;
                case Handle.PropTurn:
                    if (GroundAtHeight(ray, p.elevation, out var g))
                    {
                        float yaw = Mathf.Atan2(g.x - p.x, g.y - p.z) * Mathf.Rad2Deg;
                        yaw = Alt ? yaw : Shift ? Mathf.Round(yaw) : Mathf.Round(yaw / 15f) * 15f;
                        yaw = Mathf.Repeat(yaw, 360f);
                        if (!Mathf.Approximately(yaw, p.yaw)) { p.yaw = yaw; PropTick(yaw / 360f); }
                    }
                    break;
                case Handle.PropScale:
                    if (GroundAtHeight(ray, p.elevation, out var gs))
                    {
                        float s = _scaleStart * Vector2.Distance(gs, new Vector2(p.x, p.z)) / _scaleDist0;
                        s = Mathf.Clamp(Alt ? s : Mathf.Round(s / 0.05f) * 0.05f, 0.25f, 4f);
                        if (!Mathf.Approximately(s, p.scale)) { p.scale = s; PropTick(s / 4f); }
                    }
                    break;
            }
        }

        void PropTick(float t)
        {
            BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.75f + 0.5f * Mathf.Clamp01(t));
            _strokeChanged = true;
            if (!HasView(_selected.index)) RequestRebuild();
        }

        /// <summary>The height (m) where the ray crosses the upright plane through the prop that faces the camera.</summary>
        bool LiftPlane(Ray ray, PropDef p, out float height)
        {
            height = 0f;
            var centre = W3(p.x, p.z, 0f);
            var n = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up);
            if (n.sqrMagnitude < 1e-4f) n = Vector3.forward;
            var plane = new Plane(n.normalized, centre);
            if (!plane.Raycast(ray, out float t)) return false;
            height = (ray.origin + ray.direction * t).y;
            return true;
        }

        /// <summary>Where the ray meets the level plane at a height above the floor (blueprint metres).</summary>
        bool GroundAtHeight(Ray ray, float y, out Vector2 local)
        {
            local = default;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = (y - ray.origin.y) / ray.direction.y;
            if (t < 0f) return false;
            var p = ray.origin + ray.direction * t;
            local = new Vector2(p.x - _origin.x, p.z - _origin.y);
            return true;
        }

        /// <summary>A room side dragged: the room re-framed from how it was, a cell at a time.</summary>
        void DragFrame(Ray ray)
        {
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return;
            float t = (0f - ray.origin.y) / ray.direction.y;
            if (t < 0f) return;
            var p = ray.origin + ray.direction * t;
            // In the snapshot's cells (its corner stays where it was at the start of the drag).
            float gx = (p.x - _frameOrigin.x) / C, gz = (p.z - _frameOrigin.y) / C;
            var f = _frame;
            int min = RoomVariant.MinSize, max = RoomVariant.MaxSize;
            switch (_dragHandle)
            {
                case Handle.RoomE: f.width = Mathf.Clamp(Mathf.RoundToInt(gx) - f.x, min, max); break;
                case Handle.RoomN: f.height = Mathf.Clamp(Mathf.RoundToInt(gz) - f.y, min, max); break;
                case Handle.RoomW: { int x1 = f.xMax; int x0 = Mathf.Clamp(Mathf.RoundToInt(gx), x1 - max, x1 - min); f.x = x0; f.width = x1 - x0; break; }
                case Handle.RoomS: { int z1 = f.yMax; int z0 = Mathf.Clamp(Mathf.RoundToInt(gz), z1 - max, z1 - min); f.y = z0; f.height = z1 - z0; break; }
            }
            if (f.Equals(_frame)) return;
            bool grew = f.width * f.height > _frame.width * _frame.height;
            _frame = f;
            var nv = _frameSnapshot.Clone();
            nv.Reframe(f.x, f.y, f.width, f.height);
            // Keep the room still on screen: the preview centres the room, so its corner moves as it grows.
            var oldCorner = _origin;
            _bp.variants[Mathf.Clamp(_vi, 0, _bp.variants.Count - 1)] = nv;
            var newCorner = OriginOf(nv);
            Vector2 shift = newCorner - (_frameOrigin + new Vector2(f.x, f.y) * C);
            _target += new Vector3(shift.x, 0f, shift.y);
            _frameOrigin += shift;
            _selected = Selection.None;
            BuilderSounds.Play(grew ? BuilderSounds.Sfx.Grow : BuilderSounds.Sfx.Shrink, 0.85f + 0.3f * Mathf.Clamp01((f.width + f.height) / 30f));
            _strokeChanged = true;
            _needRebuild = true;
        }

        /// <summary>A wall run's handle dragged: moved to another line, or an end stretched along its line, a cell at a time,
        /// each step done again from how the room was at the start of the drag.</summary>
        void DragWallRun(Ray ray)
        {
            if (!GroundAtHeight(ray, 0f, out var g) || _frameSnapshot == null) return;
            var s = _wallDragStart;
            var nv = _frameSnapshot.Clone();
            int lines = nv.LineCount(s.vertical), max = nv.AlongCount(s.vertical);
            var next = s;
            bool ok;
            if (_dragHandle == Handle.WallMove)
            {
                int line = Mathf.Clamp(Mathf.RoundToInt((s.vertical ? g.x : g.y) / C), 1, lines - 1);
                next.line = line;
                ok = line == s.line || nv.MoveRun(s.vertical, s.line, s.a0, s.a1, line);
            }
            else
            {
                int at = Mathf.RoundToInt((s.vertical ? g.y : g.x) / C);
                if (_dragHandle == Handle.WallEnd0) next.a0 = Mathf.Clamp(at, 0, s.a1 - 1); else next.a1 = Mathf.Clamp(at, s.a0 + 1, max);
                ok = (next.a0 == s.a0 && next.a1 == s.a1) || nv.StretchRun(s.vertical, s.line, s.a0, s.a1, next.a0, next.a1);
            }
            if (!ok) { if (!_dragInvalid) { BuilderSounds.Play(BuilderSounds.Sfx.Error); _dragInvalid = true; } return; }
            _dragInvalid = false;
            if (next.line == _selWall.line && next.a0 == _selWall.a0 && next.a1 == _selWall.a1) return;
            bool grew = next.a1 - next.a0 > _selWall.a1 - _selWall.a0;
            _bp.variants[Mathf.Clamp(_vi, 0, _bp.variants.Count - 1)] = nv;
            _selWall = next;
            BuilderSounds.Play(_dragHandle == Handle.WallMove ? BuilderSounds.Sfx.Wall : grew ? BuilderSounds.Sfx.Grow : BuilderSounds.Sfx.Shrink, 0.9f + 0.05f * (next.a1 - next.a0));
            _strokeChanged = true;
            RequestRebuild();
        }

        void EndHandleDrag()
        {
            bool frame = _dragHandle >= Handle.RoomW && _dragHandle <= Handle.RoomN;
            _dragHandle = Handle.None;
            _frameSnapshot = null;
            BuilderSounds.Play(BuilderSounds.Sfx.Drop, 0.95f);
            if (_strokeChanged) { Changed(); if (frame) SetStatus($"Room {V.width} × {V.depth} cells ({V.width * C:0.0} × {V.depth * C:0.0} m)."); }
            else if (_undo.Count > 0) _undo.Pop();          // nothing changed: no undo step
        }

        void RequestRebuild()
        {
            if (Time.unscaledTime - _lastRebuild > 0.09f) _needRebuild = true;
            else _rebuildPending = true;
        }

        /// <summary>Called each frame: a rebuild held back by the throttle happens once it may.</summary>
        void UpdateRebuildThrottle()
        {
            if (_rebuildPending && Time.unscaledTime - _lastRebuild > 0.09f) { _rebuildPending = false; _needRebuild = true; }
        }

        // ---------------- Props: their preview objects, moved live ----------------

        /// <summary>After a rebuild: each prop's preview object (the movable it became), by where it stands.</summary>
        void IndexPropViews()
        {
            var v = V;
            _propViews = new Transform[v.props.Count];
            if (!_previewObject) return;
            var movables = new List<MovableProp>(_previewObject.GetComponentsInChildren<MovableProp>());
            for (int i = 0; i < v.props.Count; i++)
            {
                var want = W3(v.props[i].x, v.props[i].z, 0f);
                float best = 0.2f; MovableProp pick = null;
                foreach (var m in movables)
                {
                    var d = m.transform.position - want; d.y = 0f;
                    if (d.magnitude < best) { best = d.magnitude; pick = m; }
                }
                if (pick) { _propViews[i] = pick.transform; movables.Remove(pick); }
            }
        }

        bool HasView(int i) => i >= 0 && i < _propViews.Length && _propViews[i];

        /// <summary>Each frame: the props being moved glide to where they are wanted (the preview is rebuilt on letting go).</summary>
        void UpdatePropViews()
        {
            var v = V;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.045f);
            for (int i = 0; i < _propViews.Length && i < v.props.Count; i++)
            {
                var t = _propViews[i];
                if (!t) continue;
                var p = v.props[i];
                float s = p.scale > 0.01f ? p.scale : 1f;
                var pos = W3(p.x, p.z, p.elevation);
                var rot = Quaternion.Euler(p.pitch, p.yaw, p.roll);
                t.position = Vector3.Lerp(t.position, pos, k);
                t.rotation = Quaternion.Slerp(t.rotation, rot, k);
                t.localScale = Vector3.Lerp(t.localScale, Vector3.one * s, k);
            }
        }

        // ---------------- Drawing ----------------

        /// <summary>The openings' outlines, the opening tool's preview, the selected things' handles and the room's side arrows.</summary>
        void DrawHandleGizmos()
        {
            var v = V;
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < v.openings.Count; i++)
            {
                var o = v.openings[i];
                bool sel = _selected.kind == SelKind.Opening && _selected.index == i;
                if (sel)
                {
                    var target = new Vector4(o.from, o.to, o.sill, o.top);
                    if (_dispFor != o) { _dispFor = o; _dispOpening = target; }
                    _dispOpening = Vector4.Lerp(_dispOpening, target, 1f - Mathf.Exp(-dt / 0.05f));
                    OpeningOutline(o, _dispOpening.x, _dispOpening.y, _dispOpening.z, _dispOpening.w, ImUi.Accent, true);
                }
                else OpeningOutline(o, o.from, o.to, o.sill, o.top, new Color(0.55f, 0.8f, 1f, 0.55f), false);
            }
            if (_ghost != null) OpeningOutline(_ghost, _ghost.from, _ghost.to, _ghost.sill, _ghost.top, _ghostFits ? new Color(1f, 1f, 1f, 0.9f) : ImUi.Bad, false);
            var so = SelectedOpening;
            if (so != null)
            {
                float mid = (_dispOpening.z + _dispOpening.w) * 0.5f, ctr = (_dispOpening.x + _dispOpening.y) * 0.5f;
                Knob(Handle.Left, OpeningPoint(so, _dispOpening.x, mid));
                Knob(Handle.Right, OpeningPoint(so, _dispOpening.y, mid));
                Knob(Handle.Top, OpeningPoint(so, ctr, _dispOpening.w));
                Knob(Handle.Bottom, OpeningPoint(so, ctr, Mathf.Max(_dispOpening.z, 0.06f)));
                Knob(Handle.Move, OpeningPoint(so, ctr, mid), true);
            }
            var sp = SelectedProp;
            if (sp != null)
            {
                PropHandlePoints(sp, out var lift, out var turn, out var scale);
                var foot = W3(sp.x, sp.z, sp.elevation + 0.04f);
                float s = sp.scale > 0.01f ? sp.scale : 1f, r = Mathf.Max(sp.size.x, sp.size.z) * 0.5f * s + 0.35f;
                var ringCol = _hoverHandle == Handle.PropTurn || _dragHandle == Handle.PropTurn ? ImUi.Accent : new Color(1f, 1f, 1f, 0.5f);
                for (int i = 0; i < 48; i++)
                {
                    float a0 = i / 48f * Mathf.PI * 2f, a1 = (i + 1) / 48f * Mathf.PI * 2f;
                    Line(foot + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * r, foot + new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * r, ringCol);
                }
                Line(W3(sp.x, sp.z, sp.elevation + sp.size.y * s), lift, new Color(0.5f, 1f, 0.6f, 0.9f));
                Line(foot, scale, new Color(1f, 0.7f, 0.4f, 0.7f));
                Knob(Handle.PropLift, lift); Knob(Handle.PropTurn, turn); Knob(Handle.PropScale, scale);
                if (Mathf.Abs(sp.elevation) > 0.01f) Line(W3(sp.x, sp.z, 0.02f), W3(sp.x, sp.z, sp.elevation), new Color(1f, 1f, 1f, 0.35f));
            }
            if (WallRunEditable(out _))
            {
                WallHandlePoints(out var mv, out var e0, out var e1);
                var across = _selWall.vertical ? Vector3.right : Vector3.forward;
                Arrow(Handle.WallMove, mv + across * 0.12f, across);
                Arrow(Handle.WallMove, mv - across * 0.12f, -across);
                Knob(Handle.WallEnd0, e0); Knob(Handle.WallEnd1, e1);
            }
            if (_tool == BuilderTool.Select && so == null)
            {
                float w = v.width * C, d = v.depth * C;
                Arrow(Handle.RoomW, W3(-0.45f, d * 0.5f, 0.05f), Vector3.left);
                Arrow(Handle.RoomE, W3(w + 0.45f, d * 0.5f, 0.05f), Vector3.right);
                Arrow(Handle.RoomS, W3(w * 0.5f, -0.45f, 0.05f), Vector3.back);
                Arrow(Handle.RoomN, W3(w * 0.5f, d + 0.45f, 0.05f), Vector3.forward);
            }
        }

        void OpeningOutline(OpeningDef o, float from, float to, float sill, float top, Color c, bool thick)
        {
            const int K = 16;
            var pts = new List<Vector3> { OpeningPoint(o, from, sill), OpeningPoint(o, to, sill) };
            if (o.arch)
            {
                float r = (to - from) * 0.5f, mid = (from + to) * 0.5f, spring = Mathf.Max(sill + 0.05f, top - r);
                for (int i = 0; i <= K; i++)
                {
                    float a = Mathf.PI * i / K;
                    pts.Add(OpeningPoint(o, mid + Mathf.Cos(a) * r, spring + Mathf.Sin(a) * (top - spring)));
                }
            }
            else { pts.Add(OpeningPoint(o, to, top)); pts.Add(OpeningPoint(o, from, top)); }
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                if (thick) Thick(a, b, c); else Line(a, b, c);
            }
        }

        /// <summary>A handle: a small square facing the camera, bigger and brighter under the pointer or while dragged.</summary>
        void Knob(Handle h, Vector3 p, bool round = false)
        {
            bool hot = _hoverHandle == h || _dragHandle == h;
            float size = Vector3.Distance(_cam.transform.position, p) * (hot ? 0.016f : 0.011f);
            var right = _cam.transform.right * size; var up = _cam.transform.up * size;
            var c = hot ? new Color(1f, 0.95f, 0.55f, 1f) : new Color(1f, 1f, 1f, 0.9f);
            int n = round ? 12 : 4;
            for (int i = 0; i < n; i++)
            {
                float a0 = (i + 0.5f) / n * Mathf.PI * 2f, a1 = (i + 1.5f) / n * Mathf.PI * 2f;
                Vector3 A = p + right * Mathf.Cos(a0) * 1.4f + up * Mathf.Sin(a0) * 1.4f, B = p + right * Mathf.Cos(a1) * 1.4f + up * Mathf.Sin(a1) * 1.4f;
                Line(A, B, c); Line(p + (A - p) * 0.7f, p + (B - p) * 0.7f, c);
            }
        }

        void Arrow(Handle h, Vector3 p, Vector3 dir)
        {
            bool hot = _hoverHandle == h || _dragHandle == h;
            var c = hot ? ImUi.Accent : new Color(0.9f, 0.78f, 0.3f, 0.55f);
            var side = Vector3.Cross(Vector3.up, dir) * 0.22f;
            float len = hot ? 0.42f : 0.32f;
            Thick(p - dir * 0.1f, p + dir * len, c);
            Thick(p + dir * len, p + dir * (len - 0.18f) + side, c);
            Thick(p + dir * len, p + dir * (len - 0.18f) - side, c);
        }

        /// <summary>Sizes next to what is being dragged or is selected (OnGUI, virtual pixels).</summary>
        void DrawHandleLabels()
        {
            if (_screen != ScreenKind.Editor || _bp == null || _showHelp || _modal != Modal.None) return;
            var o = SelectedOpening;
            if (o != null)
            {
                string text = $"{o.Width:0.00} × {o.top - o.sill:0.00} m" + (o.sill > 0.001f ? $"   sill {o.sill:0.00}" : "") + (o.arch ? "   arch" : "");
                Label(OpeningPoint(o, o.Centre, o.top + 0.25f), text);
            }
            var p = SelectedProp;
            if (p != null && _dragHandle >= Handle.PropLift)
            {
                string text = _dragHandle == Handle.PropLift ? $"up {p.elevation:0.00} m" : _dragHandle == Handle.PropTurn ? $"{p.yaw:0}°" : $"× {p.scale:0.00}";
                PropHandlePoints(p, out var lift, out var turn, out var scale);
                Label(_dragHandle == Handle.PropLift ? lift : _dragHandle == Handle.PropTurn ? turn : scale, text);
            }
            if (_dragHandle >= Handle.RoomW && _dragHandle <= Handle.RoomN)
                Label(W3(V.width * C * 0.5f, V.depth * C * 0.5f, 0.1f), $"{V.width} × {V.depth} cells   {V.width * C:0.0} × {V.depth * C:0.0} m");
        }

        void Label(Vector3 world, string text)
        {
            var sp = _cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;
            var gp = new Vector2(sp.x / ImUi.Scale, (Screen.height - sp.y) / ImUi.Scale);
            var style = new GUIStyle(ImUi.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 15, normal = { textColor = Color.white } };
            var size = style.CalcSize(new GUIContent(text));
            var r = new Rect(gp.x - size.x * 0.5f - 8, gp.y - 14, size.x + 16, 26);
            ImUi.Fill(r, new Color(0f, 0f, 0f, 0.65f));
            GUI.Label(r, text, style);
        }
    }
}
