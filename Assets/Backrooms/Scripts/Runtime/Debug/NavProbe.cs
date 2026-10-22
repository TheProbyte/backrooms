using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Automated collision test. Walks the player along BFS paths through the generated layout (across
    /// chunk borders) and reports two kinds of bug:
    ///  - WALKTHROUGH: the player crossed a cell edge that the layout says is a solid wall.
    ///  - BARRIER: the player stalled on a path the layout says is open.
    /// Add the component in play mode (or call Run) and read <see cref="report"/>.
    /// </summary>
    public class NavProbe : MonoBehaviour
    {
        public int trips = 6;
        public int tripCells = 40;
        public int seedOffset = 1;

        public readonly List<string> report = new List<string>();
        public bool Done { get; private set; }
        public int Walked { get; private set; }

        WorldStreamer _ws;
        CamcorderPlayerController _player;
        readonly Dictionary<Vector2Int, OfficeLayout> _layouts = new Dictionary<Vector2Int, OfficeLayout>();
        readonly Dictionary<Vector2Int, HashSet<Vector2Int>> _blocked = new Dictionary<Vector2Int, HashSet<Vector2Int>>();
        List<Vector2Int> _path;
        int _index, _trip;
        Vector2Int _lastCell;
        float _stallTimer;
        Vector3 _stallPos;
        System.Random _rng;

        const int N = OfficeBiome.ChunkCells;
        const float C = OfficeBiome.CellSize;

        void Start()
        {
            _ws = FindAnyObjectByType<WorldStreamer>();
            _player = FindAnyObjectByType<CamcorderPlayerController>();
            _rng = new System.Random(seedOffset);
            _lastCell = CellOf(_player.transform.position);
            if (!wallHug) NextTrip();
        }

        OfficeLayout Layout(Vector2Int chunk)
        {
            if (!_layouts.TryGetValue(chunk, out var l))
            {
                // Use the layout actually built (biome rules), falling back to the live settings.
                l = _ws.LayoutAt(chunk);
                if (l == null)
                {
                    var s = _ws.Settings.Evaluate(_ws.Seed, chunk);
                    l = OfficeLayoutGenerator.Generate(_ws.Seed, chunk, N, s.rules, s.Dominant);
                }
                _layouts[chunk] = l;
                var b = new HashSet<Vector2Int>();
                foreach (var p in l.props)
                {
                    int px = Mathf.FloorToInt(p.position.x / C), pz = Mathf.FloorToInt(p.position.y / C);
                    for (int a = -1; a <= 1; a++) for (int c = -1; c <= 1; c++) b.Add(new Vector2Int(px + a, pz + c));
                }
                foreach (var p in l.pillars) for (int a = -1; a <= 0; a++) for (int c = -1; c <= 0; c++) b.Add(new Vector2Int(p.vertex.x + a, p.vertex.y + c));
                foreach (var h in l.halfWalls)
                    for (int i = h.start - 1; i <= h.start + h.length; i++) for (int o = -1; o <= 0; o++)
                        b.Add(h.alongX ? new Vector2Int(i, h.line + o) : new Vector2Int(h.line + o, i));
                _blocked[chunk] = b;
            }
            return l;
        }

        static Vector2Int CellOf(Vector3 w) => new Vector2Int(Mathf.FloorToInt(w.x / C), Mathf.FloorToInt(w.z / C));
        static Vector2Int ChunkOfCell(Vector2Int g) => new Vector2Int(Mathf.FloorToInt(g.x / (float)N), Mathf.FloorToInt(g.y / (float)N));
        static Vector2Int Local(Vector2Int g) { var c = ChunkOfCell(g); return new Vector2Int(g.x - c.x * N, g.y - c.y * N); }

        EdgeKind EdgeBetween(Vector2Int g, int side)
        {
            var l = Layout(ChunkOfCell(g)); var lc = Local(g);
            return l.EdgeOf(lc.x, lc.y, side);
        }

        bool Walkable(Vector2Int g)
        {
            var ch = ChunkOfCell(g); var l = Layout(ch); var lc = Local(g);
            return l.RoomAt(lc.x, lc.y).Walkable && !_blocked[ch].Contains(lc);
        }

        static readonly Vector2Int[] Dirs = { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };

        void NextTrip()
        {
            if (_trip >= trips) { Done = true; _player.botInput = null; Debug.Log("[NavProbe] done: " + string.Join(" | ", report)); return; }
            _trip++;
            var start = CellOf(_player.transform.position);
            // BFS outward; pick a random reachable cell around tripCells steps away.
            var prev = new Dictionary<Vector2Int, Vector2Int>();
            var dist = new Dictionary<Vector2Int, int> { [start] = 0 };
            var q = new Queue<Vector2Int>(); q.Enqueue(start);
            var far = new List<Vector2Int>();
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                int d = dist[c];
                if (d >= tripCells) { far.Add(c); continue; }
                for (int s = 0; s < 4; s++)
                {
                    if (!OfficeLayout.IsPassable(EdgeBetween(c, s))) continue;
                    var n = c + Dirs[s];
                    if (dist.ContainsKey(n) || !Walkable(n)) continue;
                    dist[n] = d + 1; prev[n] = c; q.Enqueue(n);
                }
            }
            if (far.Count == 0) { report.Add($"trip {_trip}: nothing reachable {tripCells} cells from {start}"); Done = true; _player.botInput = null; return; }
            var goal = far[_rng.Next(far.Count)];
            _path = new List<Vector2Int>();
            for (var c = goal; c != start; c = prev[c]) _path.Add(c);
            _path.Reverse();
            _index = 0;
            _stallTimer = 0f; _stallPos = _player.transform.position;
        }

        // ---------------- Wall-hug mode ----------------

        [Tooltip("Instead of walking paths, shove into wall corners, door jambs and posts at an angle.")]
        public bool wallHug;
        readonly List<(Vector3 pos, float yaw, Vector2Int cell)> _hugs = new List<(Vector3, float, Vector2Int)>();
        int _hugIndex = -1;
        float _hugTime;

        void BuildHugList()
        {
            var center = WorldStreamer.ChunkOf(_player.transform.position);
            for (int cx = center.x - 1; cx <= center.x + 1; cx++)
            for (int cz = center.y - 1; cz <= center.y + 1; cz++)
            {
                var ch = new Vector2Int(cx, cz); var l = Layout(ch);
                for (int x = 0; x < N; x++)
                for (int z = 0; z < N; z++)
                {
                    if (!l.RoomAt(x, z).Walkable || _blocked[ch].Contains(new Vector2Int(x, z))) continue;
                    var g = new Vector2Int(cx * N + x, cz * N + z);
                    var w = new Vector3((g.x + 0.5f) * C, 0.05f, (g.y + 0.5f) * C);
                    // Aim at each of the four cell corners where at least one adjoining edge is solid.
                    for (int k = 0; k < 4; k++)
                    {
                        int sx = (k & 1) == 0 ? 0 : 1, sz = (k & 2) == 0 ? 2 : 3;
                        if (l.EdgeOf(x, z, sx) != EdgeKind.Wall && l.EdgeOf(x, z, sz) != EdgeKind.Wall) continue;
                        float yaw = Mathf.Atan2(sx == 0 ? -1f : 1f, sz == 2 ? -1f : 1f) * Mathf.Rad2Deg + (float)(_rng.NextDouble() - 0.5) * 30f;
                        _hugs.Add((w, yaw, g));
                    }
                }
            }
            // Random sample to keep the run short.
            for (int i = _hugs.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (_hugs[i], _hugs[j]) = (_hugs[j], _hugs[i]); }
            if (_hugs.Count > 120) _hugs.RemoveRange(120, _hugs.Count - 120);
        }

        void UpdateHug()
        {
            if (_hugIndex < 0) { BuildHugList(); _hugIndex = 0; _hugTime = 999f; }
            _hugTime += Time.deltaTime;
            var cellNow = CellOf(_player.transform.position);
            if (cellNow != _lastCell)
            {
                var d = cellNow - _lastCell;
                bool through = false;
                if (Mathf.Abs(d.x) + Mathf.Abs(d.y) == 1)
                    through = EdgeBetween(_lastCell, d.x < 0 ? 0 : d.x > 0 ? 1 : d.y < 0 ? 2 : 3) == EdgeKind.Wall;
                else if (Mathf.Abs(d.x) == 1 && Mathf.Abs(d.y) == 1)
                {
                    // Diagonal step across a vertex: legal only if one of the two L-shaped routes is open.
                    var ax = _lastCell + new Vector2Int(d.x, 0); var az = _lastCell + new Vector2Int(0, d.y);
                    bool viaX = OfficeLayout.IsPassable(EdgeBetween(_lastCell, d.x < 0 ? 0 : 1)) && OfficeLayout.IsPassable(EdgeBetween(ax, d.y < 0 ? 2 : 3));
                    bool viaZ = OfficeLayout.IsPassable(EdgeBetween(_lastCell, d.y < 0 ? 2 : 3)) && OfficeLayout.IsPassable(EdgeBetween(az, d.x < 0 ? 0 : 1));
                    through = !viaX && !viaZ;
                }
                if (through) report.Add($"HUG-WALKTHROUGH {_lastCell} -> {cellNow} at {_player.transform.position:F2}");
                _lastCell = cellNow;
            }
            if (_hugTime > 0.9f)
            {
                if (_hugIndex >= _hugs.Count) { Done = true; _player.botInput = null; Debug.Log("[NavProbe] wall hug done: " + string.Join(" | ", report)); return; }
                var h = _hugs[_hugIndex++];
                _player.Teleport(h.pos, h.yaw);
                _lastCell = h.cell;
                _hugTime = 0f;
                Walked++;
            }
            _player.botInput = new Vector2(0f, 1f);
        }

        void Update()
        {
            if (Done || _player == null) return;
            if (wallHug) { UpdateHug(); return; }
            if (_path == null) return;
            var pos = _player.transform.position;
            var cell = CellOf(pos);

            // Crossing check: moving from one cell to a neighbour through a solid edge.
            if (cell != _lastCell)
            {
                var d = cell - _lastCell;
                if (Mathf.Abs(d.x) + Mathf.Abs(d.y) == 1)
                {
                    int side = d.x < 0 ? 0 : d.x > 0 ? 1 : d.y < 0 ? 2 : 3;
                    var e = EdgeBetween(_lastCell, side);
                    if (e == EdgeKind.Wall)
                        report.Add($"WALKTHROUGH from cell {_lastCell} side {side} at {pos:F2} (chunk {ChunkOfCell(_lastCell)})");
                }
                _lastCell = cell;
                Walked++;
            }

            if (_index >= _path.Count) { NextTrip(); return; }
            var target = new Vector3((_path[_index].x + 0.5f) * C, pos.y, (_path[_index].y + 0.5f) * C);
            var to = target - pos; to.y = 0f;
            if (to.magnitude < 0.25f) { _index++; return; }
            float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            _player.SetYaw(yaw);
            _player.botInput = new Vector2(0f, 1f);

            _stallTimer += Time.deltaTime;
            if (_stallTimer > 2f)
            {
                if (Vector3.Distance(pos, _stallPos) < 0.15f)
                {
                    string what = Physics.SphereCast(pos + Vector3.up * 0.9f, 0.25f, to.normalized, out var hit, 1f)
                        ? $"{hit.collider.name} ({hit.collider.GetType().Name}) at {hit.point:F2}" : "nothing in front";
                    report.Add($"BARRIER at {pos:F2} cell {cell} heading to {_path[_index]} hit {what}");
                    // Skip ahead so the probe continues.
                    _player.Teleport(target + Vector3.up * 0.05f, yaw);
                    _lastCell = _path[_index];
                    _index++;
                }
                _stallTimer = 0f; _stallPos = pos;
            }
        }
    }
}
