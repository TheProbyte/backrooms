using UnityEngine;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// Water on the lens (2026-10-31, the water interaction plan): a splash near the camcorder leaves drops on the front of
    /// the lens. Each one is a little lens of its own in the picture (the scene through it small and upside down, blurred,
    /// a dark rim and a bright edge), slides slowly down and dries away over a few seconds. Up to eight, drawn in the
    /// camcorder's integrate pass (CamcorderPipeline.shader, _CamDrops).
    /// </summary>
    public static class LensDrops
    {
        const int Max = 8;
        static readonly Vector4[] s_out = new Vector4[Max];
        static readonly Vector2[] s_pos = new Vector2[Max];
        static readonly float[] s_r = new float[Max], s_strength = new float[Max], s_life = new float[Max], s_age = new float[Max], s_slide = new float[Max];
        static readonly int Id = Shader.PropertyToID("_CamDrops");

        /// <summary>count drops at strength (0..1) somewhere on the lens.</summary>
        public static void Add(int count, float strength)
        {
            for (int c = 0; c < count; c++)
            {
                // Replace the driest.
                int pick = 0; float worst = float.MaxValue;
                for (int i = 0; i < Max; i++) { float w = s_strength[i] * (1f - s_age[i] / Mathf.Max(0.01f, s_life[i])); if (w < worst) { worst = w; pick = i; } }
                s_pos[pick] = new Vector2(Random.Range(0.1f, 0.9f), Random.Range(0.15f, 0.95f));
                s_r[pick] = Random.Range(0.018f, 0.055f);
                s_strength[pick] = Mathf.Clamp01(strength * Random.Range(0.7f, 1f));
                s_life[pick] = Random.Range(3f, 7f);
                s_age[pick] = 0f;
                s_slide[pick] = Random.Range(0.002f, 0.012f);
            }
        }

        /// <summary>Ages them and hands them to the pipeline's material (CamcorderRig, each frame).</summary>
        public static void Apply(Material m, float dt)
        {
            for (int i = 0; i < Max; i++)
            {
                if (s_strength[i] <= 0f) { s_out[i] = Vector4.zero; continue; }
                s_age[i] += dt;
                float k = 1f - s_age[i] / s_life[i];
                if (k <= 0f) { s_strength[i] = 0f; s_out[i] = Vector4.zero; continue; }
                s_pos[i].y -= s_slide[i] * dt * (0.5f + k);                       // running down, slowing as it dries
                float r = s_r[i] * Mathf.Lerp(0.6f, 1f, k);
                s_out[i] = new Vector4(s_pos[i].x, s_pos[i].y, r, s_strength[i] * Mathf.SmoothStep(0f, 1f, k * 1.5f));
            }
            m.SetVectorArray(Id, s_out);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { for (int i = 0; i < Max; i++) { s_strength[i] = 0f; s_out[i] = Vector4.zero; } }
    }
}
