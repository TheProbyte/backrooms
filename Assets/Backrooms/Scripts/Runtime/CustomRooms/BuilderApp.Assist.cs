using System;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Build mode's feel (round 18): what the pointer is over outlined before it is clicked (and the selected prop's box);
    /// live measurements (a prop's distance to the walls round it, the room's size); smart placing (furniture flat against a
    /// wall it is pushed up to, turned square to it, lined up with its neighbours; Alt: off); a small map in the corner; and a
    /// right-click menu on anything.
    /// </summary>
    public partial class BuilderApp
    {
        // ---------------- What the pointer is over ----------------

        Selection _hoverPick = Selection.None;
        (bool vertical, int line, int a0, int a1, float at) _hoverWall;

        void UpdateHoverPick(Vector2 screen)
        {
            _hoverPick = Selection.None;
            if (_tool != BuilderTool.Select || _dragHandle != Handle.None || _dragging || _stroking || _hoverHandle != Handle.None) return;
            _hoverPick = PickUnder(screen, out _hoverWall);
        }

        /// <summary>What a click would take (nearest first, as SELECT takes it), without taking it.</summary>
        Selection PickUnder(Vector2 screen, out (bool vertical, int line, int a0, int a1, float at) wall)
        {
            wall = default;
            var v = V;
            if (_hoverGround)
            {
                float best = 0.6f; var pick = Selection.None;
                for (int i = 0; i < v.props.Count; i++) { float d = Vector2.Distance(new Vector2(v.props[i].x, v.props[i].z), _hoverMetres); if (d < best) { best = d; pick = new Selection { kind = SelKind.Prop, index = i }; } }
                for (int i = 0; i < v.loot.Count; i++) { float d = Vector2.Distance(new Vector2(v.loot[i].x, v.loot[i].z), _hoverMetres); if (d < best) { best = d; pick = new Selection { kind = SelKind.Loot, index = i }; } }
                for (int i = 0; i < v.sounds.Count; i++) { float d = Vector2.Distance(new Vector2(v.sounds[i].x, v.sounds[i].z), _hoverMetres); if (d < Mathf.Min(best, 0.35f)) { best = d; pick = new Selection { kind = SelKind.Sound, index = i }; } }
                if (pick.kind != SelKind.None) return pick;
                for (int i = 0; i < v.pillars.Count; i++)
                {
                    var p = v.pillars[i];
                    if (Mathf.Abs(_hoverMetres.x - p.x * C) < p.size * 0.5f + 0.12f && Mathf.Abs(_hoverMetres.y - p.z * C) < p.size * 0.5f + 0.12f) return new Selection { kind = SelKind.Pillar, index = i };
                }
                if (_view != BuilderView.Solid)
                    for (int i = 0; i < v.fixtures.Count; i++)
                        if (Vector2.Distance(_hoverMetres, LightMarker(v.fixtures[i])) < 0.3f) return new Selection { kind = SelKind.Light, index = i };
            }
            int dc = DecalUnder(screen);
            if (dc >= 0) return new Selection { kind = SelKind.Decal, index = dc };
            int op = OpeningUnder(screen);
            if (op >= 0) return new Selection { kind = SelKind.Opening, index = op };
            var ray = PointerRay(screen);
            bool hit = RayWall(ray, true, out bool vert, out int line, out float along, out float height);
            if (!hit) hit = RayHalf(ray, out vert, out line, out along);
            if (hit)
            {
                var wp = vert ? W3(line * C, along, height) : W3(along, line * C, height);
                if (!_hoverGround || Vector3.Distance(ray.origin, W3(_hoverMetres.x, _hoverMetres.y, 0f)) >= Vector3.Distance(ray.origin, wp) - 0.05f)
                {
                    int alongN = vert ? v.depth : v.width;
                    int cell = Mathf.Clamp(Mathf.FloorToInt(along / C), 0, alongN - 1);
                    int kind = EdgeCode.Kind(vert ? v.V(line, cell) : v.H(cell, line));
                    int a0 = cell, a1 = cell + 1;
                    while (a0 > 0 && EdgeCode.Kind(vert ? v.V(line, a0 - 1) : v.H(a0 - 1, line)) == kind) a0--;
                    while (a1 < alongN && EdgeCode.Kind(vert ? v.V(line, a1) : v.H(a1, line)) == kind) a1++;
                    wall = (vert, line, a0, a1, along);
                    return new Selection { kind = SelKind.Wall, index = 0 };
                }
            }
            if (_hoverCellValid && !v.Outside(_hoverCell.x, _hoverCell.y)) return new Selection { kind = SelKind.Room, index = v.RoomOf(_hoverCell.x, _hoverCell.y) };
            return Selection.None;
        }

        /// <summary>A prop's box as it stands (posed: lifted, tipped, scaled), as 8 corners.</summary>
        Vector3[] PropCorners(PropDef p)
        {
            float s = p.scale > 0.01f ? p.scale : 1f;
            var q = Quaternion.Euler(p.pitch, p.yaw, p.roll);
            var foot = W3(p.x, p.z, p.elevation);
            var size = p.kind == PropKind.WallClock ? new Vector3(p.size.x, p.size.x, Mathf.Max(0.05f, p.size.z)) : p.size;
            if (p.kind == PropKind.WallClock) foot = W3(p.x, p.z, p.size.y - p.size.x * 0.5f);
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = foot + q * (Vector3.Scale(new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? 0f : 1f, (i & 4) == 0 ? -0.5f : 0.5f), size) * s);
            return c;
        }

        void BoxLines(Vector3[] c, Color col, bool thick)
        {
            int[,] e = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            for (int i = 0; i < 12; i++) { if (thick) Thick(c[e[i, 0]], c[e[i, 1]], col); else Line(c[e[i, 0]], c[e[i, 1]], col); }
        }

        void DrawHoverGizmos()
        {
            var v = V;
            // The selected prop (and its group): its box, so it is clear which it is.
            var sp = SelectedProp;
            if (sp != null) BoxLines(PropCorners(sp), new Color(ImUi.Accent.r, ImUi.Accent.g, ImUi.Accent.b, 0.85f), false);
            foreach (int gi in _group) if (gi < v.props.Count) BoxLines(PropCorners(v.props[gi]), new Color(ImUi.Accent.r, ImUi.Accent.g, ImUi.Accent.b, 0.6f), false);
            foreach (var (a, b) in _guides) Line(a, b, new Color(0.4f, 0.9f, 1f, 0.9f));
            var h = _hoverPick;
            if (h.kind == SelKind.None || (h.kind == _selected.kind && h.index == _selected.index && h.kind != SelKind.Wall)) return;
            var c = new Color(1f, 1f, 1f, 0.75f);
            switch (h.kind)
            {
                case SelKind.Prop when h.index < v.props.Count: BoxLines(PropCorners(v.props[h.index]), c, true); break;
                case SelKind.Loot when h.index < v.loot.Count: Marker(W3(v.loot[h.index].x, v.loot[h.index].z, 0.35f), 0.28f, 45f, c, false); break;
                case SelKind.Opening when h.index < v.openings.Count: { var o = v.openings[h.index]; OpeningOutline(o, o.from, o.to, o.sill, o.top, c, true); break; }
                case SelKind.Pillar when h.index < v.pillars.Count:
                {
                    var p = v.pillars[h.index]; float s = p.size * 0.5f + 0.06f; var at = W3(p.x * C, p.z * C, 0.05f);
                    for (int k = 0; k < 2; k++) { var y = Vector3.up * (k * 2.6f); Line(at + y + new Vector3(-s, 0, -s), at + y + new Vector3(s, 0, -s), c); Line(at + y + new Vector3(s, 0, -s), at + y + new Vector3(s, 0, s), c); Line(at + y + new Vector3(s, 0, s), at + y + new Vector3(-s, 0, s), c); Line(at + y + new Vector3(-s, 0, s), at + y + new Vector3(-s, 0, -s), c); }
                    break;
                }
                case SelKind.Light when h.index < v.fixtures.Count:
                {
                    var m = LightMarker(v.fixtures[h.index]); float s = 0.34f;
                    Thick(W3(m.x - s, m.y - s, 0.06f), W3(m.x + s, m.y - s, 0.06f), c); Thick(W3(m.x + s, m.y - s, 0.06f), W3(m.x + s, m.y + s, 0.06f), c);
                    Thick(W3(m.x + s, m.y + s, 0.06f), W3(m.x - s, m.y + s, 0.06f), c); Thick(W3(m.x - s, m.y + s, 0.06f), W3(m.x - s, m.y - s, 0.06f), c);
                    break;
                }
                case SelKind.Decal when h.index < v.decals.Count:
                {
                    var d = v.decals[h.index]; float plane = d.line * C + d.side * 0.08f, s = Mathf.Max(0.06f, d.size);
                    Vector3 P(float a, float y) => d.vertical ? W3(plane, d.along + a, y) : W3(d.along + a, plane, y);
                    Thick(P(-s, d.height - s), P(s, d.height - s), c); Thick(P(s, d.height - s), P(s, d.height + s), c); Thick(P(s, d.height + s), P(-s, d.height + s), c); Thick(P(-s, d.height + s), P(-s, d.height - s), c);
                    break;
                }
                case SelKind.Wall:
                {
                    var w = _hoverWall;
                    int kind = EdgeCode.Kind(w.vertical ? v.V(w.line, Mathf.Min(w.a0, v.AlongCount(w.vertical) - 1)) : v.H(Mathf.Min(w.a0, v.width - 1), w.line));
                    float top = kind == EdgeCode.Wall ? v.CeilingAlong(w.vertical, w.line, w.a0 * C, w.a1 * C) : 1.15f;
                    var a = w.vertical ? W3(w.line * C, w.a0 * C, 0.04f) : W3(w.a0 * C, w.line * C, 0.04f);
                    var b = w.vertical ? W3(w.line * C, w.a1 * C, 0.04f) : W3(w.a1 * C, w.line * C, 0.04f);
                    Line(a, b, c); Line(a + Vector3.up * top, b + Vector3.up * top, c); Line(a, a + Vector3.up * top, c); Line(b, b + Vector3.up * top, c);
                    break;
                }
                case SelKind.Room:
                {
                    if (!_hoverCellValid) break;
                    float x0 = _hoverCell.x * C, z0 = _hoverCell.y * C;
                    var cc = new Color(1f, 1f, 1f, 0.35f);
                    Line(W3(x0, z0, 0.05f), W3(x0 + C, z0, 0.05f), cc); Line(W3(x0 + C, z0, 0.05f), W3(x0 + C, z0 + C, 0.05f), cc);
                    Line(W3(x0 + C, z0 + C, 0.05f), W3(x0, z0 + C, 0.05f), cc); Line(W3(x0, z0 + C, 0.05f), W3(x0, z0, 0.05f), cc);
                    break;
                }
            }
        }

        // ---------------- Walls round a point ----------------

        static readonly Vector2[] Dirs = { Vector2.left, Vector2.right, Vector2.down, Vector2.up };

        /// <summary>
        /// From a point in the room, the nearest wall face along one axis direction (0 -X, 1 +X, 2 -Z, 3 +Z): its coordinate on
        /// that axis (m), or NaN if none within the room. Walls, half walls, railings and the room's own edge stop it.
        /// </summary>
        float WallFaceFrom(Vector2 p, int dir)
        {
            var v = V;
            bool alongX = dir < 2;                       // the line crossed is vertical (X = const) when moving along X
            int cx = Mathf.Clamp(Mathf.FloorToInt(p.x / C), 0, v.width - 1), cz = Mathf.Clamp(Mathf.FloorToInt(p.y / C), 0, v.depth - 1);
            int step = dir == 0 || dir == 2 ? -1 : 1;
            int cell = alongX ? cx : cz, row = alongX ? cz : cx;
            float t = OfficeBiome.WallThickness * 0.5f;
            for (int k = 0; k < 64; k++)
            {
                int line = step > 0 ? cell + 1 : cell;
                if (line < 0 || line > (alongX ? v.width : v.depth)) return float.NaN;
                int code = alongX ? v.V(line, row) : v.H(row, line);
                int kind = EdgeCode.Kind(code);
                bool stop = kind == EdgeCode.Wall || kind == EdgeCode.Half || kind == EdgeCode.Railing || v.Boundary(alongX, line, row);
                if (stop)
                {
                    // An opening right there: the gap, not the wall (unless the point is beside the opening's wall).
                    float along = alongX ? p.y : p.x;
                    bool hole = v.openings.Exists(o => o.vertical == alongX && o.line == line && along > o.from && along < o.to && o.Door);
                    if (!hole) return line * C - step * t;
                }
                cell += step;
                if (cell < 0 || cell >= (alongX ? v.width : v.depth)) return float.NaN;
            }
            return float.NaN;
        }

        /// <summary>Half the footprint of a prop turned by yaw, along X and along Z (m).</summary>
        static Vector2 HalfFootprint(Vector3 size, float scale, float yaw)
        {
            float c = Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad)), s = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad));
            return new Vector2(c * size.x + s * size.z, s * size.x + c * size.z) * 0.5f * scale;
        }

        // ---------------- Smart placing ----------------

        readonly List<(Vector3 a, Vector3 b)> _guides = new List<(Vector3, Vector3)>();

        /// <summary>
        /// Where a prop dragged to m goes: pushed up to a wall (within 0.22 m) it sits flat against it and, if it was nearly
        /// facing away from it, turns square to it; it lines up with a neighbour's middle within 7 cm. Guide lines show both.
        /// </summary>
        Vector2 SmartPlace(Vector2 m, Vector3 size, float scale, ref float yaw, int self)
        {
            _guides.Clear();
            var v = V;
            // Line up with neighbours first (a wall snap after it wins).
            for (int i = 0; i < v.props.Count; i++)
            {
                if (i == self || _group.Contains(i)) continue;
                var q = v.props[i];
                if (Mathf.Abs(q.x - m.x) < 0.07f && Mathf.Abs(q.z - m.y) < 4f) { m.x = q.x; _guides.Add((W3(q.x, q.z, 0.05f), W3(m.x, m.y, 0.05f))); }
                if (Mathf.Abs(q.z - m.y) < 0.07f && Mathf.Abs(q.x - m.x) < 4f) { m.y = q.z; _guides.Add((W3(q.x, q.z, 0.05f), W3(m.x, m.y, 0.05f))); }
            }
            for (int pass = 0; pass < 2; pass++)
            {
                var half = HalfFootprint(size, scale, yaw);
                float bestGap = 0.22f; int bestDir = -1; float bestFace = 0f;
                for (int d = 0; d < 4; d++)
                {
                    float face = WallFaceFrom(m, d);
                    if (float.IsNaN(face)) continue;
                    float edge = d == 0 ? m.x - half.x : d == 1 ? m.x + half.x : d == 2 ? m.y - half.y : m.y + half.y;
                    float gap = d == 0 || d == 2 ? edge - face : face - edge;
                    if (gap > -0.1f && gap < bestGap) { bestGap = gap; bestDir = d; bestFace = face; }
                }
                if (bestDir < 0) break;
                // Square to it if nearly so: its back to the wall (front +Z facing into the room).
                float want = bestDir == 0 ? 90f : bestDir == 1 ? 270f : bestDir == 2 ? 0f : 180f;
                if (pass == 0 && Mathf.Abs(Mathf.DeltaAngle(yaw, want)) < 20f && !Mathf.Approximately(Mathf.DeltaAngle(yaw, want), 0f)) { yaw = want; continue; }
                const float touch = 0.01f;
                if (bestDir == 0) m.x = bestFace + half.x + touch; else if (bestDir == 1) m.x = bestFace - half.x - touch;
                else if (bestDir == 2) m.y = bestFace + half.y + touch; else m.y = bestFace - half.y - touch;
                var wa = bestDir < 2 ? W3(bestFace, m.y - half.y, 0.05f) : W3(m.x - half.x, bestFace, 0.05f);
                var wb = bestDir < 2 ? W3(bestFace, m.y + half.y, 0.05f) : W3(m.x + half.x, bestFace, 0.05f);
                _guides.Add((wa, wb));
                break;
            }
            return m;
        }

        // ---------------- Measurements ----------------

        readonly List<(Vector3 at, string text)> _measureLabels = new List<(Vector3, string)>();

        void DrawMeasureGizmos()
        {
            _measureLabels.Clear();
            var v = V;
            var c = new Color(0.4f, 0.9f, 1f, 0.85f);
            var p = SelectedProp;
            if (p != null && !WallProp(p.kind) && !Grouped)
            {
                var half = HalfFootprint(p.size, p.scale > 0.01f ? p.scale : 1f, p.yaw);
                var at = new Vector2(p.x, p.z);
                for (int d = 0; d < 4; d++)
                {
                    float face = WallFaceFrom(at, d);
                    if (float.IsNaN(face)) continue;
                    float edge = d == 0 ? at.x - half.x : d == 1 ? at.x + half.x : d == 2 ? at.y - half.y : at.y + half.y;
                    float gap = Mathf.Abs(face - edge);
                    if (gap > 6f) continue;
                    Vector3 a = d < 2 ? W3(edge, at.y, 0.06f) : W3(at.x, edge, 0.06f), b = d < 2 ? W3(face, at.y, 0.06f) : W3(at.x, face, 0.06f);
                    Line(a, b, c);
                    var tick = d < 2 ? new Vector3(0, 0, 0.08f) : new Vector3(0.08f, 0, 0);
                    Line(a - tick, a + tick, c); Line(b - tick, b + tick, c);
                    if (gap > 0.02f) _measureLabels.Add(((a + b) * 0.5f + Vector3.up * 0.05f, $"{gap:0.00} m"));
                    else _measureLabels.Add(((a + b) * 0.5f + Vector3.up * 0.05f, "against the wall"));
                }
            }
            if (_tool == BuilderTool.Select && _selected.kind == SelKind.None && _dragHandle == Handle.None)
            {
                float w = v.width * C, dd = v.depth * C;
                var a = W3(0f, -0.9f, 0.03f); var b = W3(w, -0.9f, 0.03f);
                Line(a, b, c); Line(a + Vector3.back * 0.1f, a + Vector3.forward * 0.1f, c); Line(b + Vector3.back * 0.1f, b + Vector3.forward * 0.1f, c);
                _measureLabels.Add(((a + b) * 0.5f, $"{w:0.0} m"));
                var e = W3(-0.9f, 0f, 0.03f); var f = W3(-0.9f, dd, 0.03f);
                Line(e, f, c); Line(e + Vector3.left * 0.1f, e + Vector3.right * 0.1f, c); Line(f + Vector3.left * 0.1f, f + Vector3.right * 0.1f, c);
                _measureLabels.Add(((e + f) * 0.5f, $"{dd:0.0} m"));
            }
            if (_selected.kind == SelKind.Wall)
            {
                var wr = _selWall;
                var mid = wr.vertical ? W3(wr.line * C, (wr.a0 + wr.a1) * 0.5f * C, 0.3f) : W3((wr.a0 + wr.a1) * 0.5f * C, wr.line * C, 0.3f);
                _measureLabels.Add((mid, $"{(wr.a1 - wr.a0) * C:0.0} m"));
            }
        }

        void DrawMeasureLabels()
        {
            if (_showHelp || _modal != Modal.None) return;
            foreach (var (at, text) in _measureLabels) Label(at, text);
        }

        // ---------------- Small map ----------------

        bool _showMap = true;

        Rect MapRect
        {
            get
            {
                var v = V;
                float max = 210f, k = max / Mathf.Max(v.width, v.depth);
                float w = v.width * k, h = v.depth * k;
                return new Rect(LeftW + 14f, ImUi.Height - BottomH - 14f - h - 20f, w + 16f, h + 36f);
            }
        }

        bool OverMiniMap(Vector2 p) => _showMap && _screen == ScreenKind.Editor && _bp != null && !_walking && MapRect.Contains(p);

        void DrawMiniMap()
        {
            if (!_showMap || _showHelp || _modal != Modal.None) return;
            var v = V;
            var box = MapRect;
            ImUi.Fill(box, new Color(0.04f, 0.04f, 0.05f, 0.88f));
            GUI.Label(new Rect(box.x + 6, box.y + 1, box.width - 12, 18), "MAP  ·  click to look there", new GUIStyle(ImUi.Small) { fontSize = 11 });
            float k = (box.width - 16f) / v.width;
            var area = new Rect(box.x + 8f, box.y + 24f, v.width * k, v.depth * k);
            // Map space: +X right, +Z up (the plan as seen from above).
            Rect CellRect(int x, int z) => new Rect(area.x + x * k, area.yMax - (z + 1) * k, k, k);
            for (int z = 0; z < v.depth; z++)
            for (int x = 0; x < v.width; x++)
            {
                if (v.Outside(x, z)) { ImUi.Fill(CellRect(x, z), new Color(0.12f, 0.12f, 0.13f, 1f)); continue; }
                var c = RoomColor(v.RoomOf(x, z)); c = Color.Lerp(new Color(0.18f, 0.18f, 0.2f), c, 0.35f); c.a = 1f;
                ImUi.Fill(CellRect(x, z), c);
            }
            for (int pass = 0; pass < 2; pass++)
            {
                bool vert = pass == 0;
                for (int line = 0; line <= v.LineCount(vert); line++)
                for (int a = 0; a < v.AlongCount(vert); a++)
                {
                    int kind = EdgeCode.Kind(v.Edge(vert, line, a));
                    if (kind == EdgeCode.Open) continue;
                    var col = kind == EdgeCode.Wall ? new Color(0.92f, 0.88f, 0.75f) : kind == EdgeCode.Half || kind == EdgeCode.Railing ? new Color(0.5f, 0.85f, 1f) : new Color(0.45f, 0.95f, 0.5f);
                    float th = kind == EdgeCode.Wall ? 2f : 1f;
                    if (vert) ImUi.Fill(new Rect(area.x + line * k - th * 0.5f, area.yMax - (a + 1) * k, th, k), col);
                    else ImUi.Fill(new Rect(area.x + a * k, area.yMax - line * k - th * 0.5f, k, th), col);
                }
            }
            foreach (var o in v.openings)
            {
                var col = o.Door ? new Color(0.45f, 0.95f, 0.5f) : new Color(0.5f, 0.75f, 1f);
                if (o.vertical) ImUi.Fill(new Rect(area.x + o.line * k - 2f, area.yMax - o.to / C * k, 4f, o.Width / C * k), col);
                else ImUi.Fill(new Rect(area.x + o.from / C * k, area.yMax - o.line * k - 2f, o.Width / C * k, 4f), col);
            }
            foreach (var p in v.props) { var pp = new Vector2(area.x + p.x / C * k, area.yMax - p.z / C * k); ImUi.Fill(new Rect(pp.x - 2, pp.y - 2, 4, 4), new Color(0.55f, 0.8f, 1f)); }
            // Where the camera looks, and which way.
            var t = new Vector2(area.x + (_target.x - _origin.x) / C * k, area.yMax - (_target.z - _origin.y) / C * k);
            ImUi.Fill(new Rect(t.x - 3, t.y - 3, 6, 6), ImUi.Accent);
            var fwd = new Vector2(Mathf.Sin(_yaw * Mathf.Deg2Rad), -Mathf.Cos(_yaw * Mathf.Deg2Rad));
            for (int i = 1; i <= 6; i++) ImUi.Fill(new Rect(t.x + fwd.x * i * 3f - 1, t.y + fwd.y * i * 3f - 1, 2, 2), ImUi.Accent);
            var e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && box.Contains(e.mousePosition))
            {
                var lm = new Vector2((e.mousePosition.x - area.x) / k * C, (area.yMax - e.mousePosition.y) / k * C);
                _target = new Vector3(_origin.x + Mathf.Clamp(lm.x, 0f, v.width * C), _target.y, _origin.y + Mathf.Clamp(lm.y, 0f, v.depth * C));
                e.Use();
            }
        }

        // ---------------- Right-click menu ----------------

        bool _contextOpen;
        Vector2 _contextAt;                 // virtual pixels
        readonly List<(string label, Action act)> _contextItems = new List<(string, Action)>();
        string _contextTitle = "";
        Vector2 _rmbDown; float _rmbTime; bool _rmbMoved;

        Rect ContextRect => new Rect(Mathf.Min(_contextAt.x, ImUi.Width - 250f), Mathf.Min(_contextAt.y, ImUi.Height - 40f - 34f * (_contextItems.Count + 1)), 240f, 30f + 34f * _contextItems.Count);
        bool OverContextMenu(Vector2 p) => _contextOpen && ContextRect.Contains(p);

        void UpdateContextClick(Mouse mouse, bool overUi)
        {
            if (mouse.rightButton.wasPressedThisFrame) { _rmbDown = mouse.position.ReadValue(); _rmbTime = Time.unscaledTime; _rmbMoved = false; if (!OverContextMenu(ImUi.Pointer)) _contextOpen = false; }
            if (mouse.rightButton.isPressed && (mouse.position.ReadValue() - _rmbDown).magnitude > 6f) _rmbMoved = true;
            if (!mouse.rightButton.wasReleasedThisFrame || _rmbMoved || Time.unscaledTime - _rmbTime > 0.45f || overUi || _textFocus || _walking) return;
            OpenContextMenu(mouse.position.ReadValue());
        }

        void OpenContextMenu(Vector2 screen)
        {
            UpdateHover(screen);
            var pick = PickUnder(screen, out var wall);
            _contextItems.Clear();
            var v = V;
            void Add(string label, Action a) => _contextItems.Add((label, a));
            switch (pick.kind)
            {
                case SelKind.Prop:
                {
                    _selected = pick; _group.Clear();
                    var p = v.props[pick.index];
                    _contextTitle = PropName(p);
                    Add("DUPLICATE", DuplicateSelected);
                    if (!WallProp(p.kind)) Add("TURN 90°", () => { PushUndo(); p.yaw = Mathf.Repeat(p.yaw + 90f, 360f); Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick); });
                    if (!WallProp(p.kind) && p.kind != PropKind.FloorLamp) Add(p.pinned ? "PHYSICS ON" : "PHYSICS OFF (STUCK)", () => { PushUndo(); p.pinned = !p.pinned; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Place); });
                    if (!WallProp(p.kind)) Add("PUT ON CEILING", () => { PushUndo(); v.ToCeiling(p); Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Grow); });
                    Add("SELECT ALL LIKE IT", () =>
                    {
                        _group.Clear();
                        for (int i = 0; i < v.props.Count; i++) if (i != _selected.index && v.props[i].kind == p.kind && v.props[i].style == p.style && v.props[i].model == p.model) _group.Add(i);
                        SetStatus($"{_group.Count + 1} selected: drag one to move them all."); BuilderSounds.Play(BuilderSounds.Sfx.Pick, 1.15f);
                    });
                    Add("DELETE", DeleteSelected);
                    break;
                }
                case SelKind.Opening:
                {
                    _selected = pick; _dispFor = null;
                    var o = v.openings[pick.index];
                    _contextTitle = o.Door ? (o.arch ? "Arch" : "Door") : "Window";
                    Add("DUPLICATE", DuplicateSelected);
                    Add(o.Door ? "MAKE WINDOW" : "MAKE DOOR", () => { PushUndo(); o.sill = o.Door ? Mathf.Min(0.9f, o.top - 0.3f) : 0f; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Place); });
                    if (o.Door) Add(o.link != 0 ? "UNLINK" : "LINK TO ANOTHER DOOR…", () =>
                    {
                        if (o.link != 0) { PushUndo(); var other = v.openings.Find(q => q.id == o.link); if (other != null && other.link == o.id) other.link = 0; o.link = 0; Changed(); return; }
                        v.Validate(); SetMode(Modes[5]); SetTool(BuilderTool.Link); _linkFirst = o; SetStatus("Now click the door it leads to.");
                    });
                    Add("DELETE", DeleteSelected);
                    break;
                }
                case SelKind.Wall:
                {
                    _selWall = wall; _selected = pick;
                    int kind = EdgeCode.Kind(wall.vertical ? v.V(wall.line, wall.a0) : v.H(wall.a0, wall.line));
                    bool outer = v.PerimeterLine(wall.vertical, wall.line);
                    _contextTitle = outer ? "Outer wall" : kind == EdgeCode.Half ? "Half wall" : kind == EdgeCode.Railing ? "Railing" : "Wall";
                    if (kind == EdgeCode.Wall) { Add("+ DOOR HERE", () => AddOpeningOnWall(OpeningKind.Doorway)); Add("+ WINDOW HERE", () => AddOpeningOnWall(OpeningKind.Window)); }
                    Add("+ MARK ON IT…", () => { SetMode(Modes[4]); SetTool(BuilderTool.Decal); SetStatus("Pick a mark on the left, then click the wall."); });
                    if (!outer)
                    {
                        if (kind != EdgeCode.Wall) Add("MAKE IT A WALL", () => SetWallRun(EdgeCode.Wall));
                        if (kind != EdgeCode.Half) Add("MAKE IT A HALF WALL", () => SetWallRun(EdgeCode.Half));
                        Add("REMOVE", () => SetWallRun(EdgeCode.Open));
                    }
                    break;
                }
                case SelKind.Light:
                {
                    _selected = pick;
                    var f = v.fixtures[pick.index];
                    _contextTitle = "Ceiling light";
                    Add("WORKING", () => { PushUndo(); f.on = true; f.faulty = false; Changed(); });
                    Add("FLICKERS", () => { PushUndo(); f.on = true; f.faulty = true; Changed(); });
                    Add("DEAD", () => { PushUndo(); f.on = false; f.faulty = false; Changed(); });
                    Add(f.buzz ? "STOP THE BUZZ" : "MAKE IT BUZZ", () => { PushUndo(); f.buzz = !f.buzz; Changed(); });
                    Add("REMOVE", DeleteSelected);
                    break;
                }
                case SelKind.Pillar: _selected = pick; _contextTitle = "Pillar"; Add("REMOVE", DeleteSelected); break;
                case SelKind.Loot: _selected = pick; _contextTitle = "Loot spot"; Add("DUPLICATE", DuplicateSelected); Add("DELETE", DeleteSelected); break;
                case SelKind.Sound: _selected = pick; _contextTitle = "Sound"; Add("DELETE", DeleteSelected); break;
                case SelKind.Decal:
                {
                    _selected = pick;
                    var d = v.decals[pick.index];
                    _contextTitle = DecalNames[(int)d.kind].ToLowerInvariant();
                    Add("ANOTHER LOOK", () => { PushUndo(); d.seed = UnityEngine.Random.Range(1, 1 << 20); Changed(); });
                    Add("DELETE", DeleteSelected);
                    break;
                }
                case SelKind.Room:
                {
                    int area = pick.index; var cell = _hoverCell;
                    _contextTitle = v.rooms[area].name;
                    Add("ROOM SETTINGS", () => { SelectRoom(area); BuilderSounds.Play(BuilderSounds.Sfx.Pick); });
                    Add("WHEN YOU WALK IN… (TRIGGER)", () => { PushUndo(); v.triggers.Add(new TriggerDef { area = area }); SetMode(Modes[5]); SetTool(BuilderTool.Trigger); _selected = new Selection { kind = SelKind.Trigger, index = v.triggers.Count - 1 }; Changed(); });
                    Add("PUT A SOUND HERE", () => { PushUndo(); var m = Snap(_hoverMetres); v.sounds.Add(new SoundDef { x = m.x, z = m.y, kind = _soundKind }); _selected = new Selection { kind = SelKind.Sound, index = v.sounds.Count - 1 }; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Place); });
                    Add("CUT THIS CELL AWAY", () =>
                    {
                        int left = 0; foreach (int c in v.cellRoom) if (c >= 0) left++;
                        if (left <= 1) return;
                        PushUndo(); v.SetCell(cell.x, cell.y, -1); v.FixOutsideEdges(); Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Shrink);
                    });
                    Add("FURNISH FOR ME", FurnishForMe);
                    if (_areaClipboard != null) Add("PASTE ROOM HERE", () => { _hoverCell = cell; _hoverCellValid = true; if (_areaClipboard.Fits(v, cell)) { PushUndo(); if (_areaClipboard.Paste(v, cell, out int a)) { _activeRoom = a; Changed(); } } else SetStatus("It does not fit there."); });
                    break;
                }
                default: return;
            }
            _contextAt = ImUi.Pointer + new Vector2(6f, 6f);
            _contextOpen = true;
            BuilderSounds.Play(BuilderSounds.Sfx.Tick, 1.2f);
        }

        void DrawContextMenu()
        {
            if (!_contextOpen) return;
            var r = ContextRect;
            ImUi.Fill(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), ImUi.Accent);
            ImUi.Fill(r, ImUi.Panel);
            GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, 22), _contextTitle.ToUpperInvariant(), new GUIStyle(ImUi.Small) { fontStyle = FontStyle.Bold, normal = { textColor = ImUi.Accent } });
            for (int i = 0; i < _contextItems.Count; i++)
            {
                var b = new Rect(r.x + 4, r.y + 28 + i * 34, r.width - 8, 30);
                if (ImUi.Button(b, _contextItems[i].label)) { _contextOpen = false; var act = _contextItems[i].act; act(); return; }
            }
            var e = Event.current;
            if (e.type == EventType.MouseDown && !r.Contains(e.mousePosition)) { _contextOpen = false; }
        }

        // ---------------- Room tools in the overview ----------------

        string _newTag = "";

        void DrawRoomTools(ref ImUi.Column col)
        {
            ImUi.Header(col.Row(24), "Quick");
            var r = col.Row(34);
            if (ImUi.Button(new Rect(r.x, r.y, r.width * 0.5f - 3, r.height), "FURNISH FOR ME", false, "The world's own dressing: sofas against walls, armchairs, lamps in corners, boxes (racks and pallets in a warehouse). Ctrl+Z takes it back.")) FurnishForMe();
            if (ImUi.Button(new Rect(r.center.x + 3, r.y, r.width * 0.5f - 3, r.height), "MESS IT UP", false, "Push, turn and knock things over; some lights die or fail. Again for more."))
            { PushUndo(); V.Mess(_messAmount, UnityEngine.Random.Range(1, int.MaxValue)); Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Drop, 0.8f); }
            col.Space(4);
            ImUi.Header(col.Row(24), "Tags");
            if (_bp.tags.Count > 0)
            {
                float x = col.x, y = col.y;
                foreach (var t in new List<string>(_bp.tags))
                {
                    float w = Mathf.Min(col.w, 30f + t.Length * 8f);
                    if (x + w > col.x + col.w) { x = col.x; y += 32f; }
                    if (ImUi.Button(new Rect(x, y, w, 28), t + "  ×", false, "Click to take this tag off.")) { _bp.tags.Remove(t); _dirty = true; BuildSession.Dirty = true; }
                    x += w + 4f;
                }
                col.y = y + 34f;
            }
            var tr = col.Row(30);
            _newTag = ImUi.TextField(new Rect(tr.x, tr.y, tr.width - 70, tr.height), _newTag, "newtag", 24);
            if (string.IsNullOrEmpty(_newTag)) GUI.Label(new Rect(tr.x + 8, tr.y, tr.width - 80, tr.height), "a tag (scary, hallway…)", ImUi.Small);
            if (ImUi.Button(new Rect(tr.xMax - 66, tr.y, 66, tr.height), "ADD") && !string.IsNullOrWhiteSpace(_newTag))
            {
                string t = _newTag.Trim().ToLowerInvariant();
                if (!_bp.tags.Contains(t)) _bp.tags.Add(t);
                _newTag = ""; _dirty = true; BuildSession.Dirty = true; _clearFocus = true;
            }
        }

        /// <summary>FURNISH FOR ME: the room dressed by the world's own generator (DistrictGenerator.DressLikeTheWorld), kept as props.</summary>
        void FurnishForMe()
        {
            var v = V;
            var L = CustomRoomHarness.BuildLayout(_bp, _vi, 0, out var pl);
            var rooms = L.rooms.FindAll(r => r.custom);
            int before = L.props.Count;
            DistrictGenerator.DressLikeTheWorld(L, new System.Random(UnityEngine.Random.Range(1, int.MaxValue)), rooms);
            var origin = new Vector2(pl.rect.x, pl.rect.y) * C;
            var add = new List<PropDef>();
            for (int i = before; i < L.props.Count; i++)
            {
                var p = L.props[i];
                var local = p.position - origin;
                int cx = Mathf.FloorToInt(local.x / C), cz = Mathf.FloorToInt(local.y / C);
                if (!v.InBounds(cx, cz) || v.Outside(cx, cz)) continue;
                add.Add(new PropDef { kind = p.kind, x = local.x, z = local.y, yaw = p.yaw, size = p.size, variant = p.variant, lit = p.lit });
            }
            if (add.Count == 0) { SetStatus("Nothing found room to go: clear some space, or try again."); BuilderSounds.Play(BuilderSounds.Sfx.Error); return; }
            PushUndo();
            v.props.AddRange(add);
            Changed();
            BuilderSounds.Play(BuilderSounds.Sfx.Place);
            SetStatus($"{add.Count} things put in, the way the world furnishes its rooms. Again for more; Ctrl+Z takes them back.");
        }

        /// <summary>TRIGGERS: a click on a room's floor makes the selected trigger watch that room (or chooses it for the next).</summary>
        void TriggerFloorClick()
        {
            var v = V;
            if (!_hoverCellValid || v.Outside(_hoverCell.x, _hoverCell.y)) return;
            int area = v.RoomOf(_hoverCell.x, _hoverCell.y);
            if (_selected.kind == SelKind.Trigger && _selected.index < v.triggers.Count)
            {
                PushUndo(); v.triggers[_selected.index].area = area; Changed();
                SetStatus($"The trigger now watches '{v.rooms[area].name}'.");
            }
            else { _activeRoom = area; SetStatus($"'{v.rooms[area].name}' chosen: + NEW TRIGGER makes one for it."); }
            BuilderSounds.Play(BuilderSounds.Sfx.Pick);
        }
    }
}
