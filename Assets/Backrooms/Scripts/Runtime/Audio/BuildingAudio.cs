using System;
using System.Collections.Generic;
using UnityEngine;
using Backrooms.Generation;

namespace Backrooms.Audio
{
    /// <summary>Developer tuning for the building audio (F5 > Audio > Developer), saved separately.</summary>
    [Serializable]
    public class AudioDevSettings
    {
        public int emitterBudget = 12;          // fixture hum voices with their own position
        public float candidateRadius = 20f;     // fixtures considered for a voice, m
        public float hysteresis = 0.75f;        // a voice is kept until its score falls below this x the cut-off
        public float fadeIn = 0.8f, fadeOut = 1.2f;
        public float occlusion = 1f;            // 0 = walls don't muffle, 1 = full model
        public float reverb = 1f;               // room reverb amount
        public float densityReference = 0.55f;  // hum loudness above which many fixtures are compressed
        public float ambience = 1f;             // master for beds (building hum, ventilation, room tone)
        // Camcorder chain internals.
        public float agcTargetDb = -30f, agcMaxBoostDb = 15f, agcAttack = 0.12f, agcRelease = 3.5f;
        public float compression = 0.4f, saturation = 0.3f;

        const string Key = "Backrooms.AudioDev.v1";
        public static AudioDevSettings Load()
        {
            try { var j = PlayerPrefs.GetString(Key, ""); if (!string.IsNullOrEmpty(j)) return JsonUtility.FromJson<AudioDevSettings>(j); }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not load audio dev settings: " + e.Message); }
            return new AudioDevSettings();
        }
        public void Save() { PlayerPrefs.SetString(Key, JsonUtility.ToJson(this)); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// Sound of the building, bounded for an endless world:
    ///  - A small pool of fixture hum voices is assigned to the fixtures that would be loudest at the
    ///    listener (by acoustic path distance, walls and power, not by visibility). Voices stay put on
    ///    their fixture, keep their assignment with hysteresis, and crossfade in and out.
    ///  - Everything the pool doesn't cover is summed into a quiet 2D "distant building hum" bed, and the
    ///    total is compressed in rooms with many fixtures so big halls don't get loud.
    ///  - Hum level follows each fixture's real power curve every frame (strike-on, faulty stutter,
    ///    power-down), and strike / stutter sounds fire from the same curve, so sound and light match.
    ///  - Ventilation and room tone beds change character with the space; a reverb filter on the
    ///    listener follows the visible area and ceiling type.
    /// Occlusion and doorway paths come from <see cref="AcousticField"/>.
    /// </summary>
    public class BuildingAudio : MonoBehaviour
    {
        public static BuildingAudio Instance { get; private set; }

        public WorldStreamer streamer;
        public FixtureLightManager lights;
        [Tooltip("The AudioListener (camcorder). Found automatically if empty.")]
        public Transform listener;

        public AudioDevSettings Dev { get; private set; }
        public AcousticField Field { get; } = new AcousticField();

        // ---- Diagnostics ----
        public int Candidates { get; private set; }
        public int ActiveEmitters { get; private set; }
        public int FadingEmitters { get; private set; }
        public int PoolSize => _hum.Count;
        public int BuzzActive { get; private set; }
        public int EventsPlaying { get; private set; }
        public float HumLoudness { get; private set; }
        public float DensityComp { get; private set; } = 1f;
        public float DistantLevel { get; private set; }
        public float ReverbDecay { get; private set; }
        public string SpaceLabel { get; private set; } = "";
        public int Reassignments { get; private set; }

        class Voice
        {
            public AudioSource src;
            public AudioLowPassFilter lpf;
            public bool busy;
            public LightSource light;
            public float fade, target;
            public float level = 1f, detune = 1f;
            public float extra = 1f, cutoff = 22000f;
            public Vector3 apparent;
            public float lastPower = 1f;
            public float buzzEnv;
            public float lastTick;
            public int curveVersion = -1;
            public float assignedAt;
            public Vector3 dir;            // smoothed direction from the listener
            public float gainDb = -100f;   // smoothed path gain (distance x walls), dB
        }

        readonly List<Voice> _hum = new List<Voice>();
        readonly List<Voice> _buzz = new List<Voice>();
        readonly List<Voice> _events = new List<Voice>();
        readonly List<LightSource> _cands = new List<LightSource>();
        readonly List<float> _scores = new List<float>();
        readonly List<int> _order = new List<int>();
        readonly HashSet<Vector3> _struck = new HashSet<Vector3>();
        Comparison<int> _byScore, _byRank;
        readonly List<float> _rank = new List<float>();
        readonly HashSet<Vector3> _wanted = new HashSet<Vector3>();

        AudioSource _bed, _vent, _tone, _preview;
        AudioLowPassFilter _ventLpf;
        AudioReverbFilter _reverb;
        Vector2Int _fieldCell = new Vector2Int(int.MinValue, 0);
        float _nextField, _nextSelect, _nextReverb;
        float _distantSmoothed, _compSmoothed = 1f, _ventSmoothed, _poweredNearby;
        int _previewKind; string _previewId; float _previewNext;

        // Reverb targets/smoothed (mB and seconds).
        float _rvDecay = 0.5f, _rvRoom = -1600f, _rvRoomHF = -1800f, _rvRefl = -1200f, _rvRev = -600f, _rvHFRatio = 0.5f, _rvRefDelay = 0.01f, _rvDelay = 0.02f;

        void Awake()
        {
            Instance = this;
            Dev = AudioDevSettings.Load();
            _byScore = (a, b) => _scores[b].CompareTo(_scores[a]);
            _byRank = (a, b) => _rank[b].CompareTo(_rank[a]);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (!listener) { var l = FindAnyObjectByType<AudioListener>(); if (l) listener = l.transform; }
            if (!lights) lights = FindAnyObjectByType<FixtureLightManager>();
            if (!streamer) streamer = FindAnyObjectByType<WorldStreamer>();

            // Older scenes had a stand-alone room tone; this manager owns all building ambience now.
            foreach (var old in FindObjectsByType<ProceduralAmbience>())
            {
                if (old.kind != ProceduralAmbience.Kind.RoomTone) continue;
                var s = old.GetComponent<AudioSource>(); if (s) s.Stop();
                old.gameObject.SetActive(false);
            }

            EnsurePool();
            _bed = Bed("BuildingHumBed", SoundBank.BuildingHum, 5);
            _vent = Bed("VentilationBed", SoundBank.Ventilation, 9);
            _ventLpf = _vent.gameObject.AddComponent<AudioLowPassFilter>(); _ventLpf.cutoffFrequency = 4000f;
            _tone = Bed("RoomToneBed", SoundBank.RoomTone, 13);
            var pgo = new GameObject("SoundPreview"); pgo.transform.SetParent(transform, false);
            _preview = pgo.AddComponent<AudioSource>(); _preview.playOnAwake = false; _preview.dopplerLevel = 0f;

            if (listener)
            {
                // Reverb first, then the camcorder chain (filters on the listener run in component order).
                _reverb = listener.GetComponent<AudioReverbFilter>();
                if (!_reverb)
                {
                    var chain = listener.GetComponent<CamcorderAudioChain>();
                    if (chain) DestroyImmediate(chain);
                    _reverb = listener.gameObject.AddComponent<AudioReverbFilter>();
                    listener.gameObject.AddComponent<CamcorderAudioChain>();
                }
                _reverb.reverbPreset = AudioReverbPreset.User;
                _reverb.dryLevel = 0f; _reverb.diffusion = 100f; _reverb.density = 100f;
                _reverb.hfReference = 5000f; _reverb.lfReference = 250f; _reverb.roomLF = 0f;
            }
        }

        AudioSource Bed(string name, AudioClip clip, int seed)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.spatialBlend = 0f; s.volume = 0f; s.playOnAwake = false; s.dopplerLevel = 0f;
            s.timeSamples = new System.Random(seed).Next(0, clip.samples);
            s.Play();
            return s;
        }

        Voice MakeVoice(string name, AudioClip loop)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var v = new Voice { src = go.AddComponent<AudioSource>(), lpf = go.AddComponent<AudioLowPassFilter>() };
            v.src.playOnAwake = false; v.src.volume = 0f; v.src.dopplerLevel = 0f;
            v.lpf.cutoffFrequency = 22000f; v.lpf.lowpassResonanceQ = 1f;
            if (loop) { v.src.clip = loop; v.src.loop = true; }
            return v;
        }

