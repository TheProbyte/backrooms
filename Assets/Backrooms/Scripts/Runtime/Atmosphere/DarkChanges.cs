using System.Collections;
using System.Collections.Generic;
using Backrooms.Audio;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.Atmosphere
{
    /// <summary>
    /// The dark changes things while you are away (2026-11-04, the brief's "unattended dark areas can occasionally undergo
    /// persistent disturbances"). Now and then (every several minutes at most, and only when there is somewhere it can),
    /// a piece of the building's furniture in a dark room out of your sight is found moved: slid 0.3-1.5 m and turned,
    /// with the scrape marks of its legs or its edges in the carpet along the way, or a light chair knocked over. Most
    /// visits are uneventful; nothing announces it. Within earshot (but still out of sight) it happens slowly and you hear
    /// it, the drag of it across the floor; further off it has simply happened when you come back.
    /// What it leaves alone: anything you moved (your barricades and furniture are yours), the build-mode rooms' pinned
    /// pieces, anything within reach of a lamp of yours that is lit, and rooms that have not been dark for a while (a
    /// flicker is not dark: <see cref="Grace"/>). Dark: the room's lights are off at its switch or were never lit
    /// (LightMode.Dark). Saved with the world: the moved furniture with the moved props (MovablePropManager), the scrape
    /// marks with the marks (Marks, as faint chalk-kind strips the colour of crushed pile).
    /// Done the way "unobserved change" is done in horror games: chosen while unseen, checked again at the moment it
    /// happens (still unseen, still dark, still away), and remembered from then on. Off with the Atmosphere slider at 0.
    /// </summary>
    public class DarkChanges : MonoBehaviour
    {
        public static DarkChanges Instance { get; private set; }
        /// <summary>What it did last (for the developer menu).</summary>
        public static string LastEvent = "nothing yet";
        public static int Count { get; private set; }

        /// <summary>How long a room must have been dark (s): a flicker or a blink is not dark.</summary>
        public const float Grace = 20f;
        /// <summary>Nearer than this (m) you are attending to it.</summary>
        public const float MinAway = 11f;
        /// <summary>Within this (m) it is heard: it happens slowly, with its sound.</summary>
        public const float Earshot = 26f;
        /// <summary>A lamp of yours that is lit keeps this far (m) around it as it is.</summary>
        public const float LampSafe = 5f;

        float _next;
        Pending _pending;
        CamcorderPlayerController _player;
        readonly System.Random _rng = new System.Random();
        readonly Collider[] _over = new Collider[24];

        class Pending { public MovableProp prop; public float at; public bool tip; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance) return;
            var go = new GameObject("DarkChanges");
            DontDestroyOnLoad(go);
            go.AddComponent<DarkChanges>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; LastEvent = "nothing yet"; Count = 0; }

        void Awake() { Instance = this; _next = Time.time + Range(150f, 300f); }

        void Update()
        {
            var ws = WorldStreamer.Current;
            if (!_player) _player = FindAnyObjectByType<CamcorderPlayerController>();
            float strength = TensionDirector.Strength;
            if (!ws || ws.MenuMode || !_player || _player.noclip || strength <= 0.01f) { _pending = null; return; }
            if (_pending != null)
            {
                if (Time.time < _pending.at) return;
                var p = _pending; _pending = null;
                // Checked again now: still out of sight, still dark (all the grace period through), still away.
                bool done = p.prop && Candidate(p.prop, out float d) && Act(p.prop, d, p.tip, false);
                _next = Time.time + (done ? Range(300f, 660f) : Range(30f, 60f)) / Mathf.Max(0.3f, strength);
                return;
            }
            if (Time.time < _next) return;
            _next = Time.time + Range(30f, 60f);                 // (nowhere it could: look again later)
            var c = Pick(out bool tip);
            if (c) _pending = new Pending { prop = c, at = Time.time + Grace, tip = tip };
        }

        // ---------------- Choosing ----------------

        /// <summary>A piece it could move now (weighted: chairs and boxes often, an armchair or a table seldom).</summary>
        MovableProp Pick(out bool tip)
        {
            tip = false;
            var list = new List<(MovableProp p, float w)>();
            float total = 0f;
            foreach (var mp in MovableProp.Live)
            {
                if (!mp || !Candidate(mp, out _)) continue;
                float w = Weight(mp.Spec.kind);
                if (w <= 0f) continue;
                list.Add((mp, w)); total += w;
            }
            if (list.Count == 0) return null;
            float r = (float)_rng.NextDouble() * total;
            foreach (var (p, w) in list)
            {
                r -= w;
                if (r > 0f) continue;
                tip = Light(p.Spec.kind) && _rng.NextDouble() < 0.3;
                return p;
            }
            return list[list.Count - 1].p;
        }

        static float Weight(MovableKind k)
        {
            switch (k)
            {
                case MovableKind.OfficeChair: case MovableKind.WoodenChair: case MovableKind.LawnChair: case MovableKind.SchoolChair: return 3f;
                case MovableKind.Box: return 2f;
                case MovableKind.Armchair: case MovableKind.Table: return 0.6f;
                case MovableKind.Sofa: return 0.25f;
                default: return 0f;
            }
        }

        static bool Light(MovableKind k) => k == MovableKind.WoodenChair || k == MovableKind.LawnChair || k == MovableKind.SchoolChair || k == MovableKind.OfficeChair;

        /// <summary>Out of sight, away from you, in a room that is dark and has been, and not yours to keep.</summary>
        bool Candidate(MovableProp mp, out float distance)
        {
            distance = 0f;
            if (mp.held || mp.Pinned || mp.Spec == null) return false;
            var mgr = MovablePropManager.Instance;
            if (mgr && mgr.IsMoved(mp.Spec.id)) return false;            // you moved it (or the dark did once already)
            if (!OnFloor(mp)) return false;                                 // (not off a shelf or a stack: it is dragged)
            Vector3 c = Centre(mp);
            Vector3 eye = Eye();
            Vector3 flat = c - eye; float dy = Mathf.Abs(flat.y); flat.y = 0f;
            distance = flat.magnitude;
            bool otherStorey = dy > 4f;
            if (!otherStorey && distance < MinAway) return false;
            if (distance > 60f) return false;
            if (Seen(mp, c)) return false;
            return Dark(c);
        }

        /// <summary>Standing on its room's floor (its bottom within a hand's breadth of it).</summary>
        static bool OnFloor(MovableProp mp)
        {
            var ws = WorldStreamer.Current;
            var b = Bounds(mp);
            Vector3 p = b.center;
            var L = ws ? ws.LayoutAt(WorldStreamer.ChunkOf(p)) : null;
            if (L == null) return false;
            int x = Mathf.FloorToInt(p.x / OfficeBiome.CellSize) - L.coord.x * L.N, z = Mathf.FloorToInt(p.z / OfficeBiome.CellSize) - L.coord.y * L.N;
            if (!L.InChunk(x, z)) return false;
            return Mathf.Abs(b.min.y - (L.BaseY + L.FloorAt(x, z))) < 0.12f;
        }

        /// <summary>Its room's lights are off (at its switch, or never lit) and no lamp of yours is lit near it.</summary>
        public static bool Dark(Vector3 p)
        {
            var ws = WorldStreamer.Current;
            if (!ws) return false;
            var L = ws.LayoutAt(WorldStreamer.ChunkOf(p));
            if (L == null || L.style == ZoneStyle.Pool) return false;
            int x = Mathf.FloorToInt(p.x / OfficeBiome.CellSize) - L.coord.x * L.N, z = Mathf.FloorToInt(p.z / OfficeBiome.CellSize) - L.coord.y * L.N;
            if (!L.InChunk(x, z)) return false;
            var room = L.RoomAt(x, z);
            if (room.custom || room.home || room.special != GoodRoom.None) return false;      // the good rooms and the player's built rooms keep
            bool dark = room.lights == LightMode.Dark || LightZones.IsOff(new Vector3Int(L.coord.x, L.storey, L.coord.y), L.roomOf[x, z]);
            if (!dark) return false;
            foreach (var it in WorldItem.Live)
            {
                if (!it) continue;
                bool lit = it is DeskLamp dl ? dl.Lit : it is FloorLamp fl && fl.Lit;
                if (lit && (it.transform.position - p).sqrMagnitude < LampSafe * LampSafe) return false;
            }
            return true;
        }

        bool Seen(MovableProp mp, Vector3 c)
        {
            var cam = Camera.main;
            if (!cam) return false;
            float r = Mathf.Max(0.3f, Extent(mp));
            // In the view's cone (a margin round it) and not behind something solid.
            Vector3 v = cam.WorldToViewportPoint(c);
            float m = 0.12f;
            if (v.z <= -r || v.x < -m || v.x > 1f + m || v.y < -m || v.y > 1f + m) return false;
            Vector3 from = cam.transform.position, to = c - from;
            int n = Physics.RaycastNonAlloc(from, to.normalized, s_hits, Mathf.Max(0f, to.magnitude - r), ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var t = s_hits[i].collider.transform;
                if (t.IsChildOf(mp.transform) || (_player && t.IsChildOf(_player.transform))) continue;
                if (s_hits[i].collider.attachedRigidbody && s_hits[i].collider.attachedRigidbody.GetComponent<MovableProp>()) continue;   // furniture does not hide it
                return false;                                              // a wall between
            }
            return true;
        }
        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        Vector3 Eye() { var cam = Camera.main; return cam ? cam.transform.position : _player.transform.position + Vector3.up * 1.6f; }

        static Vector3 Centre(MovableProp mp)
        {
            var b = Bounds(mp);
            return b.size.sqrMagnitude > 0f ? b.center : mp.transform.position;
        }

        static float Extent(MovableProp mp) => Bounds(mp).extents.magnitude;

        static Bounds Bounds(MovableProp mp)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var col in mp.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger) continue;
                if (!any) { b = col.bounds; any = true; } else b.Encapsulate(col.bounds);
            }
            return b;
        }

        // ---------------- Doing it ----------------

        /// <summary>Moves (or knocks over) the piece: a new pose clear of everything in the same room, scrape marks along
        /// the way, slowly and heard if you are near enough, at once otherwise. False if it found no room to.</summary>
        bool Act(MovableProp mp, float distance, bool tip, bool forceHeard)
        {
            var mgr = MovablePropManager.Instance;
            if (!mgr) return false;
            var b = Bounds(mp);
            float floor = b.min.y;
            Vector3 half = new Vector3(b.extents.x, b.extents.y, b.extents.z);
            Vector3 p0 = mp.transform.position;
            Quaternion r0 = mp.transform.rotation;
            var key = RoomKey(p0);
            if (!key.HasValue) return false;
            bool heard = forceHeard || distance < Earshot;
            if (tip && !heard)
            {
                // Knocked over: tilted most of the way onto a side, let physics drop it the rest.
                float side = _rng.NextDouble() < 0.5 ? -1f : 1f;
                var r1 = Quaternion.AngleAxis(side * Range(62f, 72f), mp.transform.forward) * Quaternion.Euler(0f, Range(-30f, 30f), 0f) * r0;
                mgr.Detach(mp);
                mp.transform.SetPositionAndRotation(p0 + Vector3.up * 0.06f, r1);
                mp.Body.position = mp.transform.position; mp.Body.rotation = r1;
                mp.Wake();
                Note($"a {Name(mp)} was knocked over in a dark room {distance:0} m away");
                return true;
            }
            float maxD = Light(mp.Spec.kind) ? 1.5f : mp.Spec.kind == MovableKind.Box ? 1.1f : 0.7f;
            for (int tries = 0; tries < 14; tries++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                float d = Range(0.35f, maxD);
                Vector3 p1 = p0 + new Vector3(dir.x, 0f, dir.y) * d;
                Quaternion r1 = Quaternion.Euler(0f, Range(-45f, 45f), 0f) * r0;
                if (RoomKey(p1) != key || !Clear(mp, b, p0, p1, r0, r1)) continue;
                mgr.Detach(mp);
                Scrapes(mp, p0, r0, p1, r1, floor);
                if (heard) StartCoroutine(Slide(mp, p0, r0, p1, r1, Mathf.Clamp(d * 2.2f, 1.2f, 3.2f)));
                else Place(mp, p1, r1);
                Note($"a {Name(mp)} moved {d:0.0} m in a dark room {distance:0} m away" + (heard ? " (heard)" : ""));
                return true;
            }
            return false;
        }

        /// <summary>Nothing in the way at the new pose (the floor and the piece itself aside), and nothing along the way.</summary>
        bool Clear(MovableProp mp, Bounds b, Vector3 p0, Vector3 p1, Quaternion r0, Quaternion r1)
        {
            Vector3 lift = Vector3.up * 0.06f;
            Vector3 local = Quaternion.Inverse(r0) * (b.center - p0);
            for (int k = 1; k <= 3; k++)
            {
                float t = k / 3f;
                Vector3 p = Vector3.Lerp(p0, p1, t);
                Quaternion r = Quaternion.Slerp(r0, r1, t);
                Vector3 c = p + r * local + lift;
                Vector3 h = new Vector3(Mathf.Max(0.05f, b.extents.x - 0.02f), Mathf.Max(0.05f, b.extents.y - 0.07f), Mathf.Max(0.05f, b.extents.z - 0.02f));
                int n = Physics.OverlapBoxNonAlloc(c, h, _over, Quaternion.Euler(0f, r.eulerAngles.y - r0.eulerAngles.y, 0f), ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var col = _over[i];
                    if (col.transform.IsChildOf(mp.transform)) continue;
                    return false;
                }
            }
            return true;
        }

        /// <summary>The marks it leaves: its feet's (a chair, a table) or its bottom edges' (a box, a sofa) drag lines in the
        /// carpet, from where it stood to where it stands, following its turn.</summary>
        static void Scrapes(MovableProp mp, Vector3 p0, Quaternion r0, Vector3 p1, Quaternion r1, float floor)
        {
            var b = Bounds(mp);
            Vector3 lc = Quaternion.Inverse(r0) * (b.center - p0);
            // its footprint in its own frame (the world box turned back is a little big for a turned piece: shrunk a bit)
            Vector3 he = Quaternion.Inverse(r0) * b.extents;
            float hx = Mathf.Abs(he.x) * 0.8f, hz = Mathf.Abs(he.z) * 0.8f;
            bool legs = mp.Spec.kind != MovableKind.Box && mp.Spec.kind != MovableKind.Sofa && mp.Spec.kind != MovableKind.Armchair;
            if (mp.Spec.kind == MovableKind.OfficeChair) { hx *= 0.9f; hz *= 0.9f; }
            var feet = new List<Vector3>();
            if (legs) { foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f }) feet.Add(new Vector3(lc.x + sx * hx, 0f, lc.z + sz * hz)); }
            else { foreach (var sx in new[] { -1f, 1f }) feet.Add(new Vector3(lc.x + sx * hx * 0.9f, 0f, lc.z)); }
            float width = legs ? 0.018f : 0.05f;
            bool hard = HardFloor(p0);
            var col = hard ? new Color32(70, 70, 68, 70) : new Color32(48, 38, 20, 95);
            var marks = Marks.Instance;
            foreach (var f in feet)
            {
                var pts = new List<Vector3>();
                for (int k = 0; k <= 6; k++)
                {
                    float t = k / 6f;
                    Vector3 p = Vector3.Lerp(p0, p1, t) + Quaternion.Slerp(r0, r1, t) * f;
                    p.y = floor + 0.0005f;
                    pts.Add(p);
                }
                marks.AddScrape(pts, width, col);
            }
        }

        static bool HardFloor(Vector3 p)
        {
            var ws = WorldStreamer.Current;
            var L = ws ? ws.LayoutAt(WorldStreamer.ChunkOf(p)) : null;
            if (L == null) return false;
            int x = Mathf.FloorToInt(p.x / OfficeBiome.CellSize) - L.coord.x * L.N, z = Mathf.FloorToInt(p.z / OfficeBiome.CellSize) - L.coord.y * L.N;
            if (!L.InChunk(x, z)) return false;
            return DistrictGenerator.StyleOf(L.RoomAt(x, z)) == RoomStyle.Warehouse;
        }

        static void Place(MovableProp mp, Vector3 p, Quaternion r)
        {
            mp.Body.isKinematic = true;
            mp.transform.SetPositionAndRotation(p, r);
            mp.Body.position = p; mp.Body.rotation = r;
            MovablePropManager.Instance?.Store(mp);
        }

        /// <summary>Slowly, with the sound of it: a few drags and stops across the floor (someone pulling it).</summary>
        IEnumerator Slide(MovableProp mp, Vector3 p0, Quaternion r0, Vector3 p1, Quaternion r1, float time)
        {
            var go = new GameObject("DarkScrape");
            go.transform.SetParent(mp.transform, false);
            var src = ItemModels.Source(go, 30f);
            int kind = HardFloor(p0) ? 1 : mp.Spec.kind == MovableKind.OfficeChair ? 2 : 0;
            src.clip = ProceduralSounds.ScrapeLoop(kind);
            src.loop = true; src.volume = 0f; src.minDistance = 1.2f;
            src.Play();
            mp.Body.isKinematic = true;
            float t = 0f;
            // three pulls, a breath between
            float[] stops = { 0.38f, 0.72f, 1f };
            int s = 0;
            float pause = 0f;
            while (t < 1f && mp)
            {
                if (pause > 0f) { pause -= Time.deltaTime; src.volume = Mathf.MoveTowards(src.volume, 0f, Time.deltaTime * 6f); yield return null; continue; }
                t = Mathf.Min(stops[s], t + Time.deltaTime / time);
                float e = t * t * (3f - 2f * t);
                var p = Vector3.Lerp(p0, p1, e);
                var r = Quaternion.Slerp(r0, r1, e);
                mp.Body.MovePosition(p); mp.Body.MoveRotation(r);
                mp.transform.SetPositionAndRotation(p, r);
                src.volume = Mathf.MoveTowards(src.volume, 0.55f, Time.deltaTime * 5f);
                src.pitch = 0.85f + 0.3f * Mathf.PerlinNoise(t * 6f, 0.3f);
                if (t >= stops[s] && s < stops.Length - 1) { s++; pause = Range(0.25f, 0.8f); }
                yield return null;
            }
            if (mp) Place(mp, p1, r1);
            float fade = 0.3f;
            while (fade > 0f && src) { fade -= Time.deltaTime; src.volume = Mathf.MoveTowards(src.volume, 0f, Time.deltaTime * 3f); yield return null; }
            if (go) Destroy(go);
        }

        static (Vector3Int, int)? RoomKey(Vector3 p)
        {
            var ws = WorldStreamer.Current;
            var L = ws ? ws.LayoutAt(WorldStreamer.ChunkOf(p)) : null;
            if (L == null) return null;
            int x = Mathf.FloorToInt(p.x / OfficeBiome.CellSize) - L.coord.x * L.N, z = Mathf.FloorToInt(p.z / OfficeBiome.CellSize) - L.coord.y * L.N;
            if (!L.InChunk(x, z) || L.Has(x, z, CellFlags.NoFloor)) return null;
            return (new Vector3Int(L.coord.x, L.storey, L.coord.y), L.RoomAt(x, z).globalId);
        }

        static string Name(MovableProp mp)
        {
            switch (mp.Spec.kind)
            {
                case MovableKind.OfficeChair: return "office chair";
                case MovableKind.WoodenChair: return "wooden chair";
                case MovableKind.LawnChair: return "lawn chair";
                case MovableKind.SchoolChair: return "school chair";
                case MovableKind.Box: return "box";
                case MovableKind.Armchair: return "armchair";
                case MovableKind.Table: return "table";
                case MovableKind.Sofa: return "sofa";
                default: return "thing";
            }
        }

        void Note(string s) { LastEvent = s; Count++; Debug.Log("[DarkChanges] " + s); }

        float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        // ---------------- Testing ----------------

        /// <summary>Does it now (the developer menu): the nearest-suitable piece out of sight in a dark room, heard if
        /// asked, skipping the wait and the grace. anywhere: the room need not be dark (to try it in a lit world).</summary>
        public bool Force(bool heard, bool anywhere = false)
        {
            if (!_player) _player = FindAnyObjectByType<CamcorderPlayerController>();
            if (!_player) return false;
            var found = new List<(MovableProp p, float d)>();
            foreach (var mp in MovableProp.Live)
            {
                if (!mp || mp.held || mp.Pinned || Weight(mp.Spec.kind) <= 0f || !OnFloor(mp)) continue;
                var mgr = MovablePropManager.Instance;
                if (mgr && mgr.IsMoved(mp.Spec.id)) continue;
                Vector3 c = Centre(mp);
                Vector3 flat = c - Eye(); flat.y = 0f;
                float d = flat.magnitude;
                if (d < (heard ? 6f : MinAway) || d > (heard ? Earshot : 60f) || Seen(mp, c)) continue;
                if (!anywhere && !Dark(c)) continue;
                found.Add((mp, d));
            }
            found.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var (mp, d) in found)                               // the nearest that has room to move
                if (Act(mp, d, !heard && Light(mp.Spec.kind) && _rng.NextDouble() < 0.3, heard)) return true;
            return false;
        }
    }
}
