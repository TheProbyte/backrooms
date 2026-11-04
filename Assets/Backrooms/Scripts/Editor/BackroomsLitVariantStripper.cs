using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Build-time variant cut for Backrooms/Lit, on top of the keywords already removed from the shader source.
    /// Each Backrooms/Lit variant compiles the whole light-grid lighting, so every unused one costs build time.
    /// <list type="bullet">
    /// <item>Lightmap variants: only OfficeRoom_Prototype is lightmapped, and the game never loads it (it is in the build
    /// list but nothing opens it; the world, ReferenceMatch and BuildMode are lit at runtime).</item>
    /// <item>Reflection probes: URP turns blending, box projection and atlas on or off together (one URP asset setting), so
    /// mixed combinations never occur.</item>
    /// <item>Soft shadows without additional-light shadows: the soft filter only changes shadow-map sampling, which does not
    /// happen then (and there is no main light).</item>
    /// </list>
    /// The counts are logged at the end of each build.
    /// </summary>
    class BackroomsLitVariantStripper : IPreprocessShaders, IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;   // after URP's own stripping

        static readonly HashSet<string> Lightmap = new HashSet<string>
        {
            "LIGHTMAP_ON", "DIRLIGHTMAP_COMBINED", "LIGHTMAP_SHADOW_MIXING", "SHADOWS_SHADOWMASK",
            "DYNAMICLIGHTMAP_ON", "LIGHTMAP_BICUBIC_SAMPLING", "USE_LEGACY_LIGHTMAPS",
        };
        static readonly string[] Probes = { "_REFLECTION_PROBE_BLENDING", "_REFLECTION_PROBE_BOX_PROJECTION", "_REFLECTION_PROBE_ATLAS" };

        static int _in, _out;
        static readonly Dictionary<string, int> _kept = new Dictionary<string, int>();

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (shader == null || shader.name != "Backrooms/Lit") return;
            _in += data.Count;
            var names = new HashSet<string>();
            for (int i = data.Count - 1; i >= 0; i--)
            {
                names.Clear();
                foreach (var k in data[i].shaderKeywordSet.GetShaderKeywords()) names.Add(k.name);
                if (Strip(names)) data.RemoveAt(i);
            }
            _out += data.Count;
            string pass = snippet.passName;
            _kept.TryGetValue(pass, out int n);
            _kept[pass] = n + data.Count;
        }

        /// <summary>True for a Backrooms/Lit variant the game can never use (see the class summary).</summary>
        public static bool Strip(ICollection<string> keywords)
        {
            foreach (var k in keywords) if (Lightmap.Contains(k)) return true;
            if (keywords.Contains("_SHADOWS_SOFT") && !keywords.Contains("_ADDITIONAL_LIGHT_SHADOWS")) return true;
            int probes = 0;
            foreach (var p in Probes) if (keywords.Contains(p)) probes++;
            return probes != 0 && probes != Probes.Length;
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (_in == 0) return;
            var sb = new System.Text.StringBuilder();
            sb.Append($"[Backrooms] Backrooms/Lit variants: {_in} after URP stripping, {_out} compiled.");
            foreach (var kv in _kept) sb.Append($" {kv.Key}: {kv.Value}.");
            Debug.Log(sb.ToString());
            _in = _out = 0; _kept.Clear();
        }
    }
}
