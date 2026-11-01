using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Running water in the Poolrooms (a spout, a cascade; 2026-10-31): the only place you hear it, so it is a landmark by
    /// ear. Its sound (a loop through Steam Audio, ProceduralSounds.RunningWater), and while you are near, spray where it
    /// lands (PoolSplashes) and a steady stir of the water there (PoolRipples). Made by the chunk (OfficeChunkBuilder).
    /// </summary>
    public class PoolRunningWater : MonoBehaviour
    {
        int _kind;
        float _strength, _nextSpray, _nextRipple;
        AudioSource _src;
        Transform _player;

        public void Init(int kind, float strength)
        {
            _kind = kind; _strength = strength;
            _src = Interaction.ItemModels.Source(gameObject, kind == 1 ? 26f : 18f);
            _src.clip = SoundBank.RunningWater(kind);
            _src.loop = true;
            _src.volume = (kind == 1 ? 0.55f : 0.4f) * Mathf.Lerp(0.7f, 1f, strength);
            _src.timeSamples = Random.Range(0, _src.clip.samples);
            _src.Play();
        }

        void Update()
        {
            if (!_player) { var w = PoolWorld.Instance; _player = w && w.streamer ? w.streamer.player : null; if (!_player) return; }
            float d2 = (transform.position - _player.position).sqrMagnitude;
            if (d2 > 25f * 25f) return;
            float now = Time.time;
            if (now >= _nextSpray)
            {
                _nextSpray = now + (_kind == 1 ? 0.06f : 0.09f);
                var drift = new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f));
                PoolSplashes.Burst(transform.position + drift * 0.5f, _kind == 1 ? 4 : 2, _kind == 1 ? 1.1f : 0.9f, drift, 0.7f, 0.9f);
            }
            if (now >= _nextRipple && d2 < 12f * 12f)
            {
                _nextRipple = now + 0.14f;
                PoolRipples.Splash(transform.position + new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.2f, 0.2f)), _kind == 1 ? 0.35f : 0.18f, -0.006f * (0.5f + _strength));
            }
        }
    }
}
