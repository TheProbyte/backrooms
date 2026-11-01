using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// The camcorder look switches URP to HDR colour grading while it plays (CamcorderRig: its picture needs linear light
    /// past 1.0). URP's build only keeps the post-processing shader versions its assets use, and the assets are set to LDR
    /// grading, so a build without this had no HDR grading: with the camcorder look on, colours blew out (crushed shadows,
    /// yellow-orange highlights) while the editor, which has every version, looked right. For the length of a build every
    /// URP asset in the quality levels is set to HDR grading (so those versions are kept), then put back as it was.
    ///
    /// The same for STP (Graphics > Anti-aliasing type, 2026-10-31: the owner found STP froze the picture in the build,
    /// while it worked in the editor). URP's STPResourceStripper drops STP's shaders from a build when no URP asset in it
    /// uses STP as its upscaler, so switching to it in the game had nothing to run. For the build the assets are set to
    /// STP, which keeps them; the game picks the upscaler itself as it runs (GraphicsOptions: Automatic unless STP is
    /// chosen), so this changes nothing else.
    /// </summary>
    class HdrGradingForBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => -1000;   // before URP gathers what its assets use

        static readonly Dictionary<UniversalRenderPipelineAsset, ColorGradingMode> s_was = new Dictionary<UniversalRenderPipelineAsset, ColorGradingMode>();
        static readonly Dictionary<UniversalRenderPipelineAsset, UpscalingFilterSelection> s_upWas = new Dictionary<UniversalRenderPipelineAsset, UpscalingFilterSelection>();

        public void OnPreprocessBuild(BuildReport report)
        {
            Restore();
            var assets = new HashSet<UniversalRenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset d) assets.Add(d);
            for (int i = 0; i < QualitySettings.names.Length; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset a) assets.Add(a);
            foreach (var a in assets)
            {
                if (a.upscalingFilter != UpscalingFilterSelection.STP) { s_upWas[a] = a.upscalingFilter; a.upscalingFilter = UpscalingFilterSelection.STP; }
                if (a.colorGradingMode == ColorGradingMode.HighDynamicRange) continue;
                s_was[a] = a.colorGradingMode;
                a.colorGradingMode = ColorGradingMode.HighDynamicRange;
            }
            // If the build fails, the post-build step does not run: put the assets back as soon as the editor is free.
            EditorApplication.delayCall += Restore;
        }

        public void OnPostprocessBuild(BuildReport report) => Restore();

        static void Restore()
        {
            foreach (var kv in s_was)
                if (kv.Key) { kv.Key.colorGradingMode = kv.Value; EditorUtility.ClearDirty(kv.Key); }
            s_was.Clear();
            foreach (var kv in s_upWas)
                if (kv.Key) { kv.Key.upscalingFilter = kv.Value; EditorUtility.ClearDirty(kv.Key); }
            s_upWas.Clear();
        }
    }
}
