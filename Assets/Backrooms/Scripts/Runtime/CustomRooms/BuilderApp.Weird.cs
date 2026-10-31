using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Build mode's Backrooms touches (round 18): WEIRD mode (triggers: walk in and the lights cut out, flicker, a door slams,
    /// a sound; linked doorways: walk through one, come out of the other; a mess made of the room; furniture on the ceiling),
    /// PAINT's wall marks (stains, damp, mould, peeling paper, notices, handprints, scorch, outlets, switches) and FURNISH's
    /// sound spots (a hum, a buzz, a drip, static, knocking, air). Light moods and buzzing are in the light's inspector.
    /// </summary>
    public partial class BuilderApp
    {
        sealed class WeirdMode : Mode
        {
            public WeirdMode(BuilderApp app) : base(app) { }
            public override string Name => "WEIRD";
            public override int Icon => 9;
            public override string Tip => "Things that should not happen: triggers, linked doors, a mess, furniture on the ceiling.";
            public override BuilderTool[] Tools => new[] { BuilderTool.Trigger, BuilderTool.Link };
            public override string ToolName(BuilderTool t) => t == BuilderTool.Trigger ? "TRIGGERS" : "LINK DOORS";
            public override string ToolTip(BuilderTool t) => t == BuilderTool.Trigger
                ? "When the player walks into a room: lights out, a flicker, a door slams, a sound."
                : "Click a door, then another: walk through one and you come out of the other. Put them at both ends of a hallway and it never ends.";
            public override void Options(ref ImUi.Column col)
            {
                var v = app.V;
                if (app._tool == BuilderTool.Trigger)
                {
                    ImUi.Header(col.Row(24), "Triggers");
                    for (int i = 0; i < v.triggers.Count; i++)
                    {
                        var t = v.triggers[i];
                        string area = t.area >= 0 && t.area < v.rooms.Count ? v.rooms[t.area].name : "?";
                        bool sel = app._selected.kind == SelKind.Trigger && app._selected.index == i;
                        if (ImUi.Button(col.Row(30), $"{area}: {TriggerNames[(int)t.action]}", sel)) { app._selected = new Selection { kind = SelKind.Trigger, index = i }; BuilderSounds.Play(BuilderSounds.Sfx.Pick); }
                    }
                    if (ImUi.Button(col.Row(32), "+ NEW TRIGGER", false, "A trigger on the room you chose (PAINT or a click on its floor)."))
                    {
                        app.PushUndo();
                        v.triggers.Add(new TriggerDef { area = Mathf.Clamp(app._activeRoom, 0, v.rooms.Count - 1), action = TriggerAction.LightsOut });
                        app._selected = new Selection { kind = SelKind.Trigger, index = v.triggers.Count - 1 };
                        app.Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Place);
                    }
                }
                else
                {
                    ImUi.Dimmed(col.Row(96), app._linkFirst != null
                        ? "Now click the door it leads to. Esc: cancel."
                        : "Click a door (a doorway reaching the floor), then a second one. Clicking a linked door takes its link away. Linked doors show a purple line.");
                }
                col.Space(10);
                ImUi.Header(col.Row(24), "Mess");
                app._messAmount = ImUi.Slider(col.Row(), "Amount", app._messAmount, 0.1f, 1f, "0.0", "How far things are pushed, turned and knocked over.");
                if (ImUi.Button(col.Row(34), "MESS IT UP", false, "Pushes, turns and knocks over the props (stuck ones stay), kills or breaks some lights. Ctrl+Z undoes it."))
                {
                    app.PushUndo();
                    int n = v.Mess(app._messAmount, Random.Range(1, int.MaxValue));
                    app.Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Drop, 0.8f);
                    app.SetStatus(n == 0 ? "Nothing to mess up: put some furniture in first (FURNISH)." : $"{n} things pushed about. Again for more; Ctrl+Z to put them back.");
                }
                ImUi.Dimmed(col.Row(54), "Furniture on the ceiling: select a prop and press PUT ON CEILING (or right-click it).");
            }
            public override string Hint() => app._tool == BuilderTool.Trigger
                ? "Pick a trigger on the left (or make one); set what it does on the right."
                : app._linkFirst != null ? "Click the door it leads to." : "Click a door to link it to another.";
        }

        static readonly string[] TriggerNames = { "LIGHTS OUT", "FLICKER", "DOOR SLAMS", "SOUND", "LIGHTS ON" };
        static readonly string[] SoundNames = { "HUM", "BUZZ", "DRIP", "STATIC", "KNOCKING", "AIR" };
        static readonly string[] DecalNames = { "WATER STAIN", "DAMP", "MOULD", "PEELING PAPER", "NOTICE", "HANDPRINTS", "SCORCH", "OUTLET", "SWITCH" };
        static readonly string[] MoodNames = { "AS MADE", "DIM", "SICKLY", "YELLOWED", "COLD", "PINK" };

        float _messAmount = 0.5f;
        OpeningDef _linkFirst;
        DecalKind _decalKind = DecalKind.WaterStain;
        float _decalSize = 0.35f;
        SoundKind _soundKind = SoundKind.Hum;
        bool _draggingDecal;

        // ---------------- Pointing at things on walls ----------------

        /// <summary>The door or window the pointer is on (through its hole or the wall round it), without selecting it.</summary>
        int OpeningUnder(Vector2 screen)
        {
            if (!RayWall(PointerRay(screen), false, out bool vert, out int line, out float along, out float height)) return -1;
            var v = V;
            for (int i = 0; i < v.openings.Count; i++)
            {
                var o = v.openings[i];
                if (o.vertical != vert || o.line != line) continue;
                if (along < o.from - 0.08f || along > o.to + 0.08f || height > o.top + 0.15f) continue;
                return i;
            }
            return -1;
        }

        /// <summary>The face of a wall the pointer is on, seen from the camera's side: line, along, height and side.</summary>
        bool WallFaceUnder(Vector2 screen, out bool vertical, out int line, out float along, out float height, out int side)
        {
            side = 1;
            if (!RayWall(PointerRay(screen), true, out vertical, out line, out along, out height)) return false;
            float cam = vertical ? _cam.transform.position.x - _origin.x : _cam.transform.position.z - _origin.y;
            side = cam >= line * C ? 1 : -1;
            return true;
        }

        int DecalUnder(Vector2 screen)
        {
            if (!WallFaceUnder(screen, out bool vert, out int line, out float along, out float height, out int side)) return -1;
            var v = V; int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < v.decals.Count; i++)
            {
                var d = v.decals[i];
                if (d.vertical != vert || d.line != line || d.side != side) continue;
                float r = Mathf.Max(0.12f, d.size * 1.2f), dist = new Vector2(along - d.along, height - d.height).magnitude;
                if (dist < r && dist < bd) { bd = dist; best = i; }
            }
            return best;
        }

        // ---------------- Placing ----------------

        void PlaceDecal(Vector2 screen)
        {
            int existing = DecalUnder(screen);
            if (existing >= 0) { _selected = new Selection { kind = SelKind.Decal, index = existing }; PushUndo(); _draggingDecal = true; BuilderSounds.Play(BuilderSounds.Sfx.Pick); return; }
            if (!WallFaceUnder(screen, out bool vert, out int line, out float along, out float height, out int side)) { SetStatus("Point at a wall."); return; }
            var v = V;
            int cell = Mathf.Clamp(Mathf.FloorToInt(along / C), 0, v.AlongCount(vert) - 1);
            if (!v.InsideAt(vert, line, cell, side)) { SetStatus("Marks go on the room's own side of a wall: look at it from inside."); BuilderSounds.Play(BuilderSounds.Sfx.Error); return; }
            PushUndo();
            float size = _decalKind == DecalKind.Outlet || _decalKind == DecalKind.Switch ? 0.06f : _decalSize;
            float h = _decalKind == DecalKind.Outlet ? 0.3f : _decalKind == DecalKind.Switch ? 1.2f : Mathf.Clamp(height, size, 2.6f);
            v.decals.Add(new DecalDef { vertical = vert, line = line, side = side, along = along, height = h, size = size, kind = _decalKind, seed = Random.Range(1, 1 << 20) });
            _selected = new Selection { kind = SelKind.Decal, index = v.decals.Count - 1 };
            _draggingDecal = true;
            BuilderSounds.Play(BuilderSounds.Sfx.Place, 1.15f);
            Changed();
        }

        void DragDecal(Vector2 screen)
        {
            var v = V;
            if (!_draggingDecal || _selected.kind != SelKind.Decal || _selected.index >= v.decals.Count) return;
            var d = v.decals[_selected.index];
            if (!WallFaceUnder(screen, out bool vert, out int line, out float along, out float height, out int side)) return;
            int cell = Mathf.Clamp(Mathf.FloorToInt(along / C), 0, v.AlongCount(vert) - 1);
            if (!v.InsideAt(vert, line, cell, side)) return;
            float a = SnapLen(along), h = d.kind == DecalKind.Outlet || d.kind == DecalKind.Switch ? d.height : Mathf.Clamp(SnapLen(height), d.size, 2.8f);
            if (d.vertical == vert && d.line == line && d.side == side && Mathf.Approximately(a, d.along) && Mathf.Approximately(h, d.height)) return;
            d.vertical = vert; d.line = line; d.side = side; d.along = a; d.height = h;
            _strokeChanged = true; RequestRebuild();
            BuilderSounds.Play(BuilderSounds.Sfx.Tick, 1f, 0.5f);
        }

        void PlaceSound()
        {
            if (!_hoverGround) return;
            var m = Snap(_hoverMetres);
            if (!InsideRoom(m)) { SetStatus("Put sounds inside the room."); return; }
            PushUndo();
            V.sounds.Add(new SoundDef { x = m.x, z = m.y, kind = _soundKind });
            _selected = new Selection { kind = SelKind.Sound, index = V.sounds.Count - 1 };
            _tool = BuilderTool.Select;
            BuilderSounds.Play(BuilderSounds.Sfx.Place, 1.2f);
            Changed();
        }

        void LinkClick(Vector2 screen)
        {
            int i = OpeningUnder(screen);
            var v = V;
            if (i < 0 || !v.openings[i].Door) { SetStatus("Click a doorway (one that reaches the floor)."); BuilderSounds.Play(BuilderSounds.Sfx.Error); return; }
            v.Validate();                               // (ids given)
            var o = v.openings[i];
            if (_linkFirst == null)
            {
                if (o.link != 0)
                {
                    PushUndo();
                    var other = v.openings.Find(q => q.id == o.link);
                    if (other != null && other.link == o.id) other.link = 0;
                    o.link = 0; Changed();
                    SetStatus("Link taken away."); BuilderSounds.Play(BuilderSounds.Sfx.Delete);
                    return;
                }
                _linkFirst = o;
                SetStatus("Now click the door it leads to.");
                BuilderSounds.Play(BuilderSounds.Sfx.Pick);
                return;
            }
            if (o == _linkFirst) { _linkFirst = null; SetStatus("Cancelled."); return; }
            PushUndo();
            foreach (var q in v.openings) if (q.link == o.id || q.link == _linkFirst.id) q.link = 0;
            _linkFirst.link = o.id; o.link = _linkFirst.id;
            _linkFirst = null;
            BuilderSounds.Play(BuilderSounds.Sfx.Grow);
            SetStatus("Linked: walk through one and you come out of the other. TEST IN WORLD or WALK to try it. FLIP on the door turns you round.", 8f);
            Changed();
        }

        // ---------------- Inspectors ----------------

        bool DrawExtraInspector(ref ImUi.Column col)
        {
            var v = V;
            if (_selected.kind == SelKind.Decal && _selected.index < v.decals.Count)
            {
                var d = v.decals[_selected.index];
                ImUi.Header(col.Row(24), "Mark on the wall");
                ImUi.Dimmed(col.Row(22), "Drag it along the wall in WALL MARKS (PAINT).");
                for (int i = 0; i < DecalNames.Length; i += 2)
                {
                    var r = col.Row(28);
                    for (int k = 0; k < 2 && i + k < DecalNames.Length; k++)
                        if (ImUi.Button(new Rect(r.x + k * r.width * 0.5f, r.y, r.width * 0.5f - 3, r.height), DecalNames[i + k], (int)d.kind == i + k))
                        { PushUndo(); d.kind = (DecalKind)(i + k); Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick); }
                }
                if (d.kind != DecalKind.Outlet && d.kind != DecalKind.Switch)
                {
                    float s = Slide(col.Row(), "Size (m)", d.size, 0.08f, 1.2f, "0.00");
                    float h = Slide(col.Row(), "Height (m)", d.height, 0.05f, 2.8f, "0.00");
                    if (Mathf.Abs(s - d.size) > 0.004f || Mathf.Abs(h - d.height) > 0.004f) { d.size = s; d.height = h; _dirty = true; BuildSession.Dirty = true; RequestRebuild(); }
                    if (ImUi.Button(col.Row(), "ANOTHER LOOK", false, "The same kind of mark, drawn differently.")) { PushUndo(); d.seed = Random.Range(1, 1 << 20); Changed(); }
                }
                if (ImUi.Button(col.Row(), "DELETE  [Del]")) DeleteSelected();
                return true;
            }
            if (_selected.kind == SelKind.Sound && _selected.index < v.sounds.Count)
            {
                var s = v.sounds[_selected.index];
                ImUi.Header(col.Row(24), "Sound");
                for (int i = 0; i < SoundNames.Length; i += 3)
                {
                    var r = col.Row(28);
                    for (int k = 0; k < 3 && i + k < SoundNames.Length; k++)
                        if (ImUi.Button(new Rect(r.x + k * r.width / 3f, r.y, r.width / 3f - 3, r.height), SoundNames[i + k], (int)s.kind == i + k))
                        { PushUndo(); s.kind = (SoundKind)(i + k); Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick); }
                }
                float vol = Slide(col.Row(), "Loudness", s.volume, 0.05f, 1f, "0.00");
                float range = Slide(col.Row(), "Heard to (m)", s.range, 2f, 30f, "0");
                float y = Slide(col.Row(), "Height (m)", s.y, 0f, 5f, "0.0");
                if (Mathf.Abs(vol - s.volume) > 0.004f || Mathf.Abs(range - s.range) > 0.05f || Mathf.Abs(y - s.y) > 0.01f) { s.volume = vol; s.range = range; s.y = y; _dirty = true; BuildSession.Dirty = true; RequestRebuild(); }
                ImUi.Dimmed(col.Row(40), "WALK (V) or TEST IN WORLD to hear it; it plays all the time.");
                if (ImUi.Button(col.Row(), "DELETE  [Del]")) DeleteSelected();
                return true;
            }
            if (_selected.kind == SelKind.Trigger && _selected.index < v.triggers.Count)
            {
                var t = v.triggers[_selected.index];
                ImUi.Header(col.Row(24), "Trigger");
                string area = t.area >= 0 && t.area < v.rooms.Count ? v.rooms[t.area].name : "?";
                if (ImUi.Button(col.Row(), "WHEN YOU WALK INTO: " + area, false, "Click to choose the next room (an area of cells; PAINT makes them)."))
                { PushUndo(); t.area = (t.area + 1) % v.rooms.Count; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick); }
                ImUi.Text_(col.Row(22), "Then:");
                for (int i = 0; i < TriggerNames.Length; i += 2)
                {
                    var r = col.Row(28);
                    for (int k = 0; k < 2 && i + k < TriggerNames.Length; k++)
                        if (ImUi.Button(new Rect(r.x + k * r.width * 0.5f, r.y, r.width * 0.5f - 3, r.height), TriggerNames[i + k], (int)t.action == i + k))
                        { PushUndo(); t.action = (TriggerAction)(i + k); Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick); }
                }
                if (t.action == TriggerAction.DoorSlam)
                {
                    var doors = v.openings.FindAll(o => o.Door);
                    int at = doors.FindIndex(o => o.id == t.door);
                    string label = at >= 0 ? $"door {at + 1} of {doors.Count} ({doors[at].Width:0.0} m)" : doors.Count == 0 ? "no doors yet" : "choose one";
                    if (ImUi.Button(col.Row(), "WHICH DOOR: " + label, false, "Click for the next door. It hangs open in its doorway and slams shut.") && doors.Count > 0)
                    { v.Validate(); PushUndo(); t.door = doors[(at + 1) % doors.Count].id; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick); }
                }
                if (t.action == TriggerAction.Sound)
                {
                    var r = col.Row(28);
                    for (int k = 0; k < SoundNames.Length; k++)
                    {
                        float w = r.width / 3f; var b = new Rect(r.x + (k % 3) * w, r.y + (k / 3) * 32, w - 3, r.height);
                        if (ImUi.Button(b, SoundNames[k], (int)t.sound == k)) { PushUndo(); t.sound = (SoundKind)k; Changed(); }
                    }
                    col.Space(32);
                }
                bool once = t.once; Toggled(ref once, col.Row(), "Only the first time");
                if (once != t.once) { t.once = once; }
                float delay = Slide(col.Row(), "After (s)", t.delay, 0f, 10f, "0.0", "How long after you walk in.");
                if (Mathf.Abs(delay - t.delay) > 0.01f) { t.delay = Mathf.Round(delay * 10f) / 10f; _dirty = true; BuildSession.Dirty = true; }
                ImUi.Dimmed(col.Row(40), "WALK (V) to try it here, or TEST IN WORLD.");
                if (ImUi.Button(col.Row(), "DELETE  [Del]")) DeleteSelected();
                return true;
            }
            return false;
        }

        /// <summary>The light inspector's moods and buzz (called from DrawPickInspector).</summary>
        void DrawLightMood(ref ImUi.Column col, FixtureDef f)
        {
            ImUi.Text_(col.Row(22), "Mood");
            for (int i = 0; i < MoodNames.Length; i += 3)
            {
                var r = col.Row(28);
                for (int k = 0; k < 3 && i + k < MoodNames.Length; k++)
                    if (ImUi.Button(new Rect(r.x + k * r.width / 3f, r.y, r.width / 3f - 3, r.height), MoodNames[i + k], f.mood == i + k, i + k == 0 ? "As the world makes it." : "Its tubes aged this way: less light, a different colour."))
                    { PushUndo(); f.mood = i + k; Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.9f + k * 0.1f); }
            }
            bool buzz = f.buzz;
            Toggled(ref buzz, col.Row(), "It buzzes", "A fluorescent buzz you hear near it (louder if it flickers).");
            if (buzz != f.buzz) { PushUndo(); f.buzz = buzz; Changed(); }
        }

        /// <summary>The opening inspector's link (called from DrawSelected).</summary>
        void DrawOpeningLink(ref ImUi.Column col, OpeningDef so)
        {
            if (!so.Door) return;
            var v = V;
            var other = so.link != 0 ? v.openings.Find(q => q.id == so.link) : null;
            if (other == null) { ImUi.Dimmed(col.Row(34), "Not linked. WEIRD (6) > LINK DOORS links it to another door."); return; }
            ImUi.Dimmed(col.Row(22), $"Linked to a {other.Width:0.0} m door: walk through, come out there.");
            var r = col.Row();
            bool flip = so.flip;
            if (ImUi.Button(new Rect(r.x, r.y, r.width * 0.5f - 3, r.height), flip ? "FLIPPED" : "FLIP", flip, "Come out of the other door facing the other way.")) { PushUndo(); so.flip = !flip; other.flip = so.flip; Changed(); }
            if (ImUi.Button(new Rect(r.center.x + 3, r.y, r.width * 0.5f - 3, r.height), "UNLINK")) { PushUndo(); so.link = 0; if (other.link == so.id) other.link = 0; Changed(); }
        }

        // ---------------- Gizmos ----------------

        void DrawExtraGizmos()
        {
            var v = V;
            var hot = ImUi.Accent;
            // Marks: a little square on their wall face.
            for (int i = 0; i < v.decals.Count; i++)
            {
                var d = v.decals[i];
                bool sel = _selected.kind == SelKind.Decal && _selected.index == i;
                if (!sel && !(CurrentMode is PaintMode && _tool == BuilderTool.Decal)) continue;
                float plane = d.line * C + d.side * 0.08f;
                Vector3 P(float a, float y) => d.vertical ? W3(plane, d.along + a, y) : W3(d.along + a, plane, y);
                float s = Mathf.Max(0.06f, d.size);
                var c = sel ? hot : new Color(1f, 1f, 1f, 0.5f);
                Line(P(-s, d.height - s), P(s, d.height - s), c); Line(P(s, d.height - s), P(s, d.height + s), c);
                Line(P(s, d.height + s), P(-s, d.height + s), c); Line(P(-s, d.height + s), P(-s, d.height - s), c);
            }
            // Sounds: a ring with a stem.
            for (int i = 0; i < v.sounds.Count; i++)
            {
                var s = v.sounds[i];
                bool sel = _selected.kind == SelKind.Sound && _selected.index == i;
                var c = sel ? hot : new Color(0.8f, 0.55f, 1f, 0.85f);
                var p = W3(s.x, s.z, s.y);
                for (int k = 0; k < 16; k++)
                {
                    float a0 = k / 16f * Mathf.PI * 2f, a1 = (k + 1) / 16f * Mathf.PI * 2f;
                    Line(p + new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * 0.22f, p + new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * 0.22f, c);
                }
                Line(p, W3(s.x, s.z, 0.03f), new Color(c.r, c.g, c.b, 0.4f));
                if (sel)
                    for (int k = 0; k < 32; k++)
                    {
                        float a0 = k / 32f * Mathf.PI * 2f, a1 = (k + 1) / 32f * Mathf.PI * 2f;
                        Line(W3(s.x, s.z, 0.04f) + new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * s.range, W3(s.x, s.z, 0.04f) + new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * s.range, new Color(0.8f, 0.55f, 1f, 0.35f));
                    }
            }
            // A trigger's room, outlined.
            if (_selected.kind == SelKind.Trigger && _selected.index < v.triggers.Count)
            {
                int area = v.triggers[_selected.index].area;
                var c = new Color(0.8f, 0.4f, 1f, 1f);
                for (int z = 0; z < v.depth; z++)
                for (int x = 0; x < v.width; x++)
                {
                    if (v.RoomOf(x, z) != area) continue;
                    if (x == 0 || v.RoomOf(x - 1, z) != area) Thick(W3(x * C, z * C, 0.07f), W3(x * C, (z + 1) * C, 0.07f), c);
                    if (x == v.width - 1 || v.RoomOf(x + 1, z) != area) Thick(W3((x + 1) * C, z * C, 0.07f), W3((x + 1) * C, (z + 1) * C, 0.07f), c);
                    if (z == 0 || v.RoomOf(x, z - 1) != area) Thick(W3(x * C, z * C, 0.07f), W3((x + 1) * C, z * C, 0.07f), c);
                    if (z == v.depth - 1 || v.RoomOf(x, z + 1) != area) Thick(W3(x * C, (z + 1) * C, 0.07f), W3((x + 1) * C, (z + 1) * C, 0.07f), c);
                }
            }
            // Linked doors: a purple line between them; the first of a link being made, outlined.
            var purple = new Color(0.85f, 0.45f, 1f, 0.95f);
            foreach (var o in v.openings)
            {
                if (o.link == 0 || o.link < o.id) continue;
                var other = v.openings.Find(q => q.id == o.link);
                if (other == null) continue;
                var a = OpeningPoint(o, o.Centre, 1.1f); var b = OpeningPoint(other, other.Centre, 1.1f);
                for (int k = 0; k < 20; k += 2) Thick(Vector3.Lerp(a, b, k / 20f) + Vector3.up * Mathf.Sin(k / 20f * Mathf.PI) * 0.8f, Vector3.Lerp(a, b, (k + 1) / 20f) + Vector3.up * Mathf.Sin((k + 1) / 20f * Mathf.PI) * 0.8f, purple);
            }
            if (_linkFirst != null) OpeningOutline(_linkFirst, _linkFirst.from, _linkFirst.to, _linkFirst.sill, _linkFirst.top, purple, true);
            // Cut-away cells: crossed out.
            for (int z = 0; z < v.depth; z++)
            for (int x = 0; x < v.width; x++)
            {
                if (!v.Outside(x, z)) continue;
                var c = new Color(0.55f, 0.55f, 0.6f, 0.6f);
                Line(W3(x * C, z * C, 0.05f), W3((x + 1) * C, (z + 1) * C, 0.05f), c);
                Line(W3((x + 1) * C, z * C, 0.05f), W3(x * C, (z + 1) * C, 0.05f), c);
            }
        }
    }
}
