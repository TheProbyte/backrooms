using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// The operator's own sounds: footsteps, and the handling signal (movement rumble + a thump per step) that the
    /// camcorder chain turns into handling noise. Walking with the legs shown, each step is heard when and where a foot
    /// actually comes down (FirstPersonBody.FootLanded), on what the floor is there (carpet, wood, tile, concrete, water;
    /// <see cref="Surfaces"/>), with a scuff of the shoe as you stop. Crawling (no legs shown) steps by distance, on
    /// sheet metal in the ducts.
    /// </summary>
    [RequireComponent(typeof(CamcorderPlayerController))]
    public class PlayerAudio : MonoBehaviour
    {
        public float walkStride = 0.72f;
        public float runStride = 1.05f;

        CamcorderPlayerController _player;
        CamcorderAudioChain _chain;
        AudioSource _feet, _zoom;
        float _zoomLevel;
        int _zoomVersion = -1;
        float _travel;
        int _last = -1, _version = -1;
        Vector3 _lastPos;
        Interaction.FirstPersonBody _body;
        float _recentSpeed;
        int _scuffPick;

        void Start()
        {
            _player = GetComponent<CamcorderPlayerController>();
            var go = new GameObject("Footsteps");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, 0.1f);
            _feet = go.AddComponent<AudioSource>();
            _feet.playOnAwake = false; _feet.loop = false;
            // Zoom motor: in the camera body, just behind the lens and microphone.
            var cam = Camera.main ? Camera.main.transform : transform;
            var zgo = new GameObject("ZoomMotor");
            zgo.transform.SetParent(cam, false);
            zgo.transform.localPosition = new Vector3(0.03f, -0.02f, -0.12f);
            _zoom = zgo.AddComponent<AudioSource>();
            _zoom.clip = SoundBank.ZoomMotor; _zoom.loop = true; _zoom.playOnAwake = false; _zoom.volume = 0f;
            _zoom.Play();
            _lastPos = transform.position;
            _body = GetComponent<Interaction.FirstPersonBody>();
            if (_body) _body.FootLanded += OnFoot;
        }

        void OnDestroy() { if (_body) _body.FootLanded -= OnFoot; }

        /// <summary>A foot came down: its step, from where it is, on what is under it.</summary>
        void OnFoot(Vector3 at, float speed)
        {
            if (!_feet || !_player || !_player.IsGrounded || _player.noclip) return;
            const string id = SoundIds.Footsteps;
            var floor = Surfaces.At(at, transform);
            bool stopping = speed < 0.35f && _recentSpeed > 0.8f;
            float heavy = _player.IsRunning ? 1.5f : _player.CurrentStance == CamcorderPlayerController.Stance.Crouch ? 0.6f
                        : Mathf.Lerp(0.55f, 1f, speed / Mathf.Max(0.1f, _player.walkSpeed));
            if (speed < 0.25f && !stopping) heavy *= 0.5f;                                   // shuffling on the spot
            Play(floor, at, heavy);
            if (stopping)
            {
                _feet.PlayOneShot(SoundBank.Scuff(_scuffPick++), SoundBank.BaseLevel(id) * SoundCatalog.Volume(id) * (floor == ProceduralSounds.Floor.Carpet ? 0.5f : 0.8f));
                _recentSpeed = 0f;
            }
            if (_chain) _chain.HandlingImpulse();
        }

        void Play(ProceduralSounds.Floor floor, Vector3 at, float heavy)
        {
            const string id = SoundIds.Footsteps;
            float varAmt = SoundCatalog.ParamA(id);
            int pick;
            int count = floor == ProceduralSounds.Floor.Carpet ? SoundBank.StepCount : 6;
            do pick = Random.Range(0, count); while (pick == _last && varAmt > 0.01f && count > 1);
            _last = pick;
            AudioClip clip; float k = 1f;
            switch (floor)
            {
                case ProceduralSounds.Floor.Water: clip = SoundBank.WaterStep(pick); k = 1.6f; break;
                case ProceduralSounds.Floor.Carpet: clip = SoundBank.Step(pick); break;
                case ProceduralSounds.Floor.Tile: clip = SoundBank.HardStep(floor, pick); k = 1.25f; break;
                case ProceduralSounds.Floor.Metal: clip = SoundBank.HardStep(floor, pick); k = 1.3f; break;
                default: clip = SoundBank.HardStep(floor, pick); k = 1.1f; break;
            }
            _feet.transform.position = at + Vector3.up * 0.03f;
            _feet.pitch = SoundCatalog.Pitch(id) * (1f + (Random.value - 0.5f) * 0.05f * varAmt);
            _feet.volume = 1f;
            // PlayOneShot lets the previous step's tail finish instead of cutting it off (that cut was the pop).
            _feet.PlayOneShot(clip, SoundBank.BaseLevel(id) * SoundCatalog.Volume(id) * heavy * k * (1f + (Random.value - 0.5f) * 0.3f * varAmt));
        }

        void Update()
        {
            if (!_chain) { var l = FindAnyObjectByType<AudioListener>(); if (l) _chain = l.GetComponent<CamcorderAudioChain>(); }
            const string id = SoundIds.Footsteps;
            if (SoundCatalog.Version != _version) { SoundBank.ApplySpatial(_feet, id); _version = SoundCatalog.Version; }

            // Zoom motor follows the motor speed (no sound when the lens is still).
            if (SoundCatalog.Version != _zoomVersion) { SoundBank.ApplySpatial(_zoom, SoundIds.ZoomMotor); _zoomVersion = SoundCatalog.Version; }
            float zm = Mathf.Abs(_player.ZoomMotor);
            _zoomLevel = Mathf.MoveTowards(_zoomLevel, zm, Time.deltaTime * 6f);
            _zoom.volume = SoundBank.BaseLevel(SoundIds.ZoomMotor) * SoundCatalog.Volume(SoundIds.ZoomMotor) * _zoomLevel;
            _zoom.pitch = SoundCatalog.Pitch(SoundIds.ZoomMotor) * (0.85f + 0.15f * _zoomLevel);

            // Landing from a jump: a heavier step and a thump on the mic.
            if (_player.LandedThisFrame > 0f)
            {
                _feet.PlayOneShot(SoundBank.Step(Random.Range(0, SoundBank.StepCount)),
                    SoundBank.BaseLevel(SoundIds.Footsteps) * SoundCatalog.Volume(SoundIds.Footsteps) * Mathf.Clamp(_player.LandedThisFrame / 2.5f, 0.8f, 2f));
                if (_chain) _chain.HandlingImpulse();
            }

            Vector3 pos = transform.position;
            Vector3 d = pos - _lastPos; d.y = 0f;
            _lastPos = pos;
            bool walking = !_player.noclip && _player.IsGrounded;
            float speed = _player.PlanarVelocity.magnitude;
            _recentSpeed = Mathf.Max(speed, _recentSpeed - Time.deltaTime * 1.5f);
            if (_chain) _chain.handlingMotion = walking ? Mathf.Clamp01(speed / Mathf.Max(0.1f, _player.runSpeed)) : 0f;
            if (!walking || d.magnitude > 2f) { _travel = 0f; return; }       // teleports don't step
            if (_body && _body.FeetActive) { _travel = 0f; return; }           // the feet play their own steps

            _travel += d.magnitude;
            float stride = _player.IsCrawling ? 0.42f : _player.IsRunning ? runStride : walkStride;
            if (_travel < stride) return;
            _travel -= stride;
            if (speed < 0.15f) return;

            // Crawling (or no legs to step with): by distance, hands and knees on what is under you.
            var under = Surfaces.At(transform.position, transform);
            float heavy = _player.IsCrawling ? (under == ProceduralSounds.Floor.Metal ? 0.6f : 0.45f) : _player.IsRunning ? 1.5f : Mathf.Lerp(0.7f, 1f, speed / Mathf.Max(0.1f, _player.walkSpeed));
            Play(under, transform.position, heavy);
            if (_chain) _chain.HandlingImpulse();
        }
    }
}
