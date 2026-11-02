using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Dust in the air where the light is strong (Docs/RealismPlan.md, section 7): motes drift in a box round the camera
    /// all the time (world space, so they stay put as you walk), and Backrooms/AirDust shows each one only while it is in
    /// the light under one of the eight nearest lit ceiling fixtures. Very faint: a sparkle in the air under a light, as
    /// in any old office. Strength from F5 > LIGHT > AIR (GraphicsController sets <see cref="strength"/>).
    /// </summary>
    public class AirDust : MonoBehaviour
    {
        public float strength = 1f;

        static Material s_mat;
        static readonly int LightsId = Shader.PropertyToID("_AirDustLights"), CountId = Shader.PropertyToID("_AirDustCount"),
            StrengthId = Shader.PropertyToID("_AirDustStrength");
        ParticleSystem _ps;
        FixtureLightManager _lights;
        readonly List<LightSource> _near = new List<LightSource>();
        readonly Vector4[] _arr = new Vector4[8];

        public static AirDust Ensure(Camera cam)
        {
            if (!cam) return null;
            var have = cam.GetComponentInChildren<AirDust>();
            if (have) return have;
            var go = new GameObject("AirDust");
            go.transform.SetParent(cam.transform, false);
            return go.AddComponent<AirDust>();
        }

        void Awake()
        {
            if (!s_mat)
            {
                var sh = Shader.Find("Backrooms/AirDust");
                if (!sh) { enabled = false; return; }
                s_mat = new Material(sh) { name = "AirDust", hideFlags = HideFlags.DontSave };
            }
            _ps = gameObject.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.loop = true; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.01f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.0035f, 0.007f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.35f), new Color(1f, 1f, 1f, 1f));
            main.gravityModifier = 0.0004f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 900;
            main.prewarm = true;
            var emission = _ps.emission;
            emission.rateOverTime = 75f;
            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(7f, 2.6f, 7f);
            var noise = _ps.noise;
            noise.enabled = true; noise.strength = 0.01f; noise.frequency = 0.3f; noise.scrollSpeed = 0.04f;
            var r = GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = s_mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _ps.Play();
        }

        void LateUpdate()
        {
            if (!_ps) return;
            // The box stays level, round the eye, a little ahead of it.
            var cam = transform.parent;
            if (cam)
            {
                Vector3 fwd = cam.forward; fwd.y = 0f;
                transform.position = cam.position + (fwd.sqrMagnitude > 1e-4f ? fwd.normalized * 1.5f : Vector3.zero);
                transform.rotation = Quaternion.identity;
            }
            bool on = strength > 0.001f;
            if (on != _ps.isPlaying) { if (on) _ps.Play(); else _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
            if (!on) return;
            Shader.SetGlobalFloat(StrengthId, strength);
            if (!_lights) _lights = FindAnyObjectByType<FixtureLightManager>();
            int n = 0;
            if (_lights)
            {
                _near.Clear();
                Vector3 at = transform.position;
                _lights.GatherTubes(at, 6f, _near);
                _near.Sort((a, b) => (a.position - at).sqrMagnitude.CompareTo((b.position - at).sqrMagnitude));
                float now = Time.time;
                foreach (var l in _near)
                {
                    if (n >= _arr.Length) break;
                    float level = _lights.PowerLevel(l, now);
                    if (level < 0.05f) continue;
                    _arr[n++] = new Vector4(l.position.x, l.position.y, l.position.z, level * strength * 1.6f);
                }
            }
            for (int i = n; i < _arr.Length; i++) _arr[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(LightsId, _arr);
            Shader.SetGlobalFloat(CountId, n);
        }
    }
}
