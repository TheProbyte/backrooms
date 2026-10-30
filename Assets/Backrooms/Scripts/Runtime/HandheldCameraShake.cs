using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// Handheld camcorder motion, applied as a local offset on the camera transform.
    /// Three layers: slow irregular "breathing" sway at rest, a gentle footstep-driven
    /// bob while walking, and heavier, rougher shake while running. Mouse look lives on
    /// the parent pivot, so shake never fights or delays aim. Walking plays a real recording of a
    /// camera carried in the hand when one is loaded (CameraMotionClip, recordedWalk), in step with the feet.
    /// </summary>
    public class HandheldCameraShake : MonoBehaviour
    {
        [Tooltip("Player controller that reports speed and run state.")]
        public CamcorderPlayerController player;

        [Header("Overall")]
        [Tooltip("Master multiplier for all handheld motion. 0 disables shake entirely.")]
        [Range(0f, 2f)] public float shakeIntensity = 1f;

        [Header("Idle sway")]
        [Tooltip("Rotational sway at rest, in degrees.")]
        [Range(0f, 1.5f)] public float idleSwayDegrees = 0.35f;
        [Range(0.05f, 1f)] public float idleSwayFrequency = 0.22f;
        [Tooltip("Breathing: slow rise and fall of the hands holding the camera, in degrees of tilt.")]
        [Range(0f, 1f)] public float breathingDegrees = 0.2f;
        [Tooltip("Breathing height in metres.")]
        [Range(0f, 0.01f)] public float breathingHeight = 0.0022f;
        [Tooltip("Breaths per second (resting adult ~0.2-0.3).")]
        [Range(0.1f, 0.5f)] public float breathingRate = 0.23f;

        [Header("Stabilisation")]
        [Tooltip("Optical/electronic image stabilisation: 0 = none, 1 = removes most high-frequency shake.")]
        [Range(0f, 1f)] public float stabilization = 0f;

        [Header("Walking")]
        [Tooltip("Vertical bob per step while walking, in metres.")]
        [Range(0f, 0.05f)] public float walkBobHeight = 0.012f;
        [Tooltip("Rotational wobble added while walking, in degrees.")]
        [Range(0f, 2f)] public float walkSwayDegrees = 0.45f;
        [Tooltip("Steps per second at walking speed.")]
        [Range(0.5f, 3f)] public float walkStepRate = 1.7f;

        [Header("Running")]
        [Range(0f, 0.1f)] public float runBobHeight = 0.032f;
        [Range(0f, 4f)] public float runShakeDegrees = 1.3f;
        [Range(1f, 4f)] public float runStepRate = 2.6f;

        [Tooltip("How quickly shake blends between rest, walk and run.")]
        [Range(0.5f, 10f)] public float blendSpeed = 3f;

        [Header("Look motion (operator's hands)")]
        [Tooltip("When you stop turning, the camera carries on a touch further and settles back. 0 = off.")]
        [Range(0f, 2f)] public float lookOvershoot = 0.5f;
        [Tooltip("Small irregular wobble while turning. 0 = off.")]
        [Range(0f, 2f)] public float lookJitter = 0.5f;

        [Header("Roughness")]
        [Tooltip("Fast, irregular hand shake (about 2 to 12 Hz) on top of the smooth bob, strongest while walking: an operator " +
                 "walking with the camera in one hand rather than a steady glide. 0 = off. In degrees at 1.")]
        [Range(0f, 3f)] public float roughness = 0f;

        [Header("Recorded walk")]
        [Tooltip("While walking, the camera moves the way a phone held like the camcorder did (Resources/CameraMotion/Walk, " +
                 "built by Tools/CameraMotion), in step with the feet, instead of the generated bob and sway.")]
        public bool recordedWalk = true;

        /// <summary>Physical effort from moving something (0..1, set each frame by PropGrabber): adds a strained tremble.</summary>
        [System.NonSerialized] public float effort;
        /// <summary>How far the camcorder hand dips while the other hand works (0..1, set by PropGrabber).</summary>
        [System.NonSerialized] public float dip;
        /// <summary>1: the camcorder hangs on its strap at the chest (both hands free: PropGrabber H), still recording. It
        /// looks ahead and down from there, swings like a pendulum when you start, stop and turn, and bobs with the steps.</summary>
        [System.NonSerialized] public float strap;
        /// <summary>What the hands hold while it hangs: the camera keeps out of it (pulled up toward the eye, over it).</summary>
        [System.NonSerialized] public Collider[] avoid;
        /// <summary>While it hangs: a point it tips up or down to keep in the picture (a box lifted to stack it), or null.</summary>
        [System.NonSerialized] public Vector3? strapLook;
        float _strapLookW, _strapLookTip;
        Vector3 _clear;
        float _strapT;
        Vector3 _swing, _swingVel, _lastVel, _velF;
        float _lastYaw;
        /// <summary>How hard the operator is breathing (set by Interaction.BodyPresence): above 0 deeper, below 0 shallower.</summary>
        [System.NonSerialized] public float exertion;
        /// <summary>Where the breath is (radians; in from 0, out from pi).</summary>
        public float BreathPhase => _breathPhase;
        float _effort, _dip;

        float _walkBlend, _runBlend, _stepPhase, _breathPhase;
        // The recorded walk: where along it (strides), its pace (strides a second, from the feet), a correction still to
        // ease in, and when a foot last landed.
        static CameraMotionClip s_walk;
        static bool s_walkTried;
        Interaction.FirstPersonBody _body;
        float _bodyLook = -10f, _strideT, _strideRate, _strideFix, _lastFoot = -10f;
        /// <summary>Where along the recorded walk the camera is (strides; the left foot lands at whole numbers).</summary>
        public float WalkStride => _strideT;
        /// <summary>Whether walking plays the recording (it is on and loaded).</summary>
        public bool PlaysRecordedWalk => recordedWalk && Walk != null;
        Vector2 _lookVel, _lookOff, _lookOffVel;
        float _lookRoll;
        Vector3 _seed;

        void Awake()
        {
            _seed = new Vector3(Random.value * 100f, Random.value * 100f, Random.value * 100f);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float t = Time.time;

            float speed = player ? player.NormalizedSpeed : 0f;
            bool running = player && player.IsRunning && speed > 1.2f;
            bool grounded = !player || player.IsGrounded;

            float walkTarget = grounded ? Mathf.Clamp01(speed) : 0f;
            float runTarget = grounded && running ? 1f : 0f;
            _walkBlend = Mathf.MoveTowards(_walkBlend, walkTarget, blendSpeed * dt);
            _runBlend = Mathf.MoveTowards(_runBlend, runTarget, blendSpeed * dt);

            float stepRate = Mathf.Lerp(walkStepRate, runStepRate, _runBlend);
            _stepPhase += stepRate * dt * Mathf.Max(_walkBlend, 0.0001f) * Mathf.PI;
            float step = _stepPhase;

            // Idle: two octaves of slow noise reads like breathing hands.
            Vector3 idleRot = Noise3(t * idleSwayFrequency) * idleSwayDegrees
                            + Noise3(t * idleSwayFrequency * 2.7f + 31f) * idleSwayDegrees * 0.35f;

            // Walk: bob tied to footsteps (|sin| gives a heel-strike rhythm), soft noise on top.
            float bob = (Mathf.Abs(Mathf.Sin(step)) - 0.5f);
            Vector3 walkPos = new Vector3(Mathf.Sin(step * 0.5f) * 0.4f, bob, 0f) * walkBobHeight;
            Vector3 walkRot = new Vector3(bob * 0.6f, Mathf.Sin(step * 0.5f) * 0.3f, Mathf.Sin(step * 0.5f) * 0.5f) * walkSwayDegrees
                            + Noise3(t * 0.9f + 57f) * walkSwayDegrees * 0.4f;
            // Recorded: the phone recording's sway, step bob and tremor, at the feet's own pace. It already has the hand's
            // own shake in it, so the generated idle wander and the roughness's walking part give way to it.
            bool rec = PlaysRecordedWalk;
            if (rec)
            {
                UpdateStride(dt, speed);
                Walk.Sample(_strideT, out walkRot, out walkPos);
            }
            float gen = rec ? 0f : 1f;

            // Run: heavier bob plus higher-frequency rough shake.
            Vector3 runPos = new Vector3(Mathf.Sin(step * 0.5f) * 0.5f, bob, 0f) * runBobHeight;
            Vector3 runRot = new Vector3(bob * 0.8f, Mathf.Sin(step * 0.5f) * 0.4f, Mathf.Sin(step * 0.5f) * 0.6f) * runShakeDegrees
                           + Noise3(t * 4.5f + 113f) * runShakeDegrees * 0.45f;

            float walkOnly = _walkBlend * (1f - _runBlend);
            // Breathing: irregular period (rate wanders a little) so it never reads as a metronome.
            _breathPhase += dt * breathingRate * (0.85f + 0.3f * Mathf.PerlinNoise(t * 0.05f, _seed.x)) * Mathf.PI * 2f;
            float breath = Mathf.Sin(_breathPhase) + 0.25f * Mathf.Sin(_breathPhase * 2f + 0.8f);
            // Out of breath, it shows even while walking.
            float ex = Mathf.Clamp(exertion, -0.6f, 1f);
            float breathWeight = (1f - _walkBlend * 0.7f * (1f - Mathf.Max(0f, ex))) * (1f + (ex >= 0f ? 2.2f * ex : ex));
            Vector3 breathRot = new Vector3(breath * breathingDegrees, 0f, breath * breathingDegrees * 0.25f) * breathWeight;
            Vector3 breathPos = new Vector3(0f, breath * breathingHeight, 0f) * breathWeight;

            // Stabilisation removes the fast components first (run shake), then some of the slow ones.
            float stab = 1f - stabilization;
            Vector3 pos = breathPos * (1f - stabilization * 0.5f) + (walkPos * walkOnly + runPos * _runBlend) * Mathf.Lerp(1f, 0.45f, stabilization);
            Vector3 rot = breathRot * (1f - stabilization * 0.5f)
                        + idleRot * Mathf.Lerp(1f, rec ? 0.25f : 0.6f, _walkBlend) * Mathf.Lerp(1f, 0.4f, stabilization)
                        + walkRot * walkOnly * Mathf.Lerp(1f, 0.35f, stabilization)
                        + runRot * _runBlend * Mathf.Lerp(1f, 0.25f, stabilization) * (0.5f + 0.5f * stab);

            rot += LookMotion(dt, t) * (1f - stabilization * 0.5f);

            // Rough hand shake: three octaves of noise (the fastest is what reads as jerky), more while walking,
            // and each footstep lands with a small extra jolt.
            if (roughness > 0f)
            {
                float active = 0.35f + 0.65f * _walkBlend * gen;
                Vector3 r = Noise3(t * 2.3f + 401f) * 0.6f + Noise3(t * 5.9f + 433f) * 0.3f + Noise3(t * 12.7f + 467f) * 0.05f;
                r.y += (Mathf.PerlinNoise(t * 6.7f, _seed.y + 17f) * 2f - 1f) * 0.25f;          // a little extra fast yaw: the wrist
                float jolt = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin(step)), 3f) * _walkBlend;    // the heel strike
                r += new Vector3(bob * 0.9f * _walkBlend + jolt * 0.25f, 0f, jolt * 0.1f * Mathf.Sign(Mathf.Sin(step * 0.5f))) * gen;
                rot += r * roughness * active * Mathf.Lerp(1f, 0.3f, stabilization);
            }

            // Working with the other hand: the camcorder sinks and tilts a little, and trembles under effort.
            _effort = Mathf.MoveTowards(_effort, Mathf.Clamp01(effort), dt * 2.5f);
            _dip = Mathf.Lerp(_dip, Mathf.Clamp01(dip), 1f - Mathf.Exp(-dt / 0.35f));
            // The camcorder is in the left hand: it sags and turns toward the right, where the other hand is busy.
            pos += new Vector3(0.012f, -0.035f, 0f) * _dip;
            rot += new Vector3(3.2f, 1.2f, -2.0f) * _dip;
            rot += (Noise3(t * 9f + 333f) * 0.35f + Noise3(t * 3.1f + 71f) * 0.5f) * _effort;

            // Jolts (a cord yanked out of the hand): a damped spring, felt even with the shake turned off. Stepped at
            // 120 Hz at most: in one long frame (a hitch while far chunks load after a teleport) a single step blew the
            // spring up and left the camera on its side.
            for (float rem = Mathf.Min(dt, 0.25f); rem > 1e-5f; rem -= 1f / 120f)
            {
                float h = Mathf.Min(rem, 1f / 120f);
                _joltVel += (-_jolt * 260f - _joltVel * 22f) * h;
                _jolt += _joltVel * h;
            }
            if (float.IsNaN(_jolt.x + _jolt.y + _jolt.z) || float.IsNaN(_joltVel.x + _joltVel.y + _joltVel.z)) _jolt = _joltVel = Vector3.zero;
            _jolt = Vector3.ClampMagnitude(_jolt, 20f);
            // Held a little out in front of the eye (CamcorderPlayerController.CamcorderHold), the shake on top.
            Vector3 lp = (player ? player.CamcorderHold : Vector3.zero) + pos * shakeIntensity;
            Quaternion lr = Quaternion.Euler(rot * shakeIntensity + _jolt);
            _strapT = Mathf.MoveTowards(_strapT, Mathf.Clamp01(strap), dt / 0.7f);
            var pivot = transform.parent;
            if (_strapT > 0f && player && pivot)
            {
                // On the strap: at the chest, facing the way the body does, tipped down; the look moves it only as far as
                // the chest leans. A pendulum: it swings back as you set off, forward as you stop, out as you turn.
                var body = player.transform;
                // (From the body's smoothed velocity: frame-to-frame acceleration is noise, and shook it.)
                Vector3 v = Quaternion.Inverse(body.rotation) * player.PlanarVelocity;
                _velF = Vector3.Lerp(_velF, v, 1f - Mathf.Exp(-dt / 0.12f));
                Vector3 acc = dt > 1e-4f ? (_velF - _lastVel) / dt : Vector3.zero;
                float yawRate = dt > 1e-4f ? Mathf.DeltaAngle(_lastYaw, body.eulerAngles.y) / dt : 0f;
                if (Mathf.Abs(yawRate) > 400f) yawRate = 0f;                                   // a teleport, not a turn
                _lastVel = _velF; _lastYaw = body.eulerAngles.y;
                Vector3 drive = new Vector3(-acc.z * 1.6f, -yawRate * 0.03f, acc.x * 2f);
                const float k = 75f, c = 2.8f;
                for (float rem = Mathf.Min(dt, 0.1f); rem > 1e-5f; rem -= 1f / 120f)
                {
                    float h = Mathf.Min(rem, 1f / 120f);
                    _swingVel += (k * (Vector3.ClampMagnitude(drive, 25f) - _swing) - c * _swingVel) * h;
                    _swing += _swingVel * h;
                }
                _swing = Vector3.ClampMagnitude(_swing, 30f);
                float tip = 10f + 0.25f * player.Pitch;
                Vector3 wp = pivot.position + body.rotation * (new Vector3(0f, -0.34f, 0.1f) + pos * shakeIntensity * 1.3f);
                // Kept on what the hands lift (round 14: a box raised to stack it went out of the top of the picture).
                if (strapLook.HasValue)
                {
                    Vector3 d = strapLook.Value - wp;
                    _strapLookTip = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg + 8f, -50f, 60f);
                }
                _strapLookW = Mathf.MoveTowards(_strapLookW, strapLook.HasValue ? 1f : 0f, dt / 0.4f);
                tip = Mathf.Lerp(tip, _strapLookTip, Mathf.SmoothStep(0f, 1f, _strapLookW));
                // Not inside what you hold: up toward the eye until clear of it (eased, so it does not jump).
                Vector3 clearTo = KeepClear(pivot.position, wp) - wp;
                _clear = Vector3.Lerp(_clear, clearTo, 1f - Mathf.Exp(-dt / (clearTo.sqrMagnitude > _clear.sqrMagnitude ? 0.12f : 0.4f)));
                wp += _clear;
                Quaternion wr = body.rotation * Quaternion.Euler(tip + _swing.x, _swing.y, _swing.z) * Quaternion.Euler(rot * shakeIntensity * 0.7f + _jolt);
                Vector3 sp = Quaternion.Inverse(pivot.rotation) * (wp - pivot.position);
                Quaternion sr = Quaternion.Inverse(pivot.rotation) * wr;
                float s = Mathf.SmoothStep(0f, 1f, _strapT);
                lp = Vector3.Lerp(lp, sp, s);
                lr = Quaternion.Slerp(lr, sr, s);
            }
            else if (player) { _clear = Vector3.zero; _velF = _lastVel = Quaternion.Inverse(player.transform.rotation) * player.PlanarVelocity; _lastYaw = player.transform.eulerAngles.y; _swing = _swingVel = Vector3.zero; }
            transform.localPosition = lp;
            transform.localRotation = lr;
        }

        /// <summary>The camera's spot, moved along the line up to the eye until it is 30 cm clear of what the hands hold.</summary>
        Vector3 KeepClear(Vector3 eye, Vector3 at)
        {
            if (avoid == null || avoid.Length == 0) return at;
            const float room = 0.3f;                           // (12 cm left only the fabric in view)
            for (int step = 0; step <= 20; step++)
            {
                Vector3 p = Vector3.Lerp(at, eye, step / 20f);
                bool clear = true;
                foreach (var c in avoid)
                {
                    if (!c || !c.enabled || c.isTrigger) continue;
                    if (c is MeshCollider mc && !mc.convex) continue;
                    if ((c.ClosestPoint(p) - p).sqrMagnitude < room * room) { clear = false; break; }
                }
                if (clear) return p;
            }
            return eye;
        }

        /// <summary>How far down onto its strap the camcorder is (0 in the hand, 1 hanging).</summary>
        public float StrapAmount => _strapT;
        /// <summary>For probes: the strap pendulum (degrees) and how far the camera rose to stay clear (m).</summary>
        public Vector3 StrapSwing => _swing;
        public Vector3 StrapClear => _clear;

        Vector3 _jolt, _joltVel;

        /// <summary>A sudden knock to the camera (degrees per second of pitch, yaw, roll), which springs back.</summary>
        public void Jolt(Vector3 kick) { _joltVel += kick; }

        // ---------------- Recorded walk ----------------

        static CameraMotionClip Walk
        {
            get
            {
                if (!s_walkTried) { s_walkTried = true; s_walk = CameraMotionClip.Load("CameraMotion/Walk"); }
                return s_walk;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_walk = null; s_walkTried = false; }

        /// <summary>
        /// Moves along the recorded walk at the feet's pace: each time a foot lands (FirstPersonBody.FootLanded) the
        /// recording is drawn toward that foot's landing in it (the left at the start of a stride, the right part-way), eased
        /// in, never backward and at most three times the pace; the pace comes from the time between the last two steps.
        /// With no body, or standing, the recorded pace scaled by the walking speed.
        /// </summary>
        void UpdateStride(float dt, float speed)
        {
            if (!_body && Time.unscaledTime > _bodyLook)
            {
                _bodyLook = Time.unscaledTime + 1f;
                _body = FindAnyObjectByType<Interaction.FirstPersonBody>();
                if (_body) _body.FootLanded += OnFootLanded;
            }
            var clip = Walk;
            bool paced = _strideRate > 0f && Time.time - _lastFoot < 1.2f;
            float advance = (paced ? _strideRate : Mathf.Clamp(speed, 0.5f, 1.4f) / clip.StrideSeconds) * dt;
            float fix = Mathf.Clamp(_strideFix * (1f - Mathf.Exp(-dt / 0.1f)), -0.8f * advance, 2f * advance);
            _strideFix -= fix;
            _strideT = Mathf.Repeat(_strideT + advance + fix, clip.Strides);
        }

        void OnFootLanded(Vector3 at, float speed)
        {
            var clip = Walk;
            if (clip == null || !player || !recordedWalk) return;
            float now = Time.time, gap = now - _lastFoot;
            _lastFoot = now;
            if (gap > 0.25f && gap < 1.2f) _strideRate = _strideRate > 0f ? Mathf.Lerp(_strideRate, 0.5f / gap, 0.5f) : 0.5f / gap;
            Transform root = player.transform;
            bool right = Vector3.Dot(at - root.position, root.right) > 0f;
            float phase = _strideT - Mathf.Floor(_strideT);
            float err = Mathf.Repeat((right ? clip.RightFootPhase : 0f) - phase + 0.5f, 1f) - 0.5f;
            // Not walking yet (the layer is off): straight there.
            if (_walkBlend < 0.05f) { _strideT = Mathf.Repeat(_strideT + err, clip.Strides); _strideFix = 0f; }
            else _strideFix = err;
        }

        void OnDestroy() { if (_body) _body.FootLanded -= OnFootLanded; }

        /// <summary>
        /// Secondary motion from turning, added on top of the (instant) mouse look, so there is no input
        /// delay: the offset leads slightly in the turn direction and is held by an under-damped spring,
        /// so when you stop it carries on a fraction of a degree and settles back. Plus a little
        /// speed-dependent wobble and roll.
        /// </summary>
        Vector3 LookMotion(float dt, float t)
        {
            if (dt <= 0f) return Vector3.zero;
            Vector2 d = player ? player.LookDelta : Vector2.zero;
            Vector2 vel = d / dt;                                   // degrees per second
            _lookVel = Vector2.Lerp(_lookVel, vel, 1f - Mathf.Exp(-dt / 0.05f));
            // Spring toward a small lead proportional to turn speed (capped).
            Vector2 target = Vector2.ClampMagnitude(_lookVel * 0.012f * lookOvershoot, 2.5f * lookOvershoot);
            const float k = 90f, c = 9f;                            // under-damped: a small overshoot when you stop
            Vector2 acc = (target - _lookOff) * k - _lookOffVel * c;
            _lookOffVel += acc * dt;
            _lookOff += _lookOffVel * dt;
            float speed = _lookVel.magnitude;
            Vector3 jitter = Noise3(t * 7f + 211f) * Mathf.Min(speed * 0.004f, 0.6f) * lookJitter;
            _lookRoll = Mathf.Lerp(_lookRoll, Mathf.Clamp(-_lookVel.x * 0.006f, -1.5f, 1.5f) * lookOvershoot, 1f - Mathf.Exp(-dt / 0.15f));
            // Camera local rotation: x = pitch (down positive), y = yaw.
            return new Vector3(_lookOff.y + jitter.x, _lookOff.x + jitter.y, _lookRoll + jitter.z);
        }

        Vector3 Noise3(float t)
        {
            return new Vector3(
                Mathf.PerlinNoise(t, _seed.x) * 2f - 1f,
                Mathf.PerlinNoise(_seed.y, t) * 2f - 1f,
                (Mathf.PerlinNoise(t + _seed.z, t * 0.5f) * 2f - 1f) * 0.6f);
        }
    }
}