        /// <summary>Grows or shrinks the hum pool to the budget (plus spare voices for crossfades).</summary>
        void EnsurePool()
        {
            int want = Mathf.Clamp(Dev.emitterBudget, 0, 24) + 4;
            while (_hum.Count < want) _hum.Add(MakeVoice("HumVoice_" + _hum.Count, null));
            for (int i = _hum.Count - 1; i >= want; i--)
                if (!_hum[i].busy) { Destroy(_hum[i].src.gameObject); _hum.RemoveAt(i); }
            while (_buzz.Count < 2) _buzz.Add(MakeVoice("BuzzVoice_" + _buzz.Count, SoundBank.Buzz));
            while (_events.Count < 4) _events.Add(MakeVoice("EventVoice_" + _events.Count, null));
        }

        // ---------------- Per frame ----------------

        void LateUpdate()
        {
            if (!listener) return;
            float now = Time.time, dt = Time.deltaTime;
            Vector3 ear = listener.position;

            var cell = new Vector2Int(Mathf.FloorToInt(ear.x / OfficeBiome.CellSize), Mathf.FloorToInt(ear.z / OfficeBiome.CellSize));
            if (cell != _fieldCell || now >= _nextField)
            {
                Field.Rebuild(ear, streamer);
                _fieldCell = cell; _nextField = now + 0.5f;
            }

            if (now >= _nextSelect) { _nextSelect = now + 0.1f; EnsurePool(); Select(ear, now); }
            UpdateHumVoices(ear, now, dt);
            UpdateBuzz(ear, now, dt);
            UpdateEvents(ear, now);
            UpdateBeds(dt);
            if (now >= _nextReverb) { _nextReverb = now + 0.1f; UpdateReverb(); }
            UpdatePreview(now);
            ApplyChainDev();
        }

