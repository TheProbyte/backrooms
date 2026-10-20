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

        float _walkBlend, _runBlend, _stepPhase, _breathPhase;
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

            transform.localPosition = pos * shakeIntensity;
            transform.localRotation = Quaternion.Euler(rot * shakeIntensity);
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
