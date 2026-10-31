using System;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Destruction
{
    /// <summary>What a swing struck in a wall.</summary>
    public enum StrikeKind { None, Board, BoardBack, Stud, Insulation, Baseboard, Metal, Post, Passed }

    /// <summary>A part knocked out of a wall, to fall as debris.</summary>
    public struct Fallen
    {
        public enum Part : byte { Board, Baseboard, Stud, Insulation }
        public Part part;
        public int side, index;       // board / baseboard: side and piece; stud: stud; insulation: bay * rows + row
        public Vector2 span;          // a stud's length (v from, to)
        public Vector3 velocity, spin;
        public bool crumble;          // too small to keep: it crumbles to dust
    }

    /// <summary>
    /// How a blow breaks drywall: how hard it lands (0 a tap .. 1 a full swing; more for a heavier tool or a tuned one: studs,
    /// baseboards and the debris take it), the radius of the board it knocks out (m), and how far its cracks reach round
    /// that (times the radius). The sledgehammer's come from its settings (the F2 menu).
    /// </summary>
    public struct Blow
    {
        public float energy, radius, crackSpread;

        /// <summary>The built-in blow for a strength 0..1 (a thrown hammer, the tests).</summary>
        public static Blow Of(float energy) => new Blow { energy = energy, radius = Mathf.Lerp(0.08f, 0.22f, Mathf.Clamp01(energy)), crackSpread = 2.2f };
    }

    public sealed class StrikeResult
    {
        public StrikeKind kind;
        public Vector3 point, normal;     // world
        public int removed;               // pieces knocked out (and fallen)
        public bool broke;                // anything changed
        public bool through;              // the wall can now be seen through where it could not before
        public bool studBroke;
        public bool steel;
        public List<Fallen> fallen;
    }

    /// <summary>
    /// The breakable walls of the world being played: every panel that has been struck (<see cref="WallPanelModel"/> for
    /// what is in it, <see cref="WallPanelState"/> for what is left), and what a swing does to one. A struck panel is taken
    /// over from its chunks (they stop drawing it; <see cref="BrokenPanel"/> draws what is left) and the light grid, the
    /// chunk culling and the sound read through its holes. Lasts the session and is saved with the world.
    /// </summary>
    public static class WallDamage
    {
        public sealed class Panel
        {
            public WallPanelModel model;
            public WallPanelState state;
            /// <summary>Light blocks (0.15 m) the wall is open through (most of the block), bit row * 8 + column.</summary>
            public readonly uint[] mask = new uint[WallSpec.MaskWords];
            /// <summary>Light blocks with any opening at all: where lights with shadow maps may pass (their shadows do the rest).</summary>
            public readonly uint[] maskAny = new uint[WallSpec.MaskWords];
            /// <summary>Any piece of either board gone: real lights are stopped by the pieces' shadows, not by the wall line.</summary>
            public bool holed;
            /// <summary>Some of it can be seen through.</summary>
            public bool seeThrough;
            /// <summary>Share of the panel open right through.</summary>
            public float openFraction;
            /// <summary>Debris lying where it settled (world poses), drawn with the panel.</summary>
            public readonly List<Rubble> rubble = new List<Rubble>();
            /// <summary>Goes up whenever a piece comes to rest (or the oldest is swept away).</summary>
            public int rubbleVersion;
        }

        [Serializable]
        public struct Rubble
        {
            public Fallen.Part part;
            public int side, index;
            public Vector2 span;
            public Vector3 position;
            public Quaternion rotation;
        }

        public static float Pitch = 9f;
        public static int WorldSeed;
        /// <summary>The layout of any chunk (the generator's, thread-safe).</summary>
        public static Func<Vector3Int, OfficeLayout> Layouts;
        public static OfficeBiomeAssets Assets;

        /// <summary>The world the walls belong to: its seed (the pieces' dice), its layouts and storey height, the materials.</summary>
        public static void Bind(WorldGenerator world, float pitch, OfficeBiomeAssets assets)
        {
            WorldSeed = world != null ? world.Seed : 0;
            Layouts = world != null ? (Func<Vector3Int, OfficeLayout>)(c => world.Chunk(c)) : null;
            Pitch = pitch > 0f ? pitch : 9f;
            Assets = assets;
        }

        static readonly Dictionary<WallKey, Panel> s_panels = new Dictionary<WallKey, Panel>();
        // Chunk builders run on worker threads: they read a copy, replaced whole when a panel is taken over.
        static volatile Dictionary<WallKey, float> s_taken = new Dictionary<WallKey, float>();

        /// <summary>A panel's geometry or holes changed.</summary>
        public static event Action<WallKey> Changed;
        /// <summary>A panel was taken over from its chunks (they draw it no more): rebuild their walls.</summary>
        public static event Action<WallKey> TakenOver;
        /// <summary>Goes up with every change.</summary>
        public static int Version { get; private set; }

        public static int Count => s_panels.Count;
        public static IEnumerable<KeyValuePair<WallKey, Panel>> All => s_panels;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            s_panels.Clear();
            s_taken = new Dictionary<WallKey, float>();
            Version++;
        }

        /// <summary>Worker threads: is this wall drawn by a broken panel (and up to what height)?</summary>
        public static bool Taken(bool vertical, int line, int along, int storey, out float height)
        {
            var t = s_taken;
            if (t.Count == 0) { height = 0f; return false; }
            return t.TryGetValue(new WallKey(vertical, line, along, storey), out height);
        }

        public static bool Taken(WallKey k) => s_taken.ContainsKey(k);

        public static Panel Find(WallKey k) => s_panels.TryGetValue(k, out var p) ? p : null;

        /// <summary>The light-grid view of a wall: holed (and its open blocks), or not.</summary>
        public static bool Holed(WallKey k, out Panel p) => s_panels.TryGetValue(k, out p) && p.holed;

        public static bool SeeThrough(WallKey k) => s_panels.TryGetValue(k, out var p) && p.seeThrough;

        static OfficeLayout LayoutOf(WallKey k, int side) => Layouts?.Invoke(k.Chunk(side));

        /// <summary>
        /// Builds a wall's panel (its fracture, framing and mesh) and throws it away, keeping nothing: WallBreaker's warm-up,
        /// so the first blow of a session does not build all this (and compile its code) for the first time (owner: "there
        /// always seems to be one lag spike when you first hammer a wall hole").
        /// </summary>
        public static bool Rehearse(WallKey k)
        {
            var l0 = LayoutOf(k, 0); var l1 = LayoutOf(k, 1);
            if (!WallPanelModel.Breakable(k, l0, l1, out float height)) return false;
            var m = new WallPanelModel(k, WorldSeed, height, WallPanelModel.SteelFor(l0, l1), new[] { l0, l1 }, Assets);
            var st = new WallPanelState(m);
            OpenAt(m, st, 0.6f, 1f);
            Loose(m, st, 0);
            var mb = new PanelMesh(PanelGeometry.Slots);
            PanelGeometry.View(mb, m, st, Pitch);
            var mesh = mb.Apply(new Mesh(), PanelGeometry.Materials(m), out _, "Rehearsal");
            if (mesh) UnityEngine.Object.Destroy(mesh);
            return true;
        }

        /// <summary>Can the wall be broken (and what would it be built of)?</summary>
        public static bool Breakable(WallKey k)
        {
            if (s_panels.ContainsKey(k)) return true;
            return WallPanelModel.Breakable(k, LayoutOf(k, 0), LayoutOf(k, 1), out _);
        }

        /// <summary>The panel, made (and taken over) if it is breakable and not yet struck.</summary>
        public static Panel Get(WallKey k, bool create)
        {
            if (s_panels.TryGetValue(k, out var p)) return p;
            if (!create) return null;
            var l0 = LayoutOf(k, 0); var l1 = LayoutOf(k, 1);
            if (!WallPanelModel.Breakable(k, l0, l1, out float height)) return null;
            var m = new WallPanelModel(k, WorldSeed, height, WallPanelModel.SteelFor(l0, l1), new[] { l0, l1 }, Assets);
            p = new Panel { model = m, state = new WallPanelState(m) };
            s_panels[k] = p;
            return p;
        }

        static void TakeOver(WallKey k, Panel p)
        {
            if (s_taken.ContainsKey(k)) return;
            var copy = new Dictionary<WallKey, float>(s_taken) { [k] = p.model.height };
            s_taken = copy;
            TakenOver?.Invoke(k);
        }

        // ---------------- A swing ----------------

        /// <summary>
        /// A swing along a ray (world) with a strength of 0..1 (a quick tap to a full wind-up): finds what in the wall the
        /// hammer's face meets first, breaks it as far as the blow goes, lets fall whatever is left hanging free.
        /// </summary>
        public static StrikeResult Strike(WallKey k, Vector3 origin, Vector3 dir, float maxDistance, float energy)
            => Strike(k, origin, dir, maxDistance, Blow.Of(energy));

        public static StrikeResult Strike(WallKey k, Vector3 origin, Vector3 dir, float maxDistance, Blow blow)
        {
            float energy = blow.energy;
            var r = new StrikeResult { kind = StrikeKind.None, fallen = new List<Fallen>() };
            var p = Get(k, true);
            if (p == null) return r;
            var m = p.model; var st = p.state;
            r.steel = m.steel;
            Vector3 O = k.ToLocal(origin, Pitch), d = k.DirToLocal(dir.normalized);
            if (Mathf.Abs(d.z) < 0.05f) return r;                         // along the wall
            int front = d.z > 0f ? 0 : 1, back = 1 - front;
            float sign = front == 0 ? -1f : 1f;

            // The nearest thing along the ray.
            float best = maxDistance; StrikeKind kind = StrikeKind.None; int which = -1, which2 = -1; Vector3 at = Vector3.zero, nrm = Vector3.zero;
            void Offer(float t, StrikeKind sk, int a, int b, Vector3 n)
            {
                if (t < 0f || t >= best) return;
                best = t; kind = sk; which = a; which2 = b; at = O + d * t; nrm = n;
            }
            float H = m.height;
            // The baseboard in front.
            for (int i = 0; i < WallPanelModel.BaseboardPieces; i++)
            {
                if (!m.HasBaseboard(st, front, i)) continue;
                var bb = m.baseboard[i];
                float w0 = sign * WallSpec.T * 0.5f, w1 = w0 + sign * WallPanelModel.BaseboardDepth;
                if (RayBox(O, d, new Vector3(bb.x, 0f, Mathf.Min(w0, w1)), new Vector3(bb.y, WallPanelModel.BaseboardHeight, Mathf.Max(w0, w1)), out float tb))
                    Offer(tb, StrikeKind.Baseboard, i, front, Vector3.forward * sign);
            }
            // The front board's face.
            {
                float wf = sign * WallSpec.T * 0.5f;
                float t = (wf - O.z) / d.z;
                Vector3 q = O + d * t;
                if (q.y >= 0f && q.y <= H)
                {
                    if (q.x < WallSpec.U0 || q.x > WallSpec.U1) Offer(t, StrikeKind.Post, -1, -1, Vector3.forward * sign);
                    else if (st.BoardAt(m, front, new Vector2(q.x, q.y))) Offer(t, StrikeKind.Board, m.boards[front].PieceAt(new Vector2(q.x, q.y)), front, Vector3.forward * sign);
                }
                else if (q.y > H && q.y < H + 0.5f) Offer(t, StrikeKind.Post, -1, -1, Vector3.forward * sign);      // above the breakable part
            }
            // Inside: studs, tracks, boxes, pipe, insulation; then the back board from behind.
            float ch = WallSpec.CavityHalf, sw = WallSpec.StudWidth(m.steel) * 0.5f;
            for (int i = 0; i < m.studs.Length; i++)
                foreach (var seg in st.studSegments[i])
                    if (RayBox(O, d, new Vector3(m.studs[i] - sw, seg.x, -ch), new Vector3(m.studs[i] + sw, seg.y, ch), out float ts))
                        Offer(ts, StrikeKind.Stud, i, -1, Vector3.forward * sign);
            float bottom = WallSpec.StudBottom(m.steel), top = WallSpec.StudTop(m.steel, H);
            if (RayBox(O, d, new Vector3(WallSpec.U0, 0f, -ch), new Vector3(WallSpec.U1, Mathf.Max(bottom, 0.02f), ch), out float tt)) Offer(tt, StrikeKind.Metal, 0, -1, Vector3.up);
            if (RayBox(O, d, new Vector3(WallSpec.U0, top, -ch), new Vector3(WallSpec.U1, H, ch), out tt)) Offer(tt, StrikeKind.Metal, 1, -1, Vector3.down);
            foreach (var pl in m.plates)
            {
                m.BoxOf(pl, out var c, out var h);
                if (RayBox(O, d, c - h, c + h, out float tp)) Offer(tp, StrikeKind.Metal, 2, -1, Vector3.forward * sign);
            }
            if (m.HasPipe && RayBox(O, d, new Vector3(m.pipe.x - m.pipe.y, 0f, -m.pipe.y), new Vector3(m.pipe.x + m.pipe.y, H, m.pipe.y), out float tq)) Offer(tq, StrikeKind.Metal, 3, -1, Vector3.forward * sign);
            if (m.insulated)
                for (int b = 0; b < m.bays.Count; b++)
                for (int row = 0; row < m.rows; row++)
                {
                    if (!st.InsulationPresent(m, b, row)) continue;
                    float v0 = bottom + row * m.rowHeight;
                    if (RayBox(O, d, new Vector3(m.bays[b].x, v0, -ch), new Vector3(m.bays[b].y, v0 + m.rowHeight, ch), out float ti))
                        Offer(ti, StrikeKind.Insulation, b, row, Vector3.forward * sign);
                }
            {
                float wb = -sign * ch;                                       // the back board's inner face
                float t = (wb - O.z) / d.z;
                Vector3 q = O + d * t;
                if (q.y >= 0f && q.y <= H && q.x >= WallSpec.U0 && q.x <= WallSpec.U1 && st.BoardAt(m, back, new Vector2(q.x, q.y)))
                    Offer(t, StrikeKind.BoardBack, m.boards[back].PieceAt(new Vector2(q.x, q.y)), back, Vector3.forward * sign);
            }
            bool wasTaken = s_taken.ContainsKey(k);
            if (kind == StrikeKind.None)
            {
                // Through a hole, or short of the wall.
                float wf = sign * WallSpec.T * 0.5f, t = (wf - O.z) / d.z;
                r.kind = t >= 0f && t < maxDistance ? StrikeKind.Passed : StrikeKind.None;
                if (!wasTaken) s_panels.Remove(k);                       // nothing done to it: no record
                return r;
            }
            r.kind = kind;
            r.point = k.ToWorld(at, Pitch);
            r.normal = k.DirToWorld(nrm).normalized;
            bool seeBefore = p.seeThrough;
            Vector3 swing = d;
            Vector2 uv = new Vector2(at.x, at.y);
            switch (kind)
            {
                case StrikeKind.Board: BreakBoard(p, front, uv, blow, swing, true, r); break;
                case StrikeKind.BoardBack: { var b = blow; b.energy *= 0.9f; BreakBoard(p, back, uv, b, swing, false, r); break; }
                case StrikeKind.Stud: HitStud(p, which, at.y, energy, swing, r); break;
                case StrikeKind.Insulation:
                {
                    int idx = which * m.rows + which2;
                    st.insulationGone[idx] = true;
                    r.fallen.Add(new Fallen { part = Fallen.Part.Insulation, index = idx, velocity = swing * (0.6f + energy), spin = UnityEngine.Random.insideUnitSphere * 2f });
                    r.removed++; r.broke = true;
                    break;
                }
                case StrikeKind.Baseboard:
                {
                    int bi = which2 * 3 + which;
                    st.baseboardDamage[bi] += energy;
                    if (st.baseboardDamage[bi] >= 1.05f || energy > 0.9f)
                    {
                        st.baseboardGone |= 1 << bi;
                        r.fallen.Add(new Fallen { part = Fallen.Part.Baseboard, side = which2, index = which, velocity = swing * (0.4f + energy * 1.2f) + Vector3.up * 0.4f, spin = UnityEngine.Random.insideUnitSphere * 3f });
                        r.removed++;
                    }
                    r.broke = true;
                    break;
                }
            }
            if (r.broke)
            {
                Drop(p, r);
                DropLodged(p);                                            // rubble on an edge that just broke away
                Refresh(p);
                st.version++; Version++;
                if (!wasTaken) TakeOver(k, p);
                r.through = p.seeThrough && !seeBefore;
                // Debris was worked out in the panel's frame.
                for (int i = 0; i < r.fallen.Count; i++)
                {
                    var f = r.fallen[i];
                    if (f.part == Fallen.Part.Board)
                    {
                        // Its top tips the way it goes, so it falls flat rather than coming to rest upright on the edge below.
                        var away = k.DirToWorld(new Vector3(0f, 0f, f.velocity.z >= 0f ? 1f : -1f)).normalized;
                        f.spin += Vector3.Cross(Vector3.up, away) * UnityEngine.Random.Range(2f, 5f);
                    }
                    f.velocity = k.DirToWorld(f.velocity);
                    r.fallen[i] = f;
                }
                Changed?.Invoke(k);
            }
            else if (!wasTaken) s_panels.Remove(k);                       // a post, a track: nothing done to the wall
            return r;
        }

        /// <summary>Breaks a board round a point: the plates the face of the hammer meets come out, the ones round them crack.</summary>
        static void BreakBoard(Panel p, int side, Vector2 uv, Blow blow, Vector3 swing, bool fromFront, StrikeResult r)
        {
            float energy = blow.energy;
            var m = p.model; var st = p.state; var board = m.boards[side];
            int stud = m.StudAt(uv.x, 0.012f);
            bool backed = fromFront && stud >= 0 && st.StudAt(stud, uv.y);
            float radius = Mathf.Max(0.01f, blow.radius) * (backed ? 0.55f : 1f) * (fromFront ? 1f : 0.85f);
            int hitPiece = board.PieceAt(uv);
            for (int i = 0; i < board.pieces.Count; i++)
            {
                if (st.removed[side][i]) continue;
                var f = board.pieces[i];
                float near = radius * Mathf.Max(2.4f, blow.crackSpread + 0.2f) + f.radius * 2f;
                if ((f.centroid - uv).sqrMagnitude > near * near) continue;
                float dist = FracturePattern.Distance(f, uv);
                float reach = radius * (0.7f + 0.6f * (Dice.Hash(i, side, m.key.line, m.key.along) * 0.5f + 0.5f));
                // A weak tap only cracks; one on a cracked plate knocks it out. A full swing knocks out everything it meets.
                bool knock = i == hitPiece ? energy >= 0.3f || st.cracked[side][i] : dist < reach && energy >= 0.3f;
                if (knock)
                {
                    st.removed[side][i] = true;
                    r.removed++;
                    var v = swing * energy * UnityEngine.Random.Range(1.2f, 2.8f);
                    bool spall = fromFront && UnityEngine.Random.value < 0.25f;
                    if (spall) v = -swing * UnityEngine.Random.Range(0.3f, 1.1f) + Vector3.up * 0.6f;   // spalls back at you
                    // Out of the board's plane whatever the swing: dropping straight down it would stand on the edge of the hole.
                    float out_ = (spall ? -swing.z : swing.z) >= 0f ? 1f : -1f, least = 0.45f + energy * 0.6f;
                    if (v.z * out_ < least) v.z = out_ * least;
                    r.fallen.Add(new Fallen { part = Fallen.Part.Board, side = side, index = i, velocity = v, spin = UnityEngine.Random.insideUnitSphere * 9f, crumble = f.area < 0.0035f });
                }
                else if (dist < radius * Mathf.Max(1f, blow.crackSpread)) st.cracked[side][i] = true;
            }
            if (backed) Damage(p, stud, uv.y, energy * 0.35f, swing, r);
            r.broke = true;
        }

        static void HitStud(Panel p, int stud, float v, float energy, Vector3 swing, StrikeResult r)
        {
            Damage(p, stud, v, energy, swing, r);
            r.broke = true;
        }

        /// <summary>Damage to a stud at a height; past what it takes (steel bends and tears, wood splits) it breaks there.</summary>
        static void Damage(Panel p, int stud, float v, float energy, Vector3 swing, StrikeResult r)
        {
            var m = p.model; var st = p.state;
            if (m.steel) return;                         // steel studs ring and stop the hammer; they do not give way
            int bin = Mathf.Clamp(Mathf.FloorToInt(v / WallPanelState.BinHeight), 0, WallPanelState.DamageBins - 1);
            var dmg = st.studDamage[stud];
            dmg[bin] += energy;
            if (bin > 0) dmg[bin - 1] += energy * 0.25f;
            if (bin < dmg.Length - 1) dmg[bin + 1] += energy * 0.25f;
            float need = m.steel ? 1.5f : 2.9f;
            if (dmg[bin] < need) return;
            dmg[bin] = 0f;
            float gap = m.steel ? 0.11f : 0.07f;
            var segs = st.studSegments[stud];
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                if (v < s.x || v > s.y) continue;
                segs.RemoveAt(i);
                var lo = new Vector2(s.x, v - gap * 0.5f);
                var hi = new Vector2(v + gap * 0.5f, s.y);
                if (hi.y - hi.x > 0.04f) segs.Insert(i, hi);
                if (lo.y - lo.x > 0.04f) segs.Insert(i, lo);
                r.studBroke = true;
                break;
            }
            // A length held at neither end falls out.
            float bottom = WallSpec.StudBottom(m.steel), top = WallSpec.StudTop(m.steel, m.height);
            for (int i = segs.Count - 1; i >= 0; i--)
            {
                var s = segs[i];
                if (s.x <= bottom + 0.01f || s.y >= top - 0.01f) continue;
                segs.RemoveAt(i);
                r.fallen.Add(new Fallen { part = Fallen.Part.Stud, index = stud, span = s, velocity = swing * (0.5f + energy), spin = UnityEngine.Random.insideUnitSphere * 2f });
                r.removed++;
            }
        }

        /// <summary>
        /// Pieces of a board held by nothing fall. Drywall hangs on its screws: one every 0.3 m (12 in.) up each post and each
        /// standing length of stud. A piece with a screw in it holds, and so does one joined to a held piece along 3 cm or more
        /// of unbroken edge; the rest come out. (Before: any piece whose box overlapped a stud's line, or touched the board's
        /// edge, held, and so did anything joined to one at a corner: chips stayed floating in the hole.)
        /// </summary>
        static void Drop(Panel p, StrikeResult r)
        {
            var m = p.model; var st = p.state;
            for (int side = 0; side < 2; side++)
                foreach (int i in Loose(m, st, side))
                {
                    st.removed[side][i] = true;
                    r.removed++;
                    var f = m.boards[side].pieces[i];
                    float out_ = side == 0 ? -1f : 1f;
                    var v = new Vector3(UnityEngine.Random.Range(-0.2f, 0.2f), UnityEngine.Random.Range(-0.3f, 0.1f), out_ * UnityEngine.Random.Range(0.3f, 0.6f));
                    r.fallen.Add(new Fallen { part = Fallen.Part.Board, side = side, index = i, velocity = v, spin = UnityEngine.Random.insideUnitSphere * 3f, crumble = f.area < 0.0035f });
                }
        }

        /// <summary>Spacing of the screws up a stud or a post (m), and the first one's height.</summary>
        public const float ScrewSpacing = 0.3f, ScrewFirst = 0.15f;
        /// <summary>Shortest unbroken edge by which a piece still holds its neighbour (m).</summary>
        public const float MinJoin = 0.03f;
        static readonly List<int> s_loose = new List<int>();
        static readonly Queue<int> s_queue = new Queue<int>();

        /// <summary>The pieces of a board still in the wall that nothing holds (see <see cref="Drop"/>).</summary>
        public static List<int> Loose(WallPanelModel m, WallPanelState st, int side)
        {
            s_loose.Clear();
            var board = m.boards[side];
            int n = board.pieces.Count;
            var held = new bool[n];
            s_queue.Clear();
            void Screw(float u, float v)
            {
                int i = board.PieceAt(new Vector2(u, v));
                if (i < 0 || held[i] || st.removed[side][i]) return;
                held[i] = true; s_queue.Enqueue(i);
            }
            for (float v = ScrewFirst; v < m.height - 0.05f; v += ScrewSpacing)
            {
                Screw(WallSpec.U0 + 0.012f, v); Screw(WallSpec.U1 - 0.012f, v);           // the posts
                for (int s = 0; s < m.studs.Length; s++)
                    foreach (var seg in st.studSegments[s])
                        if (v >= seg.x && v <= seg.y) { Screw(m.studs[s], v); break; }
            }
            while (s_queue.Count > 0)
            {
                var f = board.pieces[s_queue.Dequeue()];
                for (int k = 0; k < f.neighbours.Length; k++)
                {
                    int j = f.neighbours[k];
                    if (held[j] || st.removed[side][j] || f.joins[k] < MinJoin) continue;
                    held[j] = true; s_queue.Enqueue(j);
                }
            }
            for (int i = 0; i < n; i++) if (!held[i] && !st.removed[side][i]) s_loose.Add(i);
            return s_loose;
        }

        /// <summary>
        /// A saved wall's pieces that nothing holds any more (the rule changed, 2026-10-27): they are found on the floor at
        /// the foot of the wall, lying flat on their side of it, as if they fell while you were away.
        /// </summary>
        static void SettleLoose(Panel p)
        {
            var m = p.model; var st = p.state; var k = m.key;
            for (int side = 0; side < 2; side++)
            {
                var loose = Loose(m, st, side);
                if (loose.Count == 0) continue;
                var dice = new Dice(k.Seed(WorldSeed) * 13 + side);
                float sgn = side == 0 ? -1f : 1f;
                foreach (int i in loose)
                {
                    st.removed[side][i] = true;
                    var f = m.boards[side].pieces[i];
                    if (f.area < 0.0035f) continue;                                         // crumbled to dust
                    p.rubble.Add(StackOn(p, FloorPose(k, Fallen.Part.Board, side, i, Vector2.zero, f.centroid.x, sgn, dice.Value, dice.Value, dice.Value), p.rubble.Count));
                }
                p.rubbleVersion++;
            }
        }

        // ---------------- What the wall lets through ----------------

        /// <summary>Works out the panel's open light blocks, whether it can be seen through and how open it is.</summary>
        public static void Refresh(Panel p)
        {
            var m = p.model; var st = p.state;
            Array.Clear(p.mask, 0, p.mask.Length);
            Array.Clear(p.maskAny, 0, p.maskAny.Length);
            int open = 0, total = 0;
            for (int row = 0; row < WallSpec.MaskRows; row++)
            for (int col = 0; col < WallSpec.MaskCols; col++)
            {
                int blockOpen = 0;
                for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                {
                    float u = (col + (a + 0.5f) / 3f) * WallSpec.MaskCell, v = (row + (b + 0.5f) / 3f) * WallSpec.MaskCell;
                    if (u < WallSpec.U0 || u > WallSpec.U1 || v > m.height) continue;
                    total++;
                    if (OpenAt(m, st, u, v)) { blockOpen++; open++; }
                }
                int bit = row * WallSpec.MaskCols + col;
                if (blockOpen >= 5) p.mask[bit >> 5] |= 1u << (bit & 31);
                if (blockOpen >= 1) p.maskAny[bit >> 5] |= 1u << (bit & 31);
            }
            p.seeThrough = open > 0;
            p.openFraction = total > 0 ? open / (float)total : 0f;
            p.holed = st.AnyRemoved;
        }

        /// <summary>Nothing at a point of the panel: both boards gone, no batt, no box.</summary>
        public static bool OpenAt(WallPanelModel m, WallPanelState st, float u, float v)
        {
            var uv = new Vector2(u, v);
            if (st.BoardAt(m, 0, uv) || st.BoardAt(m, 1, uv)) return false;
            if (m.insulated)
            {
                int bay = m.BayAt(u);
                float bottom = WallSpec.StudBottom(m.steel);
                int row = Mathf.FloorToInt((v - bottom) / m.rowHeight);
                if (st.InsulationPresent(m, bay, row)) return false;
            }
            foreach (var pl in m.plates)
            {
                m.BoxOf(pl, out var c, out var h);
                if (Mathf.Abs(u - c.x) <= h.x && Mathf.Abs(v - c.y) <= h.y) return false;
            }
            return true;
        }

        /// <summary>Is the light-grid block at (u, v) of a holed wall open? (The same test the shaders make.)</summary>
        public static bool MaskOpen(uint[] mask, float u, float v)
        {
            int col = Mathf.FloorToInt(u / WallSpec.MaskCell), row = Mathf.FloorToInt(v / WallSpec.MaskCell);
            if (col < 0 || col >= WallSpec.MaskCols || row < 0 || row >= WallSpec.MaskRows) return false;
            int bit = row * WallSpec.MaskCols + col;
            return (mask[bit >> 5] & (1u << (bit & 31))) != 0;
        }

        // ---------------- Rubble ----------------

        /// <summary>
        /// A loose part lying flat on the floor at the foot of a wall, on one side of it (sgn), at u along it: out from the wall
        /// by 8-45 cm (out01), face up or down (flip01), turned any way (yaw01).
        /// </summary>
        public static Rubble FloorPose(WallKey k, Fallen.Part part, int side, int index, Vector2 span, float u, float sgn, float out01, float flip01, float yaw01)
        {
            Quaternion lie; float lift;
            switch (part)
            {
                case Fallen.Part.Stud: lie = Quaternion.AngleAxis(90f, k.Across); lift = 0.02f; break;          // a length of stud, on its side
                case Fallen.Part.Baseboard: lie = Quaternion.AngleAxis(flip01 < 0.5f ? 90f : -90f, k.Along); lift = WallPanelModel.BaseboardDepth * 0.5f; break;
                default: lie = Quaternion.AngleAxis(flip01 < 0.5f ? 90f : -90f, k.Along); lift = WallSpec.Board * 0.5f; break;
            }
            Vector3 at = k.ToWorld(Mathf.Clamp(u, 0.05f, WallSpec.C - 0.05f), 0f, sgn * (WallSpec.T * 0.5f + Mathf.Lerp(0.08f, 0.45f, out01)), Pitch);
            at.y = k.storey * Pitch + lift + 0.002f;
            return new Rubble { part = part, side = side, index = index, span = span, position = at, rotation = Quaternion.AngleAxis(yaw01 * 360f, Vector3.up) * lie };
        }

        /// <summary>
        /// Is a point wedged in a wall: inside its thickness (or just off it) and off the floor? Debris that comes to rest
        /// there (in the cavity, on the edge of a hole, against a stud) would hang in the hole for good: it is dropped to
        /// the floor instead (WallDebris, <see cref="DropLodged"/>).
        /// </summary>
        public static bool Lodged(WallKey k, Vector3 world)
        {
            var l = k.ToLocal(world, Pitch);
            return l.y > 0.1f && Mathf.Abs(l.z) < WallSpec.T * 0.5f + 0.04f && l.x > -0.1f && l.x < WallSpec.C + 0.1f;
        }

        /// <summary>A part that has come to rest wedged in its wall: on the floor below instead, on the side it is nearer.</summary>
        public static void SettleOnFloor(WallKey k, Fallen.Part part, int side, int index, Vector2 span, Vector3 world)
        {
            var l = k.ToLocal(world, Pitch);
            float sgn = Mathf.Abs(l.z) > 0.005f ? Mathf.Sign(l.z) : (side == 0 ? -1f : 1f);
            Settle(k, FloorPose(k, part, side, index, span, l.x, sgn, UnityEngine.Random.value * 0.6f, UnityEngine.Random.value, UnityEngine.Random.value));
        }

        /// <summary>Rubble wedged in its wall (saved so by an older version, or left on an edge that has since broken away) falls to the floor.</summary>
        public static bool DropLodged(Panel p)
        {
            var k = p.model.key; bool any = false;
            for (int i = 0; i < p.rubble.Count; i++)
            {
                var r = p.rubble[i];
                if (!Lodged(k, r.position)) continue;
                var l = k.ToLocal(r.position, Pitch);
                float sgn = Mathf.Abs(l.z) > 0.005f ? Mathf.Sign(l.z) : (r.side == 0 ? -1f : 1f);
                var dice = new Dice(k.Seed(WorldSeed) * 31 + i);
                p.rubble[i] = StackOn(p, FloorPose(k, r.part, r.side, r.index, r.span, l.x, sgn, dice.Value * 0.6f, dice.Value, dice.Value), i);
                any = true;
            }
            if (any) p.rubbleVersion++;
            return any;
        }

        /// <summary>Board pieces smaller than this (m², about 7 x 8 cm) are crumbs: never kept when tidying (<see cref="Tidy"/>).</summary>
        public const float CrumbArea = 0.006f;
        /// <summary>Pieces of rubble a wall keeps once tidied: the biggest few, so the floor still shows the wall was smashed.</summary>
        public const int KeepRubble = 6;
        static readonly List<(float area, int i)> s_bySize = new List<(float, int)>();

        /// <summary>The size of a piece of rubble (m², roughly what it covers of the floor): for keeping the biggest.</summary>
        static float Size(WallPanelModel m, Rubble r)
        {
            switch (r.part)
            {
                case Fallen.Part.Board: return Fits(m, r) ? m.boards[r.side].pieces[r.index].area : 0f;
                case Fallen.Part.Stud: return Mathf.Abs(r.span.y - r.span.x) * 0.09f;
                case Fallen.Part.Baseboard: return 0.05f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Sweeps a wall's rubble while it is out of sight (BrokenPanel, after <see cref="BrokenPanel.TidyAfter"/> seconds
        /// unseen, or as its chunk unloads): all but the <see cref="KeepRubble"/> biggest pieces go, crumbs always. Rubble
        /// costs little to draw (one mesh a wall); this keeps the floors, the saves and the rubble meshes from growing.
        /// </summary>
        public static bool Tidy(Panel p)
        {
            var m = p.model; int before = p.rubble.Count;
            p.rubble.RemoveAll(r => r.part == Fallen.Part.Board && Size(m, r) < CrumbArea);
            if (p.rubble.Count > KeepRubble)
            {
                s_bySize.Clear();
                for (int i = 0; i < p.rubble.Count; i++) s_bySize.Add((Size(m, p.rubble[i]), i));
                s_bySize.Sort((a, b) => b.area.CompareTo(a.area));
                var keep = new HashSet<int>();
                for (int i = 0; i < KeepRubble; i++) keep.Add(s_bySize[i].i);
                int at = 0;
                p.rubble.RemoveAll(r => !keep.Contains(at++));
            }
            if (p.rubble.Count == before) return false;
            p.rubbleVersion++;
            return true;
        }

        /// <summary>
        /// Rubble has no colliders, so the next piece to fall lands inside the ones already lying there, and two plates at
        /// one height flicker through each other. A piece coming to rest on others is lifted onto them instead (a board's
        /// thickness above the highest it overlaps, with a little play so no two faces are ever level).
        /// </summary>
        static Rubble StackOn(Panel p, Rubble r, int upTo)
        {
            float Size(Rubble x) => x.part == Fallen.Part.Board && Fits(p.model, x) ? p.model.boards[x.side].pieces[x.index].radius * 1.2f
                                  : x.part == Fallen.Part.Baseboard ? 0.1f : 0.06f;
            float ri = Size(r);
            for (int pass = 0; pass < 6; pass++)
            {
                bool moved = false;
                for (int j = 0; j < upTo && j < p.rubble.Count; j++)
                {
                    var e = p.rubble[j];
                    Vector2 d = new Vector2(r.position.x - e.position.x, r.position.z - e.position.z);
                    float reach = (ri + Size(e)) * 0.75f;
                    if (d.sqrMagnitude > reach * reach || Mathf.Abs(r.position.y - e.position.y) > WallSpec.Board * 0.9f) continue;
                    r.position.y = e.position.y + WallSpec.Board * 1.05f + 0.0004f * ((j * 7 + r.index) % 5);
                    moved = true;
                }
                if (!moved) break;
            }
            return r;
        }

        /// <summary>Lifts every piece of a wall's rubble off the ones under it (a save from before the stacking).</summary>
        static void Restack(Panel p)
        {
            for (int i = 1; i < p.rubble.Count; i++) p.rubble[i] = StackOn(p, p.rubble[i], i);
        }

        public static void Settle(WallKey k, Rubble r)
        {
            if (!s_panels.TryGetValue(k, out var p)) return;
            p.rubble.Add(StackOn(p, r, p.rubble.Count));
            if (p.rubble.Count > 320) p.rubble.RemoveAt(0);      // the oldest small bits are swept into the dust
            p.rubbleVersion++;
        }

        // ---------------- Saving ----------------

        /// <summary>Every struck panel, for the save.</summary>
        public static List<Saves.WallData> Export()
        {
            var list = new List<Saves.WallData>(s_panels.Count);
            foreach (var kv in s_panels)
            {
                if (!s_taken.ContainsKey(kv.Key)) continue;
                var st = kv.Value.state; var k = kv.Key;
                var d = new Saves.WallData
                {
                    vertical = k.vertical, line = k.line, along = k.along, storey = k.storey,
                    board0 = WallPanelState.Bits(st.removed[0]), board1 = WallPanelState.Bits(st.removed[1]),
                    crack0 = WallPanelState.Bits(st.cracked[0]), crack1 = WallPanelState.Bits(st.cracked[1]),
                    baseboard = st.baseboardGone, insulation = WallPanelState.Bits(st.insulationGone),
                };
                var studs = new List<float>();
                for (int s = 0; s < st.studSegments.Length; s++)
                {
                    studs.Add(st.studSegments[s].Count);
                    foreach (var seg in st.studSegments[s]) { studs.Add(seg.x); studs.Add(seg.y); }
                    studs.AddRange(st.studDamage[s]);
                }
                d.studs = studs.ToArray();
                d.baseboardDamage = (float[])st.baseboardDamage.Clone();
                d.rubble = new List<Rubble>(kv.Value.rubble);
                list.Add(d);
            }
            return list;
        }

        /// <summary>Puts saved panels back (before any chunk is built, so the chunks leave them out).</summary>
        public static void Import(List<Saves.WallData> list)
        {
            if (list == null) return;
            var taken = new Dictionary<WallKey, float>(s_taken);
            foreach (var d in list)
            {
                var k = new WallKey(d.vertical, d.line, d.along, d.storey);
                var p = Get(k, true);
                if (p == null) { Debug.LogWarning($"[Backrooms] A saved broken wall ({k}) is not breakable in this world; left out."); continue; }
                var st = p.state;
                WallPanelState.ReadBits(d.board0, st.removed[0]); WallPanelState.ReadBits(d.board1, st.removed[1]);
                WallPanelState.ReadBits(d.crack0, st.cracked[0]); WallPanelState.ReadBits(d.crack1, st.cracked[1]);
                WallPanelState.ReadBits(d.insulation, st.insulationGone);
                st.baseboardGone = d.baseboard;
                if (d.baseboardDamage != null) for (int i = 0; i < Mathf.Min(6, d.baseboardDamage.Length); i++) st.baseboardDamage[i] = d.baseboardDamage[i];
                if (d.studs != null && !p.model.steel)                 // steel studs do not break (since 2026-10-27): whole
                {
                    int at = 0;
                    for (int s = 0; s < st.studSegments.Length && at < d.studs.Length; s++)
                    {
                        int n = (int)d.studs[at++];
                        st.studSegments[s].Clear();
                        for (int i = 0; i < n && at + 1 < d.studs.Length; i++, at += 2) st.studSegments[s].Add(new Vector2(d.studs[at], d.studs[at + 1]));
                        for (int i = 0; i < WallPanelState.DamageBins && at < d.studs.Length; i++) st.studDamage[s][i] = d.studs[at++];
                    }
                }
                if (d.rubble != null)
                    foreach (var rb in d.rubble)
                        if (Fits(p.model, rb)) p.rubble.Add(rb);       // not batts (off for now), nor parts the wall no longer has
                SettleLoose(p);
                DropLodged(p);
                Restack(p);
                Refresh(p);
                st.version++;
                taken[k] = p.model.height;
            }
            s_taken = taken;
            Version++;
        }

        /// <summary>
        /// Is a piece of rubble a part this wall has? A save from before a change of the wall's make-up can name one it no
        /// longer has (a second steel stud, batts): building it would fail, and with it the chunk.
        /// </summary>
        public static bool Fits(WallPanelModel m, Rubble r)
        {
            switch (r.part)
            {
                case Fallen.Part.Board: return (r.side == 0 || r.side == 1) && r.index >= 0 && r.index < m.boards[r.side].pieces.Count;
                case Fallen.Part.Baseboard: return (r.side == 0 || r.side == 1) && r.index >= 0 && r.index < WallPanelModel.BaseboardPieces;
                case Fallen.Part.Stud: return r.index >= 0 && r.index < m.studs.Length;
                default: return WallPanelModel.InsulationOn && m.insulated && r.index >= 0 && r.index < m.bays.Count * m.rows;
            }
        }

        // ---------------- Helpers ----------------

        static bool RayBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float t)
        {
            float t0 = 0f, t1 = float.MaxValue;
            for (int a = 0; a < 3; a++)
            {
                if (Mathf.Abs(d[a]) < 1e-8f) { if (o[a] < min[a] || o[a] > max[a]) { t = 0f; return false; } continue; }
                float inv = 1f / d[a], ta = (min[a] - o[a]) * inv, tb = (max[a] - o[a]) * inv;
                if (ta > tb) { float x = ta; ta = tb; tb = x; }
                t0 = Mathf.Max(t0, ta); t1 = Mathf.Min(t1, tb);
                if (t0 > t1) { t = 0f; return false; }
            }
            t = t0;
            return true;
        }
    }
}
