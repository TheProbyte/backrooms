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
    /// nears, lights turn off in its path"; then: "like an actual long tall shadow, against walls, keeping its distance until
    /// enraged enough; in the final stages redder eyes glowing in the dark"). Nobody is ever seen: what you see is a long,
    /// tall human shadow on a wall near where it is, with no one there to cast it (<see cref="Sheet"/>,
    /// Backrooms/ShadowFigure). The tubes of the room it is in, and of the room it is about to step into, stutter out as
    /// it comes and strike again a few seconds after it has gone (<see cref="LightZones.Drain"/>). It makes no sound of
    /// its own: what you hear is the building's own (the hum of a room cutting out, tubes striking behind it).
    ///
    /// Its rules, after the Bracken (stalks, backs off when looked at, every look angers it, a long stare makes it
    /// charge) and Alien: Isolation's two brains (the <see cref="TensionDirector"/> decides when it comes, how bold it may
    /// be and when it goes; the shadow itself only knows where you are and where you are looking):
    ///  - Stalk: it moves cell by cell through the building (through doors: it is not solid), toward a spot behind you,
    ///    never stepping into view (its route goes round what you can see; it waits behind the corner and leans out now
    ///    and then), and keeps its distance: 7.5 m, or 4.5 m at the director's peak (<see cref="Bold"/>). Its shadow falls
    ///    on a wall 1-5 m from it that faces you, a lit one where it can; it slides along the wall as it moves, and fades
    ///    to move to another.
    ///  - Seen (its shadow in view within 22 m): it freezes a moment, then draws back out of sight. Every look adds to its
    ///    anger (more up close), staring adds more; at the peak anger also builds by itself; it ebbs slowly.
    ///  - Its anger shows: from 0.45 two dull red points in the shadow's head, from 0.75 they glow and the shadow darkens.
    ///  - Angry (anger full): it comes straight at you, fast, for a few seconds, standing up out of the wall: a darkness
    ///    in the dark with two red eyes; lights die ahead of it.
    ///  - Caught (only when angry): the tape breaks up and cuts; the recording picks up somewhere else 20-45 m away, in a
    ///    room whose lights are off. Then it is gone for a while.
    /// Near it the camcorder's picture breaks up (<see cref="CamcorderRig.Interference"/>) and the power sags.
    /// </summary>
    public class ShadowStalker : MonoBehaviour, IDevEnemy
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
        public string Status => $"{Current}{(Bold ? " bold" : "")} {Distance:0.0} m anger {Anger:0.00}{(_seenLast ? " SEEN" : "")}{(_sheet != null ? _sheet.Status : "")}";

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
        float _think, _wait, _peek, _peekT, _angryLeft, _evadeLeft, _interference;
        bool _seenLast;
        readonly HashSet<(Vector3Int, int)> _drained = new HashSet<(Vector3Int, int)>(), _want = new HashSet<(Vector3Int, int)>();
        readonly List<(Vector3Int, int)> _scratch = new List<(Vector3Int, int)>();
        readonly Dictionary<Vector2Int, Vector2Int> _parent = new Dictionary<Vector2Int, Vector2Int>();
        readonly Dictionary<Vector2Int, int> _steps = new Dictionary<Vector2Int, int>();
        readonly Queue<Vector2Int> _queue = new Queue<Vector2Int>();
        readonly System.Random _rng = new System.Random();
        AudioSource _crackle;
        Sheet _sheet;

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
            _sheet?.Dispose();
        }

        // ---------------- The director's calls ----------------

        /// <summary>Comes: somewhere 14-22 cells' walk from you, out of sight. False if there is nowhere (or no player).</summary>
        public bool Begin(bool bold) => Begin(bold, 14, 22);

        /// <summary>Comes: somewhere minSteps-maxSteps cells' walk from you, out of sight.</summary>
        public bool Begin(bool bold, int minSteps, int maxSteps)
        {
            if (Current == Mode.Caught) return false;
            if (!Ready()) return false;
            Vector3 p = _player.transform.position;
            _storey = StoreyAt(p, out _baseY);
            var from = CellOf(p);
            if (!Walkable(from)) return false;
            Flood(from, maxSteps + 4);
            var picks = new List<Vector2Int>();
            foreach (var kv in _steps)
                if (kv.Value >= minSteps && kv.Value <= maxSteps && !CellVisible(kv.Key)) picks.Add(kv.Key);
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
            _sheet?.Hide();
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

            // Seen? (its shadow on the wall, or, charging, its eyes.)
            bool seen = _sheet != null && _sheet.Seen();
            bool frozen = EnemyDebug.Freeze;                           // (the test menu: it stands, its anger stays)
            if (seen && !frozen)
            {
                bool close = _sheet.DistanceToEye < 6f;
                if (!_seenLast) Anger += close ? 0.15f : 0.08f;
                Anger += dt * (close ? 0.15f : 0.06f);                 // (a stare of about 5 s up close fills it)
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
            else if (frozen) { }
            else if (Bold && Current == Mode.Stalk) Anger += dt * 0.012f;          // the peak: it works itself up
            else Anger = Mathf.Max(0f, Anger - dt * 0.015f);
            Anger = Mathf.Min(Anger, 1.5f);
            _seenLast = seen;

            _think -= dt;
            if (_wait > 0f) _wait -= dt;
            if (!frozen) switch (Current)
            {
                case Mode.Stalk:
                {
                    if (_think <= 0f)
                    {
                        _think = 0.8f;
                        Vector3 back = Back();
                        PathTo(pp - back * (Bold ? 5f : 9f), true, 24);       // (about 1.5 ms in the editor at 30)
                    }
                    // It keeps its distance until it is angry.
                    float keep = Bold ? 4.5f : 7.5f;
                    if (Distance < keep - 2f && !seen) { Current = Mode.Evade; _evadeLeft = 4f; _think = 0f; }   // you came close: it draws back
                    else if (_wait <= 0f && Distance > keep) Walk(Bold ? 1.7f : 1.3f, true, dt);
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
                    if (Distance < 0.8f)
                    {
                        if (!EnemyDebug.NoCatch) { StartCoroutine(Catch()); return; }
                        EnemyDebug.Say("The shadow reached you (can't be caught is on: it backs off).");
                        _angryLeft = 0f;
                    }
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
            if (_sheet == null) _sheet = new Sheet(this);
            _sheet.Update(dt);
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

        // ---------------- The shadow on the wall ----------------

        /// <summary>
        /// What you see of it: a long, tall human shadow on a wall, with nobody there to cast it (Backrooms/ShadowFigure,
        /// multiplied onto the wall: it takes the wall's light), as tall as the wall, its edge soft. It lies on a wall 1-5 m
        /// from where the shadow is, facing you (a lit one where it can), slides along that wall as it moves, and fades out
        /// to move to another. Its eyes (from anger 0.45) are a second, added pass. Charging, it stands up off the wall at
        /// its own place, turned to you.
        /// </summary>
        sealed class Sheet
        {
            readonly ShadowStalker _s;
            readonly GameObject _root;
            readonly Material _shadowMat, _eyeMat;
            float _strength, _wantStrength, _find, _glow;
            bool _onWall, _has;
            Vector3 _at, _n, _pendingAt, _pendingN;
            float _h = 2.6f, _pendingH;
            static Mesh s_quad;
            static readonly int StrengthId = Shader.PropertyToID("_Strength"), GlowId = Shader.PropertyToID("_Glow"),
                                AspectId = Shader.PropertyToID("_Aspect"), SeedId = Shader.PropertyToID("_Seed"), ModeId = Shader.PropertyToID("_Mode"),
                                SrcId = Shader.PropertyToID("_SrcBlend"), DstId = Shader.PropertyToID("_DstBlend");
            const float Aspect = 0.32f;
            public float DistanceToEye { get; private set; } = 999f;
            /// <summary>Its middle, while it shows (for the test menu's marker).</summary>
            public Vector3? Where => _root && _root.activeSelf ? _root.transform.position + Vector3.up * _h * 0.6f : (Vector3?)null;
            public string Status => _root && _root.activeSelf ? $" shadow {(_onWall ? "on a wall" : "standing")} {_strength:0.00} {_h:0.0} m eyes {_glow:0.00}" : "";

            public Sheet(ShadowStalker s)
            {
                _s = s;
                var sh = Shader.Find("Backrooms/ShadowFigure");
                if (!sh) return;
                if (!s_quad)
                {
                    s_quad = new Mesh { name = "ShadowQuad", hideFlags = HideFlags.DontSave };
                    s_quad.SetVertices(new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f) });
                    s_quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                    s_quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                    s_quad.RecalculateBounds();
                }
                _shadowMat = new Material(sh) { name = "Shadow", hideFlags = HideFlags.DontSave };
                _shadowMat.SetFloat(ModeId, 0f);
                _shadowMat.SetFloat(SrcId, (float)UnityEngine.Rendering.BlendMode.DstColor); _shadowMat.SetFloat(DstId, (float)UnityEngine.Rendering.BlendMode.Zero);
                _eyeMat = new Material(sh) { name = "Shadow eyes", hideFlags = HideFlags.DontSave, renderQueue = 2000 + 461 };
                _eyeMat.SetFloat(ModeId, 1f);
                _eyeMat.SetFloat(SrcId, (float)UnityEngine.Rendering.BlendMode.One); _eyeMat.SetFloat(DstId, (float)UnityEngine.Rendering.BlendMode.One);
                float seed = Random.value * 10f;
                _shadowMat.SetFloat(SeedId, seed); _eyeMat.SetFloat(SeedId, seed);
                _shadowMat.SetFloat(AspectId, Aspect); _eyeMat.SetFloat(AspectId, Aspect);
                _root = new GameObject("Shadow");
                _root.transform.SetParent(s.transform, false);
                Part("Shadow", _shadowMat, 0f);
                Part("Eyes", _eyeMat, 0.004f);
                _root.SetActive(false);
            }

            void Part(string name, Material m, float forward)
            {
                var g = new GameObject(name);
                g.transform.SetParent(_root.transform, false);
                g.transform.localPosition = new Vector3(0f, 0f, -forward);
                g.AddComponent<MeshFilter>().sharedMesh = s_quad;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                r.renderingLayerMask = uint.MaxValue;
            }

            public void Hide()
            {
                _strength = _wantStrength = 0f; _has = false; _onWall = false;
                if (_root) _root.SetActive(false);
            }

            public void Dispose()
            {
                if (_root) Object.Destroy(_root);
                if (_shadowMat) Object.Destroy(_shadowMat);
                if (_eyeMat) Object.Destroy(_eyeMat);
            }

            public void Update(float dt)
            {
                if (!_root) return;
                var cam = Camera.main;
                if (!cam) return;
                float anger = _s.Anger;
                bool angry = _s.Current == Mode.Angry;
                float stage = anger >= 0.75f ? 2f : anger >= 0.45f ? 1f : 0f;
                _glow = Mathf.MoveTowards(_glow, angry ? 4f : stage >= 2f ? 1.6f : stage >= 1f ? 0.45f : 0f, dt * 2f);

                if (angry)
                {
                    // Up off the wall: standing where it is, turned to you, a darkness with two red eyes.
                    Vector3 look = cam.transform.position - _s._pos; look.y = 0f;
                    _onWall = false; _has = true;
                    _at = _s._pos; _n = look.sqrMagnitude > 1e-4f ? look.normalized : Vector3.forward;
                    _h = 2.3f;
                    _wantStrength = 0.55f;                     // a vague darkness: it is the eyes you see
                    _strength = Mathf.MoveTowards(_strength, _wantStrength, dt * 3f);
                }
                else
                {
                    _find -= dt;
                    if (_find <= 0f)
                    {
                        _find = 0.25f;
                        if (FindWall(out var p, out var n, out float h))
                        {
                            bool samePlane = _has && _onWall && Vector3.Dot(n, _n) > 0.99f && Mathf.Abs(Vector3.Dot(p - _at, _n)) < 0.05f;
                            if (samePlane || !_has || _strength < 0.03f) { _pendingAt = p; _pendingN = n; _pendingH = h; if (!samePlane) { _at = p; _n = n; _h = h; } _has = true; _onWall = true; }
                            else { _pendingAt = p; _pendingN = n; _pendingH = h; }        // fade out here, then over there
                            _wantStrength = 0.42f + 0.12f * stage;      // faint at first, darker as it angers
                        }
                        else _wantStrength = 0f;
                    }
                    bool moving = _has && (Vector3.Dot(_pendingN, _n) < 0.99f || Mathf.Abs(Vector3.Dot(_pendingAt - _at, _n)) > 0.05f);
                    if (moving)
                    {
                        _strength = Mathf.MoveTowards(_strength, 0f, dt * 2f);
                        if (_strength <= 0.001f) { _at = _pendingAt; _n = _pendingN; _h = _pendingH; }
                    }
                    else
                    {
                        _at = Vector3.MoveTowards(_at, _pendingAt, dt * 1.4f);     // sliding along the wall
                        _h = Mathf.MoveTowards(_h, _pendingH, dt);
                        _strength = Mathf.MoveTowards(_strength, _wantStrength, dt * 1.2f);
                    }
                }

                bool show = _has && (_strength > 0.005f || _glow > 0.01f);
                if (_root.activeSelf != show) _root.SetActive(show);
                if (!show) { DistanceToEye = 999f; return; }
                _shadowMat.SetFloat(StrengthId, _strength);
                _eyeMat.SetFloat(GlowId, _glow * Mathf.Clamp01(_strength * 2f));
                Vector3 basePos = new Vector3(_at.x, _s._baseY, _at.z) + (_onWall ? _n * 0.012f : Vector3.zero);
                _root.transform.SetPositionAndRotation(basePos, Quaternion.LookRotation(-_n, Vector3.up));
                _root.transform.localScale = new Vector3(_h * Aspect, _h, 1f);
                DistanceToEye = Vector3.Distance(cam.transform.position, basePos + Vector3.up * _h * 0.9f);
            }

            /// <summary>Is it in view: its middle or its head, within 22 m, dark enough to see?</summary>
            public bool Seen()
            {
                if (!_root || !_root.activeSelf || _strength < 0.3f || DistanceToEye > 22f) return false;
                Vector3 off = _onWall ? _n * 0.06f : Vector3.zero;
                Vector3 b = new Vector3(_at.x, _s._baseY, _at.z) + off;
                return Visible(b + Vector3.up * _h * 0.55f, 0.05f) || Visible(b + Vector3.up * _h * 0.9f, 0.05f);
            }

            /// <summary>
            /// A wall 1-5 m from it, facing the camera, clear for the shadow's width, as tall as the room: the lit one nearest
            /// 2.5 m away, keeping the one it is on if it still will do.
            /// </summary>
            bool FindWall(out Vector3 at, out Vector3 normal, out float height)
            {
                at = normal = default; height = 0f;
                var cam = Camera.main;
                Vector3 from = _s.Shown() + Vector3.up * 1.2f;      // (leaning out: its shadow leans out with it)
                float best = float.MaxValue;
                for (int i = 0; i < 16; i++)
                {
                    float ang = i * Mathf.PI * 2f / 16f;
                    var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    if (!Physics.Raycast(from, dir, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    var n = hit.normal; n.y = 0f;
                    if (Mathf.Abs(hit.normal.y) > 0.2f || hit.distance < 1f || hit.collider.attachedRigidbody || n.sqrMagnitude < 0.5f) continue;
                    n.Normalize();
                    if (_s._player && hit.collider.transform.IsChildOf(_s._player.transform)) continue;
                    if (Vector3.Dot(n, cam.transform.position - hit.point) <= 0f) continue;        // the face you can see
                    // Room for it: the wall goes on half its width either way, and down to the floor.
                    if (!Physics.Raycast(hit.point + n * 0.1f, Vector3.up, out var up, 3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float ceiling = up.point.y;
                    float h = Mathf.Min(ceiling - _s._baseY - 0.03f, 3.4f);
                    if (h < 1.9f) continue;
                    Vector3 t = Vector3.Cross(Vector3.up, n);
                    float w = h * Aspect * 0.45f;
                    bool room = true;
                    foreach (var probe in new[] { t * w + Vector3.up * 0f, -t * w, Vector3.up * -0.9f, t * w * 0.8f + Vector3.up * 0.9f, -t * w * 0.8f + Vector3.up * 0.9f })
                        if (!Physics.Raycast(hit.point + n * 0.1f + probe, -n, 0.2f, ~0, QueryTriggerInteraction.Ignore)) { room = false; break; }
                    if (!room) continue;
                    float score = Mathf.Abs(hit.distance - 2.5f);
                    var z = _s.ZoneOf(CellOf(hit.point + n * 0.3f));
                    if (z.HasValue && LightZones.Level(z.Value.Item1, z.Value.Item2, Time.time, 0.5f) > 0.5f) score -= 2f;     // shadows show on a lit wall
                    if (_has && _onWall && Vector3.Dot(n, _n) > 0.99f && Mathf.Abs(Vector3.Dot(hit.point - _at, _n)) < 0.05f) score -= 1.5f;
                    if (score < best) { best = score; at = hit.point; normal = n; height = h; }
                }
                return best < float.MaxValue;
            }
        }

        // ---------------- The test menu (EnemiesMenu, K) ----------------

        public string DevName => "The shadow";
        public string DevStatus => Current == Mode.Absent ? "Not here." : Status;
        public bool DevPresent => Current != Mode.Absent && Current != Mode.Caught;
        public Vector3 DevPosition => _pos;
        public Vector3? DevShownAt => _sheet?.Where;

        public void DevActions(List<DevAction> into)
        {
            into.Add(DevAction.Header("Bring it"));
            into.Add(new DevAction("Come close", () => DevBring(false, true), "It comes about 8-14 m off (7-12 cells' walk), out of sight."));
            into.Add(new DevAction("Come", () => DevBring(false, false), "It comes as the director sends it: 17-26 m off, out of sight."));
            into.Add(new DevAction(Bold ? "Not bold" : "Bold (the peak)", () => { if (DevBring(!Bold, true)) EnemyDebug.Say(Bold ? "The shadow is bold: 4.5 m off, its anger builds by itself." : "The shadow is not bold."); },
                "Bold: it keeps 4.5 m off instead of 7.5 m, and its anger builds by itself."));
            into.Add(DevAction.Header("Anger (it comes close first if it is not here)"));
            into.Add(new DevAction("Calm (0)", () => DevAnger(0f, false), null));
            into.Add(new DevAction("Dull eyes (0.45)", () => DevAnger(0.45f, false), "Two dull red points in its head."));
            into.Add(new DevAction("Glowing eyes (0.8)", () => DevAnger(0.8f, false), "Its eyes glow and it darkens; a look or two more and it charges."));
            into.Add(new DevAction("Charge now", () => DevAnger(1f, true), "It stands up off the wall and comes straight at you."));
            into.Add(DevAction.Header("Send it away"));
            into.Add(new DevAction("Leave", () => { Leave(); EnemyDebug.Say("The shadow is leaving."); }, "It drifts off and is gone."));
            into.Add(new DevAction("Gone now", () => { Vanish(); EnemyDebug.Say("The shadow is gone."); }, null));
        }

        /// <summary>Here (bringing it if it is not); the director hands off. False if there is nowhere out of sight.</summary>
        bool DevBring(bool bold, bool near)
        {
            EnemyDebug.Hold = true;
            if (Current == Mode.Caught) { EnemyDebug.Say("It has just caught you: wait a moment."); return false; }
            if (Current != Mode.Absent)
            {
                Bold = bold;
                if (Current == Mode.Leave) { Current = Mode.Stalk; _think = 0f; }
                return true;
            }
            bool ok = near ? Begin(bold, 7, 12) : Begin(bold);
            if (ok) EnemyDebug.Say($"The shadow came, {Vector3.Distance(_pos, _player.transform.position):0} m off{(bold ? ", bold" : "")}.");
            else EnemyDebug.Say("The shadow could not come: nowhere out of sight here (or the director is off: noclip, the Poolrooms, Tension director 0).");
            return ok;
        }

        void DevAnger(float a, bool charge)
        {
            if (Current == Mode.Absent && !DevBring(Bold, true)) return;
            if (!DevPresent) return;
            EnemyDebug.Hold = true;
            Anger = a;
            if (charge) { Current = Mode.Angry; _angryLeft = 4.5f; _think = 0f; _wait = 0.25f; EnemyDebug.Say("The shadow charges."); }
            else
            {
                if (Current == Mode.Angry) { Current = Mode.Evade; _evadeLeft = 3f; _think = 0f; }
                EnemyDebug.Say($"The shadow's anger: {a:0.00}.");
            }
        }

        // ---------------- Caught ----------------

        IEnumerator Catch()
        {
            Current = Mode.Caught;
            _sheet?.Hide();
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
