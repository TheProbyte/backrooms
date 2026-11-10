using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>The kinds of Overflow shelf (a shelf prop's variant: its type in the low bits, <see cref="Broken"/>).</summary>
    public static class ShelfTypes
    {
        public struct Type
        {
            public string name, model;
            public Vector3 size;          // width, height, depth (its back at -Z)
            public int levels;            // decks, the lowest 0.1 m up, the top one at the top
        }

        /// <summary>The ladder of storage (Docs/GameDesign.md section 10): starter shelving, taller shelving, pallet racking.
        /// Deck heights as the generated shelving's (OfficeChunkBuilder.Special): 0.1 m up, then evenly to the top.</summary>
        public static readonly Type[] All =
        {
            new Type { name = "SHELVING", model = "Shelving_110", size = new Vector3(1.1f, 2.0f, 0.46f), levels = 4 },
            new Type { name = "TALL SHELVING", model = "ShelvingTall", size = new Vector3(1.1f, 2.6f, 0.46f), levels = 5 },
            new Type { name = "PALLET RACKING", model = "PalletRack", size = new Vector3(2.7f, 2.6f, 1.0f), levels = 3 },
        };

        public const int Standard = 0, Tall = 1, Rack = 2;
        /// <summary>Knocked over (found out on the floor): stand it up with the hammer before it holds anything.</summary>
        public const int Broken = 1 << 8;

        public static Type Get(int variant) => All[Mathf.Clamp(variant & 0xF, 0, All.Length - 1)];
        public static bool IsBroken(int variant) => (variant & Broken) != 0;
        public static float Pitch(Type t) => (t.size.y - 0.12f) / (t.levels - 1);
        public static float DeckTop(Type t, int level) => 0.1f + level * Pitch(t);

        /// <summary>Its model (the standard shelving's until a type's own model exists).</summary>
        public static string Model(int variant)
        {
            var t = Get(variant);
            return FittingModels.Ready && FittingModels.Get(t.model) != null ? t.model : "Shelving_110";
        }
    }

    /// <summary>
    /// Overflow's shelves (2026-11-08, Docs/GameDesign.md section 8: "a box is stored only while it sits on a shelf in a lit
    /// spot"). Every one is a pinned moved prop (MovablePropManager keeps it, streams it, saves it): the starter shelf by the
    /// lift, kit shelves you put up, and knocked-over ones you find and stand up again. Storage is wherever they are.
    /// <list type="bullet">
    /// <item>Light is measured at each shelf, from your own lamps only (the ceiling lights go out at closing for the night):
    ///   like a stealth game's light gem, the light of every lit lamp is added up at points along each deck's front, a ray
    ///   to the lamp checking nothing solid is in the way. 1 is safe. <see cref="ShelfWatch"/> measures the shelves near
    ///   you and keeps the last value of the rest (saved).</item>
    /// <item>At night (<see cref="Night"/>) a stored box not on a safe shelf may be moved by the dark: the darker, the
    ///   likelier and the farther.</item>
    /// </list>
    /// </summary>
    public static class Shelves
    {
        public static ulong Id(int n) => 0xC4E9000000000000ul | (uint)n;
        public static bool IsShelfId(ulong id) => (id >> 48) == 0xC4E9ul;

        /// <summary>Light at a deck that counts as safe, and the bands below it (dim, dark; under that pitch black).</summary>
        public const float Safe = 0.95f, DimFrom = 0.6f, DarkFrom = 0.25f;
        /// <summary>Lamp light at a point that counts as full (Light.intensity over distance squared), and how much of a
        /// spot lamp's light reaches outside its cone (it lights the room it is in, not only its pool).</summary>
        // (0.25: tested 2026-11-08, the starter desk lamp a metre out makes the starter shelf safe, 0.96; two metres off,
        // only dim.)
        public static float Full = 0.25f, Spill = 0.35f;

        static WorldSaveData D => GameSession.Data;
        static readonly Dictionary<ulong, float> s_level = new Dictionary<ulong, float>();
        static WorldSaveData s_loadedFrom;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_level.Clear(); s_loadedFrom = null; }

        /// <summary>The save's light levels were replaced (co-op: the host's work arrived): read them again.</summary>
        public static void Reloaded() => s_loadedFrom = null;

        static void EnsureLoaded()
        {
            if (D == null || s_loadedFrom == D) return;
            s_loadedFrom = D;
            s_level.Clear();
            for (int i = 0; i < D.shelfLightIds.Count && i < D.shelfLight.Count; i++) s_level[D.shelfLightIds[i]] = D.shelfLight[i];
        }

        // ---------------- Making and finding them ----------------

        public static MovableSpec Spec(ulong id, int variant, Vector3 at, float yaw)
        {
            var streamer = WorldStreamer.Current;
            var a = streamer ? streamer.assets : null;
            var t = ShelfTypes.Get(variant);
            return new MovableSpec
            {
                id = id, kind = MovableKind.Shelf, renderMask = uint.MaxValue, proxyMaterial = a ? a.baseboard : null,
                position = at, yaw = yaw, size = t.size, variant = variant, pinned = true, fitting = ShelfTypes.Model(variant),
            };
        }

        /// <summary>A new shelf on the floor (its foot's middle at, its back to -forward of rot).</summary>
        public static ulong Add(int variant, Vector3 at, Quaternion rot)
        {
            var mgr = MovablePropManager.Instance;
            if (D == null || !mgr) return 0;
            // (Co-op: a joined game numbers its shelves from its own range, so two players putting one up at the same
            // moment never give them the same name, and tells the host.)
            ulong id = Net.WorkNet.Client ? Id(((Net.NetSession.LocalId + 1) << 24) | Random.Range(1, 1 << 24)) : Id(D.nextShelf++);
            D.shelves.Add(id);
            Net.WorkNet.Ask("shelf+ " + id);
            mgr.Place(Spec(id, variant, at, rot.eulerAngles.y), at, rot);
            return id;
        }

        /// <summary>A shelf taken down (packed back into its crate).</summary>
        public static void Remove(ulong id)
        {
            if (D == null) return;
            D.shelves.Remove(id);
            SetLevel(id, -1f);
            Net.WorkNet.Ask("shelf- " + id);
            Net.WorkNet.Changed();
            MovablePropManager.Instance?.Forget(id);
        }

        public static bool Pose(ulong id, out Vector3 at, out Quaternion rot, out int variant)
        {
            at = default; rot = Quaternion.identity; variant = 0;
            var mgr = MovablePropManager.Instance;
            if (!mgr || !mgr.Pose(id, out at, out rot)) return false;
            var spec = mgr.SpecOf(id);
            variant = spec != null ? spec.variant : 0;
            return true;
        }

        /// <summary>Is the point (a box's foot) on one of the shelves' decks (standing ones only), and which.</summary>
        public static bool On(Vector3 p, out ulong shelf)
        {
            shelf = 0;
            if (D == null) return false;
            foreach (var id in D.shelves)
            {
                if (!Pose(id, out var at, out var rot, out int v) || ShelfTypes.IsBroken(v)) continue;
                var t = ShelfTypes.Get(v);
                var l = Quaternion.Inverse(rot) * (p - at);
                if (Mathf.Abs(l.x) > t.size.x * 0.5f + 0.06f || l.z < -t.size.z * 0.5f - 0.05f || l.z > t.size.z * 0.5f + 0.15f) continue;
                if (l.y < 0.06f || l.y > t.size.y + 0.8f) continue;
                shelf = id;
                return true;
            }
            return false;
        }

        public static bool OnSafe(Vector3 p) => On(p, out var s) && Level(s) >= Safe;

        /// <summary>The nearest standing shelf's distance from a point (m), or infinity.</summary>
        public static float NearestSafe(Vector3 p)
        {
            float best = float.PositiveInfinity;
            if (D == null) return best;
            foreach (var id in D.shelves)
                if (Pose(id, out var at, out _, out int v) && !ShelfTypes.IsBroken(v) && Level(id) >= Safe)
                    best = Mathf.Min(best, Vector3.Distance(new Vector3(p.x, at.y, p.z), at));
            return best;
        }

        // ---------------- Standing a knocked-over one up ----------------

        /// <summary>Nails it takes to stand a knocked-over shelf up again (the hammer and nails, or later the repair kit).</summary>
        public const int MendNails = 4;
        static readonly Dictionary<ulong, int> s_mend = new Dictionary<ulong, int>();

        public static int MendLeft(ulong id) => MendNails - (s_mend.TryGetValue(id, out var n) ? n : 0);

        /// <summary>
        /// A nail driven into a prop (Hammer): if it is a knocked-over shelf, one more toward standing it up; at the last it
        /// stands up where it lay (its middle there, facing as it did), yours from then on. True if it was one.
        /// </summary>
        public static bool Mend(MovableProp p, int nails = 1)
        {
            if (!p || p.Spec.kind != MovableKind.Shelf || !ShelfTypes.IsBroken(p.Spec.variant) || D == null) return false;
            ulong id = p.Spec.id;
            s_mend[id] = (s_mend.TryGetValue(id, out var n) ? n : 0) + nails;
            if (s_mend[id] < MendNails) { CamcorderOSD.Notice($"{MendLeft(id)} MORE"); return true; }
            s_mend.Remove(id);
            var t = ShelfTypes.Get(p.Spec.variant);
            var tr = p.transform;
            var middle = tr.position + tr.rotation * new Vector3(0f, t.size.y * 0.5f, 0f);
            float floor = tr.position.y - t.size.z * 0.5f;
            float yaw = p.Spec.yaw;
            int v = p.Spec.variant & ~ShelfTypes.Broken;
            Remove(id);
            Add(v, new Vector3(middle.x, floor, middle.z), Quaternion.Euler(0f, yaw, 0f));
            CamcorderOSD.Notice("SHELF STOOD UP - IT'S YOURS");
            return true;
        }

        // ---------------- Light ----------------

        public static float Level(ulong id) { EnsureLoaded(); return s_level.TryGetValue(id, out var v) ? v : 0f; }

        /// <summary>0 safe, 1 dim, 2 dark, 3 pitch black.</summary>
        public static int Band(float level) => level >= Safe ? 0 : level >= DimFrom ? 1 : level >= DarkFrom ? 2 : 3;

        public static void SetLevel(ulong id, float level)
        {
            if (D == null) return;
            EnsureLoaded();
            int i = D.shelfLightIds.IndexOf(id);
            if (level < 0f)
            {
                s_level.Remove(id);
                if (i >= 0 && i < D.shelfLight.Count) { D.shelfLightIds.RemoveAt(i); D.shelfLight.RemoveAt(i); }
                return;
            }
            s_level[id] = level;
            if (i < 0 || i >= D.shelfLight.Count) { D.shelfLightIds.Add(id); D.shelfLight.Add(level); }
            else D.shelfLight[i] = level;
        }

        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        /// <summary>
        /// The building's own light at a point (2026-11-09, decision 6: any light reaching a shelf counts, the ceiling's
        /// included): a room with every tube working gives <see cref="CeilingAllOn"/>, one with some working
        /// <see cref="CeilingMixed"/>, a dark room (or one switched off) nothing. The tubes stay as they are now (no
        /// closing): working ones stay on, dead ones stay dead.
        /// </summary>
        public static float CeilingAt(Vector3 p)
        {
            var ws = WorldStreamer.Current;
            if (!ws) return 0f;
            var L = ws.LayoutAt(WorldStreamer.ChunkOf(p));
            if (L == null) return 0f;
            int x = Mathf.FloorToInt(p.x / OfficeBiome.CellSize) - L.coord.x * L.N, z = Mathf.FloorToInt(p.z / OfficeBiome.CellSize) - L.coord.y * L.N;
            if (!L.InChunk(x, z)) return 0f;
            var room = L.RoomAt(x, z);
            if (LightZones.IsOff(new Vector3Int(L.coord.x, L.storey, L.coord.y), L.roomOf[x, z])) return 0f;
            return room.lights == LightMode.AllOn ? CeilingAllOn : room.lights == LightMode.Mixed ? CeilingMixed : 0f;
        }

        /// <summary>The ceiling's share of a shelf's light (tune here if lamps end up mattering too little: about half the
        /// floor's rooms have every tube working).</summary>
        public static float CeilingAllOn = 1f, CeilingMixed = 0.5f;

        /// <summary>How lit a standing shelf is by these lamps' lights (0..1): the mean over points along each deck's front
        /// (two a deck), each point's light added up from every lamp that reaches it and capped at full.</summary>
        public static float Measure(MovableProp shelf, List<Light> lights)
        {
            if (!shelf || ShelfTypes.IsBroken(shelf.Spec.variant)) return 0f;
            var t = ShelfTypes.Get(shelf.Spec.variant);
            var tr = shelf.transform;
            float sum = 0f; int n = 0;
            for (int l = 0; l < t.levels; l++)
                for (int k = -1; k <= 1; k += 2)
                {
                    var p = tr.TransformPoint(new Vector3(k * t.size.x * 0.25f, ShelfTypes.DeckTop(t, l) + 0.18f, t.size.z * 0.5f + 0.04f));
                    float e = 0f;
                    foreach (var light in lights) e += At(light, p, shelf);
                    sum += Mathf.Clamp01(e / Full);
                    n++;
                }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>One lamp's light at a point (as URP lights it: intensity over distance squared, faded to nothing at its
        /// range; a spot's cone, with some spill outside it), or 0 if something solid is between.</summary>
        static float At(Light light, Vector3 p, MovableProp shelf)
        {
            if (!light || light.intensity < 0.01f) return 0f;
            Vector3 lp = light.transform.position, v = p - lp;
            float d2 = v.sqrMagnitude, r = light.range;
            if (d2 > r * r) return 0f;
            float f = d2 / (r * r), win = Mathf.Clamp01(1f - f * f);
            float e = light.intensity * win * win / Mathf.Max(d2, 0.25f);
            if (light.type == LightType.Spot)
            {
                float d = Mathf.Sqrt(d2);
                float c = Vector3.Dot(light.transform.forward, v / Mathf.Max(d, 1e-4f));
                float co = Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad), ci = Mathf.Cos(light.innerSpotAngle * 0.5f * Mathf.Deg2Rad);
                float s = Mathf.Clamp01((c - co) / Mathf.Max(ci - co, 1e-3f));
                e *= Mathf.Lerp(Spill, 1f, s * s * (3f - 2f * s));
            }
            if (e < 0.004f) return 0f;
            return Blocked(lp, p, shelf) ? 0f : e;
        }

        /// <summary>Something solid between a lamp and a point: walls, furniture; not the shelf itself, boxes, items (the lamp,
        /// its cord) or people.</summary>
        static bool Blocked(Vector3 from, Vector3 to, MovableProp shelf)
        {
            Vector3 dir = to - from;
            float d = dir.magnitude;
            if (d < 0.25f) return false;
            dir /= d;
            int n = Physics.RaycastNonAlloc(from + dir * 0.12f, dir, s_hits, d - 0.2f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = s_hits[i].collider;
                if (!c) continue;
                var mp = c.GetComponentInParent<MovableProp>();
                if (mp && (mp == shelf || mp.Spec.kind == MovableKind.Box)) continue;
                if (c.GetComponentInParent<WorldItem>()) continue;
                if (c is CharacterController || c.GetComponentInParent<CamcorderPlayerController>()) continue;
                return true;
            }
            return false;
        }

        // ---------------- At night ----------------

        public struct Report { public int safe, moved, gone; }

        /// <summary>
        /// The night after a working day: every stored box not on a safe shelf may be moved by the dark (Docs/GameDesign.md
        /// section 8): dim, it falls off or turns up a few metres away; dark, in a room nearby; pitch black (or dark three
        /// nights running), somewhere else on the floor, and now and then gone for good. Seeded by the world and the day, so
        /// co-op games move the same boxes to the same places.
        /// </summary>
        public static Report Night(WorldGenerator world, int seed, int slot, int day)
        {
            var rep = new Report();
            var mgr = MovablePropManager.Instance;
            if (D == null || !mgr || world == null) return rep;
            var rng = new System.Random(unchecked(seed * 92821 + day * 4099 + 0xDA2C));
            List<Vector3> cells = null;
            var keep = new List<ulong>(D.stored);
            foreach (var id in keep)
            {
                if (!mgr.Where(id, out var p, out bool held) || held) continue;
                int band;
                bool onShelf = On(p, out var shelf);
                if (onShelf) band = Band(Level(shelf));
                else band = NearestSafe(p) < 2.5f ? 1 : 2;               // on the floor: dim beside a lit shelf, else dark
                int nights = DarkNights(id, band >= 2);
                if (band == 0) { rep.safe++; continue; }
                double chance = band == 1 ? 0.35 : band == 2 ? 0.7 : 1.0;
                if (rng.NextDouble() >= chance) continue;
                bool far = band == 3 || nights >= 3;
                if (far && nights >= 3 && rng.NextDouble() < 0.04)
                {
                    D.stored.Remove(id);
                    ForgetDark(id);
                    mgr.Forget(id);
                    rep.gone++;
                    continue;
                }
                Vector3 to;
                if (band == 1)
                {
                    // Off the shelf onto the floor in front of it, or a few metres off.
                    if (onShelf && Pose(shelf, out var at, out var rot, out int v))
                    {
                        var t = ShelfTypes.Get(v);
                        to = at + rot * new Vector3((float)(rng.NextDouble() - 0.5) * t.size.x, 0.01f, t.size.z * 0.5f + 0.35f + (float)rng.NextDouble() * 0.9f);
                    }
                    else to = Near(rng, p, 1.5f, 4f, ref cells, world, slot);
                }
                else to = far ? Near(rng, p, 35f, 160f, ref cells, world, slot) : Near(rng, p, 6f, 25f, ref cells, world, slot);
                float yaw = (float)rng.NextDouble() * 360f;
                var spec = Contracts.BoxSpec(id, to, yaw);
                if (spec != null) mgr.Place(spec, to, Quaternion.Euler(0f, yaw, 0f));
                rep.moved++;
            }
            return rep;
        }

        static int DarkNights(ulong id, bool dark)
        {
            int i = D.darkIds.IndexOf(id);
            if (!dark) { if (i >= 0) ForgetDark(id); return 0; }
            if (i < 0 || i >= D.darkNights.Count) { D.darkIds.Add(id); D.darkNights.Add(1); return 1; }
            return ++D.darkNights[i];
        }

        static void ForgetDark(ulong id)
        {
            int i = D.darkIds.IndexOf(id);
            if (i >= 0 && i < D.darkNights.Count) { D.darkIds.RemoveAt(i); D.darkNights.RemoveAt(i); }
        }

        /// <summary>A floor cell's middle (an ordinary room on the leased floor) between near and far metres from a point; the
        /// closest try if none is.</summary>
        static Vector3 Near(System.Random rng, Vector3 from, float near, float far, ref List<Vector3> cells, WorldGenerator world, int slot)
        {
            if (cells == null) cells = FloorCells(world, slot);
            if (cells.Count == 0) return from;
            Vector3 best = cells[rng.Next(cells.Count)];
            float bestErr = float.MaxValue;
            for (int tries = 0; tries < 300; tries++)
            {
                var c = cells[rng.Next(cells.Count)];
                float d = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(from.x, from.z));
                float err = d < near ? near - d : d > far ? d - far : 0f;
                if (err < bestErr) { bestErr = err; best = c; }
                if (err <= 0f) break;
            }
            float j = OfficeBiome.CellSize * 0.3f;
            return best + new Vector3((float)(rng.NextDouble() - 0.5) * j, 0f, (float)(rng.NextDouble() - 0.5) * j);
        }

        /// <summary>The middles of the leased floor's ordinary room cells (the floor level, not crawlspaces or landmarks).</summary>
        static List<Vector3> FloorCells(WorldGenerator world, int slot)
        {
            var list = new List<Vector3>();
            const float C = OfficeBiome.CellSize;
            foreach (var d in LeasePlan.Districts(slot))
            {
                var L = world.District(new Vector3Int(d.x, 0, d.y));
                var o = LeasePlan.DistrictOrigin(d);
                for (int x = 0; x < L.N; x++)
                    for (int z = 0; z < L.N; z++)
                    {
                        if (L.roomOf[x, z] < 0) continue;
                        var r = L.RoomAt(x, z);
                        if (r.landmark != null || r.custom || r.noFloor || r.kind == RoomKind.Crawl || r.ramp != 0 || Mathf.Abs(r.floor) > 0.01f) continue;
                        list.Add(o + new Vector3((x + 0.5f) * C, 0.01f, (z + 0.5f) * C));
                    }
            }
            return list;
        }
    }

    /// <summary>
    /// On the contract floor (added by Interaction.Contracts): measures the light at the shelves near you a few times a
    /// second (Shelves.Measure, kept per shelf), and shows the camcorder's LOW LIGHT warning when you look at a shelf for a
    /// moment and it is not lit enough to be safe.
    /// </summary>
    public class ShelfWatch : MonoBehaviour
    {
        readonly List<Light> _lights = new List<Light>();
        readonly List<Light> _userLights = new List<Light>();
        readonly List<MovableProp> _live = new List<MovableProp>();
        float _next, _nextFind;
        int _cursor;
        float _gaze, _alpha;
        ulong _gazed;

        void Update()
        {
            var d = GameSession.Data;
            if (d == null) return;
            if (Time.time >= _next)
            {
                _next = Time.time + 0.3f;
                _lights.Clear();
                // (2026-11-08 profile: searching the scene for the lamps each pass spiked; the lamps change rarely.)
                if (Time.time >= _nextFind)
                {
                    _nextFind = Time.time + 2f;
                    _userLights.Clear();
                    foreach (var u in FindObjectsByType<PowerUser>(FindObjectsSortMode.None)) _userLights.AddRange(u.GetComponentsInChildren<Light>());
                }
                foreach (var l in _userLights) if (l && l.intensity > 0.01f) _lights.Add(l);
                foreach (var l in Machines.Lights) if (l && l.intensity > 0.01f) _lights.Add(l);      // floodlights
                _live.Clear();
                foreach (var id in d.shelves) { var mp = MovableProp.Find(id); if (mp) { _live.Add(mp); ShelfTicket.Ensure(mp); } }
                ShelfTickets.Refresh();
                // A few shelves a pass (all of them every second or so).
                for (int k = 0; k < Mathf.Min(_live.Count, 8); k++)
                {
                    var mp = _live[_cursor++ % _live.Count];
                    float level = ShelfTypes.IsBroken(mp.Spec.variant) ? 0f : Mathf.Clamp01(Shelves.Measure(mp, _lights) + Shelves.CeilingAt(mp.transform.position + Vector3.up * 0.5f));
                    // (Co-op: what a joined game measures at the shelves near its player is told to the host, which may be far off.)
                    if (Net.WorkNet.Client && Mathf.Abs(level - Shelves.Level(mp.Spec.id)) > 0.04f)
                        Net.WorkNet.Ask("light " + mp.Spec.id + " " + level.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                    Shelves.SetLevel(mp.Spec.id, level);
                }
            }
            Gaze();
        }

        void Gaze()
        {
            var cam = Camera.main;
            ulong looked = 0;
            if (cam && Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                var mp = hit.collider.GetComponentInParent<MovableProp>();
                if (mp && mp.Spec.kind == MovableKind.Shelf && !ShelfTypes.IsBroken(mp.Spec.variant)) looked = mp.Spec.id;
                else if (mp && mp.Spec.kind == MovableKind.Box && Shelves.On(mp.transform.position, out var s)) looked = s;
            }
            if (looked != 0 && looked == _gazed) _gaze += Time.deltaTime; else _gaze = 0f;
            _gazed = looked;
            bool show = looked != 0 && _gaze > 0.6f && Shelves.Level(looked) < Shelves.Safe;
            _alpha = Mathf.MoveTowards(_alpha, show ? 1f : 0f, Time.deltaTime * (show ? 2.5f : 4f));
            CamcorderOSD.LowLight = _alpha;
        }

        void OnDisable() { CamcorderOSD.LowLight = 0f; }
    }
}
