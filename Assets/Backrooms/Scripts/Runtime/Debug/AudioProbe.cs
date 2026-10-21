using System.Collections;
using System.Collections.Generic;
using System.Text;
using Backrooms.Audio;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Automated play-mode checks for the building audio. Add in play mode and call Run(name); the
    /// result text appears in <see cref="Report"/>. It measures what Unity will actually play (source
    /// volume x rolloff at the source's rendered position) every frame, so jumps, cut-offs and orphaned
    /// voices show up as numbers. It cannot judge how anything sounds.
    /// </summary>
    public class AudioProbe : MonoBehaviour
    {
        public string Report = "";
        public bool Busy;

        CamcorderPlayerController _player;
        Transform _ear;
        BuildingAudio _ba;
        FixtureLightManager _lights;
        CamcorderAudioChain _chain;
        readonly StringBuilder _sb = new StringBuilder();

        void Awake()
        {
            _player = FindAnyObjectByType<CamcorderPlayerController>();
            _ba = BuildingAudio.Instance;
            _lights = FindAnyObjectByType<FixtureLightManager>();
            _ear = _ba ? _ba.listener : Camera.main.transform;
            _chain = _ear.GetComponent<CamcorderAudioChain>();
        }

        public void Run(string test)
        {
            if (Busy) return;
            StartCoroutine(Wrap(test));
        }

        IEnumerator Wrap(string test)
        {
            Busy = true; _sb.Clear();
            _sb.AppendLine("== " + test + " ==");
            IEnumerator e = test switch
            {
                "approach" => Approach(),
                "turn" => Turn(),
                "walls" => Walls(),
                "faulty" => Faulty(),
                "walk" => Walk(60f, false),
                "stream" => Walk(110f, true),
                "still" => Still(),
                "all" => All(),
                _ => null,
            };
            if (e != null) yield return e; else _sb.AppendLine("unknown test");
            Report = _sb.ToString();
            Busy = false;
        }

        IEnumerator All()
        {
            foreach (var t in new[] { "approach", "turn", "walls", "faulty", "walk", "still" })
            {
                _sb.AppendLine("-- " + t);
                IEnumerator e = t switch { "approach" => Approach(), "turn" => Turn(), "walls" => Walls(), "faulty" => Faulty(), "walk" => Walk(80f, true), _ => Still() };
                yield return e;
                Report = _sb.ToString();
            }
        }

        // ---------------- Measurement ----------------

        struct Snap { public float gain; public bool playing; public Vector3 pos; public string info; public float dt; }
        readonly Dictionary<AudioSource, Snap> _prev = new Dictionary<AudioSource, Snap>();
        float _maxJump; string _maxJumpWhere = "";
        int _hardStarts, _hardStops, _bigSteps, _hitches;

        /// <summary>Linear gain Unity applies: volume x custom rolloff at the rendered distance.</summary>
        float Effective(AudioSource s)
        {
            if (!s.isPlaying) return 0f;
            float g = s.volume;
            if (s.spatialBlend > 0f && s.rolloffMode == AudioRolloffMode.Custom)
            {
                float d = Vector3.Distance(s.transform.position, _ear.position);
                float roll = s.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(Mathf.Clamp01(d / s.maxDistance));
                g *= Mathf.Lerp(1f, roll, s.spatialBlend);
            }
            return g;
        }

        void Measure(bool ignoreFaulty = true)
        {
            foreach (var s in _ba.GetComponentsInChildren<AudioSource>())
            {
                if (!s.loop) continue;                                  // one-shots are allowed to start abruptly
                if (ignoreFaulty && s.name.StartsWith("Buzz")) continue;
                float g = Effective(s);
                if (_prev.TryGetValue(s, out var p))
                {
                    // Per-frame level step in dB, for levels that are audible at all (above -60 dBFS).
                    // Frames after an editor hitch (> 25 ms) are skipped: levels move per second, not per frame.
                    if (Time.deltaTime > 0.025f || p.dt > 0.025f) _hitches++;
                    else if (g > 0.001f && p.gain > 0.001f && !IsFlickering(s))
                    {
                        float stepDb = Mathf.Abs(20f * Mathf.Log10(g / p.gain));
                        if (stepDb > _maxJump) { _maxJump = stepDb; _maxJumpWhere = s.name + " " + p.gain.ToString("0.0000") + "->" + g.ToString("0.0000") + " [" + _ba.DebugVoice(s) + " prev " + p.info + "] dt " + (Time.deltaTime * 1000f).ToString("0") + " ms"; }
                        if (stepDb > 3f) _bigSteps++;
                    }
                    if (!p.playing && s.isPlaying && g > 0.0015f) _hardStarts++;
                    if (p.playing && !s.isPlaying && p.gain > 0.0015f) _hardStops++;
                }
                _prev[s] = new Snap { gain = g, playing = s.isPlaying, pos = s.transform.position, info = _ba.DebugVoice(s), dt = Time.deltaTime };
            }
        }

        // A hum voice on a faulty or still-starting tube is meant to follow the light's stutter.
        bool IsFlickering(AudioSource s) => _ba.VoiceFollowsFlicker(s);

        void ResetMeasure() { _prev.Clear(); _maxJump = 0f; _maxJumpWhere = ""; _hardStarts = _hardStops = _bigSteps = _hitches = 0; }

        void Summary(string label)
        {
            _sb.AppendLine($"{label}: largest one-frame level step {_maxJump:0.00} dB ({_maxJumpWhere}), steps over 3 dB {_bigSteps}, starts above -56 dB {_hardStarts}, cut-offs above -56 dB {_hardStops}, " +
                           $"hitch frames skipped {_hitches}, voices {_ba.ActiveEmitters}+{_ba.FadingEmitters}/{_ba.PoolSize}, swaps {_ba.Reassignments}");
        }

        void Place(Vector3 feet, float yaw) => _player.Teleport(feet, yaw, 0f);

        Vector3 Feet => _player.transform.position;

        LightSource NearestTube(Vector3 p, bool faulty = false)
        {
            var l = new List<LightSource>();
            _lights.GatherTubes(p, 40f, l);
            LightSource best = default; float bd = float.MaxValue;
            foreach (var x in l)
            {
                if (faulty && x.faultyId < 0) continue;
                float d = (x.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = x; }
            }
            return best;
        }

        float HumAt(Vector3 fixture)
        {
            // Effective gain of the voice (if any) assigned to this fixture.
            var src = _ba.VoiceFor(fixture);
            return src ? Effective(src) : 0f;
        }

        // ---------------- Tests ----------------

        IEnumerator Approach()
        {
            // A lit fixture in an ordinary room, approached and passed along the longest clear line
            // through the point beneath it (walking pace, no jumps).
            var start = Feet;
            var tubes = new List<LightSource>();
            _lights.GatherTubes(start, 30f, tubes);
            LightSource f = default; Vector3 under = start, dir = Vector3.forward; float bestLen = 0f, bestA = 0f, bestB = 0f;
            foreach (var t in tubes)
            {
                if (t.dome) continue;
                var u = new Vector3(t.position.x, start.y, t.position.z);
                if (!Walkable(u)) continue;
                for (int k = 0; k < 4; k++)
                {
                    var d = Quaternion.Euler(0, k * 45f, 0) * Vector3.forward;
                    float a = 0f, b = 0f;
                    while (a < 9f && Walkable(u - d * (a + 0.1f))) a += 0.1f;
                    while (b < 9f && Walkable(u + d * (b + 0.1f))) b += 0.1f;
                    if (Mathf.Min(a, b) > Mathf.Min(bestA, bestB)) { bestA = a; bestB = b; bestLen = a + b; f = t; under = u; dir = d; }
                }
            }
            if (bestLen < 4f) { _sb.AppendLine("no clear line under a troffer nearby"); yield break; }
            Place(under - dir * bestA, Quaternion.LookRotation(dir).eulerAngles.y);
            yield return new WaitForSeconds(3f);
            ResetMeasure();
            var samples = new StringBuilder();
            float nextSample = -bestA;
            for (float t = -bestA; t <= bestB; t += 1.35f * Time.deltaTime)
            {
                Place(under + dir * t, Quaternion.LookRotation(dir).eulerAngles.y);
                yield return null;
                Measure();
                if (t >= nextSample) { nextSample += 1f; samples.Append($"{t:0}m:{20f * Mathf.Log10(Mathf.Max(1e-5f, HumAt(f.position))):0}dB "); }
            }
            _sb.AppendLine($"troffer at {f.position}, walked {bestA:0.0} m before to {bestB:0.0} m past (signed distance: its hum level) " + samples);
            Summary("approach");
            Place(start, 0f);
            yield return new WaitForSeconds(3f);
        }

        bool Walkable(Vector3 p) => !Physics.CheckCapsule(p + Vector3.up * 0.35f, p + Vector3.up * 1.4f, 0.28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        IEnumerator Turn()
        {
            ResetMeasure();
            yield return new WaitForSeconds(1f);
            float before = TotalHum();
            float min = float.MaxValue, max = 0f;
            for (float a = 0; a < 720f; a += 180f * Time.deltaTime)
            {
                _player.SetYaw(a);
                yield return null;
                Measure();
                float t = TotalHum(); min = Mathf.Min(min, t); max = Mathf.Max(max, t);
            }
            _sb.AppendLine($"total hum gain while turning 720°: start {before:0.0000}, min {min:0.0000}, max {max:0.0000}");
            Summary("turn");
        }

        float TotalHum()
        {
            float s2 = 0f;
            foreach (var s in _ba.GetComponentsInChildren<AudioSource>()) if (s.name.StartsWith("HumVoice")) { float g = Effective(s); s2 += g * g; }
            return Mathf.Sqrt(s2);
        }

        IEnumerator Walls()
        {
            // Find a lit fixture behind a wall: straight line blocked but reachable through a doorway.
            var start = Feet;
            var l = new List<LightSource>();
            _lights.GatherTubes(start, 16f, l);
            int found = 0;
            foreach (var f in l)
            {
                var p = _ba.Field.Query(f.position);
                if (p.walls == 0 || !p.reachable) continue;
                found++;
                if (found > 3) break;
                _sb.AppendLine($"fixture {f.position}: direct {p.direct:0.0} m, path {p.path:0.0} m, walls {p.walls}, visible point {p.visiblePoint}, hum {HumAt(f.position):0.0000}");
            }
            if (found == 0) _sb.AppendLine("no occluded fixture near spawn");
            // Walk straight through walls (noclip) so occlusion changes as abruptly as it ever can,
            // and measure how fast the muffling filters move.
            ResetMeasure();
            _player.SetNoclip(true);
            var prevCut = new Dictionary<AudioLowPassFilter, float>();
            float maxOctPerSec = 0f;
            Vector3 q = start;
            for (float d = 0; d < 30f; d += 1.35f * Time.deltaTime)
            {
                q = start + Vector3.right * d;
                _player.transform.position = q;
                yield return null;
                Measure();
                foreach (var f in _ba.GetComponentsInChildren<AudioLowPassFilter>())
                {
                    if (!f.name.StartsWith("HumVoice")) continue;
                    if (prevCut.TryGetValue(f, out var pc) && f.GetComponent<AudioSource>().isPlaying)
                        maxOctPerSec = Mathf.Max(maxOctPerSec, Mathf.Abs(Mathf.Log(f.cutoffFrequency / pc, 2f)) / Time.deltaTime);
                    prevCut[f] = f.cutoffFrequency;
                }
            }
            _player.SetNoclip(false);
            _sb.AppendLine($"muffling filter fastest change {maxOctPerSec:0.0} octaves/s");
            Summary("walk past walls and doorways");
            Place(start, 0f);
        }

        IEnumerator Faulty()
        {
            var start = Feet;
            var f = NearestTube(start, true);
            if (f.faultyId < 0) { _sb.AppendLine("no faulty tube loaded"); yield break; }
            Vector3 under = new Vector3(f.position.x, start.y, f.position.z);
            Vector3 stand = under;
            for (int i = 0; i < 8 && !Walkable(stand); i++) stand = under + Quaternion.Euler(0, i * 45f, 0) * Vector3.forward * 1.2f;
            Place(stand, 0f);
            int dips = 0, buzzDuringDip = 0, buzzOutsideDip = 0, ticks = 0, frames = 0;
            float lastP = 1f;
            AudioSource buzz = null;
            float t0 = Time.time;
            while (Time.time - t0 < 120f && dips < 6)
            {
                yield return null;
                frames++;
                float p = _lights.PowerLevel(f, Time.time);
                if (lastP > 0.9f && p < 0.5f) dips++;
                if (!buzz) foreach (var s in _ba.GetComponentsInChildren<AudioSource>()) if (s.name.StartsWith("Buzz") && s.isPlaying && Vector3.Distance(s.transform.position, f.position) < 2f) buzz = s;
                if (buzz)
                {
                    bool loud = buzz.volume > 0.002f;
                    if (p < 0.97f && loud) buzzDuringDip++;
                    if (p >= 0.999f && loud) buzzOutsideDip++;
                }
                foreach (var s in _ba.GetComponentsInChildren<AudioSource>()) if (s.name.StartsWith("EventVoice") && s.isPlaying && s.time < Time.deltaTime * 1.5f) ticks++;
                lastP = p;
            }
            _sb.AppendLine($"faulty tube over {Time.time - t0:0} s: stutter dips {dips}, frames buzzing during dips {buzzDuringDip}, frames buzzing while steady {buzzOutsideDip} (release tail), tick starts {ticks}, buzz voice {(buzz ? "assigned" : "none")}");
            Place(start, 0f);
        }

        IEnumerator Walk(float metres, bool stream)
        {
            var start = Feet;
            ResetMeasure();
            int swaps0 = _ba.Reassignments;
            int maxPlaying = 0, maxPool = 0;
            // Noclip-free straight walk through whatever is there by following open space (teleport steps).
            _player.SetNoclip(true);
            Vector3 dir = Vector3.forward;
            Vector3 p = start + Vector3.up * 0.0f;
            float walked = 0f;
            while (walked < metres)
            {
                float step = 1.35f * Time.deltaTime;
                p += dir * step; walked += step;
                _player.transform.position = p;
                yield return null;
                Measure();
                int playing = 0; foreach (var s in FindObjectsByType<AudioSource>()) if (s.isPlaying) playing++;
                maxPlaying = Mathf.Max(maxPlaying, playing); maxPool = Mathf.Max(maxPool, _ba.PoolSize);
            }
            _player.SetNoclip(false);
            yield return new WaitForSeconds(2f);
            int orphans = 0;
            foreach (var s in _ba.GetComponentsInChildren<AudioSource>())
            {
                if (!s.name.StartsWith("HumVoice") || !s.isPlaying) continue;
                var near = NearestTube(s.transform.position);
                if (Vector3.Distance(near.position, s.transform.position) > 30f) orphans++;
            }
            _sb.AppendLine($"walked {metres:0} m (through walls, like noclip): voice swaps {_ba.Reassignments - swaps0}, max sources playing {maxPlaying}, max pool {maxPool}, orphaned voices after {orphans}");
            Summary(stream ? "stream" : "walk");
            Place(start, 0f);
        }

        IEnumerator Still()
        {
            var sb = new StringBuilder();
            float t0 = Time.time;
            while (Time.time - t0 < 20f)
            {
                sb.Append($"{Time.time - t0:0}s:{_chain.GainDb:+0.0;-0.0} ");
                yield return new WaitForSeconds(2f);
            }
            _sb.AppendLine("AGC gain standing still: " + sb);
            // A loud sound right next to the camera, then recovery.
            var src = new GameObject("ProbeLoud").AddComponent<AudioSource>();
            src.transform.position = _ear.position + _ear.forward * 1f;
            src.clip = SoundBank.Strike(0); src.volume = 0.8f; src.spatialBlend = 1f; src.loop = false;
            sb.Clear();
            float g0 = _chain.GainDb;
            src.Play();
            for (int i = 0; i < 6; i++) { yield return new WaitForSeconds(0.25f); sb.Append($"{(i + 1) * 0.25f:0.00}s:{_chain.GainDb:+0.0;-0.0} "); }
            for (int i = 0; i < 6; i++) { yield return new WaitForSeconds(1f); sb.Append($"{2.5f + i:0.0}s:{_chain.GainDb:+0.0;-0.0} "); }
            _sb.AppendLine($"loud sound at +{g0:0.0} dB: " + sb);
            Destroy(src.gameObject);
        }
    }
}
