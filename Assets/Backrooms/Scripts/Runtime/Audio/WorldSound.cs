using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// Every sound of something in the world goes through Steam Audio (owner, 2026-10-30: "all the audio should go through
    /// steam audio"): made with <see cref="Interaction.ItemModels.Source"/> (HRTF, occluded by walls and shut doors, muffled
    /// through them). This is the one-shot side: a small pool of such voices for a sound at a point (a chalk tick, a cord's
    /// whip; it was AudioSource.PlayClipAtPoint, a plain 3D voice heard through walls). The player's own body (steps,
    /// breath) and the screens' sounds stay plain: they are not out in the world.
    ///
    /// Leaving Play Mode (or the game) with Steam Audio voices still playing once crashed its mixer thread (2026-10-27): on
    /// quitting, every Steam Audio voice is stopped first (<see cref="StopAll"/>).
    /// </summary>
    public static class WorldSound
    {
        const int Voices = 12;
        static readonly List<AudioSource> s_voices = new List<AudioSource>();
        static GameObject s_root;
        static int s_next;

        /// <summary>A sound at a point in the world, through Steam Audio.</summary>
        public static void PlayAt(AudioClip clip, Vector3 at, float volume, float maxDistance = 16f, float pitch = 1f)
        {
            if (!clip || volume < 0.005f || !Application.isPlaying) return;
            Ensure();
            AudioSource src = null;
            for (int i = 0; i < s_voices.Count; i++)
            {
                var a = s_voices[(s_next + i) % s_voices.Count];
                if (a && !a.isPlaying) { src = a; s_next = (s_next + i + 1) % s_voices.Count; break; }
            }
            if (!src) { src = s_voices[s_next]; s_next = (s_next + 1) % s_voices.Count; }
            if (!src) return;
            src.transform.position = at;
            src.maxDistance = maxDistance;
            src.pitch = pitch;
            src.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        static void Ensure()
        {
            if (s_root) return;
            s_root = new GameObject("WorldSound");
            Object.DontDestroyOnLoad(s_root);
            s_voices.Clear();
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(s_root.transform, false);
                s_voices.Add(Interaction.ItemModels.Source(go, 16f));
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            s_root = null; s_voices.Clear(); s_next = 0;
            Application.quitting -= StopAll;
            Application.quitting += StopAll;
        }

        /// <summary>Stops every Steam Audio voice and switches its Steam Audio part off (before the mixer goes away).</summary>
        public static void StopAll()
        {
#if STEAMAUDIO_ENABLED
            foreach (var s in Object.FindObjectsByType<SteamAudio.SteamAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!s) continue;
                var a = s.GetComponent<AudioSource>();
                if (a) { a.Stop(); a.enabled = false; }
                s.enabled = false;
            }
#endif
        }
    }
}
