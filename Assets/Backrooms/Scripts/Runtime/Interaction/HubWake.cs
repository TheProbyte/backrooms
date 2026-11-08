using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Waking in the hub (2026-11-07, the owner: "behind each cot in the wall will be a small opening leading into darkness
    /// with the floor being conveyor and we will be conveyored onto our cots ... like the orientation video"). Each cot has a
    /// return chute behind its head (HubChute, OverflowRooms), shut by a steel shutter. A world loaded (or, later, someone
    /// found): your chute's shutter is up, you lie on its belt in the dark looking up at its roof; the belt starts with a
    /// clunk and carries you out feet first, through the hole, until your head is on the pillow and you are looking up at
    /// the ceiling tiles. Then Space, E or a walk key gets you up at the foot of the cot, and the shutter drops behind you;
    /// nobody climbs back in (a shut shutter is solid).
    ///
    /// The lying uses the seat the controller already has (CamcorderPlayerController.seated: held in place, the eye at
    /// seatEye over seatFeet, free to look about); the body is posed half reclining (FirstPersonBody.SetSeat).
    /// </summary>
    public class HubWake : MonoBehaviour
    {
        public static HubWake Instance { get; private set; }

        /// <summary>Is the player being brought out (or lying on the cot still)?</summary>
        public bool Busy => _phase != Phase.Done;

        enum Phase { Dark, Belt, OnCot, Done }
        Phase _phase = Phase.Done;
        float _t, _bumpLast;
        int _cot;
        CamcorderPlayerController _player;
        FirstPersonBody _body;
        PropGrabber _hand;
        readonly Transform[] _shutters = new Transform[4];
        readonly BoxCollider[] _shutterBlocks = new BoxCollider[4];
        readonly float[] _open = new float[4];
        AudioSource _belt, _clunk;
        static AudioClip _beltClip;

        const float Sill = 0.42f, Top = 0.92f, Lie = 0.47f, Eye = 0.13f;
        const float StartBack = 1.45f, EndOut = 0.3f, Speed = 0.3f, DarkTime = 1.6f;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>The chute's mouth of cot i: on the room's wall face, the way into the room.</summary>
        static Vector3 Mouth(int i) => HubPlan.World(CustomRooms.OverflowRooms.WakeSpots[i].x, 3.6f + OfficeBiome.WallThickness * 0.5f);

        void Start()
        {
            // The shutters, all shut (yours opens when you are brought out).
            for (int i = 0; i < 4; i++)
            {
                var tr = FittingModels.Spawn("HubChuteShutter", transform, 0xFFFFFFFFu, Mouth(i) + new Vector3(0f, Sill - 0.01f, -OfficeBiome.WallThickness * 0.5f), Quaternion.identity);   // in the wall's middle
                if (!tr) continue;
                foreach (var r in tr.GetComponentsInChildren<Renderer>()) r.renderingLayerMask = uint.MaxValue;
                var block = tr.gameObject.AddComponent<BoxCollider>();
                block.center = new Vector3(0f, (Top - Sill) * 0.5f, 0f); block.size = new Vector3(0.66f, Top - Sill + 0.04f, 0.04f);
                _shutters[i] = tr; _shutterBlocks[i] = block;
            }
            if (!_beltClip) _beltClip = MakeBelt();
            _belt = Source("Belt", _beltClip, true);
            _clunk = Source("Clunk", null, false);
        }

        AudioSource Source(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var a = ItemModels.Source(go, 10f);
            a.clip = clip; a.loop = loop; a.volume = 0f; a.minDistance = 1f;
            Audio.SoundRoute.Add(a);
            return a;
        }

        /// <summary>A conveyor's rattle: rollers ticking past over a low motor drone.</summary>
        static AudioClip MakeBelt()
        {
            const int rate = 22050; int n = rate * 2;
            var d = new float[n];
            var rng = new System.Random(5);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.08f;
                float tick = Mathf.Exp(-((t * 6f) % 1f) * 40f) * 0.6f;               // a roller every 1/6 s
                d[i] = lp * 0.5f + 0.25f * Mathf.Sin(2f * Mathf.PI * 48f * t) + tick * noise;
            }
            var clip = AudioClip.Create("HubBelt", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        /// <summary>Brings player number i out of their chute onto their cot.</summary>
        public void Begin(int i)
        {
            _player = FindAnyObjectByType<CamcorderPlayerController>();
            if (!_player) return;
            _body = _player.GetComponent<FirstPersonBody>();
            _hand = _player.GetComponent<PropGrabber>();
            _cot = Mathf.Clamp(i, 0, 3);
            _phase = Phase.Dark; _t = 0f;
            _open[_cot] = 1f;
            _player.Teleport(Head(0f), 0f, -78f);                // lying, looking up at the chute's roof
            _player.SetSeated(true);
            _player.seatYawRange = 360f;
            Hold(0f);
        }

        /// <summary>Where the head is, s metres along the way from deep in the chute to the pillow.</summary>
        Vector3 Head(float s) => Mouth(_cot) + new Vector3(0f, Lie, -StartBack + s);

        void Hold(float s)
        {
            var head = Head(s);
            _player.seatFeet = new Vector3(head.x, head.y, head.z);
            _player.seatEye = Eye;
            if (_body)
            {
                // Half reclining: the hips a little way down the body, the legs out along the belt or the mattress.
                var hip = head + new Vector3(0f, -0.05f, 0.55f);
                _body.SetSeat(true, hip, hip + new Vector3(-0.1f, 0f, 0.85f), hip + new Vector3(0.1f, 0f, 0.85f), Vector3.forward);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            // The shutters: yours up while you are brought out, all down otherwise (solid then).
            for (int i = 0; i < 4; i++)
            {
                if (!_shutters[i]) continue;
                float want = i == _cot && (_phase == Phase.Dark || _phase == Phase.Belt) ? 1f : 0f;
                float was = _open[i];
                _open[i] = Mathf.MoveTowards(_open[i], want, dt / (want > 0f ? 0.4f : 0.7f));
                if (was > 0f && _open[i] <= 0f && _clunk) { _clunk.transform.position = Mouth(i); _clunk.volume = 1f; _clunk.PlayOneShot(ElevatorSounds.DoorShut, 0.5f); }
                var lp = _shutters[i].localPosition;
                _shutters[i].localPosition = new Vector3(lp.x, Sill - 0.01f + _open[i] * (Top - Sill + 0.06f), lp.z);
                _shutterBlocks[i].enabled = _open[i] < 0.5f;
            }
            if (_phase == Phase.Done || !_player) return;
            _t += dt;
            float total = (StartBack + EndOut) / Speed;
            switch (_phase)
            {
                case Phase.Dark:
                    if (_t >= DarkTime) { _phase = Phase.Belt; _t = 0f; Clunk(); }
                    break;
                case Phase.Belt:
                {
                    float s = Mathf.Min(StartBack + EndOut, _t * Speed);
                    Hold(s);
                    // Over each roller a small jolt.
                    float bump = Mathf.Repeat(_t * 2.2f, 1f) < 0.08f ? 0.006f : 0f;
                    _player.seatEye = Eye + bump;
                    if (_belt) { _belt.transform.position = Head(s); if (!_belt.isPlaying) _belt.Play(); _belt.volume = Mathf.MoveTowards(_belt.volume, 0.35f, dt); }
                    if (_t >= total) { _phase = Phase.OnCot; _t = 0f; Clunk(); }
                    break;
                }
                case Phase.OnCot:
                {
                    if (_belt) { _belt.volume = Mathf.MoveTowards(_belt.volume, 0f, dt * 0.8f); if (_belt.volume <= 0f) _belt.Stop(); }
                    if (_t > 0.8f && _t < 4f && _hand) _hand.Say("SPACE - GET UP");
                    var kb = UnityEngine.InputSystem.Keyboard.current;
                    bool up = kb != null && _t > 0.6f && (kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame
                                                          || kb.sKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame);
                    if (up) GetUp();
                    break;
                }
            }
        }

        void Clunk()
        {
            if (!_clunk || !_player) return;
            _clunk.transform.position = _player.transform.position;
            _clunk.volume = 1f;
            _clunk.PlayOneShot(ElevatorSounds.DoorShut, 0.35f);
        }

        /// <summary>Up off the cot, standing at its foot (the eye eases up from lying: the controller's own easing).</summary>
        public void GetUp()
        {
            if (_phase == Phase.Done || !_player) return;
            _phase = Phase.Done;
            if (_body) _body.SetSeat(false, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.forward);
            _player.SetSeated(false);
            HubPlan.WakePose(_cot, out var foot, out float yaw);
            _player.Teleport(foot, yaw, 10f);
        }

        /// <summary>For tests: skips to lying on the cot.</summary>
        public void SkipToCot() { if (_phase != Phase.Done) { _phase = Phase.OnCot; _t = 1f; Hold(StartBack + EndOut); } }
    }
}
