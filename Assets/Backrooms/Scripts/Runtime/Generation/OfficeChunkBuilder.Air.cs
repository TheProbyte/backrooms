using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>
    /// Air moving (Docs/RealismPlan.md, section 7): now and then a strand or two of old cobweb, dusty grey, hangs from the
    /// frame of a return grille and sways in the draught it pulls. Seeded like the wear; a few small objects a chunk, made
    /// on the main thread with the chunk (CobwebSway moves them only while they are on screen).
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Strands to hang (chunk-local attach point, length, seed).</summary>
        readonly List<(Vector3 at, float len, int seed)> _webs = new List<(Vector3, float, int)>();
        static Material s_webMat;

        /// <summary>A return grille just built at a tile (centre c on the ceiling): maybe a strand or two off its frame.</summary>
        void Cobweb(Vector3 c, int gx, int gz, int i)
        {
            if (Dice(gx, gz, 1600 + i) > 0.3f) return;
            int n = 1 + (int)(Dice(gx, gz, 1601 + i) * 2.5f);
            for (int k = 0; k < n; k++)
            {
                int s = 1610 + i * 8 + k;
                float edge = Dice(gx, gz, s) * 4f;
                int e = Mathf.Min(3, (int)edge);
                float along = (edge - e - 0.5f) * 0.5f;
                Vector3 off = e == 0 ? new Vector3(-0.285f, 0f, along) : e == 1 ? new Vector3(0.285f, 0f, along)
                            : e == 2 ? new Vector3(along, 0f, -0.285f) : new Vector3(along, 0f, 0.285f);
                _webs.Add((c + off + Vector3.down * 0.004f, 0.05f + 0.12f * Dice(gx, gz, s + 100), gx * 97 + gz * 13 + s));
            }
            Saw(c, Vector3.down, "cobweb");
        }

        void MakeWebs(Transform root, GeneratedChunkResources resources)
        {
            if (_webs.Count == 0) return;
            if (!s_webMat)
            {
                var sh = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
                if (!sh) return;
                s_webMat = new Material(sh) { name = "Cobweb", hideFlags = HideFlags.DontSave };
                s_webMat.SetColor("_BaseColor", new Color(0.7f, 0.69f, 0.66f));
                s_webMat.SetFloat("_Smoothness", 0f);
                if (s_webMat.HasProperty("_Cull")) s_webMat.SetFloat("_Cull", 0f);
                s_webMat.doubleSidedGI = true;
            }
            foreach (var (at, len, seed) in _webs)
            {
                var go = new GameObject("Cobweb");
                go.transform.SetParent(root, false);
                go.transform.localPosition = at;
                go.AddComponent<MeshFilter>().sharedMesh = resources.Own(WebMesh(len, seed));
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = s_webMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.renderingLayerMask = 0xFFFFFFFFu;
                go.AddComponent<CobwebSway>().Init(seed);
            }
        }

        /// <summary>A strand hanging from its top (the origin): a thin ribbon that wanders a little, a fluff of dust at its end.</summary>
        static Mesh WebMesh(float len, int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble() - 0.5f;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            const int segs = 5;
            const float w = 0.0024f;
            Vector3 p = Vector3.zero, drift = new Vector3(R() * 0.02f, 0f, R() * 0.02f);
            for (int i = 0; i <= segs; i++)
            {
                float f = i / (float)segs;
                Vector3 c = Vector3.down * (len * f) + drift * f * f;
                Vector3 side = Vector3.right * w * (1f - 0.5f * f);
                v.Add(c - side); v.Add(c + side);
                n.Add(Vector3.forward); n.Add(Vector3.forward);
                if (i > 0) { int b = (i - 1) * 2; t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b + 1); t.Add(b + 2); t.Add(b + 3); }
                p = c;
            }
            // The fluff: two small crossed quads.
            float fs = 0.007f + 0.007f * ((float)rng.NextDouble());
            for (int q = 0; q < 2; q++)
            {
                Vector3 a = q == 0 ? Vector3.right : Vector3.forward;
                int b = v.Count;
                v.Add(p - a * fs); v.Add(p + a * fs); v.Add(p + a * fs + Vector3.down * fs * 1.4f); v.Add(p - a * fs + Vector3.down * fs * 1.4f);
                Vector3 nn = Vector3.Cross(a, Vector3.up);
                for (int k = 0; k < 4; k++) n.Add(nn);
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
            var m = new Mesh { name = "Cobweb" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>A cobweb strand swaying in a return grille's draught (only while it is drawn).</summary>
    public class CobwebSway : MonoBehaviour
    {
        Quaternion _rest;
        float _seed, _amp, _speed;
        Renderer _r;

        public void Init(int seed)
        {
            var rng = new System.Random(seed);
            _seed = (float)rng.NextDouble() * 100f;
            _amp = 8f + 10f * (float)rng.NextDouble();
            _speed = 0.5f + 0.9f * (float)rng.NextDouble();
            _rest = transform.localRotation;
            _r = GetComponent<Renderer>();
        }

        void Update()
        {
            if (_r && !_r.isVisible) return;
            float t = Time.time * _speed;
            float x = (Mathf.PerlinNoise(_seed, t) - 0.5f) * 2f * _amp, z = (Mathf.PerlinNoise(t, _seed + 7f) - 0.5f) * 2f * _amp;
            transform.localRotation = _rest * Quaternion.Euler(x, 0f, z);
        }
    }
}