        CamcorderAudioChain _chain;
        void ApplyChainDev()
        {
            if (!_chain && listener) _chain = listener.GetComponent<CamcorderAudioChain>();
            if (!_chain) return;
            _chain.agcTargetDb = Dev.agcTargetDb; _chain.agcMaxBoostDb = Dev.agcMaxBoostDb;
            _chain.agcAttack = Dev.agcAttack; _chain.agcRelease = Dev.agcRelease;
            _chain.compression = Dev.compression; _chain.saturation = Dev.saturation;
        }

        /// <summary>Unity-side gain for a path (distance gain at the path length times wall/doorway loss).</summary>
        struct Render { public float gain, extra, cutoff; public Vector3 pos; }

        Render Evaluate(string id, Vector3 source, Vector3 ear)
        {
            var p = Field.Query(source);
            float occ = Mathf.Clamp01(Dev.occlusion);
            var r = new Render { pos = source, extra = 1f, cutoff = 22000f };
            if (p.walls == 0 || occ <= 0f) { r.gain = SoundCatalog.DistanceGain(id, p.direct); return r; }

            // Around the walls: from the doorway's direction, at the walking distance, a little duller.
            float bend = Mathf.Clamp01((p.path - p.direct) / 6f);
            float viaExtra = Mathf.Lerp(1f, 0.5f, bend * occ);
            float via = p.reachable ? SoundCatalog.DistanceGain(id, p.path) * viaExtra : 0f;
            // Through the walls: quiet and heavily muffled (about -18 dB per wall).
            float thruExtra = Mathf.Lerp(1f, Mathf.Pow(0.12f, p.walls), occ);
            float thru = SoundCatalog.DistanceGain(id, p.direct) * thruExtra;
            if (via >= thru && p.reachable)
            {
                Vector3 dir = p.visiblePoint - ear; dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = source - ear;
                dir.y = 0f; dir.Normalize();
                r.pos = new Vector3(ear.x, source.y, ear.z) + dir * Mathf.Sqrt(Mathf.Max(0.01f, p.path * p.path - (source.y - ear.y) * (source.y - ear.y)));
                r.extra = viaExtra;
                r.cutoff = Mathf.Lerp(22000f, 3500f, bend * occ);
                r.gain = via;
            }
            else
            {
                r.extra = thruExtra;
                r.cutoff = Mathf.Lerp(22000f, p.walls > 1 ? 450f : 700f, occ);
                r.gain = thru;
            }
            return r;
        }

