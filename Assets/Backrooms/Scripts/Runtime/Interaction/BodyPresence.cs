using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The operator's body as the camera and the microphone feel it (presentation only: nothing here limits what you can
    /// do):
    ///  - Exertion: running, jumps and heavy dragging raise it, rest lowers it. Breathing follows it: faster and deeper
    ///    after a run (the camera rises and falls with it, and the breaths are heard close to the mic), slow and shallow
    ///    when crouched still.
    ///  - Clothes rustle as you turn, crouch, get up and walk.
    ///  - A shoulder brushing along a wall: a rub of the sleeve, and a small knock to the camera when it first touches.
    /// </summary>
    [RequireComponent(typeof(CamcorderPlayerController))]
    public class BodyPresence : MonoBehaviour
    {
        [Range(0f, 1f)] public float Exertion;

        CamcorderPlayerController _player;
        PropGrabber _grab;
        HandheldCameraShake _shake;
        CamcorderAudioChain _chain;
        CharacterController _cc;
        AudioSource _breath, _rustle, _brush;
        float _lastPhase, _baseRate = -1f, _baseDeg, _baseHeight;
        float _rustleLevel, _brushLevel, _lastEye;
        bool _brushing;
        int _breathPick;

        void Start()
        {
            _player = GetComponent<CamcorderPlayerController>();
            _grab = GetComponent<PropGrabber>();
            _cc = GetComponent<CharacterController>();
            var cam = Camera.main ? Camera.main.transform : transform;
            _breath = Source("Breath", cam, new Vector3(0f, -0.06f, 0.02f), false);
            _rustle = Source("Rustle", transform, new Vector3(0f, 1.1f, 0f), true);
            _rustle.clip = SoundBank.Rustle; _rustle.volume = 0f; _rustle.Play();
            _brush = Source("Brush", transform, new Vector3(0f, 1.3f, 0f), true);
            _brush.clip = SoundBank.Brush; _brush.volume = 0f;
            _lastEye = _player.Eye;
        }

        AudioSource Source(string name, Transform parent, Vector3 at, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false; a.loop = loop; a.spatialBlend = 0.6f; a.minDistance = 0.3f; a.maxDistance = 6f;
            a.dopplerLevel = 0f; a.rolloffMode = AudioRolloffMode.Linear;
            return a;
        }

        void Update()
        {
            if (!_player) return;
            float dt = Time.deltaTime;
            if (!_shake) _shake = FindAnyObjectByType<HandheldCameraShake>();
            if (!_chain) { var l = FindAnyObjectByType<AudioListener>(); if (l) _chain = l.GetComponent<CamcorderAudioChain>(); }
            float speed = _player.PlanarVelocity.magnitude;
            bool moving = speed > 0.3f && _player.IsGrounded && !_player.noclip;

            // ---- Exertion and breathing ----
            float gain = 0f;
            if (_player.IsRunning && speed > 2f) gain += 0.07f;
            if (_player.LandedThisFrame > 0f) Exertion = Mathf.Min(1f, Exertion + 0.06f * _player.LandedThisFrame);
            if (_grab && _grab.Current == PropGrabber.State.Holding) gain += 0.04f * _grab.Strain;
            float rest = moving ? 0.012f : _player.CurrentStance == CamcorderPlayerController.Stance.Stand ? 0.03f : 0.045f;
            Exertion = Mathf.Clamp01(Exertion + (gain - rest) * dt);
            if (_shake)
            {
                if (_baseRate < 0f) { _baseRate = _shake.breathingRate; _baseDeg = _shake.breathingDegrees; _baseHeight = _shake.breathingHeight; }
                bool stillLow = !moving && _player.CurrentStance != CamcorderPlayerController.Stance.Stand;
                float calm = stillLow ? 0.6f : 1f;
                _shake.breathingRate = _baseRate * (1f + 1.7f * Exertion) * (stillLow ? 0.85f : 1f);
                // CamcorderRig scales degrees and height by the preset each frame from its own base: work on top of it.
                _shake.exertion = Exertion * calm + (calm - 1f);
                // Breaths heard with the camera's rise and fall: in as it rises, out as it falls.
                float ph = Mathf.Repeat(_shake.BreathPhase, Mathf.PI * 2f);
                // Heard only when you are out of breath, and quietly (it was up to 0.5, close to the mic, and the camcorder's
                // automatic gain lifts it further in a quiet room).
                float vol = Mathf.Clamp01((Exertion - 0.3f) / 0.7f) * 0.16f;
                if (vol > 0.01f)
                {
                    if (_lastPhase > ph) _breath.PlayOneShot(SoundBank.BreathIn(_breathPick++), vol);
                    else if (_lastPhase < Mathf.PI && ph >= Mathf.PI) _breath.PlayOneShot(SoundBank.BreathOut(_breathPick++), vol * 1.1f);
                }
                _lastPhase = ph;
            }

            // ---- Clothes ----
            float look = Mathf.Abs(_player.LookDelta.x) + Mathf.Abs(_player.LookDelta.y);
            float lookRate = look / Mathf.Max(dt, 1e-4f);
            float eyeRate = Mathf.Abs(_player.Eye - _lastEye) / Mathf.Max(dt, 1e-4f);
            _lastEye = _player.Eye;
            float want = Mathf.Clamp01(lookRate / 220f) * 0.55f + Mathf.Clamp01(eyeRate / 1.2f) * 0.8f + Mathf.Clamp01(speed / 3f) * 0.2f;
            _rustleLevel = Mathf.MoveTowards(_rustleLevel, Mathf.Clamp01(want), dt * (want > _rustleLevel ? 6f : 2f));
            _rustle.volume = _rustleLevel * 0.09f;

            // ---- Wind on the microphone: running, or whipping the camera round ----
            if (_chain) _chain.wind = Mathf.Clamp01((speed - 2f) / 1.8f) * 0.8f + Mathf.Clamp01((lookRate - 250f) / 500f) * 0.5f;

            // ---- A shoulder along a wall ----
            UpdateBrush(dt, speed, moving);
        }

        void UpdateBrush(float dt, float speed, bool moving)
        {
            // Only when the shoulder is really on the wall (it was within 6 cm of it, so walking near one rubbed).
            float reach = (_cc ? _cc.radius : 0.3f) + 0.025f;
            Vector3 origin = transform.position + Vector3.up * 1.3f;
            Vector3 vel = _player.PlanarVelocity;
            int sideHit = 0;
            if (moving && !_player.IsCrawling)
            {
                foreach (int s in new[] { 1, -1 })
                {
                    if (!Physics.Raycast(origin, transform.right * s, out var h, reach, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (h.collider.transform.IsChildOf(transform) || Mathf.Abs(h.normal.y) > 0.3f) continue;
                    if (Mathf.Abs(Vector3.Dot(h.normal, vel.normalized)) > 0.6f) continue;     // walking into it, not along it
                    sideHit = s;
                    break;
                }
            }
            bool brushing = sideHit != 0;
            if (brushing && !_brushing)
            {
                // First touch: the camera is knocked a little away from the wall, the mic hears the body.
                if (_shake) _shake.Jolt(new Vector3(1f, -5f * sideHit, 6f * sideHit) * Mathf.Clamp01(speed / 1.5f));
                if (_chain) _chain.HandlingImpulse();
            }
            _brushing = brushing;
            _brushLevel = Mathf.MoveTowards(_brushLevel, brushing ? Mathf.Clamp01(speed / 2.5f) : 0f, dt * (brushing ? 2.5f : 5f));
            if (_brushLevel > 0.001f && !_brush.isPlaying) { _brush.time = Random.Range(0f, _brush.clip.length); _brush.Play(); }
            _brush.volume = _brushLevel * 0.12f;
            if (_brushLevel <= 0.001f && _brush.isPlaying) _brush.Stop();
            if (brushing) _brush.transform.localPosition = new Vector3(0.25f * sideHit, 1.3f, 0f);
        }
    }
}
