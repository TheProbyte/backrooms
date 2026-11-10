using System.Collections;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Sounds made in code for build mode's sound spots and triggers (no files): an electrical hum, a fluorescent buzz, a
    /// drip, radio static, knocking, air in a vent. Each a seamless loop, made once and shared.
    /// </summary>
    public static class RoomSoundClips
    {
        static readonly Dictionary<SoundKind, AudioClip> s_loops = new Dictionary<SoundKind, AudioClip>();
        static AudioClip s_slam;
        const int Rate = 44100;

        public static AudioClip Loop(SoundKind kind)
        {
            if (s_loops.TryGetValue(kind, out var c) && c) return c;
            float seconds = kind == SoundKind.Drip ? 6f : kind == SoundKind.Knocking ? 9f : kind == SoundKind.Radio ? 16f : kind == SoundKind.Traffic ? 12f : 4f;
            int n = Mathf.RoundToInt(seconds * Rate);
            var d = new float[n];
            var rng = new System.Random(1000 + (int)kind);
            float Noise() => (float)rng.NextDouble() * 2f - 1f;
            float lp = 0f, lp2 = 0f;
            const float Tau = Mathf.PI * 2f;
            switch (kind)
            {
                case SoundKind.Hum:
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)Rate;          // whole numbers of cycles in 4 s: a seamless loop
                        float beat = 0.85f + 0.15f * Mathf.Sin(Tau * 0.5f * t);
                        d[i] = beat * (0.5f * Mathf.Sin(Tau * 60f * t) + 0.3f * Mathf.Sin(Tau * 120f * t) + 0.12f * Mathf.Sin(Tau * 180f * t + 1f)) * 0.5f;
                    }
                    break;
                case SoundKind.Buzz:
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)Rate, ph = (120f * t) % 1f;
                        float saw = ph * 2f - 1f;
                        lp += (saw - lp) * 0.25f;
                        float wob = 0.75f + 0.25f * Mathf.Sin(Tau * 0.25f * t) * Mathf.Sin(Tau * 3f * t);
                        float crackle = rng.NextDouble() < 0.0004 ? Noise() * 0.6f : 0f;
                        d[i] = (lp * 0.35f * wob + 0.15f * Mathf.Sin(Tau * 240f * t) + crackle) * 0.6f;
                    }
                    break;
                case SoundKind.Air:
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)Rate;
                        lp += (Noise() - lp) * 0.04f; lp2 += (lp - lp2) * 0.08f;
                        d[i] = lp2 * (2.2f + 0.6f * Mathf.Sin(Tau * 0.25f * t));
                    }
                    Fade(d, Rate / 4);
                    break;
                case SoundKind.Static:
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)Rate;
                        lp += (Noise() - lp) * 0.5f;
                        float tone = (t % 1.3f) < 0.35f ? 0.12f * Mathf.Sin(Tau * (440f + 220f * Mathf.Floor(t / 1.3f % 3f)) * t) : 0f;
                        d[i] = lp * 0.22f * (0.7f + 0.3f * Mathf.Sin(Tau * 0.75f * t)) + tone;
                    }
                    Fade(d, Rate / 8);
                    break;
                case SoundKind.Drip:
                    foreach (var at in new[] { 0.4f, 2.3f, 3.1f, 5.0f })
                    {
                        float f0 = 900f + 500f * (float)rng.NextDouble();
                        int s0 = Mathf.RoundToInt(at * Rate);
                        for (int k = 0; k < Rate / 3 && s0 + k < n; k++)
                        {
                            float t = k / (float)Rate;
                            d[s0 + k] += Mathf.Sin(Tau * f0 * (1f + 0.6f * Mathf.Exp(-t * 40f)) * t) * Mathf.Exp(-t * 22f) * 0.6f;
                        }
                    }
                    break;
                case SoundKind.Radio:
                {
                    // A little radio on a shelf (the real world's, SeamRooms): a cheerful tune, tinny, a hiss under it.
                    // Four bars of I-V-vi-IV, four seconds each, a bass on the beat and a tune over the chords.
                    float[][] chords = { new[] { 261.6f, 329.6f, 392f }, new[] { 196f, 246.9f, 293.7f }, new[] { 220f, 261.6f, 329.6f }, new[] { 174.6f, 220f, 261.6f } };
                    float[] tune = { 659.3f, 587.3f, 523.3f, 587.3f, 659.3f, 659.3f, 587.3f, 493.9f, 523.3f, 587.3f, 659.3f, 783.99f, 698.5f, 659.3f, 587.3f, 523.3f };
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)Rate;
                        int bar = Mathf.Min(3, (int)(t / 4f));
                        float inBeat = (t * 2f) % 1f;
                        float s = 0f;
                        foreach (var f in chords[bar]) s += 0.12f * Mathf.Sin(Tau * f * t) * (0.6f + 0.4f * Mathf.Exp(-inBeat * 3f));
                        s += 0.25f * Mathf.Sin(Tau * chords[bar][0] * 0.5f * t) * Mathf.Exp(-inBeat * 5f);
                        int note = Mathf.Min(tune.Length - 1, (int)(t * 1f));
                        float inNote = t % 1f;
                        s += 0.18f * Mathf.Sign(Mathf.Sin(Tau * tune[note] * t)) * Mathf.Exp(-inNote * 2.5f) * 0.6f;
                        // A small speaker: no bass, no top; and the hiss.
                        lp += (s - lp) * 0.35f; lp2 += (lp - lp2) * 0.9f;
                        d[i] = (lp - lp2 * 0.6f) * 0.9f + Noise() * 0.02f;
                    }
                    Fade(d, Rate / 8);
                    break;
                }
                case SoundKind.Traffic:
                {
                    // A road outside, through the wall: a low rumble that swells as cars go by, a lorry, a far horn.
                    float[] passes = { 1.0f, 3.6f, 5.2f, 8.4f, 10.5f };
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)Rate;
                        float swell = 0.25f;
                        foreach (var p in passes) { float u = (t - p) / 0.9f; swell += Mathf.Exp(-u * u) * (p == 5.2f ? 1.6f : 0.9f); }
                        lp += (Noise() - lp) * 0.02f; lp2 += (lp - lp2) * 0.05f;
                        float s = lp2 * 4f * swell;
                        if (t > 7.0f && t < 7.6f) s += 0.05f * Mathf.Sin(Tau * 410f * t) * Mathf.Sin(Mathf.PI * (t - 7.0f) / 0.6f);
                        d[i] = s;
                    }
                    Fade(d, Rate / 2);
                    break;
                }
                case SoundKind.Knocking:
                    foreach (var at in new[] { 0.5f, 0.85f, 1.2f, 6.1f, 6.38f })
                    {
                        int s0 = Mathf.RoundToInt(at * Rate);
                        for (int k = 0; k < Rate / 4 && s0 + k < n; k++)
                        {
                            float t = k / (float)Rate;
                            lp += (Noise() - lp) * 0.1f;
                            d[s0 + k] += (Mathf.Sin(Tau * 85f * t) * 0.8f + lp * 0.9f) * Mathf.Exp(-t * 28f);
                        }
                    }
                    break;
            }
            Normalise(d, 0.8f);
            c = AudioClip.Create("Room_" + kind, n, 1, Rate, false);
            c.SetData(d, 0);
            s_loops[kind] = c;
            return c;
        }

        /// <summary>A door slamming shut: a hard thud and a rattle.</summary>
        public static AudioClip Slam()
        {
            if (s_slam) return s_slam;
            int n = Rate / 2;
            var d = new float[n];
            var rng = new System.Random(77);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.3f;
                d[i] = (Mathf.Sin(Mathf.PI * 2f * 62f * t) * Mathf.Exp(-t * 14f) + lp * Mathf.Exp(-t * 30f) * 0.8f + Mathf.Sin(Mathf.PI * 2f * 410f * t) * Mathf.Exp(-t * 18f) * 0.15f * Mathf.Sin(t * 90f));
            }
            Normalise(d, 0.95f);
            s_slam = AudioClip.Create("Room_Slam", n, 1, Rate, false);
            s_slam.SetData(d, 0);
            return s_slam;
        }

        static void Normalise(float[] d, float peak)
        {
            float m = 1e-5f;
            foreach (var x in d) m = Mathf.Max(m, Mathf.Abs(x));
            for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
        }

        /// <summary>Noise loops: the end faded into the start, so the loop has no click.</summary>
        static void Fade(float[] d, int len)
        {
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)len;
                d[i] = d[i] * t + d[d.Length - len + i] * (1f - t);
            }
        }

        /// <summary>A 3D voice through Steam Audio (walls muffle it), fading out linearly by range.</summary>
        public static AudioSource Source(GameObject go, float volume, float range, bool loop)
        {
            var a = Interaction.ItemModels.Source(go, Mathf.Max(1.5f, range));
            a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = 0.6f; a.maxDistance = Mathf.Max(1.5f, range);
            a.volume = volume; a.loop = loop; a.playOnAwake = false; a.dopplerLevel = 0f;
            return a;
        }
    }

    /// <summary>Who the triggers and linked doors act on: the player, or (build mode's WALK) the builder's walker.</summary>
    public static class RoomPlayer
    {
        public static Transform Override;
        public static System.Action<Vector3, float> WarpOverride;
        static CamcorderPlayerController s_player;
        static float s_next;

        public static Transform Current
        {
            get
            {
                if (Override) return Override;
                if (!s_player && Time.unscaledTime >= s_next) { s_player = Object.FindFirstObjectByType<CamcorderPlayerController>(); s_next = Time.unscaledTime + 1f; }
                return s_player ? s_player.transform : null;
            }
        }

        public static void Warp(Vector3 position, float yawDelta)
        {
            if (Override && WarpOverride != null) { WarpOverride(position, yawDelta); return; }
            if (s_player) s_player.Warp(position, yawDelta);
        }
    }

    /// <summary>A sound spot of a custom room (or a buzzing fitting): its loop, started at a random point.</summary>
    public class RoomSoundEmitter : MonoBehaviour
    {
        AudioSource _a;
        float _level;

        public void Init(SoundKind kind, float volume, float range)
        {
            _level = Mathf.Clamp01(volume) * 0.7f;
            _a = RoomSoundClips.Source(gameObject, _level * GameSettings.Ambience, range, true);
            _a.clip = RoomSoundClips.Loop(kind);
        }

        // The building's sound: the AMBIENCE setting (Settings) as it changes.
        void Update() { if (_a) _a.volume = _level * GameSettings.Ambience; }

        // (Its chunk is built hidden and shown when complete: it starts once it is there.)
        void OnEnable()
        {
            if (!_a || _a.isPlaying) return;
            _a.timeSamples = Random.Range(0, _a.clip.samples);
            _a.Play();
        }
    }

    /// <summary>
    /// A custom room's trigger: when the player walks into its cells, something happens (after a delay): the lights of those
    /// rooms cut out or come on (LightZones, as a switch does), flicker for a while, a door hung open in a doorway slams shut,
    /// or a sound plays. Once, or every time the player comes back in.
    /// </summary>
    public class RoomTriggerZone : MonoBehaviour
    {
        static readonly HashSet<ulong> s_fired = new HashSet<ulong>();
        ulong _id;
        Vector3 _size;
        TriggerAction _action;
        SoundKind _sound;
        bool _once, _inside, _busy;
        float _delay;
        Vector3Int _chunk;
        List<int> _rooms;
        Transform _leaf;

        public void Init(ulong id, Vector3 size, TriggerAction action, SoundKind sound, bool once, float delay, Vector3Int chunk, List<int> rooms, Transform leaf)
        {
            _id = id; _size = size; _action = action; _sound = sound; _once = once; _delay = delay; _chunk = chunk; _rooms = rooms; _leaf = leaf;
        }

        void Update()
        {
            if (_busy || (_once && s_fired.Contains(_id))) return;
            var player = RoomPlayer.Current;
            if (!player) return;
            var local = transform.InverseTransformPoint(player.position + Vector3.up * 0.2f);
            bool inside = Mathf.Abs(local.x) < _size.x * 0.5f && Mathf.Abs(local.z) < _size.z * 0.5f && local.y > -0.5f && local.y < _size.y + 1f;
            if (inside && !_inside) StartCoroutine(Fire());
            _inside = inside;
        }

        IEnumerator Fire()
        {
            _busy = true;
            s_fired.Add(_id);
            if (_delay > 0f) yield return new WaitForSeconds(_delay);
            switch (_action)
            {
                case TriggerAction.LightsOut: foreach (int r in _rooms) LightZones.Set(_chunk, r, true); Thud(0.35f); break;
                case TriggerAction.LightsOn: foreach (int r in _rooms) LightZones.Set(_chunk, r, false); break;
                case TriggerAction.Flicker:
                    for (int k = 0; k < 9; k++)
                    {
                        bool off = k % 2 == 0;
                        foreach (int r in _rooms) LightZones.Set(_chunk, r, off, true);
                        yield return new WaitForSeconds(off ? Random.Range(0.05f, 0.25f) : Random.Range(0.08f, 0.6f));
                    }
                    foreach (int r in _rooms) LightZones.Set(_chunk, r, false, true);
                    break;
                case TriggerAction.DoorSlam:
                    if (_leaf)
                    {
                        var from = _leaf.localRotation; var to = from * Quaternion.Euler(0f, 88f, 0f);
                        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.22f) { _leaf.localRotation = Quaternion.Slerp(from, to, t * t); yield return null; }
                        _leaf.localRotation = to;
                        var a = RoomSoundClips.Source(_leaf.gameObject, 1f, 30f, false);
                        a.PlayOneShot(RoomSoundClips.Slam());
                    }
                    break;
                case TriggerAction.Sound:
                {
                    var go = new GameObject("TriggerSound");
                    go.transform.position = transform.position + Vector3.up * 1.5f;
                    var a = RoomSoundClips.Source(go, 0.9f, 20f, false);
                    a.clip = RoomSoundClips.Loop(_sound); a.Play();
                    Destroy(go, Mathf.Min(a.clip.length, 9f));
                    break;
                }
            }
            yield return new WaitForSeconds(0.5f);
            if (!_once) s_fired.Remove(_id);
            _busy = false;
        }

        void Thud(float volume)
        {
            var go = new GameObject("TriggerThud");
            go.transform.position = transform.position + Vector3.up * 2.4f;
            var a = RoomSoundClips.Source(go, volume, 18f, false);
            a.pitch = 1.8f; a.PlayOneShot(RoomSoundClips.Slam());
            Destroy(go, 1f);
        }

        /// <summary>A new world forgets which triggers fired.</summary>
        public static void ResetAll() => s_fired.Clear();
    }

    /// <summary>
    /// Two linked doorways: walk through one and you come out of the other, going on as you were (turned with it; flip: the
    /// other way round). Put them at the two ends of a hallway and it never ends; put one on a wall you know leads outside and
    /// it leads somewhere else. Frames: the doorway's middle on the wall line at the floor, +Z its line's normal.
    /// </summary>
    public class LinkedDoorway : MonoBehaviour
    {
        Pose _a, _b;
        bool _flip;
        float _aFrom, _aTo, _sill, _top, _bWidth;
        float _prevA = float.NaN, _prevB = float.NaN, _cool;

        public void Init(Pose a, Pose b, bool flip, float aFrom, float aTo, float sill, float top, float bWidth)
        {
            _a = a; _b = b; _flip = flip; _aFrom = aFrom; _aTo = aTo; _sill = sill; _top = top; _bWidth = bWidth;
        }

        void LateUpdate()
        {
            var player = RoomPlayer.Current;
            if (!player) return;
            var p = player.position;
            float half = (_aTo - _aFrom) * 0.5f;
            Cross(_a, _b, half, ref _prevA, ref _prevB, p);
            if (_cool <= 0f) Cross(_b, _a, _bWidth * 0.5f, ref _prevB, ref _prevA, p);
            _cool -= Time.deltaTime;
        }

        void Cross(Pose from, Pose to, float half, ref float prevFrom, ref float prevTo, Vector3 p)
        {
            var local = Quaternion.Inverse(from.rotation) * (p - from.position);
            bool inSpan = Mathf.Abs(local.x) <= half + 0.05f && local.y > _sill - 0.6f && local.y < _top && Mathf.Abs(local.z) < 1f;
            float prev = prevFrom;
            prevFrom = inSpan ? local.z : float.NaN;
            if (_cool > 0f || float.IsNaN(prev) || !inSpan || Mathf.Sign(prev) == Mathf.Sign(local.z)) return;
            // Through: the same place in the other doorway, going on the same way (turned with it).
            var turn = to.rotation * (_flip ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity) * Quaternion.Inverse(from.rotation);
            var target = to.position + turn * (p - from.position);
            float yaw = turn.eulerAngles.y;
            RoomPlayer.Warp(target, yaw);
            var after = Quaternion.Inverse(to.rotation) * (target - to.position);
            prevTo = after.z;
            _cool = 0.35f;
        }
    }
}
