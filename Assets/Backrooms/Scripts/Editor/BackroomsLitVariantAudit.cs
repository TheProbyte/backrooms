using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Backrooms/Lit's variants per pass as they reach the build's stripping (first) and as they leave it (last), with the
    /// keywords of one forward variant: where a pass's variants go when a build changes (the GPU Resident Drawer build had
    /// no ForwardLit variants left). Logged at the end of each build.
    /// </summary>
    class BackroomsLitVariantAudit : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        internal static readonly Dictionary<string, int> In = new Dictionary<string, int>(), Out = new Dictionary<string, int>();
        internal static string Sample;

        internal static void Count(Dictionary<string, int> d, Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (shader == null || shader.name != "Backrooms/Lit") return;
            d.TryGetValue(snippet.passName, out int n);
            d[snippet.passName] = n + data.Count;
            if (d == In && Sample == null && snippet.passName == "ForwardLit" && data.Count > 0)
            {
                var names = new List<string>();
                foreach (var k in data[0].shaderKeywordSet.GetShaderKeywords()) names.Add(k.name);
                Sample = string.Join(" ", names);
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            var sb = new System.Text.StringBuilder("[Backrooms] Backrooms/Lit variants by pass, in -> out:");
            foreach (var kv in In) { Out.TryGetValue(kv.Key, out int o); sb.Append($" {kv.Key} {kv.Value} -> {o};"); }
            sb.Append(" ForwardLit sample: ").Append(Sample ?? "(none)");
            Debug.Log(sb.ToString());
            In.Clear(); Out.Clear(); Sample = null;
        }
    }

    class BackroomsLitVariantAuditFirst : IPreprocessShaders
    {
        public int callbackOrder => int.MinValue;
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data) => BackroomsLitVariantAudit.Count(BackroomsLitVariantAudit.In, shader, snippet, data);
    }

    class BackroomsLitVariantAuditLast : IPreprocessShaders
    {
        public int callbackOrder => int.MaxValue;
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data) => BackroomsLitVariantAudit.Count(BackroomsLitVariantAudit.Out, shader, snippet, data);
    }
}
