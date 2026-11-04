using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Build mode, step D (round 17): checks that help fix what they find. No way in: ADD A DOOR puts one in an outer wall.
    /// No working lights: ADD LIGHTS (one every two cells). Places you cannot walk to from a way in (walls, half walls and
    /// windows block; doors and open floor do not): SHOW flashes them. A door with a prop in front of it: SHOW selects it.
    /// </summary>
    public partial class BuilderApp
    {
        readonly List<Vector2Int> _flashCells = new List<Vector2Int>();
        float _flashUntil;

        /// <summary>Cells that cannot be reached on foot from any way in (empty if there is no way in at all).</summary>
        List<Vector2Int> UnreachableCells()
        {
            var v = V;
            var seen = new bool[v.width, v.depth];
            var queue = new Queue<Vector2Int>();
            void Seed(int x, int z) { if (v.InBounds(x, z) && !seen[x, z]) { seen[x, z] = true; queue.Enqueue(new Vector2Int(x, z)); } }
            // Ways in: grid gaps in the outer wall, doors in it.
            for (int z = 0; z < v.depth; z++) { if (EdgeCode.Walkthrough(v.V(0, z))) Seed(0, z); if (EdgeCode.Walkthrough(v.V(v.width, z))) Seed(v.width - 1, z); }
            for (int x = 0; x < v.width; x++) { if (EdgeCode.Walkthrough(v.H(x, 0))) Seed(x, 0); if (EdgeCode.Walkthrough(v.H(x, v.depth))) Seed(x, v.depth - 1); }
            foreach (var o in v.openings)
            {
                if (!o.Door || !v.PerimeterLine(o.vertical, o.line)) continue;
                int a = Mathf.Clamp(Mathf.FloorToInt(o.Centre / C), 0, (o.vertical ? v.depth : v.width) - 1);
                if (o.vertical) Seed(o.line == 0 ? 0 : v.width - 1, a); else Seed(a, o.line == 0 ? 0 : v.depth - 1);
            }
            if (queue.Count == 0) return new List<Vector2Int>();
            bool Passable(bool vertical, int line, int along)
            {
                int k = EdgeCode.Kind(vertical ? v.V(line, along) : v.H(along, line));
                if (EdgeCode.Walkthrough(vertical ? v.V(line, along) : v.H(along, line))) return true;
                if (k != EdgeCode.Wall) return false;
                // A door of its own size over at least 0.5 m of this edge.
                foreach (var o in v.openings)
                    if (o.Door && o.vertical == vertical && o.line == line && Mathf.Min(o.to, (along + 1) * C) - Mathf.Max(o.from, along * C) >= 0.5f) return true;
                return false;
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (c.x > 0 && Passable(true, c.x, c.y)) Seed(c.x - 1, c.y);
                if (c.x < v.width - 1 && Passable(true, c.x + 1, c.y)) Seed(c.x + 1, c.y);
                if (c.y > 0 && Passable(false, c.y, c.x)) Seed(c.x, c.y - 1);
                if (c.y < v.depth - 1 && Passable(false, c.y + 1, c.x)) Seed(c.x, c.y + 1);
            }
            var lost = new List<Vector2Int>();
            for (int x = 0; x < v.width; x++) for (int z = 0; z < v.depth; z++) if (!seen[x, z]) lost.Add(new Vector2Int(x, z));
            return lost;
        }

        /// <summary>Props standing in front of a door (within 0.6 m of it, across its width), as indices.</summary>
        List<int> PropsBlockingDoors()
        {
            var v = V; var list = new List<int>();
            foreach (var o in v.openings)
            {
                if (!o.Door) continue;
                for (int i = 0; i < v.props.Count; i++)
                {
                    var p = v.props[i];
                    if (list.Contains(i) || p.kind == PropKind.WallClock || p.elevation > 1.8f) continue;
                    float along = o.vertical ? p.z : p.x, across = Mathf.Abs((o.vertical ? p.x : p.z) - o.line * C);
                    float half = Mathf.Max(p.size.x, p.size.z) * 0.5f * (p.scale > 0.01f ? p.scale : 1f);
                    if (along > o.from - half * 0.6f && along < o.to + half * 0.6f && across < 0.6f + half * 0.6f) list.Add(i);
                }
            }
            return list;
        }

        /// <summary>A door in an outer wall: the middle of the south wall, else the first place one fits.</summary>
        void AddEntrance()
        {
            var v = V;
            foreach (var (vertical, line) in new[] { (false, 0), (false, v.depth), (true, 0), (true, v.width) })
            {
                float len = v.LineLength(vertical);
                for (int k = 0; k <= 10; k++)
                    foreach (int sign in new[] { 1, -1 })
                    {
                        float c = len * 0.5f + sign * k * 0.6f;
                        var o = new OpeningDef { vertical = vertical, line = line, from = c - 0.45f, to = c + 0.45f, top = 2.1f };
                        v.ClampOpening(o);
                        if (!v.OpeningFits(o)) continue;
                        PushUndo(); v.openings.Add(o);
                        _selected = new Selection { kind = SelKind.Opening, index = v.openings.Count - 1 }; _dispFor = null;
                        BuilderSounds.Play(BuilderSounds.Sfx.Place); Changed();
                        SetStatus("A door in the outer wall: the way in. Drag it where you want it.");
                        return;
                    }
            }
            SetStatus("No outer wall has room for a door."); BuilderSounds.Play(BuilderSounds.Sfx.Error);
        }

        void DrawChecks(ref ImUi.Column col)
        {
            var v = V;
            ImUi.Header(col.Row(24), "Checks");
            void Row(ref ImUi.Column c, bool ok, string text, string fix, System.Action act)
            {
                var r = c.Row(26);
                ImUi.Fill(new Rect(r.x, r.y + 8, 10, 10), ok ? ImUi.Good : ImUi.Bad);
                GUI.Label(new Rect(r.x + 14, r.y, r.width - 14 - (fix != null ? 104 : 0), r.height), text, new GUIStyle(ImUi.Label) { fontSize = 14 });
                if (fix != null && ImUi.Button(new Rect(r.xMax - 100, r.y, 100, r.height), fix)) act();
            }
            int exits = v.CountExits();
            Row(ref col, exits > 0, exits > 0 ? $"{exits} way{(exits == 1 ? "" : "s")} in" : "No way in", exits > 0 ? null : "ADD DOOR", AddEntrance);
            int lights = v.fixtures.FindAll(f => f.on).Count;
            Row(ref col, lights > 0, lights > 0 ? $"{lights} working lights" : "No working lights", lights > 0 ? null : "ADD LIGHTS", () => { PushUndo(); v.AutoFixtures(); Changed(); BuilderSounds.Play(BuilderSounds.Sfx.Place); });
            if (exits > 0)
            {
                var lost = UnreachableCells();
                Row(ref col, lost.Count == 0, lost.Count == 0 ? "Every cell can be walked to" : $"{lost.Count} cells can't be reached", lost.Count == 0 ? null : "SHOW", () =>
                {
                    _flashCells.Clear(); _flashCells.AddRange(lost); _flashUntil = Time.unscaledTime + 3f; BuilderSounds.Play(BuilderSounds.Sfx.Error);
                    SetStatus("Those cells (flashing red) are walled off from every way in: open a wall or put a door in.");
                });
            }
            var blocked = PropsBlockingDoors();
            Row(ref col, blocked.Count == 0, blocked.Count == 0 ? "No doors blocked" : $"{blocked.Count} prop{(blocked.Count == 1 ? "" : "s")} in front of a door", blocked.Count == 0 ? null : "SHOW", () =>
            {
                _selected = new Selection { kind = SelKind.Prop, index = blocked[0] }; _group.Clear();
                for (int k = 1; k < blocked.Count; k++) _group.Add(blocked[k]);
                SetMode(Modes[0]); BuilderSounds.Play(BuilderSounds.Sfx.Pick);
            });
            ImUi.Dimmed(col.Row(22), $"{v.openings.Count} doors and windows, {v.props.Count} props, {v.loot.Count} loot spots, {v.pillars.Count} pillars");
        }

        /// <summary>The cells a check is showing, flashing red (GL quads).</summary>
        void DrawFlash()
        {
            if (Time.unscaledTime > _flashUntil || _flashCells.Count == 0) return;
            float a = 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 12f);
            var col = new Color(1f, 0.2f, 0.15f, a);
            foreach (var c in _flashCells)
                if (V.InBounds(c.x, c.y)) Quad(W3(c.x * C, c.y * C, 0.07f), W3((c.x + 1) * C, c.y * C, 0.07f), W3((c.x + 1) * C, (c.y + 1) * C, 0.07f), W3(c.x * C, (c.y + 1) * C, 0.07f), col);
        }
    }
}