        void Select(Vector3 ear, float now)
        {
            const string id = SoundIds.FluorescentHum;
            float radius = Mathf.Min(Dev.candidateRadius, SoundCatalog.MaxDistance(id));
            if (lights) lights.GatherTubes(ear, radius, _cands); else _cands.Clear();
            Candidates = _cands.Count;
            _scores.Clear(); _order.Clear();
            float sumAll = 0f;
            for (int i = 0; i < _cands.Count; i++)
            {
                var c = _cands[i];
                float power = lights.PowerLevel(c, now);
                float g = Evaluate(id, c.position, ear).gain * Mathf.Clamp01(power) * Variation(c.position).level;
                _scores.Add(g); _order.Add(i);
                sumAll += g * g;
            }
            // Rank: a fixture that already has a voice is favoured (hysteresis), more so for its first
            // 1.5 s, so shuffling ranks while you walk don't swap voices; a much louder fixture (more than
            // the hysteresis margin) always wins a voice.
            float favour = 1f / Mathf.Clamp(Dev.hysteresis, 0.2f, 1f);
            _rank.Clear();
            for (int i = 0; i < _cands.Count; i++)
            {
                var v = VoiceOf(_cands[i].position);
                float k = v == null || v.target <= 0f ? 1f : (now - v.assignedAt < 1.5f ? favour * 1.3f : favour);
                _rank.Add(_scores[i] * k);
            }
            _order.Sort(_byRank);
            int budget = Mathf.Clamp(Dev.emitterBudget, 0, _hum.Count);

            // The top of the ranking is the wanted set.
            _wanted.Clear();
            for (int k = 0; k < _order.Count && _wanted.Count < budget; k++)
            {
                int idx = _order[k];
                if (_scores[idx] <= 1e-4f) break;
                _wanted.Add(_cands[idx].position);
            }
            foreach (var v in _hum)
            {
                if (!v.busy) continue;
                bool keep = _wanted.Contains(v.light.position);
                v.target = keep ? 1f : 0f;
                if (keep) { int idx = IndexOf(v.light.position); if (idx >= 0) v.light = _cands[idx]; }
            }
            for (int k = 0; k < _order.Count; k++)
            {
                int idx = _order[k];
                if (!_wanted.Contains(_cands[idx].position) || VoiceOf(_cands[idx].position) != null) continue;
                var free = FreeVoice();
                if (free == null) break;
                Assign(free, _cands[idx]);
                Reassignments++;
            }

            float sumVoiced = 0f;
            foreach (var v in _hum)
                if (v.busy && v.target > 0f) { int idx = IndexOf(v.light.position); if (idx >= 0) sumVoiced += _scores[idx] * _scores[idx]; }
            HumLoudness = Mathf.Sqrt(sumAll);
            DistantLevel = Mathf.Sqrt(Mathf.Max(0f, sumAll - sumVoiced));
            DensityComp = HumLoudness > Dev.densityReference ? Mathf.Pow(Dev.densityReference / HumLoudness, 0.6f) : 1f;
            _poweredNearby = Mathf.Clamp01(HumLoudness / 0.4f);

            // Forget strikes of fixtures that have finished starting (so a later power-up strikes again).
            if (_struck.Count > 0) _struck.RemoveWhere(p => !StillStarting(p));
        }

        bool StillStarting(Vector3 pos)
        {
            int i = IndexOf(pos);
            return i >= 0 && lights.IsStarting(_cands[i]);
        }

        /// <summary>Debug: internal state of the voice playing through a source.</summary>
        public string DebugVoice(AudioSource src)
        {
            foreach (var v in _hum)
                if (v.src == src)
                    return $"busy {v.busy} fade {v.fade:0.000} target {v.target} gainDb {v.gainDb:0.0} power {(lights ? lights.PowerLevel(v.light, Time.time) : 1f):0.00} comp {_compSmoothed:0.00} fixture {v.light.position} age {Time.time - v.assignedAt:0.00}";
            return "";
        }

        /// <summary>Debug: true if the source is a hum voice on a faulty or still-starting tube (meant to stutter).</summary>
        public bool VoiceFollowsFlicker(AudioSource src)
        {
            foreach (var v in _hum)
                if (v.src == src && v.busy) return v.light.faultyId >= 0 || (lights && lights.IsStarting(v.light));
            return false;
        }

        /// <summary>Debug: the hum voice currently assigned to a fixture (null if none).</summary>
        public AudioSource VoiceFor(Vector3 fixture)
        {
            foreach (var v in _hum) if (v.busy && v.light.position == fixture) return v.src;
            return null;
        }

        int IndexOf(Vector3 pos)
        {
            for (int i = 0; i < _cands.Count; i++) if (_cands[i].position == pos) return i;
            return -1;
        }

        Voice VoiceOf(Vector3 pos)
        {
            foreach (var v in _hum) if (v.busy && v.light.position == pos) return v;
            return null;
        }

        Voice FreeVoice()
        {
            foreach (var v in _hum) if (!v.busy) return v;
            return null;
        }

        struct Var { public float level, detune; public int variant; public float phase; }

