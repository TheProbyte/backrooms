#if STEAMAUDIO_ENABLED
using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// Takes a silent Steam Audio source out of the simulation and puts it back the frame it plays. Steam Audio works out
    /// every registered source's occlusion and transmission on the main thread each frame (8 volumetric rays and a wall
    /// test each), sounding or not: 68 sources were registered in a typical spot (30 floor lamps, every item, the impact
    /// and settling voices), 1 of them playing, and it cost 1.7 ms a frame, the second most expensive script.
    ///
    /// It runs early in LateUpdate (order -1000), after Update, the physics and coroutines (where sounds start) and
    /// before Steam Audio's own LateUpdate, so a sound that starts this frame is simulated this frame (no unmuffled first
    /// moment from behind a wall). A source sleeps after <see cref="Linger"/> seconds of silence.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SteamAudioSleep : MonoBehaviour
    {
        /// <summary>Seconds of silence before the source leaves the simulation.</summary>
        public const float Linger = 1f;

        AudioSource _audio;
        SteamAudio.SteamAudioSource _steam;
        float _quietSince = -1f;

        /// <summary>Sources awake now (for tests and the perf readout).</summary>
        public static int AwakeCount { get; private set; }

        public void Bind(AudioSource audio, SteamAudio.SteamAudioSource steam) { _audio = audio; _steam = steam; if (isActiveAndEnabled && steam && steam.enabled) AwakeCount++; }

        void OnDisable()
        {
            if (_steam && _steam.enabled) AwakeCount--;
        }

        void OnEnable()
        {
            if (_steam && _steam.enabled) AwakeCount++;
        }

        void LateUpdate()
        {
            if (!_audio || !_steam) return;
            bool sounding = _audio.isPlaying && _audio.enabled;
            if (sounding)
            {
                _quietSince = -1f;
                if (!_steam.enabled) { _steam.enabled = true; AwakeCount++; }
                return;
            }
            if (!_steam.enabled) return;
            if (_quietSince < 0f) { _quietSince = Time.time; return; }
            if (Time.time - _quietSince > Linger) { _steam.enabled = false; AwakeCount--; }
        }
    }
}
#endif
