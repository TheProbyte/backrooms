using System.Collections.Generic;
using Backrooms.Audio;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.Destruction
{
    /// <summary>
    /// Breaking walls in the world being streamed. A blow (<see cref="Strike"/>, from the sledgehammer) finds what it hits
    /// along the swing: a wall is broken (<see cref="WallDamage.Strike"/>) and what came out of it falls as debris
    /// (<see cref="WallDebris"/>) or crumbles to dust, with the sound of what was hit; furniture and loose things are
    /// knocked; anything else just takes the blow. Keeps a <see cref="BrokenPanel"/> for every broken wall while a chunk on
    /// either side of it is loaded, and has those chunks rebuild their walls when one is first broken.
    /// Added by the WorldStreamer.
    /// </summary>
    public sealed class WallBreaker : MonoBehaviour
    {
        public static WallBreaker Instance { get; private set; }
        public WorldStreamer streamer;
        [Tooltip("Most pieces of wall falling at once; more come to rest straight away.")] public int maxDebris = 70;

        public enum Surface { None, Drywall, Steel, Wood, Batt, Metal, Hard, Soft, Floor, Item }

        /// <summary>What a blow met.</summary>
        public struct Impact
        {
            public bool hit;
            public Vector3 point, normal;
            public Surface surface;
            /// <summary>How much it gave (0 dead stop .. 1 the hammer went on through).</summary>
            public float give;
            public bool broke, through;
        }

        /// <summary>What the last blow met (for probes and the log).</summary>
        public static string LastBlow { get; private set; } = "";

        ParticleSystem _puffs, _crumbs;
        readonly List<AudioSource> _voices = new List<AudioSource>();
        int _voice;
        Collider _player;
        Transform _debrisRoot;
        static float s_landQuiet;
        static int s_landCount;

        void OnEnable()
        {
            Instance = this;
            if (!streamer) streamer = GetComponent<WorldStreamer>();
            if (streamer) { streamer.ChunkLoaded += OnChunkLoaded; streamer.ChunkUnloaded += OnChunkUnloaded; }
            WallDamage.TakenOver += OnTakenOver;
            WallDamage.Changed += OnChanged;
        }

        void OnDisable()
        {
            if (streamer) { streamer.ChunkLoaded -= OnChunkLoaded; streamer.ChunkUnloaded -= OnChunkUnloaded; }
            WallDamage.TakenOver -= OnTakenOver;
            WallDamage.Changed -= OnChanged;
            foreach (var v in _voices) if (v) v.Stop();
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            _debrisRoot = new GameObject("WallDebris").transform;
            _debrisRoot.SetParent(transform, false);
            // Walls broken in this world before (the save) whose chunks are already in.
            foreach (var kv in WallDamage.All) if (Loaded(kv.Key)) Ensure(kv.Key);
        }

        Collider Player
        {
            get
            {
                if (_player) return _player;
                var p = streamer && streamer.player ? streamer.player : null;
                _player = p ? p.GetComponent<CharacterController>() : null;
                return _player;
            }
        }

        // ---------------- Broken panels ----------------

        bool Loaded(WallKey k) => streamer && (streamer.LayoutAt(k.Chunk(0)) != null || streamer.LayoutAt(k.Chunk(1)) != null);

        void Ensure(WallKey k)
        {
            var p = WallDamage.Find(k);
            if (p == null || BrokenPanel.Live.ContainsKey(k)) return;
            // Called while a chunk comes in: whatever goes wrong with one broken wall must not stop the chunk (and its floor).
            try { BrokenPanel.Spawn(k, p, transform); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        void OnChunkLoaded(Vector3Int coord, GameObject go)
        {
            foreach (var kv in WallDamage.All)
                if (kv.Key.Chunk(0) == coord || kv.Key.Chunk(1) == coord) Ensure(kv.Key);
        }

        readonly List<WallKey> _drop = new List<WallKey>();

        void OnChunkUnloaded(Vector3Int coord, GameObject go)
        {
            _drop.Clear();
            foreach (var kv in BrokenPanel.Live)
            {
                var k = kv.Key;
                if (k.Chunk(0) != coord && k.Chunk(1) != coord) continue;
                var other = k.Chunk(0) == coord ? k.Chunk(1) : k.Chunk(0);
                if (other == coord || streamer.LayoutAt(other) == null) _drop.Add(k);
            }
            foreach (var k in _drop)
            {
                // Its debris still falling comes to rest where it is.
                foreach (var d in _debrisRoot.GetComponentsInChildren<WallDebris>()) if (d.key == k) d.Settle();
                if (BrokenPanel.Live.TryGetValue(k, out var b) && b) { b.Unloading(); Destroy(b.gameObject); }
                BrokenPanel.Live.Remove(k);
            }
        }

        void OnTakenOver(WallKey k)
        {
            // Its chunks stop drawing it (their walls are rebuilt off the main thread); the panel draws it from now on.
            if (streamer)
            {
                streamer.RebuildWalls(k.Chunk(0));
                if (k.Chunk(1) != k.Chunk(0)) streamer.RebuildWalls(k.Chunk(1));
            }
            Ensure(k);
        }

        void OnChanged(WallKey k)
        {
            if (BrokenPanel.Live.TryGetValue(k, out var b) && b) b.Rebuild();
            EraseMarks(k);
        }

        // ---------------- A blow ----------------

        static readonly RaycastHit[] s_hits = new RaycastHit[24];

        /// <summary>
        /// A blow along a ray (the hammer's face travelling into what the view is on): what it meets within reach takes it.
        /// energy: 0..1. ignore: the player (and what they hold).
        /// </summary>
        public Impact Strike(Vector3 origin, Vector3 dir, float reach, float energy, Transform ignore)
            => Strike(origin, dir, reach, Blow.Of(energy), ignore);

        /// <summary>A blow with its own break radius and crack spread (the sledgehammer's settings).</summary>
        public Impact Strike(Vector3 origin, Vector3 dir, float reach, Blow blow, Transform ignore)
        {
            float energy = Mathf.Clamp01(blow.energy);                               // sounds, dust, knocks
            var impact = new Impact();
            LastBlow = "nothing in reach";
            dir.Normalize();
            int n = Physics.RaycastNonAlloc(origin, dir, s_hits, reach + 0.3f, ~0, QueryTriggerInteraction.Ignore);
            // Nearest first.
            for (int i = 1; i < n; i++)
            {
                var h = s_hits[i]; int j = i - 1;
                while (j >= 0 && s_hits[j].distance > h.distance) { s_hits[j + 1] = s_hits[j]; j--; }
                s_hits[j + 1] = h;
            }
            var tried = new HashSet<WallKey>();
            for (int i = 0; i < n; i++)
            {
                var h = s_hits[i];
                if (!h.collider || (ignore && h.collider.transform.IsChildOf(ignore))) continue;
                if (h.collider.GetComponentInParent<WallDebris>()) continue;              // bits in the air
                if (h.distance > reach) break;
                // A wall: a broken panel, or a chunk's wall (a face standing on a cell line).
                WallKey key = default; bool wall = false;
                var panel = h.collider.GetComponentInParent<BrokenPanel>();
                if (panel) { key = panel.Key; wall = true; }
                else if (h.collider.GetComponent<GeneratedChunkResources>() && Mathf.Abs(h.normal.y) < 0.3f)
                    wall = WallKey.At(h.point, h.normal, Mathf.FloorToInt((h.point.y + 0.05f) / WallDamage.Pitch), out key);
                if (wall)
                {
                    if (!tried.Add(key)) continue;
                    var r = WallDamage.Strike(key, origin, dir, reach, blow);
                    if (r.kind == StrikeKind.Passed) continue;                          // through a hole: on to what is behind
                    if (r.kind == StrikeKind.None)
                    {
                        if (panel) continue;
                        return Hard(h, energy, ref impact);                              // a wall that does not break
                    }
                    return Struck(key, r, dir, energy, ref impact);
                }
                return Thing(h, dir, energy, ref impact);
            }
            return impact;
        }

        Impact Struck(WallKey k, StrikeResult r, Vector3 dir, float energy, ref Impact impact)
        {
            LastBlow = $"{r.kind} ({k}), {r.removed} out{(r.studBroke ? ", stud broke" : "")}{(r.through ? ", through" : "")}";
            impact.hit = true; impact.point = r.point; impact.normal = r.normal; impact.broke = r.removed > 0; impact.through = r.through;
            var p = WallDamage.Find(k);
            float vol = 0.55f + 0.45f * energy;
            int v = Random.Range(0, 3);
            switch (r.kind)
            {
                case StrikeKind.Board:
                case StrikeKind.BoardBack:
                    impact.surface = Surface.Drywall;
                    impact.give = r.through ? 0.8f : r.removed > 0 ? 0.45f : 0.12f;
                    Play(SoundBank.DrywallHit(v), r.point, vol * (r.kind == StrikeKind.BoardBack ? 0.7f : 1f), Random.Range(0.92f, 1.08f));
                    if (r.removed > 0) Play(SoundBank.DrywallCrumble(v), r.point, Mathf.Clamp01(0.35f + r.removed * 0.06f), Random.Range(0.94f, 1.06f));
                    Puff(r.point, r.normal, dir, 6 + Mathf.Min(54, r.removed * 7), new Color(0.93f, 0.92f, 0.88f, 0.32f));
                    break;
                case StrikeKind.Stud:
                    impact.surface = r.steel ? Surface.Steel : Surface.Wood;
                    impact.give = r.studBroke ? 0.5f : r.steel ? 0.12f : 0.04f;
                    Play(r.steel ? SoundBank.SteelStudHit(v) : SoundBank.WoodStudHit(v), r.point, vol, Random.Range(0.94f, 1.06f));
                    if (r.studBroke) Play(r.steel ? SoundBank.SteelTear : SoundBank.WoodCrack, r.point, 0.8f, Random.Range(0.95f, 1.05f));
                    if (!r.steel) Puff(r.point, r.normal, dir, 4, new Color(0.85f, 0.74f, 0.55f, 0.25f));
                    break;
                case StrikeKind.Insulation:
                    impact.surface = Surface.Batt; impact.give = 0.9f;
                    Play(SoundBank.BattWhump, r.point, 0.6f, Random.Range(0.9f, 1.1f));
                    Puff(r.point, r.normal, dir, 8, p != null && p.model.steel ? new Color(0.93f, 0.84f, 0.55f, 0.2f) : new Color(0.95f, 0.7f, 0.72f, 0.2f));
                    break;
                case StrikeKind.Baseboard:
                    impact.surface = Surface.Wood; impact.give = r.removed > 0 ? 0.4f : 0.06f;
                    Play(SoundBank.WoodStudHit(v), r.point, vol * 0.8f, Random.Range(1.1f, 1.25f));
                    if (r.removed > 0) Play(SoundBank.WoodCrack, r.point, 0.6f, Random.Range(1.05f, 1.2f));
                    break;
                case StrikeKind.Metal:
                    impact.surface = Surface.Metal; impact.give = 0.05f;
                    Play(SoundBank.MetalClank(v), r.point, vol, Random.Range(0.9f, 1.1f));
                    break;
                default:
                    impact.surface = Surface.Hard; impact.give = 0f;
                    Play(SoundBank.HardThud(v), r.point, vol, Random.Range(0.92f, 1.06f));
                    Puff(r.point, r.normal, dir, 3, new Color(0.9f, 0.9f, 0.87f, 0.2f));
                    break;
            }
            if (p != null) Spill(p, r);
            return impact;
        }

        /// <summary>What came out of a wall: debris that falls, or crumbs and dust for the smallest bits.</summary>
        void Spill(WallDamage.Panel p, StrikeResult r)
        {
            foreach (var f in r.fallen)
            {
                Vector3 at = f.part == Fallen.Part.Board ? PanelGeometry.PieceOrigin(p.model, f.side, f.index, WallDamage.Pitch) : r.point;
                if (f.crumble)
                {
                    Crumbs(at, f.velocity, 5);
                    Puff(at, Vector3.zero, f.velocity.normalized, 2, new Color(0.93f, 0.92f, 0.88f, 0.25f));
                    continue;
                }
                if (WallDebris.Active < maxDebris) WallDebris.Spawn(p, f, _debrisRoot, Player);
                else RestNow(p, f, at);
                Crumbs(at, f.velocity, 2);
            }
        }

        /// <summary>Too much in the air already: this piece lands straight away at the foot of the wall.</summary>
        void RestNow(WallDamage.Panel p, Fallen f, Vector3 at)
        {
            Vector3 drop = at + f.velocity * 0.15f;
            if (Physics.Raycast(drop + Vector3.up * 0.1f, Vector3.down, out var floor, 4f, ~0, QueryTriggerInteraction.Ignore)) drop = floor.point + Vector3.up * 0.008f;
            var rot = Quaternion.LookRotation(Random.onUnitSphere, Vector3.up);
            if (f.part == Fallen.Part.Board) rot = Quaternion.FromToRotation(p.model.key.Across, Vector3.up) * Quaternion.AngleAxis(Random.Range(0f, 360f), p.model.key.Across);
            WallDamage.Settle(p.model.key, new WallDamage.Rubble { part = f.part, side = f.side, index = f.index, span = f.span, position = drop, rotation = rot });
        }

        Impact Hard(RaycastHit h, float energy, ref Impact impact)
        {
            LastBlow = "a wall that does not break";
            impact.hit = true; impact.point = h.point; impact.normal = h.normal; impact.surface = Surface.Hard; impact.give = 0f;
            Play(SoundBank.HardThud(Random.Range(0, 3)), h.point, 0.55f + 0.45f * energy, Random.Range(0.92f, 1.06f));
            Puff(h.point, h.normal, -h.normal, 3, new Color(0.9f, 0.9f, 0.87f, 0.18f));
            return impact;
        }

        /// <summary>Not a wall: furniture and loose things are knocked (and woken), the floor and the rest just take it.</summary>
        Impact Thing(RaycastHit h, Vector3 dir, float energy, ref Impact impact)
        {
            LastBlow = "not a wall: " + h.collider.name;
            impact.hit = true; impact.point = h.point; impact.normal = h.normal;
            float impulse = 6f + 16f * energy;                    // N s: a 3 kg head at 2 to 7 m/s
            var prop = h.collider.GetComponentInParent<MovableProp>();
            var item = h.collider.GetComponentInParent<WorldItem>();
            var door = h.collider.GetComponentInParent<Door>();
            if (prop && !prop.held)
            {
                prop.Wake();
                prop.Body.AddForceAtPosition(dir * impulse, h.point, ForceMode.Impulse);
                impact.surface = Surface.Soft; impact.give = Mathf.Clamp01(0.6f - prop.Mass * 0.01f);
                Play(SoundBank.WoodStudHit(Random.Range(0, 3)), h.point, 0.5f + 0.4f * energy, Random.Range(0.7f, 0.85f));
                return impact;
            }
            if (item && !item.Held && item.Body && !item.Body.isKinematic)
            {
                item.Body.AddForceAtPosition(dir * impulse * 0.5f, h.point, ForceMode.Impulse);
                impact.surface = Surface.Item; impact.give = 0.8f;
                Play(SoundBank.MetalClank(Random.Range(0, 2)), h.point, 0.35f + 0.3f * energy, Random.Range(1.1f, 1.3f));
                return impact;
            }
            if (door)
            {
                door.Shove(door.SideOf(h.point - dir * 0.2f), h.point, dir, false);
                impact.surface = Surface.Wood; impact.give = 0.3f;
                Play(SoundBank.WoodStudHit(Random.Range(0, 3)), h.point, 0.6f + 0.4f * energy, Random.Range(0.75f, 0.9f));
                return impact;
            }
            if (h.rigidbody && !h.rigidbody.isKinematic) h.rigidbody.AddForceAtPosition(dir * impulse, h.point, ForceMode.Impulse);
            impact.surface = h.normal.y > 0.7f ? Surface.Floor : Surface.Hard;
            impact.give = h.normal.y > 0.7f ? 0.06f : 0f;
            Play(SoundBank.HardThud(Random.Range(0, 3)), h.point, (0.5f + 0.4f * energy) * (impact.surface == Surface.Floor ? 0.7f : 1f), Random.Range(0.9f, 1.05f));
            return impact;
        }

        // ---------------- Sound ----------------

        /// <summary>
        /// Plays a wall sound. Plain 3D voices, not Steam Audio ones: a pool of spatialised voices still playing as the
        /// editor left Play Mode crashed Steam Audio's mixer thread (2026-10-27); blows are close and loud anyway.
        /// </summary>
        void Play(AudioClip clip, Vector3 at, float volume, float pitch)
        {
            if (!clip) return;
            if (_voices.Count < 8)
            {
                var go = new GameObject("WallSound");
                go.transform.SetParent(transform, false);
                var a = go.AddComponent<AudioSource>();
                a.playOnAwake = false; a.spatialBlend = 1f; a.minDistance = 0.6f; a.maxDistance = 30f; a.dopplerLevel = 0f;
                a.rolloffMode = AudioRolloffMode.Logarithmic;
                _voices.Add(a);
            }
            var src = _voices[_voice];
            _voice = (_voice + 1) % 8;
            if (!src) return;
            src.transform.position = at;
            src.pitch = pitch;
            src.PlayOneShot(clip, Mathf.Clamp01(volume * SoundCatalog.Volume(SoundIds.WallBreak)));
        }

        /// <summary>A piece of wall landing (debris), quietly, and not too many at once.</summary>
        public static void DebrisLanded(Vector3 at, Fallen.Part part, bool steel, float strength)
        {
            var me = Instance;
            if (!me || strength < 0.05f) return;
            if (Time.time > s_landQuiet) { s_landQuiet = Time.time + 0.12f; s_landCount = 0; }
            if (++s_landCount > 3) return;
            int kind = part == Fallen.Part.Stud ? (steel ? 1 : 2) : part == Fallen.Part.Baseboard ? 2 : 0;
            if (part == Fallen.Part.Insulation) return;
            me.Play(SoundBank.DebrisLand(kind, Random.Range(0, 3)), at, 0.25f + 0.5f * strength, Random.Range(0.9f, 1.15f));
        }

        // ---------------- Dust ----------------

        ParticleSystem MakeSystem(string name, bool crumbs)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = crumbs ? 600 : 1500;
            main.startSpeed = 0f;
            main.gravityModifier = crumbs ? 1f : 0.012f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            if (crumbs) g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            else g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.6f, 0.45f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            if (!crumbs)
            {
                var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.6f));
                var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.dampen = 0.5f; limit.limit = 0.12f;
                var noise = ps.noise; noise.enabled = true; noise.strength = 0.06f; noise.frequency = 0.6f; noise.scrollSpeed = 0.2f;
            }
            else
            {
                var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
                c.quality = ParticleSystemCollisionQuality.Low; c.dampen = 0.6f; c.bounce = 0.15f; c.lifetimeLoss = 0f; c.radiusScale = 0.5f;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(DemolitionMaterials.Dust) { name = crumbs ? "Wall crumbs" : "Wall dust", hideFlags = HideFlags.DontSave };
            if (mat.HasProperty("_Hard")) mat.SetFloat("_Hard", crumbs ? 1f : 0f);
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>A cloud of gypsum dust from a blow, drifting off the wall and slowly settling.</summary>
        public void Puff(Vector3 at, Vector3 normal, Vector3 dir, int count, Color colour)
        {
            if (!_puffs) _puffs = MakeSystem("WallDust", false);
            var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector3 spread = Random.insideUnitSphere;
                e.position = at + spread * 0.06f + normal * 0.03f;
                e.velocity = (normal * Random.Range(0.1f, 0.6f) + dir * Random.Range(-0.1f, 0.5f) + spread * 0.35f + Vector3.down * 0.05f);
                e.startSize = Random.Range(0.07f, 0.24f);
                e.startLifetime = Random.Range(2.2f, 5f);
                var c = colour; c.a *= Random.Range(0.6f, 1.1f);
                e.startColor = c;
                e.rotation = Random.Range(0f, 360f);
                _puffs.Emit(e, 1);
            }
        }

        /// <summary>Crumbs of gypsum falling and bouncing on the floor.</summary>
        public void Crumbs(Vector3 at, Vector3 velocity, int count)
        {
            if (!_crumbs) _crumbs = MakeSystem("WallCrumbs", true);
            var e = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                e.position = at + Random.insideUnitSphere * 0.04f;
                e.velocity = velocity * Random.Range(0.3f, 0.9f) + Random.insideUnitSphere * 0.8f;
                e.startSize = Random.Range(0.006f, 0.018f);
                e.startLifetime = Random.Range(1.5f, 2.6f);
                float g = Random.Range(0.82f, 0.95f);
                e.startColor = new Color(g, g, g * 0.97f, 1f);
                _crumbs.Emit(e, 1);
            }
        }

        // ---------------- Paint and marks on what broke ----------------

        /// <summary>Paint, chalk and tape on board pieces that are gone go with them.</summary>
        static void EraseMarks(WallKey k)
        {
            var p = WallDamage.Find(k);
            if (p == null || !p.state.AnyRemoved) return;
            float pitch = WallDamage.Pitch;
            Vector3 a = k.ToWorld(0f, -0.05f, -WallSpec.T, pitch), b = k.ToWorld(WallSpec.C, p.model.height + 0.05f, WallSpec.T, pitch);
            var bounds = new Bounds((a + b) * 0.5f, Vector3.zero);
            bounds.Encapsulate(a); bounds.Encapsulate(b);
            bool Gone(Vector3 world)
            {
                var l = k.ToLocal(world, pitch);
                int side = l.z < 0f ? 0 : 1;
                if (Mathf.Abs(Mathf.Abs(l.z) - WallSpec.T * 0.5f) > 0.03f) return false;
                if (l.x < WallSpec.U0 || l.x > WallSpec.U1 || l.y < 0f || l.y > p.model.height) return false;
                return !p.state.BoardAt(p.model, side, new Vector2(l.x, l.y));
            }
            if (SprayPaint.Existing) SprayPaint.Existing.Erase(bounds, Gone);
            if (Marks.Existing) Marks.Existing.Erase(bounds, Gone);
        }
    }
}