        /// <summary>Seeded per-fixture character: stays the same every time this fixture is heard.</summary>
        static Var Variation(Vector3 p)
        {
            unchecked
            {
                uint h = (uint)(Mathf.RoundToInt(p.x * 10f) * 73856093 ^ Mathf.RoundToInt(p.z * 10f) * 19349663 ^ Mathf.RoundToInt(p.y * 10f) * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                float a = (h & 0xFFFF) / 65535f, b = ((h >> 16) & 0xFF) / 255f, c = ((h >> 24) & 0xFF) / 255f;
                float amt = SoundCatalog.ParamA(SoundIds.FluorescentHum);
                return new Var
                {
                    level = Mathf.Pow(10f, (a - 0.5f) * 0.25f * amt),          // about +-3 dB at 1
                    detune = 1f + (b - 0.5f) * 0.003f * amt,                     // +-0.15 % (slow beating)
                    variant = (int)(h % 4), phase = c,
                };
            }
        }

        void Assign(Voice v, LightSource l)
        {
            var va = Variation(l.position);
            v.busy = true; v.light = l; v.fade = 0f; v.target = 1f; v.assignedAt = Time.time;
            v.level = va.level; v.detune = va.detune;
            v.src.clip = SoundBank.Hum(va.variant);
            v.src.loop = true;
            v.apparent = l.position; v.src.transform.position = l.position;
            v.lastPower = lights ? lights.PowerLevel(l, Time.time) : 1f;
            v.curveVersion = -1;
            v.src.timeSamples = (int)(va.phase * (v.src.clip.samples - 1));
            v.src.volume = 0f;
            v.src.Play();
        }

        void Retire(Voice v)
        {
            v.busy = false; v.fade = 0f; v.target = 0f;
            v.src.Stop(); v.src.volume = 0f;
        }

        void UpdateHumVoices(Vector3 ear, float now, float dt)
        {
            const string id = SoundIds.FluorescentHum;
            float catVol = SoundCatalog.Volume(id), pitch = SoundCatalog.Pitch(id), baseLvl = SoundBank.BaseLevel(id);
            int version = SoundCatalog.Version;
            _compSmoothed = Mathf.MoveTowards(_compSmoothed, DensityComp, dt * 0.5f);
            int active = 0, fading = 0;
            float cutK = 1f - Mathf.Exp(-dt / 0.35f);
            foreach (var v in _hum)
            {
                if (!v.busy) continue;
                float rate = v.target > v.fade ? 1f / Mathf.Max(0.05f, Dev.fadeIn) : 1f / Mathf.Max(0.05f, Dev.fadeOut);
                v.fade = Mathf.MoveTowards(v.fade, v.target, dt * rate);
                if (v.fade <= 0f && v.target <= 0f) { Retire(v); continue; }
                if (v.target > 0f) active++; else fading++;
                bool fresh = v.curveVersion == -1;
                if (v.curveVersion != version) { SoundBank.ApplySpatial(v.src, id, true); v.curveVersion = version; }

                // Power follows the light exactly (no smoothing), so the hum sputters with a faulty tube
                // and dies with the area's power. Unpowered fixtures are never candidates at all.
                float power = lights ? Mathf.Clamp01(lights.PowerLevel(v.light, now)) : 1f;
                var r = Evaluate(id, v.light.position, ear);
                float pathGain = Place(v, r, ear, dt, fresh);
                // Muffling glides (time constant ~0.35 s, at most 10 octaves per second).
                float oct = Mathf.Log(r.cutoff / v.cutoff, 2f) * cutK;
                oct = Mathf.Clamp(oct, -10f * dt, 10f * dt);
                v.cutoff *= Mathf.Pow(2f, oct);
                v.lpf.cutoffFrequency = v.cutoff;
                v.src.pitch = pitch * v.detune;
                v.src.volume = baseLvl * catVol * v.level * pathGain * _compSmoothed * Smooth(v.fade) * power;

                // Faulty stutter and strike edges come from the same curve as the light.
                if (v.lastPower < 0.35f && power > 0.75f)
                {
                    if (lights.IsStarting(v.light) && _struck.Add(v.light.position)) PlayEvent(SoundIds.FluorescentStrike, v.light.position, ear, -1);
                    else if (v.light.faultyId >= 0 && now - v.lastTick > 0.09f) { v.lastTick = now; PlayEvent(SoundIds.FaultyBuzz, v.light.position, ear, (int)(now * 31f)); }
                }
                v.lastPower = power;
            }
            ActiveEmitters = active; FadingEmitters = fading;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>
        /// Places a voice at the smoothed direction of its route and returns its smoothed path gain.
        /// Level and direction are smoothed as single numbers (dB at most 40 dB/s, direction ~0.25 s), so a
        /// route change (around a corner vs through a wall) can never make the level jump.
        /// </summary>
        float Place(Voice v, Render r, Vector3 ear, float dt, bool fresh)
        {
            Vector3 to = r.pos - ear;
            Vector3 want = to.sqrMagnitude > 1e-4f ? to.normalized : Vector3.forward;
            float targetDb = 20f * Mathf.Log10(Mathf.Max(r.gain, 1e-5f));
            if (fresh) { v.dir = want; v.gainDb = targetDb; }
            else
            {
                v.dir = Vector3.Lerp(v.dir, want, 1f - Mathf.Exp(-dt / 0.25f));
                float step = (targetDb - v.gainDb) * (1f - Mathf.Exp(-dt / 0.1f));
                v.gainDb += Mathf.Clamp(step, -40f * dt, 40f * dt);
            }
            v.src.transform.position = ear + v.dir * 2f;
            return v.gainDb <= -99f ? 0f : Mathf.Pow(10f, v.gainDb * 0.05f);
        }

        /// <summary>Buzz voices follow the nearest faulty tubes; they sound only while the tube stutters.</summary>
        void UpdateBuzz(Vector3 ear, float now, float dt)
        {
            const string id = SoundIds.FaultyBuzz;
            float catVol = SoundCatalog.Volume(id), crackle = SoundCatalog.ParamA(id), baseLvl = SoundBank.BaseLevel(id);
            int version = SoundCatalog.Version;
            // Pick up to two nearest faulty candidates.
            int bestA = -1, bestB = -1; float dA = float.MaxValue, dB = float.MaxValue;
            for (int i = 0; i < _cands.Count; i++)
            {
                if (_cands[i].faultyId < 0) continue;
                float d = (_cands[i].position - ear).sqrMagnitude;
                if (d < dA) { bestB = bestA; dB = dA; bestA = i; dA = d; }
                else if (d < dB) { bestB = i; dB = d; }
            }
            int count = 0;
            for (int k = 0; k < _buzz.Count; k++)
            {
                var v = _buzz[k];
                int want = -1;
                // Keep the current assignment if it is still one of the two.
                if (v.busy)
                {
                    if (bestA >= 0 && _cands[bestA].position == v.light.position) { want = bestA; bestA = -1; }
                    else if (bestB >= 0 && _cands[bestB].position == v.light.position) { want = bestB; bestB = -1; }
                }
                if (want < 0 && v.busy) { v.target = 0f; }
                v.fade = Mathf.MoveTowards(v.fade, v.busy ? v.target : 0f, dt * 2f);
                if (v.busy && v.fade <= 0f && v.target <= 0f) { v.busy = false; v.src.Stop(); }
                if (!v.busy && want < 0)
                {
                    int take = bestA >= 0 ? bestA : bestB;
                    if (take >= 0)
                    {
                        if (take == bestA) bestA = -1; else bestB = -1;
                        v.busy = true; v.light = _cands[take]; v.target = 1f; v.fade = 0f; v.buzzEnv = 0f;
                        v.apparent = v.light.position; v.curveVersion = -1;
                        v.src.timeSamples = (int)(Variation(v.light.position).phase * (v.src.clip.samples - 1));
                        v.src.Play();
                    }
                }
                if (!v.busy) { v.src.volume = 0f; continue; }
                bool freshB = v.curveVersion == -1;
                if (v.curveVersion != version) { SoundBank.ApplySpatial(v.src, id, true); v.curveVersion = version; }
                count++;
                float power = Mathf.Clamp01(lights.PowerLevel(v.light, now));
                // Buzz while the tube is stuttering: the envelope opens on every dip and closes ~0.25 s after.
                if (power < 0.97f) v.buzzEnv = 1f; else v.buzzEnv = Mathf.MoveTowards(v.buzzEnv, 0f, dt * 4f);
                var r = Evaluate(id, v.light.position, ear);
                float posK = 1f - Mathf.Exp(-dt / 0.35f);
                float pathGainB = Place(v, r, ear, dt, freshB);
                v.lpf.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(10f, v.lpf.cutoffFrequency)), Mathf.Log(r.cutoff), posK));
                v.src.pitch = SoundCatalog.Pitch(id);
                float stutterGate = power > 0.5f ? 0.6f : 1f;    // louder arcing in the dark dips
                v.src.volume = baseLvl * catVol * Mathf.Clamp(crackle, 0f, 2f) * v.buzzEnv * stutterGate * pathGainB * Smooth(v.fade);
            }
            BuzzActive = count;
        }

