using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Lights an ordinary object (the player's arm and body, items, furniture) by the Poolrooms' light while it is in
    /// them: its materials are swapped for copies using Backrooms/PoolLit (same colours and textures), and swapped back
    /// when it leaves. Backrooms/Lit itself has no Poolrooms light (its lamps and light grid do not exist there).
    /// Checked a few times a second.
    /// </summary>
    public class PoolLitSwitch : MonoBehaviour
    {
        static readonly Dictionary<Material, Material> s_copies = new Dictionary<Material, Material>();
        static Shader s_shader;

        Renderer[] _renderers;
        Material[][] _original;
        bool _inPool;
        float _next;

        /// <summary>The Poolrooms copy of a material (made once per material).</summary>
        public static Material For(Material m)
        {
            if (!m) return m;
            if (s_copies.TryGetValue(m, out var c) && c) return c;
            if (!s_shader) s_shader = Shader.Find("Backrooms/PoolLit");
            if (!s_shader) return m;
            c = new Material(s_shader) { name = m.name + " (Poolrooms)", hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap")) { c.SetTexture("_BaseMap", m.GetTexture("_BaseMap")); c.SetTextureScale("_BaseMap", m.GetTextureScale("_BaseMap")); c.SetTextureOffset("_BaseMap", m.GetTextureOffset("_BaseMap")); }
            else if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex")) c.SetTexture("_BaseMap", m.GetTexture("_MainTex"));
            if (m.HasProperty("_BaseColor")) c.SetColor("_BaseColor", m.GetColor("_BaseColor"));
            else if (m.HasProperty("_Color")) c.SetColor("_BaseColor", m.GetColor("_Color"));
            if (m.HasProperty("_Smoothness")) c.SetFloat("_Smoothness", m.GetFloat("_Smoothness"));
            if (m.HasProperty("_Metallic")) c.SetFloat("_Metallic", m.GetFloat("_Metallic"));
            s_copies[m] = c;
            return c;
        }

        /// <summary>Adds the switch to an object (once).</summary>
        public static PoolLitSwitch Attach(GameObject go)
        {
            var s = go.GetComponent<PoolLitSwitch>();
            return s ? s : go.AddComponent<PoolLitSwitch>();
        }

        void Start() => Collect();

        /// <summary>Remembers the renderers and their own materials (call again if renderers are added).</summary>
        public void Collect()
        {
            if (_inPool) Apply(false);
            var list = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) list.Add(r);
            _renderers = list.ToArray();
            _original = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++) _original[i] = _renderers[i].sharedMaterials;
            _next = 0f;
        }

        void LateUpdate()
        {
            if (_renderers == null || Time.time < _next) return;
            _next = Time.time + 0.2f + Random.value * 0.1f;
            bool pool = PoolWorld.Instance && PoolWorld.Instance.IsPool(transform.position);
            if (pool != _inPool) Apply(pool);
        }

        void Apply(bool pool)
        {
            _inPool = pool;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (!r) continue;
                var mats = (Material[])_original[i].Clone();
                if (pool) for (int k = 0; k < mats.Length; k++) mats[k] = For(mats[k]);
                r.sharedMaterials = mats;
            }
        }
    }
}
