using System.Collections.Generic;
using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The water park's moving water (2026-11-01; built in OfficeChunkBuilder.WaterPark, listed in PoolChunk.fittings):
    ///  - where a mushroom's curtain, an arch's stream or a flume's end lands, the surface is kept stirred (the ripple
    ///    simulation) and a little spray thrown up, and the nearest three are heard: falling water on water
    ///    (ProceduralSounds.WaterCurtain);
    ///  - a wave pool runs sessions as real ones do: a wave every four seconds or so for forty seconds, then half a minute
    ///    of calm. Each wave lifts a line of water at the chambers that runs off down the pool, with the chamber's surge.
    /// 2026-11-01, the owner: "waterfall sounds stop the second i step behind a wall, it should echo and be less loud but
    /// become louder closer i get like other stuff". The falls no longer go through Steam Audio's occlusion (which cut them
    /// off behind any wall, within a 12 m reach): they are heard the way the building's hum is (BuildingAudio, AcousticField),
    /// round the walls from the doorway's direction at the walking distance and a little duller, or faintly and muffled
    /// through a wall, whichever is louder, over 30 m, and in the room's reverb like everything else.
    /// And: standing inside a mushroom's curtain the rest of the world is heard muffled through the water (the curtain
    /// itself all round, close); walking through a curtain leaves the lens running with water (LensDrops.Wash).
    /// Added by PoolWorld; idle when the player is not in the Poolrooms.
    /// </summary>
    public class PoolFalls : MonoBehaviour
    {
        [Range(0f, 1f)] public float curtainVolume = 0.09f;
        [Range(0f, 1f)] public float waveVolume = 0.14f;
        public const float Reach = 30f, HearAt = 30f;
        public const float WaveEvery = 4.2f, SessionOn = 40f, SessionOff = 30f;
        const int Voices = 3;

        /// <summary>How far inside a curtain the camera is (0 out, 1 in): the world outside is muffled (PoolSounds).</summary>
        public static float Inside { get; private set; }

        struct Fall { public Vector3 p, d; public int kind; }
        readonly List<Fall> _near = new List<Fall>();
        readonly List<(int idx, float gain)> _rank = new List<(int, float)>();
        float _gather, _spray, _nextWave = -1f, _nextMist;
        int _wavePick;
        AudioSource[] _loops;
        AudioLowPassFilter[] _lpf;
        AudioClip[] _curtain, _surge;
        Vector3 _lastCam;
        bool _haveCam;

        void Update()
        {
            var w = PoolWorld.Instance;
            var player = w && w.streamer ? w.streamer.player : null;
            if (!w || !player || !w.IsPool(player.position)) { Inside = Mathf.MoveTowards(Inside, 0f, Time.deltaTime * 3f); Quiet(); _haveCam = false; return; }
            var p = player.position;
            float dt = Time.deltaTime;
            _gather -= dt;
            if (_gather <= 0f) { _gather = 0.3f; Gather(p); }
            Curtains();
            if (_near.Count == 0) { Quiet(); return; }
            _spray -= dt;
            bool spray = _spray <= 0f;
            if (spray) _spray = 0.12f;
            int stirred = 0;
            foreach (var f in _near)
            {
                if ((f.p - p).sqrMagnitude > 18f * 18f) continue;
                if (f.kind == 5)
                {
                    // A random point round the ring each frame; spray thrown out and up off it now and then.
                    if (stirred++ < 3)
                    {
                        float a = Random.value * Mathf.PI * 2f;
                        var at = f.p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * f.d.x;
                        Stir(at, 0.14f, 0.0035f);
                        if (spray) PoolSplashes.Burst(at, 3, 0.5f + 0.15f * f.d.y, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.3f, 0.5f, 0.7f);
                    }
                }
                else if (f.kind == 6)
                {
                    if (stirred++ < 3)
                    {
                        Stir(f.p + Random.insideUnitSphere * 0.04f, 0.1f, 0.003f);
                        if (spray) PoolSplashes.Burst(f.p, 2, 0.6f, f.d * 0.3f, 0.4f, 0.6f);
                    }
                }
            }
            Waves(p);
            Sounds();
        }

        /// <summary>Water landing on water, every frame: a dip where it goes in and the same volume raised in a ring round it
        /// (a Gaussian twice as wide, a quarter as high), so the pool's level does not creep away under a fall that never
        /// stops (2026-11-01: dips alone drained the surface into a slope across the pool that the shader drew as a smear).</summary>
        static void Stir(Vector3 at, float r, float depth)
        {
            PoolRipples.Splash(at, r, -depth);
            PoolRipples.Splash(at, r * 2f, depth * 0.25f);
        }

        void Gather(Vector3 near)
        {
            _near.Clear();
            foreach (var c in PoolChunk.All)
            {
                if (!c || c.fittings == null) continue;
                for (int i = 0; i < c.fittings.Length; i++)
                {
                    int kind = Mathf.RoundToInt(c.fittings[i].w);
                    if (kind < 4 || kind > 6) continue;
                    var at = c.FittingAt(i);
                    if ((at - near).sqrMagnitude > Reach * Reach || Mathf.Abs(at.y - near.y) > 4f) continue;
                    var d = kind == 5 ? c.fittingDirs[i] : c.transform.TransformDirection(c.fittingDirs[i]);
                    _near.Add(new Fall { p = at, d = d, kind = kind });
                }
            }
            _near.Sort((a, b) => (a.p - near).sqrMagnitude.CompareTo((b.p - near).sqrMagnitude));
        }

        /// <summary>
        /// Inside a mushroom's curtain (under its cap, within the ring where it falls)? And through one since the last frame
        /// (the ring crossed below the cap): the lens gets a wash.
        /// </summary>
        void Curtains()
        {
            var cam = Camera.main;
            if (!cam) return;
            var e = cam.transform.position;
            float inside = 0f;
            for (int i = 0; i < _near.Count; i++)
            {
                var f = _near[i];
                if (f.kind != 5) continue;
                float r = f.d.x, top = f.p.y + f.d.y;
                if (e.y < f.p.y - 0.3f || e.y > top + 0.05f) continue;
                float dNow = new Vector2(e.x - f.p.x, e.z - f.p.z).magnitude;
                inside = Mathf.Max(inside, 1f - Mathf.Clamp01((dNow - (r - 0.35f)) / 0.35f));
                if (_haveCam)
                {
                    float dWas = new Vector2(_lastCam.x - f.p.x, _lastCam.z - f.p.z).magnitude;
                    if ((dWas - r) * (dNow - r) < 0f && (_lastCam - e).sqrMagnitude < 4f) Camcorder.LensDrops.Wash(1f);
                }
                // Inside, now and then a drop off the cap's rim reaches the lens.
                if (dNow < r && Time.time > _nextMist) { _nextMist = Time.time + Random.Range(1.5f, 4f); Camcorder.LensDrops.Add(1, 0.5f); }
            }
            Inside = Mathf.MoveTowards(Inside, inside, Time.deltaTime * 4f);
            _lastCam = e; _haveCam = true;
        }

        /// <summary>The wave pools' sessions: one clock for all of them (the hall's own timer).</summary>
        void Waves(Vector3 p)
        {
            float cycle = SessionOn + SessionOff, t = Time.time % cycle;
            if (t > SessionOn) { _nextWave = -1f; return; }
            if (_nextWave < 0f) _nextWave = Time.time + 1.5f;
            if (Time.time < _nextWave) return;
            _nextWave = Time.time + WaveEvery * Random.Range(0.92f, 1.08f);
            foreach (var f in _near)
            {
                if (f.kind != 4) continue;
                var n = f.d; n.y = 0f;
                float half = n.magnitude;
                if (half < 0.1f) continue;
                n /= half;
                var t3 = Vector3.Cross(Vector3.up, n);
                // A raised line of water just out from the chambers: it spreads into the pool as a swell.
                // (A crest and the trough it is drawn from, the same volume, so the pool's level stays where it is.)
                for (int k = -2; k <= 2; k += 2)
                {
                    PoolRipples.Splash(f.p + n * 0.9f + t3 * (k * half * 0.3f), 0.9f, 0.05f);
                    PoolRipples.Splash(f.p + n * 2.6f + t3 * (k * half * 0.3f), 0.9f, -0.05f);
                }
                if (_surge == null) { _surge = new AudioClip[3]; for (int k = 0; k < 3; k++) _surge[k] = ProceduralSounds.WaveSurge(k); }
                WorldSound.PlayAt(_surge[_wavePick++ % 3], f.p + n * 0.5f, waveVolume, 26f, Random.Range(0.94f, 1.04f));
                break;                                   // the nearest wave pool only (the simulation is round the player)
            }
        }

        /// <summary>Loudness of falling water at a distance (m) along the way the sound goes: about half at 3 m, an eighth at
        /// 10 m, still there at 25.</summary>
        static float DistanceGain(float d) => 1f / (1f + Mathf.Pow(Mathf.Max(0f, d) / 3f, 1.6f));

        /// <summary>
        /// How a fall at source reaches the ear: from where (the doorway's direction at the walking distance when the way
        /// round is louder), how loud and how dull (BuildingAudio's model, on its acoustic field).
        /// </summary>
        static void Route(Vector3 source, Vector3 ear, out Vector3 at, out float gain, out float cutoff)
        {
            at = source; cutoff = 22000f;
            var ba = BuildingAudio.Instance;
            if (!ba) { gain = DistanceGain((source - ear).magnitude); return; }
            var p = ba.Field.Query(source);
            if (!p.inField) { gain = DistanceGain(p.direct) * 0.15f; cutoff = 900f; return; }
            if (p.walls == 0 && p.doors == 0) { gain = DistanceGain(p.direct); return; }
            float bend = Mathf.Clamp01((p.path - p.direct) / 6f);
            float viaExtra = Mathf.Lerp(1f, 0.5f, bend);
            float via = p.reachable ? DistanceGain(p.path) * viaExtra : 0f;
            float thru = DistanceGain(p.direct) * Mathf.Pow(0.12f, p.walls) * Mathf.Pow(0.35f, p.doors);
            if (via >= thru && p.reachable)
            {
                Vector3 dir = p.visiblePoint - ear; dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = source - ear;
                dir.y = 0f; dir.Normalize();
                at = new Vector3(ear.x, source.y, ear.z) + dir * Mathf.Sqrt(Mathf.Max(0.01f, p.path * p.path - (source.y - ear.y) * (source.y - ear.y)));
                cutoff = Mathf.Lerp(22000f, 3500f, bend);
                gain = via;
            }
            else
            {
                cutoff = p.walls > 1 ? 450f : p.walls == 1 ? 700f : 1600f;
                gain = thru;
            }
        }

        /// <summary>The three loudest falls (as they reach the ear), each from its own looping voice.</summary>
        void Sounds()
        {
            var listener = BuildingAudio.Instance && BuildingAudio.Instance.listener ? BuildingAudio.Instance.listener : (Camera.main ? Camera.main.transform : null);
            if (!listener) return;
            var ear = listener.position;
            if (_loops == null)
            {
                _loops = new AudioSource[Voices];
                _lpf = new AudioLowPassFilter[Voices];
                _curtain = new AudioClip[2];
                for (int k = 0; k < 2; k++) _curtain[k] = ProceduralSounds.WaterCurtain(k);
                var flat = AnimationCurve.Constant(0f, 1f, 1f);
                for (int k = 0; k < Voices; k++)
                {
                    var go = new GameObject("FallVoice" + k);
                    go.transform.SetParent(transform, false);
                    var a = go.AddComponent<AudioSource>();
                    a.playOnAwake = false; a.spatialBlend = 1f; a.dopplerLevel = 0f;
                    a.rolloffMode = AudioRolloffMode.Custom; a.SetCustomCurve(AudioSourceCurveType.CustomRolloff, flat);
                    a.minDistance = 1f; a.maxDistance = 500f;
                    a.clip = _curtain[k & 1]; a.loop = true; a.volume = 0f;
                    a.time = k * 3.1f;
                    _loops[k] = a;
                    _lpf[k] = go.AddComponent<AudioLowPassFilter>();
                    _lpf[k].cutoffFrequency = 22000f; _lpf[k].lowpassResonanceQ = 1f;
                }
            }
            // Rank the falls by how loud they arrive.
            _rank.Clear();
            for (int i = 0; i < _near.Count && i < 12; i++)
            {
                var f = _near[i];
                if (f.kind != 5 && f.kind != 6) continue;
                if ((f.p - ear).sqrMagnitude > HearAt * HearAt) continue;
                Route(f.p + Vector3.up * 0.3f, ear, out _, out float g, out _);
                _rank.Add((i, g * (f.kind == 5 ? 1f : 0.55f)));
            }
            _rank.Sort((a, b) => b.gain.CompareTo(a.gain));
            int used = 0;
            float dt = Time.deltaTime;
            for (; used < Voices && used < _rank.Count; used++)
            {
                var f = _near[_rank[used].idx];
                var a = _loops[used];
                Route(f.p + Vector3.up * 0.3f, ear, out var at, out float g, out float cutoff);
                float size = f.kind == 5 ? 1f : 0.55f;
                // Inside this curtain: all round and close, not from one point.
                bool inThis = f.kind == 5 && Inside > 0.01f && new Vector2(ear.x - f.p.x, ear.z - f.p.z).magnitude < f.d.x + 0.1f;
                a.spatialBlend = inThis ? Mathf.Lerp(1f, 0.25f, Inside) : 1f;
                a.bypassListenerEffects = inThis && !PoolWorld.CameraUnderwater;
                a.transform.position = at;
                float target = curtainVolume * size * Mathf.Clamp01(g * 2.2f) * (inThis ? 1.6f : 1f);
                a.volume = Mathf.MoveTowards(a.volume, target, dt * 0.25f);
                var lp = _lpf[used];
                lp.cutoffFrequency = Mathf.Exp(Mathf.MoveTowards(Mathf.Log(lp.cutoffFrequency), Mathf.Log(cutoff), dt * 6f));
                if (!a.isPlaying) a.Play();
            }
            for (int k = used; k < Voices; k++) Fade(_loops[k]);
        }

        void Quiet()
        {
            if (_loops == null) return;
            foreach (var a in _loops) Fade(a);
        }

        static void Fade(AudioSource a)
        {
            if (!a || !a.isPlaying) return;
            a.volume = Mathf.MoveTowards(a.volume, 0f, Time.deltaTime * 0.3f);
            if (a.volume <= 0f) a.Stop();
        }
    }
}
