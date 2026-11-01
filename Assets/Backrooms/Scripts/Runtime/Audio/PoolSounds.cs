using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// The Poolrooms' own small sounds, in their places (2026-11-01, the realism plan 8):
    ///  - drops falling from the roof openings now and then, a plink into the pool (with a ring on the water) or a tick on
    ///    the deck, one at a time, never a loop (the shower drips' loop had rung on in the reverb, 2026-10-31);
    ///  - while you wade, the ripples you make reaching the nearest pool wall a moment later (at the speed shallow water
    ///    carries them) and lapping there.
    /// 2026-11-01, the owner: "a humming noise thats loud and annoying, and weird drips that are too loud": the filtration
    /// hum at the skimmers and grilles is gone (there was always one within reach), the drips a third as loud, half as
    /// often, heard over 10 m (was 18), and softer (ProceduralSounds.Plink).
    /// Added by PoolWorld; quiet when the player is not in the Poolrooms.
    /// </summary>
    public class PoolSounds : MonoBehaviour
    {
        [Range(0f, 1f)] public float dripVolume = 0.1f;
        [Range(0f, 1f)] public float lapVolume = 0.2f;

        AudioClip[] _plinks, _laps;
        AudioLowPassFilter _muffle;          // under the water (the realism plan 11)
        AudioListener _listener;
        float _nextDrip = 3f, _lapTimer;
        int _variant;
        readonly List<(float at, Vector3 pos, float vol)> _pending = new List<(float, Vector3, float)>();

        void Update()
        {
            var w = PoolWorld.Instance;
            var cam = Camera.main;
            if (!w || !cam) return;
            var p = cam.transform.position;
            bool inPool = w.IsPool(p);
            Muffle();
            if (!inPool) { _pending.Clear(); return; }
            float dt = Time.deltaTime;
            Drips(w, p, dt);
            Laps(w, dt);
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pending[i].at) continue;
                if (_laps == null) { _laps = new AudioClip[4]; for (int k = 0; k < 4; k++) _laps[k] = ProceduralSounds.Lap(k); }
                WorldSound.PlayAt(_laps[_variant++ & 3], _pending[i].pos, _pending[i].vol, 14f, Random.Range(0.9f, 1.1f));
                _pending.RemoveAt(i);
            }
        }

        void Drips(PoolWorld w, Vector3 p, float dt)
        {
            _nextDrip -= dt;
            if (_nextDrip > 0f) return;
            _nextDrip = Random.Range(9f, 24f);
            float C = OfficeBiome.CellSize;
            int storey = w.StoreyOf(p.y);
            float pitch = w.streamer && w.streamer.Settings != null ? w.streamer.Settings.storeyPitch : 9f;
            for (int tries = 0; tries < 16; tries++)
            {
                int cx = Mathf.FloorToInt(p.x / C) + Random.Range(-8, 9), cz = Mathf.FloorToInt(p.z / C) + Random.Range(-8, 9);
                var cell = w.MapCell(cx, cz, storey);
                if (!cell.valid || cell.sky == SkylightShape.None) continue;
                var at = new Vector3((cx + Random.Range(0.3f, 0.7f)) * C, storey * pitch, (cz + Random.Range(0.3f, 0.7f)) * C);
                if (w.WaterAt(at, out float level))
                {
                    at.y = level;
                    if (_plinks == null) { _plinks = new AudioClip[6]; for (int k = 0; k < 6; k++) _plinks[k] = ProceduralSounds.Plink(k); }
                    WorldSound.PlayAt(_plinks[_variant++ % 6], at, dripVolume, 10f, Random.Range(0.92f, 1.08f));
                    PoolRipples.Splash(at, 0.05f, -0.004f);
                }
                else WorldSound.PlayAt(SoundBank.Drip(_variant++), at, dripVolume * 0.5f, 8f, Random.Range(0.9f, 1.1f));
                return;
            }
        }

        void Laps(PoolWorld w, float dt)
        {
            if (!PoolRipples.PlayerInWater) return;
            var player = w.streamer ? w.streamer.player : null;
            var ctrl = player ? player.GetComponent<CamcorderPlayerController>() : null;
            float speed = ctrl ? ctrl.PlanarVelocity.magnitude : 0f;
            if (speed < 0.3f) return;
            _lapTimer -= dt * Mathf.Clamp(speed, 0.3f, 2.5f);
            if (_lapTimer > 0f) return;
            _lapTimer = Random.Range(0.8f, 1.5f);
            // The nearest wall of the water, along the four axes and the diagonals.
            var p = player.position;
            float best = 99f; Vector3 bestPt = default;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (float r = 0.4f; r < 5f; r += 0.3f)
                {
                    var q = p + d * r;
                    if (w.WaterAt(q, out float lv)) continue;
                    if (r < best) { best = r; bestPt = p + d * (r - 0.15f); bestPt.y = lv == 0f ? p.y : lv; }
                    break;
                }
            }
            if (best > 5f) return;
            w.WaterAt(p, out float level);
            bestPt.y = level;
            float wave = Mathf.Sqrt(9.81f * 0.4f);                 // shallow water's speed, about 2 m/s
            _pending.Add((Time.time + best / wave + 0.1f, bestPt, lapVolume * Mathf.Clamp01(speed / 2f + 0.3f)));
        }

        /// <summary>Under the water everything is heard dull and low, through the water.</summary>
        void Muffle()
        {
            if (!_listener) _listener = FindAnyObjectByType<AudioListener>();
            if (!_listener) return;
            bool under = PoolWorld.CameraUnderwater;
            if (!_muffle)
            {
                if (!under) return;
                _muffle = _listener.GetComponent<AudioLowPassFilter>();
                if (!_muffle) _muffle = _listener.gameObject.AddComponent<AudioLowPassFilter>();
                _muffle.cutoffFrequency = 22000f; _muffle.lowpassResonanceQ = 1f;
            }
            float target = under ? 600f : 22000f;
            float f = Mathf.Exp(Mathf.MoveTowards(Mathf.Log(_muffle.cutoffFrequency), Mathf.Log(target), Time.deltaTime * 12f));
            _muffle.cutoffFrequency = f;
            _muffle.enabled = under || f < 21000f;
        }

    }
}
