using System.Collections;
using System.Collections.Generic;
using Backrooms.Audio;
using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Atmosphere
{
    /// <summary>
    /// The shadow (owner, 2026-10-31: "like Lethal Company's Bracken"; "in the shadows, when it nears, lights turn off";
    /// 2026-11-02: "maybe he shouldn't move at all but disappears the second he's out of frame. If you look at him for too
    /// long then it's too late for you. If you glance but look away immediately he retreats slightly. He only shows up in
    /// shadows (or his own shadows of the lights he turns off). A scarier shadow, still tall and thin, maybe shimmering, like
    /// heat waves; a slenderman style shadow").
    ///
    /// What you see: a tall, thin, faceless figure standing perfectly still in a dark room, its edge boiling like the air
    /// over a hot road and the room behind it rippling (<see cref="Figure"/>, Backrooms/ShadowWatcher). It never moves while
    /// anything of it is in frame. Its rules, after SCP-173 and the Weeping Angels (it only moves unseen) and Slender (the
    /// longer you look the worse it gets; the static is both the warning and what hides it):
    ///  - It takes a place out of frame but in your line of sight (so turning round finds it), in the dark: the room's
    ///    tubes stutter out before it is there (<see cref="LightZones.Drain"/>) and strike again after it has gone. Behind
    ///    you or to the side, by preference.
    ///  - Unseen, every 6-10 s (4-6 s at the director's peak) it takes a new place a few metres nearer.
    ///  - Seen: it stands. A glance (under 0.6 s) and away: it is gone, and next time a few metres further off. Longer: the
    ///    tape breaks up more and more as you look (CamcorderRig.Interference) and its shimmer boils harder; look away and it
    ///    is gone (the look stays counted for a while). Look too long (about 2-3 s, less up close and at the peak) and it is
    ///    too late: it is right in front of you, the tape cuts, the recording picks up somewhere else.
    ///  - The moment it is out of frame after being seen, it is gone.
    /// The <see cref="TensionDirector"/> decides when it comes, when it is bold (the peak: nearer, quicker, less patient) and
    /// when it goes. It makes no sound of its own: you hear the building's hum cut out in the room it stands in.
    /// </summary>
    public class ShadowStalker : MonoBehaviour, IDevEnemy
    {
        public enum Mode { Absent, Waiting, Watched, Leave, Caught }

        public static ShadowStalker Instance { get; private set; }
        public Mode Current { get; private set; } = Mode.Absent;
        /// <summary>The director's peak: it comes nearer, moves up sooner and tolerates less looking.</summary>
        public bool Bold;
        /// <summary>0..1: how much you have looked at it lately (full: too late).</summary>
        public float Stare { get; private set; }
        /// <summary>Planar distance to you (m) while it stands somewhere, large otherwise.</summary>
        public float Distance { get; private set; } = 999f;
        /// <summary>The power sag it causes near you (0..1 of the supply lost; the director adds it to its own).</summary>
        public float Sag { get; private set; }
        public Vector3 Position => _pos;
        public string Status => $"{Current}{(Bold ? " bold" : "")} {(_present ? $"{Distance:0.0} m" : "hidden")} want {_want:0.0} m stare {Stare:0.00}{(_lookTime > 0f ? $" look {_lookTime:0.0} s" : "")}";

        const float C = OfficeBiome.CellSize;
        const int N = OfficeBiome.ChunkCells;
        static readonly Vector2Int[] Dirs = { new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(0, 1) };

        // Tuning (seconds, metres).
        const float Glance = 0.6f;                   // a look shorter than this and away is a glance: it draws back
        const float Retreat = 4f, Advance = 2.5f;    // how far it draws back after a glance / comes on while unseen
        const float StareFull = 2.6f, StareFullBold = 1.7f;   // looking this long (at about 12 m) is too late
        const float NearestCalm = 4f, NearestBold = 2.5f, Farthest = 18f;

        CamcorderPlayerController _player;
        int _storey;
        float _baseY;
        Vector3 _pos;
        Vector2Int _cell;
        float _height = 2.55f;
        bool _present, _hasSpot;                       // _present: it stands there (dark now); _hasSpot: a place is chosen
        float _want = 14f;                            // the distance it aims for next
        float _cooldown, _unseen, _lookTime, _arming, _interference;
        (Vector3Int, int)? _zone;                     // the room whose lights it has put out
        readonly Dictionary<Vector2Int, int> _steps = new Dictionary<Vector2Int, int>();
        readonly Queue<Vector2Int> _queue = new Queue<Vector2Int>();
        readonly List<(Vector2Int cell, float score)> _picks = new List<(Vector2Int, float)>();
        readonly System.Random _rng = new System.Random();
        AudioSource _crackle;
        Figure _figure;

        void Awake()
        {
            Instance = this;
            _crackle = gameObject.AddComponent<AudioSource>();
            _crackle.playOnAwake = false; _crackle.spatialBlend = 0f;
        }

        void OnEnable() { EnemyDebug.Register(this); }
        void OnDisable() { EnemyDebug.Unregister(this); }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ReleaseLights();
            CamcorderRig.Interference = 0f;
            _figure?.Dispose();
        }

        // ---------------- The director's calls ----------------

        /// <summary>Comes: about 14-22 cells' walk from you, out of sight. False if there is nowhere (or no player).</summary>
        public bool Begin(bool bold) => Begin(bold, 14, 22);

        /// <summary>Comes: at about the distance of minSteps-maxSteps cells' walk (straight-line metres, capped), out of frame.</summary>
        public bool Begin(bool bold, int minSteps, int maxSteps)
        {
            if (Current == Mode.Caught) return false;
            if (!Ready()) return false;
            Vector3 p = _player.transform.position;
            _storey = StoreyAt(p, out _baseY);
            if (!Walkable(CellOf(p))) return false;
            Bold = bold;
            _want = Mathf.Clamp((minSteps + maxSteps) * 0.5f * C * 0.6f, Nearest() + 1f, Farthest);
            Stare = 0f; _lookTime = 0f; _unseen = 0f;
            if (!PickSpot()) return false;
            Current = Mode.Waiting;
            return true;
        }

        /// <summary>Goes: the next time it is out of frame it is gone.</summary>
        public void Leave()
        {
            if (Current == Mode.Absent || Current == Mode.Caught) return;
            Current = Mode.Leave; Bold = false;
        }

        /// <summary>Gone at once (out of the level, into noclip, a menu).</summary>
        public void Vanish()
        {
            if (Current == Mode.Caught) return;
            Current = Mode.Absent; Bold = false;
            Disappear();
            Distance = 999f;
        }

        float Nearest() => Bold ? NearestBold : NearestCalm;

        // ---------------- Each frame ----------------

        void Update()
        {
            float dt = Time.deltaTime;
            var td = TensionDirector.Instance;
            if (Current == Mode.Caught) return;
            if (Current == Mode.Absent || !td || !td.Active || !Ready())
            {
                if (Current != Mode.Absent) Vanish();
                Distance = 999f; Sag = 0f;
                _interference = Mathf.MoveTowards(_interference, 0f, dt);
                CamcorderRig.Interference = _interference;
                return;
            }
            Vector3 pp = _player.transform.position;
            if (StoreyAt(pp, out _) != _storey || Lay(CellOf(pp)) == null) { Vanish(); return; }
            bool frozen = EnemyDebug.Freeze;                           // (the test menu: it stands, the stare stays)

            if (_hasSpot && !_present)
            {
                // Taking its place: it is there once its room has gone dark (or a moment has passed, for a room with no light).
                _arming += dt;
                bool dark = !_zone.HasValue || LightZones.Level(_zone.Value.Item1, _zone.Value.Item2, Time.time, 0.99f) < 0.05f;
                if ((dark && _arming > 0.6f) || _arming > 3f)
                {
                    if (InFrame()) { Disappear(); _cooldown = 1f; }    // (you turned to it meanwhile: elsewhere then)
                    else { _present = true; _unseen = 0f; }
                }
            }
            Vector3 d = pp - _pos; d.y = 0f;
            Distance = _present ? d.magnitude : 999f;

            bool seen = _present && Seen();
            if (!frozen) switch (Current)
            {
                case Mode.Waiting:
                    if (seen) { Current = Mode.Watched; _lookTime = 0f; break; }
                    if (!_hasSpot)
                    {
                        _cooldown -= dt;
                        if (_cooldown <= 0f && !PickSpot()) _cooldown = 1.5f;
                        break;
                    }
                    if (_present)
                    {
                        // Unseen: it comes on, a few metres at a time, to a new place out of frame.
                        _unseen += dt;
                        float patience = Bold ? 4f + (float)_rng.NextDouble() * 2f : 6f + (float)_rng.NextDouble() * 4f;
                        if (_unseen > patience || Distance < 1.4f)
                        {
                            _want = Mathf.Max(Nearest(), Mathf.Min(_want, Distance) - Advance);
                            Disappear();
                            if (!PickSpot()) _cooldown = 1f;
                        }
                    }
                    break;

                case Mode.Watched:
                    if (seen)
                    {
                        _lookTime += dt;
                        // Up close and at the peak it takes less looking.
                        float full = (Bold ? StareFullBold : StareFull) * Mathf.Clamp(0.55f + Distance / 26f, 0.6f, 1.25f);
                        Stare += dt / full;
                        if (Stare >= 1f) { StartCoroutine(TooLate()); return; }
                    }
                    else
                    {
                        // Out of frame: gone. A glance sends it back a few metres; a longer look leaves it where it was.
                        if (_lookTime < Glance) _want = Mathf.Min(Farthest, Mathf.Max(_want, Distance) + Retreat);
                        else _want = Mathf.Max(Nearest(), Distance);
                        Disappear();
                        _cooldown = 2.5f + (float)_rng.NextDouble() * 3f;
                        _lookTime = 0f;
                        Current = Mode.Waiting;
                    }
                    break;

                case Mode.Leave:
                    if (!seen) { Vanish(); return; }
                    break;
            }
            if (!seen && !frozen) Stare = Mathf.Max(0f, Stare - dt * 0.08f);      // a look stays counted for a while

            if (_figure == null) _figure = new Figure(this);
            _figure.Update(_present, Current == Mode.Watched ? Stare : 0f);
            // The tape breaks up as you look (the warning, and it hides it); a little near it.
            float near = _present ? Mathf.Clamp01(1f - Distance / 10f) : 0f;
            float want = (Current == Mode.Watched ? 0.12f + 0.75f * Stare * Stare : 0f) + near * near * 0.2f;
            _interference = Mathf.MoveTowards(_interference, Mathf.Clamp01(want), dt * (Current == Mode.Watched ? 2f : 0.8f));
            CamcorderRig.Interference = _interference;
            Sag = _present ? Mathf.Clamp01(1f - Distance / 20f) * (Current == Mode.Watched ? 0.18f : 0.08f) : 0f;
        }

        /// <summary>Not there any more: its figure hidden, its room's lights let go (they strike again in a few seconds).</summary>
        void Disappear()
        {
            _present = false; _hasSpot = false; _arming = 0f; _unseen = 0f;
            ReleaseLights();
            _figure?.Hide();
        }

        // ---------------- Where it stands ----------------

        /// <summary>
        /// A place about _want metres off (on this storey, reachable from you), out of frame but in your line of sight
        /// (turning round finds it), clear to stand in; behind you or to the side by preference, in a room already dark by
        /// preference. Its room's lights go out (not your own room's unless it is bold).
        /// </summary>
        bool PickSpot()
        {
            var cam = Camera.main;
            if (!cam || !_player) return false;
            Vector3 eye = cam.transform.position;
            Vector3 pp = _player.transform.position;
            var from = CellOf(pp);
            var mine = ZoneOf(from);
            Flood(from, 40);
            Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            _picks.Clear();
            float lo = Mathf.Max(Nearest(), _want - 2.5f), hi = Mathf.Max(lo + 2f, _want + 2.5f);
            for (int pass = 0; pass < 2 && _picks.Count == 0; pass++)
            {
                foreach (var kv in _steps)
                {
                    Vector3 c = Centre(kv.Key);
                    Vector3 off = c - pp; off.y = 0f;
                    float dist = off.magnitude;
                    if (dist < lo || dist > hi) continue;
                    var z = ZoneOf(kv.Key);
                    if (!Bold && z.HasValue && mine.HasValue && z.Value.Equals(mine.Value)) continue;   // never in your own room (calm)
                    float h = Height(c);
                    Vector3 head = c + Vector3.up * h * 0.9f, chest = c + Vector3.up * h * 0.6f;
                    if (Visible(head, 0.05f) || Visible(chest, 0.2f) || Visible(c + Vector3.up * 0.4f, 0.2f)) continue;   // in frame now
                    bool sight = Clear(eye, chest) || Clear(eye, head);
                    if (pass == 0 && !sight) continue;                 // (first: only where turning round finds it)
                    if (Physics.CheckCapsule(c + Vector3.up * 0.35f, c + Vector3.up * 2f, 0.22f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float score = (float)_rng.NextDouble() * 0.6f;
                    score += 0.6f * Mathf.Clamp01(-Vector3.Dot(off / Mathf.Max(dist, 0.01f), fwd) + 0.3f);   // behind you or to the side
                    if (z.HasValue && LightZones.IsOff(z.Value.Item1, z.Value.Item2)) score += 0.4f;       // a dark room already
                    score -= Mathf.Abs(dist - _want) * 0.08f;
                    _picks.Add((kv.Key, score));
                }
            }
            if (_picks.Count == 0) return false;
            var best = _picks[0];
            foreach (var p in _picks) if (p.score > best.score) best = p;
            _cell = best.cell;
            // A little off the middle of the cell, so it is never on a grid line.
            _pos = Centre(_cell) + new Vector3(((float)_rng.NextDouble() - 0.5f) * 0.4f, 0f, ((float)_rng.NextDouble() - 0.5f) * 0.4f);
            _height = Height(_pos);
            _hasSpot = true; _present = false; _arming = 0f;
            _zone = ZoneOf(_cell);
            if (_zone.HasValue) LightZones.Drain(_zone.Value.Item1, _zone.Value.Item2, true);
            return true;
        }

        /// <summary>Its height where it stands: up to just under the ceiling, 2.25-2.85 m.</summary>
        float Height(Vector3 feet)
        {
            float h = 2.85f;
            if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.up, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
                h = Mathf.Min(h, hit.point.y - feet.y - 0.05f);
            return Mathf.Max(h, 2.25f);
        }

        /// <summary>Is any of it in frame (in the camera's view and not behind something), within 30 m?</summary>
        bool InFrame()
        {
            var cam = Camera.main;
            if (!cam) return false;
            if ((_pos - cam.transform.position).sqrMagnitude > 30f * 30f) return false;
            return Visible(_pos + Vector3.up * _height * 0.9f, 0.04f) || Visible(_pos + Vector3.up * _height * 0.6f, 0.08f)
                || Visible(_pos + Vector3.up * _height * 0.3f, 0.08f);
        }

        bool Seen() => InFrame();

        /// <summary>Nothing solid between a and b (the player's own colliders aside)?</summary>
        bool Clear(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            if (d.magnitude < 0.05f) return true;
            var hits = Physics.RaycastAll(a, d.normalized, d.magnitude - 0.05f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (!_player || !h.collider.transform.IsChildOf(_player.transform)) return false;
            return true;
        }

        // ---------------- Lights ----------------

        void ReleaseLights()
        {
            if (_zone.HasValue) LightZones.Drain(_zone.Value.Item1, _zone.Value.Item2, false);
            _zone = null;
        }

        (Vector3Int, int)? ZoneOf(Vector2Int c)
        {
            var L = Lay(c);
            if (L == null) return null;
            int x = c.x - L.coord.x * N, z = c.y - L.coord.y * N;
            if (!L.InChunk(x, z)) return null;
            return (new Vector3Int(L.coord.x, L.storey, L.coord.y), L.roomOf[x, z]);
        }

        // ---------------- What you see ----------------

        /// <summary>
        /// The figure (Backrooms/ShadowWatcher): one quad standing at its feet, as tall as it is, turned to the camera about
        /// the vertical. It does not fade in or out: it is only ever there, or not, while out of frame. Its shimmer boils
        /// harder the longer you look.
        /// </summary>
        sealed class Figure
        {
            readonly ShadowStalker _s;
            readonly GameObject _go;
            readonly Material _mat;
            static Mesh s_quad;
            static readonly int StrengthId = Shader.PropertyToID("_Strength"), WatchId = Shader.PropertyToID("_Watch"),
                                AspectId = Shader.PropertyToID("_Aspect"), SeedId = Shader.PropertyToID("_Seed");
            const float Aspect = 0.42f, Above = 1.03f;     // the quad: its width and its height over the figure's
            Vector3? _forced;                              // (too late: in front of the camera)

            public Vector3? Where => _go && _go.activeSelf ? _go.transform.position + Vector3.up * _s._height * 0.6f : (Vector3?)null;

            public Figure(ShadowStalker s)
            {
                _s = s;
                var sh = Shader.Find("Backrooms/ShadowWatcher");
                if (!sh) return;
                if (!s_quad)
                {
                    s_quad = new Mesh { name = "ShadowWatcherQuad", hideFlags = HideFlags.DontSave };
                    s_quad.SetVertices(new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f) });
                    s_quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                    s_quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                    s_quad.RecalculateBounds();
                }
                _mat = new Material(sh) { name = "Shadow watcher", hideFlags = HideFlags.DontSave };
                _mat.SetFloat(AspectId, Aspect);
                _mat.SetFloat(SeedId, Random.value * 10f);
                _go = new GameObject("Shadow");
                _go.transform.SetParent(s.transform, false);
                _go.AddComponent<MeshFilter>().sharedMesh = s_quad;
                var r = _go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;   // (no ray-traced shadow either)
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                r.renderingLayerMask = uint.MaxValue;
                _go.SetActive(false);
            }

            /// <summary>Too late: it stands right in front of the camera (or null: back where it was).</summary>
            public void Force(Vector3? at) => _forced = at;

            public void Hide() { if (_go) _go.SetActive(false); }

            public void Dispose()
            {
                if (_go) Object.Destroy(_go);
                if (_mat) Object.Destroy(_mat);
            }

            public void Update(bool present, float watch)
            {
                if (!_go) return;
                var cam = Camera.main;
                bool show = (present || _forced.HasValue) && cam;
                if (_go.activeSelf != show) _go.SetActive(show);
                if (!show) return;
                Vector3 feet = _forced ?? _s._pos;
                Vector3 look = cam.transform.position - feet; look.y = 0f;
                if (look.sqrMagnitude < 1e-4f) look = Vector3.forward;
                _go.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(-look.normalized, Vector3.up));
                float h = _s._height * Above;
                _go.transform.localScale = new Vector3(h * Aspect, h, 1f);
                _mat.SetFloat(StrengthId, 1f);
                _mat.SetFloat(WatchId, _forced.HasValue ? 1f : watch);
            }
        }

        // ---------------- The test menu (EnemiesMenu, K) ----------------

        public string DevName => "The shadow";
        public string DevStatus => Current == Mode.Absent ? "Not here." : Status;
        public bool DevPresent => Current != Mode.Absent && Current != Mode.Caught;
        public Vector3 DevPosition => _pos;
        public Vector3? DevShownAt => _figure?.Where;

        public void DevActions(List<DevAction> into)
        {
            into.Add(DevAction.Header("Bring it"));
            into.Add(new DevAction("Come close", () => DevBring(false, 7f), "It takes a place about 7 m off, out of frame, in the dark (turn round to find it)."));
            into.Add(new DevAction("Come", () => DevBring(false, 14f), "As the director sends it: about 14 m off, out of frame."));
            into.Add(new DevAction("Right behind me", () => DevBring(Bold, 3f), "About 3 m off, behind you."));
            into.Add(new DevAction(Bold ? "Not bold" : "Bold (the peak)", () => { Bold = !Bold; EnemyDebug.Hold = true; EnemyDebug.Say(Bold ? "The shadow is bold: nearer, quicker, less patient." : "The shadow is not bold."); },
                "Bold: it comes on sooner and nearer, and looking at it is too late sooner."));
            into.Add(DevAction.Header("Send it away"));
            into.Add(new DevAction("Leave", () => { Leave(); EnemyDebug.Say("The shadow is leaving (gone the next time it is out of frame)."); }, null));
            into.Add(new DevAction("Gone now", () => { Vanish(); EnemyDebug.Say("The shadow is gone."); }, null));
        }

        /// <summary>Here, about want metres off (the director hands off). False if there is nowhere out of frame.</summary>
        bool DevBring(bool bold, float want)
        {
            EnemyDebug.Hold = true;
            if (Current == Mode.Caught) { EnemyDebug.Say("It has just caught you: wait a moment."); return false; }
            if (!Ready()) return false;
            Vector3 p = _player.transform.position;
            _storey = StoreyAt(p, out _baseY);
            Bold = bold;
            Disappear();
            _want = Mathf.Clamp(want, Nearest(), Farthest);
            Stare = 0f; _lookTime = 0f;
            bool ok = PickSpot();
            if (ok) { Current = Mode.Waiting; EnemyDebug.Say($"The shadow is somewhere about {Vector3.Distance(_pos, p):0} m off, in the dark."); }
            else EnemyDebug.Say("The shadow could not come: nowhere out of frame here (or the director is off: noclip, the Poolrooms, Tension director 0).");
            return ok;
        }

        // ---------------- Too late ----------------

        /// <summary>You looked too long: it is right in front of you, the tape breaks up and cuts.</summary>
        IEnumerator TooLate()
        {
            var cam = Camera.main;
            if (EnemyDebug.NoCatch)
            {
                EnemyDebug.Say("You looked too long (can't be caught is on: it goes).");
                Stare = 0f; Disappear(); Current = Mode.Waiting; _cooldown = 4f; _want = Mathf.Min(Farthest, _want + Retreat);
                yield break;
            }
            Current = Mode.Caught;
            ReleaseLights();
            if (cam && _figure != null)
            {
                Vector3 f = cam.transform.forward; f.y = 0f; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
                _figure.Force(new Vector3(cam.transform.position.x, _baseY, cam.transform.position.z) + f * 1.1f);
                _figure.Update(true, 1f);
            }
            CamcorderRig.Interference = _interference = 0.85f;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.35f) { _figure?.Update(true, 1f); yield return null; }
            yield return Catch();
        }

        // ---------------- Caught ----------------

        IEnumerator Catch()
        {
            Current = Mode.Caught;
            // The tape breaks up and cuts: the last frame held, the sound gone to crackle.
            CamcorderRig.Interference = _interference = 1f;
            CamcorderFrameHold.Freeze = true;
            _crackle.PlayOneShot(SoundBank.NoclipCrackle, 0.8f * SoundCatalog.Volume(SoundIds.Noclip));
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.8f) yield return null;
            _figure?.Force(null);
            Disappear();

            // The recording picks up somewhere else, in a dark room (saved: find its switch).
            if (_player && FindWakeSpot(_player.transform.position, out var to))
            {
                var z = ZoneOf(CellOf(to));
                if (z.HasValue) LightZones.Set(z.Value.Item1, z.Value.Item2, true, true);
                _player.TeleportSafe(to + Vector3.up * 0.05f, (float)_rng.NextDouble() * 360f);
            }
            t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.5f) yield return null;
            CamcorderFrameHold.Freeze = false;
            float t = 0f;
            while (t < 1.6f)
            {
                t += Time.unscaledDeltaTime;
                CamcorderRig.Interference = _interference = Mathf.Lerp(1f, 0f, Mathf.SmoothStep(0f, 1f, t / 1.6f));
                yield return null;
            }
            CamcorderRig.Interference = _interference = 0f;
            Current = Mode.Absent; Bold = false; Stare = 0f; _lookTime = 0f;
            Distance = 999f; Sag = 0f;
            if (TensionDirector.Instance) TensionDirector.Instance.Caught();
        }

        /// <summary>A floor spot 20-45 m away on this storey, in a room (not a crawl space or the Poolrooms), clear to stand in.</summary>
        bool FindWakeSpot(Vector3 from, out Vector3 to)
        {
            to = default;
            for (int tries = 0; tries < 60; tries++)
            {
                float ang = (float)_rng.NextDouble() * Mathf.PI * 2f, r = 20f + (float)_rng.NextDouble() * 25f;
                var c = CellOf(from + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r);
                if (!Walkable(c)) continue;
                Vector3 p = Centre(c);
                if (Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.6f, 0.32f, ~0, QueryTriggerInteraction.Ignore)) continue;
                to = p;
                return true;
            }
            return false;
        }

        // ---------------- The grid ----------------

        bool Ready()
        {
            if (!_player) _player = FindAnyObjectByType<CamcorderPlayerController>();
            return _player && WorldStreamer.Current && Camera.main;
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
        static Vector2Int CellOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / C), Mathf.FloorToInt(p.z / C));
        Vector3 Centre(Vector2Int c) => new Vector3((c.x + 0.5f) * C, _baseY, (c.y + 0.5f) * C);

        int StoreyAt(Vector3 p, out float baseY)
        {
            var ws = WorldStreamer.Current;
            var ground = ws ? ws.LayoutAt(WorldStreamer.ChunkOf(p)) : null;
            float pitch = ground != null ? ground.storeyPitch : 9f;
            int s = Mathf.Max(0, Mathf.FloorToInt((p.y + 0.6f) / pitch));
            baseY = s * pitch;
            return s;
        }

        OfficeLayout _lastL;
        OfficeLayout Lay(Vector2Int c)
        {
            int cx = FloorDiv(c.x, N), cz = FloorDiv(c.y, N);
            if (_lastL != null && _lastL.coord.x == cx && _lastL.coord.y == cz && _lastL.storey == _storey) return _lastL;
            var ws = WorldStreamer.Current;
            _lastL = ws ? ws.LayoutAt(new Vector3Int(cx, _storey, cz)) : null;
            return _lastL;
        }

        bool Walkable(Vector2Int c)
        {
            var L = Lay(c);
            if (L == null || L.style == ZoneStyle.Pool) return false;
            int x = c.x - L.coord.x * N, z = c.y - L.coord.y * N;
            if (!L.InChunk(x, z) || L.Has(x, z, CellFlags.NoFloor)) return false;
            var room = L.RoomAt(x, z);
            return room.kind != RoomKind.Crawl && !room.noFloor;
        }

        bool Open(Vector2Int c, int side)
        {
            var L = Lay(c);
            if (L == null) return false;
            int x = c.x - L.coord.x * N, z = c.y - L.coord.y * N;
            return L.InChunk(x, z) && L.EdgeOf(x, z, side) != EdgeKind.Wall;
        }

        /// <summary>Every cell within maxSteps' walk of from (doors count as open).</summary>
        void Flood(Vector2Int from, int maxSteps)
        {
            _steps.Clear(); _queue.Clear();
            _steps[from] = 0; _queue.Enqueue(from);
            while (_queue.Count > 0 && _steps.Count < 3000)
            {
                var c = _queue.Dequeue();
                int s = _steps[c];
                if (s >= maxSteps) continue;
                for (int side = 0; side < 4; side++)
                {
                    var n = c + Dirs[side];
                    if (_steps.ContainsKey(n) || !Open(c, side) || !Walkable(n)) continue;
                    _steps[n] = s + 1; _queue.Enqueue(n);
                }
            }
        }

        /// <summary>Is a point in view (in the camera's cone and not behind something)?</summary>
        static bool Visible(Vector3 p, float radius)
        {
            var cam = Camera.main;
            if (!cam) return false;
            Vector3 v = cam.WorldToViewportPoint(p);
            const float m = 0.02f;
            if (v.z <= 0f || v.x < -m || v.x > 1f + m || v.y < -m || v.y > 1f + m) return false;
            Vector3 from = cam.transform.position, to = p - from;
            return !Physics.SphereCast(from, Mathf.Min(radius, 0.1f), to.normalized, out _, Mathf.Max(0f, to.magnitude - radius), ~0, QueryTriggerInteraction.Ignore);
        }
    }
}
