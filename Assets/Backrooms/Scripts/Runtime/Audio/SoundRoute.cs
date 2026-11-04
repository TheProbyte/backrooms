using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// The way a lasting sound of a thing in the world reaches you (2026-11-04, the owner's "sound walls block"): the
    /// building's own hum already went round walls through the doorways (BuildingAudio, AcousticField), but the things'
    /// sounds (a boombox playing, a cart rolling, a drinks machine, the dark dragging a chair, the elevator) only had Steam
    /// Audio's direct path: in the next room with the door open beside you they were muffled as if through the wall, and
    /// came from behind it. Now each of them takes the same walking path as the building's sound, the way the big audio
    /// engines do rooms and portals (Wwise's spatial audio, Hitman's room graph): with a clear line it is heard where it is;
    /// round a corner it is heard from the doorway it comes through, as far away as the way round is and a little duller
    /// the more it bends; when the way round is longer than going through the wall is loud (a thin wall right beside it),
    /// through the wall: quiet and muffled, about 18 dB and a 700 Hz cut a wall, 9 dB and 1.6 kHz a shut door. All of it
    /// eased (no jumps as you pass a doorway). It moves its own voice (this object, a child of the thing) to where the
    /// sound is heard from, and gives Steam Audio the occlusion and transmission itself (Steam Audio still does the HRTF).
    /// Another storey, or outside the field round you: Steam Audio's own simulation, as before.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SoundRoute : MonoBehaviour
    {
        Vector3 _home;                 // where the sound really is (the parent's frame)
        Transform _parent;
        AudioLowPassFilter _lp;
        AudioSource _src;
        Vector3 _at;                   // where it is heard from now (world)
        float _occ = 1f, _trans = 1f, _cut = 22000f;
        bool _init;
        float _next;
#if STEAMAUDIO_ENABLED
        SteamAudio.SteamAudioSource _steam;
#endif
        static AudioListener s_listener;

        /// <summary>The thing's own cut on its sound (a cart rolling on carpet), under the route's (Hz).</summary>
        [System.NonSerialized] public float baseCutoff = 22000f;

        /// <summary>For tests: how it is heard now (0 straight, 1 round a corner, 2 through walls, -1 Steam Audio's own).</summary>
        public int Route { get; private set; } = -1;

        /// <summary>Routes a source (its own object, a child of the thing it belongs to) by the walking path.</summary>
        public static SoundRoute Add(AudioSource src)
        {
            if (!src) return null;
            var had = src.GetComponent<SoundRoute>();
            if (had) return had;
            if (!src.transform.parent) return null;          // (it must be its own voice: moving it moves nothing else)
            return src.gameObject.AddComponent<SoundRoute>();
        }

        void Awake()
        {
            _src = GetComponent<AudioSource>();
            _parent = transform.parent;
            _home = transform.localPosition;
            // One low-pass a voice (Unity allows one of a kind): the thing's own wish (a cart on carpet) is baseCutoff.
            _lp = GetComponent<AudioLowPassFilter>();
            if (!_lp) _lp = gameObject.AddComponent<AudioLowPassFilter>();
            _lp.cutoffFrequency = 22000f;
#if STEAMAUDIO_ENABLED
            _steam = GetComponent<SteamAudio.SteamAudioSource>();
#endif
        }

        /// <summary>Where the sound really is (world).</summary>
        public Vector3 Home => _parent ? _parent.TransformPoint(_home) : transform.position;

        void LateUpdate()
        {
            if (!_parent) return;
            Vector3 home = Home;
            var ba = BuildingAudio.Instance;
            var field = ba ? ba.Field : null;
            if (!s_listener) s_listener = FindAnyObjectByType<AudioListener>();
            bool sounding = _src.isPlaying;
            if (field == null || !field.Built || !s_listener || !sounding)
            {
                if (Route != -1 || !_init) Direct(home);
                return;
            }
            Vector3 ear = s_listener.transform.position;
            float dt = Time.deltaTime;
            // Worked out a few times a second (the field is a lookup; the walk back to the visible point is cached in it).
            Vector3 target = _at;
            float occW = _occ, transW = _trans, cutW = _cut;
            int route = Route;
            if (Time.time >= _next || !_init)
            {
                _next = Time.time + 0.1f;
                var p = field.Query(home);
                if (!p.inField || Mathf.Abs(home.y - ear.y) > 2.6f) { Direct(home); return; }
                if (p.walls == 0 && p.doors == 0) { route = 0; target = home; occW = 1f; transW = 1f; cutW = 22000f; }
                else
                {
                    // Through the walls: the straight line's walls and shut doors.
                    float through = Mathf.Pow(10f, (-18f * p.walls - 9f * p.doors) / 20f);
                    float throughCut = p.walls > 0 ? 700f / Mathf.Max(1, p.walls) : 1600f / Mathf.Max(1, p.doors);
                    // Round by the walking path: as loud as its length allows next to the straight distance (inverse
                    // distance, as the sources roll off), duller the more it turns at the doorway.
                    float around = p.reachable ? Mathf.Clamp01(Mathf.Max(0.3f, p.direct) / Mathf.Max(0.3f, p.path)) : 0f;
                    Vector3 vis = p.visiblePoint;
                    Vector3 toVis = vis - ear, toHome = home - ear;
                    float bend = toVis.sqrMagnitude > 1e-4f && toHome.sqrMagnitude > 1e-4f ? Vector3.Angle(toVis, toHome) : 0f;
                    float aroundCut = Mathf.Lerp(16000f, 2600f, Mathf.Clamp01(bend / 120f));
                    if (around >= through)
                    {
                        route = 1;
                        // Heard from the doorway's direction, as far away as the way round (so the distance rolloff is the path's).
                        Vector3 dir = toVis.sqrMagnitude > 1e-4f ? toVis.normalized : toHome.normalized;
                        target = ear + dir * Mathf.Max(0.3f, p.path);
                        target.y = home.y;
                        occW = 1f; transW = 1f; cutW = aroundCut;
                    }
                    else { route = 2; target = home; occW = 0f; transW = through; cutW = throughCut; }
                }
            }
            if (!_init) { _at = target; _occ = occW; _trans = transW; _cut = cutW; _init = true; }
            float k = 1f - Mathf.Exp(-dt / 0.25f);
            _at = Vector3.Lerp(_at, target, k);
            _occ = Mathf.Lerp(_occ, occW, k);
            _trans = Mathf.Lerp(_trans, transW, k);
            _cut = Mathf.Exp(Mathf.Lerp(Mathf.Log(_cut), Mathf.Log(cutW), k));
            Route = route;
            transform.position = _at;
            _lp.cutoffFrequency = Mathf.Min(_cut, baseCutoff);
#if STEAMAUDIO_ENABLED
            if (_steam)
            {
                _steam.occlusionInput = SteamAudio.OcclusionInput.UserDefined;
                _steam.occlusionValue = _occ;
                _steam.transmissionInput = SteamAudio.TransmissionInput.UserDefined;
                _steam.transmissionLow = _trans; _steam.transmissionMid = _trans; _steam.transmissionHigh = _trans;
            }
#endif
        }

        /// <summary>Heard where it is, by Steam Audio's own occlusion (another storey, no field, silent).</summary>
        void Direct(Vector3 home)
        {
            _init = false;
            Route = -1;
            transform.position = home;
            if (_lp) _lp.cutoffFrequency = baseCutoff;
            _at = home; _occ = 1f; _trans = 1f; _cut = 22000f;
#if STEAMAUDIO_ENABLED
            if (_steam)
            {
                _steam.occlusionInput = SteamAudio.OcclusionInput.SimulationDefined;
                _steam.transmissionInput = SteamAudio.TransmissionInput.SimulationDefined;
            }
#endif
        }
    }
}
