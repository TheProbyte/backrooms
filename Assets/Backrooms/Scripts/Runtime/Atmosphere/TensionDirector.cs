using System.Collections;
using System.Collections.Generic;
using Backrooms.Audio;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.Atmosphere
{
    /// <summary>
    /// Paces the dread (2026-10-28), the way Left 4 Dead's AI Director and Alien: Isolation's director do: it keeps a
    /// running estimate of how tense the player is, and moves through a cycle of phases so that quiet stretches make the
    /// next thing count. It is the shadow's (<see cref="ShadowStalker"/>) "macro brain": it sends it, lets it come close at
    /// the peak, and calls it off.
    ///
    ///  - Relax: nothing of its own (the building still creaks and hums by itself). The shadow is gone.
    ///  - Build-up: the shadow comes, out of sight, and stalks; and now and then, rarely at first: the lights where you are
    ///    blink, a room nearby goes dark, a door out of sight swings shut, the power sags.
    ///  - Peak: the shadow is bold (it may come right up behind you); the lights of a room nearby die, a deep sag.
    ///  - Fade: the shadow leaves, the building goes quiet (the ambience drops away), then back to Relax.
    /// (2026-10-31: no more sounds of its own: the knocks in walls, things in ducts and footsteps are gone; the owner did
    /// not like them. What you hear now is the building itself: hum cutting out, tubes striking, a door.)
    ///
    /// Tension rises with what you hear (closer counts more), in the dark, and while you run; it falls while nothing
    /// happens. When you are already on edge (running, a lot just happened) it backs off rather than piling on; when you
    /// creep along calmly it builds. Nothing it does is ever in plain sight: every sound is out of view or behind a wall
    /// (and goes through Steam Audio, so walls muffle it), doors and lights it touches are out of view. Lights it puts out
    /// stay out (saved like a wall switch) unless they only flickered. Off in the Poolrooms, the menus and noclip.
    /// Strength: the audio menu's Atmosphere slider (0 off).
    /// </summary>
    public class TensionDirector : MonoBehaviour
    {
        public enum Phase { Relax, BuildUp, Peak, Fade }

        public static TensionDirector Instance { get; private set; }
        const string PrefsKey = "Backrooms.TensionDirector.Strength";
        static float s_strength = -1f;

        /// <summary>0 off, 0.5 subtle, 1 normal, up to 2 intense (saved).</summary>
        public static float Strength
        {
            get { if (s_strength < 0f) s_strength = PlayerPrefs.GetFloat(PrefsKey, 1f); return s_strength; }
            set { s_strength = Mathf.Clamp(value, 0f, 2f); PlayerPrefs.SetFloat(PrefsKey, s_strength); }
        }

        /// <summary>How tense the player is now, 0..1 (the director's estimate).</summary>
        public float Tension { get; private set; }
        public Phase Current { get; private set; } = Phase.Relax;
        public string LastEvent { get; private set; } = "";
        /// <summary>One line for debugging: phase, tension and the last event.</summary>
        public string Status => $"{Current} {Tension:0.00} (next in {Mathf.Max(0f, _nextEvent - Time.time):0}s) last: {LastEvent}"
                                + (_stalker ? " | shadow: " + _stalker.Status : "");
        /// <summary>Is it running now (on, in a level, not in a menu, the Poolrooms or noclip)?</summary>
        public bool Active { get; private set; }
        ShadowStalker _stalker;

        CamcorderPlayerController _player;
        CamcorderAutoExposure _exposure;
        float _phaseStart, _phaseEnd, _nextEvent, _duck = 1f, _sag;
        float _lastSeen;          // when a director event was last heard / seen happening (time)
        Door[] _doors = new Door[0];
        float _nextDoorScan;
        readonly System.Random _rng = new System.Random();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance) return;
            var go = new GameObject("TensionDirector");
            DontDestroyOnLoad(go);
            go.AddComponent<TensionDirector>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; s_strength = -1f; }

        void Awake()
        {
            Instance = this;
            _stalker = gameObject.AddComponent<ShadowStalker>();
            gameObject.AddComponent<EnemiesMenu>();          // the developer menu (K)
            Enter(Phase.Relax, 30f, 60f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            BuildingAudio.Duck = 1f;
            FixtureLightManager.DirectorSag = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var ws = WorldStreamer.Current;
            if (!_player) _player = FindAnyObjectByType<CamcorderPlayerController>();
            bool active = Strength > 0.01f && ws && !ws.MenuMode && _player && !_player.noclip && !InPool(ws, _player.transform.position);
            Active = active;
            // The quiet and the power dip ease back whatever happens.
            float duckWant = active && Current == Phase.Fade ? 0.3f : 1f;
            _duck = Mathf.MoveTowards(_duck, duckWant, dt * (duckWant < _duck ? 0.5f : 0.15f));
            BuildingAudio.Duck = _duck;
            _sag = Mathf.MoveTowards(_sag, 0f, dt * 0.12f);
            FixtureLightManager.DirectorSag = Mathf.Max(_sag, _stalker ? _stalker.Sag : 0f);
            if (!active) return;
            // The shadow, by phase: it comes with the build-up (once a little of it has passed), is bold at the peak, goes.
            if (_stalker && !EnemyDebug.Hold)                 // (the test menu can tell it to leave the shadow alone)
            {
                if (Current == Phase.BuildUp && _stalker.Current == ShadowStalker.Mode.Absent && Time.time - _phaseStart > 12f && Time.time >= _stalkerRetry)
                {
                    if (!_stalker.Begin(false)) _stalkerRetry = Time.time + 5f;
                    else { LastEvent = "the shadow came"; _stalkerRetry = _phaseEnd; }
                }
                if (Current == Phase.Peak && !_stalker.Bold && _stalker.Current != ShadowStalker.Mode.Caught)
                {
                    if (_stalker.Current != ShadowStalker.Mode.Absent || _stalker.Begin(true)) _stalker.Bold = true;
                }
                if ((Current == Phase.Fade || Current == Phase.Relax) && _stalker.Current != ShadowStalker.Mode.Absent) _stalker.Leave();
            }

            UpdateTension(dt);
            if (Time.time >= _phaseEnd) NextPhase();
            if (Time.time >= _nextEvent) Act();
        }

        // ---------------- Tension and phases ----------------

        void UpdateTension(float dt)
        {
            if (!_exposure) _exposure = FindAnyObjectByType<CamcorderAutoExposure>();
            float dark = _exposure ? Mathf.Clamp01(_exposure.LowLightBoost / 3f) : 0f;
            float speed = _player.PlanarVelocity.magnitude;
            // Running is adrenaline; the dark keeps a floor under it; calm time lets it fall (about a minute to half).
            if (_player.IsRunning && speed > 2.5f) Tension += dt * 0.02f;
            float floor = 0.12f * dark;
            Tension = Mathf.Max(floor, Tension - dt * Tension * 0.012f);
            Tension = Mathf.Clamp01(Tension);
        }

        float _stalkerRetry;

        /// <summary>The shadow caught you: everything stops a while (it backs off, the building goes quiet).</summary>
        public void Caught()
        {
            Tension = 1f;
            LastEvent = "the shadow caught you";
            Enter(Phase.Fade, 10f, 14f);
        }

        void Enter(Phase p, float minS, float maxS)
        {
            Current = p;
            _phaseStart = Time.time;
            float k = Strength > 0.01f ? 1f / Mathf.Lerp(0.6f, 1.4f, Strength / 2f) : 1f;     // stronger: shorter lulls
            _phaseEnd = Time.time + Range(minS, maxS) * (p == Phase.Relax ? k : 1f);
            _nextEvent = p == Phase.BuildUp ? Time.time + Range(8f, 16f) : p == Phase.Peak ? Time.time + Range(1f, 3f) : _phaseEnd + 1f;
        }

        void NextPhase()
        {
            switch (Current)
            {
                case Phase.Relax:
                    // Only build up once you have calmed down.
                    if (Tension > 0.45f) { _phaseEnd = Time.time + 15f; return; }
                    Enter(Phase.BuildUp, 50f, 110f);
                    break;
                case Phase.BuildUp: Enter(Phase.Peak, 10f, 18f); break;
                case Phase.Peak: Enter(Phase.Fade, 7f, 12f); break;
                default: Enter(Phase.Relax, 70f, 160f); break;
            }
        }

        /// <summary>The next thing to happen, by phase: rarer and fainter early in a build-up, a set piece at the peak.</summary>
        void Act()
        {
            if (Current == Phase.BuildUp)
            {
                // On edge already (running, a lot just happened): let it be, or end the build-up early in a peak.
                if (Tension > 0.8f) { Enter(Phase.Peak, 8f, 12f); return; }
                float progress = Mathf.InverseLerp(_phaseStart, _phaseEnd, Time.time);
                bool done = progress < 0.4f ? Subtle() : Medium();
                float gap = Mathf.Lerp(26f, 12f, progress) / Mathf.Lerp(0.7f, 1.3f, Strength / 2f);
                _nextEvent = Time.time + (done ? gap : 3f) * Range(0.75f, 1.25f);
            }
            else if (Current == Phase.Peak)
            {
                SetPiece();
                _nextEvent = _phaseEnd + 1f;
            }
        }

        bool Subtle()
        {
            switch (_rng.Next(3))
            {
                case 0: Sag(0.06f); LastEvent = "the power dipped"; Heard(0.03f); return true;
                default: return Flicker(false);
            }
        }

        bool Medium()
        {
            switch (_rng.Next(4))
            {
                case 0: return DoorSwingsShut(false);
                case 1: return RoomGoesDark(false);
                case 2: Sag(0.12f); LastEvent = "power sag"; Heard(0.05f); return true;
                default: return Flicker(true);
            }
        }

        /// <summary>The peak (the shadow is bold now): the lights of a room nearby die with a deep sag.</summary>
        void SetPiece()
        {
            if (RoomGoesDark(true)) Sag(0.25f);
            else Sag(0.3f);
        }

        void Heard(float amount, float distance = 8f)
        {
            Tension = Mathf.Clamp01(Tension + amount * Mathf.Lerp(1.4f, 0.6f, Mathf.InverseLerp(3f, 18f, distance)));
            _lastSeen = Time.time;
        }

        // ---------------- Events ----------------

        /// <summary>An open door out of sight swings shut (slam: hard, with a bang).</summary>
        bool DoorSwingsShut(bool slam)
        {
            var ear = Ear();
            if (Time.time >= _nextDoorScan) { _nextDoorScan = Time.time + 5f; _doors = FindObjectsByType<Door>(FindObjectsSortMode.None); }
            Door best = null; float bestD = float.MaxValue;
            foreach (var d in _doors)
            {
                if (!d || d.Held || d.Latched || d.Angle < 15f) continue;
                float dist = Vector3.Distance(d.transform.position, ear);
                if (dist < 4f || dist > 16f || Visible(d.transform.position + Vector3.up * 1f, 0.8f)) continue;
                if (dist < bestD) { bestD = dist; best = d; }
            }
            if (!best) return false;
            Vector3 at = best.GripPoint(1, 0f);
            best.Nudge(at, -best.FaceNormal(1) * (slam ? Range(3.5f, 4.5f) : Range(1.4f, 2f)));
            LastEvent = slam ? "a door slammed" : "a door swung shut";
            Heard(slam ? 0.35f : 0.18f, bestD);
            return true;
        }

        /// <summary>
        /// The lights of a room near you go out, as if someone switched them off (saved: it stays dark until you find the
        /// switch). Not your own room, and not one in plain sight.
        /// </summary>
        bool RoomGoesDark(bool peak)
        {
            var ws = WorldStreamer.Current;
            var ear = Ear();
            var here = Zone(ws, _player.transform.position);
            for (int tries = 0; tries < 12; tries++)
            {
                if (!Spot(5f, 15f, peak, out var p, out var L, out _)) continue;
                var z = Zone(ws, p);
                if (!z.HasValue || (here.HasValue && z.Value == here.Value)) continue;
                if (LightZones.IsOff(z.Value.chunk, z.Value.room)) continue;
                LightZones.Set(z.Value.chunk, z.Value.room, true);
                LastEvent = "the lights went out in a room " + Vector3.Distance(p, ear).ToString("0") + " m away";
                Heard(peak ? 0.3f : 0.18f, Vector3.Distance(p, ear));
                return true;
            }
            return false;
        }

        /// <summary>The lights where you are blink off and strike on again (stutter: two blinks).</summary>
        bool Flicker(bool stutter)
        {
            var z = Zone(WorldStreamer.Current, _player.transform.position);
            if (!z.HasValue || LightZones.IsOff(z.Value.chunk, z.Value.room)) return false;
            StartCoroutine(Blink(z.Value.chunk, z.Value.room, stutter ? 2 : 1));
            LastEvent = stutter ? "the lights stuttered" : "the lights blinked";
            Heard(stutter ? 0.12f : 0.06f, 2f);
            return true;
        }

        IEnumerator Blink(Vector3Int chunk, int room, int times)
        {
            for (int i = 0; i < times; i++)
            {
                LightZones.Set(chunk, room, true, true);
                yield return new WaitForSeconds(Range(0.08f, 0.22f));
                LightZones.Set(chunk, room, false);                        // strikes back on (a flicker of its own)
                yield return new WaitForSeconds(Range(0.6f, 1.1f));
            }
        }

        void Sag(float depth) => _sag = Mathf.Max(_sag, depth);

        // ---------------- Places ----------------

        Vector3 Ear() { var cam = Camera.main; return cam ? cam.transform.position : _player.transform.position + Vector3.up * 1.6f; }

        /// <summary>Is a point in view (in the camera's cone and not behind something)?</summary>
        bool Visible(Vector3 p, float radius)
        {
            var cam = Camera.main;
            if (!cam) return false;
            Vector3 v = cam.WorldToViewportPoint(p);
            float m = 0.08f;
            if (v.z <= 0f || v.x < -m || v.x > 1f + m || v.y < -m || v.y > 1f + m) return false;
            Vector3 from = cam.transform.position, to = p - from;
            return !Physics.SphereCast(from, Mathf.Min(radius, 0.1f), to.normalized, out _, Mathf.Max(0f, to.magnitude - radius), ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>A floor point in a room near you (minR..maxR), out of sight if hidden; its layout and room.</summary>
        bool Spot(float minR, float maxR, bool hidden, out Vector3 p, out OfficeLayout L, out Room room)
        {
            p = default; L = null; room = default;
            var ws = WorldStreamer.Current;
            if (!ws) return false;
            Vector3 ear = Ear();
            for (int tries = 0; tries < 14; tries++)
            {
                Vector2 r = Random.insideUnitCircle.normalized * Range(minR, maxR);
                var q = new Vector3(ear.x + r.x, ear.y, ear.z + r.y);
                L = ws.LayoutAt(WorldStreamer.ChunkOf(q));
                if (L == null || L.style == ZoneStyle.Pool) continue;
                int x = Mathf.FloorToInt(q.x / OfficeBiome.CellSize) - L.coord.x * L.N, z = Mathf.FloorToInt(q.z / OfficeBiome.CellSize) - L.coord.y * L.N;
                if (!L.InChunk(x, z) || L.Has(x, z, CellFlags.NoFloor)) continue;
                room = L.RoomAt(x, z);
                if (room.kind == RoomKind.Crawl || room.noFloor) continue;
                q.y = L.BaseY;
                if (Mathf.Abs(q.y - _player.transform.position.y) > 1.5f) continue;     // this storey
                if (hidden && Visible(q + Vector3.up * 1.2f, 0.4f)) continue;
                p = q;
                return true;
            }
            return false;
        }

        static (Vector3Int chunk, int room)? Zone(WorldStreamer ws, Vector3 p)
        {
            if (!ws) return null;
            var L = ws.LayoutAt(WorldStreamer.ChunkOf(p));
            if (L == null) return null;
            int x = Mathf.FloorToInt(p.x / OfficeBiome.CellSize) - L.coord.x * L.N, z = Mathf.FloorToInt(p.z / OfficeBiome.CellSize) - L.coord.y * L.N;
            if (!L.InChunk(x, z)) return null;
            return (new Vector3Int(L.coord.x, L.storey, L.coord.y), L.roomOf[x, z]);
        }

        static bool InPool(WorldStreamer ws, Vector3 p)
        {
            var L = ws.LayoutAt(WorldStreamer.ChunkOf(p));
            return L != null && L.style == ZoneStyle.Pool;
        }

        float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        /// <summary>Makes something happen now (testing): "door", "slam", "dark", "flicker", "sag", "peak", and the shadow:
        /// "shadow" (comes), "bold" (comes, bold), "leave".</summary>
        public bool Force(string what)
        {
            if (!_player) _player = FindAnyObjectByType<CamcorderPlayerController>();
            if (!_player) return false;
            switch (what)
            {
                case "shadow": return _stalker && _stalker.Begin(false);
                case "bold": if (!_stalker) return false; if (_stalker.Current == ShadowStalker.Mode.Absent && !_stalker.Begin(true)) return false; _stalker.Bold = true; return true;
                case "leave": if (_stalker) _stalker.Leave(); return _stalker;
                case "door": return DoorSwingsShut(false);
                case "slam": return DoorSwingsShut(true);
                case "dark": return RoomGoesDark(false);
                case "flicker": return Flicker(true);
                case "sag": Sag(0.25f); return true;
                case "peak": Enter(Phase.Peak, 10f, 14f); return true;
            }
            return false;
        }
    }
}
