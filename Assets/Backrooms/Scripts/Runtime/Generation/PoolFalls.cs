using System.Collections.Generic;
using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The water park's moving water (2026-11-01; built in OfficeChunkBuilder.WaterPark, listed in PoolChunk.fittings):
    ///  - where a mushroom's curtain or an arch's stream lands, the surface is kept stirred (the ripple simulation) and
    ///    a little spray thrown up, and the nearest two are heard: falling water on water, quiet (ProceduralSounds
    ///    .WaterCurtain), through Steam Audio;
    ///  - a wave pool runs sessions as real ones do: a wave every four seconds or so for forty seconds, then half a minute
    ///    of calm. Each wave lifts a line of water at the chambers that runs off down the pool, with the chamber's surge.
    /// Added by PoolWorld; idle when the player is not in the Poolrooms.
    /// </summary>
    public class PoolFalls : MonoBehaviour
    {
        [Range(0f, 1f)] public float curtainVolume = 0.07f;
        [Range(0f, 1f)] public float waveVolume = 0.14f;
        public const float Reach = 22f, HearAt = 12f;
        public const float WaveEvery = 4.2f, SessionOn = 40f, SessionOff = 30f;

        struct Fall { public Vector3 p, d; public int kind; }
        readonly List<Fall> _near = new List<Fall>();
        float _gather, _spray, _nextWave = -1f;
        int _wavePick;
        AudioSource[] _loops;
        AudioClip[] _curtain, _surge;

        void Update()
        {
            var w = PoolWorld.Instance;
            var player = w && w.streamer ? w.streamer.player : null;
            if (!w || !player || !w.IsPool(player.position)) { Quiet(); return; }
            var p = player.position;
            float dt = Time.deltaTime;
            _gather -= dt;
            if (_gather <= 0f) { _gather = 0.3f; Gather(p); }
            if (_near.Count == 0) { Quiet(); return; }
            _spray -= dt;
            bool spray = _spray <= 0f;
            if (spray) _spray = 0.12f;
            int stirred = 0;
            foreach (var f in _near)
            {
                if ((f.p - p).sqrMagnitude > HearAt * HearAt * 1.5f) continue;
                if (f.kind == 5)
                {
                    // A random point round the ring each frame; spray thrown out and up off it now and then.
                    if (stirred++ < 3)
                    {
                        float a = Random.value * Mathf.PI * 2f;
                        var at = f.p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * f.d.x;
                        PoolRipples.Splash(at, 0.14f, -0.0035f);
                        if (spray) PoolSplashes.Burst(at, 2, 0.5f + 0.15f * f.d.y, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.25f, 0.5f, 0.7f);
                    }
                }
                else if (f.kind == 6)
                {
                    if (stirred++ < 3)
                    {
                        PoolRipples.Splash(f.p + Random.insideUnitSphere * 0.04f, 0.1f, -0.003f);
                        if (spray) PoolSplashes.Burst(f.p, 2, 0.6f, f.d * 0.3f, 0.4f, 0.6f);
                    }
                }
            }
            Waves(p);
            Sounds(p);
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
                for (int k = -2; k <= 2; k++)
                    PoolRipples.Splash(f.p + n * 0.9f + t3 * (k * half * 0.45f), 0.9f, 0.045f);
                if (_surge == null) { _surge = new AudioClip[3]; for (int k = 0; k < 3; k++) _surge[k] = ProceduralSounds.WaveSurge(k); }
                WorldSound.PlayAt(_surge[_wavePick++ % 3], f.p + n * 0.5f, waveVolume, 26f, Random.Range(0.94f, 1.04f));
                break;                                   // the nearest wave pool only (the simulation is round the player)
            }
        }

        /// <summary>The nearest two falls heard, each from its own looping voice.</summary>
        void Sounds(Vector3 p)
        {
            if (_loops == null)
            {
                _loops = new AudioSource[2];
                _curtain = new AudioClip[2];
                for (int k = 0; k < 2; k++)
                {
                    _curtain[k] = ProceduralSounds.WaterCurtain(k);
                    var go = new GameObject("FallVoice" + k);
                    go.transform.SetParent(transform, false);
                    var a = Interaction.ItemModels.Source(go, HearAt);
                    a.clip = _curtain[k]; a.loop = true; a.volume = 0f;
                    a.time = k * 3.1f;
                    _loops[k] = a;
                }
            }
            int used = 0;
            foreach (var f in _near)
            {
                if (used >= 2) break;
                if (f.kind != 5 && f.kind != 6) continue;
                if ((f.p - p).sqrMagnitude > HearAt * HearAt) break;
                var a = _loops[used++];
                a.transform.position = f.p + Vector3.up * 0.3f;
                float target = curtainVolume * (f.kind == 5 ? 1f : 0.55f);
                a.volume = Mathf.MoveTowards(a.volume, target, Time.deltaTime * 0.2f);
                if (!a.isPlaying) a.Play();
            }
            for (int k = used; k < 2; k++) Fade(_loops[k]);
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