        // ---------------- One-shot events ----------------

        void PlayEvent(string id, Vector3 source, Vector3 ear, int variant)
        {
            if (SoundCatalog.Volume(id) <= 0f) return;
            var r = Evaluate(id, source, ear);
            if (r.gain <= 0.002f) return;                                   // inaudible: don't spend a voice
            Voice v = null;
            foreach (var e in _events) if (!e.src.isPlaying) { v = e; break; }
            if (v == null) return;
            SoundBank.ApplySpatial(v.src, id, true);
            Vector3 to = r.pos - ear;
            v.src.transform.position = ear + (to.sqrMagnitude > 1e-4f ? to.normalized : Vector3.forward) * 2f;
            v.lpf.cutoffFrequency = r.cutoff;
            bool tick = id == SoundIds.FaultyBuzz;
            v.src.clip = tick ? SoundBank.Tick(variant) : SoundBank.Strike(variant < 0 ? UnityEngine.Random.Range(0, 3) : variant);
            v.src.loop = false;
            v.src.pitch = SoundCatalog.Pitch(id);
            float lvl = tick ? SoundBank.BaseLevel(SoundIds.FaultyBuzz) * 0.8f * Mathf.Clamp(SoundCatalog.ParamA(id), 0f, 2f) : SoundBank.BaseLevel(id);
            v.src.volume = lvl * SoundCatalog.Volume(id) * r.gain;
            v.src.Play();
        }

