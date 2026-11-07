using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// The work camcorder as a model (the Overflow look, 2026-11-06): the style lab's (Tools/Blender/style_lab_models.py:
    /// chocolate body, the company stripe, a ribbed lens and hood, the carry handle, a microphone on its post), in
    /// Resources/Overflow/Player/Camcorder.prefab with its origin at the viewfinder's eyecup and its lens toward +Z, so it
    /// is placed where the eye is. Its materials are made again on Backrooms/Lit with the lab materials' colours (the
    /// game's lights, fog and light grid). Null in the Classic look: callers keep their own simple shapes.
    /// </summary>
    public static class CamcorderModel
    {
        public static Transform Make(Transform parent, string name, List<Material> made, bool shadowOnly)
        {
            var prefab = Generation.ArtStyle.Load<GameObject>("Player/Camcorder");
            if (!prefab) return null;
            var shader = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            var go = Object.Instantiate(prefab, parent, false);
            go.name = name;
            var byName = new Dictionary<string, Material>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    string key = src ? src.name : "Camcorder";
                    if (!byName.TryGetValue(key, out var m))
                    {
                        m = new Material(shader) { name = "Cam_" + key };
                        if (src && src.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                        if (src && src.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Min(0.6f, src.GetFloat("_Smoothness")));
                        if (src && src.HasProperty("_Metallic")) m.SetFloat("_Metallic", Mathf.Min(0.2f, src.GetFloat("_Metallic")));
                        if (m.HasProperty("_DetailAlbedoMapScale")) m.SetFloat("_DetailAlbedoMapScale", -1f);
                        if (src && src.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor"))
                        {
                            m.EnableKeyword("_EMISSION");
                            m.SetColor("_EmissionColor", src.GetColor("_EmissionColor"));
                        }
                        byName[key] = m;
                        made?.Add(m);
                    }
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
                if (shadowOnly) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            Generation.PoolLitSwitch.Attach(go);
            return go.transform;
        }
    }
}
