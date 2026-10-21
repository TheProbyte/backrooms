using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// The operator's own sounds: carpet footsteps from actual distance walked, and the handling signal
    /// (movement rumble + a thump per step) that the camcorder chain turns into handling noise.
    /// </summary>
    [RequireComponent(typeof(CamcorderPlayerController))]
    public class PlayerAudio : MonoBehaviour
    {
        public float walkStride = 0.72f;
        public float runStride = 1.05f;

        CamcorderPlayerController _player;
        CamcorderAudioChain _chain;
        AudioSource _feet;
        float _travel;
        int _last = -1, _version = -1;
        Vector3 _lastPos;

        void Start()
        {
            _player = GetComponent<CamcorderPlayerController>();
            var go = new GameObject("Footsteps");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, 0.1f);
            _feet = go.AddComponent<AudioSource>();
            _feet.playOnAwake = false; _feet.loop = false;
            _lastPos = transform.position;
        }

        void Update()
        {
            if (!_chain) { var l = FindAnyObjectByType<AudioListener>(); if (l) _chain = l.GetComponent<CamcorderAudioChain>(); }
            const string id = SoundIds.Footsteps;
            if (SoundCatalog.Version != _version) { SoundBank.ApplySpatial(_feet, id); _version = SoundCatalog.Version; }

            Vector3 pos = transform.position;
            Vector3 d = pos - _lastPos; d.y = 0f;
            _lastPos = pos;
            bool walking = !_player.noclip && _player.IsGrounded;
            float speed = _player.PlanarVelocity.magnitude;
            if (_chain) _chain.handlingMotion = walking ? Mathf.Clamp01(speed / Mathf.Max(0.1f, _player.runSpeed)) : 0f;
            if (!walking || d.magnitude > 2f) { _travel = 0f; return; }       // teleports don't step

            _travel += d.magnitude;
            float stride = _player.IsRunning ? runStride : walkStride;
            if (_travel < stride) return;
            _travel -= stride;
            if (speed < 0.25f) return;

            float varAmt = SoundCatalog.ParamA(id);
            int pick;
            int count = SoundBank.StepCount;
            do pick = Random.Range(0, count); while (pick == _last && varAmt > 0.01f && count > 1);
            _last = pick;
            var clip = SoundBank.Step(pick);
            float heavy = _player.IsRunning ? 1.5f : Mathf.Lerp(0.7f, 1f, speed / Mathf.Max(0.1f, _player.walkSpeed));
            _feet.pitch = SoundCatalog.Pitch(id) * (1f + (Random.value - 0.5f) * 0.05f * varAmt);
            _feet.volume = 1f;
            // PlayOneShot lets the previous step's tail finish instead of cutting it off (that cut was the pop).
            _feet.PlayOneShot(clip, SoundBank.BaseLevel(id) * SoundCatalog.Volume(id) * heavy * (1f + (Random.value - 0.5f) * 0.3f * varAmt));
            if (_chain) _chain.HandlingImpulse();
        }
    }
}