        void UpdateEvents(Vector3 ear, float now)
        {
            int n = 0;
            foreach (var e in _events) if (e.src.isPlaying) n++;
            EventsPlaying = n;
        }

        // ---------------- Beds and reverb ----------------

        void UpdateBeds(float dt)
        {
            float amb = Mathf.Clamp(Dev.ambience, 0f, 2f);
            float bedK = 1f - Mathf.Exp(-dt / 1.5f);

            _distantSmoothed = Mathf.Lerp(_distantSmoothed, DistantLevel, bedK);
            _bed.volume = SoundBank.BaseLevel(SoundIds.BuildingHum) * SoundCatalog.Volume(SoundIds.BuildingHum) * amb
                          * Mathf.Clamp01(_distantSmoothed * 1.6f) * _compSmoothed;

            // Ventilation: bigger, harder spaces have more, brighter air; dark (unpowered) areas less.
            float size = Mathf.Clamp01(Mathf.Sqrt(Field.VisibleArea) / 20f);
            float hard = Field.HardCeilingFraction;
            float drift = 1f + (Mathf.PerlinNoise(Time.time * 0.05f, 3.7f) - 0.5f) * 0.4f * SoundCatalog.ParamA(SoundIds.Ventilation);
            float ventT = SoundBank.BaseLevel(SoundIds.Ventilation) * SoundCatalog.Volume(SoundIds.Ventilation) * amb
                          * Mathf.Lerp(0.55f, 1.15f, size) * Mathf.Lerp(0.45f, 1f, _poweredNearby) * drift;
            _ventSmoothed = Mathf.Lerp(_ventSmoothed, ventT, bedK);
            _vent.volume = _ventSmoothed;
            _vent.pitch = SoundCatalog.Pitch(SoundIds.Ventilation);
            _ventLpf.cutoffFrequency = Mathf.Lerp(_ventLpf.cutoffFrequency, Mathf.Lerp(1800f, 6000f, Mathf.Max(size * 0.6f, hard)), bedK);

            _tone.volume = SoundBank.BaseLevel(SoundIds.RoomTone) * SoundCatalog.Volume(SoundIds.RoomTone) * amb;
        }

        void UpdateReverb()
        {
            if (!_reverb) return;
            float area = Mathf.Max(2f, Field.VisibleArea), h = Field.VisibleCeiling, hard = Field.HardCeilingFraction;
            float vol = area * h, size = Mathf.Sqrt(area);
            // Drop ceilings and carpet absorb almost everything; tall plaster halls ring.
            float rtSoft = 0.28f + 0.013f * Mathf.Sqrt(vol);
            float rtHard = 0.45f + 0.036f * Mathf.Sqrt(vol);
            float decay = Mathf.Clamp(Mathf.Lerp(rtSoft, rtHard, hard), 0.2f, 2.4f);
            float amt = Mathf.Clamp(Dev.reverb, 0f, 2f);
            float roomT = amt <= 0.001f ? -10000f : Mathf.Lerp(-1900f, -1000f, hard) + 2000f * Mathf.Log10(amt);
            float roomHFT = Mathf.Lerp(-1900f, -500f, hard);
            float reflT = Mathf.Lerp(-1500f, -700f, Mathf.Max(hard, 1f - Mathf.Clamp01(size / 12f)));
            float revT = Mathf.Lerp(-900f, -150f, hard) + Mathf.Lerp(0f, 200f, Mathf.Clamp01(size / 25f));
            float hfRatioT = Mathf.Lerp(0.45f, 0.8f, hard);
            float refDelayT = Mathf.Clamp(size / 340f * 0.6f, 0.003f, 0.05f);
            float delayT = Mathf.Clamp(size / 340f, 0.008f, 0.07f);
            const float k = 0.15f;              // 10 Hz updates, ~0.65 s time constant
            _rvDecay += (decay - _rvDecay) * k; _rvRoom += (roomT - _rvRoom) * k; _rvRoomHF += (roomHFT - _rvRoomHF) * k;
            _rvRefl += (reflT - _rvRefl) * k; _rvRev += (revT - _rvRev) * k; _rvHFRatio += (hfRatioT - _rvHFRatio) * k;
            _rvRefDelay += (refDelayT - _rvRefDelay) * k; _rvDelay += (delayT - _rvDelay) * k;
            _reverb.decayTime = _rvDecay; _reverb.room = Mathf.Max(-10000f, _rvRoom); _reverb.roomHF = _rvRoomHF;
            _reverb.reflectionsLevel = _rvRefl; _reverb.reverbLevel = _rvRev; _reverb.decayHFRatio = _rvHFRatio;
            _reverb.reflectionsDelay = _rvRefDelay; _reverb.reverbDelay = _rvDelay;
            ReverbDecay = _rvDecay;
            SpaceLabel = (hard > 0.5f ? "plaster" : "drop ceiling") + ", " + area.ToString("0") + " m² visible, ceiling " + h.ToString("0.0") + " m";
        }

