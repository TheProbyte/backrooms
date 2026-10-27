using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Destruction
{
    /// <summary>A small deterministic random source (the same numbers on every machine and in every build).</summary>
    public struct Dice
    {
        ulong _s;
        public Dice(int seed) { _s = (ulong)(uint)seed * 0x9E3779B97F4A7C15ul + 0x632BE59BD9B4E019ul; Next(); }
        public ulong Next()
        {
            unchecked
            {
                ulong z = _s += 0x9E3779B97F4A7C15ul;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
                return z ^ (z >> 31);
            }
        }
        /// <summary>0..1.</summary>
        public float Value => (Next() >> 40) * (1f / (1 << 24));
        public float Range(float a, float b) => a + (b - a) * Value;
        public int Range(int a, int b) => a + (int)((Next() >> 33) % (ulong)Mathf.Max(1, b - a));

        /// <summary>A fixed number in -1..1 for a few integers (the same from anywhere that asks).</summary>
        public static float Hash(int a, int b, int c, int d = 0)
        {
            unchecked
            {
                uint h = (uint)a * 0x8DA6B343u ^ (uint)b * 0xD8163841u ^ (uint)c * 0xCB1AB31Fu ^ (uint)d * 0x165667B1u;
                h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
                return (h >> 8) * (2f / (1 << 24)) - 1f;
            }
        }
    }

    /// <summary>One piece a gypsum board breaks into.</summary>
    public sealed class FracturePiece
    {
        public int index;
        public Vector2 site;
        /// <summary>Outline in panel coordinates (u, v), counter-clockwise, with the broken edges' jags.</summary>
        public Vector2[] outline;
        /// <summary>Per outline segment (from point k to k + 1): the piece on the other side, or -1 (the board's own edge).</summary>
        public int[] label;
        /// <summary>The plain cell (straight edges), for the shadow caster and the sound geometry.</summary>
        public Vector2[] cell;
        public int[] neighbours;
        public Vector2 centroid;
        public float area, radius;
        public Rect bounds;
        /// <summary>Touches the board's edge: screwed to a post, the floor track or the top track.</summary>
        public bool border;
    }

    /// <summary>
    /// How one gypsum board of a wall panel breaks: a fixed pattern of irregular pieces (a Voronoi diagram of jittered,
    /// thinned sites, its shared edges jagged the same way from both sides), made from the panel's own dice, so it never
    /// needs saving: a save only lists which pieces are gone. Pieces are 6 to 25 cm across, like the crumbled plates a
    /// sledgehammer knocks out of drywall.
    /// </summary>
    public sealed class FracturePattern
    {
        public readonly float u0, u1, v0, v1;
        public readonly List<FracturePiece> pieces = new List<FracturePiece>();
        readonly int _nx, _ny;
        readonly float _gx, _gy;
        readonly int[] _grid;          // site per jitter cell, -1 where the site was left out

        public const float Spacing = 0.11f;

        public FracturePattern(int seed, float u0, float u1, float v0, float v1)
        {
            this.u0 = u0; this.u1 = u1; this.v0 = v0; this.v1 = v1;
            var dice = new Dice(seed);
            float W = u1 - u0, H = v1 - v0;
            _nx = Mathf.Max(2, Mathf.RoundToInt(W / Spacing)); _ny = Mathf.Max(2, Mathf.RoundToInt(H / Spacing));
            _gx = W / _nx; _gy = H / _ny;
            _grid = new int[_nx * _ny];
            var sites = new List<Vector2>(_nx * _ny);
            for (int j = 0; j < _ny; j++)
            for (int i = 0; i < _nx; i++)
            {
                // Thinned (about one in five left out, never two side by side), so some pieces are twice the size.
                bool drop = dice.Value < 0.22f && (i == 0 || _grid[j * _nx + i - 1] >= 0) && (j == 0 || _grid[(j - 1) * _nx + i] >= 0);
                float jx = dice.Value - 0.5f, jy = dice.Value - 0.5f;
                if (drop) { _grid[j * _nx + i] = -1; continue; }
                _grid[j * _nx + i] = sites.Count;
                sites.Add(new Vector2(u0 + (i + 0.5f + jx * 0.85f) * _gx, v0 + (j + 0.5f + jy * 0.85f) * _gy));
            }
            for (int s = 0; s < sites.Count; s++) pieces.Add(Cell(s, sites));
            for (int s = 0; s < pieces.Count; s++) Jag(pieces[s]);
        }

        // ---------------- Queries ----------------

        /// <summary>The piece a point of the board lies in (its nearest site), or -1 off the board.</summary>
        public int PieceAt(Vector2 p)
        {
            if (p.x < u0 - 1e-4f || p.x > u1 + 1e-4f || p.y < v0 - 1e-4f || p.y > v1 + 1e-4f) return -1;
            int ci = Mathf.Clamp((int)((p.x - u0) / _gx), 0, _nx - 1), cj = Mathf.Clamp((int)((p.y - v0) / _gy), 0, _ny - 1);
            // No two neighbouring jitter cells are both left out, so the nearest site is always within two cells.
            int best = -1; float bd = float.MaxValue;
            for (int j = Mathf.Max(0, cj - 2); j <= Mathf.Min(_ny - 1, cj + 2); j++)
            for (int i = Mathf.Max(0, ci - 2); i <= Mathf.Min(_nx - 1, ci + 2); i++)
            {
                int s = _grid[j * _nx + i];
                if (s < 0) continue;
                float d = (pieces[s].site - p).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        /// <summary>Distance from a point to a piece's outline (0 inside).</summary>
        public static float Distance(FracturePiece f, Vector2 p)
        {
            if (Inside(f.cell, p)) return 0f;
            float best = float.MaxValue;
            var o = f.cell;
            for (int k = 0; k < o.Length; k++)
            {
                Vector2 a = o[k], b = o[(k + 1) % o.Length], ab = b - a;
                float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (a + ab * t - p).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }

        public static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        // ---------------- Building ----------------

        FracturePiece Cell(int s, List<Vector2> sites)
        {
            Vector2 c = sites[s];
            var poly = new List<Vector2> { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) };
            var lab = new List<int> { -3, -2, -4, -1 };       // bottom, right, top, left (the board's own edges)
            // Candidates: sites in the 7 x 7 jitter cells around, nearest first.
            int ci = Mathf.Clamp((int)((c.x - u0) / _gx), 0, _nx - 1), cj = Mathf.Clamp((int)((c.y - v0) / _gy), 0, _ny - 1);
            var cand = new List<(float d, int i)>(48);
            for (int j = Mathf.Max(0, cj - 3); j <= Mathf.Min(_ny - 1, cj + 3); j++)
            for (int i = Mathf.Max(0, ci - 3); i <= Mathf.Min(_nx - 1, ci + 3); i++)
            {
                int o = _grid[j * _nx + i];
                if (o >= 0 && o != s) cand.Add(((sites[o] - c).sqrMagnitude, o));
            }
            cand.Sort((a, b) => a.d.CompareTo(b.d));
            var np = new List<Vector2>(16); var nl = new List<int>(16);
            foreach (var (d2, o) in cand)
            {
                float maxR = 0f;
                foreach (var p in poly) maxR = Mathf.Max(maxR, (p - c).sqrMagnitude);
                if (d2 > 4f * maxR) break;                         // too far to cut this cell any more
                Vector2 mid = (c + sites[o]) * 0.5f, dir = sites[o] - c;
                np.Clear(); nl.Clear();
                for (int k = 0; k < poly.Count; k++)
                {
                    Vector2 a = poly[k], b = poly[(k + 1) % poly.Count];
                    float sa = Vector2.Dot(a - mid, dir), sb = Vector2.Dot(b - mid, dir);
                    bool ina = sa <= 0f, inb = sb <= 0f;
                    if (ina) { np.Add(a); nl.Add(lab[k]); }
                    if (ina != inb)
                    {
                        float t = sa / (sa - sb);
                        np.Add(a + (b - a) * t);
                        nl.Add(ina ? o : lab[k]);
                    }
                }
                if (np.Count < 3) break;
                poly.Clear(); poly.AddRange(np); lab.Clear(); lab.AddRange(nl);
            }
            // Drop edges of no length (two cuts through one point): the vertex goes with the empty edge it starts.
            for (int k = poly.Count - 1; k >= 0 && poly.Count > 3; k--)
                if ((poly[(k + 1) % poly.Count] - poly[k]).sqrMagnitude < 1e-10f) { poly.RemoveAt(k); lab.RemoveAt(k); }
            return new FracturePiece { index = s, site = c, cell = poly.ToArray(), label = lab.ToArray() };
        }

        /// <summary>Breaks the straight shared edges into jagged ones (the same points from both of the pieces on an edge).</summary>
        void Jag(FracturePiece f)
        {
            var pts = new List<Vector2>(f.cell.Length * 3);
            var lab = new List<int>(f.cell.Length * 3);
            var nb = new HashSet<int>();
            bool border = false;
            for (int k = 0; k < f.cell.Length; k++)
            {
                Vector2 a = f.cell[k], b = f.cell[(k + 1) % f.cell.Length];
                int l = f.label[k];
                pts.Add(a); lab.Add(l);
                if (l < 0) { border = true; continue; }
                nb.Add(l);
                float len = (b - a).magnitude;
                int segs = Mathf.Clamp(Mathf.RoundToInt(len / 0.028f), 1, 4);
                if (segs < 2 || len < 0.012f) continue;
                // Canonical direction: from the lexicographically smaller end, so both pieces put the jags in the same places.
                bool fwd = a.x < b.x || (a.x == b.x && a.y < b.y);
                Vector2 c0 = fwd ? a : b, c1 = fwd ? b : a;
                Vector2 dir = (c1 - c0) / len, nrm = new Vector2(-dir.y, dir.x);
                float amp = Mathf.Min(0.011f, len * 0.14f);
                int lo = Mathf.Min(f.index, l), hi = Mathf.Max(f.index, l);
                for (int q = 1; q < segs; q++)
                {
                    int s = fwd ? q : segs - q;
                    float t = s / (float)segs + Dice.Hash(lo, hi, s, 7) * 0.12f / segs;
                    Vector2 p = Vector2.Lerp(c0, c1, t) + nrm * (amp * Dice.Hash(lo, hi, s));
                    pts.Add(p); lab.Add(l);
                }
            }
            f.outline = pts.ToArray(); f.label = lab.ToArray();
            f.neighbours = new int[nb.Count]; nb.CopyTo(f.neighbours);
            f.border = border;
            // Area, centroid and bounds of the outline.
            float A = 0f; Vector2 C = Vector2.zero;
            Vector2 mn = f.outline[0], mx = f.outline[0];
            for (int i = 0; i < f.outline.Length; i++)
            {
                Vector2 p = f.outline[i], q = f.outline[(i + 1) % f.outline.Length];
                float cr = p.x * q.y - q.x * p.y;
                A += cr; C += (p + q) * cr;
                mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p);
            }
            A *= 0.5f;
            f.area = Mathf.Abs(A);
            f.centroid = Mathf.Abs(A) > 1e-9f ? C / (6f * A) : f.site;
            f.bounds = Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
            f.radius = Mathf.Sqrt(f.area / Mathf.PI);
        }
    }
}
