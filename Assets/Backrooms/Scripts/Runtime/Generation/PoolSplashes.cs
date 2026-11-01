using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Spray in the Poolrooms (2026-10-31, the water interaction plan): droplets thrown up where something goes into the
    /// water, falling back under gravity. One world-space particle system for all of it (one draw call, no per-splash
    /// objects), emitted into by bursts (<see cref="Burst"/>); the droplets are drawn stretched along their motion, lit by
    /// the Poolrooms' own sun and sky (Backrooms/PoolSpray, per vertex), and vanish under the surface (the water writes
    /// depth). The rings on the water come from the ripple simulation (<see cref="PoolRipples.Splash"/>).
    /// </summary>
    public class PoolSplashes : MonoBehaviour
    {
        static PoolSplashes s_inst;
        ParticleSystem _ps;
        Material _mat;

        void Awake()
        {
            s_inst = this;
            var sh = Shader.Find("Backrooms/PoolSpray");
            if (!sh) return;
            _mat = new Material(sh) { name = "Pool spray", hideFlags = HideFlags.DontSave };
            var go = new GameObject("PoolSpray");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.playOnAwake = false; main.loop = true; main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 900; main.gravityModifier = 1f;
            main.startSpeed = 0f; main.startLifetime = 0.8f; main.startSize = 0.02f;
            var em = _ps.emission; em.enabled = false;
            var shape = _ps.shape; shape.enabled = false;
            var fade = _ps.colorOverLifetime; fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.03f; r.lengthScale = 1.6f;
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _ps.Play();
        }

        void OnDestroy()
        {
            if (s_inst == this) s_inst = null;
            if (_mat) Destroy(_mat);
        }

        /// <summary>
        /// A burst of droplets from a point on the water: count of them, thrown up at about speed (m/s) within a cone
        /// (spread: 0 straight up .. 1 wide), carried along by drift, sized by size (1 = rain-drop sized).
        /// </summary>
        public static void Burst(Vector3 at, int count, float speed, Vector3 drift, float spread = 0.6f, float size = 1f)
        {
            var s = s_inst;
            if (!s || !s._ps || count <= 0) return;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector2 c = Random.insideUnitCircle * spread;
                var dir = new Vector3(c.x, 1f, c.y).normalized;
                ep.position = at + new Vector3(c.x, 0f, c.y) * 0.08f * size;
                ep.velocity = dir * speed * Random.Range(0.45f, 1.1f) + drift * Random.Range(0.6f, 1.1f);
                ep.startLifetime = Random.Range(0.3f, 0.75f);
                ep.startSize = Random.Range(0.01f, 0.032f) * size;
                ep.startColor = new Color(1f, 1f, 1f, Random.Range(0.55f, 0.9f));
                s._ps.Emit(ep, 1);
            }
        }
    }
}