        // ---------------- Preview (F5 > Sound) ----------------

        public string PreviewingId => _previewId;

        public void Preview(string id)
        {
            StopPreview();
            _previewId = id;
            var s = _preview;
            SoundBank.ApplySpatial(s, id);
            Vector3 at = listener ? listener.position + listener.forward * 2.5f : transform.position;
            s.transform.position = at;
            s.pitch = SoundCatalog.Pitch(id);
            switch (id)
            {
                case SoundIds.FluorescentHum: s.clip = SoundBank.Hum(0); s.loop = true; _previewKind = 0; break;
                case SoundIds.FaultyBuzz: s.clip = SoundBank.Buzz; s.loop = true; _previewKind = 2; break;
                case SoundIds.BuildingHum: s.clip = SoundBank.BuildingHum; s.loop = true; _previewKind = 0; break;
                case SoundIds.Ventilation: s.clip = SoundBank.Ventilation; s.loop = true; _previewKind = 0; break;
                case SoundIds.RoomTone: s.clip = SoundBank.RoomTone; s.loop = true; _previewKind = 0; break;
                case SoundIds.ZoomMotor: s.clip = SoundBank.ZoomMotor; s.loop = true; _previewKind = 0; break;
                case SoundIds.FluorescentStrike: s.clip = SoundBank.Strike(0); s.loop = false; _previewKind = 1; break;
                case SoundIds.Footsteps: s.clip = SoundBank.Step(0); s.loop = false; _previewKind = 1; break;
                default: _previewId = null; return;
            }
            s.volume = PreviewVolume(id);
            s.Play();
            _previewNext = Time.time + (_previewKind == 1 ? (id == SoundIds.Footsteps ? 0.55f : 1.6f) : 0.12f);
        }

        float PreviewVolume(string id)
        {
            float v = SoundBank.BaseLevel(id) * SoundCatalog.Volume(id);
            if (id == SoundIds.FaultyBuzz) v *= Mathf.Clamp(SoundCatalog.ParamA(id), 0f, 2f);
            return v;
        }

        public void StopPreview()
        {
            if (_preview) _preview.Stop();
            _previewId = null;
        }

        void UpdatePreview(float now)
        {
            if (_previewId == null || !_preview) return;
            if (listener) _preview.transform.position = listener.position + listener.forward * 2.5f;
            _preview.volume = PreviewVolume(_previewId);
            _preview.pitch = SoundCatalog.Pitch(_previewId);
            if (now < _previewNext) return;
            if (_previewKind == 1)
            {
                // One-shot sounds repeat until stopped.
                if (_previewId == SoundIds.Footsteps) _preview.clip = SoundBank.Step(UnityEngine.Random.Range(0, 8));
                else _preview.clip = SoundBank.Strike(UnityEngine.Random.Range(0, 3));
                _preview.Play();
                _previewNext = now + (_previewId == SoundIds.Footsteps ? 0.55f : 1.6f);
            }
            else if (_previewKind == 2)
            {
                _preview.volume = PreviewVolume(_previewId) * (Mathf.PerlinNoise(now * 3f, 1f) > 0.5f ? 1f : 0.2f);
                _previewNext = now + 0.08f;
            }
        }
    }
}
