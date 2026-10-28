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
    /// next sound count.
    ///
    ///  - Relax: nothing of its own (the building still creaks and hums by itself).
    ///  - Build-up: unexplained things happen out of sight, rarely at first and nearer and stranger as it goes on: a knock
    ///    inside a wall, something shifting in a duct above the ceiling, footsteps in the next room, a door swinging shut,
    ///    the lights of a room nearby going out, the power sagging.
    ///  - Peak: one set piece: footsteps coming toward you that stop just behind a wall, the lights next door dying, a door
    ///    slamming, a deep sag in the power.
    ///  - Fade: the building goes quiet (the ambience drops away), then back to Relax.
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
        public string Status => $"{Current} {Tension:0.00} (next in {Mathf.Max(0f, _nextEvent - Time.time):0}s) last: {LastEvent}";

        CamcorderPlayerController _player;
        CamcorderAutoExposure _exposure;
        float _phaseStart, _phaseEnd, _nextEvent, _duck = 1f, _sag;
        float _lastSeen;          // when a director event was last heard / seen happening (time)
        readonly List<AudioSource> _sources = new List<AudioSource>();
        int _src;
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
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("DirectorSound" + i);
                go.transform.SetParent(transform, false);
                _sources.Add(ItemModels.Source(go, 24f));
            }
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
            // The quiet and the power dip ease back whatever happens.
            float duckWant = active && Current == Phase.Fade ? 0.3f : 1f;
            _duck = Mathf.MoveTowards(_duck, duckWant, dt * (duckWant < _duck ? 0.5f : 0.15f));
            BuildingAudio.Duck = _duck;
            _sag = Mathf.MoveTowards(_sag, 0f, dt * 0.12f);
            FixtureLightManager.DirectorSag = _sag;
            if (!active) return;

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
            switch (_rng.Next(4))
            {
                case 0: return WallKnocks(1 + _rng.Next(2), 6f, 12f);
                case 1: return DuctThumps(5f, 12f);
                case 2: return Footsteps(4 + _rng.Next(4), 12f, 20f, false);
                default: return Flicker(false);
            }
        }

        bool Medium()
        {
            switch (_rng.Next(6))
            {
                case 0: return Footsteps(6 + _rng.Next(6), 8f, 15f, false);
                case 1: return DoorSwingsShut(false);
                case 2: return RoomGoesDark(false);
                case 3: return WallKnocks(2 + _rng.Next(2), 3.5f, 8f);
                case 4: Sag(0.12f); LastEvent = "power sag"; Heard(0.05f); return true;
                default: return Flicker(true);
            }
        }

        void SetPiece()
        {
            switch (_rng.Next(3))
            {
                case 0:
                    if (Footsteps(10 + _rng.Next(5), 9f, 14f, true)) return;
                    break;
                case 1:
                    if (RoomGoesDark(true)) { Sag(0.25f); return; }
                    break;
            }
            if (!DoorSwingsShut(true)) { Sag(0.3f); WallKnocks(3, 3f, 6f); }
        }

        void Heard(float amount, float distance = 8f)
        {
            Tension = Mathf.Clamp01(Tension + amount * Mathf.Lerp(1.4f, 0.6f, Mathf.InverseLerp(3f, 18f, distance)));
            _lastSeen = Time.time;
        }

        // ---------------- Events ----------------

        /// <summary>One to three knocks from inside a wall near you (out of sight), a moment apart.</summary>
        bool WallKnocks(int count, float minR, float maxR)
        {
            var ear = Ear();
            for (int tries = 0; tries < 10; tries++)
            {
                Vector3 dir = Quaternion.Euler(0f, Range(0f, 360f), 0f) * Vector3.forward;
                if (Physics.Raycast(ear, dir, out var hit, maxR, ~0, QueryTriggerInteraction.Ignore) && hit.distance >= minR
                    && Mathf.Abs(hit.normal.y) < 0.3f && !hit.collider.attachedRigidbody && !Visible(hit.point, 0.3f))
                {
                    Vector3 at = hit.point - hit.normal * 0.08f + Vector3.up * Range(-0.5f, 0.6f);
                    StartCoroutine(Knocks(at, count));
                    LastEvent = count + " knock(s) in a wall " + hit.distance.ToString("0") + " m away";
                    Heard(0.08f * count, hit.distance);
                    return true;
                }
            }
            return false;
        }

        IEnumerator Knocks(Vector3 at, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Play(Clips.Knock(_rng.Next(3)), at, Range(0.55f, 0.8f), Range(0.92f, 1.06f));
                yield return new WaitForSeconds(Range(0.38f, 0.75f));
            }
        }

        /// <summary>Something shifting in a duct over a room near you.</summary>
        bool DuctThumps(float minR, float maxR)
        {
            Vector3 p = default; OfficeLayout L = null; Room room = default;
            bool found = false;
            for (int i = 0; i < 6 && !found; i++)
                found = Spot(minR, maxR, true, out p, out L, out room) && !room.Tall && !room.openAbove;   // a drop ceiling
            if (!found) return false;
            p.y = L.BaseY + room.ceiling + 0.25f;
            Play(Clips.Duct(_rng.Next(3)), p, Range(0.45f, 0.65f), Range(0.9f, 1.08f));
            LastEvent = "something in a duct";
            Heard(0.1f, Vector3.Distance(p, Ear()));
            return true;
        }

        /// <summary>
        /// Footsteps out of sight: someone walking across a room nearby, on what that floor is. approach: they come toward
        /// you and stop (out of view, a few metres away, usually behind a wall).
        /// </summary>
        bool Footsteps(int steps, float minR, float maxR, bool approach)
        {
            if (!Spot(minR, maxR, true, out var p, out _, out _)) return false;
            StartCoroutine(Walk(p, steps, approach));
            LastEvent = approach ? "footsteps coming closer" : "footsteps nearby";
            Heard(approach ? 0.3f : 0.12f, Vector3.Distance(p, Ear()));
            return true;
        }

        IEnumerator Walk(Vector3 at, int steps, bool approach)
        {
            Vector3 dir = Quaternion.Euler(0f, Range(0f, 360f), 0f) * Vector3.forward;
            float pace = Range(0.52f, 0.64f) * (approach ? 1.08f : 1f);
            for (int i = 0; i < steps; i++)
            {
                if (approach)
                {
                    Vector3 to = Ear() - at; to.y = 0f;
                    if (to.magnitude < 4.5f) break;                       // it stops: just behind the wall, waiting
                    dir = Vector3.Slerp(dir, to.normalized, 0.6f).normalized;
                }
                Vector3 next = at + dir * 0.68f;
                // Not through walls: turn along it (a person walks round).
                if (Physics.Raycast(at + Vector3.up * 0.5f, dir, out var h, 0.9f, ~0, QueryTriggerInteraction.Ignore))
                {
                    dir = Vector3.ProjectOnPlane(dir, h.normal).normalized;
                    if (dir.sqrMagnitude < 0.1f) dir = Quaternion.Euler(0f, 90f, 0f) * h.normal;
                    next = at + dir * 0.68f;
                }
                if (Visible(next + Vector3.up * 1f, 0.6f)) break;          // never seen
                at = next;
                var floor = Surfaces.At(at + Vector3.up * 0.1f, null);
                var clip = floor == ProceduralSounds.Floor.Carpet ? SoundBank.Step(_rng.Next(SoundBank.StepCount)) : SoundBank.HardStep(floor, _rng.Next(6));
                Play(clip, at + Vector3.up * 0.05f, Range(0.5f, 0.68f) * (approach ? 0.85f + 0.2f * i / steps : 1f), Range(0.94f, 1.04f));
                yield return new WaitForSeconds(pace * Range(0.9f, 1.1f));
            }
        }

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

        void Play(AudioClip clip, Vector3 at, float volume, float pitch)
        {
            if (!clip) return;
            var s = _sources[_src];
            _src = (_src + 1) % _sources.Count;
            s.transform.position = at;
            s.pitch = pitch;
            s.PlayOneShot(clip, Mathf.Clamp01(volume * SoundCatalog.Volume(SoundIds.Director)));
        }

        float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        /// <summary>Makes something happen now (testing): "knock", "duct", "steps", "approach", "door", "slam", "dark", "flicker", "sag", "peak".</summary>
        public bool Force(string what)
        {
            if (!_player) _player = FindAnyObjectByType<CamcorderPlayerController>();
            if (!_player) return false;
            switch (what)
            {
                case "knock": return WallKnocks(2, 3f, 9f);
                case "duct": return DuctThumps(3f, 10f);
                case "steps": return Footsteps(8, 8f, 15f, false);
                case "approach": return Footsteps(12, 9f, 14f, true);
                case "door": return DoorSwingsShut(false);
                case "slam": return DoorSwingsShut(true);
                case "dark": return RoomGoesDark(false);
                case "flicker": return Flicker(true);
                case "sag": Sag(0.25f); return true;
                case "peak": Enter(Phase.Peak, 10f, 14f); return true;
            }
            return false;
        }

        static class Clips
        {
            static AudioClip[] s_knock, s_duct;
            public static AudioClip Knock(int i) { if (s_knock == null) { s_knock = new AudioClip[3]; for (int k = 0; k < 3; k++) s_knock[k] = ProceduralSounds.WallKnock(k); } return s_knock[i % 3]; }
            public static AudioClip Duct(int i) { if (s_duct == null) { s_duct = new AudioClip[3]; for (int k = 0; k < 3; k++) s_duct[k] = ProceduralSounds.DuctThumps(k); } return s_duct[i % 3]; }
        }
    }
}
