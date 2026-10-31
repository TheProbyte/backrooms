using System.Collections;
using System.Collections.Generic;
using Backrooms.Audio;
using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Atmosphere
{
    /// <summary>
    /// The shadow (owner, 2026-10-31: "like Lethal Company's Bracken"; "maybe not a physical thing; in the shadows, when it
    /// nears, lights turn off in its path"). A tall thin figure of darker-than-dark that is only there where the light has
    /// gone: the tubes of the room it is in, and of the room it is about to step into, stutter out as it comes and strike
    /// again a few seconds after it has gone (<see cref="LightZones.Drain"/>). It makes no sound of its own: what you hear
    /// is the building's own (the hum of a room cutting out, tubes striking behind it).
    ///
    /// Its rules, after the Bracken (stalks from behind, backs off when looked at, every look angers it, a long stare makes
    /// it charge) and Alien: Isolation's two brains (the <see cref="TensionDirector"/> decides when it comes, how bold it
    /// may be and when it goes; the shadow itself only knows where you are and where you are looking):
    ///  - Stalk: it moves cell by cell through the building (through doors: it is not solid), toward a spot behind you,
    ///    and never steps into view: it waits behind the corner instead, and now and then leans out (a glimpse). It keeps
    ///    a few metres off, except at the director's peak (<see cref="Bold"/>), when it comes right up behind you.
    ///  - Seen (in view, in the dark where it can be seen, within 22 m): it freezes a moment, then flees out of sight.
    ///    Every look adds to its anger (more up close), staring adds more; anger ebbs slowly.
    ///  - Angry (anger full): it comes straight at you, fast, for a few seconds; lights die ahead of it.
    ///  - Caught (it reaches you from behind at the peak, or while angry): the tape breaks up and cuts; the recording
    ///    picks up somewhere else 20-45 m away, in a room whose lights are off. Then it is gone for a while.
    /// Near it the camcorder's picture breaks up (<see cref="CamcorderRig.Interference"/>) and the power sags.
    /// </summary>
    public class ShadowStalker : MonoBehaviour
    {
        public enum Mode { Absent, Stalk, Evade, Angry, Leave, Caught }

        public static ShadowStalker Instance { get; private set; }
        public Mode Current { get; private set; } = Mode.Absent;
        /// <summary>The director's peak: it may come right up behind you (and catch you).</summary>
        public bool Bold;
        /// <summary>0..1: each look at it adds, a long stare fills it (then it charges).</summary>
        public float Anger { get; private set; }
        /// <summary>Planar distance to you (m), large while absent.</summary>
        public float Distance { get; private set; } = 999f;
        /// <summary>The power sag it causes near you (0..1 of the supply lost; the director adds it to its own).</summary>
        public float Sag { get; private set; }
        public Vector3 Position => _pos;
        public string Status => $"{Current}{(Bold ? " bold" : "")} {Distance:0.0} m anger {Anger:0.00}{(_seenLast ? " SEEN" : "")}";

        const float C = OfficeBiome.CellSize;
        const int N = OfficeBiome.ChunkCells;
        static readonly Vector2Int[] Dirs = { new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(0, 1) };

        CamcorderPlayerController _player;
        int _storey;
        float _baseY;
        Vector3 _pos;
        Vector2Int _cell;
        readonly List<Vector2Int> _path = new List<Vector2Int>();
        int _pathAt;
        float _think, _wait, _peek, _peekT, _angryLeft, _evadeLeft, _show, _interference;
        bool _seenLast;
        readonly HashSet<(Vector3Int, int)> _drained = new HashSet<(Vector3Int, int)>(), _want = new HashSet<(Vector3Int, int)>();
        readonly List<(Vector3Int, int)> _scratch = new List<(Vector3Int, int)>();
        readonly Dictionary<Vector2Int, Vector2Int> _parent = new Dictionary<Vector2Int, Vector2Int>();
        readonly Dictionary<Vector2Int, int> _steps = new Dictionary<Vector2Int, int>();
        readonly Queue<Vector2Int> _queue = new Queue<Vector2Int>();
        readonly System.Random _rng = new System.Random();
        Transform _figure;
        Renderer[] _parts;
        Material _black;
        AudioSource _crackle;

        void Awake()
        {
            Instance = this;
            _crackle = gameObject.AddComponent<AudioSource>();
            _crackle.playOnAwake = false; _crackle.spatialBlend = 0f;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ReleaseLights();
            CamcorderRig.Interference = 0f;
            if (_black) Destroy(_black);
        }

        // ---------------- The director's calls ----------------

        /// <summary>Comes: somewhere 15-25 cells' walk from you, out of sight. False if there is nowhere (or no player).</summary>
        public bool Begin(bool bold)
        {
            if (Current == Mode.Caught) return false;
            if (!Ready()) return false;
            Vector3 p = _player.transform.position;
            _storey = StoreyAt(p, out _baseY);
            var from = CellOf(p);
            if (!Walkable(from)) return false;
            Flood(from, 26);
            var picks = new List<Vector2Int>();
            foreach (var kv in _steps)
                if (kv.Value >= 14 && kv.Value <= 22 && !CellVisible(kv.Key)) picks.Add(kv.Key);
            if (picks.Count == 0) return false;
            _cell = picks[_rng.Next(picks.Count)];
            _pos = Centre(_cell);
            _path.Clear(); _pathAt = 0;
            Anger = 0f; Bold = bold; _wait = 1f; _think = 0f; _seenLast = false;
            Current = Mode.Stalk;
            return true;
        }

        /// <summary>Goes: drifts off away from you and is gone.</summary>
        public void Leave()
        {
            if (Current == Mode.Absent || Current == Mode.Caught) return;
            Current = Mode.Leave; _think = 0f; Bold = false;
        }

        /// <summary>Gone at once (out of the level, into noclip, a menu).</summary>
        public void Vanish()
        {
            if (Current == Mode.Caught) return;
            Current = Mode.Absent; Bold = false;
            _path.Clear();
            ReleaseLights();
            Show(0f);
        }

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
            var L = Lay(_cell);
            Vector3 pp = _player.transform.position;
            if (L == null || StoreyAt(pp, out _) != _storey) { Vanish(); return; }

            Vector3 d = pp - _pos; d.y = 0f;
            Distance = d.magnitude;

            // Seen? (it has to be seeable: in the dark, far enough from the lens.)
            bool seen = _show > 0.5f && Distance < 22f && Visible(Shown() + Vector3.up * 1.3f, 0.2f);
            if (seen)
            {
                if (!_seenLast) Anger += Distance < 6f ? 0.3f : 0.15f;
                Anger += dt * (Distance < 6f ? 0.3f : 0.1f);
                if (Anger >= 1f && Current != Mode.Angry && Current != Mode.Leave)
                {
                    Current = Mode.Angry; _angryLeft = 2.5f + 2f * Anger; _think = 0f; _wait = 0.25f;
                }
                else if (Current == Mode.Stalk)
                {
                    Current = Mode.Evade; _evadeLeft = 6f; _think = 0f; _wait = 0.4f;     // caught in the act: a moment's stillness
                    _peekT = 0f;
                }
            }
            else Anger = Mathf.Max(0f, Anger - dt * 0.03f);
            Anger = Mathf.Min(Anger, 1.5f);
            _seenLast = seen;

            _think -= dt;
            if (_wait > 0f) _wait -= dt;
            switch (Current)
            {
                case Mode.Stalk:
                {
                    if (_think <= 0f)
                    {
                        _think = 0.8f;
                        Vector3 back = Back();
                        PathTo(pp - back * (Bold ? 1.1f : 5.5f), true, 24);       // (about 1.5 ms in the editor at 30)
                    }
                    float keep = Bold ? 0f : 4.5f;
                    // Bold, behind you and nothing between: the last step straight at you (off the grid).
                    if (Bold && _wait <= 0f && Distance < 2.6f && !seen && Clear(pp))
                    {
                        Vector3 aim = pp; aim.y = _pos.y;
                        _pos = Vector3.MoveTowards(_pos, aim, 1.6f * dt);
                        if (CellOf(_pos) != _cell && Walkable(CellOf(_pos))) _cell = CellOf(_pos);
                    }
                    else if (_wait <= 0f && Distance > keep) Walk(Bold ? 1.9f : 1.3f, true, dt);
                    if (Bold && Distance < 0.75f && !seen) { StartCoroutine(Catch()); return; }
                    break;
                }
                case Mode.Evade:
                {
                    _evadeLeft -= dt;
                    if (_think <= 0f) { _think = 1.2f; PathAway(pp); }
                    if (_wait <= 0f) Walk(4f, false, dt);
                    if (_evadeLeft <= 0f || (_pathAt >= _path.Count && !seen))
                    {
                        Current = Mode.Stalk; _wait = 2f + (float)_rng.NextDouble() * 3f; _think = 0f;
                    }
                    break;
                }
                case Mode.Angry:
                {
                    _angryLeft -= dt;
                    if (_think <= 0f) { _think = 0.3f; PathTo(pp, false, 30); }
                    if (_wait <= 0f)
                    {
                        if (Distance < 2.6f && Clear(pp)) { Vector3 aim = pp; aim.y = _pos.y; _pos = Vector3.MoveTowards(_pos, aim, 5.6f * dt); }
                        else Walk(5.6f, false, dt);
                    }
                    if (Distance < 0.8f) { StartCoroutine(Catch()); return; }
                    if (_angryLeft <= 0f) { Anger = 0.3f; Current = Mode.Evade; _evadeLeft = 5f; _think = 0f; }
                    break;
                }
                case Mode.Leave:
                {
                    if (_think <= 0f) { _think = 1.5f; PathAway(pp); }
                    Walk(2f, false, dt);
                    if (Distance > 30f || (_pathAt >= _path.Count && !seen && _think < 1f)) { Vanish(); return; }
                    break;
                }
            }

            UpdateLights();
            UpdateFigure(dt);
            float near = Mathf.Clamp01(1f - Distance / 9f);
            float want = near * near * 0.4f + (seen ? 0.12f : 0f) + (Current == Mode.Angry ? 0.25f : 0f);
            _interference = Mathf.MoveTowards(_interference, Mathf.Clamp01(want), dt * 0.8f);
            CamcorderRig.Interference = _interference;
            Sag = Mathf.Clamp01(1f - Distance / 20f) * (Current == Mode.Angry ? 0.25f : 0.12f);
        }

        /// <summary>Along the path, a cell at a time. careful: never into a cell you can see (it waits, and leans out now and then).</summary>
        void Walk(float speed, bool careful, float dt)
        {
            _peek = Mathf.MoveTowards(_peek, 0f, dt * 0.8f);
            if (_pathAt >= _path.Count) return;
            var next = _path[_pathAt];
            if (careful && next != _cell && CellVisible(next))
            {
                // Behind the corner: every 6-10 s it leans out toward the next cell for a moment.
                _peekT += dt;
                if (_peekT > 6f + (float)_rng.NextDouble() * 4f) _peekT = -1.2f;
                if (_peekT < 0f) _peek = Mathf.Sin(Mathf.PI * (1f + _peekT / 1.2f)) * 0.45f;
                _peekDir = (Centre(next) - _pos).normalized;
                return;
            }
            Vector3 target = Centre(next);
            _pos = Vector3.MoveTowards(_pos, target, speed * dt);
            if ((_pos - target).sqrMagnitude < 0.0025f) { _cell = next; _pathAt++; }
            else if (CellOf(_pos) == next) _cell = next;
        }
        Vector3 _peekDir;

        Vector3 Shown() => _pos + _peekDir * _peek;

        /// <summary>Nothing solid between it and you (chest height)?</summary>
        bool Clear(Vector3 pp)
        {
            Vector3 a = _pos + Vector3.up * 1.1f, b = pp + Vector3.up * 1.1f;
            Vector3 d = b - a;
            if (d.magnitude < 0.05f) return true;
            var hits = Physics.RaycastAll(a, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (!h.collider.transform.IsChildOf(_player.transform)) return false;
            return true;
        }

        /// <summary>The way the camera looks, flat (where "behind you" is).</summary>
        Vector3 Back()
        {
            var cam = Camera.main;
            Vector3 f = cam ? cam.transform.forward : _player.transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }

        // ---------------- Lights ----------------

        /// <summary>The rooms it is in and about to step into go dark; the ones it has left strike again.</summary>
        void UpdateLights()
        {
            _want.Clear();
            var z = ZoneOf(_cell);
            if (z.HasValue) _want.Add(z.Value);
            if (_pathAt < _path.Count && Current != Mode.Leave)
            {
                var n = ZoneOf(_path[_pathAt]);
                if (n.HasValue) _want.Add(n.Value);
                if (Current == Mode.Angry && _pathAt + 2 < _path.Count) { var m = ZoneOf(_path[_pathAt + 2]); if (m.HasValue) _want.Add(m.Value); }
            }
            _scratch.Clear();
            foreach (var h in _drained) if (!_want.Contains(h)) _scratch.Add(h);
            foreach (var h in _scratch) { _drained.Remove(h); LightZones.Drain(h.Item1, h.Item2, false); }
            foreach (var w in _want) if (_drained.Add(w)) LightZones.Drain(w.Item1, w.Item2, true);
        }

        void ReleaseLights()
        {
            foreach (var h in _drained) LightZones.Drain(h.Item1, h.Item2, false);
            _drained.Clear();
        }

        (Vector3Int, int)? ZoneOf(Vector2Int c)
        {
            var L = Lay(c);
            if (L == null) return null;
            int x = c.x - L.coord.x * N, z = c.y - L.coord.y * N;
            if (!L.InChunk(x, z)) return null;
            return (new Vector3Int(L.coord.x, L.storey, L.coord.y), L.roomOf[x, z]);
        }

        // ---------------- The figure ----------------

        void UpdateFigure(float dt)
        {
            // Seeable only where the light has gone, and not right on top of the lens.
            var z = ZoneOf(CellOf(Shown()));
            bool dark = z.HasValue && LightZones.Level(z.Value.Item1, z.Value.Item2, Time.time, 0.5f) < 0.05f;
            Show(dark && Distance > 1.3f ? 1f : 0f);
            if (!_figure) return;
            var cam = Camera.main;
            Vector3 at = Shown();
            Vector3 look = (cam ? cam.transform.position : _player.transform.position) - at; look.y = 0f;
            Quaternion face = look.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(look) : _figure.rotation;
            _figure.SetPositionAndRotation(at + Vector3.up * (0.01f + 0.012f * Mathf.Sin(Time.time * 0.9f)),
                Quaternion.Slerp(_figure.rotation, face, dt * 3f) * Quaternion.Euler(0f, 0f, 1.5f * Mathf.Sin(Time.time * 0.37f)));
        }

        void Show(float k)
        {
            _show = k;
            if (k > 0.5f && !_figure) Build();
            if (_parts == null) return;
            foreach (var r in _parts) if (r && r.enabled != k > 0.5f) r.enabled = k > 0.5f;
        }

        /// <summary>Tall (2 m), thin, long arms to mid-thigh, a slight hunch: a silhouette in black that takes no light.</summary>
        void Build()
        {
            var a = WorldStreamer.Current ? WorldStreamer.Current.assets : null;
            var src = a && a.fixtureHousing ? a.fixtureHousing : a ? a.lampMetal : null;
            if (src)
            {
                _black = new Material(src) { name = "Shadow", hideFlags = HideFlags.DontSave };
                if (_black.HasProperty("_BaseColor")) _black.SetColor("_BaseColor", new Color(0.004f, 0.004f, 0.005f, 1f));
                if (_black.HasProperty("_Color")) _black.SetColor("_Color", new Color(0.004f, 0.004f, 0.005f, 1f));
                if (_black.HasProperty("_Smoothness")) _black.SetFloat("_Smoothness", 0f);
                if (_black.HasProperty("_Metallic")) _black.SetFloat("_Metallic", 0f);
            }
            _figure = new GameObject("Shadow").transform;
            _figure.SetParent(transform, false);
            var parts = new List<Renderer>();
            void Part(PrimitiveType t, Vector3 p, Vector3 s, Vector3 euler)
            {
                var g = GameObject.CreatePrimitive(t);
                DestroyImmediate(g.GetComponent<Collider>());       // (now: a sight line this frame must not hit it)
                g.transform.SetParent(_figure, false);
                g.transform.localPosition = p; g.transform.localScale = s; g.transform.localEulerAngles = euler;
                var r = g.GetComponent<Renderer>();
                if (_black) r.sharedMaterial = _black;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                r.renderingLayerMask = uint.MaxValue;
                parts.Add(r);
            }
            Part(PrimitiveType.Capsule, new Vector3(-0.09f, 0.47f, 0f), new Vector3(0.12f, 0.47f, 0.12f), new Vector3(0f, 0f, -1.5f));
            Part(PrimitiveType.Capsule, new Vector3(0.09f, 0.47f, 0f), new Vector3(0.12f, 0.47f, 0.12f), new Vector3(0f, 0f, 1.5f));
            Part(PrimitiveType.Capsule, new Vector3(0f, 1.25f, 0.02f), new Vector3(0.34f, 0.37f, 0.19f), new Vector3(8f, 0f, 0f));
            Part(PrimitiveType.Capsule, new Vector3(0f, 1.67f, 0.07f), new Vector3(0.08f, 0.09f, 0.08f), new Vector3(20f, 0f, 0f));
            Part(PrimitiveType.Sphere, new Vector3(0f, 1.86f, 0.1f), new Vector3(0.2f, 0.27f, 0.22f), new Vector3(10f, 0f, 0f));
            Part(PrimitiveType.Capsule, new Vector3(-0.22f, 1.15f, 0.04f), new Vector3(0.075f, 0.45f, 0.075f), new Vector3(4f, 0f, -5f));
            Part(PrimitiveType.Capsule, new Vector3(0.22f, 1.15f, 0.04f), new Vector3(0.075f, 0.45f, 0.075f), new Vector3(4f, 0f, 5f));
            _parts = parts.ToArray();
            _figure.position = _pos;
        }

        // ---------------- Caught ----------------

        IEnumerator Catch()
        {
            Current = Mode.Caught;
            Show(0f);
            // The tape breaks up and cuts: the last frame held, the sound gone to crackle.
            CamcorderRig.Interference = _interference = 1f;
            CamcorderFrameHold.Freeze = true;
            _crackle.PlayOneShot(SoundBank.NoclipCrackle, 0.8f * SoundCatalog.Volume(SoundIds.Noclip));
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.8f) yield return null;
            ReleaseLights();

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
            Current = Mode.Absent; Bold = false; Anger = 0f; _path.Clear();
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

        /// <summary>Every cell within maxSteps' walk of from (doors count as open: it is not solid): steps and the way back.</summary>
        void Flood(Vector2Int from, int maxSteps, bool unseen = false)
        {
            _parent.Clear(); _steps.Clear(); _queue.Clear();
            _steps[from] = 0; _parent[from] = from; _queue.Enqueue(from);
            while (_queue.Count > 0 && _steps.Count < 3000)
            {
                var c = _queue.Dequeue();
                int s = _steps[c];
                if (s >= maxSteps) continue;
                for (int side = 0; side < 4; side++)
                {
                    var n = c + Dirs[side];
                    if (_steps.ContainsKey(n) || !Open(c, side) || !Walkable(n)) continue;
                    if (unseen && CellVisible(n)) continue;              // (round what you can see, not through it)
                    _steps[n] = s + 1; _parent[n] = c; _queue.Enqueue(n);
                }
            }
        }

        void Trace(Vector2Int goal)
        {
            _path.Clear(); _pathAt = 0;
            var c = goal;
            int guard = 0;
            while (c != _cell && guard++ < 4000) { _path.Add(c); c = _parent[c]; }
            _path.Reverse();
        }

        /// <summary>A path to the reachable cell nearest goal (hidden: one you cannot see, where there is one).</summary>
        void PathTo(Vector3 goal, bool hidden, int maxSteps)
        {
            Flood(_cell, maxSteps, hidden);
            Vector2Int best = _cell; float bestD = float.MaxValue;
            foreach (var kv in _steps)
            {
                float d = (Centre(kv.Key) - goal).sqrMagnitude;
                if (d >= bestD) continue;
                if (hidden && kv.Key != _cell && CellVisible(kv.Key)) continue;
                bestD = d; best = kv.Key;
            }
            Trace(best);
        }

        /// <summary>A path to a hidden cell 6-16 cells' walk away, as far from you as it can.</summary>
        void PathAway(Vector3 from)
        {
            Flood(_cell, 16);
            Vector2Int best = _cell; float bestD = -1f;
            foreach (var kv in _steps)
            {
                if (kv.Value < 6) continue;
                float d = (Centre(kv.Key) - from).sqrMagnitude;
                if (d <= bestD || CellVisible(kv.Key)) continue;
                bestD = d; best = kv.Key;
            }
            Trace(best);
        }

        bool CellVisible(Vector2Int c)
        {
            var cam = Camera.main;
            if (!cam) return false;
            Vector3 p = Centre(c) + Vector3.up * 1.3f;
            if ((p - cam.transform.position).sqrMagnitude > 26f * 26f) return false;
            // Its whole height: the head over a half wall or the legs under a table count as seen.
            return Visible(p, 0.35f) || Visible(p + Vector3.up * 0.55f, 0.2f) || Visible(p - Vector3.up * 0.9f, 0.2f);
        }

        /// <summary>Is a point in view (in the camera's cone and not behind something)?</summary>
        static bool Visible(Vector3 p, float radius)
        {
            var cam = Camera.main;
            if (!cam) return false;
            Vector3 v = cam.WorldToViewportPoint(p);
            const float m = 0.05f;
            if (v.z <= 0f || v.x < -m || v.x > 1f + m || v.y < -m || v.y > 1f + m) return false;
            Vector3 from = cam.transform.position, to = p - from;
            return !Physics.SphereCast(from, Mathf.Min(radius, 0.1f), to.normalized, out _, Mathf.Max(0f, to.magnitude - radius), ~0, QueryTriggerInteraction.Ignore);
        }
    }
}
