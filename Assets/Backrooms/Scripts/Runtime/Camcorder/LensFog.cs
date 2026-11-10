using UnityEngine;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// The lens misting over (2026-11-01, the realism plan 9): carried from the cool Backrooms into the warm, wet Poolrooms,
    /// a camcorder's lens fogs with condensation, softening and greying the picture, and clears from the edges inward over
    /// about ten seconds as it warms (and a little after coming up from under the water). Drawn in the camcorder's
    /// integrate pass (CamcorderPipeline.shader, _CamLensFog: x amount, y how far it has cleared 0..1).
    /// </summary>
    public static class LensFog
    {
        static float s_amount, s_clear = 1f, s_seconds = 10f;
        static readonly int Id = Shader.PropertyToID("_CamLensFog");

        /// <summary>Fog the lens (0..1), clearing over the given time.</summary>
        public static void Fog(float amount, float seconds = 10f)
        {
            if (amount <= 0f) return;
            float left = s_amount * (1f - s_clear);
            if (amount < left) return;
            s_amount = Mathf.Clamp01(amount); s_clear = 0f; s_seconds = Mathf.Max(1f, seconds);
        }

        public static bool Active => s_amount > 0f && s_clear < 1f;

        /// <summary>A clear lens at once (stills, tests).</summary>
        public static void Clear() { s_amount = 0f; s_clear = 1f; }

        /// <summary>Ages the fog and hands it to the pipeline's material (CamcorderRig, each frame).</summary>
        public static void Apply(Material m, float dt)
        {
            if (s_amount > 0f && s_clear < 1f) s_clear = Mathf.Min(1f, s_clear + dt / s_seconds);
            m.SetVector(Id, s_clear >= 1f ? Vector4.zero : new Vector4(s_amount, s_clear, 0f, 0f));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { s_amount = 0f; s_clear = 1f; }
    }
}
