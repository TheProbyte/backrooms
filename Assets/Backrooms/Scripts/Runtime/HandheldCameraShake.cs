using UnityEngine;

namespace Backrooms
{
    /// <summary>
    /// Handheld camcorder motion, applied as a local offset on the camera transform.
    /// Three layers: slow irregular "breathing" sway at rest, a gentle footstep-driven
    /// bob while walking, and heavier, rougher shake while running. Mouse look lives on
    /// the parent pivot, so shake never fights or delays aim.
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

        float _walkBlend, _runBlend, _stepPhase, _breathPhase;
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

            // Run: heavier bob plus higher-frequency rough shake.
            Vector3 runPos = new Vector3(Mathf.Sin(step * 0.5f) * 0.5f, bob, 0f) * runBobHeight;
            Vector3 runRot = new Vector3(bob * 0.8f, Mathf.Sin(step * 0.5f) * 0.4f, Mathf.Sin(step * 0.5f) * 0.6f) * runShakeDegrees
                           + Noise3(t * 4.5f + 113f) * runShakeDegrees * 0.45f;

            float walkOnly = _walkBlend * (1f - _runBlend);
            // Breathing: irregular period (rate wanders a little) so it never reads as a metronome.
            _breathPhase += dt * breathingRate * (0.85f + 0.3f * Mathf.PerlinNoise(t * 0.05f, _seed.x)) * Mathf.PI * 2f;
            float breath = Mathf.Sin(_breathPhase) + 0.25f * Mathf.Sin(_breathPhase * 2f + 0.8f);
            float breathWeight = 1f - _walkBlend * 0.7f;
            Vector3 breathRot = new Vector3(breath * breathingDegrees, 0f, breath * breathingDegrees * 0.25f) * breathWeight;
            Vector3 breathPos = new Vector3(0f, breath * breathingHeight, 0f) * breathWeight;

            // Stabilisation removes the fast components first (run shake), then some of the slow ones.
            float stab = 1f - stabilization;
            Vector3 pos = breathPos * (1f - stabilization * 0.5f) + (walkPos * walkOnly + runPos * _runBlend) * Mathf.Lerp(1f, 0.45f, stabilization);
            Vector3 rot = breathRot * (1f - stabilization * 0.5f)
                        + idleRot * Mathf.Lerp(1f, 0.6f, _walkBlend) * Mathf.Lerp(1f, 0.4f, stabilization)
                        + walkRot * walkOnly * Mathf.Lerp(1f, 0.35f, stabilization)
                        + runRot * _runBlend * Mathf.Lerp(1f, 0.25f, stabilization) * (0.5f + 0.5f * stab);

            rot += LookMotion(dt, t) * (1f - stabilization * 0.5f);

            transform.localPosition = pos * shakeIntensity;
            transform.localRotation = Quaternion.Euler(rot * shakeIntensity);
        }

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
