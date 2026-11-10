using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Spray, mist and bubbles in the Poolrooms (2026-10-31, the water interaction plan; splashes made fuller the same day).
    /// Three world-space particle systems for all of it (three draw calls; no object per splash), emitted into by bursts:
    ///  - droplets thrown up and falling back under gravity, drawn stretched along their flight (Backrooms/PoolSpray), lit
    ///    by the Poolrooms' own sun and sky; the water's depth hides them once they are under the surface;
    ///  - a puff of mist over big splashes;
    ///  - bubbles under the water (Backrooms/PoolBubble), rising and wobbling to the surface where they pop.
    /// <see cref="Splash"/> makes a whole splash the way one goes: a crown of drops thrown outward, for a heavy thing a jet
    /// shooting back up a moment later (the cavity it opened closing), mist, bubbles, a ring and foam on the water.
    /// </summary>
    public class PoolSplashes : MonoBehaviour
    {
        static PoolSplashes s_inst;
        ParticleSystem _ps, _mist, _bubbles;
        Material _mat, _bubbleMat;
        readonly List<(float at, Vector3 pos, float strength)> _jets = new List<(float, Vector3, float)>();

        void Awake()
        {
            s_inst = this;
            var sh = Shader.Find("Backrooms/PoolSpray");
            if (!sh) return;
            _mat = new Material(sh) { name = "Pool spray", hideFlags = HideFlags.DontSave };
            _ps = MakeSystem("PoolSpray", 900, 1f, _mat, ParticleSystemRenderMode.Stretch, 0.9f);
            _mist = MakeSystem("PoolMist", 200, 0.02f, _mat, ParticleSystemRenderMode.Billboard, 0.35f);
            var bsh = Shader.Find("Backrooms/PoolBubble");
            if (bsh)
            {
                _bubbleMat = new Material(bsh) { name = "Pool bubbles", hideFlags = HideFlags.DontSave };
                _bubbles = MakeSystem("PoolBubbles", 600, -0.12f, _bubbleMat, ParticleSystemRenderMode.Billboard, 1f);
                // A wobble on the way up, and a speed they cannot rise faster than (water's drag).
                var noise = _bubbles.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 2.5f; noise.scrollSpeed = 0.6f; noise.damping = true;
                var limit = _bubbles.limitVelocityOverLifetime; limit.enabled = true; limit.limit = 0.35f; limit.dampen = 0.15f;
            }
        }

        ParticleSystem MakeSystem(string name, int max, float gravity, Material mat, ParticleSystemRenderMode mode, float fadeFrom)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = true; main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max; main.gravityModifier = gravity;
            main.startSpeed = 0f; main.startLifetime = 0.8f; main.startSize = 0.02f;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, fadeFrom), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode;
            if (mode == ParticleSystemRenderMode.Stretch) { r.velocityScale = 0.03f; r.lengthScale = 1.6f; }
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            ps.Play();
            return ps;
        }

        void OnDestroy()
        {
            if (s_inst == this) s_inst = null;
            if (_mat) Destroy(_mat);
            if (_bubbleMat) Destroy(_bubbleMat);
        }

        void Update()
        {
            // Jets: the cavity a heavy thing opened closes and throws a column of water straight up.
            for (int i = _jets.Count - 1; i >= 0; i--)
            {
                if (Time.time < _jets[i].at) continue;
                var (_, pos, k) = _jets[i];
                _jets.RemoveAt(i);
                Burst(pos, Mathf.RoundToInt(Mathf.Lerp(10f, 40f, k)), Mathf.Lerp(2.2f, 4f, k), Vector3.zero, 0.15f, Mathf.Lerp(1.1f, 1.6f, k));
                PoolRipples.Splash(pos, 0.2f, -0.01f * k);
            }
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

        /// <summary>A whole splash at a point on the water: strength 0 (a pebble) .. 1 (a chair dropped in).</summary>
        public static void Splash(Vector3 at, float strength, Vector3 drift, bool heavy)
        {
            var s = s_inst;
            if (!s || !s._ps) return;
            strength = Mathf.Clamp01(strength);
            float k = Mathf.Sqrt(strength);
            // The crown: a ring of drops thrown up and outward.
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            int crown = Mathf.RoundToInt(Mathf.Lerp(10f, 110f, k));
            for (int i = 0; i < crown; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float up = Random.Range(1.2f, 2.0f), side = Random.Range(0.5f, 1.2f);
                ep.position = at + outward * Mathf.Lerp(0.04f, 0.18f, k);
                ep.velocity = (Vector3.up * up + outward * side) * Mathf.Lerp(1f, 2.2f, k) + drift * 0.3f;
                ep.startLifetime = Random.Range(0.35f, 0.8f);
                ep.startSize = Random.Range(0.01f, 0.035f) * Mathf.Lerp(1f, 1.6f, k);
                ep.startColor = new Color(1f, 1f, 1f, Random.Range(0.6f, 0.95f));
                s._ps.Emit(ep, 1);
            }
            // A jet a moment later for something heavy.
            if (heavy && strength > 0.15f) s._jets.Add((Time.time + Mathf.Lerp(0.14f, 0.22f, k), at, strength));
            // Mist over the bigger ones.
            if (s._mist && strength > 0.3f)
            {
                var mp = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                int puffs = Mathf.RoundToInt(Mathf.Lerp(3f, 10f, k));
                for (int i = 0; i < puffs; i++)
                {
                    mp.position = at + Random.insideUnitSphere * 0.15f + Vector3.up * 0.1f;
                    mp.velocity = Random.insideUnitSphere * 0.3f + Vector3.up * Random.Range(0.3f, 0.8f);
                    mp.startLifetime = Random.Range(0.6f, 1.2f);
                    mp.startSize = Random.Range(0.12f, 0.35f) * k;
                    mp.startColor = new Color(1f, 1f, 1f, Random.Range(0.08f, 0.16f));
                    s._mist.Emit(mp, 1);
                }
            }
            Bubbles(at, Mathf.RoundToInt(Mathf.Lerp(6f, 60f, k)), Mathf.Lerp(0.1f, 0.3f, k), Mathf.Lerp(0.08f, 0.3f, k));
        }

        /// <summary>Bubbles under the water at a point (below the surface, spread about), rising to it.</summary>
        public static void Bubbles(Vector3 surface, int count, float spread, float depth)
        {
            var s = s_inst;
            if (!s || !s._bubbles || count <= 0) return;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                float d = Random.Range(0.03f, depth);
                Vector2 c = Random.insideUnitCircle * spread;
                ep.position = surface + new Vector3(c.x, -d, c.y);
                ep.velocity = new Vector3(c.x * 0.5f, Random.Range(0.05f, 0.2f), c.y * 0.5f);
                // Up to the surface at about 0.25 m/s, then gone (popped).
                ep.startLifetime = Mathf.Max(0.12f, d / 0.25f) * Random.Range(0.85f, 1.1f);
                ep.startSize = Random.Range(0.006f, 0.02f) * (Random.value < 0.15f ? 1.8f : 1f);
                ep.startColor = new Color(1f, 1f, 1f, Random.Range(0.5f, 0.9f));
                s._bubbles.Emit(ep, 1);
            }
        }
    }
}
